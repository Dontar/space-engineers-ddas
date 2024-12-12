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
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            gridProps = new GridProps(this);
            menuSystem = new MenuSystem(this);
            TaskManager.AddTask(Util.DisplayLogo("DDAS", Me.GetSurface(0)));
            TaskManager.AddTask(ScreensTask());
            TaskManager.AddTask(AutopilotTask());
            _AddWheelsTask = TaskManager.AddTask(AddWheelsTask());
            _SuspensionStrengthTask = TaskManager.AddTask(SuspensionStrengthTask());
            _SubSuspensionStrengthTask = TaskManager.AddTask(SubSuspensionStrengthTask());
            _PowerTask = TaskManager.AddTask(PowerTask());
            _StopLightsTask = TaskManager.AddTask(StopLightsTask());
            _FrictionTask = TaskManager.AddTask(FrictionTask());
            TaskManager.AddTask(MainTask());

            _AddWheelsTask.IsPaused = !Ini.Get("Options", "AddWheels").ToBoolean();
            _SuspensionStrengthTask.IsPaused = !Ini.Get("Options", "SuspensionStrength").ToBoolean();
            _SubSuspensionStrengthTask.IsPaused = !Ini.Get("Options", "SubWheelsStrength").ToBoolean();
            _PowerTask.IsPaused = !Ini.Get("Options", "Power").ToBoolean();
            _StopLightsTask.IsPaused = !Ini.Get("Options", "StopLights").ToBoolean();
            _FrictionTask.IsPaused = !Ini.Get("Options", "Friction").ToBoolean();
        }

        readonly TaskManager.Task _AddWheelsTask;
        readonly TaskManager.Task _SuspensionStrengthTask;
        readonly TaskManager.Task _SubSuspensionStrengthTask;
        readonly TaskManager.Task _PowerTask;
        readonly TaskManager.Task _StopLightsTask;
        readonly TaskManager.Task _FrictionTask;

        public void Main(string argument, UpdateType updateSource)
        {
            ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            try
            {
                gridProps.UpdateGridProps(Config, Controllers);

                if (gridProps.MainController == null)
                {
                    Echo("No controller found");
                    return;
                };
                TaskManager.RunTasks(Runtime.TimeSinceLastRun);

            }
            catch (Exception e)
            {
                Echo(e.ToString());
            }
        }

        private void ProcessCommands(string argument)
        {

            switch (argument.ToLower())
            {
                case "low":
                case "high":
                case "toggle_hight":
                    TaskManager.AddTaskOnce(ToggleHightModeTask());
                    break;
                case "flip":
                    TaskManager.AddTaskOnce(FlipGridTask(), 2f);
                    break;
                case "cruise":
                    TaskManager.AddTaskOnce(CruiseTask());
                    break;
                case "record":
                    if (!gridProps.Recording)
                        TaskManager.AddTaskOnce(RecordPathTask(), 1.7f);
                    else
                        gridProps.Recording = false;
                    break;
                case "import":
                    TaskManager.AddTaskOnce(ImportPathTask());
                    break;
                case "reverse":
                    TaskManager.AddTaskOnce(ReversePathTask());
                    break;
                case "export":
                    TaskManager.AddTaskOnce(ExportPathTask());
                    break;
                default:
                    if (argument.ToLower().StartsWith("toggle"))
                    {
                        var parts = argument.Split(' ');
                        switch (parts[1])
                        {
                            case "addwheels":
                                _AddWheelsTask.IsPaused = !_AddWheelsTask.IsPaused;
                                Ini.Set("Options", "AddWheels", (!_AddWheelsTask.IsPaused).ToString());
                                break;
                            case "suspensionstrength":
                                _SuspensionStrengthTask.IsPaused = !_SuspensionStrengthTask.IsPaused;
                                Ini.Set("Options", "SuspensionStrength", (!_SuspensionStrengthTask.IsPaused).ToString());
                                break;
                            case "subsuspensionstrength":
                                _SubSuspensionStrengthTask.IsPaused = !_SubSuspensionStrengthTask.IsPaused;
                                Ini.Set("Options", "SubWheelsStrength", (!_SubSuspensionStrengthTask.IsPaused).ToString());
                                break;
                            case "power":
                                _PowerTask.IsPaused = !_PowerTask.IsPaused;
                                Ini.Set("Options", "Power", (!_PowerTask.IsPaused).ToString());
                                break;
                            case "stoplights":
                                _StopLightsTask.IsPaused = !_StopLightsTask.IsPaused;
                                Ini.Set("Options", "StopLights", (!_StopLightsTask.IsPaused).ToString());
                                break;
                            case "friction":
                                _FrictionTask.IsPaused = !_FrictionTask.IsPaused;
                                Ini.Set("Options", "Friction", (!_FrictionTask.IsPaused).ToString());
                                break;
                        }
                        Me.CustomData = Ini.ToString();
                    }
                    menuSystem.ProcessMenuCommands(argument);
                    break;
            }
        }

        IEnumerable MainTask()
        {
            var ini = Config;
            var strength = ini.GetValueOrDefault("SuspensionStrength", "true").ToLower() == "true";
            var subWheels = ini.GetValueOrDefault("SubWheelsStrength", "true").ToLower() == "true";
            var powerFlag = ini.GetValueOrDefault("Power", "true").ToLower() == "true";
            var frictionFlag = ini.GetValueOrDefault("Friction", "true").ToLower() == "true";
            var suspensionHight = ini.GetValueOrDefault("SuspensionHight", "true").ToLower() == "true";
            var suspensionHightRoll = double.Parse(ini.GetValueOrDefault("SuspensionHightRoll", "45"));

            var high = Config.GetValueOrDefault("HighModeHight", "Max");
            var low = float.Parse(Config.GetValueOrDefault("LowModeHight", "0"));
            var calcHigh = high == "Max" ? MyWheels.FirstOrDefault().HeightOffsetMin : float.Parse(high);

            while (ini.Equals(Config))
            {
                var taskResults = TaskManager.TaskResults;
                var updateStrength = taskResults.OfType<StrengthTaskResult>().FirstOrDefault().action;
                var propulsion = taskResults.OfType<CruiseTaskResult>().FirstOrDefault().Propulsion;
                var power = taskResults.OfType<PowerTaskResult>().FirstOrDefault().Power;
                var friction = taskResults.OfType<FrictionTaskResult>().FirstOrDefault().action;
                var updateSubStrength = taskResults.OfType<SubStrengthTaskResult>().FirstOrDefault().action;
                var autopilot = taskResults.OfType<AutopilotTaskResult>().FirstOrDefault();

                foreach (var w in MyWheels)
                {
                    // update strength
                    if (strength)
                        updateStrength?.Invoke(w, GridUnsprungMass * gridProps.GravityMagnitude);
                    if (frictionFlag)
                        friction?.Invoke(w);
                    if (powerFlag)
                        w.Wheel.Power = power;

                    w.Wheel.PropulsionOverride = w.IsLeft ? propulsion : -propulsion;

                    // update height
                    if (suspensionHight && ((gridProps.Roll > suspensionHightRoll && w.IsLeft) || (gridProps.Roll < -suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, calcHigh, low);
                        w.Wheel.Height += (float)((value - w.Wheel.Height) * 0.5f);
                    }
                    else
                        w.Wheel.Height += (w.TargetHeight - w.Wheel.Height) * 0.3f;

                    // update steering
                    if (gridProps.LeftRight != 0)
                    {
                        w.Wheel.MaxSteerAngle = (float)(gridProps.LeftRight > 0 ? w.SteerAngleRight : w.SteerAngleLeft);
                    }

                    // half breaking
                    w.Wheel.Brake = true;
                    var halfBreaking = gridProps.UpDown > 0 && gridProps.ForwardBackward < 0;
                    if (halfBreaking && w.IsFront)
                    {
                        w.Wheel.Brake = false;
                        w.Wheel.Power = 0;
                    }

                    if (gridProps.Autopilot?.IsAutoPilotEnabled ?? false)
                    {
                        w.Wheel.MaxSteerAngle = (float)(autopilot.Steer > 0 ? w.SteerAngleRight : w.SteerAngleLeft);
                        w.Wheel.SteeringOverride = w.IsFrontFocal ? autopilot.Steer : -autopilot.Steer;
                    }
                    else
                        w.Wheel.SteeringOverride = 0;
                }
                foreach (var w in SubWheels)
                {
                    w.SpeedLimit = MyWheels.First().SpeedLimit;

                    if (subWheels)
                        updateSubStrength?.Invoke(w, GridUnsprungMass * gridProps.GravityMagnitude);
                    if (frictionFlag)
                        friction?.Invoke(w);
                    if (powerFlag)
                        w.Wheel.Power = power;

                    if (suspensionHight && ((gridProps.Roll > 5 && w.IsLeft) || (gridProps.Roll < -5 && !w.IsLeft)))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, calcHigh, low);
                        w.Wheel.Height += (float)((value - w.Wheel.Height) * 0.5f);
                    }
                    else
                        w.Wheel.Height += (w.TargetHeight - w.Wheel.Height) * 0.3f;

                    if (gridProps.ForwardBackward != 0 && !(gridProps.UpDown > 0))
                    {
                        var p = gridProps.Cruise ? propulsion : (gridProps.ForwardBackward < 0 ? 1 : -1);
                        w.Wheel.PropulsionOverride = w.IsLeft ? p : -p;
                    }
                    else
                        w.Wheel.PropulsionOverride = 0;
                }
                yield return null;
            }
        }

        IEnumerable AddWheelsTask()
        {
            var allWheels = AllWheels;
            while (allWheels.Equals(AllWheels))
            {
                allWheels.Where(w => !w.IsAttached).ToList().ForEach(w => w.ApplyAction("Add Top Part"));
                yield return null;
            }
        }

        IEnumerable StopLightsTask()
        {
            var ini = Config;
            var gridMass = gridProps.Mass.BaseMass;
            var orientation = gridProps.MainController.Orientation;
            var tag = ini.GetValueOrDefault("Tag", "{DDAS}");
            var ignoreTag = ini.GetValueOrDefault("IgnoreTag", "{Ignore}");
            var lights = Util.GetBlocks<IMyLightingBlock>(b => Util.IsTagged(b, tag) || (Util.IsNotIgnored(b, ignoreTag) && orientation.TransformDirectionInverse(b.Orientation.Forward) == Base6Directions.Direction.Backward));

            while (gridMass.Equals(gridProps.Mass.BaseMass) && ini.Equals(Config))
            {
                lights.ForEach(l =>
                {
                    l.Radius = 1f;
                    l.Intensity = 1f;
                    l.Falloff = 0;
                    l.Color = Color.DarkRed;

                    if (gridProps.UpDown > 0)
                    {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.Red;
                    }
                    if (gridProps.ForwardBackward > 0)
                    {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.White;
                    }

                });
                yield return null;
            }
        }

        struct FrictionTaskResult
        {
            public Action<WheelWrapper> action;
        }
        IEnumerable<FrictionTaskResult> FrictionTask()
        {
            var ini = Config;
            var FrictionInner = float.Parse(ini.GetValueOrDefault("FrictionInner", "80"));
            var FrictionOuter = float.Parse(ini.GetValueOrDefault("FrictionOuter", "60"));
            var FrictionMinSpeed = float.Parse(ini.GetValueOrDefault("FrictionMinSpeed", "5"));
            while (ini.Equals(Config))
            {
                yield return new FrictionTaskResult
                {
                    action = w =>
                    {
                        if (gridProps.Speed > FrictionMinSpeed && gridProps.LeftRight != 0)
                        {
                            w.Friction = gridProps.LeftRight < 0 ? (w.IsLeft ? FrictionInner : FrictionOuter) : (w.IsLeft ? FrictionOuter : FrictionInner);
                        }
                        else w.Friction = 100;
                    }
                };
            }
        }

        IEnumerable ToggleHightModeTask()
        {
            var targetHigh = Config.GetValueOrDefault("HighModeHight", "Max");
            var targetLow = float.Parse(Config.GetValueOrDefault("LowModeHight", "0"));
            var controlWheel = MyWheels.FirstOrDefault();
            var targetHeight = controlWheel.TargetHeight;
            var calcHigh = targetHigh == "Max" ? controlWheel.HeightOffsetMin : float.Parse(targetHigh);
            Action<WheelWrapper> handler = (WheelWrapper w) =>
            {
                w.TargetHeight = targetHeight == calcHigh ? targetLow : calcHigh;
            };
            MyWheels.ForEach(handler);
            SubWheels.ForEach(handler);

            yield return null;
        }
    }
}
