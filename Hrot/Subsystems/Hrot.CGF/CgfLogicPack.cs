using System;
using System.Collections.Generic;
using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Perception.Systems;
using Fdp.Toolkit.Replication.Services;
using Hrot.CGF.Systems;
using Hrot.CGF.Systems.Routing;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Fdp.ModuleHost;
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
    ///   <item><see cref="MissionControlModule"/> — behavior ingress + mission direction</item>
    ///   <item><see cref="CognitiveRuntimeModule"/> — BTree/HSM tick + channel arbitration</item>
    ///   <item><see cref="ActionDispatchModule"/> — locomotion + weapon dispatchers</item>
    /// </list>
    ///
    /// <para><b>Registration pattern:</b> The contained modules expose phase-typed
    /// <c>IReadOnlyList&lt;IEcsModuleSystem&gt;</c> array properties (<c>InputSystems</c>,
    /// <c>SimulationSystems</c>).  Wrap those lists in <c>TogglableInputGroup</c> and
    /// <c>TogglableSimulationGroup</c> at the composition root.
    /// The <see cref="IEcsModule.RegisterSystems(ISystemRegistry)"/> overload is a no-op
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

        // ── Standalone systems (moved from RegisterSystems overloads) ──────────
        private readonly HealthApplicationSystem      _healthApplicationSystem;        private readonly ActiveSensorTracksUpdateSystem _activeSensorTracksUpdateSystem;        private readonly CgfThreatEvaluationSystem    _cgfThreatEvaluationSystem;
        private readonly RouteContextSystem           _routeContextSystem;
        private readonly TacticalIntentResolutionSystem _tacticalIntentResolutionSystem;
        private readonly UnitHierarchySystem          _unitHierarchySystem;

        // ── Shared scenario source (constructed once by CgfApplication / CgfSubsystem) ─
        // Held here for future hand-off to load handlers (Phases 3-4).
        internal ScenarioEntityCreationRequestSource ScenarioSource { get; }

        /// <summary>Systems to wrap in TogglableInputGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the Brain-tier CGF logic pack with the standard Brain executor set
        /// (MoveToExecutor, FollowRouteExecutor, JoinFormationExecutor, AimAndFireExecutor).
        /// </summary>
        /// <param name="behaviorRegistry">
        /// Behavior definitions registry forwarded to <see cref="MissionControlModule"/>
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
        /// <param name="mapperRegistry">
        /// Registry of <see cref="ITacticalOrderMapper"/> implementations used by
        /// <see cref="TacticalIntentResolutionSystem"/> to translate generic tactical
        /// intent IDs into concrete behavior assignments.  Must not be null.
        /// </param>
        /// <param name="vehicleApi">
        /// Optional high-level vehicle command façade forwarded to
        /// <see cref="JoinFormationExecutor"/>.  <c>null</c> while the executor is a stub.
        /// </param>
        public CgfLogicPack(
            BehaviorRegistry                     behaviorRegistry,
            NetworkEntityMap                     entityMap,
            ScenarioEntityCreationRequestSource  scenarioSource,
            TacticalIntentMapperRegistry         mapperRegistry,
            VehicleAPI?                          vehicleApi = null)
        {
            if (behaviorRegistry == null) throw new ArgumentNullException(nameof(behaviorRegistry));
            if (entityMap        == null) throw new ArgumentNullException(nameof(entityMap));
            if (scenarioSource   == null) throw new ArgumentNullException(nameof(scenarioSource));
            if (mapperRegistry   == null) throw new ArgumentNullException(nameof(mapperRegistry));

            ScenarioSource = scenarioSource;

            _missionControlModule   = new MissionControlModule(behaviorRegistry);
            _cognitiveRuntimeModule = new CognitiveRuntimeModule(behaviorRegistry);
            _missionExecutionSystem              = new MissionControlExecutionSystem(entityMap, behaviorRegistry, mapperRegistry);
            _missionAdapterSystem                = new MissionAdapterSystem();
            _tacticalIntentResolutionSystem      = new TacticalIntentResolutionSystem(mapperRegistry, behaviorRegistry);
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

            _healthApplicationSystem   = new HealthApplicationSystem();
            _activeSensorTracksUpdateSystem = new ActiveSensorTracksUpdateSystem();
            _cgfThreatEvaluationSystem = new CgfThreatEvaluationSystem();
            _routeContextSystem        = new RouteContextSystem();
            _unitHierarchySystem       = new UnitHierarchySystem();

            var inputList = new List<IEcsModuleSystem>();
            var simList   = new List<IEcsModuleSystem>();

            inputList.Add(_missionExecutionSystem);
            foreach (var s in _missionControlModule.InputSystems) inputList.Add(s);

            simList.Add(_missionAdapterSystem);
            simList.Add(_tacticalIntentResolutionSystem);
            foreach (var s in _missionControlModule.SimulationSystems) simList.Add(s);
            simList.Add(_healthApplicationSystem);
            simList.Add(_activeSensorTracksUpdateSystem);
            simList.Add(_cgfThreatEvaluationSystem);
            foreach (var s in _cognitiveRuntimeModule.SimulationSystems) simList.Add(s);
            foreach (var s in _actionDispatchModule.SimulationSystems)   simList.Add(s);
            simList.Add(_routeContextSystem);
            simList.Add(_unitHierarchySystem);

            InputSystems      = inputList;
            SimulationSystems = simList;
        }

        /// <summary>
        /// No-op — the contained sub-modules expose phase-typed array properties
        /// (<c>InputSystems</c>, <c>SimulationSystems</c>).  Wrap those lists in
        /// <c>TogglableInputGroup</c> and <c>TogglableSimulationGroup</c> at the composition root.
        /// </summary>
        public void RegisterSystems(ISystemRegistry registry) { }

        /// <summary>Empty — all logic is handled by registered systems.</summary>
        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
