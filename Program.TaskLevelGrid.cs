using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        bool Flipping = false;

        IEnumerable<IMyGyro> Gyros;

        BoundingBox Dimensions;

        void InitAutoLevel() {
            Gyros = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, _ignoreTag));

            var scale = (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) ? 2.5f : 0.5f; // meters
            var min = (Me.CubeGrid.Min - Vector3.Half) * scale;
            var max = (Me.CubeGrid.Max + Vector3.Half) * scale;
            Dimensions = new BoundingBox(min, max);
        }

        float CalcRequiredGyroForce(float roll, int magnitude = 10) {
            if (Gyros.Count() == 0) return 0;

            var width = Dimensions.Width;
            var height = Dimensions.Height;

            var pivotPoint = width / 2;
            var maxForce = Gyros.Sum(g => g.CubeGrid.GridSizeEnum == MyCubeSize.Small ? 448000 : 3.36E+07) * pivotPoint;

            var force = Mass.PhysicalMass * GravityMagnitude * pivotPoint;

            var inertia = 1.0 / 12.0 * Mass.PhysicalMass * (width * width + height * height);
            var torqueAccel = inertia * Math.Abs(roll) * magnitude;

            return (float)(Math.Min(force + torqueAccel, maxForce) / maxForce);
        }

        IEnumerable FlipGridTask() {
            if (Flipping || Gyros.Count() == 0) yield break;

            Flipping = true;
            _AutoLevelTask.Pause();

            var roll = -10 * Math.Sign(OrientationResult.Roll) * MathHelper.RPMToRadiansPerSecond;
            var force = CalcRequiredGyroForce(roll);

            Util.ApplyGyroOverride(0, 0, roll, force, Gyros, Controllers.MainController.WorldMatrix);
            while (Math.Abs(OrientationResult.Roll) > 25 && UpDown == 0) {
                yield return null;
            }
            ResetGyros();
            Flipping = false;
            _AutoLevelTask.Pause(!_autoLevel);
        }

        private void ResetGyros() {
            var ai = Pilot.IsAutoPilotEnabled;
            foreach (var g in Gyros) {
                g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false; g.GyroPower = ai ? 0 : 1;
            }
        }

        IEnumerable AutoLevelTask() {
            if (Gyros.Count() == 0) yield break;
            var isFastEnough = Speed * 3.6 > 20;
            if (!isFastEnough) {
                if (Speed < 1 && Math.Abs(OrientationResult.Roll) >= 60) {
                    Task.RunTask(FlipGridTask()).Once();
                }
                yield break;
            }
            var mainController = Controllers.MainController;

            var pidRoll = new PID(_pidRoll);
            var pidPitch = new PID(_pidPitch);

            var power = CalcRequiredGyroForce(30 * MathHelper.RPMToRadiansPerSecond, 5);
            while (isFastEnough) {
                var orientation = OrientationResult;
                var currentElevation = orientation.Elevation;
                if (currentElevation > 4) {
                    isFastEnough = Speed * 3.6 > 20;
                    var dt = Task.CurrentTaskLastRun.TotalSeconds;
                    var rollSpeed = pidRoll.Signal(orientation.RadRoll, dt);
                    var pitchSpeed = pidPitch.Signal(orientation.RadPitch - MathHelper.ToRadians(5), dt);

                    Util.ApplyGyroOverride(pitchSpeed, /* yawSpeed */0, -rollSpeed, (float)power, Gyros, mainController.WorldMatrix);
                }
                else {
                    pidRoll.Clear();
                    pidPitch.Clear();
                    ResetGyros();
                }
                yield return null;
            }
            ResetGyros();
        }
    }
}
