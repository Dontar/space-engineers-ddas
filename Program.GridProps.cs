using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        struct GridOrientation
        {
            public double Roll;
            public double RadRoll;
            public double Pitch;
            public double RadPitch;
            // public double Yaw;
            public double Elevation;
        }

        GridOrientation OrientationResult = new GridOrientation();

        IEnumerable GridOrientationsTask() {
            while (true) {
                var controller = Controllers.MainController;
                var grav = Gravity.Normalized();
                var matrix = controller.WorldMatrix;
                var roll = Math.Atan2(grav.Dot(matrix.Right), grav.Dot(matrix.Down));
                var pitch = Math.Atan2(grav.Dot(matrix.Backward), grav.Dot(matrix.Down));
                double elevation;
                controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation);

                OrientationResult.RadRoll = roll;
                OrientationResult.Roll = MathHelper.ToDegrees(roll);
                OrientationResult.RadPitch = pitch;
                OrientationResult.Pitch = MathHelper.ToDegrees(pitch);
                OrientationResult.Elevation = elevation;

                yield return null;
            }
        }

        struct TControllers
        {
            public IMyShipController MainController;
            public IMyShipController SubController;
            public IMyShipController[] Controllers;
        }
        TControllers Controllers;

        IMyShipController Controller => Controllers.Controllers.FirstOrDefault(c => c.IsUnderControl) ?? Controllers.MainController;
        float ForwardBackward => Controller?.MoveIndicator.Z ?? 0;
        float LeftRight => Controller?.MoveIndicator.X ?? 0;
        float UpDown => Controller?.MoveIndicator.Y ?? 0;
        MyShipMass Mass => Controllers.MainController.CalculateShipMass();
        Vector3D Gravity => Controllers.MainController.GetTotalGravity();
        double GravityMagnitude => Gravity.Length();
        MyShipVelocities Velocities => Controllers.MainController.GetShipVelocities();
        double Speed => Velocities.LinearVelocity.Length();

        void InitGridProps() {
            var controllers = Util.GetBlocks<IMyShipController>(b => Util.IsNotIgnored(b, _ignoreTag));
            var myControllers = controllers.Where(c => c.CubeGrid == Me.CubeGrid && c.CanControlShip);
            var subControllers = controllers.Where(c => c.CubeGrid != Me.CubeGrid);

            var mainController = myControllers.FirstOrDefault(c => Util.IsTagged(c, _tag) && c is IMyRemoteControl)
                ?? myControllers.FirstOrDefault(c => c is IMyRemoteControl)
                ?? myControllers.FirstOrDefault();

            var subController = subControllers
                .FirstOrDefault(c => Util.IsTagged(c, _tag) && c is IMyRemoteControl)
                ?? subControllers.FirstOrDefault();

            Controllers = new TControllers { MainController = mainController, SubController = subController, Controllers = myControllers.ToArray() };
        }
    }
}
