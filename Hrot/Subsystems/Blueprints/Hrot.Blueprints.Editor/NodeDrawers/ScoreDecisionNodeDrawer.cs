using ImGuiNET;
using Fdp.Toolkit.Utility;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// ⭐ BP-27 / CE-3083 (<c>DESIGN_Decision_Layer</c> §3.3d) — the Details editor of a Score Decision: which authored
/// decision it evaluates. The picker lists <see cref="UtilityDecisionCatalog"/>'s decisions by display name and stores the
/// decision's ASSET ID — the string the compiler hashes (<c>UtilityIdHash</c>) into the id the catalog registers under, so a
/// picked decision resolves by construction. ⚠ A stored id not in the catalog is kept and flagged, never dropped.
/// ⭐ Same decision list as the <c>UtilityDecisionRef</c> component field (<c>ComponentEditDrawer</c>, CE-2068).
/// </summary>
public sealed class ScoreDecisionNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<(string Name, string AssetId)>> _decisions;

    /// <param name="decisions">The pickable decisions (display name, asset id). Null = the process catalog
    /// (<see cref="CatalogDecisions"/>) — the production answer, so a host need not pass it.</param>
    public ScoreDecisionNodeDrawer(IEditService editService, Func<IReadOnlyList<(string Name, string AssetId)>>? decisions = null)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _decisions   = decisions ?? CatalogDecisions;
    }

    public bool Handles(Node node) => node is ScoreDecisionNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ScoreDecisionNodeSession((ScoreDecisionNode)node, parentAsset, _editService, _decisions);

    /// <summary>Every catalog decision that carries an authored asset id (a hand-registered one has none and cannot be
    /// referenced from an asset), ordered by name.</summary>
    public static IReadOnlyList<(string Name, string AssetId)> CatalogDecisions()
    {
        UtilityDecisionCatalog.EnsureRegistered();
        return UtilityDecisionCatalog.Shared.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Def.AssetId))
            .Select(e => (Name: string.IsNullOrWhiteSpace(e.Def.DebugName) ? e.Def.AssetId : e.Def.DebugName, e.Def.AssetId))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}

internal sealed class ScoreDecisionNodeSession : INodeEditSession
{
    private readonly ScoreDecisionNode _node;
    private readonly BlueprintAsset _parent;
    private readonly IEditService _editService;
    private readonly Func<IReadOnlyList<(string Name, string AssetId)>> _decisions;
    private string _filter = "";

    public bool IsDirty { get; private set; }

    public ScoreDecisionNodeSession(ScoreDecisionNode node, BlueprintAsset parent, IEditService editService,
        Func<IReadOnlyList<(string Name, string AssetId)>> decisions)
    {
        _node = node; _parent = parent; _editService = editService; _decisions = decisions;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────
    internal void SetDecisionForTest(string assetId) => ApplyDecision(assetId);
    internal IReadOnlyList<(string Name, string AssetId)> GetFilteredDecisionsForTest(string filter) => Filtered(filter);
    internal bool IsCurrentUnlistedForTest() => IsUnlisted();
    internal string CurrentLabelForTest() => CurrentLabel();

    private IReadOnlyList<(string Name, string AssetId)> Filtered(string filter)
    {
        var all = _decisions();
        var names = SharedTypePickerLogic.Filter(all.Select(d => d.Name).Distinct(StringComparer.Ordinal).ToList(), filter);
        return all.Where(d => names.Contains(d.Name, StringComparer.Ordinal)).ToList();
    }

    private bool IsUnlisted()
        => !string.IsNullOrWhiteSpace(_node.AssetId)
        && !_decisions().Any(d => string.Equals(d.AssetId, _node.AssetId, StringComparison.Ordinal));

    private string CurrentLabel()
    {
        if (string.IsNullOrWhiteSpace(_node.AssetId)) return "(none)";
        foreach (var d in _decisions())
            if (string.Equals(d.AssetId, _node.AssetId, StringComparison.Ordinal)) return d.Name;
        return _node.AssetId;
    }

    /// <summary>One undoable edit. The node's pins do not depend on the decision, so no pin resync is needed.</summary>
    private void ApplyDecision(string assetId)
    {
        assetId = assetId.Trim();
        if (assetId == _node.AssetId) return;
        var before = _node.AssetId;

        void Set(string v)
        {
            _node.AssetId = v;
            IsDirty = true;
            _editService.NotifyStructureChanged(_parent);
        }

        _editService.RecordPropertyEdit(_parent, "Set Score Decision",
            apply: () => Set(assetId),
            undo:  () => Set(before));
    }

    public void Draw()
    {
        ImGui.Text("Score Decision");
        ImGui.Separator();

        if (ImGui.BeginCombo("Decision", CurrentLabel()))
        {
            ImGui.InputTextWithHint("##ScoreDecisionFilter", "Filter...", ref _filter, 256);
            foreach (var (name, assetId) in Filtered(_filter))
            {
                bool selected = assetId == _node.AssetId;
                if (ImGui.Selectable(name + "##" + assetId, selected)) ApplyDecision(assetId);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (string.IsNullOrWhiteSpace(_node.AssetId))
            ImGui.TextColored(EditorColors.Warning, "(no decision picked)");
        else if (IsUnlisted())
            ImGui.TextColored(EditorColors.Warning, $"(not in the decision catalog — kept: {_node.AssetId})");

        ImGui.TextDisabled("Scores the decision's options for this entity each time it runs; Winner is the chosen option.");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
