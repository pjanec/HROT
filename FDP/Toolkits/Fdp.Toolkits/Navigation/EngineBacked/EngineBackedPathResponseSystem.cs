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

                ref var navState = ref repo.GetComponentRW<NavState>(entity);
                navState.TrajectoryId = handle;
                navState.Mode         = KinematicsMode.CustomTrajectory;
                navState.ProgressS    = 0f;
                navState.HasArrived   = 0;
            }
        }
    }
}
