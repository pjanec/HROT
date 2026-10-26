using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Hrot.UI.Common.Audio;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Map3D.Director;
using ImGuiNET;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §1a) — the editor as the director's host: every member a delegate onto what
    /// <c>EditorSubsystem</c> already holds (its load, its cluster state, the preview controller, <c>ITimeCommands</c>, the world's clock).
    /// ⛔ It holds nothing of its own, so it cannot drift from the editor's own controls.
    /// </summary>
    internal sealed class EditorDirectorHost : IDirectorHost
    {
        private readonly Func<EntityRepository?> _world;
        private readonly Action<string> _load;
        private readonly Func<bool> _operatingEdit;
        private readonly Func<bool> _inPreview;
        private readonly Action<bool> _enterPreview;
        private readonly Action _exitPreview;
        private readonly Func<bool> _paused;
        private readonly Action _pause;
        private readonly Action<float> _stepBy;
        private readonly Func<int, string?> _behaviourName;
        private readonly Action<string, string> _writeCheckpoint;
        private readonly Func<string, double> _restoreCheckpoint;
        private readonly Func<bool> _slotRunInFlight;
        private readonly Action<string> _loadLive;
        private readonly Hrot.Presentation.DebugApi.IClusterMasterControl _master;
        private readonly Func<Fdp.Toolkit.Orchestration.ClusterState> _state;
        private readonly Func<string?> _recordingPath;
        private readonly Func<string, IRecordedRun> _openRecording;
        private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

        public EditorDirectorHost(Func<EntityRepository?> world, Action<string> load, Func<bool> operatingEdit, Func<bool> inPreview,
                                  Action<bool> enterPreview, Action exitPreview, Func<bool> paused, Action pause, Action<float> stepBy,
                                  Func<int, string?> behaviourName, TakeStore takes, Func<bool> slotRunInFlight,
                                  Action<string, string> writeCheckpoint, Func<string, double> restoreCheckpoint,
                                  Action<string> loadLive, Hrot.Presentation.DebugApi.IClusterMasterControl master, Func<Fdp.Toolkit.Orchestration.ClusterState> state,
                                  Func<string?> recordingPath, Func<string, IRecordedRun> openRecording)
        {
            _loadLive = loadLive ?? throw new ArgumentNullException(nameof(loadLive));
            _master = master ?? throw new ArgumentNullException(nameof(master));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _recordingPath = recordingPath ?? throw new ArgumentNullException(nameof(recordingPath));
            _openRecording = openRecording ?? throw new ArgumentNullException(nameof(openRecording));
            _world = world; _load = load; _operatingEdit = operatingEdit; _inPreview = inPreview; _enterPreview = enterPreview;
            _exitPreview = exitPreview; _paused = paused; _pause = pause; _stepBy = stepBy; _behaviourName = behaviourName;
            _writeCheckpoint = writeCheckpoint ?? throw new ArgumentNullException(nameof(writeCheckpoint));
            _restoreCheckpoint = restoreCheckpoint ?? throw new ArgumentNullException(nameof(restoreCheckpoint));
            _slotRunInFlight = slotRunInFlight ?? throw new ArgumentNullException(nameof(slotRunInFlight));
            Takes = takes;
        }

        public EntityRepository? World => _world();
        private bool _loaded;

        public void LoadScenario(string scenario) { _loaded = true; _load(scenario); }
        public bool IsOperatingEdit => _operatingEdit();
        /// <summary>The director loaded one, or the user did (the editor is then OperatingEdit).</summary>
        public bool HasLoadedAScenario => _loaded || _operatingEdit();
        public int? EntityCount => _world()?.EntityCount;
        public bool InPreview => _inPreview();
        public void EnterPreview(bool startPaused) => _enterPreview(startPaused);
        public void ExitPreview() => _exitPreview();
        public bool Paused => _paused();
        public void Pause() => _pause();
        public void StepBy(float seconds) => _stepBy(seconds);
        public double SimTime => _world() is { } w ? Fdp.Toolkit.Time.SimClock.Of(w).TotalTime : 0d;
        public double WallNow => _wall.Elapsed.TotalSeconds;
        public string? BehaviourName(int hash) => _behaviourName(hash);
        public TakeStore Takes { get; }
        public void WriteCheckpoint(string path, string label) => _writeCheckpoint(path, label);
        public double RestoreCheckpoint(string path) => _restoreCheckpoint(path);
        public bool SlotRunInFlight => _slotRunInFlight();

        // ⭐ CE-4045 H1a — a live run and its recording (Scenario Regression §10-1)
        public void LoadLive(string scenario) { _loaded = true; _loadLive(scenario); }
        public bool IsOperatingLive => _state() == Fdp.Toolkit.Orchestration.ClusterState.OperatingLive;
        public void StopLive() => _master.Transition(new Fdp.Toolkit.Orchestration.TransitionStateIntent
            { TransactionId = Guid.NewGuid(), TargetState = Fdp.Toolkit.Orchestration.ClusterState.Idle });

        // ⭐ CE-4045 H5 — the review through the ONE construction of its intents (the same as POST /review and the facade)
        public void EnterReview(double simSeconds) => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReview(simSeconds: simSeconds));
        public void SeekReview(double simSeconds) => _master.SeekReplay(Hrot.UI.Common.Facades.ReplaySessionIntents.Seek(0, simSeconds));
        public void ExitReview() => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.ExitReview());
        public bool IsOperatingReview => _state() == Fdp.Toolkit.Orchestration.ClusterState.OperatingReview;
        public bool IsIdle => _state() == Fdp.Toolkit.Orchestration.ClusterState.Idle;
        public string? LiveRecordingPath => _recordingPath();
        public IRecordedRun OpenRecording(string path) => _openRecording(path);

        /// <summary>⭐ Holding the frame: the main thread sleeps until the step is due (a ms or two of it spun, so it is not overslept).</summary>
        public void Hold(double seconds)
        {
            if (!(seconds > 0)) return;
            double until = WallNow + Math.Min(seconds, 0.25);
            double sleep = until - WallNow - 0.002;
            if (sleep > 0) System.Threading.Thread.Sleep(TimeSpan.FromSeconds(sleep));
            while (WallNow < until) System.Threading.Thread.SpinWait(64);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4038</c> B9 (D-6, D-7) — the editor's cinema: the window manager's <see cref="Fdp.Presentation.WindowManager.WindowManager.Cinema"/>
    /// hides the menu bar and every panel (the map fills the window), and a frame overlay draws the card and the caption. ⭐ Esc stops the
    /// director (the only control left on screen).
    /// </summary>
    internal sealed class EditorCinema : ICinema
    {
        private readonly Func<Fdp.Presentation.WindowManager.WindowManager?> _wm;
        private string? _card, _caption;
        private bool _registered;

        public EditorCinema(Func<Fdp.Presentation.WindowManager.WindowManager?> wm) => _wm = wm;

        /// <summary>Esc was pressed while the cinema showed (the runner's owner stops it).</summary>
        public Action? EscapePressed { get; set; }

        public bool Active { get; private set; }

        public void Enter()
        {
            Active = true;
            if (_wm() is { } wm)
            {
                wm.Cinema = true;
                if (!_registered) { wm.RegisterFrameOverlay(Draw); _registered = true; }
            }
        }

        public void Exit()
        {
            Active = false;
            if (_wm() is { } wm) wm.Cinema = false;
        }

        public void Card(string? text) => _card = text;
        public void Caption(string? text) => _caption = text;

        private void Draw()
        {
            if (!Active) return;
            var io = ImGui.GetIO();
            var dl = ImGui.GetForegroundDrawList();
            var size = io.DisplaySize;
            var font = ImGui.GetFont();
            if (_card != null)
            {
                dl.AddRectFilled(Vector2.Zero, size, 0xFF000000);
                var lines = _card.Split('\n');
                float big = MathF.Max(18f, size.Y / 14f), small = MathF.Max(14f, size.Y / 24f);
                float y = size.Y * 0.4f;
                for (int i = 0; i < lines.Length; i++)
                {
                    float s = i == lines.Length - 1 ? big : small;
                    var w = font.CalcTextSizeA(s, float.MaxValue, 0f, lines[i]).X;
                    dl.AddText(font, s, new Vector2((size.X - w) / 2f, y), i == lines.Length - 1 ? 0xFFFFFFFF : 0xFFC8C8C8, lines[i]);
                    y += s * 1.4f;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_caption))
            {
                float s = MathF.Max(14f, size.Y / 30f);
                var w = font.CalcTextSizeA(s, size.X * 0.86f, size.X * 0.86f, _caption).X;
                var h = font.CalcTextSizeA(s, size.X * 0.86f, size.X * 0.86f, _caption).Y;
                float top = size.Y - h - s - size.Y / 18f;
                dl.AddRectFilled(new Vector2(0, top), new Vector2(size.X, top + h + s), 0x96000000);
                dl.AddText(font, s, new Vector2((size.X - w) / 2f, top + s / 2f), 0xFFFFFFFF, _caption, size.X * 0.86f);
            }
            if (FilmStudio.Fade is > 0f and var fade && _card == null)   // ⭐ T4 / TE-8 — a dip's black over the picture
                dl.AddRectFilled(Vector2.Zero, size, (uint)Math.Round(fade * 255f) << 24);
            dl.AddText(new Vector2(8, size.Y - 20), 0x80FFFFFF, "Esc: stop the director");
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) EscapePressed?.Invoke();
        }
    }

    /// <summary>⭐ <c>CE-4038</c> B10 — the take's layers on the map's own layer control (a transient override, never saved).</summary>
    internal sealed class EditorLayerOverride : ILayerOverride
    {
        private readonly Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> _layers;

        public EditorLayerOverride(Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> layers) => _layers = layers;

        /// <summary>Names the last <see cref="Apply"/> did not know.</summary>
        public IReadOnlyList<string> Unknown { get; private set; } = Array.Empty<string>();

        public void Apply(IReadOnlyList<string> on, IReadOnlyList<string> off) => Unknown = _layers()?.SetOverride(on, off) ?? Array.Empty<string>();
        public void Clear() => _layers()?.ClearOverride();
    }

    /// <summary>⭐ <c>CE-4038</c> B8 — frames through the film's mailbox: the map on screen writes the scene (or a card) in its own update.</summary>
    internal sealed class FilmFrameSink : IFrameSink
    {
        public Task Frame(string path, int width, int height, int supersample, string? caption)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, supersample, caption));

        public Task Card(string path, int width, int height, string text)
            => FilmStudio.Capture(new FilmCaptureRequest(path, width, height, 1, Card: text));
    }

    /// <summary>⭐ <c>CE-4038</c> B11 — the take's sound switch and the film's sound track, through <see cref="AudioDirector"/>.</summary>
    internal sealed class EditorDirectorSound : IDirectorSound
    {
        private readonly Func<double> _filmSimTime;

        /// <param name="filmSimTime">The film's sim time now (the item's sim time plus its offset on the one track).</param>
        public EditorDirectorSound(Func<double> filmSimTime) => _filmSimTime = filmSimTime;

        public void SetTakeSound(bool? on) => AudioDirector.TakeSound = on ?? (AudioDirector.Recorder != null ? true : null);

        public void BeginLog()
        {
            AudioDirector.Recorder = new SoundTrack(_filmSimTime);
            AudioDirector.TakeSound = true;
        }

        public void Reset()
        {
            AudioDirector.Recorder = null;
            AudioDirector.TakeSound = null;
        }

        public bool EndLog(string wavPath, IReadOnlyList<double> frameSimTimes, int fps)
        {
            var track = AudioDirector.Recorder;
            AudioDirector.Recorder = null;
            AudioDirector.TakeSound = null;
            return track != null && track.RenderWav(wavPath, frameSimTimes, fps);
        }
    }
}
