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

using Bagira.IG.Services;

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

using FDP.Toolkit.Vis2D.Tools;

using ImGuiNET;

using FdpEntityInspectorPanel = FDP.Toolkit.ImGui.Panels.EntityInspectorPanel;

using FdpEventBrowserPanel    = FDP.Toolkit.ImGui.Panels.EventBrowserPanel;

using FdpRepositoryAdapter    = FDP.Toolkit.ImGui.Adapters.RepositoryAdapter;

using FdpInspectorState       = FDP.Toolkit.ImGui.Abstractions.InspectorState;

using FDP.Toolkit.ImGui.Utils;

using ModuleHost.Core;

using ModuleHost.Core.Abstractions;

using ModuleHost.Core.Network;

using ModuleHost.Core.Network.Interfaces;

using ModuleHost.Network.Cyclone.Modules;

using DdsIdAllocator = ModuleHost.Network.Cyclone.Services.DdsIdAllocator;

using NodeIdMapper    = ModuleHost.Network.Cyclone.Services.NodeIdMapper;

// Disambiguate StandardInteractionTool: both Bagira.IG.Tools and FDP.Toolkit.Vis2D.Tools define it.
// Use the Bagira.IG variant which exposes OnWorldClick.
using StandardInteractionTool = Bagira.IG.Tools.StandardInteractionTool;

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

    private DdsReader<MapInteractionConfig>?  _configReader;

    /// <summary>
    /// Reads instance-scoped tool-activation commands published by the IOS
    /// via <see cref="MapCommandRequest"/> (preferred over the legacy
    /// <see cref="MapInteractionConfig"/> group-broadcast approach).
    /// </summary>
    private DdsReader<MapCommandRequest>?     _commandReader;

    private Guid                             _activeContextId;

    private bool                             _showGrid;

    private Guid                             _lastPlacementContextId;

    private Guid                             _lastAreaContextId;

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

    // Task 46: track last known map selection so we only push map→inspector
    // when the selection actually changes, and never overwrite a user-chosen
    // inspector selection when the map has nothing selected.
    private Entity                  _fdpLastMapSelection = Entity.Null;

    // Ensures context menu handlers are registered only once.
    private bool                    _fdpContextMenusWired;

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

        // Prevent Raylib from treating ESC as a window-close signal.
        // Map tools handle ESC via IMapTool.HandleKeyPressed routed through MapCanvas,
        // ensuring it is consumed by the active tool and does not bubble to the main loop.
        Raylib.SetExitKey(KeyboardKey.Null);

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



        //  Shared foundation 
        // Registers network replication, geographic, shared definitions, and
        // lifecycle events identically to SimHost (via SimHostComponentRegistry).
        BagiraSharedComponentRegistry.RegisterAll(_world);

        //  IG-specific visualization and display components 
        _world.RegisterComponent<ResolvedStyle>();
        _world.RegisterComponent<CullingState>();
        _world.RegisterComponent<SelectionState>();

        //  IG copies of replicated simulation components 
        // (SimHost owns simulation; IG needs these registered for DDS deserialization
        // and query support, but does not run the associated logic systems.)
        _world.RegisterComponent<VehicleParams>();
        _world.RegisterComponent<IgHealthState>();
        _world.RegisterComponent<Faction>();
        _world.RegisterComponent<PerceptionReceptor>();
        _world.RegisterComponent<TargetMemory>();
        _world.RegisterComponent<WeaponState>();
        _world.RegisterComponent<Health>();
        _world.RegisterComponent<HealthData>();
        _world.RegisterComponent<PhysicsCollider>();

        //  IG Advanced Features components 
        _world.RegisterComponent<HistoryTrail>();
        _world.RegisterComponent<VisualEffectState>();
        _world.RegisterComponent<TracerTarget>();
        _world.RegisterManagedComponent<ContextMenuState>();
        _world.RegisterManagedComponent<EditablePolyline>();
        _world.RegisterComponent<MapOverlayStyle>();
        _world.RegisterComponent<MapDisplayComponent>();
        _world.RegisterManagedComponent<IgEntityData>();

        // SimCombatDef, TkbCompositionDef, VisualData, lifecycle events, and
        // FireInteractionEvent are all handled by BagiraSharedComponentRegistry above.
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

                _commandReader   = new DdsReader<MapCommandRequest>(participant, "MapCommandRequest");

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

                var mapVisualOverlayTranslator = new MapVisualOverlayIngressTranslator(

                    participant, _entityMap, _geoTransform, _ghostCreationSystem);

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

                    mapVisualOverlayTranslator,

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

                _commandReader?.Dispose();

                _createEntityDdsWriter?.Dispose();

                participant?.Dispose();

                _commandGateway = null;

                _clickWriter = null;

                _selectionWriter = null;

                _configReader = null;

                _commandReader = null;

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



        // G2. MapLayerModule — assigns MapDisplayComponent bitmask per entity (time-sliced)

        _kernel.RegisterModule(new MapLayerModule());



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
        // Area-overlay entities are excluded so they render via MapOverlayRenderLayer instead.

        var query = _world.Query()

            .With<NetworkIdentity>()

            .With<SimTransform>()

            .Without<MapOverlayStyle>()

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



        // MapOverlayRenderLayer — draws tactical graphic area overlays.
        // Guards on SimTransform: only renders area entities where geo ingress has already arrived.

        var overlayQuery = _world.Query()

            .WithManaged<EditablePolyline>()

            .With<MapOverlayStyle>()

            .With<SimTransform>()

            .WithLifecycle(EntityLifecycle.All)

            .Build();

        var overlayLayer = new MapOverlayRenderLayer(_world, overlayQuery);

        _canvas.AddLayer(overlayLayer);



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

        // Advertise this IG's capabilities so the IOS can build its layer-control UI.
        if (_networkEnabled && participant != null)
            IgCapabilitiesPublisher.Publish(participant, IgNetworkConstants.InstanceId);

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



        // Task 5a: Poll instance-scoped tool-activation commands (CMD_*) -- preferred path.

        if (_networkEnabled && _commandReader != null)

        {

            using var cmdLoan = _commandReader.Take(1);

            foreach (var cmdSample in cmdLoan)

            {

                if (!cmdSample.IsValid) continue;

                var cmd = cmdSample.Data;

                // Accept only broadcast (MapId==0) or commands addressed to this IG instance.

                if (cmd.MapId != 0 && cmd.MapId != IgNetworkConstants.InstanceId)

                    continue;

                FdpLog<IgApplication>.Debug(

                    "[TRACE-IG] MapCommandRequest: Type={0} MapId={1}", cmd.Type, cmd.MapId);

                switch (cmd.Type)

                {

                    case CommandType.CMD_START_AUTHORING:

                        ParseCommandAndActivateAreaTool(cmd.CommandArgsJson);

                        break;

                    case CommandType.CMD_PLACE_ENTITY:

                        ParseCommandAndActivatePlacementTool(cmd.CommandArgsJson);

                        break;


                    case CommandType.CMD_START_EDITING:

                        ParseCommandAndActivateEditTool(cmd.CommandArgsJson);

                        break;

                }

            }

        }



        // Task 5b: Poll IOS => IG interaction-config updates (legacy -- grid/view toggle).

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

            // Task 43/46: one-directional sync map → FDP inspector.
            // Only update when the map selection actually changes to a real entity.
            // When the map is cleared (Entity.Null) we intentionally do NOT clear
            // the FDP inspector so the user can keep a selection made via the list.
            var fdpSelected = GetSelectedEntity();
            if (fdpSelected != _fdpLastMapSelection)
            {
                _fdpLastMapSelection = fdpSelected;
                if (fdpSelected != Entity.Null)
                    _fdpInspectorState.SelectedEntity = fdpSelected;
            }

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

        if (!_fdpContextMenusWired)
        {
            _fdpContextMenusWired = true;
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                builder.AddItem("Center on entity", () => CenterCameraOn(entity));
                builder.AddItem("Select entity",    () => SelectEntityOnMap(entity));

                // "Edit Overlay" — only shown for area entities that carry an EditablePolyline.
                if (_world.HasManagedComponent<EditablePolyline>(entity)
                 && _entityMap.TryGetNetworkId(entity, out long editNetId))
                {
                    builder.AddSeparator();
                    builder.AddItem("Edit overlay", () => ActivateAreaEditingTool(editNetId));
                }
            }));
        }

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

    /// <summary>
    /// Programmatically selects <paramref name="entity"/> on the map by updating ECS
    /// <see cref="SelectionState"/> components directly — mirroring the path used by
    /// <see cref="StandardInteractionTool"/> when the user clicks on the canvas.
    /// Also updates the FDP inspector and last-known-selection tracker so that the
    /// one-directional chain (map → inspector) does not immediately overwrite this choice.
    /// </summary>
    private void SelectEntityOnMap(Entity entity)
    {
        // 1. Clear all existing ECS selection state.
        var q = _world.Query().With<SelectionState>().Build();
        foreach (var e in q)
        {
            if (_world.IsAlive(e))
                _world.SetComponent(e, new SelectionState { IsSelected = false, IsPrimarySelection = false });
        }

        // 2. Apply selection to the target entity if it is alive.
        if (_world.IsAlive(entity))
            _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        // 3. Keep the FDP inspector and map-selection tracker in sync so that
        //    the per-frame change-detection in DrawUI does not revert this choice.
        _fdpInspectorState.SelectedEntity = entity;
        _fdpLastMapSelection = entity;
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

        _commandReader?.Dispose();

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

    /// Returns <c>true</c> when <see cref="PointSequenceTool"/> is the active map tool.

    /// </summary>

    internal bool TestHook_IsPointSequenceToolActive => _canvas.ActiveTool is PointSequenceTool;



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

    /// Directly drives a <see cref="PointSequenceTool"/> with a list of points and commits

    /// the sequence with a right-click. No-op if the active tool is not a point sequence tool.

    /// </summary>

    internal void TestHook_DirectPointSequenceToolCommit(IReadOnlyList<Vector2> points)

    {

        if (_canvas.ActiveTool is not PointSequenceTool tool)

            return;



        if (points == null || points.Count == 0)

        {

            tool.HandleClick(Vector2.Zero, MouseButton.Right);

            return;

        }



        for (int i = 0; i < points.Count; i++)

        {

            tool.HandleHover(points[i]);

            tool.HandleClick(points[i], MouseButton.Left);

        }



        tool.HandleHover(points[^1]);

        tool.HandleClick(points[^1], MouseButton.Right);

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

        // Permanent identity component — drives GhostPromotionSystem.
        cmd.AddComponent(entity, new TkbIdentity { TkbType = descriptor.TkbType });

        // Store DIS entity type natively in the entity header.
        _world.SetDisType(entity, new DISEntityType { Value = descriptor.DisType });

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

                TkbType    = _world.HasComponent<TkbIdentity>(hit)

                    ? (int)_world.GetComponentRO<TkbIdentity>(hit).TkbType : 0,

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

    // ─── CMD_* command handlers ────────────────────────────────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_START_AUTHORING"/> command.
    /// Extracts <c>contextId</c> and <c>styleOverrideJson</c> from the JSON args,
    /// stores the context ID, then activates the area-authoring point-sequence tool.
    /// </summary>
    private void ParseCommandAndActivateAreaTool(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (root.TryGetProperty("contextId", out var ctxEl)
             && Guid.TryParse(ctxEl.GetString(), out var ctx))
            {
                _activeContextId = ctx;
            }

            string styleJson = string.Empty;
            if (root.TryGetProperty("styleOverrideJson", out var styleEl)
             && styleEl.ValueKind == JsonValueKind.String)
            {
                styleJson = styleEl.GetString() ?? string.Empty;
            }

            ActivateAreaAuthoringTool(styleJson);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ParseCommandAndActivateAreaTool failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_PLACE_ENTITY"/> command.
    /// Extracts <c>contextId</c>, <c>entityType</c>, and <c>affiliation</c> from
    /// the JSON args, stores the context ID, then activates the placement tool.
    /// </summary>
    private void ParseCommandAndActivatePlacementTool(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (root.TryGetProperty("contextId", out var ctxEl)
             && Guid.TryParse(ctxEl.GetString(), out var ctx))
            {
                _activeContextId = ctx;
            }

            long    tkbType = 0;
            ForceId aff     = ForceId.Unknown;

            if (root.TryGetProperty("entityType", out var etEl))
                tkbType = etEl.GetInt64();

            if (root.TryGetProperty("affiliation", out var affEl))
            {
                aff = affEl.GetString() switch
                {
                    "Friend"   => ForceId.Friend,
                    "Hostile"  => ForceId.Hostile,
                    "Neutral"  => ForceId.Neutral,
                    _          => ForceId.Unknown,
                };
            }

            ActivatePlacementTool(tkbType, aff);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ParseCommandAndActivatePlacementTool failed: {0}", ex.Message);
        }
    }




    // ─── EditTool activation from CMD_START_EDITING ──────────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_START_EDITING"/> command.
    /// Extracts <c>contextId</c> and <c>entityId</c> from the JSON args, then
    /// activates the <see cref="EditTool"/> for the specified area entity.
    /// </summary>
    private void ParseCommandAndActivateEditTool(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (root.TryGetProperty("contextId", out var ctxEl)
             && Guid.TryParse(ctxEl.GetString(), out var ctx))
            {
                _activeContextId = ctx;
            }

            long networkEntityId = 0;
            if (root.TryGetProperty("entityId", out var eidEl))
                networkEntityId = eidEl.GetInt64();

            ActivateAreaEditingTool(networkEntityId);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ParseCommandAndActivateEditTool failed: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Activates an <see cref="EditTool"/> for the area entity identified by
    /// <paramref name="networkEntityId"/>.
    ///
    /// <para>
    /// On commit (operator right-clicks to finish editing), the updated
    /// relative-Cartesian vertex list is converted back to relative geo offsets
    /// and published as an <see cref="UpdateEntityDescriptorRequest"/> for
    /// <c>dtMapVisualOverlay</c>, so the SimHost updates its authority copy and
    /// broadcasts the changes.
    /// </para>
    /// </summary>
    private void ActivateAreaEditingTool(long networkEntityId)
    {
        if (!_entityMap.TryGetEntity(networkEntityId, out var entity))
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ActivateAreaEditingTool: entity not found for NetID {0}.", networkEntityId);
            return;
        }

        if (!World.HasManagedComponent<EditablePolyline>(entity))
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ActivateAreaEditingTool: entity {0} has no EditablePolyline.", networkEntityId);
            return;
        }

        // Pop any existing EditTool (prevents stack accumulation on rapid re-activation).
        if (_canvas.ActiveTool is EditTool)
            _canvas.PopTool();

        // EditablePolyline.Points are stored as relative Cartesian offsets from SimTransform.
        // Translate to absolute world space so the EditTool ghost renders at the correct canvas
        // position and mouse hit-testing works with the unmodified world coordinates.
        if (!World.HasComponent<SimTransform>(entity))
        {
            FdpLog<IgApplication>.Warn(
                "[IG] ActivateAreaEditingTool: entity {0} has no SimTransform yet.", networkEntityId);
            return;
        }

        ref readonly var initSimTr = ref World.GetComponentRO<SimTransform>(entity);
        var originOffset = new Vector2(initSimTr.Position.X, initSimTr.Position.Y);
        var editTool = new EditTool(entity, World, originOffset: originOffset);

        editTool.OnPolylineCommitted += (committedEntity, absCartPoints) =>
        {
            // absCartPoints are in absolute world space (originOffset already baked in by EditTool).
            // Convert back to relative Cartesian before storing in ECS.
            ref readonly var simTr = ref World.GetComponentRO<SimTransform>(committedEntity);
            var origin = new Vector2(simTr.Position.X, simTr.Position.Y);

            var relPoints = new List<Vector2>(absCartPoints.Count);
            for (int i = 0; i < absCartPoints.Count; i++)
                relPoints.Add(absCartPoints[i] - origin);
            World.SetManagedComponent(committedEntity, new EditablePolyline { Points = relPoints });

            // Send UpdateEntityDescriptorRequest(dtMapVisualOverlay) with relative geo offsets.
            if (_networkEnabled && _commandGateway != null && _geoTransform != null
             && _entityMap.TryGetNetworkId(committedEntity, out long netId))
            {
                var (refLat, refLon, refAlt) = _geoTransform.ToGeodetic(simTr.Position);

                var relGeoPoints = new List<GeoPosition>(absCartPoints.Count);
                for (int i = 0; i < absCartPoints.Count; i++)
                {
                    var absCart = new Vector3(absCartPoints[i].X, absCartPoints[i].Y, simTr.Position.Z);
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(absCart);
                    relGeoPoints.Add(new GeoPosition
                    {
                        Latitude  = lat - refLat,
                        Longitude = lon - refLon,
                        Altitude  = alt - refAlt,
                    });
                }

                var request = new UpdateEntityDescriptorRequest
                {
                    RequestId      = Guid.NewGuid(),
                    EntityId       = (int)netId,
                    DescriptorType = EDescriptorType.dtMapVisualOverlay,
                    Payload        = new EntityDescriptorUnion
                    {
                        _d = EDescriptorType.dtMapVisualOverlay,
                        MapVisualOverlay = new MapVisualOverlay
                        {
                            EntityId        = (int)netId,
                            PersistenceMode = PersistenceMode.MODE_PERSISTENT,
                            Points          = relGeoPoints,
                            IsEditable      = true,
                            IsClickable     = true,
                        }
                    }
                };

                _commandGateway.SendUpdateDescriptor(request);
                FdpLog<IgApplication>.Info(
                    "[IG] Committed overlay edit for NetID {0}: {1} vertices.", netId, absCartPoints.Count);
            }
        };

        _canvas.PushTool(editTool);
        FdpLog<IgApplication>.Info("[IG] Area editing tool activated for NetID {0}.", networkEntityId);
    }

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

            // view.layers.* → update MapCanvas.ActiveLayerMask
            // Missing keys leave their bits unchanged (forward-compatible with future IOS versions).
            if (root.TryGetProperty("view", out var viewLayersEl)
             && viewLayersEl.TryGetProperty("layers", out var layerFlagsEl))
            {
                uint currentMask = _canvas.ActiveLayerMask;
                foreach (var layerDef in MapLayerRegistry.All)
                {
                    if (layerFlagsEl.TryGetProperty(layerDef.Name, out var layerEl)
                     && (layerEl.ValueKind == JsonValueKind.True
                      || layerEl.ValueKind == JsonValueKind.False))
                    {
                        if (layerEl.GetBoolean())
                            currentMask |=  layerDef.BitMask;
                        else
                            currentMask &= ~layerDef.BitMask;
                    }
                }
                _canvas.ActiveLayerMask = currentMask;
            }



            // interaction.activeTool + toolConfig Ôæå activate canvas tool

            if (root.TryGetProperty("interaction", out var interactionEl)

             && interactionEl.TryGetProperty("activeTool", out var toolEl))

            {

                string? toolName = toolEl.GetString();

                if (toolName == "PLACEMENT"

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

                else if (toolName == "AREA_AUTHORING")

                {

                    string styleJson = string.Empty;

                    if (interactionEl.TryGetProperty("toolSettings", out var toolSettingsEl)

                     && toolSettingsEl.TryGetProperty("styleOverrideJson", out var styleEl)

                     && styleEl.ValueKind == JsonValueKind.String)

                    {

                        styleJson = styleEl.GetString() ?? string.Empty;

                    }

                    ActivateAreaAuthoringTool(styleJson);

                }

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



    /// <summary>

    /// Pushes a <see cref="PointSequenceTool"/> onto the canvas tool stack for area authoring.

    /// Guarded by <see cref="_lastAreaContextId"/> so repeated keep-last DDS deliveries do not

    /// re-activate the tool for the same interaction context.

    /// </summary>

    private void ActivateAreaAuthoringTool(string styleJson = "")

    {

        if (_lastAreaContextId == _activeContextId)

            return;

        _lastAreaContextId = _activeContextId;



        if (!_networkEnabled || _createEntityDdsWriter == null)

            return;



        if (_canvas.ActiveTool is PointSequenceTool)

            _canvas.PopTool();



        var tool = new PointSequenceTool(points =>

        {

            if (points.Length < 3)

            {

                _canvas.PopTool();

                return;

            }



            // Compute absolute geo positions for all drawn points.

            var absPositions = new List<(double Lat, double Lon, double Alt)>(points.Length);

            for (int i = 0; i < points.Length; i++)

            {

                double lat, lon, alt;

                if (_geoTransform != null)

                {

                    (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(points[i].X, points[i].Y, 0f));

                }

                else

                {

                    lat = points[i].Y;

                    lon = points[i].X;

                    alt = 0.0;

                }

                absPositions.Add((lat, lon, alt));

            }



            // Centroid (reference point) = arithmetic mean of absolute positions.

            double refLat = 0.0, refLon = 0.0, refAlt = 0.0;

            for (int i = 0; i < absPositions.Count; i++)

            {

                refLat += absPositions[i].Lat;

                refLon += absPositions[i].Lon;

                refAlt += absPositions[i].Alt;

            }

            refLat /= absPositions.Count;

            refLon /= absPositions.Count;

            refAlt /= absPositions.Count;



            // Store vertices as RELATIVE geo offsets from the centroid (reference point).

            // The overlay ingress translator converts these to relative Cartesian offsets.

            var relGeoPoints = new List<GeoPosition>(absPositions.Count);

            for (int i = 0; i < absPositions.Count; i++)

            {

                relGeoPoints.Add(new GeoPosition

                {

                    Latitude  = absPositions[i].Lat - refLat,

                    Longitude = absPositions[i].Lon - refLon,

                    Altitude  = absPositions[i].Alt - refAlt,

                });

            }



            var request = new CreateEntityRequest

            {

                RequestId = Guid.NewGuid(),

                Owner     = default,

                Flags     = 0,

                InitialDescriptors = new List<EntityDescriptorUnion>

                {

                    new EntityDescriptorUnion

                    {

                        _d           = EDescriptorType.dtEntityMaster,

                        EntityMaster = new EntityMaster { TkbType = TkbEntityTypes.TacGraphic_Area }

                    },

                    // Reference point: the entity's geographic position (centroid of the drawn polygon).

                    // GeoSpatial ingress will set SimTransform to this position.

                    new EntityDescriptorUnion

                    {

                        _d         = EDescriptorType.dtGeoSpatial,

                        GeoSpatial = new GeoSpatial

                        {

                            Pos = new GeoPosition

                            {

                                Latitude  = refLat,

                                Longitude = refLon,

                                Altitude  = refAlt,

                            }

                        }

                    },

                    // Overlay: vertices stored as RELATIVE geo offsets from the reference point.

                    new EntityDescriptorUnion

                    {

                        _d = EDescriptorType.dtMapVisualOverlay,

                        MapVisualOverlay = new MapVisualOverlay

                        {

                            PersistenceMode   = PersistenceMode.MODE_PERSISTENT,

                            Points            = relGeoPoints,

                            IsEditable        = true,

                            IsClickable       = true,

                            StyleOverrideJson = styleJson,

                        }

                    }

                }

            };



            _createEntityDdsWriter.Write(request);

            _canvas.PopTool();

        });



        _canvas.PushTool(tool);



        FdpLog<IgApplication>.Info("[IG] Area authoring tool activated.");

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

