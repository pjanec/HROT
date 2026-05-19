using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fbt;
using Fbt.HotReload;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Examples.Scenarios.Integrated;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Translators;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Hrot.CGF;
using Hrot.CGF.Orchestration;
using Hrot.CGF.Systems;
using Hrot.Editor.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Presentation.Windows;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Common.Diagnostics;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;
using Hrot.Common.Systems;
using Hrot.Common.Scenario;
using Hrot.Editor;
using Hrot.Editor.Adapters;
using Hrot.Editor.Events;
using Hrot.Editor.Modules;
using Hrot.Editor.Rendering;
using Hrot.Editor.UI;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.IG.Modules;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Rendering;
using Hrot.ScenarioEditor.Services;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.Core.Diagnostics;
using Fdp.ModuleHost.Diagnostics;
using Fdp.ModuleHost.Scheduling;
// Disambiguate IMapCameraProvider: Hrot.SimHost.Modules also defines this interface.
using IMapCameraProvider = Fdp.Toolkit.Runner.IMapCameraProvider;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpRepositoryAdapter = Fdp.Presentation.Adapters.RepositoryAdapter;
using FdpInspectorState = Fdp.Presentation.Abstractions.InspectorState;
// (Phase 5: EditorInteractionTool alias removed with StandardInteractionTool)
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Interfaces;
using Fdp.Toolkit.Spatial;
using CarKinem.Tkb;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Combat.Translators;
using Hrot.Editor.Commands;
using Hrot.Common.Events;

