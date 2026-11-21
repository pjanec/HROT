using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Network.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — the master's ONE note list, driven through the ONE CRUD message on a live bus-mode master: applied in order, saved at once to the
/// master's staging, published whole on every change, written back to the NAS on leaving, loaded back on entering; and the wire's twins (the op value, the payload).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterNotesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "master-notes-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private (ClusterMaster Master, FdpEventBus Bus, Guid Exercise) Live()
    {
        const int NId = 42;
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var cfg = TestClusterConfig.NothingMandatory() with { StagingRoot = Path.Combine(_root, "staging"), NasBasePath = Path.Combine(_root, "nas") };
        var master = new ClusterMaster(bus, cfg);
        bus.PublishManaged(new NodeHeartbeatEvent { NodeId = (Fdp.Core.NodeId)NId, LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks, SubsystemName = "SimHost" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        var exercise = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { TargetState = ClusterState.OperatingLive.ToString(), ExerciseId = exercise }),
        });
        for (int round = 0; round < 10 && master.CurrentClusterState != ClusterState.OperatingLive; round++)
        {
            bus.SwapBuffers();
            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.TargetNodeId == NId && i.Operation != FdpNodeOpType.CommitState))
                bus.PublishManaged(new NodeOpCompletedEvent { TransactionId = intent.TransactionId, Operation = intent.Operation, NodeId = (Fdp.Core.NodeId)NId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
            bus.SwapBuffers(); master.Tick();
        }
        bus.SwapBuffers(); master.Tick();   // the list follows the exercise on show
        return (master, bus, master.ActiveExerciseId);
    }

    private static void Send(ClusterMaster master, FdpEventBus bus, NoteEditKind kind, ExerciseNote note)
    {
        bus.PublishManaged(new NoteEditIntent { RequestId = Guid.NewGuid(), Kind = kind, Note = note });
        bus.SwapBuffers(); master.Tick();
    }

    /// <summary>🔴 Mutation: <c>PublishNotes</c> not called after an edit ⇒ no <c>ExerciseNotesPublishedEvent</c> ⇒ red.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4325_AnAddedNote_IsSavedAtOnce_AndThePublishedListIsTheWholeList()
    {
        var (master, bus, ex) = Live();
        Assert.NotEqual(Guid.Empty, ex);
        Assert.Equal(ex, master.NotesExerciseId);
        var note = new ExerciseNote(Guid.NewGuid(), "Mark 1 · 0:05", 0, "Bridge crossed", "discuss", "Instructor-1", 9, "Alpha 1-2");
        Send(master, bus, NoteEditKind.Add, note);

        Assert.Single(master.Notes);
        Assert.True(File.Exists(master.NotesStagingFile(ex)));                      // saved at once, not on leaving
        bus.SwapBuffers();
        var published = bus.ReadManaged<ExerciseNotesPublishedEvent>().ToList();
        Assert.Contains(published, e => (Guid)e.ExerciseId == ex && e.Notes.Length == 1 && e.Notes[0].Name == "Bridge crossed");

        Send(master, bus, NoteEditKind.Edit, note with { Text = "second thoughts" });
        Assert.Equal("second thoughts", master.Notes[0].Text);
        Send(master, bus, NoteEditKind.Delete, note);
        Assert.Empty(master.Notes);
        var saved = new Fdp.Toolkit.Orchestration.ExerciseNotes();
        Assert.True(saved.TryLoad(master.NotesStagingFile(ex), (Fdp.Core.ExerciseId)ex));                 // the staging copy followed every change
        Assert.Empty(saved.Notes);
    }

    /// <summary>🔴 Mutation: <c>WriteBackNotes</c> not called on leaving ⇒ no NAS copy ⇒ red.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4325_LeavingTheExercise_WritesNotesJsonBackToTheNas_AndEnteringItAgainLoadsIt()
    {
        var (master, bus, ex) = Live();
        var note = new ExerciseNote(Guid.NewGuid(), "", 12.5, "(AAR)", "good use of smoke", "Observer-2");
        Send(master, bus, NoteEditKind.Add, note);
        Assert.False(File.Exists(master.NotesNasFile(ex)));                         // exported on leaving, not before

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { TargetState = ClusterState.Idle.ToString() }),
        });
        for (int round = 0; round < 10 && master.CurrentClusterState != ClusterState.Idle; round++)
        {
            bus.SwapBuffers();
            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState))
                bus.PublishManaged(new NodeOpCompletedEvent { TransactionId = intent.TransactionId, Operation = intent.Operation, NodeId = (Fdp.Core.NodeId)intent.TargetNodeId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
            bus.SwapBuffers(); master.Tick();
        }
        bus.SwapBuffers(); master.Tick();
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);
        Assert.True(File.Exists(master.NotesNasFile(ex)));                          // written back
        Assert.Empty(master.Notes);                                                 // nothing on show
        Assert.Equal(Guid.Empty, master.NotesExerciseId);

        // the NAS copy is the whole list, readable by anything (an AAR's master loads it when the exercise comes on show)
        File.Delete(master.NotesStagingFile(ex));
        var loaded = new Fdp.Toolkit.Orchestration.ExerciseNotes();
        Assert.True(loaded.TryLoad(master.NotesNasFile(ex), (Fdp.Core.ExerciseId)ex));
        Assert.Equal("good use of smoke", loaded.Notes[0].Text);
    }

    private static void LeaveToIdle(ClusterMaster master, FdpEventBus bus)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { TargetState = ClusterState.Idle.ToString() }),
        });
        for (int round = 0; round < 10 && master.CurrentClusterState != ClusterState.Idle; round++)
        {
            bus.SwapBuffers();
            foreach (var intent in bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState))
                bus.PublishManaged(new NodeOpCompletedEvent { TransactionId = intent.TransactionId, Operation = intent.Operation, NodeId = (Fdp.Core.NodeId)intent.TargetNodeId, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
            bus.SwapBuffers(); master.Tick();
        }
        bus.SwapBuffers(); master.Tick();
        Assert.Equal(ClusterState.Idle, master.CurrentClusterState);
    }

    /// <summary>
    /// ⭐ CE-1467 (e) — two stations press B in the same moment and ask for the SAME label: the master (the one writer) makes the second unique and each mark's record rides the label
    /// actually used; a later edit carrying the label the station guessed keeps the master's. Both entries: the bus intent and the wire payload (<c>TakeCheckpointPayloadDto</c>).
    /// 🔴 Mutation: <c>UniqueCheckpointLabel</c> returning what was asked ⇒ both records on one label ⇒ red. Mutation: <c>KeepMoment</c> not applied ⇒ the edit moves the record ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE1467_TwoStationsMarkingAtOnce_GetTwoLabels_AndEachRecordRidesItsOwn()
    {
        var (master, bus, _) = Live();
        const string asked = "Mark 1 · 0:05";
        var a = new ExerciseNote(Guid.NewGuid(), asked, 5, "", "", "Instructor-1", 7, "Alpha 1-2");
        var b = new ExerciseNote(Guid.NewGuid(), asked, 5, "", "", "Observer-2");
        bus.PublishManaged(new TakeCheckpointIntent { RequestId = Guid.NewGuid(), Label = asked, Note = a });
        bus.PublishManaged(new TakeCheckpointIntent { RequestId = Guid.NewGuid(), Label = asked, Note = b });
        bus.SwapBuffers(); master.Tick();

        Assert.Equal(2, master.Notes.Length);
        Assert.Equal(asked, master.Notes.Single(n => n.Id == a.Id).Mark);
        Assert.Equal(asked + " (2)", master.Notes.Single(n => n.Id == b.Id).Mark);   // minted, not refused by the recording

        Send(master, bus, NoteEditKind.Edit, b with { Name = "Smoke", Mark = asked });   // the station's popup still holds the label it asked for
        Assert.Equal((asked + " (2)", "Smoke"), (master.Notes.Single(n => n.Id == b.Id).Mark, master.Notes.Single(n => n.Id == b.Id).Name));

        // the wire: a station's TakeCheckpoint carries the note in its payload ({"label":…,"note":…}); a plain checkpoint stays {"label":…}
        var c = new ExerciseNote(Guid.NewGuid(), asked, 6, "", "", "Instructor-3");
        string json = TakeCheckpointPayloadDto.Encode(asked, c);
        Assert.Null(TakeCheckpointPayloadDto.NoteOf("{\"label\":\"x\"}"));
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TakeCheckpoint, PayloadJson = json });
        master.Tick();
        Assert.Equal(asked + " (3)", master.Notes.Single(n => n.Id == c.Id).Mark);
    }

    /// <summary>
    /// ⭐ CE-1467 (b) — an edit leaves a marker beside the staging copy until the NAS has it; a master that died before writing back (here: a second master over the same roots) writes it
    /// back at its start and clears the marker; leaving normally clears it too.
    /// 🔴 Mutation: <c>RetryPendingNotesWriteBacks</c> not run at the start ⇒ no NAS copy ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE1467_NotesTheNasNeverGot_AreWrittenBackAtTheNextMasterStart()
    {
        var (master, bus, ex) = Live();
        Send(master, bus, NoteEditKind.Add, new ExerciseNote(Guid.NewGuid(), "", 12, "Ambush", "too early", "Observer-2"));
        Assert.True(File.Exists(master.NotesPendingFile(ex)));                     // the NAS copy is behind
        Assert.False(File.Exists(master.NotesNasFile(ex)));

        // the master dies here; the next one starts over the same staging and NAS
        var bus2 = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus2);
        var next = new ClusterMaster(bus2, TestClusterConfig.NothingMandatory() with { StagingRoot = Path.Combine(_root, "staging"), NasBasePath = Path.Combine(_root, "nas") });
        bus2.SwapBuffers(); next.Tick();
        Assert.True(File.Exists(next.NotesNasFile(ex)));
        Assert.False(File.Exists(next.NotesPendingFile(ex)));
        var loaded = new Fdp.Toolkit.Orchestration.ExerciseNotes();
        Assert.True(loaded.TryLoad(next.NotesNasFile(ex), (Fdp.Core.ExerciseId)ex));
        Assert.Equal("too early", Assert.Single(loaded.Notes).Text);

        Send(master, bus, NoteEditKind.Add, new ExerciseNote(Guid.NewGuid(), "", 13, "Second"));
        LeaveToIdle(master, bus);                                                   // the normal path: written back on leaving, marker gone
        Assert.False(File.Exists(master.NotesPendingFile(ex)));
    }

    /// <summary>
    /// ⭐ CE-1467 (c) — <i>Discard</i> of an unarchived run takes its notes too: the staging copy and the NAS copy the run's end wrote, with that NAS folder when the notes were all it held.
    /// ⛔ A NAS folder holding anything else keeps it. 🔴 Mutation: <c>DiscardNotes</c> not called ⇒ the NAS notes outlive the recording ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE1467_DiscardingAnUnarchivedRun_TakesItsNotes_AndOnlyAnEmptyNasFolder()
    {
        var (master, bus, ex) = Live();
        Send(master, bus, NoteEditKind.Add, new ExerciseNote(Guid.NewGuid(), "", 12, "Ambush"));
        LeaveToIdle(master, bus);
        Assert.True(File.Exists(master.NotesNasFile(ex)));                          // the run's end wrote it (before the keep / discard answer)
        master.ExerciseDeleter = _ => true;

        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.DeleteExercise, PayloadJson = $"{{\"ExerciseId\":\"{ex}\"}}" });
        master.Tick();
        Assert.False(File.Exists(master.NotesNasFile(ex)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(master.NotesNasFile(ex))));   // it held only the notes
        Assert.False(File.Exists(master.NotesStagingFile(ex)));

        // a NAS folder with anything else in it keeps its other files
        var other = Guid.NewGuid();
        var folder = Path.GetDirectoryName(master.NotesNasFile(other))!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(master.NotesNasFile(other), "{}");
        File.WriteAllText(Path.Combine(folder, "node-1.fdp"), "x");
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.DeleteExercise, PayloadJson = $"{{\"ExerciseId\":\"{other}\"}}" });
        master.Tick();
        Assert.True(File.Exists(Path.Combine(folder, "node-1.fdp")));
    }

    /// <summary>
    /// ⭐ CE-1467 (d) — a branch from an AAR keeps the notes of the marks BEFORE the branch point (the nodes' <see cref="LiveBranchResult.BranchWallTicks"/>), not every mark the replay held.
    /// The inherited list is seeded as the inheritance leaves it when the point is not known yet; the point then arrives on the bus as the nodes' answer.
    /// 🔴 Mutation: the cut ignoring the point (all marks kept) ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE1467_ABranchFromAnAar_KeepsOnlyTheNotesOfTheMarksBeforeTheBranchPoint()
    {
        var (master, bus, ex) = Live();
        Send(master, bus, NoteEditKind.Add, new ExerciseNote(Guid.NewGuid(), "Mark 1 · 0:10", 10, "before"));
        Send(master, bus, NoteEditKind.Add, new ExerciseNote(Guid.NewGuid(), "Mark 2 · 0:40", 40, "after"));
        master.SeedBranchMarksForTests(new System.Collections.Generic.Dictionary<string, long> { ["Mark 1 · 0:10"] = 1_000, ["Mark 2 · 0:40"] = 4_000 });

        bus.PublishManaged(new ClusterOpCompletedEvent
        {
            RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
            ResultPayload = new LiveBranchResult(default, true, (Fdp.Core.ExerciseId)Guid.NewGuid(), BranchWallTicks: 2_500),
        });
        bus.SwapBuffers(); master.Tick();
        Assert.Equal("before", Assert.Single(master.Notes).Name);
        var saved = new Fdp.Toolkit.Orchestration.ExerciseNotes();
        Assert.True(saved.TryLoad(master.NotesStagingFile(ex), (Fdp.Core.ExerciseId)ex));
        Assert.Single(saved.Notes);
    }

    /// <summary>🔴 Mutation: no refusal when nothing is on show ⇒ a note into the void ⇒ red.</summary>
    [Fact(Timeout = 10_000)]
    public void CE4325_ANoteWithNoExerciseOnShow_IsRejected()
    {
        var bus = new FdpEventBus();
        OrchestrationEventRegistry.RegisterAll(bus);
        var master = new ClusterMaster(bus, TestClusterConfig.NothingMandatory() with { StagingRoot = Path.Combine(_root, "s2"), NasBasePath = Path.Combine(_root, "n2") });
        var id = Guid.NewGuid();
        bus.PublishManaged(new NoteEditIntent { RequestId = id, Kind = NoteEditKind.Add, Note = new ExerciseNote(Guid.NewGuid(), "m", 0, "x") });
        bus.SwapBuffers(); master.Tick();
        Assert.Empty(master.Notes);
        Assert.False(Directory.Exists(Path.Combine(_root, "s2")));
    }

    /// <summary>🔴 Mutation: the op value moved or the payload lost a field ⇒ red (the wire twin of the engine enum and the ONE encoding).</summary>
    [Fact]
    public void CE4325_TheCrudMessage_HasOneWireValue_AndItsPayloadRoundTrips()
    {
        Assert.Equal((int)Fdp.Toolkit.Orchestration.ClusterOpType.EditExerciseNote, (int)ClusterOpType.EditExerciseNote);
        Assert.Equal(28, (int)ClusterOpType.EditExerciseNote);
        var note = new ExerciseNote(Guid.NewGuid(), "Mark 2 · 7:43", 0, "Bridge crossed", "Alpha 1-2 crossed without cover — discuss", "Instructor-1", 4711, "Alpha 1-2", new DateTime(2026, 11, 8, 12, 31, 24, DateTimeKind.Utc));
        var intent = new NoteEditIntent { RequestId = Guid.NewGuid(), Kind = NoteEditKind.Edit, Note = note };
        var json = System.Text.Json.JsonSerializer.Serialize(NoteEditPayloadDto.From(intent), OrchestrationJsonOptions.Default);
        var back = NoteEditPayloadDto.Decode(intent.RequestId, json);
        Assert.Equal(intent.RequestId, back.RequestId);
        Assert.Equal(NoteEditKind.Edit, back.Kind);
        Assert.Equal(note, back.Note);
        Assert.Null(NoteEditPayloadDto.Decode(Guid.NewGuid(), "not json").Note);   // unreadable ⇒ no note ⇒ the master refuses it
    }

    /// <summary>🔴 Mutation: <c>ToTopic</c> / <c>FromTopic</c> disagreeing on the exercise or the list ⇒ red.</summary>
    [Fact]
    public void CE4325_TheTopicCarriesTheExerciseAndTheWholeList()
    {
        var ex = (Fdp.Core.ExerciseId)Guid.NewGuid();
        var notes = new[] { new ExerciseNote(Guid.NewGuid(), "m1", 0, "a"), new ExerciseNote(Guid.NewGuid(), "", 5.5, "b") };
        var topic = ExerciseNotesWire.ToTopic(ex, notes);
        Assert.Equal(0, topic.NodeId);
        Assert.Equal((Guid)ex, topic.ExerciseId);
        Assert.Equal(notes, ExerciseNotesWire.FromTopic(topic));
        Assert.Null(ExerciseNotesWire.FromTopic(new ExerciseNotesTopic { NotesJson = "{" }));
    }
}
