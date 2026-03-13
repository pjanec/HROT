using System;
using Bagira.SimHost.Systems;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Modules
{
    /// <summary>
    /// Registers all behavior, navigation, and physics simulation systems (TASK-S4.1).
    ///
    /// System registration order (strict — must not be reordered):
    /// <list type="number">
    ///   <item><see cref="MissionDirectorSystem"/> — advances MissionPlanQueue phases</item>
    ///   <item><see cref="ChannelArbitrationSystem"/> — preempts stale channels on doctrine change</item>
    ///   <item><see cref="BTreeTickSystem"/> — zero-alloc BTree tick per entity</item>
    ///   <item><see cref="LocomotionDispatcherSystem"/> + executors: MoveTo, FollowRoute, JoinFormation (stub)</item>
    ///   <item><see cref="SpatialHashSystem"/> — builds spatial grid from SimTransform positions</item>
    ///   <item><see cref="FormationTargetSystem"/> — computes formation slot targets</item>
    ///   <item><see cref="VehicleCommandSystem"/> — processes high-level vehicle command events</item>
    ///   <item><see cref="CarKinematicsSystem"/> — vehicle physics (wheeled/tracked, uses VehicleState)</item>
    ///   <item><see cref="LinearKinematicsSystem"/> — position integration for non-wheeled entities</item>
    /// </list>
    /// <para>
    /// GeoSpatial egress is handled automatically: <see cref="EntityRepository.GetUnmanagedComponentRW{T}"/>
    /// stamps <c>EntityHeader.LastChangeTick</c>, and <c>SmartEgressUtil.ShouldPublish</c> compares
    /// that against the last-published tick — no extra dirty-marking system is needed.
    /// </para>
    /// </summary>
    public class SimulationLogicModule
    {
        private readonly DoctrineRegistry       _doctrineRegistry;
        private readonly NetworkEntityMap        _entityMap;
        private readonly VehicleAPI?             _vehicleAPI;
        private readonly RoadNetworkBlob         _roadNetwork;
        private readonly TrajectoryPoolManager   _trajectoryPool;
        private readonly FormationTemplateManager _formationTemplateManager;

        /// <summary>
        /// Initialises a new <see cref="SimulationLogicModule"/>.
        /// </summary>
        /// <param name="doctrineRegistry">
        ///   Doctrine registry shared with <see cref="BTreeTickSystem"/>.
        /// </param>
        /// <param name="entityMap">
        ///   Network entity map shared with <see cref="JoinFormationExecutor"/>.
        /// </param>
        /// <param name="vehicleAPI">
        ///   High-level vehicle command façade, forwarded to
        ///   <see cref="JoinFormationExecutor"/> (S4.4). May be <c>null</c> while
        ///   the executor is a stub.
        /// </param>
        /// <param name="roadNetwork">
        ///   Road network blob for <see cref="CarKinematicsSystem"/>.
        ///   An empty / default blob is valid for testing and for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool for <see cref="CarKinematicsSystem"/> and
        ///   <see cref="FormationTargetSystem"/>. A new pool is created when <c>null</c>.
        /// </param>
        /// <param name="formationTemplateManager">
        ///   Formation layout templates for <see cref="FormationTargetSystem"/>.
        ///   A new manager (with default templates) is created when <c>null</c>.
        /// </param>
        public SimulationLogicModule(
            DoctrineRegistry        doctrineRegistry,
            NetworkEntityMap         entityMap,
            VehicleAPI?              vehicleAPI              = null,
            RoadNetworkBlob          roadNetwork             = default,
            TrajectoryPoolManager?   trajectoryPool          = null,
            FormationTemplateManager? formationTemplateManager = null)
        {
            _doctrineRegistry       = doctrineRegistry       ?? throw new ArgumentNullException(nameof(doctrineRegistry));
            _entityMap              = entityMap              ?? throw new ArgumentNullException(nameof(entityMap));
            _vehicleAPI             = vehicleAPI;
            _roadNetwork            = roadNetwork;
            _trajectoryPool         = trajectoryPool         ?? new TrajectoryPoolManager();
            _formationTemplateManager = formationTemplateManager ?? new FormationTemplateManager();
        }

        // ── Public accessors for shared resources ────────────────────────────────
        // Exposed so that SimHostVisualization (and unit tests) can share the same
        // instances that were wired into the simulation systems.

        /// <summary>Shared trajectory pool (used by CarKinematicsSystem and visualization).</summary>
        public TrajectoryPoolManager TrajectoryPool => _trajectoryPool;

        /// <summary>Shared formation-template manager (used by FormationTargetSystem).</summary>
        public FormationTemplateManager FormationTemplates => _formationTemplateManager;

        /// <summary>Road-network blob (used by CarKinematicsSystem and visualization).</summary>
        public RoadNetworkBlob RoadNetwork => _roadNetwork;

        /// <summary>
        /// Registers all simulation-logic systems to the provided system groups in
        /// strict execution order. Each group must already be initialised (<c>Create</c>)
        /// with the target <see cref="EntityRepository"/> before this method is invoked.
        /// </summary>
        /// <param name="inputGroup">Input-phase systems (fire/raycast/hit processing).</param>
        /// <param name="simGroup">Main simulation systems.</param>
        /// <param name="postSimGroup">Post-simulation systems (ballistics).</param>
        public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)
        {
            if (inputGroup == null) throw new ArgumentNullException(nameof(inputGroup));
            if (simGroup == null) throw new ArgumentNullException(nameof(simGroup));
            if (postSimGroup == null) throw new ArgumentNullException(nameof(postSimGroup));

            // ── Input phase ───────────────────────────────────────────────────
            inputGroup.AddSystem(new FireProcessingSystem());
            inputGroup.AddSystem(new RaycastSolverSystem());
            inputGroup.AddSystem(new HitResolutionSystem());

            // ── 1. MissionDirectorSystem ────────────────────────────────────────
            // Evaluates MissionPlanQueue triggers and advances doctrine phases.
            simGroup.AddSystem(new MissionDirectorSystem());
            simGroup.AddSystem(new DoctrineIngressSystem(_doctrineRegistry));
            // ── 2. Channel arbitration ───────────────────────────────────────────
            // Preempts stale locomotion / weapon / interaction channels when the
            // active doctrine instance changes.
            simGroup.AddSystem(new ChannelArbitrationSystem());

            // ── 3. BTree tick ────────────────────────────────────────────────────
            // Steps each entity's FastBTree interpreter (zero allocation per tick).
            simGroup.AddSystem(new BTreeTickSystem(_doctrineRegistry));

            // ── 3b. Combat systems (inserted after BTree tick) ───────────────────
            var weaponSys = new WeaponDispatcherSystem();
            weaponSys.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor());
            simGroup.AddSystem(weaponSys);

            simGroup.AddSystem(new PerceptionBroadphaseSystem());
            simGroup.AddSystem(new LosRequestBatchingSystem());
            simGroup.AddSystem(new ThreatEvaluationAdapterSystem());
            simGroup.AddSystem(new DamageSystem());
            simGroup.AddSystem(new HsmDamageBridgeSystem());
            simGroup.AddSystem(new HsmTickSystem<BrainHsm128>(_doctrineRegistry));

            // ── 4. Locomotion dispatcher + executors ─────────────────────────────
            // Handles OnEnter / Execute / OnExit lifecycle for active locomotion actions.
            var locoDispatcher = new LocomotionDispatcherSystem();
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdMoveTo,      new MoveToExecutor());
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdFollowRoute, new FollowRouteExecutor());
            // JoinFormationExecutor — registered now that full logic is implemented (TASK-S4.4).
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdJoinFormation,
                new JoinFormationExecutor(_vehicleAPI, _entityMap));
            simGroup.AddSystem(locoDispatcher);

            // ── 5. Spatial hash ──────────────────────────────────────────────────
            // Builds the SpatialHashGrid singleton from SimTransform positions every
            // frame. Must run before CarKinematicsSystem.
            simGroup.AddSystem(new SpatialHashSystem());

            // ── 6. Formation target ──────────────────────────────────────────────
            // Computes target slot positions for formation members.
            simGroup.AddSystem(new FormationTargetSystem(_formationTemplateManager, _trajectoryPool));

            // ── 7. Vehicle command ───────────────────────────────────────────────
            // Processes high-level vehicle command events (spawn, navigate, formation, etc.).
            simGroup.AddSystem(new VehicleCommandSystem());

            // ── 8. Car kinematics ────────────────────────────────────────────────
            // Main vehicle physics for wheeled/tracked entities (requires VehicleState).
            simGroup.AddSystem(new CarKinematicsSystem(_roadNetwork, _trajectoryPool));

            // ── 9. Linear kinematics ─────────────────────────────────────────────
            // Integrates position via SimVelocity for entities WITHOUT VehicleState
            // (infantry, projectiles, etc.). The [UpdateBefore(SpatialHashSystem)]
            // attribute on LinearKinematicsSystem ensures updated positions are
            // consumed by SpatialHashSystem next frame.
            simGroup.AddSystem(new LinearKinematicsSystem());

            // ── Post-simulation ───────────────────────────────────────────────
            postSimGroup.AddSystem(new BallisticsSystem());
        }
    }
}
