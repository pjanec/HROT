using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Vis2D;
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
    private EntityInspectorPanel? _inspectorPanel;
    private EventBrowserPanel? _eventPanel;
    private ReplaySearchPanel? _searchPanel;

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

            _exportService = new RecordingExportService();
            _fileDialogService = new WinFormsFileDialogService();
            _timelinePanel = new ReplayTimelinePanel(
                _context,
                _exportService,
                _fileDialogService,
                _playbackHistory);

            _inspectorPanel = new EntityInspectorPanel();
            _diffPanel = new ComponentDiffPanel();
            _eventPanel = new EventBrowserPanel(_context.HistoryService);

            WireDelegates();

            // Search panel is created after WireDelegates so it receives the wired
            // seek/select intents.
        }
    }

    public void Update(float deltaTime) { }

    public void DrawWorld()
    {
        if (!_headless)
            _canvas?.Draw();
    }

    public void DrawUI() { }

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
    /// objects so ExecuteCausalityJump operates on the same instances in tests.
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

        return (seekIntent, selectIntent);
    }

    /// <summary>
    /// Executes the "Step Forward and Diff Target" causality macro.
    /// Sequence: push pre-frame, step forward, push post-frame, select target.
    /// Exposed internal for testability.
    /// </summary>
    internal void ExecuteCausalityJump(Entity target)
    {
        int preFrame = _context.CurrentFrame;
        _playbackHistory.PushFrame(preFrame);
        _context.StepForward();
        int postFrame = _context.CurrentFrame;
        _playbackHistory.PushFrame(postFrame);
        _entityHistory.PushSelection(target);
    }

    // ── Null service stubs (used until real implementations are injected) ──

    private sealed class NullRecordingExportService : IRecordingExportService
    {
        public void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options) { }
    }

    private sealed class NullFileDialogService : IFileDialogService
    {
        public System.Threading.Tasks.Task<string?> ShowSaveAsDialogAsync(
            string defaultFileName, string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);

        public System.Threading.Tasks.Task<string?> ShowOpenFileDialogAsync(string extensionFilter)
            => System.Threading.Tasks.Task.FromResult<string?>(null);
    }
}