namespace Hrot.Editor
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the standalone HROT Editor.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> ? builds the offline ECS composition root
    ///   (entities, kernel, logic packs, adapters, UI panels) without DDS.</item>
    ///   <item><see cref="Update"/> ? steps the time controller and ticks the kernel.</item>
    ///   <item><see cref="DrawWorld"/> ? renders the 2-D map canvas (skipped in headless).</item>
    ///   <item><see cref="DrawUI"/> ? renders ImGui panels not registered as managed windows
    ///   (skipped in headless).</item>
    ///   <item><see cref="RegisterWindows"/> ? registers editor panels with the Window Manager
    ///   so they participate in the shared docking layout.</item>
    ///   <item><see cref="Shutdown"/> ? disposes the kernel and ECS world.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class EditorSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar, Hrot.Common.Diagnostics.Gizmos.IGizmoControllable
    {
        private const int EditorNodeId = 0;

        // ?? Subsystem identity ????????????????????????????????????????????????

        /// <inheritdoc/>
        public string Name => "Editor";

        /// <inheritdoc/>
        /// <remarks>Slate blue ? distinct from IG (green), SimHost (red) and ExCon (violet).</remarks>
        public Vector4 TitleBarColor => new(0.15f, 0.22f, 0.48f, 1f);

        // ?? Network factory (no-op stubs for offline editor) ?????????????????

        private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();

        // ?? Core state ????????????????????????????????????????????????????????

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private MasterSyncController?   _timeController;
        private PhysicsToolkitModule?   _physicsModule;
        private IEditorLogic?           _editorLogic;
        private MapCanvas?              _canvas;
        private MapCamera?              _camera;
        private bool                    _headless;
        // GZH-016: gate � false when another subsystem owns the map view.
        private Func<bool>              _isActiveMapOwner = () => true;

        // ?? Adapters (canvas-dependent; null in headless) ?????????????????????

        private EditorSpawnAdapter?             _spawnAdapter;
        private EditorMissionService?           _missionService;
        private EditorOrbatAdapter?             _orbatAdapter;
        private EditorMapConfigAdapter?         _mapConfigAdapter;
        private EditorMapPickAdapter?           _mapPickAdapter;
        private EditorZoneAdapter?              _zoneAdapter;
        private JsonEntityContextMenuHandler? _contextMenuHandler;
        private EditorPreviewController?        _previewController;
        private MapViewConfig?                  _mapViewConfig;

        // ?? UI panels (legacy, always created) ????????????????????????????????

        private ScenarioBrowserPanel? _browserPanel;
        private EditorToolbarPanel?   _toolbarPanel;
        private EditorOrbatPanel?     _orbatPanel;

        // ?? Shared UI panels (skipped in headless) ????????????????????????????

        private SpawnerPanel?    _spawnerPanel;
        private MissionPanel?    _missionPanel;
        private ConfigPanel?     _configPanel;
        private SharedOrbatPanel? _sharedOrbatPanel;
        private PreviewPanel?    _previewPanel;
        private ZoneEditorPanel? _zoneEditorPanel;

        // ?? FDP framework panels ??????????????????????????????????????????????

        private FdpEntityInspectorPanel _fdpEntityInspector = new();
        private FdpEventBrowserPanel                 _fdpEventBrowser    = null!;
        private DiagnosticEventHistoryService        _fdpEventHistory    = new();
        private FdpRepositoryAdapter?   _fdpRepoAdapter;
        private FdpInspectorState       _fdpInspectorState  = new();
        private uint                    _fdpFrameCount;
        private Hrot.SimHost.Modules.CognitiveSpatialModule? _perceptionMod;

        // ?? Offline orchestrator (single-node scenario listing) ???????????????????

        private FdpEventBus?           _orchestrationBus;
        private ClusterMaster?                _clusterMaster;
        private ReplaySeekProcessManager?     _seekProcessManager;
        private ReplayProcessManager?         _replayProcessManager;
        private AssetInventoryProcessManager?  _assetInventoryProcessManager;
        private AssetPrefetchProcessManager?   _assetPrefetchProcessManager;
        private StorageGatewayModule?          _storageGateway;
        private ClusterUiCache?                _uiCache;
        private ClusterScenarioPanel?          _clusterPanel;
        private ClusterDiagnosticsPanel?       _clusterDiagnosticsPanel;
        private IFileDialogService?            _fileDialogService;
        private DiagnosticsDumpProcessManager? _diagnosticsDumpProcessManager;
        private DiagnosticLogMergeWorker?      _logMergeWorker;

        // ?? Selection state ???????????????????????????????????????????????????????

        private DefaultSelectionState? _selectionState;
        private Hrot.ScenarioEditor.Gizmos.RubberBandState? _rubberBandState;
        private Hrot.ScenarioEditor.Systems.SelectionInteractionSystem? _selectionSystem;
        // ?? Behavior registry (promoted for tooltip rendering) ?????????????????

        private BehaviorRegistry? _behaviorRegistry;

        // ?? AI behavior hot-reload coordinator ?????????????????????????????????

        private AiHotReloadCoordinator?    _aiCoordinator;
        private HotReloadMessageLogSource? _hotReloadSource;
        // Captured at Initialize() so the coordinator can pass them to the behavior factory.
        private IGeographicTransform? _geoTransform;
        private NetworkEntityMap?     _entityMap;
        // ?? Production visualizer dependencies ???????????????????????????????????

        private readonly MapUserConfig     _userConfig     = new();
        private readonly MapCameraViewport _cameraViewport = new();
        private DebugPrimitiveBuffer? _gizmoBuffer;
        private DataDrivenGizmoSystem? _editorDataDrivenGizmoSystem;
        private GlobalGizmoManager?  _globalGizmoManager;
        private FdpEventBus?         _interactionBus;
        private GizmoExecutionController? _gizmoController;
        // DEBT-002: hub broadcasts DTO state to all connected terminals.
        private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub();
        // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
        // GZH-014: public to satisfy IGizmoControllable.
        public GizmoExecutionController GizmoController => _gizmoController!;
        // DEBT-002: exposed for future module installation (BATCH-04).
        internal Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub GizmoUiHub => _gizmoUiHub;

        // ?? Tool handling ?????????????????????????????????????????????????????????

        // (Phase 5: _interactionTool removed; entity interaction via ECS gizmos)

        // ?? Context menu (ImGui popup trigger) ????????????????????????????????????

        private DebugGizmoLayer? _gizmoLayer;

        // ?? Rename dialog state ???????????????????????????????????????????????????

        private long   _renameTargetNetworkId;
        private bool   _openRenameModalThisFrame;
        private string _renameBuffer = string.Empty;

        // ?? Private helpers ???????????????????????????????????????????????????

        /// <summary>
        /// Lightweight IPreviewController that wraps <see cref="PreviewClusterOpHandler"/>
        /// and tracks preview state internally without requiring <c>IScenarioStateProvider</c>.
        /// </summary>
        private sealed class EditorPreviewController : IPreviewController
        {
            private readonly PreviewClusterOpHandler _handler;
            private readonly MasterSyncController    _timeController;
            private bool _inPreview;

            internal EditorPreviewController(EntityRepository world, MasterSyncController timeController)
            {
                _handler        = new PreviewClusterOpHandler(world);
                _timeController = timeController;
            }

            public bool IsInPreviewMode => _inPreview;

            public void EnterPreviewMode(bool startPaused = false)
            {
                _handler.TriggerLoadingPreview();
                if (!startPaused)
                    _timeController.SwitchToContinuous();
                _inPreview = true;
            }

            public void ExitPreviewMode()
            {
                _handler.TriggerUnloadingPreview();
                _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());
                _inPreview = false;
            }
        }

        // ?? Nested helper: offline sequential ID allocator ????????????????????

        private sealed class SequentialIdAllocator : INetworkIdAllocator
        {
            private long _next = 1000;
            public long AllocateId()            => _next++;
            public void Reset(long startId = 0) => _next = startId;
            public void Dispose() { }
        }

        // ?? Internal test accessors ???????????????????????????????????????????

        /// <summary>Internal test hook: direct access to the ECS world.</summary>
        internal EntityRepository World =>
            _world ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the kernel.</summary>
        internal ModuleHostKernel Kernel =>
            _kernel ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the editor logic facade.</summary>
        internal IEditorLogic EditorLogic =>
            _editorLogic ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the time controller.</summary>
        internal MasterSyncController TimeController =>
            _timeController ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <summary>Internal test hook: direct access to the preview controller.</summary>
        internal IPreviewController PreviewController =>
            _previewController ?? throw new InvalidOperationException("EditorSubsystem is not initialized.");

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _camera?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _camera?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _camera;

        // ?? ISubsystem lifecycle ??????????????????????????????????????????????

        // ctor for unit tests
        public EditorSubsystem()
        {
        }


        // ctor for ClusterRunner
        public EditorSubsystem( INetworkFactory _ )
        {
            // we do not use the injected network factory in the offline editor,
            // but we accept it in the constructor to satisfy the dependency graph and allow for future online features.
        }

        /// <summary>
        /// Relative path segments to the AI Behaviors .csproj used by <see cref="IEditorLogic.RebuildAndReloadAI"/>.
        /// Set by the composition root (e.g. Program.cs) before <see cref="Initialize"/> is called.
        /// Defaults to the standard workspace layout.
        /// </summary>
        public string[] AiBehaviorsProjectPath { get; set; } =
            new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };


        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;
            // GZH-016: store active-map-owner predicate injected by SubsystemOrchestrator.
            _isActiveMapOwner = config.IsActiveMapOwner;

            // ?? 1. ECS world ?????????????????????????????????????????????????
            _world = new EntityRepository();
            _orchestrationBus = new FdpEventBus(); // Control Plane bus (cluster management)
            Fdp.Toolkit.Orchestration.OrchestrationEventRegistry.RegisterAll(_orchestrationBus);
            Hrot.Orchestrator.OrchestratorEventRegistry.RegisterInternalEvents(_orchestrationBus);
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(_world);

            // ?? 1b. Register all components BEFORE building serializers ???????
            // FdpAutoSerializer compiles property-extraction delegates at Build() time
            // against the current ComponentTypeRegistry, so all types must be registered
            // first ? otherwise the serializer schema is empty and Save/Load is a no-op.
            SimHostComponentRegistry.RegisterAll(_world);
            CgfComponentRegistry.RegisterAll(_world);
            _world.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
            // MapDisplayComponent is used by MapLayerAssignmentSystem to tag entities
            // with the layer bitmask used by the DebugGizmoLayer for visibility culling.
            _world.RegisterComponent<MapDisplayComponent>();
            // IG presentation components required by MapCullingModule / StyleResolutionModule.
            _world.RegisterComponent<Hrot.IG.Components.CullingState>();
            _world.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
            _world.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
            // Visual effect components required by EventEffectModule (EventToEffectSystem).
            _world.RegisterComponent<VisualEffectState>();
            _world.RegisterComponent<TracerTarget>();
            _world.RegisterEvent<ActivateEditorToolEvent>();
            _world.RegisterEvent<CenterOnEntityCommand>();

            // ?? 2. Time controller (MasterSyncController in Deterministic/frozen mode) ??
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            _timeController = (MasterSyncController)TimeControllerFactory.Create(_world.Bus, timeConfig);
            _kernel.SetTimeController(_timeController);
            // Start in Deterministic mode so authoring starts paused (dt == 0 every frame).
            _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());

            // ?? 3. Shared services ????????????????????????????????????????????
            var geoTransform     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = geoTransform;
            var entityMap        = new NetworkEntityMap();
            _entityMap = entityMap;
            _world.SetSingletonManaged<NetworkEntityMap>(entityMap);
            var behaviorRegistry = new BehaviorRegistry();
            _behaviorRegistry = behaviorRegistry;
            // Register Urban Combat behaviors so MissionAdapterSystem can resolve Ambush
            // and InfantryCombat behavior trees when loading UrbanCombatNew scenario files.
            // CGF behaviors (MoveToLocation, FollowRoute, ...) are loaded asynchronously
            // from Hrot.AI.Behaviors.dll via TriggerInitialLoad() below.
            UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(behaviorRegistry);

            // Expose the registry to the diagnostic renderers so the entity inspector
            // can project BrainBlackboard memory and visualize the BTree execution state.
            Hrot.Presentation.Renderers.BrainBlackboardRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.Blackboard1024Renderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;

            // ?? Hot reload: watch the deployment directory for Hrot.AI.Behaviors.dll changes ??
            // When the user clicks "Reload BTrees" and MSBuild overwrites the DLL, the watcher
            // detects the change, loads the new assembly into a fresh collectible ALC on a
            // background thread, and enqueues an interpreter swap for the main thread to apply.
            // Watch specifically for Hrot.AI.Behaviors.dll so the watcher does not fire
            // on unrelated DLL writes during compilation.
            string aiAssemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            _aiCoordinator = new AiHotReloadCoordinator(
                aiAssemblyDir, "Hrot.AI.Behaviors.dll",
                _world!, _behaviorRegistry!,
                _geoTransform, _entityMap);

            _aiCoordinator.OnReloadCompleted += _ =>
                Console.WriteLine("[HotReload] AI Behaviors hot-swapped.");

            // Load the current DLL immediately so behaviors are ready before the first frame.
            _aiCoordinator.TriggerInitialLoad();

            // ?? Hot-reload message log source ?????????????????????????????????
            // Wire up after the coordinator is configured so that both the
            // behavior-swap callbacks and the log-source callbacks are registered.
            _hotReloadSource = new HotReloadMessageLogSource();
            _aiCoordinator.OnReloadCompleted += _hotReloadSource.OnReloadCompleted;
            _aiCoordinator.OnReloadFailed    += _hotReloadSource.OnReloadFailed;

            var clusterSlave     = new ClusterSlave(EditorNodeId, "Editor", _orchestrationBus);
            var zoneService      = new ZoneManagerService();

            // Build the serializer with custom translators AFTER component registration
            // so FdpAutoSerializer compiles extraction delegates for all registered types.
            var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);

            // Wire the unified serialization path so the entity inspector Copy JSON
            // buttons produce readable DTO output for BrainBlackboard and Blackboard1024.
            _fdpEntityInspector.Serializer = scenarioSerializer;
            _fdpEntityInspector.ExtractionService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap, scenarioSerializer);

            // Inject bus and zoneService so file ops trigger WorldResetEvent and persist zone data.
            var fileService = new ScenarioFileService(scenarioSerializer, _world.Bus, zoneService);

            // ?? 3b. TKB + ELM + offline spawning ?????????????????????????????
            var tkbDb       = HrotEnvironment.CreateTkb();
            // Register Urban Combat entity blueprints (TKB types 1001?2003) so the
            // ScenarioSerializer can resolve MilitaryApc, InfantrySoldier, and Insurgent.
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkbDb);
            var translators = new List<ITkbEntityTranslator>
            {
                new SpatialCoreTkbTranslator(),
                new VehicleKinematicsTkbTranslator(),
                new BehaviorTkbTranslator(),
                new CombatTkbTranslator(),
                new PerceptionTkbTranslator()
            }.AsReadOnly();
            var elm               = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
            elm.SetTranslators(translators);
            var idAllocator       = new SequentialIdAllocator();
            var spawnSys          = new NetworkSpawningSystem(tkbDb, elm, entityMap, idAllocator, localNodeId: EditorNodeId, translators: translators);
            var scenarioLoadSource = new ScenarioEntityCreationRequestSource();
            var extractor          = new StagingEntityExtractor();
            string isolatedTempRoot = Fdp.Toolkit.Orchestration.OrchestrationConstants.GetNodeStagingRoot(EditorNodeId);

            // ?? 3c. Offline scenario load handler ?????????????????????????????
            var storageProvider = new LocalDiskStorageProvider(isolatedTempRoot);
            var scenarioLoader  = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(storageProvider));

            // FIX: EcsRecordReplayController now handles NetworkEntityMap resync internally for all subsystems.
            // Pass null (no downstream callbacks for offline Editor); the controller will rebuild the map.
            var rrController    = new Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController(
                _kernel, EditorNodeId, _world!);
            clusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, extractor, scenarioLoadSource, idAllocator, _world));
            clusterSlave.RegisterHandler(new Hrot.SimHost.Orchestration.Handlers.HrotScenarioLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, extractor, scenarioLoadSource, idAllocator, _world,
                controller: rrController,
                storageDirectory: isolatedTempRoot));
            clusterSlave.RegisterHandler(new DiagnosticsDumpClusterOpHandler(
                _fdpEventHistory,
                new ArchitectureDiagnosticsService(() => _kernel),
                new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap, scenarioSerializer),
                new Hrot.Core.Diagnostics.LogArchiveExtractionService(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                    "Editor",
                    0),
                new Hrot.Common.Infrastructure.HrotNodeConfig
                {
                    NodeId = EditorNodeId,
                    SubsystemName = "Editor",
                    LocalTempRoot = isolatedTempRoot,
                    LogDirectory = System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs"),
                }));

            // ?? 4. Module registration (offline ? no translator packs) ????????
            var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
            var perceptionMod    = new CognitiveSpatialModule(
                _world,
                colliderRadiusReader: (view, e) => view.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e)
                    ? view.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).Radius
                    : 0f);
            _perceptionMod = perceptionMod;
            var mapperRegistry = new TacticalIntentMapperRegistry();
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.DefendAreaMapper());
            mapperRegistry.Register(new Hrot.AI.Behaviors.Mappers.HullDownAttackMapper());
            var cgfLogicPackInst = new CgfLogicPack(behaviorRegistry, entityMap,
                scenarioLoadSource,
                mapperRegistry);

            var toggleInput = new TogglableInputGroup(
                "EditorInput",
                cgfLogicPackInst.InputSystems.Concat(simHostCorePack.InputSystems).ToArray());
    
            var toggleSim = new TogglableSimulationGroup(
                "EditorSim",
                cgfLogicPackInst.SimulationSystems.Concat(simHostCorePack.SimulationSystems).ToArray());

            var togglePostSim = new TogglablePostSimulationGroup(
                "EditorPostSim",
                simHostCorePack.PostSimulationSystems.ToArray());
            var orchPack         = new OrchestrationLogicPack(clusterSlave);
            var scenarioMod      = new ScenarioEditorModule(fileService);

            _kernel.RegisterModule(new BehaviorDiagnosticsModule());
            _kernel.RegisterModule(perceptionMod);
            _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.AreaQueryResultMaterializationSystem());
            _kernel.RegisterModule(orchPack);
            _kernel.RegisterModule(scenarioMod);

            // Register the event history service and its capture system.
            _fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);
            _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("World", _fdpEventHistory, _world.Bus));
            if (_orchestrationBus != null)
                _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, _orchestrationBus));

            // ?? 4a. Multi-phase system registration for SimHostCorePack and CgfLogicPack ??
            _kernel.RegisterGlobalSystem(toggleInput);
            _kernel.RegisterGlobalSystem(togglePostSim);
            // Simulation-phase systems must go through a module (kernel forbids global registration).
            _kernel.RegisterModule(new EditorSimulationModule(toggleSim));

            // Register replay handler before live handler so the replay branch can claim
            // PrepareLive while replay is active.
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(
                rrController,
                inputGroup:            toggleInput,
                simGroup:              toggleSim,
                postSimGroup:          togglePostSim,
                lifecycleGroup:        null,
                bypassLifecycleToggle: null,
                storageDirectory:      isolatedTempRoot,
                suspendGlobalTimePush: _kernel.SuspendGlobalTimePush,
                resumeGlobalTimePush:  _kernel.ResumeGlobalTimePush));
            clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                checkpointWorker: null,
                controller: rrController,
                storageDirectory: isolatedTempRoot));

            // NOTE: SimHostComponentRegistry.RegisterAll was moved to step 1b above.
            _kernel.RegisterModule(new EditorSystemsModule());

            // ?? 4c. ELM + offline spawning module + scenario genesis pipeline ??????????????????
            // CreateEntityRequestSystem drains scenarioLoadSource each Input tick and emits
            // SpawnEntityCommand events for NetworkSpawningSystem (BeforeSync tick), which
            // sets AuthorityMask = ComponentMask for locally owned entities.
            var requestSystem = new CreateEntityRequestSystem(
                requestSource:      scenarioLoadSource,
                ackSink:            new NullEntityAckSink(),
                tkbDb:              tkbDb,
                idAllocator:        idAllocator,
                localNodeId:        EditorNodeId,
                isDefaultProcessor: true);
            _kernel.RegisterModule(elm);
            _kernel.RegisterModule(new SimHostModule(spawnSys));
            _kernel.RegisterGlobalSystem(requestSystem);
            _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(entityMap));

            // ?? 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ??
            var logicPacks = new List<IEcsModule> { simHostCorePack, perceptionMod, cgfLogicPackInst };

            // ?? 4d. MapLayerAssignmentSystem ? must be registered BEFORE Initialize() ??
            // Stamps MapDisplayComponent.LayerMask on each entity so the DebugGizmoLayer
            // can cull entities whose layer is toggled off in the editor's config panel.
            _kernel.RegisterGlobalSystem(new MapLayerAssignmentSystem());

            // ?? 4e. IG presentation modules ? compute CullingState and ResolvedStyle ??
            // Must be registered BEFORE Initialize() so their component queries are built.
            _kernel.RegisterModule(new MapCullingModule(_cameraViewport));
            _kernel.RegisterModule(new StyleResolutionModule(_userConfig, localNodeId: EditorNodeId));

            // ?? 4f. Visual effects module ? spawns and cleans up tracers / explosions ??
            _kernel.RegisterModule(new EventEffectModule());

            // ?? 4g. Gizmo subsystem ? local stateless gizmo rendering ?????????????????
            // The Editor has no DDS transport; primitives are produced locally and consumed
            // by a DebugGizmoLayer on the canvas.
            _gizmoBuffer = new DebugPrimitiveBuffer();
            var editorGizmoRegistry = new GizmoRegistry();
            var editorStatelessGizmoRegistry = new StatelessGizmoRegistry();
            var editorGizmoSettings = new GizmoSettingsRegistry();
            // Auto-register all [GizmoProjector]-decorated gizmos in Hrot.ScenarioEditor.Gizmos
            // (IgEntityPresentationGizmo, RouteGizmo, MapOverlayGizmo, EffectPresentationGizmo, ...).
            Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register gizmos from Hrot.Common.Diagnostics (SelectionHighlightGizmo, HealthBarGizmo, ...).
            Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register gizmos from Hrot.IG.Gizmos (EffectPresentationGizmo, ...).
            Hrot.IG.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // Register CanvasContextMenuGizmo so empty-space right-click resolves through the binding pipeline.
            Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(
                editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);
            // behavior gizmos
            Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(editorGizmoRegistry, editorStatelessGizmoRegistry, editorGizmoSettings);

            // MissionPresentationGizmo requires IGeographicTransform ? register manually.
            editorStatelessGizmoRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.MissionPresentationGizmo(geoTransform),
                new[] { typeof(SimTransform), typeof(SelectionState) });
            // EntityEditorLabelGizmo requires BehaviorRegistry ? register manually.
            editorStatelessGizmoRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.EntityEditorLabelGizmo(_behaviorRegistry!),
                new[] { typeof(SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
            // EntityDragGizmoDefinition has an optional callback constructor ? register manually.
            editorGizmoRegistry.Register(new Hrot.ScenarioEditor.Gizmos.EntityDragGizmoDefinition());
            // Editor has no DDS transport so no network ingress/egress translators.
            var interactionBus = new FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(interactionBus);
            _interactionBus = interactionBus;
            _editorDataDrivenGizmoSystem = new DataDrivenGizmoSystem(
                editorGizmoRegistry,
                _gizmoBuffer,
                isSelectedPredicate: static (view, entity) =>
                    view.HasComponent<SelectionState>(entity) &&
                    view.GetComponentRO<SelectionState>(entity).IsSelected,
                interactionBus: interactionBus);
            _globalGizmoManager = new GlobalGizmoManager(_gizmoBuffer, interactionBus);
            var actionRegistry = new GlobalActionRegistry();
            long layerControlId = GlobalGizmoManager.NewId();
            var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(layerControlId, interactionBus, new StructEdit.Reflection.ComponentEditServiceBuilder().Build(), _gizmoUiHub);
            _globalGizmoManager.Register(layerControlId, layerControlGizmo);
            actionRegistry.Register(GlobalActionIds.OpenLayerControl, (_, _) =>
            {
                interactionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
            });
            actionRegistry.Register(GlobalActionIds.Rotate, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (!view.HasComponent<SimTransform>(target)) return;
                _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                    view, target, onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
            });
            actionRegistry.Register(GlobalActionIds.Measure, (_, _) =>
            {
                _world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Measure));
            });
            actionRegistry.Register(GlobalActionIds.PlaceEntity, (_, _) =>
            {
                _world.Bus.Publish(new ActivateEditorToolEvent(EditorTool.Spawn));
            });
            actionRegistry.Register(GlobalActionIds.EditOverlay, (view, target) =>
            {
                if (target == Entity.Null || !view.HasManagedComponent<EditablePolyline>(target)) return;

                if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(target))
                {
                    _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                }
                else
                {
                    long netId = view.HasComponent<NetworkIdentity>(target)
                        ? view.GetComponentRO<NetworkIdentity>(target).Value
                        : 0L;
                    var gizmo = new Hrot.ScenarioEditor.Gizmos.VertexEditGizmo(
                        _world!, target, netId,
                        onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                    _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
                }
            });
            actionRegistry.Register(GlobalActionIds.EditRoute, (view, target) =>
            {
                if (target == Entity.Null || !view.HasManagedComponent<RoutePlan>(target)) return;

                if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(target))
                {
                    _editorDataDrivenGizmoSystem!.DeactivateGizmo(target);
                }
                else
                {
                    long netId = view.HasComponent<NetworkIdentity>(target)
                        ? view.GetComponentRO<NetworkIdentity>(target).Value
                        : 0L;
                    var gizmo = new Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo(
                        _world!, target, netId,
                        onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(target));
                    _editorDataDrivenGizmoSystem!.ActivateGizmo(target, gizmo);
                }
            });
            actionRegistry.Register(GlobalActionIds.CenterOnEntity, (view, target) =>
            {
                if (target == Entity.Null) return;
                long netId = view.HasComponent<NetworkIdentity>(target)
                    ? view.GetComponentRO<NetworkIdentity>(target).Value
                    : 0L;
                if (netId != 0)
                    _world!.Bus.Publish(new Hrot.Editor.Commands.CenterOnEntityCommand { NetworkId = netId });
            });
            actionRegistry.Register(GlobalActionIds.Delete, (view, target) =>
            {
                if (target == Entity.Null) return;
                long netId = view.HasComponent<NetworkIdentity>(target)
                    ? view.GetComponentRO<NetworkIdentity>(target).Value
                    : 0L;
                if (netId != 0)
                    _world!.Bus.PublishManaged(new DestroyEntityCommand { NetworkId = netId, Reason = "ContextMenu" });
                else
                    _world!.DestroyEntity(target);
            });
            actionRegistry.Register(GlobalActionIds.Select, (_, target) =>
            {
                if (target == Entity.Null) return;

                var q = _world!.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
                foreach (var e in q)
                {
                    if (_world.IsAlive(e))
                        _world.SetComponent(e, new SelectionState { IsSelected = false, IsPrimarySelection = false });
                }

                _world.SetComponent(target, new SelectionState { IsSelected = true, IsPrimarySelection = true });
                if (_selectionState != null) _selectionState.PrimarySelected = target;
                _fdpInspectorState.SelectedEntity = target;
            });
            actionRegistry.Register(GlobalActionIds.ToggleAiTrace, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (view is not EntityRepository repo) return;
                if (!repo.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(target)) return;

                const Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags flag = Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EnableTraceBuffer;
                bool current = repo.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target)
                    && (repo.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target).Behavior & flag) != 0;
                bool next = !current;
                string nextStr = next ? "true" : "false";
                string patchJson = $$"""
                {
                    "{{nameof(Fdp.Toolkit.Behavior.Diagnostics.DebugState.Behavior)}}": {
                        "{{flag}}": {{nextStr}}
                    }
                }
                """;

                repo.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand
                {
                    Target = target,
                    PatchJson = patchJson,
                });
            });
            actionRegistry.Register(GlobalActionIds.ToggleAiTraceLog, (view, target) =>
            {
                if (target == Entity.Null) return;
                if (view is not EntityRepository repo) return;
                if (!repo.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(target)) return;

                const Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags flag = Fdp.Toolkit.Behavior.Diagnostics.BehaviorDebugFlags.EmitToLog;
                bool current = repo.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target)
                    && (repo.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.DebugState>(target).Behavior & flag) != 0;
                bool next = !current;
                string nextStr = next ? "true" : "false";
                string patchJson = $$"""
                {
                    "{{nameof(Fdp.Toolkit.Behavior.Diagnostics.DebugState.Behavior)}}": {
                        "{{flag}}": {{nextStr}}
                    }
                }
                """;

                repo.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Diagnostics.PatchDebugStateCommand
                {
                    Target = target,
                    PatchJson = patchJson,
                });
            });

            var contextIngress = new ContextActionIngressSystem(entityMap, interactionBus);
            _rubberBandState = new Hrot.ScenarioEditor.Gizmos.RubberBandState();
            editorStatelessGizmoRegistry.RegisterGlobal(new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(_rubberBandState));
            _selectionSystem = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(_world, interactionBus, _rubberBandState);
            _selectionSystem.OnSelectionChanged += (entity, _) =>
            {
                if (entity == Entity.Null)
                {
                    if (_selectionState != null) _selectionState.PrimarySelected = null;
                    _fdpInspectorState.SelectedEntity = null;
                }
                else if (_world.IsAlive(entity))
                {
                    if (_selectionState != null) _selectionState.PrimarySelected = entity;
                    _fdpInspectorState.SelectedEntity = entity;
                }
            };
            var gizmoGroup = new TogglablePostSimulationGroup("GizmoExecution",
                _editorDataDrivenGizmoSystem,
                _globalGizmoManager,
                new StatelessGizmoSystem(editorStatelessGizmoRegistry, _gizmoBuffer));
            // GZH-003: Editor is interactive, always has a window at startup.
            gizmoGroup.Enabled = true;
            _gizmoController = new GizmoExecutionController(gizmoGroup, _globalGizmoManager, _editorDataDrivenGizmoSystem);
            _kernel.RegisterModule(new GizmoInteractionModule(
                interactionBus,
                contextIngress: contextIngress,
                interactionSystems: new IEcsModuleSystem[]
                {
                    new GlobalActionDispatchSystem(actionRegistry, interactionBus),
                    gizmoGroup,
                },
                gizmoIngress: null,
                gizmoEgress:  null));
            _kernel.RegisterGlobalSystem(new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, interactionBus));
            // Register canvas menu update so CanvasContextMenuGizmo has state to project.
            _kernel.RegisterGlobalSystem(new Hrot.Presentation.Systems.CanvasMenuUpdateSystem());

            // ?? 5. Kernel initialization ??????????????????????????????????????
            _kernel.Initialize();

            // ?? 6. Editor application (IEditorLogic facade) ??????????????????
            var app = new EditorApplication(
                fileService, _world.Bus, _orchestrationBus!, _world, _kernel, logicPacks,
                hotReloadSource: _hotReloadSource,
                aiProjectPathSegments: AiBehaviorsProjectPath);
            _editorLogic = app;

            // ?? 6b. Offline orchestrator ? scenario listing via ClusterMaster + UICache ??
            var offlineConfig = new ClusterConfiguration { Mandatory = Array.Empty<string>() };
            _clusterMaster  = new ClusterMaster(_orchestrationBus!, offlineConfig);

            // Register the seek aggregator and process manager so the clock snaps on seek
            _seekProcessManager = new ReplaySeekProcessManager(_orchestrationBus!, _timeController);
            _clusterMaster.RegisterAggregator(new ReplaySeekAggregator());

            // Register replay manager and aggregator so duration payload flows through 2PC
            _replayProcessManager = new ReplayProcessManager(_orchestrationBus!, _timeController);
            _clusterMaster.RegisterAggregator(_replayProcessManager.CreateAggregator());

            _storageGateway = new StorageGatewayModule();
            _assetInventoryProcessManager = new AssetInventoryProcessManager(
                _orchestrationBus!,
                _storageGateway,
                ClusterConfiguration.Default.NasBasePath,
                OrchestrationConstants.DefaultStagingDirectory,
                EditorNodeId);
            _assetPrefetchProcessManager = new AssetPrefetchProcessManager(
                _orchestrationBus!,
                _storageGateway,
                ClusterConfiguration.Default.NasBasePath,
                OrchestrationConstants.DefaultStagingDirectory);
            _uiCache = new ClusterUiCache(_orchestrationBus!, _timeController);
            _clusterPanel = new ClusterScenarioPanel(_orchestrationBus!, _uiCache);
            _fileDialogService = new WinFormsFileDialogService();
            _clusterDiagnosticsPanel = new ClusterDiagnosticsPanel(
                _uiCache,
                _orchestrationBus!,
                _fileDialogService,
                EditorBootstrap.ScenariosRoot);
            var diagnosticsAggregator = new DiagnosticsConsensusAggregator();
            _clusterMaster.RegisterAggregator(diagnosticsAggregator);
            _diagnosticsDumpProcessManager = new DiagnosticsDumpProcessManager(
                _orchestrationBus!,
                _storageGateway,
                EditorBootstrap.ScenariosRoot,
                diagnosticsAggregator);
            _logMergeWorker = new DiagnosticLogMergeWorker(_orchestrationBus!);
            app.SetAvailableScenariosSource(() => _uiCache?.AvailableScenarios ?? Array.Empty<string>());

            // ?? 7. Map canvas + camera (skipped in headless) ??????????????????
            if (!_headless)
            {
                _camera = new MapCamera();
                _canvas = new MapCanvas(new RaylibInputProvider());
                _canvas.Camera = _camera;
            }

            // ?? 8. Preview controller (works headless too ? no canvas dep) ????
            _previewController = new EditorPreviewController(_world, _timeController!);

            // ?? 9. Mission service (no canvas dependency) ?????????????????????
            _missionService = new EditorMissionService(_world.Bus, _world, behaviorRegistry);

            // ?? 10. Canvas-dependent adapters, layers, and interaction tool ???
            if (!_headless)
            {
                _mapViewConfig    = new MapViewConfig();
                _mapPickAdapter   = new EditorMapPickAdapter(_canvas!, geoTransform, _world, _globalGizmoManager!);

                // Build the JSON?ECS attribute compiler with the geo-transform so that
                // geodetic spawn coordinates are projected correctly on entity placement.
                var jsonCompiler  = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform);
                _spawnAdapter     = new EditorSpawnAdapter(_world.Bus, jsonCompiler, tkbDb, scenarioLoadSource, _globalGizmoManager!);
                _zoneAdapter      = new EditorZoneAdapter(_canvas!, _world.Bus, _globalGizmoManager!);
                _mapConfigAdapter = new EditorMapConfigAdapter(_mapViewConfig, _canvas!);
                _selectionState   = new DefaultSelectionState();
                _orbatAdapter     = new EditorOrbatAdapter(_world, _world.Bus, _editorLogic, _spawnAdapter);
                _contextMenuHandler = new JsonEntityContextMenuHandler(_world, interactionBus);
                _fdpRepoAdapter = new FdpRepositoryAdapter(_world);

                // Register context menu handlers with the FDP entity inspector.
                // 1) JSON-driven domain actions (populated by ExCon via ContextMenuState.MenuJson).
                _fdpEntityInspector.RegisterContextMenuHandler(_contextMenuHandler);
                // 2) Local editor authoring actions (centre, rename, edit, delete).
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.IsAlive(entity)) return;
                    if (entity == FdpRepositoryAdapter.SingletonEntity) return;

                    long networkId = _world.HasComponent<NetworkIdentity>(entity)
                        ? _world.GetComponentRO<NetworkIdentity>(entity).Value
                        : 0L;

                    bool hasPolyline = _world.HasManagedComponent<EditablePolyline>(entity);
                    bool hasRoute    = _world.HasManagedComponent<RoutePlan>(entity);

                    builder.AddItem("Center on Entity", () => _editorLogic?.CenterOnEntity(networkId));
                    if (networkId != 0)
                        builder.AddItem("Rename...", () => _editorLogic?.OpenRenameDialog(networkId));
                    if (hasPolyline)
                        builder.AddItem("Edit Shape", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Edit); });
                    if (hasRoute)
                        builder.AddItem("Edit Route", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Route); });
                    builder.AddItem("Rotate", () => { _editorLogic?.SelectEntity(networkId); _editorLogic?.ActivateTool(EditorTool.Rotate); });
                    builder.AddSeparator();
                    builder.AddItem("Delete", () => _world?.Bus.PublishManaged(
                        new DestroyEntityCommand { NetworkId = networkId, Reason = "EditorContextMenu" }));
                }));
                // 3) Perception seeding actions (mark target memory entries for selected perceivers).
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.HasComponent<TargetMemory>(entity)) return;

                    int perceiverCount = _selectionState?.SelectedEntities.Count ?? 0;

                    builder.AddSeparator();

                    builder.AddItem(
                        $"Mark Target for {perceiverCount} Units...",
                        async void () =>
                        {
                            int targetNetId = await _mapPickAdapter!.PickEntityAsync();
                            Entity target   = FindEntityByNetworkId(targetNetId);
                            if (!_world.IsAlive(target)) return;

                            foreach (var perceiver in _selectionState?.SelectedEntities ?? System.Array.Empty<Entity>())
                                _world.Bus.Publish(new SeedTargetCommand
                                {
                                    Perceiver  = perceiver,
                                    Target     = target,
                                    ScoreBoost = 1.0f,
                                });
                        });

                    builder.AddItem(
                        $"Mark Area Targets for {perceiverCount} Units...",
                        async void () =>
                        {
                            IReadOnlyList<int> targetNetIds = await _mapPickAdapter!.PickAreaEntitiesAsync();
                            foreach (var perceiver in _selectionState?.SelectedEntities ?? System.Array.Empty<Entity>())
                                foreach (int netId in targetNetIds)
                                {
                                    Entity target = FindEntityByNetworkId(netId);
                                    if (!_world.IsAlive(target)) continue;
                                    _world.Bus.Publish(new SeedTargetCommand
                                    {
                                        Perceiver  = perceiver,
                                        Target     = target,
                                        ScoreBoost = 1.0f,
                                    });
                                }
                        });
                }));

                // 4) AI tracing toggles (behav-diag-1). Only shown on entities with a brain.
                _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                {
                    if (!_world.IsAlive(entity)) return;
                    if (!_world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(entity)) return;

                    builder.AddSeparator();
                    builder.AddItem("Toggle AI Trace Buffer", () =>
                        interactionBus.Publish(new Hrot.Common.Events.GlobalActionRequestedEvent
                        {
                            ActionId = Hrot.Common.Constants.GlobalActionIds.ToggleAiTrace,
                            Target   = entity,
                        }));
                    builder.AddItem("Toggle AI Trace Log", () =>
                        interactionBus.Publish(new Hrot.Common.Events.GlobalActionRequestedEvent
                        {
                            ActionId = Hrot.Common.Constants.GlobalActionIds.ToggleAiTraceLog,
                            Target   = entity,
                        }));
                }));

                // Entity query ? all networked simulation entities with a location.
                var entityQuery = _world.Query()
                    .With<NetworkIdentity>()
                    .With<SimTransform>()
                    .WithLifecycle(EntityLifecycle.All)
                    .Build();

                // Gizmo layer ? renders entity presentation primitives produced locally by StatelessGizmoSystem.
                var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
                var layerControlEditService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
                using var layerControlSchemaSession = layerControlEditService.Open(
                    new Hrot.Common.Diagnostics.Gizmos.LayerControlDto
                    {
                        Entities = true,
                        Perception = true,
                        AiHelpers = true
                    },
                    typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
                schemaRegistry.Register(
                    Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash,
                    layerControlSchemaSession.Document);
                _gizmoLayer = new DebugGizmoLayer(
                    31,
                    _gizmoBuffer!,
                    interactionBus,
                    _world,
                    _canvas!.Camera,
                    new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(),
                    schemaRegistry);
                _canvas!.AddLayer(_gizmoLayer);
                if (_canvas != null) _canvas.DrawBuffer = _gizmoBuffer;

                // Grid map layer ? reads MapViewConfig.ShowGrid each frame.
                var gridLayer = new GridMapLayer(() => _mapViewConfig!.ShowGrid);
                _canvas!.AddLayer(gridLayer);

                // (Phase 5: StandardInteractionTool removed; entity interaction via ECS gizmos)
            }

            // ?? 11. UI panels ?????????????????????????????????????????????????
            _browserPanel = new ScenarioBrowserPanel();
            _toolbarPanel = new EditorToolbarPanel();
            _orbatPanel   = new EditorOrbatPanel();

            if (!_headless)
            {
                var tkbCatalog = new TkbCatalogEntry[]
                {
                    new(TkbEntityTypes.Tank_M1Abrams,      "M1 Abrams"),
                    new(TkbEntityTypes.IFV_Bradley,        "M2 Bradley IFV"),
                    new(TkbEntityTypes.Truck_HMMWV,        "HMMWV"),
                    new(TkbEntityTypes.Tank_T72,           "T-72"),
                    new(TkbEntityTypes.Infantry_Rifleman,  "Infantry Rifleman"),
                    new(TkbEntityTypes.Infantry_Officer,   "Infantry Officer"),
                    new(TkbEntityTypes.CivilianPedestrian, "Civilian Pedestrian"),
                    new(TkbEntityTypes.CivilianCar,        "Civilian Car"),
                    new(TkbEntityTypes.MilitaryApc,        "Military APC"),
                    new(TkbEntityTypes.InfantrySoldier,    "Infantry Soldier"),
                    new(TkbEntityTypes.Insurgent,          "Insurgent"),
                    new(TkbEntityTypes.Unit_TankPlatoon,   "Tank Platoon"),
                    new(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad"),
                    new(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto-Spawn)"),
                };

                _spawnerPanel     = new SpawnerPanel(tkbCatalog);
                _missionPanel     = new MissionPanel(0, Hrot.Presentation.Behavior.BehaviorUiSetup.CreateRegistry());
                _configPanel      = new ConfigPanel();
                _sharedOrbatPanel = new SharedOrbatPanel();
                _previewPanel     = new PreviewPanel();
                _zoneEditorPanel  = new ZoneEditorPanel();
            }
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            // Process input pipeline BEFORE kernel update so authored tools
            // (CreationTool, ObstaclePlacementTool, etc.) receive mouse events this frame.
            _canvas?.Update(deltaTime);
            _selectionSystem?.Tick(deltaTime);
            // Update camera viewport so MapCullingModule knows what area is on-screen.
            if (_camera != null)
            {
                var topLeft     = _camera.ScreenToWorld(System.Numerics.Vector2.Zero);
                var bottomRight = _camera.ScreenToWorld(
                    new System.Numerics.Vector2(Raylib_cs.Raylib.GetScreenWidth(), Raylib_cs.Raylib.GetScreenHeight()));

                _cameraViewport.WorldMinX = System.MathF.Min(topLeft.X, bottomRight.X);
                _cameraViewport.WorldMaxX = System.MathF.Max(topLeft.X, bottomRight.X);
                _cameraViewport.WorldMinY = System.MathF.Min(topLeft.Y, bottomRight.Y);
                _cameraViewport.WorldMaxY = System.MathF.Max(topLeft.Y, bottomRight.Y);
                _cameraViewport.Zoom      = _camera.Zoom;
            }

            // Clear the primitive buffer before backend ECS systems populate it.
            // This must happen before kernel.Update() and after canvas.Update() so that
            // tool-emitted primitives (written during canvas.Update ? ActiveTool.Draw) are
            // already in the buffer when StatelessGizmoSystem runs.
            _gizmoBuffer?.EndFrame(deltaTime);

            // Kernel.Update() internally calls bus.SwapBuffers() then ticks registered modules.
            _kernel?.Update();

            // Drain AI hot-reload callbacks safely on the main thread.
            // Any BTreeInterpreter pointer swaps queued by the background ALC worker
            // are applied here, between kernel ticks, so no active simulation tick
            // can observe a half-swapped pointer.
            _aiCoordinator?.DrainPendingCallbacks();

            // Swap the Control Plane bus so intents published by the UI this frame
            // are readable by ClusterMaster/ClusterUiCache on the orchestration bus.
            _orchestrationBus?.SwapBuffers();
            _clusterMaster?.Tick();
            _seekProcessManager?.Tick(); // Pump the seek Saga
            _replayProcessManager?.Tick(); // Pump the replay manager for duration extraction
            _assetInventoryProcessManager?.Tick();
            _assetPrefetchProcessManager?.Tick();
            _diagnosticsDumpProcessManager?.Tick();
            _logMergeWorker?.Tick();
            _uiCache?.Update();
            _editorLogic?.Update();
            _clusterPanel?.Update(deltaTime);

            // Drain ActivateEditorToolEvent ? published by toolbar / context menu.
            if (!_headless)
                DrainToolActivationEvents();

            // Poll mission ACKs so async CommitMissionAsync tasks can resolve.
            _missionService?.PollAcks();

            // Synchronise map selection to the MissionPanel using Network ID.
            if (_missionPanel != null && _selectionState != null && _world != null)
            {
                var selected = _selectionState.PrimarySelected;
                int selectedNetId = 0;

                if (selected.HasValue && selected.Value != Entity.Null && _world.IsAlive(selected.Value))
                {
                    if (_world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(selected.Value))
                    {
                        selectedNetId = (int)_world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(selected.Value).Value;
                    }
                }

                _missionPanel.SelectedEntityId = selectedNetId;
            }

            // Feed the FDP event browser each frame.
            if (!_headless && _world != null)
            {
                _fdpFrameCount++;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders the 2-D map canvas.
        /// Called inside <c>Raylib.BeginDrawing()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawWorld()
        {
            if (_headless) return;
            _canvas?.Draw();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// After <see cref="RegisterWindows"/>, the main editor panels are rendered by
        /// the Window Manager.  This method renders the map right-click context menu popup
        /// and the entity rename modal.
        /// </remarks>
        public void DrawUI()
        {
            if (_headless) return;

            // Render hover tooltip with entity info (label, health, cognitive state).
            if (_isActiveMapOwner() && !ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null && _world != null)
            {
                var mouseWorld = _canvas.Camera.ScreenToWorld(Raylib_cs.Raylib.GetMousePosition());
                var hovered = _canvas.PickTopmostEntity(mouseWorld);

                if (hovered.HasValue && hovered.Value != Entity.Null)
                {
                    var sb = new System.Text.StringBuilder();

                    if (_world.HasComponent<Hrot.IG.Components.ResolvedStyle>(hovered.Value))
                    {
                        ref readonly var style = ref _world.GetComponentRO<Hrot.IG.Components.ResolvedStyle>(hovered.Value);
                        string label = style.GetLabelText();
                        if (!string.IsNullOrEmpty(label)) sb.AppendLine(label);
                    }

                    if (_world.HasComponent<Fdp.Toolkit.Combat.Components.Health>(hovered.Value))
                    {
                        ref readonly var hp = ref _world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(hovered.Value);
                        sb.AppendLine($"Health: {hp.Current:F0} / {hp.Max:F0}");
                    }

                    if (_world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(hovered.Value))
                    {
                        ref readonly var ds = ref _world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(hovered.Value);
                        string docName = ds.ActiveBehaviorHash == 0 ? "Idle" : ds.ActiveBehaviorHash.ToString();

                        if (_behaviorRegistry != null && _behaviorRegistry.TryGetName(ds.ActiveBehaviorHash, out string? name))
                            docName = name;

                        sb.AppendLine($"Behavior: {docName} (Tier {ds.BrainTier})");
                    }

                    if (sb.Length > 0)
                    {
                        ImGuiNET.ImGui.BeginTooltip();
                        ImGuiNET.ImGui.TextUnformatted(sb.ToString().TrimEnd());
                        ImGuiNET.ImGui.EndTooltip();
                    }
                }
            }

            // Trigger ImGui popup when a right-click was recorded this frame.
            _gizmoLayer?.DrawContextMenu();
            _gizmoLayer?.DrawStructInspector();

            // Render gizmo-contributed main menu items (e.g. "View > Tactical Map Layers...").
            var gizmoMenus = _gizmoLayer?.ConsumeMainMenu();
            if (gizmoMenus != null && gizmoMenus.Count > 0)
            {
                if (ImGuiNET.ImGui.BeginMainMenuBar())
                {
                    GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenus, actionId =>
                    {
                        _interactionBus?.Publish(new GizmoMenuActionEvent { AnchorId = 0, ActionId = actionId });
                    });
                    ImGuiNET.ImGui.EndMainMenuBar();
                }
            }

            // Trigger rename modal when requested by DrainToolActivationEvents.
            if (_openRenameModalThisFrame)
            {
                ImGuiNET.ImGui.OpenPopup("Rename Entity");
                _openRenameModalThisFrame = false;
            }

            // Render the rename modal.
            bool isRenameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename Entity", ref isRenameOpen, ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (ImGuiNET.ImGui.IsKeyPressed(ImGuiNET.ImGuiKey.Escape))
                    ImGuiNET.ImGui.CloseCurrentPopup();

                ImGuiNET.ImGui.InputText("New Name", ref _renameBuffer, 64);
                ImGuiNET.ImGui.Separator();

                bool canSave = !string.IsNullOrWhiteSpace(_renameBuffer);
                if (!canSave) ImGuiNET.ImGui.BeginDisabled();
                if (ImGuiNET.ImGui.Button("Save") && canSave)
                {
                    // Find entity by network id, read existing EntityInfo and update name.
                    if (_world != null)
                    {
                        var q = _world.Query()
                            .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                            .With<EntityInfo>()
                            .Build();
						EntityInfo updatedInfo = default;
                        foreach (var e in q)
                        {
                            if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == _renameTargetNetworkId)
                            {
                                updatedInfo = _world.GetComponent<EntityInfo>(e);
                                break;
                            }
                        }
                        updatedInfo.Name = new Fdp.Core.FixedString64(_renameBuffer.Trim());
                        _editorLogic?.CommitPropertyEdit(_renameTargetNetworkId, new List<object> { updatedInfo });
                    }
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                if (!canSave) ImGuiNET.ImGui.EndDisabled();

                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                    ImGuiNET.ImGui.CloseCurrentPopup();

                ImGuiNET.ImGui.EndPopup();
            }
        }

        /// <inheritdoc/>
        public void RegisterWindows(Fdp.Presentation.WindowManager.WindowManager windowManager)
        {
            if (_editorLogic == null) return;

            // ?? Legacy editor-specific windows ????????????????????????????????
            windowManager.RegisterWindow(new EditorToolbarWindow(_toolbarPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorBrowserWindow(_browserPanel!, _editorLogic));
            if (_clusterPanel != null && _uiCache != null)
                windowManager.RegisterWindow(new Hrot.Orchestrator.Windows.ClusterControlWindow(_clusterPanel, _uiCache));
            if (_clusterDiagnosticsPanel != null)
                windowManager.RegisterWindow(new Hrot.Orchestrator.Windows.DiagnosticsWindow(_clusterDiagnosticsPanel));

            if (_headless) return;

            // ?? Shared UI panels ??????????????????????????????????????????????
            if (_spawnerPanel     != null && _spawnAdapter     != null)
                windowManager.RegisterWindow(new EditorSpawnerWindow(_spawnerPanel, _spawnAdapter));

            if (_missionPanel     != null && _missionService   != null && _mapPickAdapter != null)
                windowManager.RegisterWindow(new EditorMissionWindow(_missionPanel, _missionService, _mapPickAdapter));

            if (_configPanel      != null && _mapConfigAdapter  != null)
                windowManager.RegisterWindow(new EditorConfigWindow(_configPanel, _mapConfigAdapter));

            if (_sharedOrbatPanel != null && _orbatAdapter     != null)
                windowManager.RegisterWindow(new EditorSharedOrbatWindow(_sharedOrbatPanel, _orbatAdapter, _orbatAdapter));

            if (_previewPanel     != null && _previewController != null)
                windowManager.RegisterWindow(new EditorPreviewWindow(_previewPanel, _previewController));

            if (_zoneEditorPanel  != null && _zoneAdapter       != null)
                windowManager.RegisterWindow(new EditorZoneEditorWindow(_zoneEditorPanel, _zoneAdapter));

            // ?? FDP framework panels (entity inspector + event browser) ???????
            windowManager.RegisterWindow(new FdpEntityInspectorWindow(
                "editor_fdp_inspector", "Editor Entity Inspector", "Editor",
                _fdpEntityInspector,
                () => _fdpRepoAdapter,
                () => _fdpInspectorState,
                EditorWindowColor.TitleBar));

            // Wire component-editor reflector and "Inspect..." context menu.
            MapPickServiceBridge? editorPickBridge = _mapPickAdapter != null && _world != null
                ? new MapPickServiceBridge(_mapPickAdapter, _world)
                : null;
            FdpEntityInspectorHelper.WireInspectorWithInspectContextMenu(
                _fdpEntityInspector,
                windowManager,
                "Editor",
                () => _fdpRepoAdapter,
                editorPickBridge,
                EditorWindowColor.TitleBar);

            // Register the blackboard view provider so the editor projects typed DTO params.
            _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.BrainBlackboardViewProvider());
            // Register the heavy blackboard view provider for Blackboard1024.
            _fdpEntityInspector.Reflector.AddBufferViewProvider(new Hrot.Presentation.Renderers.Blackboard1024ViewProvider());

            // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType/HeavyDtoType to StructEdit.
            var capturedEditorRegistry = _behaviorRegistry;
            _fdpEntityInspector.Reflector.EditContextFactory = (session, e, type) =>
            {
                if (type != typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)
                 && type != typeof(Fdp.Toolkit.Behavior.Components.Blackboard1024)) return null;
                if (!session.HasComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))) return null;
                var ds = session.GetComponent(e, typeof(Fdp.Toolkit.Behavior.Components.BehaviorState))
                    as Fdp.Toolkit.Behavior.Components.BehaviorState?;
                if (ds == null) return null;
                if (capturedEditorRegistry?.TryGetDefinition(ds.Value.ActiveBehaviorHash, out var def) != true) return null;
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
                "editor_fdp_events", "Editor Event Browser", "Editor",
                _fdpEventBrowser,
                EditorWindowColor.TitleBar));

            // ?? Message Log: register hot-reload source ???????????????????????
            // The NLog source and the global window are created by Program.cs.
            // Here we attach the Editor-specific Hot Reload source so its messages
            // appear as a second tab in the shared Message Log window.
            if (_hotReloadSource != null)
                windowManager.MessageLogRegistry?.RegisterSource(_hotReloadSource);
            // Register the AI Behaviors log tab (dedicated tab for structured AI diagnostics).
            windowManager.MessageLogRegistry?.RegisterSource(AiBehaviorLogTarget.SharedInstance);

            if (_kernel != null)
            {
                windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
                    "editor_architecture_diagnostics", "Editor Architecture Diagnostics", "Editor",
                    new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(
                        new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(_kernel)),
                    EditorWindowColor.TitleBar));
            }

            // ?? Time transport controls in status bar ?????????????????????????
            if (_previewController != null && _timeController != null && _world != null)
            {
                var timeControls = new TimeControlStatusBarSection(_previewController, _timeController, _world);
                windowManager.StatusBar.RegisterSection(
                    id:             "editor_time_controls",
                    sortOrder:      100,
                    renderDelegate: timeControls.Render,
                    perspective:    "Editor");
            }

            // ?? Message Log notification icon in status bar ???????????????????
            // The MessageLogWindow is registered globally by Program.cs; we look it
            // up here so the Editor also shows the notification badge.
            if (windowManager.TryGetWindow("fdp_message_log", out var msgLogWin) &&
                msgLogWin is Fdp.Presentation.Windows.MessageLogWindow typedMsgLogWin)
            {
                var msgLogSection = new Fdp.Presentation.WindowManager.MessageLogStatusBarSection(
                    typedMsgLogWin, windowManager);
                windowManager.StatusBar.RegisterSection(
                    "msg_log_notify", sortOrder: 90, msgLogSection.Render);
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _aiCoordinator?.Dispose();
            _aiCoordinator = null;
            _kernel?.Dispose();
            _kernel = null;
            _physicsModule?.Dispose();
            _physicsModule = null;
            _world?.Dispose();
            _world = null;
            _editorLogic = null;
            _timeController = null;
            _canvas = null;
            _camera = null;
            _spawnAdapter     = null;
            _missionService   = null;
            _orbatAdapter     = null;
            _mapConfigAdapter = null;
            _mapPickAdapter   = null;
            _zoneAdapter      = null;
            _contextMenuHandler = null;
            _previewController  = null;
            _mapViewConfig      = null;
            _spawnerPanel     = null;
            _missionPanel     = null;
            _configPanel      = null;
            _sharedOrbatPanel = null;
            _previewPanel     = null;
            _zoneEditorPanel  = null;
            _fdpRepoAdapter   = null;
            _selectionState   = null;
            // (Phase 5: _interactionTool was here; removed)
            _clusterMaster?.Dispose();
            _clusterMaster  = null;
            _assetInventoryProcessManager = null;
            _assetPrefetchProcessManager = null;
            _diagnosticsDumpProcessManager = null;
            _logMergeWorker?.Dispose();
            _logMergeWorker = null;
            _uiCache?.Dispose();
            _uiCache        = null;
            _clusterPanel = null;
            _clusterDiagnosticsPanel = null;
            _fileDialogService = null;
            _storageGateway = null;
        }

        // ?? Private helpers ???????????????????????????????????????????????????

        /// <summary>
        /// Drains <see cref="ActivateEditorToolEvent"/> from the bus and routes each
        /// request to the appropriate canvas tool or adapter.
        /// Called once per frame from <see cref="Update"/> (non-headless only).
        /// </summary>
        private Entity FindEntityByNetworkId(long networkId)
        {
            if (_world == null) return default;
            var query = _world.Query().With<NetworkIdentity>().Build();
            foreach (var e in query)
                if (_world.GetComponent<NetworkIdentity>(e).Value == networkId)
                    return e;
            return default;
        }

        private void DrainToolActivationEvents()
        {
            if (_world == null || _canvas == null || _selectionState == null) return;

            foreach (ref readonly var evt in _world.Bus.Read<Hrot.Editor.Events.ActivateEditorToolEvent>())
            {
                switch (evt.Tool)
                {
                    case Hrot.Editor.EditorTool.Select:
                        // (Phase 5: _interactionTool removed; selection via ECS gizmos)
                        break;

                    case Hrot.Editor.EditorTool.Spawn:
                        // Start placement with the last selected type (tracked by the adapter).
                        _spawnAdapter?.StartPlacementModeWithLastType();
                        break;

                    case Hrot.Editor.EditorTool.Edit:
                    {
                        // Inject VertexEditGizmo directly via the gizmo system (toggle if already active).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(e))
                        {
                            if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(e))
                            {
                                _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            }
                            else
                            {
                                long netId = _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                                    ? _world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value
                                    : 0L;
                                var gizmo = new Hrot.ScenarioEditor.Gizmos.VertexEditGizmo(
                                    _world!, e, netId,
                                    onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                                _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                            }
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Route:
                    {
                        // Inject RouteWaypointGizmo directly via the gizmo system (toggle if already active).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(e))
                        {
                            if (_editorDataDrivenGizmoSystem!.HasInjectedGizmo(e))
                            {
                                _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            }
                            else
                            {
                                long netId = _world.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                                    ? _world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value
                                    : 0L;
                                var gizmo = new Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo(
                                    _world!, e, netId,
                                    onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                                _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                            }
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Measure:
                        if (_globalGizmoManager != null)
                        {
                            var id = GlobalGizmoManager.NewId();
                            var gizmo = new Hrot.ScenarioEditor.Gizmos.MeasureGizmo(onRemove: () => _globalGizmoManager?.Unregister(id));
                            _globalGizmoManager.Register(id, gizmo);
                        }
                        break;

                    case Hrot.Editor.EditorTool.Rotate:
                    {
                        // Inject EntityRotatorGizmo directly via the gizmo system.
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasComponent<Fdp.Core.SimTransform>(e))
                        {
                            _editorDataDrivenGizmoSystem!.DeactivateGizmo(e);
                            var gizmo = new Hrot.SimHost.Gizmos.EntityRotatorGizmo(
                                _world!, e,
                                onRemove: () => _editorDataDrivenGizmoSystem!.DeactivateGizmo(e));
                            _editorDataDrivenGizmoSystem!.ActivateGizmo(e, gizmo);
                        }
                        break;
                    }
                }
            }

            // ?? Drain camera-center requests ??????????????????????????????????
            foreach (ref readonly var cmd in _world.Bus.Read<Hrot.Editor.Commands.CenterOnEntityCommand>())
            {
                if (_camera == null) continue;
                var q = _world.Query()
                    .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<Fdp.Core.SimTransform>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        ref readonly var tf = ref _world.GetComponentRO<Fdp.Core.SimTransform>(e);
                        _camera.FocusOn(new System.Numerics.Vector2(tf.Position.X, tf.Position.Y));
                        break;
                    }
                }
            }

            // ?? Drain rename-dialog requests ??????????????????????????????????
            foreach (ref readonly var cmd in _world.Bus.Read<Hrot.Common.Events.OpenRenameDialogCommand>())
            {
                _renameTargetNetworkId    = cmd.NetworkId;
                _openRenameModalThisFrame = true;
                _renameBuffer             = string.Empty;

                // Pre-fill buffer with the entity's current name.
                var q = _world.Query()
                    .With<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<EntityInfo>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        _renameBuffer = _world.GetComponent<EntityInfo>(e).Name.ToString();
                        break;
                    }
                }
            }
        }

        // IEcsModule wrapper for Simulation-phase systems in the offline Editor.
        // The kernel forbids registering SystemPhase.Simulation systems as global systems;
        // they must be routed through a module.
        private sealed class EditorSimulationModule : IEcsModule
        {
            private readonly TogglableSimulationGroup _simulationGroup;

            public string Name => "EditorSimulation";
            public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

            public EditorSimulationModule(TogglableSimulationGroup simulationGroup)
                => _simulationGroup = simulationGroup;

            public void RegisterSystems(ISystemRegistry registry)
                => registry.RegisterSystem(_simulationGroup);

            public void Tick(ISimulationView view, float deltaTime) { }
        }
    }
}






