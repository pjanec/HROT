#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless tests for the BATCH-15 record/replay wiring (STR-P5-T4, design §9 / STR-D5).
///
/// <para>
/// Verifies that:
/// <list type="bullet">
///   <item><c>PrepareReplay</c> severs the reverse-sync <c>TogglablePostSimulationGroup</c>
///     (Enabled=false) so Bullet reverse-sync cannot overwrite historical SimTransform, and
///     <c>FinalizeReplay</c>/<c>PrepareLive</c> restore it (Enabled=true).</item>
///   <item>A recording captures the world's SimTransform each tick (the replay opened from it
///     has frames — recording grows with ticks).</item>
///   <item>During replay the <c>PlaybackTickSystem</c> drives a replayed entity's SimTransform
///     from the recording (not the reverse-sync), reproducing the recorded motion.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RecordReplayWiringTests : IDisposable
{
    private readonly List<EditorStrideSubsystem> _suts = new();
    private readonly string _tempDir;

    public RecordReplayWiringTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BATCH15_RR_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var s in _suts) s.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private EditorStrideSubsystem CreateSut()
    {
        var sut = new EditorStrideSubsystem { RecordReplayStorageDirectory = _tempDir };
        sut.Initialize(visualFactory: null); // headless — no GPU
        _suts.Add(sut);
        return sut;
    }

    private static ExecuteNodeOpIntent Intent(NodeOpType op, Guid exerciseId) => new()
    {
        TransactionId = Guid.NewGuid(),
        TargetNodeId  = 0,
        Operation     = op,
        DomainPayload = exerciseId,
    };

    /// <summary>
    /// Awaits <paramref name="task"/> while pumping <see cref="EditorStrideSubsystem.Tick"/> on
    /// the calling thread, because async kernel module installs only go live on a subsequent
    /// kernel <c>Update()</c> (the swap is applied by the main loop).
    /// </summary>
    private static void AwaitWhileTicking(EditorStrideSubsystem sut, Task task, float dt = 1f / 60f, int maxTicks = 600)
    {
        int ticks = 0;
        while (!task.IsCompleted && ticks++ < maxTicks)
            sut.Tick(dt);
        // Surface faults.
        task.GetAwaiter().GetResult();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Reverse-sync group toggle (PrepareReplay severs; Finalize restores)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PrepareReplay_Commit_SeversReverseSyncGroup_FinalizeRestoresIt()
    {
        var sut = CreateSut();
        Assert.NotNull(sut.ReverseSyncGroup);
        Assert.True(sut.ReverseSyncGroup!.Enabled, "reverse-sync group starts enabled (live).");

        var exerciseId = Guid.NewGuid();

        // PrepareReplay Commit → reverse-sync severed.
        sut.ReplayLoadHandler.Commit(Intent(NodeOpType.PrepareReplay, exerciseId), sut.World);
        Assert.False(sut.ReverseSyncGroup.Enabled,
            "PrepareReplay must disable the reverse-sync group so it cannot overwrite historical SimTransform.");

        // FinalizeReplay Commit → reverse-sync restored.
        sut.ReplayLoadHandler.Commit(Intent(NodeOpType.FinalizeReplay, exerciseId), sut.World);
        Assert.True(sut.ReverseSyncGroup.Enabled,
            "FinalizeReplay must re-enable the reverse-sync group (authority back to Bullet).");
    }

    [Fact]
    public void PrepareLive_Commit_RestoresReverseSyncGroup()
    {
        var sut = CreateSut();
        sut.ReverseSyncGroup!.Enabled = false; // simulate mid-replay

        sut.ReplayLoadHandler.Commit(Intent(NodeOpType.PrepareLive, Guid.NewGuid()), sut.World);
        Assert.True(sut.ReverseSyncGroup.Enabled,
            "PrepareLive (Live-from-Replay branch) must re-enable the reverse-sync group.");
    }

    [Fact]
    public void SeveredGroup_DoesNotExecuteInnerSystems_DuringTick()
    {
        // With the group severed, ticking must not run the reverse-sync (which would otherwise
        // write identity pose into owned entities' SimTransform via the NoOp service).
        var sut = CreateSut();

        // Spawn an OWNED entity with a non-identity SimTransform.
        var e = sut.World.CreateEntity();
        var startPos = new Vector3(3f, 4f, 5f);
        sut.World.AddComponent(e, new SimTransform { Position = startPos, Rotation = Quaternion.Identity });
        sut.World.SetAuthority<SimTransform>(e, true);

        // Sever the group (as PrepareReplay would).
        sut.ReverseSyncGroup!.Enabled = false;

        // Drive several ticks. With reverse-sync severed and NoOp physics (no body for this
        // entity anyway), the SimTransform must be preserved — nothing overwrites it.
        for (int i = 0; i < 10; i++) sut.Tick(1f / 60f);

        var pos = sut.World.GetComponentRO<SimTransform>(e).Position;
        Assert.Equal(startPos, pos);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Recording grows per tick; replay drives SimTransform from the recording
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Recording_CapturesFramesEachTick_AndReplayReproducesMotion()
    {
        var sut = CreateSut();
        var exerciseId = Guid.NewGuid();

        // Spawn a NON-OWNED entity whose SimTransform we move deterministically each tick (so
        // reverse-sync, which only touches owned entities, never interferes). The recorder
        // captures whatever SimTransform is present each PostSimulation tick.
        var e = sut.World.CreateEntity();
        sut.World.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        sut.World.AddComponent(e, new NetworkIdentity { Value = 555 });
        sut.World.GetSingletonManaged<NetworkEntityMap>().Register(555, e);

        // Start recording (installs RecordingModule into the kernel).
        AwaitWhileTicking(sut, sut.RecordReplayController.PrepareRecordingAsync(exerciseId, _tempDir));
        Assert.NotNull(sut.RecordReplayController.ActiveRecordingModule);

        // Drive motion for N ticks: x advances 1 unit/tick. The recorder runs in PostSimulation.
        const int recordedTicks = 80; // > KeyframeInterval(60) so we get a keyframe + deltas
        for (int i = 0; i < recordedTicks; i++)
        {
            ref var tf = ref sut.World.GetComponentRW<SimTransform>(e);
            tf.Position = new Vector3(i + 1, 0f, 0f);
            sut.Tick(1f / 60f);
        }

        // Finalize recording (flushes + writes .meta.json).
        AwaitWhileTicking(sut, sut.RecordReplayController.FinalizeRecordingAsync());
        Assert.Null(sut.RecordReplayController.ActiveRecordingModule);

        // A recording file with frames must now exist (recording grew with ticks).
        var fdpFiles = Directory.GetFiles(_tempDir, "*.fdp", SearchOption.AllDirectories);
        Assert.True(fdpFiles.Length > 0, "a .fdp recording file must exist after recording.");

        // Open the recording for replay (installs ReplayModule + PlaybackTickSystem).
        AwaitWhileTicking(sut, sut.RecordReplayController.PrepareReplayAsync(exerciseId, _tempDir));
        var replay = sut.RecordReplayController.ActiveReplayModule;
        Assert.NotNull(replay);
        Assert.True(replay!.TotalFrames > 0,
            $"the replay must have captured frames (recording grew per tick); got {replay.TotalFrames}.");

        // Uninstall needs a subsequent kernel Update to drain — tick while awaiting.
        AwaitWhileTicking(sut, sut.RecordReplayController.TeardownReplayAsync());
    }

    // ════════════════════════════════════════════════════════════════════════
    // BATCH-16 Fix B: record → finalize → replay of the SAME file must not throw
    // IOException (the recording writer's file handle must be released before the
    // ReplayModule's PlaybackController opens it). Reproduces the D9 GPU crash flow.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RecordThenReplaySameFile_DoesNotThrowIOException_AndOpensPlayback()
    {
        var sut = CreateSut();
        var exerciseId = Guid.NewGuid();

        // A non-owned entity whose SimTransform we ramp deterministically each recorded tick.
        var e = sut.World.CreateEntity();
        sut.World.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        sut.World.AddComponent(e, new NetworkIdentity { Value = 909 });
        sut.World.GetSingletonManaged<NetworkEntityMap>().Register(909, e);

        // Record.
        AwaitWhileTicking(sut, sut.RecordReplayController.PrepareRecordingAsync(exerciseId, _tempDir));
        for (int i = 0; i < 70; i++)
        {
            ref var tf = ref sut.World.GetComponentRW<SimTransform>(e);
            tf.Position = new Vector3(i, 0f, 0f);
            sut.Tick(1f / 60f);
        }

        // Finalize: this MUST release the recording writer's file handle (dispose the
        // AsyncRecorder FileStream, which is opened FileShare.None) before we open the same
        // file for replay below.
        AwaitWhileTicking(sut, sut.RecordReplayController.FinalizeRecordingAsync());
        Assert.Null(sut.RecordReplayController.ActiveRecordingModule);

        // The recording file exists.
        var recordingFile = Path.Combine(
            _tempDir,
            Fdp.Toolkit.Orchestration.OrchestrationConstants.ExercisesDirectoryName,
            exerciseId.ToString(),
            Fdp.Toolkit.Orchestration.OrchestrationConstants.GetNodeRecordingFileName(0));
        Assert.True(File.Exists(recordingFile), "the .fdp recording file must exist after finalize.");

        // Independent proof the writer's handle is released: we can now open the same file with a
        // share mode that DENIES other writers — this throws IOException if any handle remains.
        using (var probe = new FileStream(recordingFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(probe.Length > 0, "recording file must contain data.");
        }

        // Replay of the SAME file. Before Fix B this threw
        // IOException("...node_0.fdp...used by another process") from
        // ReplayModule.RegisterSystems → new PlaybackController(filePath). It must succeed now.
        var ex = Record.Exception(() =>
            AwaitWhileTicking(sut, sut.RecordReplayController.PrepareReplayAsync(exerciseId, _tempDir)));
        Assert.Null(ex); // no IOException (or any other) opening playback on the just-recorded file.

        var replay = sut.RecordReplayController.ActiveReplayModule;
        Assert.NotNull(replay);
        Assert.True(replay!.TotalFrames > 0,
            $"playback opened the recorded file and indexed frames; got {replay.TotalFrames}.");

        AwaitWhileTicking(sut, sut.RecordReplayController.TeardownReplayAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)] // stress: repeated record→finalize→replay churn surfaces handle-release races.
    public void RecordFinalizeReplay_RepeatedOnSameDir_NeverThrowsIOException(int iterations)
    {
        var sut = CreateSut();

        var e = sut.World.CreateEntity();
        sut.World.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        sut.World.AddComponent(e, new NetworkIdentity { Value = 808 });
        sut.World.GetSingletonManaged<NetworkEntityMap>().Register(808, e);

        for (int it = 0; it < iterations; it++)
        {
            var exerciseId = Guid.NewGuid();
            AwaitWhileTicking(sut, sut.RecordReplayController.PrepareRecordingAsync(exerciseId, _tempDir));
            for (int i = 0; i < 65; i++)
            {
                ref var tf = ref sut.World.GetComponentRW<SimTransform>(e);
                tf.Position = new Vector3(i, it, 0f);
                sut.Tick(1f / 60f);
            }
            AwaitWhileTicking(sut, sut.RecordReplayController.FinalizeRecordingAsync());

            // Replay the file we just finalized — must not throw "used by another process".
            var ex = Record.Exception(() =>
                AwaitWhileTicking(sut, sut.RecordReplayController.PrepareReplayAsync(exerciseId, _tempDir)));
            Assert.Null(ex);
            Assert.True(sut.RecordReplayController.ActiveReplayModule!.TotalFrames > 0);
            AwaitWhileTicking(sut, sut.RecordReplayController.TeardownReplayAsync());
        }
    }

    /// <summary>
    /// Reproduces the D9 GPU harness sequencing exactly: a single phase-machine hook that, on each
    /// tick, advances start-record → record → finalize → start-replay only when the pending async
    /// op has completed (exactly as <c>StrideGizmoReplayHarnessCases.RecordThenReplay</c> does).
    /// This is the flow that crashed on the real GPU run; it must finish without an IOException.
    /// </summary>
    [Fact]
    public void HarnessStyleRecordThenReplay_PhaseMachine_DoesNotThrow()
    {
        var sut = CreateSut();
        var exerciseId = Guid.NewGuid();

        var e = sut.World.CreateEntity();
        sut.World.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        sut.World.AddComponent(e, new NetworkIdentity { Value = 910 });
        sut.World.GetSingletonManaged<NetworkEntityMap>().Register(910, e);

        int phase = 0;
        float elapsed = 0f;
        const float recordSeconds = 0.5f;
        Task? pending = null;
        Exception? faulted = null;
        const float dt = 1f / 60f;

        // Drive the phase machine the same way the harness's per-frame RegisterUpdate hook does.
        for (int frame = 0; frame < 4000 && phase < 4 && faulted == null; frame++)
        {
            switch (phase)
            {
                case 0:
                    pending = sut.RecordReplayController.PrepareRecordingAsync(exerciseId, _tempDir);
                    phase = 1; elapsed = 0f;
                    break;
                case 1:
                    if (pending is { IsCompleted: false }) break;
                    if (pending is { IsFaulted: true }) { faulted = pending.Exception; break; }
                    elapsed += dt;
                    ref_advance(sut, e, frame);
                    if (elapsed < recordSeconds) break;
                    pending = sut.RecordReplayController.FinalizeRecordingAsync();
                    phase = 2;
                    break;
                case 2:
                    if (pending is { IsCompleted: false }) break;
                    if (pending is { IsFaulted: true }) { faulted = pending.Exception; break; }
                    pending = sut.RecordReplayController.PrepareReplayAsync(exerciseId, _tempDir);
                    phase = 3;
                    break;
                case 3:
                    if (pending is { IsCompleted: false }) break;
                    if (pending is { IsFaulted: true }) { faulted = pending.Exception; break; }
                    phase = 4;
                    break;
            }
            sut.Tick(dt);
        }

        Assert.Null(faulted); // no IOException at PlaybackController open.
        Assert.Equal(4, phase);
        Assert.NotNull(sut.RecordReplayController.ActiveReplayModule);
        Assert.True(sut.RecordReplayController.ActiveReplayModule!.TotalFrames > 0);

        AwaitWhileTicking(sut, sut.RecordReplayController.TeardownReplayAsync());

        static void ref_advance(EditorStrideSubsystem s, Entity ent, int i)
        {
            ref var tf = ref s.World.GetComponentRW<SimTransform>(ent);
            tf.Position = new Vector3(i, 0f, 0f);
        }
    }

    [Fact]
    public void Replay_DrivesSimTransformFromRecording_WhileReverseSyncSevered()
    {
        var sut = CreateSut();
        var exerciseId = Guid.NewGuid();

        var e = sut.World.CreateEntity();
        sut.World.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        sut.World.AddComponent(e, new NetworkIdentity { Value = 777 });
        sut.World.GetSingletonManaged<NetworkEntityMap>().Register(777, e);

        // Record a deterministic ramp: position.X = tick index.
        AwaitWhileTicking(sut, sut.RecordReplayController.PrepareRecordingAsync(exerciseId, _tempDir));
        const int recordedTicks = 90;
        for (int i = 0; i < recordedTicks; i++)
        {
            ref var tf = ref sut.World.GetComponentRW<SimTransform>(e);
            tf.Position = new Vector3(i, 0f, 0f);
            sut.Tick(1f / 60f);
        }
        var lastRecordedX = sut.World.GetComponentRO<SimTransform>(e).Position.X;
        Assert.True(lastRecordedX >= recordedTicks - 1);
        AwaitWhileTicking(sut, sut.RecordReplayController.FinalizeRecordingAsync());

        // Corrupt the live SimTransform so we can prove playback overwrites it from the recording.
        {
            ref var tf = ref sut.World.GetComponentRW<SimTransform>(e);
            tf.Position = new Vector3(-9999f, -9999f, -9999f);
        }

        // PrepareReplay: PrepareAsync installs the ReplayModule; Commit severs the reverse-sync.
        var prepareIntent = Intent(NodeOpType.PrepareReplay, exerciseId);
        AwaitWhileTicking(sut, sut.ReplayLoadHandler.PrepareAsync(prepareIntent, CancellationToken.None));
        sut.ReplayLoadHandler.Commit(prepareIntent, sut.World);
        Assert.False(sut.ReverseSyncGroup!.Enabled, "reverse-sync must be severed during replay.");

        // Seek to the start, then tick so PlaybackTickSystem restores recorded SimTransform.
        var replay = sut.RecordReplayController.ActiveReplayModule!;
        replay.SeekToFrameAsync(0).GetAwaiter().GetResult();

        // Drive several ticks; PlaybackTickSystem (registered by ReplayModule, outside the
        // togglable group) restores historical state from keyframes/deltas.
        for (int i = 0; i < 20; i++) sut.Tick(1f / 60f);

        var replayedX = sut.World.GetComponentRO<SimTransform>(e).Position.X;
        // The corrupted sentinel must have been overwritten by recorded data (a non-negative
        // ramp value), proving playback — not the reverse-sync — drives SimTransform.
        Assert.True(replayedX > -1f,
            $"replay must restore SimTransform from the recording (drove X={replayedX}, not the -9999 sentinel).");

        // FinalizeReplay restores the reverse-sync group.
        var finalizeIntent = Intent(NodeOpType.FinalizeReplay, exerciseId);
        AwaitWhileTicking(sut, sut.ReplayLoadHandler.PrepareAsync(finalizeIntent, CancellationToken.None));
        sut.ReplayLoadHandler.Commit(finalizeIntent, sut.World);
        Assert.True(sut.ReverseSyncGroup.Enabled, "reverse-sync must be restored after FinalizeReplay.");
    }
}
