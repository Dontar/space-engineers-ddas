using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        class WheelWrapper
        {
            public IMyMotorSuspension Wheel;
            public float SpeedLimit { get { return Wheel.GetValueFloat("Speed Limit"); } set { Wheel.SetValueFloat("Speed Limit", value); } }
            // public float SteerOverride { get { return Wheel.GetValueFloat("Steer override"); } set { Wheel.SetValueFloat("Steer override", value); } }
            public Vector3D ToCoM = Vector3D.Zero;
            public Vector3D ToFocalPoint = Vector3D.Zero;
            public double Radius;
            public float HeightOffsetMin => Wheel.GetMinimum<float>("Height");
            public float HeightOffsetMax => Wheel.GetMaximum<float>("Height");
            public float TargetHeight;
            public double DistanceCoM => Math.Abs(ToCoM.Z);
            public double DistanceFocal => Math.Abs(ToFocalPoint.Z);
            public double TargetStrength = 5;
            public string Debug = "";
            public float Friction { get { return Wheel.Friction; } set { Wheel.Friction = value; } }
            public bool IsLeft => ToCoM.X < 0;
            public bool IsFront => ToCoM.Z < 0;
            public bool IsFrontFocal => ToFocalPoint.Z < 0;
            public double WeightRatio = 1;
            public double BlackMagicFactor;

            public double SteerAngleLeft
            {
                get
                {
                    Wheel.InvertSteer = IsFront != IsFrontFocal;
                    var halfWidth = Math.Abs(ToFocalPoint.X);
                    return Math.Atan(DistanceFocal / (Radius + (IsLeft ? -halfWidth : halfWidth)));
                }
            }

            public double SteerAngleRight
            {
                get
                {
                    Wheel.InvertSteer = IsFront != IsFrontFocal;
                    var halfWidth = Math.Abs(ToFocalPoint.X);
                    return Math.Atan(DistanceFocal / (Radius + (IsLeft ? halfWidth : -halfWidth)));
                }
            }

            public double MaxPower
            {
                get
                {
                    var isSmall = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small;
                    var subType = Wheel.BlockDefinition.SubtypeName;
                    return
                        subType.Contains("5x5") ? (isSmall ? 0.3 : 1.5) :
                        subType.Contains("3x3") ? (isSmall ? 0.2 : 1) :
                        subType.Contains("2x2") ? (isSmall ? 0.15 : 0.8) :
                        subType.Contains("1x1") ? (isSmall ? 0.1 : 0.5) : 0;
                }
            }

            public WheelWrapper(IMyMotorSuspension wheel, IMyShipController controller, Dictionary<string, MyIniValue> ini)
            {
                Wheel = wheel;
                var RC = ini["AckermanFocalPoint"].ToString("CoM") == "RC" && controller is IMyRemoteControl;
                var transposition = MatrixD.Transpose(controller.WorldMatrix);
                var wheelPos = wheel.Top.GetPosition();
                ToCoM = Vector3D.TransformNormal(wheelPos - controller.CenterOfMass, transposition);
                ToFocalPoint = RC ? Vector3D.TransformNormal(wheelPos - controller.GetPosition(), transposition) : ToCoM;
                ToFocalPoint.Z += ini["AckermanFocalPointOffset"].ToDouble();
                var isBigWheel = Wheel.BlockDefinition.SubtypeName.Contains("5x5");
                BlackMagicFactor = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small
                    ? isBigWheel ? 18.5 : 15
                    : isBigWheel ? 55 : 52.5;
            }
            public WheelWrapper(IMyMotorSuspension wheel, GridProps props)
            {
                Wheel = wheel;
                var center = props.SubController == null ? wheel.CubeGrid.WorldVolume.Center : props.SubController.CenterOfMass;
                ToCoM = Vector3D.TransformNormal(wheel.Top.GetPosition() - center, MatrixD.Transpose(props.MainController.WorldMatrix));
                var isBigWheel = Wheel.BlockDefinition.SubtypeName.Contains("5x5");
                BlackMagicFactor = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small
                    ? isBigWheel ? 18.5 : 15
                    : isBigWheel ? 55 : 52.5;
            }

        }
    }
}
