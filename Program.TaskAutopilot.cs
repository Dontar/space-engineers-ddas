using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
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
        IMyBasicMissionBlock Basic;

        float WayPointReachThreshold;
        float WayPointCloseThreshold;

        void InitAutopilot()
        {
            AutopilotBlock = Util.GetBlocks<IMyFlightMovementBlock>().FirstOrDefault();
            Remote = Controllers.MainController is IMyRemoteControl ? (IMyRemoteControl)Controllers.MainController : null;
            Pilot = Autopilot.FromBlock(new IMyTerminalBlock[] { AutopilotBlock, Remote });
            Sensor = Util.GetBlocks<IMySensorBlock>(b => Util.IsNotIgnored(b, _ignoreTag)).FirstOrDefault();
            Basic = Util.GetBlocks<IMyBasicMissionBlock>(b => Util.IsNotIgnored(b, _ignoreTag)).FirstOrDefault();

            WayPointReachThreshold = (float)Me.CubeGrid.WorldVolume.Radius + 2;
            WayPointCloseThreshold = WayPointReachThreshold + 40;

            if (Sensor != null)
            {
                SetupSensor();
            }
        }

        struct AutopilotTaskResult
        {
            public float Steer;
            public string Waypoint;
            public int WaypointCount;
            public string Mode;
            public double Distance;
            public void Reset()
            {
                Waypoint = "None";
                Steer = 0;
                Mode = "Idle";
                WaypointCount = 0;
                Distance = 0;
            }
        }

        AutopilotTaskResult AutopilotResult = new AutopilotTaskResult();

        Timer EmergencySteerTimer = new Timer(3);

        IEnumerable AutopilotTask()
        {
            EmergencySteerTimer.Reset();
            AutopilotResult.Reset();

            if (!Pilot.IsAutoPilotEnabled) yield break;

            bool isRoute = Pilot.Waypoints.Count() > 1;

            var basic = Basic != null && Basic.GetValueBool("ActivateBehavior") && Basic.SelectedMissionId == 2;

            var routine = isRoute
                ? FollowRoute()
                : basic
                    ? FollowTarget()
                    : TrackTarget();

            foreach (var _ in routine)
            {
                yield return null;
            }
        }

        IEnumerable FollowTarget()
        {
            var controller = Controllers.MainController;
            var queue = new UniqueTimedQueue(5);
            AutopilotResult.Mode = "Follow";
            while (Pilot.IsAutoPilotEnabled)
            {
                queue.Enqueue(Pilot.CurrentWaypoint, i => queue.Count == 0 || !queue.LastOrDefault().Item.Equals(i, 20));
                var currentWaypoint = queue.TryPeek();

                SetCruiseControl();

                if (CheckEmergencyStop()) yield break;

                if (CheckNoEmergencySteer())
                {
                    Vector3D currentPosition, direction;
                    double directionAngle, distance;
                    CalcSteer(currentWaypoint, out currentPosition, out direction, out directionAngle, out distance);

                    AutopilotResult.Waypoint = queue.TryPeek().Name ?? "None";
                    AutopilotResult.Steer = (float)MathHelper.Clamp(directionAngle, -1, 1);
                    AutopilotResult.WaypointCount = queue.Count;
                    AutopilotResult.Distance = distance;

                    var distanceToTarget = Math.Abs(Vector3D.Distance(currentPosition, queue.Last().Item.Coords));
                    if (distanceToTarget < WayPointCloseThreshold)
                    {
                        var matchedSpeed = queue.CalcSpeed();
                        CruiseSpeed = (float)MathHelper.Clamp(matchedSpeed * 3.6, 10, Pilot.SpeedLimit * 3.6);
                    }

                    var threshold = WayPointReachThreshold + Speed * 3.6 * 50 / 180;

                    controller.HandBrake = distanceToTarget < threshold;

                    if (distance < threshold) queue.TryDequeue();
                }
                yield return null;
            }
        }

        IEnumerable TrackTarget()
        {
            var controller = Controllers.MainController;
            AutopilotResult.Mode = "Track";
            while (Pilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = Pilot.CurrentWaypoint;

                controller.HandBrake = currentWaypoint.Equals(MyWaypointInfo.Empty);

                SetCruiseControl();

                if (CheckEmergencyStop()) yield break;

                if (CheckNoEmergencySteer())
                {
                    Vector3D currentPosition, direction;
                    double directionAngle, distance;
                    CalcSteer(currentWaypoint, out currentPosition, out direction, out directionAngle, out distance);

                    AutopilotResult.Waypoint = currentWaypoint.Name ?? "None";
                    AutopilotResult.Steer = (float)directionAngle;
                    AutopilotResult.WaypointCount = 1;
                    AutopilotResult.Distance = distance;

                    var threshold = WayPointReachThreshold + Speed * 3.6 * 50 / 180;

                    controller.HandBrake = distance < threshold;
                }
                yield return null;
            }
        }

        IEnumerable FollowRoute()
        {
            var controller = Controllers.MainController;
            var wayPoints = Pilot.Waypoints;
            var wayPointsIter = wayPoints.GetEnumerator();

            var currentPos = Pilot.GetPosition();
            var closest = wayPoints.OrderBy(w => Vector3D.Distance(w.Coords, currentPos)).First();
            wayPointsIter =
                closest.Equals(wayPoints.Last())
                ? wayPointsIter
                : wayPoints.SkipWhile(w => !w.Equals(closest)).Skip(1).GetEnumerator();

            wayPointsIter.MoveNext();
            AutopilotResult.Mode = "Route";
            AutopilotResult.WaypointCount = wayPoints.Count();

            while (Pilot.IsAutoPilotEnabled)
            {
                var currentWaypoint = wayPointsIter.Current;

                SetCruiseControl();

                if (CheckEmergencyStop()) yield break;

                if (CheckNoEmergencySteer())
                {
                    Vector3D currentPosition, direction;
                    double directionAngle, distance;
                    CalcSteer(currentWaypoint, out currentPosition, out direction, out directionAngle, out distance);

                    AutopilotResult.Waypoint = wayPointsIter.Current.Name ?? "None";
                    AutopilotResult.Steer = (float)MathHelper.Clamp(directionAngle, -1, 1);
                    AutopilotResult.Distance = distance;

                    if (distance < WayPointReachThreshold + Speed * 3.6 * 50 / 180)
                    {
                        if (!wayPointsIter.MoveNext())
                        {
                            switch (Pilot.FlightMode)
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
                                    Pilot.SetAutoPilotEnabled(false);
                                    controller.HandBrake = true;
                                    yield break;
                            }
                        }
                    }
                }
                yield return null;
            }
        }

        void CalcSteer(MyWaypointInfo currentWaypoint, out Vector3D currentPosition, out Vector3D direction, out double directionAngle, out double distance)
        {
            var matrix = Pilot.WorldMatrix;
            currentPosition = Pilot.GetPosition();
            direction = AvoidCollision(Pilot.Block, Sensor, currentPosition, currentWaypoint.Coords);
            distance = direction.Length();
            var directionNormal = Vector3D.Normalize(direction);
            directionAngle = Math.Atan2(directionNormal.Dot(matrix.Left), directionNormal.Dot(matrix.Forward));
        }

        void SetCruiseControl()
        {
            var controller = Controllers.MainController;
            if (!Cruise && !controller.HandBrake)
            {
                TaskManager.RunTask(CruiseTask(Pilot.SpeedLimit * 3.6f, () => Pilot.IsAutoPilotEnabled && !controller.HandBrake)).Once();
            }
            else
                CruiseSpeed = Pilot.SpeedLimit * 3.6f;
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
                        TaskManager.RunTask(RecordRouteTask()).Every(1.7f).Once();
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
