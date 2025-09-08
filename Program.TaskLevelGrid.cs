using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        bool Flipping = false;

        IEnumerable<IMyGyro> Gyros;

        void InitAutoLevel()
        {
            Gyros = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, _ignoreTag));
        }

        IEnumerable FlipGridTask()
        {
            if (Flipping) yield break;
            if (Gyros.Count() == 0) yield break;

            var orientation = OrientationResult;
            var pidRoll = new PID(_pidRoll);
            var pidPower = new PID(_pidLevelPower);

            Flipping = true;
            TaskManager.PauseTask(_AutoLevelTask, true);
            while (!(Util.IsBetween(orientation.Roll, -25, 25) || UpDown < 0))
            {
                orientation = OrientationResult;
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -60, 60);
                var powerError = Math.Abs(orientation.Roll);
                var power = MathHelperD.Clamp(pidPower.Signal(powerError, dt), 20, 100);
                Util.ApplyGyroOverride(0, 0, -rollSpeed, (float)power, Gyros, Controllers.MainController.WorldMatrix);
                yield return null;
            }
            pidRoll.Clear();
            pidPower.Clear();

            ResetGyros();
            Flipping = false;
            TaskManager.PauseTask(_AutoLevelTask, !_autoLevel);
        }

        private void ResetGyros()
        {
            foreach (var g in Gyros)
            {
                g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
        }

        IEnumerable AutoLevelTask()
        {
            if (Gyros.Count() == 0) yield break;
            var isFastEnough = Speed * 3.6 > 20;
            if (!isFastEnough) yield break;
            var mainController = Controllers.MainController;

            var pidRoll = new PID(_pidRoll);
            var pidPitch = new PID(_pidPitch);
            var pidPower = new PID(_pidLevelPower);

            while (isFastEnough)
            {
                var orientation = OrientationResult;
                var currentElevation = orientation.Elevation;
                if (currentElevation > 4)
                {
                    isFastEnough = Speed * 3.6 > 20;
                    var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                    var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -60, 60);
                    var pitchSpeed = Util.NormalizeClamp(pidPitch.Signal(orientation.Pitch - 5, dt), -180, 180, -60, 60);
                    var powerError = Math.Abs(orientation.Pitch - 5 + orientation.Roll);
                    var power = MathHelperD.Clamp(pidPower.Signal(powerError, dt), 20, 100);
                    // var yawSpeed = Util.NormalizeClamp(orientation.Yaw, -180, 180, -60, 60);
                    Util.ApplyGyroOverride(pitchSpeed, /* yawSpeed */0, -rollSpeed, (float)power, Gyros, mainController.WorldMatrix);
                }
                else
                {
                    pidRoll.Clear();
                    pidPitch.Clear();
                    pidPower.Clear();
                    ResetGyros();
                }
                yield return null;
            }
            ResetGyros();
        }
    }
}
