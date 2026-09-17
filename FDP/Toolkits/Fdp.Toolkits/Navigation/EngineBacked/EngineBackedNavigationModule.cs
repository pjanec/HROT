using System;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// All-in-one ECS module wiring engine-backed navigation providers.
    /// Mutually exclusive with NavigationFakesModule.
    /// </summary>
    public sealed class EngineBackedNavigationModule : IEcsModule, IDisposable
    {
        /// <inheritdoc/>
        public string Name => "EngineBackedNavigationModule";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        /// <summary>
        /// ⚠ MEASURED DEAD (2026-09-17): assigned here and read by NOTHING in this class — this module
        /// does no road-graph work of its own (<see cref="Tick"/> is empty; the providers it registers
        /// are navmesh/volumetric/crowd). It is therefore NOT an instance of the frozen-constructor-blob
        /// hazard that <c>PathfindingSolverSystem</c> had, contrary to what
        /// <c>DESIGN_Terrain_Zones_And_Assets.md</c> §4 assumed: there is no stale read to fix because
        /// there is no read. The parameter is kept so the constructor signature stays stable for the
        /// composition roots that already pass a blob.
        /// ⭐ The live road graph is the <c>ZoneEnvironmentData</c> singleton, re-read per tick by
        /// <c>CarKinematicsSystem</c> and (as of R2) <c>PathfindingSolverSystem</c>.
        /// </summary>
        private readonly RoadNetworkBlob        _roadNetwork;

        private readonly TrajectoryPoolManager  _pool;

        private EngineBackedNavmeshProvider?        _navmesh;
        private EngineBackedDtCrowdProvider?        _crowd;
        private EngineBackedVolumetricPathProvider? _volumetric;
        private EngineBackedPathRegistry?           _registry;
        private EngineBackedPathResponseSystem?     _responseSystem;

        public EngineBackedNavigationModule(RoadNetworkBlob roadNetwork, TrajectoryPoolManager pool)
        {
            _roadNetwork = roadNetwork;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <inheritdoc/>
        public void RegisterSystems(ISystemRegistry reg)
        {
            _navmesh        = new EngineBackedNavmeshProvider();
            _crowd          = new EngineBackedDtCrowdProvider();
            _volumetric     = new EngineBackedVolumetricPathProvider();
            _registry       = new EngineBackedPathRegistry(_pool);
            _responseSystem = new EngineBackedPathResponseSystem(_registry);

            reg.RegisterSystem(_responseSystem);
        }

        /// <summary>
        /// Register providers into the ECS world. Throws if providers are already registered
        /// (mutual exclusion with NavigationFakesModule).
        /// </summary>
        public void RegisterProviders(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            // Mutual exclusion guard.
            if (repo.HasSingletonManaged<INavmeshProvider>())
                throw new InvalidOperationException(
                    "Navigation providers are already registered. " +
                    "Only one of EngineBackedNavigationModule / NavigationFakesModule may be active.");

            // RegisterSystems must have been called first.
            if (_navmesh == null || _registry == null)
                throw new InvalidOperationException(
                    "Call RegisterSystems before RegisterProviders.");

            repo.SetSingletonManaged<INavmeshProvider>(_navmesh);
            repo.SetSingletonManaged<IPathRegistry>(_registry);
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float deltaTime)
        {
            // No work here — systems handle everything.
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Road network and pool are owned by the host.
        }
    }
}
