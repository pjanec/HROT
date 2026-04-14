using System;
using CarKinem.Road;
using CarKinem.Trajectory;
using FDP.Toolkit.Navigation.Systems;
using Fdp.ModuleHost.Abstractions;

namespace FDP.Toolkit.Navigation.Modules
{
    /// <summary>
    /// Wraps <see cref="PathfindingSolverSystem"/> into a self-contained <see cref="IEcsModule"/>
    /// that can be installed on dedicated NavigationSolver nodes.
    ///
    /// <para><b>Execution model:</b> <see cref="ExecutionPolicy.Synchronous"/> — the solver
    /// runs on the main simulation thread after the Brain tier has submitted
    /// <see cref="PathRequest"/>s into <see cref="PathfindingBatchData"/>.</para>
    /// </summary>
    public sealed class NavigationSolverModule : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "NavigationSolver";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

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
        /// Registers <see cref="PathfindingSolverSystem"/> into the kernel registry.
        /// </summary>
        public void RegisterSystems(ISystemRegistry reg)
        {
            reg.RegisterSystem(new PathfindingSolverSystem(_roadNetwork, _trajectoryPool));
        }

        /// <inheritdoc/>
        public void Tick(ISimulationView view, float dt) { }
    }
}
