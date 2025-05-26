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
            var controller = Controllers.MainController is IMyRemoteControl ? Controllers.MainController : null;
            var autopilot = Autopilot.FromBlock(Memo.Of(() => Util.GetBlocks<IMyFlightMovementBlock>().FirstOrDefault() as IMyTerminalBlock, "AI", Memo.Refs(Mass.BaseMass)) ?? controller);

            if (autopilot == null || !autopilot.IsAutoPilotEnabled) yield break;

            var sensor = Memo.Of(() => Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, _ignoreTag)).FirstOrDefault(), "sensor", Memo.Refs(Mass.BaseMass));

            var wayPoints = autopilot.Waypoints;
            int wayPointsCount = wayPoints.Count();
            var wayPointsInfo = wayPoints.GetEnumerator();
            if (wayPointsCount > 1)
            {
                var closest = wayPoints.OrderBy(w => Vector3D.Distance(w.Coords, autopilot.GetPosition())).FirstOrDefault().Coords;
                wayPointsInfo = closest.Equals(wayPoints.Last()) ? wayPointsInfo : wayPoints.SkipWhile(w => !w.Equals(closest)).Skip(1).GetEnumerator();
            }
            wayPointsInfo.MoveNext();

            while (autopilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = wayPointsCount > 1 ? wayPointsInfo.Current : autopilot.CurrentWaypoint;

                if (currentWaypoint.Equals(MyWaypointInfo.Empty))
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


                if (UpDown > 0)
                {
                    autopilot.SetAutoPilotEnabled(false);
                    yield break;
                }

                var currentPosition = autopilot.GetPosition();
                var destinationVector = currentWaypoint.Coords - currentPosition + AvoidCollision(autopilot.Block, sensor, currentPosition);

                var T = MatrixD.Transpose(autopilot.WorldMatrix);
                var direction = Vector3D.TransformNormal(destinationVector, T); direction.Y = 0;
                var directionAngle = Util.ToAzimuth(direction);

                var isWayPointReached = direction.Length() < autopilot.Block.CubeGrid.WorldVolume.Radius;

                if (isWayPointReached)
                {
                    if (wayPointsCount > 1)
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
                                    var distanceFirst = Vector3D.Distance(wayPoints.First().Coords, currentPosition);
                                    var distanceLast = Vector3D.Distance(wayPoints.Last().Coords, currentPosition);
                                    wayPointsInfo = (distanceLast < distanceFirst ? wayPoints.Select(w => w).Reverse() : wayPoints).Skip(1).GetEnumerator();
                                    wayPointsInfo.MoveNext();
                                    break;
                                default:
                                    controller.HandBrake = true;
                                    autopilot.SetAutoPilotEnabled(false);
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
                    Waypoint = (wayPointsCount > 1 ? wayPointsInfo.Current.Name : autopilot.CurrentWaypoint.Name) ?? "None",
                    Direction = direction,
                    Steer = (float)MathHelper.Clamp(directionAngle, -1, 1),
                    Mode = autopilot.FlightMode,
                    WaypointCount = wayPointsCount
                };
            }
        }

        double AvoidCollision(IMyTerminalBlock autopilot, IMySensorBlock sensor, Vector3D currentPosition)
        {
            if (autopilot.GetValueBool("CollisionAvoidance") && sensor != null)
            {
                var detectionBox = new BoundingBoxD(
                    currentPosition + autopilot.WorldMatrix.Forward * 15 - new Vector3D(5, 5, 5),
                    currentPosition + autopilot.WorldMatrix.Forward * 15 + new Vector3D(5, 5, 5)
                );

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

        class Autopilot
        {
            public Autopilot(IMyTerminalBlock block)
            {
                Block = block;
            }

            public IMyTerminalBlock Block { get; }
            public bool IsAutoPilotEnabled => Block is IMyRemoteControl ? (Block as IMyRemoteControl).IsAutoPilotEnabled : (Block as IMyFlightMovementBlock).IsAutoPilotEnabled;
            public IEnumerable<MyWaypointInfo> Waypoints
            {
                get
                {
                    if (Block is IMyRemoteControl)
                    {
                        var autopilot = Block as IMyRemoteControl;
                        var waypoints = new List<MyWaypointInfo>();
                        autopilot.GetWaypointInfo(waypoints);
                        return waypoints;
                    }
                    else
                    {
                        var autopilot = Block as IMyFlightMovementBlock;
                        var waypoints = new List<IMyAutopilotWaypoint>();
                        autopilot.GetWaypoints(waypoints);
                        return waypoints.Select(w => new MyWaypointInfo(w.Name, w.Matrix.Translation));
                    }
                }
            }
            public MyWaypointInfo CurrentWaypoint
            {
                get
                {
                    if (Block is IMyRemoteControl)
                    {
                        var autopilot = Block as IMyRemoteControl;
                        return autopilot.CurrentWaypoint;
                    }
                    else
                    {
                        var autopilot = Block as IMyFlightMovementBlock;
                        if (autopilot.CurrentWaypoint == null)
                            return MyWaypointInfo.Empty;
                        return new MyWaypointInfo(autopilot.CurrentWaypoint.Name, autopilot.CurrentWaypoint.Matrix.Translation);
                    }
                }
            }
            public FlightMode FlightMode => Block is IMyRemoteControl ? (Block as IMyRemoteControl).FlightMode : (Block as IMyFlightMovementBlock).FlightMode;
            public float SpeedLimit => Block is IMyRemoteControl ? (Block as IMyRemoteControl).SpeedLimit : (Block as IMyFlightMovementBlock).SpeedLimit;
            public MatrixD WorldMatrix => Block.WorldMatrix;
            public Vector3D GetPosition()
            {
                return Block.GetPosition();
            }
            public void SetAutoPilotEnabled(bool enabled)
            {
                Block.SetValueBool(Block is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot", enabled);
            }
            public static Autopilot FromBlock(IMyTerminalBlock block)
            {
                if (block != null)
                    return new Autopilot(block);
                else
                    return null;
            }
        }

        IEnumerable RecordPathTask()
        {
            if (Recording) yield break;
            var autopilot = Controllers.MainController;
            var minDistance = autopilot.CubeGrid.WorldVolume.Radius * 2;
            var wayPoints = new List<MyWaypointInfo>();
            var counter = 0;
            Recording = true;
            while (Recording)
            {
                var current = autopilot.GetPosition();
                if (Vector3D.Distance(current, wayPoints.LastOrDefault().Coords) > minDistance)
                {
                    wayPoints.Add(new MyWaypointInfo($"Waypoint-#{counter++}", current));
                }
                yield return null;
            }
            autopilot.CustomData = string.Join("\n", wayPoints);
            TaskManager.AddTaskOnce(ImportPathTask());
        }

        IEnumerable ImportPathTask()
        {
            var autopilot = Controllers.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            MyWaypointInfo.FindAll(autopilot.CustomData, wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ReversePathTask()
        {
            var autopilot = Controllers.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.ClearWaypoints();
            wayPoints.Reverse();
            wayPoints.ForEach(w => autopilot.AddWaypoint(w));
            yield return null;
        }

        IEnumerable ExportPathTask()
        {
            var autopilot = Controllers.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            autopilot.GetWaypointInfo(wayPoints);
            autopilot.CustomData = string.Join("\n", wayPoints);
            yield return null;
        }
    }
}