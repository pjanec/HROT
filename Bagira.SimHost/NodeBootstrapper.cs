using System.Collections.Generic;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Modules.Orchestration;
using Bagira.SimHost.Network;
using CarKinem.Commands;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.CarKinem.Modules;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Combat.Modules;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Navigation.Modules;
using FDP.Toolkit.Perception.Modules;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;

namespace Bagira.SimHost
{
    /// <summary>
    /// Role-based composition root that replaces the monolithic initialisation block
    /// in <c>SimHostApp.OnLoad</c>.
    ///
    /// <para>
    /// <see cref="Bootstrap"/> populates the <see cref="RegisteredModules"/> list with
    /// the sub-module instances appropriate for the given <see cref="NodeRole"/>, enabling
    /// tests to assert module presence/absence without running a full DDS stack.
    /// </para>
    ///
    /// <para><b>Role → module mapping:</b></para>
    /// <list type="table">
    ///   <listheader><term>Role</term><description>Module set</description></listheader>
    ///   <item><term>AllInOne</term><description>All six modules.</description></item>
    ///   <item><term>Brain</term><description>MissionControl + CognitiveRuntime + ActionDispatch (no Combat, no GroundKinematics).</description></item>
    ///   <item><term>MuscleGround</term><description>ActionDispatch + GroundKinematics + Combat + DamageAssessment (no MissionControl or CognitiveRuntime).</description></item>
    ///   <item><term>ImageGenerator</term><description>No simulation modules (presentation only).</description></item>
    /// </list>
    /// </summary>
    public sealed class NodeBootstrapper
    {
        private readonly List<object> _registeredModules = new();

        /// <summary>
        /// The sub-module instances that were registered during the last
        /// <see cref="BuildSimulationLogic"/> call. Populated in dependency order.
        /// </summary>
        public IReadOnlyList<object> RegisteredModules => _registeredModules;

        // ── Module construction ───────────────────────────────────────────────

        /// <summary>
        /// Creates and tracks the role-appropriate simulation sub-modules, then
        /// returns a fully wired <see cref="SimulationLogicModule"/> for use with
        /// <see cref="ModuleHostKernel"/> registration.
        /// </summary>
        /// <param name="role">Node role that determines which modules are included.</param>
        /// <param name="doctrineRegistry">Doctrine definitions registry (required for Brain-tier modules).</param>
        /// <param name="entityMap">Shared network entity map (required for ActionDispatch).</param>
        /// <param name="vehicleApi">Optional high-level vehicle command façade.</param>
        /// <param name="roadNetwork">Road-network blob for CarKinematicsSystem.</param>
        /// <param name="trajectoryPool">Shared trajectory pool; a new pool is created when <c>null</c>.</param>
        /// <param name="formationTemplates">Formation templates; defaults are created when <c>null</c>.</param>
        public SimulationLogicModule BuildSimulationLogic(
            NodeRole                  role,
            DoctrineRegistry          doctrineRegistry,
            NetworkEntityMap          entityMap,
            VehicleAPI?               vehicleApi         = null,
            RoadNetworkBlob           roadNetwork        = default,
            TrajectoryPoolManager?    trajectoryPool     = null,
            FormationTemplateManager? formationTemplates = null)
        {
            _registeredModules.Clear();

            // Dedicated Perception solver — only autonomous perception systems.
            if (role == NodeRole.Perception)
            {
                _registeredModules.Add(new AutonomousPerceptionModule());
                return new SimulationLogicModule(
                    doctrineRegistry,
                    entityMap,
                    vehicleApi,
                    roadNetwork,
                    trajectoryPool,
                    formationTemplates,
                    NodeRole.Perception);
            }

            // Dedicated NavigationSolver — only path computation.
            if (role == NodeRole.NavigationSolver)
            {
                _registeredModules.Add(new NavigationSolverModule(roadNetwork, trajectoryPool));
                return new SimulationLogicModule(
                    doctrineRegistry,
                    entityMap,
                    vehicleApi,
                    roadNetwork,
                    trajectoryPool,
                    formationTemplates,
                    NodeRole.NavigationSolver);
            }

            // Brain tier — absent on MuscleGround (receives NavigationIntent via DDS instead).
            if (role != NodeRole.MuscleGround && role != NodeRole.ImageGenerator)
            {
                _registeredModules.Add(new MissionControlModule(doctrineRegistry));
                _registeredModules.Add(new CognitiveRuntimeModule(doctrineRegistry));
            }

            // Action dispatch — present on Brain, MuscleGround, AllInOne.
            if (role != NodeRole.ImageGenerator)
            {
                _registeredModules.Add(new ActionDispatchModule(
                    locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                    {
                        (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                        (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
                        (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleApi, entityMap)),
                    },
                    weaponExecutors: new (ushort, IActionExecutor<WeaponChannel>)[]
                    {
                        (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor(entityMap)),
                    }));
            }

            // Ground kinematics — absent on Brain (movement is handled by remote Muscle).
            if (role != NodeRole.Brain && role != NodeRole.ImageGenerator)
            {
                _registeredModules.Add(new GroundKinematicsModule(roadNetwork, trajectoryPool, formationTemplates));
            }

            // Combat — present on Muscle and AllInOne; absent on Brain (no ballistics on the Brain tier).
            if (role != NodeRole.ImageGenerator && role != NodeRole.Brain)
            {
                _registeredModules.Add(new CombatModule());
            }

            // DamageAssessment — collocated with Muscle; also present on AllInOne.
            if (role != NodeRole.Brain && role != NodeRole.ImageGenerator)
            {
                _registeredModules.Add(new DamageAssessmentModule());
            }

            // Return a SimulationLogicModule with role-filtered sub-module construction.
            return new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleApi,
                roadNetwork,
                trajectoryPool,
                formationTemplates,
                role);
        }

