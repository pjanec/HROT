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
/// <see cref="GetSharedNode.VariableId"/> stays free-text (a designer-chosen, entity-scoped
/// slot name -- there is no fixed set to pick from). <see cref="GetSharedNode.SharedTypeId"/>
/// is instead authored via a filtered picker (incremental filter box + selectable list) over
/// the FQNs an <see cref="ISharedStructTypeProvider"/> discovers -- by default,
/// <see cref="ReflectionSharedStructTypeProvider"/>, which scans loaded assemblies for
/// <c>[BlackboardDtoStruct]</c> value types, the same predicate
/// <see cref="Hrot.Editor.AiShared.Blackboard.BlackboardFieldClassifier"/> uses. If the node's
/// current <c>SharedTypeId</c> isn't in the discovered set (assembly not loaded, typo, renamed
/// type) the picker still shows and preserves it rather than silently blanking it.
/// </para>
/// </summary>
public sealed class GetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    public GetSharedNodeDrawer(IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is GetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new GetSharedNodeSession((GetSharedNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Node-drawer for <see cref="SetSharedNode"/> (Slice 2a-3). See <see cref="GetSharedNodeDrawer"/>
/// for the rationale (free-text VariableId + picker-driven SharedTypeId).
/// </summary>
public sealed class SetSharedNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    public SetSharedNodeDrawer(IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _editService  = editService  ?? throw new ArgumentNullException(nameof(editService));
        _typeProvider = typeProvider ?? throw new ArgumentNullException(nameof(typeProvider));
    }

    public bool Handles(Node node) => node is SetSharedNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new SetSharedNodeSession((SetSharedNode)node, parentAsset, _editService, _typeProvider);
}

/// <summary>
/// Non-ImGui logic shared by <see cref="GetSharedNodeSession"/> and
/// <see cref="SetSharedNodeSession"/> for the "Shared Type FQN" picker, kept separate from
/// <c>Draw()</c> so it is headlessly testable (mirrors the rest of this file's
/// mutation-helpers-vs-Draw split).
/// </summary>
internal static class SharedTypePickerLogic
{
    /// <summary>
    /// Returns the subset of <paramref name="candidates"/> whose text contains
    /// <paramref name="filterText"/> (case-insensitive, substring match). Returns all
    /// candidates unchanged when <paramref name="filterText"/> is null/empty.
    /// </summary>
    internal static IReadOnlyList<string> Filter(IReadOnlyList<string> candidates, string? filterText)
    {
        if (string.IsNullOrEmpty(filterText)) return candidates;
        return candidates
            .Where(c => c.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="value"/> is a non-empty string present (ordinal match) in
    /// <paramref name="candidates"/>.
    /// </summary>
    internal static bool Contains(IReadOnlyList<string> candidates, string? value)
        => !string.IsNullOrEmpty(value) && candidates.Contains(value, StringComparer.Ordinal);
}

/// <summary>
/// Edit session for <see cref="GetSharedNode"/>.
/// <para>
/// Mutation logic lives in <see cref="SetVariableIdForTest"/>/<see cref="SetSharedTypeIdForTest"/>
/// (internal test hooks, mirroring <c>FunctionCallNodeSession.SelectFunctionGraphForTest</c>) so
/// it is exercised headlessly; <see cref="Draw"/> calls the exact same helpers and is the only
/// ImGui-coupled surface (Windows-verifiable only). The "Shared Type FQN" filter/selection logic
/// is likewise headlessly testable via <see cref="GetAvailableSharedTypesForTest"/>,
/// <see cref="GetFilteredSharedTypesForTest"/>, and <see cref="IsCurrentSharedTypeIdUnlistedForTest"/>.
/// </para>
/// </summary>
internal sealed class GetSharedNodeSession : INodeEditSession
{
    private readonly GetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    // ImGui view-state only (the incremental filter box's current text). Not part of the
    // node's data and not exercised by Draw()-adjacent test hooks below.
    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public GetSharedNodeSession(
        GetSharedNode node, BlueprintAsset parentAsset, IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer typing into the "Slot Name" field.</summary>
    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    /// <summary>
    /// Test hook: simulates the designer selecting (or otherwise setting) the "Shared Type FQN"
    /// value -- used both for picker selections and for the pre-existing free-text mutation path.
    /// </summary>
    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    /// <summary>Test hook: the full, unfiltered set of FQNs the type provider discovered.</summary>
    internal IReadOnlyList<string> GetAvailableSharedTypesForTest() => _typeProvider.GetSharedStructTypeFqns();

    /// <summary>Test hook: the discovered FQNs matching <paramref name="filterText"/> (case-insensitive substring).</summary>
    internal IReadOnlyList<string> GetFilteredSharedTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetSharedStructTypeFqns(), filterText);

    /// <summary>
    /// Test hook: true when the node's current <see cref="GetSharedNode.SharedTypeId"/> is
    /// non-empty but NOT present in the provider's discovered set (unloaded assembly, typo,
    /// renamed type). The picker must display and preserve such a value rather than blank it.
    /// </summary>
    internal bool IsCurrentSharedTypeIdUnlistedForTest()
        => !string.IsNullOrEmpty(_node.SharedTypeId)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetSharedStructTypeFqns(), _node.SharedTypeId);

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

        DrawSharedTypePicker();

        ImGui.TextDisabled("(entity-scoped, self only — reads BlueprintSharedState.TryGetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    /// <summary>
    /// Filtered combo picker for <see cref="GetSharedNode.SharedTypeId"/>: an incremental
    /// filter box above a selectable list of discovered <c>[BlackboardDtoStruct]</c> FQNs
    /// (mirrors <c>FilteredTypeComboFieldDrawer</c>/<c>BehaviorHashPickerDrawer</c>'s
    /// BeginCombo+Selectable pattern). If the current value isn't in the discovered set it is
    /// still surfaced as a selectable entry so re-opening/closing this combo without picking
    /// anything new never clears <see cref="GetSharedNode.SharedTypeId"/> (design constraint:
    /// never silently lose an unlisted value).
    /// </summary>
    private void DrawSharedTypePicker()
    {
        var current  = _node.SharedTypeId ?? "";
        var unlisted = IsCurrentSharedTypeIdUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Shared Type FQN", comboLabel))
        {
            ImGui.InputTextWithHint("##GetSharedTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredSharedTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplySharedTypeId(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}

/// <summary>
/// Edit session for <see cref="SetSharedNode"/>. See <see cref="GetSharedNodeSession"/> for the
/// test-hook and picker rationale.
/// </summary>
internal sealed class SetSharedNodeSession : INodeEditSession
{
    private readonly SetSharedNode  _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService   _editService;
    private readonly ISharedStructTypeProvider _typeProvider;

    private string _typeFilterText = "";

    public bool IsDirty { get; private set; }

    public SetSharedNodeSession(
        SetSharedNode node, BlueprintAsset parentAsset, IEditService editService, ISharedStructTypeProvider typeProvider)
    {
        _node         = node;
        _parent       = parentAsset;
        _editService  = editService;
        _typeProvider = typeProvider;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    internal void SetVariableIdForTest(string variableId) => ApplyVariableId(variableId);

    internal void SetSharedTypeIdForTest(string sharedTypeId) => ApplySharedTypeId(sharedTypeId);

    internal IReadOnlyList<string> GetAvailableSharedTypesForTest() => _typeProvider.GetSharedStructTypeFqns();

    internal IReadOnlyList<string> GetFilteredSharedTypesForTest(string filterText)
        => SharedTypePickerLogic.Filter(_typeProvider.GetSharedStructTypeFqns(), filterText);

    internal bool IsCurrentSharedTypeIdUnlistedForTest()
        => !string.IsNullOrEmpty(_node.SharedTypeId)
           && !SharedTypePickerLogic.Contains(_typeProvider.GetSharedStructTypeFqns(), _node.SharedTypeId);

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

        DrawSharedTypePicker();

        ImGui.TextDisabled("(entity-scoped, self only — writes BlueprintSharedState.TrySetShared<T>)");
        if (string.IsNullOrEmpty(_node.VariableId) || string.IsNullOrEmpty(_node.SharedTypeId))
            ImGui.TextColored(EditorColors.Warning, "(both Slot Name and Shared Type FQN are required)");
    }

    /// <summary>See <see cref="GetSharedNodeSession.DrawSharedTypePicker"/> for the rationale.</summary>
    private void DrawSharedTypePicker()
    {
        var current  = _node.SharedTypeId ?? "";
        var unlisted = IsCurrentSharedTypeIdUnlistedForTest();
        var comboLabel = current.Length > 0 ? current : "(none)";

        if (ImGui.BeginCombo("Shared Type FQN", comboLabel))
        {
            ImGui.InputTextWithHint("##SetSharedTypeFilter", "Filter...", ref _typeFilterText, 256);

            if (unlisted)
            {
                ImGui.Selectable($"{current} (current — not discovered)", true);
                ImGui.Separator();
            }

            foreach (var fqn in GetFilteredSharedTypesForTest(_typeFilterText))
            {
                bool selected = fqn == current;
                if (ImGui.Selectable(fqn, selected))
                    ApplySharedTypeId(fqn);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (unlisted)
            ImGui.TextColored(EditorColors.Warning,
                $"(current value not in the discovered type list — kept as-is: {current})");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
