using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="FunctionCallNode"/>.
/// Lets the designer configure either an in-blueprint Function graph call
/// (sets <see cref="FunctionCallNode.TargetGraphId"/>, clears CLR fields)
/// or a CLR library method call (sets <see cref="FunctionCallNode.TargetTypeId"/> /
/// <see cref="FunctionCallNode.MethodName"/> / <see cref="FunctionCallNode.IsPure"/>,
/// clears TargetGraphId). The two modes are mutually exclusive.
/// </summary>
public sealed class FunctionCallNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public FunctionCallNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is FunctionCallNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new FunctionCallNodeSession((FunctionCallNode)node, parentAsset, _editService);
}

internal sealed class FunctionCallNodeSession : INodeEditSession
{
    private readonly FunctionCallNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService _editService;

    // View-state: which mode tab is shown. Initialized from the node, but persisted across
    // frames so that selecting "In-Blueprint Function" does not flicker back to "CLR Method"
    // before the user has picked a graph (TargetGraphId is empty until then). 0 = CLR, 1 = graph.
    private int _modeIdx;

    public bool IsDirty { get; private set; }

    public FunctionCallNodeSession(
        FunctionCallNode node,
        BlueprintAsset parentAsset,
        IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
        _modeIdx     = !string.IsNullOrEmpty(node.TargetGraphId) ? 1 : 0;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>
    /// Test hook: simulates the designer selecting an in-blueprint Function graph.
    /// Sets TargetGraphId, clears CLR fields, and marks session dirty.
    /// </summary>
    internal void SelectFunctionGraphForTest(Guid graphId)
    {
        ApplyFunctionGraphSelection(graphId);
    }

    /// <summary>
    /// Test hook: simulates the designer entering CLR target information.
    /// Sets TargetTypeId / MethodName / IsPure, clears TargetGraphId, and marks session dirty.
    /// </summary>
    internal void SetClrTargetForTest(string typeId, string methodName, bool isPure)
    {
        ApplyClrTarget(typeId, methodName, isPure);
    }

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyFunctionGraphSelection(Guid graphId)
    {
        _node.TargetGraphId = graphId.ToString();
        _node.TargetTypeId  = "";
        _node.MethodName    = "";
        MarkChanged();
    }

    private void ApplyClrTarget(string typeId, string methodName, bool isPure)
    {
        _node.TargetTypeId  = typeId;
        _node.MethodName    = methodName;
        _node.IsPure        = isPure;
        _node.TargetGraphId = "";
        MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService?.MarkDirty(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Function Call");
        ImGui.Separator();

        // IsPure checkbox — applicable to both modes
        bool isPure = _node.IsPure;
        if (ImGui.Checkbox("Pure (no exec pins)", ref isPure))
        {
            _node.IsPure = isPure;
            MarkChanged();
        }

        ImGui.Separator();

        // Mode persists across frames (see _modeIdx). Keep it in sync if the node was changed
        // externally to an in-blueprint target.
        if (!string.IsNullOrEmpty(_node.TargetGraphId)) _modeIdx = 1;

        string[] modeLabels = { "CLR Method", "In-Blueprint Function" };
        if (ImGui.Combo("Mode", ref _modeIdx, modeLabels, modeLabels.Length))
        {
            if (_modeIdx == 0 && !string.IsNullOrEmpty(_node.TargetGraphId))
            {
                // Switched to CLR mode — clear TargetGraphId
                _node.TargetGraphId = "";
                MarkChanged();
            }
            else if (_modeIdx == 1 && (!string.IsNullOrEmpty(_node.TargetTypeId) || !string.IsNullOrEmpty(_node.MethodName)))
            {
                // Switched to in-blueprint mode — clear CLR fields; TargetGraphId set when the user picks.
                _node.TargetTypeId = "";
                _node.MethodName   = "";
                MarkChanged();
            }
        }

        ImGui.Separator();

        if (_modeIdx == 1)
        {
            DrawFunctionGraphPicker();
        }
        else
        {
            DrawClrMethodForm();
        }
    }

    private void DrawFunctionGraphPicker()
    {
        var functionGraphs = _parent.Graphs
            .Where(g => g.Kind == GraphKind.Function)
            .ToList();

        if (functionGraphs.Count == 0)
        {
            ImGui.TextColored(EditorColors.Warning, "(no function graphs in this blueprint)");
            return;
        }

        var names = functionGraphs.Select(g => g.Name).ToArray();

        // Find current selection index
        int currentIdx = -1;
        for (int i = 0; i < functionGraphs.Count; i++)
        {
            if (functionGraphs[i].Id.ToString() == _node.TargetGraphId)
            {
                currentIdx = i;
                break;
            }
        }

        if (ImGui.Combo("Function Graph", ref currentIdx, names, names.Length))
        {
            if (currentIdx >= 0)
            {
                var chosen = functionGraphs[currentIdx];
                if (chosen.Id.ToString() != _node.TargetGraphId)
                {
                    ApplyFunctionGraphSelection(chosen.Id);
                }
            }
        }

        if (string.IsNullOrEmpty(_node.TargetGraphId))
            ImGui.TextColored(EditorColors.Warning, "(no function graph selected)");
    }

    private void DrawClrMethodForm()
    {
        // TargetTypeId text field
        var typeId = _node.TargetTypeId ?? "";
        if (ImGui.InputText("Type ID", ref typeId, 256))
        {
            if (typeId != _node.TargetTypeId)
            {
                _node.TargetTypeId  = typeId;
                _node.TargetGraphId = "";
                MarkChanged();
            }
        }

        // MethodName text field
        var methodName = _node.MethodName ?? "";
        if (ImGui.InputText("Method Name", ref methodName, 256))
        {
            if (methodName != _node.MethodName)
            {
                _node.MethodName    = methodName;
                _node.TargetGraphId = "";
                MarkChanged();
            }
        }

        ImGui.TextDisabled("(CLR method browser deferred — enter type/method names directly)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
