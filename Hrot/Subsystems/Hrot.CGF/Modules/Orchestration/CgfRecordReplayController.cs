using System;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Orchestration;
using Fdp.Core.Logging;

namespace Hrot.CGF.Modules.Orchestration
{
    /// <summary>
    /// Brain-appropriate seam for the CGF node's participation in the recording and
    /// replay cluster lifecycle (CGF-1-BATCH-23 A.1).
    ///
    /// <para>
    /// <b>Current behaviour — Phase 3 skeleton:</b>
    /// All lifecycle methods return <see langword="Task.CompletedTask"/> immediately.
    /// CGF participates in the cluster handshake (ACKs to the orchestrator) but does not
    /// write ECS frame data to disk because the CGF skeleton has no recordable
    /// <c>ModuleHostKernel</c>.  <see cref="IsReplayActive"/> is tracked consistently
    /// so that <c>ReferenceLiveLoadHandler</c> and <c>ReferenceReplayLoadHandler</c>
    /// can correctly gate the Live-from-Replay branch (CGF1-S0305).
    /// </para>
    ///
    /// <para>
    /// <b>Phase 3+ road-map:</b> When CGF acquires a recordable kernel the body of
    /// <see cref="PrepareRecordingAsync"/> and <see cref="FinalizeRecordingAsync"/>
    /// will install / flush a <c>RecordingModule</c> on the CGF kernel, mirroring
    /// the <c>EcsRecordReplayController</c> path on SimHost.
    /// </para>
    /// </summary>
    public sealed class CgfRecordReplayController : IRecordReplayController
    {
        private bool _replayActive;

        /// <inheritdoc />
        /// <remarks>
        /// No-op in Phase 3 skeleton.  Phase 3+ will install a recording module
        /// into the CGF kernel here.
        /// </remarks>
        public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory)
        {
            FdpLog<CgfRecordReplayController>.Info(
                "[CgfRecordReplayController] PrepareRecording called (exerciseId={0}). " +
                "Phase 3 skeleton: no ECS recording — ACK only.", exerciseId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>No-op in Phase 3 skeleton.</remarks>
        public Task FinalizeRecordingAsync(long maxNetworkId = 0)
        {
            FdpLog<CgfRecordReplayController>.Info(
                "[CgfRecordReplayController] FinalizeRecording called (maxNetworkId={0}). " +
                "Phase 3 skeleton: no ECS recording — ACK only.", maxNetworkId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>
        /// No-op in Phase 3 skeleton.  Sets <see cref="IsReplayActive"/> to
        /// <see langword="true"/> so that subsequent <c>PrepareLive</c> commands are
        /// correctly routed to the Live-from-Replay branch handler (CGF1-S0305).
        /// </remarks>
        public Task PrepareReplayAsync(Guid exerciseId, string storageDirectory)
        {
            FdpLog<CgfRecordReplayController>.Info(
                "[CgfRecordReplayController] PrepareReplay called (exerciseId={0}). " +
                "Phase 3 skeleton: no ECS replay — ACK only.", exerciseId);
            _replayActive = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>No-op in Phase 3 skeleton.</remarks>
        public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
        {
            FdpLog<CgfRecordReplayController>.Info(
                "[CgfRecordReplayController] SeekToTime called (ticks={0}). " +
                "Phase 3 skeleton: no-op.", targetWallClockTicks);
            return Task.FromResult(default(GlobalTime));
        }

        /// <inheritdoc />
        /// <remarks>No-op in Phase 3 skeleton.</remarks>
        public void ProcessPlaybackTick(GlobalTime currentTime) { }

        /// <inheritdoc />
        /// <remarks>
        /// Clears <see cref="IsReplayActive"/> so future <c>PrepareLive</c> commands
        /// are handled as cold Live ops rather than Live-from-Replay branches.
        /// </remarks>
        public Task TeardownReplayAsync()
        {
            FdpLog<CgfRecordReplayController>.Info(
                "[CgfRecordReplayController] TeardownReplay called. " +
                "Phase 3 skeleton: clearing replay-active flag.");
            _replayActive = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public bool IsReplayActive => _replayActive;

        /// <inheritdoc />
        /// <remarks>Always returns 0 — CGF has no ECS network entity map.</remarks>
        public long ActiveMaxNetworkId => 0;

        /// <inheritdoc />
        /// <remarks>Always returns 0 — CGF does not hold a replay file.</remarks>
        public float ActiveReplayDurationSeconds => 0f;
    }
}
