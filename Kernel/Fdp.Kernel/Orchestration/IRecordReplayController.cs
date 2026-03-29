using System;
using System.Threading.Tasks;

namespace Fdp.Kernel.Orchestration
{
    /// <summary>
    /// Application-agnostic contract for ECS recording and replay lifecycle.
    ///
    /// <para>
    /// Implementations are responsible for wiring <c>RecordingModule</c> /
    /// <c>ReplayModule</c> into the <c>ModuleHostKernel</c> in response to
    /// DSM state transitions.  The interface itself only uses types from
    /// <c>Fdp.Kernel</c> — no <c>Bagira.*</c> references are permitted here.
    /// </para>
    /// </summary>
    public interface IRecordReplayController
    {
        /// <summary>
        /// Opens a new drill recording.  Installs the recording module into the
        /// kernel so that every subsequent frame is captured to disk.
        /// </summary>
        /// <param name="drillId">Unique identifier for this drill session.</param>
        /// <param name="storageDirectory">
        /// Root directory under which per-drill recording files are created.
        /// </param>
        Task PrepareRecordingAsync(Guid drillId, string storageDirectory);

        /// <summary>
        /// Finalizes and closes the active recording.  Blocks until all LZ4
        /// buffers are flushed and the <c>.meta.json</c> manifest is written.
        /// </summary>
        Task FinalizeRecordingAsync();

        /// <summary>
        /// Opens a drill recording for playback.  Installs the replay module and
        /// validates the schema manifest so drift is detected before the first tick.
        /// </summary>
        /// <param name="drillId">Drill identifier whose recording to open.</param>
        /// <param name="storageDirectory">
        /// Root directory from which the recording file is resolved.
        /// </param>
        Task PrepareReplayAsync(Guid drillId, string storageDirectory);

        /// <summary>
        /// Seeks to the specified wall-clock position within the active replay.
        /// </summary>
        /// <param name="targetWallClockTicks">UTC wall-clock ticks (DateTime.UtcNow.Ticks scale).</param>
        Task SeekToTimeAsync(long targetWallClockTicks);

        /// <summary>
        /// Advances playback by one frame.  Called by the application loop
        /// on every tick when <c>RunningReplay</c> is the active DSM state.
        /// </summary>
        /// <param name="currentTime">Current simulation time for rate-control.</param>
        void ProcessPlaybackTick(GlobalTime currentTime);

        /// <summary>
        /// Tears down the active replay without modifying the
        /// <see cref="EntityRepository"/> — historical state is preserved in-place
        /// for a Live-from-Replay branch (CGF1-S0305).
        /// </summary>
        Task TeardownReplayAsync();
    }
}
