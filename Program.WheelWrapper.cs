using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using System.Linq;
using System.Numerics;

namespace IngameScript
{
    partial class Program
    {
        IEnumerable<IMyMotorSuspension> AllWheels => Memo.Of(() => Util.GetBlocks<IMyMotorSuspension>(b => Util.IsNotIgnored(b, Config["IgnoreTag"].ToString()) && b.Enabled && b.IsSameConstructAs(Me)), "wheels", Memo.Refs(Mass.BaseMass));

        IEnumerable<WheelWrapper> MyWheels => Memo.Of(() =>
        {
            var T = MatrixD.Transpose(Controllers.MainController.WorldMatrix);
            var config = Config;
            var wh = AllWheels
                .Where(w => w.CubeGrid == Me.CubeGrid)
                .Select(w => new WheelWrapper(w, Controllers.MainController, config, T));
            var maxSteerAngle = config["MaxSteeringAngle"].ToDouble(25);
            var distance = wh.Max(w => Math.Abs(w.ToFocalPoint.Z));
            var hight = wh.Min(w => w.Wheel.Height);
            var radius = distance / Math.Tan(MathHelper.ToRadians(maxSteerAngle));

            return wh.Select(w =>
            {
                w.TargetHeight = hight;
                var halfWidth = Math.Abs(w.ToFocalPoint.X);
                w.SteerAngleLeft = Math.Atan(w.DistanceFocal / (radius + (w.IsLeft ? -halfWidth : halfWidth)));
                w.SteerAngleRight = Math.Atan(w.DistanceFocal / (radius + (w.IsLeft ? halfWidth : -halfWidth)));
                return w;
            }).ToArray();
        }, "myWheels", Memo.Refs(AllWheels, Config));

        IEnumerable<WheelWrapper> SubWheels => Memo.Of(() =>
        {
            var T = MatrixD.Transpose(Controllers.MainController.WorldMatrix);
            var sw = AllWheels
                .Where(w => w.CubeGrid != Me.CubeGrid)
                .Select(w => new WheelWrapper(w, Controllers, T));
            if (sw.Count() > 0)
            {
                var hight = sw.Min(w => w.Wheel.Height);
                sw = sw.Select(w =>
                {
                    w.TargetHeight = hight;
                    return w;
                });
            }
            return sw.ToArray();
        }, "subWheels", Memo.Refs(AllWheels));


        class WheelWrapper
        {
            public IMyMotorSuspension Wheel;
            public float SpeedLimit { get { return Wheel.GetValueFloat("Speed Limit"); } set { Wheel.SetValueFloat("Speed Limit", value); } }
            public Vector3D ToCoM = Vector3D.Zero;
            public Vector3D ToFocalPoint = Vector3D.Zero;
            public float HeightOffsetMin => Wheel.GetMinimum<float>("Height");
            public float HeightOffsetMax => Wheel.GetMaximum<float>("Height");
            public float TargetHeight;
            public double DistanceCoM => Math.Abs(ToCoM.Z);
            public double DistanceFocal => Math.Abs(ToFocalPoint.Z);
            public double TargetStrength = 5;
            public string Debug = "";
            public float Friction { get { return Wheel.Friction; } set { Wheel.Friction = value; } }
            public bool IsLeft = false;
            public bool IsFront => ToCoM.Z < 0;
            public bool IsFrontFocal => ToFocalPoint.Z < 0;
            public double WeightRatio = 1;
            public double BlackMagicFactor;
            public double SteerAngleLeft;
            public double SteerAngleRight;
            public double MaxPower;

            public WheelWrapper(IMyMotorSuspension wheel, IMyShipController controller, Dictionary<string, MyIniValue> ini, MatrixD T)
            {
                Wheel = wheel;
                var RC = ini["AckermanFocalPoint"].ToString("CoM") == "RC" && controller is IMyRemoteControl;

                IsLeft = Wheel.Orientation.Up == controller.Orientation.Left;
                TargetStrength = wheel.Strength;

                if (wheel.Top != null)
                {
                    var wheelPos = wheel.Top.GetPosition();

                    ToCoM = Vector3D.TransformNormal(wheelPos - controller.CenterOfMass, T);
                    ToFocalPoint = RC ? Vector3D.TransformNormal(wheelPos - controller.GetPosition(), T) : ToCoM;
                    ToFocalPoint.Z += ini["AckermanFocalPointOffset"].ToDouble();
                }

                var subType = Wheel.BlockDefinition.SubtypeName;
                var isSmallGrid = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small;
                var isBigWheel = subType.Contains("5x5");
                
                BlackMagicFactor = isSmallGrid
                    ? isBigWheel ? 18.5 : 15
                    : isBigWheel ? 55 : 52.5;

                Wheel.InvertSteer = IsFront != IsFrontFocal;

                MaxPower =
                    subType.Contains("5x5") ? (isSmallGrid ? 0.3 : 1.5) :
                    subType.Contains("3x3") ? (isSmallGrid ? 0.2 : 1) :
                    subType.Contains("2x2") ? (isSmallGrid ? 0.15 : 0.8) :
                    subType.Contains("1x1") ? (isSmallGrid ? 0.1 : 0.5) : 0;
            }

            public WheelWrapper(IMyMotorSuspension wheel, TControllers props, MatrixD T)
            {
                Wheel = wheel;

                var wheelUp = Vector3D.TransformNormal(Wheel.WorldMatrix.Up, T);
                IsLeft = Base6Directions.GetDirection(wheelUp) == props.MainController.Orientation.Left;
                TargetStrength = wheel.Strength;

                if (wheel.Top != null)
                {
                    var center = props.SubController == null ? wheel.CubeGrid.WorldVolume.Center : props.SubController.CenterOfMass;
                    ToCoM = Vector3D.TransformNormal(wheel.Top.GetPosition() - center, T);
                }

                var isBigWheel = Wheel.BlockDefinition.SubtypeName.Contains("5x5");
                var isSmallGrid = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small;
                var subType = Wheel.BlockDefinition.SubtypeName;

                BlackMagicFactor = isSmallGrid
                    ? isBigWheel ? 18.5 : 15
                    : isBigWheel ? 55 : 52.5;

                MaxPower =
                    subType.Contains("5x5") ? (isSmallGrid ? 0.3 : 1.5) :
                    subType.Contains("3x3") ? (isSmallGrid ? 0.2 : 1) :
                    subType.Contains("2x2") ? (isSmallGrid ? 0.15 : 0.8) :
                    subType.Contains("1x1") ? (isSmallGrid ? 0.1 : 0.5) : 0;
            }
        }
    }
}