        // ── Translator construction ───────────────────────────────────────────

        /// <summary>
        /// Creates the role-appropriate DDS translator instances using the three
        /// static translator packs.
        /// </summary>
        /// <param name="role">Node role that determines which translator packs are installed.</param>
        /// <param name="participant">Live DDS participant.</param>
        /// <param name="entityMap">Shared network entity map.</param>
        /// <param name="geoTransform">Geodetic transform for coordinate conversion.</param>
        /// <param name="eventBus">Application event bus (needed by EntityMasterIngressTranslator).</param>
        /// <param name="ghostCreationSystem">Ghost-creation system for replica entity materialisation.</param>
        /// <param name="doctrineRegistry">Doctrine registry forwarded to EntityMissionEgressTranslator.</param>
        /// <param name="localNodeId">Local DDS node identifier.</param>
        public List<IDescriptorTranslator> BuildTranslators(
            NodeRole             role,
            DdsParticipant       participant,
            NetworkEntityMap     entityMap,
            IGeographicTransform geoTransform,
            FdpEventBus          eventBus,
            GhostCreationSystem  ghostCreationSystem,
            DoctrineRegistry?    doctrineRegistry,
            long                 localNodeId)
        {
            var translators = new List<IDescriptorTranslator>();

            // Shared pack — all roles install entity lifecycle translators.
            translators.AddRange(SharedTranslatorPack.Create(
                participant, entityMap, localNodeId, eventBus, ghostCreationSystem));

            // Kinematic pack — Muscle nodes publish NavStatus and receive NavIntent.
            if (role != NodeRole.Brain && role != NodeRole.ImageGenerator)
            {
                translators.AddRange(KinematicTranslatorPack.Create(
                    participant, entityMap, geoTransform));
            }

            // Cognitive pack — Brain nodes publish NavIntent and receive NavStatus.
            if (role != NodeRole.MuscleGround && role != NodeRole.ImageGenerator
                && role != NodeRole.Perception && role != NodeRole.NavigationSolver)
            {
                translators.AddRange(CognitiveTranslatorPack.Create(
                    participant, entityMap, geoTransform, doctrineRegistry, ghostCreationSystem));
            }

            // Brain perception pack — Brain/AllInOne publish sensor config + raycast batches.
            if (role == NodeRole.Brain || role == NodeRole.AllInOne)
            {
                translators.AddRange(BrainPerceptionTranslatorPack.Create(
                    participant, entityMap, geoTransform));
            }

            // Brain pathfinding pack — Brain/AllInOne publish path request batches.
            if (role == NodeRole.Brain || role == NodeRole.AllInOne)
            {
                translators.AddRange(BrainPathfindingTranslatorPack.Create(
                    participant, entityMap, geoTransform));
            }

            // Sim perception pack — Perception solver nodes receive requests and publish targets.
            if (role == NodeRole.Perception || role == NodeRole.AllInOne)
            {
                translators.AddRange(SimPerceptionTranslatorPack.Create(
                    participant, entityMap, geoTransform));
            }

            // Sim pathfinding pack — NavigationSolver nodes receive requests and publish results.
            if (role == NodeRole.NavigationSolver || role == NodeRole.AllInOne)
            {
                translators.AddRange(SimPathfindingTranslatorPack.Create(
                    participant, entityMap, geoTransform));
            }

            return translators;
        }

