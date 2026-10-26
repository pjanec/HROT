using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Hrot.CGF.Configuration;
using Hrot.Core.Network;
using StructEdit.Reflection;

namespace Hrot.ReplayBrowser;

/// <summary>
/// Standalone replay-browser subsystem. Launches via <c>-m replaybrowser</c>.
/// Hosts an isolated <see cref="ReplayBrowserContext"/> that never touches the
/// live simulation state. Does not implement <c>IMapCameraProvider</c> so the
/// spatial camera remains independent of other subsystems.
/// </summary>
public sealed partial class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    // ── ISubsystem ────────────────────────────────────────────────────────

    public string Name => "ReplayBrowser";
    public Vector4 TitleBarColor => new(0.2f, 0.6f, 0.8f, 1f);

    // ── State (always allocated on Initialize) ────────────────────────────


    private FederatedReplayManager? _manager;
    private EntityRepository? _activeRepo;
    private EntitySelectionHistory _entityHistory = null!;
    private PlaybackHistoryTracker _playbackHistory = null!;
    private bool _headless;

    // ── Federation / Merged View state ────────────────────────────────────

    private ViewMode _viewMode = ViewMode.SingleNode;
    private EntityRepository? _transientMaster;
    private TransientMasterBuilder? _transientBuilder;
    private FederationPanel? _federationPanel;

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — kept so <c>OnLoadGroup</c> can RE-REGISTER
    /// <see cref="Fdp.Presentation.Windows.ReplayBrowser.FederationWindow"/> under the same id every
    /// time <see cref="_federationPanel"/> is replaced by a fresh group load; <c>RegisterWindows</c> is
    /// called only once, before any group is loaded, so this is the only place that can do it.</summary>
    private WindowManager? _windowManager;

    // ── Internal accessors for testing ────────────────────────────────────

    internal FederatedReplayManager? Manager => _manager;
    internal EntityRepository? ActiveRepo => _activeRepo;
    internal ViewMode ViewMode => _viewMode;

    /// <summary>
    /// Test seam: when set, replaces the <see cref="TransientMasterBuilder.Build"/> call
    /// inside <see cref="BuildAndBindTransientMaster"/> so tests can count builds or
    /// return a controlled repo without spinning up <see cref="TransientMasterBuilder"/>.
    /// </summary>
    internal Func<FederatedReplayManager, EntityRepository>? TransientBuildOverride;

    // ── State (non-headless only) ─────────────────────────────────────────

    private MapCanvas? _canvas;
    private InspectorState? _inspectorState;
    private RepositoryAdapter? _session;
    private ReplayTimelinePanel? _timelinePanel;
    // ⭐ CE-1302 R1-c (DESIGN_Director_Timeline_Editor.md §9) — the event timeline over the primary node's recording: its index comes from
    //   the sidecar or a background pass on its own playback (TL-7), its clicks seek through the manager (D1, FederatedReplayCursor).
    private readonly Fdp.Presentation.Panels.Timeline.TimelinePanel _eventTimeline = new();
    private Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSource? _timelineSource;
    private string? _timelineFor;
    private int _timelineLanesVersion = -1;
    // ⭐ R-4078 / R-4079 (DESIGN_Director_Timeline_Editor.md §9c) — the lanes are REGISTERED by the toolkits that own their data; this host
    //   only composes them (as the editor's §15.2 registers its trace providers) and hands the catalogue to the index and the panel.
    private readonly Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog _timelineLanes = new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog()
        .Register(new Fdp.Toolkit.Replication.ReplicationTimelineLanes())
        .Register(new Fdp.Toolkit.Combat.CombatTimelineLanes())
        .Register(new Fdp.Toolkit.Behavior.Diagnostics.BehaviourTimelineLanes());
    private static readonly Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneContext TimelineContext = new(Recording: true);
    private Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler? _timelineCompiler;
    internal Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCatalog TimelineLanes => _timelineLanes;
    internal Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSource? TimelineSource => _timelineSource;
    private IFileDialogService? _fileDialogService;
    private IRecordingExportService? _exportService;
    private ComponentDiffPanel? _diffPanel;
    private ComponentDiffService _diffService = new ComponentDiffService();
    private EntityInspectorPanel? _inspectorPanel;
    private EventBrowserPanel? _eventPanel;
    private ReplaySearchPanel? _searchPanel;
    private ScenarioSerializer _scenarioSerializer = null!;
    private BehaviorRegistry _behaviorRegistry = new();
    // Required by PredicateCompiler for BlueprintVariablePredicateDto; without it, blueprint
    // conditional breakpoints compile to a constant-false delegate (BP-29). Mirrors CgfSubsystem.
    private Fdp.Toolkit.Blueprints.BlueprintRegistry _blueprintRegistry = new();
    // ── Continuous Diff Tracking ──────────────────────────────────────────
    private int _lastDiffFrame = -1;
    private Entity? _lastDiffEntity = null;
    private JsonNode? _lastDiffEntityJson = null;
    private float _playbackAccumulator = 0f;
    private Task? _seekToChangeTask;
    private volatile int _pendingChangeSeekFrame = -1;
    // â”€â”€ Gizmo debug overlay â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? _gizmoBuffer;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager? _globalGizmoManager;

    /// <summary>🔒 <c>UXI-07</c> step 4b — this host's tool arbiter.</summary>
    private Hrot.ScenarioEditor.Tools.ToolController? _replayToolController;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem? _dataDrivenGizmoSystem;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem? _statelessGizmoSystem;
    // ⭐ CE-1042 E2 — the realism effects, driven here like the other map systems (this host runs them from Update).
    private Hrot.UI.Common.Effects.EffectSpawnSystem? _effectSpawn;
    private Fdp.Toolkit.Time.LifespanSystem? _lifespan;
    private Hrot.UI.Common.Effects.WreckSpawnSystem? _wreckSpawn;
    private Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer? _gizmoLayer;
    private Fdp.Core.FdpEventBus? _interactionBus;
    private Hrot.Common.Systems.GlobalActionDispatchSystem? _actionDispatchSystem;
    /// <summary>⭐ <c>CE-3179</c> — the map-menu clicks (GizmoMenuActionEvent) become GlobalActionRequestedEvent here, as on every
    /// other map host; it follows the ACTIVE world's entity map, which changes when a recording is rebound.</summary>
    private Hrot.Common.Systems.ContextActionIngressSystem? _contextIngress;
    // ⭐ CE-3123 — applies the map menu's pin / AI-trace patches; the pack constructs it, this host ticks it (no kernel).
    private Fdp.Toolkit.Behavior.Diagnostics.DebugStatePatchSystem? _debugStatePatchSystem;
    private Hrot.ScenarioEditor.Systems.SelectionInteractionSystem? _selectionSystem;

    // ⭐⭐⭐ UXI-11 S-3b — ReplayBrowser is NOT SPECIAL either (user ruling, 2026-09-20).
    // 🔴 S-3 claimed this host "inspects a RECORDING, there is no global selection for it to agree
    //    with". 📐 MEASURED FALSE: it holds a real EntityRepository and runs SelectionInteractionSystem,
    //    which writes the SelectionState component — so it had the same two-store split as the rest,
    //    plus its own hand-written map->inspector callback. ⇒ same view, same request/notify pair.
    // ⚠ Rebuilt with _activeRepo, which is REPLACED on load: a cached view would pin a dead world.
    private Hrot.ScenarioEditor.Selection.EcsSelectionState?        _selectionView;
    private Hrot.ScenarioEditor.Systems.SelectionRequestSystem?     _selectionRequests;
    private Hrot.ScenarioEditor.Systems.SelectionNotificationSystem? _selectionNotifications;

    /// <summary>⭐ <c>CE-259am</c> — the shared camera-centring system; see its tick in <see cref="Update"/>.</summary>
    private Hrot.ScenarioEditor.Systems.CenterOnEntitySystem? _centerOnEntitySystem;
    private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new();

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Constructor used by <c>ScanForSubsystems</c> / <c>TryCreateSubsystem</c>.
    /// The <paramref name="networkFactory"/> is accepted but intentionally unused;
    /// the replay browser is fully offline.
    /// </summary>
    public ReplayBrowserSubsystem(INetworkFactory networkFactory) { _ = networkFactory; }

    /// <summary>Parameterless constructor for unit tests.</summary>
    /// <summary>⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-1) — THIS host's actions, made with the host: the map registers its
    /// actions on them, the Details bundle registers Properties… and View ▸ Details on them.</summary>
    internal Hrot.Common.Interactions.HostActions HostActions { get; } = Hrot.ScenarioEditor.Map.SharedHostActions.Create();

    public ReplayBrowserSubsystem() { }

    // ── ISubsystem lifecycle ──────────────────────────────────────────────

    public void Initialize(SubsystemConfig config)
    {
        _headless = config.Headless;
        _manager = null;
        _activeRepo = null;
        _entityHistory = new EntitySelectionHistory();
        _playbackHistory = new PlaybackHistoryTracker();
        if (_headless) WireHeadlessHistories();   // RB-3 — a windowed host wires them with its panels

        if (!_headless)
        {
            _activeRepo = new EntityRepository();
            Fdp.Toolkit.ReplayBrowser.Federation.RepositoryPriming.RegisterDiscoveredComponents(_activeRepo);
            // ⭐ §6.7 — the map exists from boot, not only from the first rebind: the canvas and the
            //   gizmo layer are constructed below and may resolve an anchor before any recording loads.
            PrepareRepo(_activeRepo);
            _canvas = new MapCanvas();

            _inspectorState = new InspectorState();
            _session = new RepositoryAdapter(_activeRepo);

            var behaviorRegistry = new BehaviorRegistry();
            _behaviorRegistry = behaviorRegistry;
            CgfBehaviorSetup.LoadFromAiAssembly(behaviorRegistry, _blueprintRegistry);
            _behaviorsLoaded = true;
            _scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);
            // ⭐ P4-③: see CgfSubsystem — the params section is on the tier renderers now.
            Hrot.Presentation.Renderers.BlueprintBlackboardRenderers.BehaviorRegistry = behaviorRegistry;
            // ⛔ P4-①: Blackboard1024Renderer is gone with its component (§30.13).
            Hrot.Presentation.Renderers.BTreeVisualizerRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BehaviorStateRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.BTreeTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            Hrot.Presentation.Renderers.HsmTraceWorkingMemoryRenderer.BehaviorRegistryAccessor = behaviorRegistry;
            // â”€â”€ Gizmo Setup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            BuildMapInteraction(_activeRepo!, behaviorRegistry);

            // ⭐ §6.7 — the world IS passed now, for ONE reader: PickEntity resolves a picked anchor's
            //   network id to an Entity, against the NetworkEntityMap this module now maintains
            //   (EnsureNetworkEntityMap). ⚠ NOT a revival of R3's deleted `view` parameter, which was
            //   stored nowhere. See DebugGizmoLayer._world.
            // ⭐ CE-1033 — the shared attach (gizmo layer + draw buffer + the 3-D mode), the same on every host.
            _gizmoLayer = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(
                _canvas, _gizmoBuffer!, _interactionBus!, () => _activeRepo).GizmoLayer;   // set by BuildMapInteraction above

            _diffService = new ComponentDiffService();
            _exportService = new RecordingExportService(_scenarioSerializer, _diffService);
            _fileDialogService = FileDialogServiceFactory.Create();
            _timelinePanel = new ReplayTimelinePanel(
                null,
                () => _manager?.LocalEntitiesProviderNodeId ?? 0,
                _exportService,
                _fileDialogService,
                _playbackHistory,
                _inspectorState);

            // Wire up group-load delegate and merged-view query so the timeline panel
            // can coordinate with the federation manager without knowing about it.
            // ⭐ RB-1 — the panel's load and POST /replay are ONE path (OpenGroup).
            _timelinePanel.OnLoadGroup = OpenGroup;
            _timelinePanel.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;

            _transientBuilder = new TransientMasterBuilder(_scenarioSerializer);

            _inspectorPanel = new EntityInspectorPanel();
            _inspectorPanel.Serializer = _scenarioSerializer;
            _diffPanel = new ComponentDiffPanel();
            _eventPanel = new EventBrowserPanel()
            {
                SelectedProvider = "All",
                CurrentFrameProvider = () => (uint)Math.Max(0, PrimaryNodeCurrentFrame())
            };

            WireDelegates();

            // Search panel is created after WireDelegates so it receives the wired
            // seek/select intents.
        }
    }

    /// <summary>
    /// ⭐⭐ The shared map pack's machinery for this host (selection, actions, gizmos) — ONE construction, used at boot by a windowed
    /// host and (RB-7, <c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) on the first open by a headless one, which has no world before a
    /// recording opens. ⭐ So <c>POST /selection</c> and the selection history work on a headless Replay Browser through the same
    /// <c>SelectionRequestSystem</c> a click goes through.
    /// </summary>
    private void BuildMapInteraction(EntityRepository world, BehaviorRegistry behaviorRegistry)
    {
        // ── UXI-23 S2b: the shared pack constructs the map's machinery ──────────────────────
        // 🔒 The pack CONSTRUCTS; this host still SCHEDULES (it holds the two systems directly rather
        // than scheduling the togglable group, which remains its choice — the run-set is its role).
        //
        // ⚠⚠ The four projectors below go in through ContributeExtras, which the pack invokes AFTER
        // the reflection pass and BEFORE constructing StatelessGizmoSystem. That ordering is
        // load-bearing: the system sizes its visibility cache from registry.Rules.Count, so a rule
        // registered afterwards lands beyond the cache and silently ignores its visibility policy.
        //
        // ⭐ CE-3123: EntityEditorLabelGizmo is reflected now (its BehaviorRegistry comes via Services).
        var rubberBandState = new Hrot.ScenarioEditor.Gizmos.RubberBandState();

        var mapInteraction = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(
            new Hrot.ScenarioEditor.Map.MapInteractionContext
            {
                World = world,
                // ⭐ CE-3327 RB-7 (DESIGN_Replay_Browser_Debug_Http.md §7.4) — the selection lives in the world SHOWN now (a node's sandbox
                //   or the merged world), never the one the pack was built over: the views switch under one map.
                SelectionWorld = () => _activeRepo ?? world,
                Host  = HostActions,   // ⭐ CE-1150 — the map registers its actions on the host's
                // ⭐ CE-3123 — constructor services for reflected projectors (behaviour labels, mission lines). ⭐ CE-3126 — the
                //   replay's geo transform (origin from the recording), so MissionPresentationGizmo draws here too.
                Services = Hrot.ScenarioEditor.Map.MapServices.Of(_geoTransform, behaviorRegistry),
                GizmoUiPublisher = _gizmoUiHub,
                // ⭐ UXI-11 — the shared predicate, no longer hand-written here.
                IsSelectedPredicate = Hrot.ScenarioEditor.Map.MapInteractionContext.SelectedEntitiesOnly,
                // ⭐⭐⭐ UXI-11 — the pack builds this host's selection too. Resolver: _inspectorState
                //   is rebuilt whenever a recording loads.
                Inspector  = () => _inspectorState,
                RubberBand = rubberBandState,
                // The replay browser is an interactive window: it has a viewer from startup.
                StartEnabled = true,
                // ⭐ W1 (DESIGN_Visual_Effects.md §7 VE-R) — this host only ever shows a recording, and effects are recorded:
                //   the spawner and the lifespan collector stand down; the recorded effects are drawn as they were.
                IsReplayActive = static () => true,
                ContributeExtras = regs =>
                {
                    // ⭐ CE-3123 — the label gizmo is reflected now (BehaviorRegistry via Services); the retired
                    //   EntityEditorPolylineGizmo (superseded by EntityPresentationGizmo) is no longer drawn here.
                    // ⛔ The RubberBandGizmo registration MOVED into MapInteractionPack (2026-09-20,
                    //    §2.7.16): every host with a 2-D map gets the marquee, not just the two that
                    //    remembered to register it. Registering here too would draw it TWICE.
                    regs.Stateless.RegisterGlobal(new ReplaySpatialBoundsGizmo(() => _searchPanel?.ActiveSpatialBounds));
                },
            });

        _gizmoBuffer           = mapInteraction.Buffer;
        _interactionBus        = mapInteraction.InteractionBus;
        _globalGizmoManager    = mapInteraction.GlobalManager;
        _replayToolController  = mapInteraction.Tools;
        _dataDrivenGizmoSystem = mapInteraction.DataDrivenSystem;
        _statelessGizmoSystem  = mapInteraction.StatelessSystem;
        _effectSpawn           = mapInteraction.EffectSpawn;
        _lifespan              = mapInteraction.Lifespan;
        _wreckSpawn            = mapInteraction.WreckSpawn;
        var gizmoRegistry      = mapInteraction.GizmoRegistry;
        var statelessRegistry  = mapInteraction.StatelessRegistry;
        var settingsRegistry   = mapInteraction.Settings;
        // ⭐⭐ UXI-23 S3: this host drives the three systems directly from its own Update rather than
        // scheduling the group, so it declares exactly those three. §3.2e.
        foreach (string problem in mapInteraction.Unserviceable(
                     new object[] { _globalGizmoManager, _dataDrivenGizmoSystem, _statelessGizmoSystem, mapInteraction.ActionDispatch,
                                    _effectSpawn, _wreckSpawn, _lifespan }))
            Fdp.Core.Logging.FdpLog<ReplayBrowserSubsystem>.Info("[Map] {0}", problem);

        // ⭐⭐⭐ UXI-11 — ALL FOUR COME FROM THE PACK NOW. 📐 This host used to construct the gesture
        //    system, the view and both systems by hand, exactly as the other four did — which is
        //    the duplication MapInteractionPack exists to remove.
        // ⭐ The hand-written map->inspector callback went at S-3b: it fired for a MAP click and
        //    nothing else, so an inspector click or a diff/event entity link left the map behind.
        _selectionSystem        = mapInteraction.SelectionInteraction;
        _selectionView          = mapInteraction.Selection;
        _selectionRequests      = mapInteraction.SelectionRequests;
        _selectionNotifications = mapInteraction.SelectionNotifications;

        // â”€â”€ Layer Control & Actions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // ⭐ The registry, its dispatcher and the layer control come from the shared map pack.
        // ⭐ CE-1134 / CE-1150 — Center View is SharedHostActions' (every host; 2-D and 3-D, the replicated position first). This
        //   host's own copy read SimTransform only and moved only the 2-D camera.

        _actionDispatchSystem = mapInteraction.ActionDispatch;
        // ⭐ CE-3179 — the dispatcher above was ticked here with nothing feeding it: no ingress turned a map-menu click (Center
        //   View, the pins, the main menu's layer control) into a request, so none of them ran in the replay browser.
        _contextIngress = new Hrot.Common.Systems.ContextActionIngressSystem(
            () => _activeRepo is { } r && r.HasSingletonManaged<NetworkEntityMap>() ? r.GetSingletonManaged<NetworkEntityMap>() : null,
            _interactionBus!);
        _debugStatePatchSystem = mapInteraction.DebugStatePatch;
        // ⭐⭐ CE-259am — a DELEGATE, not `_canvas.Camera`: the canvas is replaced on a view-mode
        //    switch, and the system's own param doc gives that as the reason it takes a Func.
        _centerOnEntitySystem = new Hrot.ScenarioEditor.Systems.CenterOnEntitySystem(() => _canvas?.Camera);
    }

    // ⭐ CE-3293 S2 (DESIGN_Paused_And_Stepped_Execution.md rule 3) — this host runs no ModuleHostKernel and ticks its systems
    //   itself; every one still goes through a scheduler's gate, which reads its TickMode and (from S5) the replay clock.
    private readonly Fdp.ModuleHost.Scheduling.SystemScheduler _systemRunner = new();

    private System.Diagnostics.Stopwatch? _headlessClock;

    private void RunSystem(Fdp.ModuleHost.Abstractions.IEcsModuleSystem? system, float deltaTime)
    {
        if (system != null && _activeRepo != null) _systemRunner.Run(system, _activeRepo, deltaTime);
    }

    public void Update(float deltaTime)
    {
        // ⭐ CE-3327 RB-1 — a headless orchestrator hands a non-deterministic host NO time (SubsystemOrchestrator.GetDeltaTime: 0); a kernel
        //   host keeps its own clock, and so must this one or POST /sim/play never moves (🔴 measured live: frame 0 after 1 s of play).
        if (_headless && deltaTime <= 0f)
        {
            _headlessClock ??= System.Diagnostics.Stopwatch.StartNew();
            deltaTime = (float)_headlessClock.Elapsed.TotalSeconds;
            _headlessClock.Restart();
        }

        int pendingSeek = _pendingChangeSeekFrame;
        if (pendingSeek >= 0)
        {
            _pendingChangeSeekFrame = -1;
            Entity currentEntity = _inspectorState?.SelectedEntity ?? Entity.Null;
            _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), currentEntity);
            _playbackHistory.PushWaypoint(pendingSeek, currentEntity);
            SeekFrameViaManager(pendingSeek);
        }

        if (_searchPanel != null)
        {
            if (_viewMode == ViewMode.Merged || _manager == null || _manager.Contexts.Count == 0)
                _searchPanel.CurrentFilePath = null;
            else
            {
                int searchNodeId = _manager.LocalEntitiesProviderNodeId;
                _searchPanel.CurrentFilePath = _manager.Contexts.TryGetValue(searchNodeId, out var searchCtx)
                    ? searchCtx.CurrentFdpPath : null;
            }
        }

        SyncTimelineSource();   // ⭐ RB-6 — a headless host keeps the index too (GET /timeline/*)

        // ⭐ RB-1 — playback runs on a headless host too (POST /sim/play): the one play state, the panel's or the headless host's.
        if (IsPlaying)
        {
            _playbackAccumulator += deltaTime * PlaybackRate;
            float frameTime = 1.0f / 60.0f;

            if (_playbackAccumulator > frameTime * 10f)
                _playbackAccumulator = frameTime * 10f;

            while (_playbackAccumulator >= frameTime)
            {
                _playbackAccumulator -= frameTime;
                if (!TryStepForwardViaManager())
                {
                    IsPlaying = false;
                    _playbackAccumulator = 0f;
                    break;
                }
            }
        }

        if (!_headless)
        {
            int currentFrame = PrimaryNodeCurrentFrame();
            Entity? currentEntity = _inspectorState?.SelectedEntity;
            bool isPlaying = _timelinePanel != null && _timelinePanel.IsPlaying;

            // Reactive diff engine: re-evaluate whenever time or selection shifts.
            if ((_lastDiffFrame != currentFrame || _lastDiffEntity != currentEntity) && !isPlaying)
            {
                bool isNextFrame = (currentFrame == _lastDiffFrame + 1) && (_lastDiffEntity == currentEntity);

                if (_diffPanel != null)
                {
                    if (currentFrame > 0 && currentEntity.HasValue && !currentEntity.Value.IsNull)
                    {
                        _diffPanel.CurrentDiffs = ComputeDiffInternal(currentFrame, currentEntity.Value, isNextFrame);
                    }
                    else
                    {
                        _diffPanel.CurrentDiffs = Array.Empty<DiffNode>();
                        _lastDiffEntityJson = null;
                    }
                }

                _lastDiffFrame = currentFrame;
                _lastDiffEntity = currentEntity;
            }

            // Allow user to pan the replay viewport when ImGui isn't capturing the mouse
            if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null)
                _canvas.Camera.HandleInput(new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider());

            _canvas?.Update(deltaTime);
        }

        // ⭐ RB-7 — the map pack's systems run on a headless host too (it builds them on its first open): POST /selection goes through
        //   the same SelectionRequestSystem a click does. Only the mouse gesture system needs a window.
        {
            // Evict transient primitives before backend population
            _gizmoBuffer?.EndFrame(deltaTime);

            if (_activeRepo != null)
            {
                // ⭐ CE-3327 RB-7 — the shown world's own bus, swapped once a frame as a kernel does (ModuleHostKernel.cs:657): what a
                //   route or a panel published on it (SelectEntityCommand, SelectionChangeRequest, CenterOnEntityCommand) becomes readable
                //   to the systems below. 🔴 Nothing swapped it before, so those events were never read. ⚠ Safe: the playback injects the
                //   recorded events into the context's separate SandboxBus, never this one.
                _activeRepo.Bus.SwapBuffers();
                if (!_headless) _selectionSystem?.Tick(deltaTime);
                // ⭐⭐⭐ UXI-11 — the shared request/notify pair, ticked directly like its neighbours
                //    here because this host runs no ModuleHostKernel. ⚠ ORDER IS LOAD-BEARING:
                //    requests apply, THEN the announcement is consumed, so a cause and its consequence
                //    land in one frame (the order ScenarioEditorModule registers them in).
                RunSystem(_selectionRequests, deltaTime);
                RunSystem(_selectionNotifications, deltaTime);
                RunSystem(_contextIngress, deltaTime);   // ⭐ CE-3179 — before the dispatch, the order every host keeps
                RunSystem(_actionDispatchSystem, deltaTime);
                // ⭐ CE-3123 — the order InteractionSystems gives: dispatch, patch, then the gizmos below.
                RunSystem(_debugStatePatchSystem, deltaTime);
                // ⭐⭐⭐ CE-259am — the SHARED CenterOnEntitySystem, ticked directly like its five
                //    neighbours here because this host runs no ModuleHostKernel
                //    (DESIGN_Subsystem_Composition_Unification.md:1153 measures zero kernel references).
                //
                // ⛔⛔ REGISTERING THE EVENT WITHOUT THIS WOULD HAVE MADE THE ROUTE LIE, which is worse
                //    than the 500 it replaces: POST /entities/{id}/focus would answer ok:true and the
                //    camera would not move. 🔒 DESIGN_Mcp_Diagnostics_Federation.md §9.3 — "a 200 with a
                //    transaction id proves only that the ROUTE ran", a gap that surface has already been
                //    bitten by twice (MA-004, MA-017).
                // ⭐ Reuse, not a new mechanism: the system is the one the editor and CGF both run, and
                //   its single dependency is a Func<MapCamera?> — written as a delegate for exactly this
                //   host's shape ("the canvas is created during window registration"), so nothing had to
                //   be adapted. ⚠ It also fixes the UI path: the entity-inspector context menu's
                //   "Center on entity" published into a world that had never heard of the event.
                RunSystem(_centerOnEntitySystem, deltaTime);
                RunSystem(_dataDrivenGizmoSystem, deltaTime);
                RunSystem(_globalGizmoManager, deltaTime);
                RunSystem(_statelessGizmoSystem, deltaTime);
                // ⭐ W1 (DESIGN_Visual_Effects.md §7 VE-R) — the effects are IN the recording (with their Lifespan), so the layer draws
                //   them at the recording's own sim time and a jump lands on the right ones. ⛔ The hand-spawning from the replayed
                //   fire / hit events is gone (it lost every long effect on a jump). Both systems are still ticked so this host's
                //   run-set is the map's whole set; IsReplayActive keeps them standing down.
                RunSystem(_effectSpawn, deltaTime);
                RunSystem(_wreckSpawn, deltaTime);
                RunSystem(_lifespan, deltaTime);
                FollowTheSelectionIntoItsHistory();
            }

            // Swap the interaction bus so intent events are visible on the next frame
            _interactionBus?.SwapBuffers();
        }
    }

    public void DrawWorld()
    {
        if (!_headless)
            _canvas?.Draw();
    }

    public void DrawUI()
    {
        if (_headless) return;

        _gizmoLayer?.DrawContextMenu();
        _gizmoLayer?.DrawStructInspector();

        // ⭐ CE-1243 — the map's menus are a contributor of the one menu bar (RegisterWindowsCore).
    }

    /// <summary>⭐ CE-1302 R1-c — re-opens the timeline's index when the primary node's recording changes (a new group, another node).</summary>
    internal void SyncTimelineSource()
    {
        string? path = PrimaryNodeCurrentFdpPath();
        if (path == _timelineFor && _timelineLanes.Version == _timelineLanesVersion) return;
        _timelineSource?.Dispose();
        _timelineCompiler ??= new Fdp.Toolkit.ReplayBrowser.Timeline.TimelineLaneCompiler(
            new EventScannerCompiler(new ComponentEditServiceBuilder().Build()));
        _timelineSource = path == null ? null
            : Fdp.Toolkit.ReplayBrowser.Timeline.RecordingTimelineSource.Open(path, _timelineLanes.Indexed(TimelineContext), _timelineCompiler);
        _timelineFor = path;
        _timelineLanesVersion = _timelineLanes.Version;
    }

    public void Shutdown()
    {
        _timelineSource?.Dispose();
        _manager?.Dispose();
        _transientMaster?.Dispose();
        _terrain?.Unload(_terrainCarrier);   // CE-3118 — the holder frees the road graph
        _terrainCarrier.Dispose();
    }

    // ── Federation wiring (internal for tests) ────────────────────────────

    /// <summary>
    /// Switches <see cref="ActiveRepo"/> to the sandbox repo of the local-entities
    /// provider node whenever the manager reports a time change.
    /// In Merged View, rebuilds the transient master and binds to that instead.
    /// </summary>
    private void OnManagerTimeChanged()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return;

        if (_viewMode == ViewMode.Merged)
        {
            BuildAndBindTransientMaster();
        }
        else
        {
            int nodeId = _manager.LocalEntitiesProviderNodeId;
            if (_manager.Contexts.TryGetValue(nodeId, out var ctx))
                RebindActiveRepo(ctx.SandboxRepo);
        }

        CaptureEventsAtTheNewFrame();

        // Update EventBrowserPanel's history service to the primary node's service.
        if (_eventPanel != null && _manager != null
            && _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var primaryCtxForEvent))
            _eventPanel.HistoryService = primaryCtxForEvent.HistoryService;
    }

    /// <summary>
    /// Builds a fresh transient master from the current manager state and
    /// rebinds <see cref="ActiveRepo"/> to it.
    /// Disposes any previously allocated transient master.
    /// </summary>
    private void BuildAndBindTransientMaster()
    {
        if (_manager == null) return;

        EntityRepository newMaster;
        if (TransientBuildOverride != null)
        {
            newMaster = TransientBuildOverride(_manager);
        }
        else if (_transientBuilder != null)
        {
            newMaster = _transientBuilder.Build(_manager);
        }
        else
        {
            // Fallback: no builder available — bind to single-node as before.
            int nodeId = _manager.LocalEntitiesProviderNodeId;
            if (_manager.Contexts.TryGetValue(nodeId, out var ctx))
                RebindActiveRepo(ctx.SandboxRepo);
            return;
        }

        _transientMaster?.Dispose();
        _transientMaster = newMaster;
        RebindActiveRepo(_transientMaster);
    }

    /// <summary>
    /// Switches the active view mode. Notifies panels and triggers a rebind.
    /// </summary>
    internal void SetViewMode(ViewMode mode)
    {
        if (mode == ViewMode.Merged && _seekToChangeTask != null && !_seekToChangeTask.IsCompleted)
        {
            if (_diffPanel != null)
                _diffPanel.IsSearching = false;
        }

        _viewMode = mode;

        if (_searchPanel != null)
            _searchPanel.IsMergedViewActive = mode == ViewMode.Merged;

        if (_inspectorState != null)
            _inspectorState.IsMergedView = mode == ViewMode.Merged;

        // Trigger immediate rebind for the new mode.
        OnManagerTimeChanged();
    }

    /// <summary>
    /// Test seam: loads a federated group from <paramref name="paths"/> using a provided
    /// <paramref name="builder"/> (bypassing headless-guard so tests can call it directly).
    /// </summary>
    internal void LoadFdpGroupForTest(string[] paths, TransientMasterBuilder builder)
    {
        _manager?.Dispose();
        _transientMaster?.Dispose();
        _transientMaster = null;

        _transientBuilder = builder;
        _manager = FederatedReplayManager.LoadGroup(paths);
        _manager.OnTimeChanged += OnManagerTimeChanged;
        ApplyRecordingGeoOrigin();   // CE-3126
        ApplyRecordingTerrain();     // CE-3118
        _timelinePanel?.SetManager(_manager);

        CreateOrReplaceFederationPanel();

        OnManagerTimeChanged();
    }

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — creates (or replaces, on a subsequent group load) the
    /// <see cref="FederationPanel"/> for the CURRENT <see cref="_manager"/>, and — when
    /// <see cref="_windowManager"/> is already known (i.e. <see cref="RegisterWindows"/> already ran) —
    /// (re)registers <see cref="Fdp.Presentation.Windows.ReplayBrowser.FederationWindow"/> under the
    /// same id so <c>WindowManager</c>'s replace-by-id semantics swap in the new panel. Shared by the
    /// real <c>OnLoadGroup</c> delegate and the <see cref="LoadFdpGroupForTest"/> test seam so both
    /// paths exercise the identical wiring.</summary>
    private void CreateOrReplaceFederationPanel()
    {
        if (_federationPanel != null)
            _federationPanel.OnViewModeChanged -= SetViewMode;
        _federationPanel = new FederationPanel(_manager!);
        _federationPanel.OnViewModeChanged += SetViewMode;

        if (_windowManager != null)
            RegisterFederationWindow(_windowManager, _federationPanel);
    }

    private void RebindActiveRepo(EntityRepository repo)
    {
        _activeRepo = repo;
        _session = new RepositoryAdapter(repo);
        PrepareRepo(repo);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Everything a repo this subsystem BINDS must have before the shared viewport systems run on
    /// it</b> — called from <see cref="Initialize"/> and from <see cref="RebindActiveRepo"/>, which between
    /// them cover every repo this host ever ticks *(the boot transient master, each per-node sandbox, and
    /// the Merged transient master)*.
    ///
    /// <para>🔴🔴 <b><c>CE-259am</c> — THE SECOND DEFECT, and it was PRE-EXISTING: this host ran the shared
    /// viewport systems on worlds where their EVENTS WERE NEVER REGISTERED.</b> 📐 Measured `2026-09-11`
    /// the moment <see cref="CreateDebugProvider"/> made the routes reachable:
    /// <c>POST /entities/1001/focus</c> answered <c>500 "Strict Mode Violation: Unmanaged event type
    /// 'CenterOnEntityCommand' (ID: 8104) was published without being explicitly registered."</c>
    /// ⚠⚠ <b>Byte-for-byte the crash <c>CE-065</c> already fixed on CGF</b> — see
    /// <c>PresentationComponentRegistry</c>'s own header, which records
    /// <c>POST /entities/1000/focus → 500 … (ID: 8104)</c> as the reproduction. ⇒ the shared list was
    /// adopted by four hosts *(Stride · CGF · SimHost · editor)* and this is the fifth that needed it.
    /// 🔒 That header's standing instruction: <i>"If a fourth system joins ScenarioEditorModule, its event
    /// belongs HERE"</i> — ⭐ so the fix is to ADOPT the list, not to hand-register three events beside it.
    /// ⛔ <c>RepositoryPriming.RegisterDiscoveredComponents</c> above does NOT cover this: it registers
    /// component tables, never events.</para>
    ///
    /// <para>⭐ Idempotent by construction — <c>RegisterAll</c>'s own remark: <i>"every call resolves to
    /// GetOrCreate…, so a host that reaches this twice is fine."</i> ⇒ safe on every seek.</para>
    /// </summary>
    private void PrepareRepo(EntityRepository repo)
    {
        // ⭐⭐ The shared list, not a local copy of three RegisterEvent calls.
        Hrot.Map.Common.PresentationComponentRegistry.RegisterAll(repo);
        EnsureNetworkEntityMap(repo);
        // ⭐ CE-3126 — the replay's ONE geo transform, on every repo this host binds, like every other node's world.
        repo.SetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>(_geoTransform);
        // ⭐ CE-3118 — the recording's terrain and road network, on every repo this host draws (docs/DESIGN_Geo_Origin.md §5).
        Hrot.Map.Common.Services.TerrainResidency.MirrorTerrain(_terrainCarrier, repo);
    }

    /// <summary>
    /// ⭐ <c>CE-3118</c> — the terrain the loaded recording names (<c>RecordingMetadata.TerrainName</c>), made resident by the same
    /// <see cref="Hrot.Map.Common.Services.TerrainResidency"/> every node uses, with its own road-graph holder and no navmesh (the
    /// browser only draws it). It commits into <see cref="_terrainCarrier"/>, a world with no geo transform, so the terrain FILE's
    /// origin never overrides the RECORDING's (§2 F); <see cref="PrepareRepo"/> mirrors it onto every repo it binds.
    /// </summary>
    private readonly EntityRepository _terrainCarrier = new();
    private readonly CarKinem.Road.RoadNetworkHolder _terrainRoads = new();
    private Hrot.Map.Common.Services.TerrainResidency? _terrain;

    /// <summary>Where terrain names resolve (test seam): the shared NAS stand-in, then the shipped terrains.</summary>
    internal Fdp.Toolkit.Terrain.TerrainCatalog TerrainCatalog { get; set; } = Fdp.Toolkit.Terrain.TerrainCatalog.ForNode(
        localStagingRoot: null, Fdp.Toolkit.Orchestration.OrchestrationConstants.GetSharedRoot());

    /// <summary>The name of the terrain the browser holds, or null (test seam).</summary>
    internal string? ResidentTerrainName => _terrain?.ResidentTerrainName;

    /// <summary>
    /// ⭐ <c>CE-3118</c> — loads the terrain the loaded recording names (the local-entities provider node's, else any node's).
    /// A recording that names none, or a terrain that no longer resolves, leaves the map without terrain and says so — the
    /// replay itself still plays (🔒 §2 F: <i>"if it still exists"</i>).
    /// </summary>
    private void ApplyRecordingTerrain()
    {
        if (_manager == null) return;
        _terrain ??= new Hrot.Map.Common.Services.TerrainResidency(TerrainCatalog, _terrainRoads);

        string? name = null;
        if (_manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var primary))
            name = primary.Playback?.Metadata?.TerrainName;
        if (string.IsNullOrEmpty(name))
            foreach (var ctx in _manager.Contexts.Values)
                if (!string.IsNullOrEmpty(name = ctx.Playback?.Metadata?.TerrainName)) break;

        if (string.IsNullOrEmpty(name))
            Fdp.Core.Logging.FdpLog<ReplayBrowserSubsystem>.Info(
                "[ReplayBrowser] the recording names no terrain — the map shows none.");
        try
        {
            _terrain.Commit(_terrainCarrier, _terrain.Prepare(string.IsNullOrEmpty(name) ? null : name));
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            Fdp.Core.Logging.FdpLog<ReplayBrowserSubsystem>.Warn(
                $"[ReplayBrowser] the recording's terrain '{name}' could not be loaded ({ex.Message}) — the map shows no terrain.");
            _terrain.Unload(_terrainCarrier);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3126</c> (R-229) — the replay's geo transform. Its origin comes from the RECORDING
    /// (<see cref="ApplyRecordingGeoOrigin"/>), never from the terrain file at replay time: 🔒 user — <i>"Origin can be saved in
    /// recording metadata in case terrain with remembered name no longer exists"</i>. Held by the map's projectors
    /// (MapServices) and published on every bound repo. 📄 docs/DESIGN_Geo_Origin.md §2 F.
    /// </summary>
    private readonly Fdp.Modules.Geographic.Transforms.WGS84Transform _geoTransform = new();

    /// <summary>The replay's geo transform (test seam).</summary>
    internal Fdp.Modules.Geographic.IGeographicTransform GeoTransform => _geoTransform;

    /// <summary>
    /// ⭐ <c>CE-3126</c> — sets the replay's origin from the loaded recording's metadata (the local-entities provider node's,
    /// else any node's that has one). A recording that pre-dates the field keeps 0,0,0 and says so.
    /// </summary>
    private void ApplyRecordingGeoOrigin()
    {
        if (_manager == null) return;
        Fdp.Core.FlightRecorder.Metadata.GeoOriginRecord? origin = null;
        if (_manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var primary))
            origin = primary.Playback?.Metadata?.GeoOrigin;
        if (origin == null)
            foreach (var ctx in _manager.Contexts.Values)
                if ((origin = ctx.Playback?.Metadata?.GeoOrigin) != null) break;
        if (origin == null)
        {
            Fdp.Core.Logging.FdpLog<ReplayBrowserSubsystem>.Warn(
                "[ReplayBrowser] the recording carries no geo origin (it pre-dates CE-3126) — lat/lon shown at origin 0,0,0.");
            _geoTransform.SetOrigin(0.0, 0.0, 0.0);
            return;
        }
        _geoTransform.SetOrigin(origin.Lat, origin.Lon, origin.Alt);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>This module gets a <see cref="NetworkEntityMap"/>, like every other ECS module.</b>
    /// 🔒 User, <c>2026-09-11</c>: *"what prevents adding network id map to replaybrowser? … replaybrowser
    /// is ecs module like any else. i do not want such exceptions."*
    /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7.
    ///
    /// <para>⛔⛔ <b>What its absence was costing, and it was not local.</b> The gizmo pick token carried
    /// the producer's raw ECS index+generation as a payload — a process-local handle in a network-stable
    /// contract — and the justification on record was *this module*: it had no map, so a network-id
    /// resolve "would silently drop its selection". ⇒ one module's missing service was shaping the
    /// identity model of the whole gizmo pipeline. 📐 Nothing prevented it: the map is a world singleton
    /// three other hosts already set (<c>CgfSubsystem.cs:637</c>, <c>SimHostApp.cs:546</c>,
    /// <c>EditorSubsystem.cs:1122</c>).</para>
    ///
    /// <para>⭐⭐ <b>Called from <see cref="RebindActiveRepo"/>, which is the ONE choke point</b> —
    /// <c>OnManagerTimeChanged</c> routes every seek, step and view-mode switch through it, in both the
    /// per-node and Merged arms. ⇒ the map cannot drift behind the frame.</para>
    ///
    /// <para>⛔⛔ <b><see cref="NetworkEntityMap.Clear"/> FIRST, and that is not defensive noise.</b>
    /// <see cref="NetworkEntityMap.RebuildFromWorld"/> prunes dead entries and adds missing ones, which is
    /// right for a forward-running world. ⚠ A replay TIME-TRAVELS: the same network id can be a
    /// DIFFERENT live handle after a seek, and an entry that is still alive survives the prune and blocks
    /// the re-add — a stale map that answers with the wrong entity, which is the exact failure
    /// <c>NetworkIdResolver</c>'s header warns a maintained index can produce. ⇒ rebuild from empty.</para>
    /// </summary>
    private static void EnsureNetworkEntityMap(EntityRepository repo)
    {
        if (!repo.HasSingletonManaged<NetworkEntityMap>())
            repo.SetSingletonManaged(new NetworkEntityMap());

        var map = repo.GetSingletonManaged<NetworkEntityMap>();
        if (map == null) return;

        map.Clear();
        map.RebuildFromWorld(repo);
    }

    // ── IProvidesDebugSurface — ReplayBrowserSubsystem.DebugSurface.cs (RB-1) ──


    /// <summary>
    /// Loads one or more .fdp recording files via a fresh <see cref="FederatedReplayManager"/>
    /// and binds <see cref="ActiveRepo"/> to the local-entities provider node's sandbox repo.
    /// Disposes any previously loaded manager first.
    /// </summary>
    internal void LoadFdpViaManager(string path)
    {
        _manager?.Dispose();
        _manager = FederatedReplayManager.LoadGroup(new[] { path });
        _manager.OnTimeChanged += OnManagerTimeChanged;
        ApplyRecordingGeoOrigin();   // CE-3126
        ApplyRecordingTerrain();     // CE-3118
        _timelinePanel?.SetManager(_manager);
        OnManagerTimeChanged();
    }

    // ── IWindowRegistrar ──────────────────────────────────────────────────

    public void RegisterWindows(WindowManager windowManager)
    {
        if (_headless) return;
        _windowManager = windowManager;
        RegisterWindowsCore(
            windowManager,
            _timelinePanel!,
            _inspectorPanel!,
            _diffPanel!,
            _eventPanel!,
            _searchPanel!,
            _federationPanel);

        // ⭐ CE-1289 (+ CE-3151) — the point under the map cursor, flat and lat/long/alt, on the 2-D and the 3-D map.
        Hrot.UI.Common.Panels.MapCursorStatusBarSection.Register(windowManager.StatusBar, () => _canvas, "ReplayBrowser");

        // Wire the ImGui file dialog fallback so it renders on non-Windows hosts.
        // Harmless no-op for the Win32 backend: WindowManager only draws the service
        // when it is an ImGuiFileDialogService.
        if (_fileDialogService != null)
            windowManager.SetFileDialogService(_fileDialogService);
    }

    /// <summary>
    /// Test seam: registers the replay-browser windows using caller-supplied
    /// panel instances. Skips the headless guard so tests can exercise window
    /// registration without initialising Raylib.
    /// </summary>
    /// <param name="federationPanel">⭐⭐⭐ U-obs-5 follow-up — <c>null</c> until a replay group has
    /// been loaded (the panel is created lazily); when non-null its window is registered too.</param>
    internal void RegisterWindowsCore(
        WindowManager windowManager,
        ReplayTimelinePanel timelinePanel,
        EntityInspectorPanel inspectorPanel,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel,
        ReplaySearchPanel searchPanel,
        FederationPanel? federationPanel = null)
    {
        string perspective = "ReplayBrowser";
        Vector4 color = TitleBarColor;

        // Capture safe references for the inspector window factories.
        InspectorState stateRef = _inspectorState ?? new InspectorState();

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplayTimelineWindow(
            "rb_timeline", "Replay Timeline", perspective, timelinePanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.Timeline.EventTimelineWindow(
            "rb_event_timeline", "Event Timeline", perspective, _eventTimeline, () => _timelineSource,
            new Fdp.Presentation.Panels.Timeline.FederatedReplayCursor(() => _manager, () => _manager?.LocalEntitiesProviderNodeId ?? 0,
                isPlaying: () => !Transport.IsPaused, togglePlay: () => Transport.TogglePlayPause()),   // ⭐ CE-3330 — Space, "Play from here"
            () => _timelineLanes.Lanes(TimelineContext), color,
            onSelect: e => SelectTimelineEvent(e),        // ⭐ CE-3330 (R-4087 addendum 5) — the ONE selection, RB-7's path
            rowMenu: _timelineLanes.MenuItems));          // ⭐ CE-3330 (addendum 4) — the row's provider's items

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ComponentDiffWindow(
            "rb_diff", "Frame Diff Viewer", perspective, diffPanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplaySearchWindow(
            "rb_search", "Replay Search", perspective, searchPanel, color));

        if (federationPanel != null)
            RegisterFederationWindow(windowManager, federationPanel);

        // ⭐⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-4) — THE Details panel, the same shared one every ECS host builds: the
        //   Components view over the replayed world, and Properties… / View ▸ Details on this host's actions. 📐 Before: none here.
        // ⭐⭐ CE-1243 MB-4 — this host's map menus join the ONE menu bar as a contributor.
        Hrot.ScenarioEditor.Map.MapInteractionPack.RegisterMainMenu(windowManager.GlobalMenu, "replay/map-menus", perspective,
            () => _gizmoLayer?.ConsumeMainMenu(), () => _interactionBus);

        // ⭐⭐ CE-1244 (DESIGN_Subsystem_Composition_Unification §5c.7.8 RI-1…RI-3) — the entity inspector and the event browser come
        //   from the SAME diagnostics bundle every host composes (with the map's entity menu and "Inspect…"); no kernel here ⇒ no
        //   architecture / profiler window. ⛔ SUPERSEDED: this host's own copies of the two window classes (rb_inspector / rb_events).
        Fdp.Toolkit.Runner.UiBundleHost.Compose(
            new Fdp.Toolkit.Runner.IUiBundle[]
            {
                new Hrot.Presentation.Windows.DiagnosticsWindowsBundle(new Hrot.Presentation.Windows.DiagnosticsHostServices(
                    IdPrefix:       "rb_",
                    TitlePrefix:    "Replay",
                    Perspective:    perspective,
                    Inspector:      inspectorPanel,
                    RepoAdapter:    () => _session,
                    InspectorState: () => stateRef,
                    EventBrowser:   eventPanel,
                    TitleBarColor:  color,
                    Host:           () => HostActions,
                    World:          () => _activeRepo)),
                new Hrot.Editor.AiShared.Windows.EntityDetailsBundle(new Hrot.Editor.AiShared.Windows.EntityDetailsHostServices(
                    WindowId:    "rb_details",
                    Perspective: perspective,
                    World:       () => _activeRepo,
                    Inspector:   () => inspectorPanel,
                    Session:     () => _session,
                    RunState:    static () => Hrot.Editor.AiShared.Variables.VariableRunState.Replay,
                    Host:        () => HostActions)),
            },
            new Fdp.Toolkit.Runner.UiBundleContext(windowManager));
    }

    /// <summary>⭐⭐⭐ U-obs-5 follow-up — shared by <see cref="RegisterWindowsCore"/> (the initial
    /// registration, when a group happened to already be loaded) and <c>OnLoadGroup</c> (every
    /// subsequent group load, which replaces <see cref="_federationPanel"/>).</summary>
    private void RegisterFederationWindow(WindowManager windowManager, FederationPanel panel)
        => windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FederationWindow(
            "rb_federation", "Federation", "ReplayBrowser", panel, TitleBarColor));

    // ── Delegate wiring ───────────────────────────────────────────────────

    private void WireDelegates()
    {
        var (seekIntent, selectIntent, matchIntent) = WireDelegatesForTest(
            _entityHistory, _playbackHistory, _inspectorState!, null!, _diffPanel!, _eventPanel!);

        // ⭐⭐ UXI-11 S-3 — ChainToMap is RETIRED; OnEntitySelected now fires on every explicit row
        //    click. ⚠ It is no longer a selection WRITE — it is "the user clicked this entity", which
        //    is exactly what this host's selection HISTORY wants to record.
        // ⭐⭐⭐ S-3b — and this host IS given the shared pair. 🔴 S-3 said it should not be, on the
        //    claim that a recording has no global selection; measured false — it runs
        //    SelectionInteractionSystem over a real repository. 📄 §2.7.8.
        _inspectorPanel!.OnEntitySelected = selectIntent;
        _inspectorPanel.Selection = _selectionView;
        _inspectorPanel.RequestSelectionChange = req => _activeRepo?.Bus.PublishManaged(req);
        _diffPanel!.IsMergedViewQuery = () => _viewMode == ViewMode.Merged;
        _diffPanel!.OnSeekToChangeRequested = direction =>
        {
            if (_viewMode == ViewMode.Merged) return; // disabled in Merged View
            if (_inspectorState?.SelectedEntity != null)
                _seekToChangeTask = SeekToNextChangeAsync(_inspectorState.SelectedEntity.Value, direction);
        };

        // Build search services.
        var editSvc = new ComponentEditServiceBuilder()
            .RegisterFieldEditor<Type>(new Fdp.Presentation.Editing.TypeFieldEditor())
            .RegisterFieldEditor<BoundingBox2D>(new Fdp.Presentation.Editing.BoundingBoxFieldEditor())
            .RegisterFieldEditor<SearchPredicateDto>(new Fdp.Presentation.Editing.PredicateValueFieldEditor())
            .Build();
        // See BP-29: the registry is what makes BlueprintVariablePredicateDto evaluable.
        var predicateCompiler = new PredicateCompiler(editSvc, _behaviorRegistry, _blueprintRegistry);
        var eventScannerCompiler = new EventScannerCompiler(editSvc);
        var searchSvc = new RecordingSearchService(predicateCompiler, eventScannerCompiler);
        Func<Entity?> getSelectedEntity = () => _inspectorState?.SelectedEntity;
        Func<long?> getSelectedNetworkId = () =>
        {
            var e = _inspectorState?.SelectedEntity;
            if (e == null || e.Value.IsNull || !(_activeRepo?.IsAlive(e.Value) ?? false)) return null;
            if (_activeRepo?.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e.Value) ?? false)
                return _activeRepo!.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e.Value).Value;
            return null;
        };

        _searchPanel = new ReplaySearchPanel(
            editSvc, searchSvc, seekIntent, selectIntent, matchIntent,
            _behaviorRegistry, getSelectedEntity, getSelectedNetworkId);
        if (_globalGizmoManager != null)
        {
            // 🔒 UXI-07 step 4b — the bounds pick SUSPENDS the active tool instead of arming beside it.
            _searchPanel.SpatialPickerCtx = new ReplaySpatialPickerContext(
                new Hrot.ScenarioEditor.Tools.PickerToolHost(
                    () => _replayToolController, () => _globalGizmoManager));
        }
    }

    private async Task SeekToNextChangeAsync(Entity target, int direction)
    {
        if (PrimaryNodeCurrentFdpPath() == null || _diffPanel == null || _diffPanel.IsSearching)
            return;

        _diffPanel.IsSearching = true;
        string fdpPath = PrimaryNodeCurrentFdpPath()!;
        int startFrame = PrimaryNodeCurrentFrame();
        var excludedNames = new HashSet<string>(_diffPanel.ExcludedTypes.Select(t => t.Name));
        double epsilon = _diffPanel.IsEpsilonIgnored ? 0.001 : 0.0;
        var diffSvc = _diffService;
        var serializer = _scenarioSerializer;

        try
        {
            // ⭐ RB-2 — the ONE finder the debug API's POST /replay/seek {next} runs too (EntityChangeFinder).
            int? foundFrame = await Task.Run(() => EntityChangeFinder.Find(
                fdpPath, startFrame, _ => target, direction,
                (repo, e) => serializer.SerializeEntity(repo, e, new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver(), repo.GetSnapshotableMask()),
                name => !excludedNames.Contains(name), epsilon, diffSvc));

            if (foundFrame.HasValue)
            {
                _pendingChangeSeekFrame = foundFrame.Value;
            }
        }
        finally
        {
            _diffPanel.IsSearching = false;
        }
    }

    /// <summary>
    /// Test seam: wires delegates using caller-supplied dependencies.
    /// Returns the seek and select intents so tests can invoke them directly.
    /// Replaces _entityHistory, _playbackHistory, and _context with the injected
    /// objects so causality-jump and reactive diff logic operate on the same instances in tests.
    /// </summary>
    internal (Action<int> seekIntent, Action<Entity> selectIntent, Action<int, Entity> matchIntent) WireDelegatesForTest(
        EntitySelectionHistory entityHistory,
        PlaybackHistoryTracker playbackHistory,
        InspectorState inspectorState,
        ReplayBrowserContext context,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel)
    {
        _entityHistory   = entityHistory;
        _playbackHistory = playbackHistory;

        // ⭐⭐⭐ UXI-11 S-3b — history navigation REQUESTS a selection instead of writing the inspector
        //    behind the map's back. 🔴 Before, stepping back through the selection history moved the
        //    inspector and left the map ring where it was. ⚠ Falls back to the direct write when no
        //    world is attached (headless / before a recording loads), which is not a second store —
        //    there is nothing to be second to.
        entityHistory.OnSelectionChanged += e =>
        {
            if (!RequestSelectionFromHistory(e))
                inspectorState.SelectedEntity = e;
        };

        // Seek history: when the playback history fires, seek frame + restore selection.
        playbackHistory.OnWaypointRequested += wp =>
        {
            SeekFrameViaManager(wp.FrameIndex);
            inspectorState.SelectedEntity = wp.SelectedEntity.IsNull ? null : wp.SelectedEntity;
            if (!wp.SelectedEntity.IsNull)
                entityHistory.PushSelection(wp.SelectedEntity);
        };

        // Intents passed down to panels (panels stay unaware of history trackers).
        Action<int> seekIntent = f =>
        {
            Entity selected = inspectorState.SelectedEntity ?? Entity.Null;
            playbackHistory.PushWaypoint(f, selected);
            SeekFrameViaManager(f);
        };
        Action<Entity> selectIntent = e => entityHistory.PushSelection(e);
        Action<int, Entity> matchIntent = (f, e) =>
        {
            playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), inspectorState.SelectedEntity ?? Entity.Null);
            playbackHistory.PushWaypoint(f, e);
            entityHistory.PushSelection(e);
            SeekFrameViaManager(f);
        };

        diffPanel.OnEntityLinkClicked  = selectIntent;
        eventPanel.OnEntityLinkClicked = selectIntent;
        eventPanel.OnCausalityJumpRequested = ExecuteCausalityJump;

        return (seekIntent, selectIntent, matchIntent);
    }

    /// <summary>
    /// Executes the causality jump by seeking to the frame immediately after the source event
    /// and selecting the target entity. Diff rendering is handled reactively in Update().
    /// </summary>
    internal void ExecuteCausalityJump(int eventFrame, Entity target)
    {
        _playbackHistory.PushWaypoint(PrimaryNodeCurrentFrame(), _inspectorState?.SelectedEntity ?? Entity.Null);
        // Do not push Entity.Null to entity selection history; only push the target.

        int targetFrame = eventFrame + 1;

        _entityHistory.PushSelection(target);
        _playbackHistory.PushWaypoint(targetFrame, target);
        SeekFrameViaManager(targetFrame);
    }

    /// <summary>
    /// Compatibility overload retained for existing tests. Uses the current frame as jump origin.
    /// </summary>
    internal void ExecuteCausalityJump(Entity target)
        => ExecuteCausalityJump(PrimaryNodeCurrentFrame(), target);

    // ── Manager-driven helpers ────────────────────────────────────────────

    internal int PrimaryNodeCurrentFrame()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return -1;
        return _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx)
            ? ctx.CurrentFrame : -1;
    }

    private string? PrimaryNodeCurrentFdpPath()
    {
        if (_manager == null || _manager.Contexts.Count == 0) return null;
        return _manager.Contexts.TryGetValue(_manager.LocalEntitiesProviderNodeId, out var ctx)
            ? ctx.CurrentFdpPath : null;
    }

    private bool TryStepForwardViaManager()
    {
        if (_manager == null) return false;
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var ctx) || ctx.Playback == null) return false;
        int nextFrame = ctx.CurrentFrame + 1;
        if (nextFrame >= ctx.Playback.TotalFrames) return false;
        _manager.StepForwardAll();
        return true;
    }

    private void SeekFrameViaManager(int frame)
    {
        if (_manager == null) return;
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var ctx) || ctx.Playback == null) return;
        _manager.SetBaseWallTicks(ctx.Playback.GetFrameMetadata(frame).WallClockTicks);
    }

    private IReadOnlyList<DiffNode> ComputeDiffInternal(int frame, Entity entity, bool isNextFrame)
    {
        if (_manager == null || _scenarioSerializer == null || entity.IsNull) return Array.Empty<DiffNode>();
        int nodeId = _manager.LocalEntitiesProviderNodeId;
        if (!_manager.Contexts.TryGetValue(nodeId, out var primaryCtx) || primaryCtx.Playback == null)
            return Array.Empty<DiffNode>();
        if (frame <= 0 || frame >= primaryCtx.Playback.TotalFrames)
            return Array.Empty<DiffNode>();

        long? networkId = GetStableNetworkId(entity, _activeRepo);
        var resolver = new Fdp.Toolkit.Diagnostics.DiagnosticGuidResolver();
        JsonNode? before = null;

        if (isNextFrame && _lastDiffEntityJson != null)
        {
            before = _lastDiffEntityJson;
        }
        else
        {
            _manager.StepBackwardAll();
            Entity beforeEntity = networkId.HasValue ? FindEntityByNetworkId(networkId.Value) : entity;
            if (_activeRepo != null && _activeRepo.IsAlive(beforeEntity))
            {
                var mask = _activeRepo.GetSnapshotableMask();
                before = _scenarioSerializer.SerializeEntity(_activeRepo, beforeEntity, resolver, mask);
            }
            _manager.StepForwardAll();
        }

        Entity afterEntity = networkId.HasValue ? FindEntityByNetworkId(networkId.Value) : entity;
        JsonNode? after = null;
        if (_activeRepo != null && _activeRepo.IsAlive(afterEntity))
        {
            var mask = _activeRepo.GetSnapshotableMask();
            after = _scenarioSerializer.SerializeEntity(_activeRepo, afterEntity, resolver, mask);
        }

        _lastDiffEntityJson = after;
        return _diffService.ComputeTreeDiff(before, after, 0.0);
    }

    /// <summary>Test seam: exposes the two-rebuild diff cycle for headless tests.</summary>
    internal IReadOnlyList<DiffNode> ComputeDiffForTest(int frame, Entity entity)
        => ComputeDiffInternal(frame, entity, false);

    /// <summary>Test seam: injects a ScenarioSerializer for headless diff tests.</summary>
    internal void SetSerializerForTest(Fdp.Toolkit.Scenario.ScenarioSerializer serializer)
        => _scenarioSerializer = serializer;

    private static long? GetStableNetworkId(Entity entity, EntityRepository? repo)
    {
        if (repo == null || entity.IsNull || !repo.IsAlive(entity)) return null;
        if (!repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity)) return null;
        return repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value;
    }

    /// <summary>
    /// ⭐ <c>BP-508</c> — routed through the ONE resolver *(<c>R-77</c>)*. ⛔ This copy scanned
    /// <b>every</b> entity and asked <c>HasComponent</c> per entity; the shared one filters the query.
    /// ⭐⭐ §6.7 — now the MAP-FIRST entry point: this module maintains a <see cref="NetworkEntityMap"/>
    /// like any other (see <see cref="EnsureNetworkEntityMap"/>), so the diff cycle's two lookups per
    /// frame are O(1) instead of two scans.
    /// </summary>
    private Entity FindEntityByNetworkId(long networkId)
        => Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(_activeRepo, networkId);

    // ── Null service stubs (used until real implementations are injected) ──

    private sealed class NullRecordingExportService : IRecordingExportService
    {
        public void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options) { }
    }

    private sealed class NullFileDialogService : IFileDialogService
    {
        public System.Threading.Tasks.Task<string?> ShowSaveAsDialogAsync(
            string callSiteId, string defaultFileName, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string[]?>(null);
    }

    private sealed class ReplaySpatialPickerContext : Fdp.Presentation.Editing.ISpatialPickerContext
    {
        private readonly Hrot.ScenarioEditor.Tools.PickerToolHost _pickers;
        private string? _pendingPath;
        private Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D? _resolvedBox;

        /// <summary>
        /// ⚠⚠ <b>The RE-HOMED half of the deleted <c>_activeGizmoId</c>.</b> That field did TWO jobs:
        /// *"unregister my previous picker"* — ⛔ a private one-slot arbiter, now the controller's job —
        /// and *"is a pick in flight"*, which <see cref="IsPickPendingFor"/> exposes to the search panel.
        /// ⭐ Only the second is a real contract, so only the second survives, as a plain flag.
        /// </summary>
        private bool _pickActive;

        public ReplaySpatialPickerContext(Hrot.ScenarioEditor.Tools.PickerToolHost pickers)
        {
            _pickers = pickers;
        }

        public bool IsPickPendingFor(string jsonPath) => _pickActive && _pendingPath == jsonPath;

        public void RequestBoundingBoxPick(string jsonPath)
        {
            _pendingPath = jsonPath;
            _resolvedBox = null;

            // ⭐⭐⭐ UXI-07 step 4b — PUSH through the arbiter. 🔴 This used to Register straight on
            //   GlobalGizmoManager and unregister its own previous gizmo first; re-arming is now a
            //   RE-TARGET the controller handles, so that bookkeeping is gone (§4.12).
            _pickActive = _pickers.PushPicker(
                Hrot.ScenarioEditor.Tools.ScenarioToolIds.PickBounds,
                remove => new Fdp.Toolkit.ReplayBrowser.BoundingBoxPickerGizmo(
                    box => _resolvedBox = box,
                    () =>
                    {
                        remove();
                        _pickActive = false;
                    }));
        }

        public bool TryConsumeBoundingBoxPick(string jsonPath, out Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D box)
        {
            if (_pendingPath == jsonPath && _resolvedBox.HasValue)
            {
                box = _resolvedBox.Value;
                _pendingPath = null;
                _resolvedBox = null;
                return true;
            }

            box = default;
            return false;
        }
    }

    private sealed class ReplaySpatialBoundsGizmo : Fdp.Toolkit.Diagnostics.Gizmos.IGlobalStatelessGizmo
    {
        private readonly Func<Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D?> _getBounds;

        public ReplaySpatialBoundsGizmo(Func<Fdp.Toolkit.ReplayBrowser.Search.BoundingBox2D?> getBounds)
        {
            _getBounds = getBounds;
        }

        public void Draw(Fdp.ModuleHost.Abstractions.ISimulationView view, Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder drawBuilder)
        {
            var bounds = _getBounds();
            if (!bounds.HasValue) return;

            var box = bounds.Value;
            if (box.Min == box.Max) return;

            Vector2 center = (box.Min + box.Max) * 0.5f;
            Vector2 extents = new Vector2(
                MathF.Abs(box.Max.X - box.Min.X) * 0.5f,
                MathF.Abs(box.Max.Y - box.Min.Y) * 0.5f);

            drawBuilder.DrawBox2D(
                center,
                extents,
                new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 100, 0, 255),
                angleDeg: 0f,
                thickness: 1.5f,
                sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters,
                target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.All,
                layer: 0,
                fillColor: default,
                style: Fdp.Toolkit.Diagnostics.Gizmos.LineStyle.Dashed);
        }
    }
}
