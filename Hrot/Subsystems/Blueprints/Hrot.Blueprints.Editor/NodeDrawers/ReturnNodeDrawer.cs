using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Blueprints.Editor.Windows;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-89: node-drawer for <see cref="ReturnNode"/>.
///
/// <para>
/// Before this drawer, the Return node's Details panel fell back to
/// <c>BlueprintDetailsWindow.DrawReadOnlySummary</c> — a reflection-based read-only dump of
/// <see cref="ReturnNode.Status"/> — with no way to declare a function's outputs anywhere near
/// the node that returns them. The affordance existed only in the standalone
/// <c>GraphSignatureWindow</c> (Outputs table), which the reported designer never found ("Return
/// node detail panel always shows Success and nothing else"). Unreal puts Inputs/Outputs with
/// <c>+</c> buttons on the result node's Details panel — this mirrors that, reusing
/// <see cref="ParameterRowsView"/> so the two surfaces render byte-identically.
/// </para>
///
/// <para>
/// <b>BP-105.</b> The first version of this drawer rendered BOTH the Outputs table and the Status
/// combo unconditionally, on every dispatch — but <c>Stage5_Schedule.BuildReturnTerminator</c>
/// (BP-104) reads exactly one of them, so the other control was inert: editing it changed nothing
/// the compiler would ever look at. The panel now shows only the control(s) the current
/// <see cref="BlueprintAsset.Dispatch"/> actually uses, per BP-104's rule:
/// <list type="bullet">
/// <item><b>Instance</b> — declared Outputs only (Status is never read for Instance).</item>
/// <item><b>Library</b> — Outputs always (it is how you declare them), <b>and</b> Status only while
/// zero outputs are declared (the compiler falls back to <c>NodeStatus</c> for a zero-output Library
/// function; once an output is declared, Status stops being read).</item>
/// <item><b>AiPrimitive</b> — Status only (an AiPrimitive returns a node status to its BTree/HSM
/// host unconditionally; it never has declared outputs to fall back on).</item>
/// </list>
/// Each rendered section is labeled with why it applies, so the panel does not look like an
/// arbitrary subset — see the internal test hooks
/// <see cref="ReturnNodeSession.ShowsOutputsForTest"/> / <see cref="ReturnNodeSession.ShowsStatusForTest"/>.
/// </para>
/// </summary>
public sealed class ReturnNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public ReturnNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is ReturnNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ReturnNodeSession((ReturnNode)node, parentAsset, _editService);
}

internal sealed class ReturnNodeSession : INodeEditSession
{
    private readonly ReturnNode      _node;
    private readonly BlueprintAsset  _parent;
    private readonly IEditService?   _editService;

    // Resolved once at session construction: the graph in _parent.Graphs whose Nodes contains
    // this node instance. CreateSession only receives the whole asset (not the containing graph),
    // so the drawer has to find it itself — null when the node is not (or no longer) parented by
    // any graph in this asset, which is rendered as a short disabled explanation rather than thrown.
    private readonly Graph? _graph;

    public bool IsDirty { get; private set; }

    public ReturnNodeSession(ReturnNode node, BlueprintAsset parentAsset, IEditService? editService)
    {
        _node        = node        ?? throw new ArgumentNullException(nameof(node));
        _parent      = parentAsset ?? throw new ArgumentNullException(nameof(parentAsset));
        _editService = editService;
        _graph       = ResolveContainingGraph(node, parentAsset);
    }

    private static Graph? ResolveContainingGraph(Node node, BlueprintAsset asset)
        => asset.Graphs.FirstOrDefault(g => g.Nodes.Contains(node));

    // ── BP-105: which control(s) apply, mirroring BP-104's terminator rule ──────
    //
    // Instance   -> Outputs only (BuildReturnTerminator never reads Status for Instance).
    // Library    -> Outputs ALWAYS (it is how you declare them), Status only while the graph
    //               declares ZERO outputs (BuildReturnTerminator/SealFallThrough fall back to
    //               NodeStatus only in that case -- BP-104).
    // AiPrimitive -> Status only (NodeStatus is unconditional -- its BTree/HSM hosting contract).
    //
    // Both are false when the containing graph could not be resolved: Draw() renders the warning
    // and nothing else in that case, so neither control is actually on screen.

