using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐ CE-4221 (R4; RV-1 X1 · X5) — the PURE halves of the panel extensions: which links become connectors, the elbow and arrowhead geometry, and Coming up's order and filter. ✅ Red-proof: <c>ComingUp.Of</c> not sorting by time ⇒ the order test red.
/// </summary>
public sealed class TrackConnectorsAndComingUpTests
{
    private sealed class T : ITimelineTrack
    {
        public T(string id, IReadOnlyList<TimelineTrackItem> items, IReadOnlyList<TrackLink>? links = null) { Id = id; Items = items; Links = links ?? new List<TrackLink>(); }
        public string Id { get; }
        public string Label => "label " + Id;
        public IReadOnlyList<TimelineTrackItem> Items { get; }
        public IReadOnlyList<TrackLink> Links { get; }
        public void Select(string itemId) { }
        public void Move(string itemId, double toSimSeconds, bool commit) { }
        public bool Key(char key, double atSimSeconds) => false;
        public IEnumerable<TimelineMenuItem> MenuItems(string? itemId) => new List<TimelineMenuItem>();
    }

    private static TimelineTrackItem Seg(string id, double a, double b, TimelineTrackItemStyle st = TimelineTrackItemStyle.Solid) => new(id, a, b, id, TimelineTrackItemKind.Segment, false, null, st);
    private static TimelineTrackItem Key(string id, double at, TimelineTrackItemStyle st = TimelineTrackItemStyle.Solid) => new(id, at, null, id, TimelineTrackItemKind.Key, false, null, st);

    [Fact]
    public void ALink_BecomesAConnector_FromTheSourcesRightEnd_ToTheTargetsStart()
    {
        var a = new T("a", new[] { Seg("s", 0, 10) }, new[] { new TrackLink("s", "b", "k") });
        var b = new T("b", new[] { Key("k", 20) });
        var hits = new List<TimelinePanel.TrackItemHit> { new("a", "s", 12f, 8f, 40f), new("b", "k", 90f, 28f) };
        var c = Assert.Single(TrackConnectors.Resolve(new ITimelineTrack[] { a, b }, hits));
        Assert.Equal((new Vector2(40f, 8f), new Vector2(90f, 28f)), (c.From, c.To));                       // a segment leaves from its END, a key is its own end (NaN XEnd)
        hits[0] = hits[0] with { XEnd = float.NaN };
        Assert.Equal(new Vector2(12f, 8f), TrackConnectors.Resolve(new ITimelineTrack[] { a, b }, hits)[0].From);
    }

    /// <summary>The hidden-end rule: an end that was not drawn (a collapsed row, an item scrolled out) leaves no hit, so the link is not drawn — and an unknown target is not drawn either.</summary>
    [Fact]
    public void ALinkWhoseEndWasNotDrawn_IsNotAConnector()
    {
        var a = new T("a", new[] { Seg("s", 0, 10) }, new[] { new TrackLink("s", "b", "k"), new TrackLink("s", "gone", "k") });
        var b = new T("b", new[] { Key("k", 20) });
        var onlySource = new List<TimelinePanel.TrackItemHit> { new("a", "s", 12f, 8f, 40f) };
        Assert.Empty(TrackConnectors.Resolve(new ITimelineTrack[] { a, b }, onlySource));
        var onlyTarget = new List<TimelinePanel.TrackItemHit> { new("b", "k", 90f, 28f) };
        Assert.Empty(TrackConnectors.Resolve(new ITimelineTrack[] { a, b }, onlyTarget));
        var both = new List<TimelinePanel.TrackItemHit> { new("a", "s", 12f, 8f, 40f), new("b", "k", 90f, 28f) };
        Assert.Single(TrackConnectors.Resolve(new ITimelineTrack[] { a, b }, both));                       // "gone" is unknown: skipped, the other still draws
    }

    [Fact]
    public void TheElbow_GoesRightThenAcross_AndTheArrowPointsAlongItsLastSegment()
    {
        var path = TrackConnectors.Elbow(new Vector2(10, 5), new Vector2(110, 25));
        Assert.Equal(4, path.Length);
        Assert.Equal((new Vector2(10, 5), new Vector2(110, 25)), (path[0], path[^1]));
        Assert.Equal(path[1].X, path[2].X);                                                               // one vertical run between the two rows
        Assert.Equal((5f, 25f), (path[1].Y, path[2].Y));
        var arrow = TrackConnectors.Arrow(path);
        Assert.Equal(path[^1], arrow[0]);                                                                 // the tip is the target
        Assert.All(arrow.Skip(1), p => Assert.True(p.X < arrow[0].X));                                   // the base lies behind it: it points right
        Assert.Equal(2, TrackConnectors.Elbow(new Vector2(10, 5), new Vector2(110, 5)).Length);           // the same row: a straight line
        var back = TrackConnectors.Elbow(new Vector2(100, 5), new Vector2(20, 25));                        // a target to the LEFT: out past the source first, never through it
        Assert.True(back[1].X > 100f);
    }

    [Fact]
    public void ComingUp_ListsOnlyExpectedAndPinned_AfterNow_SoonestFirst_AndCapsAtN()
    {
        var t1 = new T("t1", new[] { Seg("plan", 5, 90, TimelineTrackItemStyle.Expected), Key("pin", 40, TimelineTrackItemStyle.Pinned), Seg("past", 0, 3, TimelineTrackItemStyle.Solid), Seg("wait", 6, 80, TimelineTrackItemStyle.Hatched) });
        var t2 = new T("t2", new[] { Key("soon", 12, TimelineTrackItemStyle.Pinned), Key("old", 1, TimelineTrackItemStyle.Pinned), Key("plain", 30, TimelineTrackItemStyle.Solid) });
        var got = ComingUp.Of(new ITimelineTrack[] { t1, t2 }, now: 5, n: 10);
        Assert.Equal(new[] { "soon", "pin", "plan" }, got.Select(e => e.ItemId));                          // an Expected SEGMENT happens at its end (90); nothing Solid / Hatched; nothing before now
        Assert.Equal(new[] { 12d, 40d, 90d }, got.Select(e => e.At));
        Assert.Equal(new[] { "soon", "pin" }, ComingUp.Of(new ITimelineTrack[] { t1, t2 }, 5, 2).Select(e => e.ItemId));
        Assert.Empty(ComingUp.Of(new ITimelineTrack[] { t1, t2 }, 5, 0));
        Assert.Empty(ComingUp.Of(new ITimelineTrack[] { t1, t2 }, 100, 5));
        var tie = ComingUp.Of(new ITimelineTrack[] { new T("x", new[] { Key("k", 20, TimelineTrackItemStyle.Pinned) }), new T("y", new[] { Key("k", 20, TimelineTrackItemStyle.Pinned) }) }, 0, 5);
        Assert.Equal(new[] { "x", "y" }, tie.Select(e => e.TrackId));                                      // ties keep track order
    }

    /// <summary>A track that implements nothing new (the director's) has no links, no handle and no drop: every default is OFF.</summary>
    [Fact]
    public void ADirectorLikeTrack_HasEveryNewMemberOff()
    {
        ITimelineTrack plain = new T("director", new[] { Seg("s", 0, 10) });
        Assert.Empty(plain.Links);
        Assert.False(plain.ConnectsFrom("s"));
        Assert.False(plain.CanConnect("s", plain));
        Assert.Empty(TrackConnectors.Resolve(new[] { plain }, new List<TimelinePanel.TrackItemHit> { new("director", "s", 1f, 1f, 5f) }));
        Assert.Equal(0, new TimelinePanel().ComingUpCount);
    }
}
