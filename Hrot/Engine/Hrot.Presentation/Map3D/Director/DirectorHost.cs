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
    public interface IDirectorHost
    {
        EntityRepository? World { get; }

        /// <summary>Opens <paramref name="scenario"/> from its saved file (the editor's load, to edit).</summary>
        void LoadScenario(string scenario);

        bool IsOperatingEdit { get; }

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