    // ── BP-80: Macro overrides BOTH, and does so on GRAPH KIND rather than dispatch ─────
    //
    // A macro is a source-level template spliced into its call sites; it never becomes a method, so
    // the asset's dispatch says nothing about its Return node.
    //   Outputs -> ALWAYS shown. A macro declares data outputs exactly like a Function graph does
    //              (F3 reuses ReturnNode as the output boundary), including inside an AiPrimitive
    //              asset -- where the dispatch rule below would otherwise hide the table and print
    //              the "this AiPrimitive returns a node Status" line, which is false for a macro.
    //   Status  -> NEVER shown. There is no NodeStatus to report: nothing returns from a macro, the
    //              body is spliced into the host's exec chain. Same shape as the BP-105 precedent.
    private bool IsMacroGraph => _graph?.Kind == GraphKind.Macro;

    private bool ShowOutputs =>
        _graph != null &&
        (IsMacroGraph
         || _parent.Dispatch == BlueprintDispatchKind.Instance
         || _parent.Dispatch == BlueprintDispatchKind.Library);

    private bool ShowStatus =>
        _graph != null && !IsMacroGraph &&
        (_parent.Dispatch == BlueprintDispatchKind.AiPrimitive
         || (_parent.Dispatch == BlueprintDispatchKind.Library && _graph.Outputs.Count == 0));

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: the graph this session resolved as the node's container, or null.</summary>
    internal Graph? ResolvedGraphForTest => _graph;

    /// <summary>Test hook: whether <see cref="Draw"/> renders the Outputs table for this session's dispatch.</summary>
    internal bool ShowsOutputsForTest => ShowOutputs;

    /// <summary>Test hook: whether <see cref="Draw"/> renders the Status combo for this session's dispatch.</summary>
    internal bool ShowsStatusForTest => ShowStatus;

    /// <summary>
    /// Test hook: the same <see cref="GraphSignatureEditModel"/> over <c>Outputs</c> that
    /// <see cref="Draw"/> builds and hands to <see cref="ParameterRowsView"/> — null when the
    /// containing graph could not be resolved.
    /// </summary>
    internal GraphSignatureEditModel? OutputsModelForTest => _graph == null ? null : BuildOutputsModel();

    /// <summary>Test hook: simulates picking a new Status from the combo.</summary>
    internal void SetStatusForTest(NodeStatus status) => ApplyStatus(status);

    // ── Private mutation helpers ─────────────────────────────────────────────

    /// <summary>
    /// BP-89: adding/removing/renaming/retyping an output changes pin projection on this Return
    /// node AND on every <c>FunctionCallNode</c> call site, so every Outputs mutation must notify
    /// structure-changed in addition to being undo-recorded — on undo too, so the projection is
    /// restored along with the data.
    /// </summary>
    private GraphSignatureEditModel BuildOutputsModel()
        => new(
            _graph!,
            isOutputs: true,
            onChanged: () => IsDirty = true,
            record: _editService == null ? null : RecordOutputsChange);

    private void RecordOutputsChange(string label, Action apply, Action undo)
    {
        _editService!.RecordPropertyEdit(
            _parent, label,
            apply: () => { apply(); _editService.NotifyStructureChanged(_parent); },
            undo:  () => { undo();  _editService.NotifyStructureChanged(_parent); });
    }

