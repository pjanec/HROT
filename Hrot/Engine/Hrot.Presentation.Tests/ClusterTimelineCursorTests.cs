using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐ <c>CE-3330</c> E2 (docs/DESIGN_Director_Timeline_Editor.md §2e) — <see cref="ClusterTimelineCursor"/>: the event timeline's cursor over THE ONE
/// facade, per mode. Wall ticks in; the facade's seconds on the session axis out. Pure (a fake facade, a synthetic index), no ImGui.
/// </summary>
public sealed class ClusterTimelineCursorTests
{
    private const long S = TimeSpan.TicksPerSecond;
    private const long Zero = 1000 * S;   // a session whose zero is NOT 0

    private sealed class Source : ITimelineSource
    {
        public TimelineIndex Index { get; init; } = TimelineIndex.Empty;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    /// <summary>Ten frames, one per second after <see cref="Zero"/>, whose sim clock stood still for frames 3…6 (a pause).</summary>
    private static readonly double[] PausedSim = { 0, 1, 2, 3, 3, 3, 3, 4, 5, 6 };

    private static long[] Wall() => Enumerable.Range(0, 10).Select(i => Zero + i * S).ToArray();

    private static TimelineIndex LiveIndex(bool withSim = true)
    {
        var wall = Wall();
        return new TimelineIndex(new Dictionary<string, SharedList<TimelineEvent>>(), new SharedList<long>(wall), 10, 10, null,
            withSim ? new SharedList<double>(PausedSim) : null);
    }

    private static ReplayTimeMap Map()
    {
        var flags = new FrameFlags[10];
        for (int i = 4; i <= 6; i++) flags[i] = FrameFlags.Halted;
        return ReplayTimeMap.FromFrames(Wall(), PausedSim, flags);
    }

    private static ReplaySession Session(ReplayTimeMap? map, bool review = true) => new()
    {
        Active = true, IsReview = review, ZeroWallTicks = Zero, LengthSeconds = 9, TimeMap = map,
        LivePointSeconds = review ? 8 : null,
        Checkpoints = new[] { new CheckpointMark { Label = "alpha", WallTicks = Zero + 2 * S }, new CheckpointMark { Label = "beta", WallTicks = Zero + 7 * S } },
    };

    private static ClusterTimelineCursor Cursor(FakeTimeTransportFacade? f, TimelineIndex? index = null)
    {
        var source = index == null ? null : new Source { Index = index };
        return new ClusterTimelineCursor(() => f, () => source);
    }

    [Theory]
    [InlineData(TransportMode.Live, TimelineMode.Live)]
    [InlineData(TransportMode.Review, TimelineMode.Review)]
    [InlineData(TransportMode.Replay, TimelineMode.Replay)]
    [InlineData(TransportMode.Edit, TimelineMode.None)]
    [InlineData(TransportMode.Preview, TimelineMode.None)]
    [InlineData(TransportMode.Idle, TimelineMode.None)]
    public void CE3330_E2_TheModeFollowsTheFacade(TransportMode transport, TimelineMode expected)
    {
        Assert.Equal(expected, Cursor(new FakeTimeTransportFacade { Mode = transport }).Mode);
    }

    [Fact]
    public void CE3330_E2_WithNoFacade_NothingMoves()
    {
        var c = Cursor(null, LiveIndex());
        Assert.Equal(TimelineMode.None, c.Mode);
        Assert.False(c.CanSeek);
        Assert.False(c.CanReview);
        Assert.False(c.CanPlay);
        Assert.False(c.IsPlaying);
        Assert.Same(TimelineAxis.Wall, c.Axis);
        c.Seek(Zero); c.EnterReview(Zero); c.TogglePlay();   // no throw
    }

    [Fact]
    public void CE3330_E2_TheExtent_IsTheIndex_ExceptInAReplay_WhereItIsTheSession()
    {
        var index = LiveIndex();
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Live };
        var live = Cursor(f, index);
        Assert.Equal((Zero, Zero + 9 * S, Zero + 9 * S), (live.ExtentFrom, live.ExtentTo, live.Now));   // "now" is the live edge

        f.Mode = TransportMode.Review; f.Session = Session(Map()); f.TotalTime = 4;
        Assert.Equal((Zero, Zero + 9 * S), (live.ExtentFrom, live.ExtentTo));                           // the (frozen) live index
        Assert.Equal(Zero + 4 * S, live.Now);                                                           // zero + the cluster clock (wall s after zero)

        // a replay: no lanes on this host (the window passes no source) — the extent is the SESSION's span
        f.Mode = TransportMode.Replay; f.Session = Session(Map(), review: false); f.TotalTime = 6;
        var replay = Cursor(f, null);
        Assert.Equal((Zero, Zero + 9 * S, Zero + 6 * S), (replay.ExtentFrom, replay.ExtentTo, replay.Now));

        f.Session = new ReplaySession { Active = true, ZeroWallTicks = Zero, LengthSeconds = 30 };
        Assert.Equal(Zero + 30 * S, replay.ExtentTo);

        f.TotalTime = 99;                                                                               // a clock past the end shows at the end
        Assert.Equal(Zero + 30 * S, replay.Now);
    }

