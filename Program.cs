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
            _SubSuspensionStrengthTask.IsPaused = !Ini.Get("Options", "SubSuspensionStrength").ToBoolean();
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
                    TaskManager.AddTaskOnce(LowModeTask());
                    break;
                case "high":
                    TaskManager.AddTaskOnce(HighModeTask());
                    break;
                case "flip":
                    TaskManager.AddTaskOnce(FlipGridTask(), 2f);
                    break;
                case "cruise":
                    TaskManager.AddTaskOnce(CruiseTask());
                    break;
                case "record":
                    TaskManager.AddTaskOnce(RecordPathTask(), 1.7f);
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
                case "stop_recording":
                case "stop_record":
                    gridProps.Recording = false;
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
                    if ((gridProps.Roll > 3 && w.IsLeft) || (gridProps.Roll < -3 && !w.IsLeft))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, w.HeightOffsetMin, 0);
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

                    if ((gridProps.Roll > 3 && w.IsLeft) || (gridProps.Roll < -3 && !w.IsLeft))
                    {
                        var value = Util.NormalizeClamp(Math.Abs(gridProps.Roll), 0, 25, w.HeightOffsetMin, 0);
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

        IEnumerable FlipGridTask()
        {
            if (gridProps.Flipping) yield break;
            var ini = Config;
            var gyroList = Util.GetBlocks<IMyGyro>(b => Util.IsNotIgnored(b, ini["IgnoreTag"]));
            if (gyroList.Count == 0) yield break;
            var pidRoll = new PID(ini.GetValueOrDefault("PIDFlip", "10/0/0/0"));
            gyroList.ForEach(g => g.GyroOverride = true);
            gridProps.Flipping = true;
            while (ini.Equals(Config))
            {
                if ((gridProps.Roll > -25 && gridProps.Roll < 25) || gridProps.UpDown < 0)
                {
                    gyroList.ForEach(g => { g.GyroPower = 100; g.Roll = g.Yaw = g.Pitch = 0; g.GyroOverride = false; });
                    gridProps.Flipping = false;
                    yield break;
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var power = Util.NormalizeValue(Math.Abs(gridProps.Roll), 0, 180, 5, 100);
                var rollSpeed = MathHelper.Clamp(pidRoll.Signal(gridProps.Roll, dt), -60, 60);
                gyroList.ForEach(g =>
                {
                    g.GyroPower = (float)power;
                    Util.ApplyGyroOverride(0, 0, rollSpeed, g, gridProps.MainController.WorldMatrix);
                });
                yield return null;
            }
        }
        List<IMyTerminalBlock> Screens => Memo.Of(() =>
        {
            var tag = Config.GetValueOrDefault("Tag", "{DDAS}");
            var screensBlocks = Util.GetBlocks<IMyTerminalBlock>(b => Util.IsTagged(b, tag) && b is IMyTextSurfaceProvider && (b as IMyTextSurfaceProvider).SurfaceCount > 0);
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

                screenText.Clear();
                screenText.AppendLine($"Cruise:      {gridProps.Cruise}");
                screenText.AppendLine($"CruiseSpeed: {gridProps.CruiseSpeed:N2} km/h");
                screenText.AppendLine($"Speed:       {gridProps.Speed * 3.6:N2} km/h");
                screenText.AppendLine($"Flipping:    {gridProps.Flipping}");
                screenText.AppendLine($"Power:       {power.Power:N2}");
                screenText.AppendLine($"WheelPower:  {power.MaxPowerPercent:N2}");
                screenText.AppendLine($"Propulsion:  {propulsion:N2}");
                screenText.AppendLine($"Recording:   {gridProps.Recording}");
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

        struct CruiseTaskResult
        {
            public float Propulsion;
        }
        IEnumerable<CruiseTaskResult> CruiseTask(float cruiseSpeed = -1, Func<bool> cruiseWhile = null)
        {
            if (gridProps.Cruise) yield break;
            var ini = Config;
            gridProps.Cruise = true;
            gridProps.CruiseSpeed = cruiseSpeed > -1 ? cruiseSpeed : (float)(gridProps.Speed * 3.6);
            cruiseWhile = cruiseWhile ?? (() => gridProps.UpDown == 0);
            var pid = new PID(ini.GetValueOrDefault("PIDCruise", "10/0/0/0"));
            while (ini.Equals(Config) && cruiseWhile())
            {
                if (cruiseSpeed == -1)
                {
                    gridProps.CruiseSpeed = MathHelper.Clamp(gridProps.CruiseSpeed + (float)gridProps.ForwardBackward * -5f, 5, MyWheels.First().SpeedLimit);
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = gridProps.Speed * 3.6;
                var targetSpeed = gridProps.CruiseSpeed;
                var error = targetSpeed - currentSpeedKmh;
                var propulsion = Util.NormalizeClamp(pid.Signal(error, dt), 0, targetSpeed, 0, 1);

                yield return new CruiseTaskResult { Propulsion = (float)propulsion };
            }
            gridProps.Cruise = false;
            gridProps.CruiseSpeed = 0;
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

        struct PowerTaskResult
        {
            public float Power;
            public float WheelMaxPower;
            public float GridMaxPower;
            public float MaxPowerPercent;

        }
        IEnumerable<PowerTaskResult> PowerTask()
        {
            var ini = Config;
            var wheels = MyWheels;
            var PID = new PID(ini.GetValueOrDefault("PIDPower", "4/4/0/1"));
            var wheelPower = wheels.Concat(SubWheels).Sum(w => w.MaxPower);
            float passivePower = 0;
            while (ini.Equals(Config) && wheels.Equals(MyWheels))
            {
                if (gridProps.Speed < 0.1)
                {
                    passivePower = PowerProducersPower.CurrentOutput;
                }
                var vehicleMaxPower = PowerProducersPower.MaxOutput;
                var powerMaxPercent = MathHelper.Clamp((vehicleMaxPower - passivePower) / wheelPower, 0, 1);
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = gridProps.Speed * 3.6;
                var targetSpeed = gridProps.Cruise ? gridProps.CruiseSpeed : (gridProps.ForwardBackward != 0 ? wheels.First().SpeedLimit : 0);
                var error = targetSpeed - currentSpeedKmh;
                var power = MathHelper.Clamp(PID.Signal(error, dt), 5, 100 * powerMaxPercent);
                yield return new PowerTaskResult
                {
                    Power = (float)power,
                    WheelMaxPower = (float)wheelPower,
                    GridMaxPower = vehicleMaxPower,
                    MaxPowerPercent = (float)powerMaxPercent
                };
            }
        }

        double CalcStrength(List<WheelWrapper> wheels)
        {
            if (wheels.Count == 0) return 0;
            var frontMostAxel = wheels.Min(w => w.ToCoM.Z);
            var rearMostAxel = wheels.Max(w => w.ToCoM.Z);
            var chassisLength = rearMostAxel - frontMostAxel;

            var isTrailer = frontMostAxel > 0;
            // is trailer no front wheels
            if (isTrailer)
            {
                frontMostAxel = -rearMostAxel;
                chassisLength = wheels.First().Wheel.CubeGrid.WorldVolume.Radius * 2;
            }

            double normalizeFactor = 0;
            wheels.ForEach(w =>
            {
                if (chassisLength < 0.1) return;
                w.WeightRatio = w.IsFront
                    ? Math.Abs(Util.NormalizeValue(w.ToCoM.Z, rearMostAxel, frontMostAxel, 0, rearMostAxel / chassisLength))
                    : Math.Abs(Util.NormalizeValue(w.ToCoM.Z, frontMostAxel, rearMostAxel, 0, frontMostAxel / chassisLength));
                normalizeFactor += w.WeightRatio * (isTrailer ? 2 : 1);
            });
            return normalizeFactor;
        }

        struct StrengthTaskResult
        {
            public Action<WheelWrapper, double> action;
        }
        IEnumerable<StrengthTaskResult> SuspensionStrengthTask()
        {
            var myWheels = MyWheels;
            if (myWheels.Count == 0) yield return default(StrengthTaskResult);
            double normalizeFactor = CalcStrength(myWheels);
            while (myWheels.Equals(MyWheels))
            {
                yield return new StrengthTaskResult
                {
                    action = (w, GridUnsprungWeight) =>
                    {
                        w.TargetStrength = Memo.Of(() =>
                            MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100)
                        , $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight));
                        w.Wheel.Strength += (float)((w.TargetStrength - w.Wheel.Strength) * 0.5);
                    }
                };
            }
        }

        struct SubStrengthTaskResult
        {
            public Action<WheelWrapper, double> action;
        }
        IEnumerable<SubStrengthTaskResult> SubSuspensionStrengthTask()
        {
            var subWheels = SubWheels;
            if (subWheels.Count == 0) yield return default(SubStrengthTaskResult);
            double normalizeFactor = CalcStrength(subWheels);
            while (subWheels.Equals(SubWheels))
            {
                yield return new SubStrengthTaskResult
                {
                    action = (w, GridUnsprungWeight) =>
                    {
                        w.TargetStrength = Memo.Of(() =>
                            MathHelper.Clamp(Math.Sqrt(w.WeightRatio / normalizeFactor * GridUnsprungWeight) / w.BlackMagicFactor, 5, 100)
                        , $"TargetStrength-{w.Wheel.EntityId}", Memo.Refs(GridUnsprungWeight));
                        w.Wheel.Strength += (float)((w.TargetStrength - w.Wheel.Strength) * 0.5);
                    }
                };
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

        IEnumerable HighModeTask()
        {
            var targetHigh = Config.GetValueOrDefault("HighModeHight", "Max");
            MyWheels.ForEach(w => w.TargetHeight = targetHigh == "Max" ? w.HeightOffsetMin : float.Parse(targetHigh));
            SubWheels.ForEach(w => w.TargetHeight = targetHigh == "Max" ? w.HeightOffsetMin : float.Parse(targetHigh));
            yield return null;
        }

        IEnumerable LowModeTask()
        {
            var targetLow = float.Parse(Config.GetValueOrDefault("LowModeHight", "0"));
            MyWheels.ForEach(w => w.TargetHeight = targetLow);
            SubWheels.ForEach(w => w.TargetHeight = targetLow);
            yield return null;
        }

        struct AutopilotTaskResult
        {
            public Vector3D Direction;
            public float Steer;
        }
        IEnumerable AutopilotTask()
        {
            var ini = Config;
            var autopilot = gridProps.Autopilot;
            if (autopilot == null) yield break;
            var sensor = Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, ini["IgnoreTag"])).FirstOrDefault();
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);

            while (ini.Equals(Config) && autopilot.IsAutoPilotEnabled)
            {
                if (!gridProps.Cruise)
                {
                    TaskManager.AddTask(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled));
                }

                gridProps.CruiseSpeed = autopilot.SpeedLimit * 3.6f;

                var currentPosition = autopilot.GetPosition();
                var destinationVector = autopilot.CurrentWaypoint.Coords - currentPosition;

                if (autopilot.GetValueBool("CollisionAvoidance") && sensor != null)
                {
                    var detectionBox = autopilot.CubeGrid.WorldAABB
                        .Inflate(2)
                        .Include(currentPosition + autopilot.WorldMatrix.Forward * 15)
                        .Include(currentPosition + autopilot.WorldMatrix.Right * 5)
                        .Include(currentPosition + autopilot.WorldMatrix.Left * 5);

                    var obstructions = new List<MyDetectedEntityInfo>();
                    sensor.DetectedEntities(obstructions);
                    var obstruction = obstructions.FirstOrDefault(o => detectionBox.Intersects(o.BoundingBox));
                    if (!obstruction.IsEmpty())
                    {
                        var v = obstruction.BoundingBox.TransformFast(autopilot.WorldMatrix);
                        var corners = Enumerable.Range(0, 8).Select(i => v.GetCorner(i)).Max(i => Vector3D.DistanceSquared(i, currentPosition));
                        destinationVector += corners;
                    }
                }

                var T = MatrixD.Transpose(autopilot.WorldMatrix);
                var directionVector = Vector3D.TransformNormal(destinationVector, T);
                var direction = Vector3D.ProjectOnPlane(ref directionVector, ref Vector3D.Up);
                var directionAngle = Math.Atan2(direction.Dot(Vector3D.Right), direction.Dot(Vector3D.Forward));

                if (direction.Length() < autopilot.CubeGrid.WorldVolume.Radius)
                {
                    if (autopilot.CurrentWaypoint.Coords == wayPoints.LastOrDefault().Coords)
                    {
                        switch (autopilot.FlightMode)
                        {
                            case FlightMode.OneWay:
                                autopilot.SetAutoPilotEnabled(false);
                                break;
                            case FlightMode.Patrol:
                                autopilot.ClearWaypoints();
                                wayPoints.Reverse();
                                wayPoints.ForEach(w => autopilot.AddWaypoint(w));
                                autopilot.SetAutoPilotEnabled(true);
                                break;
                        }
                    }
                    else
                    {
                        var waypointData = new List<MyWaypointInfo>();
                        autopilot.GetWaypointInfo(waypointData);
                        waypointData.Add(waypointData.FirstOrDefault());
                        waypointData.RemoveAt(0);
                        autopilot.ClearWaypoints();
                        waypointData.ForEach(w => autopilot.AddWaypoint(w));
                        autopilot.SetAutoPilotEnabled(true);
                    }
                }
                yield return new AutopilotTaskResult { Direction = direction, Steer = (float)directionAngle };
            }
        }

        IEnumerable RecordPathTask()
        {
            if (gridProps.Recording) yield break;
            var autopilot = gridProps.Autopilot;
            var wayPoints = new List<MyWaypointInfo>();
            var counter = 0;
            gridProps.Recording = true;
            while (gridProps.Recording)
            {
                var current = autopilot.GetPosition();
                if (Vector3D.Distance(current, wayPoints.LastOrDefault().Coords) > 1)
                {
                    wayPoints.Add(new MyWaypointInfo($"Waypoint-#{counter++}", current));
                }
                yield return null;
            }
            autopilot.CustomData = string.Join("\n", wayPoints);
        }

        IEnumerable ImportPathTask()
        {
            var autopilot = gridProps.Autopilot;
            var wayPoints = new List<MyWaypointInfo>();
            MyWaypointInfo.FindAll(autopilot.CustomData, wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ReversePathTask()
        {
            var autopilot = gridProps.Autopilot;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.Reverse();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ExportPathTask()
        {
            var autopilot = gridProps.Autopilot;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.CustomData = string.Join("\n", wayPoints);
            yield return null;
        }

    }
}
