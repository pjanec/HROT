using System;

using System.Collections.Generic;

using System.Numerics;

using System.Threading.Tasks;

using System.Text.Json;

using CarKinem.Core;

using Hrot.ScenarioEditor.Adapters;

using Hrot.ScenarioEditor.Rendering;

using Hrot.IG.Components;

using Hrot.IG.Modules;

using Hrot.IG.Services;

using Hrot.IG.Systems;

using Hrot.Common.Systems;

using Hrot.ScenarioEditor.Tools;

using Hrot.IG.UI;

using Hrot.Map.Common;

using Hrot.Map.Common.Events;

using Hrot.Map.Definitions.Tkb;

using Hrot.Core.Network;

using CycloneDDS.Runtime;

using CycloneDDS.Runtime.Tracking;

using Fdp.Core;

using Fdp.Modules.Geographic.Components;

using Fdp.Modules.Geographic.Systems;

using Fdp.Modules.Geographic.Transforms;

using Fdp.Toolkit.Lifecycle;

using Fdp.Core.Logging;

using Fdp.Toolkit.Combat.Components;

using Fdp.Toolkit.Lifecycle.Events;

using Fdp.Toolkit.Perception.Components;

using Fdp.Toolkit.Physics.Components;

using Fdp.Toolkit.NetworkSpawning.Events;

using Fdp.Toolkit.NetworkSpawning.Systems;

using Fdp.Toolkit.Replication;

using Fdp.Toolkit.Replication.Components;

using Fdp.Toolkit.Replication.Patching;

using Fdp.Toolkit.Replication.Services;

using Fdp.Toolkit.Replication.Systems;

using Hrot.Common.Orchestration;
using Hrot.Common.Infrastructure;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

using Fdp.Toolkit.Time.Controllers;

using Fdp.Toolkit.Vis2D;

using Fdp.Toolkit.Vis2D.Abstractions;

using Fdp.Toolkit.Vis2D.Components;

using Fdp.Toolkit.Vis2D.Defaults;

using Fdp.Toolkit.Vis2D.Layers;

using Fdp.Toolkit.Vis2D.Tools;

using ImGuiNET;

using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;

using FdpEventBrowserPanel    = Fdp.Presentation.Panels.EventBrowserPanel;

using FdpRepositoryAdapter    = Fdp.Presentation.Adapters.RepositoryAdapter;

using FdpInspectorState       = Fdp.Presentation.Abstractions.InspectorState;

using Fdp.Presentation.Utils;

using Fdp.ModuleHost;

using Fdp.ModuleHost.Abstractions;

using Fdp.Toolkit.Replication;

using Fdp.Network.Cyclone.Modules;

using Fdp.Network.Cyclone.Systems;

using DdsIdAllocator = Fdp.Network.Cyclone.Services.DdsIdAllocator;

using NodeIdMapper    = Fdp.Network.Cyclone.Services.NodeIdMapper;

// Disambiguate StandardInteractionTool: both Hrot.IG.Tools and FDP.Toolkit.Vis2D.Tools define it.
// Use the Hrot.IG variant which exposes OnWorldClick.
using StandardInteractionTool = Hrot.ScenarioEditor.Tools.StandardInteractionTool;

using Raylib_cs;

using rlImGui_cs;



namespace Hrot.IG;



/// <summary>

/// Main application shell for the IG Mock. Owns the Raylib window, MapCanvas, and camera.

/// </summary>

public class IgApplication : IDisposable

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



    // -- Network enabled flag — false when DDS libraries are unavailable (e.g. unit-test host)

    private bool _networkEnabled;

    // -- ClusterSlave (CGF1-S0104 / CMC-S016) — wired in InitializeNetwork ------
    private Fdp.Toolkit.Orchestration.ClusterSlave? _clusterSlave;
    // CMC-S016: orchestration bus + slave translator (Option C).
    private Fdp.Core.FdpEventBus?                             _igOrchestrationBus;
    private Hrot.Common.Orchestration.NodeOpSlaveTranslator?    _igSlaveTranslator;

    // ── HrotNodeBuilder infrastructure context (EAM-M002) ─────────────────────
    private HrotNodeContext? _context;



    // -- Headless flag — set by InitializeEmbedded(); skips all Raylib/ImGui calls in Update/Draw

    private bool _headless;

    // -- Optional IG translator provider (injected via InitializeEmbedded; null = no NED translators)
    private Hrot.Core.Network.IIgTranslators? _igTranslatorsProvider;

    // -- Optional network factory injected from composition root
    private Hrot.Core.Network.INetworkFactory? _networkFactory;



    // -- Optional domain override (tests) -------------------------------------

    private int? _domainOverride;

    // -- Optional node-id override (multi-instance support) -------------------

    private int _nodeIdOverride;

    /// <summary>
    /// Effective DDS instance ID for this IG process.
    /// When <c>_nodeIdOverride</c> is non-zero it is used directly; otherwise falls
    /// back to <see cref="IgNetworkConstants.InstanceId"/> (legacy constant = 300).
    /// Computed once in <see cref="InitializeEmbedded"/> and reused at runtime.
    /// </summary>
    private int _effectiveInstanceId;

    // -- Task 5: IG-to-ExCon event translator state ----------------------------------------------

    private WGS84Transform?                  _geoTransform;

    // Protocol-neutral adapter wrapping all IG DDS writers and readers (Task 18).
    private Hrot.Core.Network.IIgNetworkAdapter? _networkAdapter;

    /// <summary>
    /// Neutral command gateway for entity creation, descriptor updates, and mission control.
    /// Obtained from <see cref="_networkAdapter"/> at initialization time.
    /// Injectable via <see cref="TestHook_SetCommandGateway"/> in unit tests.
    /// </summary>
    private Hrot.Core.Network.ICommandGateway?   _commandGateway;

    private Guid                             _activeContextId;

    private bool                             _showGrid;

    private Guid                             _lastPlacementContextId;

    private Guid                             _lastAreaContextId;

    /// <summary>Guard that prevents re-activating the location-picker for the same context ID.</summary>
    private Guid _lastPickLocationContextId;

    /// <summary>Guard that prevents re-activating the entity-picker for the same context ID.</summary>
    private Guid _lastPickEntityContextId;

    /// <summary>
    /// Factory compiled once after the ECS world is created; translates layer-preset
    /// strings (e.g. <c>"road_graphs"</c>) into allocation-free <see cref="IEntityFilter"/> instances.
    /// </summary>
#pragma warning disable CS0649 // field assigned by future DI wiring
    private HrotEntityFilterFactory? _entityFilterFactory;
