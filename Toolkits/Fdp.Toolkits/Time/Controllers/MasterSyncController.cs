using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Controllers
{
    /// <summary>
    /// Unified master time controller that subsumes <see cref="MasterTimeController"/>,
    /// <see cref="SteppedMasterController"/>, and <see cref="DistributedTimeCoordinator"/>.
    /// <para>
    /// Implements a state machine:
    /// <c>Continuous → BarrierPending → Stepping → Continuous</c>
    /// All DDS traffic flows through <see cref="FdpEventBus"/> — no direct DDS coupling.
    /// </para>
    /// </summary>
    public sealed class MasterSyncController : ISteppableTimeController
    {
        // ── State machine ────────────────────────────────────────────────────
        private enum MasterMode { Continuous, BarrierPending, Stepping }
        private MasterMode _mode = MasterMode.Continuous;

        private long _pendingBarrierWallTicks = -1;
        private readonly HashSet<int> _expectedSlaves;
        private HashSet<int> _pendingAcks;

        // ── Time state ───────────────────────────────────────────────────────
        private long   _frameNumber;
        private double _totalTime;
        private double _unscaledTotalTime;
        private float  _timeScale = 1.0f;
        private long   _totalWallTicks;
        private float  _pendingStepDelta;
        private float  _pendingUnscaledStepDelta;

        // ── Infrastructure ───────────────────────────────────────────────────
        private readonly FdpEventBus _eventBus;
        private readonly TimeConfig  _config;


        // ── Tick source (test seam; defaults to HighResUtcClock.GetTicks) ──────
        private long _lastTickSample;
        private readonly Func<long> _getTick;

        /// <summary>
        /// Constructs a <see cref="MasterSyncController"/>.
        /// </summary>
        /// <param name="eventBus">Shared event bus (must not be null).</param>
        /// <param name="slaveNodeIds">Node IDs of all slaves participating in lockstep. May be empty.</param>
        /// <param name="config">Time configuration; defaults to <see cref="TimeConfig.Default"/> when null.</param>
        /// <param name="tickSource">
        /// Optional override for <c>HighResUtcClock.GetTicks</c>. Inject a controlled counter
        /// in unit tests to avoid <c>Thread.Sleep</c>.
        /// </param>
        public MasterSyncController(
            FdpEventBus          eventBus,
            HashSet<int>?        slaveNodeIds = null,
            TimeConfig?          config       = null,
            Func<long>?          tickSource   = null)
        {
            _eventBus       = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _config         = config ?? TimeConfig.Default;
            _expectedSlaves = slaveNodeIds != null
                ? new HashSet<int>(slaveNodeIds)   // defensive copy
                : new HashSet<int>();
            _pendingAcks    = new HashSet<int>();  // starts empty; populated after each Step()
            _getTick        = tickSource ?? HighResUtcClock.GetTicks;

            // Pre-register event types for the types this controller publishes
            _eventBus.Register<SwitchTimeModeEvent>();
            // AdvanceFrameIntent and FrameStepCompletedEvent carry no [EventId] attribute —
            // they use the managed bus path (PublishManaged / ConsumeManaged). No registration needed.

            long now        = _getTick();
            _lastTickSample = now;
            _totalWallTicks = now;
            Fdp.Core.Logging.FdpLog<MasterSyncController>.Debug(
                "[TC3][Master] Initialized. _totalWallTicks={0}, TimeSpan.TicksPerSecond={1}",
                _totalWallTicks, TimeSpan.TicksPerSecond);

            // Bug 3 fix: broadcast the initial t=0 baseline so the DDS TransientLocal buffer
            // holds a valid reference for late-joining slaves.  Without this, IG and ExCon
            // that boot 200–500 ms after the Orchestrator have no anchor point and start their
            // clocks from an independent t=0, producing a permanent startup offset.
            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = _totalWallTicks,
                SimTimeSnapshot  = 0.0,
                TimeScale        = _timeScale,
                FixedDelta       = 0f,
            });
        }

        // ── ITimeController ──────────────────────────────────────────────────

        /// <summary>
        /// Advances the controller one frame.  In Continuous / BarrierPending: accumulates
        /// wall-clock and sim time, publishes <see cref="TimePulseDescriptor"/> at ~1 Hz.
        /// In Stepping: drains <see cref="FrameStepCompletedEvent"/> from bus to unlock the
        /// next <see cref="Step(float)"/> call.
        /// </summary>
        public GlobalTime Update()
        {
            // Drain bus-published time-control intents (HEXAG2-S011).
            // SlaveNodeSetUpdatedEvent must be consumed before PauseTimeIntent so
            // SwitchToDeterministic gets the correct slave roster.
            HashSet<int>? updatedSlaves = null;
            foreach (var ev in _eventBus.ReadManaged<SlaveNodeSetUpdatedEvent>())
                updatedSlaves = new HashSet<int>(ev.SlaveNodeIds);

            foreach (var _ in _eventBus.ReadManaged<PauseTimeIntent>())
                SwitchToDeterministic(updatedSlaves ?? new HashSet<int>(_expectedSlaves));
            foreach (var _ in _eventBus.ReadManaged<ResumeTimeIntent>())
                SwitchToContinuous();
            foreach (var ev in _eventBus.ReadManaged<StepTimeIntent>())
                Step(ev.DeltaSeconds);
            foreach (var ev in _eventBus.ReadManaged<SetTimeScaleIntent>())
                SetTimeScale(ev.TimeScale);

            long currentTicks  = _getTick();
            long elapsedTicks  = currentTicks - _lastTickSample;
            _lastTickSample    = currentTicks;

            double elapsedSeconds = (double)elapsedTicks / TimeSpan.TicksPerSecond;
            float  scaledDelta    = (float)(elapsedSeconds * _timeScale);

            return _mode switch
            {
                MasterMode.Continuous     => UpdateContinuous(currentTicks, elapsedTicks, elapsedSeconds, scaledDelta),
                MasterMode.BarrierPending => UpdateBarrierPending(currentTicks, elapsedTicks, elapsedSeconds, scaledDelta),
                MasterMode.Stepping       => UpdateStepping(),
                _                         => GetCurrentState(),
            };
        }

        public void SetTimeScale(float scale)
        {
            if (scale < 0.0f)
                throw new ArgumentException("TimeScale cannot be negative.", nameof(scale));
            _timeScale = scale;
        }

        public float    GetTimeScale()    => _timeScale;

        /// <summary>
        /// Returns <see cref="TimeMode.Continuous"/> for both the <c>Continuous</c> and
        /// <c>BarrierPending</c> internal states (the pending barrier is an implementation
        /// detail not exposed via the public API). Returns <see cref="TimeMode.Deterministic"/>
        /// only when in the <c>Stepping</c> state.
        /// </summary>
        public TimeMode GetMode()         => _mode == MasterMode.Stepping
                                            ? TimeMode.Deterministic
                                            : TimeMode.Continuous;

        public GlobalTime GetCurrentState() => BuildGlobalTime(0.0f, 0.0f);

        /// <summary>
        /// Seeds the controller with a previously saved time state (e.g. after scenario load).
        /// Resets the tick baseline so the next <see cref="Update()"/> measures a fresh delta.
        /// </summary>
        public void SeedState(GlobalTime state)
        {
            _frameNumber        = state.FrameNumber;
            _totalTime          = state.TotalTime;
            _unscaledTotalTime  = state.UnscaledTotalTime;
            _timeScale          = state.TimeScale;
            _totalWallTicks     = state.TotalWallTicks;

            long now            = _getTick();
            _lastTickSample     = now;
        }

        public void Dispose() { /* no unmanaged resources */ }

        // ── ISteppableTimeController ─────────────────────────────────────────

        /// <summary>
        /// Advances one deterministic step of <paramref name="fixedDelta"/> seconds.
        /// Only valid in <c>Stepping</c> mode — silently returns current state in other modes.
        /// Blocked while the previous step's <see cref="FrameStepCompletedEvent"/> ACKs are
        /// still outstanding (non-empty <c>_pendingAcks</c>).
        /// </summary>
        public GlobalTime Step(float fixedDelta)
        {
            if (_mode != MasterMode.Stepping)
                return GetCurrentState();

            if (_pendingAcks.Count > 0)
                return GetCurrentState();

            _frameNumber++;
            float scaledDelta       = fixedDelta * _timeScale;
            _totalTime             += scaledDelta;
            _unscaledTotalTime     += fixedDelta;
            _totalWallTicks        += (long)(fixedDelta * TimeSpan.TicksPerSecond);
            _pendingStepDelta      += scaledDelta;
            _pendingUnscaledStepDelta += fixedDelta;

            _eventBus.PublishManaged(new AdvanceFrameIntent
            {
                FrameID       = _frameNumber,
                FixedDelta    = fixedDelta,
                TargetSimTime = _totalTime,
            });

            // Re-arm the pending ACK set so the next Step() blocks until all slaves confirm.
            _pendingAcks = new HashSet<int>(_expectedSlaves);

            Fdp.Core.Logging.FdpLog<MasterSyncController>.Debug(
                "[TC3][Master] STEP #{0}. TargetSimTime={1}, Delta={2:F4}s, AwaitingACKs=[{3}]",
                _frameNumber,
                TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"),
                fixedDelta,
                string.Join(", ", _pendingAcks));

            Fdp.Core.Logging.FdpLog<MasterSyncController>.Info(
                "[TimeSync] STEP #{0}. SimTime: {1}, StepSize: {2:F4}s, Waiting for nodes: [{3}]",
                _frameNumber,
                TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"),
                fixedDelta,
                string.Join(", ", _pendingAcks));

            return BuildGlobalTime(scaledDelta, fixedDelta);
        }

        // ── Mode switching ───────────────────────────────────────────────────

        /// <summary>
        /// Initiates a transition to Deterministic (lockstep) mode using the Future Barrier
        /// protocol.  Computes a barrier as
        /// <c>_totalWallTicks + config.LookaheadWallTicks</c> and broadcasts a
        /// <see cref="SwitchTimeModeEvent"/> with <c>TargetMode = Deterministic</c>.
        /// The actual switch to <c>Stepping</c> happens inside <see cref="Update()"/> when
        /// the virtual wall clock crosses the barrier.
        /// </summary>
        /// <param name="slaveNodeIds">
        /// The roster of slave node IDs that must ACK every step during lockstep. Replaces any prior slave set.
        /// </param>
        public void SwitchToDeterministic(HashSet<int> slaveNodeIds)
        {
            _expectedSlaves.Clear();
            if (slaveNodeIds != null)
                _expectedSlaves.UnionWith(slaveNodeIds);

            // Always use the physical clock for the barrier; _totalWallTicks may have drifted
            // synthetically during previous lockstep sessions.
            long barrierWallTicks       = _getTick() + _config.LookaheadWallTicks;
            _pendingBarrierWallTicks    = barrierWallTicks;
            _mode                       = MasterMode.BarrierPending;

            Fdp.Core.Logging.FdpLog<MasterSyncController>.Debug(
                "[TC3][Master] PAUSE issued. BarrierTicks={0}, SimTime={1}",
                barrierWallTicks,
                TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"));

            Fdp.Core.Logging.FdpLog<MasterSyncController>.Info(
                "[TimeSync] PAUSE. SimTime: {0}, BarrierTicks: {1}, Expecting ACKs from {2} slave(s): [{3}]",
                TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"),
                barrierWallTicks,
                _expectedSlaves.Count,
                string.Join(", ", _expectedSlaves));

            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta       = _config.FixedDeltaSeconds,
                TimeScale        = _timeScale,
                SimTimeSnapshot  = _totalTime, // Bug 1 fix: was 0; slave needs this to sync on Pause
            });
        }

        /// <summary>
        /// Initiates an immediate return to Continuous mode.
        /// Idempotent: no-op if already in <c>Continuous</c> with no pending barrier.
        /// Cancels any in-flight barrier, captures <c>SimTimeSnapshot</c>, and broadcasts
        /// a <see cref="SwitchTimeModeEvent"/> with <c>TargetMode = Continuous</c>.
        /// </summary>
        /// <param name="resumeTimeScale">
        /// When positive, updates <c>_timeScale</c> atomically with the resume event.
        /// When zero (default), retains the current time scale.
        /// </param>
        public void SwitchToContinuous(float resumeTimeScale = 0f)
        {
            // Idempotent guard: no-op if already Continuous with no in-flight barrier.
            if (_mode == MasterMode.Continuous && _pendingBarrierWallTicks < 0)
                return;

            Fdp.Core.Logging.FdpLog<MasterSyncController>.Info(
                "[TimeSync] RESUME. SimTime: {0}, Cleared {1} pending ACK(s).",
                TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"),
                _pendingAcks.Count);

            _pendingAcks.Clear();

            _pendingBarrierWallTicks = -1;
            double simTimeSnapshot   = _totalTime;
            _mode                    = MasterMode.Continuous;

            if (resumeTimeScale > 0f)
                _timeScale = resumeTimeScale;

            // Re-anchor the wall-clock baseline to the REAL clock so the slave's formula
            // (SyncedWallTicks - BarrierWallTicks) picks up from "now" rather than from
            // a synthetic tick value that accumulated (fixedDelta × Freq) per Step() and
            // has drifted from the real clock by the difference between real stepping time
            // and the sum of nominal step deltas.
            long now = _getTick();
            _totalWallTicks = now;
            _lastTickSample = now;  // prevent a large catch-up spike on the very next Update()

            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = now,  // real-clock anchor so slaves compute correct elapsed-since-resume
                SimTimeSnapshot  = simTimeSnapshot,
                TimeScale        = _timeScale,
                FixedDelta       = 0f,
            });
        }

        /// <summary>
        /// Atomically snaps the master clock to <paramref name="targetWallTicks"/> /
        /// <paramref name="targetSimTime"/> and enters Deterministic (lockstep) mode.
        /// Unlike <see cref="SwitchToDeterministic"/>, no future-barrier window is used --
        /// the mode switch is instantaneous and the published <see cref="SwitchTimeModeEvent"/>
        /// carries a <c>BarrierWallTicks</c> already in the past so slaves apply the snap
        /// immediately via the instant-snap path in <c>SlaveSyncController</c>.
        /// </summary>
        /// <param name="targetWallTicks">Wall-clock tick value to snap to.</param>
        /// <param name="targetSimTime">Simulation time (seconds) to snap to.</param>
        /// <param name="slaveNodeIds">Slave roster for ACK tracking during subsequent steps.</param>
        public void SnapAndPause(long targetWallTicks, double targetSimTime, HashSet<int> slaveNodeIds)
        {
            _totalWallTicks    = targetWallTicks;
            _totalTime         = targetSimTime;
            _mode              = MasterMode.Stepping;
            _pendingAcks       = new HashSet<int>();

            _expectedSlaves.Clear();
            if (slaveNodeIds != null)
                _expectedSlaves.UnionWith(slaveNodeIds);

            _eventBus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = _getTick(),
                SimTimeSnapshot  = _totalTime,
                TimeScale        = _timeScale,
                FixedDelta       = _config.FixedDeltaSeconds,
            });

            _lastTickSample = _getTick();
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private GlobalTime UpdateContinuous(
            long   currentTicks,
            long   elapsedTicks,
            double elapsedSeconds,
            float  scaledDelta)
        {
            _frameNumber++;
            _totalTime         += scaledDelta;
            _unscaledTotalTime += elapsedSeconds;
            _totalWallTicks    += elapsedTicks;

            return BuildGlobalTime(scaledDelta, (float)elapsedSeconds);
        }

        private GlobalTime UpdateBarrierPending(
            long   currentTicks,
            long   elapsedTicks,
            double elapsedSeconds,
            float  scaledDelta)
        {
            // Bug fix: do NOT advance _totalTime during BarrierPending.
            // Sim time is logically frozen from the moment SwitchToDeterministic() is called.
            // The SwitchTimeModeEvent already carries SimTimeSnapshot = _totalTime (the paused
            // value) so slaves snap to that exact value.  If the master continues accumulating
            // here it goes 200ms+ ahead of slaves (= LookaheadWallTicks), causing the cluster
            // UI to show a different paused time than all slave nodes.
            // Only _totalWallTicks advances — that is what the barrier check needs.
            _totalWallTicks    += elapsedTicks;

            // Evaluate against the physical clock.  _totalWallTicks may be synthetic after stepping.
            if (_getTick() >= _pendingBarrierWallTicks)
            {
                _mode        = MasterMode.Stepping;
                // _pendingAcks starts empty; the first Step() populates it from _expectedSlaves.
                _pendingAcks = new HashSet<int>();
            }

            return BuildGlobalTime(0.0f, 0.0f);
        }

        private GlobalTime UpdateStepping()
        {
            // Drain any incoming ACKs from slaves.  Unknown node IDs are silently discarded.
            var acks = _eventBus.ReadManaged<FrameStepCompletedEvent>();
            bool wasWaiting = _pendingAcks.Count > 0;

            foreach (var ack in acks)
            {
                if (_pendingAcks.Remove(ack.NodeID))
                    Fdp.Core.Logging.FdpLog<MasterSyncController>.Debug(
                        "[TC3][Master] ACKs remaining={0}", _pendingAcks.Count);
            }

            if (wasWaiting && _pendingAcks.Count == 0)
            {
                Fdp.Core.Logging.FdpLog<MasterSyncController>.Info(
                    "[TimeSync] STEP SUCCESS. All slaves ACKed. SimTime: {0}",
                    TimeSpan.FromSeconds(_totalTime).ToString(@"hh\:mm\:ss\.fff"));
            }

            float dt = _pendingStepDelta;
            float unscaledDt = _pendingUnscaledStepDelta;
            _pendingStepDelta = 0f;
            _pendingUnscaledStepDelta = 0f;

            return BuildGlobalTime(dt, unscaledDt);
        }

        private GlobalTime BuildGlobalTime(float deltaTime, float unscaledDelta) =>
            new GlobalTime
            {
                FrameNumber      = _frameNumber,
                DeltaTime        = deltaTime,
                TotalTime        = _totalTime,
                TimeScale        = _timeScale,
                UnscaledDeltaTime = unscaledDelta,
                UnscaledTotalTime = _unscaledTotalTime,
                StartWallTicks   = 0,
                TotalWallTicks   = _totalWallTicks,
            };
    }
}
