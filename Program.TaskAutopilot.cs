using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        bool Recording = false;
        IMyTerminalBlock AutopilotBlock;
        IMySensorBlock Sensor;
        IMyRemoteControl Remote;
        Autopilot Pilot;

        void InitAutopilot()
        {
            AutopilotBlock = Util.GetBlocks<IMyFlightMovementBlock>().FirstOrDefault();
            Sensor = Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, _ignoreTag)).FirstOrDefault();
            Pilot = Autopilot.FromBlock(new IMyTerminalBlock[] { AutopilotBlock, Controllers.MainController });
            Remote = Controllers.MainController is IMyRemoteControl ? (IMyRemoteControl)Controllers.MainController : null;
        }

        struct AutopilotTaskResult
        {
            public Vector3D Direction;
            public float Steer;
            public string Waypoint;
            public int WaypointCount;
            public FlightMode Mode;
            public void Reset()
            {
                Waypoint = "None";
                Direction = Vector3D.Zero;
                Steer = 0;
                Mode = FlightMode.Patrol;
                WaypointCount = 0;

            }
        }

        AutopilotTaskResult AutopilotResult = new AutopilotTaskResult();

        Timer EmergencySteerTimer = new Timer(3);

        IEnumerable AutopilotTask()
        {
            var controller = Remote;
            var autopilot = Pilot;

            if (autopilot == null || !autopilot.IsAutoPilotEnabled) yield break;

            var sensor = Sensor;

            var wayPoints = autopilot.Waypoints;
            bool hasManyWaypoints = wayPoints.Count() > 1;
            var wayPointsIter = wayPoints.GetEnumerator();

            if (hasManyWaypoints)
            {
                var closest = wayPoints.OrderBy(w => Vector3D.Distance(w.Coords, autopilot.GetPosition())).FirstOrDefault().Coords;
                wayPointsIter = closest.Equals(wayPoints.Last()) ? wayPointsIter : wayPoints.SkipWhile(w => !w.Equals(closest)).Skip(1).GetEnumerator();
            }
            wayPointsIter.MoveNext();

            var previousTargetPos = Vector3D.Zero;

            while (autopilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = hasManyWaypoints ? wayPointsIter.Current : autopilot.CurrentWaypoint;

                if (currentWaypoint.Equals(MyWaypointInfo.Empty))
                {
                    controller.HandBrake = true;
                    EmergencySteerTimer.Reset();
                    AutopilotResult.Reset();
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
                    EmergencySteerTimer.Reset();
                    AutopilotResult.Reset();
                    yield break;
                }

                if (LeftRight != 0)
                {
                    EmergencySteerTimer.Reset();
                    EmergencySteerTimer.Active = true;
                }

                if (EmergencySteerTimer.Active)
                {
                    EmergencySteerTimer.Update(TaskManager.CurrentTaskLastRun);
                    AutopilotResult.Steer = -LeftRight;
                }
                else
                {
                    var currentPosition = autopilot.GetPosition();
                    var destinationVector = currentWaypoint.Coords - currentPosition + AvoidCollision(autopilot.Block, sensor, currentPosition);

                    var T = MatrixD.Transpose(autopilot.WorldMatrix);
                    var direction = Vector3D.TransformNormal(destinationVector, T);
                    var directionAngle = Util.ToAzimuth(direction);


                    if (direction.Length() < autopilot.MaxDistance)
                    {
                        var targetSpeed = CalcSpeed(previousTargetPos, currentWaypoint.Coords, TaskManager.CurrentTaskLastRun);
                        if (targetSpeed > 0)
                        {
                            CruiseSpeed = (float)Math.Min(CruiseSpeed, targetSpeed * 3.6f);
                        }
                    }

                    if (direction.Length() < autopilot.MinDistance)
                    {
                        if (!wayPointsIter.MoveNext())
                        {
                            switch (autopilot.FlightMode)
                            {
                                case FlightMode.Circle:
                                    wayPointsIter = wayPoints.GetEnumerator();
                                    wayPointsIter.MoveNext();
                                    continue;
                                case FlightMode.Patrol:
                                    var distanceFirst = Vector3D.Distance(wayPoints.First().Coords, currentPosition);
                                    var distanceLast = Vector3D.Distance(wayPoints.Last().Coords, currentPosition);
                                    wayPointsIter = (distanceLast < distanceFirst ? wayPoints.Select(w => w).Reverse() : wayPoints).Skip(1).GetEnumerator();
                                    wayPointsIter.MoveNext();
                                    continue;
                                default:
                                    controller.HandBrake = true;
                                    EmergencySteerTimer.Reset();
                                    if (!autopilot.IsFollowing)
                                    {
                                        AutopilotResult.Reset();
                                        autopilot.SetAutoPilotEnabled(false);
                                        yield break;
                                    }
                                    break;
                            }
                        }
                    }

                    previousTargetPos = currentWaypoint.Coords;

                    AutopilotResult.Waypoint = (hasManyWaypoints ? wayPointsIter.Current.Name : autopilot.CurrentWaypoint.Name) ?? "None";
                    AutopilotResult.Direction = direction;
                    AutopilotResult.Steer = (float)MathHelper.Clamp(directionAngle, -1, 1);
                    AutopilotResult.Mode = autopilot.FlightMode;
                    AutopilotResult.WaypointCount = wayPoints.Count();
                }
                yield return null;
            }
            AutopilotResult.Reset();
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

        double CalcSpeed(Vector3D previousTargetPos, Vector3D currentTargetPos, TimeSpan time)
        {
            return Vector3D.Distance(previousTargetPos, currentTargetPos) / time.TotalSeconds;
        }

        class Autopilot
        {
            private readonly IEnumerable<IMyBasicMissionBlock> TaskBlocks = null;
            public Autopilot(IMyTerminalBlock[] blocks)
            {
                Blocks = blocks;
                    TaskBlocks = Util.GetBlocks<IMyBasicMissionBlock>();
                }

            public IMyTerminalBlock[] Blocks;

            public IMyTerminalBlock Block => Blocks.FirstOrDefault(b =>
                b != null &&
                ((b is IMyRemoteControl && ((IMyRemoteControl)b).IsAutoPilotEnabled) ||
                 (b is IMyFlightMovementBlock && ((IMyFlightMovementBlock)b).IsAutoPilotEnabled))
            );

            public bool IsAutoPilotEnabled => Block != null;

            public IEnumerable<MyWaypointInfo> Waypoints
            {
                get
                {
                    if (Block is IMyRemoteControl)
                    {
                        var waypoints = new List<MyWaypointInfo>();
                        (Block as IMyRemoteControl).GetWaypointInfo(waypoints);
                        return waypoints;
                    }
                    var aiWaypoints = new List<IMyAutopilotWaypoint>();
                    (Block as IMyFlightMovementBlock).GetWaypoints(aiWaypoints);
                    return aiWaypoints.Select(w => new MyWaypointInfo(w.Name, w.Matrix.Translation));
                }
            }
            public MyWaypointInfo CurrentWaypoint
            {
                get
                {
                    if (Block is IMyRemoteControl) return (Block as IMyRemoteControl).CurrentWaypoint;
                        var autopilot = Block as IMyFlightMovementBlock;
                        if (autopilot.CurrentWaypoint == null)
                            return MyWaypointInfo.Empty;
                        return new MyWaypointInfo(autopilot.CurrentWaypoint.Name, autopilot.CurrentWaypoint.Matrix.Translation);
                }
            }
            public FlightMode FlightMode => Block is IMyRemoteControl ? (Block as IMyRemoteControl).FlightMode : (Block as IMyFlightMovementBlock).FlightMode;
            public float SpeedLimit => Block is IMyRemoteControl ? (Block as IMyRemoteControl).SpeedLimit : (Block as IMyFlightMovementBlock).SpeedLimit;
            public MatrixD WorldMatrix => Block.WorldMatrix;
            public bool IsFollowing
            {
                get
                {
                    if (Block is IMyRemoteControl || TaskBlocks.Count() < 1) return false;

                    var activeTask = TaskBlocks.FirstOrDefault(t => t.GetValueBool("ActivateBehavior"));
                    if (activeTask == null) return false;

                    return activeTask.SelectedMissionId < 3;
                }
            }
            public double MinDistance
            {
                get
                {
                    var defaultDistance = Block.CubeGrid.WorldVolume.Radius + 2;
                    if (Block is IMyRemoteControl || TaskBlocks.Count() < 1) return defaultDistance;

                    var activeTask = TaskBlocks.FirstOrDefault(t => t.GetValueBool("ActivateBehavior"));
                    if (activeTask == null) return defaultDistance;

                    switch (activeTask.SelectedMissionId)
                    {
                        case 1:
                            return activeTask.GetValueFloat("FollowDistance");
                        case 2:
                            return activeTask.GetValueFloat("FollowHomeMinRange");
                        default:
                            return defaultDistance;
                    }
                }
            }
            public double MaxDistance
            {
                get
                {
                    var defaultDistance = Block.CubeGrid.WorldVolume.Radius + 1;
                    if (Block is IMyRemoteControl || TaskBlocks.Count() < 1) return defaultDistance;

                    var activeTask = TaskBlocks.FirstOrDefault(t => t.GetValueBool("ActivateBehavior"));
                    if (activeTask == null) return defaultDistance;

                    switch (activeTask.SelectedMissionId)
                    {
                        case 1:
                            return activeTask.GetValueFloat("FollowDistance") + 20;
                        case 2:
                            return activeTask.GetValueFloat("FollowHomeMaxRange");
                        default:
                            return defaultDistance;
                    }
                }
            }
            public Vector3D GetPosition()
            {
                return Block?.GetPosition() ?? Vector3D.Zero;
            }
            public void SetAutoPilotEnabled(bool enabled)
            {
                Block?.SetValueBool(Block is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot", enabled);
            }
            public static Autopilot FromBlock(IMyTerminalBlock[] blocks)
            {
                return new Autopilot(blocks);
            }
        }

        Random Rand = new Random((int)DateTime.Now.Ticks & 0x0000FFFF);
        string GenerateRandomStr()
        {
            var suffix = "";
            for (int i = 0; i < 3; i++)
            {
                suffix += (char)('A' + Rand.Next(0, 26));
            }
            return suffix;
        }

        IEnumerable RecordRouteTask()
        {
            if (Remote == null || Recording) yield break;
            var minDistance = Remote.CubeGrid.WorldVolume.Radius * 2;
            var wayPoints = new List<MyWaypointInfo>();
            var counter = 0;
            Recording = true;

            while (Recording)
            {
                var current = Remote.GetPosition();
                if (Vector3D.Distance(current, wayPoints.LastOrDefault().Coords) > minDistance)
                {
                    wayPoints.Add(new MyWaypointInfo($"Waypoint-#{counter++}", current));
                }
                yield return null;
            }

            var routeList = new MyIni();
            routeList.TryParse(Remote.CustomData);

            var newRouteName = $"Route-#{GenerateRandomStr()}";
            while (routeList.ContainsSection(newRouteName))
            {
                newRouteName = $"Route-#{GenerateRandomStr()}";
            }

            for (int i = 0; i < wayPoints.Count; i++)
            {
                routeList.Set(newRouteName, $"#{i}", wayPoints[i].ToString());
            }

            Remote.CustomData = routeList.ToString();
        }

        void PlayRoute(string route, bool reverse, bool play = false)
        {
            if (Remote == null) return;
            var routeList = new MyIni();
            routeList.TryParse(Remote.CustomData);

            var wayPoints = new List<MyIniKey>();
            routeList.GetKeys(route, wayPoints);
            Remote.ClearWaypoints();
            IEnumerable<MyIniKey> list = reverse ? wayPoints.AsEnumerable().Reverse() : wayPoints;
            foreach (var w in list)
            {
                MyWaypointInfo wayPoint;
                if (MyWaypointInfo.TryParse(routeList.Get(w).ToString(), out wayPoint))
                {
                    Remote.AddWaypoint(wayPoint);
                }
            }
            Remote.SetAutoPilotEnabled(play);
        }

        void SaveRoute()
        {
            if (Remote == null) return;
            var wayPoints = new List<MyWaypointInfo>();
            Remote.GetWaypointInfo(wayPoints);

            var routeList = new MyIni();
            routeList.TryParse(Remote.CustomData);

            var newRouteName = $"Route-#{GenerateRandomStr()}";
            while (routeList.ContainsSection(newRouteName))
            {
                newRouteName = $"Route-#{GenerateRandomStr()}";
            }

            for (int i = 0; i < wayPoints.Count; i++)
            {
                routeList.Set(newRouteName, $"#{i}", wayPoints[i].ToString());
            }

            Remote.CustomData = routeList.ToString();
        }

        bool ProcessAICommands(string args)
        {
            var commandLine = args.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (commandLine.Length < 1) return false;

            var command = commandLine[0].ToLower();

            switch (command)
            {
                case "record":
                    if (!Recording)
                        TaskManager.AddTaskOnce(RecordRouteTask(), 1.7f);
                    else
                        Recording = false;
                    return true;
                case "load":
                case "play":
                    var route = commandLine.Last();
                    var reverse = commandLine.Any(s => s.ToLower() == "reverse");
                    var play = command == "play";
                    PlayRoute(route, reverse, play);
                    return true;
                case "save":
                    SaveRoute();
                    return true;
            }
            return false;
        }
    }
}
