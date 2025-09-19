using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;

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
            CurrentScreenType = (CurrentScreenType + 1) % 5;
        }

        void DisplayStatus(InfoDisplay s)
        {
            var propulsionSystemStatus = CruiseResult;
            var power = PowerResult;
            var orientation = OrientationResult;
            var propulsion = propulsionSystemStatus.Propulsion * 100;

            s.Sb.Clear();
            s.Label("Status");
            s.Row("CruiseSpeed", CruiseSpeed, "N0", " km/h");
            s.Row("Speed", Speed * 3.6, "N0", " km/h");
            s.Row("A Speed", Velocities.AngularVelocity.Length(), "N1", " m/s");
            s.Sep();
            s.Row("Roll", orientation.Roll, "N1", " °");
            s.Row("Pitch", orientation.Pitch, "N1", " °");
            s.Sep();
            s.Row("Power", power.Power, "N1", " %");
            s.Row("Propulsion", propulsion, "N1", " %");
            s.Sep();
            s.Row(F("Cruise", Cruise), F("Rec", Recording));
            s.Row(F("Flip", Flipping), F("Level", _autoLevel));
        }

        void DisplayRollPitchStatus(InfoDisplay s)
        {
            var orientation = OrientationResult;
            var gyro = Gyros.FirstOrDefault();

            s.Sb.Clear();
            s.Label("Orientation");
            s.Row("Roll", orientation.Roll, "N1", " °");
            s.Row("Pitch", orientation.Pitch, "N1", " °");
            s.Label("Gyros");
            if (gyro != null)
            {
                s.Row("Yaw", gyro.Yaw * MathHelper.RadiansPerSecondToRPM, "N1", " RPM");
                s.Row("Pitch", gyro.Pitch * MathHelper.RadiansPerSecondToRPM, "N1", " RPM");
                s.Row("Roll", gyro.Roll * MathHelper.RadiansPerSecondToRPM, "N1", " RPM");
                s.Row("Power", gyro.GyroPower * 100, "N1", " %");
                s.Row("Override", gyro.GyroOverride);
            }
            else
            {
                s.Row("No Gyros Found", "");
            }
        }

        void DisplayAutopilot(InfoDisplay s)
        {
            var autopilot = AutopilotResult;
            s.Sb.Clear();
            s.Label("Autopilot");
            s.Row("Waypoint", autopilot.Waypoint);
            s.Row("Distance", autopilot.Distance, "N1", " m");
            s.Sep();
            s.Row("Mode", autopilot.Mode);
            s.Row("Waypoint #", autopilot.WaypointCount);
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

        void DisplayWheelStrength(InfoDisplay s)
        {
            var frontWheels = MyWheels.Where(w => w.IsFront).OrderBy(w => w.ToCoM.Z);
            var backWheels = MyWheels.Where(w => !w.IsFront).OrderBy(w => w.ToCoM.Z);
            var subWheels = SubWheels.OrderBy(w => w.ToCoM.Z);

            Action<IEnumerable<IEnumerable<WheelWrapper>>> PrintAxel = wheels =>
            {
                foreach (var g in wheels)
                {
                    var axel = g.OrderBy(w => !w.IsLeft).ToArray();
                    s.Row($"|{axel[0].Wheel.Strength,3:N0}%|", $"|{axel[1].Wheel.Strength,3:N0}%|", "");
                    s.Label("", '-');
                }
            };

            s.Sb.Clear();
            s.Label("Suspension Strength");
            s.Label("Front", '-');
            PrintAxel(ZipPairs(frontWheels));
            s.Label("Rear", '-');
            PrintAxel(ZipPairs(backWheels));
            if (subWheels.Count() < 1) return;
            s.Label("Trailer", '-');
            PrintAxel(ZipPairs(subWheels));
        }

        void DisplayWheelHeight(InfoDisplay s)
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
                    s.Row($"|{right,4:N1}cm|", $"|{left,4:N1}cm|");
                    s.Label("", '-');
                }
            };

            s.Sb.Clear();
            s.Label("Suspension Height");
            s.Label("Front", '-');
            PrintAxel(ZipPairs(frontWheels));
            s.Label("Rear", '-');
            PrintAxel(ZipPairs(backWheels));
            if (subWheels.Count() < 1) return;
            s.Label("Trailer", '-');
            PrintAxel(ZipPairs(subWheels));
        }

        IEnumerable ScreensTask()
        {
            var screenText = new InfoDisplay(new StringBuilder(), 22);

            while (true)
            {
                switch (CurrentScreenType)
                {
                    case 4:
                        DisplayRollPitchStatus(screenText);
                        break;
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
                    s.WriteText(screenText.Sb);
                }
                yield return null;
            }
        }
    }
}
