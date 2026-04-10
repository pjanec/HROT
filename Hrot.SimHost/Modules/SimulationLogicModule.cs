using System;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Systems.Routing;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.CarKinem.Modules;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Combat.Modules;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Navigation.Systems;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Services;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Delegation facade that organises all behaviour, navigation, physics and combat
    /// simulation systems into coherent module groupings. Call-sites are unchanged
    /// (the constructor and <see cref="RegisterSystems"/> signature are preserved);
    /// the internal implementation now delegates to discrete sub-modules.
    ///
    /// <para><b>Sub-module responsibilities:</b></para>
    /// <list type="bullet">
    ///   <item><see cref="CombatModule"/> — fire processing, raycast/hit resolution, perception broadphase, damage, ballistics</item>
    ///   <item><see cref="MissionControlModule"/> — doctrine ingress + mission direction</item>
    ///   <item><see cref="CognitiveRuntimeModule"/> — BTree/HSM tick + channel arbitration</item>
    ///   <item><see cref="ActionDispatchModule"/> — locomotion + weapon dispatchers</item>
    ///   <item><see cref="GroundKinematicsModule"/> — spatial hash, formation, vehicle physics, nav execution</item>
    /// </list>
    ///
    /// System registration order (strict — must not be reordered):
    /// <list type="number">
    ///   <item>Input + Sim + PostSim: <see cref="CombatModule"/> systems</item>
    ///   <item>Sim: <see cref="MissionControlModule"/> systems</item>
    ///   <item>Sim: <see cref="CognitiveRuntimeModule"/> systems</item>
    ///   <item>Sim: <see cref="ActionDispatchModule"/> systems</item>
    ///   <item>Sim: <see cref="GroundKinematicsModule"/> systems (includes LinearKinematicsSystem)</item>
    /// </list>
    /// </summary>
    public class SimulationLogicModule
    {
        private readonly CombatModule?            _combatModule;
        private readonly DamageAssessmentModule?  _damageAssessmentModule;
        private readonly MissionControlModule?    _missionControlModule;
        private readonly CognitiveRuntimeModule?  _cognitiveRuntimeModule;
        private readonly ActionDispatchModule?    _actionDispatchModule;
        private readonly GroundKinematicsModule?  _groundKinematicsModule;
        private readonly NetworkEntityMap         _entityMap;

        /// <summary>
        /// Initialises a new <see cref="SimulationLogicModule"/>.
        /// </summary>
        /// <param name="doctrineRegistry">
        ///   Doctrine registry shared with <see cref="FDP.Toolkit.Behavior.Systems.BTreeTickSystem"/>.
        /// </param>
        /// <param name="entityMap">
        ///   Network entity map shared with <c>JoinFormationExecutor</c>.
        /// </param>
        /// <param name="vehicleAPI">
        ///   High-level vehicle command façade, forwarded to
        ///   <c>JoinFormationExecutor</c> (S4.4). May be <c>null</c> while
        ///   the executor is a stub.
        /// </param>
        /// <param name="roadNetwork">
        ///   Road network blob for <c>CarKinematicsSystem</c>.
        ///   An empty / default blob is valid for testing and for maps without roads.
        /// </param>
        /// <param name="trajectoryPool">
        ///   Shared trajectory pool for <c>CarKinematicsSystem</c> and
        ///   <c>FormationTargetSystem</c>. A new pool is created when <c>null</c>.
        /// </param>
        /// <param name="formationTemplateManager">
        ///   Formation layout templates for <c>FormationTargetSystem</c>.
        ///   A new manager (with default templates) is created when <c>null</c>.
        /// </param>
        /// <param name="role">
        ///   Node role that determines which sub-modules are created.
        ///   Node role that determines which sub-modules are created.
        ///   Defaults to <see cref="NodeRole.AllInOne"/> (all five sub-modules).
        ///   Role → module mapping:
        ///   AllInOne: all six; Brain: Mission+Cognitive+Action (no Combat, no DamageAssessment, no GroundKinematics);
        ///   MuscleGround: Combat+DamageAssessment+Action+GroundKinematics; ImageGenerator/Perception/NavigationSolver: none.
        /// </param>
        public SimulationLogicModule(
            DoctrineRegistry         doctrineRegistry,
            NetworkEntityMap          entityMap,
            VehicleAPI?               vehicleAPI               = null,
            RoadNetworkBlob           roadNetwork              = default,
            TrajectoryPoolManager?    trajectoryPool           = null,
            FormationTemplateManager? formationTemplateManager = null,
            NodeRole                  role                     = NodeRole.AllInOne)
        {
            if (doctrineRegistry == null) throw new ArgumentNullException(nameof(doctrineRegistry));
            if (entityMap == null)        throw new ArgumentNullException(nameof(entityMap));

            _entityMap = entityMap;

            bool hasCombat           = role.HasFlag(NodeRole.MuscleGround);
            bool hasDamageAssessment = role.HasFlag(NodeRole.MuscleGround);
            bool hasMissionControl   = role.HasFlag(NodeRole.Brain);
            bool hasCognitive        = role.HasFlag(NodeRole.Brain);
            bool hasActionDispatch   = role.HasFlag(NodeRole.Brain) || role.HasFlag(NodeRole.MuscleGround);
            bool hasGroundKinem      = role.HasFlag(NodeRole.MuscleGround);

            if (hasCombat)
                _combatModule = new CombatModule();

            if (hasDamageAssessment)
                _damageAssessmentModule = new DamageAssessmentModule();

            if (hasMissionControl)
                _missionControlModule = new MissionControlModule(doctrineRegistry);

            if (hasCognitive)
                _cognitiveRuntimeModule = new CognitiveRuntimeModule(doctrineRegistry);

            if (hasActionDispatch)
                _actionDispatchModule = new ActionDispatchModule(
                    locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                    {
                        (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                        (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
                        (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleAPI, entityMap)),
                    },
                    weaponExecutors: new (ushort, IActionExecutor<WeaponChannel>)[]
                    {
                        (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor()),
                    },
                    interactionExecutors: new (ushort, IActionExecutor<InteractionChannel>)[]
                    {
                        (BehaviorConstants.ActionIdEjectPassengers, new EjectPassengersExecutor()),
                    });

            if (hasGroundKinem)
                _groundKinematicsModule = new GroundKinematicsModule(
                    roadNetwork,
                    trajectoryPool,
                    formationTemplateManager);

            RoadNetwork = roadNetwork;
        }

        // ── Public accessors for shared resources ────────────────────────────────
        // Exposed so that SimHostVisualization (and unit tests) can share the same
        // instances that were wired into the simulation systems.

        /// <summary>
        /// Returns the shared trajectory pool, or <see langword="null"/> when the role
        /// does not include ground kinematics (e.g. Brain or ImageGenerator).
        /// </summary>
        public TrajectoryPoolManager? TrajectoryPool => _groundKinematicsModule?.TrajectoryPool;

        /// <summary>
        /// Returns the shared formation-template manager, or <see langword="null"/> when the role
        /// does not include ground kinematics.
        /// </summary>
        public FormationTemplateManager? FormationTemplates => _groundKinematicsModule?.FormationTemplates;

        /// <summary>Road-network blob (used by CarKinematicsSystem and visualization).</summary>
        public RoadNetworkBlob RoadNetwork { get; }

        /// <summary>
        /// Registers all simulation-logic systems in strict execution order by
        /// delegating to the specialist sub-modules and adding the remaining
        /// combat/perception/ballistics systems inline (pending a future CombatModule).
        /// </summary>
        /// <param name="inputGroup">Input-phase systems (fire/raycast/hit processing).</param>
        /// <param name="simGroup">Main simulation systems.</param>
        /// <param name="postSimGroup">Post-simulation systems (ballistics).</param>
        public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)
        {
            if (inputGroup   == null) throw new ArgumentNullException(nameof(inputGroup));
            if (simGroup     == null) throw new ArgumentNullException(nameof(simGroup));
            if (postSimGroup == null) throw new ArgumentNullException(nameof(postSimGroup));

            // Combat (present for all sim roles).
            _combatModule?.RegisterSystems(inputGroup, simGroup, postSimGroup, _entityMap);

            // Brain tier: doctrine + cognitive (omitted on MuscleGround / IG / leaf nodes).
            _missionControlModule?.RegisterSystems(simGroup);
            _cognitiveRuntimeModule?.RegisterSystems(simGroup);

            // Action dispatch (present for Brain, MuscleGround, AllInOne).
            _actionDispatchModule?.RegisterSystems(simGroup);

            // DamageAssessment (Muscle and AllInOne): consumes DetonationNotification → DamageAssessedEvent.
            _damageAssessmentModule?.RegisterSystems(simGroup);

            // NavigationIntentBridgeSystem: translates NavigationIntent → NavState for
            // CarKinematicsSystem. Only added when ground kinematics are present.
            if (_groundKinematicsModule != null)
            {
                simGroup.AddSystem(new NavigationIntentBridgeSystem());
                // Route → trajectory pool sync. Runs in BeforeSync phase (before kinematics).
                simGroup.AddSystem(new RouteTrajectorySyncSystem(_groundKinematicsModule.TrajectoryPool!));
                // Personal route authoring: processes CmdAppendPersonalWaypoint events.
                inputGroup.AddSystem(new PersonalRouteAuthoringSystem());
                // Route context: writes per-waypoint ExtensionJson advice to BrainBlackboard.
                simGroup.AddSystem(new RouteContextSystem());
            }

            // Ground kinematics (MuscleGround / AllInOne).
            _groundKinematicsModule?.RegisterSystems(simGroup);
        }
    }
}
