using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using System.Text.Json.Nodes;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-10 Tier-1 tests — preview recording + isolated replay round-trip.
///
/// Threading note:
///   EcsRecordReplayController.PrepareRecordingAsync / FinalizeRecordingAsync call
///   ModuleHostKernel.InstallModuleAsync / UninstallModuleAsync which internally await
///   a TaskCompletionSource (swapTcs) that is only fulfilled when the main thread reaches
///   its next BeforeSync boundary (Kernel.Update()).
///
///   xUnit tests run on a single thread, so we CANNOT do:
///       await svc.StartRecordingAsync();  // while nothing is calling Kernel.Update()
///
///   Instead we split each recording operation into phases:
///     Phase-1 (sync):   BeginRecordingStart()  — validates + enters preview
///     Phase-2 (async):  CompleteRecordingStartAsync() — installs kernel module
///                       → run on Task.Run WHILE test thread pumps frames
///     (stop)
///     Phase-1 (async):  CompleteRecordingStopAsync() — finalizes kernel module
///                       → run on Task.Run WHILE test thread pumps frames
///     Phase-2 (sync):   FinishRecordingStop()  — exits preview
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiBatch10Tests
{
    private const long TestNetworkId = 90_010L;
    private const int PumpTimeoutMs = 10_000; // 10 s ceiling per kernel-await

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts preview recording using the two-phase API.
    /// Phase-1 (BeginRecordingStart) runs on the calling (test) thread.
    /// Phase-2 (CompleteRecordingStartAsync) runs on a Task.Run thread while
    /// the test thread pumps Kernel.Update() so swapTcs can be fulfilled.
    /// Returns the fdpPath.
    /// </summary>
    private static string StartRecording(
        EditorHarness h,
        Hrot.Editor.DebugApi.DebugApiService svc,
        string mode = "preview")
    {
        // Phase 1 — sync: validate, enter preview, set IDs.
        var fdpPath = svc.BeginRecordingStart(mode);

        // Phase 2 — async: install kernel module on background thread.
        var installTask = Task.Run(svc.CompleteRecordingStartAsync);

        // Pump until _isRecording is set (CompleteRecordingStartAsync succeeds).
        bool ok = h.PumpUntil(() => installTask.IsCompleted, PumpTimeoutMs);
        installTask.GetAwaiter().GetResult(); // propagate any exception
        Assert.True(ok, $"Recording install did not complete within {PumpTimeoutMs}ms.");
        return fdpPath;
    }

    /// <summary>
    /// Stops preview recording using the two-phase API.
    /// Phase-1 (CompleteRecordingStopAsync) runs on a Task.Run thread while the
    /// test thread pumps frames.  Phase-2 (FinishRecordingStop) runs synchronously.
    /// Returns the finalized fdpPath.
    /// </summary>
    private static string? StopRecording(
        EditorHarness h,
        Hrot.Editor.DebugApi.DebugApiService svc)
    {
        // Phase 1 — async: finalize kernel module on background thread.
        var finalizeTask = Task.Run(svc.CompleteRecordingStopAsync);

        // Pump until uninstall completes.
        bool ok = h.PumpUntil(() => finalizeTask.IsCompleted, PumpTimeoutMs);
        var fdpPath = finalizeTask.GetAwaiter().GetResult(); // propagate any exception
        Assert.True(ok, $"Recording finalize did not complete within {PumpTimeoutMs}ms.");

        // Phase 2 — sync: exit preview (rewind).
        svc.FinishRecordingStop();
        return fdpPath;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Preview recording: start → pump frames → stop → .fdp exists on disk; world rewound.
    /// </summary>
    [Fact]
    public void PreviewRecording_ProducesFdpFile_AndWorldIsRewound()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn a test entity.
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = TestNetworkId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new System.Numerics.Vector3(10f, 0f, 20f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(TestNetworkId, out _), 5000),
            "Entity did not spawn within timeout.");

        // Start preview recording.
        var fdpPath = StartRecording(h, svc);
        Assert.False(string.IsNullOrWhiteSpace(fdpPath), "fdpPath should be set after start");
        Assert.True(svc.IsRecording, "Should be recording after start");
        Assert.True(h.Preview.IsInPreviewMode, "Should be in preview mode after start");

        // Pump a few frames (workload).
        h.PumpFrames(5);

        // Stop recording.
        StopRecording(h, svc);
        Assert.False(svc.IsRecording, "Should not be recording after stop");
        Assert.False(h.Preview.IsInPreviewMode, "Should have exited preview after stop");

        // .fdp must exist on disk.
        Assert.False(string.IsNullOrWhiteSpace(fdpPath));
        Assert.True(File.Exists(fdpPath!), $".fdp file must exist on disk at: {fdpPath}");
    }

    /// <summary>
    /// Recording is mutually exclusive with checkpoint (preview slot occupied).
    /// </summary>
    [Fact]
    public void RecordingStart_WhileCheckpointed_Throws()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Take a checkpoint first (enters preview mode).
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = TestNetworkId + 1,
            OwnerNodeId = 0, InitType = ReliableInitType.None,
        });
        h.PumpFrames(3);
        svc.Checkpoint(); // enters preview slot

        // Attempt Phase-1 of recording while checkpointed — must throw synchronously.
        Assert.Throws<InvalidOperationException>(() => svc.BeginRecordingStart("preview"));
    }

    /// <summary>
    /// Isolated replay: load .fdp → replay-scoped ListEntities is non-empty → seek changes frame →
    /// live _world entity unchanged during replay seeking.
    /// </summary>
    [Fact]
    public void IsolatedReplay_LoadAndSeek_LiveWorldUnaffected()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        // Spawn entity and record a short preview run.
        const long liveEntityId = 90_020L;
        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType          = 1L,
            NetworkId        = liveEntityId,
            OwnerNodeId      = 0,
            InitType         = ReliableInitType.None,
            InitialTransform = new SimTransform { Position = new System.Numerics.Vector3(5f, 0f, 5f) },
        });
        Assert.True(h.PumpUntil(() => h.EntityMap.TryGetEntity(liveEntityId, out _), 5000));

        var fdpPath = StartRecording(h, svc);
        h.PumpFrames(3); // record 3 frames
        StopRecording(h, svc);

        // The fdp must exist.
        Assert.True(File.Exists(fdpPath!), $"fdp file must exist: {fdpPath}");

        // Get live entity position BEFORE loading replay.
        Assert.True(h.EntityMap.TryGetEntity(liveEntityId, out var liveEntity));
        var posBeforeReplay = h.Repo.GetComponentRO<SimTransform>(liveEntity).Position;

        // Load the replay.
        var loadResult = svc.LoadReplay(fdpPath!);
        Assert.NotNull(loadResult);
        Assert.True(svc.IsReplayActive, "Replay should be active after load");
        var totalFrames = loadResult["totalFrames"]?.GetValue<int>() ?? 0;
        Assert.True(totalFrames > 0, $"Replay should have >0 frames (got {totalFrames})");

        // Replay-scoped entities should be non-empty.
        var replayEntities = svc.ListReplayEntities();
        Assert.IsType<JsonArray>(replayEntities);
        var arr = (JsonArray)replayEntities;
        Assert.True(arr.Count > 0, $"Replay entities should be non-empty (got {arr.Count})");

        // Seek to frame 0 and then step forward.
        svc.SeekReplay(0);
        for (int i = 0; i < totalFrames - 1; i++)
            svc.StepReplay("forward");

        // CRITICAL: Live world entity must be UNCHANGED during replay seeking.
        Assert.True(h.Repo.IsAlive(liveEntity), "Live entity should still be alive");
        var posAfterReplay = h.Repo.GetComponentRO<SimTransform>(liveEntity).Position;
        Assert.Equal(posBeforeReplay.X, posAfterReplay.X, precision: 3);
        Assert.Equal(posBeforeReplay.Y, posAfterReplay.Y, precision: 3);
        Assert.Equal(posBeforeReplay.Z, posAfterReplay.Z, precision: 3);

        // Unload.
        svc.UnloadReplay();
        Assert.False(svc.IsReplayActive, "Replay should not be active after unload");
    }

    /// <summary>
    /// Replay status properties return correct frame info.
    /// </summary>
    [Fact]
    public void ReplayStatus_ReflectsCurrentFrame()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();

        h.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType = 1L, NetworkId = 90_030L, OwnerNodeId = 0, InitType = ReliableInitType.None,
        });
        h.PumpFrames(3);

        var fdpPath = StartRecording(h, svc);
        h.PumpFrames(5);
        StopRecording(h, svc);

        Assert.True(File.Exists(fdpPath!));

        svc.LoadReplay(fdpPath!);
        Assert.True(svc.IsReplayActive);
        Assert.True(svc.ReplayTotalFrames > 0);

        var frameBefore = svc.ReplayCurrentFrame;
        svc.StepReplay("forward");
        var frameAfter = svc.ReplayCurrentFrame;
        Assert.True(frameAfter >= frameBefore, "Frame should advance or stay same at end");

        svc.UnloadReplay();
        Assert.False(svc.IsReplayActive);
        Assert.Equal(-1, svc.ReplayCurrentFrame);
        Assert.Equal(0, svc.ReplayTotalFrames);
    }
}
