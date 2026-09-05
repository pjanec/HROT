using CarKinem.Core;
using Fdp.Core.Logging;
using Hrot.Common.EntityCreation;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization.Migrations;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Spatial;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common;
using Hrot.Common.Diagnostics;
using Hrot.Common.Infrastructure;
using Hrot.Common.Scenario.Migrations;
using Hrot.Common.Orchestration;
using Hrot.Common.Systems;
using Hrot.Core.Diagnostics;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.IG.Modules;
using Hrot.IG.Modules.Orchestration;
using Hrot.IG.Systems;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Presentation.Map;

namespace Hrot.IG;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the IG (Image Generator) node.
/// Implements all abstract hooks to produce a visualization-only node with role
/// <see cref="NodeRole.ImageGenerator"/>.
/// </summary>
internal sealed class IgNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly INetworkFactory? _networkFactory;

    /// <summary>
    /// ⭐⭐ The node's LOCAL entity-creation request source, owned by the creation pack and published
    /// here so IG's authoring tools can enqueue INTENTS onto it (host (f)). Null until
    /// <see cref="RegisterSpawningPipeline"/> has run.
    /// </summary>
    public ScenarioEntityCreationRequestSource? LocalEntityCreationRequests { get; private set; }
    private readonly int _effectiveInstanceId;
    private readonly bool _headless;
    private readonly IIgTranslators? _igTranslatorsProvider;
    private readonly MapUserConfig _userConfig;
    private readonly MapCameraViewport _cameraViewport;
    private readonly IDiagnosticEventHistoryService? _eventHistoryService;
    private readonly HrotNodeConfig _hrotConfig;

    /// <summary>True when DDS network was successfully configured. Valid after BootstrapNode() returns.</summary>
    public bool NetworkEnabled { get; private set; }

    /// <summary>Protocol-neutral IG network adapter. Valid after BootstrapNode() returns.</summary>
    public IIgNetworkAdapter? NetworkAdapter { get; private set; }

    /// <summary>Command gateway obtained from the network adapter. Valid after BootstrapNode() returns.</summary>
    public Hrot.Core.Network.ICommandGateway? CommandGateway { get; private set; }

    // ⛔⛔ CE-164 — `OrchestrationBus` and `IgSlaveTranslator` are GONE.
    //
    // They exposed a SECOND orchestration stack that IG built on top of the shared one and then ticked
    // instead of it. 📐 Measured 2026-09-03 on a four-process cluster: HrotNodeBuilder.Build() Step 8
    // already builds this node's ClusterSlave AND a COMPLETE ISlaveOrchestrationTranslator
    // (NodeOpSlaveTranslator + ClusterOpEgressTranslator) on context.EventBus, and IgNodeBootstrapper:151
    // calls that builder — so the egress half was BUILT on IG and then discarded. IG published
    // TransitionStateIntent onto context.EventBus (IgApplication.OrchestrationBus) whose complete
    // translator nobody ticked, while ticking its own bare ingress-only one on a different bus. Result:
    // `POST /scenario/load/live` on the IG port answered ok/"cluster-intent" and the cluster never moved,
    // where the identical call on CGF moved all three nodes.
    //
    // ⭐ Readers now use context.EventBus and context.SlaveTranslator — the same members SimHostApp:504/560,
    //   StrideNodeBootstrapper:174, CgfSubsystem:1294 and EyesAndMuscleSubsystem:104 already use. IG was
    //   the only host that did not.
    // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1b.

    /// <summary>Migration services bundle. Valid after BootstrapNode() returns.</summary>
    public MigrationServices? MigrationServices { get; private set; }

    /// <summary>
    /// Optional callback invoked during Phase 6d (after network translators, before Initialize).
    /// IgApplication sets this to register gizmo modules, event-history capture systems, and
    /// other systems that must be part of the initialized kernel topology.
    /// </summary>
    public Action<HrotNodeContext>? ApplicationSystemsRegistrar { get; set; }

    internal IgNodeBootstrapper(
        INetworkFactory? networkFactory,
        int effectiveInstanceId,
        bool headless,
        IIgTranslators? igTranslatorsProvider,
        MapUserConfig userConfig,
        MapCameraViewport cameraViewport,
        IDiagnosticEventHistoryService? eventHistoryService,
        HrotNodeConfig hrotConfig)
    {
        _networkFactory = networkFactory;
        _effectiveInstanceId = effectiveInstanceId;
        _headless = headless;
        _igTranslatorsProvider = igTranslatorsProvider;
        _userConfig = userConfig;
        _cameraViewport = cameraViewport;
        _eventHistoryService = eventHistoryService;
        _hrotConfig = hrotConfig;
    }

    // ── Phase 1: Build context ────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override HrotNodeContext BuildContext(HrotNodeConfig config, NodeRole role, INetworkFactory? networkFactory)
    {
        // ⭐ CE-197 — remember the role so the capability set can be resolved FROM it (B4b step 3).
        //   ⚠ Captured HERE, at boot phase 1, because every later step declares `requires: ["context"]`
        //     — so the plan cannot run one before this has run. That is the same ordering argument the
        //     lazy resolve in GetAdditionalModules rests on, and it is why this is not a hidden channel.
        _declaredRole = role;

        // ⭐⭐⭐ CE-141 CLOSED 2026-09-03 — THE TRANSLATOR LIST IS NOT BUILT HERE, AND NOT PER-HOST.
        //
        // 🔒 User ruling: "entity creation needs to be unified. There should be nothing we give just to
        //    IG. every ECS nodes must use same TKB in same way using the same shared code."
        //
        // ⭐ This is a DELETION, not a substitution: IG was the LAST host still calling
        //    .WithTranslators(...). SimHost dropped it at CE-140 step 3 and the reason applies verbatim
        //    here — that argument fed NedReplicationModule's `tkbEntityTranslators`, whose ONLY consumer
        //    is GhostPromotionSystem, and that system falls back to EntityLifecycleModule.Translators
        //    when no explicit list is given (GhostPromotionSystem.cs: `_explicitTranslators ??
        //    _lifecycleModule.Translators`). The ELM's list is the ONE instance EntityCreationPack
        //    composed from TkbTranslatorSet.Base(). ⇒ dropping the call makes tkb-1/DESIGN.md §6.3's
        //    "identical for all three systems within the same node" true BY CONSTRUCTION instead of by
        //    two copies agreeing.
        //
        // ⛔⛔ WHY THE OLD 2-ENTRY LIST WAS WRONG, and it was wrong on the shared type's own terms:
        //    TkbTranslatorSet's contract says "Do NOT subtract from this list to make a host materialise
        //    less ... every ITkbEntityTranslator is contractually required to guard each write with
        //    repo.IsComponentTypeRegistered<T>(), so a translator whose components a host never
        //    registered is ALREADY a no-op there." 📐 Verified 2026-09-03 across all six Base()
        //    translators: guards >= component adds in every one. ⇒ THE NARROWING LEVER IS THE COMPONENT
        //    REGISTRATION SET, never the list. If IG should not seed Health/WeaponState/PerceptionReceptor
        //    /TargetMemory/VehicleParams/PhysicsCollider from TKB, the answer is to stop REGISTERING
        //    them in IgRoleComponentRegistry — one loud decision, and any later write throws.
        //
        // ⚠ Two earlier comments here are SUPERSEDED and are gone: (a) "a shorter list is a real decision
        //    here" (asserted, never measured) and (b) "this list feeds ONLY the ghost projection ... never
        //    a local spawn" — false since CE-144 gave IG the shared NetworkSpawningSystem.
        // 📄 docs/DESIGN_Entity_Creation_Unification.md §2.3 · §3.4c · docs/designs/tkb-1/DESIGN.md §6.3/§6.5b.
        return new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .Build();
    }

    // ── Phase 2: Register domain ECS components ───────────────────────────────

    /// <inheritdoc/>
    protected override void RegisterDomainComponents(EntityRepository world)
    {
        var tkb = HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);

        //  Shared foundation 
        // Registers network replication, geographic, shared definitions, and
        // lifecycle events identically to SimHost (via SimHostComponentRegistry).
        HrotSharedComponentRegistry.RegisterAll(world);

        IgRoleComponentRegistry.RegisterAll(world);


        // SimCombatDef, TkbCompositionDef, VisualData, lifecycle events, and
        // FireInteractionEvent are all handled by HrotSharedComponentRegistry above.
    }

    // ── Phase 3: Build scenario serializer ───────────────────────────────────

    /// <inheritdoc/>
    protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
        => new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("Hrot.IG").Build();

    // ── Phase 4a: Populate togglable system groups ────────────────────────────

    /// <inheritdoc/>
    protected override void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim)
    {
        // IG is a visualization-only node; all real ECS processing is done by the
        // modules registered in phases 4b, 6a, and 6b.
    }

    // ── Phase 4b: Additional ECS modules ─────────────────────────────────────

    /// <inheritdoc/>
    /// <summary>
    /// ⭐⭐⭐ <b><c>B4b</c> step 2, host (b) — IG's modules come from a RESOLVED capability set.</b>
    ///
    /// <para>The five module constructions that stood here inline now live in
    /// <c>IgCapabilities.Presentation</c>, and this hook walks whatever the plan resolved. 📄
    /// <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §4.1t.</para>
    ///
    /// <para>⛔⛔ <b>The plan is resolved LAZILY, not stashed by an earlier phase.</b> The obvious shape
    /// — build it in <c>PopulateSystems</c> as SimHost does, and read the field here — is a trap on this
    /// host: the base declares <c>additional-modules</c> with <c>requires: ["context"]</c> only, NOT
    /// <c>"system-groups"</c> (<c>SharedApplicationBootstrapper.cs:148</c>), so the boot plan is free to
    /// run this step before <c>PopulateSystems</c>. The field would then be empty and IG would register
    /// <i>no presentation modules at all</i> — silently, with a healthy boot. Resolving on first use
    /// depends on nothing but this object, so no step ordering can break it.</para>
    ///
    /// <para>⚠ <b>The role is not consulted, deliberately</b> — the same stance
    /// <c>SimHostNodeBootstrapper:254</c> takes. This host IS the image generator, and <c>B4b</c> is
    /// behaviour-preserving, so the set must not narrow at the switchover. Selecting BY the declared
    /// role flags is the next step and needs its own measurement of what each deployed role carries.</para>
    /// </summary>
    protected override IEnumerable<IEcsModule> GetAdditionalModules()
    {
        foreach (INodeCapability capability in ResolveCapabilities())
            foreach (IEcsModule module in capability.ProvideModules())
                yield return module;
    }

    /// <summary>Builds the node's composition plan and resolves it, once.</summary>
    private IReadOnlyList<INodeCapability> ResolveCapabilities()
    {
        if (_capabilities != null) return _capabilities;

        var plan = new NodeCompositionPlan()
            .Capability(
                NodeRole.ImageGenerator,
                new IgCapabilities.Presentation(
                    _userConfig, _effectiveInstanceId, _cameraViewport, _headless));

        // ⭐⭐⭐ CE-197 — resolved from the DECLARED role, not a constant (B4b step 3).
        //    📐 Provably a no-op here: IgApplication.cs:923 passes the literal NodeRole.ImageGenerator,
        //    which is exactly what the capability above is declared for. ⚠ Unlike SimHost — whose
        //    declared role was MISSING NavigationSolver and had to be corrected before this swap was
        //    safe — IG's declaration was already true.
        NodeRole composed = _declaredRole;

        _capabilities = plan.Resolve(composed);

        // ⭐⭐ CE-199 — the resource half is no longer refused here; it simply has nothing to do.
        //
        // ⛔ HISTORY: this block used to THROW if any provider resolved, for two reasons that were both
        //    true — IG resolves its plan LAZILY (no HrotNodeContext), and NodeBootValues refused writes
        //    outside a declaring boot step. The base now owns a `node-resources` step, so reason ② is
        //    gone for every host; reason ① is why IG still declares its keys from the role alone.
        //
        // ⭐ Today the presentation capability declares no Needs, so DeclaredResourceKeys returns empty
        //    and the node allocates nothing — which is the correct answer, not a gap. ⚠ The moment an IG
        //    capability gains a Need, this assertion is what makes the omission loud instead of silent.
        var declared = new SortedSet<string>(DeclaredResourceKeys(composed), System.StringComparer.Ordinal);
        var needed   = new SortedSet<string>(System.StringComparer.Ordinal);
        foreach (INodeResourceProvider provider in plan.RequiredResources(composed))
            needed.Add(provider.Key);

        if (!needed.SetEquals(declared))
            throw new InvalidOperationException(
                $"IG role {composed} declares resources [{string.Join(", ", declared)}] but its composed " +
                $"capabilities need [{string.Join(", ", needed)}]. Add the key to DeclaredResourceKeys so " +
                "the node-resources boot step allocates it — see CE-199.");

        return _capabilities;
    }

    private IReadOnlyList<INodeCapability>? _capabilities;

    /// <summary>The role this node was bootstrapped with, captured in <see cref="BuildContext"/>.</summary>
    private NodeRole _declaredRole = NodeRole.ImageGenerator;

    // ── Phase 5: Build orchestration ─────────────────────────────────────────

    /// <inheritdoc/>
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        // ⭐⭐⭐ CE-164 — the node's ONE orchestration bus, the one HrotNodeBuilder Step 8 created and put
        //    this node's complete ISlaveOrchestrationTranslator on. ⛔ NOT a second `new FdpEventBus()`.
        //
        // ⚠ CMC-S016 ("each slave subsystem has its own orchestration bus + translator, Option C") is
        //   SATISFIED by this, and always was: the ruling is about each SUBSYSTEM having its own bus —
        //   true of every host, since each builds its own context — ⛔ NOT about a subsystem having TWO.
        //   📐 Searched docs/ and .dev/ for a record justifying a second bus inside IG: none found.
        //
        // ⭐ Same shape as StrideNodeBootstrapper:278, which delegates with `eventBus: context.EventBus`,
        //   and as SimHost/CGF/EyesAndMuscle. A FRESH ClusterSlave on the SHARED bus is the normal pattern
        //   (SharedApplicationBootstrapper:104 then swaps it onto the context); what must never be fresh
        //   is the BUS or the slave TRANSLATOR.
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1b.
        var orchestrationBus = context.EventBus;
        MigrationServices = HrotMigrationBootstrap.BuildIg();

        // CGF1-S0104: wire ClusterSlave once DDS participant is confirmed healthy.
        // Use _effectiveInstanceId (= _nodeIdOverride when set, else IgNetworkConstants.InstanceId=300)
        // so the IG ClusterSlave always registers on a cluster-unique node ID.
        // Using IgNetworkConstants.LocalNodeId (1) caused collision with SimHost when --node-id 0.
        var slave = new ClusterSlave(_effectiveInstanceId, "IG", orchestrationBus);

        // ⛔ CE-164 — the hand-built `new NodeOpSlaveTranslator(...)` that stood here is DELETED.
        //    context.SlaveTranslator already IS a NodeOpSlaveTranslator + ClusterOpEgressTranslator on this
        //    very bus, built through INetworkFactory.CreateSlaveOrchestratorTranslators. Rebuilding the
        //    ingress half by hand is what dropped the EGRESS half, and with it every transition intent IG
        //    ever published. ⭐ IgApplication now ticks context.SlaveTranslator, like every other host.

        // CGF1-BATCH-23 A.2: IG participates in recording/replay cluster operations as a
        // listen-only node.  Shared controller tracks IsReplayActive so the
        // Live-from-Replay branch (CGF1-S0305) is correctly gated.
        var igRrController = new Hrot.Common.Orchestration.ListenerRecordReplayController("IG");

        string storageDirectory = !string.IsNullOrWhiteSpace(_hrotConfig.LocalTempRoot)
            ? _hrotConfig.LocalTempRoot
            : OrchestrationConstants.ResolveStagingRoot();

        // Wire ReferenceReplayLoadHandler FIRST (PrepareReplay / FinalizeReplay
        // unconditional; PrepareLive only when replay active).
        slave.RegisterHandler(new ReferenceReplayLoadHandler(
            igRrController,
            inputGroup:            null,
            simGroup:              null,
            postSimGroup:          null,
            lifecycleGroup:        context.NedReplication?.NetworkLifecycleGroup,
            bypassLifecycleToggle: null,
            storageDirectory:      storageDirectory));

        // Wire ReferenceLiveLoadHandler: ACKs cold PrepareLive and FinalizeLive
        // without recording (IG carries no ECS frame data).
        slave.RegisterHandler(new ReferenceLiveLoadHandler(
            checkpointWorker: null,
            controller:       igRrController,
            storageDirectory: storageDirectory));

        // CGF1-BATCH-23 A.2: dummy zone handler - IG acknowledges
        // PrepareZone / CommitZone without terrain DB load.
        // Full terrain-DB preload from scenario entities is future work.
        slave.RegisterHandler(new IgZoneDummyHandler(_effectiveInstanceId));

        // Wire ReferencePrefetchHandler so IG can stage scenario files and ACK.
        var igStorageProvider = new LocalDiskStorageProvider(storageDirectory);
        slave.RegisterHandler(new ReferencePrefetchHandler(igStorageProvider));

        // CGF1-S0309: wire dry-run snapshot/rewind handler (IG carries no ECS state in ClusterSlave).
        slave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

        // Diagnostics dump support: IG must ACK CollectDiagnostics in cluster 2PC.
        var archService = new ArchitectureDiagnosticsService(context.Kernel);
        var entityService = new EntityStateExtractionService(context.World, context.EntityMap);
        string logDirectory = !string.IsNullOrWhiteSpace(_hrotConfig.LogDirectory)
            ? _hrotConfig.LogDirectory
            : System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");
        var logService = new LogArchiveExtractionService(
            logDirectory,
            _hrotConfig.SubsystemName,
            context.NodeId);
        slave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
            _eventHistoryService!,
            archService,
            entityService,
            logService,
            _hrotConfig));

        return slave;
    }

    // ── Phase 6a: Register spawning pipeline ─────────────────────────────────

    /// <inheritdoc/>
    /// <summary>
    /// ⭐⭐⭐ <b>host (f) — IG composes the SAME entity-creation tier as every other ECS node.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b ·
    /// <c>Architect_Question_65</c> <c>Q65-A′</c>: every ECS node composes the full genesis pipeline;
    /// the pack has no opt-out, and a node that never creates locally simply never enqueues a
    /// self-targeted request.</para>
    ///
    /// <para>🔴 <b>What this replaces, and why the old arrangement was an accident.</b> IG used to
    /// register NO spawn pipeline at all, with the comment <i>"replaces SpawningModule so IG does not
    /// duplicate entities"</i>. ⛔ That prevented the double spawn by OMITTING the systems — an
    /// arrangement that only holds while nothing else consumes the order, and it is exactly what §3.4a
    /// identifies as the hazard: <c>FdpEventBus</c> is a broadcast, not a work queue, so two subscribers
    /// on one <c>SpawnEntityCommand</c> each act on it. ⇒ ⭐ the duplication is now prevented
    /// STRUCTURALLY, one level up: the tools post an INTENT, and
    /// <c>ForwardingEntityCreationRequestSource</c> decides per request whether this node services it or
    /// the NED egress sends it to the node that should.</para>
    ///
    /// <para>⭐⭐ <b>Why registering the spawn system does NOT reintroduce the double spawn.</b>
    /// 📐 Measured: IG's requests carry <c>OwnerAppInstanceId = 0</c> (untargeted — see
    /// <c>IgEntityCreationRequests</c>) and this node is NOT the broadcast arbiter, so
    /// <c>EntityCreationRouting.IsHandledLocally</c> is false for every one of them. The forwarder sends
    /// them, no local order is published, and the authoritative entity still comes back as a replicated
    /// ghost — today's behaviour exactly. ⭐ The systems are present and idle, which is what
    /// <c>Q65-A′</c> asks for: capability by composition, not by node role.</para>
    /// </summary>
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // ⭐ Every optional input is threaded from the SAME adapters object, exactly as CgfSubsystem
        //    does — the pack substitutes NullEntityAckSink when offline.
        var adapters = _networkFactory?.CreateCgfEntityLifecycleAdapters();

        var creation = EntityCreationPack.Build(new EntityCreationContext
        {
            World       = context.World,
            EntityMap   = context.EntityMap,
            TkbDb       = context.TkbDb!,
            IdAllocator = context.IdAllocator!,
            Elm         = (EntityLifecycleModule)context.BaseModules
                              .First(m => m is EntityLifecycleModule),
            NodeId      = context.NodeId,

            NetworkRequestSource  = adapters?.RequestSource,
            AckSink               = adapters?.AckSink,
            JsonAttributeCompiler = adapters?.JsonCompiler,
            OwnershipStrategy     = adapters?.OwnershipStrategy,

            // ⭐⭐⭐ D1: the forwarding half. Without it a request addressed elsewhere is silently
            //    dropped by the Level-1 guard, which is the other half of the level mismatch.
            RequestEgress         = adapters?.RequestEgress,

            // ⛔ NOT the cluster's broadcast arbiter — that is CGF, and exactly one node may be it.
            IsBroadcastArbiter = false,
        });

        // ⭐ The tools' sink. RegisterSpawningPipeline runs BEFORE RegisterApplicationSystems
        //    (SharedApplicationBootstrapper.cs:111 vs :139), so the registrar callback that constructs
        //    MapCommandController always sees a non-null value. Same arrangement as CgfSubsystem's
        //    _scenarioSource and EditorSubsystem's _scenarioLoadSource.
        LocalEntityCreationRequests = creation.LocalRequests;

        context.Kernel.RegisterGlobalSystem(creation.RequestSystem);       // Input
        context.Kernel.RegisterGlobalSystem(creation.FinalizationSystem);  // PostSimulation

        // ⭐⭐⭐ CE-144 RESOLVED 2026-09-03 — the spawn system IS scheduled, and IG's private
        //    GhostDestructionSystem is GONE. 📄 DESIGN_Entity_Creation_Unification.md §3.4c.
        //
        // 📐 What the old shortcut broke, measured: the wire dispose is written by
        //    CycloneNetworkCleanupSystem (phase Export), and it triggers on DestructionOrder — which only
        //    EntityLifecycleModule.BeginDestruction publishes. GhostDestructionSystem destroyed the entity
        //    WITHOUT the ELM, so no DestructionOrder, so no translator.Dispose(netId), so no DDS dispose
        //    sample. For an entity IG OWNS that means peers keep the instance forever, silently.
        //
        // ⇒ ONE consumer of DestroyEntityCommand on every host: NetworkSpawningSystem.ProcessDestroy,
        //    which sets TearDown and calls BeginDestruction. The map entry is then removed by the shared
        //    DisposalMonitoringSystem (NedReplicationModule.cs:420), which is exactly what the ghost path
        //    was doing eagerly.
        //
        // ⚠ The one behavioural difference: a ghost now outlives its dispose by ~1 frame
        //    (ELM.DrainInstantComplete requires currentFrame > StartFrame). That is the same latency every
        //    other host already has, and uniformity is the point.
        context.Kernel.RegisterGlobalSystem(creation.SpawnSystem);         // BeforeSync

        var unserviceable = creation.Unserviceable(new object[]
        {
            creation.RequestSystem, creation.FinalizationSystem, creation.SpawnSystem,
        });
        if (unserviceable.Length > 0)
            FdpLog<IgNodeBootstrapper>.Info(
                "[IG] entity-creation pieces not scheduled: {0}", unserviceable);

        // UnitHierarchySystem - maintains ECS commander-subordinate hierarchy on the IG node (CS016).
        context.Kernel.RegisterModule(new Fdp.ModuleHost.Scheduling.SingleSystemModule("UnitHierarchy", new UnitHierarchySystem()));
    }

    // ── Phase 6b: Register network translators ────────────────────────────────

    /// <inheritdoc/>
    protected override void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory? configuredFactory)
    {
        if (configuredFactory == null || context.Participant == null)
            return;

        // Use raw _networkFactory for methods that require a participant directly.
        NetworkAdapter = _networkFactory != null
            ? _networkFactory.CreateIgNetworkAdapter(context.Participant, _effectiveInstanceId)
            : NullIgNetworkAdapter.Instance;
        CommandGateway = NetworkAdapter?.CommandGateway;

        var translators = new List<INetworkTranslator>();

        // IG-specific ingress translators (entity context-actions, combat, etc.)
        // DO NOT add TimeNetworkModule translators here - base class Phase 6c handles them.
        if (_igTranslatorsProvider != null)
        {
            foreach (var t in _igTranslatorsProvider.GetTranslators(
                context.Participant,
                context.EntityMap,
                context.World.Bus,
                context.GhostCreationSystem,
                _effectiveInstanceId,
                _headless))
            {
                translators.Add(t);
            }
        }

        // D005: ACL egress translators convert bus events back to DDS.
        // Created via network factory to avoid direct NED type references in IG.
        if (_networkFactory != null)
        {
            foreach (var t in _networkFactory.CreateIgEgressTranslators(
                context.Participant, context.World.Bus, context.GeoTransform!, _effectiveInstanceId))
            {
                translators.Add(t);
            }
        }

        if (translators.Count > 0)
        {
            context.Kernel.RegisterGlobalSystem(
                new CycloneNetworkIngressSystem(translators.ToArray()));
            context.Kernel.RegisterGlobalSystem(
                new CycloneEgressSystem(translators.ToArray()));
            context.Kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(
                    translators.OfType<IDescriptorTranslator>()));
        }

        NetworkEnabled = true;
    }

    // ── Phase 6d: Application-level systems ──────────────────────────────────

    /// <inheritdoc/>
    protected override void RegisterApplicationSystems(HrotNodeContext context)
        => ApplicationSystemsRegistrar?.Invoke(context);
}
