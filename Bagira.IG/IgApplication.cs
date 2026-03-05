using System;

using System.Collections.Generic;

using System.Numerics;

using System.Threading.Tasks;

using System.Text.Json;

using CarKinem.Core;

using Bagira.BDC.SSTD;

using Bagira.BDC.SSTM;

using Bagira.DDS.DM;

using Bagira.IG.Adapters;

using Bagira.IG.Components;

using Bagira.IG.Modules;

using Bagira.IG.Systems;

using Bagira.IG.Tools;

using Bagira.IG.Translators;

using Bagira.IG.UI;

using Bagira.Map.Common;

using Bagira.Map.Common.Commands;

using Bagira.Map.Common.Events;

using Bagira.Map.Common.Replication;

using Bagira.Map.Common.Replication.Ingress;

using Bagira.Map.Definitions.Tkb;

using CycloneDDS.Runtime;

using Fdp.Kernel;

using Fdp.Modules.Geographic.Components;

using Fdp.Modules.Geographic.Transforms;

using FDP.Toolkit.Lifecycle;

using FDP.Kernel.Logging;

using FDP.Toolkit.Combat.Components;

using FDP.Toolkit.Lifecycle.Events;

using FDP.Toolkit.Perception.Components;

using FDP.Toolkit.Physics.Components;

using FDP.Toolkit.NetworkSpawning.Systems;

using FDP.Toolkit.Replication;

using FDP.Toolkit.Replication.Components;

using FDP.Toolkit.Replication.Services;

using FDP.Toolkit.Replication.Systems;

using FDP.Toolkit.Time.Controllers;

using FDP.Toolkit.Vis2D;

using FDP.Toolkit.Vis2D.Abstractions;

using FDP.Toolkit.Vis2D.Components;

using FDP.Toolkit.Vis2D.Defaults;

using FDP.Toolkit.Vis2D.Layers;

using ImGuiNET;

using FdpEntityInspectorPanel = FDP.Toolkit.ImGui.Panels.EntityInspectorPanel;

using FdpEventBrowserPanel    = FDP.Toolkit.ImGui.Panels.EventBrowserPanel;

using FdpRepositoryAdapter    = FDP.Toolkit.ImGui.Adapters.RepositoryAdapter;

using FdpInspectorState       = FDP.Toolkit.ImGui.Abstractions.InspectorState;

using ModuleHost.Core;

using ModuleHost.Core.Abstractions;

using ModuleHost.Core.Network;

using ModuleHost.Core.Network.Interfaces;

using ModuleHost.Network.Cyclone.Modules;

using DdsIdAllocator = ModuleHost.Network.Cyclone.Services.DdsIdAllocator;

using NodeIdMapper    = ModuleHost.Network.Cyclone.Services.NodeIdMapper;

using Raylib_cs;

using rlImGui_cs;



namespace Bagira.IG;



/// <summary>

/// Main application shell for the IG Mock. Owns the Raylib window, MapCanvas, and camera.

/// </summary>

public class IgApplication

{

    // --- Window constants ---

    public const int    WindowWidth  = 1600;

    public const int    WindowHeight = 900;

    public const int    TargetFps    = 60;

    public const string WindowTitle  = "IG Mock";



    // --- Debug overlay layout ---

    private const int DebugFontSize   = 18;

    private const int DebugLineHeight = 22;

    private const int DebugMarginX    = 10;

    private const int DebugMarginY    = 10;



    // --- Runtime state (rendering) ---

    private MapCanvas _canvas = null!;

    private MapCamera _camera = null!;



    /// <summary>

    /// Tracks the camera target set by arrow-key panning.

    /// Maintained separately from MapCamera._targetTarget so that mouse-drag pan

    /// and keyboard pan do not fight each other.

    /// </summary>

    private Vector2 _keyboardPanTarget;



    // --- Runtime state (ECS / network) ---

    private EntityRepository _world   = null!;

    private ModuleHostKernel _kernel  = null!;

    private NetworkEntityMap _entityMap = null!;

    private GhostCreationSystem? _ghostCreationSystem;

    private GeoSpatialIngressTranslator? _geoSpatialIngressTranslator;



    // ÔöÇÔöÇ Network enabled flag ÔÇö false when DDS libraries are unavailable (e.g. unit-test host)

    private bool _networkEnabled;



    // ÔöÇÔöÇ Headless flag ÔÇö set by InitializeEmbedded(); skips all Raylib/ImGui calls in Update/Draw

    private bool _headless;



    // ÔöÇÔöÇ Optional domain override (tests) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private int? _domainOverride;



    // ÔöÇÔöÇ Task 5: IG-to-IOS event translator state ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private WGS84Transform?                  _geoTransform;

    private BdcCommandGateway?               _commandGateway;

    private DdsWriter<MapClickEvent>?           _clickWriter;

    /// <summary>
    /// Writer that publishes the IG's current selection state to IOS so that the
    /// "Selection &amp; Mission" panel reflects whatever entity is clicked on the map.
    /// </summary>
    private DdsWriter<SelectionChangedEvent>?  _selectionWriter;

    private DdsReader<MapInteractionConfig>? _configReader;

    private Guid                             _activeContextId;

    private bool                             _showGrid;

    private Guid                             _lastPlacementContextId;

    private DdsWriter<CreateEntityRequest>?  _createEntityDdsWriter;

    // ÔöÇÔöÇ Drag tracking: world-space drop position set by OnEntityMoved ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private System.Numerics.Vector2          _lastDragWorldPos;

    // ÔöÇÔöÇ Style and culling objects ÔÇö updated and injected into modules

    private MapUserConfig     _userConfig     = null!;

    private MapCameraViewport _cameraViewport = null!;



    // ÔöÇÔöÇ ImGui UI panels (TASK-IF008) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private DebugPanelState       _debugPanelState   = null!;

    private IgDebugPanel          _debugPanel        = null!;

    private EntityInspectorState  _inspectorState    = null!;

    private EntityInspectorPanel  _inspectorPanel    = null!;

    private MiniIosPanelState     _miniIosState      = null!;

    private MiniIosPanel          _miniIosPanel      = null!;

    private PerformanceMetrics    _performanceMetrics = null!;

    private PerformanceOverlay    _performanceOverlay = null!;

    private ContextMenuPanel      _contextMenuPanel   = null!;

