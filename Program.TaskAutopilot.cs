using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;
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
            public float TargetSpeed;
            public void Reset()
            {
                Waypoint = "None";
                Direction = Vector3D.Zero;
                Steer = 0;
                Mode = FlightMode.Patrol;
                WaypointCount = 0;
                TargetSpeed = 0;
            }
        }

        class Timer
        {
            private TimeSpan interval;
            private TimeSpan timeSinceLastTrigger = TimeSpan.Zero;

            public bool Active = false;

            public Timer(float intervalSeconds)
            {
                interval = TimeSpan.FromSeconds(intervalSeconds);
            }

            public bool Update(TimeSpan timeSinceLastRun)
            {
                timeSinceLastTrigger += timeSinceLastRun;
                if (timeSinceLastTrigger >= interval)
                {
                    timeSinceLastTrigger = TimeSpan.Zero;
                    Active = false;
                    return true;
                }
                Active = true;
                return false;
            }

            public void Reset()
            {
                timeSinceLastTrigger = TimeSpan.Zero;
            }
        }

        struct TimedItem<T>
        {
            public T Item;
            public TimeSpan TimeAdded;
            public TimedItem(T item)
            {
                Item = item;
                TimeAdded = DateTime.Now.TimeOfDay;
            }
        }

        class UniqueTimedQueue : Queue<TimedItem<MyWaypointInfo>>
        {
            private int _capacity = 10;

            public UniqueTimedQueue() : base() { }

            public UniqueTimedQueue(int capacity) : base(capacity)
            {
                _capacity = capacity;
            }

            public void Enqueue(MyWaypointInfo item, Func<MyWaypointInfo, bool> compare)
            {
                if (compare(item))
                {
                    Enqueue(new TimedItem<MyWaypointInfo>(item));
                }
                if (Count > _capacity)
                {
                    Dequeue();
                }
            }

            public MyWaypointInfo TryDequeue()
            {
                if (Count > 0)
                {
                    return Dequeue().Item;
                }
                return default(MyWaypointInfo);
            }


            public MyWaypointInfo TryPeek()
            {
                if (Count > 0)
                {
                    return Peek().Item;
                }
                return default(MyWaypointInfo);
            }
        }

        AutopilotTaskResult AutopilotResult = new AutopilotTaskResult();

        Timer EmergencySteerTimer = new Timer(3);

        IEnumerable AutopilotTask()
        {
            var controller = Controllers.MainController;
            var autopilot = Pilot;
            var sensor = Sensor;

            EmergencySteerTimer.Reset();
            AutopilotResult.Reset();

            foreach (var g in Gyros) g.Enabled = true;

            if (!autopilot.IsAutoPilotEnabled) yield break;

            foreach (var g in Gyros) g.Enabled = false;

            bool isRoute = autopilot.Waypoints.Count() > 1;

            var wayPointReachThreshold = autopilot.Block.CubeGrid.WorldVolume.Radius + 2;
            var wayPointCloseThreshold = wayPointReachThreshold + 40;

            // if (currentWaypoint.Equals(MyWaypointInfo.Empty))
            // {
            //     controller.HandBrake = true;
            //     yield break;
            // }
            // else
            //     controller.HandBrake = false;


            if (isRoute)
            {
                foreach (var _ in FollowRoute(controller, autopilot, sensor, wayPointReachThreshold))
                {
                    yield return null;
                }
            }
            else
            {
                foreach (var _ in FollowTarget(controller, autopilot, sensor, wayPointReachThreshold, wayPointCloseThreshold))
                {
                    yield return null;
                }
            }
            AutopilotResult.Reset();
            foreach (var g in Gyros) g.Enabled = true;
        }

        private IEnumerable FollowTarget(IMyShipController controller, Autopilot autopilot, IMySensorBlock sensor, double wayPointReachThreshold, double wayPointCloseThreshold)
        {
            var queue = new UniqueTimedQueue(5);
            AutopilotResult.Mode = autopilot.FlightMode;
            while (autopilot.IsAutoPilotEnabled)
            {
                queue.Enqueue(autopilot.CurrentWaypoint, i => queue.Count == 0 || !queue.LastOrDefault().Item.Equals(i, 20));
                var currentWaypoint = queue.TryPeek();

                SetCruiseControl(controller, autopilot);

                if (UpDown > 0)
                {
                    autopilot.SetAutoPilotEnabled(false);
                    yield break;
                }
                if (CheckNoEmergencySteer())
                {
                    Vector3D currentPosition, direction;
                    double directionAngle, distance;
                    CalcSteer(autopilot, sensor, currentWaypoint, out currentPosition, out direction, out directionAngle, out distance);

                    AutopilotResult.Waypoint = queue.TryPeek().Name ?? "None";
                    AutopilotResult.Direction = direction;
                    AutopilotResult.Steer = (float)MathHelper.Clamp(directionAngle, -1, 1);
                    AutopilotResult.WaypointCount = queue.Count;

                    var distanceToTarget = Math.Abs(Vector3D.Distance(currentPosition, queue.Last().Item.Coords));
                    if (distanceToTarget < wayPointCloseThreshold)
                    {
                        var matchedSpeed = MatchSpeed(queue);
                        AutopilotResult.TargetSpeed = (float)matchedSpeed * 3.6f;
                        CruiseSpeed = (float)MathHelper.Clamp(matchedSpeed * 3.6, 5, autopilot.SpeedLimit * 3.6);
                    }

                    var distanceModifier = Util.NormalizeValue(Speed * 3.6, 10, 180, 0, 50);
                    var threshold = wayPointReachThreshold + distanceModifier;

                    if (distanceToTarget < threshold) controller.HandBrake = true;

                    if (distance < threshold) queue.TryDequeue();
                }
                yield return null;
            }
        }

        private IEnumerable FollowRoute(IMyShipController controller, Autopilot autopilot, IMySensorBlock sensor, double wayPointReachThreshold)
        {
            var wayPoints = autopilot.Waypoints;
            var wayPointsIter = wayPoints.GetEnumerator();

            var currentPos = autopilot.GetPosition();
            var closest = wayPoints.OrderBy(w => Vector3D.Distance(w.Coords, currentPos)).First();
            wayPointsIter =
                closest.Equals(wayPoints.Last())
                ? wayPointsIter
                : wayPoints.SkipWhile(w => !w.Equals(closest)).Skip(1).GetEnumerator();

            wayPointsIter.MoveNext();
            AutopilotResult.Mode = autopilot.FlightMode;
            AutopilotResult.WaypointCount = wayPoints.Count();

            while (autopilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = wayPointsIter.Current;
                SetCruiseControl(controller, autopilot);
                if (UpDown > 0)
                {
                    autopilot.SetAutoPilotEnabled(false);
                    yield break;
                }
                if (CheckNoEmergencySteer())
                {
                    Vector3D currentPosition, direction;
                    double directionAngle, distance;
                    CalcSteer(autopilot, sensor, currentWaypoint, out currentPosition, out direction, out directionAngle, out distance);
                    var distanceModifier = Util.NormalizeValue(Speed * 3.6, 10, 180, 0, 50);

                    AutopilotResult.Waypoint = wayPointsIter.Current.Name ?? "None";
                    AutopilotResult.Direction = direction;
                    AutopilotResult.Steer = (float)MathHelper.Clamp(directionAngle, -1, 1);

                    if (distance < wayPointReachThreshold + distanceModifier)
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
                                    autopilot.SetAutoPilotEnabled(false);
                                    controller.HandBrake = true;
                                    yield break;
                            }
                        }
                    }
                }
                yield return null;
            }
        }

        private bool CheckNoEmergencySteer()
        {
            if (LeftRight != 0)
            {
                EmergencySteerTimer.Reset();
                EmergencySteerTimer.Active = true;
            }
            if (EmergencySteerTimer.Active)
            {
                EmergencySteerTimer.Update(TaskManager.CurrentTaskLastRun);
                AutopilotResult.Steer = -LeftRight;
                return false;
            }
            return true;
        }

        private void CalcSteer(
            Autopilot autopilot,
            IMySensorBlock sensor,
            MyWaypointInfo currentWaypoint,
            out Vector3D currentPosition,
            out Vector3D direction,
            out double directionAngle,
            out double distance
        )
        {
            currentPosition = autopilot.GetPosition();
            var destinationVector = currentWaypoint.Coords - currentPosition + AvoidCollision(autopilot.Block, sensor, currentPosition);

            var T = MatrixD.Transpose(autopilot.WorldMatrix);
            direction = Vector3D.TransformNormal(destinationVector, T);
            directionAngle = Util.ToAzimuth(direction);
            distance = direction.Length();
        }

        private void SetCruiseControl(IMyShipController controller, Autopilot autopilot)
        {
            if (!Cruise && !controller.HandBrake)
            {
                TaskManager.AddTaskOnce(CruiseTask(autopilot.SpeedLimit * 3.6f, () => autopilot.IsAutoPilotEnabled && !controller.HandBrake));
            }
            else
                CruiseSpeed = autopilot.SpeedLimit * 3.6f;
        }

        private double MatchSpeed(UniqueTimedQueue queue)
        {
            if (queue.Count > 1)
            {
                var list = queue.ToArray();
                // Calculate average speed from list
                double totalDistance = 0;
                double totalTime = 0;
                for (int i = 1; i < list.Length; i++)
                {
                    var prev = list[i - 1];
                    var curr = list[i];
                    double distance = Math.Abs(Vector3D.Distance(prev.Item.Coords, curr.Item.Coords));
                    double time = Math.Abs((curr.TimeAdded - prev.TimeAdded).TotalSeconds);
                    if (time > 0)
                    {
                        totalDistance += distance;
                        totalTime += time;
                    }
                }
                return totalTime > 0 ? totalDistance / totalTime : 0;
            }
            return 0;
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
            public Autopilot(IMyTerminalBlock[] blocks)
            {
                Blocks = blocks;
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
