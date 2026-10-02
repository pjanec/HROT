using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ S7a (<c>DESIGN_Unified_Behaviour_Run</c> "S7 design" D8) — the Details editor of a Behaviour Task: which behaviour it
/// runs. The picker lists the host's REGISTERED behaviour names (every tier: BTree, HSM, blueprint, hand-written) — the
/// names the runtime resolves the child by (<c>HostedChildren</c>). ⚠ A name not in the list is kept and flagged, never
/// dropped (a child may register later).
/// </summary>
public sealed class BehaviorTaskNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _behaviourNames;

    /// <param name="behaviourNames">The host's registered behaviour names. ⛔ A production host HAS a
    /// <c>BehaviorRegistry</c> and must pass it (the silent-default rule); null lists nothing.</param>
    public BehaviorTaskNodeDrawer(IEditService editService, Func<IReadOnlyList<string>>? behaviourNames = null)
    {
        _editService    = editService ?? throw new ArgumentNullException(nameof(editService));
        _behaviourNames = behaviourNames ?? (() => Array.Empty<string>());
    }

    public bool Handles(Node node) => node is RunBehaviorNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new BehaviorTaskNodeSession((RunBehaviorNode)node, parentAsset, _editService, _behaviourNames);
}

internal sealed class BehaviorTaskNodeSession : INodeEditSession
{
    private readonly RunBehaviorNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<string>> _names;
    private string _filter = "";

    public bool IsDirty { get; private set; }

    public BehaviorTaskNodeSession(RunBehaviorNode node, BlueprintAsset parent, IEditService editService,
        Func<IReadOnlyList<string>> names)
    {
        _node = node; _parent = parent; _editService = editService; _names = names;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────
    internal void SetBehaviourForTest(string name) => ApplyBehaviour(name);
    internal IReadOnlyList<string> GetFilteredBehavioursForTest(string filter) => Filtered(filter);
    internal bool IsCurrentUnlistedForTest() => IsUnlisted();

    private IReadOnlyList<string> Filtered(string filter)
        => SharedTypePickerLogic.Filter(
            _names().Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList(),
            filter);

    private bool IsUnlisted()
        => !string.IsNullOrWhiteSpace(_node.BehaviorName) && !_names().Contains(_node.BehaviorName, StringComparer.Ordinal);

    private void ApplyBehaviour(string name)
    {
        name = name.Trim();
        if (name == _node.BehaviorName) return;
        var before = _node.BehaviorName;
        _editService.RecordPropertyEdit(_parent, $"Set Behaviour Task '{name}'",
            apply: () => { _node.BehaviorName = name;   IsDirty = true; },
            undo:  () => { _node.BehaviorName = before; IsDirty = true; });
    }

    public void Draw()
    {
        ImGui.Text("Behaviour Task");
        ImGui.Separator();

        var current = string.IsNullOrWhiteSpace(_node.BehaviorName) ? "(none)" : _node.BehaviorName;
        if (ImGui.BeginCombo("Behaviour", current))
        {
            ImGui.InputTextWithHint("##BehaviourTaskFilter", "Filter...", ref _filter, 256);
            foreach (var name in Filtered(_filter))
            {
                bool selected = name == _node.BehaviorName;
                if (ImGui.Selectable(name, selected)) ApplyBehaviour(name);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (string.IsNullOrWhiteSpace(_node.BehaviorName))
            ImGui.TextColored(EditorColors.Warning, "(no behaviour picked — this task does not compile, BP1659)");
        else if (IsUnlisted())
            ImGui.TextColored(EditorColors.Warning, $"(not registered on this host now — kept: {_node.BehaviorName})");

        ImGui.TextDisabled("Start runs it and waits: Succeeded / Failed when it ends, While Running each frame.");
        ImGui.TextDisabled("Wire Started to run it alongside (a second Start restarts it).");
        ImGui.TextDisabled("Abort (from While Running, or after Started) stops it and continues on Failed.");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
