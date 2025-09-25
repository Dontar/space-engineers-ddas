using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        Vector3D AvoidanceVector = Vector3D.Zero;

        Dictionary<long, int> RecentlyAvoided = new Dictionary<long, int>();

        Vector3D AvoidCollision(IMySensorBlock sensor, Vector3D currentPosition, Vector3D destination)
        {
            var up = Pilot.Matrix.Up;
            var right = Pilot.Matrix.Right;
            var backward = Pilot.Matrix.Backward;
            var destinationVector = destination - currentPosition;
            var directionVector = Vector3D.ProjectOnPlane(ref destinationVector, ref up);
            if (!Pilot.CollisionAvoidance || sensor == null) return directionVector;

            var obstructions = new List<MyDetectedEntityInfo>();
            sensor.DetectedEntities(obstructions);

            if (obstructions.Count == 0)
            {
                AvoidanceVector = Vector3D.Zero;
                RecentlyAvoided.Clear();
                return directionVector;
            }

            foreach (var obstruction in obstructions)
            {
                if (obstruction.IsEmpty()) continue;
                if (RecentlyAvoided.ContainsKey(obstruction.EntityId)) continue;

                if (!Vector3.IsZero(obstruction.Velocity))
                {
                    var obstructionVelocity = new Vector3D(obstruction.Velocity);
                    if (Vector3D.Dot(obstructionVelocity, Velocities.LinearVelocity) < 0)
                    {
                        var timeToCollision = Vector3D.Distance(obstruction.Position, currentPosition) / (obstruction.Velocity - Velocities.LinearVelocity).Length();
                        if (timeToCollision < 10) // seconds
                        {
                            var awayFromObstacle = currentPosition - obstruction.Position;
                            var rightDot = Vector3D.Dot(awayFromObstacle, right);
                            AvoidanceVector += (rightDot > 0 ? right : -right) * awayFromObstacle.LengthSquared();
                            RecentlyAvoided[obstruction.EntityId] = 10;
                        }
                    }
                }
                else
                {
                    var bbox = obstruction.BoundingBox;
                    if (bbox.Contains(currentPosition) == ContainmentType.Contains)
                    {
                        var center = bbox.Center;
                        var directionOut = Vector3D.Normalize(currentPosition - center);
                        // If at center, pick a random direction
                        if (directionOut.LengthSquared() < 1e-6)
                            directionOut = Vector3D.Normalize(right + backward); // or any arbitrary direction
                        AvoidanceVector += directionOut * WayPointCloseThreshold;
                        continue;
                    }

                    var closestPoint = Vector3D.Clamp(currentPosition, bbox.Min, bbox.Max);
                    var awayFromObstacle = currentPosition - closestPoint;

                    // Is it leftish or rightish
                    var rightDot = Vector3D.Dot(Vector3D.Normalize(awayFromObstacle), right);
                    var isLeftish = rightDot > 0;

                    double distance = awayFromObstacle.Length();
                    double minDistance = WayPointReachThreshold; // minimum effective distance (meters)
                    double maxForce = WayPointCloseThreshold;   // maximum avoidance force

                    double avoidanceStrength = maxForce / Math.Max(distance, minDistance);

                    if (Math.Abs(rightDot) < 0.2)
                    {
                        AvoidanceVector += (isLeftish ? right : -right) * avoidanceStrength;
                        RecentlyAvoided[obstruction.EntityId] = 10;
                    }
                    else if (Math.Abs(rightDot) < 0.4)
                    {
                        var forward = Pilot.Matrix.Forward;
                        AvoidanceVector += (forward + (isLeftish ? right : -right)) * avoidanceStrength;
                        RecentlyAvoided[obstruction.EntityId] = 10;
                    }
                }
            }

            foreach (var key in RecentlyAvoided.Keys.ToList())
            {
                RecentlyAvoided[key]--;
                if (RecentlyAvoided[key] <= 0) RecentlyAvoided.Remove(key);
            }

            if (AvoidanceVector.LengthSquared() > 0) return AvoidanceVector * WayPointCloseThreshold; // weight avoidance vector

            return directionVector;
        }
    }
}
