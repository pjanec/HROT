using System;
using System.Collections.Generic;
using System.Linq;
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
    internal sealed class EditorDirectorHost : IDirectorHost, IVerifyHost, IDirectorLoadOutcome,   // ⭐ CE-4061 — the editor hosts the film AND the verifier
                                               Hrot.Presentation.DebugApi.IRecordingOpener              // ⭐ CE-11023 — and opens recordings through the start screen's door
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
        private readonly Func<Fdp.Toolkit.Vis3D.Map3DBuild?>? _mapBuild;
        private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

        public EditorDirectorHost(Func<EntityRepository?> world, Action<string> load, Func<bool> operatingEdit, Func<bool> inPreview,
                                  Action<bool> enterPreview, Action exitPreview, Func<bool> paused, Action pause, Action<float> stepBy,
                                  Func<int, string?> behaviourName, TakeStore takes, Func<bool> slotRunInFlight,
                                  Action<string, string> writeCheckpoint, Func<string, double> restoreCheckpoint,
                                  Action<string> loadLive, Hrot.Presentation.DebugApi.IClusterMasterControl master, Func<Fdp.Toolkit.Orchestration.ClusterState> state,
                                  Func<string?> recordingPath, Func<string, IRecordedRun> openRecording,
                                  Func<Fdp.Toolkit.Vis3D.Map3DBuild?>? mapBuild = null,
                                  Func<Hrot.UI.Common.RunMode.ScenarioDoor?>? door = null, Func<string?>? loadedScenario = null,
                                  Func<Hrot.UI.Common.RunMode.RunGate?>? gate = null, Func<Guid>? replayExercise = null)
        {
            _door = door;
            _gate = gate;
            _replayExercise = replayExercise;
            _loadedScenario = loadedScenario;
            _asker = new DirectorAsker(this);
            _mapBuild = mapBuild;
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

        public void LoadScenario(string scenario)
        {
            _loaded = true;
            LoadWent = false; LoadRefusal = null; LoadMayRetry = false;
            // ⭐ CE-4267 — through the scenario DOOR with the director's OWN asker: the UI's asker made every question an invisible modal behind the
            //   cinema (the load never went, later loads were dropped while it stayed open) — this one answers or says why, and `then` says it went
            if (_door?.Invoke() is { } door) { door.Open(scenario, _asker, then: () => { LoadWent = true; _lastOwnLoad = scenario; }); return; }
            _load(scenario);
            LoadWent = true; _lastOwnLoad = scenario;
        }

        // ── ⭐ CE-4267 — the load's outcome (IDirectorLoadOutcome) ──
        private readonly Func<Hrot.UI.Common.RunMode.ScenarioDoor?>? _door;
        private readonly DirectorAsker _asker;
        private readonly Func<string?>? _loadedScenario;
        private string? _lastOwnLoad;
        public bool LoadWent { get; private set; }
        public string? LoadRefusal { get; private set; }
        public bool LoadMayRetry { get; private set; }

        /// <summary>
        /// ⭐ <c>CE-4267</c> — the director's answers to the door: <b>not ready</b> (the run gate) ⇒ wait and ask again; <b>unsaved changes</b> of a scenario the director
        /// itself loaded (its previous take) ⇒ Discard (they are the take's preview, not the user's work); the user's OWN unsaved edits, or a run in progress ⇒ the load
        /// is refused WITH the reason (shown on the take's card) — ⛔ never a question nobody sees, never a silent discard of the user's work.
        /// </summary>
        internal sealed class DirectorAsker : Hrot.UI.Common.RunMode.IDoorAsker
        {
            private readonly EditorDirectorHost _host;
            public DirectorAsker(EditorDirectorHost host) => _host = host;
            public bool DiscardsUnsaved => false;
            public bool CanPrompt => false;   // ⭐ CE-4099 A7 — nobody sees a take's question: the autosave of its scenario is never offered (the take loads the saved one)
            public void Ask(Hrot.UI.Common.RunMode.DoorQuestion question)
            {
                switch (question.Kind)
                {
                    case Hrot.UI.Common.RunMode.DoorQuestionKind.Unsaved or Hrot.UI.Common.RunMode.DoorQuestionKind.NeverSaved
                        when _host._lastOwnLoad is { } own && string.Equals(own, _host._loadedScenario?.Invoke(), StringComparison.Ordinal)
                             && question.Buttons.FirstOrDefault(b => b.Label == "Discard") is { Action: { } discardAction }:
                        discardAction();   // the director's own previous take
                        return;
                    case Hrot.UI.Common.RunMode.DoorQuestionKind.Refused:
                        _host.LoadRefusal = "the editor is not ready (" + question.Text + ")";
                        _host.LoadMayRetry = true;
                        return;
                    case Hrot.UI.Common.RunMode.DoorQuestionKind.StopAndLoad:
                        // ⭐ measured on the reel: every take after a played one met its OWN preview still leaving (the exit takes frames) ⇒ a WAIT,
                        //   asked again; a run the user started stays and the take fails after the load cap WITH this reason
                        _host.LoadRefusal = "a run is in progress" + (question.Run is { } run ? " (" + run + ")" : "") + " — stop it, then play again";
                        _host.LoadMayRetry = true;
                        return;
                    default:
                        _host.LoadRefusal = question.Text + " — save or discard it in the editor, then play again";
                        return;
                }
            }
        }
        // ── ⭐⭐ CE-11023 (Q113-G) — the editor's RECORDING OPENER: the start screen's door into AAR (ScenarioDoor.OpenRecording), with the director's
        //    OWN asker — the same shape as its scenario load (CE-4267): ask, retry a wait, refuse with the reason. The open runs through the cluster,
        //    so it is Opening until the cluster operates the replay of THAT exercise and the shown map is built (CE-11025 made its terrain resident). ──
        private readonly Func<Hrot.UI.Common.RunMode.RunGate?>? _gate;
        private readonly Func<Guid>? _replayExercise;
        private Guid _openWant;
        private double? _openAt;
        private bool _openWent;
        private double _openAskedAt;
        private string? _openProgress;
        private double _openProgressAt;
        private Fdp.Toolkit.Orchestration.ClusterState _openStateSeen;

        /// <summary>⭐ CE-11023 — the cluster's load progress (the run gate's), while it loads.</summary>
        public string? LoadProgress => _gate?.Invoke() is { Loading: true } g ? g.Progress : null;

        /// <summary>⭐ CE-11023 — the cluster's ERROR state (R-3073) with its reason.</summary>
        public string? LoadError => _gate?.Invoke() is { InError: true } g ? g.ErrorReason : null;

        public string? Open(Hrot.Presentation.DebugApi.RecordingName recording, double atSeconds)
        {
            if (recording is null) throw new ArgumentNullException(nameof(recording));
            Guid id = recording.IsExercise ? recording.ExerciseId : ExerciseOf(recording.Paths);
            if (id == Guid.Empty)
                return recording.IsExercise
                    ? "the editor opens a recording by its exercise id (GET /exercises), not the newest"
                    : "its files name no exercise (no readable .meta.json beside them)";
            if (_door?.Invoke() is not { } door) return "the editor has no scenario door";
            _openWant = id; _openAt = atSeconds > 0 ? atSeconds : null; _openWent = false;
            _openProgress = null; _openProgressAt = WallNow; _openStateSeen = _state();
            AskOpen(door);
            if (LoadRefusal is { } why && !LoadMayRetry) { _openWant = Guid.Empty; return why; }
            return null;
        }

        private void AskOpen(Hrot.UI.Common.RunMode.ScenarioDoor door)
        {
            LoadRefusal = null; LoadMayRetry = false;
            _openAskedAt = WallNow;
            var want = _openWant;
            door.OpenRecording(want, _openAt, _asker, then: () => { if (_openWant == want) _openWent = true; });
        }

        public Hrot.Presentation.DebugApi.RecordingName? Shown
            => IsOperatingReplay && _replayExercise?.Invoke() is { } shown && shown != Guid.Empty ? Hrot.Presentation.DebugApi.RecordingName.Exercise(shown) : null;

        public bool Opening
        {
            get
            {
                if (_openWant == Guid.Empty) return false;
                if (!_openWent)
                {
                    // a wait (the run gate, the previous take's preview still leaving): asked again, as the scenario load is
                    if (LoadMayRetry && WallNow - _openAskedAt >= DirectorRunner.LoadRetrySeconds && _door?.Invoke() is { } door) AskOpen(door);
                    return true;
                }
                bool ready = IsOperatingReplay && _replayExercise?.Invoke() == _openWant && (MapBuild?.IsBuilt ?? true);
                if (ready) { _openWant = Guid.Empty; return false; }
                var progress = LoadProgress;   // the stall clock: any change of the cluster's state or load step is progress
                var state = _state();
                if (!string.Equals(progress, _openProgress, StringComparison.Ordinal) || state != _openStateSeen)
                { _openProgress = progress; _openStateSeen = state; _openProgressAt = WallNow; }
                return true;
            }
        }

        public string? OpenFailure
        {
            get
            {
                if (_openWant == Guid.Empty) return null;
                if (LoadError is { } error) return $"the cluster is in error — {error}";
                if (!_openWent)
                {
                    if (LoadRefusal is { } why && !LoadMayRetry) return why;
                    if (WallNow - _openAskedAt > DirectorRunner.LoadTimeoutSeconds && WallNow - _openProgressAt > DirectorRunner.LoadTimeoutSeconds)
                        return $"the editor did not take the open within {DirectorRunner.LoadTimeoutSeconds:0} s" + (LoadRefusal is { } wait ? $": {wait}" : "");
                    return null;
                }
                if (WallNow - _openProgressAt > DirectorRunner.LoadStallSeconds)
                    return $"the replay's load made no progress for {DirectorRunner.LoadStallSeconds:0} s" + (_openProgress is { Length: > 0 } at ? $" (at: {at})" : "");
                return null;
            }
        }

        /// <summary>⭐ CE-11023 — the exercise a recording's files belong to: the first one's <c>.meta.json</c>; Empty when none reads.</summary>
        internal static Guid ExerciseOf(IReadOnlyList<string> paths)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    var meta = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(System.IO.File.ReadAllText(paths[i] + ".meta.json"));
                    if (meta != null && (Guid)meta.ExerciseId != Guid.Empty) return (Guid)meta.ExerciseId;
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            }
            return Guid.Empty;
        }

        public bool IsOperatingEdit => _operatingEdit();
        /// <summary>The director loaded one, or the user did (the editor is then OperatingEdit).</summary>
        public bool HasLoadedAScenario => _loaded || _operatingEdit();
        public int? EntityCount => _world()?.EntityCount;
        /// <summary>⭐ R-1046 — the editor's shown map's 3-D build (null headless: no map).</summary>
        public Fdp.Toolkit.Vis3D.Map3DBuild? MapBuild => _mapBuild?.Invoke();
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
        // ⭐ CE-4101 — the director stops a LIVE run (no scenario is being edited there, so the door has nothing to ask): the ONE Idle intent, not a hand-built copy
        public void StopLive() => _master.Transition(Fdp.Toolkit.Orchestration.ScenarioLoadSequence.UnloadIntent());

        // ⭐ CE-4045 H5 — the review through the ONE construction of its intents (the same as POST /review and the facade)
        public void EnterReview(double simSeconds) => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReview(simSeconds: simSeconds));
        public void SeekReview(double simSeconds) => _master.SeekReplay(Hrot.UI.Common.Facades.ReplaySessionIntents.Seek(0, simSeconds));
        public void ExitReview() => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.ExitReview());
        public bool IsOperatingReview => _state() == Fdp.Toolkit.Orchestration.ClusterState.OperatingReview;
        // ⭐ CE-4045 H1b — the cluster replay of the finished run (the master picks the newest exercise); a seek is SeekReview's
        public void EnterReplay(double simSeconds) => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReplay(Guid.Empty, simSeconds: simSeconds));
        public bool IsOperatingReplay => _state() == Fdp.Toolkit.Orchestration.ClusterState.OperatingReplay;
        // ⭐ CE-4045 H3 — the D18 branch from the replay, through the same construction as every other go-live
        public void BranchLive() => _master.Transition(Hrot.UI.Common.Facades.ReplaySessionIntents.BranchLive());
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
}
