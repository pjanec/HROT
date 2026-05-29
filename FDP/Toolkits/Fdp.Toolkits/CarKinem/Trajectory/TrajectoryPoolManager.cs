using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core.Collections;

namespace CarKinem.Trajectory
{
    /// <summary>
    /// Trajectory pool singleton (managed component).
    /// Stores all custom trajectories in the simulation.
    /// Thread-safe for reads (immutable after creation).
    /// </summary>
    public class TrajectoryPoolManager : IDisposable
    {
        private readonly Dictionary<int, CustomTrajectory> _trajectories = new();
        private int _nextId = 1;
        private readonly object _lock = new();
        
        /// <summary>
        /// Register a new custom trajectory with optional interpolation mode.
        /// </summary>
        /// <param name="positions">Waypoint positions</param>
        /// <param name="speeds">Desired speeds at each waypoint (optional)</param>
        /// <param name="looped">True if trajectory loops back to start</param>
        /// <param name="interpolation">Interpolation mode (default: Linear for backward compat)</param>
        /// <param name="tangents">Explicit tangents (only for HermiteExplicit mode)</param>
        /// <returns>Unique trajectory ID</returns>
        public int RegisterTrajectory(
            Vector3[] positions,
            float[]? speeds = null,
            bool looped = false,
            TrajectoryInterpolation interpolation = TrajectoryInterpolation.Linear,
            Vector2[]? tangents = null)
        {
            if (positions == null || positions.Length < 2)
                throw new ArgumentException("Trajectory must have at least 2 waypoints", nameof(positions));

            if (speeds != null && speeds.Length != positions.Length)
                throw new ArgumentException("Speeds array must match positions length", nameof(speeds));

            if (interpolation == TrajectoryInterpolation.HermiteExplicit && tangents == null)
                throw new ArgumentException("HermiteExplicit mode requires tangents array", nameof(tangents));

            if (tangents != null && tangents.Length != positions.Length)
                throw new ArgumentException("Tangents array must match positions length", nameof(tangents));

            lock (_lock)
            {
                int id = _nextId++;

                // Project to XY for all arc-length / spline-curvature math (§0.2): the carried Z
                // (positions[i].Z) is stored on the waypoint but never feeds curvature or heading.
                Vector2[] posXY = ProjectXY(positions);

                // Precompute waypoints with cumulative distance
                var waypoints = new NativeArray<TrajectoryWaypoint>(positions.Length, Allocator.Persistent);
                float cumulativeDistance = 0f;

                for (int i = 0; i < positions.Length; i++)
                {
                    // Compute arc length (depends on interpolation mode) — XY projection only.
                    if (i > 0)
                    {
                        if (interpolation == TrajectoryInterpolation.Linear)
                        {
                            // Linear: Straight line distance
                            cumulativeDistance += Vector2.Distance(posXY[i - 1], posXY[i]);
                        }
                        else
                        {
                            // Hermite: Sample-based arc length
                            Vector2 p0 = posXY[i - 1];
                            Vector2 p1 = posXY[i];
                            Vector2 t0 = GetTangent(posXY, tangents, i - 1, interpolation);
                            Vector2 t1 = GetTangent(posXY, tangents, i, interpolation);

                            cumulativeDistance += ComputeHermiteArcLength(p0, t0, p1, t1);
                        }
                    }

                    // Compute tangent based on mode (XY projection)
                    Vector2 tangent = GetTangent(posXY, tangents, i, interpolation);

                    waypoints[i] = new TrajectoryWaypoint
                    {
                        Position = positions[i],   // full 3D position (Sim Z-up); Z carried
                        Tangent = tangent,  // Now actually used!
                        DesiredSpeed = speeds?[i] ?? 10.0f,
                        CumulativeDistance = cumulativeDistance
                    };
                }
                
                var trajectory = new CustomTrajectory
                {
                    Id = id,
                    Waypoints = waypoints,
                    TotalLength = cumulativeDistance,
                    IsLooped = (byte)(looped ? 1 : 0),
                    Interpolation = interpolation
                };
                
                _trajectories[id] = trajectory;
                return id;
            }
        }

