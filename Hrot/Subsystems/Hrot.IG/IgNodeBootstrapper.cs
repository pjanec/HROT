using CarKinem.Core;
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
using Hrot.IG.Translators;
using Hrot.Map.Common;
using Hrot.Network.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.IG;

/// <summary>
/// Concrete <see cref="SharedApplicationBootstrapper"/> for the IG (Image Generator) node.
/// Implements all abstract hooks to produce a visualization-only node with role
/// <see cref="NodeRole.ImageGenerator"/>.
/// </summary>
internal sealed class IgNodeBootstrapper : SharedApplicationBootstrapper
{
    private readonly INetworkFactory? _networkFactory;
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

        // ST-022 (Q52 §2): the schema the GIZMO PROJECTOR FAMILIES require, as opposed to the schema
        // this node's own role needs. IG declares four projector families in Phase 6d and
        // StatelessGizmoRegistry validates each projector's required components against the world --
        // so this must run first, and Phase 2 is where it belongs.
        //
        // ⭐ Registering these types does NOT put brain/combat data on an IG entity, and is not meant to:
        // 🔒 "support all and decide on current presence of component". No IG entity carries
        // BrainBlackboard, so HillAttackGizmo matches nothing and never draws -- the ruling holds by DATA,
        // not by the host curating which families it declares.
        //
        // ⛔ Without it, --mode ig died in bootstrap (ST-020). --mode all masked that, because SimHost's
        // registries supply the same schema on the shared world.
        Hrot.Common.Diagnostics.Gizmos.MapSchemaPack.RegisterAll(world);

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
    protected override void RegisterSpawningPipeline(HrotNodeContext context)
    {
        // B. Ghost destruction - replaces SpawningModule so IG does not duplicate entities.
        // SpawnEntityCommand is forwarded to SimHost via SpawnEntityCommandEgressTranslator;
        // SimHost creates the authoritative ghost which DDS replicates back.
        // GhostDestructionSystem tears down those ghosts on EntityMaster DISPOSE.
        context.Kernel.RegisterGlobalSystem(new GhostDestructionSystem(context.EntityMap));

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
