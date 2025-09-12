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
            public double Pitch;
            public double Yaw;
            public double Elevation;
        }

        GridOrientation OrientationResult = new GridOrientation();

        IEnumerable GridOrientationsTask()
        {
            while (true)
            {
                var controller = Controllers.MainController;
                var speed = controller.GetShipVelocities().LinearVelocity.Normalized();
                var grav = Gravity;
                var matrix = controller.WorldMatrix;
                // var yaw = Math.Atan2(speed.Dot(matrix.Right), speed.Dot(matrix.Forward));
                var roll = Math.Atan2(grav.Dot(matrix.Right), grav.Dot(matrix.Down));
                var pitch = Math.Atan2(grav.Dot(matrix.Backward), grav.Dot(matrix.Down));
                double elevation;
                controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation);

                OrientationResult.Roll = MathHelper.ToDegrees(roll);
                OrientationResult.Pitch = MathHelper.ToDegrees(pitch);
                // OrientationResult.Yaw = MathHelper.ToDegrees(yaw);
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
        public float ForwardBackward => Controller?.MoveIndicator.Z ?? 0;
        public float LeftRight => Controller?.MoveIndicator.X ?? 0;
        public float UpDown => Controller?.MoveIndicator.Y ?? 0;
        public MyShipMass Mass => Controllers.MainController.CalculateShipMass();
        public Vector3D Gravity => Controllers.MainController.GetTotalGravity();
        public double GravityMagnitude => Gravity.Length();
        public MyShipVelocities Velocities => Controllers.MainController.GetShipVelocities();
        public double Speed => Velocities.LinearVelocity.Length();

        void InitGridProps()
        {
            var controllers = Util.GetBlocks<IMyShipController>(b => Util.IsNotIgnored(b, _ignoreTag) && b.IsSameConstructAs(Me));
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
