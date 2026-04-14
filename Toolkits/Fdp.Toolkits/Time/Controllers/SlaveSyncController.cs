using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Controllers
{
    /// <summary>
    /// Unified slave time controller that subsumes <see cref="SlaveTimeController"/>,
    /// <see cref="SteppedSlaveController"/>, and <see cref="SlaveTimeModeListener"/>.
    /// <para>
    /// Implements a state machine:
    /// <c>Continuous → BarrierPending → Stepping → Continuous</c>
    /// </para>
    /// <para>
    /// SimTime is calculated as a pure deterministic function of the virtual wall clock:
    ///   <c>TotalTime = _baselineSimTime + (SyncedWallTicks - _baselineWallTicks) / Freq * TimeScale</c>
    /// The baseline is set on every <see cref="SwitchTimeModeEvent"/> (Continuous/Resume).
    /// No TimePulse or PLL is used.
    /// </para>
    /// </summary>
    public sealed class SlaveSyncController : ITimeController
    {
        // ── State machine ────────────────────────────────────────────────────
        private enum SlaveMode { Continuous, BarrierPending, Stepping }
        private SlaveMode _mode = SlaveMode.Continuous;

        private long _pendingBarrierWallTicks = -1;

        // ── Identity ─────────────────────────────────────────────────────────
        private readonly int _localNodeId;

        // ── Virtual wall clock baseline (set on resume) ──────────────────────
        private long   _baselineWallTicks;   // SyncedWallTicks when baseline was captured
        private long   _prevFrameStartTicks; // SyncedWallTicks from the end of the PREVIOUS Update()
        private double _baselineSimTime;     // TotalTime value at that moment
        private double _baselineUnscaledTime; // UnscaledTotalTime at that moment

        // ── Time state ───────────────────────────────────────────────────────
        private double _totalTime;
        private double _unscaledTotalTime;
        private long   _frameNumber;
        private float  _timeScale = 1.0f;

        // ── Stepping state ───────────────────────────────────────────────────
        private readonly Queue<AdvanceFrameIntent> _pendingIntents = new();
        // Tracks the last FrameID accepted in Stepping mode.  Reset to -1 each time Stepping
        // is entered so that the very first intent from the master (whose FrameID may exceed
        // the slave's continuous-mode _frameNumber) is not wrongly rejected as stale.
        private long _lastAcceptedStepFrameId = -1L;

        // ── NTP Real-Time Sync ────────────────────────────────────────────────
        private long  _masterWallClockOffset = 0;   // Master ticks - local ticks
        private long  _lastSyncRequestTicks  = 0;   // Physical tick when last request was sent
#pragma warning disable CS0414 // field is written for future use but value not yet consumed
        private bool  _isTimeSynced          = false; // Unlocked once first valid response arrives
#pragma warning restore CS0414

        /// <summary>The slave's best estimate of the master node's current OS tick.</summary>
        public long SyncedWallTicks => _getTick() + _masterWallClockOffset;

        // ── Infrastructure ───────────────────────────────────────────────────
        private readonly FdpEventBus _eventBus;
        private readonly TimeConfig  _config;
        private readonly Func<long>  _getTick;

        /// <summary>
        /// Constructs a <see cref="SlaveSyncController"/>.
        /// </summary>
        /// <param name="eventBus">Shared event bus (must not be null).</param>
        /// <param name="localNodeId">This node's ID, embedded in <see cref="FrameStepCompletedEvent"/>.</param>
        /// <param name="config">Time configuration; defaults to <see cref="TimeConfig.Default"/> when null.</param>
        /// <param name="tickSource">
        /// Optional override for <c>Stopwatch.GetTimestamp()</c>. Inject a controlled counter
        /// in unit tests to avoid <c>Thread.Sleep</c>.
        /// </param>
        public SlaveSyncController(
            FdpEventBus  eventBus,
            int          localNodeId,
            TimeConfig?  config     = null,
            Func<long>?  tickSource = null)
        {
            _eventBus    = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _localNodeId = localNodeId;
            _config      = config ?? TimeConfig.Default;
            _getTick     = tickSource ?? Stopwatch.GetTimestamp;

            long now              = _getTick();
            _baselineWallTicks    = now;   // SimTime formula baseline at startup: t=0
            _prevFrameStartTicks  = now;
            _baselineSimTime      = 0.0;
            _baselineUnscaledTime = 0.0;

            // Register bus types that carry [EventId] — bus needs them pre-registered.
            _eventBus.Register<SwitchTimeModeEvent>();
            // AdvanceFrameIntent and FrameStepCompletedEvent are domain types
            // (no [EventId]) — they use PublishManaged / ConsumeManaged, no registration needed.

            // Register NTP message types on the bus.
            // TimeSyncOffsetCalculatedEvent is the internal event published by SlaveTimeSyncTranslator
            // after computing the NTP formula at the network boundary (precise t4 capture).
            _eventBus.Register<TimeSyncOffsetCalculatedEvent>();
            _eventBus.Register<TimeSyncRequest>();

            // Send the initial handshake request.
            SendTimeSyncRequest();

            Fdp.Core.Logging.FdpLog<SlaveSyncController>.Debug(
                "[TC3][Slave#{0}] Initialized. _baselineWallTicks={1}", _localNodeId, _baselineWallTicks);
        }

        // ── ITimeController ──────────────────────────────────────────────────

        /// <summary>
        /// Advances the controller one frame.
        /// </summary>
        public GlobalTime Update()
        {
            DrainTimeSyncResponses();
            if (_getTick() - _lastSyncRequestTicks > _config.SyncRefreshIntervalTicks)
                SendTimeSyncRequest();

            DrainModeSwitchEvents();

            var result = _mode switch
            {
                SlaveMode.Continuous     => UpdateContinuous(),
                SlaveMode.BarrierPending => UpdateBarrierPending(),
                SlaveMode.Stepping       => UpdateStepping(),
                _                        => GetCurrentState(),
            };

            // Remember end-of-frame SyncedWallTicks so ApplyResume can use it
            // as the baseline on the very next frame (avoiding zero delta on first
            // Continuous frame after a Stepping → Continuous transition).
            _prevFrameStartTicks = SyncedWallTicks;
            return result;
        }

        public void SetTimeScale(float scale)
        {
            // Slave time scale is driven by received SwitchTimeModeEvent / TimePulse.
            // Honour explicit external calls (e.g. from test harness) all the same.
            _timeScale = scale;
        }

        public float    GetTimeScale()    => _timeScale;

        /// <summary>
        /// Returns <see cref="TimeMode.Continuous"/> for both <c>Continuous</c> and
        /// <c>BarrierPending</c> internal states.
        /// Returns <see cref="TimeMode.Deterministic"/> only when in <c>Stepping</c>.
        /// </summary>
        public TimeMode GetMode() => _mode == SlaveMode.Stepping
                                     ? TimeMode.Deterministic
                                     : TimeMode.Continuous;

        public GlobalTime GetCurrentState() => BuildGlobalTime(0.0f, 0.0f);

        public void SeedState(GlobalTime state)
        {
            _frameNumber          = state.FrameNumber;
            _totalTime            = state.TotalTime;
            _unscaledTotalTime    = state.UnscaledTotalTime;
            _timeScale            = state.TimeScale;
            // Re-anchor the wall clock baseline to the current synced time.
            _baselineSimTime      = state.TotalTime;
            _baselineUnscaledTime = state.UnscaledTotalTime;
            _baselineWallTicks    = SyncedWallTicks;
            _prevFrameStartTicks  = SyncedWallTicks;
        }

        public void Dispose() { /* no unmanaged resources */ }

        // ── Private: mode-switch drain ───────────────────────────────────────

        private void DrainModeSwitchEvents()
        {
            var events = _eventBus.Consume<SwitchTimeModeEvent>();
            foreach (var evt in events)
            {
                if (evt.TargetMode == TimeMode.Deterministic)
                {
                    if (_mode != SlaveMode.Stepping)
                    {
                        _pendingBarrierWallTicks = evt.BarrierWallTicks;
                        _mode = SlaveMode.BarrierPending;
                        _pendingIntents.Clear();
                        _lastAcceptedStepFrameId = -1L;
                    }
                }
                else
                {
                    ApplyResume(evt);
                }
            }
        }

        private void SendTimeSyncRequest()
        {
            _lastSyncRequestTicks = _getTick();
            _eventBus.Publish(new TimeSyncRequest
            {
                ClientNodeId    = _localNodeId,
                ClientSendTicks = _lastSyncRequestTicks,
            });
            Fdp.Core.Logging.FdpLog<SlaveSyncController>.Trace(
                "[TC3][Slave#{0}] TimeSyncRequest sent. ClientSendTicks={1}",
                _localNodeId, _lastSyncRequestTicks);
        }

        private void DrainTimeSyncResponses()
        {
            // Consume pre-computed NTP offsets produced by SlaveTimeSyncTranslator.
            // The translator captures t1 and t4 at the exact network boundary, eliminating
            // event-bus double-buffer jitter from the RTT measurement.
            var offsets = _eventBus.Consume<TimeSyncOffsetCalculatedEvent>();
            foreach (var offset in offsets)
            {
                double rttMs = offset.Rtt * 1000.0 / Stopwatch.Frequency;

                if (offset.Rtt > _config.MaxRttTicks)
                {
                    Fdp.Core.Logging.FdpLog<SlaveSyncController>.Debug(
                        "[TC3][Slave#{0}] Discarded sync result: RTT={1:F3}ms exceeds max={2:F3}ms",
                        _localNodeId, rttMs, _config.MaxRttTicks * 1000.0 / Stopwatch.Frequency);
                    continue;
                }

                bool hardSnap = _masterWallClockOffset == 0
                             || Math.Abs(offset.NewOffset - _masterWallClockOffset) > Stopwatch.Frequency;

                if (hardSnap)
                    _masterWallClockOffset = offset.NewOffset;
                else
                    _masterWallClockOffset += (long)((offset.NewOffset - _masterWallClockOffset)
                                                     * _config.SyncCorrectionWeight);

                Fdp.Core.Logging.FdpLog<SlaveSyncController>.Trace(
                    "[TC3][Slave#{0}] RTT={1:F3}ms, Offset={2} ticks. {3}",
                    _localNodeId, rttMs, _masterWallClockOffset,
                    hardSnap ? "HARD-SNAP" : "gentle-steer");

                _isTimeSynced = true;
            }
        }

        private void ApplyResume(SwitchTimeModeEvent evt)
        {
            // Snap sim time to master's authoritative snapshot.
            // Bug 7 fix: gate on BarrierWallTicks > 0 to detect a proper master-originated event.
            // Real Stopwatch ticks are always > 0, so production events always enter the first branch.
            // Legacy / test events that carry BarrierWallTicks = 0 fall through to the old sentinel
            // logic, keeping backward-compat with unit tests that use SimTimeSnapshot = 0 to mean
            // "no authoritative snapshot" (old default).
            // Without this fix, a startup event with SimTimeSnapshot = 0.0 failed the
            // " > 0.0" check and the slave used its own accumulated warmup time as the base,
            // producing a permanent positive offset (IG ahead by ~20 ms, ExCon by ~100 ms).
            if (evt.BarrierWallTicks > 0)
            {
                // Authoritative master event — SimTimeSnapshot 0.0 is valid (t=0 at startup).
                _baselineSimTime      = evt.SimTimeSnapshot;
                _baselineUnscaledTime = evt.TimeScale > 0f
                    ? evt.SimTimeSnapshot / evt.TimeScale
                    : _unscaledTotalTime;
            }
            else if (evt.SimTimeSnapshot > 0.0)
            {
                _baselineSimTime      = evt.SimTimeSnapshot;
                _baselineUnscaledTime = _unscaledTotalTime;
            }
            else
            {
                _baselineSimTime      = _totalTime;
                _baselineUnscaledTime = _unscaledTotalTime;
            }

            // Apply time scale if carried.
            if (evt.TimeScale > 0f)
                _timeScale = evt.TimeScale;

            // Bug 5 fix: anchor the wall-clock baseline to the master's exact tick snapshot
            // transmitted in BarrierWallTicks.  Because SyncedWallTicks is calibrated to the
            // master's domain via NTP, the formula (SyncedWallTicks - _baselineWallTicks) will
            // immediately and correctly fast-forward any time that elapsed during network transit.
            // Without this fix the slave anchors to _prevFrameStartTicks, permanently baking in
            // the boot-time and full-frame delays as a constant offset.
            if (evt.BarrierWallTicks > 0)
                _baselineWallTicks = evt.BarrierWallTicks;
            else
                _baselineWallTicks = _prevFrameStartTicks; // fallback for legacy/test events

            _pendingBarrierWallTicks = -1;
            _mode = SlaveMode.Continuous;
        }

        // ── Private: continuous-mode update ─────────────────────────────────

        private GlobalTime UpdateContinuous()
        {
            // Prevent memory leak: drain any AdvanceFrameIntent that arrived while in Continuous mode
            _eventBus.ConsumeManaged<AdvanceFrameIntent>();

            return AdvanceContinuousTime();
        }

        // ── Private: barrier-pending update ─────────────────────────────────

        private GlobalTime UpdateBarrierPending()
        {
            // Drain stray step intents so they don't pile up.
            _eventBus.ConsumeManaged<AdvanceFrameIntent>();

            // Do NOT advance simulation time: the slave freezes sim-time immediately on entering
            // BarrierPending. The cached _totalTime from the last Continuous frame is returned.

            // Check if the synced wall clock has reached the barrier.
            if (_pendingBarrierWallTicks >= 0 && SyncedWallTicks >= _pendingBarrierWallTicks)
            {
                Fdp.Core.Logging.FdpLog<SlaveSyncController>.Debug(
                    "[TC3][Slave#{0}] BARRIER HIT. SyncedWallTicks={1}, BarrierWallTicks={2}. Entering Stepping.",
                    _localNodeId, SyncedWallTicks, _pendingBarrierWallTicks);
                _mode = SlaveMode.Stepping;
                _pendingIntents.Clear();
                _lastAcceptedStepFrameId = -1L;
            }

            return GetCurrentState(); // frozen
        }

        // ── Private: stepping-mode update ───────────────────────────────────

        private GlobalTime UpdateStepping()
        {
            // Drain AdvanceFrameIntent from managed bus.
            var intents = _eventBus.ConsumeManaged<AdvanceFrameIntent>();

            // Filter stale / out-of-order intents.  Use _lastAcceptedStepFrameId rather than
            // _frameNumber because the continuous-mode frame counter and the master's step
            // counter naturally diverge; on first Stepping entry _lastAcceptedStepFrameId == -1
            // so any positive FrameID is accepted.
            foreach (var intent in intents)
            {
                if (intent.FrameID > _lastAcceptedStepFrameId)
                    _pendingIntents.Enqueue(intent);
                // else: stale / out-of-order — silently ignore (spec requirement).
            }

            if (_pendingIntents.Count == 0)
            {
                // No intents available — frozen frame, DeltaTime = 0.
                return BuildGlobalTime(0.0f, 0.0f);
            }

            // Process one intent per Update() call.
            var next = _pendingIntents.Dequeue();

            float unscaledDelta = next.FixedDelta;

            // Snap to TargetSimTime if provided; otherwise accumulate.
            if (next.TargetSimTime > 0.0)
                _totalTime = next.TargetSimTime;
            else
                _totalTime += unscaledDelta * _timeScale;

            _unscaledTotalTime += unscaledDelta;
            _frameNumber              = next.FrameID;
            _lastAcceptedStepFrameId  = next.FrameID;

            float scaledDelta = (float)(unscaledDelta * _timeScale);

            // ACK the master.
            _eventBus.PublishManaged(new FrameStepCompletedEvent
            {
                FrameID = next.FrameID,
                NodeID  = _localNodeId,
            });

            return BuildGlobalTime(scaledDelta, unscaledDelta);
        }

        // ── Private: continuous-time advancement ─────────────────────────────

        private GlobalTime AdvanceContinuousTime()
        {
            _frameNumber++;

            long   syncedNow    = SyncedWallTicks;
            long   elapsed      = syncedNow - _baselineWallTicks;
            double elapsedSec   = elapsed / (double)Stopwatch.Frequency;

            double prevTotal    = _totalTime;
            _totalTime          = _baselineSimTime + elapsedSec * _timeScale;
            _unscaledTotalTime  = _baselineUnscaledTime + elapsedSec;

            float scaledDelta   = (float)(_totalTime - prevTotal);
            float unscaledDelta = scaledDelta > 0f ? scaledDelta / _timeScale : 0f;

            return BuildGlobalTime(scaledDelta, unscaledDelta);
        }

        // ── Private: builder ─────────────────────────────────────────────────

        private GlobalTime BuildGlobalTime(float deltaTime, float unscaledDelta) =>
            new GlobalTime
            {
                FrameNumber       = _frameNumber,
                DeltaTime         = deltaTime,
                TotalTime         = _totalTime,
                TimeScale         = _timeScale,
                UnscaledDeltaTime = unscaledDelta,
                UnscaledTotalTime = _unscaledTotalTime,
                StartWallTicks    = 0,
                TotalWallTicks    = SyncedWallTicks,
            };
    }
}
