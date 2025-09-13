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
            Gyros = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, _ignoreTag) && b.IsSameConstructAs(Me));
        }

        IEnumerable FlipGridTask()
        {
            if (Flipping || Gyros.Count() == 0) yield break;

            Flipping = true;
            TaskManager.PauseTask(_AutoLevelTask, true);

            var roll = -60 * Math.Sign(OrientationResult.Roll);
            Util.ApplyGyroOverride(0, 0, roll, 1f, Gyros, Controllers.MainController.WorldMatrix);
            while (Math.Abs(OrientationResult.Roll) > 25 && UpDown == 0)
            {
                if (Math.Abs(OrientationResult.Roll) < 90)
                {
                    roll = -30 * Math.Sign(OrientationResult.Roll);
                    Util.ApplyGyroOverride(0, 0, roll, 1f, Gyros, Controllers.MainController.WorldMatrix);
                }
                yield return null;
            }
            ResetGyros();
            Flipping = false;
            TaskManager.PauseTask(_AutoLevelTask, !_autoLevel);
        }

        private void ResetGyros()
        {
            foreach (var g in Gyros)
            {
                g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false; g.GyroPower = 1;
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
                    var rollSpeed = MathHelper.Clamp(pidRoll.Signal(orientation.Roll, dt) * 60 / 180, -60, 60);
                    var pitchSpeed = MathHelper.Clamp(pidPitch.Signal(orientation.Pitch - 5, dt) * 60 / 180, -60, 60);
                    var powerError = Math.Abs(orientation.Pitch - 5 + orientation.Roll);
                    var power = MathHelperD.Clamp(Math.Round(pidPower.Signal(powerError, dt) * 5 / 270 / 5, 1), 0.2, 1);

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
        public double DitherWindow(double rpm, double angleError, double dt, ref bool activeState)
        {
            double absRpm = Math.Abs(rpm);
            double absAngle = Math.Abs(angleError);
            var A = _flipDitherAmplitude;
            var F = _flipDitherFrequency;

            // Toggle activeState based on thresholds
            activeState = (activeState && !(absRpm > 5 || absAngle < 60))
                       || (!activeState && absRpm < 1 && absAngle >= 60);

            return (activeState ? 1.0 : 0.0) * (A * Math.Sin(2 * Math.PI * F * dt));
        }
    }
}
