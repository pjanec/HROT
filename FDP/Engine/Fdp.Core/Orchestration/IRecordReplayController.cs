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
        /// <para>⭐ CE-3299 D17 (R-3045) — <paramref name="checkpointingSeconds"/> &gt; 0 records in CHECKPOINTING mode (a keyframe every N s of sim
        /// time, nothing else); 0 = FULL. A branch records in its PARENT's mode (one session, one mode) and ignores it.</para>
        /// </summary>
        Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory, FlightRecorder.RecordingBranch? branch, double checkpointingSeconds = 0)
            => branch == null && checkpointingSeconds <= 0
                ? PrepareRecordingAsync(exerciseId, storageDirectory)
                : throw new NotSupportedException($"{GetType().Name} cannot record a branch or a checkpointing session");

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
        /// ⭐ CE-3329 PW (§12c W2) — hold this node's egress from a world swap until the master's commit: no sample is published in
        /// between (the cleanup's disposes still go out). Released by <see cref="ReleaseEgress"/>, or it lapses (bounded).
        /// A controller with no network world ignores it.
        /// </summary>
        void HoldEgress(string reason) { }

        /// <summary>
        /// ⭐ CE-3329 P0 (row 6) — the RECORDED sim time of the frame on screen (<c>GlobalTime.TotalTime</c> as the live run had it), or
        /// <see langword="null"/> outside a replay. ⚠ Not <see cref="GetCurrentReplayTime"/>, which is the replay clock (wall after the zero).
        /// </summary>
        double? CurrentReplaySimSeconds => null;

        /// <summary>⭐ CE-3329 P1 (R3, row 8) — the open replay's wall ↔ sim map (from its v7 frame index); <see langword="null"/> outside a
        /// replay. Read once per replay — the node's prepare result carries it to the master.</summary>
        FlightRecorder.ReplayTimeMap? ActiveReplayTimeMap => null;

        /// <summary>⭐ CE-3329 (§9f) — the open replay's checkpoint marks (its recording's metadata); <see langword="null"/> outside a
        /// replay. The node's prepare result carries them to the master's session layer.</summary>
        FlightRecorder.Metadata.CheckpointMark[]? ActiveReplayCheckpoints => null;

        /// <summary>⭐ AQ107 C — the geodetic origin the open replay's recording was made under (its metadata); <see langword="null"/>
        /// outside a replay or for a recording that pre-dates the field. The node's prepare result carries it to the master, which
        /// publishes it as the NED wire's frame origin.</summary>
        FlightRecorder.Metadata.GeoOriginRecord? ActiveReplayGeoOrigin => null;

        /// <summary>⭐ AQ107 C — the terrain that was resident while the open replay's recording was made; empty outside a replay or when none.</summary>
        string ActiveReplayTerrainName => string.Empty;

        /// <summary>⭐ CE-3329 PW — releases <see cref="HoldEgress"/>: the next egress frame republishes the world.</summary>
        void ReleaseEgress() { }

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
        /// ⭐⭐ CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — the exit's RELEASE: from <see cref="TeardownReplayAsync"/> until this
        /// call the node's ingress stays gated as if it still restored (state left in the readers, events dropped); here every gated
        /// ingress system is told to discard what its readers hold before it takes anything in again. Called by the ONE gate set
        /// (<c>ReferenceReplayLoadHandler.LeaveReplayGates</c>) and by a branch to live. ⭐ A controller with no ingress does nothing.
        /// </summary>
        void ReleaseReplayExit() { }

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

        /// <summary>⭐ R-3055 — the recording this node replays now, or <see langword="null"/> outside a replay: what the debug
        /// surface's journal history and recording search scan, read-only.</summary>
        string? ActiveReplayFilePath { get; }

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
