using Hrot.Common.EntityCreation;   // CE-140 step 3
using Fdp.Core.Logging;
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.Network.Infrastructure;
using Hrot.IG.Systems;
using Hrot.Map.Common;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Serializers;
using Hrot.SimHost.Systems;

namespace Hrot.NodeComposition;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the Stride-hosted simulation node.
///
/// <para>
/// ST-014: this type used to live in <c>Hrot.StrideMock</c> and outlived it. The mock was temporary
/// scaffolding standing in for the real Stride app; this is the node composition root that app
/// actually consumes (<c>StrideHrotGame</c> holds it and takes it via <c>AttachBootstrapper</c>).
/// It could not move down into <c>Hrot.Common</c> with <see cref="SharedApplicationBootstrapper"/>,
/// because it composes <c>Hrot.SimHost</c> and <c>Hrot.IG</c> systems and both of those reference
/// <c>Hrot.Common</c> - that edge would be a cycle. See <c>DESIGN_Stride_Port.md</c>.
/// </para>
///
/// <para>
/// Implements all abstract hooks to produce a headless-compatible simulation node
/// with the following roles:
/// <see cref="NodeRole.MuscleGround"/> |
/// <see cref="NodeRole.Perception"/> |
/// <see cref="NodeRole.NavigationSolver"/> |
/// <see cref="NodeRole.ImageGenerator"/>.
/// </para>
///
/// <para>
/// Domain modules (kinematics, perception, combat, navigation) are injected via the
/// constructor so Stage 2 can swap in Stride-native implementations without touching
/// the orchestration code.
/// </para>
///
/// <para>
/// IMPORTANT: this class must not reference Raylib, ImGui, or
/// <c>IMapCameraProvider</c>. It is engine-agnostic by design.
/// </para>
/// </summary>
public sealed class StrideNodeBootstrapper : SharedApplicationBootstrapper, IDisposable
{
    /// <summary>Combined node role for all Stride-hosted node responsibilities.</summary>
    public static readonly NodeRole Role =
        NodeRole.MuscleGround | NodeRole.Perception |
        NodeRole.NavigationSolver | NodeRole.ImageGenerator;

    /// <summary>
    /// The capabilities this node composes, handed in by whichever shell boots it.
    ///
    /// <para><b>⭐ S2b / CE-208 — this REPLACED four <c>IEcsModule?</c> constructor slots</b>
    /// (kinematics · perception · combat · navigation). Those were a private, per-host swap
    /// mechanism for exactly what <see cref="INodeCapability"/> does for every other node, and
    /// keeping both would be two ways to express one thing — 🔒 the ruling this programme runs on
    /// ("no keeping two implementations for the same concept"). Nothing was lost: the slots' only
    /// production use was <c>StrideMuscleModules.Build</c>, which is now a capability, and
    /// <c>StrideNodeBootstrapperTests</c> already constructed this type with no arguments.</para>
    ///
    /// <para>⚠ Handed IN rather than resolved here, deliberately: mode 1 has no bootstrapper at all
    /// (it composes through <c>EditorSubsystem</c>), so the declaration must be resolvable by a shell
    /// that never constructs this class. 📄 <c>DESIGN_Stride_Node_Modes.md</c> §4.</para>
    /// </summary>
    private IReadOnlyList<INodeCapability> _capabilities = System.Array.Empty<INodeCapability>();

    // Saved by the overriding BootstrapNode so the abstract hooks can access them.
    private HrotNodeConfig?   _savedConfig;
    private NodeRole          _savedRole;
    private NodeBootstrapper? _nodeBootstrapper;

    /// <summary>
    /// The fully wired node context. Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public HrotNodeContext Context { get; private set; } = default!;

    /// <summary>
    /// Sim-phase togglable group exposed for subsystem rendering and test inspection.
    /// Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public TogglableSimulationGroup SimGroup { get; private set; } = default!;