    [Fact]
    public void CE3330_E2_OnlyAReviewOrAReplay_Seeks_AndTheSecondsAreOnTheSessionAxis()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Live };
        var c = Cursor(f, LiveIndex());
        Assert.False(c.CanSeek);
        c.Seek(Zero + 3 * S);
        Assert.Null(f.LastSeek);                                  // a live host never seeks (R-3059): it enters a review

        f.Mode = TransportMode.Review; f.Session = Session(Map());
        Assert.True(c.CanSeek);

        f.Axis = ReplayAxis.Sim;                                  // sim seconds: wall second 8 is sim 5
        c.Seek(Zero + 8 * S);
        Assert.Equal(5.0, f.LastSeek!.Value, 3);

        f.Axis = ReplayAxis.Wall;                                 // seconds AFTER THE ZERO: not the absolute second
        c.Seek(Zero + 8 * S);
        Assert.Equal(8.0, f.LastSeek!.Value, 3);

        f.Mode = TransportMode.Replay; f.Session = Session(null, review: false);   // a v6 replay: no map ⇒ the wall seconds even on "Sim"
        f.Axis = ReplayAxis.Sim;
        c.Seek(Zero + 2 * S);
        Assert.Equal(2.0, f.LastSeek!.Value, 3);
    }

    [Fact]
    public void CE3330_E2_AReviewIsEnteredFromLive_BySimSecondsOfTheFrameAtOrBeforeTheClick()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Live };
        var c = Cursor(f, LiveIndex());
        Assert.True(c.CanReview);
        c.EnterReview(Zero + 4 * S + S / 2);                      // between frames 4 and 5 (sim 3 and 3): frame 4 → sim 3
        Assert.Equal(3.0, f.LastEnterReviewAtSim!.Value, 3);
        c.EnterReview(Zero + 8 * S);                              // frame 8 → sim 5
        Assert.Equal(5.0, f.LastEnterReviewAtSim!.Value, 3);

        var none = new FakeTimeTransportFacade { Mode = TransportMode.Live };
        var noSim = Cursor(none, LiveIndex(withSim: false));
        Assert.False(noSim.CanReview);                            // no sim column ⇒ no review to enter
        noSim.EnterReview(Zero + 3 * S);
        Assert.Null(none.LastEnterReviewAtSim);

        foreach (var m in new[] { TransportMode.Review, TransportMode.Replay, TransportMode.Edit })
        {
            f.Mode = m;
            Assert.False(c.CanReview);                            // only the live run can be reviewed
        }
    }

    [Fact]
    public void CE3330_E2_TheLivePointAndTheCheckpoints_ComeFromTheSession()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Review, Session = Session(Map()) };
        var c = Cursor(f, LiveIndex());
        Assert.Equal(Zero + 8 * S, c.LivePoint);
        Assert.Equal(new[] { ("alpha", Zero + 2 * S), ("beta", Zero + 7 * S) }, c.Checkpoints.Select(m => (m.Label, m.WallTicks)));

        f.Mode = TransportMode.Replay; f.Session = Session(Map(), review: false);
        Assert.Null(c.LivePoint);                                 // a replay has no live point
        Assert.Equal(2, c.Checkpoints.Count);

        f.Session = new ReplaySession { Active = false, Checkpoints = Session(null).Checkpoints, LivePointSeconds = 3, IsReview = true };
        Assert.Null(c.LivePoint);                                 // an inactive sample draws nothing
        Assert.Empty(c.Checkpoints);
    }

    [Fact]
    public void CE3330_E2_TheAxis_IsSimOverTheSessionMapInAReview_TheIndexMapInLive_WallWhenTheFacadeSaysWall()
    {
        var map = Map();
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Review, Session = Session(map), Axis = ReplayAxis.Sim };
        var c = Cursor(f, LiveIndex());
        Assert.True(c.Axis.IsSim);
        Assert.Same(map, c.Axis.Map);
        Assert.Same(c.Axis, c.Axis);                              // one axis per map: the panel's projection cache holds
        Assert.Same(map, c.TimeMap);

        f.Axis = ReplayAxis.Wall;
        Assert.Same(TimelineAxis.Wall, c.Axis);
        Assert.Same(map, c.TimeMap);                              // the pauses are still shaded on the wall axis

        f.Mode = TransportMode.Live; f.Session = null; f.Axis = ReplayAxis.Sim;
        var live = c.Axis;
        Assert.True(live.IsSim);
        Assert.NotSame(map, live.Map);                            // the map built from the live index's own frame sim…
        Assert.Equal(3.0, live.Map!.SimAt(Zero + 4 * S), 3);      // …a pause: frames 3…6 stand at sim 3
        Assert.Equal(5.0, live.Map.SimAt(Zero + 8 * S), 3);
        Assert.Same(live, c.Axis);                                // cached per index snapshot

        var noSim = Cursor(f, LiveIndex(withSim: false));
        Assert.Same(TimelineAxis.Wall, noSim.Axis);               // no frame sim ⇒ no Sim axis
        Assert.Null(noSim.TimeMap);
    }

    [Fact]
    public void CE3330_E2_PlayAndPause_AreTheFacades()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Live, IsPaused = false, IsPlayPauseEnabled = true };
        var c = Cursor(f, LiveIndex());
        Assert.True(c.IsPlaying);
        Assert.True(c.CanPlay);
        c.TogglePlay();
        Assert.Equal(1, f.TogglePlayPauseCallCount);

        f.IsPaused = true; f.IsPlayPauseEnabled = false;
        Assert.False(c.IsPlaying);
        Assert.False(c.CanPlay);
        c.TogglePlay();
        Assert.Equal(1, f.TogglePlayPauseCallCount);              // a disabled transport is not toggled
    }
}
