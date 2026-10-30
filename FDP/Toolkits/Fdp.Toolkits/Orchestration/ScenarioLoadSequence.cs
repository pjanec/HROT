using System;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-295</c> — <b>the one way to load a scenario</b>: if the cluster is not <see cref="ClusterState.Idle"/>,
    /// first ask for <c>Idle</c> (the unload), wait until the cluster reports <c>Idle</c>, then ask for the load.
    /// 📄 <c>docs/DESIGN_Cluster_Load_Phase.md</c> §9.
    /// <para>🔒 User, <c>2026-10-05</c>: the planner is right to plan an EMPTY path when the cluster is already in the
    /// target state, and the master stays literal — <i>"Isn't the OpenForEdit style cleaner?"</i> · <i>"Ok shared
    /// helper. No refusal to load when not in idle."</i> ⇒ the CALLER says what it wants in two plain requests. This
    /// class is that sequence, written once for every caller (the editor session, the debug API routes, the
    /// orchestrator panel) — it was <c>EditorScenarioSession.OpenForEdit</c>'s alone, and every other load sent a single
    /// request that, from Live or Edit, only copied the files.</para>
    /// <para>⭐ Transport-free: it sends through <paramref name="send"/> and reads the state through
    /// <paramref name="currentState"/>, so a bus caller and a network caller use the same class. The caller PUMPS it
    /// (<see cref="Pump"/>) from its own loop.</para>
    /// </summary>
    public sealed class ScenarioLoadSequence
    {
        private readonly Action<TransitionStateIntent> _send;
        private readonly Func<ClusterState> _currentState;
        private readonly Action<TransitionStateIntent>? _beforeLoad;
        private TransitionStateIntent? _pending;

        /// <param name="send">Sends one state-change request to the cluster master.</param>
        /// <param name="currentState">The cluster state as this caller sees it.</param>
        /// <param name="beforeLoad">Optional: runs with the load just before it goes out (the editor clears its world).</param>
        public ScenarioLoadSequence(Action<TransitionStateIntent> send, Func<ClusterState> currentState, Action<TransitionStateIntent>? beforeLoad = null)
        {
            _send         = send ?? throw new ArgumentNullException(nameof(send));
            _currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
            _beforeLoad   = beforeLoad;
        }

        /// <summary>True while a load waits for the cluster to reach <c>Idle</c>.</summary>
        public bool IsWaitingForIdle => _pending.HasValue;

        /// <summary>The scenario of the waiting load, or null.</summary>
        public string? PendingScenario => _pending?.ScenarioId;

        /// <summary>
        /// Loads <paramref name="load"/> (a request that names a scenario). From <c>Idle</c> it goes out now; from any
        /// other state an <c>Idle</c> request goes out and the load waits for <see cref="Pump"/> to see <c>Idle</c>.
        /// A newer load replaces a waiting one.
        /// </summary>
        public void Request(TransitionStateIntent load)
        {
            if (string.IsNullOrWhiteSpace(load.ScenarioId))
                throw new ArgumentException("A scenario load names its scenario (ScenarioId).", nameof(load));

            if (_currentState() == ClusterState.Idle)
            {
                _pending = null;
                SendLoad(load);
                return;
            }

            _pending = load;
            Unload();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4101</c> (AQ108 §2.11a) — <b>the ONE Idle intent</b>: "unload to <see cref="ClusterState.Idle"/>". A fresh <c>TransactionId</c> each time, nothing else set. Built HERE and nowhere else:
        /// the load's first half (<see cref="Request"/>), New Exercise, the transport's raw ■, the film director's stop and HTTP <c>/replay/stop</c> all send THIS (⛔ not a hand-written
        /// <c>new TransitionStateIntent { TargetState = Idle }</c> beside it — the master's own bootstrap unload, <c>ClusterMaster</c>, is the master and stays its own).
        /// </summary>
        public static TransitionStateIntent UnloadIntent() => new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.Idle };

        /// <summary>
        /// ⭐⭐ <c>CE-3354</c> ⑥ — <b>the ONE live-load intent</b>, built here and nowhere else: <c>POST /scenario/load/live</c> and the command line's startup load (<c>--scenario</c> / <c>--play</c>) both send THIS.
        /// A fresh <c>ExerciseId</c> (a live load IS a new exercise run); <c>TimeMode = "Deterministic"</c> when <paramref name="startPaused"/> (the clock is reset to 0 either way — Q86 §4-G — and a paused load
        /// waits for ▶), <see langword="null"/> when the load must RUN once loaded (the master runs it when every node's load succeeded, CE-3354 ⑤); <paramref name="forceRecording"/> is the scenario verifier's
        /// (CE-4090 D14 §5a: it records whatever the cluster's "Record live runs" setting says).
        /// </summary>
        public static TransitionStateIntent LiveLoadIntent(string scenario, bool startPaused = true, bool forceRecording = false) => new()
        {
            TransactionId  = Guid.NewGuid(),
            TargetState    = ClusterState.OperatingLive,
            ScenarioId     = scenario,
            ExerciseId     = Guid.NewGuid(),
            TimeMode       = startPaused ? "Deterministic" : null,
            ForceRecording = forceRecording,
        };

        /// <summary>⭐⭐ <c>CE-3354</c> ⑥ — <b>the ONE edit-load intent</b> (HTTP's and the command line's): <c>ExerciseId</c> is <see cref="Guid.Empty"/>, mirroring the orchestrator panel's "Load into Edit" — authoring is not an exercise run.</summary>
        public static TransitionStateIntent EditLoadIntent(string scenario) => new()
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = ClusterState.OperatingEdit,
            ScenarioId    = scenario,
            ExerciseId    = Guid.Empty,
        };

        /// <summary>⭐ CE-4101 — sends <see cref="UnloadIntent"/> through this sequence's sender (does not touch a waiting load).</summary>
        public void Unload() => _send(UnloadIntent());

        /// <summary>Sends the waiting load once the cluster is <c>Idle</c>. Returns true on the call that sent it.</summary>
        public bool Pump()
        {
            if (_pending is not { } load || _currentState() != ClusterState.Idle) return false;
            _pending = null;
            SendLoad(load);
            return true;
        }

        /// <summary>Drops a waiting load (e.g. the operator asked for a fresh exercise instead).</summary>
        public void Cancel() => _pending = null;

        private void SendLoad(TransitionStateIntent load)
        {
            _beforeLoad?.Invoke(load);
            _send(load);
        }
    }
}
