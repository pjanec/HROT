using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Variables;
using Hrot.Diagnostics.Breakpoints;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using StructEdit.Core;

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
    /// ⭐⭐ <c>C-outline</c> — the My Blueprint outline for this perspective. ⛔ Null on the Blueprint
    /// perspective, which has its own <c>BlueprintMyBlueprintWindow</c>; non-null on BTree and HSM.
    /// </summary>
    public AiMyBlueprintWindow? MyBlueprint { get; }

    /// <summary>
    /// ⭐⭐ <c>C-table</c> — the variables table for this perspective. ⛔ NOT folded into the node
    /// inspector; that fold is <c>Architect_Question_38</c>'s merge and is deferred.
    /// </summary>
    public AiVariablesWindow Variables { get; }

    /// <summary>
    /// ⭐ The one value formatter, shared by <see cref="Variables"/> and <see cref="Watch"/>.
    /// ⛔ Two formatters would be two places to fix a rendering rule.
    /// </summary>
    public VariableValueFormatter ValueFormatter { get; }

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
    /// <param name="schemaExporter">
    ///   Optional action schema exporter. Forwarded to <see cref="InspectorWindow"/> so
    ///   sub-element collision diagnostics are shown in the Inspector diagnostic strip
    ///   (AIE-053).
    /// </param>
    /// <param name="facetEditService">
    ///   Optional StructEdit <see cref="IComponentEditService"/> forwarded to
    ///   <see cref="InspectorWindow"/> so facet structs render as editable fields (SE1).
    ///   Registered at the composition root with all picker field drawers.
    /// </param>
    /// <param name="facetCustomDrawers">
    ///   Optional map of CLR type → <see cref="IImGuiFieldDrawer"/> forwarded to
    ///   <see cref="InspectorWindow"/> for attribute-dispatched picker fields (SE1).
    /// </param>
    /// <param name="expressionTargetFieldAccessor">
    ///   Optional delegate (injected from composition root) that extracts the
    ///   <c>ExpressionTargetField</c> value from a boxed facet struct (B-3).
    ///   When supplied, enables the "Static Parameters" default-value StructEdit panel in
    ///   <see cref="InspectorWindow"/>. The delegate should return non-null only for
    ///   facet types that carry an <c>ExpressionTargetField</c> (BTree Action/Condition
    ///   facets; HSM Transition/GlobalTransition facets). Return null for other types.
    /// </param>
    /// <param name="liveValueProvider">
    ///   Optional live-value provider (BATCH-11). When non-null, the Blackboard Authoring
    ///   window shows a "Value" column with the selected entity's live values.
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
        BlackboardAggregatorService? aggregatorService = null,
        IActionSchemaExporter? schemaExporter = null,
        IComponentEditService? facetEditService = null,
        IReadOnlyDictionary<Type, IImGuiFieldDrawer>? facetCustomDrawers = null,
        Func<object?, string?>? expressionTargetFieldAccessor = null,
        ILiveBlackboardValueProvider? liveValueProvider = null,
        BlackboardHostKind? hostKind = null,
        DecodeRawValue? valueDecoder = null)
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
            store:                         selectionStore,
            refactorService:               refactorService,
            findResults:                   FindResults,
            idOverride:                    $"ai_inspector_{suffix}",
            owningPerspective:             perspectiveName,
            schemaExporter:                schemaExporter,
            facetEditService:              facetEditService,
            facetCustomDrawers:            facetCustomDrawers,
            expressionTargetFieldAccessor: expressionTargetFieldAccessor);

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
            owningPerspective:  perspectiveName,
            liveValueProvider:  liveValueProvider,
            // ⭐⭐ DEBT-AIB-009 (Batch 69): this argument was MISSING, so the hardcoded-DTO reflection
            //    path -- the one that contributes [BlackboardDtoStruct] field rows and the
            //    schema-derived type choices -- ran against a null exporter in production and silently
            //    contributed nothing. ⛔ The registrar already HELD the exporter; it handed it to the
            //    validator two lines up and not to this window.
            actionSchemaExporter: schemaExporter);

        Diagnostics = new DiagnosticsWindow(
            catalog:           catalog,
            validators:        vl,
            idOverride:        $"ai_diagnostics_{suffix}",
            owningPerspective: perspectiveName);

        // ⭐⭐ Track C, WIRED (Batch 79). Everything below was built, tested and hosted by NOTHING
        //    until this batch -- the table, its dialog launcher, the tick highlight and the outline.
        //    ⛔ Purely additive: BlackboardAuthoring above is untouched and still registered, per the
        //    user's ruling that the two variable surfaces coexist until Q38 decides the merge.
        ValueFormatter = new VariableValueFormatter(valueDecoder ?? RawValueDecoder.Instance);

        Variables = new AiVariablesWindow(
            id:                $"ai_variables_{suffix}",
            owningPerspective: perspectiveName,
            formatter:         ValueFormatter);

        // ⭐⭐⭐ Batch 80 — DERIVED, not required. The registrar already knows its perspective, so the
        //    host kind is not information a caller can supply better; leaving it to be passed is what
        //    let EditorSubsystem forget it for both AI perspectives while the tests passed.
        //    ⛔ The parameter survives as an OVERRIDE, so an unusual perspective name can still say so.
        var effectiveHost = hostKind ?? HostKindOf(perspectiveName);

        // ⛔ The Blueprint perspective already has BlueprintMyBlueprintWindow; a second outline there
        //    would be two panels for one concept. BTree and HSM had none at all -- that is the gap.
        if (effectiveHost != null)
        {
            MyBlueprint = new AiMyBlueprintWindow(
                id:                $"ai_my_blueprint_{suffix}",
                owningPerspective: perspectiveName,
                host:              effectiveHost.Value,
                // ⭐⭐ The store is PASSED, so the outline follows the active document by itself.
                //    ⛔ Batch 79 left that to the host and the host never did it (Batch 80's finding).
                store:             selectionStore);

            // ⭐ design §1c: selection yields a SECTION, and the section is the routing key. Wired
            //   here rather than left to the host, because "built but nothing connects it" is the
            //   defect this whole batch exists to fix.
            // ⭐⭐⭐ A DEFAULT resolver over the same selection store, so routing works out of the box.
            //    ⛔ Batch 79 made this depend on the host calling SetSectionSourceResolver, and no
            //    production caller did — "inert unless someone remembers" is the very shape this
            //    programme keeps finding. A host may still override it for a live (asset, entity).
            _sectionSource ??= section => new BlackboardSectionRowSource(
                asset:   () => selectionStore.ActiveAsset as IBlackboardManagedAsset,
                assetId: selectionStore.ActiveAsset?.AssetId ?? Guid.Empty,
                section: section);

            MyBlueprint.SectionSelected += section =>
            {
                var source = _sectionSource?.Invoke(section);
                if (source != null) Variables.ShowSection(section, source);
            };
        }

        // AIE-034: per-perspective Watch + Breakpoints windows (optional).
        if (breakpointManager != null)
        {
            Breakpoints = new AiBreakpointsWindow(
                id:                $"ai_breakpoints_{suffix}",
                owningPerspective: perspectiveName,
                manager:           breakpointManager);

            // ⭐⭐ The formatter is PASSED, not defaulted away. The registrar holds it two lines up;
            //    a production caller that HAS a dependency must pass it (2026-08-16 rule).
            Watch = new AiWatchWindow(
                id:                $"ai_watch_{suffix}",
                owningPerspective: perspectiveName,
                manager:           breakpointManager,
                formatter:         ValueFormatter);
        }
    }

    /// <summary>
    /// ⭐ Supplies the row source for a section id, so an outline click can re-filter the table.
    /// ⚠ Optional: without it the outline still selects and the table simply keeps its current
    /// contents — ⛔ the routing is inert, not broken, and <see cref="AiMyBlueprintWindow.SelectedSection"/>
    /// still records the choice.
    /// </summary>
    /// <summary>
    /// ⭐⭐ Which AI host a perspective name denotes — <c>"BTree"</c> and <c>"HSM"</c>, and
    /// <c>null</c> for everything else *(Blueprint included: it has its own outline)*.
    ///
    /// <para>⛔ <b>Case-insensitive on purpose.</b> The perspective name is a string chosen at the
    /// composition root; matching it exactly would turn a harmless casing difference into a silently
    /// missing panel, which is the failure this method exists to remove.</para>
    /// </summary>
    public static BlackboardHostKind? HostKindOf(string perspectiveName)
        => string.Equals(perspectiveName, "BTree", StringComparison.OrdinalIgnoreCase) ? BlackboardHostKind.BTree
         : string.Equals(perspectiveName, "HSM",   StringComparison.OrdinalIgnoreCase) ? BlackboardHostKind.Hsm
         : null;

    public void SetSectionSourceResolver(Func<string, IVariableRowSource?> resolver)
        => _sectionSource = resolver ?? throw new ArgumentNullException(nameof(resolver));

    private Func<string, IVariableRowSource?>? _sectionSource;

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

        // ⭐⭐ Track C (Batch 79). ⛔ Registered here, not left to RegisterExtraWindow: a surface the
        //    host must remember to attach is how these five came to be unreachable in the first place.
        RegisterCore(windowManager, Variables);
        if (MyBlueprint != null) RegisterCore(windowManager, MyBlueprint);
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
