using Fdp.Presentation.WindowManager;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Creates and registers all <b>side-panel</b> windows for one editor perspective
/// (BTree, HSM, or Blueprint), binding each to a distinct ImGui <c>###Id</c> and
/// the correct <c>OwningPerspective</c> so each perspective remembers its own
/// dock layout independently.
/// <para>
/// <b>What is registered:</b> Inspector, RuntimeInspector, TraceTimeline, FindResults,
/// BlackboardAuthoring, Diagnostics — one instance each, tagged with
/// <c>OwningPerspective = perspectiveName</c>.
/// </para>
/// <para>
/// <b>What is NOT registered here:</b> the graph canvas window and kind-specific
/// panels (My Blueprint, HSM Globals, Variables) — these are Phase 2/4 concerns.
/// Use <see cref="RegisterExtraWindow"/> to attach them from the composition root
/// or a derived registrar.
/// </para>
/// </summary>
public class PerspectiveWorkspaceRegistrar
{
    private readonly string _perspectiveName;
    private readonly List<ManagedWindow> _registered = new();

    // ── Windows ───────────────────────────────────────────────────────────────

    /// <summary>The Find-Results window for this perspective.</summary>
    public FindResultsWindow FindResults { get; }

    /// <summary>The Inspector window for this perspective.</summary>
    public InspectorWindow Inspector { get; }

    /// <summary>The Runtime Inspector window for this perspective.</summary>
    public RuntimeInspectorWindow RuntimeInspector { get; }

    /// <summary>The Trace Timeline window for this perspective.</summary>
    public TraceTimelineWindow TraceTimeline { get; }

    /// <summary>The Blackboard Authoring window for this perspective.</summary>
    public BlackboardAuthoringWindow BlackboardAuthoring { get; }

    /// <summary>The Diagnostics window for this perspective.</summary>
    public DiagnosticsWindow Diagnostics { get; }

    /// <summary>
    /// Optional per-perspective Breakpoints window (null when no breakpoint manager
    /// was supplied at construction). Registered by <see cref="RegisterWindows"/>.
    /// </summary>
    public AiBreakpointsWindow? Breakpoints { get; }

    /// <summary>
    /// Optional per-perspective Watch window (null when no breakpoint manager was
    /// supplied at construction). Registered by <see cref="RegisterWindows"/>.
    /// </summary>
    public AiWatchWindow? Watch { get; }

