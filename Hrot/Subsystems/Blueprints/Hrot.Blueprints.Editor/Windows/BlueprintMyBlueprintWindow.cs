using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using System;
using System.Linq;
using Hrot.Blueprints.Core.Compiler.Ir;   // VariableKind — one vocabulary for the three lists
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Variables;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Action;
using NodeEditor.UI.Panels;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="ManagedWindow"/> that hosts the NodeEdit <see cref="MyBlueprintPanel"/>
/// for the Blueprint perspective (AIE-047).
///
/// <para>
/// The window owns a <see cref="BlueprintMyBlueprintModel"/> and retargets it
/// (via <see cref="Retarget"/>) whenever the active document changes.
/// </para>
/// </summary>
public sealed class BlueprintMyBlueprintWindow : ManagedWindow, IVariableOutlineSelectionSource,
                                                 Hrot.Editor.AiShared.Selection.IDetailsSurfaceClaimant,
                                                 ILiveVariableProjectionHost,
                                                 Hrot.Editor.AiShared.Variables.IVariableWatchToggleHost
{
    // ── 98c: BP-360, the outline's watch entry ───────────────────────────────

    private Action<Hrot.Editor.AiShared.Variables.VariableRow>? _watchToggle;

    /// <inheritdoc/>
    public void SetWatchToggle(Action<Hrot.Editor.AiShared.Variables.VariableRow>? toggle)
        => _watchToggle = toggle;

    /// <summary>⭐ True once a real toggle has been installed. ⭐ A rail surface — asserted on the
    /// CONSTRUCTED window, ⛔ never on the registrar's source (📌 <c>M-22</c>).</summary>
    public bool HasWatchToggle => _watchToggle != null;

    /// <summary>
    /// ⭐⭐ <b>Resolves an outline item id to the row the Watch would pin.</b>
    ///
    /// <para>⭐ Routed through <see cref="ResolveVariableSelection"/> — the SAME resolver the Details
    /// panel uses — so the outline cannot pin a row the panel would not show. ⛔ A second lookup here
    /// would be a second answer to "which variable is this item?".</para>
    ///
    /// <para>⚠ <c>null</c> when the id names no variable *(a graph, a function, a stale id)*, and the
    /// command then refuses rather than pinning a guess.</para>
    /// </summary>
    internal Hrot.Editor.AiShared.Variables.VariableRow? RowForItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;

        foreach (var section in new[]
                 {
                     BlueprintMyBlueprintModel.SectionVariables,
                     BlueprintMyBlueprintModel.SectionParameters,
                     BlueprintMyBlueprintModel.SectionLocalVariables,
                 })
        {
            foreach (var item in _model.GetItems(section))
            {
                if (!string.Equals(item.ItemId, itemId, StringComparison.Ordinal)) continue;

                var selection = ResolveVariableSelection(item);
                if (selection.Source is null) return null;

                foreach (var row in selection.Source.GetRows())
                    if (string.Equals(row.ShortName, selection.SelectedVariablePath, StringComparison.Ordinal))
                        return row;
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 98 (<c>98c</c>) — registers the command the outline's menu looks for.</b>
    ///
    /// <para>⛔⛔ <b>Only when a real toggle was installed.</b> 📐 The menu enables itself on
    /// <c>commands.Get(id) is not null</c> — so registering unconditionally would ENABLE the entry on a
    /// perspective with no Watch and make the click do nothing. ⚠ That is the trap this item exists to
    /// close, and re-creating it one layer down would be worse than leaving it greyed.</para>
    ///
    /// <para>⭐ The refusal stays honest: with no toggle the entry greys and its tooltip already names
    /// the missing command.</para>
    /// </summary>
    /// <summary>⭐ The outline's own item id for a named variable — ⛔ so a rail asks the OUTLINE what
    /// it would pass rather than fabricating an id the scheme could move away from.</summary>
    internal string RowIdForTest(string displayName)
        => _model.GetItems(BlueprintMyBlueprintModel.SectionVariables)
                 .First(i => string.Equals(i.DisplayName, displayName, StringComparison.Ordinal))
                 .ItemId;

    private void RegisterWatchCommand(EditorCommandsImpl commands)
    {
        if (_watchToggle is null) return;

        BlueprintDocumentFactory.RegisterToggleVariableWatchCommand(
            commands,
            itemId =>
            {
                // ⛔ Never silence: BP-223/Q26-B2 — a gesture that cannot proceed says so, through the
                //   SAME indicator every other refusal in this window uses.
                if (RowForItem(itemId) is not { } row)
                {
                    Refuse("That item is not a variable this Watch can pin.");
                    return;
                }
                _watchToggle(row);
            });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 90 (<c>90b</c>) — the live projection, installed by the registrar.</b>
    /// 📌 <c>R-67</c>: the registrar already HOLDS the provider, so this arrives in its one
    /// <c>RegisterExtraWindow</c> pass and ⛔ <b>the composition root gains nothing to forget.</b>
    /// ⚠ Null in headless tests and before registration, which is exactly the <c>(pending)</c> case.
    /// </summary>
    private Hrot.Editor.AiShared.Blackboard.ILiveVariableProjection? _liveProjection;

    /// <inheritdoc/>
    public void SetLiveProjection(Hrot.Editor.AiShared.Blackboard.ILiveVariableProjection? projection)
        => _liveProjection = projection;

    /// <summary>True once a live projection has been installed. ⭐ A rail surface — asserted on the
    /// CONSTRUCTED window, ⛔ never on the registrar's source.</summary>
    public bool HasLiveProjection => _liveProjection != null;

    /// <summary>
    /// ⭐⭐ This frame's decoded live values for the active asset, or <c>null</c>.
    /// ⛔ Resolved per CALL — the row source invokes it once per <c>GetRows()</c>, i.e. once per frame,
    /// and the active asset changes under it.
    /// </summary>
    private System.Collections.Generic.IReadOnlyDictionary<string, object>? LiveObjects()
    {
        var asset = _model.EditableAsset;
        return asset is null ? null : _liveProjection?.GetLiveObjects(asset);
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 98 (<c>98a</c>) — the ONE place this window resolves "mark the document dirty".</b>
    ///
    /// <para>🔴 Before this it was computed as a LOCAL inside <c>Retarget</c> and therefore
    /// unreachable from <see cref="ResolveVariableSelection"/>, which is why the Details row source
    /// was handed <c>onChanged: () =&gt; { }</c>. ⇒ once <c>98a</c> gave that source a write, the edit
    /// would have landed in memory and never reached the file.</para>
    ///
    /// <para>⭐ Reads <c>_model.EditableAsset</c> rather than capturing one — <c>Retarget</c> assigns
    /// it before anything here runs, and the active document changes underneath. ⚠ <c>null</c> for a
    /// non-file asset *(headless tests, an in-memory document)*, which is an honest "nothing to mark".</para>
    /// </summary>
    private Action? MarkDirtyAction()
        => _model.EditableAsset is Catalog.BlueprintFileAsset bpFile ? bpFile.MarkDirty : null;

    private readonly BlueprintMyBlueprintModel _model = new();

    // Panel is lazy — requires host services, which may be null at boot if no canvas context exists.
    private MyBlueprintPanel? _panel;

    // Last known host services (updated on Retarget when AiCanvasContext is present).
    private IEditorHostServices? _hostServices;
    private IEditorCommands? _commands;

    // BCP-BATCH-02-FIX2 Task 5: variable-create modal (name + type). Rebuilt per active
    // asset so its confirm callback targets the current asset.
    private VariableCreateModal? _createVariableModal;

    // BP-12c: custom-event-create modal (name + parameters). Rebuilt per active asset for the
    // same reason as the variable modal — its confirm callback closes over the target asset.
    private CustomEventCreateModal? _createCustomEventModal;

    // BP-24: function-create modal (name only; the signature is edited in the Graph Signature
    // window). Rebuilt per active asset like its siblings.
    private FunctionCreateModal? _createFunctionModal;
    private FunctionCreateModal? _createMacroModal;

    // BP-57: the Local Variables section's "+". Same modal type as the asset-variable create —
    // the two sections offer the same gesture over two different lists, so they should not feel
    // like two different features.
    private VariableCreateModal? _createLocalVariableModal;

    // ⭐ C-sections' two "+" dialogs (2026-08-17 user ruling). Same class as the variable modal,
    //   distinguished by their nouns — which drive the title, the default name and the popup id.
    private VariableCreateModal? _createParameterModal;

    // BP-57: the locals schema source for the active document. Rebuilt per asset like the modals,
    // and for the same reason — it closes over the asset and the document's undo recorder.
    private Variables.BlueprintLocalVariableSchemaSource? _locals;

    // BP-223: where a refusal goes. Null in headless tests, which read _lastRefusal instead.
    private IEditorIndicators? _indicators;

    // BP-12b: the rename prompt for My Blueprint items. Shared by variables and custom events —
    // the per-kind validity rules live in BlueprintDocumentFactory.RenameItem, not here.
    private readonly ItemRenameModal _renameItemModal = new();

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="idOverride">Stable ImGui id; defaults to <c>"ai_my_blueprint_blueprint"</c>.</param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    public BlueprintMyBlueprintWindow(
        string? idOverride        = null,
        string? owningPerspective = null)
        : base(idOverride        ?? "ai_my_blueprint_blueprint",
               "My Blueprint",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a different active Blueprint asset (or null to clear).
    /// Also receives host services so the panel can be (re)created.
    /// </summary>
    /// <param name="view">
    /// BP-12b — the document's canvas view, when one is open. Item rename/delete/duplicate are
    /// recorded on its undo stack; without it they still work, just unrecorded.
    /// </param>
    /// <param name="currentGraphId">
    /// BP-57/BP-72 — provider for the graph the canvas is showing; pass
    /// <c>AiCanvasContext.CurrentGraphId</c>. The Local Variables section reads it; the other five
    /// sections are asset-scoped and ignore it.
    /// </param>
    /// <param name="indicators">
    /// BP-223 — where a refusal reaches the designer. ⛔ Without it, the locals "+" on a macro graph
    /// would fail silently, which is the one outcome Q26-B2 rules out.
    /// </param>
    public void Retarget(
        IEditableAsset?      editableAsset,
        BlueprintAsset?      blueprintAsset,
        IEditorHostServices? hostServices,
        IEditorCommands?     commands,
        NodeEditor.Core.View.GraphView? view = null,
        Func<Guid>?          currentGraphId = null,
        IEditorIndicators?   indicators = null)
    {
        _model.Retarget(editableAsset, blueprintAsset, currentGraphId);
        _indicators = indicators;

        // If host services changed (or panel not yet built), rebuild the panel.
        if (!ReferenceEquals(_hostServices, hostServices) || _panel == null)
        {
            _hostServices = hostServices;
            _commands     = commands;
            _panel        = null; // will be created lazily in DrawClientArea
        }

        // Build the variable-create modal for the active asset and route the My Blueprint
        // "+" command (editor.create-variable) to open it. On confirm the modal calls the
        // headless-tested create path (BlueprintDocumentFactory.CreateVariable).
        if (blueprintAsset != null && commands is EditorCommandsImpl cmdImpl)
        {
            var markDirty = MarkDirtyAction();

            // ⭐⭐⭐ Batch 98 (98c) — BP-360. Registered HERE, with the document's other commands,
            //    because this is where an EditorCommandsImpl exists. ⚠ Re-registered per retarget for
            //    the same reason the modals are rebuilt: the handler closes over this window, and the
            //    ROW it resolves must come from the document now active.
            RegisterWatchCommand(cmdImpl);
            _createVariableModal = new VariableCreateModal(
                (name, typeId, capacity, initialLength) => BlueprintDocumentFactory.CreateVariable(
                    blueprintAsset, name, typeId, markDirty, capacity, initialLength),
                blueprintAsset);

            BlueprintDocumentFactory.RegisterCreateVariableCommand(
                cmdImpl, _createVariableModal.Open);

            // BP-12c: same shape for "Custom Events +". Until this, the section declared
            // editor.create-custom-event and nothing registered it, so the button was inert —
            // and BP-07's CallCustomEvent picker had nothing it could ever list.
            // BP-24: the create now also builds the event's body graph (one undo entry via the
            // view) and the canvas switches to it, Unreal-style — declare, land in the body, wire.
            _createCustomEventModal = new CustomEventCreateModal(
                (name, parameters) =>
                {
                    var decl = BlueprintDocumentFactory.CreateCustomEvent(
                        blueprintAsset, name, parameters, markDirty, view);
                    if (decl is not null)
                        NavigateToGraphOf($"evt:{decl.Id}");
                },
                blueprintAsset);

            BlueprintDocumentFactory.RegisterCreateCustomEventCommand(
                cmdImpl, _createCustomEventModal.Open);

            // BP-24: "Functions +" / the header's "+ Function". Both were greyed-out stubs
            // (BP-12e's honesty pass) because editor.create-function had no handler — and could
            // not have one while the canvas was locked to a single graph.
            _createFunctionModal = new FunctionCreateModal(
                name =>
                {
                    var graph = BlueprintDocumentFactory.CreateFunctionGraph(
                        blueprintAsset, name, markDirty, view);
                    if (graph is not null)
                        NavigateToGraphOf($"graph:{graph.Id}");
                },
                blueprintAsset);

            BlueprintDocumentFactory.RegisterCreateFunctionCommand(
                cmdImpl, _createFunctionModal.Open);

            // BP-77: "Macros +". The command id and the button have existed since BP-12e and the
            // section header has always been drawn — only the handler was missing, so the item was
            // permanently greyed. It became worth having the moment collapse could produce macros.
            _createMacroModal = new FunctionCreateModal(
                name =>
                {
                    var graph = BlueprintDocumentFactory.CreateMacroGraph(
                        blueprintAsset, name, markDirty, view);
                    if (graph is not null)
                        NavigateToGraphOf($"graph:{graph.Id}");
                },
                blueprintAsset,
                noun: "Macro");

            BlueprintDocumentFactory.RegisterCreateMacroCommand(
                cmdImpl, _createMacroModal.Open);

            // BP-57: the Local Variables section. ⭐ The source reads the graph through a DELEGATE
            // (_model.CurrentGraph, itself resolved through the canvas's provider), so it follows a
            // BP-24 graph switch with no further wiring — a captured Graph would go stale on the
            // first switch.
            _locals = new Variables.BlueprintLocalVariableSchemaSource(
                asset:        blueprintAsset,
                currentGraph: () => _model.CurrentGraph,
                onChanged:    () => { },   // the panel re-reads GetItems every frame
                record:       BlueprintDocumentFactory.LocalVariableUndoRecorder(
                                  view, blueprintAsset, markDirty),
                refuse:       Refuse);

            _createLocalVariableModal = new VariableCreateModal(
                (name, typeId, _, _) => CreateLocalVariable(name, typeId),
                // ⚠ Deliberately NOT passed the asset. The modal's duplicate check is against
                // BlueprintAsset.Variables, and Q27-C1 makes a local that SHADOWS an asset variable
                // legal on purpose — handing it the asset would refuse a legal declaration. The
                // same-graph collision that IS an error is checked in CreateLocalVariable instead.
                asset: null,
                // ⭐⭐ The noun is LOAD-BEARING, not a label. Both fields of this window are the same
                // class, and while its popup id was a `const` the two shared ONE ImGui window: the
                // locals "+" drew both field sets and its first Create button was the ASSET modal's,
                // so declaring a local silently created a global. See VariableCreateModal.PopupId.
                noun: "Local Variable");

            BlueprintDocumentFactory.RegisterCreateLocalVariableCommand(
                cmdImpl, _createLocalVariableModal.Open);

            // ⭐⭐ C-sections — the Inputs / Working State sections' "+".
            // ⛔ BP-12c: registered HERE, beside the sections that declare the ids, so the button
            //    cannot ship inert the way Custom Events' and Macros' did.
            // ⭐⭐⭐ 2026-08-17 (user): each "+" now opens the SAME name+type dialog every other
            //    variable section opens — the quick-add was overruled as inconsistent, and its own
            //    premise ("renamable in place") was false until this batch fixed the row commands.
            // ⚠ The noun is LOAD-BEARING, not a label: VariableCreateModal derives its ImGui POPUP
            //    ID from it, and two modals sharing one popup id is the exact bug the locals modal
            //    hit (both field sets drawn in one window, the wrong Create button firing).
            _createParameterModal = new VariableCreateModal(
                (name, typeId, capacity, initialLength) => BlueprintDocumentFactory.CreateDeclaration(
                    blueprintAsset, DeclarationKind.Parameter, name, typeId, markDirty,
                    capacity, initialLength),
                blueprintAsset,
                noun: "Input");

            BlueprintDocumentFactory.RegisterCreateDeclarationCommands(
                // ⭐ Batch 86 — the Working State section is retired (R-01: one concept, one section),
                //   so its create modal is the VARIABLE one: there is only one state kind to create.
                cmdImpl, _createParameterModal.Open);

            // BP-12b: rename / delete / duplicate. The context menu has always invoked these three
            // and nothing ever handled them, so a variable could be created but never renamed or
            // removed.
            // BP-57: `locals` is what routes a `local:` item to the schema source — whose delete
            // refuses while referenced and whose undo covers Graph.LocalVariables.
            BlueprintDocumentFactory.RegisterMyBlueprintItemCommands(
                cmdImpl, blueprintAsset, view, markDirty,
                promptForName: (current, onConfirm) => _renameItemModal.Open(current, onConfirm),
                locals: _locals);
        }
        else
        {
            _createVariableModal      = null;
            _createCustomEventModal   = null;
            _createFunctionModal      = null;
            _createMacroModal         = null;
            _createLocalVariableModal = null;
            _locals                   = null;
        }
    }

    /// <summary>
    /// BP-57 — the locals "+" confirm path. ⭐ Guards the one collision the modal cannot: two locals
    /// of the same name in the same graph.
    ///
    /// <para>
    /// ⚠ <b>This is the section proving the schema source incomplete, and it is reported as such
    /// rather than patched there.</b> <c>BlueprintLocalVariableSchemaSource.AddVariable</c> appends
    /// unconditionally — correct for the drag-and-drop blackboard surface it was written against,
    /// where names are generated — but a modal takes a typed name, so the collision becomes
    /// reachable. Refusing here keeps the source's contract unchanged for <c>U-6</c> to absorb.
    /// </para>
    /// </summary>
    private void CreateLocalVariable(string name, string typeId)
    {
        if (_locals is null) return;

        var graph = _model.CurrentGraph;
        if (graph is not null &&
            graph.LocalVariables.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Refuse($"'{graph.Name}' already declares a local variable named '{name}'. "
                   + "Pick another name, or edit the existing declaration.");
            return;
        }

        // The source stores the CLR type's full name back as the TypeId, so this round-trips the
        // selectable id the modal handed us.
        var clrType = Type.GetType(typeId) ?? typeof(int);
        _locals.AddVariable(new Hrot.Editor.AiShared.Blackboard.BlackboardVariableEntry(
            Name: name, FieldType: clrType, Comment: null));
    }

    /// <inheritdoc/>
    public event Action<VariableOutlineSelection>? VariableSelectionChanged;

    /// <summary>
    /// ⭐⭐ Publishes an outline selection to whatever is listening. ⛔ The panel's own click path goes
    /// through ImGui, which no headless test can drive — this is the same call it makes, and it is the
    /// ONE place that resolves and raises so a panel rebuild cannot double-fire.
    /// </summary>
    public void PublishSelection(NodeEditor.Core.Interfaces.MyBlueprintItem? item)
        => VariableSelectionChanged?.Invoke(ResolveVariableSelection(item));

    /// <summary>
    /// ⭐⭐⭐ <b><c>Q32</c> ruling 2, resolved.</b> 📌 verbatim: <i>"Selection routes: click a
    /// <b>global</b> in My Blueprint ⇒ the list of <b>globals / working state</b>. Click a
    /// <b>local</b> ⇒ the locals of the <b>currently selected graph</b>."</i>
    ///
    /// <para>⭐ <b>Keyed on the SECTION, which is what the designer actually picked.</b> Track C's
    /// routing is section-keyed too ⇒ ⛔ <b>one routing mechanism, not two</b> (ruling 9).</para>
    ///
    /// <para>⚠ <b>One stated deviation from ruling 2's wording, and why.</b> The ruling says a global
    /// click yields <i>"globals / working state"</i> — one merged list. ⛔ <b>Not merged here:</b>
    /// 📌 <c>Q39</c> settles that <c>Variable</c> ≡ <c>WorkingState</c> and rules that the merge is
    /// <b>stage <c>D</c></b>, <i>"the only risky stage"</i>, with its own batch and a JSON migration.
    /// ⇒ ⭐ merging them in the ROUTER would do stage <c>D</c>'s job in the UI layer and would have to
    /// be undone; routing per section collapses <b>by construction</b> the day the sections do.</para>
    ///
    /// <para>⛔ A graph, function, macro or custom-event row resolves to
    /// <see cref="VariableOutlineSelection.None"/> — the Details panel then falls back to its node
    /// arm rather than leaving a stale list beside an unrelated selection.</para>
    /// </summary>
    internal VariableOutlineSelection ResolveVariableSelection(
        NodeEditor.Core.Interfaces.MyBlueprintItem? item)
    {
        var asset = _model.Asset;
        if (item is null || asset is null) return VariableOutlineSelection.None;

        // ⭐ The locals arm is GRAPH-SCOPED, and the source already follows the canvas by delegate —
        //   a captured Graph would go stale on the first BP-24 graph switch.
        if (item.SectionId == BlueprintMyBlueprintModel.SectionLocalVariables)
        {
            if (_locals is null) return VariableOutlineSelection.None;

            // ⭐⭐⭐ BATCH 84 item 4b — the heading resolves WHEN DRAWN, not when clicked.
            // 📐 Measured: the ROWS already followed the canvas (BlueprintLocalVariableSchemaSource
            //    reads the graph through a Func<Graph?>). ⛔ The HEADING did not — it was
            //    $"Local Variables — {graph.Name}" computed once, here. ⇒ ⚠ switching graph updated
            //    the rows while the label kept naming the OLD graph, so the panel contradicted itself.
            // ⭐ A delegate, not a stored Guid — the same shape the row source already uses, so there
            //   is ONE way this arm follows the canvas rather than two.
            string? LocalsHeading()
            {
                var g = _model.CurrentGraph;
                return g is null ? "Local Variables" : $"Local Variables — {g.Name}";
            }

            return new VariableOutlineSelection(
                LocalsHeading(),
                new SectionVariableRowSource(
                    assetId:   asset.AssetId,
                    assetName: asset.Name,
                    entity:    default,
                    section:   BlueprintMyBlueprintModel.SectionLocalVariables,
                    schema:    _locals,
                    // ⭐⭐⭐ Batch 90 (90b) — THE LIVE VALUES. 🔴 This source has never had a reader,
                    //    which is why every Details cell read "(pending)". ⭐ OBJECTS and not bytes:
                    //    BlueprintStateSnapshot.FieldValues is already decoded, and re-encoding it so
                    //    the byte arm could decode it again is REPORT_Batch88 §2.2's rejected (a).
                    liveObjects: LiveObjects),
                // ⭐ item 4a — WHICH row was clicked. VariablePath is the variable's name
                //   (SectionVariableRowSource builds its origin from v.Name).
                SelectedVariablePath: item.DisplayName,
                HeadingAtReadTime:    LocalsHeading);
        }

        var kind = item.SectionId switch
        {
            BlueprintMyBlueprintModel.SectionVariables    => VariableKind.Variable,
            BlueprintMyBlueprintModel.SectionParameters   => VariableKind.Parameter,
            _                                             => VariableKind.Unresolved,
        };
        if (kind == VariableKind.Unresolved) return VariableOutlineSelection.None;

        var heading = _model.Sections.FirstOrDefault(s => s.Id == item.SectionId)?.DisplayName
                      ?? item.SectionId;

        // ⭐ The asset-scoped arms need NO live heading: a section's name does not depend on the
        //   canvas, so the click-time string is still true when drawn (item 4b applies to the
        //   graph-scoped arm only).
        return new VariableOutlineSelection(
            heading,
            new SectionVariableRowSource(
                assetId:   asset.AssetId,
                assetName: asset.Name,
                entity:    default,
                section:   item.SectionId,
                // ⭐⭐⭐ Batch 98 (98a) — THE SILENT DEFAULT, in its textbook form.
                // 🔴🔴 This read `onChanged: () => { }`. ⛔ Harmless while the source was READ-ONLY —
                //    the panel re-reads every frame, so nothing needed telling — but `98a` gives this
                //    source a WRITE (UpdateVariableDefaultValueJson), and an edit that lands in the
                //    declaration without marking the document dirty is LOST ON CLOSE. The designer
                //    sees the new value, saves nothing, and the file still holds the old one.
                // ⭐ 📌 CLAUDE.md's rule, exactly: "a production caller that HAS a dependency must
                //   PASS it." This window computed `markDirty` from the SAME editable asset ~260
                //   lines above and did not hand it here. Two hundred lines away
                //   BlueprintVariablesWindow:403 constructs the SAME CLASS with the real callback.
                // ⚠ Resolved PER CALL, not captured: the active document changes under this window.
                schema:    new BlueprintVariableSchemaSource(
                               asset, kind, onChanged: MarkDirtyAction() ?? (() => { })),
                // ⭐⭐⭐ Batch 90 (90b) — the asset-scoped arm gets the same live map. ⛔ BOTH sites, or
                //    the designer would see live values on locals and "(pending)" on globals, which
                //    reads as a broken feature rather than as two seams.
                liveObjects: LiveObjects),
            SelectedVariablePath: item.DisplayName);
    }

    /// <summary>
    /// BP-223/Q26-B2 — a refusal reaches the designer as a toast. ⛔ Never a silent return: BP-76 and
    /// BP-77 were both filed because a gesture did nothing and explained nothing.
    /// </summary>
    private void Refuse(string message)
    {
        LastRefusal = message;
        _indicators?.Notify(new EditorNotification(
            Id:          "local-variable.refused",
            Severity:    NotificationSeverity.Warning,
            Title:       "Cannot do that here",
            Body:        message,
            AutoDismiss: TimeSpan.FromSeconds(10),
            Actions:     null));
    }

    /// <summary>
    /// Headless seam — the last refusal message, so a test can assert the gesture said WHY rather
    /// than merely that it changed nothing. ⚠ A test asserting only "nothing changed" would pass
    /// just as well against the silent failure this replaced.
    /// </summary>
    internal string? LastRefusal { get; private set; }

    /// <summary>
    /// BP-57 — the locals schema source for the active document, exposed for tests that drive the
    /// gestures without ImGui.
    /// </summary>
    internal Variables.BlueprintLocalVariableSchemaSource? Locals => _locals;

    /// <summary>
    /// Routes a My Blueprint item id to <c>editor.go-to-graph</c>, which owns all id resolution
    /// (BP-24). Used after a create so the canvas lands on the new graph, and by double-click.
    /// </summary>
    private void NavigateToGraphOf(string itemId)
        => _commands?.Invoke(
            NodeEditor.Core.CommandCatalog.GoToGraph,
            new NodeEditor.Core.Action.EditorCommandContext(
                null, null, new Dictionary<string, object?> { ["itemId"] = itemId }));

    /// <summary>
    /// Exposes the underlying model for tests that need to verify projection
    /// without going through ImGui.
    /// </summary>
    public BlueprintMyBlueprintModel Model => _model;

    // ── ManagedWindow ─────────────────────────────────────────────────────────


    /// <summary>
    /// ⭐⭐ <b>Invoked every frame this window holds focus</b>, so the selection store can record that
    /// the designer is working in the OUTLINE *(<c>SelectionOrigin.VariableOutline</c>)*.
    /// ⭐ A callback rather than a store reference — the registrar owns the wiring, so there is nothing
    /// for a composition root to forget.
    /// </summary>
    public Action? NotifyFocusClaim { get; set; }

    /// <inheritdoc/>
    public Hrot.Editor.AiShared.Selection.SelectionOrigin DetailsOrigin
        => Hrot.Editor.AiShared.Selection.SelectionOrigin.VariableOutline;

    protected override void DrawClientArea()
    {
        // ⭐⭐⭐ Batch 87 — claim the Details panel for the OUTLINE while this window holds focus
        //    (user ruling, 2026-08-18). ⛔ A LEVEL, not an edge — see AiGraphCanvasWindow.
        if (ImGuiNET.ImGui.IsWindowFocused(ImGuiNET.ImGuiFocusedFlags.ChildWindows))
            NotifyFocusClaim?.Invoke();

        if (_model == null || _hostServices == null || _commands == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint open.");
            return;
        }

        // Lazy panel creation (needs host services).
        if (_panel == null)
        {
            _panel = new MyBlueprintPanel(
                model:           _model,
                host:            _hostServices,
                commands:        _commands,
                navigateToGraph: _ => { },
                // BP-24 (Q23-D1): double-click navigates. The panel fires this for every item;
                // graph rows (Graphs/Functions sections) and custom events (→ their body graph)
                // route to the editor.go-to-graph handler, which owns all id resolution.
                // Variables stay non-navigating — nowhere sensible to go.
                navigateToItem:  (sectionId, itemId) =>
                {
                    if (sectionId is BlueprintMyBlueprintModel.SectionGraphs
                                  or BlueprintMyBlueprintModel.SectionFunctions
                                  or BlueprintMyBlueprintModel.SectionCustomEvents)
                    {
                        NavigateToGraphOf(itemId);
                    }
                });

            // ⭐⭐⭐ U-6 / Q32 ruling 2 — "Selection routes." A SINGLE click publishes what the
            //    Details panel should show. 🔴 BP-315 measured that MyBlueprintPanel.SelectionChanged
            //    had ZERO subscribers anywhere in the repo — the hook existed and fed nothing, which
            //    is why "Details keeps showing 'No node selected'" was a missing capability rather
            //    than a broken wire. ⭐ This is that capability.
            // ⚠ The panel is REBUILT whenever host services change, so subscribing here without a
            //   guard would double-fire after the first rebuild. ⭐ PublishSelection is the one home.
            _panel.SelectionChanged += PublishSelection;
        }

        // BP-57/BP-72: notice a canvas graph switch and fire Changed. ⚠ The panel itself re-reads
        // GetItems every frame, so the section already follows the canvas without this; the poll is
        // here because Changed is part of IMyBlueprintModel's contract. Mirrors GraphSignatureWindow's
        // snap — the same provider, polled from the same place in the same way.
        _model.SyncCurrentGraph();

        _panel.Draw();

        // Draw the create modals (opened by the section "+" commands). No-op when closed.
        _createVariableModal?.Draw();
        _createLocalVariableModal?.Draw();
        // ⛔ A modal that is opened but never drawn is a "+" that does nothing — the same inert-button
        //    shape BP-12c names, one level down.
        _createParameterModal?.Draw();
        _createCustomEventModal?.Draw();
        _createFunctionModal?.Draw();
        _createMacroModal?.Draw();
        _renameItemModal.Draw();
    }
}
