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

        IEnumerable<IMyGyro> Gyros;

        void InitAutoLevel()
        {
            Gyros = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, _ignoreTag) && b.Enabled);
        }
        IEnumerable FlipGridTask()
        {
            if (Flipping) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var orientation = TaskManager.GetTaskResult<GridOrientation>();

            var OldAutoLevel = AutoLevel;
            Flipping = true;
            AutoLevel = false;
            while (!(Util.IsBetween(orientation.Roll, -25, 25) || UpDown < 0))
            {
                orientation = TaskManager.GetTaskResult<GridOrientation>();
                var rollSpeed = Util.NormalizeClamp(orientation.Roll, -180, 180, -60, 60);
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
            if (!(AutoLevel && isFastEnough)) yield break;
            var gyroList = Gyros;
            if (gyroList.Count() == 0) yield break;

            var mainController = Controllers.MainController;
            while (AutoLevel && isFastEnough)
            {
                var orientation = TaskManager.GetTaskResult<GridOrientation>();
                isFastEnough = Speed > 5;
                var rollSpeed = Util.NormalizeClamp(orientation.Roll, -180, 180, -60, 60);
                var pitchSpeed = Util.NormalizeClamp(orientation.Pitch - 5, -180, 180, -60, 60);
                var yawSpeed = Util.NormalizeClamp(orientation.Yaw, -180, 180, -60, 60);
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
