using System;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Logging;

namespace Hrot.Common.Orchestration
{
    /// <summary>
    /// No-op <see cref="IRecordReplayController"/> for listener / instructor nodes
    /// (IG, ExCon) that participate in the cluster handshake but do not record or replay
    /// ECS frame data (CGF-1-BATCH-23 A.2/A.3).
    ///
    /// <para>
    /// These nodes must still ACK <c>PrepareReplay</c>, <c>FinalizeReplay</c>,
    /// <c>PrepareLive</c>, and <c>FinalizeLive</c> so that the orchestrator 2PC does
    /// not stall waiting for acknowledgements.  All methods return
    /// <see langword="Task.CompletedTask"/> immediately.  <see cref="IsReplayActive"/>
    /// is tracked consistently so that the Live-from-Replay
    /// <c>PrepareLive</c> branch in <c>ReferenceReplayLoadHandler</c> is correctly
    /// gated (CGF1-S0305).
    /// </para>
    /// </summary>
    public sealed class ListenerRecordReplayController : IRecordReplayController
    {
        private readonly string _nodeName;
        private bool _replayActive;

        /// <param name="nodeName">Human-readable name used in log messages (e.g. <c>"IG"</c>).</param>
        public ListenerRecordReplayController(string nodeName = "Listener")
        {
            _nodeName = nodeName ?? "Listener";
        }

        /// <inheritdoc />
        public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory)
        {
            FdpLog<ListenerRecordReplayController>.Info(
                "[{0}] PrepareRecording — listener node, no ECS recording, ACK only (exerciseId={1}).",
                _nodeName, exerciseId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task FinalizeRecordingAsync(long maxNetworkId = 0)
        {
            FdpLog<ListenerRecordReplayController>.Info(
                "[{0}] FinalizeRecording — listener node, ACK only.", _nodeName);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PrepareReplayAsync(Guid exerciseId, string storageDirectory)
        {
            FdpLog<ListenerRecordReplayController>.Info(
                "[{0}] PrepareReplay — listener node, no ECS replay, ACK only (exerciseId={1}).",
                _nodeName, exerciseId);
            _replayActive = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
        {
            FdpLog<ListenerRecordReplayController>.Info(
                "[{0}] SeekToTime — listener node, no-op (ticks={1}).",
                _nodeName, targetWallClockTicks);
            return Task.FromResult(default(GlobalTime));
        }

        /// <inheritdoc />
        public void ProcessPlaybackTick(GlobalTime currentTime) { }

        /// <inheritdoc />
        public Task TeardownReplayAsync()
        {
            FdpLog<ListenerRecordReplayController>.Info(
                "[{0}] TeardownReplay — listener node, clearing replay-active flag.", _nodeName);
            _replayActive = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public bool IsReplayActive => _replayActive;

        /// <inheritdoc />
        /// <remarks>Always 0 — listener nodes have no ECS entity map.</remarks>
        public long ActiveMaxNetworkId => 0;

        /// <inheritdoc />
        /// <remarks>Always 0 — listener nodes do not hold a replay file.</remarks>
        public float ActiveReplayDurationSeconds => 0f;

        /// <inheritdoc />
        /// <remarks>Listener nodes do not replay ECS frame data.</remarks>
        public GlobalTime GetCurrentReplayTime() => default;
    }
}
