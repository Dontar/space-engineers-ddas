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
            TaskManager.AddTask(Util.DisplayLogo("DDAS", Me.GetSurface(0)), 1.5f);
            TaskManager.AddTask(ScreensTask());
            TaskManager.AddTask(AutopilotTask());
            TaskManager.AddTask(AutopilotAITask());
            TaskManager.AddTask(AutoLevelTask());
            TaskManager.AddTask(SuspensionStrengthTask(), 3f);
            _PowerTask = TaskManager.AddTask(PowerTask());
            _StopLightsTask = TaskManager.AddTask(StopLightsTask());
            TaskManager.AddTask(MainTask());
            TaskManager.AddTask(Util.StatusMonitor(this));

            _PowerTask.IsPaused = !Config["Power"].ToBoolean(true);
            _StopLightsTask.IsPaused = !Config["StopLights"].ToBoolean(true);
        }

        readonly TaskManager.Task _PowerTask;
        readonly TaskManager.Task _StopLightsTask;

        public void Main(string argument, UpdateType updateSource)
        {
            ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            try
            {
                gridProps.UpdateGridProps(Config, Controllers);
                if (gridProps.MainController == null)
                {
                    Util.Echo("No controller found");
                    return;
                };

                TaskManager.RunTasks(Runtime.TimeSinceLastRun);

            }
            catch (Exception e)
            {
                Util.Echo(e.ToString());
            }
        }

        private void ProcessCommands(string argument)
        {

            switch (argument.ToLower())
            {
                // case "info":
                //     Echo($"{Runtime.UpdateFrequency}\n{updateSource}");
                //     break;
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
                    // menuSystem.ProcessMenuCommands(argument);
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
            var addWheels = ini["AddWheels"].ToBoolean(true);
            var FrictionInner = ini["FrictionInner"].ToSingle(80);
            var FrictionOuter = ini["FrictionOuter"].ToSingle(60);
            var FrictionMinSpeed = ini["FrictionMinSpeed"].ToSingle(5);

            var high = ini["HighModeHight"].ToDouble(MyWheels.FirstOrDefault().HeightOffsetMin);
            var low = ini["LowModeHight"].ToDouble();

            while (ini.Equals(Config))
            {
                var taskResults = TaskManager.TaskResults;
                var updateStrength = taskResults.OfType<StrengthTaskResult>().FirstOrDefault();
                var propulsion = taskResults.OfType<CruiseTaskResult>().FirstOrDefault().Propulsion;
                var power = taskResults.OfType<PowerTaskResult>().FirstOrDefault().Power;
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
                        updateStrength.Action?.Invoke(w, GridUnsprungMass * gridProps.GravityMagnitude);
                    if (frictionFlag)
                        if (gridProps.Speed > FrictionMinSpeed && gridProps.LeftRight != 0)
                        {
                            w.Friction = gridProps.LeftRight < 0 ? (w.IsLeft ? FrictionInner : FrictionOuter) : (w.IsLeft ? FrictionOuter : FrictionInner);
                        }
                        else w.Friction = 100;

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

                    if (!autopilot.Equals(default(AutopilotTaskResult)))
                    {
                        w.Wheel.MaxSteerAngle = (float)(autopilot.Steer > 0 ? w.SteerAngleRight : w.SteerAngleLeft);
                        w.Wheel.SteeringOverride = w.IsFrontFocal ? autopilot.Steer : -autopilot.Steer;
                    }
                    else
                        w.Wheel.SteeringOverride = 0;

                    if (addWheels && !w.Wheel.IsAttached)
                    {
                        w.Wheel.ApplyAction("Add Top Part");
                    }

                }
                if (SubWheels.Count() > 0)
                {
                    high = ini["HighModeHight"].ToDouble(SubWheels.FirstOrDefault().HeightOffsetMin);
                }
                foreach (var w in SubWheels)
                {
                    w.SpeedLimit = MyWheels.First().SpeedLimit;

                    if (subWheels)
                        updateStrength.SubAction?.Invoke(w, GridUnsprungMass * gridProps.GravityMagnitude);
                    if (frictionFlag)
                        if (gridProps.Speed > FrictionMinSpeed && gridProps.LeftRight != 0)
                        {
                            w.Friction = gridProps.LeftRight < 0 ? (w.IsLeft ? FrictionInner : FrictionOuter) : (w.IsLeft ? FrictionOuter : FrictionInner);
                        }
                        else w.Friction = 100;

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

                    if (addWheels && !w.Wheel.IsAttached)
                    {
                        w.Wheel.ApplyAction("Add Top Part");
                    }

                }
                yield return null;
            }
        }

        IEnumerable<IMyLightingBlock> Lights => Memo.Of(() =>
        {
            var gridMass = gridProps.Mass.BaseMass;
            var orientation = gridProps.MainController.Orientation;
            var tag = Config["Tag"].ToString("{DDAS}");
            var ignoreTag = Config["IgnoreTag"].ToString("{Ignore}");

            return Util.GetBlocks<IMyLightingBlock>(b =>
                b.IsSameConstructAs(Me) && (
                    Util.IsTagged(b, tag) || (
                        Util.IsNotIgnored(b, ignoreTag) &&
                        orientation.TransformDirectionInverse(b.Orientation.Forward) == Base6Directions.Direction.Backward
                    )
                )
            );
        }, "stopLights", Memo.Refs(gridProps.Mass.BaseMass, Config));

        IEnumerable StopLightsTask()
        {
            var ini = Config;
            var lights = Lights;

            while (lights.Equals(Lights) && ini.Equals(Config))
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

        IEnumerable ToggleHightModeTask()
        {
            var controlWheel = MyWheels.FirstOrDefault();
            var currentHeight = controlWheel.TargetHeight;// -31.9

            var high = Config["HighModeHight"].ToSingle(controlWheel.HeightOffsetMin);//Max
            var low = Config["LowModeHight"].ToSingle();//0

            var closeHigh = high - currentHeight;// -32 -31.9 = -0.1
            var closeLow = currentHeight - low;// -31.9 - 0 = -31.9

            var targetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;

            foreach (var w in MyWheels) w.TargetHeight = targetHeight;

            if (SubWheels.Count() == 0) yield break;
            var controlSubWheel = SubWheels.FirstOrDefault();
            currentHeight = controlSubWheel.TargetHeight;

            high = Config["HighModeHight"].ToSingle(controlSubWheel.HeightOffsetMin);//Max
            closeHigh = high - currentHeight;
            closeLow = currentHeight - low;
            targetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;
            foreach (var w in SubWheels) w.TargetHeight = targetHeight;

            yield return null;
        }
    }
}
