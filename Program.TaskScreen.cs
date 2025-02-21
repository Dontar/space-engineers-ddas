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
        IEnumerable<IMyTextSurface> ScreensStat => Memo.Of(() => Util.GetScreens("ddas-status").ToArray(), "screensStatus", 100);
        IEnumerable ScreensTask()
        {
            var screenText = new StringBuilder();
            // GridProps gridProps = this.gridProps;

            while (true)
            {
                var propulsion = TaskManager.GetTaskResult<CruiseTaskResult>();
                var power = TaskManager.GetTaskResult<PowerTaskResult>();
                var autopilot = TaskManager.GetTaskResult<AutopilotTaskResult>();
                var orientation = TaskManager.GetTaskResult<GridOrientation>();

                screenText.Clear();
                screenText.AppendLine($"Speed:       {Speed * 3.6:N2} km/h");
                screenText.AppendLine($"Roll:        {orientation.Roll:N2} Degrees");
                screenText.AppendLine($"Pitch:       {orientation.Pitch:N2} Degrees");
                screenText.AppendLine($"CruiseSpeed: {CruiseSpeed:N2} km/h");
                screenText.AppendLine($"Cruise:      {Cruise}");
                screenText.AppendLine($"Recording:   {Recording}");
                screenText.AppendLine($"Flipping:    {Flipping}");
                screenText.AppendLine($"AutoLevel:   {AutoLevel}");
                screenText.AppendLine($"Power:       {power.Power:N2}");
                screenText.AppendLine($"Propulsion:  {propulsion.Propulsion:N2}");
                screenText.AppendLine($"Steer:       {autopilot.Steer}");
                screenText.AppendLine($"Waypoint:    {autopilot.Waypoint}");
                screenText.AppendLine($"Waypoint #:  {autopilot.WaypointCount}");
                screenText.AppendLine($"Mode:        {autopilot.Mode}");

                foreach (var s in ScreensStat)
                {
                    s.ContentType = ContentType.TEXT_AND_IMAGE;
                    s.Font = "Monospace";
                    s.WriteText(screenText);
                }
                yield return null;
            }
        }
    }
}