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
    [TickMode(TickPolicy.WhenTimeAdvances)]
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
        [field: Fdp.Core.NotState("system list assembled once in ctor")]
        public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

        /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
        [field: Fdp.Core.NotState("system list assembled once in ctor")]
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
            bool                                 gateOnAuthority = false,
            Fdp.Core.FdpEventBus?                nodeBus = null,
            Func<Fdp.Toolkit.Lifecycle.EntityLifecycleModule?>? lifecycle = null,
            Func<CarKinem.Trajectory.TrajectoryPoolManager?>? remoteRoutes = null,
            Fdp.Core.NodeId                      localNodeId = default)
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
            _missionControlModule.Ingress.Holds  = IntentClears;   // ⭐ CE-3494 (R-3115) — the ingress holds the plan after an order STARTS (silent-default rule)
            _missionControlModule.Ingress.TasksTakenByOrders = true;   // ⭐ CE-3494 §10 (R-3119) — the tactical system above takes a person's task from the intent (silent-default rule)
            // ⭐ agg-behaviors ST-2 (R-8311, the silent-default rule) — the ingress starts a stochastic unit's template-default SOP through the SAME routing table that
            //   resolves its orders; Fdp.Toolkits cannot see that table, so the host that holds it hands the ingress the resolver. Every brain host builds this pack.
            Fdp.Toolkit.Behavior.StochasticSopRouting.Resolver = TacticalIntentResolutionSystem.SopFor;
            _actionDispatchModule   = new ActionDispatchModule(
                locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                {
                    (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                    (NavigationConstants.ActionIdApproachDoor,  new Fdp.Toolkit.Navigation.Executors.ApproachDoorExecutor()),   // ⭐ Q94 C — the muscle picks the door's side
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
                        (BehaviorConstants.ActionIdPickUpRound,     new PickUpRoundExecutor()),   // ⭐ CE-1207 (Entity Control §8m PK-4) — a stone / round into his hands
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
            // ⭐ CE-2125 / CE-514 (R-272) — the owner places a unit at its members' centre. ⭐⭐ F-3 (CE-4503): FIRST, in Input — before anything
            //   of the frame reads a unit's position, so a step reads what a paused frame would compute (⛔ was mid-Simulation, below).
            inputList.Add(new Fdp.Toolkit.Squad.Systems.UnitCentroidSystem());
            inputList.Add(new Fdp.Toolkit.Behavior.Possession.PossessionSystem(Hrot.Map.Definitions.Behavior.BehaviorNames.OperatorControl));
            inputList.Add(_missionExecutionSystem);
            foreach (var s in _missionControlModule.InputSystems) inputList.Add(s);
            foreach (var s in _cognitiveRuntimeModule.InputSystems) inputList.Add(s);

            simList.Add(_missionAdapterSystem);
            // ⭐ CE-4676 (DESIGN_LLM_Commander §3) — a seated commander's orders, from his mind, validated and given through CommandOrders (origin
            //   Superior) — before the resolver, which takes them this frame as it takes a play's
            simList.Add(new Hrot.CGF.Commander.CommanderActionSystem(clockBus: nodeBus));   // ⭐ CE-4679 — a held clock is the node's ONE pause (nodeBus), the seats the world's own
            simList.Add(new Hrot.CGF.Commander.CommanderWatchSystem());   // ⭐ CE-4684 C-2 — a decision's starts, completions, aborts and contingencies, no model call
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
            // ⭐ CE-2235 (docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — every civilian's fear from what he heard this frame, before
            //   the brain tick, so his CivilianHsm reads this frame's fright.
            simList.Add(new Fdp.Toolkit.Civilians.CivilianFearSystem());
            simList.Add(new Hrot.Core.Animals.AnimalAreaSystem());   // ⭐ CE-4623 AB-9 — populate each animal area once, at run start (its owner)
            simList.Add(new Fdp.Toolkit.Animals.HerdSystem());   // ⭐ CE-4623 AB-4 — herds: centre, grazing spot, night, contagion (serial, before the brains read it)
            // ⭐ CE-2237 (§5f) — the crowds: who an incident draws, who stands with whom, and the fear that spreads among them —
            //   after the fear system, before the brain tick, so a guard reads this frame's contagion.
            simList.Add(new Fdp.Toolkit.Civilians.CrowdSystem());
            // ⭐ CE-3064 (R-206) — a near miss becomes the unit's SensorChange.NearMiss, beside Hit.
            simList.Add(new Fdp.Toolkit.Perception.Systems.NearMissSensingSystem());
            // ⭐ CE-2189 (DESIGN_Missiles_And_Ammo §3c) — a missile launched at one of its aircraft: SensorChange.MissileWarning + details.
            simList.Add(new Fdp.Toolkit.Perception.Systems.MissileWarningSensingSystem());
            // ⭐ CE-4617 (B-4, DESIGN_Radar_SAM RS-6 / RS-10) — each of its aircraft's radar warning, derived from the replicated emitters.
            simList.Add(new Fdp.Toolkit.Perception.Systems.RadarWarningReceiverSystem(gateOnAuthority));
            // ⭐ CE-2176 IF-3 — the bursts this node saw, for a fire-mission observer (DESIGN_Indirect_Fire §3.8).
            simList.Add(new Fdp.Toolkit.Combat.DetonationMemorySystem());
            // ⭐ CE-3174 (C-7, docs/DESIGN_Entity_Control.md §8d) — the ammunition per mount, mirrored for every station's HUD (dtWeaponAmmo).
            simList.Add(new Fdp.Toolkit.Combat.Systems.WeaponAmmoMirrorSystem());
            simList.Add(_squadCoordinationSystem);   // CE-454: after TargetMemory is boosted, before behaviours read the pool
            // ⭐⭐ CE-2293 CC-C (Command Chain §5) — the staff: compose and send the standing reports, then fold the ones that arrived into
            //   each commander's picture — after the merge (the pool is fresh), before the brain tick (this frame's guards see it).
            // ⭐ CE-4617 (B-5, DESIGN_Radar_SAM RS-7) — a radar radiating 30 s is located: a SPOT to the other sides' top commanders, before the inbox folds it
            simList.Add(new Fdp.Toolkit.Squad.Command.EmitterLocatingSystem(gateOnAuthority));
            simList.Add(new Fdp.Toolkit.Squad.Command.StaffComposerSystem(gateOnAuthority));
            simList.Add(new Fdp.Toolkit.Squad.Command.StaffInboxSystem(gateOnAuthority));
            // ⭐ CE-4676 — a seated commander's FEED, after the inbox (this frame's reports are in his history)
            simList.Add(new Hrot.CGF.Commander.CommanderFeedSystem(gateOnAuthority: gateOnAuthority, isBehaviour: n => behaviorRegistry.TryGetId(n, out _), registry: behaviorRegistry));   // ⭐ CE-4685 A-2 — a seat's kinds from his class · CE-4686 ② its play's roles
            // ⭐ CE-2273 DA-A — a unit's authored proficiency reaches its men (written on each man: the hit model reads only him).
            simList.Add(new Fdp.Toolkit.Behavior.Systems.ProficiencyFillSystem(gateOnAuthority));
            // ⭐ CE-2360 (AQ102 I) — a live grenade lying near a man: his reflex (run, drop, throw it back) as a reaction, before the brain
            //   tick — every man, under command or not (it is no SOP row).
            simList.Add(new Hrot.AI.Behaviors.Brains.GrenadeDangerSystem(gateOnAuthority));
            // ⭐ CE-4624 MS-B1 — a mine set off by a man or a vehicle: the unit's MineStrike drill as a reaction, before the brain tick.
            simList.Add(new Hrot.AI.Behaviors.Brains.MineStrikeReflexSystem(gateOnAuthority));
            // ⭐ CE-2270 (TS-B, docs/DESIGN_Temperament_And_Stress.md §3) — a combatant's stress from last frame's edges (near miss, hit, a
            //   friend down) and the continuous terms — after perception and the squad merge, before the brain tick reads it.
            simList.Add(new Hrot.AI.Behaviors.Brains.StressSystem(gateOnAuthority));
            // ⭐ CE-8016 (agg-behaviors' F-9; SV-3) — an authored stochastic unit's slots and holdings from its TKB composite, once, before the
            //   unit's steps read them (serial; the owner only)
            simList.Add(new Hrot.Core.Tkb.StochasticUnitFillSystem(gateOnAuthority));
            // ⭐ CE-8004 (ST-1, M-4; DESIGN_Stochastic_Units_And_Resolution §14) — the stochastic units' path follower, beside the stress phase
            //   (§22's module diagram): every CGF node runs it by construction; it moves only the units this node owns.
            simList.Add(new Fdp.Toolkit.Aggregate.StochasticUnitMovementSystem(gateOnAuthority) { RemoteRoutes = remoteRoutes, LocalNodeId = localNodeId });   // ⭐ CE-8005
            // ⭐ CE-8006 (ST-2, N-1 … N-5) — the stochastic units' sensing, a per-entity phase; writes each observer's own detections and pool
            simList.Add(new Fdp.Toolkit.Aggregate.StochasticUnitSensorSystem(gateOnAuthority));
            // ⭐ CE-8007 (ST-3, C-2) — the attrition step: phase A (each firer's allocation) then phase B (each victim's draw), per-entity phases
            simList.Add(new Fdp.Toolkit.Aggregate.StochasticFirePhase(gateOnAuthority));
            simList.Add(new Fdp.Toolkit.Aggregate.StochasticLossPhase(gateOnAuthority));
            simList.Add(new Fdp.Toolkit.Aggregate.StochasticAreaEffectSystem(gateOnAuthority));   // ⭐ CE-8005 (R-171) — a burst on a unit, applied by its owner
            // ⭐ CE-8013 (ST-5 production wiring; §16b) — the resolution manager: a split's children are the TKB composite's slots
            //   (StochasticUnitFill.ChildType, the same leaves the fill keyed), held until the handover (EntityCreation's HeldChildParticipant),
            //   and leave through the node's lifecycle (lazy: the creation pack is built after this pack). The policy is agg-behaviors' seam.
            // ⭐ CE-8303 (agg-behaviors D-P3 DP3-B, R-4286) — a splitting stochastic squad holding a Defend plans REAL posts (SquadDefend's own plan over
            //   its slot table) into PlannedPosts, serially, BEFORE the manager, so the manager sees Ready in the same frame (DESIGN_DP3_Planned_Posts §5)
            simList.Add(new Hrot.CGF.Systems.UnitPostPlanningSystem(gateOnAuthority));
            // ⭐ X-2 (agg-behaviors, R-8353 / R-8357) — the resolution-fit reconciler (F-5's system, renamed): built first, so the manager's SwitchBlocked
            //   seam asks the SAME registry it re-resolves with (an automatic switch waits while the unit's order has no play at the target)
            var resolutionFit = new Hrot.CGF.Systems.UnitResolutionFitSystem(behaviorRegistry, gateOnAuthority);
            simList.Add(new Fdp.Toolkit.Aggregate.ResolutionManagerSystem(new Fdp.Toolkit.Aggregate.BusResolutionSwitch
            {
                ChildType = static (repo, unit, slot) =>
                    repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db
                    && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit)
                        ? Hrot.Core.Tkb.StochasticUnitFill.ChildType(db, repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit).TkbType, slot)
                        : default,
                // ⭐ X-2 X2-B (R-8354, agg-behaviors, cross-lane) — a released man joins the squad's roster under his leaf's designation
                ChildDesignation = static (repo, unit, slot) =>
                    repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db
                    && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit)
                        ? Hrot.Core.Tkb.StochasticUnitFill.ChildDesignation(db, repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit).TkbType, slot)
                        : Fdp.Core.CommandHierarchy.TacticalDesignation.Undefined,
                // ⭐⭐ CE-8307 (agg-behaviors, cross-lane; DESIGN_Split_One_Level.md) — a split goes ONE level: the composite's DIRECT subordinates
                //   (a platoon's stochastic squads, each with its share of the table), never its flattened men (X-5 / D-P7)
                Composition = static (repo, unit) =>
                    repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db
                    && repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit)
                        ? Hrot.Core.Tkb.StochasticUnitFill.Composition(db, repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit).TkbType)
                        : null,
                Lifecycle = lifecycle,
            }, gateOnAuthority)
            {
                Policy = Fdp.Toolkit.Aggregate.ResolutionPolicies.BandsOf,
                SwitchBlocked = (repo, unit, target) => Hrot.AI.Behaviors.Orders.OrderReissue.BlockReason(repo, unit, target, resolutionFit.IsBehaviour),   // ⭐ X2-D (cross-lane)
            });   // ⭐ CE-8300 (agg-behaviors, cross-lane one-liner) — the bands from the World Environment's ResolutionPolicy
            // ⭐ CE-8301 (agg-behaviors ST-5, F-5, R-8327) — a unit whose resolution changed (a merge, a split) re-resolves its template-default SOP: the
            //   routing table's Sop row when stochastic, the template's own when physical. Stateless, 1 Hz, after the manager that flips the resolution.
            // ⭐ X-2 — and every frame it re-issues a unit's ORDER whose play no longer fits its resolution (DESIGN_X2_Order_Reresolution.md)
            simList.Add(resolutionFit);
            // ⭐ rules ② (§00.11a, R2) — a live edit of a rule's params, applied BEFORE the brain tick (the edited rule ticks with its new params this frame); the module's one ingress does the work.
            simList.Add(new Hrot.AI.Behaviors.Brains.Objectives.RuleWakeSystem());   // ⭐ CE-2383 — operator requests / the exercise closing wake cadenced rules, before the brain tick
            simList.Add(new Hrot.AI.Behaviors.Brains.Objectives.RuleEditSystem(_missionControlModule.Ingress));
            // ⭐ N-11 (CE-4473) — the rules' tree is an index the rule brains read: refreshed serially before the phase (BrainPhase). ⭐ CE-3448 — now
            //   contributed by AiBrainReads.Contribute (CgfBehaviorSetup.LoadFromAiAssembly), the loader every host and rail goes through
            foreach (var s in _cognitiveRuntimeModule.SimulationSystems) simList.Add(s);
            // ⭐ rules S2 ⑨ (SR-116) — the exercise ended ⇒ ask the cluster to pause, once; after the brain tick that ran EndExercise. [NodeFeature("exercise.pause-on-end")], default ON.
            simList.Add(new Hrot.AI.Behaviors.Brains.Objectives.ExercisePauseOnEndSystem(nodeBus));   // ⭐ the NODE bus — a production host passes HrotNodeContext.EventBus
            // ⭐ rules V-3 (§00.10) — each owned trigger's when-leaves with their live values, ≤ 2 Hz of sim time; [NodeFeature("ui.rules-live-values")], OFF at load (switched Live on demand).
            simList.Add(new Hrot.AI.Behaviors.Brains.Objectives.RuleLeafSampler());
            foreach (var s in _actionDispatchModule.SimulationSystems)   simList.Add(s);
            // ⭐ CE-2319 / CE-2320 (AQ96 §2c) — a soldier's move under fire: AFTER the dispatchers, so a move issued this frame already has its
            //   intent when the reflex halts or re-paces it.
            simList.Add(new Hrot.AI.Behaviors.Brains.UnderFireMoveSystem(gateOnAuthority));
            // ⭐ CE-9004 (C-10, DESIGN_Road_Convoy) — a vehicle no longer running PlatformConvoy (contact, a new order) leaves its column at once
            simList.Add(new Hrot.AI.Behaviors.Brains.ConvoyMembershipSystem(gateOnAuthority));
            // ⭐ CE-2368 (AQ103 §3.3) — idle life: a stationary person shifts a little — after the reflex over a MOVE, so a man on one is its
            simList.Add(new Hrot.AI.Behaviors.Brains.RestlessSystem(gateOnAuthority));
            // ⭐ CE-2375 (AQ104 A-1, R-2081) — a lone man's area order: once the squad asked for him exists here, assign him and give it the play.
            simList.Add(new Hrot.AI.Behaviors.Brains.OneManCommandSystem(gateOnAuthority));
            // ⭐ CE-2372 (AQ105 B, R-2080) — the squad calls whose moment happens in engine code: a new contact, a throw, a reload, a man down.
            simList.Add(new Hrot.AI.Behaviors.Brains.CalloutSystem(gateOnAuthority));
            simList.Add(_routeContextSystem);
            // ⭐ CE-4624 MS-T2 (KR-5) — the place-knowledge writer on THIS deciding node too: the recon / marking events the brains raised (a host that also composes the
            //   SimHost pack keeps one writer — the first instance claims the world).
            simList.Add(new Fdp.Toolkit.Terrain.PlaceKnowledgeSystem());
            // ⭐ CE-2468 UF-2 — the under-fire writer on THIS deciding node too: the broken contacts the brains raise (Squad Tactics R1); one writer per world (the first claims it)
            simList.Add(new Fdp.Toolkit.Terrain.UnderFireWriter());
            // ⭐ CE-4624 MS-T2 (MR-5, R-5049) — each driven unit's own track near a minefield: the struck unit backs out along it, the medic comes in along it
            simList.Add(new Fdp.Toolkit.Terrain.MineBreadcrumbSystem(gateOnAuthority));
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
