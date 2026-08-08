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
/// A registered drawer fully replaces <c>DrawReadOnlySummary</c>, so this drawer still renders
/// <see cref="ReturnNode.Status"/> itself (as an editable combo) — otherwise the fix would remove
/// the one thing the panel showed before.
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

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: the graph this session resolved as the node's container, or null.</summary>
    internal Graph? ResolvedGraphForTest => _graph;

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

        // ── Outputs ──────────────────────────────────────────────────────────
        ImGui.TextUnformatted(
            "Outputs — one data-in pin on this Return node, and one data-out pin on every call site.");
        ImGui.Separator();

        if (_graph.Outputs.Count == 0)
        {
            // BP-89: the reported defect was exactly this state read as "broken" — make it obvious
            // it is correct, not broken, and put the "+" control right here so adding the first
            // output does not require finding the separate Graph Signature window.
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

        ImGui.Separator();

        // ── Status ───────────────────────────────────────────────────────────
        DrawStatusCombo();
    }

    private void DrawStatusCombo()
    {
        ImGui.TextUnformatted("Status");
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
