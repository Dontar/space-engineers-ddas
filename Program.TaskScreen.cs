using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        IEnumerable<IMyTextSurface> ScreensStat;
        int CurrentScreenType = 0;
        void InitScreens()
        {
            ScreensStat = Util.GetScreens("ddas-status").ToArray();
        }

        string F(string flag, bool state)
        {
            return state ? $"[{flag}]" : flag;
        }

        void ChangeScreenType()
        {
            CurrentScreenType = (CurrentScreenType + 1) % 4;
        }

        void DisplayStatus(StringBuilder s)
        {
            var propulsionSystemStatus = CruiseResult;
            var power = PowerResult;
            var orientation = OrientationResult;
            var propulsion = propulsionSystemStatus.Propulsion * 100;

            s.Clear();
            s.AppendLine("==Status==============");
            s.AppendLine($" CruiseSpeed:{CruiseSpeed,3:N0} km/h");
            s.AppendLine($" Speed:      {Speed * 3.6,3:N0} km/h");
            s.AppendLine($" A Speed:    {Velocities.AngularVelocity.Length(),3:N1} m/s");
            s.AppendLine("======================");
            s.AppendLine($" Roll:       {orientation.Roll,6:N1} °");
            s.AppendLine($" Pitch:      {orientation.Pitch,6:N1} °");
            // s.AppendLine($" Yaw:        {orientation.Yaw,6:N1} °");
            s.AppendLine("======================");
            s.AppendLine($" Power:      {power.Power,6:N1} %");
            s.AppendLine($" Propulsion: {propulsion,6:N1} %");
            s.AppendLine("======================");
            s.AppendLine($" {F("Cruise", Cruise),-10}{F("Rec", Recording),11}");
            s.AppendLine($" {F("Flip", Flipping),-10}{F("Level", _autoLevel),11}");
        }

        void DisplayAutopilot(StringBuilder s)
        {
            var autopilot = AutopilotResult;
            s.Clear();
            s.AppendLine("==Autopilot===========\n");
            s.AppendLine($" Waypoint:   {autopilot.Waypoint}");
            s.AppendLine($" Distance:   {autopilot.Distance,6:N1} m");
            s.AppendLine("======================\n");
            s.AppendLine($" Mode:       {autopilot.Mode}");
            s.AppendLine($" Waypoint #: {autopilot.WaypointCount}");
        }

        IEnumerable<IEnumerable<T>> ZipPairs<T>(IEnumerable<T> list)
        {
            var length = list.Count();
            for (int i = 0; i < length / 2; i++)
            {
                yield return list.Take(2);
                list = list.Skip(2);
            }
        }

        void DisplayWheelStrength(StringBuilder s)
        {
            var frontWheels = MyWheels.Where(w => w.IsFront).OrderBy(w => w.ToCoM.Z);
            var backWheels = MyWheels.Where(w => !w.IsFront).OrderBy(w => w.ToCoM.Z);
            var subWheels = SubWheels.OrderBy(w => w.ToCoM.Z);

            Action<IEnumerable<IEnumerable<WheelWrapper>>> PrintAxel = wheels =>
            {
                foreach (var g in wheels)
                {
                    var axel = g.OrderBy(w => !w.IsLeft).ToArray();
                    s.AppendLine($"|{axel[0].Wheel.Strength,3:N0}%|          |{axel[1].Wheel.Strength,3:N0}%|");
                    s.AppendLine("----------------------");
                }
            };

            s.Clear();
            s.AppendLine("=Suspension Strength==");
            s.AppendLine("--Front---------------");
            PrintAxel(ZipPairs(frontWheels));
            s.AppendLine("--Rear----------------");
            PrintAxel(ZipPairs(backWheels));
            if (subWheels.Count() < 1) return;
            s.AppendLine("--Trailer-------------");
            PrintAxel(ZipPairs(subWheels));
        }

        void DisplayWheelHeight(StringBuilder s)
        {
            var frontWheels = MyWheels.Where(w => w.IsFront).OrderBy(w => w.ToCoM.Z);
            var backWheels = MyWheels.Where(w => !w.IsFront).OrderBy(w => w.ToCoM.Z);
            var subWheels = SubWheels.OrderBy(w => w.ToCoM.Z);

            Action<IEnumerable<IEnumerable<WheelWrapper>>> PrintAxel = wheels =>
            {
                foreach (var g in wheels)
                {
                    var axel = g.OrderBy(w => !w.IsLeft).ToArray();
                    var right = -axel[0].Wheel.Height * 100;
                    var left = -axel[1].Wheel.Height * 100;
                    s.AppendLine($"|{right,4:N1}cm|      |{left,4:N1}cm|");
                    s.AppendLine("----------------------");
                }
            };

            s.Clear();
            s.AppendLine("==Suspension Height===");
            s.AppendLine("--Front---------------");
            PrintAxel(ZipPairs(frontWheels));
            s.AppendLine("--Rear----------------");
            PrintAxel(ZipPairs(backWheels));
            if (subWheels.Count() < 1) return;
            s.AppendLine("--Trailer-------------");
            PrintAxel(ZipPairs(subWheels));
        }

        IEnumerable ScreensTask()
        {
            var screenText = new StringBuilder();

            while (true)
            {
                switch (CurrentScreenType)
                {
                    case 3:
                        DisplayAutopilot(screenText);
                        break;
                    case 2:
                        DisplayWheelHeight(screenText);
                        break;
                    case 1:
                        DisplayWheelStrength(screenText);
                        break;
                    default:
                        DisplayStatus(screenText);
                        break;
                }
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
