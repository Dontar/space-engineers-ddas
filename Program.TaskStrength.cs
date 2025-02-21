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

            return wheels.Sum(w =>
            {
                if (chassisLength < 0.1) return 0;
                w.WeightRatio = w.IsFront
                    ? Math.Abs(Util.NormalizeValue(w.ToCoM.Z, rearMostAxel, frontMostAxel, 0, rearMostAxel / chassisLength))
                    : Math.Abs(Util.NormalizeValue(w.ToCoM.Z, frontMostAxel, rearMostAxel, 0, frontMostAxel / chassisLength));
                return w.WeightRatio * (isTrailer ? 2 : 1);
            });
        }

        struct StrengthTaskResult
        {
            public Action<WheelWrapper, double> Action;
            public Action<WheelWrapper, double> SubAction;
        }
        IEnumerable<StrengthTaskResult> SuspensionStrengthTask()
        {
            var config = Config;
            var myWheels = MyWheels;
            var subWheels = SubWheels;

            var suspensionStrength = config["SuspensionStrength"].ToBoolean(true);
            var subSuspensionStrength = config["SubWheelsStrength"].ToBoolean(true);
            var strengthFactor = config["StrengthFactor"].ToSingle(1);

            if (!suspensionStrength) yield break;

            var normalizeFactor = suspensionStrength && myWheels.Count() > 0 ? CalcStrength(myWheels) : 0;
            var subNormalizeFactor = subSuspensionStrength && subWheels.Count() > 0 ? CalcStrength(subWheels) : 0;

            while (myWheels.Equals(MyWheels) || subWheels.Equals(SubWheels) || config.Equals(Config))
            {
                Action<WheelWrapper, double> action = (w, GridUnsprungWeight) =>
                {
                    w.TargetStrength = Memo.Of(() =>
                        MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100) * strengthFactor,
                    $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight));
                };

                Action<WheelWrapper, double> subAction = (w, GridUnsprungWeight) =>
                {
                    w.TargetStrength = Memo.Of(() =>
                        MathHelper.Clamp(Math.Sqrt(w.WeightRatio / subNormalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100) * strengthFactor,
                    $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight));
                };

                yield return new StrengthTaskResult
                {
                    Action = suspensionStrength ? action : null,
                    SubAction = subSuspensionStrength ? subAction : null
                };
            }
        }
    }
}