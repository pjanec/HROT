using System.Collections.Generic;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.CarKinem.Systems;

namespace Fdp.Toolkit.CarKinem.Modules
{
    /// <summary>
    /// Grouping for ground-vehicle physics and spatial management systems
    /// (the "Ground Muscle" layer of the Brain/Muscle decomposition).
    ///
    /// <para><b>Systems registered (in order):</b></para>
    /// <list type="number">
    ///   <item><see cref="SpatialHashSystem"/> — builds spatial grid from SimTransform positions</item>
    ///   <item><see cref="FormationTargetSystem"/> — computes formation slot targets</item>
    ///   <item><see cref="VehicleCommandSystem"/> — processes high-level vehicle command events</item>
    ///   <item><see cref="CarKinematicsSystem"/> — vehicle physics for wheeled/tracked entities</item>
    ///   <item><see cref="NavigationExecutionSystem"/> — writes NavigationStatus based on arrival/frustration</item>
    /// </list>
    ///
    /// <para>
    /// Note: <c>LinearKinematicsSystem</c> (from <c>FDP.Toolkit.Physics</c>) cannot be
    /// included here due to a circular project reference
    /// (<c>FDP.Toolkit.Physics</c> already references <c>FDP.Toolkit.CarKinem</c>).
    /// It is registered separately by the <c>SimulationLogicModule</c> facade.
    /// </para>
    ///
    /// <para>
    /// All queries within <see cref="CarKinematicsSystem"/> use
    /// <c>.WithOwned&lt;SimTransform&gt;()</c> rather than manual
    /// <c>NetworkOwnership</c> checks, ensuring correct distributed split-authority
    /// behavior (MOD1 §3.2.5).
    /// </para>
    /// </summary>
    public sealed class GroundKinematicsModule
    {
        private readonly RoadNetworkBlob          _roadNetwork;
        // Lazy-allocated: null until first access via the properties below.
        // This avoids eagerly creating pools for roles that construct this module
        // but never call RegisterSystems (e.g. unit test probing / dry-run inspection).
        private TrajectoryPoolManager?    _trajectoryPool;
        private FormationTemplateManager? _formationTemplates;

        /// <param name="roadNetwork">
        ///   Road network blob for <see cref="CarKinematicsSystem"/>.
        ///   A default (empty) blob is valid for tests and maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool for <see cref="CarKinematicsSystem"/> and
        ///   <see cref="FormationTargetSystem"/>. A new pool is created lazily when <c>null</c>.
        /// </param>
        /// <param name="formationTemplates">
        ///   Formation layout templates for <see cref="FormationTargetSystem"/>.
        ///   A new manager (with default templates) is created lazily when <c>null</c>.
        /// </param>
        /// <summary>Systems that run in the Simulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        /// <summary>Systems that run in the PostSimulation phase.</summary>
        public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }

        public GroundKinematicsModule(
            RoadNetworkBlob roadNetwork = default,
            TrajectoryPoolManager? trajectoryPool = null,
            FormationTemplateManager? formationTemplates = null)
        {
            _roadNetwork        = roadNetwork;
            _trajectoryPool     = trajectoryPool;     // null = lazy-allocate on first use
            _formationTemplates = formationTemplates; // null = lazy-allocate on first use

            // Accessing TrajectoryPool and FormationTemplates here triggers lazy allocation.
            SimulationSystems = new IEcsModuleSystem[]
            {
                new SpatialHashSystem(),
                new FormationTargetSystem(FormationTemplates, TrajectoryPool),
                new VehicleCommandSystem(),
                new NavigationExecutionSystem(),
            };
            PostSimulationSystems = new IEcsModuleSystem[]
            {
                new CarKinematicsSystem(TrajectoryPool),
                new LinearKinematicsSystem(),
            };
        }

        /// <summary>Shared trajectory pool (lazy-allocated on first access when not provided at construction).</summary>
        public TrajectoryPoolManager TrajectoryPool => _trajectoryPool ??= new TrajectoryPoolManager();

        /// <summary>Shared formation-template manager (lazy-allocated on first access when not provided at construction).</summary>
        public FormationTemplateManager FormationTemplates => _formationTemplates ??= new FormationTemplateManager();
    }
}
