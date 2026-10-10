using System;
using System.Collections.Generic;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Systems
{
    /// <summary>
    /// ⭐ <c>CE-3117</c> (R-226) — keeps each mover's <see cref="PathTrace"/> equal to the trajectory it follows, so the map can draw
    /// the planned path from recorded state (the pool is not in the world). Rewrites a trace only when the trajectory changed — a new
    /// id, or a replan in place (a different waypoint count or length) — so the component is recorded once per path, not per frame.
    /// A path longer than <see cref="PathTrace.Capacity"/> points keeps its ends and every door step and thins the rest evenly.
    /// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class PathTraceSystem : IEcsModuleSystem
    {
        private readonly TrajectoryPoolManager _pool;
        private readonly List<(Entity Entity, PathTrace Trace, bool Add)> _writes = new();

        public PathTraceSystem(TrajectoryPoolManager pool) => _pool = pool;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<PathTrace>()) return;
            _writes.Clear();
            foreach (var e in repo.Query().With<NavState>().Build())
            {
                ref readonly var nav = ref repo.GetComponentRO<NavState>(e);
                bool has = repo.HasComponent<PathTrace>(e);
                bool follows = nav.Mode == KinematicsMode.CustomTrajectory && nav.TrajectoryId >= 0
                               && _pool.TryGetTrajectory(nav.TrajectoryId, out _);
                if (!follows)
                {
                    if (has && repo.GetComponentRO<PathTrace>(e).Count > 0)
                        _writes.Add((e, new PathTrace { TrajectoryId = -1 }, false));
                    continue;
                }
                _pool.TryGetTrajectory(nav.TrajectoryId, out var traj);
                if (!traj.Waypoints.IsCreated) continue;
                if (has)
                {
                    ref readonly var old = ref repo.GetComponentRO<PathTrace>(e);
                    if (old.TrajectoryId == traj.Id && old.SourceCount == traj.Waypoints.Length && old.TotalLength == traj.TotalLength)
                        continue;
                }
                _writes.Add((e, Build(in traj), !has));
            }
            foreach (var (e, t, add) in _writes)
            {
                if (add) repo.AddComponent(e, t);
                else repo.SetComponent(e, t);
            }
        }

        /// <summary>The trace of <paramref name="traj"/>.</summary>
        public static PathTrace Build(in CustomTrajectory traj)
        {
            var t = new PathTrace
            {
                TrajectoryId = traj.Id, SourceCount = traj.Waypoints.Length, TotalLength = traj.TotalLength, IsLooped = traj.IsLooped,
            };
            var pts = t.PointsRW();
            int n = traj.Waypoints.Length;
            int step = n <= PathTrace.Capacity ? 1 : (int)Math.Ceiling((n - 1) / (double)(PathTrace.Capacity - 1));
            for (int i = 0; i < n && t.Count < PathTrace.Capacity; i++)
            {
                var w = traj.Waypoints[i];
                bool keep = i == 0 || i == n - 1 || w.Traversal != 0 || i % step == 0;
                if (!keep) continue;
                if (t.Count == PathTrace.Capacity - 1 && i != n - 1) continue;   // the last slot is the end of the path
                pts[t.Count++] = new PathTracePoint { Position = w.Position, S = w.CumulativeDistance, Traversal = w.Traversal };
            }
            return t;
        }
    }
}