        /// <summary>
        /// Backward-compatible 2D overload (lifts each waypoint to altitude 0). Used by
        /// 2D-authored callers (editor scenarios, tests); the 3D path uses the <see cref="Vector3"/>
        /// overload so real altitude is carried (P3D-303).
        /// </summary>
        public int RegisterTrajectory(
            Vector2[] positions,
            float[]? speeds = null,
            bool looped = false,
            TrajectoryInterpolation interpolation = TrajectoryInterpolation.Linear,
            Vector2[]? tangents = null)
            => RegisterTrajectory(Lift(positions), speeds, looped, interpolation, tangents);

        /// <summary>Lifts 2D positions to 3D (Z = 0) for the backward-compatible overloads.</summary>
        private static Vector3[] Lift(Vector2[] positions)
        {
            if (positions == null) return null!;
            var v3 = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                v3[i] = new Vector3(positions[i].X, positions[i].Y, 0f);
            return v3;
        }

        /// <summary>
        /// Registers a trajectory under a caller-supplied <paramref name="key"/> (e.g., a
        /// Brain- or Muscle-allocated <c>RouteHandle</c>).  If a trajectory already exists
        /// for that key its <see cref="NativeArray{T}"/> is disposed before being replaced.
        /// </summary>
        /// <summary>Backward-compatible 2D overload of <see cref="RegisterTrajectoryWithKey(Vector3[],int)"/> (Z = 0).</summary>
        public void RegisterTrajectoryWithKey(Vector2[] positions, int key)
            => RegisterTrajectoryWithKey(Lift(positions), key);

        public void RegisterTrajectoryWithKey(Vector3[] positions, int key)
        {
            if (positions == null || positions.Length < 2)
                throw new ArgumentException("Trajectory must have at least 2 waypoints", nameof(positions));

            lock (_lock)
            {
                // Dispose any existing trajectory stored at this key.
                if (_trajectories.TryGetValue(key, out var existing) && existing.Waypoints.IsCreated)
                    existing.Waypoints.Dispose();

                // XY projection for arc length (carried Z does not affect distance, §0.2).
                Vector2[] posXY = ProjectXY(positions);

                // Build linear waypoints (same path as default RegisterTrajectory).
                var waypoints = new NativeArray<TrajectoryWaypoint>(positions.Length, Allocator.Persistent);
                float cumulativeDistance = 0f;

                for (int i = 0; i < positions.Length; i++)
                {
                    if (i > 0)
                        cumulativeDistance += Vector2.Distance(posXY[i - 1], posXY[i]);

                    waypoints[i] = new TrajectoryWaypoint
                    {
                        Position           = positions[i],   // full 3D position (Sim Z-up)
                        Tangent            = GetTangent(posXY, null, i, TrajectoryInterpolation.Linear),
                        DesiredSpeed       = 10.0f,
                        CumulativeDistance = cumulativeDistance,
                    };
                }

                _trajectories[key] = new CustomTrajectory
                {
                    Id            = key,
                    Waypoints     = waypoints,
                    TotalLength   = cumulativeDistance,
                    IsLooped      = 0,
                    Interpolation = TrajectoryInterpolation.Linear,
                };
            }
        }

        /// <summary>
        /// Get trajectory by ID (read-only).
        /// </summary>
        public bool TryGetTrajectory(int id, out CustomTrajectory trajectory)
        {
            lock (_lock)
            {
                return _trajectories.TryGetValue(id, out trajectory);
            }
        }
        
