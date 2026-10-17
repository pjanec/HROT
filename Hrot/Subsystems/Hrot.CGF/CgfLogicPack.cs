using System;
using System.Collections.Generic;
using CarKinem.Commands;
using CarKinem.Formation;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Systems;
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
using Hrot.SimHost.Systems;
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
        private readonly ActiveSensorTracksUpdateSystem _activeSensorTracksUpdateSystem;        private readonly CgfThreatEvaluationSystem    _cgfThreatEvaluationSystem;
        private readonly RouteContextSystem           _routeContextSystem;
        private readonly TacticalIntentResolutionSystem _tacticalIntentResolutionSystem;
        private readonly Fdp.Toolkit.Squad.Systems.SquadCoordinationSystem _squadCoordinationSystem;   // CE-454
        // CE-221: UnitHierarchySystem and EqsResultUpdateSystem are CROSS-ROLE INFRASTRUCTURE and no
        // longer live in this pack. They are contributed once per node by
        // CoreInfrastructureCapabilities.UnitHierarchy / EqsResultUpdateCapability, declared LAST in
        // each plan so they keep their tail-of-Simulation position. Carrying them here made every
        // Brain+Muscle node register them twice.

        /// <summary>Systems to wrap in TogglableInputGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
        public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

        /// <summary>
        /// A4 / <c>O0</c> — the blueprint <b>maintenance</b> system, for the capability to carry into
        /// the kernel as a <c>SingleSystemModule</c>.
        ///
        /// <para>⚠ It is <c>[UpdateInPhase(BeforeSync)]</c>, so unlike the tick it cannot live in
        /// <see cref="SimulationSystems"/>. ⛔ And it is deliberately NOT registered by each
        /// composition root: a per-host <c>RegisterGlobalSystem</c> call is a per-host chance to
        /// forget, which is exactly how <c>CE-161</c> arose (three of four bootstrappers missed the
        /// tier registration). ⭐ Exposing it here lets the ONE capability that provides this pack
        /// provide it too.</para>
        /// </summary>


        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>⭐ <c>CE-3167</c> — the providers an order with <c>ClearsPriorIntent</c> runs on this brain (the mission plan).</summary>
        public IntentClearRegistry IntentClears { get; }

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
        /// <param name="mapperRegistry">
        /// Registry of <see cref="ITacticalOrderMapper"/> implementations used by
        /// <see cref="TacticalIntentResolutionSystem"/> to translate generic tactical
        /// intent IDs into concrete behavior assignments.  Must not be null.
        /// </param>
        /// <param name="vehicleApi">
        /// Optional high-level vehicle command façade forwarded to
        /// <see cref="JoinFormationExecutor"/>.  <c>null</c> while the executor is a stub.
        /// </param>
        /// <param name="blueprintRegistry">
        /// A4 / <c>O0</c> — the registry <see cref="BlueprintTickSystem"/> ticks against. ⭐ Required,
        /// not optional: both production roots already hold one (<c>CgfSubsystem.cs:718</c>,
        /// <c>EditorSubsystem</c>'s <c>_blueprintRegistry</c>), and a defaulted-to-new instance would
        /// tick against an EMPTY registry — a silent no-op, and precisely the silent-default shape
        /// this codebase keeps finding (<i>"a production caller that HAS a dependency must PASS it"</i>).
        /// </param>
        public CgfLogicPack(
            BehaviorRegistry                     behaviorRegistry,
            NetworkEntityMap                     entityMap,
            TacticalIntentMapperRegistry         mapperRegistry,
            BlueprintRegistry                    blueprintRegistry,
            VehicleAPI?                          vehicleApi = null,
            bool                                 gateOnAuthority = false)
        {
            if (behaviorRegistry  == null) throw new ArgumentNullException(nameof(behaviorRegistry));
            if (entityMap         == null) throw new ArgumentNullException(nameof(entityMap));
            if (mapperRegistry    == null) throw new ArgumentNullException(nameof(mapperRegistry));
            if (blueprintRegistry == null) throw new ArgumentNullException(nameof(blueprintRegistry));

            // ⭐⭐⭐ P3 step 3b — the EXECUTION gate, threaded to BOTH cognitive modules from ONE flag so
            //   they cannot disagree. 📄 docs/DESIGN_Role_Affinity_Ownership.md §3.5.
            //   ⚠ false today on every host: the gate follows the POLICY, and nothing supplies one until
            //   step 4. Turning it on earlier would stop this node processing every entity it did not
            //   create, because a promoted ghost owns nothing — CE-256's own failure, reproduced by its
            //   own fix.
            _missionControlModule   = new MissionControlModule(behaviorRegistry, gateOnAuthority);
            _cognitiveRuntimeModule = new CognitiveRuntimeModule(behaviorRegistry, gateOnAuthority);
            _missionExecutionSystem              = new MissionControlExecutionSystem(entityMap, behaviorRegistry, mapperRegistry);
            _missionAdapterSystem                = new MissionAdapterSystem();
            // ⭐ CE-3167 (Entity Commanding §2.5d) — what an order with ClearsPriorIntent clears, registered once here: the
            //   mission plan, by the system that owns it (one clear, shared with CMD_ABORT_ALL).
            IntentClears = new IntentClearRegistry();
            IntentClears.Register(_missionExecutionSystem);
            _tacticalIntentResolutionSystem      = new TacticalIntentResolutionSystem(mapperRegistry, behaviorRegistry, IntentClears);
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
                    (CombatConstants.ActionIdFireAtPoint, new Fdp.Toolkit.Combat.Executors.FireAtPointExecutor()),   // ⭐ CE-1032 W-8
                    (CombatConstants.ActionIdTrackTarget, new Fdp.Toolkit.Combat.Executors.TrackTargetExecutor()),   // ⭐ WL-3 (Weapon_Layers W-1)
                },
                // ⭐ CE-502 — the Brain writes embarkation state (DESIGN_Role_Affinity_Ownership.md §6, "the Brain's
                //   EmbarkExecutor / EjectPassengersExecutor"). 🔴 None was registered here, so the dispatcher ran with no
                //   interaction executors: an APC's HSM issued EjectPassengers on MobilityLost and nothing executed it — the
                //   squad stayed aboard with no capabilities. The same set the example hosts register (HeadlessDemoApp).
                interactionExecutors: System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(
                    new (ushort, IActionExecutor<InteractionChannel>)[]
                    {
                        (BehaviorConstants.ActionIdEjectPassengers, new EjectPassengersExecutor()),
                        (BehaviorConstants.ActionIdEmbark,          new EmbarkExecutor()),   // ⭐ CE-3110 I-2a — boarding was constructed only by its unit test
                        (BehaviorConstants.ActionIdDisembark,       new DisembarkExecutor()),   // ⭐ CE-1056 I-2d — one passenger leaves
                        (BehaviorConstants.ActionIdPickUp,          new PickUpExecutor()),    // ⭐ CE-2152 C-b — a carrier picks a casualty up
                        (BehaviorConstants.ActionIdPutDown,         new PutDownExecutor()),   // ⭐ CE-2152 C-b — and puts him down
                        (BehaviorConstants.ActionIdEject,           new EjectExecutor()),     // ⭐ airborne insertion B-9 — the aircraft's crew ejects
                    },
                    // ⭐ Buildings 5d — Open/Close/Lock/Unlock/Breach door, one list so no host forgets a verb
                    System.Linq.Enumerable.Select(BehaviorConstants.DoorActionExecutors(),
                        d => (d.Id, (IActionExecutor<InteractionChannel>)d.Executor)))));

            _activeSensorTracksUpdateSystem = new ActiveSensorTracksUpdateSystem();
            _cgfThreatEvaluationSystem = new CgfThreatEvaluationSystem();
            _routeContextSystem        = new RouteContextSystem();

            // ⭐⭐ CE-454 (W1/W2) — 📄 docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md §3.
            //   W1: the Utility input readers (Standard + Squad) are registered by the analyzer-emitted
            //   [UtilityRegistrar]s, which NOTHING in production ever invoked — so every input read 0. One
            //   idempotent call, here, because every Brain host builds this pack.
            Fdp.Toolkit.Utility.UtilityAutoDiscovery.ScanAndRegister();
            //   ⭐ CE-2068 — …and the DECISIONS: nothing filled UtilityDecisionCatalog.Shared in production either, so every
            //   ScoreDecision found no decision. Idempotent. 📄 docs/DESIGN_Decision_Layer.md §3.3 (as-built).
            Fdp.Toolkit.Utility.UtilityDecisionCatalog.EnsureRegistered();
            //   W2: the squad layer's one frame driver (perception merge on every owned commander).
            //   ⭐ CE-3088 — and the squad's fire distribution after each merge (📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §11).
            _squadCoordinationSystem   = new Fdp.Toolkit.Squad.Systems.SquadCoordinationSystem(gateOnAuthority,
                new Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem(Fdp.Toolkit.Utility.LeaderAssignmentDecision.Id));

            var inputList     = new List<IEcsModuleSystem>();
            var simList       = new List<IEcsModuleSystem>();

            // ⭐ CE-3172 (docs/DESIGN_Entity_Control.md §8b) — the brain owner's side of possession: a station's request starts
            //   OperatorControl at origin Possession (pausing the task), its frames become OperatorInput, a release resumes the task.
            //   First, so the assignment it publishes is on the bus before the behaviour ingress reads it.
            inputList.Add(new Fdp.Toolkit.Behavior.Possession.PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl));
            inputList.Add(_missionExecutionSystem);
            foreach (var s in _missionControlModule.InputSystems) inputList.Add(s);
            foreach (var s in _cognitiveRuntimeModule.InputSystems) inputList.Add(s);

            simList.Add(_missionAdapterSystem);
            simList.Add(_tacticalIntentResolutionSystem);
            foreach (var s in _missionControlModule.SimulationSystems) simList.Add(s);
            // ⛔ CE-2144 (R-2008) — HealthApplicationSystem is NOT pack content: any live-simulation node may own Health, so it is the
            //   cross-role CoreInfrastructureCapabilities.HealthApplication, declared FIRST by the shared LiveSimulationPlan head — ahead of
            //   everything here, so the capabilities it strips at 0 HP are stripped before the cognitive runtime and the dispatchers read them.
            // ⛔ CE-3110 — the interaction APPLIERS (DoorCommandSystem, EmbarkationSystem) and EmbarkedStateSystem are NOT pack content:
            //   any node that can own a live entity needs them, so they are the cross-role CoreInfrastructureCapabilities.Interactions,
            //   declared in every host's plan (like UnitHierarchy).
            simList.Add(_activeSensorTracksUpdateSystem);
            simList.Add(_cgfThreatEvaluationSystem);
            // ⭐ CE-3064 (R-206) — a near miss becomes the unit's SensorChange.NearMiss, beside Hit.
            simList.Add(new Fdp.Toolkit.Perception.Systems.NearMissSensingSystem());
            // ⭐ CE-2189 (DESIGN_Missiles_And_Ammo §3c) — a missile launched at one of its aircraft: SensorChange.MissileWarning + details.
            simList.Add(new Fdp.Toolkit.Perception.Systems.MissileWarningSensingSystem());
            // ⭐ CE-2176 IF-3 — the bursts this node saw, for a fire-mission observer (DESIGN_Indirect_Fire §3.8).
            simList.Add(new Fdp.Toolkit.Combat.DetonationMemorySystem());
            // ⭐ CE-3174 (C-7, docs/DESIGN_Entity_Control.md §8d) — the ammunition per mount, mirrored for every station's HUD (dtWeaponAmmo).
            simList.Add(new Fdp.Toolkit.Combat.Systems.WeaponAmmoMirrorSystem());
            simList.Add(_squadCoordinationSystem);   // CE-454: after TargetMemory is boosted, before behaviours read the pool
            simList.Add(new Fdp.Toolkit.Squad.Systems.UnitCentroidSystem());   // ⭐ CE-2125 / CE-514 (R-272) — the owner places a unit at its members' centre
            foreach (var s in _cognitiveRuntimeModule.SimulationSystems) simList.Add(s);
            foreach (var s in _actionDispatchModule.SimulationSystems)   simList.Add(s);
            simList.Add(_routeContextSystem);
            // CE-221: UnitHierarchySystem + EqsResultUpdateSystem were appended here. They are now
            // contributed by the infrastructure capabilities (see the field block above).

            // ⭐⭐⭐ A4 / O0 — SPLICE THE BLUEPRINT TICK HERE, not at each composition root.
            //   📐 CE-161's lesson, one level up: the tier COMPONENTS moved into a shared path on
            //      2026-09-03 after a --mode all cluster aborted on CGF, but the SCHEDULING stayed in
            //      Hrot.Blueprints.Editor — so only the Editor ever ticked a blueprint Instance.
            //   ⭐ This pack is provided by CgfCapabilities.Brain, which both CgfSubsystem and
            //      EditorSubsystem construct, so one splice serves both hosts.
            //   ⚠ It must go BEFORE the action dispatchers (its own [UpdateBefore] targets, contributed
            //      by _actionDispatchModule just above): module-group order is ARRAY POSITION, and an
            //      appended tick dispatches intent a tick late — the Q#16-B contract. The shared helper
            //      reads the targets off the attributes so this stays true by construction.
            SimulationSystems = BlueprintRuntimeComposition.SpliceIntoSimulation(
                simList, new BlueprintTickSystem(blueprintRegistry));

            InputSystems       = inputList;
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
