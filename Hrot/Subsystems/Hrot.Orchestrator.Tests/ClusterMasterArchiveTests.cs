using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType    = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Integration tests for the ExportArchive / ImportArchive / CancelOperation
/// branches in <see cref="ClusterMaster"/> (CGF1-S0505 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterArchiveTests
{

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    // ── Reflection helper to read _activeCancellations ────────────────────────

    private static Dictionary<Guid, CancellationTokenSource> GetActiveCancellations(ClusterMaster dm)
    {
        var field = typeof(ClusterMaster).GetField(
            "_activeCancellations",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Dictionary<Guid, CancellationTokenSource>)field.GetValue(dm)!;
    }

    // ── CGF1-S0505 Success Condition 4: CancelOperation kills CTS ────────────

    /// <summary>
    /// Posting an <see cref="ClusterOpType.ExportArchive"/> request followed by a
    /// <see cref="ClusterOpType.CancelOperation"/> referencing the same RequestId must
    /// cancel the <see cref="CancellationTokenSource"/> that was registered for the
    /// export operation.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CancelOperation_CancelsActiveCts()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        // Register a fake node so FanOutSerializeLocal actually queues an ACK
        // (otherwise the 0-node path completes synchronously and removes the CTS).
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = (Fdp.Core.NodeId)(1),
            SubsystemName = "TestNode",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick();  // ingest heartbeat into roster
        bus.SwapBuffers();

        var exportRequestId = Guid.NewGuid();
        var exerciseId      = Guid.NewGuid();

        // 1. Post ExportArchive — creates CTS in _activeCancellations.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = exportRequestId,
            OperationType = ClusterOpType.ExportArchive,
            PayloadJson   = "{\"ExerciseId\":\"00000000-0000-0000-0000-000000000099\"}",
        });
        exercise.Tick();  // drain injected requests

        // 2. Capture the CTS before CancelOperation removes it.
        var activeCancellations = GetActiveCancellations(exercise);
        Assert.True(activeCancellations.TryGetValue(exportRequestId, out var cts),
            "ExportArchive must register a CTS in _activeCancellations.");
        Assert.False(cts!.IsCancellationRequested,
            "CTS must NOT be cancelled before CancelOperation is sent.");

        // 3. Post CancelOperation referencing the export's RequestId.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.CancelOperation,
            PayloadJson   = exportRequestId.ToString(),
        });
        exercise.Tick();  // drain injected requests

        // 4. The CTS must now be in the cancelled state.
        Assert.True(cts.IsCancellationRequested,
            "CancelOperation must cancel the CTS registered for the ExportArchive operation.");
    }

    /// <summary>
    /// An <see cref="ClusterOpType.ExportArchive"/> request with a missing ExerciseId must
    /// be rejected immediately (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ExportArchive_MissingExerciseId_IsRejected()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.ExportArchive,
            PayloadJson   = "{\"WrongKey\":\"something\"}",
        });
        exercise.Tick();

        // No CTS should be registered for a rejected request.
        var activeCancellations = GetActiveCancellations(exercise);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ExportArchive must not register a CTS.");
    }

    /// <summary>
    /// An <see cref="ClusterOpType.ImportArchive"/> request with a missing ExerciseId must
    /// be rejected (no CTS added to _activeCancellations).
    /// </summary>
    [Fact]
    public void ImportArchive_MissingExerciseId_IsRejected()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var reqId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = reqId,
            OperationType = ClusterOpType.ImportArchive,
            PayloadJson   = "{\"NoId\":true}",
        });
        exercise.Tick();

        var activeCancellations = GetActiveCancellations(exercise);
        Assert.False(activeCancellations.ContainsKey(reqId),
            "A rejected ImportArchive must not register a CTS.");
    }

    /// <summary>
    /// <see cref="ClusterOpType.CancelOperation"/> with an unknown target ID must not throw.
    /// </summary>
    [Fact]
    public void CancelOperation_UnknownTargetId_DoesNotThrow()
    {
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        var ex = Record.Exception(() =>
        {
            exercise.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.CancelOperation,
                PayloadJson   = Guid.NewGuid().ToString(),
            });
            exercise.Tick();
        });

        Assert.Null(ex);
    }

    /// <summary>
    /// PACK-C001 SC3: <see cref="AssetInventoryProcessManager.Tick"/> must publish
    /// an <see cref="AssetInventoryUpdateEvent"/> on the first call.
    /// Arrays are empty when the base path does not exist on disk (expected for unit tests).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PublishAssetInventory_PublishesAssetInventoryUpdateEvent_OnFirstTick()
    {
        var bus = new FdpEventBus();

        var gateway = new StorageGatewayModule();
        var manager = new AssetInventoryProcessManager(
            bus,
            gateway,
            @"C:\DoesNotExist_Test_" + Guid.NewGuid(),
            @"C:\DoesNotExist_Test_" + Guid.NewGuid(),
            300);

        // First Tick always triggers the scan (_lastInventoryScan = DateTime.MinValue).
        bus.SwapBuffers();
        manager.Tick();
        bus.SwapBuffers();

        var events = bus.ReadManaged<AssetInventoryUpdateEvent>().ToList();
        Assert.True(events.Any(),
            "AssetInventoryProcessManager.Tick() must publish AssetInventoryUpdateEvent on first call.");

        var ev = events[0];
        // Path doesn't exist -> all lists should be empty (not null)
        Assert.NotNull(ev.LocalScenarios);
        Assert.NotNull(ev.LocalExercises);
        Assert.NotNull(ev.ArchivedExercises);
        Assert.NotNull(ev.UnarchivedLocalExercises);
    }

    // ── ⭐ CE-4330 ② — the storage job queue (📄 docs/DESIGN_Recording_Storage.md §7.2) ─────────────────────────────

    private static ClusterMaster MasterWithOneNode(FdpEventBus bus)
    {
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = (Fdp.Core.NodeId)(1),
            SubsystemName = "TestNode",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        return master;
    }

    private static Guid Ask(ClusterMaster master, ClusterOpType op, Guid exercise, string extra = "")
    {
        var id = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = id, OperationType = op, PayloadJson = $"{{\"ExerciseId\":\"{exercise}\"{extra}}}" });
        return id;
    }

    /// <summary>The last status each request got this frame.</summary>
    private static Dictionary<Guid, (OrchestrationStatusCode Code, object? Payload)> Statuses(FdpEventBus bus)
    {
        var map = new Dictionary<Guid, (OrchestrationStatusCode, object?)>();
        foreach (var ev in bus.ReadManaged<ClusterOpCompletedEvent>()) map[ev.RequestId] = (ev.StatusCode, ev.ResultPayload);
        return map;
    }

    /// <summary>
    /// ⭐⭐ CE-4330 ② — ONE job per exercise, ONE transfer at a time: a second archive of the same exercise JOINS the running job (its answer carries that
    /// job); an archive of another exercise waits QUEUED and starts only when the first ends; the joiner is answered with the job's outcome; every
    /// change reaches the stations as <see cref="StorageJobsUpdateEvent"/>.
    /// 🔴 Mutation: <c>StorageJobQueue.StartNext</c> not checking <c>Running</c> ⇒ the second exercise starts at once ⇒ two CTS ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4330_ASecondRequestJoins_AnotherExerciseWaits_AndStartsWhenTheFirstEnds()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        Guid x = Guid.NewGuid(), y = Guid.NewGuid();

        var first  = Ask(master, ClusterOpType.ExportArchive, x);
        var joiner = Ask(master, ClusterOpType.ExportArchive, x);
        var other  = Ask(master, ClusterOpType.ExportArchive, y);
        master.Tick();
        bus.SwapBuffers();

        var asked = Statuses(bus);
        var joined = Assert.IsType<StorageJob>(asked[joiner].Payload);
        Assert.Equal(first, joined.JobId);                                   // the answer names the job it joined
        Assert.Equal(new[] { first }, GetActiveCancellations(master).Keys);  // ⭐ one transfer: only the first runs
        Assert.Equal(StorageJobState.Queued, master.StorageJobs.Find(other)!.State);
        StorageJobsUpdateEvent? facts = null;
        foreach (var ev in bus.ReadManaged<StorageJobsUpdateEvent>()) facts = ev;
        Assert.NotNull(facts);
        Assert.Equal(2, facts!.Value.Jobs.Length);                           // the stations see both jobs

        // the transfer answers (StorageProcessManager's final status for the archive request)
        bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = first, StatusCode = OrchestrationStatusCode.Success });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        Assert.Equal(OrchestrationStatusCode.Success, Statuses(bus)[joiner].Code);   // the joiner learns the outcome
        Assert.Equal(StorageJobState.Done, master.StorageJobs.Find(first)!.State);
        Assert.Equal(StorageJobState.Running, master.StorageJobs.Find(other)!.State);
        Assert.Equal(new[] { other }, GetActiveCancellations(master).Keys);
    }

    /// <summary>
    /// ⭐ CE-4330 ② — Cancel of a QUEUED job ends it at once (nothing was copied); a discard of an exercise a job is moving is REFUSED with the reason;
    /// a different kind for a busy exercise (an import while it is archived) is refused too.
    /// 🔴 Mutation: the discard's busy check removed ⇒ the discard reaches the deleter ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4330_CancelOfAQueuedJob_EndsIt_AndABusyExerciseIsNeitherDiscardedNorImported()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        var discarded = new List<Guid>();
        master.ExerciseDeleter = id => { discarded.Add(id); return true; };
        Guid x = Guid.NewGuid(), y = Guid.NewGuid();

        var running = Ask(master, ClusterOpType.ExportArchive, x);
        var queued  = Ask(master, ClusterOpType.ExportArchive, y);
        var discard = Ask(master, ClusterOpType.DeleteExercise, x);
        var import  = Ask(master, ClusterOpType.ImportArchive, x);
        master.Tick();
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.CancelOperation, PayloadJson = queued.ToString() });
        master.Tick();
        bus.SwapBuffers();

        var st = Statuses(bus);
        Assert.Equal(OrchestrationStatusCode.Cancelled, st[queued].Code);
        Assert.Equal(StorageJobState.Cancelled, master.StorageJobs.Find(queued)!.State);
        Assert.Equal(OrchestrationStatusCode.Rejected, st[discard].Code);
        Assert.Contains("being archived", (string)st[discard].Payload!);
        Assert.Empty(discarded);
        Assert.Equal(OrchestrationStatusCode.Rejected, st[import].Code);
        Assert.Equal(StorageJobState.Running, master.StorageJobs.Find(running)!.State);
    }

    /// <summary>
    /// ⭐ CE-4330 ⑤ (Q5) — while the cluster is LIVE the running transfer GIVES WAY: its progress carries the word to the copy, and the job says so
    /// (the status bar's clock mark). Out of Live it speeds up again.
    /// 🔴 Mutation: giveWay hard-coded false ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4330_ATransferGivesWayWhileLiveRuns()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        var job = Ask(master, ClusterOpType.ExportArchive, Guid.NewGuid());
        master.Tick();
        var progress = ((Dictionary<Guid, StorageTransferProgress>)typeof(ClusterMaster)
            .GetField("_jobProgress", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(master)!)[job];
        var state = typeof(ClusterMaster).GetField("_currentDsmState", BindingFlags.NonPublic | BindingFlags.Instance)!;

        state.SetValue(master, ClusterState.OperatingLive);
        master.Tick();
        Assert.True(progress.GiveWay);
        Assert.True(master.StorageJobs.Find(job)!.GivingWay);

        state.SetValue(master, ClusterState.Idle);
        master.Tick();
        Assert.False(progress.GiveWay);
        Assert.False(master.StorageJobs.Find(job)!.GivingWay);
    }

    private static void BeatHolding(FdpEventBus bus, ClusterMaster master, int nodeId, params string[] recordings)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = (Fdp.Core.NodeId)(nodeId), SubsystemName = "TestNode", LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
            LocalRecordings = recordings.Length == 0 ? null : new List<string>(recordings),
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐ CE-4330 ④ (§7.2) — an import pushes ONLY to the nodes that lack the recording (fact ③); when every node holds it the job ends at once
    /// (the cache hit, §5a) and nothing is pushed.
    /// 🔴 Mutation: the NodesHolding filter removed ⇒ node 2 is targeted too ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4330_AnImportPushesOnlyToTheNodesThatLackTheRecording_AndNothingWhenAllHoldIt()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        var ex = Guid.NewGuid();
        BeatHolding(bus, master, 1);
        BeatHolding(bus, master, 2, $"{ex}:1000");

        var first = Ask(master, ClusterOpType.ImportArchive, ex);
        master.Tick();
        bus.SwapBuffers();
        ImportArchiveBegunEvent? begun = null;
        foreach (var ev in bus.ReadManaged<ImportArchiveBegunEvent>()) begun = ev;
        Assert.NotNull(begun);
        Assert.Equal(new[] { 1 }, begun!.Value.Targets.Select(t => t.NodeId.Value).ToArray());

        bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = first, StatusCode = OrchestrationStatusCode.Success });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        BeatHolding(bus, master, 1, $"{ex}:1000");   // node 1 now holds it too

        var second = Ask(master, ClusterOpType.ImportArchive, ex);
        master.Tick();
        bus.SwapBuffers();
        Assert.Equal(OrchestrationStatusCode.Success, Statuses(bus)[second].Code);
        foreach (var _ in bus.ReadManaged<ImportArchiveBegunEvent>()) Assert.Fail("every node holds it: nothing is pushed");
        Assert.Equal(StorageJobState.Done, master.StorageJobs.Find(second)!.State);
    }

    private static void Inventory(FdpEventBus bus, ClusterMaster master, params ExerciseInventoryItem[] onNodes)
    {
        bus.PublishManaged(new AssetInventoryUpdateEvent
        {
            LocalScenarios = Array.Empty<string>(), LocalExercises = onNodes, ArchivedExercises = Array.Empty<ExerciseInventoryItem>(), UnarchivedLocalExercises = onNodes,
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    /// <summary>
    /// ⭐ CE-4330 ⑧ (§3 P5 ①) — an archive cut short RESUMES BY ITSELF: an exercise on the nodes whose NAS copy is INCOMPLETE gets an Archive job
    /// ("resume"); the next inventory does not start a second one; once the operator CANCELS it, it is never resumed by itself again.
    /// 🔴 Mutation: ResumeIncompleteArchives not called ⇒ no job ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4330_AnIncompleteArchiveResumesByItself_OnceAndNeverAfterACancel()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        var ex = Guid.NewGuid();
        var cut = new ExerciseInventoryItem((Fdp.Core.ExerciseId)ex, DateTime.UtcNow, TimeSpan.FromMinutes(3), "s", NasState: ArchiveNasState.Incomplete, NasFiles: 1, NasFilesTotal: 2);

        Inventory(bus, master, cut);
        var job = Assert.Single(master.StorageJobs.Jobs);
        Assert.Equal((StorageJobKind.Archive, StorageJobState.Running, "resume"), (job.Kind, job.State, job.AskedBy));

        Inventory(bus, master, cut);
        Assert.Single(master.StorageJobs.Jobs);   // it holds the exercise: no second job

        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.CancelOperation, PayloadJson = job.JobId.ToString() });
        master.Tick();
        bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = job.JobId, StatusCode = OrchestrationStatusCode.Rejected, ResultPayload = "cancelled" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Equal(StorageJobState.Cancelled, master.StorageJobs.Find(job.JobId)!.State);

        Inventory(bus, master, cut);
        Assert.Single(master.StorageJobs.Jobs);   // ⛔ the operator cancelled it: not resumed by itself
    }
}
