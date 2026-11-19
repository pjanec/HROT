using System;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Replication.Utilities;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ⭐⭐ <c>R-4012</c> (Q91 §4-P) — what this node does for the current replay moment.
    /// </summary>
    public enum ReplaySpanState
    {
        /// <summary>Nothing decided yet (the replay has just been prepared).</summary>
        Unsettled,
        /// <summary>The moment is inside this node's recording: it restores the world itself, ingress gated.</summary>
        Restoring,
        /// <summary>The moment is outside this node's recording (or it has none): world cleared, taken from the owners' flood.</summary>
        Listening,
    }

    /// <summary>
    /// Per-frame playback tick system that advances the <see cref="PlaybackController"/>
    /// one frame per Execute call (Strategy A) or jumps directly to a target frame
    /// when the gap exceeds <see cref="StrategyBThreshold"/> (Strategy B).
    /// <para>
    /// Strategy A (small gap, ≤ <see cref="StrategyBThreshold"/> frames):
    /// sequential <see cref="PlaybackController.StepForward"/> calls — all delta
    /// frames are applied in-memory; no intermediate frames are rendered.
    /// </para>
    /// <para>
    /// Strategy B (large gap): direct <see cref="PlaybackController.SeekToFrame"/>
    /// which binary-searches the keyframe index, blasts the keyframe chunks into
    /// <c>NativeChunkTable</c>, then applies ≤ 59 delta frames.
    /// </para>
    /// <para>
    /// The indexing cursor is derived from the time controller's
    /// <c>TotalTime</c> (seconds, relative to the replay's start), anchored to an
    /// absolute wall-clock tick and converted via <c>TimeSpan.TicksPerSecond</c>
    /// (100-ns UTC ticks).  This makes replay natively speed-controllable and pauseable:
    /// fast-forward, slow-motion, and pausing are driven purely by the time controller's
    /// <c>TimeScale</c> — no manual frame-injection is needed.
    /// During a seek the time controller must be seeded with the target time
    /// (see <see cref="ReplayModule.SeekToWallClockTicksAsync"/>).
    /// </para>
    /// <para>
    /// ⭐⭐ <c>R-4012</c> (Q91 §4-P) — <b>restore inside the span, listen outside it.</b> The anchor is the CLUSTER's
    /// (<see cref="AnchorWallTicks"/>, the earliest first frame across every node, sent by the master on each seek); until
    /// one arrives the node's own first frame is used, as before. Every tick asks <see cref="PlaybackController.Covers"/>:
    /// outside, the world is cleared once and left to ingress; coming back inside, the node seeks its own file (a keyframe
    /// rebuilds the whole world).
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public sealed class PlaybackTickSystem : IEcsModuleSystem
    {
        /// <summary>
        /// Frame-gap threshold above which Strategy B (SeekToFrame) is used
        /// instead of iterative StepForward calls.
        /// </summary>
        public const int StrategyBThreshold = ReplayClockRule.SeekThreshold;

        private readonly PlaybackController? _playback;
        private readonly ITimeController _timeController;
        private readonly Action? _afterSeek;

        /// <param name="playback">
        /// The <see cref="PlaybackController"/> to drive, or <c>null</c> when this node holds no recording of the replayed
        /// exercise — it then only listens (R-4012).
        /// </param>
        /// <param name="timeController">
        /// Active time controller whose <c>TotalTime</c> (seconds, relative to the replay's start) drives the pull-model
        /// cursor.  The value is anchored to <see cref="AnchorWallTicks"/> and converted via <c>TimeSpan.TicksPerSecond</c>
        /// (100-ns UTC ticks) before comparison against the recording index.
        /// </param>
        public PlaybackTickSystem(PlaybackController? playback, ITimeController timeController, Action? afterSeek = null)
        {
            _playback       = playback;
            _timeController = timeController;
            _afterSeek      = afterSeek;
        }

        /// <summary>
        /// ⭐ The replay timeline's zero, in wall-clock ticks — the CLUSTER's, set from each seek. 0 = not yet known: the
        /// node's own first frame is used.
        /// </summary>
        [field: Fdp.Core.SessionState("session state (the replay controller's own clock and mode, driven by its commands — not the world): cluster replay anchor sent by master on each seek")]
        public long AnchorWallTicks { get; set; }

        /// <summary>What this node is doing for the current replay moment.</summary>
        [field: Fdp.Core.SessionState("session state (the replay controller's own clock and mode, driven by its commands — not the world): replay span mode (listening/restoring), re-derived each tick")]
        public ReplaySpanState State { get; private set; } = ReplaySpanState.Unsettled;

        /// <summary>The anchor in use: the cluster's when known, else this recording's first frame, else 0.</summary>
        public long EffectiveAnchorWallTicks =>
            AnchorWallTicks != 0 ? AnchorWallTicks : _playback?.FirstFrameWallTicks ?? 0;

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = (EntityRepository)view;
            long targetTicks = ClockTicks();

            // ⭐⭐ CE-4056 — a seek is applied here at once, but the clock is moved to its landing only when the seek round completes. ⛔ Until
            //   then the clock still names the OLD moment, and a seek BACK was played forward to it again — and never returned once the clock
            //   was snapped below that frame (forward-only below). ⇒ hold the seek's frame while the clock still stands where it stood.
            if (_heldWhileClockAt is { } held)
            {
                if (_timeController.GetCurrentState().TotalTime == held) return;
                _heldWhileClockAt = null;
            }

            // ⭐ R-4012 — outside this node's recording: listen.
            if (_playback == null || !_playback.Covers(targetTicks))
            {
                Listen(repo);
                return;
            }

            // ⭐ Coming back inside after listening: the world holds ghosts and the cursor is stale — seek (a keyframe
            //   rebuilds everything).
            if (State == ReplaySpanState.Listening)
            {
                Restore(repo, targetTicks);
                return;
            }
            State = ReplaySpanState.Restoring; ReplayState.SetListening(repo, false);   // ⭐ CE-3329 P2

            long currentTicks = _playback.IsAtStart
                ? long.MinValue
                : _playback.GetFrameMetadata(_playback.CurrentFrame).WallClockTicks;

            // ⭐⭐ CE-5041 — the clock stands BEHIND the frame shown (a seek back whose hold was released by an EARLIER round's late snap, so the
            //   world was pulled forward first; 📌 measured in `--verify-replay`'s shuffled seeks: the clock trails the world by one or two
            //   rounds): restore back to it. ⛔ Forward-only, the world stayed ahead for good. A clock behind by less than a frame is the frame
            //   it names (the landing floors to a recorded frame).
            if (currentTicks != long.MinValue && _playback.CurrentFrame > 0 && targetTicks < currentTicks - FrameToleranceTicks)   // frame 0: nothing earlier to show
            {
                Restore(repo, targetTicks);
                return;
            }
            if (_playback.IsAtEnd) return;   // ⭐ CE-5041 — after the backward follow: a replay at its last frame still follows a clock sent back
            if (targetTicks <= currentTicks) return;

            // ⭐ CE-1479 — the frames due at this clock: ONE rule, shared with the Replay Browser (ReplayClockRule)
            int count = ReplayClockRule.FramesDue(_playback, targetTicks, StrategyBThreshold + 1);

            if (count > StrategyBThreshold)
            {
                // Strategy B: large gap — seek directly to target wall ticks.
                Restore(repo, targetTicks);
            }
            else
            {
                // Strategy A: small gap — step forward frame by frame.
                for (int i = 0; i < count && !_playback.IsAtEnd; i++)
                    _playback.StepForward(repo);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>R-4012</c> — a seek: restores this node's recording at <paramref name="wallTicks"/> when it covers the
        /// moment, else clears the world and listens. Returns whether the node restored.
        /// </summary>
        public bool SeekTo(EntityRepository repo, long wallTicks)
        {
            if (_playback == null || !_playback.Covers(wallTicks))
            {
                Listen(repo);
                return false;
            }
            Restore(repo, wallTicks);
            HoldUntilTheClockMoves();
            return true;
        }

        /// <summary>⭐ CE-5041 — how far behind the frame shown the clock may stand and still name it (half a 60 Hz frame).</summary>
        public const long FrameToleranceTicks = TimeSpan.TicksPerSecond / 120;

        /// <summary>The moment the clock names, in wall ticks of the recording (the anchor + the clock's seconds after the zero).</summary>
        private long ClockTicks() => EffectiveAnchorWallTicks + (long)Math.Round(_timeController.GetCurrentState().TotalTime * TimeSpan.TicksPerSecond);
        //   ⭐ CE-5041 — ROUNDED: a clock snapped exactly onto a frame reads its seconds back as a double, and truncation could name the tick
        //   BEFORE the frame — the pull then stopped on the previous frame (📌 measured: `ua-civ-melee` stood at 26.9833 for 27)

        // ⭐ CE-4056 — the clock's reading (seconds after the zero) when a seek was applied; cleared once the clock moves. The clock's own
        //   reading, not anchor + clock: the anchor may be set around the seek.
        [Fdp.Core.SessionState("session state (the replay controller's own clock and mode, driven by its commands — not the world): the clock reading while a seek round completes; cleared once the clock moves (CE-4056)")]
        private double? _heldWhileClockAt;

        private void HoldUntilTheClockMoves() => _heldWhileClockAt = _timeController.GetCurrentState().TotalTime;

        /// <summary>A seek to a frame index (always inside the recording): restores it.</summary>
        public void SeekToFrame(EntityRepository repo, int frameIndex)
        {
            if (_playback == null) return;
            _playback.SeekToFrame(repo, frameIndex);
            SmartEgressUtil.ForceMarkAllDirty(repo);
            _afterSeek?.Invoke();
            HoldUntilTheClockMoves();   // ⭐ CE-4056 — the same seek round moves the clock after this
            State = ReplaySpanState.Restoring; ReplayState.SetListening(repo, false);   // ⭐ CE-3329 P2
        }

        private void Restore(EntityRepository repo, long wallTicks)
        {
            _playback!.SeekToWallClockTicks(repo, wallTicks);
            SmartEgressUtil.ForceMarkAllDirty(repo);   // the owners re-flood what they restored
            _afterSeek?.Invoke();
            if (State != ReplaySpanState.Restoring)
                Fdp.Core.Logging.FdpLog<PlaybackTickSystem>.Info(
                    "[Replay] R-4012: restoring from this node's recording at {0} (anchor {1}).", wallTicks, EffectiveAnchorWallTicks);
            State = ReplaySpanState.Restoring; ReplayState.SetListening(repo, false);   // ⭐ CE-3329 P2
        }

        /// <summary>
        /// ⭐ Leaves the restored world: the same world operation a keyframe performs (<c>SoftClear</c>, singletons kept),
        /// then the seek's after-hook (entity map rebuilt — now empty — and lifecycle bookkeeping reset). ⛔ NOT the world
        /// boundary clear: that advances <c>WorldEpoch</c>, which writes deletes on the wire (CE-4000 F2) and would remove
        /// the entities for every listener. Once per entry into listening — the ingress world is then left alone.
        /// </summary>
        private void Listen(EntityRepository repo)
        {
            if (State == ReplaySpanState.Listening) return;
            repo.SoftClear();
            _afterSeek?.Invoke();
            State = ReplaySpanState.Listening; ReplayState.SetListening(repo, true);   // ⭐ CE-3329 P2
            Fdp.Core.Logging.FdpLog<PlaybackTickSystem>.Info(
                "[Replay] R-4012: this moment is outside this node's recording — world cleared, listening (span {0}).",
                _playback == null ? "none" : $"{_playback.FirstFrameWallTicks}..{_playback.LastFrameWallTicks}");
        }
    }
}
