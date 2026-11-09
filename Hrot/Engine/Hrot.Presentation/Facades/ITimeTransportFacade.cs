namespace Hrot.UI.Common.Facades;

/// <summary>
/// Abstraction that decouples the time-transport status-bar UI from the concrete time
/// controller (editor-local <see cref="Fdp.Toolkit.Time.Controllers.MasterSyncController"/>
/// vs. cluster bus intents).
///
/// <para>Implementors supply the current transport state as readable properties and
/// handle user actions via the mutating methods.</para>
/// </summary>
public interface ITimeTransportFacade
{
    /// <summary><c>true</c> when the Play/Pause button should be interactable.</summary>
    bool IsPlayPauseEnabled { get; }

    /// <summary><c>true</c> when the Step button should be interactable.</summary>
    bool IsStepEnabled { get; }

    /// <summary><c>true</c> when the Stop button should be interactable.</summary>
    bool IsStopEnabled { get; }

    /// <summary><c>true</c> when simulation time is currently frozen (paused).</summary>
    bool IsPaused { get; }

    /// <summary>Current simulation time in seconds.</summary>
    double TotalTime { get; }

    /// <summary>
    /// ⭐ <c>CE-3351</c> — the clock as the cluster runs it: equal to <see cref="TotalTime"/> except in a review / replay, where the cluster clock counts
    /// WALL seconds after the session's zero (<c>PlaybackTickSystem</c> indexes the recording by anchor + it) and <see cref="TotalTime"/> is that moment's sim time.
    /// ⭐ What a reader that places the playhead on wall ticks uses (<c>ClusterTimelineCursor.Now</c>).
    /// </summary>
    double ClockSeconds => TotalTime;

    /// <summary>Current time-scale multiplier (e.g. 1.0 = real-time, 2.0 = 2x).</summary>
    float TimeScale { get; }

    /// <summary>Toggles between play and pause.</summary>
    void TogglePlayPause();

    /// <summary>Advances simulation by one fixed-size step (valid only when paused).</summary>
    void Step();

    /// <summary>
    /// Advances simulation by one step of <paramref name="seconds"/> (valid only when paused) — what
    /// <see cref="Step"/> does with 1/60 s. The film director's coarser rates (1/20, 1/15 s; `DESIGN_Film_Director.md` D-1).
    /// </summary>
    void StepBy(float seconds);

    /// <summary>Stops the simulation and returns to Idle.</summary>
    void Stop();

    /// <summary>Changes the time-scale multiplier.</summary>
    void SetTimeScale(float scale);

    // ── ⭐⭐ CE-3329 row 21 (R-3059, R-3060) — the review / replay session, from ANY host ─────────────────────────────────────────
    //   Each member PUBLISHES the master intent HTTP publishes (POST /review, /review/exit, /replay/seek, /replay/axis) through the one
    //   builder, ReplaySessionIntents. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §8 row 21.

    /// <summary>What the cluster shows: edit, a dry run, the live run, a review of it, or a replay.</summary>
    TransportMode Mode { get; }

    /// <summary>R-3058 — the replay session's axis: what <see cref="Seek"/>, <see cref="EnterReview"/> and <see cref="LivePoint"/> mean.</summary>
    Fdp.Toolkit.Orchestration.ReplayAxis Axis { get; }

    /// <summary>Switches the whole session's axis (no seek: the frame on screen stays).</summary>
    void SetAxis(Fdp.Toolkit.Orchestration.ReplayAxis axis);

    /// <summary>Reviews the live run in place, at <paramref name="at"/> on the active axis. ⭐ CE-4100 ③ (AQ108 D9): a DAR always has an explicit start — never "the live end", so there is no default.</summary>
    void EnterReview(double at);

    /// <summary>Back to the live run, exactly where it paused (R-3060: <see cref="Stop"/> in a review does the same).</summary>
    void ExitReview();

    /// <summary>The review's live point on the active axis (Sim: the run's sim seconds; Wall: seconds after the zero);
    /// <see langword="null"/> outside a review or before the entry pause has landed.</summary>
    double? LivePoint { get; }

    /// <summary>Seeks the open replay or review to <paramref name="t"/> on the active axis.</summary>
    void Seek(double t);

    /// <summary>⭐ CE-3329 §12n — loops the open replay / review between <paramref name="from"/> and <paramref name="to"/> on the active
    /// axis: a SESSION setting of the master's replay clock (never this host's), so every host draws the same bracket.</summary>
    void SetLoop(double from, double to);

    /// <summary>⭐ CE-3329 §12n — clears the loop.</summary>
    void ClearLoop();

    /// <summary>⭐ CE-3329 §12n — the session's loop on the active axis; <see langword="null"/> = none.</summary>
    (double From, double To)? Loop { get; }

