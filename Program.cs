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
            TaskManager.AddTask(AutoLevelTask());
            _AddWheelsTask = TaskManager.AddTask(AddWheelsTask());
            _SuspensionStrengthTask = TaskManager.AddTask(SuspensionStrengthTask());
            _SubSuspensionStrengthTask = TaskManager.AddTask(SubSuspensionStrengthTask());
            _PowerTask = TaskManager.AddTask(PowerTask());
            _StopLightsTask = TaskManager.AddTask(StopLightsTask());
            _FrictionTask = TaskManager.AddTask(FrictionTask());
            TaskManager.AddTask(MainTask());

            _AddWheelsTask.IsPaused = !Config["AddWheels"].ToBoolean(true);
            _SuspensionStrengthTask.IsPaused = !Config["SuspensionStrength"].ToBoolean(true);
            _SubSuspensionStrengthTask.IsPaused = !Config["SubWheelsStrength"].ToBoolean(true);
            _PowerTask.IsPaused = !Config["Power"].ToBoolean(true);
            _StopLightsTask.IsPaused = !Config["StopLights"].ToBoolean(true);
            _FrictionTask.IsPaused = !Config["Friction"].ToBoolean(true);
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
                case "info":
                    Echo(Runtime.UpdateFrequency.ToString());
                    break;
                case "low":
                case "high":
                case "toggle_hight":
                    TaskManager.AddTaskOnce(ToggleHightModeTask());
                    break;
                case "flip":
                    gridProps.AutoLevel = false;
                    TaskManager.AddTaskOnce(FlipGridTask(), 2f);
                    break;
                case "cruise":
                    TaskManager.AddTaskOnce(CruiseTask());
                    break;
                case "level":
                    gridProps.AutoLevel = !gridProps.AutoLevel;
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
                    menuSystem.ProcessMenuCommands(argument);
                    break;
            }
        }


        IEnumerable MainTask()
        {
            var ini = Config;
            var strength = ini["SuspensionStrength"].ToBoolean(true);
            var subWheels = ini["SubWheelsStrength"].ToBoolean(true);
            var powerFlag = ini["Power"].ToBoolean(true);
            var frictionFlag = ini["Friction"].ToBoolean(true);
            var suspensionHight = ini["SuspensionHight"].ToBoolean(true);
            var suspensionHightRoll = ini["SuspensionHightRoll"].ToDouble(30);

            var high = ini["HighModeHight"].ToDouble(MyWheels.FirstOrDefault().HeightOffsetMin);
            var low = ini["LowModeHight"].ToDouble();

            while (ini.Equals(Config))
            {
                var taskResults = TaskManager.TaskResults;
                var updateStrength = taskResults.OfType<StrengthTaskResult>().FirstOrDefault().action;
                var propulsion = taskResults.OfType<CruiseTaskResult>().FirstOrDefault().Propulsion;
                var power = taskResults.OfType<PowerTaskResult>().FirstOrDefault().Power;
                var friction = taskResults.OfType<FrictionTaskResult>().FirstOrDefault().action;
                var updateSubStrength = taskResults.OfType<SubStrengthTaskResult>().FirstOrDefault().action;
                var autopilot = taskResults.OfType<AutopilotTaskResult>().FirstOrDefault();

                var roll = gridProps.Roll + (gridProps.RollCompensating ? (gridProps.Roll > 0 ? 6 : -6) : 0);

                if (gridProps.SubController != null)
                {
                    gridProps.SubController.HandBrake = gridProps.Controller.HandBrake;
                }

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
                    if (suspensionHight && ((roll > suspensionHightRoll && w.IsLeft) || (roll < -suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, high, low);
                        w.Wheel.Height += (float)((value - w.Wheel.Height) * 0.5f);
                        gridProps.RollCompensating = true;
                    }
                    else
                    {
                        w.Wheel.Height += (w.TargetHeight - w.Wheel.Height) * 0.3f;
                        gridProps.RollCompensating = false;
                    }

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
                if (SubWheels.Count() > 0)
                {
                    high = ini["HighModeHight"].ToDouble(SubWheels.FirstOrDefault().HeightOffsetMin);
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

                    if (suspensionHight && ((roll > suspensionHightRoll && w.IsLeft) || (roll < -suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, high, low);
                        w.Wheel.Height += (float)((value - w.Wheel.Height) * 0.5f);
                    }
                    else
                        w.Wheel.Height += (w.TargetHeight - w.Wheel.Height) * 0.3f;

                    w.Wheel.PropulsionOverride = 0;
                    if (gridProps.Cruise)
                    {
                        w.Wheel.PropulsionOverride = w.IsLeft ? propulsion : -propulsion;
                    }
                    else if (gridProps.ForwardBackward != 0 && !(gridProps.UpDown > 0))
                    {
                        var p = gridProps.ForwardBackward < 0 ? 1 : -1;
                        w.Wheel.PropulsionOverride = w.IsLeft ? p : -p;
                    }
                }
                yield return null;
            }
        }

        IEnumerable AddWheelsTask()
        {
            var allWheels = AllWheels;
            while (allWheels.Equals(AllWheels))
            {
                foreach (var w in allWheels.Where(w => !w.IsAttached))
                {
                    w.ApplyAction("Add Top Part");
                }
                yield return null;
            }
        }

        IEnumerable StopLightsTask()
        {
            var ini = Config;
            var gridMass = gridProps.Mass.BaseMass;
            var orientation = gridProps.MainController.Orientation;
            var tag = ini["Tag"].ToString("{DDAS}");
            var ignoreTag = ini["IgnoreTag"].ToString("{Ignore}");
            var lights = Util.GetBlocks<IMyLightingBlock>(b =>
                b.IsSameConstructAs(Me) && (
                    Util.IsTagged(b, tag) || (
                        Util.IsNotIgnored(b, ignoreTag) &&
                        orientation.TransformDirectionInverse(b.Orientation.Forward) == Base6Directions.Direction.Backward
                    )
                )
            );

            while (gridMass.Equals(gridProps.Mass.BaseMass) && ini.Equals(Config))
            {
                foreach (var l in lights)
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

                };
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
            var FrictionInner = ini["FrictionInner"].ToSingle(80);
            var FrictionOuter = ini["FrictionOuter"].ToSingle(60);
            var FrictionMinSpeed = ini["FrictionMinSpeed"].ToSingle(5);
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
            var controlWheel = MyWheels.FirstOrDefault();
            var targetHeight = controlWheel.TargetHeight;// -31.9

            var high = Config["HighModeHight"].ToSingle(controlWheel.HeightOffsetMin);//Max
            var low = Config["LowModeHight"].ToSingle();//0


            var closeHigh = high - targetHeight;// -32 -31.9 = -0.1
            var closeLow = targetHeight - low;// -31.9 - 0 = -31.9
            foreach (var w in MyWheels)
            {
                w.TargetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;
            }

            if (SubWheels.Count() == 0) yield break;
            var controlSubWheel = SubWheels.FirstOrDefault();
            targetHeight = controlSubWheel.TargetHeight;

            high = Config["HighModeHight"].ToSingle(controlSubWheel.HeightOffsetMin);//Max
            closeHigh = high - targetHeight;
            closeLow = targetHeight - low;
            foreach (var w in SubWheels)
            {
                w.TargetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;
            }

            yield return null;
        }
    }
}
