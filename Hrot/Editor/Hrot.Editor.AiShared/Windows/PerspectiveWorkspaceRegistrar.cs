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
    /// ⭐⭐⭐ <b><c>88b</c> / <c>BP-317</c> — the Details panel for this perspective.</b> ⛔ Null on
    /// Blueprint, which has its own <c>BlueprintDetailsWindow</c> *(a second one there would be two
    /// panels for one concept)*; non-null on BTree and HSM, which had none at all.
    ///
    /// <para>⭐ 📌 <c>Q32</c> ruling 6 — <i>"the same Details panel is REUSED for every asset type"</i>:
    /// what is reused is <c>VariableDetailsSection</c>, which both windows host.</para>
    /// </summary>
    public AiDetailsWindow? AiDetails { get; }

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
    /// <param name="isSimUp">
    ///   ⭐⭐ Whether the SIMULATION is running (<c>IPreviewController.IsInPreviewMode</c>).
    ///   ⛔ Null ⇒ every variable surface reads <c>Planning</c>, which is the safe answer for a host
    ///   that cannot observe the sim. 📌 <c>R-66</c>: this replaced <c>IDebugSessionRegistry</c>, whose
    ///   <c>ActiveSession</c> means <i>"a document is open"</i> and made <c>Planning</c> unreachable.
    /// </param>
    /// <param name="isFrozen">
    ///   ⭐ Whether the debugger holds time — <c>IDataBreakpointManager.IsPaused</c> OR
    ///   <c>IEngineDebugTimeController.IsPausedByDebugger</c> (📌 ruling 15 names both arms).
    ///   ⛔ Only consulted once <paramref name="isSimUp"/> holds: the editor boots frozen.
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
        DecodeRawValue? valueDecoder = null,
        Func<bool>? isSimUp = null,
        Func<bool>? isFrozen = null)
    {
        if (string.IsNullOrWhiteSpace(perspectiveName))
            throw new ArgumentException("perspectiveName must not be null or whitespace.", nameof(perspectiveName));
        if (selectionStore is null) throw new ArgumentNullException(nameof(selectionStore));
        // ⭐ Batch 87 — kept, because the focus claims are wired in RegisterExtraWindow, long after
        //   the constructor has returned.
        _selectionStore = selectionStore;

        // ⭐⭐⭐ Batch 90 — the SAME object the composition root already passes, asked whether it can
        //    project. ⛔ Not a second service: BlueprintLiveValueProvider and LiveBlackboardValueProvider
        //    each implement ILiveVariableProjection alongside the string interface they already had.
        LiveProjection = liveValueProvider as ILiveVariableProjection;
        if (catalog       is null) throw new ArgumentNullException(nameof(catalog));
        if (refactorService is null) throw new ArgumentNullException(nameof(refactorService));
        if (debugRegistry is null) throw new ArgumentNullException(nameof(debugRegistry));

        _perspectiveName = perspectiveName;
        // ⭐ Row 58 — the run state, from signals that are ABOUT TIME.
        // 🔴🔴 Batch 84 / R-66: this used to be RunStateSource.For(debugRegistry), on the premise that
        //    "a live session is what running means to this editor". MEASURED FALSE — ActiveSession is
        //    set from the ACTIVE DOCUMENT's kind, so opening any blueprint read as Running and the
        //    INITIAL arm of the Value column was unreachable in production.
        // ⛔ The registry is still a constructor argument, and still right for what it is: which
        //    document's session is active. It is simply not a clock.
        _runState = RunStateSource.For(isSimUp, isFrozen);
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
            expressionTargetFieldAccessor: expressionTargetFieldAccessor,
            // ⭐⭐⭐ Batch 92 (92d) — THE SILENT-DEFAULT PATTERN, textbook shape.
            // 🔴 This is the ONLY production construction of InspectorWindow, and it omitted the
            //    resolver while HOLDING the catalog that answers it two lines up ⇒ the PARAMETER
            //    SYNCHRONIZATION panel rendered "Sub-asset resolver not configured." everywhere
            //    (InspectorWindow:449), so no designer could author a sync binding at all.
            // ⭐ The rule: "a production caller that HAS a dependency must PASS it." The catalog is
            //    a constructor argument; nothing new is introduced here.
            // ⚠ Coherent only now: 92b makes the bindings this panel authors actually execute.
            subAssetResolver:              id => catalog.FindByAssetId(id) as IBlackboardManagedAsset);

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
            // ⭐⭐⭐ Batch 81 — "ai_variables_{suffix}" COLLIDED with BlueprintVariablesManagedWindow's
            //    own "ai_variables_blueprint" on the Blueprint perspective. WindowManager.RegisterWindow
            //    is `_windows[id] = window`, and the legacy window is registered LATER (as an extra), so
            //    the Track C table was silently EVICTED from the registry there. ⛔ That, not a title
            //    ambiguity, is why the user saw "the old version" on Blueprint.
            //    ⭐ The NEW window changes id, not the legacy one: ImGui keys dock identity on the
            //    "###Id" suffix, so moving an id costs a saved dock slot -- and this window has no slot
            //    to lose on Blueprint precisely because it was never registered there.
            id:                $"ai_variable_values_{suffix}",
            owningPerspective: perspectiveName,
            formatter:         ValueFormatter);

        // ⭐⭐ Row 58 — the table's ONE Value column switches meaning by run state, derived here.
        //    ⛔ Batch 79 shipped a settable RunState that NOTHING in production ever set.
        Variables.SetRunStateSource(_runState);

        // ⭐⭐⭐ Row 59 — the StructEdit dialog reaches the designer.
        // 🔴 THE ELEVENTH INSTANCE: VariableEditLauncher and VariableEditGestureBinder shipped in
        //    Batch 75, complete and tested, and were constructed ONLY IN TESTS — zero production
        //    call sites, measured. The dialog, the two scopes and the run-state policy all existed
        //    and no designer could reach any of them.
        // ⭐ Derived, not passed: the edit service is already a constructor argument (the Inspector
        //   needs it), the run state was just derived, and the entry resolver reads the same
        //   selection store every other surface reads.
        if (facetEditService != null)
        {
            EditGestures = new VariableEditGestureBinder(
                new VariableEditLauncher(facetEditService),
                entryResolver: row => ResolveEntry(selectionStore, row),
                runState:      _runState);

            // ⭐⭐⭐ Batch 87 — ONE attach point, reached through IVariableTableHost.
            // 🔴🔴 THE TWELFTH INSTANCE, and it was the line right here: this said
            //    `EditGestures.Attach(Variables.Control)` — the standalone window's table and NOTHING
            //    ELSE. ⛔ The Details panel and both Watch surfaces drew rows with no menu and no
            //    double-click, and the visual check read that as "the dialog has no OK button".
            // ⭐ The fix is structural, not another Attach line: every host declares
            //   IVariableTableHost and goes through AttachEditGestures, so a FIFTH host cannot be
            //   forgotten by someone not remembering a fourth call.
            // ⭐⭐ Batch 89 (89b) — the popup id is PER PERSPECTIVE, the same way every window takes an
            //    idOverride. 🔴 Once 89a puts the modal in the frame, all three registrars draw one
            //    every frame; sharing one ImGui id was correct only because `if (!IsOpen) return` fires
            //    first for the other two — an undocumented guard between two popups with one id.
            EditModal = new VariableEditModal(EditGestures, _runState, idScope: suffix);
            AttachEditGestures(Variables);
        }

        // ⭐⭐⭐ Batch 80 — DERIVED, not required. The registrar already knows its perspective, so the
        //    host kind is not information a caller can supply better; leaving it to be passed is what
        //    let EditorSubsystem forget it for both AI perspectives while the tests passed.
        //    ⛔ The parameter survives as an OVERRIDE, so an unusual perspective name can still say so.
        var effectiveHost = hostKind ?? HostKindOf(perspectiveName);

        // ⛔ The Blueprint perspective already has BlueprintMyBlueprintWindow; a second outline there
        //    would be two panels for one concept. BTree and HSM had none at all -- that is the gap.
        if (effectiveHost != null)
        {
            // ⭐⭐ Batch 87 — the outline claims the Details panel while focused. Wired below, right
            //    after construction, so the claim cannot be lost to a forgotten composition-root line.
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
                section: section,
                // ⭐⭐⭐ Batch 90 (90c) — THE BYTES. 🔴 This argument has been null since the source was
                //    built, which is why every BTree/HSM Details cell read "(pending)".
                //    ⭐ BYTES and not objects, deliberately: LiveBlackboardValueProvider already walks
                //    (BrainBlackboard, Type, ByteOffset) and only formats at the end, so it HAS them —
                //    and bytes keep §4a's change highlight LIVE, which the object arm cannot.
                //    ⛔ Resolved per CALL, not captured: the map is a per-frame snapshot and the active
                //    asset changes under it.
                readRaw: name =>
                {
                    var asset = selectionStore.ActiveAsset;
                    if (asset is null) return Array.Empty<byte>();
                    var live = LiveProjection?.GetLiveBytes(asset);
                    // ⚠ ABSENCE IS THE SIGNAL — a name the run never wrote is simply not a key, and the
                    //   row reads "(pending)". ⛔ Never a zero-filled buffer (guide row C9).
                    return live is not null && live.TryGetValue(name, out var bytes)
                        ? bytes
                        : Array.Empty<byte>();
                });

            MyBlueprint.SectionSelected += section =>
            {
                var source = _sectionSource?.Invoke(section);
                if (source != null) Variables.ShowSection(section, source);
            };

            // ⭐⭐⭐ 88b / BP-317 — the DETAILS panel for BTree and HSM.
            //    📌 Q32 ruling 6: "The same Details panel is REUSED for every asset type — HSM, BTree,
            //       Blueprint ⇒ this is a cross-host deliverable, not a blueprint one."
            //    📐 Measured (gate 8): exactly ONE production window was titled "Details" and exactly
            //       ONE type hosted a VariableDetailsSection — BlueprintDetailsWindow, on Blueprint
            //       only. ⛔ The AI perspectives had NO Details panel at all (R-60), which is what
            //       R-62 cites for keeping visual checks suspended on those two hosts.
            //    ⭐⭐ Built HERE and not by the composition root, for the same reason MyBlueprint and
            //       Variables are: a surface the host must remember to attach is how five surfaces
            //       came to be unreachable. ⇒ EditorSubsystem gains NOTHING to forget.
            AiDetails = new AiDetailsWindow(
                id:                $"ai_details_{suffix}",
                owningPerspective: perspectiveName,
                // ⭐ The ONE formatter, shared with the standalone table and the Watch.
                formatter:         ValueFormatter);

            // ⭐⭐ How a section id becomes a LIST. The registrar already holds the row-source resolver,
            //    so the outline is handed the resolution rather than the sources — ⛔ one row-source
            //    rule, used by both the standalone table and Details.
            MyBlueprint.SetSelectionResolver(section =>
            {
                var source = _sectionSource?.Invoke(section);
                return source == null
                    ? VariableOutlineSelection.None
                    : new VariableOutlineSelection(
                        BlackboardMyBlueprintModel.DisplayNameOf(section), source);
            });

            // ⭐⭐⭐ Wired through the SAME pair RegisterExtraWindow uses for Blueprint, so the routing
            //    and the run-state install have ONE implementation across all three hosts (ruling 9).
            //    ⛔ Not re-implemented here — ConnectOutlineToDetails is the one path.
            _outlineSelection ??= MyBlueprint;
            _detailsHost      ??= AiDetails;
            ConnectOutlineToDetails();

            // ⭐⭐ Batch 87's ONE attach point. ⛔ Details is a table host like any other; a second
            //    Attach line here is precisely what the twelfth instance was.
            AttachEditGestures(AiDetails);
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

            // ⭐⭐ Batch 87 — the Watch is built AFTER the binder, so it gets its own attach call here
            //    rather than a re-ordering of the constructor. ⛔ BP-330: its table was private with no
            //    accessor, so this could not have been written before IVariableTableHost existed.
            AttachEditGestures(Watch);
        }
    }

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

    /// <summary>
    /// ⭐ Overrides the row source for a section id, so an outline click re-filters the table against
    /// a LIVE <c>(asset, entity)</c> pair rather than the authored default the constructor installs.
    /// ⛔ Not required for routing to work — Batch 80 made the default unconditional precisely because
    /// no production caller ever invoked this.
    /// </summary>
    public void SetSectionSourceResolver(Func<string, IVariableRowSource?> resolver)
        => _sectionSource = resolver ?? throw new ArgumentNullException(nameof(resolver));

    private Func<string, IVariableRowSource?>? _sectionSource;

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 90 — the live projection, when the provider this registrar was handed can serve
    /// one.</b> 📌 <c>BP-334</c>: <c>ILiveBlackboardValueProvider</c> hands out STRINGS and feeds only
    /// <c>BlackboardAuthoringWindow</c>; the TABLE needs bytes or objects. ⭐ Both providers implement
    /// <see cref="ILiveVariableProjection"/>, so this is a type-test on what the composition root
    /// already passes — ⛔ <b>not a new argument to forget</b> *(<c>R-67</c>)*.
    /// </summary>
    public ILiveVariableProjection? LiveProjection { get; }

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

        // ⭐⭐ 88b — same reasoning as the two above: registered HERE, not left to the host. ⛔ Its
        //    ROUTING is already live from the constructor, so a host that forgets to register it loses
        //    the window but never leaves a half-wired panel.
        if (AiDetails != null) RegisterCore(windowManager, AiDetails);

        // ⭐⭐⭐ Batch 89 (BP-327, REOPENED) — THE MODAL JOINS THE FRAME.
        //    🔴🔴 Batch 87 built VariableEditModal complete — drawer body, OK, Cancel, a greyed OK with
        //    the reason on hover — and constructed it in all three perspectives. ⛔ `Draw()` had ZERO
        //    callers, production and test: the gesture opened a session, the modal held it, and no
        //    frame rendered it. ⇒ the write was complete and UNREACHABLE BY A DESIGNER, which is
        //    BP-327's own sentence, still true word for word.
        //    ⛔ NOT drawn from a window's client area: ManagedWindow.Render returns early when the
        //    window is closed or belongs to another perspective, so the dialog would vanish exactly
        //    like it does today. ⛔ NOT a line in EditorSubsystem: three registrars are three lines to
        //    forget, and R-67 is the whole reason AiDetails, MyBlueprint and Variables are registered
        //    HERE. ⭐ The overlay slot is the one documented for "the modal overlays all other windows".
        //    ⭐ A METHOD GROUP, not a lambda, so a rail can assert this modal's Draw is in the path.
        if (EditModal != null) windowManager.RegisterFrameOverlay(EditModal.Draw);
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

        // ⭐⭐⭐ Batch 81 — DERIVED, not passed, exactly as the host kind was in Batch 80.
        //    MyBlueprintPanel needs IEditorHostServices + IEditorCommands, which are built PER
        //    DOCUMENT and so cannot be constructor arguments. But the composition root already hands
        //    THIS registrar THIS perspective's canvas window, and AiGraphCanvasWindow.ActiveContext
        //    already resolves the active document's context (filtered to its own asset kind).
        //    ⇒ ⛔ there is nothing new for EditorSubsystem to pass, and therefore nothing to forget.
        if (window is AiGraphCanvasWindow canvas
            && MyBlueprint != null
            && !MyBlueprint.HasCanvasContextResolver)
        {
            MyBlueprint.SetCanvasContextResolver(() => canvas.ActiveContext);
        }

        // ⭐⭐⭐ U-6 — the outline→Details routing is DERIVED, not remembered.
        //    Q32 ruling 2 is "selection routes": an outline click decides what the Details panel
        //    shows. Both halves already arrive HERE — the composition root registers this
        //    perspective's outline and its details window through this very method — so connecting
        //    them is the registrar's job, not another line EditorSubsystem must not forget.
        //    ⛔ Batches 79/80/81 each lost a surface to a seam of exactly this shape.
        // ⭐ Stated over the INTERFACES so a BTree/HSM details host wires itself the day it exists
        //   (ruling 6: "the same Details panel is REUSED for every asset type").
        if (window is IVariableOutlineSelectionSource outline) _outlineSelection ??= outline;
        if (window is IVariableDetailsHost      detailsHost)   _detailsHost      ??= detailsHost;
        ConnectOutlineToDetails();

        // ⭐⭐⭐ Batch 87 — the Details panel and the Blueprint Watch arrive HERE, as extras, which is
        //    precisely why the constructor's single Attach could never have reached them. ⛔ Stated
        //    over the INTERFACE so a host added later binds itself with no new line anywhere.
        if (window is IVariableTableHost tableHost) AttachEditGestures(tableHost);

        // ⭐⭐⭐ Batch 90 (90b) — a window that BUILDS ROW SOURCES gets the live projection HERE.
        //    📌 R-67, and the Blueprint registrar is the one that has forgotten a service four times:
        //    this registrar already HOLDS the provider (it forwards it to BlackboardAuthoring), so the
        //    outline is handed it in the same pass ⇒ ⛔ EditorSubsystem gains nothing to forget.
        //    ⚠ Installed even when null, so a host can tell "asked, and there is none" from "never
        //      asked" — the second is the bug this line exists to make impossible.
        if (window is ILiveVariableProjectionHost projectionHost)
            projectionHost.SetLiveProjection(LiveProjection);

        // ⭐⭐⭐ Batch 87 — WHICH SURFACE owns the Details panel (user ruling, 2026-08-18).
        //    🔴 B8: the panel decided by comparing NODE IDENTITY, so re-clicking the same node could
        //    never win it back. ⛔ Measured: a re-click produces no signal at any layer — CanvasInput
        //    guards its assignment with !Selection.Contains(node), SelectionState has no event, the
        //    bridge assigns every frame and the store short-circuits on Equals.
        //    ⭐ FOCUS is observable every frame, so each contributing surface claims the panel while it
        //    holds focus and the store latches the last claim.
        //    ⚠ Only CONTRIBUTORS are wired here — the Watch, the Inspector and Details itself must not
        //    claim, or a window that does not drive the panel would steal it.
        if (window is AiGraphCanvasWindow focusCanvas)
            focusCanvas.NotifyFocusClaim =
                () => _selectionStore.NotifySurfaceFocused(SelectionOrigin.GraphCanvas);

        if (window is IDetailsSurfaceClaimant claimant)
            claimant.NotifyFocusClaim =
                () => _selectionStore.NotifySurfaceFocused(claimant.DetailsOrigin);
    }

    private readonly Func<VariableRunState> _runState;
    private readonly EditorSelectionStore  _selectionStore;

    private IVariableOutlineSelectionSource? _outlineSelection;
    private IVariableDetailsHost?            _detailsHost;
    private bool                             _outlineConnected;

    /// <summary>
    /// ⭐ Connects the pair once both have been registered — ⛔ in either order, because
    /// <c>RegisterExtraWindow</c>'s call order is the composition root's business and depending on it
    /// would be a second thing to remember.
    /// </summary>
    private void ConnectOutlineToDetails()
    {
        if (_outlineConnected || _outlineSelection == null || _detailsHost == null) return;
        _outlineConnected = true;

        var host = _detailsHost;
        _outlineSelection.VariableSelectionChanged += selection => host.ShowVariables(selection);

        // ⭐⭐⭐ Row 58 — the run state is DERIVED from the debug-session registry this registrar was
        //    already given. ⛔ Not a new argument at the composition root, therefore nothing to forget.
        host.SetRunStateSource(_runState);
    }

    /// <summary>
    /// True once an outline and a details host on this perspective have been wired to each other.
    /// ⭐ A rail surface — asserted on the CONSTRUCTED registrar, not on the composition root's source.
    /// </summary>
    public bool OutlineIsRoutedToDetails => _outlineConnected;

    /// <summary>
    /// ⭐⭐ Row 59 — the two row gestures ("Edit value…" / "Properties…"), bound to the shared
    /// StructEdit dialog. ⛔ Null only when no <c>IComponentEditService</c> was supplied, which is a
    /// headless host, not a production one.
    /// </summary>
    public VariableEditGestureBinder? EditGestures { get; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-327</c> — the DIALOG the gestures open.</b> Null exactly when
    /// <see cref="EditGestures"/> is: no edit service ⇒ no session ⇒ nothing to draw.
    /// ⛔ Batch 84 built the whole commit path and <b>no surface drew the session it returned</b>.
    /// </summary>
    public VariableEditModal? EditModal { get; private set; }

    /// <summary>
    /// ⭐⭐⭐ <b>Every table this registrar actually bound the row gestures to.</b>
    ///
    /// <para>📌 <b><c>R-67</c>:</b> <i>"a rail that builds its own composition root cannot see a
    /// composition-root defect."</i> ⇒ ⛔ a rail over this class's SOURCE cannot catch a missing
    /// <c>Attach</c>; ⭐ it must ask the CONSTRUCTED objects, and this is the list of them.</para>
    ///
    /// <para>⚠ <b>It records what was ATTACHED, not what was OFFERED</b> — a host whose
    /// <see cref="IVariableTableHost.VariableTable"/> is null contributes nothing, so a rail asserting
    /// <c>All(t =&gt; t.HasEditGestures)</c> cannot pass vacuously on a host that simply has no
    /// table.</para>
    /// </summary>
    public IReadOnlyList<VariableTableControl> BoundTables => _boundTables;

    private readonly List<VariableTableControl> _boundTables = new();

    /// <summary>
    /// ⭐⭐⭐ <b>THE one place row gestures are attached.</b> 🔴 Batch 87's defect was a single call site
    /// naming a single window; every host now arrives here instead.
    ///
    /// <para>⭐ Idempotent by identity — <c>RegisterExtraWindow</c> can be called more than once for the
    /// same window, and a second <c>Attach</c> would subscribe the two gestures TWICE, opening two
    /// sessions per double-click and leaking the first.</para>
    ///
    /// <para>⚠ A null <paramref name="host"/> or a null table is a NO-OP, not a throw: a Watch with no
    /// variable panel is a legitimate shape.</para>
    /// </summary>
    /// <summary>
    /// ⭐ The same binding <c>RegisterExtraWindow</c> performs, reachable without a
    /// <c>WindowManager</c>. ⛔ NOT a second implementation — it calls the one path, so a rail that
    /// uses it cannot pass while production binding is broken *(<c>R-67</c>)*.
    /// </summary>
    internal void BindTableHostForTest(IVariableTableHost host) => AttachEditGestures(host);

    private void AttachEditGestures(IVariableTableHost? host)
    {
        if (EditGestures is null || host?.VariableTable is not { } table) return;
        if (_boundTables.Contains(table)) return;

        EditGestures.Attach(table);
        _boundTables.Add(table);
    }

    /// <summary>
    /// ⭐ Finds the authored entry a row stands for, on the ACTIVE asset. ⛔ Fails closed — a row whose
    /// variable is gone opens no dialog rather than opening one over a guess.
    /// </summary>
    private static BlackboardVariableEntry? ResolveEntry(EditorSelectionStore store, VariableRow row)
    {
        if (store.ActiveAsset is not IBlackboardManagedAsset asset) return null;
        foreach (var v in asset.BlackboardVariables)
            if (string.Equals(v.Name, row.Origin.VariablePath, StringComparison.Ordinal)) return v;
        return null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RegisterCore(WindowManager wm, ManagedWindow window)
    {
        // ⭐⭐⭐ Batch 81 — the durable half. WindowManager.RegisterWindow is `_windows[id] = window`,
        //    so two windows claiming one id is a SILENT eviction: the later registration wins and the
        //    earlier window vanishes from the Window menu, the dock and every lookup, with nothing
        //    logged. ⛔ That is how the Track C table disappeared from the Blueprint perspective while
        //    its own rails stayed green — they asked the registrar, and the registrar had built it.
        //    ⇒ ⭐ ask the ARTEFACT: refuse at startup rather than lose a window at runtime.
        if (wm.TryGetWindow(window.Id, out var existing) && !ReferenceEquals(existing, window))
        {
            throw new InvalidOperationException(
                $"Window id '{window.Id}' is already registered by {existing!.GetType().Name} " +
                $"(title '{existing.Title}'); {window.GetType().Name} (title '{window.Title}') would " +
                "silently replace it. Give one of them a distinct id.");
        }

        wm.RegisterWindow(window);
        _registered.Add(window);
    }
}
