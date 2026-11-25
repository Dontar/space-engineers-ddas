using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        double CalcStrength(IEnumerable<WheelWrapper> wheels) {
            var all = wheels.ToList();
            if (all.Count() == 0)
                return 0;

            var frontMostAxel = all.Min(w => w.ToCoM.Z);
            var rearMostAxel = all.Max(w => w.ToCoM.Z);
            var chassisLength = rearMostAxel + Math.Abs(frontMostAxel);

            var isTrailer = frontMostAxel > 0;
            if (isTrailer) {
                frontMostAxel = -rearMostAxel;
                chassisLength = rearMostAxel * 2;
            }

            // --- Lateral CoM offset, in the same CoM-relative X frame the wheels use ---
            var xs = all.Select(w => w.ToCoM.X).ToList();
            double minX = xs.Min(), maxX = xs.Max();
            double centerX = (minX + maxX) * 0.5;  // vehicle center in CoM frame
            double halfTrack = (maxX - minX) * 0.5; // half track width
            double comX = -centerX;                // CoM offset from vehicle center

            return all.Sum(w => {
                if (chassisLength < 0.1)
                    return 0;
                double zRatio = w.IsFront
                    ? Math.Abs(Util.NormalizeValue(w.ToCoM.Z, rearMostAxel, frontMostAxel, 0, rearMostAxel / chassisLength))
                    : Math.Abs(Util.NormalizeValue(w.ToCoM.Z, frontMostAxel, rearMostAxel, 0, frontMostAxel / chassisLength));

                double xRatio = 1.0;
                if (halfTrack > 0.05) {
                    double side = Math.Sign(w.ToCoM.X - centerX); // +1 / -1, which side of center
                    xRatio = MathHelper.Clamp(0.5 * (1 + comX / halfTrack * side), 0.0, 1.0);
                }

                w.WeightRatio = zRatio * xRatio;  // longitudinal × lateral
                return w.WeightRatio;
            });
        }

        void InitStrength() {
            var gridUnsprungWeight = GridUnsprungMass * GravityMagnitude;
            if (_suspensionStrength) {
                var normalizeFactor = CalcStrength(MyWheels);
                foreach (var w in MyWheels) {
                    w.TargetStrength = MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * gridUnsprungWeight) / w.BlackMagicFactor * _strengthFactor, 5, 100);
                }
            }

            if (_subWheelsStrength) {
                var normalizeFactor = CalcStrength(SubWheels);
                foreach (var w in SubWheels) {
                    w.TargetStrength = MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * gridUnsprungWeight) / w.BlackMagicFactor * _strengthFactor, 5, 100);
                }
            }
        }
    }
}
