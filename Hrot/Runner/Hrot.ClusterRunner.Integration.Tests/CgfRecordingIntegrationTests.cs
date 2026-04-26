using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests proving that both SimHost (node 1) and CGF (node 400) participate
/// correctly in the record/replay lifecycle.
///
/// <para>
/// Root cause addressed: <c>CgfScenarioLoadHandler</c> intercepts <c>PrepareLive</c> but
/// never called <c>PrepareRecordingAsync</c>, so CGF never produced a recording file.
/// The fix adds <see cref="Fdp.Core.Orchestration.IRecordReplayController"/> wiring in
/// <c>CgfScenarioLoadHandler</c>, mirroring the existing <c>HrotScenarioLoadHandler</c>.
/// </para>
/// <para>
/// Also covers the <c>NodeReplaySeek</c> operation which previously had no handler in
/// <c>ClusterSlave</c>.  <c>ReferenceReplayLoadHandler</c> now handles it for both nodes.
/// </para>
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class CgfRecordingIntegrationTests
{
    // Recording file name pattern: {storageDir}/{exerciseId}/node_{nodeId}.fdp
    private const int SimHostNodeId = 1;
    private const int CgfNodeId     = 400;

    private static string RecordingFile(string exerciseId, int nodeId) =>
        Path.Combine(OrchestrationConstants.DefaultStagingDirectory, exerciseId, $"node_{nodeId}.fdp");

    // Issue a TransitionState cluster op and pump until the master reaches the target state.
    private static async Task TransitionAsync(
        HrotRunnerHarness harness,
        int targetStateId,
        string? exerciseId,
        int timeoutFrames = 4000)
    {
        string payloadJson = exerciseId != null
            ? JsonSerializer.Serialize(new { TargetState = targetStateId, ExerciseId = exerciseId })
            : JsonSerializer.Serialize(new { TargetState = targetStateId });

        await harness.OrchestratorSvc.TestHook_ClusterMaster!
            .HandleClusterOpRequestAsync(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = payloadJson,
            })
            .ConfigureAwait(false);

        bool reached = harness.PumpUntil(
            () => (int)harness.OrchestratorSvc.TestHook_ClusterMaster!.CurrentSystemState == targetStateId,
            timeoutFrames);

        if (!reached)
            throw new InvalidOperationException(
                $"Cluster did not reach state {targetStateId} within {timeoutFrames} frames. " +
                $"Current: {(int)harness.OrchestratorSvc.TestHook_ClusterMaster!.CurrentSystemState}.");
    }

    /// <summary>
    /// Proves that both SimHost (<c>node_1.fdp</c>) and CGF (<c>node_400.fdp</c>) produce
    /// non-empty recording files when a live exercise runs.
    ///
    /// <para>
    /// This test would fail without the <c>CgfScenarioLoadHandler</c> recording fix because
    /// <c>PrepareReplay</c> would throw when trying to open the missing <c>node_400.fdp</c>.
    /// </para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task BothNodes_LiveSimulation_BothRecordingFilesCreated()
    {
        var exerciseId  = Guid.NewGuid().ToString();
        var simHostFile = RecordingFile(exerciseId, SimHostNodeId);
        var cgfFile     = RecordingFile(exerciseId, CgfNodeId);

        using var harness = new HrotRunnerHarness();

        // Transition to OperatingLive: triggers PrepareLive on both nodes which starts recording.
        await TransitionAsync(harness, (int)ClusterState.OperatingLive, exerciseId)
            .ConfigureAwait(false);

        // Pump frames so the recorders capture actual ECS data.
        harness.PumpFrames(100);

        // Transition to OperatingReplay: triggers FinalizeLive (flushes + closes files)
        // then PrepareReplay (opens the files for playback).
        await TransitionAsync(harness, (int)ClusterState.OperatingReplay, exerciseId)
            .ConfigureAwait(false);

        Assert.True(File.Exists(simHostFile),
            $"SimHost recording not found: {simHostFile}");
        Assert.True(File.Exists(cgfFile),
            $"CGF recording not found: {cgfFile}");
        Assert.True(new FileInfo(simHostFile).Length > 0,
            $"SimHost recording is empty: {simHostFile}");
        Assert.True(new FileInfo(cgfFile).Length > 0,
            $"CGF recording is empty: {cgfFile}");
    }

    /// <summary>
    /// Proves that the full cluster reaches <c>OperatingReplay</c> with both nodes
    /// successfully opening their respective recording files for playback.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task BothNodes_OperatingReplay_ClusterReachesReplayState()
    {
        var exerciseId = Guid.NewGuid().ToString();

        using var harness = new HrotRunnerHarness();
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        await TransitionAsync(harness, (int)ClusterState.OperatingLive, exerciseId)
            .ConfigureAwait(false);
        harness.PumpFrames(100);
        await TransitionAsync(harness, (int)ClusterState.OperatingReplay, exerciseId)
            .ConfigureAwait(false);

        // Pump replay frames to confirm the cluster stays stable in OperatingReplay.
        harness.PumpFrames(50);

        Assert.Equal((int)ClusterState.OperatingReplay, (int)master.CurrentSystemState);
    }

    /// <summary>
    /// Proves that a <c>ReplaySeek</c> does not crash the cluster.
    ///
    /// <para>
    /// Both SimHost and CGF handle <c>NodeReplaySeek</c> via the updated
    /// <c>ReferenceReplayLoadHandler</c>.  The cluster must remain in
    /// <c>OperatingReplay</c> after the seek completes.
    /// </para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task BothNodes_SeekDuringReplay_ClusterRemainsInReplayState()
    {
        var exerciseId = Guid.NewGuid().ToString();

        using var harness = new HrotRunnerHarness();
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        await TransitionAsync(harness, (int)ClusterState.OperatingLive, exerciseId)
            .ConfigureAwait(false);
        harness.PumpFrames(100);
        await TransitionAsync(harness, (int)ClusterState.OperatingReplay, exerciseId)
            .ConfigureAwait(false);

        harness.PumpFrames(30);

        // Issue a ReplaySeek to the end of the recording.
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = $"{{\"TargetWallTicks\":{long.MaxValue}}}",
        }).ConfigureAwait(false);

        // Pump frames after the seek; the cluster must remain in OperatingReplay.
        harness.PumpFrames(50);

        Assert.Equal((int)ClusterState.OperatingReplay, (int)master.CurrentSystemState);
    }
}