    // ── ⭐ CE-3330 E3 (ui lane — a cross-lane edit of this backend-owned facade, said in the commit): what the toolbar's mode menu needs ──

    /// <summary>The session layer as this host last heard it (<see langword="null"/> before the first sample) — checkpoints, the wall zero, the live point.</summary>
    Fdp.Toolkit.Orchestration.ReplaySession? Session { get; }

    /// <summary>Reviews the live run from <paramref name="simSeconds"/> of the run's own clock — from LIVE the host has no session yet (no map, no wall zero),
    /// and the master takes a sim target on either axis ("the sim target wins", <c>ReplaySessionIntents.EnterReview</c>).</summary>
    void EnterReviewAtSim(double simSeconds);

    /// <summary>
    /// ⭐ <c>CE-4097</c> part two (AQ108 D22) — this host's run-mode choice, or <see langword="null"/> when it has none. ⛔ The facade's own
    /// <see cref="TogglePlayPause"/> / <see cref="Step"/> / <see cref="StepBy"/> stay RAW (from Edit: the dry run) — HTTP routes do what they
    /// name and never follow the armed mode. A USER control (the toolbar's ▶, the timeline's Space) asks the choice through
    /// <see cref="Hrot.UI.Common.RunMode.UserTransport"/>; <see cref="Stop"/> delegates to the choice itself (<c>POST /sim/stop</c> = ■).
    /// </summary>
    Hrot.UI.Common.RunMode.RunModeChoice? RunChoice => null;

    // ── ⭐ CE-4325 (E7, R-4177, 📄 docs/UX/DESIGN_Bookmarks_And_Notes.md §2) — the exercise's notes: read off the node's ONE ClusterView, changed through the ONE CRUD message ──

    /// <summary>The notes of the exercise on show, as the master last published them (a NEW array per change); empty where the host has none.</summary>
    Fdp.Toolkit.Orchestration.ExerciseNote[] Notes => System.Array.Empty<Fdp.Toolkit.Orchestration.ExerciseNote>();

    /// <summary>⭐ CE-1467 (a) — why the notes are shown but cannot be changed here (null = they can): a host with no master (the standalone Replay Browser reads <c>notes.json</c> beside the files).</summary>
    string? NotesReadOnlyReason => null;

    /// <summary>Asks the master to add / edit / delete a note — the ONE message. No-op where the host has no master to ask.</summary>
    void EditNote(Fdp.Toolkit.Orchestration.NoteEditKind kind, Fdp.Toolkit.Orchestration.ExerciseNote note) { }

    // ── ⭐ CE-4330 ui half (📄 docs/DESIGN_Recording_Storage.md §7.2, §7.3) — the storage jobs: read off the node's ONE ClusterView, asked through the existing ops ──

    /// <summary>The cluster's storage jobs as the master last published them (fact ①); empty where the host has none.</summary>
    Fdp.Toolkit.Orchestration.StorageJob[] StorageJobs => System.Array.Empty<Fdp.Toolkit.Orchestration.StorageJob>();

    /// <summary>Cancels a storage job (<c>CancelOperation(JobId)</c>). No-op where the host has no master to ask.</summary>
    void CancelStorageJob(System.Guid jobId) { }

    /// <summary>Sends a storage ask (an <c>ExecuteStorageOpIntent</c> built by <c>StorageIntents</c>); returns its request id (empty where nothing was sent).</summary>
    System.Guid AskStorage(Fdp.Toolkit.Orchestration.ExecuteStorageOpIntent ask) => System.Guid.Empty;

    /// <summary>The master's refusal of storage ask <paramref name="requestId"/> (its op's failure reason), or null while it has not ended / when it succeeded.</summary>
    string? StorageAskRefusal(System.Guid requestId) => null;

    /// <summary>Takes a checkpoint named <paramref name="label"/> (a MARK — the existing <c>TakeCheckpointIntent</c>, the moment is the one the master names) with the mark's record
    /// <paramref name="note"/>: ⭐ CE-1467 (e) the master adds it under the label it actually used (unique). No-op where the host has no master.</summary>
    void TakeMark(string label, Fdp.Toolkit.Orchestration.ExerciseNote? note = null) { }
}

/// <summary>⭐ CE-3329 row 21 — what the cluster shows (the facade's <see cref="ITimeTransportFacade.Mode"/>).</summary>
public enum TransportMode
{
    /// <summary>Nothing loaded.</summary>
    Idle,
    /// <summary>Editing a scenario.</summary>
    Edit,
    /// <summary>A dry run of the scenario being edited.</summary>
    Preview,
    /// <summary>The live run.</summary>
    Live,
    /// <summary>A During Action Review of the live run (CE-3329).</summary>
    Review,
    /// <summary>A recorded exercise replayed.</summary>
    Replay,
}
