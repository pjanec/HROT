using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Node-drawer for <see cref="GetSharedNode"/> (Slice 2a-3).
/// <para>
/// Lets the designer author <see cref="GetSharedNode.VariableId"/> (the entity-scoped shared
/// slot name) and <see cref="GetSharedNode.SharedTypeId"/> (the Category-1 shared struct FQN)
/// on an already-placed node, via the same Details-panel <see cref="IBlueprintNodeDrawer"/> /
/// <see cref="INodeEditSession"/> mechanism used by <see cref="FunctionCallNodeDrawer"/> and
/// <see cref="LiteralNodeDrawer"/> — see <see cref="BlueprintEditorBootstrap.CreateNodeDrawerRegistry"/>.
/// </para>
/// <para>
/// Both fields are free-text (mirrors <see cref="FunctionCallNodeDrawer"/>'s "Type ID"/"Method
/// Name" text fields): a picker over the host's Entity-scoped Role=State blackboard variable
/// names, and over registered shared-struct types, is a natural follow-up but is cross-subsystem
/// (BTree/HSM blackboard authoring, not currently reachable from the Blueprint editor project)
/// and disproportionate for this slice.
/// </para>
/// </summary>
public sealed class GetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public GetSharedNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is GetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new GetSharedNodeSession((GetSharedNode)node, parentAsset, _editService);
}

/// <summary>
/// Node-drawer for <see cref="SetSharedNode"/> (Slice 2a-3). See <see cref="GetSharedNodeDrawer"/>
/// for the rationale (same two free-text fields: VariableId + SharedTypeId).
/// </summary>
public sealed class SetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public SetSharedNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is SetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SetSharedNodeSession((SetSharedNode)node, parentAsset, _editService);
}

/// <summary>
/// Edit session for <see cref="GetSharedNode"/>.
/// <para>
/// Mutation logic lives in <see cref="SetVariableIdForTest"/>/<see cref="SetSharedTypeIdForTest"/>
/// (internal test hooks, mirroring <c>FunctionCallNodeSession.SelectFunctionGraphForTest</c>) so
/// it is exercised headlessly; <see cref="Draw"/> calls the exact same helpers and is the only
/// ImGui-coupled surface (Windows-verifiable only).
/// </para>
/// </summary>
internal sealed class GetSharedNodeSession : INodeEditSession
{
    private readonly GetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;

    public bool IsDirty { get; private set; }

    public GetSharedNodeSession(GetSharedNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer typing into the "Slot Name" field.</summary>
    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    /// <summary>Test hook: simulates the designer typing into the "Shared Type FQN" field.</summary>
    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyVariableId(string variableId)
    {
        if (variableId == _node.VariableId) return;
        _node.VariableId = variableId;
        MarkChanged();
    }

    private void ApplySharedTypeId(string sharedTypeId)
    {
        if (sharedTypeId == _node.SharedTypeId) return;
        _node.SharedTypeId = sharedTypeId;
        MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService.MarkDirty(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Get Shared");
        ImGui.Separator();

        var variableId = _node.VariableId ?? "";
        if (ImGui.InputText("Slot Name", ref variableId, 256))
            ApplyVariableId(variableId);

        var sharedTypeId = _node.SharedTypeId ?? "";
        if (ImGui.InputText("Shared Type FQN", ref sharedTypeId, 512))
            ApplySharedTypeId(sharedTypeId);

        ImGui.TextDisabled("(entity-scoped, self only — reads BlueprintSharedState.TryGetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

/// <summary>
/// Edit session for <see cref="SetSharedNode"/>. See <see cref="GetSharedNodeSession"/> for the
/// test-hook rationale.
/// </summary>
internal sealed class SetSharedNodeSession : INodeEditSession
{
    private readonly SetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;

    public bool IsDirty { get; private set; }

    public SetSharedNodeSession(SetSharedNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    // ── Private mutation helpers (called by both Draw() and test hooks) ──────────

    private void ApplyVariableId(string variableId)
    {
        if (variableId == _node.VariableId) return;
        _node.VariableId = variableId;
        MarkChanged();
    }

    private void ApplySharedTypeId(string sharedTypeId)
    {
        if (sharedTypeId == _node.SharedTypeId) return;
        _node.SharedTypeId = sharedTypeId;
        MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        _editService.MarkDirty(_parent);
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Set Shared");
        ImGui.Separator();

        var variableId = _node.VariableId ?? "";
        if (ImGui.InputText("Slot Name", ref variableId, 256))
            ApplyVariableId(variableId);

        var sharedTypeId = _node.SharedTypeId ?? "";
        if (ImGui.InputText("Shared Type FQN", ref sharedTypeId, 512))
            ApplySharedTypeId(sharedTypeId);

        ImGui.TextDisabled("(entity-scoped, self only — writes BlueprintSharedState.TrySetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
