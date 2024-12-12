using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class GridProps
        {
            private Program _program;
            public double Roll;
            public double Pitch;
            public MyShipMass Mass => MainController.CalculateShipMass();
            public Vector3D Gravity => MainController.GetTotalGravity();
            public double GravityMagnitude => Gravity.Length();
            public double Speed => MainController.GetShipSpeed();
            public bool Cruise = false;
            public bool Flipping = false;
            public bool RollCompensating = false;
            public bool Recording = false;
            public bool AutoLevel = false;
            public float CruiseSpeed = 0;
            public float ForwardBackward => Controller.MoveIndicator.Z;
            public float LeftRight => Controller.MoveIndicator.X;
            public float UpDown => Controller.MoveIndicator.Y;
            public IMyShipController Controller;
            public IMyShipController MainController;
            public IMyShipController SubController;
            public IMyRemoteControl Autopilot => MainController is IMyRemoteControl ? MainController as IMyRemoteControl : null;
            public void UpdateGridProps(Dictionary<string, string> config, List<IMyShipController> controllers)
            {
                var myControllers = controllers.Where(c => c.CubeGrid == _program.Me.CubeGrid && c.CanControlShip);
                MainController = myControllers.FirstOrDefault(c => Util.IsTagged(c, config["Tag"]) && c is IMyRemoteControl)
                    ?? myControllers.FirstOrDefault(c => c is IMyRemoteControl)
                    ?? myControllers.FirstOrDefault();
                Controller = myControllers.FirstOrDefault(c => c.IsUnderControl) ?? MainController;

                var subControllers = controllers.Where(c => c.CubeGrid != _program.Me.CubeGrid);
                SubController = subControllers
                    .FirstOrDefault(c => c.Orientation.Forward == MainController.Orientation.Forward)
                    ?? subControllers.FirstOrDefault();

                if (MainController == null) return;

                var T = MatrixD.Transpose(MainController.WorldMatrix);
                var gravityLocal = Vector3D.TransformNormal(MainController.GetTotalGravity(), T);
                var roll = Math.Atan2(gravityLocal.Dot(Vector3D.Right), gravityLocal.Dot(Vector3D.Down));
                var pitch = Math.Atan2(gravityLocal.Dot(Vector3D.Backward), gravityLocal.Dot(Vector3D.Down));

                Roll = MathHelper.ToDegrees(roll);
                Pitch = MathHelper.ToDegrees(pitch);
            }
            public GridProps(Program program)
            {
                _program = program;
                AutoLevel = bool.Parse(_program.Config.GetValueOrDefault("AutoLevel", "false"));
            }
        }

        Dictionary<string, string> Config => Memo.Of(() =>
        {
            var keys = new List<MyIniKey>();
            Ini.GetKeys(keys);
            return keys.ToDictionary(k => k.Name, k => Ini.Get(k.Section, k.Name).ToString());
        }, "config", Memo.Refs(Ini));

        MyIni Ini => Memo.Of(() =>
        {
            var myIni = new MyIni();
            if (!myIni.TryParse(Me.CustomData) || Me.CustomData == "")
            {
                myIni.Set("Options", "Tag", "{DDAS}");
                myIni.Set("Options", "IgnoreTag", "{Ignore}");

                myIni.Set("Options", "LowModeHight", 0);
                myIni.Set("Options", "HighModeHight", "Max");
                myIni.SetComment("Options", "LowModeHight", "Height in meters. Same for HighModeHight, but Max will set the height to the maximum possible value");

                myIni.Set("Options", "StrengthFactor", 0.6);
                myIni.Set("Options", "SuspensionHightRoll", 30);

                myIni.Set("Options", "MaxSteeringAngle", 25);
                myIni.Set("Options", "AckermanFocalPoint", "CoM");
                myIni.SetComment("Options", "AckermanFocalPoint", "Possible values RC, CoM - (Remote Control, Center of Mass)");
                myIni.Set("Options", "AckermanFocalPointOffset", 0);
                myIni.SetComment("Options", "AckermanFocalPointOffset", "Offset from the reference point in meters");

                myIni.Set("Options", "FrictionInner", 80);
                myIni.Set("Options", "FrictionOuter", 60);
                myIni.Set("Options", "FrictionMinSpeed", 5);
                myIni.SetComment("Options", "FrictionMinSpeed", "Speed in m/s where the friction adjustment will be applied");

                myIni.Set("Options", "PIDCruise", "10/0/0/0");
                myIni.Set("Options", "PIDFlip", "8/0/0/0");
                myIni.Set("Options", "PIDPower", "4/4/0/1");
                myIni.Set("Options", "PIDRoll", "6/0/0/0");
                myIni.Set("Options", "PIDPitch", "3/0/0/0");

                myIni.Set("Options", "AddWheels", "true");
                myIni.Set("Options", "SuspensionStrength", "true");
                myIni.Set("Options", "SubWheelsStrength", "true");
                myIni.Set("Options", "Power", "true");
                myIni.Set("Options", "StopLights", "true");
                myIni.Set("Options", "Friction", "true");
                myIni.Set("Options", "SuspensionHight", "true");
                myIni.Set("Options", "AutoLevel", "false");

                Me.CustomData = myIni.ToString();
            };
            return myIni;
        }, "myIni", Memo.Refs(Me.CustomData));

        readonly GridProps gridProps;

        List<IMyMotorSuspension> AllWheels => Memo.Of(() => Util.GetBlocks<IMyMotorSuspension>(b => Util.IsNotIgnored(b, Config["IgnoreTag"]) && b.Enabled), "wheels", Memo.Refs(gridProps.Mass.BaseMass));

        List<WheelWrapper> MyWheels => Memo.Of(() =>
            {
                var wh = AllWheels
                    .Where(w => w.CubeGrid == Me.CubeGrid && w.IsAttached)
                    .Select(w => new WheelWrapper(w, gridProps.MainController, Config))
                    .ToList();
                var maxSteerAngle = double.Parse(Config.GetValueOrDefault("MaxSteeringAngle", "25"));
                var distance = wh.Max(w => Math.Abs(w.ToFocalPoint.Z));
                var hight = wh.Min(w => w.Wheel.Height);
                var radius = distance / Math.Tan(MathHelper.ToRadians(maxSteerAngle));
                wh.ForEach(w =>
                {
                    w.Radius = radius;
                    w.TargetHeight = hight;
                });
                return wh;
            }, "myWheels", Memo.Refs(AllWheels, Config));

        List<WheelWrapper> SubWheels => Memo.Of(() =>
        {
            var sw = AllWheels
                .Where(w => w.CubeGrid != Me.CubeGrid)
                .Select(w => new WheelWrapper(w, gridProps))
                .ToList();
            if (sw.Count > 0)
            {
                var hight = sw.Min(w => w.Wheel.Height);
                sw.ForEach(w => w.TargetHeight = hight);
            }
            return sw;
        }, "subWheels", Memo.Refs(AllWheels));

        double GridUnsprungMass => Memo.Of(() => gridProps.Mass.PhysicalMass - MyWheels.Concat(SubWheels).Sum(w => w.Wheel.Top.Mass), "GridUnsprungWeight", Memo.Refs(gridProps.Mass.PhysicalMass, MyWheels));

        List<IMyShipController> Controllers => Memo.Of(() => Util.GetBlocks<IMyShipController>(b => Util.IsNotIgnored(b, Config["IgnoreTag"])), "controllers", 100);

        struct GridPower
        {
            public float MaxOutput;
            public float CurrentOutput;
        }

        GridPower PowerProducersPower => Memo.Of(() =>
        {
            var blocks = Util.GetBlocks<IMyPowerProducer>(b =>
            {
                return (b.Enabled && !(b is IMyBatteryBlock))
                    || (b.Enabled && b is IMyBatteryBlock && (b as IMyBatteryBlock).ChargeMode != ChargeMode.Recharge);
            });
            return new GridPower
            {
                MaxOutput = blocks.Sum(b => b.MaxOutput),
                CurrentOutput = blocks.Sum(b => b.CurrentOutput)
            };
        }, "myPowerProducers", 10);

    }
}
