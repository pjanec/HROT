using System;

using System.Collections.Generic;

using System.Numerics;

using System.Threading.Tasks;

using System.Text.Json;

using CarKinem.Core;

using Hrot.IG.Components;

using Hrot.IG.Modules;


using Hrot.IG.Systems;

using Hrot.Common;
using Hrot.Common.Systems;
using Hrot.Common.Constants;
using Hrot.Common.Interactions;

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
using Fdp.Toolkit.Replication.Attributes;

using Fdp.Toolkit.Replication.Components;

using Fdp.Toolkit.Replication.Patching;

using Fdp.Toolkit.Replication.Services;

using Fdp.Toolkit.Replication.Systems;

using Hrot.Common.Orchestration;
using Hrot.Common.Infrastructure;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;

using Fdp.Toolkit.Time.Controllers;
using Hrot.Presentation.Map;

using Fdp.Toolkit.Vis2D;

using Fdp.Toolkit.Vis2D.Components;

using Fdp.Toolkit.Vis2D.Defaults;

using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;
using Hrot.IG.Gizmos;
using Hrot.Network.NED.Gizmos;   // DebugPrimitivesIngressTranslator (primitive streaming, not interaction)
using Hrot.Common.Events;
using Fdp.Toolkit.Vis2D.Layers;

using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;

using ImGuiNET;

using FdpEntityInspectorPanel = Fdp.Presentation.Panels.EntityInspectorPanel;

using FdpEventBrowserPanel    = Fdp.Presentation.Panels.EventBrowserPanel;

using FdpRepositoryAdapter    = Fdp.Presentation.Adapters.RepositoryAdapter;

using FdpInspectorState       = Fdp.Presentation.Abstractions.InspectorState;

using Fdp.Presentation.Utils;

using Fdp.ModuleHost;

using Fdp.ModuleHost.Abstractions;

using Fdp.ModuleHost.Scheduling;

using Fdp.Core.Diagnostics;

using Fdp.ModuleHost.Diagnostics;


using Fdp.Network.Cyclone.Modules;

using Fdp.Network.Cyclone.Systems;

using DdsIdAllocator = Fdp.Network.Cyclone.Services.DdsIdAllocator;

using NodeIdMapper    = Fdp.Network.Cyclone.Services.NodeIdMapper;

using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Systems;

using Raylib_cs;

using rlImGui_cs;
using Hrot.Presentation.Facades;
using Hrot.ScenarioEditor.Rendering;
using Hrot.UI.Common.Facades;
using Fdp.Core.Serialization;



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

    /// <summary>⭐ CE-1289 — the map canvas (the status bar's cursor section reads its reading).</summary>
    public MapCanvas Canvas => _canvas;

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



    // -- Network enabled flag ? false when DDS libraries are unavailable (e.g. unit-test host)

    private bool _networkEnabled;

    // -- ClusterSlave (CGF1-S0104 / CMC-S016) ? wired in InitializeNetwork ------
    private Fdp.Toolkit.Orchestration.ClusterSlave? _clusterSlave;
    // CMC-S016: orchestration bus + slave translator (Option C).
    // ⛔ CE-164 — `_igOrchestrationBus` is GONE. There is ONE orchestration bus, `_context.EventBus`,
    //    swapped ONCE per frame at the end of Update() (see the swap there). Two swaps of one
    //    double-buffered bus in a frame would discard whatever was published between them.
    private Hrot.Core.Network.ISlaveOrchestrationTranslator?  _slaveTranslator;

    // ?? HrotNodeBuilder infrastructure context (EAM-M002) ?????????????????????
    private HrotNodeContext? _context;



    // -- IgNodeBootstrapper (SM-010) -------------------------------------------
    private IgNodeBootstrapper? _igBootstrapper;

    // -- Headless flag - set by InitializeEmbedded(); skips all Raylib/ImGui calls in Update/Draw

    private bool _headless;

    // -- Gizmo subsystem (GZ020) ---------------------------------------------------
    private DebugPrimitiveBuffer?       _gizmoBuffer;
    private GizmoRegistry?              _gizmoRegistry;
    private StatelessGizmoRegistry?     _statelessGizmoRegistry;
    private GizmoSettingsRegistry?      _gizmoSettingsRegistry;
    private MeasureToolGizmoAdapter?    _measureToolGizmoAdapter;
    private GizmoUndoStack?             _gizmoUndoStack;
    private GlobalGizmoManager?         _globalGizmoManager;
    private DataDrivenGizmoSystem?       _igDataDrivenGizmoSystem;

    /// <summary>
    /// ⭐⭐ <c>UXI-07</c> step 3b — this host's ONE tool arbiter, built by <c>MapInteractionPack</c> beside
    /// the two focus arbiters it reconciles. 🔒 <c>Q27-B</c>: one per map subsystem.
    /// </summary>
    private Hrot.ScenarioEditor.Tools.ToolController? _igToolController;

    /// <summary>⭐ <c>UXI-07</c> step 4b — the shared picker protocol (§4.12). ⚠ A RESOLVER for the
    /// controller: this field is built once, but <c>_igToolController</c> is assigned later.</summary>
    private Hrot.ScenarioEditor.Tools.PickerToolHost? _pickerToolsBacking;

    private Hrot.ScenarioEditor.Tools.PickerToolHost _pickerTools =>
        _pickerToolsBacking ??= new Hrot.ScenarioEditor.Tools.PickerToolHost(
            () => _igToolController, () => _globalGizmoManager);
    private FdpEventBus?                 _interactionBus;
    private GizmoExecutionController?    _gizmoController;
    // GZH-003: provides Phase-5 perspective switching with ref-counted gate.
    internal GizmoExecutionController GizmoController => _gizmoController!;
    // GZH-016: gate � false when another subsystem owns the map view.
    private Func<bool> _isActiveMapOwner = () => true;
    /// <summary>Set by <see cref="IgSubsystem.Initialize"/> from <c>SubsystemConfig.IsActiveMapOwner</c>.</summary>
    internal Func<bool> IsActiveMapOwner { set => _isActiveMapOwner = value; }


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

    /// <summary>
    /// ⭐⭐⭐ The node's LOCAL entity-creation request source (host (f)). IG's authoring tools enqueue
    /// INTENTS here instead of publishing <c>SpawnEntityCommand</c> ORDERS onto the event bus; the shared
    /// pipeline drains it, and <c>ForwardingEntityCreationRequestSource</c> decides — per request — whether
    /// this node services it or the NED egress sends it to the node that should.
    ///
    /// <para>⛔ <b>Read through to the pack, never a second instance.</b> The source is created by
    /// <c>EntityCreationPack.Build</c> and published by <c>IgNodeBootstrapper</c>; owning a parallel one
    /// here would be the duplicate-mechanism trap — the tools would fill a queue nothing drains.</para>
    /// 📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b.
    /// </summary>
    /// <summary>⭐ CE-515 — this node's entity-creation pack, for the subsystem to expose.</summary>
    internal Hrot.Common.EntityCreation.EntityCreation? EntityCreation => _igBootstrapper?.EntityCreation;


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







    /// <summary>
    /// Factory compiled once after the ECS world is created; translates layer-preset
    /// strings (e.g. <c>"road_graphs"</c>) into allocation-free <see cref="IEntityFilter"/> instances.
    /// </summary>
#pragma warning disable CS0649 // field assigned by future DI wiring
    private HrotEntityFilterFactory? _entityFilterFactory;
