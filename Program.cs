using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        #region mdk preserve
        string _tag = "{DDAS}";
        string _ignoreTag = "{Ignore}";

        bool _suspensionHight = true;
        double _suspensionHightRoll = 30;
        float _lowModeHight = 0;
        string _highModeHight = "Max";

        bool _suspensionStrength = true;
        bool _subWheelsStrength = true;
        double _strengthFactor = 0.6;

        FocalPoint _ackermanFocalPoint = FocalPoint.RC;
        double _ackermanFocalPointOffset = 0;
        double _maxSteeringAngle = 25;

        bool _friction = true;
        float _frictionInner = 80;
        float _frictionOuter = 60;
        double _frictionMinSpeed = 5;

        bool _autoLevel = true;
        string _pidRoll = "10/0/0/0";
        string _pidPitch = "10/0/0/0";

        string _pidCruise = "18/1/0/0";

        bool _power = true;
        string _pidPower = "18/0/0/0";

        bool _addWheels = true;
        bool _stopLights = true;

        enum FocalPoint
        {
            CoM,
            RC
        }
        #endregion
        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            InitGridProps();
            Task.RunTask(Util.StatusMonitorTask(this));
            _MainTask = Task.RunTask(MainTask());
            Task.RunTask(ScreensTask()).Every(0.5f);
            _StopLightsTask = Task.RunTask(StopLightsTask()).Pause(!_stopLights);
            Task.RunTask(AutopilotTask()).Every(1 / 3);
            _AutoLevelTask = Task.RunTask(AutoLevelTask()).Pause(!_autoLevel);
            _PowerTask = Task.RunTask(PowerTask()).Pause(!_power);
            _PowerConsumptionTask = Task.RunTask(PowerConsumptionTask()).Every(3);
            Task.RunTask(GridOrientationsTask());
            Task.RunTask(Util.DisplayLogo("DDAS", Me.GetSurface(0))).Every(1.5f);
        }

        ITask _AutoLevelTask;
        ITask _MainTask;
        ITask _PowerTask;
        ITask _PowerConsumptionTask;
        ITask _StopLightsTask;

        public void Main(string argument, UpdateType updateSource) {
            if (Controllers.MainController == null) {
                Util.Echo("No controller found");
                return;
            }

            if (!string.IsNullOrEmpty(argument))
                ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            Memo.Of("OnBaseMassChange", Mass.BaseMass, (OldBaseMass) => {
                if (OldBaseMass != 0) InitGridProps();
                InitWheels();
                InitPower();
                InitStopLights();
                InitAutoLevel();
                InitAutopilot();
                InitScreens();

                _MainTask.Pause(AllWheels.Count() == 0);
            });

            Memo.Of("OnPhysicalMassChange", Mass.PhysicalMass, () => InitStrength());

            Task.Tick(Runtime.TimeSinceLastRun);
        }

        void ProcessCommands(string argument) {
            var cmd = new MyCommandLine();
            if (cmd.TryParse(argument)) {
                switch (cmd.Argument(0).ToLower()) {
                    case "low":
                    case "high":
                    case "toggle_hight":
                        ToggleHightMode();
                        break;
                    case "flip":
                        Task.RunTask(FlipGridTask()).Once();
                        break;
                    case "cruise":
                        Task.RunTask(CruiseTask()).Once();
                        break;
                    case "level":
                        _autoLevel = !_autoLevel;
                        _AutoLevelTask.Pause(!_autoLevel);
                        break;
                    case "status":
                        ChangeScreenType();
                        break;
                    case "rest_turrets":
                        RestTurrets();
                        break;
                    default:
                        ProcessAICommands(cmd);
                        break;
                }
            }
        }

        IEnumerable MainTask() {
            double high;
            if (!double.TryParse(_highModeHight, out high)) {
                high = MyWheels.FirstOrDefault().HeightOffsetMin;
            }
            var low = _lowModeHight;

            var rollCompensating = false;

            while (true) {
                var speed = Speed;
                var leftRight = LeftRight;
                var upDown = UpDown;
                var forwardBackward = ForwardBackward;

                var propulsion = CruiseResult.Propulsion;
                var power = PowerResult;
                var autopilot = AutopilotResult;
                var orientation = OrientationResult;

                var roll = orientation.Roll + (rollCompensating ? (orientation.Roll > 0 ? 6 : -6) : 0);

                bool isTurning = leftRight != 0 || !Util.IsBetween(autopilot.Steer, -0.4, 0.4);
                bool isTurningLeft = leftRight < 0 || autopilot.Steer > 0;
                var isHalfBreaking = upDown > 0 && forwardBackward < 0;

                if (Controllers.SubController != null)
                    Controllers.SubController.HandBrake = Controller.HandBrake || upDown > 0;

                foreach (var w in MyWheels) {
                    IMyMotorSuspension wheel = w.Wheel;
                    wheel.SteeringOverride = MathHelper.Clamp(w.IsFrontFocal ? -autopilot.Steer : autopilot.Steer, -1, 1);
                    wheel.PropulsionOverride = w.IsLeft ? propulsion : -propulsion;

                    // update strength
                    if (_suspensionStrength) {
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (_friction)
                        if (speed > _frictionMinSpeed && isTurning) {
                            w.Friction = isTurningLeft ? (w.IsLeft ? _frictionInner : _frictionOuter) : (w.IsLeft ? _frictionOuter : _frictionInner);
                        }
                        else w.Friction = 100;

                    if (_power)
                        wheel.Power = power.Power;
                    else if (wheel.Power > power.MaxPowerPercent)
                        wheel.Power = power.MaxPowerPercent;

                    // update height
                    if (_suspensionHight) {
                        if ((roll > _suspensionHightRoll && w.IsLeft) || (roll < -_suspensionHightRoll && !w.IsLeft)) {
                            var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                            wheel.Height += (value - wheel.Height) * 0.2f;
                            rollCompensating = true;
                        }
                        else {
                            wheel.Height += (w.TargetHeight - wheel.Height) * 0.3f;
                            rollCompensating = false;
                        }
                    }

                    // update steering
                    if (isTurning)
                        wheel.MaxSteerAngle = (float)(isTurningLeft ? w.SteerAngleLeft : w.SteerAngleRight);

                    // half breaking
                    wheel.Brake = true;
                    if (isHalfBreaking && w.IsFront) {
                        wheel.Brake = false;
                        wheel.Power = 0;
                    }

                    if (_addWheels && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                if (SubWheels.Count() > 0) {
                    yield return null;
                    if (!double.TryParse(_highModeHight, out high)) {
                        high = SubWheels.FirstOrDefault().HeightOffsetMin;
                    }
                }
                var subWheelPropulsion = Cruise ? propulsion : -forwardBackward;
                foreach (var w in SubWheels) {
                    IMyMotorSuspension wheel = w.Wheel;
                    w.SpeedLimit = MyWheels.First().SpeedLimit;
                    wheel.PropulsionOverride = w.IsLeft ? subWheelPropulsion : -subWheelPropulsion;

                    if (_power)
                        wheel.Power = power.Power;
                    else if (wheel.Power > power.MaxPowerPercent)
                        wheel.Power = power.MaxPowerPercent;

                    if (_subWheelsStrength) {
                        wheel.Strength += (float)((w.TargetStrength - wheel.Strength) * 0.5);
                    }

                    if (_friction)
                        if (speed > _frictionMinSpeed && isTurning) {
                            w.Friction = isTurningLeft ? (w.IsLeft ? _frictionInner : _frictionOuter) : (w.IsLeft ? _frictionOuter : _frictionInner);
                        }
                        else w.Friction = 100;

                    if (_suspensionHight) {
                        if ((roll > _suspensionHightRoll && w.IsLeft) || (roll < -_suspensionHightRoll && !w.IsLeft)) {
                            var value = (float)Util.NormalizeClamp(Math.Abs(orientation.Roll), 0, 25, high, low);
                            wheel.Height += (value - wheel.Height) * 0.5f;
                        }
                        else
                            wheel.Height += (w.TargetHeight - wheel.Height) * 0.3f;
                    }

                    if (_addWheels && !wheel.IsAttached)
                        wheel.ApplyAction("Add Top Part");

                }
                yield return null;
            }
        }

        IEnumerable<IMyLightingBlock> Lights;
        void InitStopLights() {
            var orientation = Controllers.MainController.Orientation;

            Lights = Util.GetBlocks<IMyLightingBlock>(b =>
                Util.IsTagged(b, _tag) || (
                    Util.IsNotIgnored(b, _ignoreTag) &&
                    orientation.TransformDirectionInverse(b.Orientation.Forward) == Base6Directions.Direction.Backward
                )
            );
            _StopLightsTask.Pause(Lights.Count() == 0);
        }

        IEnumerable StopLightsTask() {
            while (true) {
                foreach (var l in Lights) {
                    l.Radius = 1f;
                    l.Intensity = 1f;
                    l.Falloff = 0;
                    l.Color = Color.DarkRed;

                    if (UpDown > 0) {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.Red;
                    }

                    if (ForwardBackward > 0) {
                        l.Intensity = 5f;
                        l.Falloff = 1.3f;
                        l.Radius = 5f;
                        l.Color = Color.White;
                    }
                }
                yield return null;
            }
        }

        void ToggleHightMode() {
            var controlWheel = MyWheels.FirstOrDefault();
            var currentHeight = controlWheel.TargetHeight;// -31.9

            float high = float.TryParse(_highModeHight, out high) ? high : controlWheel.HeightOffsetMin;//Max
            var low = _lowModeHight;//0

            var closeHigh = high - currentHeight;// -32 -31.9 = -0.1
            var closeLow = currentHeight - low;// -31.9 - 0 = -31.9

            var targetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;

            foreach (var w in MyWheels) w.TargetHeight = targetHeight;

            if (SubWheels.Count() == 0) return;
            var controlSubWheel = SubWheels.FirstOrDefault();
            currentHeight = controlSubWheel.TargetHeight;

            high = MathHelper.Clamp(high, controlSubWheel.HeightOffsetMin, controlSubWheel.HeightOffsetMax);//Max
            closeHigh = high - currentHeight;
            closeLow = currentHeight - low;
            targetHeight = Math.Abs(closeHigh) < Math.Abs(closeLow) ? low : high;
            foreach (var w in SubWheels) w.TargetHeight = targetHeight;
        }

        void RestTurrets() {
            var turr = Util.GetBlocks<IMySearchlight>();
            foreach (var item in turr) {
                item.SetManualAzimuthAndElevation(0, 0);
            }
        }
    }
}
