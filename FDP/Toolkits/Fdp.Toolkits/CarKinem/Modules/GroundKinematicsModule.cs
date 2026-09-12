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
    public sealed class GroundKinematicsModule : System.IDisposable
    {
        private readonly RoadNetworkBlob          _roadNetwork;

        // ⚠ These are allocated by the CONSTRUCTOR, not on first property access: the system arrays
        // below read TrajectoryPool and FormationTemplates while being built. (An earlier comment here
        // claimed the lazy properties avoided eager allocation "for roles that never call
        // RegisterSystems" — that was never true, and it mattered, because it hid the fact that merely
        // CONSTRUCTING this module claims persistent native memory.)
        private TrajectoryPoolManager?    _trajectoryPool;
        private FormationTemplateManager? _formationTemplates;

        // OWNED vs BORROWED (B3). A pool passed in belongs to the composition root and outlives this
        // module; a pool defaulted here belongs to this module and must be freed by it. Freeing a
        // borrowed pool is the half that CORRUPTS rather than merely leaks — every other consumer
        // (PathfindingSolverSystem, FormationTargetSystem, CarKinematicsSystem) still holds it.
        private readonly bool _ownsTrajectoryPool;
        private readonly bool _ownsFormationTemplates;
        private bool _disposed;

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
            _roadNetwork            = roadNetwork;
            _trajectoryPool         = trajectoryPool;
            _formationTemplates     = formationTemplates;
            _ownsTrajectoryPool     = trajectoryPool is null;
            _ownsFormationTemplates = formationTemplates is null;

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

        /// <summary>Shared trajectory pool (allocated by the constructor when not provided).</summary>
        public TrajectoryPoolManager TrajectoryPool => _trajectoryPool ??= new TrajectoryPoolManager();

        /// <summary>Shared formation-template manager (allocated by the constructor when not provided).</summary>
        public FormationTemplateManager FormationTemplates => _formationTemplates ??= new FormationTemplateManager();

        /// <summary>
        /// <c>true</c> when this module allocated <see cref="TrajectoryPool"/> itself and is therefore
        /// responsible for freeing it. <c>false</c> when the pool was handed in and is merely borrowed.
        /// </summary>
        /// <remarks>
        /// Exposed so a composition root — and the rail that guards it — can assert the intended
        /// ownership rather than infer it. A node selecting both <c>MuscleGround</c> and
        /// <c>NavigationSolver</c> must share ONE pool: <c>PathfindingSolverSystem</c> writes routes
        /// into it while <c>FormationTargetSystem</c> and <c>CarKinematicsSystem</c> read them back,
        /// so two pools mean routes that resolve and vehicles that never follow them — silently, with
        /// no exception.
        /// </remarks>
        public bool OwnsTrajectoryPool => _ownsTrajectoryPool;

        /// <summary><c>true</c> when this module allocated <see cref="FormationTemplates"/> itself.</summary>
        public bool OwnsFormationTemplates => _ownsFormationTemplates;

        /// <summary>
        /// Frees the trajectory pool and formation templates <b>only if this module allocated them</b>.
        /// </summary>
        /// <remarks>
        /// Borrowed instances belong to the composition root and outlive this module; freeing one would
        /// leave every other consumer holding released native memory. Safe to call more than once.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ownsTrajectoryPool)     _trajectoryPool?.Dispose();
            if (_ownsFormationTemplates) _formationTemplates?.Dispose();
        }
    }
}
