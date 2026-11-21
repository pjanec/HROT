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

    /// <summary>
    /// ⭐ CE-4330 ⑦ (§8 ⑦, R-4174; mockup ⑦) — <i>Delete from archive…</i> is a row action of a run the NAS holds: it asks <i>Delete from the archive</i> through the host's
    /// ONE confirmation slot, and only its button sends <c>StorageIntents.DeleteFromArchive</c> (FromArchive, named by this station). Greyed with the reason on another
    /// seat and while a transfer of the run is queued or running; not offered on a run never archived.
    /// 🔴 Mutations: the button sending a plain Discard (FromArchive false) ⇒ red; the active-job refusal dropped ⇒ red; offered on an unarchived run ⇒ red.
    /// </summary>
    [Fact]
    public void CE4330_DeleteFromArchive_AsksFirst_ThenSendsTheOneDelete_OnTheRightSeats()
    {
        ExerciseInventoryItem Item(Guid ex, ArchiveNasState nas) => new((ExerciseId)ex, new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(12), "Ambush",
                                                                       NasState: nas, NasFiles: 3, NasFilesTotal: 3);
        var rig = With(ClusterState.Idle);
        (string Title, string Text, Hrot.UI.Common.RunMode.RunModeConfirmation.Button Button)? asked = null;
        var seat = Seat.Instructor;
        var transfers = new StorageTransfers(rig.F, () => seat, () => Me, id => { }, confirm: (t, x, b) => asked = (t, x, b));

        Assert.Null(transfers.ActionsFor(Item(Ex3, ArchiveNasState.None)));                 // never archived: nothing to delete from the NAS
        var action = Assert.Single(transfers.ActionsFor(Item(Ex2, ArchiveNasState.Complete))!);
        Assert.Equal(("storage/delete", "Delete from archive…", (string?)null), (action.IconKey, action.Label, action.DisabledReason));
        action.Run();
        Assert.Empty(Sent<ExecuteStorageOpIntent>(rig.Bus));                                // asking deletes nothing
        Assert.Equal("Delete from the archive", asked!.Value.Title);
        Assert.Equal("Delete Ambush · exercise #2c2c2c2c from the NAS?\n3 files. The cached copies on this cluster go too.\nThis cannot be undone — nothing else holds this recording.", asked.Value.Text);
        Assert.Equal("Delete from archive", asked.Value.Button.Label);
        asked.Value.Button.Action!();
        var del = Sent<ExecuteStorageOpIntent>(rig.Bus).Single();
        Assert.Equal((StorageOpType.Delete, (Guid)Ex2, Me, true), (del.Operation, (Guid)del.ExerciseId, del.AskedBy, del.FromArchive));

        Hear(rig, Job(Ex2, StorageJobKind.Import, StorageJobState.Running));
        Assert.Equal("a transfer of this run is queued or running", transfers.ActionsFor(Item(Ex2, ArchiveNasState.Complete))![0].DisabledReason);
        Assert.False(transfers.AskDeleteFromArchive(Item(Ex2, ArchiveNasState.Complete)));
        seat = Seat.Observer;
        Assert.Equal("needs the Author or Instructor seat", transfers.ActionsFor(Item(Ex3, ArchiveNasState.Incomplete))![0].DisabledReason);
        Assert.Null(new StorageTransfers(rig.F, () => Seat.Author, () => Me).ActionsFor(Item(Ex3, ArchiveNasState.Complete)));   // a host that asks no confirmation offers none
    }

    /// <summary>
    /// ⭐⭐ CE-4330 ④ (§5, §5a; mockup ④) — opening a run the NAS holds ASKS the import (named by this station) and waits on it; once the master lists it Done the run
    /// opens BY ITSELF and the dialog closes. An import already running is JOINED (no second ask). An import that had finished before the wait began never opens the
    /// run. <i>Continue in background</i> stops waiting (no cancel); <i>Cancel</i> cancels the job — the Author / Instructor seats only.
    /// 🔴 Mutations: the stale-job guard dropped ⇒ the old Done import opens at once ⇒ red; UpdateImport not opening ⇒ red.
    /// </summary>
    [Fact]
    public void CE4330_OpeningAnArchivedRun_ImportsFirst_ThenOpensByItself()
    {
        ExerciseInventoryItem Item(Guid ex) => new((ExerciseId)ex, new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(22), "Recon", NasState: ArchiveNasState.Complete);
        var rig = With(ClusterState.Idle);
        var opened = new List<Guid>();
        var seat = Seat.Instructor;
        var transfers = new StorageTransfers(rig.F, () => seat, () => Me, opened.Add);

        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Done, 2, 2));               // an import of it that ended long ago
        Assert.True(transfers.OpenOrImport(Item(Ex5)));
        var ask = Sent<ExecuteStorageOpIntent>(rig.Bus).Single();
        Assert.Equal((StorageOpType.Import, (Guid)Ex5, Me), (ask.Operation, (Guid)ask.ExerciseId, ask.AskedBy));
        Assert.Equal("Importing Recon · exercise #5b5b5b5b from the NAS", transfers.ImportTitle);
        transfers.UpdateImport();
        Assert.Empty(opened);                                                                   // ⛔ the old Done import is not this wait's
        Assert.Null(transfers.ImportJob);

        var mine = Job(Ex5, StorageJobKind.Import, StorageJobState.Running, 1, 2, 1, 2);
        Hear(rig, mine);
        transfers.UpdateImport();
        Assert.Equal(mine.JobId, transfers.ImportJob!.JobId);
        Assert.Empty(opened);
        Hear(rig, mine with { State = StorageJobState.Done, FilesDone = 2 });
        transfers.UpdateImport();
        Assert.Equal(new[] { (Guid)Ex5 }, opened);                                              // in: the run opens by itself
        Assert.Null(transfers.Importing);

        // joined: an import another station started — no second ask, the same job
        var theirs = Job(Ex3, StorageJobKind.Import, StorageJobState.Running, 1, 4, by: "Debrief-1");
        Hear(rig, theirs);
        Assert.True(transfers.OpenOrImport(Item(Ex3)));
        Assert.Empty(Sent<ExecuteStorageOpIntent>(rig.Bus));
        Assert.Equal(theirs.JobId, transfers.ImportJob!.JobId);

        transfers.ContinueInBackground();                                                       // the dialog goes, the job does not
        Assert.Null(transfers.Importing);
        Assert.Empty(Sent<CancelOperationIntent>(rig.Bus));

        seat = Seat.Observer;
        transfers.OpenOrImport(Item(Ex3));
        Assert.Equal("needs the Author or Instructor seat", transfers.CancelImportRefusal);
        Assert.False(transfers.CancelImport());
        seat = Seat.Author;
        Assert.True(transfers.CancelImport());
        Assert.Equal(theirs.JobId, Sent<CancelOperationIntent>(rig.Bus).Single().TargetRequestId);
        Assert.Null(transfers.Importing);

        Assert.False(new StorageTransfers(rig.F, () => seat, () => Me).OpenOrImport(Item(Ex3)));   // a host that opens no recording imports nothing
    }

    /// <summary>⭐ CE-4331 ③ (§9a) — an import placed on a DIFFERENT cluster ends Done with the master's note: the run does NOT open by itself; the dialog asks
    /// Start / Cancel with the note — Start opens it, Cancel leaves it imported. An import with no note opens at once (the rail above).
    /// 🔴 Mutation: UpdateImport ignoring the Reason ⇒ the run opens before the question ⇒ red.</summary>
    [Fact]
    public void CE4331_AnImportPlacedOnAnotherCluster_AsksStartOrCancel_WithTheMastersNote()
    {
        var rig = With(ClusterState.Idle);
        var opened = new List<Guid>();
        var transfers = new StorageTransfers(rig.F, () => Seat.Instructor, () => Me, opened.Add);
        var item = new ExerciseInventoryItem((ExerciseId)Ex5, DateTime.UtcNow, TimeSpan.FromMinutes(22), "Recon", NasState: ArchiveNasState.Complete);
        const string note = "not played: cgf-b (CGF) — no node of its kind here: its units replay as the other nodes saw them; their brains are not inspectable";

        transfers.OpenOrImport(item);
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Done, 2, 2, reason: note));
        transfers.UpdateImport();
        Assert.Empty(opened);                                                                   // ⛔ not by itself
        Assert.Equal(note, transfers.PlacementNote);
        Assert.True(transfers.StartAfterPlacement());
        Assert.Equal(new[] { (Guid)Ex5 }, opened);
        Assert.Null(transfers.Importing);

        transfers.OpenOrImport(item);                                                           // the same done job is now stale: a new ask, and Cancel
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Done, 2, 2, reason: note));
        transfers.UpdateImport();
        transfers.ContinueInBackground();
        Assert.Single(opened);
        Assert.False(transfers.StartAfterPlacement());
    }

    /// <summary>⭐ CE-4330 ④ (mockup ④) — the REAL section draws the import dialog while this station waits on an import, and nothing once it went to the background.
    /// Renders <c>ce4330-import-dialog.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git).</summary>
    [SkippableFact]
    public void CE4330_TheImportDialog_IsDrawnWhileWaiting()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (rig, status, transfers, _) = Rig_();
        using var frame = UiFrameHarness.Begin(900, 360);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var bar = new StatusBarManager();
        var section = StorageStatusBarSection.Register(bar, status, transfers);
        Hear(rig, Job(Ex5, StorageJobKind.Import, StorageJobState.Running, 2, 4, 644_245_094, 1_503_238_553, by: "Debrief-1"));
        transfers.OpenOrImport(new ExerciseInventoryItem((ExerciseId)Ex5, DateTime.UtcNow, TimeSpan.FromMinutes(22), "Recon", NasState: ArchiveNasState.Complete));
        frame.StepN(4, () => bar.Render(""));
        Assert.True(section.ImportDialogDrawn);
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-frames");
        System.IO.Directory.CreateDirectory(dir);
        frame.Screenshot(System.IO.Path.Combine(dir, "ce4330-import-dialog.png"));
        transfers.ContinueInBackground();
        frame.StepN(3, () => bar.Render(""));
        Assert.False(section.ImportDialogDrawn);
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
