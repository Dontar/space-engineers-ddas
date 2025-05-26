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
        #region mdk preserve
        private string _tag = "{DDAS}";
        private string _ignoreTag = "{Ignore}";
        private float _lowModeHight = 0;
        private string _highModeHight = "Max";
        private double _strengthFactor = 0.6;
        private double _suspensionHightRoll = 30;
        private double _maxSteeringAngle = 25;
        private FocalPoint _ackermanFocalPoint = FocalPoint.CoM;
        private double _ackermanFocalPointOffset = 0;
        private float _frictionInner = 80;
        private float _frictionOuter = 60;
        private double _frictionMinSpeed = 5;
        private string _pidCruise = "0.5/0/0/0";
        private string _pidPower = "10/0/0/0";
        private bool _addWheels = true;
        private bool _suspensionStrength = true;
        private bool _subWheelsStrength = true;
        private bool _power = true;
        private bool _stopLights = true;
        private bool _friction = true;
        private bool _suspensionHight = true;
        // private bool _autoLevel = true;
        // private float _autoLevelPower = 30;
        #endregion

        enum FocalPoint
        {
            CoM,
            RC
        }
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            InitGridProps();
            InitStopLights();
            InitAutoLevel();
            InitScreens();
            InitWheels();

            TaskManager.AddTask(Util.StatusMonitor(this));                          //0
            TaskManager.AddTask(MainTask());                                        //1
            TaskManager.AddTask(AutopilotTask());                                   //2
            _StopLightsTask = TaskManager.AddTask(StopLightsTask());                //3
            _PowerTask = TaskManager.AddTask(PowerTask());                          //4
            TaskManager.AddTask(SuspensionStrengthTask(), 5f);                      //5
            // TaskManager.AddTask(AutoLevelTask());                                   //6
            TaskManager.AddTask(ScreensTask(), 0.5f);                               //7
            TaskManager.AddTask(GridOrientationsTask());                            //8
            TaskManager.AddTask(Util.DisplayLogo("DDAS", Me.GetSurface(0)), 1.5f);  //9

            TaskManager.PauseTask(_PowerTask, _power);
            TaskManager.PauseTask(_StopLightsTask, _stopLights);
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
                    TaskManager.AddTaskOnce(FlipGridTask(), 1f);
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
            double high;
            if (!double.TryParse(_highModeHight, out high))
            {
                high = MyWheels.FirstOrDefault().HeightOffsetMin;
            }
            var low = _lowModeHight;

            var rollCompensating = false;

            while (true)
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
                    if (_suspensionStrength)
                    {
                        updateStrength.Action?.Invoke(w, GridUnsprungMass * gravityMagnitude);
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (_friction)
                        if (speed > _frictionMinSpeed && isTurning)
                        {
                            w.Friction = isTurningLeft ? (w.IsLeft ? _frictionInner : _frictionOuter) : (w.IsLeft ? _frictionOuter : _frictionInner);
                        }
                        else w.Friction = 100;

                    if (_power)
                        wheel.Power = power;

                    // update height
                    if (_suspensionHight && ((roll > _suspensionHightRoll && w.IsLeft) || (roll < -_suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                        wheel.Height += (value - wheel.Height) * 0.2f;
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

                    if (_addWheels && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                if (SubWheels.Count() > 0)
                {
                    if (!double.TryParse(_highModeHight, out high))
                    {
                        high = SubWheels.FirstOrDefault().HeightOffsetMin;
                    }
                }
                var subWheelPropulsion = Cruise ? propulsion : (forwardBackward != 0 && !(upDown > 0) ? (forwardBackward < 0 ? 1 : -1) : 0);
                foreach (var w in SubWheels)
                {
                    IMyMotorSuspension wheel = w.Wheel;
                    w.SpeedLimit = MyWheels.First().SpeedLimit;
                    wheel.PropulsionOverride = w.IsLeft ? subWheelPropulsion : -subWheelPropulsion;

                    if (_power)
                        wheel.Power = power;

                    if (_subWheelsStrength)
                    {
                        updateStrength.SubAction?.Invoke(w, GridUnsprungMass * gravityMagnitude);
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (_friction)
                        if (speed > _frictionMinSpeed && isTurning)
                        {
                            w.Friction = isTurningLeft ? (w.IsLeft ? _frictionInner : _frictionOuter) : (w.IsLeft ? _frictionOuter : _frictionInner);
                        }
                        else w.Friction = 100;

                    if (_suspensionHight && ((roll > _suspensionHightRoll && w.IsLeft) || (roll < -_suspensionHightRoll && !w.IsLeft)))
                    {
                        var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                        wheel.Height += (value - wheel.Height) * 0.5f;
                    }
                    else
                        wheel.Height += (w.TargetHeight - wheel.Height) * 0.3f;

                    if (_addWheels && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                yield return null;
            }
        }

        IEnumerable<IMyLightingBlock> Lights;
        void InitStopLights()
        {
            var orientation = Controllers.MainController.Orientation;

            Lights = Util.GetBlocks<IMyLightingBlock>(b =>
                b.IsSameConstructAs(Me) && (
                    Util.IsTagged(b, _tag) || (
                        Util.IsNotIgnored(b, _ignoreTag) &&
                        orientation.TransformDirectionInverse(b.Orientation.Forward) == Base6Directions.Direction.Backward
                    )
                )
            );
        }

        IEnumerable StopLightsTask()
        {
            while (true)
            {
                float upDown = UpDown;
                float forwardBackward = ForwardBackward;
                foreach (var l in Lights)
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

            float high = float.TryParse(_highModeHight, out high) ? high : controlWheel.HeightOffsetMin;//Max
            var low = _lowModeHight;//0

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
