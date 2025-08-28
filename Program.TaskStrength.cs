using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender;
using VRageRender.Animations;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        double CalcStrength(IEnumerable<WheelWrapper> wheels)
        {
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

        struct StrengthTaskResult
        {
            public Action<WheelWrapper, double> Action;
            public Action<WheelWrapper, double> SubAction;
        }
        IEnumerable<StrengthTaskResult> SuspensionStrengthTask()
        {
            var myWheels = MyWheels;
            var subWheels = SubWheels;

            var normalizeFactor = myWheels.Count() > 0 ? CalcStrength(myWheels) : 0;
            var subNormalizeFactor = subWheels.Count() > 0 ? CalcStrength(subWheels) : 0;

            Func<double, Action<WheelWrapper, double>> action = (factor) => (w, GridUnsprungWeight) =>
            {
                w.TargetStrength = MathHelper.Clamp(Math.Sqrt(w.WeightRatio / factor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100) * _strengthFactor;
            };

            while (myWheels == MyWheels && subWheels == SubWheels)
            {
                yield return new StrengthTaskResult
                {
                    Action = action(normalizeFactor),
                    SubAction = action(subNormalizeFactor)
                };
            }
        }
    }
}
