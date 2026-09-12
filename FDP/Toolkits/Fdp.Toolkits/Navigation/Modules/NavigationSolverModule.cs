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

        private readonly RoadNetworkBlob         _roadNetwork;
        private readonly TrajectoryPoolManager   _trajectoryPool;
        private readonly INavmeshProvider?       _navmesh;
        private readonly IVolumetricPathProvider? _volumetric;

        /// <summary>
        /// Initialises the module with the static road network and shared trajectory pool.
        /// </summary>
        /// <param name="roadNetwork">
        ///   Static road graph blob.  Pass <c>default</c> for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   <b>Required.</b> The shared trajectory pool this node's <c>MuscleGround</c> capability also
        ///   reads. See the remarks — passing <c>null</c> is rejected rather than defaulted.
        /// </param>
        /// <param name="navmesh">Optional navmesh provider forwarded to the solver.</param>
        /// <param name="volumetric">Optional volumetric provider forwarded to the solver.</param>
        /// <remarks>
        /// <para><b><c>B3</c> — why the pool is required and no longer defaults.</b> This parameter used
        /// to read <c>trajectoryPool ?? new TrajectoryPoolManager()</c>. That silent default is safe only
        /// while nothing constructs this module — and nothing does today, which is precisely why it was
        /// never noticed. Role-based composition is about to switch it on.</para>
        ///
        /// <para>The failure it would produce is <b>not</b> a leak. <c>PathfindingSolverSystem</c> (this
        /// module) writes resolved routes into the pool; <c>FormationTargetSystem</c> and
        /// <c>CarKinematicsSystem</c> (<c>GroundKinematicsModule</c>, the <c>MuscleGround</c> capability)
        /// read them back by handle. A node selecting both roles without threading one pool between them
        /// gets two, and then <b>routes resolve and vehicles never follow them</b> — silently, with no
        /// exception and nothing in a log.</para>
        ///
        /// <para><c>EngineBackedNavigationModule</c> — the navigation module actually in production — has
        /// required its pool from the start and its <c>Dispose</c> deliberately frees nothing because
        /// "the pool is owned by the host". That is the shape; this constructor now matches it.</para>
        /// </remarks>
        public NavigationSolverModule(
            RoadNetworkBlob          roadNetwork,
            TrajectoryPoolManager    trajectoryPool,
            INavmeshProvider?        navmesh        = null,
            IVolumetricPathProvider? volumetric     = null)
        {
            _roadNetwork    = roadNetwork;
            _trajectoryPool = trajectoryPool
                ?? throw new System.ArgumentNullException(
                    nameof(trajectoryPool),
                    "NavigationSolver must share the node's trajectory pool with MuscleGround; a private "
                  + "pool would make routes resolve into memory the kinematics systems never read.");
            _navmesh        = navmesh;
            _volumetric     = volumetric;
        }

        /// <summary>The pool this module reads and writes — exposed so a composition rail can assert
        /// that it is the same instance the node's <c>MuscleGround</c> capability holds.</summary>
        public TrajectoryPoolManager TrajectoryPool => _trajectoryPool;

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
            new PathfindingSolverSystem(_roadNetwork, _trajectoryPool, _navmesh, _volumetric)
                .Execute(view, dt);
        }
    }
}
