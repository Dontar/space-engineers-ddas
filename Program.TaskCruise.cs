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

        public bool Cruise { get; set; } = false;
        public float CruiseSpeed { get; set; } = 0;

        struct CruiseTaskResult
        {
            public float Propulsion;
        }
        IEnumerable<CruiseTaskResult> CruiseTask(float cruiseSpeed = -1, Func<bool> cruiseWhile = null)
        {
            if (Cruise) yield break;

            var gridProps = this.gridProps;
            var ini = Config;
            var pid = new PID(ini["PIDCruise"].ToString("0.5/0/0/0"));

            Cruise = true;
            CruiseSpeed = cruiseSpeed > -1 ? cruiseSpeed : (float)(gridProps.Speed * 3.6);
            cruiseWhile = cruiseWhile ?? (() => gridProps.UpDown == 0 && !gridProps.MainController.HandBrake);

            while (ini.Equals(Config) && cruiseWhile())
            {
                if (cruiseSpeed == -1)
                {
                    CruiseSpeed = MathHelper.Clamp(CruiseSpeed + (float)gridProps.ForwardBackward * -5f, 5, MyWheels.First().SpeedLimit);
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = gridProps.Speed * 3.6;
                var targetSpeed = CruiseSpeed;
                var error = targetSpeed - currentSpeedKmh;
                var propulsion = MathHelper.Clamp(pid.Signal(error, dt), -1, 1);

                yield return new CruiseTaskResult { Propulsion = (float)propulsion };
            }
            Cruise = false;
            CruiseSpeed = 0;
        }
    }
}