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
            var chassisLength = rearMostAxel - frontMostAxel;

            var isTrailer = frontMostAxel > 0;
            // is trailer no front wheels
            if (isTrailer)
            {
                frontMostAxel = -rearMostAxel;
                chassisLength = wheels.First().Wheel.CubeGrid.WorldVolume.Radius * 2;
            }

            double normalizeFactor = 0;
            foreach (var w in wheels)
            {
                if (chassisLength < 0.1) break;
                w.WeightRatio = w.IsFront
                    ? Math.Abs(Util.NormalizeValue(w.ToCoM.Z, rearMostAxel, frontMostAxel, 0, rearMostAxel / chassisLength))
                    : Math.Abs(Util.NormalizeValue(w.ToCoM.Z, frontMostAxel, rearMostAxel, 0, frontMostAxel / chassisLength));
                normalizeFactor += w.WeightRatio * (isTrailer ? 2 : 1);
            }
            return normalizeFactor;
        }

        struct StrengthTaskResult
        {
            public Action<WheelWrapper, double> action;
        }
        IEnumerable<StrengthTaskResult> SuspensionStrengthTask()
        {
            var myWheels = MyWheels;
            if (myWheels.Count() == 0) yield break;
            var strengthFactor = Config["StrengthFactor"].ToSingle(1);
            double normalizeFactor = CalcStrength(myWheels);
            while (myWheels.Equals(MyWheels))
            {
                yield return new StrengthTaskResult
                {
                    action = (w, GridUnsprungWeight) =>
                    {
                        w.TargetStrength = Memo.Of(() =>
                            MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100) * strengthFactor
                        , $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight));
                        w.Wheel.Strength += (float)((w.TargetStrength - w.Wheel.Strength) * 0.5);
                    }
                };
            }
        }

        struct SubStrengthTaskResult
        {
            public Action<WheelWrapper, double> action;
        }
        IEnumerable<SubStrengthTaskResult> SubSuspensionStrengthTask()
        {
            var subWheels = SubWheels;
            if (subWheels.Count() == 0) yield break;
            var strengthFactor = Config["StrengthFactor"].ToSingle(1);
            double normalizeFactor = CalcStrength(subWheels);
            while (subWheels.Equals(SubWheels))
            {
                yield return new SubStrengthTaskResult
                {
                    action = (w, GridUnsprungWeight) =>
                    {
                        w.TargetStrength = Memo.Of(() =>
                            MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100) * strengthFactor,
                            $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight)
                        );
                        w.Wheel.Strength += (float)((w.TargetStrength - w.Wheel.Strength) * 0.5);
                    }
                };
            }
        }
    }
}