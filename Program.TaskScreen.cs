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
        List<IMyTerminalBlock> Screens => Memo.Of(() =>
        {
            var tag = Config.GetValueOrDefault("Tag", "{DDAS}");
            var screensBlocks = Util.GetBlocks<IMyTerminalBlock>(b => Util.IsTagged(b, tag) && Util.HasScreens(b));
            return screensBlocks;
        }, "screens", 100);
        IEnumerable ScreensTask()
        {
            var screenText = new StringBuilder();
            var screens = Screens;
            var statusScreens = screens.Where(s => s.CustomData.Contains("ddas-status")).Select(s =>
            {
                var start = s.CustomData.IndexOf("ddas-status", StringComparison.OrdinalIgnoreCase);
                var idx = s.CustomData.Substring(start, 13).Split('=').Last();
                return (s as IMyTextSurfaceProvider).GetSurface(int.Parse(idx) - 1);
            }).ToList();
            var menuScreens = screens.Where(s => s.CustomData.Contains("ddas-menu")).Select(s =>
            {
                var start = s.CustomData.IndexOf("ddas-menu", StringComparison.OrdinalIgnoreCase);
                var idx = s.CustomData.Substring(start, 11).Split('=').Last();
                return (s as IMyTextSurfaceProvider).GetSurface(int.Parse(idx) - 1);
            }).ToList();

            while (screens.Equals(Screens))
            {
                var propulsion = TaskManager.TaskResults.OfType<CruiseTaskResult>().FirstOrDefault().Propulsion;
                var power = TaskManager.TaskResults.OfType<PowerTaskResult>().FirstOrDefault();
                var autopilot = TaskManager.TaskResults.OfType<AutopilotTaskResult>().FirstOrDefault();
                var waypoint = gridProps.Autopilot.CurrentWaypoint;

                screenText.Clear();
                screenText.AppendLine($"Speed:       {gridProps.Speed * 3.6:N2} km/h");
                screenText.AppendLine($"Roll:        {gridProps.Roll:N2} Degrees");
                screenText.AppendLine($"Pitch:       {gridProps.Pitch:N2} Degrees");
                screenText.AppendLine($"CruiseSpeed: {gridProps.CruiseSpeed:N2} km/h");
                screenText.AppendLine($"Cruise:      {gridProps.Cruise}");
                screenText.AppendLine($"Recording:   {gridProps.Recording}");
                screenText.AppendLine($"Flipping:    {gridProps.Flipping}");
                screenText.AppendLine($"AutoLevel:   {gridProps.AutoLevel}");
                screenText.AppendLine($"Power:       {power.Power:N2}");
                screenText.AppendLine($"Propulsion:  {propulsion:N2}");
                screenText.AppendLine($"Waypoint:    {waypoint.Name ?? "None"}");

                statusScreens.ForEach(s =>
                {
                    s.ContentType = ContentType.TEXT_AND_IMAGE;
                    s.Font = "Monospace";
                    s.WriteText(screenText);
                });
                menuScreens.ForEach(s =>
                {
                    s.ContentType = ContentType.TEXT_AND_IMAGE;
                    menuSystem.Render(s);
                });
                yield return null;
            }
        }
    }
}