        /// <summary>
        /// Sample trajectory at given progress distance.
        /// Returns (position, tangent, desired speed).
        /// Thread-safe for concurrent reads from different trajectories.
        /// </summary>
        /// <param name="id">Trajectory ID</param>
        /// <param name="progressS">Progress along trajectory (meters from start)</param>
        /// <returns>Sampled position, tangent direction, and desired speed</returns>
        public (Vector3 pos, Vector2 tangent, float speed) SampleTrajectory(int id, float progressS)
        {
            if (!TryGetTrajectory(id, out var traj))
            {
                return (Vector3.Zero, new Vector2(1, 0), 0f);
            }
            
            // Handle looping
            if (traj.IsLooped == 1)
            {
                progressS = progressS % traj.TotalLength;
                if (progressS < 0f) progressS += traj.TotalLength;
            }
            else
            {
                progressS = Math.Clamp(progressS, 0f, traj.TotalLength);
            }
            
            // Find segment containing progressS
            var waypoints = traj.Waypoints;
            for (int i = 1; i < waypoints.Length; i++)
            {
                if (waypoints[i].CumulativeDistance >= progressS)
                {
                    float segmentDist = waypoints[i].CumulativeDistance - waypoints[i - 1].CumulativeDistance;
                    float localProgress = progressS - waypoints[i - 1].CumulativeDistance;
                    float t = segmentDist > 0.001f ? localProgress / segmentDist : 0f;
                    
                    Vector3 pos;
                    Vector2 tangent;
                    float speed;

                    // Z is always linearly interpolated between bracketing waypoints (§0.2);
                    // X/Y + heading follow the configured 2D interpolation on the projection.
                    float z0 = waypoints[i - 1].Position.Z;
                    float z1 = waypoints[i].Position.Z;
                    float zLerp = z0 + (z1 - z0) * t;

                    Vector2 a = new Vector2(waypoints[i - 1].Position.X, waypoints[i - 1].Position.Y);
                    Vector2 b = new Vector2(waypoints[i].Position.X, waypoints[i].Position.Y);

                    // Interpolation based on mode
                    if (traj.Interpolation == TrajectoryInterpolation.Linear)
                    {
                        // LINEAR MODE (existing behavior, XY projection)
                        Vector2 xy = Vector2.Lerp(a, b, t);
                        Vector2 segmentDir = b - a;
                        tangent = segmentDir.LengthSquared() > 0.001f
                            ? Vector2.Normalize(segmentDir)
                            : new Vector2(1, 0);
                        speed = waypoints[i - 1].DesiredSpeed +
                                (waypoints[i].DesiredSpeed - waypoints[i - 1].DesiredSpeed) * t;
                        pos = new Vector3(xy.X, xy.Y, zLerp);
                    }
                    else
                    {
                        // HERMITE MODE (XY projection for curvature; Z linear)
                        Vector2 t0 = waypoints[i - 1].Tangent;
                        Vector2 t1 = waypoints[i].Tangent;

                        Vector2 xy = EvaluateHermite(t, a, t0, b, t1);
                        tangent = Vector2.Normalize(EvaluateHermiteTangent(t, a, t0, b, t1));
                        speed = waypoints[i - 1].DesiredSpeed +
                                (waypoints[i].DesiredSpeed - waypoints[i - 1].DesiredSpeed) * t;
                        pos = new Vector3(xy.X, xy.Y, zLerp);
                    }

                    return (pos, tangent, speed);
                }
            }

            // End of trajectory (or exactly at end)
            var lastWp = waypoints[waypoints.Length - 1];
            Vector2 lastTangent = waypoints.Length > 1
                ? Vector2.Normalize(
                    new Vector2(lastWp.Position.X, lastWp.Position.Y)
                    - new Vector2(waypoints[waypoints.Length - 2].Position.X, waypoints[waypoints.Length - 2].Position.Y))
                : new Vector2(1, 0);

            return (lastWp.Position, lastTangent, lastWp.DesiredSpeed);
        }

        /// <summary>Projects an array of Sim (Z-up) positions to their XY (ground-plane) components.</summary>
        private static Vector2[] ProjectXY(Vector3[] positions)
        {
            var xy = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                xy[i] = new Vector2(positions[i].X, positions[i].Y);
            return xy;
        }
        
        /// <summary>
        /// Get tangent for waypoint i based on interpolation mode.
        /// </summary>
        private Vector2 GetTangent(
            Vector2[] positions, 
            Vector2[]? tangents, 
            int i, 
            TrajectoryInterpolation interpolation)
        {
            switch (interpolation)
            {
                case TrajectoryInterpolation.Linear:
                    return Vector2.Zero;  // Not used
                
                case TrajectoryInterpolation.CatmullRom:
                    return ComputeCatmullRomTangent(positions, i);
                
                case TrajectoryInterpolation.HermiteExplicit:
                    return tangents![i];
                
                default:
                    return Vector2.Zero;
            }
        }
        
