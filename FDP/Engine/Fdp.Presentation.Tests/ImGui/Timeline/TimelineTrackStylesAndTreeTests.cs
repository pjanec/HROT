using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Timeline;

/// <summary>
/// ⭐ CE-4167 part 1 (docs/DESIGN_Rules_View_UI.md RV-1 X2 / X3, RV-2) — the panel's general extensions, as pure rules: each item style's geometry decision, the tree row header's
/// layout, a collapsed node hiding only its deeper tracks, and the extent the tracks give when there is no recording.
/// Red-proofs (each compiles): X2 — make <c>Expected</c> return <c>Fill: true</c> in <c>DrawOf</c> · X3 — drop the <c>depth &gt; hideBelow</c> test in <c>Visible</c> (hides siblings) ·
/// RV-2 — make <c>ExtentOfTracks</c> start at the first item instead of <c>min(0, …)</c>.
/// </summary>
public sealed class TimelineTrackStylesAndTreeTests
{
    private static TimelineTrackItem Seg(TimelineTrackItemStyle style = TimelineTrackItemStyle.Solid, double from = 1, double to = 4)
        => new("s", from, to, "s", TimelineTrackItemKind.Segment, false, Style: style);
    private static TimelineTrackItem Key(TimelineTrackItemStyle style = TimelineTrackItemStyle.Solid)
        => new("k", 2, null, "k", TimelineTrackItemKind.Key, false, Style: style);

    [Fact]
    public void X2_Solid_IsExactlyTodaysDrawing_AndAnItemNeverSetsAStyleUnlessAsked()
    {
        Assert.Equal(TimelineTrackItemStyle.Solid, new TimelineTrackItem("a", 0, null, "a", TimelineTrackItemKind.Key, false).Style);
        var seg = TrackLayout.DrawOf(Seg(), 40f);
        Assert.Equal(new TrackLayout.ItemDraw(Fill: true, Dashed: false, Hatch: false, Handles: true, Diamond: false, HollowDiamond: false, Tick: false), seg);
        Assert.False(TrackLayout.DrawOf(Seg(), 5f).Handles);   // under 6 px: no room for grips, as before
        Assert.Equal(new TrackLayout.ItemDraw(false, false, false, false, true, false, false), TrackLayout.DrawOf(Key(), 0f));
    }

    [Fact]
    public void X2_Expected_IsADashedOutlineWithNoFill_AHollowDiamondForAKey()
    {
        var seg = TrackLayout.DrawOf(Seg(TimelineTrackItemStyle.Expected), 40f);
        Assert.True(seg.Dashed); Assert.False(seg.Fill); Assert.False(seg.Handles);
        var key = TrackLayout.DrawOf(Key(TimelineTrackItemStyle.Expected), 0f);
        Assert.True(key.Diamond && key.HollowDiamond && key.Dashed); Assert.False(key.Fill);
    }

    [Fact]
    public void X2_Hatched_DrawsDiagonalLinesInsideTheSegment_Pinned_ADiamondWithATick()
    {
        var hatched = TrackLayout.DrawOf(Seg(TimelineTrackItemStyle.Hatched), 40f);
        Assert.True(hatched.Hatch); Assert.False(hatched.Fill);
        var lines = TrackLayout.HatchLines(40f, 12f, 5f);
        Assert.NotEmpty(lines);
        Assert.All(lines, l =>
        {
            Assert.InRange(l.X0, 0f, 40f); Assert.InRange(l.X1, 0f, 40f);
            Assert.InRange(l.Y0, 0f, 12f); Assert.InRange(l.Y1, 0f, 12f);
            Assert.True(l.Y1 <= l.Y0 && l.X1 >= l.X0);   // rising to the right
        });
        Assert.Empty(TrackLayout.HatchLines(0f, 12f));

        var pinnedKey = TrackLayout.DrawOf(Key(TimelineTrackItemStyle.Pinned), 0f);
        Assert.True(pinnedKey.Diamond && pinnedKey.Tick && !pinnedKey.HollowDiamond);
        var pinnedSeg = TrackLayout.DrawOf(Seg(TimelineTrackItemStyle.Pinned), 40f);
        Assert.True(pinnedSeg.Fill && pinnedSeg.Diamond && pinnedSeg.Tick);   // a pinned segment: solid, its start marked
    }

    [Fact]
    public void X2_Dashes_AlternateOnAndOff_AndTheLastIsClippedToTheEnd()
    {
        var d = TrackLayout.Dashes(0f, 20f, dash: 4f, gap: 3f);
        Assert.Equal(new[] { (0f, 4f), (7f, 11f), (14f, 18f) }, d);
        Assert.Equal((14f, 15f), TrackLayout.Dashes(0f, 15f, 4f, 3f).Last());
        Assert.Empty(TrackLayout.Dashes(5f, 5f));
    }

    // ── X3 ──

    private sealed class Row : ITimelineTrack
    {
        public Row(string id, TrackHeader? header) { Id = id; Header = header; }
        public string Id { get; }
        public string Label => Id;
        public TrackHeader? Header { get; }
        public IReadOnlyList<TimelineTrackItem> Items { get; init; } = new List<TimelineTrackItem>();
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => Enumerable.Empty<TimelineMenuItem>();
    }

