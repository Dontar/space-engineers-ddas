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
            TaskManager.AddTask(Util.StatusMonitor(this));
            TaskManager.AddTask(MainTask());
            TaskManager.AddTask(AutopilotTask());
            _StopLightsTask = TaskManager.AddTask(StopLightsTask());
            _PowerTask = TaskManager.AddTask(PowerTask());
            TaskManager.AddTask(SuspensionStrengthTask(), 5f);
            TaskManager.AddTask(AutoLevelTask());
            TaskManager.AddTask(ScreensTask(), 0.5f);
            TaskManager.AddTask(GridOrientationsTask());
            TaskManager.AddTask(Util.DisplayLogo("DDAS", Me.GetSurface(0)), 1.5f);

            TaskManager.PauseTask(_PowerTask, !Config["Power"].ToBoolean(true));
            TaskManager.PauseTask(_StopLightsTask, !Config["StopLights"].ToBoolean(true));
        }

        readonly int _PowerTask;
        readonly int _StopLightsTask;

        public void Main(string argument, UpdateType updateSource)
        {
            if (Controllers.MainController == null)
            {
                Util.Echo("No controller found");
                return;
            }

            if (!string.IsNullOrEmpty(argument))
                ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            TaskManager.RunTasks(Runtime.TimeSinceLastRun);
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
                    AutoLevel = false;
                    TaskManager.AddTaskOnce(FlipGridTask(), 2f);
                    break;
                case "cruise":
                    TaskManager.AddTaskOnce(CruiseTask());
                    break;
                case "level":
                    AutoLevel = !AutoLevel;
                    break;
                case "record":
                    if (!Recording)
                        TaskManager.AddTaskOnce(RecordPathTask(), 1.7f);
                    else
                        Recording = false;
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
            var strengthFlag = ini["SuspensionStrength"].ToBoolean(true);
            var subWheelsStrengthFlag = ini["SubWheelsStrength"].ToBoolean(true);
            var powerFlag = ini["Power"].ToBoolean(true);
            var frictionFlag = ini["Friction"].ToBoolean(true);
            var suspensionHightFlag = ini["SuspensionHight"].ToBoolean(true);
            var suspensionHightRoll = ini["SuspensionHightRoll"].ToDouble(30);
            var addWheelsFlag = ini["AddWheels"].ToBoolean(true);
            var FrictionInner = ini["FrictionInner"].ToSingle(80);
            var FrictionOuter = ini["FrictionOuter"].ToSingle(60);
            var FrictionMinSpeed = ini["FrictionMinSpeed"].ToSingle(5);

            var high = ini["HighModeHight"].ToDouble(MyWheels.FirstOrDefault().HeightOffsetMin);
            var low = ini["LowModeHight"].ToDouble();

            var rollCompensating = false;

            while (ini.Equals(Config))
            {
                var gravityMagnitude = GravityMagnitude;
                var speed = Speed;
                var leftRight = LeftRight;
                var upDown = UpDown;
                var forwardBackward = ForwardBackward;

                var updateStrength = TaskManager.GetTaskResult<StrengthTaskResult>();
                var propulsion = TaskManager.GetTaskResult<CruiseTaskResult>().Propulsion;
                var power = TaskManager.GetTaskResult<PowerTaskResult>().Power;
                var autopilot = TaskManager.GetTaskResult<AutopilotTaskResult>();
                var orientation = TaskManager.GetTaskResult<GridOrientation>();

                // var gridRoll = orientation.Roll;
                var roll = orientation.Roll + (rollCompensating ? (orientation.Roll > 0 ? 6 : -6) : 0);

                bool isTurning = leftRight != 0 || !Util.IsBetween(autopilot.Steer, -0.4, 0.4);
                bool isTurningLeft = leftRight < 0 || autopilot.Steer > 0;
                var isHalfBreaking = upDown > 0 && forwardBackward < 0;

                if (Controllers.SubController != null)
                    Controllers.SubController.HandBrake = Controller.HandBrake;

                foreach (var w in MyWheels)
                {
                    IMyMotorSuspension wheel = w.Wheel;
                    wheel.SteeringOverride = w.IsFrontFocal ? -autopilot.Steer : autopilot.Steer;
                    wheel.PropulsionOverride = w.IsLeft ? propulsion : -propulsion;

                    // update strength
                    if (strengthFlag)
                    {
                        updateStrength.Action?.Invoke(w, GridUnsprungMass * gravityMagnitude);
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (frictionFlag)
                        if (speed > FrictionMinSpeed && isTurning)
                        {
                            w.Friction = isTurningLeft ? (w.IsLeft ? FrictionInner : FrictionOuter) : (w.IsLeft ? FrictionOuter : FrictionInner);
                        }
                        else w.Friction = 100;

                    if (powerFlag)
                        wheel.Power = power;

                    // update height
                    if (suspensionHightFlag && ((roll > suspensionHightRoll && w.IsLeft) || (roll < -suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                        wheel.Height += (value - wheel.Height) * 0.5f;
                        rollCompensating = true;
                    }
                    else
                    {
                        wheel.Height += (w.TargetHeight - wheel.Height) * 0.3f;
                        rollCompensating = false;
                    }

                    // update steering
                    if (isTurning)
                        wheel.MaxSteerAngle = (float)(isTurningLeft ? w.SteerAngleLeft : w.SteerAngleRight);

                    // half breaking
                    wheel.Brake = true;
                    if (isHalfBreaking && w.IsFront)
                    {
                        wheel.Brake = false;
                        wheel.Power = 0;
                    }

                    if (addWheelsFlag && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                if (SubWheels.Count() > 0)
                {
                    high = ini["HighModeHight"].ToDouble(SubWheels.FirstOrDefault().HeightOffsetMin);
                }
                var subWheelPropulsion = Cruise ? propulsion : (forwardBackward != 0 && !(upDown > 0) ? (forwardBackward < 0 ? 1 : -1) : 0);
                foreach (var w in SubWheels)
                {
                    IMyMotorSuspension wheel = w.Wheel;
                    w.SpeedLimit = MyWheels.First().SpeedLimit;
                    wheel.PropulsionOverride = w.IsLeft ? subWheelPropulsion : -subWheelPropulsion;

                    if (powerFlag)
                        wheel.Power = power;

                    if (subWheelsStrengthFlag)
                    {
                        updateStrength.SubAction?.Invoke(w, GridUnsprungMass * gravityMagnitude);
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (frictionFlag)
                        if (speed > FrictionMinSpeed && isTurning)
                        {
                            w.Friction = isTurningLeft ? (w.IsLeft ? FrictionInner : FrictionOuter) : (w.IsLeft ? FrictionOuter : FrictionInner);
                        }
                        else w.Friction = 100;

                    if (suspensionHightFlag && ((roll > suspensionHightRoll && w.IsLeft) || (roll < -suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                        wheel.Height += (value - wheel.Height) * 0.5f;
                    }
                    else
                        wheel.Height += (w.TargetHeight - wheel.Height) * 0.3f;

                    if (addWheelsFlag && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                yield return null;
            }
        }

        IEnumerable<IMyLightingBlock> Lights => Memo.Of(() =>
        {
            var orientation = Controllers.MainController.Orientation;
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
        }, "stopLights", Memo.Refs(Mass.BaseMass, Config));

        IEnumerable StopLightsTask()
        {
            var ini = Config;
            var lights = Lights;

            while (lights.Equals(Lights) && ini.Equals(Config))
            {
                float upDown = UpDown;
                float forwardBackward = ForwardBackward;
                foreach (var l in lights)
                {
                    l.Radius = 1f;
                    l.Intensity = 1f;
                    l.Falloff = 0;
                    l.Color = Color.DarkRed;

                    if (upDown > 0)
                    {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.Red;
                    }
                    if (forwardBackward > 0)
                    {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.White;
                    }

                }
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

            high = MathHelper.Clamp(high, controlSubWheel.HeightOffsetMin, controlSubWheel.HeightOffsetMax);//Max
            closeHigh = high - currentHeight;
            closeLow = currentHeight - low;
            targetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;
            foreach (var w in SubWheels) w.TargetHeight = targetHeight;

            yield return null;
        }
    }
}
