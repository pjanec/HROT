using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Processes <see cref="PathfindingResultEvent"/>s and wires the resolved trajectory
    /// into the <see cref="EngineBackedPathRegistry"/> and the originating entity's
    /// <see cref="NavState"/> so <c>CarKinematicsSystem</c> can follow the path.
    ///
    /// <para>Runs at <see cref="SystemPhase.Input"/>, same phase as
    /// <c>PathfindingResultMaterializationSystem</c>.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public sealed class EngineBackedPathResponseSystem : IEcsModuleSystem
    {
        private readonly EngineBackedPathRegistry _registry;

        public EngineBackedPathResponseSystem(EngineBackedPathRegistry registry)
        {
            _registry = registry;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var events = view.ReadEvents<PathfindingResultEvent>();
            if (events.IsEmpty) return;
            if (view is not EntityRepository repo) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];
                if (!evt.IsReachable) continue;

                int handle = evt.RouteHandle;

                // Register in path registry (pool already populated by PathfindingSolverSystem).
                _registry.Register(handle,
                    replanCount: 0,
                    totalDistanceMeters: evt.TotalDistanceMeters,
                    primaryBackend: (byte)evt.PrimaryBackend);

                // Wire NavState so CarKinematicsSystem picks up the trajectory.
                // Entity index is packed in the HIGH 32 bits of RequestId.
                int entityIndex = (int)((ulong)evt.RequestId >> 32);
                var entity = repo.GetEntityByIndex(entityIndex);
                if (!repo.IsAlive(entity)) continue;
                if (!repo.HasComponent<NavState>(entity)) continue;
                // ⭐ CE-2245 — a path answered AFTER the move it was planned for was stopped (intent None: the bridge's explicit
                //   stop) must not revive that move. Measured: a civilian shot while walking dropped prone and then crawled
                //   2 m along the old path. The handle stays registered above; only the drive is refused.
                if (repo.IsComponentTypeRegistered<NavigationIntent>() && repo.HasComponent<NavigationIntent>(entity)
                    && repo.GetComponent<NavigationIntent>(entity).Mode == NavigationMode.None)
                    continue;

                ref var navState = ref repo.GetComponentRW<NavState>(entity);
                navState.TrajectoryId = handle;
                navState.Mode         = KinematicsMode.CustomTrajectory;
                navState.ProgressS    = 0f;
                navState.HasArrived   = 0;
                // ⭐ CE-3176 — a MoveTo waited at speed 0 (NavigationIntentBridgeSystem, PathToPoint); it drives at the
                //   requested speed from here. Other requests (a replan, a route) keep the speed they already carry.
                if (repo.IsComponentTypeRegistered<NavigationIntent>() && repo.HasComponent<NavigationIntent>(entity)
                    && repo.GetComponent<NavigationIntent>(entity) is { Mode: NavigationMode.PathToPoint } intent)
                    navState.TargetSpeed = intent.TargetSpeed;
            }
        }
    }
}
