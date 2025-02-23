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
            var orientation = TaskManager.GetTaskResult<GridOrientation>();
            var pidRoll = new PID(ini["PIDFlip"].ToString("8/0/0/0"));

            var OldAutoLevel = AutoLevel;
            AutoLevel = false;
            Flipping = true;
            while (ini.Equals(Config) && !(Util.IsBetween(orientation.Roll, -25, 25) || UpDown < 0))
            {
                orientation = TaskManager.GetTaskResult<GridOrientation>();
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -60, 60);
                Util.ApplyGyroOverride(0, 0, -rollSpeed, gyroList, Controllers.MainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
            Flipping = false;
            AutoLevel = OldAutoLevel;
        }

        IEnumerable AutoLevelTask()
        {
            var isFastEnough = Speed > 5;
            if (!AutoLevel || !isFastEnough) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var mainController = Controllers.MainController;
            var ini = Config;
            var pidRoll = new PID(ini["PIDRoll"].ToString("6/0/0/0"));
            var pidPitch = new PID(ini["PIDPitch"].ToString("3/0/0/0"));
            var pidYaw = new PID(ini["PIDYaw"].ToString("6/0/0/0"));
            // var gyroPower = float.Parse(ini["AutoLevelPower"].ToString("30"));
            while (ini.Equals(Config) && AutoLevel && isFastEnough)
            {
                var orientation = TaskManager.GetTaskResult<GridOrientation>();
                isFastEnough = Speed > 5;
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var rollSpeed = Util.NormalizeClamp(pidRoll.Signal(orientation.Roll, dt), -180, 180, -60, 60);
                var pitchSpeed = Util.NormalizeClamp(pidPitch.Signal(orientation.Pitch - 5, dt), -180, 180, -60, 60);
                var yawSpeed = Util.NormalizeClamp(pidYaw.Signal(orientation.Yaw, dt), -180, 180, -60, 60);
                Util.ApplyGyroOverride(pitchSpeed, yawSpeed, -rollSpeed, gyroList, mainController.WorldMatrix);
                yield return null;
            }
            foreach (var g in gyroList)
            {
                g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false;
            }
        }
    }
}