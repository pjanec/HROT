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
using Hrot.UI.Common.RunMode;
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

    // ── ⭐ CE-3330 E5 — the master's loop (DAR §12n) ─────────────────────────────────────────────────────────

    [Fact]
    public void CE3330_E5_CanLoop_OnlyInAReviewOrAReplay()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Live, Session = Session(Map()) };
        var c = Cursor(f, LiveIndex());
        foreach (var (m, expected) in new[] { (TransportMode.Live, false), (TransportMode.Review, true), (TransportMode.Replay, true), (TransportMode.Edit, false), (TransportMode.Idle, false) })
        {
            f.Mode = m;
            Assert.Equal(expected, c.CanLoop);
        }
        Assert.False(Cursor(null, LiveIndex()).CanLoop);

        f.Mode = TransportMode.Live;                                // outside a review the intents go nowhere
        c.SetLoop(Zero + S, Zero + 2 * S); c.ClearLoop();
        Assert.Null(f.LastSetLoop);
        Assert.Equal(0, f.ClearLoopCallCount);
        f.Loop = (1, 2);
        Assert.Null(c.Loop);                                        // and a stale loop is not drawn
    }

    [Fact]
    public void CE3330_E5_TheLoop_IsConvertedToWallTicks_OnBothAxes()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Review, Session = Session(Map()), Axis = ReplayAxis.Wall };
        var c = Cursor(f, LiveIndex());
        Assert.Null(c.Loop);                                        // none set

        f.Loop = (2, 5);                                            // wall seconds after the zero
        Assert.Equal((Zero + 2 * S, Zero + 5 * S), c.Loop);

        f.Axis = ReplayAxis.Sim;                                    // sim seconds: sim 4 is wall 7 (the pause 3…6 collapsed), sim 5 is wall 8
        f.Loop = (4, 5);
        Assert.Equal((Zero + 7 * S, Zero + 8 * S), c.Loop);
    }

    [Fact]
    public void CE3330_E5_SetLoop_GoesToTheFacade_OnTheFacadesAxis_AndClearLoopClears()
    {
        var f = new FakeTimeTransportFacade { Mode = TransportMode.Review, Session = Session(Map()), Axis = ReplayAxis.Wall };
        var c = Cursor(f, LiveIndex());

        c.SetLoop(Zero + 2 * S, Zero + 8 * S);                      // seconds AFTER THE ZERO (the zero is not 0)
        Assert.Equal((2.0, 8.0), f.LastSetLoop);

        f.Axis = ReplayAxis.Sim;                                    // sim seconds through the map (a pause in between)
        c.SetLoop(Zero + 2 * S, Zero + 8 * S);
        Assert.Equal((2.0, 5.0), f.LastSetLoop);

        f.Mode = TransportMode.Replay; f.Session = Session(null, review: false);   // a v6 replay: no map ⇒ wall seconds even on "Sim"
        c.SetLoop(Zero + S, Zero + 3 * S);
        Assert.Equal((1.0, 3.0), f.LastSetLoop);

        c.ClearLoop();
        Assert.Equal(1, f.ClearLoopCallCount);
        Assert.Null(c.Loop);
    }

    // ══ ⭐ CE-4097 part two (AQ108 D21) — the right-click menu's run-mode items, through the host's ONE run choice ═══════════════════

    private static ClusterTimelineCursor RealCursor(RunChoiceRig.Rig rig, TimelineIndex? index = null)
        => new(() => rig.F, () => index == null ? null : new Source { Index = index });

    private static void HearSession(RunChoiceRig.Rig rig, ClusterState state, bool review)
    {
        rig.Bus.PublishManaged(new ReplaySessionUpdateEvent { Session = Session(Map(), review) });
        RunChoiceRig.Hear(rig, state);
    }

    /// <summary>
    /// ⭐ Live: <i>AAR from here</i> opens the run's recording at THAT frame's sim time (frame 5 ⇒ sim 3, through the same sim column the DAR item reads) — no confirmation, AAR armed; and
    /// <i>Take checkpoint here</i> is a checkpoint of the LIVE state, so it is enabled only at the live edge (a past click shows it greyed with the reason). The DAR item stays the panel's own.
    /// 🔴 Red-proof: the AAR item running <c>OpenRecording(exercise)</c> without the moment ⇒ the call has no <c>@3</c>.
    /// </summary>
    [Fact]
    public void CE4097_D21_Live_OffersAarFromHere_AndACheckpointAtTheLiveEdgeOnly()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        var c = RealCursor(rig, LiveIndex());
        var items = c.ModeMenuItems(Zero + 5 * S, "0:05").ToList();

        var aar = Assert.Single(items, i => i.Label == "AAR from 0:05");
        Assert.True(aar.Enabled);
        aar.Run();
        Assert.Equal(new[] { $"OpenRecording:{RunChoiceRig.RunId}@3" }, rig.C.Calls);
        Assert.Equal(RunArmed.AAR, rig.Choice.Armed);
        Assert.Null(rig.Choice.Pending);                                    // the user picked the moment: nothing asked

        var past = Assert.Single(items, i => i.Label.StartsWith("Take checkpoint here"));
        Assert.False(past.Enabled);
        Assert.Contains("click the live edge", past.Label);
        var edge = c.ModeMenuItems(Zero + 9 * S, "0:09").Single(i => i.Label.StartsWith("Take checkpoint here"));
        Assert.True(edge.Enabled);
        rig.C.Calls.Clear();
        edge.Run();
        Assert.Equal(new[] { "TakeCheckpoint" }, rig.C.Calls);
        Assert.Equal("Checkpoint taken", rig.Choice.Toast!.Text);
    }

    /// <summary>
    /// ⭐ AAR: <i>Go live from here</i> seeks the replay to the clicked moment (the cursor's seek, the facade's axis), then — two frames later — the built branch goes out and Live is armed; a
    /// checkpoint is greyed (an AAR has no recorder). 🔴 Red-proof: the item calling <c>BranchLive</c> without the seek ⇒ no <c>SeekReplayIntent</c>.
    /// </summary>
    [Fact]
    public void CE4097_D21_Replay_OffersGoLiveFromHere_SeekThenBranch()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingReplay, RunArmed.Live);
        HearSession(rig, ClusterState.OperatingReplay, review: false);
        var c = RealCursor(rig, LiveIndex());
        var items = c.ModeMenuItems(Zero + 8 * S, "0:08").ToList();

        Assert.DoesNotContain(items, i => i.Label.StartsWith("AAR from"));  // already in one
        var cp = Assert.Single(items, i => i.Label.StartsWith("Take checkpoint here"));
        Assert.False(cp.Enabled);
        Assert.Contains("recording cannot take a checkpoint", cp.Label);

        var live = Assert.Single(items, i => i.Label == "Go live from 0:08");
        live.Run();
        var seek = RunChoiceRig.Sent<SeekReplayIntent>(rig).Single();       // the moment: the Sim axis, sim 5 at wall 8
        Assert.Equal(5.0, seek.TargetSimSeconds);
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);
        Assert.Empty(RunChoiceRig.Sent<TransitionStateIntent>(rig));        // the branch waits for the seek
        RunChoiceRig.Hear(rig, ClusterState.OperatingReplay);
        RunChoiceRig.Hear(rig, ClusterState.OperatingReplay);
        Assert.Equal(ClusterState.OperatingLive, rig.Bus.ReadManaged<TransitionStateIntent>().Single().TargetState);
    }

    /// <summary>⭐ A DAR offers neither move (its own menu stays); its checkpoint is greyed "go back to live"; outside a run — editing, a dry run — and for a facade with no run choice there is no run-mode item at all.</summary>
    [Fact]
    public void CE4097_D21_ADar_AnEdit_AndAFacadeWithNoChoice_OfferNoMoves()
    {
        var review = RunChoiceRig.With(ClusterState.OperatingReview, RunArmed.Live);
        HearSession(review, ClusterState.OperatingReview, review: true);
        var items = RealCursor(review, LiveIndex()).ModeMenuItems(Zero + 5 * S, "0:05").ToList();
        Assert.Equal(new[] { "Take checkpoint here — go back to live to take one" }, items.Select(i => i.Label));
        Assert.False(items[0].Enabled);

        foreach (var state in new[] { ClusterState.OperatingEdit, ClusterState.OperatingPreview, ClusterState.Idle })
            Assert.Empty(RealCursor(RunChoiceRig.With(state), LiveIndex()).ModeMenuItems(Zero + 5 * S, "0:05"));
        Assert.Empty(Cursor(new FakeTimeTransportFacade { Mode = TransportMode.Live }, LiveIndex()).ModeMenuItems(Zero + 5 * S, "0:05"));
    }

    /// <summary>⭐ The axis wrapper converts the click back to the host's wall ticks before the items see it: on the Sim axis a click at the axis position of frame 8 is "AAR from" sim 5.</summary>
    [Fact]
    public void CE4097_D21_TheAxisCursor_ForwardsTheMomentInWallTicks()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        var inner = RealCursor(rig, LiveIndex());
        var axis = TimelineAxis.Sim(Map());
        var wrapped = new AxisCursor(inner, axis);
        var aar = wrapped.ModeMenuItems(axis.ToAxis(Zero + 8 * S), "0:05").Single(i => i.Label.StartsWith("AAR from"));
        aar.Run();
        Assert.Equal(new[] { $"OpenRecording:{RunChoiceRig.RunId}@5" }, rig.C.Calls);
    }

    /// <summary>⭐ D22 — Space on the timeline (the cursor's play) is a USER control: from Edit with Live armed it is the live load, not the dry run.</summary>
    [Fact]
    public void CE4097_D22_TheTimelinesSpace_FollowsTheArmedModeFromEdit()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Unchanged);
        var c = RealCursor(rig);
        Assert.True(c.CanPlay);
        c.TogglePlay();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.C.Calls);

        var blind = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live, withControl: false);
        Assert.False(RealCursor(blind).CanPlay);                            // Live armed on a host with no session: nothing to play
    }
}