        /// <summary>
        /// Compute Catmull-Rom tangent at waypoint i.
        /// Uses finite difference: tangent = (p[i+1] - p[i-1]) / 2
        /// </summary>
        private Vector2 ComputeCatmullRomTangent(Vector2[] positions, int i)
        {
            int n = positions.Length;
            
            if (n < 2)
                return Vector2.Zero;
            
            // Special cases for endpoints
            if (i == 0)
            {
                // Start: Use forward difference
                return (positions[1] - positions[0]);
            }
            else if (i == n - 1)
            {
                // End: Use backward difference
                return (positions[n - 1] - positions[n - 2]);
            }
            else
            {
                // Middle: Central difference (Catmull-Rom formula)
                return (positions[i + 1] - positions[i - 1]) * 0.5f;
            }
        }
        
        /// <summary>
        /// Compute Hermite spline arc length via trapezoidal integration.
        /// Uses same algorithm as RoadNetworkBuilder for consistency.
        /// </summary>
        private float ComputeHermiteArcLength(Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1)
        {
            const int SAMPLES = 32;  // Trade-off between accuracy and speed
            float length = 0f;
            Vector2 prevPoint = EvaluateHermite(0f, p0, t0, p1, t1);
            
            for (int i = 1; i <= SAMPLES; i++)
            {
                float t = i / (float)SAMPLES;
                Vector2 point = EvaluateHermite(t, p0, t0, p1, t1);
                length += Vector2.Distance(prevPoint, point);
                prevPoint = point;
            }
            
            return length;
        }

        /// <summary>
        /// Evaluate Hermite spline at parameter t.
        /// Copy of RoadGraphNavigator.EvaluateHermite for trajectory use.
        /// </summary>
        private Vector2 EvaluateHermite(float t, Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            
            float h00 = 2 * t3 - 3 * t2 + 1;
            float h10 = t3 - 2 * t2 + t;
            float h01 = -2 * t3 + 3 * t2;
            float h11 = t3 - t2;
            
            return h00 * p0 + h10 * t0 + h01 * p1 + h11 * t1;
        }

        /// <summary>
        /// Evaluate Hermite tangent at parameter t.
        /// Copy of RoadGraphNavigator.EvaluateHermiteTangent.
        /// </summary>
        private Vector2 EvaluateHermiteTangent(float t, Vector2 p0, Vector2 t0, Vector2 p1, Vector2 t1)
        {
            float t2 = t * t;
            
            float dh00 = 6 * t2 - 6 * t;
            float dh10 = 3 * t2 - 4 * t + 1;
            float dh01 = -6 * t2 + 6 * t;
            float dh11 = 3 * t2 - 2 * t;
            
            return dh00 * p0 + dh10 * t0 + dh01 * p1 + dh11 * t1;
        }

        /// <summary>
        /// Remove trajectory from pool.
        /// </summary>
        public bool RemoveTrajectory(int id)
        {
            lock (_lock)
            {
                if (_trajectories.TryGetValue(id, out var traj))
                {
                    if (traj.Waypoints.IsCreated)
                        traj.Waypoints.Dispose();
                    
                    return _trajectories.Remove(id);
                }
                return false;
            }
        }
        
        /// <summary>
        /// Get total number of registered trajectories.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _trajectories.Count;
                }
            }
        }
        
        public void Clear()
        {
            lock (_lock)
            {
                foreach (var traj in _trajectories.Values)
                {
                    if (traj.Waypoints.IsCreated)
                        traj.Waypoints.Dispose();
                }
                _trajectories.Clear();
                _nextId = 1;
            }
        }

        /// <summary>
        /// Cleanup all trajectories (call on shutdown).
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var traj in _trajectories.Values)
                {
                    if (traj.Waypoints.IsCreated)
                        traj.Waypoints.Dispose();
                }
                _trajectories.Clear();
            }
        }
    }
}