#pragma warning restore CS0649

    // _createEntityDdsWriter removed by D005 ? SpawnEntityCommandEgressTranslator owns the DDS writer.



    /// <summary>
    /// Cached query for all entities that carry <see cref="SelectionState"/>.
    /// Built once during canvas setup and reused in <see cref="OnCanvasWorldClick"/>
    /// to avoid per-click allocations (CT-2).
    /// </summary>
    private EntityQuery? _selectionStateQuery;

    // -- Drag tracking: world-space drop position set by OnEntityMoved --------------------------

    private System.Numerics.Vector2          _lastDragWorldPos;

    /// <summary>Phase 5: ECS system that translates gizmo interaction events into SelectionState mutations.</summary>
    private SelectionInteractionSystem? _selectionSystem;

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

    // -- Style and culling objects ??? updated and injected into modules

    private MapUserConfig     _userConfig     = null!;

    private MapCameraViewport _cameraViewport = null!;



    // -- ImGui UI panels (TASK-IF008) ------------------------------------------

    private DebugPanelState       _debugPanelState   = null!;

    private IgDebugPanel          _debugPanel        = null!;

    // ⭐ CE-1247 — the IG's "Entity Properties" card is the shared Details panel's Summary view now (EntitySummaryView).
    private Hrot.UI.Common.Panels.WaypointEditorPanel _waypointEditorPanel = null!;



    private PerformanceMetrics    _performanceMetrics = null!;

    private PerformanceOverlay    _performanceOverlay = null!;




    // Reference to the gizmo layer held here so DrawContextMenu can be called
    // from DrawUI inside the ImGui pass.
    private DebugGizmoLayer?      _gizmoLayer;



    // ?? FDP framework panels (Task 16) ????????????????????????????????????????????

    private FdpEntityInspectorPanel              _fdpEntityInspector = new();

    private FdpEventBrowserPanel                 _fdpEventBrowser    = null!;

    private DiagnosticEventHistoryService        _fdpEventHistory    = new();

    private FdpRepositoryAdapter?                _fdpRepoAdapter;

    private FdpInspectorState       _fdpInspectorState  = new();

    // ⛔ REMOVED (UXI-11 S-3): the "last known map selection" tracker. Its whole job was to make a
    //    per-frame poll of the map's component behave like a change event; the notification IS that
    //    event, published by the one writer for every cause. 📄 UX_Feature_Selection.md §2.7.4.

    /// ⭐ UXI-11 S-2 — IG's view over the ECS selection truth, and the one thing that writes it here.
    private Hrot.ScenarioEditor.Selection.EcsSelectionState? _igSelectionState;

    // Ensures context menu handlers are registered only once.
    private bool                    _fdpContextMenusWired;

    private uint                    _fdpFrameCount;

    // Map-pick bridge for component-editor map picking (created lazily after canvas init).
    private MapPickBroker?   _mapPickBroker;
    /// <summary>⭐ CE-3169 — the adapter under <see cref="_mapPickBroker"/>, also the menu orders' follow-up picks.</summary>
    private CanvasMapPickAdapter? _mapPickAdapter;
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

    // ?? Public panel accessors for window-manager registration ????????????????
    // Exposed so IgSubsystem can create ManagedWindow wrappers without needing
    // access to IgApplication's private fields.

    /// <summary>The IG debug panel (FPS counters, render overrides).</summary>
    public IgDebugPanel          DebugPanel          => _debugPanel;
    /// <summary>The IG waypoint editor panel.</summary>
    public Hrot.UI.Common.Panels.WaypointEditorPanel WaypointEditorPanel => _waypointEditorPanel;
    /// <summary>The IG performance overlay panel.</summary>
    public PerformanceOverlay    PerformanceOverlay   => _performanceOverlay;
    /// <summary>The FDP entity inspector panel.</summary>
    public FdpEntityInspectorPanel FdpEntityInspector => _fdpEntityInspector;

    /// <summary>⭐⭐ CE-1243 MB-4 — this host's map menus join the ONE menu bar as a contributor bound to <paramref name="perspective"/>.</summary>
    public void RegisterMainMenu(Fdp.Presentation.WindowManager.GlobalMenuRegistry menu, string perspective)
        => Hrot.ScenarioEditor.Map.MapInteractionPack.RegisterMainMenu(menu, "ig/map-menus", perspective,
               () => _gizmoLayer?.ConsumeMainMenu(), () => _interactionBus);

    /// <summary>⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-1) — THIS host's actions, made with the host: the map registers its
    /// actions on them, the inspector menu and the Details bundle request through them.</summary>
    public Hrot.Common.Interactions.HostActions HostActions { get; } = Hrot.ScenarioEditor.Map.SharedHostActions.Create();
    /// <summary>The FDP event browser panel.</summary>
    public FdpEventBrowserPanel  FdpEventBrowser      => _fdpEventBrowser;
    /// <summary>Getter for the FDP repository adapter (available after first DrawUI call).</summary>
    public FdpRepositoryAdapter? GetFdpRepoAdapter() => _fdpRepoAdapter;
    /// <summary>The FDP inspector state (selection tracking).</summary>
    public FdpInspectorState     FdpInspectorState    => _fdpInspectorState;
    /// <summary>The module host kernel used by this IG instance.</summary>
    public ModuleHostKernel Kernel => _kernel;
    /// <summary>Registry of entity-bound gizmo definitions. Available after InitializeEmbedded().</summary>
    public GizmoRegistry? GizmoRegistry => _gizmoRegistry;
    /// <summary>Gizmo primitive buffer used for rendering. Available after InitializeEmbedded().</summary>
    internal DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer;
    /// <summary>
    /// Map-pick bridge for component-editor map picking.
    /// Created lazily on first call after <see cref="InitializeEmbedded"/> sets up the canvas.
    /// Returns <see langword="null"/> before the canvas is available.
    /// </summary>
    public MapPickBroker? GetMapPickBroker()
    {
        if (_mapPickBroker == null && _canvas != null)
            _mapPickBroker = new MapPickBroker(_mapPickAdapter = new CanvasMapPickAdapter(_canvas, _world,
                    // ⭐ DESIGN_Map_Picking_Unification P2 — IG HOLDS both, so it passes both (the silent-default rule):
                    //    filter presets now narrow a pick, and a location pick is geodetic.
                    filterFactory: _entityFilterFactory ??= new HrotEntityFilterFactory(_world),
                    geoTransform:  _context?.GeoTransform,
                    globalGizmoManager: _globalGizmoManager,
                    // 🔒 UXI-07 step 4b — a RESOLVER: this adapter is built here, but _igToolController
                    //    is not assigned until the pack is built further down (:816). An instance would
                    //    be permanently null.
                    tools: () => _igToolController));
        return _mapPickBroker;
    }






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
        // Map layers handle ESC via IMapLayer.HandleKeyInput routed through MapCanvas,
        // ensuring it is consumed by the active layer and does not bubble to the main loop.
        Raylib.SetExitKey(KeyboardKey.Null);

        rlImGui.Setup(darkTheme: true);

        // Load Roboto TTF into Raylib and register it for gizmo text rendering.
        // Must be called after InitWindow (GL context must exist).
        LoadAndRegisterGizmoFont();

        InitializeEmbedded(domainIdOverride: domainIdOverride);

    }



    /// <summary>
    /// Loads the embedded Roboto TTF font into Raylib and registers it for gizmo text rendering.
    /// Must be called after <c>Raylib.InitWindow</c> so the GL context exists.
    /// Safe to call multiple times (idempotent at the Raylib level; last call wins for the static field).
    /// </summary>
    private static void LoadAndRegisterGizmoFont()
    {
        byte[] ttf = Fdp.Presentation.Fonts.EmbeddedFontResources.GetRobotoRegularTtfBytes();
        // 32-pixel base size gives crisp downscaling to 9–13 px gizmo labels.
        var font = Raylib.LoadFontFromMemory(".ttf", ttf, 32, null, 0);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
        GizmoMap.Presentation.DebugPrimitiveRenderer2D.TextFont = font;
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
        // ⭐ CE-495 — IG has no Message Log window, so a behaviour fault (which every node receives over
        //   the "BehaviorFault" topic into BehaviorFaultLog.Shared) is written to IG's normal log instead.
        //   🔒 User, 2026-10-01: "simply write message to its message log using normal nlog log write for
        //   the time being, nothing more required regarding notification". Unsubscribed in Shutdown, so a
        //   torn-down IG (tests create many) does not keep logging.
        if (!_loggingBehaviorFaults)
        {
            _loggingBehaviorFaults = true;
            AcquireBehaviorFaultLogging();
        }

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

        // Offset: the camera places it at the visible map's centre on its first update (CE-1441) — no host seeds it.



        _keyboardPanTarget = new Vector2(

            IgCameraConstants.InitialPositionX,

            IgCameraConstants.InitialPositionY);



        _canvas        = new MapCanvas(new RaylibInputProvider());

        _canvas.Camera = _camera;



        // Build core ECS infrastructure (EAM-M002)
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
        // ⭐ CE-4013 D13 — the one node config: IG now stages per node like every host (it took the BARE staging root)
        var igConfig = HrotNodeConfig.ForNode(_domainOverride ?? IgNetworkConstants.DdsDomain, _effectiveInstanceId, "IgApplication", shellParticipant);
        igConfig.SkipAllocatorRouting = true;   // IG creates its own DdsIdAllocator in InitializeNetwork; skip the builder's routing wait

        // Create objects that do NOT need a world (so the ApplicationSystemsRegistrar lambda can capture them).
        _userConfig          = new MapUserConfig();
        _cameraViewport      = new MapCameraViewport();

        // -- ImGui UI panels (TASK-IF008) -------------------------------------

        _debugPanelState    = new DebugPanelState(_userConfig);

        _debugPanel         = new IgDebugPanel(_debugPanelState);

        _waypointEditorPanel = new Hrot.UI.Common.Panels.WaypointEditorPanel(() => RouteWaypointGizmo.Current);   // ⭐ CE-1096 — moved to Presentation


        _performanceMetrics = new PerformanceMetrics();

        _performanceOverlay = new PerformanceOverlay(_performanceMetrics);


        _fdpEventBrowser = new FdpEventBrowserPanel(_fdpEventHistory);

        _igBootstrapper = new IgNodeBootstrapper(
            _networkFactory,
            _effectiveInstanceId,
            _headless,
            _igTranslatorsProvider,
            _userConfig,
            _cameraViewport,
            _fdpEventHistory,
            igConfig);
        // ⭐ CE-4078 / CE-1446 (R-1046) — the profile says the load waits for the 3-D map: the chain gets the Map3D part, polled from this node's map build.
        if (RunProfile.MapInLoad)
        {
            _igBootstrapper.ProfileLoadParts = new[] { Hrot.Map.Common.ClusterLoad.LoadPart.Map3D };
            _igBootstrapper.MapBuildStatus   = () => MapBuildStatus;
        }

        _igBootstrapper.ApplicationSystemsRegistrar = ctx =>
        {
            // EventHistoryCaptureSystem (moved from InitializeEcs)
            ctx.Kernel.RegisterGlobalSystem(
                new EventHistoryCaptureSystem("World", _fdpEventHistory, ctx.World.Bus));
            ctx.Kernel.RegisterGlobalSystem(
                new EventHistoryCaptureSystem("Orchestration", _fdpEventHistory, ctx.EventBus));

            // E. SlaveSyncController - unified slave that handles Continuous/Stepping transitions.
            // Must use ctx.EventBus (the same bus as the time translators),
            // NOT ctx.World.Bus which is swapped internally by the kernel.
            var timeController = new SlaveSyncController(ctx.EventBus, _effectiveInstanceId);
            ctx.Kernel.SetTimeController(timeController);

            // DO NOT call ctx.Kernel.RegisterModule(ctx.NedReplication) here.
            // Phase 6a+ already registered it. Double-registration corrupts the system schedule.


            // Gizmo subsystem (GZ020) - renders entity-bound diagnostic overlays.
            // ── UXI-23 S2b: the shared pack constructs the map's machinery ──────────────────────
            // 🔒 The pack CONSTRUCTS; IG still SCHEDULES (below, into ctx.Kernel).
            //
            // 🔴 This also removes a DOUBLE REGISTRATION. IG used to call BOTH the reflection registrar
            // (via Hrot.IG.Gizmos.GizmoRegistrar.Register) AND the source-GENERATED
            // Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll. CanvasContextMenuGizmo carries
            // [GizmoProjector], so reflection already finds it and the generated call registered it a
            // SECOND time — measured live as ContextMenuBinding 10 on IG against 9 on Scenario. The pack
            // does exactly one reflection pass.
            //
            // ⚠ ContributeExtras runs AFTER reflection and BEFORE the systems are built, which is the only
            // safe window: StatelessGizmoSystem sizes its visibility cache from registry.Rules.Count.
            var igMapInteraction = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
                new Hrot.ScenarioEditor.Map.MapInteractionContext
                {
                    World = ctx.World,
                    Host  = HostActions,   // ⭐ CE-1150 — the map registers its actions on the host's
                    // ⭐ CE-3123 — constructor services for reflected projectors. IG holds no BehaviorRegistry: its labels
                    //   carry the id and the hit points, not the behaviour name (has data = can draw).
                    Services = Hrot.ScenarioEditor.Map.MapServices.Of(ctx.GeoTransform),
                    // IG is a dumb terminal — draw all active gizmos, not just the selection's.
                    // ⛔ NOT drift: `null` is the documented policy ("an IG draws handles on
                    //   everything"), which is why UXI-11 did NOT default this in the pack.
                    IsSelectedPredicate = null,
                    // ⭐⭐⭐ UXI-11 — the pack builds this host's selection too.
                    Inspector = () => _fdpInspectorState,
                    // ⛔ R-3067 / AQ109 X4 — the IG no longer publishes its map click / selection to remote observers (ExCon is gone; MapClickEvent and SelectionChangedEvent are retired).
                    // GZH-003: IG is interactive and always has a window at startup. It is not driven by
                    // PerspectiveCoordinatorSystem when run standalone, so starting disabled would shut
                    // its gate permanently (§3.2d ①).
                    StartEnabled = true,
                    ContributeExtras = regs =>
                    {
                        // Phase 5: EntityDragGizmo makes entities draggable and emits pick spheres.
                        if (_igBootstrapper!.NetworkEnabled)
                        {
                            regs.Gizmos.Register(
                                new EntityDragGizmoDefinition(
                                    onDragCommitted: (entity, worldPos) =>
                                    {
                                        _lastDragWorldPos = worldPos;
                                        OnEntityDragEnded(entity);
                                    },
                                    // ⭐ AX-007 — IG owns almost nothing, so a drag is a request to the owner.
                                    writerFactory: Fdp.Toolkit.Replication.Attributes.EntityWriteRouter.For));
                        }
                        else
                        {
                            regs.Gizmos.Register(new EntityDragGizmoDefinition(
                                writerFactory: Fdp.Toolkit.Replication.Attributes.EntityWriteRouter.For));
                        }

                        // ⭐ CE-3123 — MissionPresentationGizmo is reflected now (its IGeographicTransform comes via Services).
                    },
                    // ⭐⭐⭐ UXI-07 step 4a — the SHARED Measure arm pulls IG's unit preference from here,
                    //   which is what let MeasureToolGizmoAdapter stop building a SECOND MeasureGizmo
                    //   just to push units onto it (ruling 9; §4.9c).
                    // ⚠ Resolved at CALL TIME: the adapter is constructed AFTER this pack.
                    MeasureUnits = () =>
                        _measureToolGizmoAdapter?.ReadUnits()
                            ?? Hrot.ScenarioEditor.Gizmos.MeasureDisplayUnits.Meters,
                    // ⭐⭐⭐ CE-1017 — the ENTITY-AUTHORING surface from the PACK, the same as Editor, CGF and SimHost:
                    //   the shared spawn adapter (also the toolbar's Spawn tool, which IG never serviced), the Add Entity
                    //   picker + submenu, the canvas menu. 📐 Equivalent to IG's own path: the shared adapter publishes
                    //   on the SAME bus (CE-3228) with OwnerAppInstanceId 0 — untargeted, exactly what
                    //   IgEntityCreationRequests.FromSpawnCommand writes. (R-3067 / AQ109 X3: the MapCommandController that serviced ExCon's
                    //   remote CMD_PLACE_ENTITY / CMD_START_AUTHORING is retired.)
                    EntityAuthoring = _headless ? null : new Hrot.UI.Common.AddEntity.EntityAuthoringInputs(
                        Tkb:          () => ctx.World.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                                            ? ctx.World.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null,
                        Requests:     () => EntityCreation?.World.Bus),   // ⭐ CE-3228 — the creation pack reads this world's bus
                });
            _igMapInteraction = igMapInteraction;
            // ⭐ CE-3169 — the menu orders' follow-up picks (a resolver: the adapter is built lazily, once the canvas exists).
            igMapInteraction.MenuOrders.PickService = () => { GetMapPickBroker(); return _mapPickAdapter; };

            _gizmoBuffer            = igMapInteraction.Buffer;
            _gizmoRegistry          = igMapInteraction.GizmoRegistry;
            _statelessGizmoRegistry = igMapInteraction.StatelessRegistry;
            _gizmoSettingsRegistry  = igMapInteraction.Settings;
            _interactionBus         = igMapInteraction.InteractionBus;
            _igDataDrivenGizmoSystem = igMapInteraction.DataDrivenSystem;
            _igToolController        = igMapInteraction.Tools;
            _gizmoUndoStack         = new GizmoUndoStack();

            // Phase 5: selection and drag handled by SelectionInteractionSystem + EntityDragGizmo.
            // Canvas no longer has a base tool for entity picking.

            // ⭐⭐⭐ UXI-11 — the gesture system comes from the PACK, and so does its network
            //    follow-through (MapInteractionContext.OnMapSelectionChanged, above). 📐 This host used
            //    to construct it here, as all five did.
            _selectionSystem = igMapInteraction.SelectionInteraction;

            // ⭐ UXI-11 S-4 — the adapter moved to Hrot.Presentation so CGF can use it too.
            ctx.Kernel.RegisterGlobalSystem(
                new Hrot.ScenarioEditor.Systems.SelectionInteractionSystemAdapter(_selectionSystem));

            // ⭐⭐⭐ UXI-11 S-2 — IG becomes a REQUESTER, so it needs the system that serves requests.
            // 📐 MEASURED 2026-09-20: ScenarioEditorModule (which registers the shared viewport systems)
            //    is registered by the EDITOR and CGF only. ⇒ ⛔ on IG, SelectEntityCommand had NO
            //    consumer at all — the same silent no-op CE-051 found on the other hosts and fixed
            //    there. Publishing a request here without this line would have recreated it exactly.
            // ⭐ The view is a read-through handle over the SelectionState component (S-1), which is
            //   what IG already used directly, so this changes WHO writes, not WHAT is written.
            _igSelectionState = igMapInteraction.Selection;
            // ⭐⭐⭐ UXI-11 S-3 — IG's inspector is a requester too, now that this host serves requests.
            _fdpEntityInspector.Selection = _igSelectionState;
            _fdpEntityInspector.RequestSelectionChange =
                req => ctx.World.Bus.PublishManaged(req);
            // ⭐⭐⭐ UXI-11 — SCHEDULE what the pack built, in the order it hands back. 🔒 "The pack
            //    CONSTRUCTS; the host SCHEDULES" — untouched; what moved is the construction.
            foreach (var selectionSystem in igMapInteraction.SelectionSystemsInOrder)
                ctx.Kernel.RegisterGlobalSystem(selectionSystem);

            // BATCH-29: GlobalGizmoManager manages non-entity-bound gizmos (picker, measure).
            _globalGizmoManager = igMapInteraction.GlobalManager;

            // ⭐ CE-3228 — the shared spawn adapter publishes its requests on ctx.World.Bus; the creation pack must exist to read them.
            if (_igBootstrapper!.NetworkEnabled && ctx.Participant != null && EntityCreation is null)
                throw new InvalidOperationException(
                    "The entity-creation pack has not been composed yet. RegisterSpawningPipeline "
                    + "must run before RegisterApplicationSystems (SharedApplicationBootstrapper "
                    + ":111 vs :139); if that order changed, IG's entity-authoring surface has no reader.");

            // UXI-23 S2b: both come from the pack.
            // 🔒 UXI-07 step 4a — the arbiter is PASSED, so the settings checkbox ARMS THROUGH it and
            //    follows it back down when another tool displaces Measure (the dead-toggle fix, §4.9c).
            _measureToolGizmoAdapter = new MeasureToolGizmoAdapter(
                _globalGizmoManager, _gizmoSettingsRegistry, _igToolController);
            // ⭐ R3 (DESIGN_Gizmo_Renderer_Seam.md §6) — no world is passed. The layer's old
            //   `view` parameter was stored nowhere; EntityLocal resolves through SpatialAnchor
            //   primitives instead (.dev/_DONE/gizmos-1/feedback2.md:798). Named arguments because the
            //   two constructors collapsed into one.
            // ⭐ CE-1033 — the shared attach: gizmo layer + draw buffer + the 3-D mode, the same on every host.
            var mapLayers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(
                _canvas, _gizmoBuffer!, _interactionBus, () => _world, map3DBuildPolicy: RunProfile.MapBuildPolicy);   // ⭐ CE-1446 — Eager because the profile says MapInLoad
            var gizmoLayer = mapLayers.GizmoLayer;
            _map3D = mapLayers.Map3D;
            _gizmoLayer = gizmoLayer;
            // Route gizmo interaction translators and publisher through the network factory
            // so that IgApplication has no direct dependency on Hrot.Network.NED.
            CycloneNetworkIngressSystem? gizmoIngress = null;
            CycloneEgressSystem? gizmoEgress = null;
            if (_networkFactory != null)
            {
                var gizmoTranslators = _networkFactory.CreateGizmoTranslators(_interactionBus!, _effectiveInstanceId, _headless);
                var ingressList = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
                var egressList  = new System.Collections.Generic.List<Fdp.Interfaces.INetworkTranslator>();
                foreach (var t in gizmoTranslators)
                {
                    if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Ingress) != 0) ingressList.Add(t);
                    if ((t.Direction & Fdp.Interfaces.TranslatorDirection.Egress)  != 0) egressList.Add(t);
                }
                if (ingressList.Count > 0)
                    gizmoIngress = new CycloneNetworkIngressSystem(ingressList.ToArray());
                if (egressList.Count > 0)
                    gizmoEgress = new CycloneEgressSystem(egressList.ToArray());
                var publisherSystem = _networkFactory.CreateGizmoPublisherSystem(_gizmoBuffer!, _effectiveInstanceId);
                if (publisherSystem != null)
                    ctx.Kernel.RegisterGlobalSystem(publisherSystem);
            }
            // UXI-23 S2b: the group, its three members and the gate come from the pack.
            _gizmoController = igMapInteraction.Gate;
            // ⭐⭐ UXI-23 S3: report anything constructed but not scheduled (§3.2e).
            foreach (string problem in igMapInteraction.Unserviceable(igMapInteraction.InteractionSystems))
                Fdp.Core.Logging.FdpLog<IgApplication>.Info("[Map] {0}", problem);
            ctx.Kernel.RegisterModule(new GizmoInteractionModule(
                _interactionBus!,
                // ⭐ CE-3169 — the map's own menu clicks arrive as GizmoMenuActionEvent; without the ingress none of them ever became
                //   a GlobalActionRequestedEvent, so the dispatcher below ran nothing (pins, AI trace, layer control, the orders).
                //   📐 The ENTITY map is ctx's: this callback runs inside BootstrapNode, before _entityMap is assigned.
                contextIngress: new Hrot.Common.Systems.ContextActionIngressSystem(ctx.EntityMap, _interactionBus!),
                // ⭐ the action dispatcher + the group: IG now RUNS map-menu actions (it had no dispatcher).
                interactionSystems: igMapInteraction.InteractionSystems,
                gizmoIngress: gizmoIngress,
                gizmoEgress:  gizmoEgress));
            ctx.Kernel.RegisterGlobalSystem(
                new EventHistoryCaptureSystem("Interaction", _fdpEventHistory, _interactionBus!));
            // Register canvas menu update so CanvasContextMenuGizmo has state to project.
            // ⭐ CE-1017 — the pack's canvas menu (with Add Entity on a windowed IG).
            ctx.Kernel.RegisterGlobalSystem(igMapInteraction.CanvasMenu);
        };

        _context = _igBootstrapper.BootstrapNode(igConfig, NodeRole.Map2D, _networkFactory);
        // ⭐⭐⭐ CE-1445 (R-1044, §11.8) — the player's run controls come from the SAME factory as every ECS host's: the Player gate (waits only on ITS OWN readiness), a loading / waiting screen over it, no Cancel.
        //   ⭐ SuppressTimeControl is the role's EXPLICIT suppression: the IG issues no ticks (Q54/D4), so the composer registers no toolbar time control over it.
        RunControls = Hrot.UI.Common.RunMode.NodeRunControls.For(_context, RunProfile);

        _world     = _context.World;
        _entityMap = _context.EntityMap;
        // ⭐ CE-3169 — the map's menu orders read the node's entity map from the world singleton. ⭐ CE-4013 D5 — HrotNodeBuilder publishes the node's entity map, geo transform and TKB on every world; not repeated here.
        _kernel    = _context.Kernel;
        _geoTransform = _context.GeoTransform as WGS84Transform;
        _ghostCreationSystem = _context.GhostCreationSystem;

        _networkEnabled      = _igBootstrapper.NetworkEnabled;
        _networkAdapter      = _igBootstrapper.NetworkAdapter;
        _commandGateway      = _igBootstrapper.CommandGateway;
        _clusterSlave        = _context.ClusterSlave;
        // ⭐⭐⭐ CE-164 — the node's OWN slave translator, from the context, exactly as SimHostApp:504 does.
        //    ⛔ Was `_igBootstrapper.IgSlaveTranslator` — a second, ingress-only translator on a second bus.
        //    📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1b.
        _slaveTranslator     = _context.SlaveTranslator;

        _entityFilterFactory = new HrotEntityFilterFactory(_world);
        // Cache SelectionState query once to avoid per-click allocations (CT-2).
        _selectionStateQuery = _world.Query().With<SelectionState>().WithLifecycle(EntityLifecycle.All).Build();

        var igEntityService = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(_world, _entityMap);
        _fdpEntityInspector.ExtractionService = igEntityService;


    }



    public EntityRepository World => _world;



    // -------------------------------------------------------------------------




    /// <summary>⭐ CE-4013 D7 — IG's own pre-kernel work, run by <see cref="HrotNodeContext.Tick"/> after the cluster ticks.</summary>
    private void FrameInput(float dt)
    {
        if (!_headless)

        {

            // ⭐ CE-1446 (R-1046) — the 3-D map's build is pumped HERE, ungated: the canvas update below runs only for the active map owner and only while ImGui is not capturing the mouse
            //   (the waiting screen's modal does), and the player's load waits on this build.
            _map3D?.Update(dt);

            // Gate map input when ImGui is consuming the mouse (TASK-IF008).

            if (_isActiveMapOwner() && !ImGui.GetIO().WantCaptureMouse)

            {

                HandleCameraInput(dt);

                _measureToolGizmoAdapter?.Update();

                _canvas.Update(dt);

            }

            HandleGizmoUndoInput();



            // Project screen corners to world space and feed MapCullingSystem.

            var topLeft     = _camera.ScreenToWorld(Vector2.Zero);

            var bottomRight = _camera.ScreenToWorld(new Vector2(WindowWidth, WindowHeight));

            _cameraViewport.WorldMinX = MathF.Min(topLeft.X, bottomRight.X);

            _cameraViewport.WorldMaxX = MathF.Max(topLeft.X, bottomRight.X);

            _cameraViewport.WorldMinY = MathF.Min(topLeft.Y, bottomRight.Y);

            _cameraViewport.WorldMaxY = MathF.Max(topLeft.Y, bottomRight.Y);

            _cameraViewport.Zoom      = _camera.Zoom;

        }



        // Always tick ECS/network ? even in headless mode DDS messages must be processed.

        // Clear the primitive buffer before backend ECS systems populate it.
        // Must be called after canvas input (which emits tool primitives) and before
        // kernel.Update() (which runs StatelessGizmoSystem in PostSimulation).
        _gizmoBuffer?.EndFrame(dt);

    }

    /// <summary>

    /// Advances one frame of IG logic (input, ECS tick, viewport update).  

    /// Must be called before <see cref="DrawWorld"/> and <see cref="DrawUI"/> each frame.

    /// Called by both the standalone <see cref="Run"/> loop and the embedded orchestrator.

    /// </summary>
    public void Update(float dt)

    {

        _frameDt = dt;
        // ⭐⭐ CMC-S016 / CE-164: translator tick BEFORE clusterSlave so DDS→bus ingress is processed
        //    first — verbatim the SimHostApp:560-561 order, on the node's ONE orchestration bus.
        // ⛔ The `SwapBuffers()` that stood on the first line is GONE: it swapped IG's second bus, and
        //    swapping the shared bus HERE as well as at the end of Update() would double-swap it,
        //    discarding anything published in between. 📄 DESIGN_Subsystem_Composition_Unification §4.1b.
        // ⭐ CE-4013 D7 — the one node frame: translator, slave (CMC-S016 / CE-164, on the node's ONE orchestration bus), this host's
        //   own pre-kernel work (map input, the camera's viewport, the gizmo frame close), the kernel — always, even headless —, then
        //   the reset read while this frame's events are readable, and the one swap.
        if (_context is null || _clusterSlave is null) return;   // not yet up, or shut down (the slave is disposed and nulled first)
        _context.Tick(
            beforeKernel: () =>
            {
                RunControls?.Update();   // ⭐ CE-1445 — the run choice hears this frame's cluster state (before the swap, as every host's adapter does)
                FrameInput(dt);
            },
            afterKernel: () =>
            {
                // Clear gizmo undo stack on world/scenario reset.
                foreach (ref readonly var _ in _world.ReadEvents<WorldResetEvent>())
                    _gizmoUndoStack?.Clear();
            });

        _fdpFrameCount++;



        // ⛔ R-3067 / AQ109 X3 — the remote-map receive path is GONE: no MapCommandRequest poll (Task 5a), no MapInteractionConfig poll (Task 5b), and no
        //   entity-ack forward to the retired MapCommandController. 📄 docs/blueprints/Architect_Question_109_Retire_ExCon.md §6.3.

        if (!_headless)

        {

            // Update UI panel states (TASK-IF008).

            _debugPanelState.CurrentSimTime   = _kernel.CurrentTime.TotalTime;
            _debugPanelState.CurrentWallTicks = _kernel.CurrentTime.TotalWallTicks;

            _performanceMetrics.Snapshot(_world, Raylib.GetFPS(), Raylib.GetFrameTime() * 1000f);


            // ⭐⭐⭐ UXI-11 S-3 — the "Task 43/46 one-directional map -> inspector sync" is GONE.
            //    📄 UX_Feature_Selection.md §2.7.4 listed this hand-sync for retirement;
            //    SelectionNotificationSystem now drives _fdpInspectorState from the ANNOUNCEMENT.
            // ⭐⭐ Why that is better and not merely tidier: this block polled GetSelectedEntity(),
            //    i.e. the MAP's component, so it followed a map click and nothing else -- and it
            //    deliberately refused to clear, to protect a list-made selection from being wiped.
            //    🔒 Ruling ① (2026-09-10) removes the premise: there is ONE selection per host, so a
            //    list selection and a map selection are the same thing and there is nothing to protect.
            // ⚠ BEHAVIOUR CHANGE, stated rather than buried: clearing the map selection now clears
            //    the inspector too. That is what "unified behavior, every host" means.

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

        DrawDebugOverlay();

    }



    /// <summary>

    /// Renders ImGui panels.  

    /// Must be called inside <c>rlImGui.Begin()</c>.

    /// No-op in headless mode.

    /// </summary>

    /// <summary>⭐ CE-1017 — the shared map pack's result, kept for its entity-authoring surface (picker, spawn adapter).</summary>
    private Hrot.ScenarioEditor.Map.MapInteraction? _igMapInteraction;

    public void DrawUI()

    {
        _igMapInteraction?.DrawOperatorUi();   // ⛔ OpenPicker only queues (CE-1017) · ⭐ CE-1059 the order form

        _fdpRepoAdapter ??= new FdpRepositoryAdapter(_world);

        if (!_fdpContextMenusWired)
        {
            _fdpContextMenusWired = true;
            _fdpEntityInspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
            {
                // ⭐ CE-1134 — "Center on entity" / "Select entity" were here: the map's entity menu (Center View, 2-D and 3-D;
                //   Select; Properties…) now heads this menu on every host (MapEntityMenuHandler, DiagnosticsWindowsBundle).

                // "Edit Overlay" ? only shown for area entities that carry an EditablePolyline.
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
        // them ? only call Draw() here in standalone mode.
        if (!_panelsWindowManaged)
        {
            _debugPanel.Draw();
            _waypointEditorPanel.Draw(IgPanelColors.Push, IgPanelColors.Pop);   // ⭐ CE-1096 — the shared panel, the IG's colours
        }

        if (!_panelsWindowManaged)
        {
            _performanceOverlay.Draw();
        }

        _gizmoLayer?.DrawContextMenu();
        _gizmoLayer?.DrawStructInspector();

        // ⭐ CE-1243 — the map's menus are a contributor of the one menu bar (RegisterMainMenu, from IgSubsystem).

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

        // panDir is in WORLD space (the camera target is a world point).

        Vector2 panDir = Vector2.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.Right)) panDir.X += 1f;

        if (Raylib.IsKeyDown(KeyboardKey.Left))  panDir.X -= 1f;

        // ⭐ CE-1040 — the 2-D map is north-up: the Up arrow moves the view NORTH (+Y in the world the target lives in).
        if (Raylib.IsKeyDown(KeyboardKey.Up))    panDir.Y += 1f; // north

        if (Raylib.IsKeyDown(KeyboardKey.Down))  panDir.Y -= 1f; // south



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

        // Simulate a single wheel tick so the same 1.2+O factor is applied.

        bool zoomIn  = Raylib.IsKeyPressed(KeyboardKey.Equal)

                    || Raylib.IsKeyPressed(KeyboardKey.KpAdd);

        bool zoomOut = Raylib.IsKeyPressed(KeyboardKey.Minus)

                    || Raylib.IsKeyPressed(KeyboardKey.KpSubtract);



        Vector2 mousePos = Raylib.GetMousePosition();

        if (zoomIn)  _camera.ProcessInput(1.0f,  mousePos, false, false);

        if (zoomOut) _camera.ProcessInput(-1.0f, mousePos, false, false);

    }

    private void HandleGizmoUndoInput()
    {
        if (_gizmoUndoStack == null) return;
        if (ImGui.GetIO().WantCaptureKeyboard) return;

        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl)
                 || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (!ctrl) return;

        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift)
                  || Raylib.IsKeyDown(KeyboardKey.RightShift);

        if (Raylib.IsKeyPressed(KeyboardKey.Z) && !shift)
        {
            if (_gizmoUndoStack.CanUndo)
            {
                var cmd = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetCommandBuffer();
                _gizmoUndoStack.Undo(cmd);
                cmd.Playback(_world);
            }
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Y) || (Raylib.IsKeyPressed(KeyboardKey.Z) && shift))
        {
            if (_gizmoUndoStack.CanRedo)
            {
                var cmd = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetCommandBuffer();
                _gizmoUndoStack.Redo(cmd);
                cmd.Playback(_world);
            }
        }
    }

    // -------------------------------------------------------------------------






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

    /// <summary>⭐ CE-495 — whether THIS IG holds a reference on the process-wide fault logging.</summary>
    private bool _loggingBehaviorFaults;

    // ⭐ CE-495 — ONE subscription per PROCESS, reference-counted across IG instances. ⚠ The fault log is a
    //   process-wide singleton and NLog is process-wide, so a subscription per IG wrote every fault once per
    //   live IG — measured: 2 lines for 1 fault with two IGs alive (also the `--mode all` case).
    private static readonly object s_faultLogLock = new();
    private static int s_faultLogRefs;

    private static void AcquireBehaviorFaultLogging()
    {
        lock (s_faultLogLock)
            if (s_faultLogRefs++ == 0)
                Fdp.Toolkit.Behavior.Events.BehaviorFaultLog.Shared.OnMessageAdded += WriteBehaviorFaultToLog;
    }

    private static void ReleaseBehaviorFaultLogging()
    {
        lock (s_faultLogLock)
            if (--s_faultLogRefs == 0)
                Fdp.Toolkit.Behavior.Events.BehaviorFaultLog.Shared.OnMessageAdded -= WriteBehaviorFaultToLog;
    }

    /// <summary>
    /// ⭐ CE-495 — one behaviour fault, as a plain NLog error on IG's log. The row is already de-duplicated
    /// and formatted by <c>BehaviorFaultLog</c> (<i>"entity N (node M): behaviour 'X' FAULTED (Code): message"</i>).
    /// </summary>
    internal static void WriteBehaviorFaultToLog(Fdp.Core.Logging.MessageLogEntry entry)
        => FdpLog<IgApplication>.Error("[BehaviourFault] {0}", entry.Message);

    /// <summary>Test hook: true while this IG is subscribed to behaviour faults.</summary>
    internal bool TestHook_IsLoggingBehaviorFaults => _loggingBehaviorFaults;

    /// Pass <c>ownsWindow = false</c> when the orchestrator owns the Raylib window.

    /// </summary>

    public void Shutdown(bool ownsWindow = true)

    {
        if (_loggingBehaviorFaults)
        {
            _loggingBehaviorFaults = false;
            ReleaseBehaviorFaultLogging();
        }

        RunControls = null;
        _map3D = null;
        _clusterSlave?.Dispose();
        _clusterSlave = null;

        // _networkAdapter.Dispose() disposes the command gateway and all DDS writers/readers.
        _networkAdapter?.Dispose();
        _networkAdapter = null;
        _commandGateway = null;

        // QA-001: dispose the whole node context — kernel THEN world. This used to be
        // `_kernel?.Dispose()`, which leaked the EntityRepository on every IG teardown.
        _context?.Dispose();

        if (ownsWindow)

        {

            rlImGui.Shutdown();

            Raylib.CloseWindow();

        }

    }



    // -- Task 5: IG-to-ExCon event translators ----------------------------------


    /// <summary>
    /// ⭐⭐ <b>This node's <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/></b> — the control-plane
    /// endpoint that commits cluster-state transitions for IG and holds
    /// <see cref="Fdp.Toolkit.Orchestration.ClusterSlave.LocalClusterState"/>.
    ///
    /// <para>⛔ <b>Not a test hook</b> — <c>CE-163</c> makes the debug API read it in production, exactly
    /// as it reads <see cref="OrchestrationBus"/>. ⭐ Named and shaped to match
    /// <c>CgfApplication.ClusterSlave</c> and <c>SimHostApp.ClusterSlave</c>, so the three ECS nodes
    /// present one member to the one shared projection. ⚠ Read it, never latch it: <see langword="null"/>
    /// before <c>InitializeNetwork</c> *(e.g. headless tests without DDS)* and after <c>Shutdown</c>.</para>
    /// </summary>
    internal Fdp.Toolkit.Orchestration.ClusterSlave? ClusterSlave => _clusterSlave;

    /// <summary>
    /// Internal test hook: exposes the <see cref="Fdp.Toolkit.Orchestration.ClusterSlave"/>
    /// for handler-registration assertions (CGF1-S0104 / A.2).  <c>null</c> when
    /// <see cref="InitializeNetwork"/> was not called (e.g. headless tests without DDS).
    ///
    /// <para>⭐ Now a forwarder to <see cref="ClusterSlave"/> — one member, two names, no second field.</para>
    /// </summary>
    internal Fdp.Toolkit.Orchestration.ClusterSlave? TestHook_ClusterSlave => ClusterSlave;

    /// <summary>
    /// ⭐⭐ <b>This node's CONTROL-PLANE bus</b> — the one its <c>ClusterSlave</c> and
    /// <c>ClusterOpEgressTranslator</c> sit on *(`NodeBootstrapper:194-200`, shared by SimHost · CGF · IG)*, so
    /// a <c>TransitionStateIntent</c> published here reaches the master over DDS. 📄 <c>HN-029</c>.
    /// <para>⛔ Not a test hook — the debug API's <c>scenario/load/*</c> uses it in production. ⚠ Read it, never
    /// latch it: <see langword="null"/> before <c>Initialize</c> and after <c>Shutdown</c>.</para>
    /// <para>⭐ Note IG offers this while offering NO clock *(<c>drive: null</c>)* — 📌 the capabilities are
    /// genuinely independent.</para>
    /// </summary>
    internal Fdp.Core.FdpEventBus? OrchestrationBus => _context?.EventBus;

    /// <summary>
    /// ⭐⭐ <c>CE-1445</c> / <c>CE-1446</c> — what makes the IG a PLAYER (R-1044): its kind (Player gate), the toolbar time control suppressed, and — R-1046 — <c>MapInLoad</c>: its load waits for the 3-D map, so the map builds eagerly.
    /// ONE statement: the map's policy below and the run controls both read this profile.
    /// </summary>
    internal static readonly Hrot.UI.Common.RunMode.NodeRunProfile RunProfile = new(
        Hrot.UI.Common.RunMode.RunHostKind.Ig, Control: null, SuppressTimeControl: true, MapInLoad: true);

    private Fdp.Toolkit.Vis3D.Map3DBuild? _map3D;

    /// <summary>
    /// ⭐ CE-1446 (R-1046) — the IG's 3-D map build as the load chain polls it (<c>IsBuilt</c> per load, <c>Failure</c>, <c>Step</c>); <see langword="null"/> before the map is attached. Reached from the node as
    /// <c>IgSubsystem.MapBuildStatus</c> → here. The map builds <see cref="Fdp.Toolkit.Vis3D.Map3DBuildPolicy.Eager"/> and is pumped every frame, whoever owns the map and whatever the mouse is over.
    /// </summary>
    internal Fdp.Toolkit.Orchestration.IMapBuildStatus? MapBuildStatus => _map3D;

    /// <summary>⭐ CE-1445 — this node's run controls (transport + choice, Player policy), from <c>NodeRunControls.For</c>; <see langword="null"/> before <c>InitializeEmbedded</c> built the context and after <c>Shutdown</c>.</summary>
    internal Hrot.UI.Common.Adapters.ClusterTimeTransportAdapter? RunControls { get; private set; }

    /// <summary>⭐ The node's context — what the ONE ECS-node debug surface (<c>EcsNodeDebugProvider</c>) derives from.</summary>
    internal Hrot.Common.Infrastructure.HrotNodeContext? NodeContext => _context;

    /// <summary>Current kernel sim time in seconds ? available in both headless and normal mode.</summary>
    internal double TestHook_CurrentSimTime => _kernel.CurrentTime.TotalTime;

    /// <summary>⭐ P5a — IG's recording controller, for the record-and-archive rail.</summary>
    internal Hrot.SimHost.Modules.Orchestration.EcsRecordReplayController? TestHook_RecordReplayController
        => _igBootstrapper?.RecordReplayController;

    /// <summary>
    /// Exposes the <see cref="Hrot.Common.Abstractions.INedReplicationModule"/> wired during
    /// <see cref="InitializeNetwork"/>.  Used by MODINIT-S302 SC6 tests to confirm
    /// <c>DriveFromNetwork == true</c> for the <c>ImageGenerator</c> role.
    /// </summary>
    internal Hrot.Common.Abstractions.INedReplicationModule? TestHook_NedReplication
        => _context?.NedReplication;

    /// <summary>⭐ CE-276 — the node's NED replication module (for the ai-debug ownership surface's descriptor
    /// map). Same handle as the test hook above, under a production-appropriate name.</summary>
    internal Hrot.Common.Abstractions.INedReplicationModule? NedReplication => _context?.NedReplication;




    /// <summary>

    /// Internal test hook to simulate an operator click directly on a network entity,

    /// causing IG to publish <c>SelectionChangedEvent</c> with that entity selected.

    /// No-op if the entity is not found in the IG entity map.

    /// </summary>









    /// <summary>

    /// Internal test hook: simulates a Shift+Right-Click at the given world position.

    /// Exposes the private <c>OnCanvasWorldClick</c> handler for unit tests.

    /// </summary>

    internal void TestHook_SimulateShiftRightClick(System.Numerics.Vector2 worldPos)

        => OnCanvasWorldClick(worldPos, MapMouseButton.Right, shift: true, ctrl: false, hit: Entity.Null);

    /// <summary>

    /// Internal test hook: simulates a plain (non-shift) right-click.

    /// </summary>

    internal void TestHook_SimulatePlainRightClick(System.Numerics.Vector2 worldPos)

        => OnCanvasWorldClick(worldPos, MapMouseButton.Right, shift: false, ctrl: false, hit: Entity.Null);

    /// <summary>

    /// Internal test hook to expose the shared NetworkEntityMap.

    /// </summary>

    internal NetworkEntityMap TestHook_EntityMap => _entityMap;

    /// <summary>
    /// Internal test hook: activates the route waypoint gizmo for the given
    /// network entity ID (same code path as a CMD_START_AUTHORING command).
    /// Used by commit-handler safety tests (CT-1).
    /// </summary>
    internal void TestHook_ActivateRouteEditToolForNetworkId(long networkEntityId)
        => ActivateAreaEditingTool(networkEntityId);

    /// <summary>
    /// Internal test hook: returns the currently active <see cref="RouteWaypointGizmo"/>,
    /// or <see langword="null"/> when no route waypoint gizmo is active.
    /// Used by commit-handler safety tests (CT-1).
    /// </summary>
    internal Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo? TestHook_ActiveRouteEditTool
        => Hrot.ScenarioEditor.Gizmos.RouteWaypointGizmo.Current;




    /// <summary>Test hook: the current camera keyboard-pan target (set by CenterCameraOn).</summary>
    internal Vector2 TestHook_KeyboardPanTarget => _keyboardPanTarget;

    // ?? Ground clamping (MOD1-P7T5) ???????????????????????????????????????????

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
        // Permanent identity component ? drives GhostPromotionSystem.
        cmd.AddComponent(entity, new TkbIdentity { TkbType = tkbType });
        _world.SetDisType(entity, new DISEntityType { Value = disTypeValue });
        cmd.Playback(_world);
    }



    /// </summary>




    private void OnCanvasWorldClick(Vector2 worldPos, MapMouseButton button, bool shift, bool ctrl, Entity hit)

    {

        if (button != MapMouseButton.Right)

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

        // ⛔ R-3067 / AQ109 X3 — a plain right-click used to open ExCon's pushed menu here (ContextMenuSystem). The map's menu is the shared
        //   MapEntityMenuHandler path, driven by the gizmo layer on every host (R-1019), so there is nothing left for this handler to open.

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
        if (!_networkEnabled || _commandGateway == null) return;

        var view = (ISimulationView)_world;
        if (!view.HasComponent<NetworkIdentity>(entity)) return;

        long netId = view.GetComponentRO<NetworkIdentity>(entity).Value;

        // ⭐ CE-4073 — local ENU metres (keys x / y / z; they were geodetic lat / lon / alt, converted here and back at the NED boundary).
        //   A 2D map drag: Z is the ground reference 0.
        double x = worldPos.X, y = worldPos.Y, z = 0.0;

        // Encode as JSON for the neutral UpdateEntityDescriptorCommand; NedCommandGateway
        // parses it back to the wire type (WorldPos) via NedTranslationHelper.
        var descJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            type     = "WorldPos",
            entityId = (int)netId,
            x,
            y,
            z,
            time     = System.DateTime.UtcNow.Ticks,
        }, FdpJsonOptionsRegistry.DefaultRelaxed);
        var cmd = new Hrot.Core.Network.UpdateEntityDescriptorCommand
        {
            EntityId       = (int)netId,
            DescriptorJson = descJson,
            BaseVersion    = 0,
        };
        _ = _commandGateway.SendUpdateDescriptorAsync(cmd);

        FdpLog<IgApplication>.Info(
            "[Node-{0}] GeoSpatial update: sent UpdateEntityDescriptorCommand for NetID {1} to ({2:F1}, {3:F1}) m.",
            _effectiveInstanceId, netId, x, y);
    }



    /// <summary>

    /// Internal test hook to simulate a drag-end for the entity with the given network ID.

    /// Directly calls <see cref="OnEntityDragEnded"/> ??? requires the entity to already

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

    /// <summary>⭐ R-3067 / AQ109 X3 — the node's command gateway (create-entity / descriptor / mission requests), for the integration rails that drive the
    /// IG's gateway directly now that the Mini ExCon panel that used to be the only UI for it is gone.</summary>
    internal Hrot.Core.Network.ICommandGateway? TestHook_CommandGateway => _commandGateway;

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


































    /// <summary>
    /// Activates gizmo-based editing for the entity identified by
    /// <paramref name="networkEntityId"/> using direct gizmo injection.
    ///
    /// <para>
    /// For <see cref="Hrot.Map.Common.Components.RoutePlan"/> entities, injects a
    /// <see cref="RouteWaypointGizmo"/> via <c>DataDrivenGizmoSystem.ActivateGizmo</c>.
    /// Toggle behaviour: calling this a second time on the same entity deactivates the gizmo.
    /// </para>
    ///
    /// <para>
    /// For <see cref="EditablePolyline"/> entities, injects a <see cref="VertexEditGizmo"/>.
    /// Toggle behaviour likewise.
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

        // ⭐⭐⭐ UXI-07 step 3b — ACTIVATE the shared Route/Edit tools through the host's ONE arbiter.
        // 🔴 This method used to carry verbatim copies of both shared arms — the HasInjectedGizmo toggle,
        //    the RouteWaypointGizmo / VertexEditGizmo construction, the DeactivateGizmo callback — one of
        //    FIVE `D′` instances measured 2026-09-09. ⇒ deleted; MapInteractionPack registered the real
        //    ones, and the toggle now lives in exactly one place (ruling 9).
        // ⭐ Going through the controller makes these tools MODAL on IG for the first time: arming one
        //    cancels whatever held focus in the OTHER arbiter, which a direct ActivateGizmo cannot do.
        if (_igToolController == null)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: this host wired no ToolController (pass MapInteraction.Tools).",
                _effectiveInstanceId);
            return;
        }

        // 🔒 IG-ONLY GUARD, DELIBERATELY KEPT AT THE CALL SITE (R-137 — unification may not cost a
        //    capability). IG receives entities over the network, so an EditablePolyline/RoutePlan can
        //    arrive BEFORE its SimTransform; the editor and CGF author locally and never see that window.
        //    ⛔ Pushing this into the shared arm would make the editor refuse a legitimately
        //    transform-less shape. ⭐ Q27's standing ruling allows exactly this: "differences are data
        //    availability or host rules, never set membership."
        bool isRoute = World.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(entity);
        bool isArea  = World.HasManagedComponent<EditablePolyline>(entity);
        if (!isRoute && !isArea)
        {
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: entity {1} has no EditablePolyline.", _effectiveInstanceId, networkEntityId);
            return;
        }
        if (!World.HasComponent<SimTransform>(entity) && !_igDataDrivenGizmoSystem!.HasInjectedGizmo(entity))
        {
            // ⚠ Only blocks ARMING. A toggle-OFF must still work on an entity whose transform went away,
            //   or the gizmo would be unkillable from the UI.
            FdpLog<IgApplication>.Warn(
                "[Node-{0}] ActivateAreaEditingTool: entity {1} has no SimTransform yet.", _effectiveInstanceId, networkEntityId);
            return;
        }

        string toolId = isRoute
            ? Hrot.ScenarioEditor.Tools.ScenarioToolIds.Route
            : Hrot.ScenarioEditor.Tools.ScenarioToolIds.Edit;
        _igToolController.Activate(toolId, entity);

        FdpLog<IgApplication>.Info(
            "[Node-{0}] {1} tool activated for NetID {2}.",
            _effectiveInstanceId, isRoute ? "Route editing" : "Area editing", networkEntityId);
    }





    // ?? Ghost entity cleanup ??????????????????????????????????????????????????????

    // ⛔ The SelectionInteractionSystemAdapter that lived here MOVED to
    //    Hrot.ScenarioEditor.Systems (UXI-11 S-4), because CGF needed the same wrapper and could not
    //    see an IG-private one. ⭐ Its [UpdateInPhase(PostSimulation)] moved with it.
}

