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

        IEnumerable<IMyGyro> Gyros => Memo.Of(() => Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, Config["IgnoreTag"]) && b.Enabled), "gyros", Memo.Refs(gridProps.Mass.BaseMass, Config));

        IEnumerable FlipGridTask()
        {
            if (gridProps.Flipping) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;
            var ini = Config;
            var pidRoll = new PID(ini.GetValueOrDefault("PIDFlip", "8/0/0/0"));
            foreach (var g in gyroList)
            {
                g.GyroOverride = true; g.GyroPower = 1;

            }
            gridProps.Flipping = true;
            while (ini.Equals(Config) && !(Util.IsBetween(gridProps.Roll, -25, 25) || gridProps.UpDown < 0))
            {
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                // var power = Util.NormalizeValue(Math.Abs(gridProps.Roll), 0, 180, 5, 100);
                var rollSpeed = MathHelper.Clamp(pidRoll.Signal(gridProps.Roll, dt), -60, 60);
                Util.ApplyGyroOverride(0, 0, -rollSpeed, gyroList, gridProps.MainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.GyroPower = 1; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
            gridProps.Flipping = false;
        }

        IEnumerable AutoLevelTask()
        {
            if (!gridProps.AutoLevel) yield break;
            var ini = Config;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var pidRoll = new PID(ini.GetValueOrDefault("PIDRoll", "6/0/0/0"));
            var pidPitch = new PID(ini.GetValueOrDefault("PIDPitch", "3/0/0/0"));
            var gyroPower = float.Parse(ini.GetValueOrDefault("AutoLevelPower", "30"));
            var isFastEnough = gridProps.Speed > 5;
            if (isFastEnough)
            {
                foreach (var g in gyroList)
                {
                    g.GyroOverride = true; g.GyroPower = gyroPower / 100;
                }
            }
            while (ini.Equals(Config) && gridProps.AutoLevel && isFastEnough)
            {
                isFastEnough = gridProps.Speed > 5;
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = MathHelper.Clamp(pidRoll.Signal(gridProps.Roll, dt), -60, 60);
                var pitchSpeed = MathHelper.Clamp(pidPitch.Signal(gridProps.Pitch - 5, dt), -60, 60);
                Util.ApplyGyroOverride(pitchSpeed, 0, -rollSpeed, gyroList, gridProps.MainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.GyroPower = 1; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
        }
    }
}