using System;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Orchestration;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-3329</c> P3 (row 16, §3c) — During Action Review on a node: the live run paused, its FULL checkpoint in RAM, its OWN
    /// recording replayed in place, and the return to exactly where it paused.</b>
    /// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §3c, §5 R5–R9, §11 F1–F11, §12.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <c>PrepareReview</c>: wait for the master's barrier pause to land here (F5) → force the labelled keyframe <c>review-&lt;n&gt;</c>
    /// (a lost review is restorable — D-C) and wait for it → suspend the recorder and flush (the file stays open, R8) → capture the FULL
    /// checkpoint (<see cref="CaptureOptions.Recording"/>: every recorded type AND the singletons — F1) → hold egress
    /// (§12 W2) → install the replay of the open recording through the SAME controller an ordinary replay uses. Commit: the SAME gates
    /// (<see cref="ReferenceReplayLoadHandler.EnterReplayGates"/>, F9).</para>
    /// <para>⭐ <c>FinalizeReview</c>: remove the replay (awaited, F8) → Commit, on the frame thread: hold egress, put the checkpoint back
    /// with the CHECKPOINT restore's semantics (<see cref="ReferenceCheckpointHandler.PutBack"/>: <c>AfterRestore</c> — which republishes,
    /// W4 —, the ELM's re-derive, <c>ResumeAfterRestore</c>, CE-3316), the gates off, the recorder resumed (a keyframe, the review's
    /// destructions dropped — F11). The master puts the clock back to L. The cleanup's sweep retracts what the review showed and live
    /// lacks (W1); the egress hold is released by the master's next broadcast (W2).</para>
    /// <para>⭐ D-C / F4 — the node drops to Idle during a review (a lost master or node): the replay is removed and the RAM checkpoint
    /// dropped; the recording stays as written up to the live point, and <c>review-&lt;n&gt;</c> is in it — restorable by the ordinary
    /// checkpoint restore. Nothing automatic.</para>
    /// <para>⚠ Not built here (design §12i): the quiesce round (F6), W3 (acks), W5 (the readers' state discard), episode recorders (F11).</para>
    /// </remarks>
    public sealed class ReferenceReviewHandler : IClusterStateHandler, IClusterStateUpdateObserver, IIdleResetHandler
    {
        private readonly IRecordReplayController _controller;
        private readonly Func<RecordingModule?> _recording;
        private readonly ReferenceReplayLoadHandler _gates;
        private readonly EntityRepository _world;
        private readonly Fdp.Core.EventAccumulator? _accumulator;
        private readonly Action<bool>? _worldReplaced;
        private readonly Action<FdpEventBus>? _resumeSchedule;
        private readonly string _storageDirectory;

        private WorldKeyframe? _livePoint;           // the FULL checkpoint of L — in RAM for the review's duration
        private int _reviews;                        // review-<n>
        private double _liveSimSeconds;              // L's sim time — the recorder resumes when the clock is back there
        private volatile bool _awaitingCommit;

        /// <summary>How long a node waits for the master's pause / its forced keyframe before it gives up the review.</summary>
        public static TimeSpan Patience { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>True while this node holds a review's checkpoint of the live point.</summary>
        public bool InReview => _livePoint != null;

        /// <summary>The label of the keyframe the last review forced at its live point (D-C).</summary>
        public string? LastReviewLabel { get; private set; }

        public ReferenceReviewHandler(
            IRecordReplayController controller, Func<RecordingModule?> recording, ReferenceReplayLoadHandler gates,
            EntityRepository world, Fdp.Core.EventAccumulator? accumulator, Action<bool>? worldReplaced,
            Action<FdpEventBus>? resumeSchedule, string storageDirectory)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _gates = gates ?? throw new ArgumentNullException(nameof(gates));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _accumulator = accumulator;
            _worldReplaced = worldReplaced;
            _resumeSchedule = resumeSchedule;
            _storageDirectory = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
        }

        public bool CanHandle(NodeOpType operation) => operation is NodeOpType.PrepareReview or NodeOpType.FinalizeReview;

        public bool CanHandle(ExecuteNodeOpIntent intent) => CanHandle(intent.Operation);

        public async Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            if (intent.Operation == NodeOpType.PrepareReview) return await EnterAsync(intent, ct).ConfigureAwait(false);
            if (intent.Operation == NodeOpType.FinalizeReview)
            {
                await _controller.TeardownReplayAsync().ConfigureAwait(false);   // F8 — gone before the checkpoint goes back
                return null;
            }
            return null;
        }

        private async Task<object?> EnterAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            var recording = _recording() ?? throw new InvalidOperationException("a review needs this node's live recording, and none is running");
            var exercise = recording.ExerciseId;

            // F5 — capture on the PAUSED step: the master's barrier pause lands here within its look-ahead
            await WaitUntil(() => _world.HasSingletonUnmanaged<GlobalTime>() && _world.GetSingletonUnmanaged<GlobalTime>().IsHalted, "the pause", ct)
                .ConfigureAwait(false);

            // D-C — the live point as a labelled keyframe of the recording: a review lost to a failure is restorable from it
            LastReviewLabel = $"review-{++_reviews}";
            recording.RequestCheckpoint(LastReviewLabel, atWallTicks: 0);
            await WaitUntil(() => recording.PendingCheckpoints == 0, $"keyframe {LastReviewLabel}", ct).ConfigureAwait(false);
            recording.SuspendForReview();   // R8 — nothing written while the world is the replayed one; flushed, the file stays open

            // F1 / D-B — the FULL checkpoint: every recorded type and the singletons (⛔ not the dry run's NodeDryRun)
            _livePoint = WorldKeyframes.Capture(_world, CaptureOptions.Recording);
            _liveSimSeconds = _world.GetSingletonUnmanaged<GlobalTime>().TotalTime;

            EgressHold.Hold(_world, "review entry");   // W2 — published again after the master's commit
            _awaitingCommit = true;
            await _controller.PrepareReplayAsync(exercise, _storageDirectory).ConfigureAwait(false);

            FdpLog<ReferenceReviewHandler>.Info("[Review] entered: the live point is '{0}' in exercise {1}; the recording is suspended.",
                LastReviewLabel, exercise);
            return new ReplayPrepareResult(_controller.ActiveMaxNetworkId, _controller.ActiveReplayDurationSeconds,
                _controller.ActiveRecordingStartWallTicks, _controller.ActiveRecordingEndWallTicks, _controller.ActiveReplayTimeMap);
        }

        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.PrepareReview)
            {
                _gates.EnterReplayGates();   // F9 — the gates before the playback's first frame: the module installs on a later tick
                return;
            }
            if (intent.Operation != NodeOpType.FinalizeReview || _livePoint is not { } live) return;

            EgressHold.Hold(_world, "review exit");   // W2
            _awaitingCommit = true;
            ReferenceCheckpointHandler.PutBack(_world, live, _accumulator, _worldReplaced, _resumeSchedule);   // D-B: the checkpoint's return
            _gates.LeaveReplayGates();
            _recording()?.ResumeAfterReview(_liveSimSeconds);   // records again once the master's clock is back at L
            _livePoint = null;
            FdpLog<ReferenceReviewHandler>.Info("[Review] returned to the live point '{0}'.", LastReviewLabel ?? "?");
        }

        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            if (intent.Operation == NodeOpType.PrepareReview) Abandon("the review's prepare was aborted");
        }

        /// <inheritdoc />
        public void OnEnteredIdle()
        {
            if (InReview) Abandon("the node dropped to Idle during a review");
        }

        /// <inheritdoc />
        public void OnClusterStateUpdate(ClusterState state, bool masterGone)
        {
            if (!_awaitingCommit) return;
            _awaitingCommit = false;
            EgressHold.Release(_world);
        }

        // D-C / F4 — a review that cannot complete: the replay goes, the RAM checkpoint is dropped, the recording is resumed only to be
        //   finalised by whoever ends the session; review-<n> stays in it as the way back.
        private void Abandon(string why)
        {
            _ = _controller.TeardownReplayAsync();
            _gates.LeaveReplayGates();
            _livePoint = null;
            EgressHold.Release(_world);
            FdpLog<ReferenceReviewHandler>.Warn("[Review] abandoned — {0}. The live point is the keyframe '{1}' of the recording.", why, LastReviewLabel ?? "?");
        }

        private static async Task WaitUntil(Func<bool> condition, string what, CancellationToken ct)
        {
            var started = DateTime.UtcNow;
            while (!condition())
            {
                if (DateTime.UtcNow - started > Patience) throw new TimeoutException($"a review waited {Patience.TotalSeconds:F0} s for {what}");
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }
    }
}
