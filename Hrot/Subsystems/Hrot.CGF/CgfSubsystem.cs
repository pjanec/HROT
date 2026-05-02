using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Vis2D.Tools;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Configuration;
using Hrot.CGF.Systems;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Common.Scenario;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.AI.Behaviors.Mappers;
using Hrot.Presentation.Windows;
using Hrot.Presentation.Facades;
using Hrot.Presentation.Renderers;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Menus;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Panels;
using Hrot.SimHost;
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
public sealed class CgfSubsystem : ISubsystem, Fdp.Toolkit.Runner.IMapCameraProvider, IWindowRegistrar
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

    // ── Scenario entity creation source (shared with load handlers in Phases 3-4) ──
    private ScenarioEntityCreationRequestSource? _scenarioSource;

    /// <summary>
    /// Exposes the scenario entity creation request source for load handlers (Phases 3-4).
    /// Available after <see cref="Initialize"/> has been called.
    /// </summary>
    internal ScenarioEntityCreationRequestSource? ScenarioEntityCreationSource => _scenarioSource;

    // ── Visualization ─────────────────────────────────────────────────────────
    private MapCanvas?                 _canvas;
    private DefaultSelectionState?     _selectionState;
    private CgfDebugVisualizerAdapter? _visualizerAdapter;
    private StandardInteractionTool?   _interactionTool;
    private EntityQuery?               _entityQuery;

    // ── FDP panels ────────────────────────────────────────────────────────────
    private FdpEntityInspectorPanel _fdpEntityInspector = new();
    private FdpEventBrowserPanel    _fdpEventBrowser    = new();
    private FdpRepositoryAdapter?   _fdpRepoAdapter;
    private FdpInspectorState       _fdpInspectorState  = new();
    private uint                    _fdpFrameCount;

    // ── Map context menu ──────────────────────────────────────────────────────
    private Entity _pendingContextMenuEntity;
    private bool   _openContextMenuThisFrame;

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
        // ── Create DDS participant in the Application Shell (Composition Root) ───
        // Rule: only the outermost executable may instantiate DdsParticipant.
        // HrotNodeBuilder no longer has a fallback.
        var shellParticipant = _networkFactory?.Participant;
        if (shellParticipant == null)
        {
            int cgfNodeId = config.NodeId != 0 ? config.NodeId : 400;
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
            NodeId              = config.NodeId != 0 ? config.NodeId : 400,
            // CgfSubsystem always creates a DDS participant — Headless here controls only
            // the Raylib/ImGui window (UI), not the network layer.
            // This mirrors SimHostApp which also hardcodes Headless = false for HrotNodeConfig.
            Headless            = false,
            ExternalParticipant = shellParticipant,
            SubsystemName       = "CGF",
        };
        _context = new HrotNodeBuilder(nodeConfig)
            .WithRole("CgfNode", NodeRole.Brain)
            .WithNetworkFactory(_networkFactory)
            .Build();

        _entityMap = _context.EntityMap;
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

        // ── Register CGF simulation logic (Brain-specific) ─────────────────────
        var mapperRegistry = new TacticalIntentMapperRegistry();
        mapperRegistry.Register(new DefendAreaMapper());
        var cgfLogicPack = new CgfLogicPack(behaviorRegistry, _entityMap, _scenarioSource,
            mapperRegistry);
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

        var storageProvider = new LocalDiskStorageProvider(OrchestrationConstants.DefaultStagingDirectory);

        // 1. Replay handler (must be first to gate Live-from-Replay branch)
        newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
            rrController, 
            inputGroup:            _toggleInput,
            simGroup:              _toggleSim, 
            postSimGroup:          null,
            lifecycleGroup:        null, 
            bypassLifecycleToggle: null, 
            storageDirectory:      OrchestrationConstants.DefaultStagingDirectory,
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
            storageDirectory: OrchestrationConstants.DefaultStagingDirectory));

        newClusterSlave.RegisterHandler(new Hrot.CGF.Orchestration.Handlers.CgfEpisodeLoadHandler(
            scenarioSerializer, scenarioLoader, extractor, _scenarioSource!, cgfIdAllocator, _context.World, behaviorRemapper));

        // 3. Fallback Live Load Handler (claims PrepareLive ONLY if scenario handlers didn't)
        newClusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(
            checkpointWorker: null, 
            controller: rrController, 
            storageDirectory: OrchestrationConstants.DefaultStagingDirectory));

        // 4. Utility handlers
        newClusterSlave.RegisterHandler(new ReferencePreviewHandler(_context.World));
        newClusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));
        newClusterSlave.RegisterHandler(new ReferenceArchiveHandler(
            OrchestrationConstants.DefaultStagingDirectory, _context.NodeId));

        _context = _context with
        {
            ClusterSlave = newClusterSlave
            // Note: SlaveTranslator is already correctly populated by HrotNodeBuilder earlier
        };



        // ── Initialize ─────────────────────────────────────────────────────────
        _context.Kernel.Initialize();
        // ── Visualization (non-headless only) ─────────────────────────────────────
        if (!_headless)
        {
            _entityQuery = _context.World.Query().With<NetworkIdentity>().Build();

            _canvas = new MapCanvas();
            _canvas.Camera.Offset = new Vector2(1280 / 2f, 720 / 2f);

            _selectionState    = new DefaultSelectionState();
            _visualizerAdapter = new CgfDebugVisualizerAdapter(
                new Fdp.Toolkit.Vis2D.Shapes.DefaultEntityShapeLibrary(),
                _behaviorRegistry);
            _fdpRepoAdapter    = new FdpRepositoryAdapter(_context.World);

            _fdpEventBrowser.RegisterBus("World", _context.World.Bus);
            _fdpEventBrowser.RegisterBus("Orchestration", _context.EventBus);

            var renderLayer = new EntityRenderLayer(
                "CGF Entities", -1, _context.World, _entityQuery, _visualizerAdapter, _selectionState)
                { Canvas = _canvas };
            _canvas.AddLayer(renderLayer);

            // Mission route layer — draws orange lines from entity to its mission waypoints.
            _canvas.AddLayer(new Hrot.ScenarioEditor.Rendering.MissionRenderLayer(
                _context.World, _context.GeoTransform!));

            _interactionTool = new StandardInteractionTool(_context.World, _entityQuery, _visualizerAdapter);

            _interactionTool.OnEntitySelectRequest += (entity, augment) =>
            {
                if (!_context.World.IsAlive(entity)) return;
                if (augment)
                    _selectionState.AddSelection(entity);
                else
                {
                    _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                }
            };

            _interactionTool.OnRegionSelected += entities =>
            {
                _selectionState.ClearSelection();
                foreach (var e in entities)
                    _selectionState.AddSelection(e);
            };

            _interactionTool.OnWorldClick += (pos, btn, shift, ctrl, hitEntity) =>
            {
                if (btn == MouseButton.Right && hitEntity != Entity.Null)
                {
                    _selectionState.PrimarySelected = hitEntity;
                    _fdpInspectorState.SelectedEntity = hitEntity;
                    _pendingContextMenuEntity = hitEntity;
                    _openContextMenuThisFrame = true;
                }
            };

            _canvas.SwitchTool(_interactionTool);

            // Route Delete key through the tool pipeline so ImGui keyboard capture
            // (e.g. editing a value in a component window) is always respected.
            _interactionTool.OnDeleteRequested += () =>
            {
                if (_selectionState == null) return;
                foreach (var entity in new List<Entity>(_selectionState.SelectedEntities))
                    DeleteEntity(entity);
            };

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
                        _canvas?.PushTool(new Hrot.ScenarioEditor.Tools.EntityRotationTool(entity, _context.World));
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

        // Use the no-args kernel update so the SlaveSyncController measures the real
        // wall-clock delta between frames.  The legacy Update(float) path would receive
        // dt=0 from the SubsystemOrchestrator in headless mode, zeroing out every
        // DeltaTime-dependent system (e.g. ThreatEvaluationSystem boost/decay).
        _context?.Kernel.Update();
        if (!_headless && _context != null)
        {
            _fdpFrameCount++;
            _fdpEventBrowser.Update(_fdpFrameCount);
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

        // Render the CGF Hover Label Tooltip
        if (!ImGui.GetIO().WantCaptureMouse && _canvas != null && _context?.World != null && _visualizerAdapter != null)
        {
            var mouseWorld = _canvas.Camera.ScreenToWorld(Raylib.GetMousePosition());
            var hovered    = _canvas.PickTopmostEntity(mouseWorld);

            if (hovered.HasValue && hovered.Value != Entity.Null)
            {
                var label = _visualizerAdapter.GetHoverLabel((ISimulationView)_context.World, hovered.Value);
                if (!string.IsNullOrEmpty(label))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(label);
                    ImGui.EndTooltip();
                }
            }
        }

        if (_openContextMenuThisFrame)
        {
            ImGui.OpenPopup("##cgf_map_ctx");
            _openContextMenuThisFrame = false;
        }

        if (ImGui.BeginPopup("##cgf_map_ctx"))
        {
            if (_pendingContextMenuEntity != Entity.Null
                && _context?.World != null
                && _context.World.IsAlive(_pendingContextMenuEntity))
            {
                var ent = _pendingContextMenuEntity;
                SharedContextMenuPopulator.PopulateEntityMenu(
                    entityId:            0L,
                    tkbType:             0L,
                    hasEditablePolyline: false,
                    hasRoutePlan:        false,
                    builder:             new ContextMenuBuilder(),
                    actions:             new MapContextActionController(
                        centerOnEntity: _ => CenterCameraOnEntity(ent),
                        deleteEntity:   _ => DeleteEntity(ent),
                        rotateTool:     _ => _canvas?.PushTool(
                            new Hrot.ScenarioEditor.Tools.EntityRotationTool(ent, _context.World))
                    ));
            }

            ImGui.EndPopup();
        }
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
            ? new CanvasMapPickAdapter(_canvas, _context.World)
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
            new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(),
            () => _context?.Kernel,
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
}


