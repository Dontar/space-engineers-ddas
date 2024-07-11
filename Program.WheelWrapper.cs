using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRageMath;
using VRage.Game;

namespace IngameScript
{
    partial class Program
    {
        class WheelWrapper
        {
            public IMyMotorSuspension Wheel;
            public float SpeedLimit { get { return Wheel.GetValueFloat("Speed Limit"); } set { Wheel.SetValueFloat("Speed Limit", (float)value); } }
            public float SteerOverride { get { return Wheel.GetValueFloat("Steer override"); } set { Wheel.SetValueFloat("Steer override", (float)value); } }
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

            public WheelWrapper(IMyMotorSuspension wheel, IMyShipController controller, Dictionary<string, string> ini)
            {
                Wheel = wheel;
                TargetHeight = HeightOffsetMin;
                var RC = ini.GetValueOrDefault("AckermanFocalPoint", "CoM") == "RC" && controller is IMyRemoteControl;
                var transposition = MatrixD.Transpose(controller.WorldMatrix);
                var wheelPos = wheel.Top.GetPosition();
                ToCoM = Vector3D.TransformNormal(wheelPos - controller.CenterOfMass, transposition);
                ToFocalPoint = RC ? Vector3D.TransformNormal(wheelPos - controller.GetPosition(), transposition) : ToCoM;
                ToFocalPoint.Z += double.Parse(ini.GetValueOrDefault("AckermanFocalPointOffset", "0"));
                BlackMagicFactor = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small
                    ? Wheel.BlockDefinition.SubtypeName.Contains("5x5") ? 18.5 : 15
                    : Wheel.BlockDefinition.SubtypeName.Contains("5x5") ? 55 : 52.5;
            }
            public WheelWrapper(IMyMotorSuspension wheel, GridProps props)
            {
                Wheel = wheel;
                TargetHeight = HeightOffsetMin;
                ToCoM = Vector3D.TransformNormal(wheel.Top.GetPosition() - props.SubController.CenterOfMass, MatrixD.Transpose(props.MainController.WorldMatrix));
                BlackMagicFactor = Wheel.CubeGrid.GridSizeEnum == MyCubeSize.Small
                    ? Wheel.BlockDefinition.SubtypeName.Contains("5x5") ? 18.5 : 15
                    : Wheel.BlockDefinition.SubtypeName.Contains("5x5") ? 55 : 52.5;
            }

        }
    }
}
