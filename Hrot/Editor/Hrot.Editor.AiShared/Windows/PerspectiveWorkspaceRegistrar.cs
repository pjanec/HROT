using System;
using System.Linq;
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

    // ⛔⛔ S5 (BP-399, 2026-08-22) — THE INSPECTOR WINDOW IS RETIRED.
    //    📄 DESIGN_Details_Panel_View_Switching.md §7.6 ⑤ / §6 L5 ("per item, after its replacement is
    //       live"). All six of its arms are Details views or menu items now; after S4 removed the last
    //       one it drew nothing at all, so keeping it registered would have shipped an empty window.
    //    ⚠⚠ AND THE LAYOUT HALF IS REAL — I first claimed it was a no-op and was WRONG. A grep over
    //       *.cs found only this file, so I concluded `ai_inspector_*` was in no layout. 🔴
    //       TheDefaultLayoutIsNotStaleTests caught it: the ids ARE in layout/default/imgui.ini and
    //       layout/default/fdp_windows.json, and both are now updated. ⭐ B103b's stale-layout rail is
    //       exactly the control that was supposed to catch this, and it did — on the first run.

    /// <summary>
    /// ⭐⭐⭐ <b><c>S2</c> — this perspective's NODE-PROPERTIES services.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.3's catalogue · §7.6 ②.
    /// <para>⚠ <b>The composition root wires the facet DISPATCHER through this</b>, not through
    /// <see cref="Inspector"/> any more — the node arms are a Details view now
    /// *(<c>Shell.NodePropertiesDetailsView</c>)</para>
    /// </summary>
    public Shell.NodePropertiesSource NodeProperties { get; }

    /// <summary>
    /// ⭐⭐ <b><c>S4</c> — the PARAMETER SYNCHRONIZATION services for this perspective.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §7.6 ④. ⛔ Per-PERSPECTIVE, not per-window
    /// *(<c>R-120</c>)* — the composition root re-wires the resolver when the document changes.
    /// </summary>
    public Shell.ParameterSyncSource ParameterSync { get; } = new();

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
    public DetailsWindow? Details { get; }

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
    /// <param name="writeLive">
    ///   ⭐⭐ The host's LIVE blackboard writer, used only while frozen *(📌 ruling 15)*.
    ///   ⛔ <b>Null is not "refuse"</b> — it produces <c>Outcome.LiveWriteUnavailable</c>, whose message
    ///   says <i>"no live writer is installed for this host"</i>. ⚠ That is the honest answer for
    ///   BTree/HSM, which have no live write path at all.
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
        Func<bool>? isFrozen = null,
        WriteLiveValue? writeLive = null,
        Shell.IEntitySelectionSource? entitySelection = null,
        StagedWriteView? stagedWrites = null,
        WatchEntityPicker? entityPicker = null)
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

        // ⭐⭐⭐ L6.1a — THE WORKSPACE, built before ANY host and after the argument guards.
        //   ⛔ Everything below reads DetailsViews / EntitySelection / StagedWrites THROUGH it, and
        //     AttachEditGestures forwards the staged view during this very constructor. A later
        //     assignment would wire the hosts built before it against nothing — 📌 exactly L3.3's
        //     construction-order defect, which the production-built rail caught on its first run.
        //   ⚠ `_runState` is assigned just below, so the workspace takes a LAMBDA rather than the
        //     field: a context reads the run state at BUILD time, per frame, never captured.
        Workspace = new Shell.PerspectiveWorkspace(
            // ⚠ `_runState!` — the field is assigned two lines below and the compiler's flow analysis
            //   cannot see that the lambda only runs later. ⛔ Constructing the workspace AFTER the
            //   assignment would read better but put it after the run-state comment block, which is
            //   where a future edit would insert a host; the bang is the smaller risk, stated.
            perspectiveName, selectionStore, () => _runState!(), entitySelection, stagedWrites);
        // ⭐ Row 58 — the run state, from signals that are ABOUT TIME.
        // 🔴🔴 Batch 84 / R-66: this used to be RunStateSource.For(debugRegistry), on the premise that
        //    "a live session is what running means to this editor". MEASURED FALSE — ActiveSession is
        //    set from the ACTIVE DOCUMENT's kind, so opening any blueprint read as Running and the
        //    INITIAL arm of the Value column was unreachable in production.
        // ⛔ The registry is still a constructor argument, and still right for what it is: which
        //    document's session is active. It is simply not a clock.
        _runState = RunStateSource.For(isSimUp, isFrozen);
        // ⭐ 2026-08-21 — the same two predicates as a sentence, for the refusal message.
        _describeRunState = RunStateSource.Describe(isSimUp, isFrozen);
        var suffix = perspectiveName.ToLowerInvariant();
        var vl     = validators ?? Array.Empty<IAssetValidator>();

        // Each window gets a unique id suffix to isolate its dock slot.
        FindResults = new FindResultsWindow(
            idOverride:        $"ai_find_results_{suffix}",
            owningPerspective: perspectiveName);

        // ⭐⭐⭐ S4: the sub-asset resolver feeds the PARAMETER SYNCHRONIZATION Details view.
        //    📌 The 2026-08-16 rule — this registrar HOLDS the catalog, so it passes it.
        //    🔴 92d is this seam's own history of NOT doing so: the (now-retired) InspectorWindow was
        //       constructed here without it while the catalog sat two lines up, and every designer saw
        //       "Sub-asset resolver not configured." Kept as a named refusal, and railed.
        ParameterSync.SetSubAssetResolver(id => catalog.FindByAssetId(id) as IBlackboardManagedAsset);

        // ⭐⭐⭐ S2 (BP-399) — THE NODE-PROPERTIES SERVICES, and the ONE facet cache.
        //    📄 DESIGN_Details_Panel_View_Switching.md §7.3's catalogue · §7.6 ②.
        // 🔴 These four used to be InspectorWindow constructor arguments, which is why the composition
        //    root re-wired the DISPATCHER by calling `registrar.Inspector.SetFacetDispatcher(...)`.
        //    ⛔ The node arms are a VIEW now, and a view instance is per-WINDOW (docked · float · pin)
        //    while these services are per-PERSPECTIVE — so they live here, once (R-120).
        // ⭐ Registered into THIS perspective's catalogue immediately, so the root has nothing extra to
        //   remember (R-67). ⚠ The descriptor's predicate asks the source whether it can map the
        //   selection at all, so a perspective with no dispatcher simply never offers the view.
        NodeProperties = new Shell.NodePropertiesSource();
        NodeProperties.SetFacetEditService(facetEditService, facetCustomDrawers);
        NodeProperties.SetExpressionTargetFieldAccessor(expressionTargetFieldAccessor);

        // ⛔⛔ REGISTERED FOR THE FACET-DISPATCHING HOSTS ONLY — and this is NOT §7.3 ③'s mistake
        //    repeated, it is a DIFFERENT question with the same test.
        // 📐 FOUND BY THE ID GUARD, at startup, on the first full-editor run of S2: registering this
        //    unconditionally collided with BlueprintDetailsContribution's own `details.nodeproperties`
        //    (§7.4 draws them as TWO classes — NodePropertiesDetailsView from InspectorWindow and
        //    BlueprintNodeDetailsView from BlueprintDetailsWindow — and §7.6 ② gives them ONE id).
        //    ⭐ Batch 81's duplicate-id guard did exactly its job: loud at construction, not a silent
        //       shadow (the G4 precedent).
        // ⭐⭐ WHY THE GATE IS HONEST HERE: §7.3 ③ objects to HostKindOf gating THE SHELL, because
        //    "which blackboard host is this?" has nothing to do with whether a perspective deserves a
        //    Details panel. ⛔ But it has EVERYTHING to do with whether a perspective's nodes are
        //    described by FACETS: the facet dispatcher is a BTree/HSM concept, and EditorSubsystem
        //    wires one for exactly those two. ⇒ Blueprint's nodes are described by IBlueprintNodeDrawer
        //    instead, and its own root contributes that view.
        // ⚠ Without the gate this was harmless at DRAW time (Blueprint has no dispatcher, so CanShow
        //   is always false) and fatal at REGISTRATION time — which is the better place to fail.
        // ⭐ The registration itself is a few lines down, where `effectiveHost` is computed.

        RuntimeInspector = new RuntimeInspectorWindow(
            store:             selectionStore,
            registry:          debugRegistry,
            idOverride:        $"ai_runtime_inspector_{suffix}",
            owningPerspective: perspectiveName,
            // ⭐⭐⭐ L3.1 — the catalogue is PASSED, so RegisterPane can contribute the Runtime view.
            //   📌 The 2026-08-16 rule: this registrar HOLDS the registry (a field initialiser), so it
            //      must hand it over. ⛔ EditorSubsystem's three RegisterPane calls are UNCHANGED —
            //      the root gains nothing to forget (R-67).
            detailsViews:      DetailsViews);

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

        // ⭐⭐⭐ L3.3 — BlackboardAuthoring is built HERE (like Details), so the claim chain never
        //    sees it; its arm is mirrored through the SAME one implementation.
        ContributeDetailsViews(BlackboardAuthoring);

        Diagnostics = new DiagnosticsWindow(
            catalog:           catalog,
            validators:        vl,
            idOverride:        $"ai_diagnostics_{suffix}",
            owningPerspective: perspectiveName,
            // ⭐⭐⭐ AIE-053 — the short-name collision rows land in the ISSUE TABLE.
            // 🔒 User, 2026-08-22: "if collision strip is a warning about naming collision or
            //    something, it need to be routed to where the collision can be seen or fixed."
            // 📄 docs/designs/blueprint-integ-1/DESIGN.md §5.7: "surface SubElementCollision
            //    diagnostics … in the shared windows."
            // ⚠ The strip this replaces was DEAD — it called GetBindingAmbiguities, which returns
            //   Array.Empty unconditionally. See SubElementCollisionDiagnostics.
            // ⭐ PASSED, not defaulted: this registrar HOLDS the exporter (2026-08-16 rule).
            schemaDiagnostics: () => Validation.SubElementCollisionDiagnostics.For(schemaExporter));

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
                runState:      _runState,
                // ⭐⭐⭐ Batch 96 — THE SEVENTH INSTANCE, and it was two lines from the sixth.
                // 🔴🔴 Measured: `assetOf` had ZERO production call sites — only two tests ever passed
                //    one — so `_assetOf?.Invoke(row)` was null on every OK, `CommitInitialValue` hit
                //    `if (asset is null)`, and the designer was told "This row cannot be written — it
                //    is node-owned, a passthrough, or stale" about an ordinary variable. ⇒ the write
                //    the whole dialog exists for has never landed in production, on any host.
                // ⭐ Derived from the store this registrar already holds — ⛔ nothing new for the
                //   composition root to forget (R-67).
                assetOf:       row => DeclarationOwnerOf(selectionStore, row),
                // ⭐⭐⭐ Batch 97 (97c) — THE LIVE WRITER, host-supplied. 🔴 Batch 96 measured this
                //    parameter as never passed, so VariableEditCommit returned LiveWriteUnavailable
                //    on every paused edit, on every host. ⛔ It is NOT defaulted here: only a host
                //    that HAS a live write path may supply one, and BTree/HSM genuinely do not —
                //    their refusal stays honest rather than becoming a guessed byte offset.
                writeLive:     writeLive);

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
            EditModal = new VariableEditModal(EditGestures, _runState, idScope: suffix,
                                             describeRunState: _describeRunState);
        }



        // ⭐⭐⭐ Batch 80 — DERIVED, not required. The registrar already knows its perspective, so the
        //    host kind is not information a caller can supply better; leaving it to be passed is what
        //    let EditorSubsystem forget it for both AI perspectives while the tests passed.
        //    ⛔ The parameter survives as an OVERRIDE, so an unusual perspective name can still say so.
        var effectiveHost = hostKind ?? HostKindOf(perspectiveName);

        // ⭐⭐⭐ S2 — the generic NODE-PROPERTIES view, for the facet-dispatching hosts (see the note
        //    beside NodeProperties above for why this gate is honest and §7.3 ③'s is not).
        if (effectiveHost != null)
            DetailsViews.Add(Shell.NodePropertiesDetailsViewDescriptor.For(NodeProperties));

        // ⭐⭐ S3 (§7.6 ③) — the UTILITY CONSIDERATION stub, ported honestly as a stub.
        //    ⛔ UNGATED, and that is the point: its predicate is the SELECTION's existence, so it offers
        //       itself only where a consideration can be selected. Gating it by host kind would be a
        //       second, weaker statement of the same rule (§7.3 ③'s objection), and the selection
        //       already answers it exactly.
        //    ⚠ Measured 2026-08-22: nothing in this repo RAISES UtilityConsiderationSelection, so this
        //      view never claims the panel today. It is DORMANT, not dead — the utility-AI editor is
        //      designed (docs/designs/utility-ai/) and unbuilt. See the view's own remarks.
        DetailsViews.Add(Shell.UtilityConsiderationDetailsViewDescriptor.For());

        // ⭐⭐⭐ S4 (§7.6 ④) — PARAMETER SYNCHRONIZATION, the last of BP-399's five rows.
        //    ⛔ UNGATED for the same reason as S3: the predicate asks whether the SELECTION is a subtree
        //       node on an IBTreeSyncableAsset, which is a sharper statement than any host-kind gate and
        //       is exactly the condition the retired arm tested.
        //    ⚠ R-99 is satisfied now, not waived: the bindings this table authors reach the runtime
        //      since Q49 (identity survives a reload) and Q50 (the master declares the slice).
        DetailsViews.Add(Shell.ParameterSyncDetailsViewDescriptor.For(ParameterSync));

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
        }

        // ⭐⭐⭐ S1 — THE SHELL IS BUILT FOR **EVERY** PERSPECTIVE, OUTSIDE THE HOST-KIND GATE.
        // 📄 DESIGN_Details_Panel_View_Switching.md §7.3 ①③ (user ruling 2026-08-22: "one Details
        //    window … same/reused across the perspectives, no parallel implementations").
        //
        // 🔴🔴 THE DEFECT THIS ENDS: these lines used to sit INSIDE `if (effectiveHost != null)`, and
        //    HostKindOf() answers only BTree and Hsm. ⇒ Blueprint and Scenario never got a shell from
        //    this registrar, and BlueprintDetailsWindow — a separate sealed class with NO view
        //    registry, NO toolbar and NO float/pin — filled the slot under the SAME id and title.
        //    ⛔ `effectiveHost` answers "which blackboard host is this?". That is a fair question, and
        //       it has NOTHING to do with whether a perspective deserves a Details panel. Reusing it as
        //       the shell gate is the actual bug (§7.3 ③).
        Details = new DetailsWindow(
            id:                $"ai_details_{suffix}",
            owningPerspective: perspectiveName,
            // ⭐ The ONE formatter, shared with the standalone table and the Watch.
            formatter:         ValueFormatter,
            // ⭐⭐⭐ L2.1 — BOTH shell collaborators are PASSED, not attached later.
            //   📌 The 2026-08-16 rule: "a production caller that HAS a dependency must PASS it."
            views:             DetailsViews,
            // ⭐⭐⭐ §2: "only the workspace builds a context" — ⛔ the window reads no store.
            context:           Workspace.ContextSource());

        // ⭐⭐⭐ Wired through the SAME pair RegisterExtraWindow uses, so the routing and the run-state
        //    install have ONE implementation across all FOUR hosts (ruling 9).
        //    ⚠ `??=` — an outline that claimed the panel above keeps it; a perspective with no outline
        //      (Scenario, Blueprint) simply has none to connect, and ConnectOutlineToDetails no-ops.
        _detailsHost ??= Details;
        ConnectOutlineToDetails();

        // ⭐⭐⭐ L1.2 — Details is built HERE, not handed in through RegisterExtraWindow, so its
        //    claim-chain arm is mirrored here: ⭐ ONE registration path per concept (ruling 9).
        // ⚠ The guard is the same _viewSources set the chain uses, so a window reaching BOTH paths
        //   registers its views exactly once.
        ContributeDetailsViews(Details);

        // ⭐⭐ Batch 87's ONE attach point. ⛔ Details is a table host like any other.
        AttachEditGestures(Details);

        // ⭐⭐⭐ S1 — and a PROPERTIES-FORM host like any other. 📌 Ruling 9: the subscription has ONE
        //    implementation (SubscribePropertiesForm), reached from both construction paths.
        // ⚠ Subscribing is not the same as HAVING a form: the shell answers `false` until a host installs
        //   one (DetailsWindow.HasPropertiesForm), which is the honest shape R-109 asks for.
        SubscribePropertiesForm(Details);

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

            // ⭐⭐⭐ Batch 100 (100e) — THE NINTH INSTANCE OF THE SILENT DEFAULT, and it was ours.
            //
            // 🔴🔴 Measured: every SetRunStateSource call site was a DETAILS host. The Watch built its
            //    VariableTableModel and was never given one ⇒ it sat at Planning ⇒
            //    VariableValue.ModeFor(Planning) picks the INITIAL arm (Q32 ruling 3) ⇒ the pinned row
            //    rendered DefaultValueJson — 0 — for ever, WHILE THE ROW ITSELF WAS A LIVE CAMERA.
            //    ⛔ The feature looked built from every angle except the designer's.
            //
            // 📌 "A production caller that HAS a dependency must PASS it." This registrar holds
            //    _runState, hands it to the details host at ConnectOutlineToDetails, and holds this
            //    window. ⭐ Passed HERE, in the pass that already builds it — nothing new for
            //    EditorSubsystem to forget (R-67).
            Watch.SetRunStateSource(_runState);

            // ⭐⭐⭐ AQ55 — the map picker, PASSED in the same pass that builds the window.
            // ⚠ Optional because a headless host and a shell without an IG genuinely have no map;
            //   ⛔ but a composition root that HAS one must pass it (the silent-default rule), and
            //   HasEntityPicker is the rail surface that says whether it did — asserted on the
            //   CONSTRUCTED window, never on this line (R-67).
            if (entityPicker != null) Watch.SetEntityPicker(entityPicker);

            // ⭐⭐ Batch 87 — the Watch is built AFTER the binder, so it gets its own attach call here
            //    rather than a re-ordering of the constructor. ⛔ BP-330: its table was private with no
            //    accessor, so this could not have been written before IVariableTableHost existed.
            AttachEditGestures(Watch);
        }

        // ⭐⭐⭐ Batch 94 (94f) — the standalone Variables table, attached HERE and not earlier, for
        //    TWO reasons that both bit during this batch:
        // 🔴 ① It used to sit INSIDE `if (facetEditService != null)`, so a perspective built without
        //    an edit service got no table gestures at all. ⛔ The watch toggle has nothing to do with
        //    StructEdit; hiding it behind that service is the "one capability smuggled behind
        //    another's precondition" shape this programme keeps filing.
        // 🔴 ② The Watch window is constructed further down, so attaching before this point wired a
        //    watch gesture against a Watch that did not exist yet — silently, and only the
        //    constructed-object rail could see it.
        // ⭐ AttachEditGestures is idempotent per table, so the later RegisterExtraWindow path is
        //   unaffected.
        AttachEditGestures(Variables);
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

        // ⭐ S5: FIVE core side-panels now — the Inspector is retired (§7.6 ⑤).
        RegisterCore(windowManager, FindResults);
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
        if (Details != null)
        {
            RegisterCore(windowManager, Details);
            // ⭐⭐⭐ VC-1 — the explicit AttachWindowManager call that used to sit HERE is GONE.
            //    🔴 It had exactly one caller — this line — so the Scenario Details host, built at the
            //       composition root instead of through this registrar, never got a manager and its
            //       float/pin buttons could not draw (the user's VC-1 finding).
            //    ⭐ DetailsWindow now self-wires from ManagedWindow.OnRegistered, which RegisterCore's
            //       RegisterWindow call raises for EVERY path ⇒ no root can forget (R-126's PULL shape).
        }

        // ⭐⭐⭐ Batch 89 (BP-327, REOPENED) — THE MODAL JOINS THE FRAME.
        //    🔴🔴 Batch 87 built VariableEditModal complete — drawer body, OK, Cancel, a greyed OK with
        //    the reason on hover — and constructed it in all three perspectives. ⛔ `Draw()` had ZERO
        //    callers, production and test: the gesture opened a session, the modal held it, and no
        //    frame rendered it. ⇒ the write was complete and UNREACHABLE BY A DESIGNER, which is
        //    BP-327's own sentence, still true word for word.
        //    ⛔ NOT drawn from a window's client area: ManagedWindow.Render returns early when the
        //    window is closed or belongs to another perspective, so the dialog would vanish exactly
        //    like it does today. ⛔ NOT a line in EditorSubsystem: three registrars are three lines to
        //    forget, and R-67 is the whole reason Details, MyBlueprint and Variables are registered
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

        // ⭐⭐⭐ L1.2 — A WINDOW THAT CONTRIBUTES DETAILS VIEWS REGISTERS THEM HERE.
        //    📄 DESIGN_Details_Panel_View_Switching.md §6 L1.2: "registration through the existing
        //    claim chain — ⛔ no new root argument". 📌 R-67, and this registrar is the one that has
        //    forgotten a service four times: an interface arm means a host added later binds itself
        //    with NO new line anywhere, so there is nothing for EditorSubsystem to forget.
        // ⚠ Read ONCE, at registration — a source that varies its offer does so in its predicates
        //   (R-116), not by being re-read every frame.
        // ⛔ Registered even for a source that yields nothing: "asked and there are none" must be
        //   distinguishable from "never asked" — the second is the bug this arm exists to prevent.
        ContributeDetailsViews(window);

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

        // ⭐⭐⭐ Batch 98 (98c) — BP-360: the outline's "Watch this variable" was drawn and DEAD.
        //    📐 MyBlueprintContextMenu enables it on commands.Get("editor.toggle-variable-watch"), and
        //    nothing registered that command ⇒ Batch 94's "ONE command, TWO entry points" was half
        //    true: the table's entry was wired here, the outline's was not.
        // ⭐ Same pass, same reason as the projection above (R-67): this registrar already HOLDS the
        //   Watch, so the outline is handed the toggle here and EditorSubsystem gains nothing to forget.
        // ⚠ null when this perspective has no Watch window — the host then greys its entry instead of
        //   registering a command that would do nothing.
        if (window is IVariableWatchToggleHost watchHost)
            watchHost.SetWatchToggle(Watch is null ? null : ToggleWatch);

        // ⭐⭐⭐ Batch 99 (99a) — R-109: "Properties…" is a CUSTOM form, so the binder cannot open one
        //    and the host that HAS one must. ⭐ Same pass, same reason as the two above (R-67).
        // ⚠ Subscribed ONCE per host: RegisterExtraWindow can be called again for the same window, and
        //   a second subscription would open the form twice on one gesture.
        SubscribePropertiesForm(window);

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

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — <c>R-109</c>: <i>"Properties…"</i> is a CUSTOM form, so the
    /// binder cannot open one and the host that HAS one must.</b>
    ///
    /// <para>⚠ <b>Subscribed ONCE per host</b> — <see cref="RegisterExtraWindow"/> can be called again
    /// for the same window, and a second subscription would open the form twice on one gesture.</para>
    ///
    /// <para>⭐⭐ <b><c>S1</c> made this a METHOD.</b> 📐 It was inline in <see cref="RegisterExtraWindow"/>,
    /// which was enough while only <c>BlueprintDetailsWindow</c> — an EXTRA window — had a form. ⛔ The
    /// shell is built in the CONSTRUCTOR and never passes through that method, so leaving the arm inline
    /// would have made <c>DetailsWindow</c>'s form unreachable by the gesture. 📌 Ruling 9: one
    /// implementation, two call sites.</para>
    /// </summary>
    private void SubscribePropertiesForm(object? candidate)
    {
        if (candidate is IVariablePropertiesFormHost formHost
            && EditGestures is not null
            && _propertiesHosts.Add(formHost))
        {
            EditGestures.PropertiesRequestedForRow +=
                (row, editable) => formHost.OpenVariableProperties(row, editable);
        }
    }

    private readonly Func<VariableRunState> _runState;

    /// <summary>⭐ What the run state was OBSERVED to be — reported by a refusal, never inferred.</summary>
    private readonly Func<string> _describeRunState;

    /// <summary>⭐ A rail surface: the sentence a refusal would print right now.</summary>
    public string DescribeRunState() => _describeRunState();
    private readonly EditorSelectionStore  _selectionStore;

    /// <summary>
    /// ⭐ The perspective's selection store — the one this registrar was handed, not a copy.
    /// ⚠ Exposed so a rail can put the composition root into the state production is in
    /// (an ACTIVE ASSET, a SELECTED ENTITY) instead of building a second registrar to do it
    /// — 📌 <c>R-67</c>: <i>"a rail that builds its own composition root cannot see a
    /// composition-root defect."</i>
    /// </summary>
    public EditorSelectionStore SelectionStore => _selectionStore;

    private IVariableOutlineSelectionSource? _outlineSelection;
    private IVariableDetailsHost?            _detailsHost;

    /// <summary>
    /// ⭐⭐⭐ <b><c>L1.1</c>/<c>L1.2</c> — THIS PERSPECTIVE'S DETAILS-VIEW CATALOGUE.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2: <c>PerspectiveWorkspace *-- DetailsViewRegistry</c>.
    ///
    /// <para>⚠ <b>A STATED PLACEMENT DEVIATION.</b> §2 composes the registry into
    /// <c>PerspectiveWorkspace</c> — ⛔ but that type is extracted in <b><c>L6.1</c></b> *(§6:
    /// "extract <c>PerspectiveWorkspace</c>, give Scenario one, rename the key with a layout
    /// migration")*, not in <c>L1</c>. ⇒ ⭐ the registry lives on the registrar now, and <c>L6.1</c>
    /// carries it across when it splits §5's <i>"wiring hub"</i> half out. 📌 §5 is explicit that the
    /// generic half is <i>"trapped inside the specific one"</i> — this is one more thing that travels
    /// with it, ⛔ not a second registry.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b><c>L6.1a</c> — THE GENERIC HALF, now a collaborator rather than fields on this class.</b>
    /// 📄 §5: <i>"the generic half is trapped inside the specific one."</i> ⭐ This registrar keeps its
    /// 21-parameter AI-authoring bag; ⛔ the registry, the context builder, the entity source and the
    /// claim chain moved to <see cref="Shell.PerspectiveWorkspace"/>, so Scenario can have those four
    /// WITHOUT the bag *(<c>L6.1c</c>)*.
    /// <para>⚠ Every member below is a FORWARD, deliberately: <c>L6.1a</c> is a pure refactor and the
    /// existing callers must not move. 📌 The stage gate *(<c>TheAiOfferSetsAreUnchangedTests</c>)* is
    /// what says the move was clean.</para>
    /// </summary>
    public Shell.PerspectiveWorkspace Workspace { get; }

    /// <inheritdoc cref="Shell.PerspectiveWorkspace.DetailsViews"/>
    public Shell.DetailsViewRegistry DetailsViews => Workspace.DetailsViews;

    /// <summary>⚠ Guards against double-registration: <c>RegisterExtraWindow</c> can be called twice
    /// for the same window, and a second pass would throw on the duplicate id — ⛔ turning a harmless
    /// re-registration into a crash.</summary>
    /// <summary>
    /// ⭐ Exposed so a rail can assert on the CONSTRUCTED registrar that production passed a REAL
    /// source — 📌 <c>R-67</c>, and the control the <c>2026-08-16</c> rule prescribes.
    /// ⭐ <c>L6.1a</c>: forwarded from <see cref="Workspace"/>, which now owns it.
    /// </summary>
    public Shell.IEntitySelectionSource? EntitySelection => Workspace.EntitySelection;

    /// <summary>
    /// ⭐ Exposed so a rail can assert on the CONSTRUCTED registrar that production passed a REAL view,
    /// and that the SAME instance reached both a Details-shaped and a Watch-shaped model —
    /// 📌 <c>R-67</c>, and the <c>2026-08-16</c> rule's prescribed control.
    /// ⭐ <c>L6.1a</c>: forwarded from <see cref="Workspace"/>, which now carries it.
    /// </summary>
    public StagedWriteView? StagedWrites => Workspace.StagedWrites;

    // ⭐ L6.1a — the `_viewSources` guard moved with the chain (PerspectiveWorkspace.Contribute).

    /// <summary>
    /// ⭐⭐⭐ <b><c>L3.3</c> — the ONE place a window's Details views reach the catalogue.</b>
    /// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §6 <c>L1.2</c> *(<c>R-67</c>)*.
    ///
    /// <para>⚠⚠ <b>Collapsed from TWO copies, before <c>L3.3</c> could add a THIRD.</b> 📐 <c>L1.2</c>
    /// wrote this arm in <c>RegisterExtraWindow</c> and mirrored it in the constructor for <c>Details</c>
    /// *(which the chain never sees)*; ⛔ <c>BlackboardAuthoring</c> is a third window in the same
    /// position. 📌 Ruling 9 — <i>"no keeping two implementations for the same concept"</i> — and three
    /// copies of a guarded loop is how one of them quietly loses its guard.</para>
    ///
    /// <para>⭐ Read ONCE, at registration — a source that varies its offer does so in its predicates
    /// *(<c>R-116</c>)*, ⛔ not by being re-read every frame. ⭐ The <c>_viewSources</c> guard means a
    /// window reaching BOTH paths contributes exactly once.</para>
    /// </summary>
    /// <remarks>⭐ <c>L6.1a</c> — the BODY moved to <see cref="Shell.PerspectiveWorkspace.Contribute"/>;
    /// this stays as the registrar's name for it so the ~5 call sites are unchanged. ⛔ Not a second
    /// implementation — one line, forwarding.</remarks>
    private void ContributeDetailsViews(object? candidate) => Workspace.Contribute(candidate);
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

    /// <summary>
    /// ⭐ Binds every gesture one table needs. ⚠⚠ <b>The two gestures are guarded SEPARATELY</b> —
    /// 🔴 Batch 94 first nested the watch wiring inside the edit-service guard, so a perspective built
    /// without a <c>facetEditService</c> silently got no watch toggle either. ⛔ Two capabilities, two
    /// preconditions; ⭐ neither may hide behind the other.
    /// </summary>
    /// <summary>⭐ Hosts already subscribed to the Properties gesture — ⛔ so a re-registered window
    /// does not open the form twice on one click.</summary>
    private readonly HashSet<IVariablePropertiesFormHost> _propertiesHosts = new();

    private void AttachEditGestures(IVariableTableHost? host)
    {
        if (host is null) return;

        // ⭐⭐⭐ W4 — THE SHARED YELLOW REACHES EVERY TABLE HOST FROM HERE.
        // 📄 DESIGN_Staged_Live_Write.md §7: Details and Watch must show the SAME staged bytes ⇒ they
        //    must read the SAME StagedWriteView instance. ⛔ Not four assignments beside four
        //    constructors — 📌 the reason IVariableTableHost exists at all: "a fifth host added later
        //    must not depend on someone remembering a fourth Attach line." There are SIX now.
        // ⚠ Deliberately BEFORE the `VariableTable is null` guard below: a host can own a MODEL without
        //   yet owning a control, and a model with no yellow is precisely the silent default this
        //   forwarding exists to prevent.
        if (host.TableModel is { } model) model.StagedWrites = Workspace.StagedWrites;

        if (host.VariableTable is not { } table) return;

        // ⭐⭐⭐ Batch 100 (100f) — THE HOST'S OWN ANSWER, carried to its table.
        // 🔴 This method used to give EVERY table host EVERY gesture, because it had nothing to ask.
        //    ⛔ Not an `if (host is AiWatchWindow)`: that puts the Watch's editorial decision in a file
        //    that knows nothing about watching, and — measured — there are TWO watch surfaces, so the
        //    type test would already have missed one (Blueprints' WatchPanelWindow).
        table.Gestures = host.Gestures;

        if (EditGestures is not null && !_boundTables.Contains(table))
        {
            EditGestures.Attach(table);
            _boundTables.Add(table);
        }

        AttachWatchGesture(table);
    }

    /// <summary>
    /// ⭐⭐⭐ Batch 94 (<c>94f</c>) — wires one table's watch toggle to THIS perspective's Watch store.
    ///
    /// <para>⭐⭐ Attached through <see cref="IVariableTableHost"/>, i.e. the SAME one place the edit
    /// gestures go — ⛔ not per host. 📌 That interface exists precisely because <i>"a fifth host added
    /// later must not depend on someone remembering a fourth Attach line."</i></para>
    ///
    /// <para>⚠ <b>No Watch window ⇒ nothing is wired, and the entry then reports "not pinned" and does
    /// nothing.</b> ⭐ That is honest for a perspective built without a breakpoint manager — ⛔ and it
    /// is why <c>IsWatched</c> is a delegate rather than a bool the control caches.</para>
    /// </summary>
    private void AttachWatchGesture(VariableTableControl table)
    {
        if (Watch is null || table.IsWatched is not null) return;

        table.IsWatched            = IsWatched;
        table.WatchToggleRequested += ToggleWatch;

        // ⭐⭐⭐ AQ55 — the "…on entity…" entry, wired in the SAME place as the toggle. ⛔ Not a second
        //    Attach line for a host to forget: IVariableTableHost exists precisely so "a fifth host
        //    added later must not depend on someone remembering a fourth Attach line."
        table.CanPinOnEntity        = () => Watch?.HasEntityPicker == true;
        table.PinOnEntityRequested += PinOnPickedEntity;
    }

    /// <summary>
    /// ⭐⭐ <b><c>AQ55</c> — route the row to the Watch's picker.</b>
    ///
    /// <para>⚠ <b>Fire-and-forget, and deliberately so:</b> the pick is an async gesture that resolves
    /// when the designer clicks the map, possibly many frames later. ⛔ Awaiting it inside a menu
    /// callback would block the draw thread on a click that has not happened.</para>
    ///
    /// <para>⛔ A fault is swallowed to the console rather than left unobserved — a cancelled pick is
    /// already <c>false</c> inside <c>PinOnPickedEntityAsync</c>, so anything reaching here is a real
    /// failure of the host's pick service.</para>
    /// </summary>
    private void PinOnPickedEntity(VariableRow row)
    {
        if (Watch is not { } watch) return;

        _ = watch.PinOnPickedEntityAsync(row).ContinueWith(
            t => Console.WriteLine($"[AQ55] pin-on-entity failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// ⭐⭐ <b>Is this row pinned?</b> — asked of the STORE, not of a remembered flag. ⛔ Two panels can
    /// pin the same variable, and the store is the only truth about what is pinned.
    /// ⚠ <c>false</c> when the perspective has no Watch, which is honest rather than a throw.
    /// </summary>
    private bool IsWatched(VariableRow row)
        => Watch is { } w && w.Pinned.GetRows().Any(r => r.Origin.Key.Equals(row.Origin.Key));

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 98 (<c>98c</c>) — THE toggle, now shared by both entry points.</b>
    ///
    /// <para>🔴 It used to live inline in <see cref="AttachWatchGesture"/>, reachable only by the
    /// Details table. ⇒ the outline's <i>"Watch this variable"</i> had nothing to call and its command
    /// was never registered *(<c>BP-360</c>)*. ⭐ Batch 94 specified <b>ONE command, TWO entry
    /// points</b>; ⛔ giving the outline its own copy would have made that two implementations
    /// *(ruling 9)* — the second of which would drift on the unpin-first rule below.</para>
    /// </summary>
    private void ToggleWatch(VariableRow row)
    {
        if (Watch is not { } watch) return;
        // ⭐ Unpin FIRST: a toggle resolved against the store, ⛔ never a remembered flag.
        if (!watch.Pinned.Unpin(row.Origin)) watch.Pinned.Pin(row);
    }

    /// <summary>
    /// ⭐ Finds the authored entry a row stands for. ⛔ Fails closed — a row whose variable is gone
    /// opens no dialog rather than opening one over a guess.
    ///
    /// <para>⭐⭐⭐ <b>Batch 95 (<c>95a</c>) — THE ROW IS ASKED FIRST.</b> 🔴🔴 This method used to begin
    /// at the second line below, and 📐 <c>IBlackboardManagedAsset</c> is implemented by
    /// <c>HsmAsset</c> and <c>BehaviorTreeAsset</c> and by <b>nothing else</b> ⇒ on the Blueprint
    /// perspective it returned <c>null</c> ALWAYS, so <c>VariableEditGestureBinder.Open</c> bailed on
    /// its <c>if (entry is null) return;</c> and <b>neither "Edit value…" nor "Properties…" could ever
    /// open a dialog there</b> — which is also why the visual check's <c>C</c> and <c>D</c> could not
    /// be run at all.</para>
    ///
    /// <para>⭐ The store lookup STAYS as the fallback: a row from a source that predates the arm (or a
    /// hand-built one) still resolves exactly as before. ⛔ It is no longer the only answer.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b>Batch 96 — the asset whose DECLARATION an initial-value edit updates.</b>
    ///
    /// <para>⚠⚠ <b>Keyed on the ROW's asset id, not simply "the active asset".</b> The Watch mixes rows
    /// from arbitrary assets *(<c>VariableRow</c>'s own doc: <i>"in Watch there is no single one"</i>)*,
    /// so writing blindly to whatever document happens to be open would land a designer's edit
    /// <b>in the wrong asset</b> — ⛔ silently, and with an Ok outcome saying it worked.</para>
    ///
    /// <para>⚠ <b>Blueprint still resolves to <c>null</c> HERE, and since Batch 98 that no longer
    /// refuses the edit.</b> The write target is typed <c>IBlackboardManagedAsset</c>, which
    /// <c>BlueprintAsset</c> does not implement — and 📌 <b><c>98a</c> closed that</b> by giving the ROW
    /// a write-back *(<c>VariableRow.WriteDefault</c>)*, which <c>CommitInitialValue</c> now asks FIRST.
    /// ⇒ ⭐ this method is the <b>FALLBACK</b>, for a row built without one.</para>
    ///
    /// <para>⛔ <b>It is not dead code and must not be deleted:</b> a hand-constructed row, or any host
    /// with an asset but no schema source, still lands here — and its refusal is still
    /// <c>RefusedNoDeclarationOwner</c>, which says what is missing ⛔ instead of blaming the row's kind.</para>
    /// </summary>
    private static IBlackboardManagedAsset? DeclarationOwnerOf(
        EditorSelectionStore store, VariableRow row)
        => store.ActiveAsset is IBlackboardManagedAsset asset
        && store.ActiveAsset.AssetId == row.Origin.AssetId
            ? asset
            : null;

    private static BlackboardVariableEntry? ResolveEntry(EditorSelectionStore store, VariableRow row)
    {
        // ⭐⭐ The source that built the row already held the schema that declares it.
        if (row.ReadDeclaration?.Invoke() is { } carried) return carried;

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
