using System;
using System.Threading.Tasks;

namespace Fdp.Core.Orchestration
{
    /// <summary>
    /// Application-agnostic contract for ECS recording and replay lifecycle.
    ///
    /// <para>
    /// Implementations are responsible for wiring <c>RecordingModule</c> /
    /// <c>ReplayModule</c> into the <c>ModuleHostKernel</c> in response to
    /// Cluster state transitions.  The interface itself only uses types from
    /// <c>Fdp.Core</c> — no <c>Hrot.*</c> references are permitted here.
    /// </para>
    /// </summary>
    public interface IRecordReplayController
    {
        /// <summary>
        /// Opens a new exercise recording.  Installs the recording module into the
        /// kernel so that every subsequent frame is captured to disk.
        /// </summary>
        /// <param name="exerciseId">Unique identifier for this exercise session.</param>
        /// <param name="storageDirectory">
        /// Root directory under which per-exercise recording files are created.
        /// </param>
        Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory);

        /// <summary>
        /// Finalizes and closes the active recording.  Blocks until all LZ4
        /// buffers are flushed and the <c>.meta.json</c> manifest is written.
        /// </summary>
        /// <param name="maxNetworkId">
        /// Highest network entity ID used during the recording session.  Pass <c>0</c>
        /// (or omit) when no network map is available (e.g. offline / test scenarios).
        /// </param>
        Task FinalizeRecordingAsync(long maxNetworkId = 0);

        /// <summary>
        /// Opens a exercise recording for playback.  Installs the replay module and
        /// validates the schema manifest so drift is detected before the first tick.
        /// </summary>
        /// <param name="exerciseId">Exercise identifier whose recording to open.</param>
        /// <param name="storageDirectory">
        /// Root directory from which the recording file is resolved.
        /// </param>
        Task PrepareReplayAsync(Guid exerciseId, string storageDirectory);

        /// <summary>
        /// Seeks to the specified wall-clock position within the active replay.
        /// </summary>
        /// <param name="targetWallClockTicks">UTC wall-clock ticks (DateTime.UtcNow.Ticks scale).</param>
        /// <returns>The <see cref="GlobalTime"/> the recording landed on after the seek.
        /// Listener/CGF implementations that do not record return <see langword="default"/>.</returns>
        Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks);

        /// <summary>
        /// Advances playback by one frame.  Called by the application loop
        /// on every tick when <c>RunningReplay</c> is the active Cluster state.
        /// </summary>
        /// <param name="currentTime">Current simulation time for rate-control.</param>
        void ProcessPlaybackTick(GlobalTime currentTime);

        /// <summary>
        /// Tears down the active replay without modifying the
        /// <see cref="EntityRepository"/> — historical state is preserved in-place
        /// for a Live-from-Replay branch (CGF1-S0305).
        /// </summary>
        Task TeardownReplayAsync();

        /// <summary>
        /// Returns <see langword="true"/> when a replay session is currently active
        /// (i.e. <see cref="PrepareReplayAsync"/> has completed and
        /// <see cref="TeardownReplayAsync"/> has not yet been called).
        /// Used by <see cref="ReferenceReplayLoadHandler.CanHandle"/> to gate the
        /// Live-from-Replay <c>PrepareLive</c> branch (CGF1-S0305).
        /// </summary>
        bool IsReplayActive { get; }

        /// <summary>
        /// Returns the current replay position as a <see cref="GlobalTime"/> snapshot.
        /// Must be called BEFORE <see cref="TeardownReplayAsync"/> -- after teardown,
        /// the replay module is uninstalled and the time singleton reverts.
        /// Listener and CGF implementations return <c>default(GlobalTime)</c>.
        /// </summary>
        GlobalTime GetCurrentReplayTime();

        /// <summary>
        /// Returns the wall-clock duration in seconds of the active replay recording,
        /// or <c>0</c> when no replay is active.  Derived from
        /// <see cref="Fdp.Core.FlightRecorder.Metadata.RecordingMetadata.Duration"/> when
        /// available, with a <c>TotalFrames / 60</c> fallback for legacy recordings.
        /// Published as part of <c>ReplayPrepareResult</c> so the orchestrator can set the
        /// seek-slider range after a successful <c>LoadingReplay</c> transition.
        /// </summary>
        float ActiveReplayDurationSeconds { get; }

        /// <summary>
        /// Returns the highest network entity ID encountered in the active recording,
        /// or <c>0</c> when no replay is active.  Published to the orchestrator via
        /// <c>ResultJson</c> so the ID allocator can be reset above the replay's ID
        /// space before live entities are spawned (CGF1-S0304).
        /// </summary>
        long ActiveMaxNetworkId { get; }
    }
}
