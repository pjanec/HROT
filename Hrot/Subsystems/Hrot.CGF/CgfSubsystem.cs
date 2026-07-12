using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Core.Logging;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
// (Phase 5: Fdp.Toolkit.Vis2D.Tools removed with StandardInteractionTool)
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Configuration;
using Hrot.CGF.Systems;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Common.Interactions;
using Hrot.Common.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Hrot.AI.Behaviors.Mappers;
using Hrot.Presentation.Windows;
using Hrot.Presentation.Facades;
using Hrot.Presentation.Renderers;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Menus;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Panels;
using Hrot.SimHost;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Blueprints.Editor.Debug;
using StructEdit.Reflection;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using Raylib_cs;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel    = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpInspectorState       = Fdp.Presentation.Abstractions.InspectorState;
using FdpRepositoryAdapter    = Fdp.Presentation.Adapters.RepositoryAdapter;

namespace Hrot.CGF;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// Migrated in EAM-M003 to use <see cref="HrotNodeBuilder"/> instead of <see cref="CgfApplication"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem, Fdp.Toolkit.Runner.IMapCameraProvider, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
{
    private HrotNodeContext?  _context;
    private NetworkEntityMap? _entityMap;
    private Action?           _cgfNetworkPolling;

    // ── Headless + behavior registry ──────────────────────────────────────────
    private bool               _headless;    private ClusterTimeTransportAdapter? _clusterTimeAdapter;    private BehaviorRegistry?  _behaviorRegistry;
    private TogglableInputGroup?      _toggleInput;
    private TogglableSimulationGroup? _toggleSim;
    private Hrot.Core.Network.INetworkFactory? _networkFactory;
    private PhysicsToolkitModule? _physicsModule;

    // ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
    private EntityRepository?       _bpPreTickSnapshot;
    private DebugSnapshotProvider?  _bpSnapshotProvider;
    private DataBreakpointManager?  _bpManager;
    private DataBreakpointSystem?   _bpSystem;

    // ── Scenario entity creation source (shared with load handlers in Phases 3-4) ──
    private ScenarioEntityCreationRequestSource? _scenarioSource;

    // ── Blueprint materialization (BSA-203) ────────────────────────────────────
    private BlueprintRegistry? _blueprintRegistry;

    /// <summary>
    /// Exposes the scenario entity creation request source for load handlers (Phases 3-4).
    /// Available after <see cref="Initialize"/> has been called.
    /// </summary>
    internal ScenarioEntityCreationRequestSource? ScenarioEntityCreationSource => _scenarioSource;

    // ── Visualization ─────────────────────────────────────────────────────────
    private MapCanvas?                 _canvas;
    private DefaultSelectionState?     _selectionState;
    // (Phase 5: _interactionTool removed; entity selection via ECS gizmos)
    private EntityQuery?               _entityQuery;
    private Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? _cgfGizmoBuffer;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager? _cgfGizmoManager;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem? _cgfDataDrivenGizmoSystem;
    private Fdp.Core.FdpEventBus? _cgfInteractionBus;
    private Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? _cgfGizmoController;
    // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
    internal Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController CgfGizmoController => _cgfGizmoController!;
    // GZH-014: explicit interface implementation — avoids renaming the existing property.
    Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController? Hrot.Common.Diagnostics.Gizmos.IGizmoControllable.GizmoController
        => _cgfGizmoController;

    // ── FDP panels ────────────────────────────────────────────────────────────
    private FdpEntityInspectorPanel              _fdpEntityInspector = new();
    private FdpEventBrowserPanel                 _fdpEventBrowser    = null!;
    private DiagnosticEventHistoryService        _fdpEventHistory    = new();
    private FdpRepositoryAdapter?                _fdpRepoAdapter;
    private FdpInspectorState       _fdpInspectorState  = new();
    private uint                    _fdpFrameCount;

