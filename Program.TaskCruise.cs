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
        struct CruiseTaskResult
        {
            public float Propulsion;
        }
        IEnumerable<CruiseTaskResult> CruiseTask(float cruiseSpeed = -1, Func<bool> cruiseWhile = null)
        {
            if (gridProps.Cruise) yield break;
            var ini = Config;
            gridProps.Cruise = true;
            gridProps.CruiseSpeed = cruiseSpeed > -1 ? cruiseSpeed : (float)(gridProps.Speed * 3.6);
            cruiseWhile = cruiseWhile ?? (() => gridProps.UpDown == 0);
            var pid = new PID(ini.GetValueOrDefault("PIDCruise", "0.5/0/0/0"));
            while (ini.Equals(Config) && cruiseWhile())
            {
                if (cruiseSpeed == -1)
                {
                    gridProps.CruiseSpeed = MathHelper.Clamp(gridProps.CruiseSpeed + (float)gridProps.ForwardBackward * -5f, 5, MyWheels.First().SpeedLimit);
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = gridProps.Speed * 3.6;
                var targetSpeed = gridProps.CruiseSpeed;
                var error = targetSpeed - currentSpeedKmh;
                var propulsion = MathHelper.Clamp(pid.Signal(error, dt), -1, 1);

                yield return new CruiseTaskResult { Propulsion = (float)propulsion };
            }
            gridProps.Cruise = false;
            gridProps.CruiseSpeed = 0;
        }
    }
}