#pragma warning restore CS0649

    // _createEntityDdsWriter removed by D005 — SpawnEntityCommandEgressTranslator owns the DDS writer.

    /// <summary>
    /// Test-only callback: when non-null, receives every <see cref="SpawnEntityCommand"/>
    /// that would normally be forwarded to <see cref="_mapCommandController"/> or published on the bus.
    /// Only set by <see cref="TestHook_SetSpawnCommandSink"/>.
    /// </summary>
    private Action<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>? _testSpawnCommandSink;
    /// <summary>
    /// Orchestrator that manages tool-activation sessions for map commands
    /// (<c>CMD_PLACE_ENTITY</c>, <c>CMD_START_AUTHORING</c>) and routes
    /// entity-lifecycle ACKs back to ExCon.
    /// </summary>
    private MapCommandController?               _mapCommandController;

    /// <summary>
    /// Edge compiler built once at initialisation time and injected into every
    /// <see cref="Hrot.IG.Tools.CreationTool"/> created by
    /// <see cref="Hrot.IG.Systems.MapCommandController"/>.
    /// Converts placement JSON property blobs to binary attribute records on the DDS wire (ATTR2-DEBT-07).
    /// </summary>
    private JsonToRecordCompiler? _edgeCompiler;

    /// <summary>
    /// Cached query for all entities that carry <see cref="SelectionState"/>.
    /// Built once during canvas setup and reused in <see cref="OnCanvasWorldClick"/>
    /// to avoid per-click allocations (CT-2).
    /// </summary>
    private EntityQuery? _selectionStateQuery;

    // -- Drag tracking: world-space drop position set by OnEntityMoved --------------------------

    private System.Numerics.Vector2          _lastDragWorldPos;

    /// <summary>
    /// Accumulated time (seconds) since the last throttled network update during a drag.
    /// Reset to 0 on each throttled send and on drag end.
    /// Only active when <see cref="MapUserConfig.ContinuousDragUpdates"/> is <c>true</c>.
    /// </summary>
    private float _continuousDragTimer;

    /// <summary>
    /// Frame delta-time captured at the start of <see cref="Update"/> and used inside
    /// the <c>OnEntityMoved</c> drag handler to accumulate the throttle timer.
    /// </summary>
    private float _frameDt;

    /// <summary>Throttle interval for continuous drag network updates (10 Hz).</summary>
    private const float ContinuousDragIntervalSec = 0.1f;

    // -- Style and culling objects ÔÇö updated and injected into modules

    private MapUserConfig     _userConfig     = null!;

    private MapCameraViewport _cameraViewport = null!;



    // -- ImGui UI panels (TASK-IF008) ------------------------------------------

    private DebugPanelState       _debugPanelState   = null!;

    private IgDebugPanel          _debugPanel        = null!;

    private EntityInspectorState  _inspectorState    = null!;

    private EntityInspectorPanel  _inspectorPanel    = null!;
    private WaypointEditorPanel    _waypointEditorPanel = null!;

    private MiniExConPanelState     _miniIosState      = null!;

    private MiniExConPanel          _miniIosPanel      = null!;

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

    /// <summary>
    /// When <c>true</c>, the IG panels are registered as ManagedWindows with the
    /// application Window Manager and <see cref="DrawUI"/> skips calling their
    /// individual <c>Draw()</c> methods to avoid duplicate rendering.
    /// Set this via <see cref="SetPanelsWindowManaged"/> from
    /// <c>IgSubsystem.RegisterWindows</c>.
    /// </summary>
    private bool _panelsWindowManaged;

    /// <summary>
    /// Signals that the IG panels have been registered with a Window Manager.
    /// After this call <see cref="DrawUI"/> only processes popups (context menus,
    /// vertex menus) so that the Window Manager can render the panels without
    /// duplicates.
    /// </summary>
    public void SetPanelsWindowManaged() => _panelsWindowManaged = true;

    // ── Public panel accessors for window-manager registration ────────────────
    // Exposed so IgSubsystem can create ManagedWindow wrappers without needing
    // access to IgApplication's private fields.

    /// <summary>The IG debug panel (FPS counters, render overrides).</summary>
    public IgDebugPanel          DebugPanel          => _debugPanel;
    /// <summary>The IG entity properties panel.</summary>
    public EntityInspectorPanel  EntityPropertiesPanel => _inspectorPanel;
    /// <summary>The IG waypoint editor panel.</summary>
    public WaypointEditorPanel   WaypointEditorPanel  => _waypointEditorPanel;
    /// <summary>The IG Mini ExCon spawner panel.</summary>
    public MiniExConPanel        MiniExConPanel       => _miniIosPanel;
    /// <summary>The IG performance overlay panel.</summary>
    public PerformanceOverlay    PerformanceOverlay   => _performanceOverlay;
    /// <summary>The FDP entity inspector panel.</summary>
    public FdpEntityInspectorPanel FdpEntityInspector => _fdpEntityInspector;
    /// <summary>The FDP event browser panel.</summary>
    public FdpEventBrowserPanel  FdpEventBrowser      => _fdpEventBrowser;
    /// <summary>Getter for the FDP repository adapter (available after first DrawUI call).</summary>
    public FdpRepositoryAdapter? GetFdpRepoAdapter() => _fdpRepoAdapter;
    /// <summary>The FDP inspector state (selection tracking).</summary>
    public FdpInspectorState     FdpInspectorState    => _fdpInspectorState;



    // -- Context menu state ---------------------------------------------------

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

    public void InitializeEmbedded(bool headless = false, int? domainIdOverride = null, int nodeIdOverride = 0,
        Hrot.Core.Network.IIgTranslators? igTranslatorsProvider = null,
        Hrot.Core.Network.INetworkFactory? networkFactory = null)

    {

        _headless = headless;

        _domainOverride     = domainIdOverride;

        _nodeIdOverride     = nodeIdOverride;

        _effectiveInstanceId = nodeIdOverride != 0 ? nodeIdOverride : IgNetworkConstants.InstanceId;

        _igTranslatorsProvider = igTranslatorsProvider ?? networkFactory?.CreateIgTranslators();
        _networkFactory        = networkFactory;

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

        // ── Build core ECS infrastructure (EAM-M002) ─────────────────────────
        // Create participant in the Application Shell (Composition Root) before calling
        // HrotNodeBuilder. Rule: only the outermost executable may instantiate DdsParticipant.
        // HrotNodeBuilder no longer has a fallback.
        var shellParticipant = _networkFactory?.Participant;
        if (shellParticipant == null)
        {
            int igDomainId = _domainOverride ?? IgNetworkConstants.DdsDomain;
            shellParticipant = HrotEnvironment.CreateParticipant(igDomainId);
            shellParticipant.EnableSenderTracking(new SenderIdentityConfig
            {
                AppDomainId   = igDomainId,
                AppInstanceId = _effectiveInstanceId,
            });
        }
        var igConfig = new HrotNodeConfig
        {
            DomainId              = _domainOverride ?? IgNetworkConstants.DdsDomain,
            NodeId                = _effectiveInstanceId,
            Headless              = false,             // always live since we always have a participant
            ExternalParticipant   = shellParticipant,
            SubsystemName         = "IgApplication",
            // IG creates its own DdsIdAllocator in InitializeNetwork; skip the builder's routing wait.
            SkipAllocatorRouting  = true,
        };
        _context = new HrotNodeBuilder(igConfig)
            .WithRole("IgApplication", Hrot.Common.NodeRole.ImageGenerator)
            .WithNetworkFactory(_networkFactory)
            .Build();

        // Create replication module via factory (prevents direct NED reference in IG).
        // When no factory is available (unit-test path), replication runs headless: no ghosts.
        // Use ConfigureForNode(_context) so the replication module shares the same entityMap and bus as _context.
        var igNodeFactory = _networkFactory?.ConfigureForNode(_context, Hrot.Common.NodeRole.ImageGenerator);
        var igReplicationModule = igNodeFactory?.CreateReplicationModule();
        if (igReplicationModule != null)
        {
            _context = _context with
            {
                NedReplication      = igReplicationModule as Hrot.Common.Abstractions.INedReplicationModule,
                GhostCreationSystem = igReplicationModule.GhostCreationSystem,
            };
        }

        _world     = _context.World;
        _entityMap = _context.EntityMap;
        _kernel    = _context.Kernel;

        //  Shared foundation 
        // Registers network replication, geographic, shared definitions, and
        // lifecycle events identically to SimHost (via SimHostComponentRegistry).
        HrotSharedComponentRegistry.RegisterAll(_world);

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
        _world.RegisterComponent<PhysicsCollider>();

        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.ActiveMissionPlan>();

        //  IG Advanced Features components 
        _world.RegisterComponent<HistoryTrail>();
        _world.RegisterComponent<VisualEffectState>();
        _world.RegisterComponent<TracerTarget>();
        _world.RegisterManagedComponent<ContextMenuState>();
        _world.RegisterManagedComponent<EditablePolyline>();
        _world.RegisterComponent<MapOverlayStyle>();
        _world.RegisterComponent<MapDisplayComponent>();
        _world.RegisterComponent<Components.EntityInfo>();

        // ── Route planning components (ROUTES1) ───────────────────────────────
        _world.RegisterManagedComponent<Hrot.Map.Common.Components.RoutePlan>();
        _world.RegisterComponent<Hrot.Map.Common.Components.PersonalRouteRef>();
        _world.RegisterComponent<Hrot.Map.Common.Components.RouteTrajectoryCache>();

        // ── Ground clamping components (MOD1-P7T2) ────────────────────────────
        // Registered unconditionally so they are available even when
        // IgGroundClampingModule is not installed (e.g. 2D-only deployments).
        _world.RegisterComponent<Fdp.Modules.Geographic.Components.GroundClampingConfig>();
        _world.RegisterComponent<Fdp.Modules.Geographic.Components.GroundClampingState>();

        // SimCombatDef, TkbCompositionDef, VisualData, lifecycle events, and
        // FireInteractionEvent are all handled by HrotSharedComponentRegistry above.
        _userConfig     = new MapUserConfig();

        _cameraViewport = new MapCameraViewport();



        // -- ImGui UI panels (TASK-IF008) -------------------------------------

        _debugPanelState    = new DebugPanelState(_userConfig);

        _debugPanel         = new IgDebugPanel(_debugPanelState);

        _inspectorState     = new EntityInspectorState();

        _inspectorPanel     = new EntityInspectorPanel(_inspectorState);

        _waypointEditorPanel = new WaypointEditorPanel(_canvas);

        _miniIosState       = new MiniExConPanelState(_effectiveInstanceId);

        _miniIosPanel       = new MiniExConPanel(_miniIosState, _world.Bus);

        _performanceMetrics = new PerformanceMetrics();

        _performanceOverlay = new PerformanceOverlay(_performanceMetrics);

        _contextMenuSystem  = new ContextMenuSystem();

        _contextMenuPanel   = new ContextMenuPanel(_world, _contextMenuSystem, HandleContextMenuAction);



        _mapContextEntity = _world.CreateEntity();

        _world.AddComponent(_mapContextEntity, new NetworkIdentity(0));

        // ── ATTR2-DEBT-07: Build edge compiler once, shared across all CreationTool instances ──
        // Registers the same five paths used by AttributeCompilerFactory.BuildEdgeCompiler()
        // in Hrot.SimHost so the JSON→Binary schema stays in sync on both ends of the wire.
        _edgeCompiler = new JsonToRecordCompilerBuilder()
            .Register("Name",                  AttributeIds.Name,        AttributeValueKind.String)
            .Register("Affiliation",           AttributeIds.Affiliation,  AttributeValueKind.String)
            .Register("GeoPosition.Latitude",  AttributeIds.GeoLat,      AttributeValueKind.Float64)
            .Register("GeoPosition.Longitude", AttributeIds.GeoLon,      AttributeValueKind.Float64)
            .Register("GeoPosition.Altitude",  AttributeIds.GeoAlt,      AttributeValueKind.Float64)
            .Build();

    }



    /// <summary>

    /// Registers all modules and sets up the DDS participant (unless <paramref name="enableNetwork"/>

    /// is <c>false</c>).  Call after <see cref="InitializeEcs"/>.

    /// </summary>

    private void InitializeNetwork(bool enableNetwork, int? domainIdOverride)

    {

        _networkEnabled = enableNetwork;



        var domainId = domainIdOverride ?? IgNetworkConstants.DdsDomain;

        var tkb = HrotEnvironment.CreateTkb();
        _world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);

        // ── Register base infrastructure modules from builder context ─────────
        // BaseModules = [EntityLifecycleModule, GeographicModule].
        // NedReplicationModule's GhostPromotionSystem uses the same elm instance.
        foreach (var baseModule in _context!.BaseModules)
            _kernel.RegisterModule(baseModule);

        // Assign GhostCreationSystem from the replication module (populated in InitializeEcs).
        // When context was built headless without a factory, GhostCreationSystem may be null;
        // it will be set after BindReplicationParticipant in the headless path below.
        _ghostCreationSystem = _context.GhostCreationSystem;

        DdsParticipant? participant = null;

        List<Fdp.Interfaces.IDescriptorTranslator>? customTranslators = null;

        _networkEnabled = false;

        // GeoTransform is pure math — create it unconditionally so that
        // SendGeoSpatialUpdate works even in tests that skip DDS initialisation.
        _geoTransform = HrotEnvironment.CreateGeoTransform();
        _miniIosState.SetGeoTransform(_geoTransform);

        if (enableNetwork)

        {
                // Use the participant provided by the composition root via HrotNodeBuilder.
                // The subsystem accepts whatever participant it is given (Rule 3, modular-2 DESIGN.md).
                // If null, the node operates without DDS (offline / pure-domain test path).
                participant = _context?.Participant;

                if (participant == null)
                {
                    // No participant available (non-headless but no context) -- skip DDS setup.
                    customTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>();
                }
                else
                {

                // Task 5 / Task 18: Create the protocol-neutral network adapter and obtain gateway from it.
                // Use the injected factory when available; fall back to a null no-op adapter when no factory.
                _networkAdapter = _networkFactory != null
                    ? _networkFactory.CreateIgNetworkAdapter(participant, _effectiveInstanceId)
                    : Hrot.Core.Network.NullIgNetworkAdapter.Instance;
                _commandGateway = _networkAdapter.CommandGateway;



                // EntityStatesIngressPack (EntityMaster, GeoSpatial, EntityInfo, EntityDamage,
                // MapVisualOverlay, MapRoute) is now handled by NedReplicationModule.
                // Only non-pack translators remain in customTranslators.
                customTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>();

                // CGF1-A.1: Bridge SwitchTimeModeEvent for distributed time-mode switching.
                // Must use _context.EventBus (the same bus as SlaveSyncController),
                // NOT _world.Bus which the kernel swaps independently.
                var igTimeBus = _context!.EventBus;
                customTranslators.Add(
                    Fdp.Toolkit.Time.TimeNetworkModule.CreateDescriptorTranslator(participant, igTimeBus));

                // NTP slave sync: receive TimeSyncRequest/Response from master, publish into bus.
                customTranslators.Add(
                    Fdp.Toolkit.Time.TimeNetworkModule.CreateSlaveTimeSyncTranslator(participant, igTimeBus, _effectiveInstanceId));

                // Bridge FrameOrder/FrameAck for distributed lockstep stepping so IG sends
                // its step ACK back to the Orchestrator on every Step() frame.
                customTranslators.Add(
                    Fdp.Toolkit.Time.TimeNetworkModule.CreateSlaveLockstepTranslator(participant, igTimeBus, _effectiveInstanceId));

                if (_igTranslatorsProvider != null)
                {
                    foreach (var t in _igTranslatorsProvider.GetTranslators(
                        participant, _entityMap, _world.Bus, _ghostCreationSystem, _effectiveInstanceId, _headless))
                    {
                        customTranslators.Add(t);
                    }
                }

                // D005: ACL egress translators convert bus events back to DDS.
                // Created via network factory to avoid direct NED type references in IG.
                if (_networkFactory != null)
                {
                    foreach (var t in _networkFactory.CreateIgEgressTranslators(
                        participant, _world.Bus, _geoTransform!, _effectiveInstanceId))
                        customTranslators.Add(t);
                }

                // Create the MapCommandController now that canvas and DDS resources are ready.
                // D004: MapCommandController now takes FdpEventBus instead of IDdsWriter<CreateEntityRequest>.
                // SpawnEntityCommandEgressTranslator (D005) handles DDS writes for entity creation.
                _mapCommandController = new MapCommandController(
                    _canvas,
                    _world.Bus,
                    dto => _networkAdapter?.WriteMapCommandAck(dto),
                    _effectiveInstanceId);

                _contextMenuSystem.SetCacheMissWriter(
                    (reqId, mapId, sel) => _networkAdapter?.WriteContextMenuRequest(reqId, mapId, sel),
                    _effectiveInstanceId);

                _networkEnabled = true;

                // CGF1-S0104: wire ClusterSlave once DDS participant is confirmed healthy.
                // Use _effectiveInstanceId (= _nodeIdOverride when set, else IgNetworkConstants.InstanceId=300)
                // so the IG ClusterSlave always registers on a cluster-unique node ID.
                // Using IgNetworkConstants.LocalNodeId (1) caused collision with SimHost when --node-id 0.
                var igNodeId = _effectiveInstanceId;

                // CMC-S016: each slave subsystem has its own orchestration bus + translator (Option C).
                _igOrchestrationBus = new Fdp.Core.FdpEventBus();
                _igSlaveTranslator  = new Hrot.Common.Orchestration.NodeOpSlaveTranslator(
                    commandReader:   new CycloneDDS.Runtime.DdsReader<Hrot.NED.Descriptors.Orchestration.NodeOpCommand>(participant!),
                    statusWriter:    new CycloneDDS.Runtime.DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeOpStatus>(participant!),
                    heartbeatWriter: new CycloneDDS.Runtime.DdsWriter<Hrot.NED.Descriptors.Orchestration.NodeHeartbeat>(participant!),
                    bus:             _igOrchestrationBus,
                    nodeId:          igNodeId);
                _clusterSlave = new Fdp.Toolkit.Orchestration.ClusterSlave(
                    igNodeId, "IG", _igOrchestrationBus);

                // CGF1-BATCH-23 A.2: IG participates in recording/replay cluster operations as a
                // listen-only node.  Shared controller tracks IsReplayActive so the
                // Live-from-Replay branch (CGF1-S0305) is correctly gated.
                var igRrController = new Hrot.Common.Orchestration.ListenerRecordReplayController("IG");

                // Wire ReferenceReplayLoadHandler FIRST (PrepareReplay / FinalizeReplay
                // unconditional; PrepareLive only when replay active).
                _clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler(
                    igRrController,
                    simGroup:              null,
                    lifecycleGroup:        null,
                    bypassLifecycleToggle: null,
                    storageDirectory:      @"C:\FDP_Temp"));

                // Wire ReferenceLiveLoadHandler: ACKs cold PrepareLive and FinalizeLive
                // without recording (IG carries no ECS frame data).
                _clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler(
                    checkpointWorker: null,
                    controller:       igRrController,
                    storageDirectory: @"C:\FDP_Temp"));

                // CGF1-BATCH-23 A.2: dummy zone handler — IG acknowledges
                // PrepareZone / CommitZone without terrain DB load.
                // Full terrain-DB preload from scenario entities is future work.
                _clusterSlave.RegisterHandler(new Hrot.IG.Modules.Orchestration.IgZoneDummyHandler(_effectiveInstanceId));

                // Wire ReferencePrefetchHandler so IG can stage scenario files and ACK.
                var igStorageProvider = new Fdp.Toolkit.Orchestration.LocalDiskStorageProvider(@"C:\FDP_Temp");
                _clusterSlave.RegisterHandler(new Fdp.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler(
                    igStorageProvider));

                // CGF1-S0309: wire dry-run snapshot/rewind handler (IG carries no ECS state in ClusterSlave).
                _clusterSlave.RegisterHandler(new ReferencePreviewHandler(liveRepo: null));

        } // end else (participant != null)

        } // end if (enableNetwork)



        // B. Ghost destruction — replaces SpawningModule so IG does not duplicate entities.
        // SpawnEntityCommand is forwarded to SimHost via SpawnEntityCommandEgressTranslator;
        // SimHost creates the authoritative ghost which DDS replicates back.
        // GhostDestructionSystem tears down those ghosts on EntityMaster DISPOSE.
        _kernel.RegisterGlobalSystem(new GhostDestructionSystem(_entityMap));



        // E. StyleResolutionModule ÔÇö writes ResolvedStyle each Simulation tick

        _kernel.RegisterModule(new StyleResolutionModule(_userConfig, _effectiveInstanceId));



        // F. MapCullingModule ÔÇö writes CullingState each PostSimulation tick

        _kernel.RegisterModule(new MapCullingModule(_cameraViewport));



        // G2. MapLayerModule — assigns MapDisplayComponent bitmask per entity (time-sliced)

        _kernel.RegisterModule(new MapLayerModule());



        // G. HistoryTrailModule ÔÇö records entity position trails (IG.4.1)

        _kernel.RegisterModule(new HistoryTrailModule());



        // H. EventEffectModule ÔÇö spawns and cleans up visual effects (IG.4.2)

        if (!_headless)

            _kernel.RegisterModule(new EventEffectModule());



        // C. IG-specific custom translators (context-actions, time-sync, IG presentation, commands).
        // EntityStatesIngressPack (EntityMaster, GeoSpatial, EntityInfo, EntityDamage,
        // MapVisualOverlay, MapRoute) AND FireInteractionEvent are handled by NedReplicationModule.
        // Only non-pack IG-domain translators remain here, registered via direct systems.
        if (_networkEnabled && participant != null && customTranslators != null)
        {
            _kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(customTranslators.ToArray()));
            _kernel.RegisterGlobalSystem(new CycloneEgressSystem(customTranslators.ToArray()));
            _kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(customTranslators));
        }



        // D. EntityRenderLayer wired to the StubVisualizerAdapter
        // Area-overlay entities are excluded so they render via MapOverlayRenderLayer instead.
        // Route entities are excluded so they render via RouteRenderLayer instead.

        var query = _world.Query()

            .With<NetworkIdentity>()

            .With<SimTransform>()

            .Without<MapOverlayStyle>()

            .WithoutManaged<Hrot.Map.Common.Components.RoutePlan>()

            .WithLifecycle(EntityLifecycle.All)

            .Build();



        var adapter   = new NedVisualizerAdapter();

        var selection = new DefaultSelectionState();

        var layer     = new EntityRenderLayer(

            "Entities", layerBitIndex: -1,

            _world, query, adapter, selection) { Canvas = _canvas };

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

        var missionLayer = new MissionRenderLayer(_world, _geoTransform);
        _canvas.AddLayer(missionLayer);

        // RouteRenderLayer — draws RoutePlan waypoints for TacGraphic_Route entities.
        var routeQuery = _world.Query()
            .With<TkbIdentity>()
            .WithManaged<Hrot.Map.Common.Components.RoutePlan>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();
        var routeRenderLayer = new RouteRenderLayer(_world, routeQuery, _fdpInspectorState);
        _canvas.AddLayer(routeRenderLayer);

        // Cache SelectionState query once to avoid per-click allocations (CT-2).
        _selectionStateQuery = _world.Query()
            .With<SelectionState>()
            .WithLifecycle(EntityLifecycle.All)
            .Build();


        // StandardInteractionTool ÔÇö default canvas tool wiring selection to ECS.

        var interactionTool = new StandardInteractionTool(_world, query, adapter, selection);

        _canvas.SwitchTool(interactionTool);



        interactionTool.OnWorldClick += OnCanvasWorldClick;



        // Task 5: Wire IG-to-ExCon event translators when DDS participant is ready.

        if (_networkEnabled)

        {

            interactionTool.OnWorldClick += (worldPos, button, shift, ctrl, hit) => OnCanvasClicked(worldPos, button, shift, ctrl, hit, true);

            interactionTool.OnEntityDragEnd += OnEntityDragEnded;

            // Track world-space drag position and drive continuous-drag throttle timer.
            interactionTool.OnEntityMoved += (entity, worldPos) =>
            {
                bool isShiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift)
                                || Raylib.IsKeyDown(KeyboardKey.RightShift);

                if (_userConfig.ContinuousDragUpdates)
                {
                    // Existing throttle path: keep unchanged.
                    _continuousDragTimer += _frameDt;
                    if (_continuousDragTimer >= ContinuousDragIntervalSec)
                    {
                        SendGeoSpatialUpdate(entity, worldPos);
                        _continuousDragTimer = 0f;
                    }
                }
                else if (isShiftHeld && _lastDragWorldPos != worldPos)
                {
                    // Shift-held path: bypass throttle, send immediately if position changed.
                    SendGeoSpatialUpdate(entity, worldPos);
                }

                _lastDragWorldPos = worldPos;
            };

            _miniIosPanel.SetGateway(_commandGateway);

        }



        // E. SlaveSyncController — unified slave that handles Continuous/Stepping transitions.
        // Must use _context!.EventBus (the same bus as the time translators above),
        // NOT _world.Bus which is swapped internally by the kernel and carries ECS events.
        var timeController = new SlaveSyncController(_context!.EventBus, _effectiveInstanceId);

        _kernel.SetTimeController(timeController);



        // DeadReckoningSyncSystem is now registered by NedReplicationModule (driveFromNetwork:true).
        // Register NedReplicationModule before kernel init so EntityStatesIngressPack + DR are wired.
        if (_context?.NedReplication != null)
            _context.Kernel.RegisterModule(_context.NedReplication);

        _kernel.RegisterGlobalSystem(_contextMenuSystem);



        _kernel.Initialize();

        // Advertise this IG's capabilities so the ExCon can build its layer-control UI.
        if (_networkEnabled)
            IgCapabilitiesPublisher.Publish(_networkAdapter, _effectiveInstanceId);

    }



    // -------------------------------------------------------------------------



    /// <summary>

    /// Advances one frame of IG logic (input, ECS tick, viewport update).  

    /// Must be called before <see cref="DrawWorld"/> and <see cref="DrawUI"/> each frame.

    /// Called by both the standalone <see cref="Run"/> loop and the embedded orchestrator.

    /// </summary>

    public void Update(float dt)

    {

        _frameDt = dt;
        // CMC-S016: swap orch bus then tick translator before clusterSlave.
        _igOrchestrationBus?.SwapBuffers();
        _igSlaveTranslator?.Tick();
        _clusterSlave?.Tick();

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

        // Swap the context event bus so that SlaveSyncController (time controller) sees events
        // published by the time translators (SwitchTimeModeEvent, AdvanceFrameIntent) in the
        // next kernel.Update() call. Mirrors the SimHostApp.OnUpdate() pattern.
        _context?.EventBus.SwapBuffers();

        _fdpFrameCount++;
        _fdpEventBrowser.Update(_world.Bus, _fdpFrameCount);



        // Task 5a: Poll instance-scoped tool-activation commands (CMD_*) -- preferred path.

        if (_networkEnabled)

        {

            var cmdDto = _networkAdapter?.PollMapCommand();

            if (cmdDto != null)

            {

                // Accept only broadcast (TargetMapId==0) or commands addressed to this IG instance.

                if (cmdDto.TargetMapId == 0 || cmdDto.TargetMapId == _effectiveInstanceId)

                {

                    FdpLog<IgApplication>.Debug(

                        "[Node-{0}] MapCommandRequest: Type={1} MapId={2}", _effectiveInstanceId, cmdDto.CommandType, cmdDto.TargetMapId);

                    switch (cmdDto.CommandType)

                    {

                        case "CMD_START_AUTHORING":

                            ParseCommandAndActivateAreaTool(cmdDto.RequestId, cmdDto.CommandArgsJson);

                            break;

                        case "CMD_PLACE_ENTITY":

                            ParseCommandAndActivatePlacementTool(cmdDto.RequestId, cmdDto.CommandArgsJson);

                            break;


                        case "CMD_START_EDITING":

                            ParseCommandAndActivateEditTool(cmdDto.CommandArgsJson);

                            break;

                        case "CMD_PICK_LOCATION":

                            ParseCommandAndActivateLocationPicker(cmdDto.CommandArgsJson);

                            break;

                        case "CMD_PICK_ENTITY":

                            ParseCommandAndActivateEntityPicker(cmdDto.CommandArgsJson);

                            break;

                        case "CMD_SET_SELECTION":

                            ParseCommandAndSetSelection(cmdDto.CommandArgsJson);

                            break;

                        case "CMD_SET_VIEW":

                            ParseCommandAndSetView(cmdDto.CommandArgsJson);

                            break;

                        case "CMD_DRAW_PERSONAL_ROUTE":

                            ParseCommandAndActivatePersonalRoute(cmdDto.RequestId, cmdDto.CommandArgsJson);

                            break;

                    }

                }

            }

        }

        // Forward CreateUpdateDeleteEntityAck samples to the MapCommandController for session correlation.
        if (_networkEnabled && _mapCommandController != null)
        {
            var ackDto = _networkAdapter?.PollEntityLifecycleAck();
            if (ackDto != null)
                _mapCommandController.OnCreateEntityAck(ackDto);
        }


        // Task 5b: Poll ExCon => IG interaction-config updates (legacy -- grid/view toggle).

        if (_networkEnabled)

        {

            var cfgDto = _networkAdapter?.PollMapConfig();

            if (cfgDto != null)

            {

                _activeContextId = cfgDto.ActiveContextId;

                FdpLog<IgApplication>.Debug(

                    "[Node-{0}] MapInteractionConfig: ActiveContextId={1}", _effectiveInstanceId, _activeContextId);



                if (!string.IsNullOrWhiteSpace(cfgDto.ConfigJson))

                    ParseAndApplyConfig(cfgDto.ConfigJson);

            }

        }



        if (!_headless)

        {

            // Update UI panel states (TASK-IF008).

            _debugPanelState.CurrentSimTime   = _kernel.CurrentTime.TotalTime;
            _debugPanelState.CurrentWallTicks = _kernel.CurrentTime.TotalWallTicks;

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

                builder.AddSeparator();
                builder.AddItem("Delete entity", () =>
                {
                    if (_world.IsAlive(entity))
                    {
                        if (_world.HasComponent<NetworkIdentity>(entity))
                        {
                            ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);
                            // D003: always publish DestroyEntityCommand; egress translator forwards to DDS.
                            _world.Bus.PublishManaged(new DestroyEntityCommand
                            {
                                NetworkId = netId.Value,
                                Reason    = "context-menu-deleted"
                            });
                        }
                        else
                        {
                            _world.DestroyEntity(entity);
                        }

                        if (_fdpInspectorState.SelectedEntity == entity)
                            _fdpInspectorState.SelectedEntity = null;
                    }
                });
            }));
        }

        // When panels are registered as ManagedWindows, the Window Manager renders
        // them — only call Draw() here in standalone mode.
        if (!_panelsWindowManaged)
        {
            _debugPanel.Draw();
            _inspectorPanel.Draw();
            _waypointEditorPanel.Draw();
        }

        // ── Vertex context menu for RouteEditTool ─────────────────────────────
        if (_canvas.ActiveTool is RouteEditTool routeTool && routeTool.PendingVertexContextMenu)
        {
            ImGui.OpenPopup("##routeVtxCtx");
        }
        if (ImGui.BeginPopup("##routeVtxCtx"))
        {
            if (ImGui.MenuItem("Insert point after"))
                (_canvas.ActiveTool as RouteEditTool)?.InsertWaypointAfterSelected();
            if (ImGui.MenuItem("Delete point"))
                (_canvas.ActiveTool as RouteEditTool)?.DeleteSelectedWaypoint();
            ImGui.Separator();
            if (ImGui.MenuItem("Cancel"))
                (_canvas.ActiveTool as RouteEditTool)?.CloseVertexContextMenu();
            ImGui.EndPopup();
        }

        // ── Vertex context menu for EditTool (overlay shapes) ─────────────────
        if (_canvas.ActiveTool is EditTool editTool && editTool.PendingVertexContextMenu)
        {
            ImGui.OpenPopup("##overlayVtxCtx");
        }
        if (ImGui.BeginPopup("##overlayVtxCtx"))
        {
            if (ImGui.MenuItem("Insert point after"))
                (_canvas.ActiveTool as EditTool)?.InsertPointAfterSelected();
            if (ImGui.MenuItem("Delete point"))
                (_canvas.ActiveTool as EditTool)?.DeleteSelectedPoint();
            ImGui.Separator();
            if (ImGui.MenuItem("Cancel"))
                (_canvas.ActiveTool as EditTool)?.CloseVertexContextMenu();
            ImGui.EndPopup();
        }

        if (!_panelsWindowManaged)
        {
            _miniIosPanel.Draw();
            _performanceOverlay.Draw();
        }

        _contextMenuPanel.Draw();

        if (!_panelsWindowManaged)
        {
            IgPanelColors.Push();
            _fdpEntityInspector.Draw(_fdpRepoAdapter, _fdpInspectorState, "IG Entity Inspector");
            IgPanelColors.Pop();

            IgPanelColors.Push();
            _fdpEventBrowser.Draw("IG Event Browser");
            IgPanelColors.Pop();
        }

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
        // 1. Clear all existing ECS selection state (include ghosts/spawning).
        var q = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();
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

    /// <summary>Dispose alias for <see cref="Shutdown"/> (headless / test cleanup).</summary>
    public void Dispose() => Shutdown(ownsWindow: false);

    /// Pass <c>ownsWindow = false</c> when the orchestrator owns the Raylib window.

    /// </summary>

    public void Shutdown(bool ownsWindow = true)

    {

        _clusterSlave?.Dispose();
        _clusterSlave = null;

        // _networkAdapter.Dispose() disposes the command gateway and all DDS writers/readers.
        _networkAdapter?.Dispose();
        _networkAdapter = null;
        _commandGateway = null;

        _kernel?.Dispose();

        if (ownsWindow)

        {

            rlImGui.Shutdown();

            Raylib.CloseWindow();

        }

    }



    // -- Task 5: IG-to-ExCon event translators ----------------------------------


    /// <summary>
    /// Internal test hook: exposes the <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/>
    /// for handler-registration assertions (CGF1-S0104 / A.2).  <c>null</c> when
    /// <see cref="InitializeNetwork"/> was not called (e.g. headless tests without DDS).
    /// </summary>
    internal Fdp.Toolkit.Orchestration.ClusterSlave? TestHook_ClusterSlave => _clusterSlave;

    /// <summary>Current kernel sim time in seconds — available in both headless and normal mode.</summary>
    internal double TestHook_CurrentSimTime => _kernel.CurrentTime.TotalTime;

    /// <summary>
    /// Exposes the <see cref="Hrot.Common.Abstractions.INedReplicationModule"/> wired during
    /// <see cref="InitializeNetwork"/>.  Used by MODINIT-S302 SC6 tests to confirm
    /// <c>DriveFromNetwork == true</c> for the <c>ImageGenerator</c> role.
    /// </summary>
    internal Hrot.Common.Abstractions.INedReplicationModule? TestHook_NedReplication
        => _context?.NedReplication;

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

    /// canvas tool  i.e. the operator is in placement mode (activated by an ExCon

    /// <c>MapInteractionConfig</c>).

    /// </summary>

    internal bool TestHook_IsCreationToolActive => _canvas.ActiveTool is CreationTool;



    /// <summary>

    /// Returns <c>true</c> when <see cref="PointSequenceTool"/> is the active map tool.

    /// </summary>

    internal bool TestHook_IsPointSequenceToolActive => _canvas.ActiveTool is PointSequenceTool;



    /// <summary>

    /// Directly invokes <see cref="CreationTool.HandleClick"/> with a left-click at

    /// <paramref name="worldPos"/>, bypassing the ExCon-mediated <see cref="OnCanvasClicked"/>

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

    /// Internal test hook: parses CMD_START_AUTHORING args JSON and activates the

    /// appropriate authoring tool (area or route). Exposes

    /// <see cref="ParseCommandAndActivateAreaTool"/> for unit tests.

    /// </summary>

    internal void TestHook_ParseCommandAndActivateAreaTool(Guid requestId, string argsJson)

        => ParseCommandAndActivateAreaTool(requestId, argsJson);

    /// <summary>

    /// Internal test hook: injects a sink that captures every <see cref="SpawnEntityCommand"/>

    /// emitted by route/area authoring tools. Bypasses the controller so tests work headless.

    /// </summary>

    internal void TestHook_SetSpawnCommandSink(Action<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>? sink)

        => _testSpawnCommandSink = sink;

    /// <summary>

    /// Internal test hook: simulates a Shift+Right-Click at the given world position.

    /// Exposes the private <c>OnCanvasWorldClick</c> handler for unit tests.

    /// </summary>

    internal void TestHook_SimulateShiftRightClick(System.Numerics.Vector2 worldPos)

        => OnCanvasWorldClick(worldPos, MouseButton.Right, shift: true, ctrl: false, hit: Entity.Null);

    /// <summary>

    /// Internal test hook: simulates a plain (non-shift) right-click.

    /// </summary>

    internal void TestHook_SimulatePlainRightClick(System.Numerics.Vector2 worldPos)

        => OnCanvasWorldClick(worldPos, MouseButton.Right, shift: false, ctrl: false, hit: Entity.Null);

    /// <summary>

    /// Internal test hook to submit a Mini ExCon spawn request via the DDS gateway.

    /// </summary>

    internal void TestHook_SubmitMiniExConSpawn(long tkbType, ForceId affiliation, float positionX, float positionY)

    {

        if (_commandGateway == null)

            throw new InvalidOperationException("Mini ExCon gateway is not initialized.");



        _miniIosState.TkbType                = tkbType;

        _miniIosState.Affiliation            = affiliation;

        _miniIosState.PositionX              = positionX;

        _miniIosState.PositionY              = positionY;

        // Ensure explicit coordinates are used (not random) when the caller supplies a position.

        _miniIosState.UseSpecificCoordinates = true;

        _miniIosState.SubmitViaGateway(_commandGateway);

    }



    /// <summary>

    /// Internal test hook to submit a Mini ExCon spawn + WanderMilitary mission request

    /// via the DDS gateway (network distributed path).

    /// </summary>

    internal Task<long> TestHook_SubmitMiniExConSpawnWithWanderMission(

        long tkbType, ForceId affiliation, float positionX, float positionY)

    {

        if (_commandGateway == null)

            throw new InvalidOperationException("Mini ExCon gateway is not initialized.");



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
    /// Internal test hook: activates the <see cref="RouteEditTool"/> for the given
    /// network entity ID (same code path as a CMD_START_AUTHORING command).
    /// Used by commit-handler safety tests (CT-1).
    /// </summary>
    internal void TestHook_ActivateRouteEditToolForNetworkId(long networkEntityId)
        => ActivateAreaEditingTool(networkEntityId);

    /// <summary>
    /// Internal test hook: returns the currently active <see cref="RouteEditTool"/>,
    /// or <see langword="null"/> when a different tool is active.
    /// Used by commit-handler safety tests (CT-1).
    /// </summary>
    internal RouteEditTool? TestHook_ActiveRouteEditTool => _canvas.ActiveTool as RouteEditTool;

    /// <summary>Test hook: calls <see cref="ParseCommandAndSetSelection"/> directly.</summary>
    internal void TestHook_ParseCommandAndSetSelection(string argsJson)
        => ParseCommandAndSetSelection(argsJson);

    /// <summary>Test hook: calls <see cref="ParseCommandAndSetView"/> directly.</summary>
    internal void TestHook_ParseCommandAndSetView(string argsJson)
        => ParseCommandAndSetView(argsJson);

    /// <summary>Test hook: calls <see cref="ParseCommandAndActivatePersonalRoute"/> directly.</summary>
    internal void TestHook_ParseCommandAndActivatePersonalRoute(Guid requestId, string argsJson)
        => ParseCommandAndActivatePersonalRoute(requestId, argsJson);

    /// <summary>Test hook: the current camera keyboard-pan target (set by CenterCameraOn).</summary>
    internal Vector2 TestHook_KeyboardPanTarget => _keyboardPanTarget;

    // ── Ground clamping (MOD1-P7T5) ───────────────────────────────────────────

    private Fdp.Modules.Geographic.ITerrainProvider? _terrainProvider;

    /// <summary>
    /// Installs the ground-clamping pipeline.
    ///
    /// <para>
    /// Must be called <em>before</em> the first frame is rendered (i.e. before
    /// <see cref="Update"/> is invoked). Safe to omit for 2D-only deployments:
    /// when not called neither <c>TerrainQueryBatchData</c> nor any clamping
    /// systems will exist in the kernel.
    /// </para>
    /// </summary>
    public void InstallGroundClamping(Fdp.Modules.Geographic.ITerrainProvider provider)
    {
        _terrainProvider = provider;
        _kernel.RegisterModule(new Hrot.IG.Modules.IgGroundClampingModule(provider));
    }



    /// <summary>
    /// Internal test hook to inject GeoSpatial (position) data directly into an entity.
    /// Creates the entity as a ghost if it does not already exist.
    /// Uses the geographic transform to convert lat/lon/alt to Cartesian.
    /// </summary>
    internal void TestHook_InjectGeoSpatialDescriptor(int entityId, double lat, double lon, double alt, float heading = 0f)
    {
        if (_ghostCreationSystem == null)
            throw new InvalidOperationException("Ghost creation system is not initialized.");

        if (!_entityMap.TryGetEntity(entityId, out var entity))
            entity = _ghostCreationSystem.CreateGhost(_world, entityId);

        Vector3 position;
        if (_geoTransform != null)
        {
            var cartesian = _geoTransform.ToCartesian(lat, lon, alt);
            position = new Vector3((float)cartesian.X, (float)cartesian.Y, (float)cartesian.Z);
        }
        else
        {
            position = Vector3.Zero;
        }

        var rotation = SimTransformBridgeSystem.HeadingDegToRotation(heading);
        _world.SetComponent(entity, new NetworkTransform { LastPosition = position, LastRotation = rotation });
        _world.SetComponent(entity, new SimTransform    { Position = position, Rotation = rotation });
    }

    /// <summary>
    /// Internal test hook to inject EntityMaster data into the ingress pipeline.
    /// </summary>
    internal void TestHook_InjectEntityMasterDescriptor(int entityId, long tkbType, ulong disTypeValue = 0)
    {
        if (_ghostCreationSystem == null)
            throw new InvalidOperationException("Ghost creation system is not initialized.");

        if (!_entityMap.TryGetEntity(entityId, out var entity))
            entity = _ghostCreationSystem.CreateGhost(_world, entityId);

        var cmd = (EntityCommandBuffer)((ISimulationView)_world).GetCommandBuffer();
        // Permanent identity component — drives GhostPromotionSystem.
        cmd.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        _world.SetDisType(entity, new DISEntityType { Value = disTypeValue });
        cmd.Playback(_world);
    }



    /// </summary>

    private void OnCanvasClicked(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit, bool updateSelection = true)

    {

        if (!_networkEnabled || _networkAdapter == null || _geoTransform == null)

            return;



        var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(worldPos.X, worldPos.Y, 0f));



        var hitEntityIds = new List<int>();

        if (hit != Entity.Null && _world.HasComponent<NetworkIdentity>(hit))

        {

            ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(hit);

            hitEntityIds.Add((int)netId.Value);

        }



        _networkAdapter.WriteMapClick(new Hrot.Core.Network.MapClickEventDto

        {

            InteractionContextId = _activeContextId,

            Latitude             = lat,

            Longitude            = lon,

            Altitude             = alt,

            HitEntityIds         = hitEntityIds,

        });

FdpLog<IgApplication>.Info("[Node-{0}] MapClickEvent published. ContextId={1} hit={2}", _effectiveInstanceId, _activeContextId, hit.Index);

        // Publish selection state so ExCon can update the "Selection & Mission" panel.
        // A non-empty hit selects the entity; an empty-space click clears the selection.
        if (updateSelection)
        {
            var selIds = hitEntityIds.Count > 0
                ? hitEntityIds
                : new System.Collections.Generic.List<int>();
            _networkAdapter.WriteSelectionChanged(new Hrot.Core.Network.SelectionChangedEventDto
            {
                MapId             = _effectiveInstanceId,
                SelectedEntityIds = selIds,
            });
            FdpLog<IgApplication>.Debug("[Node-{0}] SelectionChangedEvent published. count={1}", _effectiveInstanceId, selIds.Count);
        }

    }



    private void OnCanvasWorldClick(Vector2 worldPos, MouseButton button, bool shift, bool ctrl, Entity hit)

    {

        if (button != MouseButton.Right)

            return;

        // Shift+Right-Click: publish CmdAppendPersonalWaypoint for each selected vehicle.

        if (shift)

        {

            var q = _selectionStateQuery ?? _world.Query().With<SelectionState>().Build();

            foreach (var entity in q)

            {

                var state = _world.GetComponent<SelectionState>(entity);

                if (!state.IsSelected && !state.IsPrimarySelection) continue;

                float altitude = _world.HasComponent<SimTransform>(entity)

                    ? _world.GetComponent<SimTransform>(entity).Position.Y

                    : 0f;

                _world.Bus.Publish(new CmdAppendPersonalWaypoint

                {

                    VehicleEntity = entity,

                    WorldPosition = new Vector3(worldPos.X, altitude, worldPos.Y),

                });

            }

            return;

        }

        var targetEntity = hit != Entity.Null ? hit : _mapContextEntity;

        var mousePos = Raylib.GetMousePosition();



        _contextMenuSystem.RequestOpen(targetEntity, mousePos.X, mousePos.Y);

    }



    /// <summary>
    /// Handles the end of an entity drag on the 2-D map canvas.
    /// Delegates to <see cref="SendGeoSpatialUpdate"/> using the last tracked drag position,
    /// then resets the continuous-drag timer.
    /// No-op when network is disabled or required services are unavailable.
    /// </summary>
    private void OnEntityDragEnded(Entity entity)
    {
        // Determine drop position: use tracked drag pos, fall back to SimTransform.
        var view = (ISimulationView)_world;
        System.Numerics.Vector2 dropPos;
        if (_lastDragWorldPos != default)
        {
            dropPos = _lastDragWorldPos;
        }
        else if (_networkEnabled && view.HasComponent<SimTransform>(entity))
        {
            var st = view.GetComponentRO<SimTransform>(entity);
            dropPos = new System.Numerics.Vector2(st.Position.X, st.Position.Y);
        }
        else
        {
            _continuousDragTimer = 0f;
            return;
        }

        SendGeoSpatialUpdate(entity, dropPos);

        // Reset stale drag position and continuous-drag timer.
        _lastDragWorldPos    = default;
        _continuousDragTimer = 0f;
    }

    /// <summary>
    /// Builds and sends a GeoSpatial update for <paramref name="entity"/> at
    /// <paramref name="worldPos"/>. Used by both the drag-end path and the throttled
    /// continuous-drag path.
    /// No-op when network is disabled or required services are unavailable.
    /// </summary>
    private void SendGeoSpatialUpdate(Entity entity, System.Numerics.Vector2 worldPos)
    {
        if (!_networkEnabled || _commandGateway == null || _geoTransform == null) return;

        var view = (ISimulationView)_world;
        if (!view.HasComponent<NetworkIdentity>(entity)) return;

        long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;

        var position = new System.Numerics.Vector3(worldPos.X, worldPos.Y, 0f);
        var (lat, lon, alt) = _geoTransform.ToGeodetic(position);

        // Encode as JSON for the neutral UpdateEntityDescriptorCommand; NedCommandGateway
        // parses it back to the wire type (WorldPos) via NedTranslationHelper.
        var descJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type     = "WorldPos",
            entityId = (int)netId,
            lat,
            lon,
            alt,
            time     = System.DateTime.UtcNow.Ticks,
        });
        var cmd = new Hrot.Core.Network.UpdateEntityDescriptorCommand
        {
            EntityId       = (int)netId,
            DescriptorJson = descJson,
            BaseVersion    = 0,
        };
        _ = _commandGateway.SendUpdateDescriptorAsync(cmd);

        FdpLog<IgApplication>.Info(
            "[Node-{0}] GeoSpatial update: sent UpdateEntityDescriptorCommand for NetID {1} to ({2:F5}, {3:F5}).",
            _effectiveInstanceId, netId, lat, lon);
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
    /// Test hook: injects a mock command gateway so unit tests can verify
    /// <see cref="SendGeoSpatialUpdate"/> calls without a live DDS participant.
    /// Also enables network-dependent code paths (sets <c>_networkEnabled = true</c>)
    /// so that the guard in <see cref="SendGeoSpatialUpdate"/> does not short-circuit.
    /// Must be called after <see cref="InitializeEmbedded"/>.
    /// </summary>
    internal void TestHook_SetCommandGateway(Hrot.Core.Network.ICommandGateway gateway)
    {
        _commandGateway = gateway;
        _networkEnabled = true;
    }

    /// <summary>
    /// Test hook: injects a mock network adapter so unit tests can verify
    /// IgApplication behaviour without a live DDS participant.
    /// Also enables network-dependent code paths (sets _networkEnabled = true).
    /// Must be called after <see cref="InitializeEmbedded"/>.
    /// </summary>
    internal void TestHook_SetNetworkAdapter(Hrot.Core.Network.IIgNetworkAdapter adapter)
    {
        _networkAdapter = adapter;
        _commandGateway = adapter.CommandGateway;
        _networkEnabled = true;
    }

    /// <summary>
    /// Test hook: accesses or overwrites the continuous-drag throttle timer
    /// so tests can pre-seed a partial accumulation before firing events.
    /// </summary>
    internal float TestHook_ContinuousDragTimer
    {
        get => _continuousDragTimer;
        set => _continuousDragTimer = value;
    }

    /// <summary>
    /// Test hook: simulates the <c>OnEntityMoved</c> handler for the entity identified by
    /// <paramref name="networkId"/> at <paramref name="worldPos"/> with an explicit
    /// <paramref name="dt"/> (seconds). Drives the continuous-drag throttle timer and,
    /// when the threshold is exceeded, calls <see cref="SendGeoSpatialUpdate"/>.
    /// Pass <paramref name="isShiftHeld"/> = <c>true</c> to exercise the shift-key immediate-
    /// send path (BUG2-I001).
    /// </summary>
    internal void TestHook_SimulateEntityMoved(long networkId, System.Numerics.Vector2 worldPos, float dt, bool isShiftHeld = false)
    {
        if (!_entityMap.TryGetEntity(networkId, out var entity))
            throw new InvalidOperationException($"Entity with networkId={networkId} not found in IG entity map.");

        if (_userConfig.ContinuousDragUpdates)
        {
            _continuousDragTimer += dt;
            if (_continuousDragTimer >= ContinuousDragIntervalSec)
            {
                SendGeoSpatialUpdate(entity, worldPos);
                _continuousDragTimer = 0f;
            }
        }
        else if (isShiftHeld && _lastDragWorldPos != worldPos)
        {
            SendGeoSpatialUpdate(entity, worldPos);
        }

        _lastDragWorldPos = worldPos;
    }

    /// <summary>
    /// Test hook: exposes the <see cref="MapUserConfig"/> so tests can toggle
    /// feature flags (e.g. <see cref="MapUserConfig.ContinuousDragUpdates"/>).
    /// </summary>
    internal MapUserConfig TestHook_UserConfig => _userConfig;



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

        if (action.ActionName.StartsWith("IG_", StringComparison.Ordinal) ||

            action.ActionName == "100" ||

            action.ActionName == "101" ||

            action.ActionName == "102" ||

            action.ActionName == "200")

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

            case "IG_DeleteEntity":

            {

                if (_world.IsAlive(entity))

                {

                    if (_world.HasComponent<NetworkIdentity>(entity))

                    {

                        ref readonly var netId = ref _world.GetComponentRO<NetworkIdentity>(entity);

                        _world.Bus.PublishManaged(new DestroyEntityCommand

                        {

                            NetworkId = netId.Value,

                            Reason    = "map-context-deleted"

                        });

                    }

                    else

                    {

                        _world.DestroyEntity(entity);

                    }

                    if (_fdpInspectorState.SelectedEntity == entity)

                        _fdpInspectorState.SelectedEntity = null;

                }

                break;

            }

            case "100": // EditOverlay — activate area-editing tool on the selected entity

            {

                var view100 = (ISimulationView)_world;

                if (view100.HasComponent<NetworkIdentity>(entity))

                {

                    ref readonly var netId = ref view100.GetComponentRO<NetworkIdentity>(entity);

                    ActivateAreaEditingTool(netId.Value);

                }

                break;

            }

            case "101": // EditRoute — activate route editing for the selected route entity

            {

                var view101 = (ISimulationView)_world;

                if (view101.HasComponent<NetworkIdentity>(entity))

                {

                    ref readonly var netId = ref view101.GetComponentRO<NetworkIdentity>(entity);

                    ActivateAreaEditingTool(netId.Value);

                }

                break;

            }

            case "102": // EditPersonalRoute — locate the vehicle's personal route and edit it

            {

                if (_world.HasComponent<Hrot.Map.Common.Components.PersonalRouteRef>(entity))

                {

                    ref readonly var routeRef = ref _world.GetComponentRO<Hrot.Map.Common.Components.PersonalRouteRef>(entity);

                    if (_world.IsAlive(routeRef.RouteEntity)

                     && _entityMap.TryGetNetworkId(routeRef.RouteEntity, out long routeNetId))

                    {

                        ActivateAreaEditingTool(routeNetId);

                    }

                }

                break;

            }

            case "200": // Measure — push the measurement tool onto the canvas

                _canvas.PushTool(new MeasureTool());

                break;



            default:

                FdpLog<IgApplication>.Warn("[Node-{0}] Unhandled local context action: {1}", _effectiveInstanceId, actionName);

                break;

        }

    }



    /// <summary>

    /// Exposes <see cref="ExecuteLocalContextAction"/> for unit tests that need to verify

    /// the per-action routing logic without going through the full UI/DDS stack.

    /// </summary>

    internal void TestHook_ExecuteLocalContextAction(Entity entity, string actionName)

        => ExecuteLocalContextAction(entity, actionName);



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



    // -- Grid rendering --------------------------------------------------------



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



    // -- Config JSON parsing ---------------------------------------------------

    // ─── CMD_* command handlers ────────────────────────────────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_START_AUTHORING"/> command.
    /// Extracts <c>contextId</c> and <c>styleOverrideJson</c> from the JSON args,
    /// stores the context ID, then activates the area-authoring point-sequence tool.
    /// </summary>
    private void ParseCommandAndActivateAreaTool(Guid requestId, string argsJson)
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

            // If TkbType specifies a route, use the route-specific authoring tool.
            if (root.TryGetProperty("tkbType", out var tkbEl)
             && tkbEl.TryGetInt64(out var tkbType)
             && tkbType == TkbEntityTypes.TacGraphic_Route)
            {
                ActivateRouteAuthoringTool(requestId);
                return;
            }

            string styleJson = string.Empty;
            if (root.TryGetProperty("styleOverrideJson", out var styleEl)
             && styleEl.ValueKind == JsonValueKind.String)
            {
                styleJson = styleEl.GetString() ?? string.Empty;
            }

            ActivateAreaAuthoringTool(requestId, styleJson);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ParseCommandAndActivateAreaTool failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_PLACE_ENTITY"/> command.
    /// Extracts <c>contextId</c>, <c>entityType</c>, <c>affiliation</c>, and
    /// optional <c>initialPropertiesJson</c> from the JSON args, then delegates
    /// to <see cref="MapCommandController"/> to activate the placement tool.
    /// </summary>
    private void ParseCommandAndActivatePlacementTool(Guid requestId, string argsJson)
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
            string? initialPropertiesJson = null;

            if (root.TryGetProperty("entityType", out var etEl))
                tkbType = etEl.GetInt64();

            if (root.TryGetProperty("initialPropertiesJson", out var propsEl)
             && propsEl.ValueKind == JsonValueKind.String)
            {
                initialPropertiesJson = propsEl.GetString();
            }

            ActivatePlacementTool(requestId, tkbType, initialPropertiesJson);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ParseCommandAndActivatePlacementTool failed: {1}", _effectiveInstanceId, ex.Message);
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
                "[Node-{0}] ParseCommandAndActivateEditTool failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    // ─── OC1-G001: CMD_SET_SELECTION ──────────────────────────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_SET_SELECTION"/> command.
    /// Selects the entity identified by <c>entityId</c> in the ECS without publishing
    /// a <see cref="SelectionChangedEvent"/> (to avoid ExCon→IG→ExCon echo loops).
    /// </summary>
    private void ParseCommandAndSetSelection(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("entityId", out var eidEl))
                return;

            long entityId = eidEl.GetInt64();

            if (!_entityMap.TryGetEntity(entityId, out var entity))
            {
                FdpLog<IgApplication>.Warn(
                    "[Node-{0}] CMD_SET_SELECTION: entity {1} not found.", _effectiveInstanceId, entityId);
                return;
            }

            SelectEntityOnMap(entity);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] ParseCommandAndSetSelection failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    // ─── OC1-G002: CMD_SET_VIEW ───────────────────────────────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_SET_VIEW"/> command (entity-centric path).
    /// Centers the camera on the entity identified by <c>entityId</c>.
    /// The raw lat/lon path is deferred to a future batch.
    /// </summary>
    private void ParseCommandAndSetView(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("entityId", out var eidEl))
                return;

            long entityId = eidEl.GetInt64();

            if (!_entityMap.TryGetEntity(entityId, out var entity))
            {
                FdpLog<IgApplication>.Warn(
                    "[Node-{0}] CMD_SET_VIEW: entity {1} not found.", _effectiveInstanceId, entityId);
                return;
            }

            CenterCameraOn(entity);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] ParseCommandAndSetView failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    // ─── OC1-G003: CMD_DRAW_PERSONAL_ROUTE ───────────────────────────────────

    /// <summary>
    /// Handles an incoming CMD_DRAW_PERSONAL_ROUTE command.
    /// Pushes a <see cref="PointSequenceTool"/> requiring >= 2 points; on completion,
    /// fire-and-forgets <see cref="OrchestratePersonalRouteAsync"/>.
    /// </summary>
    private void ParseCommandAndActivatePersonalRoute(Guid requestId, string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return;

        try
        {
            using var doc  = JsonDocument.Parse(argsJson);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("entityId", out var eidEl))
                return;

            int vehicleId = eidEl.GetInt32();

            // Pop any stale PointSequenceTool to avoid tool stack accumulation.
            if (_canvas.ActiveTool is PointSequenceTool)
                _canvas.PopTool();

            var tool = new PointSequenceTool(points =>
            {
                if (points.Length < 2)
                {
                    // Too few points — cancel the command.
                    _networkAdapter?.WriteMapCommandAck(new Hrot.Core.Network.MapCommandAckDto
                    {
                        RequestId  = requestId,
                        StatusCode = (int)MapCommandController.StatusCancelled,
                    });
                    _canvas.PopTool();
                    return;
                }

                _ = OrchestratePersonalRouteAsync(requestId, vehicleId, points);
                _canvas.PopTool();
            });

            _canvas.PushTool(tool);
            FdpLog<IgApplication>.Info(
                "[Node-{0}] CMD_DRAW_PERSONAL_ROUTE: point-sequence tool activated for vehicle {1}.", _effectiveInstanceId, vehicleId);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] ParseCommandAndActivatePersonalRoute failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    /// <summary>
    /// Fire-and-forget orchestration for CMD_DRAW_PERSONAL_ROUTE.
    /// Converts canvas points to geodetic waypoints, creates a route entity via the
    /// neutral adapter, and assigns a FollowRoute mission to <paramref name="vehicleId"/>.
    /// No-op when required services are unavailable (e.g. headless tests without DDS).
    /// </summary>
    private async System.Threading.Tasks.Task OrchestratePersonalRouteAsync(
        Guid requestId, int vehicleId, Vector2[] canvasPoints)
    {
        if (_networkAdapter == null || _commandGateway == null || _geoTransform == null)
            return;

        // Convert canvas points to absolute geodetic waypoints.
        // Canvas is XZ: canvas Y = world Z (North). Altitude (Vector3.Y) = 0 for authoring.
        var waypoints = new List<(double Lat, double Lon, double Alt)>(canvasPoints.Length);
        double anchorLat = 0, anchorLon = 0, anchorAlt = 0;
        for (int i = 0; i < canvasPoints.Length; i++)
        {
            var (lat, lon, alt) = _geoTransform.ToGeodetic(
                new Vector3(canvasPoints[i].X, 0f, canvasPoints[i].Y));
            waypoints.Add((lat, lon, alt));
            if (i == 0) { anchorLat = lat; anchorLon = lon; anchorAlt = alt; }
        }

        // Create the route entity via the neutral adapter.
        int routeEntityId;
        try
        {
            routeEntityId = await _networkAdapter.CreateRouteEntityAsync(
                TkbEntityTypes.TacGraphic_Route,
                waypoints,
                anchorLat, anchorLon, anchorAlt,
                vehicleId);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] OrchestratePersonalRoute: CreateRouteEntityAsync failed: {1}", _effectiveInstanceId, ex.Message);
            SendPersonalRouteAck(requestId, MapCommandController.StatusCancelled);
            return;
        }

        if (routeEntityId <= 0)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] OrchestratePersonalRoute: CreateRouteEntityAsync returned id {1}.", _effectiveInstanceId, routeEntityId);
            SendPersonalRouteAck(requestId, MapCommandController.StatusCancelled);
            return;
        }

        // Assign a FollowRoute mission using the newly-created route entity's network ID.
        var taskId = Guid.NewGuid();
        var missionCmd = new Hrot.Core.Network.MissionControlCommand
        {
            EntityId    = vehicleId,
            CommandType = Hrot.Core.Mission.eMissionCommandType.CMD_REPLACE_MISSION,
            Plan = new Hrot.Core.Mission.MissionPlan
            {
                ActiveTaskId = taskId,
                Tasks = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTask>
                {
                    new Hrot.Core.Mission.MissionTask
                    {
                        TaskId          = taskId,
                        ExecutingEngine = string.Empty,
                        BehaviorId      = "FollowRoute",
                        BehaviorParams  = $"{{\"routeEntityId\":{routeEntityId}}}",
                        Triggers        = new System.Collections.Generic.List<Hrot.Core.Mission.MissionTrigger>(),
                        State           = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
                    }
                }
            }
        };

        try
        {
            await _commandGateway.SendMissionControlRequestAsync(missionCmd);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] OrchestratePersonalRoute: SendMissionControlRequestAsync failed: {1}", _effectiveInstanceId, ex.Message);
        }

        SendPersonalRouteAck(requestId, MapCommandController.StatusFinished);
    }

    private void SendPersonalRouteAck(Guid requestId, long statusCode)
    {
        _networkAdapter?.WriteMapCommandAck(new Hrot.Core.Network.MapCommandAckDto
        {
            RequestId  = requestId,
            StatusCode = (int)statusCode,
        });
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
    ///
    /// <para>
    /// When the entity has a <see cref="Hrot.Map.Common.Components.RoutePlan"/> component,
    /// the method pushes a <see cref="RouteEditTool"/> instead of the generic
    /// <see cref="EditTool"/> so that route-specific interactions (waypoint insert/delete,
    /// per-waypoint speed/advice editing) are available.
    /// </para>
    /// </summary>
    private void ActivateAreaEditingTool(long networkEntityId)
    {
        if (!_entityMap.TryGetEntity(networkEntityId, out var entity))
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: entity not found for NetID {1}.", _effectiveInstanceId, networkEntityId);
            return;
        }

        // ── Route entity path — use RouteEditTool ─────────────────────────────
        if (World.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(entity))
        {
            // Pop any stale edit tool to prevent stack accumulation.
            if (_canvas.ActiveTool is RouteEditTool || _canvas.ActiveTool is EditTool)
                _canvas.PopTool();

            var view = (ISimulationView)World;
            var plan = view.GetManagedComponentRO<Hrot.Map.Common.Components.RoutePlan>(entity);
            var routeEditTool = new RouteEditTool(entity, plan,
                onCommit: (committedEntity, updatedWaypoints) =>
                {
                    // CT-1: entity may be destroyed between edit-start and commit (e.g. SimHost
                    // removes it mid-frame). Silently discard rather than crashing.
                    if (!World.IsAlive(committedEntity)) return;

                    var view2 = (ISimulationView)World;
                    var existingPlan = view2.GetManagedComponentRO<Hrot.Map.Common.Components.RoutePlan>(committedEntity);
                    existingPlan.Mutate(wps =>
                    {
                        wps.Clear();
                        wps.AddRange(updatedWaypoints);
                    });

                    // Publish network update when connected.
                    if (_entityMap.TryGetNetworkId(committedEntity, out long netId))
                    {
                        // D002: publish UpdateEntityCommand; UpdateEntityCommandEgressTranslator
                        // converts RoutePlan waypoints (Cartesian) to geodetic and writes DDS.
                        _world.Bus.PublishManaged(new UpdateEntityCommand
                        {
                            NetworkId          = netId,
                            ComponentsToUpdate = new System.Collections.Generic.List<object> { existingPlan },
                            RequestId          = Guid.NewGuid(),
                        });
                        FdpLog<IgApplication>.Info(
                            "[Node-{0}] Committed route edit for NetID {1}: {2} waypoints.", _effectiveInstanceId, netId, updatedWaypoints.Count);
                    }
                });

            _canvas.PushTool(routeEditTool);
            FdpLog<IgApplication>.Info("[Node-{0}] Route editing tool activated for NetID {1}.", _effectiveInstanceId, networkEntityId);
            return;
        }

        // ── Area overlay entity path — use generic EditTool ─────────────────────

        if (!World.HasManagedComponent<EditablePolyline>(entity))
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: entity {1} has no EditablePolyline.", _effectiveInstanceId, networkEntityId);
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
                "[Node-{0}] ActivateAreaEditingTool: entity {1} has no SimTransform yet.", _effectiveInstanceId, networkEntityId);
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
            var updatedPolyline = new EditablePolyline { Points = relPoints };
            World.SetManagedComponent(committedEntity, updatedPolyline);

            // Send UpdateEntityCommand(EditablePolyline) via bus.
            // D002: UpdateEntityCommandEgressTranslator converts relative Cartesian offsets
            // to relative geodetic and writes UpdateEntityDescriptorRequest(dtMapVisualOverlay).
            if (_entityMap.TryGetNetworkId(committedEntity, out long netId))
            {
                _world.Bus.PublishManaged(new UpdateEntityCommand
                {
                    NetworkId          = netId,
                    ComponentsToUpdate = new System.Collections.Generic.List<object> { updatedPolyline },
                    RequestId          = Guid.NewGuid(),
                });

                FdpLog<IgApplication>.Info(
                    "[Node-{0}] Committed overlay edit for NetID {1}: {2} vertices.", _effectiveInstanceId, netId, absCartPoints.Count);
            }
        };

        _canvas.PushTool(editTool);
        FdpLog<IgApplication>.Info("[Node-{0}] Area editing tool activated for NetID {1}.", _effectiveInstanceId, networkEntityId);
    }

    /// <summary>

    /// Parses a JSON Merge Patch from ExCon and applies client-side settings

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
            // Missing keys leave their bits unchanged (forward-compatible with future ExCon versions).
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

                    long    tkbType              = 0;
                    string? initialPropertiesJson = null;

                    if (toolConfigEl.TryGetProperty("entityType", out var etEl))
                        tkbType = etEl.GetInt64();

                    if (toolConfigEl.TryGetProperty("affiliation", out var affEl)
                     && affEl.ValueKind == JsonValueKind.String)
                    {
                        // Affiliation is just another property — embed it into initialPropertiesJson
                        // so CreationTool can consume it as part of the initial property blob.
                        initialPropertiesJson = System.Text.Json.JsonSerializer.Serialize(
                            new { affiliation = affEl.GetString() });
                    }

                    ActivatePlacementTool(Guid.Empty, tkbType, initialPropertiesJson);

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

                    ActivateAreaAuthoringTool(Guid.Empty, styleJson);

                }

            }

        }

        catch (Exception ex)

        {

            FdpLog<IgApplication>.Warn("[Node-{0}] Failed to parse ConfigurationJson: {1}", _effectiveInstanceId, ex.Message);

        }

    }



    /// <summary>

    /// Pushes a <see cref="CreationTool"/> onto the canvas tool stack.

    /// Guarded by <see cref="_lastPlacementContextId"/> so repeated keep-last

    /// DDS deliveries do not re-activate the tool for the same interaction context.

    /// </summary>

    private void ActivatePlacementTool(Guid requestId, long tkbType, string? initialPropertiesJson = null)
    {
        if (_lastPlacementContextId == _activeContextId)
            return;
        _lastPlacementContextId = _activeContextId;

        if (!_networkEnabled || _mapCommandController == null)
            return;

        // Build a session-scoped name generator when the patch requests auto-naming.
        Func<string>? nameGenerator = null;
        if (!string.IsNullOrWhiteSpace(initialPropertiesJson))
        {
            try
            {
                var patch = System.Text.Json.JsonSerializer.Deserialize<EntityPropertyPatch>(
                    initialPropertiesJson);

                if (patch?.AutogenerateName == true)
                {
                    // Derive the prefix: prefer the explicit NamePrefix, fall back to the TKB
                    // template name (e.g. "Tank-"), and finally fall back to "Unit-".
                    string prefix = patch.NamePrefix ?? GetTkbPrefixForType(tkbType);
                    nameGenerator = UniqueNameGenerator.CreateSessionGenerator(_world, prefix);
                    FdpLog<IgApplication>.Info(
                        "[Node-{0}] Auto-naming enabled for TkbType={1} prefix=\"{2}\".", _effectiveInstanceId, tkbType, prefix);
                }
            }
            catch (Exception ex)
            {
                FdpLog<IgApplication>.Warn(
                    "[Node-{0}] ActivatePlacementTool: could not parse EntityPropertyPatch: {1}", _effectiveInstanceId, ex.Message);
            }
        }

        _mapCommandController.ActivatePlacementCommand(
            requestId,
            _activeContextId,
            tkbType,
            _geoTransform,
            initialPropertiesJson,
            nameGenerator);

        FdpLog<IgApplication>.Info(
            "[Node-{0}] Placement tool activated via controller. TkbType={1}", _effectiveInstanceId, tkbType);
    }

    /// <summary>
    /// Returns a name prefix string derived from the TKB template for
    /// <paramref name="tkbType"/>. Falls back to <c>"Unit-"</c> when the
    /// template is not found.
    /// </summary>
    private string GetTkbPrefixForType(long tkbType)
    {
        var tkbDb = _world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
        if (tkbDb != null && tkbDb.TryGetByType(tkbType, out var template)
         && !string.IsNullOrWhiteSpace(template.Name))
        {
            return template.Name + "-";
        }
        return "Unit-";
    }



    /// <summary>

    /// Pushes a <see cref="PointSequenceTool"/> onto the canvas tool stack for area authoring.

    /// Guarded by <see cref="_lastAreaContextId"/> so repeated keep-last DDS deliveries do not

    /// re-activate the tool for the same interaction context.

    /// </summary>

    private void ActivateAreaAuthoringTool(Guid requestId, string styleJson = "")

    {

        if (_lastAreaContextId == _activeContextId)

            return;

        _lastAreaContextId = _activeContextId;



        if (!_networkEnabled && _testSpawnCommandSink == null)

            return;



        if (_canvas.ActiveTool is PointSequenceTool)

            _canvas.PopTool();



        _mapCommandController?.BeginAreaAuthoringSession(requestId, _activeContextId);

        var tool = new PointSequenceTool(points =>

        {

            if (points.Length < 3)

            {

                _mapCommandController?.OnAreaToolCancelled();

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

                    // Canvas is XY: canvas Y = world Y (North, ENU). Altitude (Vector3.Z) is 0 for authoring.
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



            // Compute anchor (centroid) in Cartesian world space.
            Vector3 anchorCartesian;
            if (_geoTransform != null)
            {
                anchorCartesian = _geoTransform.ToCartesian(refLat, refLon, refAlt);
            }
            else
            {
                anchorCartesian = new Vector3((float)refLon, (float)refLat, 0f);
            }

            // Build entity-relative Cartesian XY for each vertex.
            var relCartPoints = new List<Vector2>(absPositions.Count);
            for (int i = 0; i < absPositions.Count; i++)
            {
                if (_geoTransform != null)
                {
                    var absCart = _geoTransform.ToCartesian(absPositions[i].Lat, absPositions[i].Lon, 0.0);
                    relCartPoints.Add(new Vector2(absCart.X - anchorCartesian.X, absCart.Y - anchorCartesian.Y));
                }
                else
                {
                    relCartPoints.Add(new Vector2(points[i].X - anchorCartesian.X, points[i].Y - anchorCartesian.Y));
                }
            }

            var polyline = new EditablePolyline { Points = relCartPoints };
            var style    = MapOverlayStyle.FromJson(styleJson);

            var cmd = new Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand
            {
                NetworkId      = 0,
                TkbType        = TkbEntityTypes.TacGraphic_Area,
                OwnerNodeId    = 0,
                InitType       = ReliableInitType.AllPeers,
                RequestId      = Guid.NewGuid(),
                InitialTransform = new SimTransform { Position = anchorCartesian },
                InitialComponents = new System.Collections.Generic.List<object> { polyline, style },
            };

            if (_testSpawnCommandSink != null)
                _testSpawnCommandSink(cmd);
            else if (_mapCommandController != null)
                _mapCommandController.OnAreaEntityCreated(cmd, isToolDone: true);

            _canvas.PopTool();

        });
        _canvas.PushTool(tool);



        FdpLog<IgApplication>.Info("[Node-{0}] Area authoring tool activated.", _effectiveInstanceId);

    }

    /// <summary>
    /// Activates a <see cref="PointSequenceTool"/> configured for route authoring
    /// (minimum 2 points). When finished, emits a <see cref="SpawnEntityCommand"/>
    /// with <see cref="Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand.InitialComponents"/>
    /// carrying a <see cref="Hrot.Map.Common.Components.RoutePlan"/>.
    /// </summary>
    private void ActivateRouteAuthoringTool(Guid requestId)
    {
        if (!_networkEnabled && _testSpawnCommandSink == null)
            return;

        if (_canvas.ActiveTool is PointSequenceTool)
            _canvas.PopTool();

        _mapCommandController?.BeginAreaAuthoringSession(requestId, _activeContextId);

        var tool = new PointSequenceTool(points =>
        {
            if (points.Length < 2)
            {
                _mapCommandController?.OnAreaToolCancelled();
                _canvas.PopTool();
                return;
            }

            // Guard: cannot convert canvas positions to geodetic without a geo-transform.
            // Passing raw XY canvas coordinates as lat/lon produces invalid DDS payloads.
            if (_geoTransform == null)
            {
                FdpLog<IgApplication>.Error(
                    "[Node-{0}] Cannot create route: geographic transform is unavailable. " +
                    "Ensure the IG is initialised with a valid map origin before authoring routes.", _effectiveInstanceId);
                _canvas.PopTool();
                return;
            }

            // Convert each canvas 2D point to Cartesian world space and build RoutePlan.
            // Canvas is XZ: canvas Y = world Z (North). Altitude (Vector3.Y) is 0 for authoring.
            var routePlan = new Hrot.Map.Common.Components.RoutePlan { IsLoop = false };
            Vector3 anchorCartesian = Vector3.Zero;

            routePlan.Mutate(wps =>
            {
                for (int i = 0; i < points.Length; i++)
                {
                    var (lat, lon, alt) = _geoTransform.ToGeodetic(new Vector3(points[i].X, 0f, points[i].Y));
                    var cartPos = _geoTransform.ToCartesian(lat, lon, alt);

                    wps.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = cartPos, TargetSpeed = 0f });

                    if (i == 0) anchorCartesian = cartPos;
                }
            });

            var cmd = new Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand
            {
                NetworkId        = 0,
                TkbType          = TkbEntityTypes.TacGraphic_Route,
                OwnerNodeId      = 0,
                InitType         = ReliableInitType.AllPeers,
                RequestId        = Guid.NewGuid(),
                InitialTransform = new SimTransform { Position = anchorCartesian },
                InitialComponents = new System.Collections.Generic.List<object> { routePlan },
            };

            if (_testSpawnCommandSink != null)
                _testSpawnCommandSink(cmd);
            else if (_mapCommandController != null)
                _mapCommandController.OnAreaEntityCreated(cmd, isToolDone: true);

            _canvas.PopTool();
        });

        _canvas.PushTool(tool);

        FdpLog<IgApplication>.Info("[Node-{0}] Route authoring tool activated.", _effectiveInstanceId);
    }



    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_PICK_LOCATION"/> command.
    /// Extracts <c>contextId</c> from the JSON args, sets the active context ID,
    /// then pushes a <see cref="LocationPickerTool"/> onto the canvas.
    /// The tool publishes a <see cref="MapClickEvent"/> (via the existing
    /// <c>OnCanvasClicked</c> pathway) when the operator left-clicks.
    /// </summary>
    private void ParseCommandAndActivateLocationPicker(string argsJson)
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

            ActivateLocationPickerTool();
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ParseCommandAndActivateLocationPicker failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    private void ActivateLocationPickerTool()
    {
        if (_lastPickLocationContextId == _activeContextId)
            return;
        _lastPickLocationContextId = _activeContextId;

        if (_canvas.ActiveTool is LocationPickerTool)
            _canvas.PopTool();

        var tool = new LocationPickerTool();
        tool.OnLocationPicked += worldPos =>
            OnCanvasClicked(worldPos, MouseButton.Left, false, false, Entity.Null, updateSelection: false);
        tool.OnCancelled += () =>
            FdpLog<IgApplication>.Debug("[Node-{0}] LocationPicker cancelled.", _effectiveInstanceId);

        _canvas.PushTool(tool);
        FdpLog<IgApplication>.Info("[Node-{0}] Location picker tool activated. ContextId={1}", _effectiveInstanceId, _activeContextId);
    }

    // ─── EntityPickerTool activation from CMD_PICK_ENTITY ────────────────────

    /// <summary>
    /// Handles an incoming <see cref="CommandType.CMD_PICK_ENTITY"/> command.
    /// Extracts <c>contextId</c> and <c>filters</c> from the JSON args, then
    /// activates the <see cref="Fdp.Toolkit.Vis2D.Tools.EntityPickerTool"/>.
    /// When the operator clicks a valid entity the tool publishes a
    /// <see cref="MapClickEvent"/> (via <c>OnCanvasClicked</c>) with the entity
    /// in the <c>HitStack</c> so the ExCon can resolve its pending pick promise.
    /// </summary>
    private void ParseCommandAndActivateEntityPicker(string argsJson)
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

            string[] filters = Array.Empty<string>();
            if (root.TryGetProperty("filters", out var filtersEl)
             && filtersEl.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var item in filtersEl.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        list.Add(s!);
                }
                filters = list.ToArray();
            }

            ActivateEntityPickerTool(filters);
        }
        catch (Exception ex)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ParseCommandAndActivateEntityPicker failed: {1}", _effectiveInstanceId, ex.Message);
        }
    }

    private void ActivateEntityPickerTool(string[] filters)
    {
        if (_lastPickEntityContextId == _activeContextId)
            return;
        _lastPickEntityContextId = _activeContextId;

        if (_entityFilterFactory == null)
        {
            FdpLog<IgApplication>.Warn("[Node-{0}] EntityPickerTool requested but filter factory is not ready.", _effectiveInstanceId);
            return;
        }

        if (_canvas.ActiveTool is Fdp.Toolkit.Vis2D.Tools.EntityPickerTool)
            _canvas.PopTool();

        var tool = new Fdp.Toolkit.Vis2D.Tools.EntityPickerTool(_entityFilterFactory, filters);

        tool.OnEntityPicked += entity =>
        {
            // Re-use OnCanvasClicked to publish the MapClickEvent.
            // The entity will appear in HitStack so the ExCon receives the networkId.
            OnCanvasClicked(Vector2.Zero, MouseButton.Left, false, false, entity, updateSelection: false);
            FdpLog<IgApplication>.Info("[Node-{0}] EntityPicker picked entity {1}", _effectiveInstanceId, entity.Index);
        };

        tool.OnCancelled += () =>
            FdpLog<IgApplication>.Debug("[Node-{0}] EntityPicker cancelled.", _effectiveInstanceId);

        _canvas.PushTool(tool);
        FdpLog<IgApplication>.Info("[Node-{0}] Entity picker tool activated. ContextId={1} Filters=[{2}]",
            _effectiveInstanceId, _activeContextId, string.Join(",", filters));
    }



    // -- Private adapter -------------------------------------------------------



    /// <summary>
    /// Bridges <see cref="CycloneDDS.Runtime.DdsWriter{T}"/> to the
    /// <see cref="Hrot.IG.Abstractions.IDdsWriter{T}"/> interface,
    /// keeping the IG tool and controller layers free of CycloneDDS assembly references.
    /// </summary>
    private sealed class CycloneDdsWriterIgAdapter<T> : Hrot.IG.Abstractions.IDdsWriter<T>
    {
        private readonly DdsWriter<T> _inner;

        public CycloneDdsWriterIgAdapter(DdsWriter<T> inner)
            => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public void Write(T sample) => _inner.Write(sample);
    }

    // ── Ghost entity cleanup ──────────────────────────────────────────────────────

    /// <summary>
    /// Handles <see cref="DestroyEntityCommand"/> events (published when SimHost sends
    /// EntityMaster DISPOSE) by unregistering and destroying the local ghost entity.
    /// Replaces <see cref="SpawningModule"/> so the IG no longer acts as an authoritative
    /// spawner and thus avoids duplicate local entities.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    private sealed class GhostDestructionSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GhostDestructionSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap;
        }

        public void Execute(ISimulationView view, float dt)
        {
            var world = view as EntityRepository;
            if (world == null) return;

            foreach (var cmd in view.ConsumeManagedEvents<DestroyEntityCommand>())
            {
                if (_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
                {
                    _entityMap.Unregister(cmd.NetworkId, view.Tick);
                    if (world.IsAlive(entity))
                        world.DestroyEntity(entity);
                }
            }
        }
    }
}

