using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Infrastructure;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.Storage;
using ImGuiNET;
using Xunit;
using ClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests.Storage;

/// <summary>
/// ⭐⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.3, §8 ① ②; mockup <c>recording-storage-visual.svg</c>) — the status bar's transfer segment in its SIX states and the Transfers popover, from REAL <see cref="StorageJob"/> values through a real
/// <see cref="ClusterView"/> and a real <see cref="ClusterTimeTransportAdapter"/> (<see cref="RunChoiceRig"/>), never a model fed by hand. Cancel / Resume only on the Author and Instructor seats (Q7); the master's refusal is shown as the reason; the segment allocates nothing in a steady frame.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TheTransfersTests
{
    private static readonly Guid Ex3 = Guid.Parse("3a3a3a3a-0000-0000-0000-000000000003"), Ex5 = Guid.Parse("5b5b5b5b-0000-0000-0000-000000000005"), Ex2 = Guid.Parse("2c2c2c2c-0000-0000-0000-000000000002");
    private const string Me = "Instructor-1";

    private static StorageJob Job(Guid ex, StorageJobKind kind, StorageJobState state, int done = 0, int total = 0, long bDone = 0, long bTotal = 0, string by = Me, string? reason = null, bool giving = false, int waiting = 0)
        => new(Guid.NewGuid(), kind, (ExerciseId)ex, state, done, total, bDone, bTotal, waiting, by, reason, giving);

    private static void Hear(Rig rig, params StorageJob[] jobs)
    {
        rig.Bus.PublishManaged(new StorageJobsUpdateEvent { Jobs = jobs });
        rig.Bus.SwapBuffers(); rig.View.Update(); rig.F.Update(); rig.Bus.SwapBuffers();
    }

    private static (Rig Rig, StorageStatus Status, StorageTransfers Transfers, Clock Clock) Rig_(Seat seat = Seat.Instructor)
    {
        var rig = With(ClusterState.OperatingLive);
        var clock = new Clock();
        var transfers = new StorageTransfers(rig.F, () => seat, () => Me, id => { });
        return (rig, new StorageStatus(() => rig.F.StorageJobs, () => Me, () => clock.Now), transfers, clock);
    }

    private sealed class Clock { public double Now; }

    // ── the six states of ① ──────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 Mutation: <c>StorageStatus.Compute</c> picking the first QUEUED job as the head ⇒ the running archive is not shown ⇒ red. Mutation: <c>IsYours</c> always false ⇒ red.</summary>
    [Fact]
    public void U11_State1_Nothing_State2_Archiving_Yours_WithAQueuedCount_AndTheNumbers()
    {
        var (rig, status, _, _) = Rig_();
        Assert.Equal(StorageSegmentState.Idle, status.Segment.State);                      // 1 · nothing running: the segment says nothing
        Hear(rig, Job(Ex3, StorageJobKind.Archive, StorageJobState.Done, 4, 4, 100, 100));
        Assert.Equal(StorageSegmentState.Idle, status.Segment.State);                      // an archive that finished well is history, not news

        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Queued, by: "Debrief-1"), Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 2, 4, 1_288_490_189, 3_328_599_654));
        var s = status.Segment;                                                            // 2 · archiving — this station asked
        Assert.Equal(StorageSegmentState.Active, s.State);
        Assert.Equal("Archiving exercise 3a3a3a3a — 2 of 4 files · 1.2 / 3.1 GB", s.Text);   // the RUNNING job leads, though the queued one is first in the list
        Assert.True(s.Yours);
        Assert.Equal(1, s.Queued);
        Assert.InRange(s.Progress, 0.38f, 0.39f);
        Assert.False(s.GivingWay);
        Assert.Equal("", s.Action);
    }

    /// <summary>🔴 Mutation: the clock mark / "slowed" words dropped ⇒ red.</summary>
    [Fact]
    public void U11_State3_ArchivingWhileALiveRuns_SaysItIsSlowed_State4_AnotherStationsImport()
    {
        var (rig, status, _, _) = Rig_();
        Hear(rig, Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 3, 4, 2_000_000_000, 3_000_000_000, giving: true));
        var s = status.Segment;                                                            // 3 · a Live runs: the transfer gives way (Q5)
        Assert.True(s.GivingWay);
        Assert.EndsWith("slowed while Live runs", s.Text);

        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Running, 1, 2, 429_496_730, 1_503_238_553, by: "Debrief-1"));
        s = status.Segment;                                                                // 4 · importing — another station asked
        Assert.Equal("Importing exercise 5b5b5b5b — 1 of 2 files · 0.4 / 1.4 GB", s.Text);
        Assert.False(s.Yours);
    }

    /// <summary>🔴 Mutation: the ready window never closing ⇒ red; an old Done import flashing as ready on a late joiner ⇒ red.</summary>
    [Fact]
    public void U11_State5_AnImportThatJustFinished_IsReadyWithOpen_ForTwentySeconds_NeverAnOldOne()
    {
        var (rig, status, transfers, clock) = Rig_();
        Hear(rig, Job(Ex2, StorageJobKind.Import, StorageJobState.Done, 2, 2, 5, 5));
        Assert.Equal(StorageSegmentState.Idle, status.Segment.State);                      // already ended when this station first looked: history, not "ready"

        clock.Now = 100;
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Running, 1, 2, 1, 2, by: "Debrief-1"));
        Assert.Equal(StorageSegmentState.Active, status.Segment.State);
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Done, 2, 2, 2, 2, by: "Debrief-1"), Job(Ex2, StorageJobKind.Import, StorageJobState.Done, 2, 2, 5, 5));
        var s = status.Segment;                                                            // 5 · an import finished
        Assert.Equal((StorageSegmentState.Ready, "Exercise 5b5b5b5b ready", "Open"), (s.State, s.Text, s.Action));
        Assert.Equal(Ex5, s.ExerciseId);

        clock.Now = 100 + StorageStatus.ReadySeconds - 0.5; Assert.Equal(StorageSegmentState.Ready, status.Segment.State);
        clock.Now = 100 + StorageStatus.ReadySeconds + 0.5; Assert.Equal(StorageSegmentState.Idle, status.Segment.State);   // the bar goes quiet; the list keeps it
        Assert.True(transfers.Open(Ex5));
    }

    /// <summary>🔴 Mutation: <c>SupersededBy</c> always false ⇒ the incomplete state outlives its resume ⇒ red.</summary>
    [Fact]
    public void U11_State6_AnArchiveThatStoppedPartWay_IsIncompleteWithResume_UntilALaterJobSupersedesIt()
    {
        var (rig, status, _, _) = Rig_();
        Hear(rig, Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4, reason: "SimHost-1 offline"));
        var s = status.Segment;
        Assert.Equal((StorageSegmentState.Incomplete, "Archive of exercise 2c2c2c2c incomplete — 3 of 4 files (SimHost-1 offline)", "Resume"), (s.State, s.Text, s.Action));

        Hear(rig, Job(Ex2, StorageJobKind.Archive, StorageJobState.Running, 3, 4, 3, 4), Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4, reason: "SimHost-1 offline"));
        Assert.Equal(StorageSegmentState.Active, status.Segment.State);                    // resumed: the running job is the news
        Hear(rig, Job(Ex2, StorageJobKind.Archive, StorageJobState.Done, 4, 4, 4, 4), Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4));
        Assert.Equal(StorageSegmentState.Idle, status.Segment.State);                      // done: the old failure is history
    }

    // ── what a station may do (Q7) and the master's refusal ────────────────────────────────────────

    /// <summary>🔴 Mutation: <c>SeatRefusal</c> allowing every seat ⇒ red. Mutation: <c>Cancel</c> naming the exercise instead of the job's id ⇒ red.</summary>
    [Fact]
    public void U11_Cancel_AndResume_AreTheAuthorAndInstructorSeats_EachOneExistingOp()
    {
        foreach (var seat in new[] { Seat.Author, Seat.Instructor })
        {
            var (rig, _, transfers, _) = Rig_(seat);
            var running = Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 1, 4, 1, 4);
            var failed = Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4);
            Hear(rig, running, failed);
            Assert.Null(transfers.CancelRefusal(running));
            Assert.True(transfers.Cancel(running));
            var cancel = Sent<CancelOperationIntent>(rig.Bus).Single();
            Assert.Equal(running.JobId, cancel.TargetRequestId);                           // CancelOperation(JobId): the request that created the job

            Assert.Null(transfers.ResumeRefusal(failed));
            Assert.True(transfers.Resume(failed));
            var ask = Sent<ExecuteStorageOpIntent>(rig.Bus).Single();
            Assert.Equal((StorageOpType.Export, (Guid)Ex2, Me, false), (ask.Operation, (Guid)ask.ExerciseId, ask.AskedBy, ask.FromArchive));   // the SAME ask again: the pull skips the files already whole
        }
        foreach (var seat in new[] { Seat.Observer, Seat.Player, Seat.SideController, Seat.Reviewer })
        {
            var (rig, _, transfers, _) = Rig_(seat);
            var running = Job(Ex3, StorageJobKind.Archive, StorageJobState.Running);
            var failed = Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed);
            Hear(rig, running, failed);
            Assert.Equal("needs the Author or Instructor seat", transfers.CancelRefusal(running));
            Assert.Equal("needs the Author or Instructor seat", transfers.ResumeRefusal(failed));
            Assert.False(transfers.Cancel(running)); Assert.False(transfers.Resume(failed));
            Assert.Empty(Sent<CancelOperationIntent>(rig.Bus)); Assert.Empty(Sent<ExecuteStorageOpIntent>(rig.Bus));
        }
        var (r2, _, t2, _) = Rig_(Seat.Instructor);
        var done = Job(Ex3, StorageJobKind.Archive, StorageJobState.Done);
        Assert.Equal("the transfer has ended", t2.CancelRefusal(done));                    // an ended job has nothing to cancel
        Assert.Equal("only an archive that stopped part-way resumes", t2.ResumeRefusal(done));
    }

    /// <summary>🔴 Mutation: <c>StorageAskRefusal</c> reading nothing ⇒ the refusal never shows ⇒ red.</summary>
    [Fact]
    public void U11_TheMastersRefusal_OfAnAsk_IsShownAsTheReason()
    {
        var (rig, _, transfers, _) = Rig_();
        var failed = Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4);
        Hear(rig, failed);
        Assert.True(transfers.Resume(failed));
        var ask = Sent<ExecuteStorageOpIntent>(rig.Bus).Single();
        Assert.Null(transfers.LastRefusal);                                                // not answered yet
        rig.Bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = ask.RequestId, StatusCode = OrchestrationStatusCode.Rejected, ResultPayload = "exercise 2c2c2c2c is being archived" });
        rig.Bus.SwapBuffers(); rig.View.Update(); rig.Bus.SwapBuffers();
        Assert.Equal("exercise 2c2c2c2c is being archived", transfers.LastRefusal);        // the master's own words
    }

    /// <summary>⭐ HOT PATH — the segment is read every frame: nothing allocates while the job list is unchanged. 🔴 Mutation: <c>Segment</c> rebuilding per call ⇒ red.</summary>
    [Fact]
    public void U11_TheSegment_AllocatesNothingInASteadyFrame()
    {
        var (rig, status, transfers, clock) = Rig_();
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Queued, by: "Debrief-1"), Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 2, 4, 1_288_490_189, 3_328_599_654, giving: true));
        var section = new StorageStatusBarSection(status, transfers);
        for (int i = 0; i < 5; i++) { _ = status.Segment; _ = section.Rows.Count; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { _ = status.Segment; _ = section.Rows.Count; _ = transfers.SeatRefusal; _ = transfers.Jobs.Length; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── a frame: the bar, the six states, the popover ────────────────────────────────────────────────

    private const string SeatText = "Instructor · Observe";

    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    /// <summary>
    /// ⭐ The REAL segment in a REAL status bar beside the seat · hat text, in each of the mockup's six states: it sits LEFT of the seat text on the bar's one row (never over it, never at the left end), the running states carry a progress bar, the ended ones their
    /// button (<i>Open</i> / <i>Resume</i>); the click on the text opens the popover whose rows carry <i>Cancel</i> / <i>Resume</i>. Renders <c>u11-storage-state-N.png</c> and <c>u11-storage-popover.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git).
    /// 🔴 Mutation: the segment's x ignoring <c>reserveRight</c> ⇒ it overlaps the seat text ⇒ red.
    /// </summary>
    [SkippableFact]
    public void U11_TheSegment_SitsLeftOfTheSeatText_InSixStates_AndThePopoverListsTheJobs()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (rig, status, transfers, clock) = Rig_();
        float seatLeft = 0, seatRight = 0, barRight = 0;
        StorageStatusBarSection? section = null;
        using var frame = UiFrameHarness.Begin(1240, 260);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var bar = new StatusBarManager();
        bar.RegisterSection("source", 10, () => ImGui.TextDisabled("Review source: live"));        // the LEFT end belongs to the Review source segment
        section = StorageStatusBarSection.Register(bar, status, transfers, reserveRight: () => ImGui.CalcTextSize(SeatText).X + ImGui.GetStyle().ItemSpacing.X);
        bar.RegisterSection("seat", 200, () =>                                                         // the seat · hat text, right-aligned exactly as StationSeatStatusBarSection draws it
        {
            float x = ImGui.GetWindowWidth() - ImGui.CalcTextSize(SeatText).X - ImGui.GetStyle().WindowPadding.X;
            if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);
            ImGui.TextDisabled(SeatText);
            (seatLeft, seatRight, barRight) = (ImGui.GetItemRectMin().X, ImGui.GetItemRectMax().X, ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X);
        });
        void Draw() => bar.Render("");
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-storage");
        System.IO.Directory.CreateDirectory(dir);

        var states = new (string Name, StorageJob[] Jobs, StorageSegmentState Expect, bool Bar, bool Button)[]
        {
            ("1-idle", Array.Empty<StorageJob>(), StorageSegmentState.Idle, false, false),
            ("2-archiving-yours", new[] { Job(Ex5, StorageJobKind.Import, StorageJobState.Queued, by: "Debrief-1"), Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 2, 4, 1_288_490_189, 3_328_599_654) }, StorageSegmentState.Active, true, false),
            ("3-archiving-live", new[] { Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 3, 4, 2_000_000_000, 3_328_599_654, giving: true) }, StorageSegmentState.Active, true, false),
            ("4-importing-other", new[] { Job(Ex5, StorageJobKind.Import, StorageJobState.Running, 1, 2, 429_496_730, 1_503_238_553, by: "Debrief-1") }, StorageSegmentState.Active, true, false),
            ("5-ready", new[] { Job(Ex5, StorageJobKind.Import, StorageJobState.Done, 2, 2, 2, 2, by: "Debrief-1") }, StorageSegmentState.Ready, false, true),
            ("6-incomplete", new[] { Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4, reason: "SimHost-1 offline") }, StorageSegmentState.Incomplete, false, true),
        };
        clock.Now = 1000;
        Hear(rig);                                                                                     // primed: the list is empty when this station starts looking
        foreach (var (name, jobs, expect, hasBar, hasButton) in states)
        {
            Hear(rig, jobs);
            frame.StepN(3, Draw);
            Assert.Equal(expect, section.Segment.State);
            if (expect == StorageSegmentState.Idle) { Assert.Equal(default, section.SegmentRect); frame.Screenshot(System.IO.Path.Combine(dir, $"u11-storage-state-{name}.png")); continue; }
            var (min, max) = section.SegmentRect;
            Assert.True(Math.Abs(seatRight - barRight) < 1.5f, $"{name}: the seat text must stay at the bar's right end ({seatRight:0} vs {barRight:0}) — a segment that does not reserve its room pushes it off the bar");
            Assert.True(max.X <= seatLeft - 4f, $"{name}: the segment ends at {max.X:0}, the seat text starts at {seatLeft:0} — it must sit left of it");
            Assert.True(min.X > 300f, $"{name}: the segment starts at {min.X:0} — never the bar's left end (the Review source segment's)");
            Assert.True(Math.Abs(min.Y - seatLeft * 0 - section.SegmentRect.Min.Y) < 0.5f);
            Assert.Equal(hasButton, section.ActionRect != null);
            Assert.Equal(expect == StorageSegmentState.Active && jobs.Length > 1, section.QueuedRect != null);   // "+1 queued" only with a second job waiting
            frame.Screenshot(System.IO.Path.Combine(dir, $"u11-storage-state-{name}.png"));
        }

        // the popover: a click on the segment's words opens it; its rows carry Cancel (active) and Resume (failed)
        Hear(rig, Job(Ex3, StorageJobKind.Archive, StorageJobState.Running, 2, 4, 1_288_490_189, 3_328_599_654), Job(Ex5, StorageJobKind.Import, StorageJobState.Queued, by: "Debrief-1"), Job(Ex2, StorageJobKind.Archive, StorageJobState.Failed, 3, 4, 3, 4, reason: "SimHost-1 offline"));
        frame.StepN(3, Draw);
        var textAt = section.SegmentRect.Min + new Vector2(60, 6);
        frame.Step(() => { Pos(textAt.X, textAt.Y); Draw(); });
        frame.Step(() => { Pos(textAt.X, textAt.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); Draw(); });
        frame.Step(() => { Pos(textAt.X, textAt.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); Draw(); });
        frame.StepN(3, () => { Pos(textAt.X, textAt.Y); Draw(); });
        Assert.True(section.PopoverDrawn, "a click on the segment opens the Transfers popover");
        Assert.Equal(3, section.Rows.Count);
        Assert.Equal(new[] { "Archive · exercise 3a3a3a3a", "Import · exercise 5b5b5b5b", "Archive · exercise 2c2c2c2c" }, section.Rows.Select(r => r.Title));
        Assert.Equal(3, section.RowButtonRects.Count);                                                // Cancel · Cancel (queued) · Resume
        Assert.All(section.RowButtonRects, r => Assert.True(r.Max.X > r.Min.X + 50f));
        Assert.True(Math.Abs(section.RowButtonRects[0].Min.X - section.RowButtonRects[2].Min.X) < 0.5f, "the buttons share one column");
        frame.Screenshot(System.IO.Path.Combine(dir, "u11-storage-popover.png"));
    }
}
