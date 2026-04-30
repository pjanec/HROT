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
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Modules;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Defaults;
using Fdp.Toolkit.Vis2D.Layers;
using Hrot.CGF;
using Hrot.CGF.Orchestration;
using Hrot.CGF.Systems;
using Hrot.Editor.Windows;
using Hrot.Orchestrator.Panels;
using Hrot.Presentation.Windows;
using Hrot.Common.Orchestration.Handlers;
using Hrot.Common.Scenario;
using Hrot.Editor;
using Hrot.Editor.Adapters;
using Hrot.Editor.Events;
using Hrot.Editor.Modules;
using Hrot.Editor.Rendering;
using Hrot.Editor.UI;
using Hrot.IG.Components;
using Hrot.IG.Layers;
using Hrot.IG.Systems;
using Hrot.IG.Modules;
using Hrot.Map.Common;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Rendering;
using Hrot.ScenarioEditor.Services;
using Hrot.ScenarioEditor.Adapters;
using Hrot.ScenarioEditor.Tools;
using Hrot.Presentation.Adapters;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
// Disambiguate IMapCameraProvider: Hrot.SimHost.Modules also defines this interface.
using IMapCameraProvider = Fdp.Toolkit.Runner.IMapCameraProvider;
using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel = Fdp.Presentation.Panels.EventBrowserPanel;
using FdpRepositoryAdapter = Fdp.Presentation.Adapters.RepositoryAdapter;
using FdpInspectorState = Fdp.Presentation.Abstractions.InspectorState;
using EditorInteractionTool = Hrot.ScenarioEditor.Tools.StandardInteractionTool;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.Editor
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the standalone HROT Editor.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — builds the offline ECS composition root
    ///   (entities, kernel, logic packs, adapters, UI panels) without DDS.</item>
    ///   <item><see cref="Update"/> — steps the time controller and ticks the kernel.</item>
    ///   <item><see cref="DrawWorld"/> — renders the 2-D map canvas (skipped in headless).</item>
    ///   <item><see cref="DrawUI"/> — renders ImGui panels not registered as managed windows
    ///   (skipped in headless).</item>
    ///   <item><see cref="RegisterWindows"/> — registers editor panels with the Window Manager
    ///   so they participate in the shared docking layout.</item>
    ///   <item><see cref="Shutdown"/> — disposes the kernel and ECS world.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class EditorSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "Editor";

        /// <inheritdoc/>
        /// <remarks>Slate blue — distinct from IG (green), SimHost (red) and ExCon (violet).</remarks>
        public Vector4 TitleBarColor => new(0.15f, 0.22f, 0.48f, 1f);

        // ── Network factory (no-op stubs for offline editor) ─────────────────

        private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();

        // ── Core state ────────────────────────────────────────────────────────

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private MasterSyncController?   _timeController;
        private PhysicsToolkitModule?   _physicsModule;
        private IEditorLogic?           _editorLogic;
        private MapCanvas?              _canvas;
        private MapCamera?              _camera;
        private bool                    _headless;

        // ── Adapters (canvas-dependent; null in headless) ─────────────────────

        private EditorSpawnAdapter?             _spawnAdapter;
        private EditorMissionService?           _missionService;
        private EditorOrbatAdapter?             _orbatAdapter;
        private EditorMapConfigAdapter?         _mapConfigAdapter;
        private EditorMapPickAdapter?           _mapPickAdapter;
        private EditorZoneAdapter?              _zoneAdapter;
        private EditorEntityContextMenuHandler? _contextMenuHandler;
        private EditorPreviewController?        _previewController;
        private MapViewConfig?                  _mapViewConfig;

        // ── UI panels (legacy, always created) ────────────────────────────────

        private ScenarioBrowserPanel? _browserPanel;
        private EditorToolbarPanel?   _toolbarPanel;
        private EditorOrbatPanel?     _orbatPanel;

        // ── Shared UI panels (skipped in headless) ────────────────────────────

        private SpawnerPanel?    _spawnerPanel;
        private MissionPanel?    _missionPanel;
        private ConfigPanel?     _configPanel;
        private SharedOrbatPanel? _sharedOrbatPanel;
        private PreviewPanel?    _previewPanel;
        private ZoneEditorPanel? _zoneEditorPanel;

        // ── FDP framework panels ──────────────────────────────────────────────

        private FdpEntityInspectorPanel _fdpEntityInspector = new();
        private FdpEventBrowserPanel    _fdpEventBrowser    = new();
        private FdpRepositoryAdapter?   _fdpRepoAdapter;
        private FdpInspectorState       _fdpInspectorState  = new();
        private uint                    _fdpFrameCount;
        private Fdp.Toolkit.Perception.Modules.AutonomousPerceptionModule? _perceptionMod;

        // ── Offline orchestrator (single-node scenario listing) ───────────────────

        private FdpEventBus?           _orchestrationBus;
        private ClusterMaster?                _clusterMaster;
        private AssetInventoryProcessManager?  _assetInventoryProcessManager;
        private StorageGatewayModule?          _storageGateway;
        private ClusterUiCache?                _uiCache;

        // ── Selection state ───────────────────────────────────────────────────────

        private DefaultSelectionState? _selectionState;
        // ── Doctrine registry (promoted for tooltip rendering) ─────────────────

        private DoctrineRegistry? _doctrineRegistry;

        // ── AI doctrine hot reloader ────────────────────────────────────────────

        private FbtAssemblyHotReloader?      _aiHotReloader;
        private HotReloadMessageLogSource?   _hotReloadSource;
        // Registration action built on the background ALC thread and applied on the
        // main thread via OnReloadCompleted -> DrainPendingCallbacks.
        private volatile Action<DoctrineRegistry>? _pendingDoctrineApply;
        // Captured at Initialize() so the hot-reload lambda can pass them to the factory.
        private IGeographicTransform? _geoTransform;
        private NetworkEntityMap?     _entityMap;
        // ── Production visualizer dependencies ───────────────────────────────────

        private readonly MapUserConfig     _userConfig     = new();
        private readonly MapCameraViewport _cameraViewport = new();

        // ── Tool handling ─────────────────────────────────────────────────────────

        private EditorInteractionTool? _interactionTool;

        // ── Context menu (ImGui popup trigger) ────────────────────────────────────

        private Entity _pendingContextMenuEntity = Entity.Null;
        private bool   _openContextMenuThisFrame;

        // ── Rename dialog state ───────────────────────────────────────────────────

        private long   _renameTargetNetworkId;
        private bool   _openRenameModalThisFrame;
        private string _renameBuffer = string.Empty;

        // ── Private helpers ───────────────────────────────────────────────────

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

            public void EnterPreviewMode()
            {
                _handler.TriggerLoadingPreview();
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

        // ── Nested helper: offline sequential ID allocator ────────────────────

        private sealed class SequentialIdAllocator : INetworkIdAllocator
        {
            private long _next = 1000;
            public long AllocateId()            => _next++;
            public void Reset(long startId = 0) => _next = startId;
            public void Dispose() { }
        }

        // ── Internal test accessors ───────────────────────────────────────────

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

        // ── ISubsystem lifecycle ──────────────────────────────────────────────

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
        /// Relative path segments to the AI Doctrines .csproj used by <see cref="IEditorLogic.RebuildAndReloadAI"/>.
        /// Set by the composition root (e.g. Program.cs) before <see cref="Initialize"/> is called.
        /// Defaults to the standard workspace layout.
        /// </summary>
        public string[] AiDoctrinesProjectPath { get; set; } =
            new[] { "Subsystems", "Hrot.AI.Doctrines", "Hrot.AI.Doctrines.csproj" };


        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            // ── 1. ECS world ─────────────────────────────────────────────────
            _world = new EntityRepository();
            _orchestrationBus = new FdpEventBus(); // Control Plane bus (cluster management)
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(_world);

            // ── 1b. Register all components BEFORE building serializers ───────
            // FdpAutoSerializer compiles property-extraction delegates at Build() time
            // against the current ComponentTypeRegistry, so all types must be registered
            // first — otherwise the serializer schema is empty and Save/Load is a no-op.
            SimHostComponentRegistry.RegisterAll(_world);
            CgfComponentRegistry.RegisterAll(_world);
            _world.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
            // MapDisplayComponent is used by MapLayerAssignmentSystem to tag entities
            // with the layer bitmask read by EntityRenderLayer for visibility culling.
            _world.RegisterComponent<MapDisplayComponent>();
            // IG presentation components required by NedVisualizerAdapter.
            _world.RegisterComponent<Hrot.IG.Components.CullingState>();
            _world.RegisterComponent<Hrot.IG.Components.ResolvedStyle>();
            _world.RegisterManagedComponent<Hrot.IG.Components.IgSymbolOverride>();
            // Visual effect components required by EventEffectModule (EventToEffectSystem).
            _world.RegisterComponent<VisualEffectState>();
            _world.RegisterComponent<TracerTarget>();

            // ── 2. Time controller (MasterSyncController in Deterministic/frozen mode) ──
            var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
            _timeController = (MasterSyncController)TimeControllerFactory.Create(_world.Bus, timeConfig);
            _kernel.SetTimeController(_timeController);
            // Start in Deterministic mode so authoring starts paused (dt == 0 every frame).
            _timeController.SwitchToDeterministic(new System.Collections.Generic.HashSet<int>());

            // ── 3. Shared services ────────────────────────────────────────────
            var geoTransform     = HrotEnvironment.CreateGeoTransform();
            _geoTransform = geoTransform;
            var entityMap        = new NetworkEntityMap();
            _entityMap = entityMap;
            var doctrineRegistry = new DoctrineRegistry();
            _doctrineRegistry = doctrineRegistry;
            // Register Urban Combat doctrines so MissionAdapterSystem can resolve Ambush
            // and InfantryCombat behavior trees when loading UrbanCombatNew scenario files.
            // CGF doctrines (MoveToLocation, FollowRoute, ...) are loaded asynchronously
            // from Hrot.AI.Doctrines.dll via TriggerInitialLoad() below.
            UrbanCombatNewScenario.RegisterUrbanCombatDoctrines(doctrineRegistry);

            // Expose the registry to the diagnostic renderers so the entity inspector
            // can project BrainBlackboard memory and visualize the BTree execution state.
            Hrot.Presentation.Renderers.BrainBlackboardRenderer.DoctrineRegistryAccessor = doctrineRegistry;
            Hrot.Presentation.Renderers.BTreeVisualizerRenderer.DoctrineRegistryAccessor = doctrineRegistry;

            // ── Hot reload: watch the deployment directory for Hrot.AI.Doctrines.dll changes ──
            // When the user clicks "Reload BTrees" and MSBuild overwrites the DLL, the watcher
            // detects the change, loads the new assembly into a fresh collectible ALC on a
            // background thread, and enqueues an interpreter swap for the main thread to apply.
            // Watch specifically for Hrot.AI.Doctrines.dll so the watcher does not fire
            // on unrelated DLL writes during compilation.
            string aiAssemblyDir = AppDomain.CurrentDomain.BaseDirectory;
            _aiHotReloader = new FbtAssemblyHotReloader(
                aiAssemblyDir, "Hrot.AI.Doctrines.dll",
                (registrarType, newAssembly) =>
            {
                // Find AiDoctrineFactory.BuildRegistrationAction in the newly-loaded assembly.
                var factoryType  = newAssembly.GetType("Hrot.AI.Doctrines.AiDoctrineFactory");
                var buildMethod  = factoryType?.GetMethod(
                    "BuildRegistrationAction",
                    BindingFlags.Public | BindingFlags.Static);

                if (buildMethod != null)
                {
                    // Execute on the background thread: CPU-heavy BTree compilation happens here.
                    var applyAction = (Action<DoctrineRegistry>?)buildMethod.Invoke(
                        null, new object?[] { _geoTransform, _entityMap });
                    if (applyAction != null)
                        _pendingDoctrineApply = applyAction;
                }

                // Return a sentinel so OnReloadCompleted fires once on the main thread.
                return new (string, BehaviorTreeBlob)[] { ("__ai_doctrines__", new BehaviorTreeBlob()) };
            });

            // Apply the staged registration on the main thread via DrainPendingCallbacks().
            // Fully overwrites all CGF doctrine entries so that ParamsDtoType and
            // ParseParams always reference the ALC-local CgfNodes types.
            _aiHotReloader.OnReloadCompleted += _ =>
            {
                var apply = System.Threading.Interlocked.Exchange(ref _pendingDoctrineApply, null);
                apply?.Invoke(_doctrineRegistry!);
                Console.WriteLine("[HotReload] AI Doctrines hot-swapped.");
            };

            // Load the current DLL immediately so doctrines are ready before the first frame.
            _aiHotReloader.TriggerInitialLoad();

            // ── Hot-reload message log source ─────────────────────────────────
            // Wire up after the hot-reloader is configured so that both the
            // doctrine-swap callbacks and the log-source callbacks are registered.
            _hotReloadSource = new HotReloadMessageLogSource();
            _aiHotReloader.OnReloadCompleted += _hotReloadSource.OnReloadCompleted;
            _aiHotReloader.OnReloadFailed    += _hotReloadSource.OnReloadFailed;

            var clusterSlave     = new ClusterSlave(0, "Editor", _orchestrationBus);
            var zoneService      = new ZoneManagerService();

            // Build the serializer with custom translators AFTER component registration
            // so FdpAutoSerializer compiles extraction delegates for all registered types.
            var scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(doctrineRegistry);

            // Inject bus and zoneService so file ops trigger WorldResetEvent and persist zone data.
            var fileService = new ScenarioFileService(scenarioSerializer, _world.Bus, zoneService);

            // ── 3b. TKB + ELM + offline spawning ─────────────────────────────
            var tkbDb       = HrotEnvironment.CreateTkb();
            // Register Urban Combat entity blueprints (TKB types 1001–2003) so the
            // ScenarioSerializer can resolve MilitaryApc, InfantrySoldier, and Insurgent.
            UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkbDb);
            var elm               = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
            var idAllocator       = new SequentialIdAllocator();
            var spawnSys          = new NetworkSpawningSystem(tkbDb, elm, entityMap, idAllocator, localNodeId: 0);
            var scenarioLoadSource = new ScenarioEntityCreationRequestSource();
            var extractor          = new StagingEntityExtractor();

            // ── 3c. Offline scenario load handler ─────────────────────────────
            var storageProvider = new LocalDiskStorageProvider(EditorBootstrap.ScenariosRoot);
            var scenarioLoader  = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
            clusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, extractor, scenarioLoadSource, idAllocator, _world));

            // ── 4. Module registration (offline — no translator packs) ────────
            var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
            var perceptionMod    = new AutonomousPerceptionModule(
                colliderRadiusReader: (view, e) => view.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e)
                    ? view.GetComponentRO<Fdp.Toolkit.Physics.Components.PhysicsCollider>(e).Radius
                    : 0f);
            _perceptionMod = perceptionMod;
            var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, entityMap,
                new ScenarioEntityCreationRequestSource());
            var orchPack         = new OrchestrationLogicPack(clusterSlave);
            var scenarioMod      = new ScenarioEditorModule(fileService);

            _kernel.RegisterModule(perceptionMod);
            _kernel.RegisterModule(orchPack);
            _kernel.RegisterModule(scenarioMod);

            // Register all known buses so the event browser combo box shows them.
            _fdpEventBrowser.RegisterBus("World", _world.Bus);
            _fdpEventBrowser.RegisterBus("Orchestration", _orchestrationBus!);
            _fdpEventBrowser.RegisterBus("Perception", perceptionMod.ScopedBus);

            // ── 4a. Multi-phase system registration for SimHostCorePack and CgfLogicPack ──
            // CGF Brain systems -- register directly (no toggling needed in the editor)
            foreach (var sys in cgfLogicPackInst.InputSystems)      _kernel.RegisterGlobalSystem(sys);

            // Muscle systems -- register directly
            foreach (var sys in simHostCorePack.InputSystems)          _kernel.RegisterGlobalSystem(sys);
            foreach (var sys in simHostCorePack.PostSimulationSystems) _kernel.RegisterGlobalSystem(sys);

            // Simulation-phase systems must go through a module (kernel forbids global registration)
            _kernel.RegisterModule(new EditorSimulationModule(
                cgfLogicPackInst.SimulationSystems,
                simHostCorePack.SimulationSystems));

            // NOTE: SimHostComponentRegistry.RegisterAll was moved to step 1b above.
            _kernel.RegisterModule(new EditorSystemsModule());

            // ── 4c. ELM + offline spawning module + scenario genesis pipeline ──────────────────
            // CreateEntityRequestSystem drains scenarioLoadSource each Input tick and emits
            // SpawnEntityCommand events for NetworkSpawningSystem (BeforeSync tick), which
            // sets AuthorityMask = ComponentMask for locally owned entities.
            var requestSystem = new CreateEntityRequestSystem(
                requestSource:      scenarioLoadSource,
                ackSink:            new NullEntityAckSink(),
                tkbDb:              tkbDb,
                idAllocator:        idAllocator,
                localNodeId:        0,
                isDefaultProcessor: true);
            _kernel.RegisterModule(elm);
            _kernel.RegisterModule(new SimHostModule(spawnSys));
            _kernel.RegisterGlobalSystem(requestSystem);

            // ── 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ──
            var logicPacks = new List<IEcsModule> { simHostCorePack, perceptionMod, cgfLogicPackInst };

            // ── 4d. MapLayerAssignmentSystem — must be registered BEFORE Initialize() ──
            // Stamps MapDisplayComponent.LayerMask on each entity so EntityRenderLayer
            // can cull entities whose layer is toggled off in the editor's config panel.
            _kernel.RegisterGlobalSystem(new MapLayerAssignmentSystem());

            // ── 4e. IG presentation modules — compute CullingState and ResolvedStyle ──
            // Must be registered BEFORE Initialize() so their component queries are built.
            _kernel.RegisterModule(new MapCullingModule(_cameraViewport));
            _kernel.RegisterModule(new StyleResolutionModule(_userConfig, localNodeId: 0));

            // ── 4f. Visual effects module — spawns and cleans up tracers / explosions ──
            _kernel.RegisterModule(new EventEffectModule());

            // ── 5. Kernel initialization ──────────────────────────────────────
            _kernel.Initialize();

            // ── 6. Editor application (IEditorLogic facade) ──────────────────
            var app = new EditorApplication(
                fileService, _world.Bus, _orchestrationBus, _world, _kernel, logicPacks,
                hotReloadSource: _hotReloadSource,
                aiProjectPathSegments: AiDoctrinesProjectPath);
            _editorLogic = app;

            // ── 6b. Offline orchestrator — scenario listing via ClusterMaster + UICache ──
            var offlineConfig = new ClusterConfiguration { Mandatory = Array.Empty<string>() };
            _clusterMaster  = new ClusterMaster(_orchestrationBus, offlineConfig);
            _storageGateway = new StorageGatewayModule();
            _assetInventoryProcessManager = new AssetInventoryProcessManager(
                _orchestrationBus,
                _storageGateway,
                EditorBootstrap.ScenariosRoot);
            _uiCache = new ClusterUiCache(_orchestrationBus);
            app.SetAvailableScenariosSource(() => _uiCache?.AvailableScenarios ?? Array.Empty<string>());

            // ── 7. Map canvas + camera (skipped in headless) ──────────────────
            if (!_headless)
            {
                _camera = new MapCamera();
                _canvas = new MapCanvas(new RaylibInputProvider());
                _canvas.Camera = _camera;
            }

            // ── 8. Preview controller (works headless too — no canvas dep) ────
            _previewController = new EditorPreviewController(_world, _timeController!);

            // ── 9. Mission service (no canvas dependency) ─────────────────────
            _missionService = new EditorMissionService(_world.Bus, _world, doctrineRegistry);

            // ── 10. Canvas-dependent adapters, layers, and interaction tool ───
            if (!_headless)
            {
                _mapViewConfig    = new MapViewConfig();
                _mapPickAdapter   = new EditorMapPickAdapter(_canvas!, geoTransform, _world);

                // Build the JSON→ECS attribute compiler with the geo-transform so that
                // geodetic spawn coordinates are projected correctly on entity placement.
                var jsonCompiler  = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform);
                _spawnAdapter     = new EditorSpawnAdapter(_canvas!, _world.Bus, jsonCompiler, tkbDb);
                _zoneAdapter      = new EditorZoneAdapter(_canvas!, _world.Bus);
                _mapConfigAdapter = new EditorMapConfigAdapter(_mapViewConfig, _canvas!);
                _selectionState   = new DefaultSelectionState();
                _orbatAdapter     = new EditorOrbatAdapter(_world, _world.Bus, _editorLogic, _spawnAdapter);
                _contextMenuHandler = new EditorEntityContextMenuHandler(
                    _world, _editorLogic, _world.Bus, _mapPickAdapter, _selectionState);
                _fdpRepoAdapter = new FdpRepositoryAdapter(_world);

                // Register context menu handler with the FDP entity inspector.
                _fdpEntityInspector.RegisterContextMenuHandler(_contextMenuHandler);

                // Entity query — all networked simulation entities with a location.
                // Excludes area overlays and routes so they render on their own dedicated layers.
                var entityQuery = _world.Query()
                    .With<NetworkIdentity>()
                    .With<SimTransform>()
                    .Without<Hrot.IG.Components.MapOverlayStyle>()
                    .WithoutManaged<Hrot.Map.Common.Components.RoutePlan>()
                    .WithLifecycle(EntityLifecycle.All)
                    .Build();

                // Entity render layer — uses EditorPerspectiveVisualizer for force-coloured
                // oriented shape silhouettes driven by DefaultEntityShapeLibrary.
                var visualizerAdapter = new Hrot.Editor.Adapters.EditorPerspectiveVisualizer(
                    new Fdp.Toolkit.Vis2D.Shapes.DefaultEntityShapeLibrary());
                var renderLayer = new EntityRenderLayer(
                    "Entities", layerBitIndex: -1,
                    _world, entityQuery, visualizerAdapter, _selectionState)
                {
                    Canvas = _canvas
                };
                _canvas!.AddLayer(renderLayer);

                // Area overlay render layer — draws tactical graphic polygon overlays.
                var overlayQuery = _world.Query()
                    .WithManaged<Hrot.IG.Components.EditablePolyline>()
                    .With<Hrot.IG.Components.MapOverlayStyle>()
                    .With<SimTransform>()
                    .WithLifecycle(EntityLifecycle.All)
                    .Build();
                _canvas.AddLayer(new MapOverlayRenderLayer(_world, overlayQuery));

                // Route render layer — draws RoutePlan waypoints for TacGraphic_Route entities.
                var routeQuery = _world.Query()
                    .With<TkbIdentity>()
                    .WithManaged<Hrot.Map.Common.Components.RoutePlan>()
                    .WithLifecycle(EntityLifecycle.All)
                    .Build();
                _canvas.AddLayer(new RouteRenderLayer(_world, routeQuery, _fdpInspectorState));

                // Zone obstacle render layer — draws LOS obstacle circles (always-on overlay).
                _canvas.AddLayer(new Hrot.IG.Layers.ZoneObstacleRenderLayer(_world));

                // Perception map layer — draws target-memory links between perceivers and targets.
                var perceptionLayer = new PerceptionMapLayer(_world);
                _canvas.AddLayer(perceptionLayer);

                // Grid map layer — reads MapViewConfig.ShowGrid each frame.
                var gridLayer = new GridMapLayer(() => _mapViewConfig!.ShowGrid);
                _canvas!.AddLayer(gridLayer);

                // Mission route layer — draws orange lines from entity to its mission waypoints.
                _canvas.AddLayer(new MissionRenderLayer(_world, geoTransform));

                // Visual effects layer — draws explosion circles and tracer lines.
                _canvas.AddLayer(new EffectRenderLayer(_world));
                _canvas.AddLayer(ProjectileLayerFactory.CreateLayer(_world, _selectionState, _canvas));

                // Standard interaction tool — pan, zoom, select, drag-and-drop.
                _interactionTool = new EditorInteractionTool(_world, entityQuery, visualizerAdapter, _selectionState);
                _canvas.SwitchTool(_interactionTool);

                // Drag handler — update SimTransform so the entity follows the cursor.
                _interactionTool.OnEntityMoved += (entity, pos) =>
                {
                    if (_world != null && _world.IsAlive(entity) && _world.HasComponent<Fdp.Core.SimTransform>(entity))
                    {
                        ref var tf = ref _world.GetComponentRW<Fdp.Core.SimTransform>(entity);
                        tf.Position = new System.Numerics.Vector3(pos.X, pos.Y, tf.Position.Z);
                    }
                };

                // Sync primary map selection → FDP entity inspector.
                _interactionTool.OnWorldClick += (_, _, _, _, hitEntity) =>
                {
                    if (hitEntity != Entity.Null)
                        _fdpInspectorState.SelectedEntity = hitEntity;
                };

                // Right-click on map → trigger context menu popup.
                _interactionTool.OnWorldClick += (_, btn, _, _, hitEntity) =>
                {
                    if (btn == Raylib_cs.MouseButton.Right)
                    {
                        _pendingContextMenuEntity = hitEntity;
                        _openContextMenuThisFrame = true;
                    }
                };
            }

            // ── 11. UI panels ─────────────────────────────────────────────────
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

            // Kernel.Update() internally calls bus.SwapBuffers() then ticks registered modules.
            _kernel?.Update();

            // Drain AI hot-reload callbacks safely on the main thread.
            // Any BTreeInterpreter pointer swaps queued by the background ALC worker
            // are applied here, between kernel ticks, so no active simulation tick
            // can observe a half-swapped pointer.
            _aiHotReloader?.DrainPendingCallbacks();

            // Swap the Control Plane bus so intents published by the UI this frame
            // are readable by ClusterMaster/ClusterUiCache on the orchestration bus.
            _orchestrationBus?.SwapBuffers();
            _clusterMaster?.Tick();
            _assetInventoryProcessManager?.Tick();
            _uiCache?.Update();

            // Drain ActivateEditorToolEvent — published by toolbar / context menu.
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
                _fdpEventBrowser.Update(_fdpFrameCount);
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
            if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null && _world != null)
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

                    if (_world.HasComponent<Fdp.Toolkit.Behavior.Components.DoctrineState>(hovered.Value))
                    {
                        ref readonly var ds = ref _world.GetComponentRO<Fdp.Toolkit.Behavior.Components.DoctrineState>(hovered.Value);
                        string docName = ds.ActiveDoctrineHash == 0 ? "Idle" : ds.ActiveDoctrineHash.ToString();

                        if (_doctrineRegistry != null && _doctrineRegistry.TryGetName(ds.ActiveDoctrineHash, out string? name))
                            docName = name;

                        sb.AppendLine($"Doctrine: {docName} (Tier {ds.BrainTier})");
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
            if (_openContextMenuThisFrame)
            {
                ImGuiNET.ImGui.OpenPopup("##editor_map_ctx");
                _openContextMenuThisFrame = false;
            }

            // Render the context menu popup.
            if (ImGuiNET.ImGui.BeginPopup("##editor_map_ctx"))
            {
                var builder = new Fdp.Presentation.Utils.ContextMenuBuilder();

                if (_pendingContextMenuEntity != Entity.Null && _contextMenuHandler != null)
                {
                    _contextMenuHandler.PopulateMenu(_pendingContextMenuEntity, builder);
                }
                else
                {
                    if (_contextMenuHandler != null)
                        Hrot.UI.Common.Menus.SharedContextMenuPopulator.PopulateEmptyMapMenu(builder, _contextMenuHandler);
                    else
                        builder.AddItem("Measurement Tool", () => { });
                }

                ImGuiNET.ImGui.EndPopup();
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

            // ── Legacy editor-specific windows ────────────────────────────────
            windowManager.RegisterWindow(new EditorToolbarWindow(_toolbarPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorBrowserWindow(_browserPanel!, _editorLogic));

            if (_headless) return;

            // ── Shared UI panels ──────────────────────────────────────────────
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

            // ── FDP framework panels (entity inspector + event browser) ───────
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

            // Inject EditContextFactory so TryOpenEditWindow passes ParamsDtoType to StructEdit.
            var capturedEditorRegistry = _doctrineRegistry;
            _fdpEntityInspector.Reflector.EditContextFactory = (session, e, type) =>
            {
                if (type != typeof(Fdp.Toolkit.Behavior.Components.BrainBlackboard)) return null;
                if (!session.HasComponent(e, typeof(Fdp.Toolkit.Behavior.Components.DoctrineState))) return null;
                var ds = session.GetComponent(e, typeof(Fdp.Toolkit.Behavior.Components.DoctrineState))
                    as Fdp.Toolkit.Behavior.Components.DoctrineState?;
                if (ds == null) return null;
                if (capturedEditorRegistry?.TryGetDefinition(ds.Value.ActiveDoctrineHash, out var def) != true) return null;
                if (def.ParamsDtoType == null) return null;
                return new StructEdit.Core.EditContext().With("ParamsDtoType", def.ParamsDtoType);
            };

            windowManager.RegisterWindow(new FdpEventBrowserWindow(
                "editor_fdp_events", "Editor Event Browser", "Editor",
                _fdpEventBrowser,
                EditorWindowColor.TitleBar));

            // ── Message Log: register hot-reload source ───────────────────────
            // The NLog source and the global window are created by Program.cs.
            // Here we attach the Editor-specific Hot Reload source so its messages
            // appear as a second tab in the shared Message Log window.
            if (_hotReloadSource != null)
                windowManager.MessageLogRegistry?.RegisterSource(_hotReloadSource);

            if (_kernel != null)
            {
                windowManager.RegisterWindow(new ArchitectureDiagnosticsWindow(
                    "editor_architecture_diagnostics", "Editor Architecture Diagnostics", "Editor",
                    new Fdp.Presentation.Panels.ArchitectureDiagnosticsPanel(),
                    () => _kernel,
                    EditorWindowColor.TitleBar));
            }
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _aiHotReloader?.Dispose();
            _aiHotReloader = null;
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
            _interactionTool  = null;
            _clusterMaster?.Dispose();
            _clusterMaster  = null;
            _assetInventoryProcessManager = null;
            _uiCache?.Dispose();
            _uiCache        = null;
            _storageGateway = null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Drains <see cref="ActivateEditorToolEvent"/> from the bus and routes each
        /// request to the appropriate canvas tool or adapter.
        /// Called once per frame from <see cref="Update"/> (non-headless only).
        /// </summary>
        private void DrainToolActivationEvents()
        {
            if (_world == null || _canvas == null || _selectionState == null) return;

            foreach (var evt in _world.Bus.ReadManaged<Hrot.Editor.Events.ActivateEditorToolEvent>())
            {
                switch (evt.Tool)
                {
                    case Hrot.Editor.EditorTool.Select:
                        if (_interactionTool != null)
                            _canvas!.SwitchTool(_interactionTool);
                        break;

                    case Hrot.Editor.EditorTool.Spawn:
                        // Start placement with the last selected type (tracked by the adapter).
                        _spawnAdapter?.StartPlacementModeWithLastType();
                        break;

                    case Hrot.Editor.EditorTool.Edit:
                    {
                        // Push EditTool for the primary selected entity (must have EditablePolyline).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(e))
                        {
                            var transform = _world.HasComponent<Fdp.Core.SimTransform>(e)
                                ? _world.GetComponent<Fdp.Core.SimTransform>(e)
                                : default;
                            var offset = new System.Numerics.Vector2(transform.Position.X, transform.Position.Y);
                            _canvas!.PushTool(new Hrot.ScenarioEditor.Tools.EditTool(e, _world, offset));
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Route:
                    {
                        // Push RouteEditTool for the primary selected entity (must have RoutePlan).
                        var entity = _selectionState.PrimarySelected;
                        if (entity is { } e && e != Entity.Null && _world.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(e))
                        {
                            var plan = ((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetManagedComponentRO<Hrot.Map.Common.Components.RoutePlan>(e);
                            _canvas!.PushTool(new Hrot.ScenarioEditor.Tools.RouteEditTool(
                                e, plan,
                                onCommit: (routeEntity, wps) =>
                                {
                                    var updated = new Hrot.Map.Common.Components.RoutePlan { IsLoop = plan.IsLoop };
                                    updated.Mutate(list => list.AddRange(wps));
                                    _world!.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.UpdateEntityCommand
                                    {
                                        NetworkId          = _world.GetComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(routeEntity).Value,
                                        ComponentsToUpdate = new System.Collections.Generic.List<object> { updated },
                                    });
                                    _canvas!.PopTool();
                                }));
                        }
                        break;
                    }

                    case Hrot.Editor.EditorTool.Measure:
                        if (_canvas != null)
                            _canvas.PushTool(new Hrot.ScenarioEditor.Tools.MeasureTool());
                        break;
                }
            }

            // ── Drain camera-center requests ──────────────────────────────────
            foreach (var cmd in _world.Bus.ReadManaged<Hrot.Editor.Commands.CenterOnEntityCommand>())
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

            // ── Drain rename-dialog requests ──────────────────────────────────
            foreach (var cmd in _world.Bus.ReadManaged<Hrot.Editor.Commands.OpenRenameDialogCommand>())
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
            private readonly IEnumerable<IEcsModuleSystem> _cgfSimSystems;
            private readonly IEnumerable<IEcsModuleSystem> _muscleSimSystems;

            public string Name => "EditorSimulation";
            public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

            public EditorSimulationModule(
                IEnumerable<IEcsModuleSystem> cgfSimSystems,
                IEnumerable<IEcsModuleSystem> muscleSimSystems)
            {
                _cgfSimSystems    = cgfSimSystems;
                _muscleSimSystems = muscleSimSystems;
            }

            public void RegisterSystems(ISystemRegistry registry)
            {
                foreach (var sys in _cgfSimSystems)    registry.RegisterSystem(sys);
                foreach (var sys in _muscleSimSystems) registry.RegisterSystem(sys);
            }

            public void Tick(ISimulationView view, float deltaTime) { }
        }
    }
}

