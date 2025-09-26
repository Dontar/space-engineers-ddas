using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        double CalcStrength(IEnumerable<WheelWrapper> wheels)
        {
            if (wheels.Count() == 0) return 0;
            var frontMostAxel = wheels.Min(w => w.ToCoM.Z);
            var rearMostAxel = wheels.Max(w => w.ToCoM.Z);
            var chassisLength = rearMostAxel + Math.Abs(frontMostAxel);

            var isTrailer = frontMostAxel > 0;
            // is trailer no front wheels
            if (isTrailer)
            {
                frontMostAxel = -rearMostAxel;
                chassisLength = rearMostAxel * 2;
            }

            return wheels.Sum(w =>
            {
                if (chassisLength < 0.1) return 0;
                w.WeightRatio = w.IsFront
                    ? Math.Abs(Util.NormalizeValue(w.ToCoM.Z, rearMostAxel, frontMostAxel, 0, rearMostAxel / chassisLength))
                    : Math.Abs(Util.NormalizeValue(w.ToCoM.Z, frontMostAxel, rearMostAxel, 0, frontMostAxel / chassisLength));
                return w.WeightRatio/*  * (isTrailer ? 2 : 1) */;
            });
        }

        void InitStrength()
        {
            var gridUnsprungWeight = GridUnsprungMass * GravityMagnitude;
            if (_suspensionStrength)
            {
                var normalizeFactor = CalcStrength(MyWheels);
                foreach (var w in MyWheels)
                {
                    w.TargetStrength = MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * gridUnsprungWeight) / w.BlackMagicFactor * _strengthFactor, 5, 100);
                }
            }

            if (_subWheelsStrength)
            {
                var normalizeFactor = CalcStrength(SubWheels);
                foreach (var w in SubWheels)
                {
                    w.TargetStrength = MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * gridUnsprungWeight) / w.BlackMagicFactor * _strengthFactor, 5, 100);
                }
            }
        }
    }
}
