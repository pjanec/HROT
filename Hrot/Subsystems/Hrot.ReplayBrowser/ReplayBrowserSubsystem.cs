using System;
using System.Numerics;
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
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
{
    // ── ISubsystem ────────────────────────────────────────────────────────

    public string Name => "ReplayBrowser";
    public Vector4 TitleBarColor => new(0.2f, 0.6f, 0.8f, 1f);

    // ── State (always allocated on Initialize) ────────────────────────────

    private ReplayBrowserContext _context = null!;
    private EntitySelectionHistory _entityHistory = null!;
    private PlaybackHistoryTracker _playbackHistory = null!;
    private bool _headless;

    // ── State (non-headless only) ─────────────────────────────────────────

    private MapCanvas? _canvas;
    private InspectorState? _inspectorState;
    private RepositoryAdapter? _session;
    private ReplayTimelinePanel? _timelinePanel;
    private IFileDialogService? _fileDialogService;
    private IRecordingExportService? _exportService;
    private ComponentDiffPanel? _diffPanel;
    private ComponentDiffService _diffService = null!;
    private EntityInspectorPanel? _inspectorPanel;
    private EventBrowserPanel? _eventPanel;
    private ReplaySearchPanel? _searchPanel;
    private ScenarioSerializer _scenarioSerializer = null!;
    // ── Continuous Diff Tracking ──────────────────────────────────────────
    private int _lastDiffFrame = -1;
    private Entity? _lastDiffEntity = null;
    private float _playbackAccumulator = 0f;
    // â”€â”€ Gizmo debug overlay â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer? _gizmoBuffer;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager? _globalGizmoManager;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem? _dataDrivenGizmoSystem;
    private Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem? _statelessGizmoSystem;
    private Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer? _gizmoLayer;
    private Fdp.Core.FdpEventBus? _interactionBus;
    private Hrot.Common.Systems.GlobalActionDispatchSystem? _actionDispatchSystem;
    private Hrot.ScenarioEditor.Systems.SelectionInteractionSystem? _selectionSystem;
    private readonly Fdp.Toolkit.Diagnostics.Gizmos.Hub.GizmoUiStateHub _gizmoUiHub = new();

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Constructor used by <c>ScanForSubsystems</c> / <c>TryCreateSubsystem</c>.
    /// The <paramref name="networkFactory"/> is accepted but intentionally unused;
    /// the replay browser is fully offline.
    /// </summary>
    public ReplayBrowserSubsystem(INetworkFactory networkFactory) { _ = networkFactory; }

    /// <summary>Parameterless constructor for unit tests.</summary>
    public ReplayBrowserSubsystem() { }

    // ── ISubsystem lifecycle ──────────────────────────────────────────────

    public void Initialize(SubsystemConfig config)
    {
        _headless = config.Headless;
        _context = new ReplayBrowserContext();
        _entityHistory = new EntitySelectionHistory();
        _playbackHistory = new PlaybackHistoryTracker();

        if (!_headless)
        {
            _canvas = new MapCanvas();

            _inspectorState = new InspectorState();
            _session = new RepositoryAdapter(_context.SandboxRepo);

            var behaviorRegistry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(
                behaviorRegistry,
                geoTransform: null,
                entityMap: new NetworkEntityMap());
            _scenarioSerializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);
            // â”€â”€ Gizmo Setup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _gizmoBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            _interactionBus = new Fdp.Core.FdpEventBus();
            Hrot.Common.Interactions.InteractionEventRegistry.RegisterAll(_interactionBus);

            var gizmoRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry();
            var statelessRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.StatelessGizmoRegistry();
            var settingsRegistry = new Fdp.Toolkit.Diagnostics.Gizmos.Settings.GizmoSettingsRegistry();

            // 1. Register SimHost presentation gizmos (safe for SandboxRepo: relies on SimTransform, skips CullingState)
            Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settingsRegistry);
            // 2. Register Common diagnostics (LayerControl, SelectionHighlight, etc)
            Hrot.Common.Diagnostics.Gizmos.GizmoRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settingsRegistry);
            // 3. Register Canvas context menu
            Hrot.Presentation.Gizmos.GizmoRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settingsRegistry);
            // 4. Register AI behavior gizmos
            Hrot.AI.Behaviors.Gizmos.GizmoRegistrar.RegisterAll(gizmoRegistry, statelessRegistry, settingsRegistry);

            // 5. Register specific ScenarioEditor gizmos manually (Overlays, Routes, Areas)
            statelessRegistry.Register(new Hrot.ScenarioEditor.Gizmos.MapOverlayGizmo(), new[] { typeof(Fdp.Core.SimTransform), typeof(Hrot.IG.Components.MapOverlayStyle) });
            statelessRegistry.Register(new Hrot.ScenarioEditor.Gizmos.RouteGizmo(), new[] { typeof(Fdp.Toolkit.Replication.Components.TkbIdentity) });
            statelessRegistry.Register(new Hrot.ScenarioEditor.Gizmos.TacticalAreaGizmo(), new[] { typeof(Fdp.Toolkit.Replication.Components.TkbIdentity) });
            statelessRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.EntityEditorPolylineGizmo(),
                new[] { typeof(Fdp.Core.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });
            statelessRegistry.Register(
                new Hrot.ScenarioEditor.Gizmos.EntityEditorLabelGizmo(behaviorRegistry),
                new[] { typeof(Fdp.Core.SimTransform), typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity) });

            _globalGizmoManager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(_gizmoBuffer, _interactionBus);

            _dataDrivenGizmoSystem = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem(
                gizmoRegistry,
                _gizmoBuffer,
                isSelectedPredicate: static (view, entity) =>
                    view.HasComponent<Hrot.IG.Components.SelectionState>(entity) &&
                    view.GetComponentRO<Hrot.IG.Components.SelectionState>(entity).IsSelected,
                interactionBus: _interactionBus);

            // â”€â”€ Selection Interaction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var rubberBandState = new Hrot.ScenarioEditor.Gizmos.RubberBandState();
            statelessRegistry.RegisterGlobal(new Hrot.ScenarioEditor.Gizmos.RubberBandGizmo(rubberBandState));
            _statelessGizmoSystem = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.StatelessGizmoSystem(statelessRegistry, _gizmoBuffer);

            _selectionSystem = new Hrot.ScenarioEditor.Systems.SelectionInteractionSystem(_context.SandboxRepo, _interactionBus, rubberBandState);
            _selectionSystem.OnSelectionChanged += (entity, worldPos) =>
            {
                if (entity == Fdp.Core.Entity.Null)
                    _inspectorState.SelectedEntity = null;
                else if (_context.SandboxRepo.IsAlive(entity))
                    _inspectorState.SelectedEntity = entity;
            };

            // â”€â”€ Layer Control & Actions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var actionRegistry = new Hrot.Common.Interactions.GlobalActionRegistry();
            long layerControlId = Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager.NewId();
            var editService = new StructEdit.Reflection.ComponentEditServiceBuilder().Build();
            var layerControlGizmo = new Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo(
                layerControlId, _interactionBus, editService, _gizmoUiHub);

            _globalGizmoManager.Register(layerControlId, layerControlGizmo);

            actionRegistry.Register(Hrot.Common.Constants.GlobalActionIds.OpenLayerControl, (_, _) =>
            {
                _interactionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
            });

            actionRegistry.Register(Hrot.Common.Constants.GlobalActionIds.CenterOnEntity, (view, target) =>
            {
                if (target == Fdp.Core.Entity.Null) return;
                if (view.HasComponent<Fdp.Core.SimTransform>(target))
                {
                    ref readonly var tf = ref view.GetComponentRO<Fdp.Core.SimTransform>(target);
                    _canvas.Camera.FocusOn(new System.Numerics.Vector2(tf.Position.X, tf.Position.Y));
                }
            });

            _actionDispatchSystem = new Hrot.Common.Systems.GlobalActionDispatchSystem(actionRegistry, _interactionBus);

            var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
            using var layerControlSchemaSession = editService.Open(
                new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Entities = true, Perception = true, AiHelpers = true },
                typeof(Hrot.Common.Diagnostics.Gizmos.LayerControlDto));
            schemaRegistry.Register(Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash, layerControlSchemaSession.Document);

            _gizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(
                31, _gizmoBuffer, _interactionBus, _context.SandboxRepo, _canvas.Camera,
                new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(), schemaRegistry);

            _canvas.AddLayer(_gizmoLayer);
            _canvas.DrawBuffer = _gizmoBuffer;

            _diffService = new ComponentDiffService();
            _exportService = new RecordingExportService(_scenarioSerializer, _diffService);
            _fileDialogService = new WinFormsFileDialogService();
            _timelinePanel = new ReplayTimelinePanel(
                _context,
                _exportService,
                _fileDialogService,
                _playbackHistory,
                _inspectorState);

            _inspectorPanel = new EntityInspectorPanel();
            _inspectorPanel.Serializer = _scenarioSerializer;
            _diffPanel = new ComponentDiffPanel();
            _eventPanel = new EventBrowserPanel(_context.HistoryService)
            {
                SelectedProvider = "All",
                CurrentFrameProvider = () => (uint)Math.Max(0, _context.CurrentFrame)
            };

            WireDelegates();

            // Search panel is created after WireDelegates so it receives the wired
            // seek/select intents.
        }
    }

    public void Update(float deltaTime)
    {
        if (!_headless)
        {
            if (_timelinePanel != null && _timelinePanel.IsPlaying)
            {
                _playbackAccumulator += deltaTime * _timelinePanel.PlaybackRate;
                float frameTime = 1.0f / 60.0f;

                if (_playbackAccumulator > frameTime * 10f)
                    _playbackAccumulator = frameTime * 10f;

                while (_playbackAccumulator >= frameTime)
                {
                    _playbackAccumulator -= frameTime;
                    if (!_context.StepForward())
                    {
                        _timelinePanel.IsPlaying = false;
                        _playbackAccumulator = 0f;
                        break;
                    }
                }
            }

            int currentFrame = _context.CurrentFrame;
            Entity? currentEntity = _inspectorState?.SelectedEntity;

            // Reactive diff engine: re-evaluate whenever time or selection shifts.
            if (_lastDiffFrame != currentFrame || _lastDiffEntity != currentEntity)
            {
                _lastDiffFrame = currentFrame;
                _lastDiffEntity = currentEntity;

                if (_diffPanel != null)
                {
                    if (currentFrame > 0 && currentEntity.HasValue && !currentEntity.Value.IsNull)
                    {
                        _context.SeekToFrame(currentFrame - 1, suppressHistory: true);
                        _diffPanel.CurrentDiffs = _diffService.ComputeEntityDiff(
                            currentEntity.Value,
                            _context.SandboxRepo,
                            _scenarioSerializer,
                            () => _context.StepForward(suppressHistory: true));
                    }
                    else
                    {
                        _diffPanel.CurrentDiffs = Array.Empty<DiffNode>();
                    }
                }
            }

            // Allow user to pan the replay viewport when ImGui isn't capturing the mouse
            if (!ImGuiNET.ImGui.GetIO().WantCaptureMouse && _canvas != null)
                _canvas.Camera.HandleInput(new Fdp.Toolkit.Vis2D.Defaults.RaylibInputProvider());

            _canvas?.Update(deltaTime);

            // Evict transient primitives before backend population
            _gizmoBuffer?.EndFrame(deltaTime);

            if (_context.SandboxRepo != null)
            {
                _selectionSystem?.Tick(deltaTime);
                _actionDispatchSystem?.Execute(_context.SandboxRepo, deltaTime);
                _dataDrivenGizmoSystem?.Execute(_context.SandboxRepo, deltaTime);
                _globalGizmoManager?.Execute(_context.SandboxRepo, deltaTime);
                _statelessGizmoSystem?.Execute(_context.SandboxRepo, deltaTime);
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

        // Render gizmo-contributed main menu items (e.g., "View > Tactical Map Layers...")
        var gizmoMenus = _gizmoLayer?.ConsumeMainMenu();
        if (gizmoMenus != null && gizmoMenus.Count > 0)
        {
            if (ImGuiNET.ImGui.BeginMainMenuBar())
            {
                GizmoMap.Presentation.ImGuiMenuRenderer.DrawMenus(gizmoMenus, actionId =>
                {
                    _interactionBus?.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { AnchorId = 0, ActionId = actionId });
                });
                ImGuiNET.ImGui.EndMainMenuBar();
            }
        }
    }

    public void Shutdown()
    {
        _context?.Dispose();
    }

    // ── IWindowRegistrar ──────────────────────────────────────────────────

    public void RegisterWindows(WindowManager windowManager)
    {
        if (_headless) return;
        RegisterWindowsCore(
            windowManager,
            _timelinePanel!,
            _inspectorPanel!,
            _diffPanel!,
            _eventPanel!,
            _searchPanel!);
    }

    /// <summary>
    /// Test seam: registers the five replay-browser windows using caller-supplied
    /// panel instances. Skips the headless guard so tests can exercise window
    /// registration without initialising Raylib.
    /// </summary>
    internal void RegisterWindowsCore(
        WindowManager windowManager,
        ReplayTimelinePanel timelinePanel,
        EntityInspectorPanel inspectorPanel,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel,
        ReplaySearchPanel searchPanel)
    {
        string perspective = "ReplayBrowser";
        Vector4 color = TitleBarColor;

        // Capture safe references for the inspector window factories.
        InspectorState stateRef = _inspectorState ?? new InspectorState();
        RepositoryAdapter? sessionRef = _session;

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplayTimelineWindow(
            "rb_timeline", "Replay Timeline", perspective, timelinePanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FdpEntityInspectorWindow(
            "rb_inspector", "Replay Entity Inspector", perspective,
            inspectorPanel,
            () => sessionRef,
            () => stateRef,
            color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ComponentDiffWindow(
            "rb_diff", "Frame Diff Viewer", perspective, diffPanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.FdpEventBrowserWindow(
            "rb_events", "Replay Event Browser", perspective, eventPanel, color));

        windowManager.RegisterWindow(new Fdp.Presentation.Windows.ReplayBrowser.ReplaySearchWindow(
            "rb_search", "Replay Search", perspective, searchPanel, color));
    }

    // ── Delegate wiring ───────────────────────────────────────────────────

    private void WireDelegates()
    {
        var (seekIntent, selectIntent) = WireDelegatesForTest(
            _entityHistory, _playbackHistory, _inspectorState!, _context, _diffPanel!, _eventPanel!);

        _inspectorPanel!.OnEntitySelected = selectIntent;
        _inspectorPanel.ChainToMap = true;

        // Build search services.
        var editSvc = new ComponentEditServiceBuilder().Build();
        var predicateCompiler = new PredicateCompiler(editSvc);
        var eventScannerCompiler = new EventScannerCompiler(editSvc);
        var searchSvc = new RecordingSearchService(predicateCompiler, eventScannerCompiler);

        _searchPanel = new ReplaySearchPanel(editSvc, searchSvc, seekIntent, selectIntent);
    }

    /// <summary>
    /// Test seam: wires delegates using caller-supplied dependencies.
    /// Returns the seek and select intents so tests can invoke them directly.
    /// Replaces _entityHistory, _playbackHistory, and _context with the injected
    /// objects so causality-jump and reactive diff logic operate on the same instances in tests.
    /// </summary>
    internal (Action<int> seekIntent, Action<Entity> selectIntent) WireDelegatesForTest(
        EntitySelectionHistory entityHistory,
        PlaybackHistoryTracker playbackHistory,
        InspectorState inspectorState,
        ReplayBrowserContext context,
        ComponentDiffPanel diffPanel,
        EventBrowserPanel eventPanel)
    {
        _entityHistory   = entityHistory;
        _playbackHistory = playbackHistory;
        _context         = context;

        // History-driven selection: when the selection history changes, update inspector state.
        entityHistory.OnSelectionChanged += e => inspectorState.SelectedEntity = e;

        // Seek history: when the playback history fires, seek the context.
        playbackHistory.OnSeekRequested  += f => context.SeekToFrame(f);

        // Intents passed down to panels (panels stay unaware of history trackers).
        Action<int>    seekIntent   = f => { playbackHistory.PushFrame(f); context.SeekToFrame(f); };
        Action<Entity> selectIntent = e => entityHistory.PushSelection(e);

        diffPanel.OnEntityLinkClicked  = selectIntent;
        eventPanel.OnEntityLinkClicked = selectIntent;
        eventPanel.OnCausalityJumpRequested = ExecuteCausalityJump;

        return (seekIntent, selectIntent);
    }

    /// <summary>
    /// Executes the causality jump by seeking to the frame immediately after the source event
    /// and selecting the target entity. Diff rendering is handled reactively in Update().
    /// </summary>
    internal void ExecuteCausalityJump(int eventFrame, Entity target)
    {
        _playbackHistory.PushFrame(_context.CurrentFrame);
        _entityHistory.PushSelection(_inspectorState?.SelectedEntity ?? Entity.Null);

        int targetFrame = eventFrame + 1;

        _entityHistory.PushSelection(target);
        _playbackHistory.PushFrame(targetFrame);
        _context.SeekToFrame(targetFrame);
    }

    /// <summary>
    /// Compatibility overload retained for existing tests. Uses the current frame as jump origin.
    /// </summary>
    internal void ExecuteCausalityJump(Entity target)
        => ExecuteCausalityJump(_context.CurrentFrame, target);

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
    }
}
