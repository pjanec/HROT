using System.Threading.Tasks;
using Fdp.Core;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §1a, §2) — what the <see cref="DirectorRunner"/> drives in the editor: the load (through
    /// the editor's own load, as the panel's Load does), the cluster state and the world's count (for <see cref="ScenarioReadyProbe"/>), the
    /// PREVIEW (R-4061: the run the director plays), the sized step (<c>ITimeCommands.StepBy</c>) and the clock. ⭐ An interface so the rails
    /// drive the runner frame by frame with no editor. Main thread only.
    /// </summary>
    public interface IDirectorHost
    {
        EntityRepository? World { get; }

        /// <summary>Opens <paramref name="scenario"/> from its saved file (the editor's load, to edit).</summary>
        void LoadScenario(string scenario);

        bool IsOperatingEdit { get; }

        /// <summary>⭐ CE-4042 — this process has loaded a scenario already (by the director or the user): the next load is a RELOAD, and a
        /// reload is not where a golden may be recorded (CE-3294; Regression_Net §6 ④).</summary>
        bool HasLoadedAScenario { get; }
        int? EntityCount { get; }

        bool InPreview { get; }
        void EnterPreview(bool startPaused);
        void ExitPreview();
        bool Paused { get; }
        void Pause();

        /// <summary>ONE step of <paramref name="seconds"/> (1/60, 1/20 or 1/15 s — R-4063).</summary>
        void StepBy(float seconds);

        /// <summary>The sim clock (the world's).</summary>
        double SimTime { get; }

        /// <summary>A wall clock, in seconds.</summary>
        double WallNow { get; }

        /// <summary>⭐ Holds the frame (the main thread) for <paramref name="seconds"/>: how real time is kept with one step per frame.</summary>
        void Hold(double seconds);

        /// <summary>The name of a behaviour by its hash (the <c>task</c> cue); null when unknown.</summary>
        string? BehaviourName(int hash);

        /// <summary>The scenarios root's takes and reels.</summary>
        TakeStore Takes { get; }

        /// <summary>⭐ <c>CE-3299</c> B5 (R1, restore design §1k) — writes the world as it is NOW as checkpoint <paramref name="label"/> at
        /// <paramref name="path"/> (a one-frame recording, <c>CheckpointFile</c>).</summary>
        void WriteCheckpoint(string path, string label) => throw new NotSupportedException("this host cannot write a checkpoint");

        /// <summary>⭐ B5 — puts the checkpoint at <paramref name="path"/> back (the cluster restore's own <c>PutBack</c>) and asks the
        /// clock to its mark (D5; the clock moves on a later frame). Returns the mark's sim time.</summary>
        double RestoreCheckpoint(string path) => throw new NotSupportedException("this host cannot restore a checkpoint");

        /// <summary>⭐ <c>CE-3316</c> — a slot-paced background run is in flight (the kernel's <c>SlotRunInFlight</c>): R1 writes its checkpoint
        /// on a step where this is false, as the recorder does.</summary>
        bool SlotRunInFlight => false;

        // ── ⭐ CE-4045 H1a (docs/DESIGN_Scenario_Regression.md §10-1, R-4085) — a LIVE run and its recording ──────────────────────

        /// <summary>Loads <paramref name="scenario"/> LIVE through the cluster master, paused at 0 (the <c>POST /scenario/load/live</c>
        /// load) — a live run records (R-3053, R-4000); a preview never does.</summary>
        void LoadLive(string scenario) => throw new NotSupportedException("this host cannot load a live run");

        bool IsOperatingLive => false;

        /// <summary>Ends the live run (the master: Idle) — the recorder finalises and closes its file.</summary>
        void StopLive() => throw new NotSupportedException("this host cannot stop a live run");

        bool IsIdle => false;

        /// <summary>The file the live run records into while it records; null when nothing records (before, or once it is closed).</summary>
        string? LiveRecordingPath => null;

        /// <summary>Opens a finished recording for playback (the product's player, in this process).</summary>
        IRecordedRun OpenRecording(string path) => throw new NotSupportedException("this host cannot play a recording");

        // ── ⭐ CE-4045 H5 (docs/DESIGN_Scenario_Regression.md §10-3) — a During Action Review of the live run, through the master ────

        /// <summary>Reviews the live run in place, at <paramref name="simSeconds"/> of its own clock (DAR — <c>OperatingReview</c>).</summary>
        void EnterReview(double simSeconds) => throw new NotSupportedException("this host cannot review");

        /// <summary>Seeks the review to <paramref name="simSeconds"/> of the run's clock.</summary>
        void SeekReview(double simSeconds) => throw new NotSupportedException("this host cannot review");

        /// <summary>Back to the live run, where it paused.</summary>
        void ExitReview() => throw new NotSupportedException("this host cannot review");

        bool IsOperatingReview => false;
    }

    /// <summary>
    /// ⭐ <c>CE-4038</c> B9 (D-6, D-7) — what the director shows besides the map: the panels and gizmos hidden while it runs, a title card over
    /// each load, the shot's caption. The editor's is <c>EditorCinema</c>; the rails pass a recorder.
    /// </summary>
    public interface ICinema
    {
        void Enter();
        void Exit();
        bool Active { get; }

        /// <summary>A full-screen card (null: none) — over a load, between reel items.</summary>
        void Card(string? text);

        /// <summary>The caption under the picture (null: none).</summary>
        void Caption(string? text);
    }

    /// <summary>⭐ <c>CE-4038</c> B10 (§4b, R-4060) — a take's layers: a TRANSIENT override over the user's set, never written to their file.</summary>
    public interface ILayerOverride
    {
        /// <summary>The take starts clean: every layer off, then <paramref name="on"/> on (and <paramref name="off"/> off).</summary>
        void Apply(System.Collections.Generic.IReadOnlyList<string> on, System.Collections.Generic.IReadOnlyList<string> off);

        /// <summary>The user's own set again.</summary>
        void Clear();
    }

    /// <summary>⭐ <c>CE-4038</c> B8 (§3b, D-15) — where captured frames go: the scene with a caption burned in, or a title card.</summary>
    public interface IFrameSink
    {
        /// <summary>Asks the map on screen for this frame's scene; completes when it is written.</summary>
        Task Frame(string path, int width, int height, int supersample, string? caption);

        /// <summary>A card frame: <paramref name="text"/> on black.</summary>
        Task Card(string path, int width, int height, string text);
    }

    /// <summary>⭐ <c>CE-4038</c> B11 (§4c, R-4059) — the sound of the director: the take's switch, and the film's cue log while capturing.</summary>
    public interface IDirectorSound
    {
        /// <summary>A take's or shot's <c>sound</c> (null: the user's own switch).</summary>
        void SetTakeSound(bool? on);

        /// <summary>Starts logging every sound the mixer starts (sim time, sound, gain, pan) for the film's track.</summary>
        void BeginLog();

        /// <summary>Stops logging and renders the WAV of <paramref name="frames"/> film frames (each at its sim time) to <paramref name="wavPath"/>;
        /// false when nothing was logged.</summary>
        bool EndLog(string wavPath, System.Collections.Generic.IReadOnlyList<double> frameSimTimes, int fps);

        /// <summary>The user's own sound again, and no log (a run stopped part way).</summary>
        void Reset();
    }
}