    private void ApplyStatus(NodeStatus status)
    {
        if (_editService is null)
        {
            _node.Status = status;
            IsDirty      = true;
            return;
        }

        var before = _node.Status;
        _editService.RecordPropertyEdit(
            _parent, $"Set Return Status '{status}'",
            apply: () => { _node.Status = status; IsDirty = true; },
            undo:  () => { _node.Status = before; IsDirty = true; });
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        if (_graph == null)
        {
            ImGui.TextColored(EditorColors.Warning, "(containing graph not found — cannot edit outputs)");
            return;
        }

        bool showOutputs = ShowOutputs;
        bool showStatus  = ShowStatus;

        // ── Outputs ──────────────────────────────────────────────────────────
        // BP-105: rendered only for the dispatch kinds whose compiled terminator actually reads
        // Outputs (BP-104) — Instance always, Library always (it is how a Library function
        // declares a value return at all). AiPrimitive never declares outputs, so the table
        // is replaced by a one-line explanation instead of an empty/unusable control.
        if (showOutputs)
        {
            ImGui.TextUnformatted(_parent.Dispatch == BlueprintDispatchKind.Library
                ? "Outputs — how this Library function returns a value; declaring one switches "
                  + "it from a NodeStatus return to a value return."
                : "Outputs — one data-in pin on this Return node, and one data-out pin on every call site.");
            ImGui.Separator();

            if (_graph.Outputs.Count == 0)
            {
                // BP-89: the reported defect was exactly this state read as "broken" — make it
                // obvious it is correct, not broken, and put the "+" control right here so adding
                // the first output does not require finding the separate Graph Signature window.
                ImGui.TextUnformatted("This function declares no outputs. Add one to return a value.");
            }

            var outputsModel = BuildOutputsModel();
            ParameterRowsView.Draw("##return_outputs", _graph.Outputs, outputsModel);

            if (_graph.Outputs.Count > 1)
            {
                ImGui.TextDisabled(
                    $"{_graph.Outputs.Count} outputs — returned together; the Return node and "
                    + "every call site show one pin each.");
            }
        }
        else
        {
            ImGui.TextDisabled(
                "This AiPrimitive returns a node Status to its BTree/HSM host, so it has no "
                + "declared Outputs.");
        }

        if (showOutputs && showStatus)
            ImGui.Separator();

        // ── Status ───────────────────────────────────────────────────────────
        // BP-105: rendered only for AiPrimitive (unconditional) or a Library function that
        // currently declares zero outputs (BP-104's fallback case). An Instance function, or a
        // Library function that HAS declared outputs, never has this terminator read — showing
        // the combo there would be exactly the inert control BP-105 reports.
        if (showStatus)
        {
            ImGui.TextUnformatted(_parent.Dispatch == BlueprintDispatchKind.Library
                ? "Status — this Library function declares no outputs, so it returns a node "
                  + "status instead. Declaring an output above hides this control."
                : "Status — the node status returned to this AiPrimitive's BTree/HSM host.");
            DrawStatusCombo();

            // BP-131: for AiPrimitive the combo is now the FALLBACK, not the only writer. Say so
            // here, on the control itself — the original complaint was that a fixed set of combo
            // values is meaningless for a value that depends on execution, and a designer who
            // cannot see that the pin overrides the combo is left with the same confusion one
            // level down (two writers for one value is the BP-125 shape).
            if (_parent.Dispatch == BlueprintDispatchKind.AiPrimitive)
            {
                ImGui.TextDisabled(
                    "Wire the 'Success' pin to decide the status at runtime — true → Success, "
                    + "false → Failure. This combo applies only while that pin is unwired.");
            }
        }
        else if (_parent.Dispatch == BlueprintDispatchKind.Instance)
        {
            ImGui.TextDisabled(
                "This Instance function returns its declared Outputs above; Status is not read "
                + "by the compiler for Instance dispatch.");
        }
        else if (_parent.Dispatch == BlueprintDispatchKind.Library)
        {
            ImGui.TextDisabled(
                "This Library function declares an output above, so it returns that value; "
                + "Status is not read once any output is declared.");
        }
    }

    private void DrawStatusCombo()
    {
        var names      = Enum.GetNames(typeof(NodeStatus));
        int currentIdx = (int)_node.Status;
        if (ImGui.Combo("##return_status", ref currentIdx, names, names.Length))
        {
            var chosen = (NodeStatus)currentIdx;
            if (chosen != _node.Status)
                ApplyStatus(chosen);
        }
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
