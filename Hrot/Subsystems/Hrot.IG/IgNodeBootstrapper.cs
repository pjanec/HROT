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

    /// <summary>Orchestration event bus for NodeOp commands. Valid after BootstrapNode() returns.</summary>
    public FdpEventBus? OrchestrationBus { get; private set; }

    /// <summary>NodeOp slave translator wired to the DDS participant. Valid after BootstrapNode() returns.</summary>
    public NodeOpSlaveTranslator? IgSlaveTranslator { get; private set; }

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
        // ⭐⭐ CE-140 / CE-141 — IG's list feeds ONLY the GHOST projection, and its WIDTH is an OPEN
        //    QUESTION rather than a settled decision.
        // 📐 What IS measured: IG has no LOCAL materialisation. RegisterSpawningPipeline registers only
        //    GhostDestructionSystem + IgUnitHierarchyModule, and SpawnEntityCommand is forwarded to
        //    SimHost, whose authoritative ghost replicates back (see that method's comment). ⇒ this
        //    list reaches .WithTranslators(...) → NedReplicationModule's ghost projection, never a
        //    local spawn. ⭐ IG nonetheless ORIGINATES creation: the placement tool's
        //    MapCommandController.OnEntityCreatedByTool publishes a SpawnEntityCommand on IG's bus.
        // ⚠⚠ CORRECTED 2026-08-30. An earlier version of this comment said "⛔ Do NOT replace this
        //    with Base() — a shorter list is a real decision here." 🔴 That was ASSERTED, not measured,
        //    and the user was right to challenge it. 📐 Re-measured over IG's real registration path
        //    (HrotSharedComponentRegistry + IgRoleComponentRegistry): IG REGISTERS VehicleParams,
        //    PhysicsCollider, Health, WeaponState, PerceptionReceptor and TargetMemory — six components
        //    that TkbTranslatorSet.Base()'s kinematics/combat/perception translators would fill and
        //    that this 2-entry list leaves untouched on every ghost.
        // ⛔ Do NOT widen it on that basis alone either: those six are plausibly filled by DDS
        //    replication from SimHost instead, in which case TKB projection here is redundant. ⇒ the
        //    open question is WHICH source should populate a ghost's template-derived components, and
        //    it needs a live comparison, not a source reading. 📄 CE-141 and
        //    DESIGN_Entity_Creation_Unification.md §2.3.
        var translators = new List<ITkbEntityTranslator>
        {
            new SpatialCoreTkbTranslator(), // Enforces zero-initialization of spatial ECS chunks
            new PresentationTkbTranslator(),
        }.AsReadOnly();

        return new HrotNodeBuilder(config)
            .WithRole(config.SubsystemName, role)
            .WithNetworkFactory(networkFactory)
            .WithReplication(role)
            .WithBehaviorRegistry(GetBehaviorRegistry())
            .WithTranslators(translators)
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
    protected override IEnumerable<IEcsModule> GetAdditionalModules()
    {
        // E. StyleResolutionModule --- writes ResolvedStyle each Simulation tick
        yield return new StyleResolutionModule(_userConfig, _effectiveInstanceId);

        // F. MapCullingModule --- writes CullingState each PostSimulation tick
        yield return new MapCullingModule(_cameraViewport);

        // G2. MapLayerModule - assigns MapDisplayComponent bitmask per entity (time-sliced)
        yield return new MapLayerModule();

        // G. HistoryTrailModule --- records entity position trails (IG.4.1)
        yield return new HistoryTrailModule();

        // H. EventEffectModule --- spawns and cleans up visual effects (IG.4.2)
        if (!_headless)
            yield return new EventEffectModule();
    }

    // ── Phase 5: Build orchestration ─────────────────────────────────────────

    /// <inheritdoc/>
    protected override ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer)
    {
        // CMC-S016: each slave subsystem has its own orchestration bus + translator (Option C).
        var orchestrationBus = new FdpEventBus();
        OrchestrationBus = orchestrationBus;
        MigrationServices = HrotMigrationBootstrap.BuildIg();

        // CGF1-S0104: wire ClusterSlave once DDS participant is confirmed healthy.
        // Use _effectiveInstanceId (= _nodeIdOverride when set, else IgNetworkConstants.InstanceId=300)
        // so the IG ClusterSlave always registers on a cluster-unique node ID.
        // Using IgNetworkConstants.LocalNodeId (1) caused collision with SimHost when --node-id 0.
        var slave = new ClusterSlave(_effectiveInstanceId, "IG", orchestrationBus);

        if (context.Participant != null)
        {
            IgSlaveTranslator = new NodeOpSlaveTranslator(
                commandReader:   new DdsReader<Hrot.NED.Descriptors.Orchestration.NodeOpCommand>(context.Participant),
                statusWriter:    new DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeOpStatus>(context.Participant),
                heartbeatWriter: new DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeHeartbeat>(context.Participant),
                bus:             orchestrationBus,
                nodeId:          _effectiveInstanceId);
        }

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

        // B. Ghost destruction - tears down ghosts replicated from the owning node on EntityMaster
        // DISPOSE. Still required: most entities IG shows are owned elsewhere.
        context.Kernel.RegisterGlobalSystem(new GhostDestructionSystem(context.EntityMap));

        // ⛔⛔⛔ THE SPAWN SYSTEM IS DELIBERATELY NOT SCHEDULED ON IG — and this is a STOP, not a choice
        //    made lightly. 📄 DESIGN_Entity_Creation_Unification.md §3.4b; escalated for a decision.
        //
        // 📐 Measured, and it is EntityGenesisHazardRails that caught it: scheduling
        //    NetworkSpawningSystem here puts ProcessDestroy on the same bus event GhostDestructionSystem
        //    already consumes. That is CE-144's DESTROY hazard, and it fails SILENTLY in the worse
        //    direction — ghost-first means ELM teardown never runs, EntityMaster is never disposed on the
        //    wire, and PEERS keep the drawing as a zombie forever. ⚠ Nobody finds that by running the node
        //    they changed.
        //
        // ⭐ Nothing is lost today: IG's requests are untargeted (IgEntityCreationRequests) and this node
        //    is not the broadcast arbiter, so IsHandledLocally is false for every one of them — the
        //    forwarder sends them and no local order is ever published. The spawn system would be idle.
        //
        // ⇒ ⛔ Before IG may materialise locally, the two destroy consumers must be reconciled. Declaring
        //    the omission here keeps it LOUD rather than silent, which is exactly what Unserviceable is for.
        var unserviceable = creation.Unserviceable(new object[]
        {
            creation.RequestSystem, creation.FinalizationSystem,
        });
        if (unserviceable.Length > 0)
            FdpLog<IgNodeBootstrapper>.Info(
                "[IG] entity-creation pieces deliberately not scheduled (see CE-144 destroy hazard): {0}",
                unserviceable);

        // UnitHierarchySystem - maintains ECS commander-subordinate hierarchy on the IG node (CS016).
        context.Kernel.RegisterModule(new IgUnitHierarchyModule(new UnitHierarchySystem()));
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
