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
    internal sealed class EditorDirectorHost : IDirectorHost, IVerifyHost   // ⭐ CE-4061 — the editor hosts the film AND the verifier
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