    // ── Map context menu ──────────────────────────────────────────────────────
    private DebugGizmoLayer? _cgfGizmoLayer;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.57f, 0.47f, 0.04f, 1f);

    /// <summary>Creates CgfSubsystem without a network factory (legacy / headless path).</summary>
    public CgfSubsystem() { }

    /// <summary>Creates CgfSubsystem with an injected protocol factory from the composition root.</summary>
    public CgfSubsystem(Hrot.Core.Network.INetworkFactory networkFactory)
    {
        _networkFactory = networkFactory;
    }

    /// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
    internal NetworkEntityMap? GhostEntityMap => _entityMap;

    /// <summary>TestHook: exposes the CGF ECS world for integration tests.</summary>
    internal Fdp.Core.EntityRepository? World => _context?.World;

    /// <summary>Internal test hook: exposes the data breakpoint manager (UBP-P10T2).</summary>
    internal IDataBreakpointManager? DataBreakpointManager => _bpManager;

    /// <summary>Internal test hook: exposes the debug snapshot provider (UBP-P10T2).</summary>
    internal DebugSnapshotProvider? BpSnapshotProvider => _bpSnapshotProvider;

    /// <summary>TestHook: exposes the CGF behavior registry so integration tests can register
    /// scenario-specific behaviors (e.g. UrbanCombat) before the cluster transitions to
    /// OperatingLive and scenario entities begin executing missions.</summary>
    internal BehaviorRegistry? TestHook_BehaviorRegistry => _behaviorRegistry;

    /// <summary>
    /// TestHook: spawns an entity and publishes a <c>DeferredTakeOwnership</c> routing table
    /// that assigns the WorldPos descriptor to <paramref name="muscleNodeId"/>.
    ///
    /// <para>Mirrors what a full <c>CreateEntityRequestSystem(isDefaultProcessor:true)</c> would do
    /// without requiring ExCon wiring in integration tests.</para>
    /// </summary>
    internal long TestHook_SpawnEntityWithSplitAuthority(long tkbType, int muscleNodeId)
    {
        if (_context == null)
            throw new System.InvalidOperationException("CgfSubsystem not initialized.");

        long networkId = _context.IdAllocator?.AllocateId()
            ?? unchecked((long)System.Threading.Interlocked.Increment(ref _testIdCounter));

        // 1. Publish DeferredTakeOwnership FIRST (pre-genesis, before EntityMaster).
        var dtoCmd = new DeferredTakeOwnershipCommand { NetworkId = networkId };
        long worldPosId  = _networkFactory?.WorldPosDescriptorId          ?? 0;
        long navStatusId = _networkFactory?.NavigationStatusDescriptorId   ?? 0;
        if (worldPosId != 0)
            dtoCmd.Grants.Add(new DescriptorGrant { DescriptorTypeId = worldPosId,  NodeId = muscleNodeId });
        if (navStatusId != 0)
            dtoCmd.Grants.Add(new DescriptorGrant { DescriptorTypeId = navStatusId, NodeId = muscleNodeId });
        _context.World.Bus.PublishManaged(dtoCmd);

        // 2. Publish SpawnEntityCommand (CGF/Brain owns entity identity).
        _context.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId   = networkId,
            TkbType     = tkbType,
            OwnerNodeId = _context.NodeId,
            InitType    = Fdp.Toolkit.Replication.ReliableInitType.AllPeers,
            RequestId   = System.Guid.Empty,
        });

        return networkId;
    }

    private int _testIdCounter;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {        _headless = config.Headless;
        int cgfNodeId = config.NodeId != 0 ? config.NodeId : 400;
        string baseTempRoot = OrchestrationConstants.ResolveStagingRoot();
        string isolatedTempRoot = System.IO.Path.Combine(baseTempRoot, "nodes", $"node-{cgfNodeId}");
        string resolvedLogDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs");
        // ── Create DDS participant in the Application Shell (Composition Root) ───
        // Rule: only the outermost executable may instantiate DdsParticipant.
        // HrotNodeBuilder no longer has a fallback.
        var shellParticipant = _networkFactory?.Participant;
        if (shellParticipant == null)
        {
            shellParticipant = HrotEnvironment.CreateParticipant(config.DomainId);
            shellParticipant.EnableSenderTracking(new SenderIdentityConfig
            {
                AppDomainId   = config.DomainId,
                AppInstanceId = cgfNodeId,
            });
        }
        // ── Build common infrastructure ────────────────────────────────────────
        var nodeConfig = new HrotNodeConfig
        {
            DomainId            = config.DomainId,
            NodeId              = cgfNodeId,
            // CgfSubsystem always creates a DDS participant — Headless here controls only
            // the Raylib/ImGui window (UI), not the network layer.
            // This mirrors SimHostApp which also hardcodes Headless = false for HrotNodeConfig.
            Headless            = false,
            ExternalParticipant = shellParticipant,
            LocalTempRoot       = isolatedTempRoot,
            LogDirectory        = resolvedLogDir,
            SubsystemName       = "CGF",
        };
        _context = new HrotNodeBuilder(nodeConfig)
            .WithRole("CgfNode", NodeRole.Brain)
            .WithNetworkFactory(_networkFactory)
            .Build();

        _entityMap = _context.EntityMap;
        _context.World.SetSingletonManaged<NetworkEntityMap>(_entityMap!);
        CgfComponentRegistry.RegisterAll(_context.World);

        // ── Register base infrastructure modules ───────────────────────────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // Allocate RaycastBatchData so Action_QueryRaycast can enqueue/query requests on CGF.
        _physicsModule = new PhysicsToolkitModule();
        _physicsModule.Initialize(_context.World);

        // ── Create replication module via factory (Brain role) ─────────────────
        // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
        var behaviorRegistry = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(behaviorRegistry, _context.GeoTransform, _entityMap);
        _behaviorRegistry = behaviorRegistry;

        // Expose the registry to the diagnostic renderers so the entity inspector
        // can project BrainBlackboard memory and visualize the BTree execution state.
        BrainBlackboardRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;
        BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
        Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor   = behaviorRegistry;

        // Wire the FDP-layer trace emitter to the NLog-backed BehaviorLog (behav-diag-1).
        // Idempotent: safe to overwrite on hot-reload.
        Fdp.Toolkit.Behavior.Diagnostics.BehaviorTraceLog.Instance =
            new Hrot.AI.Behaviors.Logging.BehaviorTraceLogEmitter();

        // Configure network factory for this node so auxiliary translators can be created.
        var nodeFactory = _networkFactory?.ConfigureForNode(_context, NodeRole.Brain, behaviorRegistry);

        var replicationModule = nodeFactory?.CreateReplicationModule();
        if (replicationModule != null)
        {
            _context = _context with
            {
                NedReplication      = replicationModule as Hrot.Common.Abstractions.INedReplicationModule,
                GhostCreationSystem = replicationModule.GhostCreationSystem,
            };
            _context.Kernel.RegisterModule(replicationModule);
        }

        // ── Wire CreateEntityRequestSystem (CGF is the cluster-default processor) ─
        // This makes CGF intercept broadcast CreateEntityRequests (Owner == 0) and spawn
        // entities, delegating WorldPos (kinematics) to the least-loaded Muscle node via
        // DeferredTakeOwnership. SimHost nodes keep isDefaultProcessor=false.
        // Protocol-specific sources and sinks are obtained via the factory (Rule 3).

        // Create the scenario source once; shared with load handlers in Phases 3-4
        // via CgfLogicPack.ScenarioSource.
        _scenarioSource = new ScenarioEntityCreationRequestSource();

        // ── Blueprint registry (shared by materialization system and serializers) ──
        _blueprintRegistry = new BlueprintRegistry();

        // Populate the registry from the generated blueprint registrars in the AI assembly.
        // skipOnUnknownParam=true: silently skips AiBehaviorFactory.RegisterAll (which expects
        // IGeographicTransform / NetworkEntityMap); those behaviors are already wired by
        // CgfBehaviorSetup.LoadFromAiAssembly with proper geo/entity context above.
        //
        // Pass the LIVE behaviorRegistry (not a throwaway) so the JSON-defined BTree/HSM
        // bridges register their BehaviorDefinitions into the running registry — giving the
        // game the same set of JSON-authored behaviors the editor sees. The scanner injects
        // an ActionRegistry populated from the assembly's [FbtRegistrar], so those trees'
        // bound actions/conditions execute real logic at runtime.
        {
            var bpStaging = new BlueprintRegistryStaging();
            BlueprintRegistrarScanner.Scan(
                typeof(Hrot.AI.Behaviors.AiBehaviorFactory).Assembly,
                bpStaging,
                behaviorRegistry,
                skipOnUnknownParam: true);
            _blueprintRegistry.CommitStaging(bpStaging);
        }

        // Expose the blueprint registry to the Entity Inspector renderers so
        // BlueprintBlackboard* components can show per-tier slot summaries.
        Hrot.Presentation.Renderers.BlueprintBlackboard1024Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
        Hrot.Presentation.Renderers.BlueprintBlackboard4096Renderer.BlueprintRegistryAccessor  = _blueprintRegistry;
        Hrot.Presentation.Renderers.BlueprintBlackboard16384Renderer.BlueprintRegistryAccessor = _blueprintRegistry;

        // ── Register CGF simulation logic (Brain-specific) ─────────────────────
        var mapperRegistry = new TacticalIntentMapperRegistry();
        mapperRegistry.Register(new DefendAreaMapper());
        mapperRegistry.Register(new HullDownAttackMapper());
        var cgfLogicPack = new CgfLogicPack(behaviorRegistry, _entityMap, _scenarioSource,
            mapperRegistry);
        _context.Kernel.RegisterModule(new BehaviorDiagnosticsModule());
        _context.Kernel.RegisterModule(cgfLogicPack);

        // Execute the Brain systems every frame via two togglable phase groups.
        _toggleInput = new TogglableInputGroup("CgfInput",           cgfLogicPack.InputSystems);
        _toggleSim   = new TogglableSimulationGroup("CgfSimulation", cgfLogicPack.SimulationSystems);

        _context.Kernel.RegisterGlobalSystem(_toggleInput);
        _context.Kernel.RegisterModule(new CgfSimulationModule(_toggleSim));

        var adapters = nodeFactory?.CreateCgfEntityLifecycleAdapters();

        var tkbDb       = _context.TkbDb!;
        var idAllocator = _context.IdAllocator!;
        var elm         = (EntityLifecycleModule)_context.BaseModules
                              .First(m => m is EntityLifecycleModule);

        // 1. Composite request source: always include the scenario source; add the live
        //    NED adapter source only when network is available.
        var requestSources = new System.Collections.Generic.List<IEntityCreationRequestSource>
        {
            _scenarioSource!
        };
        if (adapters != null)
            requestSources.Add(adapters.RequestSource);
        var compositeRequestSource = new CompositeEntityCreationRequestSource(requestSources);

        // 2. ACK sink: real NED sink when connected; null-object for offline / headless runs.
        IEntityAckSink ackSink = adapters?.AckSink ?? new NullEntityAckSink();

        var finalizationSystem = new EntityRequestFinalizationSystem(ackSink, _entityMap!);

        // 3. Register the core genesis pipeline unconditionally (online and offline).
        var requestSystem = new CreateEntityRequestSystem(
            requestSource:        compositeRequestSource,
            ackSink:              ackSink,
            tkbDb:                tkbDb,
            idAllocator:          idAllocator,
            localNodeId:          _context.NodeId,
            jsonAttributeCompiler: adapters?.JsonCompiler,
            finalizationSystem:   finalizationSystem,
            isDefaultProcessor:   true,
            ownershipStrategy:    adapters?.OwnershipStrategy);

        var spawnSystem = new NetworkSpawningSystem(
            tkbDb,
            elm,
            _entityMap!,
            idAllocator,
            _context.NodeId);

        _context.Kernel.RegisterGlobalSystem(spawnSystem);
        _context.Kernel.RegisterGlobalSystem(requestSystem);
        _context.Kernel.RegisterGlobalSystem(finalizationSystem);
        _context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(_entityMap!));
        Hrot.SimHost.Systems.BlueprintGenesisRuntimeRegistration.RegisterBlueprintGenesisSystems(
            _context.Kernel, _blueprintRegistry!);

        // 4. Network-dependent deletion routing: only when a live adapter exists.
        if (adapters != null)
        {
            var deleteSystem = new DeleteEntityRequestSystem(
                adapters.DeleteSource,
                adapters.AckSink,
                _entityMap!,
                finalizationSystem,
                _context.NodeId);

            _context.Kernel.RegisterGlobalSystem(deleteSystem);

            // Store polling action for heartbeat updates in Update().
            _cgfNetworkPolling = adapters.PollNetwork;
        }

        // Auxiliary translators (time-sync, combat, mission-control) via the injected factory.
        // Mirrors SimHostApp.cs pattern: nodeFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(kernel)
        nodeFactory?.CreateSimHostAuxiliaryTranslators()?.RegisterOn(_context.Kernel);
        nodeFactory?.CreateSimHostPerceptionTranslators()?.RegisterOn(_context.Kernel);
        nodeFactory?.CreateSimHostPathfindingTranslators()?.RegisterOn(_context.Kernel);


        // ── Wire ClusterSlave with EcsRecordReplayController (CGF-Point-4) ────────
        // Create a fresh ClusterSlave manually to strictly control handler registration order.
        var newClusterSlave = new ClusterSlave(_context.NodeId, "CGF", _context.EventBus);

        var nedModuleForAfterSeek = replicationModule as Hrot.Common.Abstractions.INedReplicationModule;
        Action? afterSeekAction = nedModuleForAfterSeek?.AfterSeekCallback;

        var rrController = new Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController(
            _context.Kernel, _context.NodeId, _context.World, afterSeek: afterSeekAction);

        var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);

        // 1. Replay handler (must be first to gate Live-from-Replay branch)
        newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
            rrController,
            inputGroup:            _toggleInput,
            simGroup:              _toggleSim,
            postSimGroup:          null,
            lifecycleGroup:        null,
            bypassLifecycleToggle: null,
            storageDirectory:      isolatedTempRoot,
            suspendGlobalTimePush: _context.Kernel.SuspendGlobalTimePush,
            resumeGlobalTimePush:  _context.Kernel.ResumeGlobalTimePush));

        // 2. CGF-Authoritative Scenario and Episode Load Handlers (must be BEFORE ReferenceLiveLoadHandler)
        var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(_behaviorRegistry!);
        var scenarioLoader     = new HrotScenarioLoader(storageProvider, scenarioSerializer.SubsystemType);
        var cgfIdAllocator     = new SequentialIdAllocator();
        var behaviorRemapper   = CgfBehaviorSetup.CreateBehaviorRemapper();
        var extractor          = new Hrot.CGF.Orchestration.StagingEntityExtractor();

        newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfScenarioLoadHandler(
            scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World,
            remapper: behaviorRemapper, controller: rrController,
            storageDirectory: isolatedTempRoot));

        newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfEpisodeLoadHandler(
            scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World, behaviorRemapper));

        // 3. Fallback Live Load Handler (claims PrepareLive ONLY if scenario handlers didn't)
        newClusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
            checkpointWorker: null,
            controller: rrController,
            storageDirectory: isolatedTempRoot));

        // 4. Utility handlers
        newClusterSlave.RegisterHandler(new ReferencePreviewHandler(_context.World));
        newClusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));
        newClusterSlave.RegisterHandler(new ReferenceArchiveHandler(
            isolatedTempRoot, _context.NodeId));
        var cgfArchService = new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(_context.Kernel);
        var cgfEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_context.World, _context.EntityMap, scenarioSerializer);
        _fdpEntityInspector.ExtractionService = cgfEntityService;
        _fdpEntityInspector.Serializer        = scenarioSerializer;
        var cgfLogService = new Hrot.Core.Diagnostics.LogArchiveExtractionService(
            string.IsNullOrWhiteSpace(nodeConfig.LogDirectory)
                ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
                : nodeConfig.LogDirectory,
            nodeConfig.SubsystemName,
            nodeConfig.NodeId);
        newClusterSlave.RegisterHandler(new Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler(
            _fdpEventHistory,
            cgfArchService,
            cgfEntityService,
            cgfLogService,
            nodeConfig));

        _context = _context with
        {
            ClusterSlave = newClusterSlave
            // Note: SlaveTranslator is already correctly populated by HrotNodeBuilder earlier
        };



        // ── Initialize ─────────────────────────────────────────────────────────
        _fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
        _context.Kernel.RegisterGlobalSystem(
            new EventHistoryCaptureSystem("World", _fdpEventHistory, _context.World.Bus));
        _context.Kernel.RegisterGlobalSystem(
            new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _context.EventBus));

        // GZ057: CGF entity presentation gizmos. Buffer and registry must be set up
        // before Kernel.Initialize() because the GizmoInteractionModule is registered here.
        _cgfGizmoBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        _cgfInteractionBus = new Fdp.Core.FdpEventBus();
        _cgfGizmoManager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(_cgfGizmoBuffer, _cgfInteractionBus);
        var cgfStatelessRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry();
        var cgfGizmoRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry();
        var cgfSettingsRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();
        // Auto-register all [GizmoProjector]-decorated gizmos in Hrot.CGF.
        Hrot.CGF.Gizmos.GizmoRegistrar.RegisterAll(cgfGizmoRegistry, cgfStatelessRegistry, cgfSettingsRegistry);
        // Register CanvasContextMenuGizmo for empty-space right-click context menus.
        Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(cgfGizmoRegistry, cgfStatelessRegistry, cgfSettingsRegistry);
        _cgfDataDrivenGizmoSystem = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem(
                cgfGizmoRegistry, _cgfGizmoBuffer, isSelectedPredicate: null, interactionBus: _cgfInteractionBus);
        // Route gizmo interaction translators and publisher through the network factory
        // so that CgfSubsystem has no direct dependency on Hrot.Network.NED.
        CycloneNetworkIngressSystem? cgfGizmoIngress = null;
        CycloneEgressSystem? cgfGizmoEgress = null;
        if (_networkFactory != null)
        {
            // CGF is always headless (receives UI interactions from remote viewer).
            var gizmoTranslators = _networkFactory.CreateGizmoTranslators(_cgfInteractionBus, _context.NodeId, headless: true);
            var ingressList = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
            var egressList  = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
            foreach (var t in gizmoTranslators)
            {
                if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Ingress) != 0) ingressList.Add(t);
                if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Egress)  != 0) egressList.Add(t);
            }
            if (ingressList.Count > 0)
                cgfGizmoIngress = new CycloneNetworkIngressSystem(ingressList.ToArray());
            if (egressList.Count > 0)
                cgfGizmoEgress = new CycloneEgressSystem(egressList.ToArray());
            var publisherSystem = _networkFactory.CreateGizmoPublisherSystem(_cgfGizmoBuffer, _context.NodeId);
            if (publisherSystem != null)
                _context.Kernel.RegisterGlobalSystem(publisherSystem);
        }
        var cgfGizmoGroup = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup("GizmoExecution",
            _cgfGizmoManager,
            _cgfDataDrivenGizmoSystem,
            new Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem(cgfStatelessRegistry, _cgfGizmoBuffer));
        // GZH-003: CGF is headless-first; enable only when a terminal connects.
        cgfGizmoGroup.Enabled = false;
        _cgfGizmoController = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoExecutionController(
            cgfGizmoGroup, _cgfGizmoManager, _cgfDataDrivenGizmoSystem);
        _context.Kernel.RegisterModule(new GizmoInteractionModule(
            _cgfInteractionBus,
            contextIngress: null,
            interactionSystems: new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[]
            {
                cgfGizmoGroup,
            },
            gizmoIngress: cgfGizmoIngress,
            gizmoEgress:  cgfGizmoEgress));
        _context.Kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _cgfInteractionBus));
        // Register canvas menu update so CanvasContextMenuGizmo has state to project.
        _context.Kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());

        // ── Universal breakpoints (UBP-P10T2) ────────────────────────────────────
        // CGF uses a SlaveSyncController so we supply a no-op time adapter.
        // The breakpoint manager still collects data; pause/step are no-ops for slave nodes.
        _bpPreTickSnapshot = new EntityRepository();
        CgfComponentRegistry.RegisterAll(_bpPreTickSnapshot);

        var bpTimeAdapter          = new CgfNoOpTimeController();
        var bpEditSvc              = new ComponentEditServiceBuilder().Build();
        var bpPredicateCompiler    = new PredicateCompiler(bpEditSvc, _behaviorRegistry);
        var bpEventScannerCompiler = new EventScannerCompiler(bpEditSvc);
        _bpSnapshotProvider        = new DebugSnapshotProvider(_bpPreTickSnapshot);
        _bpManager                 = new DataBreakpointManager(
            _context.World, _bpPreTickSnapshot, _bpSnapshotProvider,
            bpTimeAdapter, bpPredicateCompiler, bpEventScannerCompiler);
        bpTimeAdapter.SetManager(_bpManager);
        _bpSystem                  = new DataBreakpointSystem(_bpManager, _context.World.Bus);

        _context.Kernel.RegisterGlobalSystem(_bpSnapshotProvider);
        _context.Kernel.RegisterGlobalSystem(_bpSystem);
        // ─────────────────────────────────────────────────────────────────────────

        _context.Kernel.Initialize();
        // ── Visualization (non-headless only) ─────────────────────────────────────
        if (!_headless)
        {
            _entityQuery = _context.World.Query().With<NetworkIdentity>().Build();

            _canvas = new MapCanvas();
            _canvas.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);

            _selectionState    = new DefaultSelectionState();
            _fdpRepoAdapter    = new FdpRepositoryAdapter(_context.World);

            // GZ057: add gizmo layer so CGF entity presentation primitives are rendered.
            _cgfGizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, _cgfGizmoBuffer, _cgfInteractionBus!);
            _canvas.AddLayer(_cgfGizmoLayer);
            _canvas.DrawBuffer = _cgfGizmoBuffer;

            // (Phase 5: StandardInteractionTool removed; entity interaction via ECS gizmos)

            // Register context menu handler for right-click in the entity inspector panel.
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                builder.AddItem("Center on entity", () => CenterCameraOnEntity(entity));
                builder.AddItem("Select entity", () =>
                {
                    _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                });
                builder.AddSeparator();
                builder.AddItem("Delete entity", () => DeleteEntity(entity));
                if (_context!.World.HasComponent<Fdp.Core.SimTransform>(entity))
                    builder.AddItem("Rotate", () =>
                    {
                        _selectionState.PrimarySelected = entity;
                        _cgfDataDrivenGizmoSystem!.DeactivateGizmo(entity);
                        var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                            _context.World, entity,
                            onRemove: () => _cgfDataDrivenGizmoSystem!.DeactivateGizmo(entity));
                        _cgfDataDrivenGizmoSystem!.ActivateGizmo(entity, gizmo);
                    });
            }));
        }    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        // Poll network state (e.g. DDS NodeHeartbeat) to keep the cluster cache up-to-date
        // so that BrainMuscleOwnershipStrategy can find the least-loaded Muscle node.
        _cgfNetworkPolling?.Invoke();

        _context?.SlaveTranslator?.Tick();
        _context?.ClusterSlave.Tick();
        _clusterTimeAdapter?.Update();

        // Evict transient primitives and advance persistence clock before backend population.
        _cgfGizmoBuffer?.EndFrame(deltaTime);

        // Use the no-args kernel update so the SlaveSyncController measures the real
        // wall-clock delta between frames.  The legacy Update(float) path would receive
        // dt=0 from the SubsystemOrchestrator in headless mode, zeroing out every
        // DeltaTime-dependent system (e.g. ThreatEvaluationSystem boost/decay).
        _context?.Kernel.Update();
        if (!_headless && _context != null)
        {
            _fdpFrameCount++;
            _canvas?.Update(deltaTime);
        }
        _context?.EventBus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void DrawWorld()
    {
        if (!_headless) _canvas?.Draw();
    }

    /// <inheritdoc/>
    public void DrawUI()
    {
        if (_headless) return;

        // Render the context menu popup via the gizmo layer's ContextMenuAdapter.
        _cgfGizmoLayer?.DrawContextMenu();
    }

    /// <inheritdoc/>
    public MapCameraView? GetCameraView() => _canvas?.Camera?.GetCameraView();

    /// <inheritdoc/>
    public void ApplyCameraView(MapCameraView view) => _canvas?.Camera?.ApplyCameraView(view);

    // Non-interface helper kept for backward-compat with tests.
    public MapCamera? GetMapCamera() => _canvas?.Camera;

    /// <inheritdoc/>
    public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
    {
        if (_headless) return;

        // Create a map-pick bridge so component fields tagged [MapPickable] can be edited.
        CanvasMapPickAdapter? cgfCanvasAdapter = _canvas != null && _context?.World != null
            ? new CanvasMapPickAdapter(_canvas, _context.World, globalGizmoManager: _cgfGizmoManager)
            : null;
        MapPickServiceBridge? cgfPickBridge = cgfCanvasAdapter != null
            ? new MapPickServiceBridge(cgfCanvasAdapter, _context!.World)
            : null;

        FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
            _fdpEntityInspector,
            windowManager,
            "CGF",
            () => _fdpRepoAdapter,
            cgfPickBridge,
            TitleBarColor);

        windowManager.RegisterWindow(new FdpEntityInspectorWindow(
            "cgf_fdp_inspector", "CGF Entity Inspector", "CGF",
            _fdpEntityInspector,
            () => _fdpRepoAdapter,
            () => _fdpInspectorState,
            TitleBarColor));

        // Register the blackboard view provider so the editor projects typed DTO params.
        _fdpEntityInspector.Reflector.AddBufferViewProvider(new BrainBlackboardViewProvider());
        // Register the heavy blackboard view provider for Blackboard1024.
        _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.Blackboard1024ViewProvider());

        // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType/HeavyDtoType to StructEdit.
        var capturedRegistry = _behaviorRegistry;
        _fdpEntityInspector.Reflector.EditContextFactory = (session, e, type) =>
        {
            if (type != typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)
             && type != typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024)) return null;
            if (!session.HasComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))) return null;
            var ds = session.GetComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))
                as Fdp.Toolkit.Behavior.Components.BehaviorState?;
            if (ds == null) return null;
            if (capturedRegistry?.TryGetDefinition(ds.Value.ActiveBehaviorHash, out var def) != true) return null;
            if (def == null) return null;
            if (type == typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard))
            {
                if (def.ParamsDtoType == null) return null;
                return new StructEdit.Core.EditContext().With("ParamsDtoType", def.ParamsDtoType);
            }
            // Blackboard1024
            if (def.HeavyDtoType == null) return null;
            return new StructEdit.Core.EditContext().With("HeavyDtoType", def.HeavyDtoType);
        };

        windowManager.RegisterWindow(new FdpEventBrowserWindow(
            "cgf_fdp_events", "CGF Event Browser", "CGF",
            _fdpEventBrowser,
            TitleBarColor));

        windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
            "cgf_architecture_diagnostics", "CGF Architecture Diagnostics", "CGF",
            new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => _context?.Kernel)),
            TitleBarColor));

        // ── Time transport controls in status bar ─────────────────────────
        var bus = _context?.EventBus;
        if (bus != null)
        {
            _clusterTimeAdapter = new ClusterTimeTransportAdapter(
                bus, () => _context?.Kernel.CurrentTime.TotalTime ?? 0.0);
            var timeSection = new ClusterTimeControlStatusBarSection(_clusterTimeAdapter);
            windowManager.StatusBar.RegisterSection(
                id:             "cgf_time_controls",
                sortOrder:      100,
                renderDelegate: timeSection.Render,
                perspective:    "CGF");
        }

        // Register the AI Behaviors log tab (dedicated tab for structured AI diagnostics).
        windowManager.MessageLogRegistry?.RegisterSource(AiBehaviorLogTarget.SharedInstance);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void CenterCameraOnEntity(Entity entity)
    {
        if (_canvas == null || _context == null || !_context.World.IsAlive(entity)) return;

        Vector2 pos;
        if (_context.World.HasComponent<NetworkTransform>(entity))
        {
            ref readonly var nt = ref _context.World.GetComponentRO<NetworkTransform>(entity);
            pos = new Vector2(nt.LastPosition.X, nt.LastPosition.Y);
        }
        else if (_context.World.HasComponent<SimTransform>(entity))
        {
            ref readonly var st = ref _context.World.GetComponentRO<SimTransform>(entity);
            pos = new Vector2(st.Position.X, st.Position.Y);
        }
        else
        {
            return;
        }

        _canvas.Camera.Target = pos;
    }

    private void DeleteEntity(Entity entity)
    {
        if (_context == null || !_context.World.IsAlive(entity)) return;

        if (_context.World.HasComponent<NetworkIdentity>(entity))
        {
            ref readonly var netId = ref _context.World.GetComponentRO<NetworkIdentity>(entity);
            _context.World.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = netId.Value,
                Reason    = "cgf-deleted",
            });
        }

        if (_selectionState?.IsSelected(entity) == true)
        {
            _selectionState.PrimarySelected = null;
            _fdpInspectorState.SelectedEntity = null;
        }
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _cgfNetworkPolling = null;
        _toggleInput = null;
        _toggleSim = null;
        _context?.Kernel.Dispose();
        _physicsModule?.Dispose();
        _physicsModule = null;

        // Guard the participant disposal.
        if (_networkFactory?.Participant == null)
        {
            _context?.Participant?.Dispose();
        }

        _context = null;
    }

    // IEcsModule wrapper that routes TogglableSimulationGroup into the Simulation phase slot.
    // RegisterGlobalSystem rejects SystemPhase.Simulation; it must be registered via RegisterModule.
    private sealed class CgfSimulationModule : IEcsModule
    {
        private readonly TogglableSimulationGroup _group;
        public string Name => "CgfSimulation";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        public CgfSimulationModule(TogglableSimulationGroup group) => _group = group;
        public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_group);
        public void Tick(ISimulationView view, float deltaTime) { }
    }

    // No-op IEngineDebugTimeController for CGF (slave node; pause/step are not applicable).
    private sealed class CgfNoOpTimeController : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        private IDataBreakpointManager? _bpManager;
        // D-BP-01: return real pause state from the breakpoint manager instead of hardcoded false.
        public bool IsPausedByDebugger => _bpManager?.IsPaused ?? false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
        public void SetManager(IDataBreakpointManager manager) => _bpManager = manager;
    }
}


