using System;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Navigation.Modules
{
    /// <summary>
    /// Wraps <see cref="PathfindingSolverSystem"/> into a self-contained <see cref="IEcsModule"/>
    /// that can be installed on dedicated NavigationSolver nodes.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.SlowBackground(int)"/> at 10 Hz —
    /// the solver runs asynchronously on a background thread, reading
    /// <see cref="PathfindingRequestEvent"/>s accumulated from the event bus and publishing
    /// <see cref="PathfindingResultEvent"/>s via <see cref="IEntityCommandBuffer"/>.
    /// Results are materialized on the main thread by
    /// <see cref="PathfindingResultMaterializationSystem"/>.</para>
    /// </summary>
    public sealed class NavigationSolverModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "NavigationSolver";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);

        private readonly RoadNetworkBlob       _roadNetwork;
        private readonly TrajectoryPoolManager _trajectoryPool;

        /// <summary>
        /// Initialises the module with the static road network and shared trajectory pool.
        /// </summary>
        /// <param name="roadNetwork">
        ///   Static road graph blob.  Pass <c>default</c> for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool.  A new (empty) pool is allocated when <c>null</c>.
        /// </param>
        public NavigationSolverModule(RoadNetworkBlob roadNetwork, TrajectoryPoolManager? trajectoryPool = null)
        {
            _roadNetwork    = roadNetwork;
            _trajectoryPool = trajectoryPool ?? new TrajectoryPoolManager();
        }

        /// <summary>
        /// Registers <see cref="PathfindingResultMaterializationSystem"/> so the module host
        /// runs it each frame on the main thread, materializing results before the BTree
        /// Simulation phase.
        /// </summary>
        public void RegisterSystems(ISystemRegistry reg)
        {
            reg.RegisterSystem(new PathfindingResultMaterializationSystem());
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float dt)
        {
            new PathfindingSolverSystem(_roadNetwork, _trajectoryPool).Execute(view, dt);
        }
    }
}