    /// <summary>
    /// All windows registered by this registrar (including any added via
    /// <see cref="RegisterExtraWindow"/>). Useful for test verification.
    /// </summary>
    public IReadOnlyList<ManagedWindow> RegisteredWindows => _registered;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the registrar and all side-panel windows for <paramref name="perspectiveName"/>.
    /// </summary>
    /// <param name="perspectiveName">
    ///   The perspective key (e.g. <c>"BTree"</c>, <c>"HSM"</c>, <c>"Blueprint"</c>).
    ///   Used as <c>OwningPerspective</c> for all registered windows and as a suffix
    ///   for the window IDs to ensure distinct dock layouts per perspective.
    /// </param>
    /// <param name="selectionStore">
    ///   The per-perspective <see cref="EditorSelectionStore"/>. Each perspective
    ///   must supply its own store so Inspector / Blackboard track the right asset.
    /// </param>
    /// <param name="catalog">The shared asset catalog (used by Diagnostics).</param>
    /// <param name="refactorService">The shared refactor service.</param>
    /// <param name="debugRegistry">The shared debug session registry.</param>
    /// <param name="validators">
    ///   Asset validators shown by the Diagnostics window.
    ///   Pass an empty array when none are registered yet.
    /// </param>
    /// <param name="breakpointManager">
    ///   Optional shared <see cref="IDataBreakpointManager"/>. When non-null, per-perspective
    ///   <see cref="AiBreakpointsWindow"/> and <see cref="AiWatchWindow"/> are created and
    ///   registered by <see cref="RegisterWindows"/>. Both windows share this single manager
    ///   instance — no duplication.
    /// </param>
    /// <param name="sanitizerRegistry">
    ///   Optional comparison sanitizer registry. Forwarded to
    ///   <see cref="BlackboardAuthoringWindow"/> so the comparison toolbar is shown (AIE-050).
    /// </param>
    /// <param name="exportBuilder">
    ///   Optional comparison export builder. Forwarded to <see cref="BlackboardAuthoringWindow"/>
    ///   (AIE-050).
    /// </param>
    /// <param name="sessionRegistry">
    ///   Optional comparison session registry. Forwarded to <see cref="BlackboardAuthoringWindow"/>
    ///   (AIE-050).
    /// </param>
    /// <param name="aggregatorService">
    ///   Optional blackboard aggregator service. Forwarded to <see cref="BlackboardAuthoringWindow"/>
    ///   so budget warnings from sub-tree DTO requirements surface in the bin-packing display
    ///   (AIE-052).
    /// </param>
    public PerspectiveWorkspaceRegistrar(
        string perspectiveName,
        EditorSelectionStore selectionStore,
        IAssetCatalog catalog,
        IRefactorService refactorService,
        IDebugSessionRegistry debugRegistry,
        IReadOnlyList<IAssetValidator>? validators = null,
        IDataBreakpointManager? breakpointManager = null,
        SanitizerRegistry? sanitizerRegistry = null,
        ComparisonExportBuilder? exportBuilder = null,
        ComparisonSessionRegistry? sessionRegistry = null,
        BlackboardAggregatorService? aggregatorService = null)
    {
        if (string.IsNullOrWhiteSpace(perspectiveName))
            throw new ArgumentException("perspectiveName must not be null or whitespace.", nameof(perspectiveName));
        if (selectionStore is null) throw new ArgumentNullException(nameof(selectionStore));
        if (catalog       is null) throw new ArgumentNullException(nameof(catalog));
        if (refactorService is null) throw new ArgumentNullException(nameof(refactorService));
        if (debugRegistry is null) throw new ArgumentNullException(nameof(debugRegistry));

        _perspectiveName = perspectiveName;
        var suffix = perspectiveName.ToLowerInvariant();
        var vl     = validators ?? Array.Empty<IAssetValidator>();

        // Each window gets a unique id suffix to isolate its dock slot.
        FindResults = new FindResultsWindow(
            idOverride:        $"ai_find_results_{suffix}",
            owningPerspective: perspectiveName);

        Inspector = new InspectorWindow(
            store:             selectionStore,
            refactorService:   refactorService,
            findResults:       FindResults,
            idOverride:        $"ai_inspector_{suffix}",
            owningPerspective: perspectiveName);

        RuntimeInspector = new RuntimeInspectorWindow(
            store:             selectionStore,
            registry:          debugRegistry,
            idOverride:        $"ai_runtime_inspector_{suffix}",
            owningPerspective: perspectiveName);

        TraceTimeline = new TraceTimelineWindow(
            store:             selectionStore,
            registry:          debugRegistry,
            idOverride:        $"ai_trace_timeline_{suffix}",
            owningPerspective: perspectiveName);

        BlackboardAuthoring = new BlackboardAuthoringWindow(
            store:              selectionStore,
            refactorService:    refactorService,
            sanitizerRegistry:  sanitizerRegistry,
            exportBuilder:      exportBuilder,
            sessionRegistry:    sessionRegistry,
            aggregatorService:  aggregatorService,
            idOverride:         $"ai_blackboard_variables_{suffix}",
            owningPerspective:  perspectiveName);

        Diagnostics = new DiagnosticsWindow(
            catalog:           catalog,
            validators:        vl,
            idOverride:        $"ai_diagnostics_{suffix}",
            owningPerspective: perspectiveName);

        // AIE-034: per-perspective Watch + Breakpoints windows (optional).
        if (breakpointManager != null)
        {
            Breakpoints = new AiBreakpointsWindow(
                id:                $"ai_breakpoints_{suffix}",
                owningPerspective: perspectiveName,
                manager:           breakpointManager);

            Watch = new AiWatchWindow(
                id:                $"ai_watch_{suffix}",
                owningPerspective: perspectiveName,
                manager:           breakpointManager);
        }
    }

    // ── Registration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all side-panel windows with <paramref name="windowManager"/>.
    /// Must be called once during editor startup.
    /// </summary>
    public virtual void RegisterWindows(WindowManager windowManager)
    {
        if (windowManager is null) throw new ArgumentNullException(nameof(windowManager));

        // Register the six core side-panels.
        RegisterCore(windowManager, FindResults);
        RegisterCore(windowManager, Inspector);
        RegisterCore(windowManager, RuntimeInspector);
        RegisterCore(windowManager, TraceTimeline);
        RegisterCore(windowManager, BlackboardAuthoring);
        RegisterCore(windowManager, Diagnostics);

        // AIE-034: per-perspective Watch + Breakpoints windows (created only when a
        // DataBreakpointManager was supplied; null means "not wired yet").
        if (Breakpoints != null) RegisterCore(windowManager, Breakpoints);
        if (Watch      != null) RegisterCore(windowManager, Watch);
    }

    // ── Extension seam ────────────────────────────────────────────────────────

    /// <summary>
    /// Registers an additional window (e.g. the graph canvas or a kind-specific panel)
    /// with the window manager tracked by this registrar.
    /// <para>
    /// Intended for Phase 2/4 composition: the canvas window, My Blueprint panel,
    /// HSM Globals strip, and Blueprint Variables window are added here by the
    /// concrete editor host after calling <see cref="RegisterWindows"/>.
    /// </para>
    /// </summary>
    /// <param name="windowManager">The window manager to register with.</param>
    /// <param name="window">The extra window to register.</param>
    public void RegisterExtraWindow(WindowManager windowManager, ManagedWindow window)
    {
        if (windowManager is null) throw new ArgumentNullException(nameof(windowManager));
        if (window        is null) throw new ArgumentNullException(nameof(window));

        RegisterCore(windowManager, window);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RegisterCore(WindowManager wm, ManagedWindow window)
    {
        wm.RegisterWindow(window);
        _registered.Add(window);
    }
}
