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
        public bool Flipping { get; set; } = false;
        public bool AutoLevel { get; set; } = false;

        IEnumerable<IMyGyro> Gyros => Memo.Of(() => Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, Config["IgnoreTag"].ToString()) && b.Enabled), "gyros", Memo.Refs(Mass.BaseMass, Config));

        IEnumerable FlipGridTask()
        {
            if (Flipping) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var ini = Config;
            var orientation = TaskManager.TaskResults.OfType<GridOrientation>().FirstOrDefault();
            var pidRoll = new PID(ini["PIDFlip"].ToString("8/0/0/0"));

            foreach (var g in gyroList)
            {
                g.GyroOverride = true; g.GyroPower = 1;
            }

            Flipping = true;
            while (ini.Equals(Config) && !(Util.IsBetween(orientation.Roll, -25, 25) || UpDown < 0))
            {
                orientation = TaskManager.TaskResults.OfType<GridOrientation>().FirstOrDefault();
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -30, 30);
                Util.ApplyGyroOverride(0, 0, -rollSpeed, gyroList, Controllers.MainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.GyroPower = 1; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
            Flipping = false;
        }

        IEnumerable AutoLevelTask()
        {
            if (!AutoLevel) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var mainController = Controllers.MainController;
            var ini = Config;
            var pidRoll = new PID(ini["PIDRoll"].ToString("6/0/0/0"));
            var pidPitch = new PID(ini["PIDPitch"].ToString("3/0/0/0"));
            var gyroPower = float.Parse(ini["AutoLevelPower"].ToString("30"));
            var isFastEnough = Speed > 5;
            if (isFastEnough)
            {
                foreach (var g in gyroList)
                {
                    g.GyroOverride = true; g.GyroPower = gyroPower / 100;
                }
            }
            while (ini.Equals(Config) && AutoLevel && isFastEnough)
            {
                var orientation = TaskManager.TaskResults.OfType<GridOrientation>().FirstOrDefault();
                isFastEnough = Speed > 5;
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -30, 30);
                var pitchSpeed = Util.NormalizeClamp(pidPitch.Signal(orientation.Pitch - 5, dt), -180, 180, -30, 30);
                Util.ApplyGyroOverride(pitchSpeed, 0, -rollSpeed, gyroList, mainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.GyroPower = 1; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
        }
    }
}