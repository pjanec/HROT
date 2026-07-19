using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

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

    // Punch-list #6: cached source-location resolution for the "open in VS" button. Recomputed
    // only when (TargetTypeId, MethodName) changes so reflection + PDB reads don't run every frame.
    private string? _srcKey;
    private ClrSourceLocator.SourceLocation? _srcLoc;
    private bool _srcResolvedMethod;

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
        // Read-only (Q#12): the CLR method is chosen from the curated picker when the node is ADDED and
        // is immutable afterward — designers never type an FQN. Fields are ReadOnly (still selectable /
        // copyable). To call a different method, add a new node.
        var typeId = _node.TargetTypeId ?? "";
        ImGui.InputText("Type ID", ref typeId, 256, ImGuiInputTextFlags.ReadOnly);

        var methodName = _node.MethodName ?? "";
        ImGui.InputText("Method Name", ref methodName, 256, ImGuiInputTextFlags.ReadOnly);

        ImGui.TextDisabled("Read-only — pick from the Add-Node picker; add a new node to change it.");

        DrawOpenSourceButton();
    }

    /// <summary>
    /// Punch-list #6: a "⋯" button that opens the targeted CLR method's source in Visual Studio.
    /// The method + its source file/line are resolved (and cached) from reflection + the portable
    /// PDB; the button is disabled and the reason shown when the method or its source cannot be
    /// located (e.g. a dynamic/hot-reloaded assembly, or a Release build with no PDB).
    /// </summary>
    private void DrawOpenSourceButton()
    {
        var key = (_node.TargetTypeId ?? "") + "|" + (_node.MethodName ?? "");
        if (key != _srcKey)
        {
            _srcKey            = key;
            _srcLoc            = null;
            _srcResolvedMethod = false;
            var method = NodePinSchema.ResolveClrMethod(_node);
            if (method != null)
            {
                _srcResolvedMethod = true;
                _srcLoc            = ClrSourceLocator.Resolve(method);
            }
        }

        ImGui.Separator();

        bool canOpen = _srcLoc.HasValue;
        ImGui.BeginDisabled(!canOpen);
        if (ImGui.Button("...")) // the punch-list "⋯" affordance (ASCII for font safety)
        {
            if (_srcLoc.HasValue) SourceFileOpener.Open(_srcLoc.Value.File, _srcLoc.Value.Line);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open the method's source in Visual Studio");

        ImGui.SameLine();
        if (_srcLoc.HasValue)
            ImGui.TextDisabled($"{ShortFile(_srcLoc.Value.File)}:{_srcLoc.Value.Line}");
        else if (_srcResolvedMethod)
            ImGui.TextDisabled("(no source — missing PDB / dynamic assembly)");
        else
            ImGui.TextDisabled("(method not resolved)");
    }

    /// <summary>Last two path segments of a source file, for a compact inspector label.</summary>
    private static string ShortFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var norm  = path.Replace('\\', '/');
        var parts = norm.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? norm : parts[^2] + "/" + parts[^1];
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
