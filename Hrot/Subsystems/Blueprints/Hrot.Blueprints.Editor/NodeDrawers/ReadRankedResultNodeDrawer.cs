using ImGuiNET;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-05 — Details-panel editor for <see cref="ReadRankedResultNode.Rank"/>.
///
/// <para>
/// The node compiled and ran, but its only authored value had no editor at all: whatever rank the
/// palette baked at creation was the rank forever. A plain integer, no catalog to consult — the
/// simplest of the drawer gaps.
/// </para>
/// </summary>
public sealed class ReadRankedResultNodeDrawer : IBlueprintNodeDrawer
{
    private readonly IEditService _editService;

    public ReadRankedResultNodeDrawer(IEditService editService)
    {
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
    }

    public bool Handles(Node node) => node is ReadRankedResultNode;

    public INodeEditSession CreateSession(Node node, BlueprintAsset parentAsset)
        => new ReadRankedResultNodeSession((ReadRankedResultNode)node, parentAsset, _editService);
}

/// <summary>
/// Edit session for <see cref="ReadRankedResultNode"/>. Mutation lives in
/// <see cref="ApplyRank"/> (reachable headlessly via <see cref="SetRankForTest"/>);
/// <see cref="Draw"/> is the only ImGui-coupled surface.
/// </summary>
internal sealed class ReadRankedResultNodeSession : INodeEditSession
{
    private readonly ReadRankedResultNode _node;
    private readonly BlueprintAsset       _parent;
    private readonly IEditService         _editService;

    /// <summary>BP-11 (Q22-E1): hold-to-repeat on the steppers is one undo entry, not one per frame.</summary>
    private readonly ContinuousEditCoalescer<int> _rankCoalescer = new();

    public bool IsDirty { get; private set; }

    public ReadRankedResultNodeSession(
        ReadRankedResultNode node, BlueprintAsset parentAsset, IEditService editService)
    {
        _node        = node;
        _parent      = parentAsset;
        _editService = editService;
    }

    // ── Internal test hooks (InternalsVisibleTo Hrot.Blueprints.Tests) ──────────

    /// <summary>Test hook: simulates the designer setting the rank (one complete gesture).</summary>
    internal void SetRankForTest(int rank) => ApplyRank(rank);

    /// <summary>Test hook: the value a raw input would be clamped to.</summary>
    internal static int ClampRankForTest(int rank) => ClampRank(rank);

    // ── Private mutation helpers (called by both Draw() and test hooks) ─────────

    /// <summary>
    /// A rank is a 0-based index into the EQS result list; a negative one would index out of range.
    /// Clamped rather than rejected, so the stepper cannot be used to author an invalid asset.
    /// </summary>
    private static int ClampRank(int rank) => Math.Max(0, rank);

    /// <summary>Applies and records a complete change as one undoable edit.</summary>
    private void ApplyRank(int rank)
    {
        int after  = ClampRank(rank);
        int before = _node.Rank;
        if (after == before) return;

        _editService.RecordPropertyEdit(
            _parent, $"Set Rank {after}",
            apply: () => { _node.Rank = after;  IsDirty = true; },
            undo:  () => { _node.Rank = before; IsDirty = true; });
    }

    // ── INodeEditSession ─────────────────────────────────────────────────────────

    public void Draw()
    {
        ImGui.Text("Read Ranked Result");
        ImGui.Separator();

        // Snapshot before the widget can mutate this frame — the gesture's undo baseline.
        int baseline = _node.Rank;

        int rank = _node.Rank;
        if (ImGui.InputInt("Rank", ref rank))
        {
            // Live, deliberately un-recorded: holding a stepper fires per frame. The gesture is
            // recorded once on deactivation below (Q22-E1).
            _node.Rank = ClampRank(rank);
            IsDirty    = true;
            _editService.MarkDirty(_parent);
        }

        _rankCoalescer.BeginIfNeeded(ImGui.IsItemActivated(), baseline);
        if (_rankCoalescer.TryCommit(ImGui.IsItemDeactivatedAfterEdit(), out var beforeGesture))
        {
            int afterGesture = _node.Rank;
            if (afterGesture != beforeGesture)
                _editService.RecordPropertyEdit(
                    _parent, $"Set Rank {afterGesture}",
                    // Already the current value — the assignment matters on redo.
                    apply: () => { _node.Rank = afterGesture;  IsDirty = true; },
                    undo:  () => { _node.Rank = beforeGesture; IsDirty = true; });
        }

        ImGui.TextDisabled("(0 = top-ranked result of the EQS query)");
    }

    public void ResetDirty() => IsDirty = false;
    public void Dispose() { }
}
