using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRageMath;
using Sandbox.ModAPI.Interfaces;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        struct TimedItem<T>
        {
            public T Item;
            public TimeSpan TimeAdded;
            public TimedItem(T item) {
                Item = item;
                TimeAdded = DateTime.Now.TimeOfDay;
            }
        }

        class UniqueTimedQueue : Queue<TimedItem<MyWaypointInfo>>
        {
            private int _capacity = 10;

            public UniqueTimedQueue() : base() { }

            public UniqueTimedQueue(int capacity) : base(capacity) {
                _capacity = capacity;
            }

            public void Enqueue(MyWaypointInfo item, Func<MyWaypointInfo, bool> compare) {
                if (compare(item)) {
                    Enqueue(new TimedItem<MyWaypointInfo>(item));
                }
                if (Count > _capacity) {
                    Dequeue();
                }
            }

            public MyWaypointInfo TryDequeue() {
                if (Count > 0) return default(MyWaypointInfo);
                return Dequeue().Item;
            }


            public MyWaypointInfo TryPeek() {
                if (Count < 0) return default(MyWaypointInfo);
                return Peek().Item;
            }

            public double CalcSpeed() {
                if (Count < 2) return 0;

                var list = ToArray();
                // Calculate average speed from list
                double totalDistance = 0;
                double totalTime = 0;
                for (int i = 1; i < list.Length; i++) {
                    var prev = list[i - 1];
                    var curr = list[i];
                    double distance = Math.Abs(Vector3D.Distance(prev.Item.Coords, curr.Item.Coords));
                    double time = Math.Abs((curr.TimeAdded - prev.TimeAdded).TotalSeconds);
                    if (time > 0) {
                        totalDistance += distance;
                        totalTime += time;
                    }
                }
                return totalTime > 0 ? totalDistance / totalTime : 0;
            }

        }

        class Autopilot
        {
            public Autopilot(IMyTerminalBlock[] blocks) {
                Blocks = blocks;
            }

            public IMyTerminalBlock[] Blocks;

            IMyTerminalBlock _block;

            public bool IsAutoPilotEnabled {
                get {
                    if (_block != null) {
                        var enabled = _block.GetValueBool(_block is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot");
                        if (!enabled) _block = null;
                        return enabled;
                    }
                    _block = Blocks.FirstOrDefault(b => b != null && b.GetValueBool(b is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot"));
                    return _block != null;
                }
            }
            public void SetAutoPilotEnabled(bool enabled) {
                _block?.SetValueBool(_block is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot", enabled);
                if (!enabled) _block = null;
            }

            public IEnumerable<MyWaypointInfo> Waypoints {
                get {
                    if (_block is IMyRemoteControl) {
                        var waypoints = new List<MyWaypointInfo>();
                        (_block as IMyRemoteControl).GetWaypointInfo(waypoints);
                        return waypoints;
                    }
                    var aiWaypoints = new List<IMyAutopilotWaypoint>();
                    (_block as IMyFlightMovementBlock).GetWaypoints(aiWaypoints);
                    return aiWaypoints.Select(w => new MyWaypointInfo(w.Name, w.Matrix.Translation));
                }
            }
            public MyWaypointInfo CurrentWaypoint {
                get {
                    if (_block is IMyRemoteControl) return (_block as IMyRemoteControl).CurrentWaypoint;
                    var autopilot = _block as IMyFlightMovementBlock;
                    if (autopilot.CurrentWaypoint == null) return MyWaypointInfo.Empty;
                    return new MyWaypointInfo(autopilot.CurrentWaypoint.Name, autopilot.CurrentWaypoint.Matrix.Translation);
                }
            }
            public FlightMode FlightMode => _block is IMyRemoteControl ? (_block as IMyRemoteControl).FlightMode : (_block as IMyFlightMovementBlock).FlightMode;
            public float SpeedLimit => _block is IMyRemoteControl ? (_block as IMyRemoteControl).SpeedLimit : (_block as IMyFlightMovementBlock).SpeedLimit;
            public bool CollisionAvoidance => _block?.GetValueBool("CollisionAvoidance") ?? false;
            public MatrixD Matrix => _block.WorldMatrix;

            public Vector3D GetPosition() => _block?.GetPosition() ?? Vector3D.Zero;
            public static Autopilot FromBlock(IMyTerminalBlock[] blocks) => new Autopilot(blocks);
        }

        void SetupSensor() {
            var scale = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5f : 0.5f;
            var sensorPos = Sensor.Position * scale;

            var offset = sensorPos - Dimensions.Center;
            var half = Dimensions.HalfExtents;

            var values = new float[] {
                /* Forward */ 50 + offset.Z,
                /* Backward */ half.Z - offset.Z,
                /* Left */ 20 + offset.X,
                /* Right */ 20 - offset.X,
                /* Up */ half.Y - offset.Y,
                /* Down */ half.Y + offset.Y,
            };

            Util.SetSensorDimensions(Sensor, values);
        }

        ITask EmergencySteer;
        bool CheckNoEmergencySteer() {
            if (LeftRight != 0) {
                Task.ClearTask(EmergencySteer);
                EmergencySteer = Task.SetTimeout(() => EmergencySteer = null, 3);
            }
            if (EmergencySteer != null) {
                AutopilotResult.Steer = -LeftRight;
            }
            return EmergencySteer == null;
        }

        bool CheckEmergencyStop() {
            if (UpDown > 0) {
                Pilot.SetAutoPilotEnabled(false);
                return true;
            }
            return false;
        }
    }
}
