using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core;

namespace Hrot.UI.Common.Map3D.Director
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §1a, §2) — what the <see cref="DirectorRunner"/> drives in the editor: the load (through
    /// the editor's own load, as the panel's Load does), the cluster state and the world's count (for <see cref="ScenarioReadyProbe"/>), the
    /// PREVIEW (R-4061: the run the director plays), the sized step (<c>ITimeCommands.StepBy</c>) and the clock. ⭐ An interface so the rails
    /// drive the runner frame by frame with no editor. Main thread only.
    /// </summary>
    /// <summary>
    /// ⭐ <c>CE-4267</c> — what became of the last <see cref="IDirectorHost.LoadScenario"/> on a host whose load goes through the scenario DOOR (the editor): the door
    /// may decide not to load (the gate is not ready, the user's own unsaved edits, a run the user started) — 📐 measured: through the UI's asker its question was an
    /// invisible modal behind the cinema, the load never went, every later load was dropped while the question stayed open, and the readiness probe accepted the OLD
    /// world after its grace (every take after the first: "no unit named X"). A host that loads directly (the Replay Browser's recordings) does not implement it.
    /// </summary>
    public interface IDirectorLoadOutcome
    {
        /// <summary>The load went out (the door admitted it): the readiness probe may start.</summary>
        bool LoadWent { get; }
        /// <summary>Why the load did not go (null: it went, or no answer yet).</summary>
        string? LoadRefusal { get; }
        /// <summary>The refusal is a wait (the run gate is not ready yet): ask again a little later.</summary>
        bool LoadMayRetry { get; }
    }

    public interface IDirectorHost
    {
        EntityRepository? World { get; }

        /// <summary>Opens <paramref name="scenario"/> from its saved file (the editor's load, to edit).</summary>
        void LoadScenario(string scenario);

        bool IsOperatingEdit { get; }

        int? EntityCount { get; }

        /// <summary>
        /// ⭐ <c>R-1046</c> (DESIGN_Cluster_Load_Phase §11.9) — the shown map's 3-D build (its <c>IMapBuildStatus</c>); <see langword="null"/>: this host has no 3-D map to wait for. The runner asks for the build when it
        /// requests the load and does not start a take until it is built.
        /// </summary>
        Fdp.Toolkit.Vis3D.Map3DBuild? MapBuild => null;

        /// <summary>⭐ <c>CE-11023</c> — what the load is doing now (the cluster's load progress, e.g. "terrain: valley"); a change counts as progress, so a
        /// long load that advances never times out. Null = the host reports none (the entity count and the map build still count).</summary>
        string? LoadProgress => null;

        /// <summary>⭐ <c>CE-11023</c> — the cluster's ERROR state (R-3073) with its reason: a load fails at once, with it. Null = not in error.</summary>
        string? LoadError => null;

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

        /// <summary>⭐ <c>CE-1303</c> P2a (CR-3, docs/DESIGN_Director_Timeline_Editor.md §11m D2) — the source can JUMP in sim time (a recording).
        /// ⛔ A live run cannot (R-3034): the editor's is false. On a seekable host the take's clock is the source's own (t0 = 0, D3).</summary>
        bool CanSeek => false;

        /// <summary>⭐ CR-3 — puts the source at sim second <paramref name="sourceSim"/>, through the host's ONE seek path (R-3059). Only when
        /// <see cref="CanSeek"/>.</summary>
        void Seek(double sourceSim) => throw new System.NotSupportedException("this host's source cannot seek (a live run)");
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

        /// <summary>⭐ <c>CE-1303</c> P2b — CR-11 (§11o S9): the subtitle on screen now — text, position, opacity (null: none); set every playing frame.
        /// A cinema that draws only text keeps the default: the text as the caption.</summary>
        void Subtitle(SubtitleFrame? frame) => Caption(frame?.Text);
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
        Task Frame(string path, int width, int height, int supersample, SubtitleFrame? subtitle);

        /// <summary>⭐ <c>CE-1305</c> — the same frame with everything the film burns over the scene: the subtitle AND the film graphics (<see cref="FilmOverlay"/>).
        /// A sink that draws only text keeps the default: the subtitle alone.</summary>
        Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay) => Frame(path, width, height, supersample, overlay.Subtitle);

        /// <summary>A card frame: <paramref name="text"/> on black.</summary>
        Task Card(string path, int width, int height, string text);

        // ── ⭐ CE-11018 (DESIGN_Linked_Client_Director.md §2b, §2c, Q113-B) — a job's frames, told as a job: Begin, each frame with its context, End. ──
        //    All default to the map's behaviour, so a sink that only draws (FilmFrameSink) is unchanged.

        /// <summary>A film job starts (a reel captured to frames, or a still). The director waits for the task before its first frame; a FAULT fails the job with its reason.</summary>
        Task Begin(Hrot.Common.Infrastructure.FilmJob job) => Task.CompletedTask;

        /// <summary>⭐ CE-11067 (unreal-ig director test H-3) — where the last job's frames went, in words ("HROT's map", "the linked client 'dps'",
        /// "HROT's map: the linked client 'dps' cannot film now"); null before the first job. The status shows it so a fallback is never silent.</summary>
        string? Route => null;

        /// <summary>A scene frame of the job, with what a renderer elsewhere needs besides the overlay (<see cref="FilmFrameContext"/>).</summary>
        Task Frame(string path, int width, int height, int supersample, FilmOverlay overlay, FilmFrameContext context) => Frame(path, width, height, supersample, overlay);

        /// <summary>A film frame with no new picture: a <c>Repeat</c> of the last scene (slow motion; the director copies the PNG) or <c>Silence</c> under a card.
        /// Nothing to draw for the map; a linked client keeps its sound track in step.</summary>
        Task Advance(Hrot.Common.Infrastructure.FilmFrameKind kind, FilmFrameContext context) => Task.CompletedTask;

        /// <summary>The job ended (once per <see cref="Begin"/>, a failed or stopped one too); the result is the sound track a linked client made, or null.</summary>
        Task<Hrot.Common.Infrastructure.FilmTrack?> End(Hrot.Common.Infrastructure.FilmJob job, Hrot.Common.Infrastructure.FilmJobOutcome outcome)
            => Task.FromResult<Hrot.Common.Infrastructure.FilmTrack?>(null);
    }

    /// <summary>
    /// ⭐ <c>CE-11018</c> (Q113-F) — what the director knows of a film frame that a renderer elsewhere needs: the job, the film frame number, the film rate (sim seconds per
    /// film second now), the director's steps in this item so far, and the take's layer override this frame.
    /// </summary>
    public sealed record FilmFrameContext(Guid JobId, int Index, double FilmRate, long Step, IReadOnlyList<string> LayersOn, IReadOnlyList<string> LayersOff)
    {
        /// <summary>The same context for film frame <paramref name="index"/>.</summary>
        public FilmFrameContext At(int index) => this with { Index = index };
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

        /// <summary>⭐ <c>CE-1305</c> TE-11 (§11v A2) — the playback rate the film plays at NOW (sim seconds per film second; 0 paused; +∞ a fast-forward): what the
        /// scene is heard at (<see cref="Hrot.UI.Common.Audio.SoundAtRate"/>). Called every playing frame; <see cref="Reset"/> and <see cref="EndLog"/> clear it.</summary>
        void SetRate(double rate) { }
    }
}
