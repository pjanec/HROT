using System;
using Bagira.SimHost.Systems;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Modules
{
    /// <summary>
    /// Registers all behavior, navigation, and physics simulation systems (TASK-S4.1).
    ///
    /// System registration order (strict — must not be reordered):
    /// <list type="number">
    ///   <item><see cref="MissionAdapterSystem"/> — stub, fully implemented in S4.3</item>
    ///   <item><see cref="ChannelArbitrationSystem"/> — preempts stale channels on doctrine change</item>
    ///   <item><see cref="BTreeTickSystem"/> — zero-alloc BTree tick per entity</item>
    ///   <item><see cref="LocomotionDispatcherSystem"/> + executors: MoveTo, FollowRoute, JoinFormation (stub)</item>
    ///   <item><see cref="SpatialHashSystem"/> — builds spatial grid from SimTransform positions</item>
    ///   <item><see cref="FormationTargetSystem"/> — computes formation slot targets</item>
    ///   <item><see cref="VehicleCommandSystem"/> — processes high-level vehicle command events</item>
    ///   <item><see cref="CarKinematicsSystem"/> — vehicle physics (wheeled/tracked, uses VehicleState)</item>
    ///   <item><see cref="LinearKinematicsSystem"/> — position integration for non-wheeled entities</item>
    /// </list>
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
        ///   Doctrine registry shared with <see cref="BTreeTickSystem"/>
        ///   and (when fully implemented) <see cref="MissionAdapterSystem"/>.
        /// </param>
        /// <param name="entityMap">
        ///   Network entity map shared with <see cref="MissionAdapterSystem"/>
        ///   and (when fully implemented) <see cref="JoinFormationExecutor"/>.
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

        /// <summary>
        /// Registers all simulation-logic systems to <paramref name="group"/> in strict
        /// execution order. The group must already be initialised (<c>Create</c> called)
        /// with the target <see cref="EntityRepository"/> before this method is invoked.
        /// </summary>
        /// <param name="group">Destination <see cref="SystemGroup"/>.</param>
        public void RegisterSystems(SystemGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));

            // ── 1. MissionAdapterSystem ──────────────────────────────────────────
            // Stub — full implementation in TASK-S4.3.
            group.AddSystem(new MissionAdapterSystem(_doctrineRegistry, _entityMap));

            // ── 2. Channel arbitration ───────────────────────────────────────────
            // Preempts stale locomotion / weapon / interaction channels when the
            // active doctrine instance changes.
            group.AddSystem(new ChannelArbitrationSystem());

            // ── 3. BTree tick ────────────────────────────────────────────────────
            // Steps each entity's FastBTree interpreter (zero allocation per tick).
            group.AddSystem(new BTreeTickSystem(_doctrineRegistry));

            // ── 4. Locomotion dispatcher + executors ─────────────────────────────
            // Handles OnEnter / Execute / OnExit lifecycle for active locomotion actions.
            var locoDispatcher = new LocomotionDispatcherSystem();
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdMoveTo,      new MoveToExecutor());
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdFollowRoute, new FollowRouteExecutor());
            // JoinFormationExecutor — registered now that full logic is implemented (TASK-S4.4).
            locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdJoinFormation,
                new JoinFormationExecutor(_vehicleAPI, _entityMap));
            group.AddSystem(locoDispatcher);

            // ── 5. Spatial hash ──────────────────────────────────────────────────
            // Builds the SpatialHashGrid singleton from SimTransform positions every
            // frame. Must run before CarKinematicsSystem.
            group.AddSystem(new SpatialHashSystem());

            // ── 6. Formation target ──────────────────────────────────────────────
            // Computes target slot positions for formation members.
            group.AddSystem(new FormationTargetSystem(_formationTemplateManager, _trajectoryPool));

            // ── 7. Vehicle command ───────────────────────────────────────────────
            // Processes high-level vehicle command events (spawn, navigate, formation, etc.).
            group.AddSystem(new VehicleCommandSystem());

            // ── 8. Car kinematics ────────────────────────────────────────────────
            // Main vehicle physics for wheeled/tracked entities (requires VehicleState).
            group.AddSystem(new CarKinematicsSystem(_roadNetwork, _trajectoryPool));

            // ── 9. Linear kinematics ─────────────────────────────────────────────
            // Integrates position via SimVelocity for entities WITHOUT VehicleState
            // (infantry, projectiles, etc.). The [UpdateBefore(SpatialHashSystem)]
            // attribute on LinearKinematicsSystem ensures updated positions are
            // consumed by SpatialHashSystem next frame.
            group.AddSystem(new LinearKinematicsSystem());
        }
    }
}
