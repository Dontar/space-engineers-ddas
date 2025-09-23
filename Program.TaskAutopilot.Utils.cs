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
                if (Count > 0) return default(MyWaypointInfo);
                return Dequeue().Item;
            }


            public MyWaypointInfo TryPeek()
            {
                if (Count < 0) return default(MyWaypointInfo);
                return Peek().Item;
            }

            public double CalcSpeed()
            {
                if (Count < 2) return 0;

                var list = ToArray();
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

        }

        class Autopilot
        {
            public Autopilot(IMyTerminalBlock[] blocks)
            {
                Blocks = blocks;
            }

            public IMyTerminalBlock[] Blocks;

            public IMyTerminalBlock Block => Memo.Of("AutopilotInternalBlock", 5, () => Blocks.FirstOrDefault(b =>
                b != null
                && ((b is IMyRemoteControl && ((IMyRemoteControl)b).IsAutoPilotEnabled)
                || (b is IMyFlightMovementBlock && ((IMyFlightMovementBlock)b).IsAutoPilotEnabled))
            ));

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
                    if (autopilot.CurrentWaypoint == null) return MyWaypointInfo.Empty;
                    return new MyWaypointInfo(autopilot.CurrentWaypoint.Name, autopilot.CurrentWaypoint.Matrix.Translation);
                }
            }
            public FlightMode FlightMode => Block is IMyRemoteControl ? (Block as IMyRemoteControl).FlightMode : (Block as IMyFlightMovementBlock).FlightMode;
            public float SpeedLimit => Block is IMyRemoteControl ? (Block as IMyRemoteControl).SpeedLimit : (Block as IMyFlightMovementBlock).SpeedLimit;
            public MatrixD WorldMatrix => Block.WorldMatrix;

            public Vector3D GetPosition() => Block?.GetPosition() ?? Vector3D.Zero;
            public void SetAutoPilotEnabled(bool enabled) => Block?.SetValueBool(Block is IMyFlightMovementBlock ? "ActivateBehavior" : "AutoPilot", enabled);
            public static Autopilot FromBlock(IMyTerminalBlock[] blocks) => new Autopilot(blocks);
        }

        void SetSensorDimension(float value, Base6Directions.Direction direction)
        {
            value = MathHelper.Clamp(value, 0.1f, 50);
            var dir = Sensor.Orientation.TransformDirectionInverse(direction);
            switch (dir)
            {
                case Base6Directions.Direction.Forward:
                    Sensor.FrontExtend = value;
                    break;
                case Base6Directions.Direction.Backward:
                    Sensor.BackExtend = value;
                    break;
                case Base6Directions.Direction.Left:
                    Sensor.LeftExtend = value;
                    break;
                case Base6Directions.Direction.Right:
                    Sensor.RightExtend = value;
                    break;
                case Base6Directions.Direction.Up:
                    Sensor.TopExtend = value;
                    break;
                case Base6Directions.Direction.Down:
                    Sensor.BottomExtend = value;
                    break;
            }
        }

        void SetupSensor()
        {
            var halfHeight = Dimensions.Height / 2;
            var vertPos = (Sensor.Position.Y - Me.CubeGrid.Min.Y) * (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 0.5); // meters;

            var values = new float[] {
                /* Forward */ 50,
                /* Backward */ Dimensions.Length,
                /* Left */ 20,
                /* Right */ 20,
                /* Up */ (float)(Dimensions.Height - vertPos),
                /* Down */ (float)vertPos,
            };

            for (int i = 0; i < values.Length && i < Base6Directions.EnumDirections.Length; i++)
            {
                SetSensorDimension(values[i], Base6Directions.EnumDirections[i]);
            }
        }

        TaskManager.ITask EmergencySteer;
        bool CheckNoEmergencySteer()
        {
            if (LeftRight != 0)
            {
                AutopilotResult.Steer = 0;
                TaskManager.ClearTask(EmergencySteer);
                EmergencySteer = TaskManager.SetTimeout(() => EmergencySteer = null, 3);
            }
            return EmergencySteer == null;
        }

        bool CheckEmergencyStop()
        {
            if (UpDown > 0)
            {
                Pilot.SetAutoPilotEnabled(false);
                return true;
            }
            return false;
        }
    }
}
