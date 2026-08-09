using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Editor window that lets an author edit a Function graph's signature:
/// its <see cref="Graph.Inputs"/> and <see cref="Graph.Outputs"/>
/// (<see cref="List{ParameterDecl}"/>: Name + <see cref="BlueprintTypeRef.TypeId"/>).
///
/// <para>
/// The window owns two <see cref="GraphSignatureEditModel"/> instances per selected
/// graph (one for Inputs, one for Outputs).  Mutations are automatically delegated
/// to the edit models; each model invokes its <c>onChanged</c> delegate which marks
/// the asset dirty via the <see cref="DirtyTracker"/> passed at construction time.
/// </para>
///
/// <para>
/// The active graph is chosen via a combo box over
/// <c>asset.Graphs.Where(g =&gt; g.Kind == GraphKind.Function)</c>
/// (BATCH-03D2: <see cref="EditorSelectionStore"/> exposes only
/// <see cref="EditorSelectionStore.SelectedAsset"/> — no active graph — so the
/// window carries its own <c>_selectedGraphId</c> view-state).
/// </para>
///
/// <para>
/// All ImGui calls are inside <see cref="DrawClientArea"/>; mutation logic lives
/// in the headless <see cref="GraphSignatureEditModel"/> so tests can drive the
/// model without a display context.  The headless seam is
/// <see cref="ResolveEditModels"/>.
/// </para>
/// </summary>
public sealed class GraphSignatureWindow : ManagedWindow
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker         _dirtyTracker;

    // ── view-state (graph picker) ─────────────────────────────────────────────
    private Guid _selectedGraphId;

    // ── cached asset ─────────────────────────────────────────────────────────
    private BlueprintAsset? _asset;

    // ── BP-72: the graph the canvas is showing ───────────────────────────────
    // Supplied by the composition root from AiCanvasContext.CurrentGraphId (which reads the
    // BlueprintGraphSwitcher). Null for callers that have no canvas (headless tests, or before a
    // document is open), in which case the window behaves exactly as it did pre-BP-72.
    private Func<Guid>? _currentCanvasGraphId;

    // Last canvas graph we snapped the picker to. The rule: follow the canvas whenever it MOVES,
    // but let an explicit pick in the combo stick until it moves again. Without this the combo
    // would fight the user every frame.
    private Guid _lastSnappedCanvasGraphId;

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="selectionStore">
    ///   Legacy <see cref="EditorSelectionStore"/> (Blueprints.Editor) driven by
    ///   the composition root's <c>ActiveChanged</c> handler.
    /// </param>
    /// <param name="dirtyTracker">
    ///   Shared dirty tracker; mutations fire
    ///   <c>dirtyTracker.MarkDirty(asset.AssetId)</c>.
    /// </param>
    /// <param name="idOverride">
    ///   Stable ImGui window id; defaults to <c>"ai_graph_signature_blueprint"</c>.
    /// </param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    /// <param name="editServiceAccessor">
    /// BP-125: resolves the shared <see cref="NodeDrawers.IEditService"/>. ⭐ Without it this window's
    /// edits only marked the asset dirty — the graph model was never rebuilt, so a declared output
    /// NEVER became a pin on the Return node, and the edit was not undoable
    /// (<c>BP-102</c>). A <b>delegate</b> rather than the service itself because the editor host
    /// constructs this window before the edit service exists; resolving lazily avoids that ordering
    /// coupling. Null (the default) preserves the pre-BP-125 behaviour for tests that do not care.
    /// </param>
    public GraphSignatureWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker         dirtyTracker,
        string?              idOverride        = null,
        string?              owningPerspective = null,
        Func<NodeDrawers.IEditService?>? editServiceAccessor = null)
        : base(idOverride        ?? "ai_graph_signature_blueprint",
               "Graph Signature",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editServiceAccessor = editServiceAccessor;
    }

    private readonly Func<NodeDrawers.IEditService?>? _editServiceAccessor;

    /// <summary>
    /// BP-125 — records one signature edit exactly as <c>ReturnNodeDrawer.RecordOutputsChange</c> does.
    ///
    /// <para>
    /// ⭐ <b>`NotifyStructureChanged` is the missing half.</b> It reaches
    /// <c>BlueprintDocumentFactory</c> ⇒ <c>graphModel.RebuildAndNotify()</c> ⇒ pins re-project. Marking
    /// the asset dirty (all this window used to do) changes the model and leaves the canvas showing the
    /// old pin set — which is why adding an output here appeared to do nothing at all.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Deliberately byte-identical to the Return-node path.</b> The two surfaces edit the same
    /// state, so they must produce indistinguishable undo entries; a second, subtly different writer is
    /// what created this divergence in the first place.
    /// </para>
    /// </summary>
    private void RecordSignatureChange(string label, Action apply, Action undo, BlueprintAsset asset)
    {
        var edit = _editServiceAccessor?.Invoke();
        if (edit is null) { apply(); return; }

        edit.RecordPropertyEdit(
            asset, label,
            apply: () => { apply(); edit.NotifyStructureChanged(asset); },
            undo:  () => { undo();  edit.NotifyStructureChanged(asset); });
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a new active Blueprint asset (e.g. when the document changes).
    /// Resets the graph picker so the next frame re-selects the canvas graph (BP-72), falling back
    /// to the first editable graph.
    /// </summary>
    /// <param name="asset">The newly active blueprint, or <c>null</c> when switching away.</param>
    /// <param name="currentCanvasGraphId">
    ///   BP-72: provider for the graph the canvas is showing — pass
    ///   <c>AiCanvasContext.CurrentGraphId</c>. When null the window keeps its pre-BP-72 behaviour
    ///   (first editable graph, combo-only navigation).
    /// </param>
    public void Retarget(BlueprintAsset? asset, Func<Guid>? currentCanvasGraphId = null)
    {
        // The provider is per-document, so it must be refreshed even when the asset instance is
        // unchanged (e.g. the same asset reopened into a new document).
        _currentCanvasGraphId = currentCanvasGraphId;

        if (_asset == asset) return;
        _asset                    = asset;
        _selectedGraphId          = Guid.Empty;
        _lastSnappedCanvasGraphId = Guid.Empty;
    }

    /// <summary>
    /// Headless seam — mirrors <c>BlueprintDetailsWindow.ResolveSession()</c>.
    /// Returns the pair of <see cref="GraphSignatureEditModel"/> instances (Inputs,
    /// Outputs) for the currently-selected Function graph, or <c>null</c> when no
    /// asset / Function graph is selected.
    /// </summary>
    /// <remarks>
    /// Tests call this directly to drive mutations without touching ImGui.
    /// </remarks>
    public (GraphSignatureEditModel Inputs, GraphSignatureEditModel Outputs)? ResolveEditModels()
    {
        var asset = _asset ?? _selectionStore.SelectedAsset;
        if (asset == null) return null;

        var graph = ResolveSelectedGraph(asset);
        if (graph == null) return null;

        return BuildEditModels(graph, asset);
    }

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        var asset = _asset ?? _selectionStore.SelectedAsset;
        if (asset == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint selected.");
            return;
        }

        var graphs = EditableGraphs(asset);

        if (graphs.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No Function or Event graphs in this blueprint.");
            return;
        }

        // ── Graph-picker combo (BP-72: seeded from the canvas graph) ──────────
        var selectedGraph = ResolveSelectedGraph(asset)!;
        _selectedGraphId = selectedGraph.Id;

        if (ImGuiNET.ImGui.BeginCombo("##graph_picker", GraphLabel(selectedGraph)))
        {
            foreach (var g in graphs)
            {
                bool isSelected = g.Id == _selectedGraphId;
                if (ImGuiNET.ImGui.Selectable(GraphLabel(g), isSelected))
                {
                    _selectedGraphId = g.Id;
                    selectedGraph    = g;
                }
                if (isSelected)
                    ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }

        ImGuiNET.ImGui.Separator();

        // ── Build edit models for selected graph ──────────────────────────────
        var (inputsModel, outputsModel) = BuildEditModels(selectedGraph, asset);

        bool isEventGraph = selectedGraph.Kind == GraphKind.Event;

        // ── Inputs section ────────────────────────────────────────────────────
        // For a custom-event body the inputs ARE the event's parameters — say so, because the
        // designer declared them in the create modal and needs to know this is the same list.
        ImGuiNET.ImGui.TextUnformatted(isEventGraph ? "Parameters" : "Inputs");
        ParameterRowsView.Draw("##inputs", selectedGraph.Inputs, inputsModel);

        ImGuiNET.ImGui.Spacing();

        // ── Outputs section ───────────────────────────────────────────────────
        // BP-72: an Event graph has no return value — the compiler emits Event_{Name} as void and
        // never reads Graph.Outputs for Kind.Event. Offering an editable Outputs list here would be
        // a fresh silent-discard of exactly the kind BP-71 just removed, so state the reason
        // instead of showing a control that does nothing.
        if (isEventGraph)
        {
            ImGuiNET.ImGui.TextDisabled("Outputs: n/a — a custom event does not return a value.");
            return;
        }

        ImGuiNET.ImGui.TextUnformatted("Outputs");
        ParameterRowsView.Draw("##outputs", selectedGraph.Outputs, outputsModel);

        // BP-73 shipped: N outputs compile. The old BP1656 gate warning here is gone; what remains
        // is a plain statement of the resulting shape, because the Return node and every call site
        // grow a pin per output and the designer should know that before adding a third.
        if (selectedGraph.Outputs.Count > 1)
        {
            ImGuiNET.ImGui.TextDisabled(
                $"{selectedGraph.Outputs.Count} outputs — returned together; the Return node and "
                + "every call site show one pin each.");
        }
    }

    /// <summary>
    /// Combo label. Event graphs are tagged so a custom-event body is not mistaken for a function —
    /// they live in one flat list and only the Kind distinguishes them.
    /// </summary>
    private static string GraphLabel(Graph g)
        => g.Kind == GraphKind.Event ? $"{g.Name}  (event)" : g.Name;

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// BP-72: the graphs whose signature is editable here — Function graphs (Inputs + Outputs) and
    /// <b>Event</b> graphs (Inputs only).
    /// <para>
    /// Event graphs were excluded before, which meant a custom event's body graph — auto-created by
    /// BP-24 — had its <c>Inputs</c> editable <em>nowhere</em>: the create modal sets the parameters
    /// once and BP-12b only covers rename, so adding one afterwards meant hand-editing JSON.
    /// </para>
    /// Construction graphs stay out: nothing in the runtime consumes them yet (Q23 scope guard).
    /// </summary>
    private static List<Graph> EditableGraphs(BlueprintAsset asset)
        => asset.Graphs
            .Where(g => g.Kind == GraphKind.Function || g.Kind == GraphKind.Event)
            .ToList();

    /// <summary>
    /// BP-72: picks the graph to edit — the canvas graph when the canvas has moved since we last
    /// snapped, otherwise the designer's explicit combo choice, otherwise the first editable graph.
    /// </summary>
    private Graph? ResolveSelectedGraph(BlueprintAsset asset)
    {
        var graphs = EditableGraphs(asset);
        if (graphs.Count == 0) return null;

        // Follow the canvas when it moves. An explicit pick then sticks until it moves again.
        var canvasId = _currentCanvasGraphId?.Invoke() ?? Guid.Empty;
        if (canvasId != Guid.Empty && canvasId != _lastSnappedCanvasGraphId
            && graphs.Any(g => g.Id == canvasId))
        {
            _lastSnappedCanvasGraphId = canvasId;
            _selectedGraphId          = canvasId;
        }

        return graphs.FirstOrDefault(g => g.Id == _selectedGraphId)
               ?? graphs[0];
    }

    /// <summary>
    /// BP-72: an Event graph's <c>Inputs</c> ARE the paired custom event's <c>Parameters</c> — the
    /// compiler emits <c>Event_{Name}</c> from the graph and <c>Stage2.V_CustomEventHandlers</c>
    /// (BP1408) requires the two counts to agree. Editing one side without the other turns a
    /// parameter edit into a compile error, so mirror graph→declaration after every mutation.
    /// <para>
    /// Parameter <b>ids are preserved by name</b> where they still match, so an edit that renames or
    /// reorders does not silently re-mint ids that something else might key on.
    /// </para>
    /// No-op for Function graphs and for Event graphs with no matching declaration (a hand-authored
    /// Event graph that is not a custom-event body).
    /// </summary>
    internal static void MirrorEventGraphInputsToDecl(BlueprintAsset asset, Graph graph)
    {
        if (asset == null || graph == null) return;
        if (graph.Kind != GraphKind.Event) return;

        var decl = asset.CustomEvents.FirstOrDefault(
            e => string.Equals(e.Name, graph.Name, StringComparison.Ordinal));
        if (decl == null) return;

        var byName = decl.Parameters.ToDictionary(p => p.Name, p => p.Id);

        decl.Parameters = graph.Inputs
            .Select(src => new ParameterDecl
            {
                Id   = byName.TryGetValue(src.Name, out var keptId) ? keptId : Guid.NewGuid(),
                Name = src.Name,
                Type = new BlueprintTypeRef { TypeId = src.Type?.TypeId ?? "" },
            })
            .ToList();
    }

    private (GraphSignatureEditModel Inputs, GraphSignatureEditModel Outputs)
        BuildEditModels(Graph graph, BlueprintAsset asset)
    {
        var assetId = asset.AssetId;

        // BP-72: every Inputs mutation on an Event graph re-syncs the paired custom-event
        // declaration before marking dirty, so the decl and the handler graph can never drift into
        // a BP1408. Function graphs pay nothing — the mirror early-returns on Kind.
        void OnInputsChanged()
        {
            MirrorEventGraphInputsToDecl(asset, graph);
            _dirtyTracker.MarkDirty(assetId);
        }

        // BP-125: route BOTH tables through the edit service, so the pins re-project and the edit is
        // undoable — the two things this window never did.
        var inputs  = new GraphSignatureEditModel(
            graph, false, OnInputsChanged,
            record: (label, apply, undo) => RecordSignatureChange(label, apply, undo, asset));
        var outputs = new GraphSignatureEditModel(
            graph, true, () => _dirtyTracker.MarkDirty(assetId),
            record: (label, apply, undo) => RecordSignatureChange(label, apply, undo, asset));
        return (inputs, outputs);
    }

}