    private static Row Node(string id, int depth, bool collapsible = false) => new(id, new TrackHeader(depth, id, Collapsible: collapsible));

    [Fact]
    public void X3_AHeader_IndentsByDepth_PlacesArrowIconTextAndRightAlignsTheStatus()
    {
        var flat = TrackLayout.PlanHeader(new TrackHeader(0, "Rule"), 240f, 30f, 0f);
        var deep = TrackLayout.PlanHeader(new TrackHeader(2, "Rule"), 240f, 30f, 0f);
        Assert.Equal(2 * TrackLayout.IndentPx, deep.TextX - flat.TextX);
        Assert.Null(flat.ArrowX); Assert.Null(flat.IconX); Assert.Null(flat.StatusX);

        var full = TrackLayout.PlanHeader(new TrackHeader(1, "Ambush", "Active 6:10", 0xFF00FF00, Icon: "T", Collapsible: true), 240f, 50f, 60f);
        Assert.Equal(TrackLayout.LabelPadPx + TrackLayout.IndentPx, full.ArrowX);                  // the arrow at the indent
        Assert.Equal(full.ArrowX + TrackLayout.ArrowPx, full.IconX);                                // then the icon
        Assert.Equal(full.IconX + TrackLayout.IconPx, full.TextX);                                  // then the text
        Assert.Equal(240f - TrackLayout.LabelPadPx - 60f, full.StatusX);                            // the status ends at the right edge

        var cramped = TrackLayout.PlanHeader(new TrackHeader(1, "A long rule name", "Active 6:10", null, null, false), 100f, 90f, 60f);
        Assert.Null(cramped.StatusX);                                                               // no room after the text: dropped, never over it
    }

    [Fact]
    public void X3_ACollapsedNode_HidesTheDeeperTracksThatFollow_NotItsSiblings()
    {
        var tree = new ITimelineTrack[]
        {
            Node("root1", 0, collapsible: true), Node("a", 1, collapsible: true), Node("a1", 2), Node("a2", 2), Node("b", 1), Node("root2", 0, collapsible: true), Node("c", 1),
        };
        Assert.Equal(tree.Select(t => t.Id), TrackLayout.Visible(tree, new HashSet<string>()).Select(t => t.Id));          // nothing collapsed: everything
        Assert.Equal(new[] { "root1", "root2", "c" }, TrackLayout.Visible(tree, new HashSet<string> { "root1" }).Select(t => t.Id));   // root1's subtree gone, root2's kept
        Assert.Equal(new[] { "root1", "a", "b", "root2", "c" }, TrackLayout.Visible(tree, new HashSet<string> { "a" }).Select(t => t.Id));   // a's children gone, its sibling b stays
        Assert.Equal(new[] { "root1", "a", "a1", "a2", "b", "root2" }, TrackLayout.Visible(tree, new HashSet<string> { "root2" }).Select(t => t.Id));
        Assert.Equal(new[] { "root1", "a", "a1", "a2", "b", "root2", "c" }, TrackLayout.Visible(tree, new HashSet<string> { "b" }).Select(t => t.Id));   // b is not collapsible: ignored
    }

    [Fact]
    public void X3_TheLabelColumn_WidensOnlyWhenATrackHasAHeader()
    {
        Assert.Equal(84f, TrackLayout.LabelWidthFor(new ITimelineTrack[] { new Row("flat", null) }, 84f, 240f));
        Assert.Equal(84f, TrackLayout.LabelWidthFor(null, 84f, 240f));
        Assert.Equal(240f, TrackLayout.LabelWidthFor(new ITimelineTrack[] { new Row("flat", null), Node("tree", 0) }, 84f, 240f));
    }

    // ── RV-2 ──

    private static Row WithItems(params TimelineTrackItem[] items) => new("t", null) { Items = items };

    [Fact]
    public void RV2_NoIndex_TheTracksGiveTheExtent_FromZeroOrTheEarliestItem_ToNowOrTheLatest()
    {
        Assert.Null(TrackLayout.ExtentOfTracks(null, 5));
        Assert.Null(TrackLayout.ExtentOfTracks(new ITimelineTrack[] { WithItems() }, 5));                                   // no item: nothing to draw
        var late = TrackLayout.ExtentOfTracks(new ITimelineTrack[] { WithItems(Seg(from: 20, to: 90)) }, 30)!.Value;
        Assert.Equal((0d, 90d), late);                                                                                       // from 0, not from the first item
        var now = TrackLayout.ExtentOfTracks(new ITimelineTrack[] { WithItems(Key()) }, 300)!.Value;
        Assert.Equal((0d, 300d), now);                                                                                       // now is past every item
        var early = TrackLayout.ExtentOfTracks(new ITimelineTrack[] { WithItems(new TimelineTrackItem("n", -30, null, "n", TimelineTrackItemKind.Key, false)) }, 0)!.Value;
        Assert.Equal(-30d, early.From);                                                                                      // an item before 0 widens it
        var tiny = TrackLayout.ExtentOfTracks(new ITimelineTrack[] { WithItems(Key()) }, 0)!.Value;
        Assert.True(tiny.To - tiny.From >= TrackLayout.MinTrackExtentSeconds);                                               // never a zero-width extent
    }
}
