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
        /// ⭐ <c>CE-3299</c> B4 / <c>D18</c> (R-3047) — opens a recording that BRANCHES: its first frames are a copy of
        /// <paramref name="branch"/>'s parent up to the branch point, then every frame is captured after them. Null = an empty start
        /// (<see cref="PrepareRecordingAsync(Guid, string)"/>). ⚠ The default supports only null — a controller that records implements it.
        /// </summary>
        Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory, FlightRecorder.RecordingBranch? branch)
            => branch == null
                ? PrepareRecordingAsync(exerciseId, storageDirectory)
                : throw new NotSupportedException($"{GetType().Name} cannot record a branch");

        /// <summary>
        /// ⭐ <c>D18</c> — where a branch from the replay on screen would start: this node's recording up to the frame it shows.
        /// Null when no replay is open, or this node has no recording of it (a listener — its branch starts empty).
        /// </summary>
        FlightRecorder.RecordingBranch? CurrentReplayBranch => null;

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
        /// Returns the starting wall-clock ticks of the active replay recording,
        /// or <c>0</c> when no replay is active.
        /// </summary>
        long ActiveRecordingStartWallTicks { get; }

        /// <summary>⭐ R-4012 (Q91 §4-P) — the last recorded frame's wall-clock ticks, or <c>0</c> (no replay, or no recording).</summary>
        long ActiveRecordingEndWallTicks { get; }

        /// <summary>
        /// ⭐⭐ R-4012 (Q91 §4-P P1) — the replay timeline's zero in wall-clock ticks: the CLUSTER's anchor once a seek has
        /// delivered it, else this node's own first frame. Set by the seek handler.
        /// </summary>
        long ReplayAnchorWallTicks { get; set; }

        /// <summary>⭐⭐ R-4012 — true while this node restores the current replay moment from its own recording.</summary>
        bool IsRestoringReplay { get; }

        /// <summary>
        /// Returns the highest network entity ID encountered in the active recording,
        /// or <c>0</c> when no replay is active.  Published to the orchestrator via
        /// <c>ResultJson</c> so the ID allocator can be reset above the replay's ID
        /// space before live entities are spawned (CGF1-S0304).
        /// </summary>
        long ActiveMaxNetworkId { get; }
    }
}