    private ContextMenuSystem     _contextMenuSystem  = null!;



    // ── FDP framework panels (Task 16) ────────────────────────────────────────────

    private FdpEntityInspectorPanel _fdpEntityInspector = new();

    private FdpEventBrowserPanel    _fdpEventBrowser    = new();

    private FdpRepositoryAdapter?   _fdpRepoAdapter;

    private FdpInspectorState       _fdpInspectorState  = new();

    private uint                    _fdpFrameCount;



    // ÔöÇÔöÇ Context menu state ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

    private Entity _mapContextEntity = Entity.Null;



    // -------------------------------------------------------------------------



    /// <summary>

    /// Standalone initialisation: opens a Raylib window then delegates to

    /// <see cref="InitializeEmbedded"/>.

    /// </summary>

    /// <param name="domainIdOverride">

    /// Optional DDS domain ID override.  When <see langword="null"/> (default) the

    /// value from <see cref="IgNetworkConstants.DdsDomain"/> is used.

    /// Pass a non-null value to isolate this instance on a separate domain.

    /// </param>

    public void Initialize(int? domainIdOverride = null)

    {

        Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);

        Raylib.SetTargetFPS(TargetFps);

        rlImGui.Setup(darkTheme: true);

        InitializeEmbedded(domainIdOverride: domainIdOverride);

    }



    /// <summary>

    /// Initialises ECS, network, camera, and canvas without creating a Raylib window.

    /// Used when the orchestrator owns the window (embedded mode).

    /// The caller must create a Raylib window before invoking any rendering methods.

    /// Pass <paramref name="headless"/> = <c>true</c> to skip all Raylib/ImGui calls

    /// even during <see cref="Update"/>.

    /// </summary>

    public void InitializeEmbedded(bool headless = false, int? domainIdOverride = null)

    {

        _headless = headless;

        _domainOverride = domainIdOverride;

        _camera = new MapCamera

        {

            MinZoom   = IgCameraConstants.MinZoom,

            MaxZoom   = IgCameraConstants.MaxZoom,

            ZoomSpeed = IgCameraConstants.ZoomSpeedPerTick

        };



        // Centre the camera over the initial world position.

        _camera.Target = new Vector2(IgCameraConstants.InitialPositionX, IgCameraConstants.InitialPositionY);

        _camera.Zoom   = IgCameraConstants.InitialZoom;

        // Offset keeps the world origin centred in the window.

        _camera.Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f);



        _keyboardPanTarget = new Vector2(

            IgCameraConstants.InitialPositionX,

            IgCameraConstants.InitialPositionY);



        _canvas        = new MapCanvas(new RaylibInputProvider());

        _canvas.Camera = _camera;



        InitializeEcs();

        InitializeNetwork(enableNetwork: true, domainIdOverride: _domainOverride);

    }



    public EntityRepository World => _world;



    // -------------------------------------------------------------------------



    /// <summary>

    /// Initialises the ECS world and kernel (no DDS ÔÇö safe to call in tests).

    /// </summary>

    private void InitializeEcs()

    {

        _world     = new EntityRepository();

        _entityMap = new NetworkEntityMap();



        var accumulator = new EventAccumulator();

        _kernel         = new ModuleHostKernel(_world, accumulator);



        // Pre-register components produced by style and culling systems.

        _world.RegisterComponent<ResolvedStyle>();

        _world.RegisterComponent<CullingState>();

        _world.RegisterComponent<SelectionState>();

        _world.RegisterComponent<NetworkIdentity>();

        _world.RegisterComponent<NetworkOwnership>();

        _world.RegisterComponent<NetworkAuthority>();

        _world.RegisterComponent<NetworkSpawnRequest>();

        _world.RegisterComponent<PendingNetworkAck>();

        _world.RegisterComponent<NetworkTransform>();

        _world.RegisterComponent<NetworkVelocity>();

        _world.RegisterComponent<SimTransform>();

        _world.RegisterComponent<SimVelocity>();

        _world.RegisterComponent<VehicleParams>();

        _world.RegisterComponent<IgHealthState>();

        _world.RegisterComponent<Faction>();

        _world.RegisterComponent<PerceptionReceptor>();

        _world.RegisterComponent<TargetMemory>();

        _world.RegisterComponent<WeaponState>();

        _world.RegisterComponent<Health>();

        _world.RegisterComponent<HealthData>();

        _world.RegisterComponent<PhysicsCollider>();

        _world.RegisterComponent<VisualData>();



        // IG4 ÔÇö Advanced Features components

        _world.RegisterComponent<HistoryTrail>();

        _world.RegisterComponent<VisualEffectState>();

        _world.RegisterComponent<TracerTarget>();

        _world.RegisterManagedComponent<ContextMenuState>();

        _world.RegisterManagedComponent<EditablePolyline>();

        _world.RegisterManagedComponent<IgEntityData>();

        _world.RegisterManagedComponent<SimCombatDef>();

        _world.RegisterManagedComponent<TkbCompositionDef>();



        _world.RegisterEvent<ConstructionOrder>();

        _world.RegisterEvent<ConstructionAck>();

        _world.RegisterEvent<DestructionOrder>();

        _world.RegisterEvent<DestructionAck>();



        // IG4 ÔÇö Events (skip in headless mode to avoid aggregated-ID collisions).

        if (!_headless)

        {

            try

            {

                _world.RegisterEvent<Bagira.Map.Common.Events.FireInteractionEvent>();

            }

            catch (TypeInitializationException ex) when (ex.InnerException is InvalidOperationException)

            {

                // Aggregated Runner mode can load multiple event types with the same ID.

                FdpLog<IgApplication>.Warn(

                    "[IG] FireInteractionEvent registration skipped: {0}",

                    ex.InnerException!.Message);

            }

            catch (InvalidOperationException ex)

            {

                // Aggregated Runner mode can load multiple event types with the same ID.

                FdpLog<IgApplication>.Warn("[IG] FireInteractionEvent registration skipped: {0}", ex.Message);

            }

        }



        _userConfig     = new MapUserConfig();

        _cameraViewport = new MapCameraViewport();



        // ÔöÇÔöÇ ImGui UI panels (TASK-IF008) ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ

        _debugPanelState    = new DebugPanelState(_userConfig);

        _debugPanel         = new IgDebugPanel(_debugPanelState);

        _inspectorState     = new EntityInspectorState();

        _inspectorPanel     = new EntityInspectorPanel(_inspectorState);

        _miniIosState       = new MiniIosPanelState();

        _miniIosPanel       = new MiniIosPanel(_miniIosState, _world.Bus);

        _performanceMetrics = new PerformanceMetrics();

        _performanceOverlay = new PerformanceOverlay(_performanceMetrics);

        _contextMenuSystem  = new ContextMenuSystem();

        _contextMenuPanel   = new ContextMenuPanel(_world, _contextMenuSystem, HandleContextMenuAction);



        _mapContextEntity = _world.CreateEntity();

        _world.AddComponent(_mapContextEntity, new NetworkIdentity(0));

    }



    /// <summary>

    /// Registers all modules and sets up the DDS participant (unless <paramref name="enableNetwork"/>

    /// is <c>false</c>).  Call after <see cref="InitializeEcs"/>.

    /// </summary>

    private void InitializeNetwork(bool enableNetwork, int? domainIdOverride)

    {

        _networkEnabled = enableNetwork;



        var domainId = domainIdOverride ?? IgNetworkConstants.DdsDomain;



        var tkb = BagiraEnvironment.CreateTkb();

        _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);



        var nodeMapper = new NodeIdMapper(

            localDomain:   domainId,

            localInstance: IgNetworkConstants.InstanceId);



        var topology = new StaticNetworkTopology(

            localNodeId: IgNetworkConstants.LocalNodeId,

            allNodes:    new[] { IgNetworkConstants.LocalNodeId });



        // A. EntityLifecycleModule ÔÇö no peers need to ACK in IG standalone mode

        var elm = new EntityLifecycleModule(tkb, Array.Empty<int>());

        _kernel.RegisterModule(elm);



        var replicationModule = new ReplicationLogicModule(_entityMap, tkb, elm);

        _kernel.RegisterModule(replicationModule);



        DdsParticipant? participant = null;

        DdsIdAllocator? ddsAllocator = null;

        List<Fdp.Interfaces.IDescriptorTranslator>? customTranslators = null;



        _networkEnabled = false;

        if (enableNetwork)

        {

            try

            {

                participant = BagiraEnvironment.CreateParticipant(domainId);



                // Task 5: Create command gateway, click writer and config reader.

                _commandGateway = new BdcCommandGateway(participant);

                _clickWriter     = new DdsWriter<MapClickEvent>(participant, "MapClickEvent");

                _selectionWriter = new DdsWriter<SelectionChangedEvent>(participant, "SelectionChangedEvent");

                _configReader    = new DdsReader<MapInteractionConfig>(participant);

                _createEntityDdsWriter = new DdsWriter<CreateEntityRequest>(participant, "CreateEntityRequest");



                _geoTransform = BagiraEnvironment.CreateGeoTransform();

                _miniIosState.SetGeoTransform(_geoTransform);



                _ghostCreationSystem = replicationModule.GhostCreationSystem;



                var entityMasterTranslator = new EntityMasterIngressTranslator(

                    participant, _entityMap, _world.Bus, _ghostCreationSystem);

                _geoSpatialIngressTranslator = new GeoSpatialIngressTranslator(

                    participant, _entityMap, _geoTransform, _ghostCreationSystem);

                var geoSpatialDrTranslator = new GeoSpatialDRIngressTranslator(

                    participant, _entityMap, _geoTransform, _ghostCreationSystem);

                var entityInfoTranslator = new EntityInfoIngressTranslator(

                    participant, _entityMap, _world.Bus, _ghostCreationSystem);

                var entityDamageTranslator = new EntityDamageIngressTranslator(

                    participant, _entityMap, _ghostCreationSystem);

                var mapEntitySymbolTranslator = new MapEntitySymbolIngressTranslator(

                    participant, _entityMap, IgNetworkConstants.MapGroupId, _ghostCreationSystem);

                var contextActionsTranslator = new ContextActionsUpdateTranslator(

                    participant, _entityMap, _world.Bus, _ghostCreationSystem);



                customTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>

                {

                    entityMasterTranslator,

                    _geoSpatialIngressTranslator,

                    geoSpatialDrTranslator,

                    entityInfoTranslator,

                    entityDamageTranslator,

                    mapEntitySymbolTranslator,

                    contextActionsTranslator,

                    new TimePulseIngressTranslator(participant, _world.Bus),

                };



                if (!_headless)

                    customTranslators.Add(new FireInteractionEventTranslator(participant, _entityMap));



                ddsAllocator = new DdsIdAllocator(participant, $"IG_{IgNetworkConstants.InstanceId}");

                _networkEnabled = true;

            }

            catch (Exception ex)

            {

                FdpLog<IgApplication>.Warn("[IG] Network init failed ({0}). Running offline.", ex.Message);

                _commandGateway?.Dispose();

                _clickWriter?.Dispose();

                _selectionWriter?.Dispose();

                _configReader?.Dispose();

                _createEntityDdsWriter?.Dispose();

                participant?.Dispose();

                _commandGateway = null;

                _clickWriter = null;

                _selectionWriter = null;

                _configReader = null;

                _createEntityDdsWriter = null;

                _geoTransform = null;

                _networkEnabled = false;

            }

        }



        // B. SpawningModule ÔÇö processes SpawnEntityCommand / DestroyEntityCommand

        INetworkIdAllocator idAllocator = _networkEnabled && ddsAllocator != null

            ? ddsAllocator

            : new IgSequentialIdAllocator();

        var spawningSystem = new NetworkSpawningSystem(

            tkb, elm, _entityMap, idAllocator,

            IgNetworkConstants.LocalNodeId);

        _kernel.RegisterModule(new SpawningModule(spawningSystem));



        // E. StyleResolutionModule ÔÇö writes ResolvedStyle each Simulation tick

        _kernel.RegisterModule(new StyleResolutionModule(_userConfig));



        // F. MapCullingModule ÔÇö writes CullingState each PostSimulation tick

        _kernel.RegisterModule(new MapCullingModule(_cameraViewport));



        // G. HistoryTrailModule ÔÇö records entity position trails (IG.4.1)

        _kernel.RegisterModule(new HistoryTrailModule());



        // H. EventEffectModule ÔÇö spawns and cleans up visual effects (IG.4.2)

        if (!_headless)

            _kernel.RegisterModule(new EventEffectModule());



        // C. CycloneNetworkModule ÔÇö DDS ingress/egress (optional)

        if (_networkEnabled && participant != null && ddsAllocator != null)

        {

            var networkModule = new CycloneNetworkModule(

                participant, nodeMapper, ddsAllocator,

                topology, elm,

                customTranslators: customTranslators,

                sharedEntityMap:   _entityMap);

            _kernel.RegisterModule(networkModule);

        }



        // D. EntityRenderLayer wired to the StubVisualizerAdapter

        var query = _world.Query()

            .With<NetworkIdentity>()

            .With<SimTransform>()

            .WithLifecycle(EntityLifecycle.All)

            .Build();



        var adapter   = new SstVisualizerAdapter();

        var selection = new DefaultSelectionState();

        var layer     = new EntityRenderLayer(

            "Entities", layerBitIndex: 0,

            _world, query, adapter, selection);

        _canvas.AddLayer(layer);



        // SelectionRenderSystem ÔÇö PostRender overlay drawing selection rings.

        var selectionQuery  = _world.Query()

            .With<SelectionState>()

            .With<SimTransform>()

            .WithLifecycle(EntityLifecycle.All)

            .Build();

        var selectionLayer  = new SelectionRenderSystem(_world, selectionQuery);

        _canvas.AddLayer(selectionLayer);



        // StandardInteractionTool ÔÇö default canvas tool wiring selection to ECS.

        var interactionTool = new StandardInteractionTool(_world, query, adapter, selection);

        _canvas.SwitchTool(interactionTool);



        interactionTool.OnWorldClick += OnCanvasWorldClick;



        // Task 5: Wire IG-to-IOS event translators when DDS participant is ready.

        if (_networkEnabled)

        {

            interactionTool.OnWorldClick += OnCanvasClicked;

            interactionTool.OnEntityDragEnd += OnEntityDragEnded;

            // Track world-space drag position so OnEntityDragEnded can read the actual drop

            // location rather than the (potentially stale) SimTransform.

            interactionTool.OnEntityMoved += (_, worldPos) => _lastDragWorldPos = worldPos;

            _miniIosPanel.SetGateway(_commandGateway);

        }



        // E. SlaveTimeController ÔÇö driven by TimePulse events on the event bus

        var timeController = new SlaveTimeController(_world.Bus);

        _kernel.SetTimeController(timeController);



        _kernel.RegisterGlobalSystem(new DeadReckoningSyncSystem());

        _kernel.RegisterGlobalSystem(_contextMenuSystem);



        _kernel.Initialize();

    }



    // -------------------------------------------------------------------------



    /// <summary>

    /// Advances one frame of IG logic (input, ECS tick, viewport update).  

    /// Must be called before <see cref="DrawWorld"/> and <see cref="DrawUI"/> each frame.

    /// Called by both the standalone <see cref="Run"/> loop and the embedded orchestrator.

    /// </summary>

    public void Update(float dt)

    {

        if (!_headless)

        {

            // Gate map input when ImGui is consuming the mouse (TASK-IF008).

            if (!ImGui.GetIO().WantCaptureMouse)

            {

                HandleCameraInput(dt);

                _canvas.Update(dt);

            }



            // Project screen corners to world space and feed MapCullingSystem.

            var topLeft     = _camera.ScreenToWorld(Vector2.Zero);

            var bottomRight = _camera.ScreenToWorld(new Vector2(WindowWidth, WindowHeight));

            _cameraViewport.WorldMinX = MathF.Min(topLeft.X, bottomRight.X);

            _cameraViewport.WorldMaxX = MathF.Max(topLeft.X, bottomRight.X);

            _cameraViewport.WorldMinY = MathF.Min(topLeft.Y, bottomRight.Y);

            _cameraViewport.WorldMaxY = MathF.Max(topLeft.Y, bottomRight.Y);

            _cameraViewport.Zoom      = _camera.Zoom;

        }



        // Always tick ECS/network ÔÇö even in headless mode DDS messages must be processed.

        _kernel.Update();

        _fdpFrameCount++;
        _fdpEventBrowser.Update(_world.Bus, _fdpFrameCount);



        // Task 5: Poll IOS Ôæå IG interaction-config updates (active context ID).

        if (_networkEnabled && _configReader != null)

        {

            using var loan = _configReader.Take(1);

            foreach (var sample in loan)

            {

                if (!sample.IsValid) continue;

                _activeContextId = sample.Data.ActiveContextId;

                FdpLog<IgApplication>.Debug(

                    "[TRACE-IG] MapInteractionConfig: ActiveContextId={0}", _activeContextId);



                if (!string.IsNullOrWhiteSpace(sample.Data.ConfigurationJson))

                    ParseAndApplyConfig(sample.Data.ConfigurationJson);

            }

        }



        if (!_headless)

        {

            // Update UI panel states (TASK-IF008).

            _performanceMetrics.Snapshot(_world, Raylib.GetFPS(), Raylib.GetFrameTime() * 1000f);

            _inspectorState.Refresh(_world, GetSelectedEntity());

        }

    }

    /// <summary>
    /// Returns the map camera owned by this IG application.
    /// Used by the Runner orchestrator to synchronise camera state when switching
    /// between IG and SimHost map perspectives.
    /// </summary>
    public MapCamera GetMapCamera() => _canvas.Camera;

    /// <summary>
    /// Renders the 2-D map canvas and debug overlay.  


    /// Must be called inside <c>Raylib.BeginDrawing()</c>, before ImGui.

    /// No-op in headless mode.

    /// </summary>

    public void DrawWorld()

    {

        _canvas.Draw();

        if (_showGrid)

            DrawGrid();

        DrawDebugOverlay();

    }



    /// <summary>

    /// Renders ImGui panels.  

    /// Must be called inside <c>rlImGui.Begin()</c>.

    /// No-op in headless mode.

    /// </summary>

    public void DrawUI()

    {

        _fdpRepoAdapter ??= new FdpRepositoryAdapter(_world);

        _debugPanel.Draw();

        _inspectorPanel.Draw();

        _miniIosPanel.Draw();

        _performanceOverlay.Draw();

        _contextMenuPanel.Draw();

        IgPanelColors.Push();

        _fdpEntityInspector.Draw(_fdpRepoAdapter, _fdpInspectorState, "IG Entity Inspector");

        IgPanelColors.Pop();

        IgPanelColors.Push();

        _fdpEventBrowser.Draw("IG Event Browser");

        IgPanelColors.Pop();

    }



    /// <summary>

    /// Runs the standalone main loop (owns window lifecycle).

    /// Uses <see cref="Update"/>, <see cref="DrawWorld"/>, and <see cref="DrawUI"/> internally.

    /// </summary>

    public void Run()

    {

        while (!Raylib.WindowShouldClose())

        {

            float dt = Raylib.GetFrameTime();

            Update(dt);



            Raylib.BeginDrawing();

            Raylib.ClearBackground(Color.DarkGray);

            DrawWorld();



            rlImGui.Begin();

            DrawUI();

            rlImGui.End();



            Raylib.EndDrawing();

        }

    }



    // -------------------------------------------------------------------------



    /// <summary>

    /// Processes keyboard camera controls (arrow-key pan, +/- zoom).

    /// Middle-mouse drag pan is handled automatically by MapCanvas/MapCamera.

    /// Mouse-wheel zoom is also handled by MapCanvas/MapCamera via RaylibInputProvider.

    /// </summary>

    private void HandleCameraInput(float dt)

    {

        // --- Arrow-key panning ---

        // panDir is in screen space: Up arrow = -Y (screen Y goes down),

        // which scrolls the view upward as the user expects.

        Vector2 panDir = Vector2.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.Right)) panDir.X += 1f;

        if (Raylib.IsKeyDown(KeyboardKey.Left))  panDir.X -= 1f;

        if (Raylib.IsKeyDown(KeyboardKey.Up))    panDir.Y -= 1f; // screen-up = -Y

        if (Raylib.IsKeyDown(KeyboardKey.Down))  panDir.Y += 1f; // screen-down = +Y



        if (panDir != Vector2.Zero)

        {

            // Accumulate displacement into our tracked target so that multiple

            // consecutive key frames add up correctly, even while the camera is

            // still interpolating toward a prior target.

            _keyboardPanTarget +=

                panDir * IgCameraConstants.ArrowKeyPanSpeedMetersPerSecond * dt;

            _camera.FocusOn(_keyboardPanTarget);

        }

        else

        {

            // Re-sync anchor to current interpolated camera position whenever

            // no arrow key is held, so the next key-press continues from wherever

            // the user has navigated (including via mouse drag).

            _keyboardPanTarget = _camera.Target;

        }



        // --- Keyboard zoom (+/=  and  -  keys) ---

        // Simulate a single wheel tick so the same 1.2+Œ factor is applied.

        bool zoomIn  = Raylib.IsKeyPressed(KeyboardKey.Equal)

                    || Raylib.IsKeyPressed(KeyboardKey.KpAdd);

        bool zoomOut = Raylib.IsKeyPressed(KeyboardKey.Minus)

                    || Raylib.IsKeyPressed(KeyboardKey.KpSubtract);



        Vector2 mousePos = Raylib.GetMousePosition();

        if (zoomIn)  _camera.ProcessInput(1.0f,  mousePos, false, false);

        if (zoomOut) _camera.ProcessInput(-1.0f, mousePos, false, false);

    }



    // -------------------------------------------------------------------------



    /// <summary>

    /// Returns the first entity in the ECS that has a <see cref="SelectionState"/>

    /// with <see cref="SelectionState.IsSelected"/> or

    /// <see cref="SelectionState.IsPrimarySelection"/> set to <c>true</c>.

    /// Returns <see cref="Entity.Null"/> when nothing is selected.

    /// </summary>

    private Entity GetSelectedEntity()

    {

        var q = _world.Query().With<SelectionState>().Build();

        foreach (var entity in q)

        {

            var state = _world.GetComponent<SelectionState>(entity);

            if (state.IsSelected || state.IsPrimarySelection)

                return entity;

        }

        return Entity.Null;

    }



    // -------------------------------------------------------------------------



    /// <summary>Draws camera state and cursor coordinates in screen space (outside Camera.BeginMode).</summary>

    private void DrawDebugOverlay()

    {

        Vector2 worldMousePos = _camera.ScreenToWorld(Raylib.GetMousePosition());



        int y = DebugMarginY;



        Raylib.DrawText(

            $"Camera: ({_camera.Target.X:F1}, {_camera.Target.Y:F1}) m",

            DebugMarginX, y, DebugFontSize, Color.White);

        y += DebugLineHeight;



        Raylib.DrawText(

            $"Zoom: {_camera.Zoom:F4} px/m  ({1f / _camera.Zoom:F2} m/px)",

            DebugMarginX, y, DebugFontSize, Color.White);

        y += DebugLineHeight;



        Raylib.DrawText(

            $"Mouse World: ({worldMousePos.X:F1}, {worldMousePos.Y:F1}) m",

            DebugMarginX, y, DebugFontSize, Color.White);

    }



    // -------------------------------------------------------------------------



    /// <summary>

    /// Releases all IG resources.  

    /// Pass <c>ownsWindow = false</c> when the orchestrator owns the Raylib window.

    /// </summary>

    public void Shutdown(bool ownsWindow = true)

    {

        _commandGateway?.Dispose();

        _clickWriter?.Dispose();

        _selectionWriter?.Dispose();

        _configReader?.Dispose();

        _createEntityDdsWriter?.Dispose();

        _kernel?.Dispose();

        if (ownsWindow)

        {

            rlImGui.Shutdown();

            Raylib.CloseWindow();

        }

    }



    // ÔöÇÔöÇ Task 5: IG-to-IOS event translators ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ



    /// <summary>

    /// Internal test hook to simulate a map click without Raylib input.

    /// </summary>

    internal void TestHook_SimulateMapClick(Vector2 worldPos)

        => OnCanvasClicked(worldPos, MouseButton.Left, false, false, Entity.Null);



    /// <summary>

    /// Internal test hook to simulate an operator click directly on a network entity,

    /// causing IG to publish <c>SelectionChangedEvent</c> with that entity selected.

    /// No-op if the entity is not found in the IG entity map.

    /// </summary>

    internal void TestHook_SimulateEntityClick(long networkId)

    {

        if (!TestHook_EntityMap.TryGetEntity(networkId, out var entity))

            return;

        OnCanvasClicked(Vector2.Zero, MouseButton.Left, false, false, entity);

    }



    /// <summary>

    /// Returns <c>true</c> when <see cref="CreationTool"/> is the currently active

    /// canvas tool  i.e. the operator is in placement mode (activated by an IOS

    /// <c>MapInteractionConfig</c>).

    /// </summary>

    internal bool TestHook_IsCreationToolActive => _canvas.ActiveTool is CreationTool;



    /// <summary>

    /// Directly invokes <see cref="CreationTool.HandleClick"/> with a left-click at

    /// <paramref name="worldPos"/>, bypassing the IOS-mediated <see cref="OnCanvasClicked"/>

    /// path.  This simulates what happens when the real operator clicks on the canvas

    /// while the placement tool is active.  No-op when <see cref="CreationTool"/> is not

    /// the active tool.

    /// </summary>

    internal void TestHook_DirectCreationToolClick(Vector2 worldPos)

    {

        if (_canvas.ActiveTool is CreationTool creationTool)

            creationTool.HandleClick(worldPos, MouseButton.Left);

    }



    /// <summary>

    /// Internal test hook to submit a Mini IOS spawn request via the DDS gateway.

    /// </summary>

    internal void TestHook_SubmitMiniIosSpawn(long tkbType, ForceId affiliation, float positionX, float positionY)

    {

        if (_commandGateway == null)

            throw new InvalidOperationException("Mini IOS gateway is not initialized.");



        _miniIosState.TkbType                = tkbType;

        _miniIosState.Affiliation            = affiliation;

        _miniIosState.PositionX              = positionX;

        _miniIosState.PositionY              = positionY;

        // Ensure explicit coordinates are used (not random) when the caller supplies a position.

        _miniIosState.UseSpecificCoordinates = true;

        _miniIosState.SubmitViaGateway(_commandGateway);

    }



    /// <summary>

    /// Internal test hook to submit a Mini IOS spawn + WanderMilitary mission request

    /// via the DDS gateway (network distributed path).

    /// </summary>

    internal Task TestHook_SubmitMiniIosSpawnWithWanderMission(

        long tkbType, ForceId affiliation, float positionX, float positionY)

    {

        if (_commandGateway == null)

            throw new InvalidOperationException("Mini IOS gateway is not initialized.");



        _miniIosState.TkbType                = tkbType;

        _miniIosState.Affiliation            = affiliation;

        _miniIosState.PositionX              = positionX;

        _miniIosState.PositionY              = positionY;

        _miniIosState.UseSpecificCoordinates = true;

        return _miniIosState.SubmitWithWanderMissionViaGateway(_commandGateway);

    }



    /// <summary>

    /// Internal test hook to expose the latest interaction context ID.

    /// </summary>

    internal Guid TestHook_ActiveContextId => _activeContextId;



    /// <summary>

    /// Internal test hook to expose the shared NetworkEntityMap.

    /// </summary>

    internal NetworkEntityMap TestHook_EntityMap => _entityMap;



    /// <summary>

    /// Internal test hook to inject GeoSpatial data into the ingress pipeline.

    /// </summary>

    internal void TestHook_InjectGeoSpatialDescriptor(GeoSpatial descriptor)

    {

        if (_geoSpatialIngressTranslator == null || _ghostCreationSystem == null)

            throw new InvalidOperationException("Ingress translators are not initialized.");



        if (!_entityMap.TryGetEntity(descriptor.EntityId, out var entity))

        {

            entity = _ghostCreationSystem.CreateGhost(_world, descriptor.EntityId);

        }



        _geoSpatialIngressTranslator.ApplyToEntity(entity, descriptor, _world);

    }



    /// <summary>

    /// Internal test hook to inject EntityMaster data into the ingress pipeline.

    /// </summary>

    internal void TestHook_InjectEntityMasterDescriptor(EntityMaster descriptor)

    {

        if (_ghostCreationSystem == null)

            throw new InvalidOperationException("Ghost creation system is not initialized.");



        if (!_entityMap.TryGetEntity(descriptor.EntityId, out var entity))

        {

            entity = _ghostCreationSystem.CreateGhost(_world, descriptor.EntityId);

        }



        var cmd = (EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();

        cmd.AddComponent(entity, new NetworkSpawnRequest

        {

            TkbType = descriptor.TkbType,

            DisType = descriptor.DisType,

            OwnerId = 0

        });

        cmd.Playback(_world);

    }



    /// <summary>

    /// Converts a canvas world-click to a <see cref="MapClickEvent"/> and writes it

    /// to the DDS "MapClickEvent" topic so IOS can route the interaction.

    /// No-op when network is disabled.

    /// </summary>

    private void OnCanvasClicked(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit)

    {

        if (!_networkEnabled || _clickWriter == null || _geoTransform == null)

            return;



        var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));



        var hitStack = new List<MapObjectRef>();

        if (hit != Entity.Null && _world.HasComponent<NetworkIdentity>(hit))

        {

            ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(hit);

            hitStack.Add(new MapObjectRef

            {

                EntityId   = (int)netId.Value,

                TkbType    = _world.HasComponent<NetworkSpawnRequest>(hit)

                    ? (int)_world.GetComponentRO<NetworkSpawnRequest>(hit).TkbType : 0,

                VisualPart = EEntitySymbolPart.ESP_BODY,

            });

        }



        var evt = new MapClickEvent

        {

            MapId                = IgNetworkConstants.InstanceId,

            Position             = new GeoPosition { Latitude = lat, Longitude = lon, Altitude = alt },

            InteractionContextId = _activeContextId,

            HitStack             = hitStack,

        };



        _clickWriter.Write(evt);

        FdpLog<IgApplication>.Info("[IG] MapClickEvent published. ContextId={0} hit={1}", _activeContextId, hit.Index);

        // Publish selection state so IOS can update the "Selection & Mission" panel.
        // A non-empty hit selects the entity; an empty-space click clears the selection.
        if (_selectionWriter != null)
        {
            var selIds = hitStack.Count > 0
                ? hitStack.ConvertAll(r => r.EntityId)
                : new System.Collections.Generic.List<int>();
            _selectionWriter.Write(new SelectionChangedEvent
            {
                MapId             = IgNetworkConstants.InstanceId,
                SelectedEntityIds = selIds,
            });
            FdpLog<IgApplication>.Debug("[IG] SelectionChangedEvent published. count={0}", selIds.Count);
        }

    }



    private void OnCanvasWorldClick(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit)

    {

        if (button != MouseButton.Right)

            return;



        var targetEntity = hit != Entity.Null ? hit : _mapContextEntity;

        var mousePos = Raylib.GetMousePosition();



        _contextMenuSystem.RequestOpen(targetEntity, mousePos.X, mousePos.Y);

    }



    /// <summary>

    /// Handles the end of an entity drag on the 2-D map canvas.

    /// Reads the entity's final drop position from <see cref="_lastDragWorldPos"/> (set on

    /// every <c>OnEntityMoved</c> frame), converts it to geodetic coordinates, and publishes

    /// an <see cref="UpdateEntityDescriptorRequest"/> so the authoritative node (SimHost)

    /// can persist the new position.

    ///

    /// Reads from the tracked drag position rather than from <see cref="SimTransform"/> because

    /// the drag tool does not write the ECS component during its drag frames ÔÇö only the visual

    /// position is updated.  No-op when network is disabled or required services are unavailable.

    /// </summary>

    private void OnEntityDragEnded(Entity entity)

    {

        if (!_networkEnabled || _commandGateway == null || _geoTransform == null) return;



        var view = (ISimulationView)_world;

        if (!view.HasComponent<NetworkIdentity>(entity)) return;



        long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;



        // Use the world-space drop position tracked by OnEntityMoved.

        // Fall back to SimTransform only when no drag move was recorded (e.g. test path).

        System.Numerics.Vector3 position;

        if (_lastDragWorldPos != default)

        {

            position = new System.Numerics.Vector3(_lastDragWorldPos.X, _lastDragWorldPos.Y, 0f);

        }

        else if (view.HasComponent<SimTransform>(entity))

        {

            position = view.GetComponentRO<SimTransform>(entity).Position;

        }

        else

        {

            return;

        }

        var (lat, lon, alt) = _geoTransform.ToGeodetic(position);



        var request = new UpdateEntityDescriptorRequest

        {

            RequestId      = Guid.NewGuid(),

            EntityId       = (int)netId,

            DescriptorType = EDescriptorType.dtGeoSpatial,

            Payload        = new EntityDescriptorUnion

            {

                _d         = EDescriptorType.dtGeoSpatial,

                GeoSpatial = new GeoSpatial

                {

                    EntityId = (int)netId,

                    Time     = DateTime.UtcNow,

                    Pos      = new GeoPosition

                    {

                        Latitude  = lat,

                        Longitude = lon,

                        Altitude  = alt,

                    },

                    Rot = new OrientationHPR(),

                },

            },

        };



        _commandGateway.SendUpdateDescriptor(request);

        // Reset stale drag position: a subsequent drag ending without movement must not reuse it.
        _lastDragWorldPos = default;


        FdpLog<IgApplication>.Info(

            "[IG] Drag end: sent UpdateEntityDescriptorRequest for NetID {0} to ({1:F5}T-, {2:F5}T-).",

            netId, lat, lon);

    }



    /// <summary>

    /// Internal test hook to simulate a drag-end for the entity with the given network ID.

    /// Directly calls <see cref="OnEntityDragEnded"/> ÔÇö requires the entity to already

    /// exist in the ECS world with a <see cref="SimTransform"/> component set to the

    /// desired drop position before calling this hook.

    /// </summary>

    /// <summary>
    /// Test hook: sets <c>_lastDragWorldPos</c> to <paramref name="dropWorldPos"/> and fires
    /// <see cref="OnEntityDragEnded"/> so that an <see cref="UpdateEntityDescriptorRequest"/>
    /// is sent to SimHost over DDS.  Network must be enabled and the entity must have a
    /// <see cref="NetworkIdentity"/> component.
    /// </summary>
    internal void TestHook_SimulateDragDrop(long networkId, System.Numerics.Vector2 dropWorldPos)
    {
        if (!_entityMap.TryGetEntity(networkId, out var entity))
            throw new InvalidOperationException($"Entity with networkId={networkId} not found.");
        _lastDragWorldPos = dropWorldPos;
        OnEntityDragEnded(entity);
    }

    internal void TestHook_SimulateDragEnd(long networkId)

    {

        if (!_entityMap.TryGetEntity(networkId, out var entity))

            throw new InvalidOperationException($"Entity with networkId={networkId} not found in IG entity map.");



        OnEntityDragEnded(entity);

    }



    /// <summary>

    /// Internal test hook to overwrite an entity's <see cref="SimTransform"/> (e.g. to

    /// simulate the local position that the drag tool writes during a drag operation).

    /// </summary>

    internal void TestHook_SetEntitySimTransform(long networkId, SimTransform transform)

    {

        if (!_entityMap.TryGetEntity(networkId, out var entity))

            throw new InvalidOperationException($"Entity with networkId={networkId} not found in IG entity map.");



        _world.SetComponent(entity, transform);

    }



    private void HandleContextMenuAction(Entity entity, ContextAction action)

    {

        if (action.ActionName.StartsWith("IG_", StringComparison.Ordinal))

        {

            ExecuteLocalContextAction(entity, action.ActionName);

            return;

        }



        int networkId = 0;

        var view = (ISimulationView)_world;

        if (view.HasComponent<NetworkIdentity>(entity))

        {

            ref readonly var id = ref view.GetComponentRO<NetworkIdentity>(entity);

            networkId = (int)id.Value;

        }



        _world.Bus.PublishManaged(new ContextActionTriggered

        {

            EntityNetworkId = networkId,

            ActionName = action.ActionName

        });

    }



    private void ExecuteLocalContextAction(Entity entity, string actionName)

    {

        switch (actionName)

        {

            case "IG_Center":

            case "IG_CenterOnEntity":

                CenterCameraOn(entity);

                break;

            default:

                FdpLog<IgApplication>.Warn("[IG] Unhandled local context action: {0}", actionName);

                break;

        }

    }



    private void CenterCameraOn(Entity entity)

    {

        var view = (ISimulationView)_world;

        if (!view.HasComponent<SimTransform>(entity))

            return;



        ref readonly var transform = ref view.GetComponentRO<SimTransform>(entity);

        var target = new Vector2(transform.Position.X, transform.Position.Y);

        _keyboardPanTarget = target;

        _camera.FocusOn(target);

    }



    // ÔöÇÔöÇ Grid rendering ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ



    /// <summary>

    /// Draws an adaptive world-space grid.  Call inside Raylib.BeginDrawing() but

    /// outside any existing camera mode (this method manages its own BeginMode / EndMode).

    /// </summary>

    private void DrawGrid()

    {

        const float BaseSpacingMeters = 1000f;

        const int   MaxGridLines      = 80;



        var topLeft     = _camera.ScreenToWorld(Vector2.Zero);

        var bottomRight = _camera.ScreenToWorld(new Vector2(WindowWidth, WindowHeight));



        float worldLeft   = MathF.Min(topLeft.X, bottomRight.X);

        float worldRight  = MathF.Max(topLeft.X, bottomRight.X);

        float worldTop    = MathF.Min(topLeft.Y, bottomRight.Y);

        float worldBottom = MathF.Max(topLeft.Y, bottomRight.Y);



        // Select a spacing so we get at most MaxGridLines in each axis.

        float spacing = BaseSpacingMeters;

        float visW = worldRight  - worldLeft;

        float visH = worldBottom - worldTop;

        while (visW / spacing > MaxGridLines || visH / spacing > MaxGridLines)

            spacing *= 10f;

        while (spacing > BaseSpacingMeters

            && visW / (spacing / 10f) <= MaxGridLines

            && visH / (spacing / 10f) <= MaxGridLines)

            spacing /= 10f;



        float startX = MathF.Floor(worldLeft / spacing) * spacing;

        float startY = MathF.Floor(worldTop  / spacing) * spacing;



        var lineColor = new Color(200, 200, 200, 60);



        _camera.BeginMode();



        for (float x = startX; x <= worldRight + spacing; x += spacing)

            Raylib.DrawLineV(new Vector2(x, worldTop    - spacing),

                             new Vector2(x, worldBottom + spacing), lineColor);



        for (float y = startY; y <= worldBottom + spacing; y += spacing)

            Raylib.DrawLineV(new Vector2(worldLeft  - spacing, y),

                             new Vector2(worldRight + spacing, y), lineColor);



        _camera.EndMode();

    }



    // ÔöÇÔöÇ Config JSON parsing ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ



    /// <summary>

    /// Parses a JSON Merge Patch from IOS and applies client-side settings

    /// (<c>view.layers.grid</c>, <c>interaction.activeTool</c>).

    /// </summary>

    private void ParseAndApplyConfig(string json)

    {

        try

        {

            using var doc  = JsonDocument.Parse(json);

            var       root = doc.RootElement;



            // view.layers.grid Ôæå toggle grid rendering

            if (root.TryGetProperty("view",   out var viewEl)

             && viewEl.TryGetProperty("layers", out var layersEl)

             && layersEl.TryGetProperty("grid",  out var gridEl))

            {

                _showGrid = gridEl.GetBoolean();

            }



            // interaction.activeTool + toolConfig Ôæå activate canvas tool

            if (root.TryGetProperty("interaction", out var interactionEl)

             && interactionEl.TryGetProperty("activeTool", out var toolEl)

             && toolEl.GetString() == "PLACEMENT"

             && interactionEl.TryGetProperty("toolConfig", out var toolConfigEl))

            {

                long    tkbType = 0;

                ForceId aff     = ForceId.Unknown;



                if (toolConfigEl.TryGetProperty("entityType", out var etEl))

                    tkbType = etEl.GetInt64();



                if (toolConfigEl.TryGetProperty("affiliation", out var affEl))

                {

                    aff = affEl.GetString() switch

                    {

                        "FORCE_FRIENDLY" => ForceId.Friend,

                        "FORCE_OPPOSING" => ForceId.Hostile,

                        "FORCE_NEUTRAL"  => ForceId.Neutral,

                        _                => ForceId.Unknown,

                    };

                }



                ActivatePlacementTool(tkbType, aff);

            }

        }

        catch (Exception ex)

        {

            FdpLog<IgApplication>.Warn("[IG] Failed to parse ConfigurationJson: {0}", ex.Message);

        }

    }



    /// <summary>

    /// Pushes a <see cref="CreationTool"/> onto the canvas tool stack.

    /// Guarded by <see cref="_lastPlacementContextId"/> so repeated keep-last

    /// DDS deliveries do not re-activate the tool for the same interaction context.

    /// </summary>

    private void ActivatePlacementTool(long tkbType, ForceId affiliation)

    {

        if (_lastPlacementContextId == _activeContextId)

            return;

        _lastPlacementContextId = _activeContextId;



        if (!_networkEnabled || _createEntityDdsWriter == null)

            return;



        // Pop any existing CreationTool before pushing a new one

        // (prevents tool stack accumulation when IOS sends rapid MapInteractionConfig updates).

        if (_canvas.ActiveTool is CreationTool)

            _canvas.PopTool();



        var writer = new CycloneDdsWriterIgAdapter(_createEntityDdsWriter);

        var tool   = new CreationTool(writer, _geoTransform, tkbType, affiliation);

        _canvas.PushTool(tool);



        FdpLog<IgApplication>.Info(

            "[IG] Placement tool activated. TkbType={0}, Affiliation={1}", tkbType, affiliation);

    }



    // ÔöÇÔöÇ Private adapter ÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇÔöÇ



    /// <summary>

    /// Bridges <see cref="CycloneDDS.Runtime.DdsWriter{T}"/> to the

    /// <see cref="Bagira.IG.Abstractions.IDdsWriter{T}"/> interface required by

    /// <see cref="CreationTool"/>, keeping the IG tool layer free of CycloneDDS

    /// assembly references.

    /// </summary>

    private sealed class CycloneDdsWriterIgAdapter : Bagira.IG.Abstractions.IDdsWriter<CreateEntityRequest>

    {

        private readonly DdsWriter<CreateEntityRequest> _inner;



        public CycloneDdsWriterIgAdapter(DdsWriter<CreateEntityRequest> inner)

            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));



        public void Write(CreateEntityRequest sample) => _inner.Write(sample);

    }

}

