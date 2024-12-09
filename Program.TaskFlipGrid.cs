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
        IEnumerable FlipGridTask()
        {
            if (gridProps.Flipping) yield break;
            var ini = Config;
            var gyroList = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, ini["IgnoreTag"]));
            if (gyroList.Count == 0) yield break;
            var pidRoll = new PID(ini.GetValueOrDefault("PIDFlip", "10/0/0/0"));
            gyroList.ForEach(g => g.GyroOverride = true);
            gridProps.Flipping = true;
            while (ini.Equals(Config))
            {
                if (Util.IsBetween(gridProps.Roll, -25, 25) || gridProps.UpDown < 0)
                {
                    gyroList.ForEach(g => { g.GyroPower = 100; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false; });
                    gridProps.Flipping = false;
                    yield break;
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                // var power = Util.NormalizeValue(Math.Abs(gridProps.Roll), 0, 180, 5, 100);
                var rollSpeed = MathHelper.Clamp(pidRoll.Signal(gridProps.Roll, dt), -60, 60);
                gyroList.ForEach(g =>
                {
                    // g.GyroPower = (float)power;
                    Util.ApplyGyroOverride(0, 0, -rollSpeed, g, gridProps.MainController.WorldMatrix);
                });
                yield return null;
            }
        }
    }
}