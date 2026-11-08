using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Presentation.Windows.Timeline;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Common.Infrastructure;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Marks;
using ImGuiNET;
using Xunit;
using ClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests.Marks;

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2, §3) — bookmarks and notes on the client: <i>B</i> takes the mark at once (the existing checkpoint) and only THEN asks for the optional words; the words are ONE add / edit /
/// delete message; an unrecorded Live greys B with the reason; the seat gates it; in an AAR the same key adds a note at a time (no ⚑, no checkpoint); the timeline's corner holds the ⚑ and the S / W toggle MOVED from the toolbar. Over a REAL
/// <see cref="ClusterTimeTransportAdapter"/> on a real bus (<see cref="RunChoiceRig"/>) — ⛔ never a facade that only echoes.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TheMarksTests
{
    private static MarksController Controller(Rig rig, Seat seat = Seat.Instructor, MarkSubject? unit = null, Func<double>? clock = null)
        => new(rig.F, () => seat, () => seat + "-1", () => unit, clock ?? (() => 0));

    private static void Publish(Rig rig, ExerciseNote[]? notes = null, CheckpointMark[]? marks = null, bool active = false, ReplayTimeMap? map = null)
    {
        if (marks != null) rig.Bus.PublishManaged(new ReplaySessionUpdateEvent { Session = new ReplaySession { SessionId = Guid.NewGuid(), Active = active, Checkpoints = marks, TimeMap = map, ZeroWallTicks = 0, LengthSeconds = 600 } });
        if (notes != null) rig.Bus.PublishManaged(new ExerciseNotesUpdateEvent { ExerciseId = (Fdp.Core.ExerciseId)RunId, Notes = notes });
        rig.Bus.SwapBuffers(); rig.View.Update(); rig.F.Update(); rig.Bus.SwapBuffers();
    }

    private static CheckpointMark Cp(string label, double sim, long wall) => new() { Label = label, TotalTime = sim, WallTicks = wall };

    // ── the model ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 Mutation: <c>MarksModel.Rows</c> not joining a note to its mark by label ⇒ the note becomes a second row ⇒ red.</summary>
    [Fact]
    public void E7_TheRows_JoinANoteToItsMark_AndKeepATimeNoteApart_SortedByTheMoment()
    {
        var marks = new[] { Cp("Mark 1 · 3:18", 198, 1980), Cp("Mark 2 · 7:43", 463, 4630) };
        var notes = new[]
        {
            new ExerciseNote(Guid.NewGuid(), "Mark 2 · 7:43", 0, "Bridge crossed", "Alpha 1-2 crossed without cover", "Instructor-1", 7, "Alpha 1-2"),
            new ExerciseNote(Guid.NewGuid(), "", 842, "", "good use of smoke", "Instructor-1", 3, "Alpha 1"),
        };
        var rows = MarksModel.Rows(marks, notes, ReplayTimeMap.Empty);
        Assert.Equal(new[] { "Mark 1 · 3:18", "Bridge crossed", "(note)" }, rows.Select(r => r.Title));
        Assert.Equal(new[] { true, true, false }, rows.Select(r => r.IsMark));
        Assert.Equal(new[] { "3:18", "7:43", "14:02" }, rows.Select(r => r.TimeText));
        Assert.Null(rows[0].Note);                                                  // an unnamed mark: the checkpoint alone
        Assert.Equal("Alpha 1-2", rows[1].Unit);
        Assert.Equal(new[] { "Alpha 1-2", "Alpha 1" }, MarksModel.Units(rows).ToArray());
        Assert.Single(MarksModel.Rows(marks, notes, ReplayTimeMap.Empty, search: "smoke"));
        Assert.Single(MarksModel.Rows(marks, notes, ReplayTimeMap.Empty, unit: "Alpha 1-2"));
    }

    /// <summary>🔴 Mutation: <c>NextMarkLabel</c> not bumping n past a taken label ⇒ a duplicate checkpoint name (the recording refuses it) ⇒ red.</summary>
    [Fact]
    public void E7_TheNextMarkLabel_IsNumbered_AndNeverRepeatsATakenOne()
    {
        Assert.Equal("Mark 1 · 0:05", MarksModel.NextMarkLabel(null, 5));
        var taken = new[] { Cp("Mark 1 · 0:05", 5, 1), Cp("Mark 2 · 0:05", 5, 2) };
        Assert.Equal("Mark 3 · 0:05", MarksModel.NextMarkLabel(taken, 5));
        var odd = new[] { Cp("Mark 2 · 0:05", 5, 1) };                              // one mark, but the next label is already taken
        Assert.Equal("Mark 3 · 0:05", MarksModel.NextMarkLabel(odd, 5));
        Assert.Equal("12:31.4", MarksModel.FormatTenths(751.46));
    }

    // ── B ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>🔴 Mutation: <c>Mark</c> waiting for Done to take the checkpoint ⇒ no checkpoint at the key press ⇒ red. Mutation: <c>Commit</c> sending an Add instead of an Edit of the mark's record ⇒ a second note on the mark ⇒ red.</summary>
    [Fact]
    public void E7_B_InLive_TakesTheMarkAtOnce_WithItsRecord_AndTheWordsAreOneEditAfterDone()
    {
        var rig = With(ClusterState.OperatingLive, clock: 751.4);
        var c = Controller(rig, Seat.Instructor, new MarkSubject(7, "Alpha 1-2"));
        Assert.Null(c.MarkDisabledReason);
        Assert.True(c.Mark());

        var taken = Sent<TakeCheckpointIntent>(rig.Bus);
        Assert.Single(taken);
        Assert.Equal("Mark 1 · 12:31", taken[0].Label);                              // the moment is taken at the key press…
        var record = Assert.Single(rig.Bus.ReadManaged<NoteEditIntent>());           // …with its record (the same swap exposed both): who took it and the selected unit (no words yet)
        Assert.Equal((NoteEditKind.Add, "Mark 1 · 12:31", "Instructor-1", 7L, "", ""), (record.Kind, record.Note.Mark, record.Note.Author, record.Note.SubjectId, record.Note.Name, record.Note.Text));
        Assert.NotNull(c.Popup);
        Assert.Equal("Marked 12:31.4", c.Popup!.Title);
        Assert.Equal((7L, "Alpha 1-2"), (c.Popup.UnitId, c.Popup.UnitName));         // Q5: the selected unit, pre-filled
        Assert.Equal(record.Note.Id, c.Popup.NoteId);

        c.Popup.Name = "Bridge crossed"; c.Popup.Text = "Alpha 1-2 crossed without cover — discuss";
        c.Commit();
        Assert.Null(c.Popup);
        var edit = Assert.Single(Sent<NoteEditIntent>(rig.Bus));
        Assert.Equal((NoteEditKind.Edit, record.Note.Id, "Mark 1 · 12:31", "Bridge crossed"), (edit.Kind, edit.Note.Id, edit.Note.Mark, edit.Note.Name));   // the words EDIT the mark's record
        Assert.Empty(Sent<TakeCheckpointIntent>(rig.Bus));                           // Done takes no second checkpoint
    }

    /// <summary>🔴 Mutation: <c>Commit</c> sending an edit for a fresh mark with nothing typed ⇒ red. Mutation: <c>Tick</c> never closing ⇒ red.</summary>
    [Fact]
    public void E7_IgnoringThePopup_KeepsTheUnnamedMark_AndSendsNoMoreWords()
    {
        double now = 100;
        var rig = With(ClusterState.OperatingLive, clock: 20);
        var c = Controller(rig, clock: () => now);
        c.Mark(); Sent<TakeCheckpointIntent>(rig.Bus);                              // the mark and its record went out at the key press

        now += 3.9; c.Tick(); Assert.NotNull(c.Popup);                              // still open inside 4 s
        now += 0.2; c.Tick(); Assert.Null(c.Popup);                                 // closed by itself at 4 s
        Assert.Empty(Sent<NoteEditIntent>(rig.Bus));

        c.Mark(); Sent<NoteEditIntent>(rig.Bus); c.Popup!.Touched = true;           // touched: it does not time out…
        now += 30; c.Tick(); Assert.NotNull(c.Popup);
        c.Dismiss();                                                                // …Esc closes it
        Assert.Null(c.Popup);
        c.Mark(); Sent<NoteEditIntent>(rig.Bus); c.Commit();                         // Done with nothing typed and the unit unchanged
        Assert.Empty(Sent<NoteEditIntent>(rig.Bus));
    }

    /// <summary>🔴 Mutation: <c>MarkDisabledReason</c> not asking the run's recording ⇒ B marks an unrecorded Live ⇒ red.</summary>
    [Fact]
    public void E7_B_IsGreyedWithTheReason_InAnUnrecordedLive_AnEditSession_AndForASeatThatMayNot()
    {
        var unrecorded = With(ClusterState.OperatingLive, runNotRecorded: true);
        var c = Controller(unrecorded);
        Assert.Equal("no recording — nothing to mark", c.MarkDisabledReason);       // D14, R-4168
        Assert.False(c.Mark());
        Assert.Empty(Sent<TakeCheckpointIntent>(unrecorded.Bus));
        Assert.Null(c.Popup);

        var live = With(ClusterState.OperatingLive);
        foreach (var seat in new[] { Seat.Author, Seat.Instructor, Seat.Observer }) Assert.Null(Controller(live, seat).MarkDisabledReason);
        foreach (var seat in new[] { Seat.Player, Seat.SideController, Seat.Reviewer })
        {
            var why = Controller(live, seat).MarkDisabledReason;
            Assert.Contains($"a {seat} seat cannot add marks or notes", why);       // Q4: not a Player
            Assert.Same(why, Controller(live, seat).MarkDisabledReason is { } w ? why : null);
        }

        var edit = With(ClusterState.OperatingEdit);
        Assert.Equal("marks are taken in a run or added in its review", Controller(edit).MarkDisabledReason);
        Assert.False(Controller(edit).Active);
    }

    /// <summary>🔴 Mutation: <c>Mark</c> in an AAR taking a checkpoint ⇒ a TakeCheckpointIntent in an AAR (R-4168) ⇒ red.</summary>
    [Fact]
    public void E7_InAnAar_TheSameKeyAddsATimeNote_NoCheckpoint()
    {
        var rig = With(ClusterState.OperatingReplay, clock: 842);
        var c = Controller(rig, Seat.Observer, new MarkSubject(3, "Alpha 1"));
        Assert.Null(c.MarkDisabledReason);                                           // the ⚑ is Add note here in a review
        Assert.Equal("Add a note here (at the playhead)", c.MarkTip);
        Assert.True(c.Mark());
        Assert.Empty(Sent<TakeCheckpointIntent>(rig.Bus));
        Assert.Equal("", c.Popup!.Label);
        c.Popup.Text = "good use of smoke";
        c.Commit();
        var add = Sent<NoteEditIntent>(rig.Bus).Single();
        Assert.Equal((NoteEditKind.Add, "", "good use of smoke", "Observer-1"), (add.Kind, add.Note.Mark, add.Note.Text, add.Note.Author));
        Assert.False(add.Note.OnMark);                                               // a time only — not restorable
    }

    /// <summary>🔴 Mutation: <c>Delete</c> sending the delete for a mark with no note (or an edit sending an Add) ⇒ red.</summary>
    [Fact]
    public void E7_EditAndDelete_AreTheOneMessage_AndANoteLessMarkHasNothingToDelete()
    {
        var rig = With(ClusterState.OperatingReplay, clock: 10);
        var note = new ExerciseNote(Guid.NewGuid(), "Mark 1 · 0:10", 0, "Contact east", "Bravo 2-3", "Observer-2");
        Publish(rig, new[] { note }, new[] { Cp("Mark 1 · 0:10", 10, 100), Cp("Mark 2 · 0:20", 20, 200) }, active: true);
        var c = Controller(rig);
        var rows = MarksModel.Rows(c.Checkpoints, c.Notes, c.TimeMap);
        Assert.Equal(2, rows.Count);

        Assert.True(c.Edit(rows[0]));
        c.Popup!.Text = "second thoughts"; c.Commit();
        var edit = Sent<NoteEditIntent>(rig.Bus).Single();
        Assert.Equal((NoteEditKind.Edit, note.Id, "second thoughts"), (edit.Kind, edit.Note.Id, edit.Note.Text));

        Assert.True(c.Delete(rows[0]));
        var del = Sent<NoteEditIntent>(rig.Bus).Single();
        Assert.Equal((NoteEditKind.Delete, note.Id), (del.Kind, del.Note.Id));
        Assert.False(c.Delete(rows[1]));                                             // an unnamed mark: nothing to delete (the checkpoint stays)
        Assert.Empty(Sent<NoteEditIntent>(rig.Bus));

        Assert.True(c.Edit(rows[1]));                                                // …but its words can be written: a NEW note on the mark
        c.Popup!.Name = "late"; c.Commit();
        var add = Sent<NoteEditIntent>(rig.Bus).Single();
        Assert.Equal((NoteEditKind.Add, "Mark 2 · 0:20"), (add.Kind, add.Note.Mark));
    }

    // ── the timeline's cursor and corner ──────────────────────────────────────────────────────────────

    /// <summary>🔴 Mutation: <c>ClusterTimelineCursor.Checkpoints</c> reading only the ACTIVE session ⇒ no ⚑ in Live ⇒ red. Mutation: <c>Notes</c> keying on the wrong label ⇒ red.</summary>
    [Fact]
    public void E7_TheLiveTimeline_DrawsEveryStationsMarks_WithTheirNotes_AndRebuildsOnlyOnAChange()
    {
        var rig = With(ClusterState.OperatingLive);
        var note = new ExerciseNote(Guid.NewGuid(), "Mark 1 · 0:10", 0, "Bridge crossed", "discuss", "Instructor-1", 7, "Alpha 1-2");
        var timeNote = new ExerciseNote(Guid.NewGuid(), "", 300, "", "later", "Observer-2");
        var bareMark = new ExerciseNote(Guid.NewGuid(), "Mark 2 · 0:20", 20, "", "", "Instructor-1", 7, "Alpha 1-2");   // an unnamed mark's record: no words ⇒ no dot
        Publish(rig, new[] { note, timeNote, bareMark }, new[] { Cp("Mark 1 · 0:10", 10, 100), Cp("Mark 2 · 0:20", 20, 200) }, active: false);
        var cursor = new ClusterTimelineCursor(() => rig.F, () => null);

        Assert.Equal(new[] { "Mark 1 · 0:10", "Mark 2 · 0:20" }, cursor.Checkpoints.Select(m => m.Label));   // Live: the master's marks ride the inactive session
        var drawn = cursor.Notes;
        var n = Assert.Single(drawn);                                                // the time note has no moment in Live (no session map): not drawn; the wordless mark's record gets no dot
        Assert.Equal((100L, "Mark 1 · 0:10", "⚑ Bridge crossed · 0:10", "discuss", "Alpha 1-2 · Instructor-1"), (n.WallTicks, n.Mark, n.Title, n.Text, n.Detail));
        Assert.Same(drawn, cursor.Notes);                                            // rebuilt only when the master's list, the marks or the map changed
    }

    /// <summary>🔴 Mutation: the binding not reading the facade's axis / mode ⇒ red. The toolbar no longer draws the toggle (the corner has it).</summary>
    [Fact]
    public void E7_TheCorner_HoldsTheMarkAndTheAxisToggle_TheToolbarsOneRuleMovedThere()
    {
        var rig = With(ClusterState.OperatingLive);
        var c = Controller(rig);
        var corner = new TimelineCornerBinding(c, rig.F);
        Assert.True(corner.HasMark);
        Assert.True(corner.HasAxis);
        Assert.Equal("S", corner.AxisFace);                                          // the master's default axis is Sim
        Assert.True(corner.AxisEnabled);
        corner.OnToggleAxis();
        Assert.Equal(Fdp.Toolkit.Orchestration.ReplayAxis.Wall, Assert.Single(Sent<SetReplayAxisIntent>(rig.Bus)).Axis);   // the ONE toggle: a request to the master, S → W
        Assert.Equal("Sim time — pauses skipped", corner.AxisTip);                  // the axis itself changes when the master says so (the broadcast), not here
        Assert.Equal("Mark this moment (B)", corner.MarkTip);

        var edit = With(ClusterState.OperatingEdit);
        var editCorner = new TimelineCornerBinding(Controller(edit), edit.F);
        Assert.False(editCorner.HasMark);                                            // no marks in Edit
        Assert.False(editCorner.AxisEnabled);                                        // the toggle is greyed outside a session, as the toolbar's was
        editCorner.OnToggleAxis();
        Assert.Empty(Sent<SetReplayAxisIntent>(edit.Bus));
    }

    /// <summary>⭐ HOT PATH — the corner is read by the window and the cursor by the ruler every frame: nothing allocates in a steady frame. 🔴 Mutation: <c>MarkTip</c> concatenating per call ⇒ red.</summary>
    [Fact]
    public void E7_TheCornerAndTheCursor_AllocateNothingInASteadyFrame()
    {
        var rig = With(ClusterState.OperatingLive, runNotRecorded: true);
        Publish(rig, new[] { new ExerciseNote(Guid.NewGuid(), "m", 0, "x") }, new[] { Cp("m", 1, 10) });
        var corner = new TimelineCornerBinding(Controller(rig, Seat.Player), rig.F);   // greyed AND a seat refusal: the worst case for strings
        var cursor = new ClusterTimelineCursor(() => rig.F, () => null);
        void Frame()
        {
            _ = corner.HasMark; _ = corner.MarkDisabledReason; _ = corner.MarkTip; _ = corner.HasAxis; _ = corner.AxisFace; _ = corner.AxisTip; _ = corner.AxisEnabled;
            _ = cursor.Checkpoints.Count; _ = cursor.Notes.Count;
        }
        for (int i = 0; i < 5; i++) Frame();                                         // warm: the caches fill
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) Frame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── a frame: the real window, the real corner, the real list ─────────────────────────────────────────

    private sealed class FrameSource : ITimelineSource
    {
        public TimelineIndex Index { get; } = TimelineIndex.Empty;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    /// <summary>Moves the pointer for the NEXT frame: ImGui's event AND the window's real pointer — rlImGui feeds Raylib's pointer into ImGui every frame and would overwrite the injected position.</summary>
    private static void Pos(float x, float y) { ImGui.GetIO().AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    private static void Click(UiFrameSession frame, Action draw, Vector2 at)
    {
        frame.Step(() => { Pos(at.X, at.Y); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, true); draw(); });
        frame.Step(() => { Pos(at.X, at.Y); ImGui.GetIO().AddMouseButtonEvent(0, false); draw(); });
        frame.Step(() => { Pos(5, 5); draw(); });   // the release lands one frame after it was injected
        frame.Step(draw);
    }

    /// <summary>
    /// ⭐ The Event Timeline's title-bar corner is DRAWN and PRESSABLE: ⚑ then S / W, left of the pin, on the title bar's row; the S / W press flips the session's axis and the ⚑ takes a mark (Live) — a frame of the REAL window over the real adapter.
    /// 🔴 Mutation: <c>DrawTitleBarCorner</c> not called from <c>ManagedWindow.Render</c> ⇒ no corner ⇒ red.
    /// </summary>
    [SkippableFact]
    public void E7_TheEventTimelinesCorner_IsDrawnInTheTitleBar_AndItsIconsWork()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var rig = With(ClusterState.OperatingLive, clock: 30);
        var marks = Controller(rig, Seat.Instructor);
        var window = new EventTimelineWindow("rail_marks_corner_" + Guid.NewGuid().ToString("N"), "Event Timeline", "Scenario", new TimelinePanel(), () => new FrameSource(),
            new ClusterTimelineCursor(() => rig.F, () => null), () => Array.Empty<TimelineLaneDescriptor>(), Vector4.One,
            corner: new TimelineCornerBinding(marks, rig.F)) { IsOpen = true };
        var atlas = new IconAtlas(IntPtr.Zero, 512, 512);
        using var frame = UiFrameHarness.Begin(1400, 400);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw() { ImGui.SetNextWindowPos(Vector2.Zero); window.Render("Scenario", atlas); }
        frame.StepN(4, Draw);

        var at = window.LastCornerAt;
        Assert.NotNull(at);
        var (mark, axis) = at!.Value;
        Assert.True(mark.X < axis.X, $"the ⚑ ({mark.X:0}) is left of the S / W ({axis.X:0}) — the mockup's order");
        Assert.InRange(mark.Y - axis.Y, -0.5f, 0.5f);                                // one row
        Assert.InRange(axis.Y, 0f, 30f);                                             // in the title bar, not in the content
        Assert.True(axis.X > 1400f * 0.5f && axis.X < 1400f, "the top right of the window");

        Click(frame, Draw, axis);
        Assert.Equal(Fdp.Toolkit.Orchestration.ReplayAxis.Wall, Assert.Single(Sent<SetReplayAxisIntent>(rig.Bus)).Axis);   // the axis toggle works from the corner
        Click(frame, Draw, mark);
        var taken = Sent<TakeCheckpointIntent>(rig.Bus);
        Assert.Single(taken);                                                        // the ⚑ takes the mark
        Assert.NotNull(marks.Popup);
    }

    /// <summary>
    /// ⭐ The LIVE timeline draws every mark as a ⚑ on the ruler (the master's checkpoints on the inactive session), an amber dot beside the ones with words, and the note's words in the tooltip on hover
    /// (name · time / the words / unit · who) — the mockup's ①. Renders <c>e7-timeline-live.png</c> under <c>HROT_RAIL_SHOTS</c>. 🔴 Mutation: <c>TimelinePanel</c> not drawing the dot / the note tooltip ⇒ the pixel and tooltip asserts red.
    /// </summary>
    [SkippableFact]
    public void E7_TheLiveTimeline_DrawsTheFlagsAndTheNoteDots_AndTheTooltipSaysTheWords()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        long t0 = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc).Ticks, tick = TimeSpan.TicksPerSecond / 10;
        const int frames = 18 * 60 * 10;
        var index = new TimelineIndex(new Dictionary<string, TimelineEvent[]>(), Enumerable.Range(0, frames).Select(f => t0 + f * tick).ToArray(), frames, frames);
        var source = new IndexSource(index);
        var rig = With(ClusterState.OperatingLive, clock: 1080);
        long Wall(double sec) => t0 + (long)(sec * TimeSpan.TicksPerSecond);
        var note = new ExerciseNote(Guid.NewGuid(), "Bridge crossed · 7:43", 0, "Bridge crossed", "Alpha 1-2 crossed without cover — discuss", "Instructor-1", 7, "Alpha 1-2");
        Publish(rig, new[] { note }, new[] { Cp("Mark 1 · 3:18", 198, Wall(198)), Cp("Bridge crossed · 7:43", 463, Wall(463)), Cp("Mark 3 · 10:12", 612, Wall(612)) }, active: false);
        var marks = Controller(rig);
        var panel = new TimelinePanel();
        var window = new EventTimelineWindow("rail_marks_live_" + Guid.NewGuid().ToString("N"), "Event Timeline", "Scenario", panel, () => source,
            new ClusterTimelineCursor(() => rig.F, () => source), () => Array.Empty<TimelineLaneDescriptor>(), new Vector4(0.15f, 0.33f, 0.60f, 1f),
            corner: new TimelineCornerBinding(marks, rig.F)) { IsOpen = true };
        var atlas = new IconAtlas(IntPtr.Zero, 1240, 300);
        using var frame = UiFrameHarness.Begin(1240, 300);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw() { ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1240, 300)); window.Render("Scenario", atlas); }
        frame.StepN(5, Draw);

        var ruler = panel.LastRulerRect;
        float width = ruler.Max.X - ruler.Min.X;
        Vector2 FlagAt(double sec) => new(ruler.Min.X + panel.Window.XOf(Wall(sec), width), ruler.Min.Y + 8f);

        // the ⚑ pixels (blue) over the ruler: three marks
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-marks");
        System.IO.Directory.CreateDirectory(dir);
        string shot = System.IO.Path.Combine(dir, "e7-timeline-live.png");
        frame.Screenshot(shot);
        var image = Raylib_cs.Raylib.LoadImage(shot);
        try
        {
            int Count(Func<Raylib_cs.Color, bool> hit, Vector2 around)
            {
                int n = 0;
                for (int y = (int)ruler.Min.Y - 2; y < (int)ruler.Max.Y + 4; y++)
                    for (int x = (int)around.X - 12; x <= (int)around.X + 12; x++) { if (hit(Raylib_cs.Raylib.GetImageColor(image, x, y))) n++; }
                return n;
            }
            bool Blue(Raylib_cs.Color c) => c.B > 200 && c.R < 120 && c.G > 150;                              // the mark's ⚑: 0xFFF0C060 ABGR = R 0x60, G 0xC0, B 0xF0 (the mockup's blue)
            bool Amber(Raylib_cs.Color c) => c.R > 220 && c.G is > 150 and < 200 && c.B < 80;                 // the note's dot: 0xFF30B0F0 ABGR = R 0xF0, G 0xB0, B 0x30
            foreach (var sec in new[] { 198d, 463d, 612d }) Assert.True(Count(Blue, FlagAt(sec)) > 12, $"a blue ⚑ at {sec} s");
            Assert.True(Count(Amber, FlagAt(463)) > 8, "the note's amber dot beside the 7:43 flag");
            Assert.True(Count(Amber, FlagAt(198)) == 0 && Count(Amber, FlagAt(612)) == 0, "an unnamed mark has no dot");
        }
        finally { Raylib_cs.Raylib.UnloadImage(image); }

        // hover the 7:43 flag: the tooltip is the note's three lines — it draws (a screenshot) and the window answers hover with the mark's time
        var at = FlagAt(463);
        frame.Step(() => { Pos(at.X, at.Y); Draw(); });
        frame.StepN(3, () => { Pos(at.X, at.Y); Draw(); });
        frame.Screenshot(System.IO.Path.Combine(dir, "e7-timeline-live-tooltip.png"));
        Assert.Equal(("⚑ Bridge crossed · 7:43", "Alpha 1-2 crossed without cover — discuss"), (new ClusterTimelineCursor(() => rig.F, () => source).Notes[0].Title, new ClusterTimelineCursor(() => rig.F, () => source).Notes[0].Text));
    }

    private sealed class IndexSource : ITimelineSource
    {
        public IndexSource(TimelineIndex index) => Index = index;
        public TimelineIndex Index { get; }
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    /// <summary>
    /// ⭐ The Marks window DRAWS the exercise's marks and notes (⚑ blue for a checkpoint, the amber note for a time note), clicking a row seeks and selects its unit, right-click edits; the popup draws the fields in one column with Done.
    /// Renders <c>e7-marks-window.png</c> / <c>e7-mark-popup.png</c> under <c>HROT_RAIL_SHOTS</c> (no PNG in git). 🔴 Mutation: <c>MarksWindow.Activate</c> not seeking ⇒ red.
    /// </summary>
    [SkippableFact]
    public void E7_TheMarksWindowAndThePopup_DrawTheMarks_AClickSeeks_AndTheFieldsAlign()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var rig = With(ClusterState.OperatingReplay, clock: 842);
        var notes = new[]
        {
            new ExerciseNote(Guid.NewGuid(), "Bridge crossed · 7:43", 0, "Bridge crossed", "Alpha 1-2 crossed without cover — discuss", "Instructor-1", 7, "Alpha 1-2"),
            new ExerciseNote(Guid.NewGuid(), "", 842, "", "good use of smoke", "Instructor-1 · in the AAR", 3, "Alpha 1"),
        };
        var map = ReplayTimeMap.FromFrames(new long[] { 0, 10_000_000L * 1000 }, new[] { 0d, 1000d }, new FrameFlags[2]);
        Publish(rig, notes, new[] { Cp("Mark 1 · 3:18", 198, 1_980_000_000), Cp("Bridge crossed · 7:43", 463, 4_630_000_000), Cp("Mark 3 · 10:12", 612, 6_120_000_000) }, active: true, map: map);
        var marks = Controller(rig, Seat.Instructor, new MarkSubject(7, "Alpha 1-2"));
        var seeks = new List<long>(); var selected = new List<long>();
        var cursor = new RecordingCursor(seeks);
        var window = new MarksWindow("rail_marks_" + Guid.NewGuid().ToString("N"), "Scenario", Vector4.One, marks, () => cursor, id => selected.Add(id)) { IsOpen = true };
        var popup = new MarkPopupWindow("rail_marks_popup_" + Guid.NewGuid().ToString("N"), "Scenario", Vector4.One, marks, () => new[] { new MarkSubject(7, "Alpha 1-2") });
        var atlas = new IconAtlas(IntPtr.Zero, 512, 512);
        using var frame = UiFrameHarness.Begin(1240, 720);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw() { window.Render("Scenario", atlas); popup.Render("Scenario", atlas); }
        frame.StepN(5, Draw);

        var vm = window.SimulateDrawClientArea();
        Assert.Equal(4, vm.Rows.Count);                                              // 3 marks (one with its note) + 1 AAR note
        Assert.Contains(vm.Rows, r => r.StartsWith("flag 7:43 | Bridge crossed | Alpha 1-2 crossed without cover — discuss | Alpha 1-2 | Instructor-1"));
        Assert.Contains(vm.Rows, r => r.StartsWith("note 14:02 | (note) | good use of smoke | Alpha 1 |"));
        Assert.Equal(4, window.LastRowRects.Count);
        Assert.All(window.LastRowRects, r => Assert.True(r.Max.X > r.Min.X + 300f, "a row spans the window"));
        Assert.Null(window.AddNoteDisabledReason);                                   // in an AAR the note is added at a time

        Click(frame, Draw, (window.LastRowRects[1].Min + window.LastRowRects[1].Max) / 2f);   // the 7:43 row
        Assert.True(seeks.Count == 1, $"the click on row 1 ({window.LastRowRects[1].Min}..{window.LastRowRects[1].Max}) seeked {seeks.Count} times");
        Assert.Equal(new long[] { 4_630_000_000 }, seeks);                           // seek there…
        Assert.Equal(new long[] { 7 }, selected);                                    // …and select its unit

        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hrot-marks");
        System.IO.Directory.CreateDirectory(dir);
        frame.Screenshot(System.IO.Path.Combine(dir, "e7-marks-window.png"));

        Click(frame, Draw, (window.LastAddNoteRect!.Value.Min + window.LastAddNoteRect.Value.Max) / 2f);   // Add note here opens the popup
        frame.StepN(3, Draw);
        Assert.NotNull(marks.Popup);
        var (nm, nt, un) = popup.LastFieldMins!.Value;
        Assert.InRange(nt.X - nm.X, -0.5f, 0.5f);                                    // Name and Note start at one x (the mockup's aligned controls)
        Assert.InRange(un.X - nm.X, -0.5f, 0.5f);
        Assert.True(nt.Y > nm.Y && un.Y > nt.Y, "Name, Note, Unit top to bottom");
        frame.Screenshot(System.IO.Path.Combine(dir, "e7-mark-popup.png"));
        Click(frame, Draw, (popup.LastDoneRect!.Value.Min + popup.LastDoneRect.Value.Max) / 2f);
        Assert.Null(marks.Popup);                                                    // Done closed it (and sent the note)
        Assert.Single(Sent<NoteEditIntent>(rig.Bus));
    }

    private sealed class RecordingCursor : ITimelineCursor
    {
        private readonly List<long> _seeks;
        public RecordingCursor(List<long> seeks) => _seeks = seeks;
        public long ExtentFrom => 0; public long ExtentTo => 10_000_000_000; public long Now => 0; public bool CanSeek => true;
        public void Seek(long ticks) => _seeks.Add(ticks);
    }
}