        // ── Orchestration construction ─────────────────────────────────────────

        /// <summary>
        /// Creates a <see cref="DrillSlave"/> with role-appropriate
        /// <see cref="IDsmHandler"/> registrations.
        /// <para>
        /// When <paramref name="participant"/> is provided the slave publishes
        /// heartbeats and subscribes to <c>NodeOpCommand</c> messages. Pass
        /// <c>null</c> in unit tests that only verify handler registration.
        /// </para>
        /// </summary>
        /// <param name="role">Node role that determines which handlers are wired.</param>
        /// <param name="kernel">Kernel used by <see cref="EcsRecordReplayController"/> for module installs.</param>
        /// <param name="world">Live entity repository forwarded to the controller.</param>
        /// <param name="nodeId">Local node identifier embedded in recording file names and heartbeats.</param>
        /// <param name="participant">Optional DDS participant. Supply to enable heartbeat/command DDS I/O.</param>
        /// <param name="subsystemName">Subsystem name published in heartbeats (default <c>"SimHost"</c>).</param>
        /// <param name="eventBus">Optional event bus; when provided, a <c>LiveLoadDsmHandler</c> is
        /// registered and <see cref="Bagira.Common.Orchestration.DsmStateChangedEvent"/> will be
        /// published on commit.</param>
        public DrillSlave BuildOrchestration(
            NodeRole role,
            ModuleHost.Core.ModuleHostKernel kernel,
            Fdp.Kernel.EntityRepository world,
            int nodeId,
            DdsParticipant? participant = null,
            string subsystemName = "SimHost",
            Fdp.Kernel.FdpEventBus? eventBus = null)
        {
            if (participant == null && (role == NodeRole.Brain || role == NodeRole.AllInOne))
                throw new ArgumentNullException(nameof(participant),
                    $"[SimHost] A DDS participant is required for orchestration role '{role}'. " +
                    "DrillSlave cannot run without DDS in production.");

            var drillSlave = participant != null
                ? new DrillSlave(participant, nodeId, subsystemName, eventBus)
                : new DrillSlave();

            if (role == NodeRole.Brain || role == NodeRole.AllInOne)
            {
                var controller = new EcsRecordReplayController(kernel, nodeId, world);
                drillSlave.RegisterHandler(controller);
            }

            // Wire LiveLoadDsmHandler when an event bus is available (CGF1-S0202).
            if (eventBus != null)
            {
                drillSlave.RegisterHandler(new LiveLoadDsmHandler(drillSlave, eventBus));
            }

            return drillSlave;
        }
    }
}
