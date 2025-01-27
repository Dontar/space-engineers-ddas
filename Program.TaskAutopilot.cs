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
using VRageMath;
using VRageRender;
using VRageRender.Animations;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        struct AutopilotTaskResult
        {
            public Vector3D Direction;
            public float Steer;
            public string Waypoint;
        }

        IEnumerable AutopilotTask()
        {
            var ini = Config;
            var autopilot = gridProps.MainController as IMyRemoteControl;
            if (autopilot == null) yield break;
            if (!autopilot.IsAutoPilotEnabled) yield break;
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
                if (!gridProps.Cruise)
                {
                    TaskManager.AddTaskOnce(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled));
                }
                else
                    gridProps.CruiseSpeed = autopilot.SpeedLimit * 3.6f;


                if (gridProps.UpDown > 0)
                {
                    autopilot.SetAutoPilotEnabled(false);
                    yield break;
                }

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
                    Steer = (float)directionAngle
                };
            }
        }

        IEnumerable AutopilotAITask()
        {
            var ini = Config;
            var autopilot = Memo.Of(() => Util.GetBlocks<IMyFlightMovementBlock>().FirstOrDefault(), "AI", Memo.Refs(gridProps.Mass.BaseMass));
            if (autopilot == null || !autopilot.IsAutoPilotEnabled) yield break;
            var sensor = Memo.Of(() => Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, ini["IgnoreTag"].ToString())).FirstOrDefault(), "sensor", Memo.Refs(gridProps.Mass.BaseMass));

            var wayPoints = new List<IMyAutopilotWaypoint>();
            autopilot.GetWaypoints(wayPoints);

            while (ini.Equals(Config) && autopilot.IsAutoPilotEnabled)
            {
                if (autopilot.CurrentWaypoint == null)
                {
                    gridProps.MainController.HandBrake = true;
                    yield break;
                }
                else
                {
                    gridProps.MainController.HandBrake = false;
                }

                if (!gridProps.Cruise)
                {
                    TaskManager.AddTaskOnce(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled || !gridProps.MainController.HandBrake));
                }
                else
                    gridProps.CruiseSpeed = autopilot.SpeedLimit * 3.6f;


                if (gridProps.UpDown > 0)
                {
                    autopilot.SetValueBool("ActivateBehavior", false);
                    yield break;
                }

                var currentPosition = autopilot.GetPosition();
                var destinationVector = autopilot.CurrentWaypoint.Matrix.Translation - currentPosition;

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

                if (autopilot.CurrentWaypoint.Matrix.Translation == wayPoints.LastOrDefault().Matrix.Translation)
                {
                    gridProps.MainController.HandBrake = direction.Length() < autopilot.CubeGrid.WorldVolume.Radius;
                }

                yield return new AutopilotTaskResult
                {
                    Waypoint = autopilot.CurrentWaypoint.Name,
                    Direction = direction,
                    Steer = (float)directionAngle
                };
            }
        }

        IEnumerable RecordPathTask()
        {
            if (gridProps.Recording) yield break;
            var autopilot = gridProps.MainController as IMyRemoteControl;
            var wayPoints = new List<MyWaypointInfo>();
            var counter = 0;
            gridProps.Recording = true;
            while (gridProps.Recording)
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