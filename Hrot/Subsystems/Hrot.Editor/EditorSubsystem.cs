using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Engine.Runner;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Adapters;
using FDP.Toolkit.ImGui.Panels;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Scenario;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;
using FDP.Toolkit.Vis2D.Layers;
using Hrot.CGF;
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
using Hrot.IG.Systems;
using Hrot.Map.Common;
using Hrot.Map.Common.Config;
using Hrot.Map.Common.Services;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor;
using Hrot.ScenarioEditor.Rendering;
using Hrot.ScenarioEditor.Services;
using Hrot.ScenarioEditor.Adapters;
using Hrot.ScenarioEditor.Tools;
using Hrot.SimHost;
using Hrot.SimHost.Modules;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.Core.Network;
using Fdp.ModuleHost.Core;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Core.Network.Interfaces;
// Disambiguate IMapCameraProvider: Hrot.SimHost.Modules also defines this interface.
using IMapCameraProvider = Fdp.Engine.Runner.IMapCameraProvider;
using FdpEntityInspectorPanel = FDP.Toolkit.ImGui.Panels.EntityInspectorPanel;
using FdpEventBrowserPanel    = FDP.Toolkit.ImGui.Panels.EventBrowserPanel;
using FdpRepositoryAdapter    = FDP.Toolkit.ImGui.Adapters.RepositoryAdapter;
using FdpInspectorState       = FDP.Toolkit.ImGui.Abstractions.InspectorState;
using EditorInteractionTool   = Hrot.ScenarioEditor.Tools.StandardInteractionTool;

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
        private SteppingTimeController? _stepping;
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

        // ── Offline orchestrator (single-node scenario listing) ───────────────────

        private ClusterMaster?         _clusterMaster;
        private StorageGatewayModule?  _storageGateway;
        private ClusterUiCache?        _uiCache;

        // ── Selection state ───────────────────────────────────────────────────────

        private DefaultSelectionState? _selectionState;

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
            private bool _inPreview;

            internal EditorPreviewController(EntityRepository world)
                => _handler = new PreviewClusterOpHandler(world);

            public bool IsInPreviewMode => _inPreview;

            public void EnterPreviewMode()
            {
                _handler.TriggerLoadingPreview();
                _inPreview = true;
            }

            public void ExitPreviewMode()
            {
                _handler.TriggerUnloadingPreview();
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

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => _camera?.GetCameraView();

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view) => _camera?.ApplyCameraView(view);

        // Non-interface helper kept for backward-compat with tests.
        public MapCamera? GetMapCamera() => _camera;

        // ── ISubsystem lifecycle ──────────────────────────────────────────────

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            // ── 1. ECS world ─────────────────────────────────────────────────
            _world = new EntityRepository();
            var accumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, accumulator);

            // ── 1b. Register all components BEFORE building serializers ───────
            // FdpAutoSerializer compiles property-extraction delegates at Build() time
            // against the current ComponentTypeRegistry, so all types must be registered
            // first — otherwise the serializer schema is empty and Save/Load is a no-op.
            SimHostComponentRegistry.RegisterAll(_world);
            _world.RegisterManagedComponent<Hrot.Map.Common.Components.ZoneMembership>();
            // MapDisplayComponent is used by MapLayerAssignmentSystem to tag entities
            // with the layer bitmask read by EntityRenderLayer for visibility culling.
            _world.RegisterComponent<MapDisplayComponent>();

            // ── 2. Time controller (stepping — no DDS sync partner) ──────────
            _stepping = new SteppingTimeController(new GlobalTime { TimeScale = 1.0f });
            _kernel.SetTimeController(_stepping);

            // ── 3. Shared services ────────────────────────────────────────────
            var entityMap        = new NetworkEntityMap();
            var doctrineRegistry = new DoctrineRegistry();
            var clusterSlave     = new ClusterSlave(0, "Editor", _world.Bus);
            var zoneService      = new ZoneManagerService();

            // Build the serializer with custom translators AFTER component registration
            // so FdpAutoSerializer compiles extraction delegates for all registered types.
            var scenarioSerializer = new ScenarioSerializerBuilder("Hrot.Scenario")
                .RegisterTranslator(new Hrot.SimHost.Serializers.TargetMemoryTranslator())
                .RegisterTranslator(new Hrot.SimHost.Serializers.PassengerBufferTranslator())
                .RegisterTranslator(new Hrot.SimHost.Serializers.WeaponChannelTranslator())
                .Build();

            // Inject bus and zoneService so file ops trigger WorldResetEvent and persist zone data.
            var fileService = new ScenarioFileService(scenarioSerializer, _world.Bus, zoneService);

            // ── 3b. TKB + ELM + offline spawning ─────────────────────────────
            var tkbDb       = HrotEnvironment.CreateTkb();
            var elm         = new EntityLifecycleModule(tkbDb, Array.Empty<int>());
            var idAllocator = new SequentialIdAllocator();
            var spawnSys    = new NetworkSpawningSystem(tkbDb, elm, entityMap, idAllocator, localNodeId: 0);

            // ── 3c. Offline scenario load handler ─────────────────────────────
            var storageProvider    = new LocalDiskStorageProvider(EditorBootstrap.ScenariosRoot);
            var scenarioLoader     = new HrotScenarioLoader(storageProvider, "Hrot.Scenario");
            clusterSlave.RegisterHandler(new Hrot.ScenarioEditor.Handlers.HrotEditLoadHandler(
                scenarioSerializer, scenarioLoader, zoneService, _world));

            // ── 4. Module registration (offline — no translator packs) ────────
            var simHostCorePack  = new SimHostCoreLogicPack(entityMap);
            var cgfLogicPackInst = new CgfLogicPack(doctrineRegistry, entityMap);
            var orchPack         = new OrchestrationLogicPack(clusterSlave);
            var scenarioMod      = new ScenarioEditorModule(fileService);

            _kernel.RegisterModule(simHostCorePack);
            _kernel.RegisterModule(cgfLogicPackInst);
            _kernel.RegisterModule(orchPack);
            _kernel.RegisterModule(scenarioMod);

            // NOTE: SimHostComponentRegistry.RegisterAll was moved to step 1b above.
            _kernel.RegisterModule(new EditorSystemsModule(_world));

            // ── 4c. ELM + offline spawning module ────────────────────────────
            _kernel.RegisterModule(elm);
            _kernel.RegisterModule(new SimHostModule(spawnSys));

            // ── 4b. Logic-pack list used by EditorApplication.SwitchToExternalAsync ──
            var logicPacks = new List<IEcsModule> { simHostCorePack, cgfLogicPackInst };

            // ── 4d. MapLayerAssignmentSystem — must be registered BEFORE Initialize() ──
            // Stamps MapDisplayComponent.LayerMask on each entity so EntityRenderLayer
            // can cull entities whose layer is toggled off in the editor's config panel.
            _kernel.RegisterGlobalSystem(new MapLayerAssignmentSystem());

            // ── 5. Kernel initialization ──────────────────────────────────────
            _kernel.Initialize();

            // ── 6. Editor application (IEditorLogic facade) ──────────────────
            var app = new EditorApplication(fileService, _world.Bus, _world, _kernel, logicPacks);
            _editorLogic = app;

            // ── 6b. Offline orchestrator — scenario listing via ClusterMaster + UICache ──
            var offlineConfig = new ClusterConfiguration { Mandatory = Array.Empty<string>() };
            _clusterMaster  = new ClusterMaster(_world.Bus, offlineConfig);
            _storageGateway = new StorageGatewayModule();
            _clusterMaster.SetStorageGateway(_storageGateway, EditorBootstrap.ScenariosRoot);
            _uiCache = new ClusterUiCache(_world.Bus);
            app.SetAvailableScenariosSource(() => _uiCache?.AvailableScenarios ?? Array.Empty<string>());

            // ── 7. Map canvas + camera (skipped in headless) ──────────────────
            if (!_headless)
            {
                _camera = new MapCamera();
                _canvas = new MapCanvas(new RaylibInputProvider());
                _canvas.Camera = _camera;
            }

            // ── 8. Preview controller (works headless too — no canvas dep) ────
            _previewController = new EditorPreviewController(_world);

            // ── 9. Mission service (no canvas dependency) ─────────────────────
            _missionService = new EditorMissionService(_world.Bus, _world, doctrineRegistry);

            // ── 10. Canvas-dependent adapters, layers, and interaction tool ───
            if (!_headless)
            {
                _mapViewConfig    = new MapViewConfig();
                _mapPickAdapter   = new EditorMapPickAdapter(_canvas!);

                // Build the JSON→ECS attribute compiler (no geo-transform: editor uses
                // Cartesian map coords) to inject EntityInfo on entity placement.
                var jsonCompiler  = Hrot.SimHost.AttributeCompilerFactory.Build(geoTransform: null);
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

                // Entity render layer — draws entity symbols on the map.
                var visualizerAdapter = new StubVisualizerAdapter();
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
                _canvas.AddLayer(new ZoneObstacleRenderLayer(_world));

                // Perception map layer — draws target-memory links between perceivers and targets.
                var perceptionLayer = new PerceptionMapLayer(_world);
                _canvas.AddLayer(perceptionLayer);

                // Grid map layer — reads MapViewConfig.ShowGrid each frame.
                var gridLayer = new GridMapLayer(() => _mapViewConfig!.ShowGrid);
                _canvas!.AddLayer(gridLayer);

                // Standard interaction tool — pan, zoom, select, drag-and-drop.
                _interactionTool = new EditorInteractionTool(_world, entityQuery, visualizerAdapter, _selectionState);
                _canvas.SwitchTool(_interactionTool);

                // Drag handler — update SimTransform so the entity follows the cursor.
                _interactionTool.OnEntityMoved += (entity, pos) =>
                {
                    if (_world != null && _world.IsAlive(entity) && _world.HasComponent<Fdp.Kernel.SimTransform>(entity))
                    {
                        ref var tf = ref _world.GetComponentRW<Fdp.Kernel.SimTransform>(entity);
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
                _missionPanel     = new MissionPanel();
                _configPanel      = new ConfigPanel();
                _sharedOrbatPanel = new SharedOrbatPanel();
                _previewPanel     = new PreviewPanel();
                _zoneEditorPanel  = new ZoneEditorPanel();
            }
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _stepping?.Step(deltaTime);

            // Process input pipeline BEFORE kernel update so authored tools
            // (CreationTool, ObstaclePlacementTool, etc.) receive mouse events this frame.
            _canvas?.Update(deltaTime);

            // Kernel.Update() internally calls bus.SwapBuffers() then ticks registered modules.
            _kernel?.Update();

            // After the kernel's SwapBuffers, events published in previous frames are in the
            // read buffer.  Tick the offline orchestrator and drain the tool-activation queue.
            _clusterMaster?.Tick();
            _uiCache?.Update();

            // Drain ActivateEditorToolEvent — published by toolbar / context menu.
            if (!_headless)
                DrainToolActivationEvents();

            // Poll mission ACKs so async CommitMissionAsync tasks can resolve.
            _missionService?.PollAcks();

            // Feed the FDP event browser each frame.
            if (!_headless && _world != null)
            {
                _fdpFrameCount++;
                _fdpEventBrowser.Update(_world.Bus, _fdpFrameCount);
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

            // Trigger ImGui popup when a right-click was recorded this frame.
            if (_openContextMenuThisFrame)
            {
                ImGuiNET.ImGui.OpenPopup("##editor_map_ctx");
                _openContextMenuThisFrame = false;
            }

            // Render the context menu popup.
            if (ImGuiNET.ImGui.BeginPopup("##editor_map_ctx"))
            {
                var builder = new FDP.Toolkit.ImGui.Utils.ContextMenuBuilder();

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
                            .With<FDP.Toolkit.Replication.Components.NetworkIdentity>()
                            .With<Hrot.IG.Components.EntityInfo>()
                            .Build();
                        Hrot.IG.Components.EntityInfo updatedInfo = default;
                        foreach (var e in q)
                        {
                            if (_world.GetComponent<FDP.Toolkit.Replication.Components.NetworkIdentity>(e).Value == _renameTargetNetworkId)
                            {
                                updatedInfo = _world.GetComponent<Hrot.IG.Components.EntityInfo>(e);
                                break;
                            }
                        }
                        updatedInfo.Name = new Fdp.Kernel.FixedString64(_renameBuffer.Trim());
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
        public void RegisterWindows(FDP.Toolkit.ImGui.WindowManager.WindowManager windowManager)
        {
            if (_editorLogic == null) return;

            // ── Legacy editor-specific windows ────────────────────────────────
            windowManager.RegisterWindow(new EditorToolbarWindow(_toolbarPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorBrowserWindow(_browserPanel!, _editorLogic));
            windowManager.RegisterWindow(new EditorOrbatWindow(_orbatPanel!, _editorLogic));

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

            windowManager.RegisterWindow(new FdpEventBrowserWindow(
                "editor_fdp_events", "Editor Event Browser", "Editor",
                _fdpEventBrowser,
                EditorWindowColor.TitleBar));
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _kernel?.Dispose();
            _kernel = null;
            _world?.Dispose();
            _world = null;
            _editorLogic = null;
            _stepping = null;
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

            foreach (var evt in _world.Bus.ConsumeManaged<Hrot.Editor.Events.ActivateEditorToolEvent>())
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
                            var transform = _world.HasComponent<Fdp.Kernel.SimTransform>(e)
                                ? _world.GetComponent<Fdp.Kernel.SimTransform>(e)
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
                            var plan = ((Fdp.ModuleHost.Core.Abstractions.ISimulationView)_world).GetManagedComponentRO<Hrot.Map.Common.Components.RoutePlan>(e);
                            _canvas!.PushTool(new Hrot.ScenarioEditor.Tools.RouteEditTool(
                                e, plan,
                                onCommit: (routeEntity, wps) =>
                                {
                                    var updated = new Hrot.Map.Common.Components.RoutePlan { IsLoop = plan.IsLoop };
                                    updated.Mutate(list => list.AddRange(wps));
                                    _world!.Bus.PublishManaged(new FDP.Toolkit.NetworkSpawning.Events.UpdateEntityCommand
                                    {
                                        NetworkId          = _world.GetComponent<FDP.Toolkit.Replication.Components.NetworkIdentity>(routeEntity).Value,
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
            foreach (var cmd in _world.Bus.ConsumeManaged<Hrot.Editor.Commands.CenterOnEntityCommand>())
            {
                if (_camera == null) continue;
                var q = _world.Query()
                    .With<FDP.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<Fdp.Kernel.SimTransform>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<FDP.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        ref readonly var tf = ref _world.GetComponentRO<Fdp.Kernel.SimTransform>(e);
                        _camera.FocusOn(new System.Numerics.Vector2(tf.Position.X, tf.Position.Y));
                        break;
                    }
                }
            }

            // ── Drain rename-dialog requests ──────────────────────────────────
            foreach (var cmd in _world.Bus.ConsumeManaged<Hrot.Editor.Commands.OpenRenameDialogCommand>())
            {
                _renameTargetNetworkId    = cmd.NetworkId;
                _openRenameModalThisFrame = true;
                _renameBuffer             = string.Empty;

                // Pre-fill buffer with the entity's current name.
                var q = _world.Query()
                    .With<FDP.Toolkit.Replication.Components.NetworkIdentity>()
                    .With<Hrot.IG.Components.EntityInfo>()
                    .Build();
                foreach (var e in q)
                {
                    if (_world.GetComponent<FDP.Toolkit.Replication.Components.NetworkIdentity>(e).Value == cmd.NetworkId)
                    {
                        _renameBuffer = _world.GetComponent<Hrot.IG.Components.EntityInfo>(e).Name.ToString();
                        break;
                    }
                }
            }
        }
    }
}