    /// <summary>
    /// Post-sim togglable group. Valid after <see cref="BootstrapNode"/> returns.
    /// </summary>
    public TogglablePostSimulationGroup PostSimGroup { get; private set; } = default!;

    /// <summary>
    /// Optional callback invoked during Phase 6d (after network translators, before Initialize).
    /// Used to register diagnostic capture systems and other application-level systems.
    /// </summary>
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }

    /// <summary>
    /// Producer buffer: local ECS systems write gizmos here each frame, then
    /// the batch is published to DDS (when a participant is present).
    /// </summary>
    public DebugPrimitiveBuffer ProducerBuffer { get; } = new DebugPrimitiveBuffer();

    /// <summary>
    /// Consumer buffer: populated from DDS by the debug-primitives ingress translator.
    /// The Raylib renderer reads from this buffer.
    /// </summary>
    public DebugPrimitiveBuffer ConsumerBuffer { get; } = new DebugPrimitiveBuffer();

    /// <summary>Map camera for 2D viewport navigation.</summary>
    public MapCamera Camera { get; } = new MapCamera();

    // ITimeControlGateway? TimeControl is inherited from SharedApplicationBootstrapper.

    /// <remarks>
    /// ⭐ Parameterless since S2b. The four <c>IEcsModule?</c> slots this used to take are gone —
    /// see the <c>_capabilities</c> field. Hand the units in with <see cref="WithCapabilities"/>.
    /// </remarks>
    public StrideNodeBootstrapper()
    {
    }

    /// <summary>
    /// Runs the 7-phase bootstrap pipeline and stores the resulting context.
    ///
    /// <para>
    /// Hides (does not override) the base-class method so config and role can
    /// be captured before the abstract hooks are invoked, and so that the
    /// <c>SlaveTranslator</c> side-effect of <see cref="BuildOrchestration"/>
    /// can be patched into the context before it is exposed publicly.
    /// </para>
    /// </summary>
    public new HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory networkFactory)
    {
        _savedConfig      = config;
        _savedRole        = role;
        _nodeBootstrapper = new NodeBootstrapper(networkFactory);

        var ctx = base.BootstrapNode(config, role, networkFactory);

        // Patch SlaveTranslator — it is a side-effect of NodeBootstrapper.BuildOrchestration
        // and is NOT set by the base class (only ClusterSlave is patched in by the base).
        if (_nodeBootstrapper.SlaveTranslator != null)
            ctx = ctx with { SlaveTranslator = _nodeBootstrapper.SlaveTranslator };

        Context = ctx;
        return Context;
    }

    /// <summary>
    /// Advances the node by one frame.  Call once per application frame from
    /// the main thread.
    /// </summary>
    public void Tick(float dt)
    {
        ProducerBuffer.EndFrame(dt);
        ConsumerBuffer.Clear();

        Context.SlaveTranslator?.Tick();
        Context.ClusterSlave.Tick();

        // ST-021: the TIME CONTROLLER owns time, not the caller's frame delta.
        //
        // This used to call the [Obsolete] Update(float) overload behind a #pragma, justified as "a
        // mock/test-only harness, not a live DDS-connected node". Both halves of that were wrong once
        // the mock was retired: this is the real Stride app's composition root, and the overload's own
        // attribute says it "will cause deterministic desync" -- it fabricates a GlobalTime (synthetic
        // FrameNumber, TotalTime approximated as simTime+dt) and leaves the real controller's state
        // behind, which is exactly the divergence the regression net rests on not happening.
        //
        // The comment also claimed SlaveSyncController "needs network sync events to advance ... absent
        // in headless/offline mode". ⚠ MEASURED FALSE: AdvanceContinuousTime derives elapsed from
        // SyncedWallTicks = _getTick() + _masterWallClockOffset, where _getTick defaults to the local
        // HighResUtcClock and the offset is simply 0 until a master answers. Sync events CORRECT the
        // offset; they do not gate advancement. So offline this node advances on its own wall clock and
        // silently starts tracking the master when one appears -- which is the intended behaviour.
        //
        // `dt` is still the caller's frame delta for the gizmo producer buffer above; only the kernel
        // stops being driven by it. Same shape CgfSubsystem already uses.
        Context.Kernel.Update();
        Context.EventBus.SwapBuffers();

        // _gizmoIngress?.PollAndApply();  // fills ConsumerBuffer from DDS — wire in SM-006
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Context?.Participant?.Dispose();
    }

    /// <inheritdoc/>
    protected override void RegisterApplicationSystems(HrotNodeContext context)
        => ApplicationSystemsRegistrar?.Invoke(context);

    // ── Abstract hook implementations ─────────────────────────────────────────

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        return new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .Build();
    }

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        // Foundation: network-replication, geographic, definitions, lifecycle events.
        HrotSharedComponentRegistry.RegisterAll(world);

        // Muscle-tier: vehicle physics, CQRS nav state, formation components.
        // CognitiveComponentRegistry is intentionally excluded — Brain AI data stays
        // on the CGF node. TkbTemplate.ApplyTo() silently skips missing components,
        // so entity spawning works correctly here without the Brain tables.
        MuscleRoleComponentRegistry.RegisterAll(world);

        // IG presentation components — not covered by any shared registry.
        // Required by SyncFdpToStrideScript queries and EventToEffectSystem.
        PresentationComponentRegistry.RegisterAll(world);
        world.RegisterComponent<VisualEffectState>();
        world.RegisterComponent<TracerTarget>();

        // Genesis Intent DTOs: transient managed components resolved by
        // GenesisMaterializationSystem during scenario load. Must mirror the
        // set registered in SimHostComponentRegistry so cross-entity references
        // are correctly materialised on this node.
        GenesisIntentRegistry.RegisterAll(world);

        // ⛔⛔⛔ CE-250 — THE COMBAT/PERCEPTION SCHEMA. This node RUNS BallisticsSystem,
        //    DamageCalculationSystem, FireProcessingSystem and HitResolutionSystem, and until now it
        //    registered none of the components or events they read and write.
        //
        // 📌 How it surfaced: adding the perception egress (CE-249) killed the process on the first
        //    frame that carried a perception command --
        //      InvalidOperationException: Component type 251 not registered. All components must be
        //      registered before command buffer playback.
        //    251 is PerceptionReceptor. Before CE-249 nothing on this node ever published a
        //    perception command, so the missing schema was INERT rather than absent-looking: the
        //    combat systems ran every frame over queries that could never match, and the entity's
        //    PhysicsCollider read back null through the debug API. ⇒ the crash was CE-249 exposing
        //    this, not CE-249 breaking anything.
        //
        // 📐 CombatComponentRegistry is the owning registry and it carries exactly this node's needs:
        //    PerceptionReceptor, TargetMemory, SensorContactList, WeaponState, BallisticProjectile,
        //    PhysicsCollider, plus the events the chain runs on -- LosCheckRequestEvent,
        //    TargetVisibleEvent, SensorTrackStateEvent, WeaponFireIntent, HitEvent,
        //    DamageAssessedEvent, FireRequestEvent. SimHost reaches it through
        //    SimHostComponentRegistry.RegisterAll (:46); this bootstrapper's hand-picked subset never
        //    did.
        //
        // ⭐ Node-wide schema, so it belongs HERE beside MuscleRoleComponentRegistry rather than in a
        //    capability: the combat systems come from the muscle pack and the perception egress from
        //    the network translators, and both need the same tables. (Contrast CE-243, where the EQS
        //    schema went into the capability that registers EqsModule because only that capability
        //    needs it.)
        //
        // ⚠ Still deliberately EXCLUDED, unchanged: CognitiveComponentRegistry. Brain AI data stays on
        //    CGF -- see the note above and DESIGN_Role_Affinity_Ownership.md's opening ruling, "SimHost
        //    having a muscle role should not instantiate any brain related components".
        CombatComponentRegistry.RegisterAll(world);

        // ⛔⛔ CE-250b — THE REST OF SIMHOST'S SCHEMA, MINUS THE BRAIN TABLES.
        //
        // 📌 Registering CombatComponentRegistry alone moved the crash rather than fixing it: the next
        //    frame died on "Event type 2030 not registered" (RaycastRequestEvent), which
        //    LosRequestBatchingSystem publishes and RaycastSolverSystem consumes -- both already
        //    running here. Fixing these ONE AT A TIME is how a night gets spent, so the set below is
        //    taken from SimHostComponentRegistry.RegisterAll (:40-79) in ITS order.
        //
        // 📐 Each line is here because this node ALREADY RUNS the system that needs it -- verified
        //    against its own /diagnostics/architecture:
        //      MissionComponentRegistry     FormationTargetSystem, VehicleCommandSystem
        //      RouteComponentRegistry       RouteTrajectorySyncSystem, PersonalRouteAuthoringSystem
        //      HierarchyComponentRegistry   UnitHierarchySystem
        //      Raycast{Request,Result}Event RaycastSolverSystem + the LOS chain (this is 2030)
        //      MapPresentationRegistry      the shared map/gizmo component set the other three
        //                                   windowed hosts register
        //    ⇒ the node was running systems whose schema nobody had declared. Harmless while nothing
        //    published to them, fatal the moment CE-249 let perception actually flow.
        //
        // ⛔⛔ STILL EXCLUDED, AND DELIBERATELY: CognitiveComponentRegistry -- BehaviorState,
        //    LocomotionChannel, BrainBTreeState, BrainBlackboard. This node has no brain systems
        //    (no BTreeTickSystem, no TacticalIntentResolutionSystem -- both are CGF's), and
        //    DESIGN_Role_Affinity_Ownership.md opens on the user's ruling that "SimHost having a
        //    muscle role should not instantiate any brain related components. If it does, this is a
        //    mistake." SimHost registers them today and that design calls it debt; ⇒ copying SimHost
        //    wholesale here would import the debt on purpose. TkbTemplate.ApplyTo() silently skips
        //    missing components, so spawning stays correct without them -- the reason the original
        //    exclusion note above gives, still true.
        MissionComponentRegistry.RegisterAll(world);
        Hrot.Presentation.Map.MapPresentationRegistry.RegisterAll(world);
        RouteComponentRegistry.RegisterAll(world);
        HierarchyComponentRegistry.RegisterAll(world);

        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastRequestEvent>();
        world.RegisterEvent<Fdp.Toolkit.Physics.RaycastResultEvent>();
        world.RegisterEvent<Hrot.Common.Events.MissionControlAckEvent>();
        world.RegisterEvent<Hrot.Common.Events.GlobalActionRequestedEvent>();
        world.RegisterEvent<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoComponentActivatedEvent>();
    }

    /// <inheritdoc/>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => HrotScenarioSerializerFactory.Build(registry ?? new BehaviorRegistry());

    /// <inheritdoc/>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        // Visual effect systems are placed in the togglable groups so they are
        // suspended during replay (SC_SM005_2).
        // EventToEffectSystem reads combat events and spawns VisualEffectState entities.
        // VisualEffectCleanupSystem removes expired effect entities.
        sim.Add(new EventToEffectSystem());
        postSim.Add(new VisualEffectCleanupSystem());

        // ⭐⭐ CE-244 (1 of 2) — the capabilities' `system-groups` half. See the CE-244 note on
        //    RegisterSpawningPipeline below for the measurement; this is the same defect's other half.
        //    UnitHierarchy and EqsResultUpdate (CoreInfrastructureCapabilities) contribute through
        //    PopulateSystems rather than ProvideModules, deliberately, so without this pass their
        //    systems were absent from a node whose boot log listed both capabilities by name.
        foreach (INodeCapability capability in _capabilities)
            capability.PopulateSystems(context, input, sim, postSim);
    }

    /// <inheritdoc/>
    protected override IEnumerable<IEcsModule> GetAdditionalModules()
    {
        // ⭐ The modules come from the resolved capability set. This is the `additional-modules` boot
        //   step, which is exactly the step INodeCapability.ProvideModules() exists to serve (§4.1t):
        //   expressing them through Register() instead would push them AFTER context.BaseModules and
        //   the spawning pipeline, and registration order is execution order.
        foreach (INodeCapability capability in _capabilities)
            foreach (IEcsModule module in capability.ProvideModules())
                yield return module;
    }

    /// <summary>
    /// Hands this bootstrapper the capabilities its shell resolved. Call BEFORE bootstrapping.
    /// </summary>
    /// <remarks>
    /// ⚠ A shell that forgets this gets a node with no muscle tier and no perception — so the
    /// bootstrapper refuses an empty set at boot rather than composing a silently hollow node.
    /// </remarks>
    public StrideNodeBootstrapper WithCapabilities(IReadOnlyList<INodeCapability> capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        return this;
    }

    /// <inheritdoc/>
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        SimGroup     = simGroup;
        PostSimGroup = postSimGroup;

        return _nodeBootstrapper!.BuildOrchestration(
            _savedRole,
            context.Kernel,
            context.World,
            context.NodeId,
            participant:         context.Participant,
            subsystemName:       _savedConfig!.SubsystemName,
            eventBus:            context.EventBus,
            scenarioSerializer:  null,    // this node does not load/save scenarios
            localTempRoot:       _savedConfig.LocalTempRoot,
            simGroup:            simGroup,
            postSimGroup:        postSimGroup,
            lifecycleGroup:      context.NedReplication?.NetworkLifecycleGroup,
            ghostCreationSystem: context.GhostCreationSystem,
            eventAccumulator:    context.EventAccumulator,
            afterSeek:           context.NedReplication?.AfterSeekCallback);
    }

    /// <inheritdoc/>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // ⛔⛔⛔ CE-244 (2 of 2) — THIS BOOTSTRAPPER HONOURED ONLY ONE THIRD OF THE CAPABILITY
        //    CONTRACT, and the missing two thirds failed SILENTLY.
        //
        // 📌 Measured 2026-09-09 on a CGF + Stride mode-2 run of hill-attack-close. The boot log
        //    reported five capabilities by name --
        //      [cap:muscle-ground, cap:perception, cap:perception:spatial,
        //       cap:infra:unit-hierarchy, cap:infra:eqs-result-update]
        //    -- and PERCEPTION WAS NEVER COMPOSED AT ALL. Only GetAdditionalModules() ran, which asks
        //    each capability for ProvideModules() alone. cap:muscle-ground contributes that way, so
        //    physics worked and the node looked healthy; every capability that contributes through
        //    the other two hooks contributed nothing:
        //      PerceptionSolver.Register    -> EqsModule                          NEVER REGISTERED
        //      PerceptionSpatial.Register   -> AreaQueryResultMaterializationSystem
        //                                      + CognitiveSpatialModule           NEVER REGISTERED
        //      UnitHierarchy.PopulateSystems / EqsResultUpdate.PopulateSystems    NEVER CALLED
        //
        // 📐 INodeCapability's own remarks name the schedule: "PopulateSystems fires at system-groups
        //    and Register at spawning-pipeline -- and additional-modules runs BETWEEN them." The three
        //    hooks exist because the base composes in distinct steps and a capability contributing to
        //    more than one must be asked more than once. This type asked once.
        //
        // ⭐ The proven template is the mode-1 editor, EditorSubsystem.cs:1506 (PopulateSystems),
        //    :1583 (ProvideModules) and :1588 (Register) -- "ONE ordered pass per capability: the
        //    modules it PROVIDES, then its Register hook". Mode 2 now runs the same three, each at the
        //    step the interface documents for it rather than all at one convenient point.
        //
        // ⚠ Why the capability pass goes FIRST here, before this node's own spawn systems: the editor
        //    registers capabilities ahead of its orchestration and scenario packs, and registration
        //    order is execution order within a phase. Nothing below reads anything a capability
        //    registers, and no capability reads the pack, so the two are independent today -- the
        //    order is chosen to match the editor rather than to satisfy a dependency.
        foreach (INodeCapability capability in _capabilities)
            capability.Register(context, BootValues);

        // GenesisMaterializationSystem resolves cross-entity Intent DTOs into live
        // component data during scenario load. Runs in the Input phase.
        context.Kernel.RegisterGlobalSystem(
            new GenesisMaterializationSystem(context.EntityMap));

        // NetworkSpawningSystem handles incoming entity creation requests from the
        // network (CGF/Brain node). Only wired when an ID allocator is available;
        // headless tests with SkipAllocatorRouting=true skip this path.
        if (context.IdAllocator != null)
        {
            // ⭐⭐⭐ CE-140 step 3 — the ENTITY CREATION PACK. This host used to hand-assemble the spawn
            //    path here, and it is where CE-139 was found: the FIFTH host with `translators:` unpassed
            //    and SetTranslators never called, so ProcessSpawn step 4's projection loop ran zero times
            //    and entities were born with identity and a DIS header but none of their components.
            //    ⇒ the pack makes that omission unrepresentable rather than merely documented.
            //
            // ⭐⭐ AND IT CLOSES A SECOND, QUIETER GAP: this node had NO `CreateEntityRequestSystem`, so
            //    nothing could ask it to create an entity — not even itself. 🔒 User ruling 2026-08-31:
            //    the shared code "should not restrict any ECS enabled node from creating own networked
            //    entities … not removing capabilities by design". The pack has no opt-out.
            //
            // 📄 DESIGN_Entity_Creation_Unification.md §3, §3.4 · Architect_Question_65 §0, §4.
            var creation = EntityCreationPack.Build(new EntityCreationContext
            {
                World       = context.World,
                EntityMap   = context.EntityMap,
                TkbDb       = context.TkbDb!,
                IdAllocator = context.IdAllocator,
                Elm         = (EntityLifecycleModule)context.BaseModules[0],
                NodeId      = context.NodeId,

                // ⛔ NOT the cluster's broadcast arbiter — that is CGF, and exactly one node may be it.
                //    ⚠ This does NOT stop this node creating entities: a request targeted at this node is
                //    processed regardless of the flag (Q65 §1).
                IsBroadcastArbiter = false,
            });

            // ⭐ The HOST schedules. NetworkSpawningSystem is BeforeSync and goes through a module here,
            //   exactly as before — composition changed, scheduling did not.
            context.Kernel.RegisterModule(new Fdp.ModuleHost.Scheduling.SingleSystemModule("NetworkSpawning", creation.SpawnSystem));
            context.Kernel.RegisterGlobalSystem(creation.RequestSystem);        // Input
            context.Kernel.RegisterGlobalSystem(creation.FinalizationSystem);  // PostSimulation

            // ⭐⭐ The S2b habit: make an omission loud. Every one of the five defects behind this design
            //   was silent.
            var unserviceable = creation.Unserviceable(new object[]
            {
                creation.SpawnSystem, creation.RequestSystem, creation.FinalizationSystem,
            });
            if (unserviceable.Length > 0)
                FdpLog<StrideNodeBootstrapper>.Warn(unserviceable);

            // ⚠ FOLLOW-UP, not a regression: no DDS ingress source or ACK sink is passed, so this node
            //   serves LOCAL requests only. `HrotNodeContext` exposes no lifecycle adapters, so wiring
            //   the network half needs a context addition — out of scope for a composition change, and
            //   strictly better than before, when this node had no request tier at all.
        }
    }

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        if (configuredFactory == null) return;
        // SimHost auxiliary translators: entity attribute updates, combat egress, etc.
        configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);

        // ⛔⛔⛔ CE-249 — THE PERCEPTION EGRESS. Without it this node SEES targets and never TELLS
        //    anyone, so the brain has nothing to shoot at.
        //
        // 📌 Measured 2026-09-09 on the two runs side by side, same scenario, same CGF:
        //      CGF + SimHost  1001 reaches (524,401) at t=11; both hostiles hp 50->25 at t=21;
        //                     1007 dead t=35, 1006 dead t=42.
        //      CGF + Stride   1001 reaches (522,401) at t=11 -- MOVEMENT AT PARITY -- and both
        //                     hostiles stay at hp=50 indefinitely (observed to t=475).
        //    On CGF, entity 1001's TargetMemory.Entries is EMPTY for the whole Stride run while
        //    WeaponState.Ammo sits at 42: the brain is armed, in position, and has no target.
        //
        // 📐 The perception TIER is not the problem and measuring it is what found this. The node's
        //    own /diagnostics/architecture reports CognitiveSpatialModule -- which owns
        //    LocalGridBuilderSystem, AreaQuerySolverSystem, VisionBroadphaseSystem,
        //    LosRequestBatchingSystem and SensorTrackDebounceSystem -- as
        //    "lifecycleState: Ready, executionCount: 741, failureCount: 0". It runs, it sees, and it
        //    publishes SensorTrackStateEvent onto this node's OWN bus. What was missing is the hop
        //    off the node: SensorTrackStateEgressTranslator (SimPerceptionTranslatorPack) writes the
        //    DDS SensorTrackState sample that CGF's SensorTrackStateIngressTranslator turns back into
        //    a SensorTrackStateEvent for ActiveSensorTracksUpdateSystem -> CgfThreatEvaluationSystem
        //    -> TargetMemory -> WeaponDispatcherSystem.
        //
        // ⚠ An earlier reading of the same dump concluded those five systems were ABSENT here because
        //    they appear in SimHost's system enumeration and not in this node's. That was WRONG -- they
        //    are RegisterManualSystem systems driven by the module's own Tick, so they are enumerated
        //    differently, and executionCount 741 settles it. Recorded because the wrong reading is the
        //    tempting one and would have sent the next session to rebuild a tier that already works.
        //
        // ⭐ SimHostNodeBootstrapper registers THREE packs here; this node registered one. The
        //    pack is role-gated inside the factory (NedSimHostPerceptionTranslators requires
        //    NodeRole.Perception), which mode 2 has -- StrideCapabilities.DefaultRole is
        //    MuscleGround | Perception -- so the gate was already satisfied and only the call was
        //    absent.
        //
        // ⛔ NOT added: CreateSimHostPathfindingTranslators, SimHost's third pack. It needs
        //    CoreLogicPack.TrajectoryPool, and mode 2 deliberately does not claim
        //    NodeRole.NavigationSolver -- DESIGN_Stride_Node_Modes.md §4.1b: "navigation is provided
        //    by the MuscleGround capability and the flag is not claimed". Leaving it out is that
        //    design decision, not an oversight; if off-node pathfinding is ever wanted here, §4.1b is
        //    the thing to revisit first.
        if (context.GhostCreationSystem != null)
        {
            configuredFactory
                .CreateSimHostPerceptionTranslators(context.GhostCreationSystem)
                .RegisterOn(context.Kernel);
        }
        else
        {
            // ⛔ Loud, not silent: NedSimHostPerceptionTranslators THROWS on a null ghost-creation
            //    system, and a node that quietly skipped its perception egress is exactly the class of
            //    defect this whole sequence was made of.
            FdpLog<StrideNodeBootstrapper>.Warn(
                "[StrideNodeBootstrapper] CE-249: GhostCreationSystem is null, so the perception " +
                "egress translators were NOT registered. This node will see targets and never report " +
                "them, and the brain will never fire. Expected only on a headless/offline node with " +
                "no replication module.");
        }
    }
}
