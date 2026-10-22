using System;
using CarKinem.Core;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;

namespace CarKinem.Systems
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S2 §7c — <b>a solved path survives a restore.</b> The trajectory pool is a per-process cache that no keyframe carries
    /// (ROUTES1 §2.1); a mover's <see cref="PathTrace"/> is its recorded copy. At a world replacement every mover following a solved
    /// (Linear) path gets its trajectory registered again from that copy, under the handle its <see cref="NavState"/> names, and the
    /// muscle's handle counter is lifted above it; a path too long to be held whole (a thinned trace) is planned again from where the unit
    /// stands — the one accepted divergence. Routes are not touched here: <c>RouteTrajectorySyncSystem</c> compiles them again from their
    /// <c>RoutePlan</c> (§7b, BeforeSync — before this). First in the Simulation phase, so nothing drives a missing path.
    /// 📄 docs/DESIGN_System_State_Inventory.md §7c.
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class TrajectoryRestoreSystem : IEcsModuleSystem
    {
        private readonly TrajectoryPoolManager _pool;
        private int _worldEpoch;
        private int _restoresSeen;   // ⭐ CE-3318 — the world restores already resumed the handle counter for
        private bool _resumePending;  // ⭐ CE-3318 — a restore waits for its first step to decide the counter (the clock has moved by then)

        public TrajectoryRestoreSystem(TrajectoryPoolManager pool) => _pool = pool ?? throw new ArgumentNullException(nameof(pool));

        /// <summary>How many paths the last world replacement registered again, and how many it had to plan again (diagnostics, rails).</summary>
        public int RestoredCount { get; private set; }
        public int ReplannedCount { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo) return;
            if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) Replaced(repo);
            // ⭐ CE-3318 (restore design §1m) — a restore is decided on its FIRST STEP: on the paused frames before it the clock has not
            //   reached the restored time yet (the snap lands later), so a carried copy would look like the abandoned future's. Nothing
            //   allocates a handle before a step (the pathfinding solver runs on steps).
            if (_resumePending)
            {
                if (deltaTime <= 0f) return;   // ⛔ and the mirror waits too: it would overwrite the carried copy
                _resumePending = false;
                if (repo.HasSingleton<NavigationHandleMark>() && repo.GetSingleton<NavigationHandleMark>() is var mark
                    && mark.At <= SimTime(repo) + 1e-6)
                    NavigationHandleAllocator.ResumeAt(Math.Max(mark.Last, _highestRestored));
            }
            // the world's copy of the handle counter, so a checkpoint carries it
            int last = NavigationHandleAllocator.Last;
            if (!repo.HasSingleton<NavigationHandleMark>() || repo.GetSingleton<NavigationHandleMark>().Last != last)
                repo.SetSingletonUnmanaged(new NavigationHandleMark { Last = last, At = SimTime(repo) });
        }

        private static double SimTime(EntityRepository repo) => repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;

        private void Replaced(EntityRepository repo)
        {
            // ⭐ CE-3318 — a CHECKPOINT put back: the next handle follows the carried count (a load, or a dry-run exit whose copy is the
            //   abandoned future's, keeps the counter above every live mover's handle, as before)
            int restores = Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(repo);
            _resumePending = restores != _restoresSeen;
            _restoresSeen = restores;
            _highestRestored = 0;
            if (!repo.IsComponentTypeRegistered<NavState>() || !repo.IsComponentTypeRegistered<PathTrace>()) return;

            RestoredCount = ReplannedCount = 0;
            int highest = 0;
            var registry = repo.HasSingletonManaged<IPathRegistry>()
                ? repo.GetSingletonManaged<IPathRegistry>() as Fdp.Toolkit.Navigation.EngineBacked.EngineBackedPathRegistry
                : null;
            foreach (var e in repo.Query().With<NavState>().Build())
            {
                ref readonly var nav = ref repo.GetComponentRO<NavState>(e);
                if (nav.Mode != KinematicsMode.CustomTrajectory || nav.TrajectoryId <= 0) continue;
                int id = nav.TrajectoryId;
                highest = Math.Max(highest, id);

                bool traced = repo.HasComponent<PathTrace>(e) && repo.GetComponentRO<PathTrace>(e).TrajectoryId == id;
                if (traced && Register(in repo.GetComponentRO<PathTrace>(e), id))
                {
                    // ⭐ and the path registry's entry (IsCached needs it — FetchPathDetails asks): the distance falls back to the
                    //   trajectory's own length; the solver's backend tag is not recorded and reads 0 after a restore (diagnostic only).
                    if (registry is not null && !registry.IsCached(id)) registry.Register(id);
                    RestoredCount++;
                    continue;
                }
                if (_pool.TryGetTrajectory(id, out _)) continue;   // a route (re-derived before this), or the same process still holds it

                // Not held whole, and gone: plan it again from where the unit stands (the muscle's one request, as a replan makes it).
                if (repo.IsComponentTypeRegistered<NavigationIntent>() && repo.HasComponent<NavigationIntent>(e)
                    && repo.GetComponentRO<NavigationIntent>(e) is { Mode: NavigationMode.PathToPoint } intent)
                {
                    var from = repo.HasComponent<SimTransform>(e) ? repo.GetComponentRO<SimTransform>(e).Position : default;
                    repo.Bus.Publish(PathRequests.FromIntent(repo, e, in intent, from, ((long)e.Index << 32) | (uint)repo.GlobalVersion));
                    ReplannedCount++;
                }
            }
            NavigationHandleAllocator.EnsureAbove(highest);   // at once (a load); a restore's carried count follows on its first step
            _highestRestored = highest;
        }

        private int _highestRestored;

        /// <summary>Registers the trajectory <paramref name="trace"/> holds whole, under <paramref name="id"/>; false when it cannot.</summary>
        private bool Register(in PathTrace trace, int id)
        {
            if (trace.Exact == 0 || trace.Interpolation != (byte)TrajectoryInterpolation.Linear || trace.Count < 2) return false;
            var points = trace.PointsRO();
            var positions = new System.Numerics.Vector3[points.Length];
            byte[]? traversals = null;
            for (int i = 0; i < points.Length; i++)
            {
                positions[i] = points[i].Position;
                if (points[i].Traversal != 0) (traversals ??= new byte[points.Length])[i] = points[i].Traversal;
            }
            // ⭐ the solver's own registration (PathfindingSolverSystem): Linear, keyed by the handle — the same trajectory, point for point.
            _pool.RegisterTrajectoryWithKey(positions, id, traversals);
            return true;
        }
    }
}
