using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        struct GridOrientation
        {
            public double Roll;
            public double Pitch;
            public double Yaw;
        }
        IEnumerable<GridOrientation> GridOrientationsTask()
        {
            while (true)
            {
                var speed = Controllers.MainController.GetShipVelocities().LinearVelocity.Normalized();
                var grav = Gravity;
                var matrix = Controllers.MainController.WorldMatrix;
                var yaw = Math.Atan2(speed.Dot(matrix.Right), speed.Dot(matrix.Forward));
                var roll = Math.Atan2(grav.Dot(matrix.Right), grav.Dot(matrix.Down));
                var pitch = Math.Atan2(grav.Dot(matrix.Backward), grav.Dot(matrix.Down));

                yield return new GridOrientation { Roll = MathHelper.ToDegrees(roll), Pitch = MathHelper.ToDegrees(pitch), Yaw = MathHelper.ToDegrees(yaw) };
            }
        }

        class ConfigDictionary : Dictionary<string, MyIniValue>
        {
            public ConfigDictionary(IDictionary<string, MyIniValue> dictionary) : base(dictionary) { }
            public new MyIniValue this[string key]
            {
                get
                {
                    MyIniValue value;
                    return TryGetValue(key, out value) ? value : MyIniValue.EMPTY;
                }
                set { this[key] = value; }
            }
        }

        ConfigDictionary Config => Memo.Of(() =>
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
                // myIni.Set("Options", "AutoLevelPower", 30);

                myIni.Set("Options", "MaxSteeringAngle", 25);
                myIni.Set("Options", "AckermanFocalPoint", "CoM");
                myIni.SetComment("Options", "AckermanFocalPoint", "Possible values RC, CoM - (Remote Control, Center of Mass)");
                myIni.Set("Options", "AckermanFocalPointOffset", 0);
                myIni.SetComment("Options", "AckermanFocalPointOffset", "Offset from the reference point in meters");

                myIni.Set("Options", "FrictionInner", 80);
                myIni.Set("Options", "FrictionOuter", 60);
                myIni.Set("Options", "FrictionMinSpeed", 5);
                myIni.SetComment("Options", "FrictionMinSpeed", "Speed in m/s where the friction adjustment will be applied");

                myIni.Set("Options", "PIDCruise", "0.5/0/0/0");
                myIni.Set("Options", "PIDFlip", "8/0/0/0");
                myIni.Set("Options", "PIDPower", "10/0/0/0");
                myIni.Set("Options", "PIDRoll", "6/0/0/0");
                myIni.Set("Options", "PIDPitch", "3/0/0/0");

                myIni.Set("Options", "AddWheels", "true");
                myIni.Set("Options", "SuspensionStrength", "true");
                myIni.Set("Options", "SubWheelsStrength", "true");
                myIni.Set("Options", "Power", "true");
                myIni.Set("Options", "StopLights", "true");
                myIni.Set("Options", "Friction", "true");
                myIni.Set("Options", "SuspensionHight", "true");
                myIni.Set("Options", "AutoLevel", "true");

                Me.CustomData = myIni.ToString();
            }
            ;
            var keys = new List<MyIniKey>();
            myIni.GetKeys(keys);
            return new ConfigDictionary(keys.ToDictionary(k => k.Name, k => myIni.Get(k.Section, k.Name)));

        }, "myIni", Memo.Refs(Me.CustomData));

        double GridUnsprungMass => Memo.Of(() => Mass.PhysicalMass - MyWheels.Concat(SubWheels).Sum(w => w.Wheel.Top?.Mass ?? 0), "GridUnsprungWeight", Memo.Refs(Mass.PhysicalMass, AllWheels));

        struct TControllers
        {
            public IMyShipController MainController;
            public IMyShipController SubController;
            public IMyShipController[] Controllers;
        }
        TControllers Controllers => Memo.Of(() =>
        {
            var controllers = Util.GetBlocks<IMyShipController>(b => Util.IsNotIgnored(b, Config["IgnoreTag"].ToString()) && b.IsSameConstructAs(Me));
            var myControllers = controllers.Where(c => c.CubeGrid == Me.CubeGrid && c.CanControlShip);
            var subControllers = controllers.Where(c => c.CubeGrid != Me.CubeGrid);
            var tag = Config["Tag"].ToString("{DDAS}");

            var mainController = myControllers.FirstOrDefault(c => Util.IsTagged(c, tag) && c is IMyRemoteControl)
                ?? myControllers.FirstOrDefault(c => c is IMyRemoteControl)
                ?? myControllers.FirstOrDefault();

            var subController = subControllers
                .FirstOrDefault(c => Util.IsTagged(c, tag) && c is IMyRemoteControl)
                ?? subControllers.FirstOrDefault();

            return new TControllers { MainController = mainController, SubController = subController, Controllers = myControllers.ToArray() };

        }, "controllers", 100);

        IMyShipController Controller => Controllers.Controllers.FirstOrDefault(c => c.IsUnderControl) ?? Controllers.MainController;
        public float ForwardBackward => Controller?.MoveIndicator.Z ?? 0;
        public float LeftRight => Controller?.MoveIndicator.X ?? 0;
        public float UpDown => Controller?.MoveIndicator.Y ?? 0;
        public MyShipMass Mass => Controllers.MainController.CalculateShipMass();
        public Vector3D Gravity => Controllers.MainController.GetTotalGravity();
        public double GravityMagnitude => Gravity.Length();
        public double Speed => Controllers.MainController.GetShipSpeed();

        struct GridPower
        {
            public float MaxOutput;
            public float CurrentOutput;
        }
        GridPower PowerProducersPower => Memo.Of(() =>
        {
            var blocks = Memo.Of(() => Util.GetBlocks<IMyPowerProducer>(b => b.IsSameConstructAs(Me)), "PowerProducers", Memo.Refs(Mass.BaseMass));
            var maxPower = Memo.Of(() => blocks.Sum(b => b.Enabled ? b.MaxOutput : 0), "PowerProducersMaxPower", Memo.Refs(blocks));
            return new GridPower
            {
                MaxOutput = maxPower,
                CurrentOutput = blocks.Sum(b => b.CurrentOutput)
            };
        }, "myPowerProducers", 10);

    }
}
