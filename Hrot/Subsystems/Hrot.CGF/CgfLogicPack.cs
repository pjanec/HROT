using System;
using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Systems;
using Hrot.CGF.Systems.Routing;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.CGF
{
    /// <summary>
    /// Composite <see cref="IEcsModule"/> that groups the three Brain-tier CGF modules
    /// into a single named unit for use by the HROT Editor composition root and
    /// the Feature Switch (Phase 5 / PACK2-C001).
    ///
    /// <para><b>Contained modules (in registration order matching the Brain role):</b></para>
    /// <list type="number">
    ///   <item><see cref="MissionControlModule"/> — doctrine ingress + mission direction</item>
    ///   <item><see cref="CognitiveRuntimeModule"/> — BTree/HSM tick + channel arbitration</item>
    ///   <item><see cref="ActionDispatchModule"/> — locomotion + weapon dispatchers</item>
    /// </list>
    ///
    /// <para><b>Registration pattern:</b> Because all three contained modules extend
    /// <see cref="ComponentSystem"/> (not <see cref="IEcsModuleSystem"/>), their
    /// systems must be wired into a simulation-phase <see cref="SystemGroup"/> via
    /// <see cref="RegisterSystems(SystemGroup)"/>. The
    /// <see cref="IEcsModule.RegisterSystems(ISystemRegistry)"/> overload is a no-op
    /// and is provided for API compliance only.</para>
    ///
    /// <para><b>Execution order:</b> matches the production order used by
    /// <c>SimulationLogicModule</c> for the <c>Brain</c> role
    /// (Mission → Cognitive → ActionDispatch).</para>
    /// </summary>
    public sealed class CgfLogicPack : IEcsModule
    {
        /// <inheritdoc/>
        public string Name => "CgfLogicPack";

        /// <inheritdoc/>
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        // ── Sub-modules ───────────────────────────────────────────────────────
        private readonly MissionControlModule   _missionControlModule;
        private readonly CognitiveRuntimeModule _cognitiveRuntimeModule;
        private readonly ActionDispatchModule   _actionDispatchModule;
        private readonly MissionControlExecutionSystem _missionExecutionSystem;
        private readonly MissionAdapterSystem   _missionAdapterSystem;

        // ── Shared scenario source (constructed once by CgfApplication / CgfSubsystem) ─
        // Held here for future hand-off to load handlers (Phases 3-4).
        internal ScenarioEntityCreationRequestSource ScenarioSource { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the Brain-tier CGF logic pack with the standard Brain executor set
        /// (MoveToExecutor, FollowRouteExecutor, JoinFormationExecutor, AimAndFireExecutor).
        /// </summary>
        /// <param name="doctrineRegistry">
        /// Doctrine definitions registry forwarded to <see cref="MissionControlModule"/>
        /// and <see cref="CognitiveRuntimeModule"/>.
        /// </param>
        /// <param name="entityMap">
        /// Network entity map forwarded to <see cref="ActionDispatchModule"/> via
        /// <see cref="JoinFormationExecutor"/>.
        /// </param>
        /// <param name="scenarioSource">
        /// In-memory entity creation request source shared by CGF load handlers (Phases 3-4)
        /// and multiplexed alongside the live NED source by <c>CgfSubsystem</c>.
        /// Must not be null.
        /// </param>
        /// <param name="vehicleApi">
        /// Optional high-level vehicle command façade forwarded to
        /// <see cref="JoinFormationExecutor"/>.  <c>null</c> while the executor is a stub.
        /// </param>
        public CgfLogicPack(
            DoctrineRegistry                     doctrineRegistry,
            NetworkEntityMap                     entityMap,
            ScenarioEntityCreationRequestSource  scenarioSource,
            VehicleAPI?                          vehicleApi = null)
        {
            if (doctrineRegistry == null) throw new ArgumentNullException(nameof(doctrineRegistry));
            if (entityMap        == null) throw new ArgumentNullException(nameof(entityMap));
            if (scenarioSource   == null) throw new ArgumentNullException(nameof(scenarioSource));

            ScenarioSource = scenarioSource;

            _missionControlModule   = new MissionControlModule(doctrineRegistry);
            _cognitiveRuntimeModule = new CognitiveRuntimeModule(doctrineRegistry);
            _missionExecutionSystem = new MissionControlExecutionSystem(entityMap, doctrineRegistry);
            _missionAdapterSystem   = new MissionAdapterSystem(doctrineRegistry, entityMap);
            _actionDispatchModule   = new ActionDispatchModule(
                locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                {
                    (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                    (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
                    (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleApi, entityMap)),
                },
                weaponExecutors: new (ushort, IActionExecutor<WeaponChannel>)[]
                {
                    (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor()),
                });
        }

        // ── IEcsModule ────────────────────────────────────────────────────────

        /// <summary>
        /// No-op — the contained sub-modules use <see cref="ComponentSystem"/>-based
        /// <see cref="SystemGroup"/> registration and cannot be registered via
        /// <see cref="ISystemRegistry"/>.
        /// Call <see cref="RegisterSystems(SystemGroup)"/> to wire them into the
        /// application's simulation-phase system group.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>Empty — all logic is handled by registered systems.</summary>
        public void Tick(ISimulationView view, float deltaTime) { }

        // ── SystemGroup-based registration ────────────────────────────────────

        /// <summary>
        /// Registers the Brain-tier systems into the supplied simulation-phase group
        /// in the same execution order used by <c>SimulationLogicModule</c> for the
        /// <c>Brain</c> role: Mission → Cognitive → ActionDispatch.
        /// </summary>
        /// <param name="simGroup">Simulation-phase system group.</param>
        public void RegisterSystems(SystemGroup simGroup)
        {
            if (simGroup == null) throw new ArgumentNullException(nameof(simGroup));

            // Mission intent execution must run before mission/cognitive runtime systems.
            simGroup.AddSystem(_missionExecutionSystem);
            // Adapter bridges MissionPlanQueue phase changes into active DoctrineState.
            simGroup.AddSystem(_missionAdapterSystem);
            _missionControlModule.RegisterSystems(simGroup);
            // Brain applies authoritative damage: EntityHitDamageIngressTranslator delivers
            // DamageAssessedEvent; HealthApplicationSystem mutates Health and strips
            // ActorCapabilities so HsmDamageBridgeSystem (in CognitiveRuntimeModule) can
            // detect the capability change and inject MobilityLost into the HSM.
            simGroup.AddSystem(new HealthApplicationSystem());            // Cognitive threat evaluation: decays TargetMemory scores and boosts them from
            // ActiveSensorTracks (written by SensorTrackStateIngressTranslator).
            // Must run before CognitiveRuntimeModule so B-Trees see freshly scored targets.
            simGroup.AddSystem(new CgfThreatEvaluationSystem());            _cognitiveRuntimeModule.RegisterSystems(simGroup);
            _actionDispatchModule.RegisterSystems(simGroup);
            // Route context: writes per-waypoint ExtensionJson danger level to BrainBlackboard.
            simGroup.AddSystem(new RouteContextSystem());
        }

        /// <summary>
        /// Registers Brain-tier systems split across an Input-phase group and a
        /// Simulation-phase group.
        /// <list type="bullet">
        ///   <item><see cref="MissionControlExecutionSystem"/> and
        ///   <see cref="DoctrineIngressSystem"/> go to <paramref name="inputGroup"/>.</item>
        ///   <item>All remaining systems go to <paramref name="simGroup"/>.</item>
        /// </list>
        /// </summary>
        /// <param name="inputGroup">Input-phase system group.</param>
        /// <param name="simGroup">Simulation-phase system group.</param>
        public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)
        {
            if (inputGroup == null) throw new ArgumentNullException(nameof(inputGroup));
            if (simGroup   == null) throw new ArgumentNullException(nameof(simGroup));

            inputGroup.AddSystem(_missionExecutionSystem);
            simGroup.AddSystem(_missionAdapterSystem);
            _missionControlModule.RegisterSystems(inputGroup, simGroup);
            simGroup.AddSystem(new HealthApplicationSystem());
            simGroup.AddSystem(new CgfThreatEvaluationSystem());
            _cognitiveRuntimeModule.RegisterSystems(simGroup);
            _actionDispatchModule.RegisterSystems(simGroup);
            simGroup.AddSystem(new RouteContextSystem());
        }
    }
}
