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
