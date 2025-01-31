using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
using VRage.Network;
using VRageMath;
using VRageRender;
using VRageRender.Animations;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {

        public bool Recording { get; set; } = false;

        struct AutopilotTaskResult
        {
            public Vector3D Direction;
            public float Steer;
            public string Waypoint;
            public int WaypointCount;
            public FlightMode Mode;
        }

        IEnumerable AutopilotTask()
        {
            var controller = gridProps.MainController;
            var autopilot = controller is IMyRemoteControl ? controller as IMyRemoteControl : null;
            if (autopilot == null || !autopilot.IsAutoPilotEnabled) yield break;
            var ini = Config;
            var sensor = Memo.Of(() => Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, ini["IgnoreTag"].ToString())).FirstOrDefault(), "sensor", Memo.Refs(gridProps.Mass.BaseMass));
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);

            Action movePointToEnd = () =>
            {
                var waypointData = new List<MyWaypointInfo>();
                autopilot.GetWaypointInfo(waypointData);
                waypointData.Add(waypointData.FirstOrDefault());
                waypointData.RemoveAt(0);
                autopilot.ClearWaypoints();
                waypointData.ForEach(w => autopilot.AddWaypoint(w));
            };

            while (ini.Equals(Config) && autopilot.IsAutoPilotEnabled)
            {
                if (!Cruise)
                {
                    TaskManager.AddTaskOnce(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled));
                }
                else
                    CruiseSpeed = autopilot.SpeedLimit * 3.6f;


                if (gridProps.UpDown > 0)
                {
                    autopilot.SetAutoPilotEnabled(false);
                    yield break;
                }

                var currentPosition = autopilot.GetPosition();
                var destinationVector = autopilot.CurrentWaypoint.Coords - currentPosition + AvoidCollision(autopilot, sensor, currentPosition);

                var T = MatrixD.Transpose(autopilot.WorldMatrix);
                var direction = Vector3D.TransformNormal(destinationVector, T); direction.Y = 0;
                var directionAngle = Util.ToAzimuth(direction);

                if (direction.Length() < autopilot.CubeGrid.WorldVolume.Radius)
                {
                    if (autopilot.CurrentWaypoint.Coords == wayPoints.LastOrDefault().Coords)
                    {
                        switch (autopilot.FlightMode)
                        {
                            case FlightMode.Circle:
                                movePointToEnd();
                                autopilot.SetAutoPilotEnabled(true);
                                break;
                            case FlightMode.Patrol:
                                autopilot.ClearWaypoints();
                                wayPoints.Reverse();
                                wayPoints.ForEach(w => autopilot.AddWaypoint(w));
                                autopilot.SetAutoPilotEnabled(true);
                                break;
                            default:
                                movePointToEnd();
                                autopilot.HandBrake = true;
                                autopilot.SetAutoPilotEnabled(false);
                                break;
                        }
                    }
                    else
                    {
                        movePointToEnd();
                        autopilot.SetAutoPilotEnabled(true);
                    }
                }
                yield return new AutopilotTaskResult
                {
                    Waypoint = autopilot.CurrentWaypoint.Name,
                    Direction = direction,
                    Steer = (float)directionAngle,
                    Mode = autopilot.FlightMode
                };
            }
        }

        IEnumerable AutopilotAITask()
        {
            var autopilot = Memo.Of(() => Util.GetBlocks<IMyFlightMovementBlock>().FirstOrDefault(), "AI", Memo.Refs(gridProps.Mass.BaseMass));
            if (autopilot == null || !autopilot.IsAutoPilotEnabled) yield break;
            var ini = Config;
            var sensor = Memo.Of(() => Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, ini["IgnoreTag"].ToString())).FirstOrDefault(), "sensor", Memo.Refs(gridProps.Mass.BaseMass));
            var controller = gridProps.MainController;

            var wayPoints = new List<IMyAutopilotWaypoint>();
            autopilot.GetWaypoints(wayPoints);

            IEnumerator<IMyAutopilotWaypoint> wayPointsInfo = wayPoints.GetEnumerator();
            wayPointsInfo.MoveNext();

            while (ini.Equals(Config) && autopilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = (wayPoints.Count > 1 ? wayPointsInfo.Current?.Matrix.Translation : autopilot.CurrentWaypoint?.Matrix.Translation) ?? Vector3D.Zero;

                if (currentWaypoint == Vector3D.Zero)
                {
                    controller.HandBrake = true;
                    yield break;
                }
                else
                    controller.HandBrake = false;


                if (!Cruise && !controller.HandBrake)
                {
                    TaskManager.AddTaskOnce(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled && !controller.HandBrake));
                }
                else
                    CruiseSpeed = autopilot.SpeedLimit * 3.6f;


                if (gridProps.UpDown > 0)
                {
                    autopilot.SetValueBool("ActivateBehavior", false);
                    yield break;
                }

                var currentPosition = autopilot.GetPosition();
                var destinationVector = currentWaypoint - currentPosition + AvoidCollision(autopilot, sensor, currentPosition);

                var T = MatrixD.Transpose(autopilot.WorldMatrix);
                var direction = Vector3D.TransformNormal(destinationVector, T); direction.Y = 0;
                var directionAngle = Util.ToAzimuth(direction);

                var isWayPointReached = direction.Length() < autopilot.CubeGrid.WorldVolume.Radius;

                if (isWayPointReached)
                {
                    if (wayPoints.Count > 1)
                    {
                        if (!wayPointsInfo.MoveNext())
                        {
                            switch (autopilot.FlightMode)
                            {
                                case FlightMode.Circle:
                                    wayPointsInfo = wayPoints.GetEnumerator();
                                    wayPointsInfo.MoveNext();
                                    break;
                                case FlightMode.Patrol:
                                    var distanceFirst = Vector3D.Distance(wayPoints.First().Matrix.Translation, currentPosition);
                                    var distanceLast = Vector3D.Distance(wayPoints.Last().Matrix.Translation, currentPosition);
                                    wayPointsInfo = (distanceLast < distanceFirst ? wayPoints.Select(w => w).Reverse() : wayPoints).Skip(1).GetEnumerator();
                                    wayPointsInfo.MoveNext();
                                    break;
                                default:
                                    controller.HandBrake = true;
                                    autopilot.SetValueBool("ActivateBehavior", false);
                                    yield break;
                            }
                        }
                    }
                    else
                    {
                        controller.HandBrake = true;
                        yield break;

                    }
                }

                yield return new AutopilotTaskResult
                {
                    Waypoint = (wayPoints.Count > 1 ? wayPointsInfo.Current?.Name : autopilot.CurrentWaypoint?.Name) ?? "None",
                    Direction = direction,
                    Steer = (float)directionAngle,
                    Mode = autopilot.FlightMode,
                    WaypointCount = wayPoints.Count
                };
            }
        }

        double AvoidCollision(IMyTerminalBlock autopilot, IMySensorBlock sensor, Vector3D currentPosition)
        {
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
                    return Enumerable.Range(0, 8).Select(i => v.GetCorner(i)).Max(i => Vector3D.DistanceSquared(i, currentPosition));
                }
            }

            return 0;
        }

        IEnumerable RecordPathTask()
        {
            if (Recording) yield break;
            var autopilot = gridProps.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            var counter = 0;
            Recording = true;
            while (Recording)
            {
                var current = autopilot.GetPosition();
                if (Vector3D.Distance(current, wayPoints.LastOrDefault().Coords) > 15)
                {
                    wayPoints.Add(new MyWaypointInfo($"Waypoint-#{counter++}", current));
                }
                yield return null;
            }
            autopilot.CustomData = string.Join("\n", wayPoints);
        }

        IEnumerable ImportPathTask()
        {
            var autopilot = gridProps.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            MyWaypointInfo.FindAll(autopilot.CustomData, wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ReversePathTask()
        {
            var autopilot = gridProps.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.Reverse();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ExportPathTask()
        {
            var autopilot = gridProps.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.CustomData = string.Join("\n", wayPoints);
            yield return null;
        }
    }
}