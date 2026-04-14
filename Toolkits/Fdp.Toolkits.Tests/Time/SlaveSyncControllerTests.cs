using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Fdp.Kernel;
using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Domain;
using FDP.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for TCU-SC001 (SlaveSyncController) and TCU-T002 (coverage spec).
    /// Covers all 10 required success conditions plus 2 edge cases.
    /// </summary>
    public class SlaveSyncControllerTests
    {
        private const int NodeId = 42;

        // ── Helpers ──────────────────────────────────────────────────────────

        private static long TicksFromSeconds(double seconds)
            => (long)(seconds * Stopwatch.Frequency);

        /// <summary>
        /// Injects a zero-offset TimeSyncOffsetCalculatedEvent so _isTimeSynced = true on the next Update().
        /// The RTT is 0 and NewOffset is 0, simulating a zero-latency, zero-offset NTP response.
        /// </summary>
        private static void InjectSyncResponse(FdpEventBus bus, long currentTick, int nodeId = NodeId)
        {
            _ = currentTick; _ = nodeId; // retained for signature compatibility
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            bus.SwapBuffers();
        }

        /// <summary>Creates a controller under test with a controllable tick source.</summary>
        private static SlaveSyncController CreateController(
            FdpEventBus bus,
            int         nodeId     = NodeId,
            TimeConfig? config     = null,
            Func<long>? tickSource = null)
        {
            return new SlaveSyncController(bus, nodeId, config, tickSource);
        }

        /// <summary>
        /// Publishes a SwitchTimeModeEvent (Deterministic with the given barrier),
        /// swaps buffers, and calls Update() once, driving the controller to at least
        /// BarrierPending.  Set barrierWallTicks to a negative value to cross the
        /// barrier immediately on the first Update() call after the swap.
        /// </summary>
        private static void SendDeterministicSwitch(
            FdpEventBus bus,
            long        barrierWallTicks)
        {
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
                SimTimeSnapshot  = 0,
            });
            bus.SwapBuffers();
        }

        private static void SendContinuousSwitch(
            FdpEventBus bus,
            double      simTimeSnapshot = 0.0,
            float       timeScale       = 0f)
        {
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = 0,
                SimTimeSnapshot  = simTimeSnapshot,
                TimeScale        = timeScale,
                FixedDelta       = 0f,
            });
            bus.SwapBuffers();
        }

        /// <summary>
        /// Publishes an AdvanceFrameIntent, swaps, and calls Update().
        /// Returns the GlobalTime from that Update().
        /// </summary>
        private static GlobalTime StepSlave(
            SlaveSyncController ctrl,
            FdpEventBus         bus,
            long                frameId,
            float               fixedDelta,
            double              targetSimTime = 0.0)
        {
            bus.PublishManaged(new AdvanceFrameIntent
            {
                FrameID       = frameId,
                FixedDelta    = fixedDelta,
                TargetSimTime = targetSimTime,
            });
            bus.SwapBuffers();
            return ctrl.Update();
        }

        // ── Required success conditions ──────────────────────────────────────

        /// <summary>
        /// TCU-SC001 §1 — In Continuous mode, advancing the wall clock by 100 ms must
        /// produce a positive TotalTime with no TimePulse required.
        /// The new design computes SimTime directly from the virtual wall clock.
        /// </summary>
        [Fact]
        public void SlaveSyncController_SimTime_AdvancesFromWallClock_NoPulseRequired()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks);

            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Advance 100 ms — no TimePulse, no NTP needed.
            ticks += TicksFromSeconds(0.1);
            var state = ctrl.Update();

            Assert.True(state.TotalTime > 0, "TotalTime must advance from wall clock without TimePulse.");
        }

        /// <summary>
        /// Helper to create a bus + controller in one shot (avoids repetitive setup).
        /// </summary>
        private static FdpEventBus CreateController_Bus(
            out SlaveSyncController ctrl,
            Func<long>?             tickSource = null,
            int                     nodeId     = NodeId,
            TimeConfig?             config     = null)
        {
            var bus = new FdpEventBus();
            ctrl    = CreateController(bus, nodeId, config, tickSource);
            return bus;
        }

        /// <summary>
        /// TCU-SC001 §2 — After NTP sync establishes a non-zero master offset,
        /// SimTime must equal (<see cref="SlaveSyncController.SyncedWallTicks"/> - initialWallTicks)
        /// / Stopwatch.Frequency * timeScale — without receiving any TimePulse.
        /// FAILS with the old PLL design because that only corrects via TimePulse.
        /// </summary>
        [Fact]
        public void SlaveSyncController_SimTime_IsExactDeterministicFormula_AfterNtpSync()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks);

            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Establish a non-zero NTP offset: master is 1 full second of ticks ahead.
            // Inject the pre-computed offset (what the translator would produce for zero-RTT).
            long masterOffset = Stopwatch.Frequency; // 1 s worth of ticks
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = masterOffset });
            bus.SwapBuffers();
            ctrl.Update(); // applies offset: _masterWallClockOffset = masterOffset
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Advance 1 second of local ticks.
            ticks += Stopwatch.Frequency;
            ctrl.Update();

            // New design: _totalTime = 0 + (SyncedWallTicks - 0) / Freq = (ticks + masterOffset) / Freq
            //           = (Freq + Freq) / Freq = 2.0s
            // Old PLL:   _totalTime ≈ 1.0s (just accumulated raw local delta, no correction)
            double expected = (ticks + masterOffset) / (double)Stopwatch.Frequency;
            Assert.Equal(expected, ctrl.GetCurrentState().TotalTime, 3);
        }

        /// <summary>
        /// TCU-SC001 §3 — While in BarrierPending (barrier far in the future),
        /// sim-time is frozen (TotalTime does NOT advance);
        /// GetMode() remains Continuous (BarrierPending is shown as Continuous externally).
        /// </summary>
        [Fact]
        public void SlaveSyncController_BarrierPending_SimTimeFrozen()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks);

            // Sync first so _isTimeSynced = true.
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Advance two frames so controller has a non-zero baseline.
            ticks += TicksFromSeconds(0.016);
            ctrl.Update();
            ticks += TicksFromSeconds(0.016);
            ctrl.Update();

            // Send Deterministic switch with a very far barrier.
            long farBarrier = ticks + TicksFromSeconds(999.0);
            SendDeterministicSwitch(bus, farBarrier);

            double timeBeforeBarrierPending = ctrl.GetCurrentState().TotalTime;

            // Advance 4 more frames — barrier not yet crossed.
            for (int i = 0; i < 4; i++)
            {
                ticks += TicksFromSeconds(0.016);
                ctrl.Update();
                bus.SwapBuffers();
            }

            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());
            Assert.True(ctrl.GetCurrentState().TotalTime == timeBeforeBarrierPending,
                "TotalTime must be frozen (not advance) while in BarrierPending.");
        }

        /// <summary>
        /// TCU-SC001 §4 — When BarrierWallTicks is equal to the current virtual wall
        /// ticks the controller must transition to Stepping on the next Update().
        /// </summary>
        [Fact]
        public void SlaveSyncController_TransitionsToStepping_WhenBarrierCrossed()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            // Sync first so _isTimeSynced = true.
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Run one frame so _virtualWallTicks > 0.
            ticks += TicksFromSeconds(0.016);
            ctrl.Update();
            bus.SwapBuffers();

            // The barrier is the current virtual wall ticks → will be crossed immediately.
            long currentVirtualWall = ctrl.GetCurrentState().TotalWallTicks;
            SendDeterministicSwitch(bus, currentVirtualWall);

            // One more tick past the barrier.
            ticks += 1;
            ctrl.Update();

            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        /// <summary>
        /// TCU-SC001 §5 — In Stepping mode, publishing an AdvanceFrameIntent causes
        /// FrameNumber to advance and DeltaTime / TotalTime to match FixedDelta.
        /// </summary>
        [Fact]
        public void SlaveSyncController_Stepping_AdvancesOnAdvanceFrameIntent()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks);

            var state = StepSlave(ctrl, bus, frameId: 1, fixedDelta: 0.016f);

            Assert.Equal(1L,    state.FrameNumber);
            Assert.Equal(0.016f, state.DeltaTime, 4);
            Assert.Equal(0.016,  state.TotalTime,  3);
        }

        private static void TransitionToStepping(SlaveSyncController ctrl, FdpEventBus bus, ref long ticks, int nodeId = NodeId)
        {
            // Drain the initial TimeSyncRequest published by the constructor.
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Inject a zero-offset sync so _isTimeSynced = true (required for mode-switch events).
            InjectSyncResponse(bus, ticks, nodeId);
            ctrl.Update(); // drains sync response → _isTimeSynced = true
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>(); // drain any re-sync request

            // Barrier at 0: SyncedWallTicks = ticks + 0; adding 1 tick crosses it immediately.
            SendDeterministicSwitch(bus, barrierWallTicks: 0);
            ticks += 1;
            ctrl.Update();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        /// <summary>
        /// TCU-SC001 §6 — In Stepping mode with no pending AdvanceFrameIntent,
        /// Update() returns DeltaTime == 0 and FrameNumber is unchanged.
        /// </summary>
        [Fact]
        public void SlaveSyncController_Stepping_WaitsWithDeltaZeroWhenNoIntent()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks);

            long frameBefore = ctrl.GetCurrentState().FrameNumber;
            var  state       = ctrl.Update(); // no intent published

            Assert.Equal(0.0f, state.DeltaTime);
            Assert.Equal(frameBefore, state.FrameNumber);
        }

        /// <summary>
        /// TCU-SC001 §7 — After advancing one frame, a FrameStepCompletedEvent must be
        /// published with the correct FrameID and NodeID.
        /// </summary>
        [Fact]
        public void SlaveSyncController_Stepping_PublishesFrameStepCompletedEvent()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, nodeId: 7, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks, nodeId: 7);

            // Publish intent and advance.
            bus.PublishManaged(new AdvanceFrameIntent { FrameID = 1, FixedDelta = 0.016f });
            bus.SwapBuffers();
            ctrl.Update();

            // Drain FrameStepCompletedEvent.
            bus.SwapBuffers();
            var completions = bus.ConsumeManaged<FrameStepCompletedEvent>();

            Assert.Single(completions);
            Assert.Equal(1L, completions[0].FrameID);
            Assert.Equal(7,  completions[0].NodeID);
        }

        /// <summary>
        /// TCU-SC001 §8 — When transitioning back to Continuous with SimTimeSnapshot = 4.5,
        /// TotalTime must snap to 4.5.
        /// </summary>
        [Fact]
        public void SlaveSyncController_Resume_SnapsToMasterSimTime()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks);

            // Advance to TotalTime ≈ 3.0 via stepping.
            for (int i = 1; i <= 3; i++)
                StepSlave(ctrl, bus, frameId: i, fixedDelta: 1.0f);

            Assert.Equal(3.0, ctrl.GetCurrentState().TotalTime, 3);

            // Send Resume with SimTimeSnapshot = 4.5.
            SendContinuousSwitch(bus, simTimeSnapshot: 4.5);

            ticks += TicksFromSeconds(0.001); // tiny tick so Update has something to measure
            ctrl.Update();

            Assert.Equal(4.5, ctrl.GetCurrentState().TotalTime, 2);
        }

        /// <summary>
        /// TCU-SC001 §9 — After a Pause/Resume cycle, DeltaTime on the first frame
        /// post-resume must be within ±10 % of the frame delta used before the pause.
        /// With pure wall-clock math there is no PLL state to corrupt, so this is
        /// always satisfied.
        /// </summary>
        [Fact]
        public void SlaveSyncController_Resume_DeltaTimeIsConsistentAfterPauseCycle()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            long frameTicks = TicksFromSeconds(0.016);

            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Run 20 frames to establish a baseline.
            GlobalTime lastContinuousState = default;
            for (int i = 0; i < 20; i++)
            {
                ticks += frameTicks;
                lastContinuousState = ctrl.Update();
                bus.SwapBuffers();
            }

            float deltaBeforePause = lastContinuousState.DeltaTime;
            Assert.True(deltaBeforePause > 0f, "DeltaTime should be positive before pause.");

            // Transition to Stepping (barrier = ticks).
            long barrier = ticks;
            SendDeterministicSwitch(bus, barrier);
            ticks += 1;
            ctrl.Update();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            // Resume, providing the current sim-time as baseline for the slave.
            double simSnap = ctrl.GetCurrentState().TotalTime;
            SendContinuousSwitch(bus, simTimeSnapshot: simSnap, timeScale: 1.0f);
            ticks += frameTicks;
            var postResume = ctrl.Update();

            // DeltaTime after resume should match the pre-pause frame rate.
            float ratio = postResume.DeltaTime / deltaBeforePause;
            Assert.True(ratio >= 0.9f && ratio <= 1.1f,
                $"DeltaTime ratio after resume ({ratio:F4}) is outside ±10 % of pre-pause delta.");
        }

        /// <summary>
        /// TCU-SC001 §10 — When AdvanceFrameIntent carries TargetSimTime > 0,
        /// TotalTime must snap to that value (not += FixedDelta).
        /// </summary>
        [Fact]
        public void SlaveSyncController_Stepping_SnapsToTargetSimTime_WhenProvided()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks);

            var state = StepSlave(ctrl, bus, frameId: 5, fixedDelta: 0.016f, targetSimTime: 10.0);

            Assert.Equal(10.0, state.TotalTime, 3);
        }

        // ── Edge cases (TCU-T002) ────────────────────────────────────────────

        /// <summary>
        /// TCU-T002 edge case 1 — Two complete Continuous → Stepping → Continuous cycles;
        /// no PLL reset between them; time advances correctly after second resume.
        /// </summary>
        [Fact]
        public void SlaveSyncController_TwoConsecutivePauseResumeCycles_WithoutPLLReset()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            long frameTicks = TicksFromSeconds(0.016);

            // Sync first so _isTimeSynced = true.
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // ── First cycle ──────────────────────────────────────────────────
            // Warm PLL.
            for (int i = 0; i < 20; i++)
            {
                ticks += frameTicks;
                ctrl.Update();
                bus.SwapBuffers();
            }

            // Pause.
            long barrier1 = ctrl.GetCurrentState().TotalWallTicks;
            SendDeterministicSwitch(bus, barrier1);
            ticks += 1;
            ctrl.Update();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            // Resume.
            double simSnap1 = ctrl.GetCurrentState().TotalTime;
            SendContinuousSwitch(bus, simTimeSnapshot: simSnap1);
            ticks += frameTicks;
            var afterResume1 = ctrl.Update();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());

            // ── Second cycle ─────────────────────────────────────────────────
            // More frames.
            for (int i = 0; i < 20; i++)
            {
                ticks += frameTicks;
                ctrl.Update();
                bus.SwapBuffers();
            }

            long barrier2 = ctrl.GetCurrentState().TotalWallTicks;
            SendDeterministicSwitch(bus, barrier2);
            ticks += 1;
            ctrl.Update();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            double simSnap2 = ctrl.GetCurrentState().TotalTime;
            SendContinuousSwitch(bus, simTimeSnapshot: simSnap2);
            ticks += frameTicks;
            var afterResume2 = ctrl.Update();
            bus.SwapBuffers();

            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());
            Assert.True(afterResume2.TotalTime > 0, "TotalTime must be positive after second resume.");
            Assert.True(afterResume2.DeltaTime > 0, "DeltaTime must be positive after second warm resume.");
        }

        /// <summary>
        /// TCU-T002 edge case 2 — An AdvanceFrameIntent with FrameID less than or equal to
        /// the current FrameNumber must be silently ignored (out-of-order / stale delivery).
        /// </summary>
        [Fact]
        public void SlaveSyncController_OutOfOrderAdvanceFrameIntent_IsIgnored()
        {
            long ticks = 0;
            var bus    = CreateController_Bus(out var ctrl, () => ticks, config: new TimeConfig { LookaheadWallTicks = 0 });

            TransitionToStepping(ctrl, bus, ref ticks);

            // Advance to FrameNumber = 5.
            for (int i = 1; i <= 5; i++)
                StepSlave(ctrl, bus, frameId: i, fixedDelta: 0.016f);

            Assert.Equal(5L, ctrl.GetCurrentState().FrameNumber);

            // Publish a stale intent (FrameID = 3 < 5).
            bus.PublishManaged(new AdvanceFrameIntent { FrameID = 3, FixedDelta = 0.016f });
            bus.SwapBuffers();
            ctrl.Update();

            // FrameNumber must remain at 5.
            Assert.Equal(5L, ctrl.GetCurrentState().FrameNumber);
        }

        // ── TC3-P3-T01 Tests ─────────────────────────────────────────────────

        [Fact]
        public void SlaveSyncController_Constructor_PublishesInitialTimeSyncRequest()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);

            bus.SwapBuffers();
            var requests = bus.Consume<TimeSyncRequest>().ToArray();

            Assert.Single(requests);
            Assert.Equal(NodeId, requests[0].ClientNodeId);
            Assert.Equal(1_000_000L, requests[0].ClientSendTicks);
        }

        [Fact]
        public void SlaveSyncController_SyncedWallTicks_IsRawTickWhenOffsetIsZero()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);

            ticks = 2_000_000L;
            Assert.Equal(2_000_000L, ctrl.SyncedWallTicks);
        }

        [Fact]
        public void SlaveSyncController_Constructor_IsTimeSynced_IsFalse()
        {
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus);

            bool isTimeSynced = (bool)typeof(SlaveSyncController)
                .GetField("_isTimeSynced", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            Assert.False(isTimeSynced);
        }

        // ── TC3-P3-T02 Tests ─────────────────────────────────────────────────

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_CalculatesCorrectOffset()
        {
            long ticks = 0L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            // Drain initial request
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Publish response: pre-computed offset=500, RTT=0
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 500L });
            bus.SwapBuffers();
            ticks = 1L; // advance ticks so SyncedWallTicks can be checked

            ctrl.Update();

            long offset = (long)typeof(SlaveSyncController)
                .GetField("_masterWallClockOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            Assert.Equal(500L, offset);
            Assert.Equal(1L + 500L, ctrl.SyncedWallTicks); // 501
        }

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_DiscardsHighRttSpikes()
        {
            long ticks = 0L;
            var config = new TimeConfig { MaxRttTicks = 100 };
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, config: config, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // RTT = 200 > MaxRttTicks (100) → discarded
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 200L, NewOffset = 100L });
            bus.SwapBuffers();
            ctrl.Update();

            long offset = (long)typeof(SlaveSyncController)
                .GetField("_masterWallClockOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            Assert.Equal(0L, offset);
        }

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_HardSnapsOnFirstSync()
        {
            long ticks = 0L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Inject pre-computed offset=300_000, RTT=0 → hard-snap (first sync)
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 300_000L });
            bus.SwapBuffers();
            ticks = 0L;

            ctrl.Update();

            long offset = (long)typeof(SlaveSyncController)
                .GetField("_masterWallClockOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            // Hard-snap: should be exactly 300_000, not 300_000 * 0.1 = 30_000
            Assert.Equal(300_000L, offset);
        }

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_GentleSteersAfterBaseline()
        {
            long ticks = 0L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // First sync: hard-snap to 300_000
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 300_000L });
            bus.SwapBuffers();
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>(); // drain any periodic request

            // Second sync: newOffset = 310_000 → gentle steer
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 310_000L });
            bus.SwapBuffers();
            ctrl.Update();

            long offset = (long)typeof(SlaveSyncController)
                .GetField("_masterWallClockOffset", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            // Gentle steer: 300_000 + (long)((310_000 - 300_000) * 0.1) = 301_000
            Assert.Equal(301_000L, offset);
        }

        [Fact]
        public void SlaveSyncController_Update_SendsPeriodicResync()
        {
            long ticks = 0L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            // Drain the initial request
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Advance ticks past SyncRefreshIntervalTicks
            ticks = Stopwatch.Frequency + 1; // > 1 second

            ctrl.Update();
            bus.SwapBuffers();
            var requests = bus.Consume<TimeSyncRequest>().ToArray();

            Assert.Single(requests);
            Assert.Equal(NodeId, requests[0].ClientNodeId);
        }

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_SetsIsTimeSynced()
        {
            long ticks = 0L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            var isTimeSyncedField = typeof(SlaveSyncController)
                .GetField("_isTimeSynced", BindingFlags.NonPublic | BindingFlags.Instance)!;

            Assert.False((bool)isTimeSyncedField.GetValue(ctrl)!);

            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            bus.SwapBuffers();
            ctrl.Update();

            Assert.True((bool)isTimeSyncedField.GetValue(ctrl)!);
        }

        [Fact]
        public void SlaveSyncController_DrainTimeSyncResponses_SpikeRejected_IsTimeSyncedRemainsfalse()
        {
            long ticks = 0L;
            var config = new TimeConfig { MaxRttTicks = 100 };
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, config: config, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // RTT = 200 > MaxRttTicks (100) → rejected, _isTimeSynced stays false
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 200L, NewOffset = 100L });
            bus.SwapBuffers();
            ctrl.Update();

            bool isTimeSynced = (bool)typeof(SlaveSyncController)
                .GetField("_isTimeSynced", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctrl)!;

            Assert.False(isTimeSynced);
        }

        [Fact]
        public void SlaveSyncController_DrainModeSwitchEvents_ProcessesEvenBeforeNtpSync()
        {
            // Corrective-02: the _isTimeSynced guard was removed from DrainModeSwitchEvents.
            // Without NTP sync, a SwitchTimeModeEvent is still processed.
            // With BarrierWallTicks = 0 and slave ticks >> 0, the slave enters Stepping immediately.
            // This is the correct behavior for same-machine scenarios (integration tests).
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Publish a SwitchTimeModeEvent without syncing first
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = 0L,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
                SimTimeSnapshot  = 0,
            });
            bus.SwapBuffers();
            ctrl.Update();

            // Slave processes the event. BarrierWallTicks=0, SyncedWallTicks=realTick >> 0 → Stepping.
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        [Fact]
        public void SlaveSyncController_DrainModeSwitchEvents_AcceptsAfterSync()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Sync first
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Publish a SwitchTimeModeEvent with barrier = 0 (already past)
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = 0L,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
                SimTimeSnapshot  = 0,
            });
            bus.SwapBuffers();
            ctrl.Update(); // SyncedWallTicks = ticks = 1_000_000 >= 0 barrier → Stepping

            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        // ── TC3-P3-T03 Test ──────────────────────────────────────────────────

        [Fact]
        public void SlaveSyncController_BarrierPending_UsesSyncedWallTicks()
        {
            // Slave ticks start at 500_000_000 (large OS offset vs master which starts at 0)
            long slaveTicks = 500_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => slaveTicks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Inject offset = -500_000_000 (computed by NTP translator: master domain is 500_000_000 ticks BEHIND slave)
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = -500_000_000L });
            bus.SwapBuffers();
            // t4 is no longer needed here (offset already pre-computed)
            ctrl.Update(); // applies sync: _masterWallClockOffset = -500_000_000
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Master issues barrier at 100_000 (in master domain / SyncedWallTicks domain)
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = 100_000L,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
                SimTimeSnapshot  = 0,
            });
            bus.SwapBuffers();

            // Advance slaveTicks so SyncedWallTicks = slaveTicks - 500_000_000 = 50
            slaveTicks = 500_000_050L; // SyncedWallTicks = 50 < 100_000 barrier
            ctrl.Update(); // Should enter BarrierPending (50 < 100_000 → no Stepping)
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode()); // still pending (GetMode returns Continuous for BarrierPending)

            // Advance past barrier: SyncedWallTicks = slaveTicks - 500_000_000 = 110_000 >= 100_000
            slaveTicks = 500_110_000L;
            ctrl.Update(); // Should enter Stepping
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        // ── TC3-P3-T06 Tests ─────────────────────────────────────────────────

        [Fact]
        public void SlaveSyncController_ContinuousMode_DrainsStrayStepIntents()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Sync so _isTimeSynced = true
            InjectSyncResponse(bus, ticks);
            ctrl.Update();

            // Publish 10 stray AdvanceFrameIntents
            for (int i = 1; i <= 10; i++)
                bus.PublishManaged(new AdvanceFrameIntent { FrameID = i, FixedDelta = 0.016f });

            // Call Update() — slave remains in Continuous mode (no SwitchTimeModeEvent published)
            ctrl.Update();

            // Bus should now be empty
            var remaining = bus.ConsumeManaged<AdvanceFrameIntent>();
            Assert.Empty(remaining);
        }

        [Fact]
        public void SlaveSyncController_BarrierPendingMode_DrainsStrayStepIntents()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Sync
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Transition to BarrierPending with a far-future barrier
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = long.MaxValue,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
            });
            bus.SwapBuffers();
            ctrl.Update(); // enters BarrierPending

            // Publish 5 stray intents
            for (int i = 1; i <= 5; i++)
                bus.PublishManaged(new AdvanceFrameIntent { FrameID = i, FixedDelta = 0.016f });

            ctrl.Update(); // barrier not crossed, stays BarrierPending

            var remaining = bus.ConsumeManaged<AdvanceFrameIntent>();
            Assert.Empty(remaining);
        }

        [Fact]
        public void SlaveSyncController_SteppingMode_ProcessesIntentNotDrains()
        {
            long ticks = 1_000_000L;
            var bus = new FdpEventBus();
            var ctrl = CreateController(bus, tickSource: () => ticks);
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Sync
            InjectSyncResponse(bus, ticks);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // Transition to Stepping (barrier = 0, already past)
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = 0L,
                FixedDelta       = 1f / 60f,
                TimeScale        = 1.0f,
            });
            bus.SwapBuffers();
            ctrl.Update(); // enters Stepping

            // Publish one intent
            bus.PublishManaged(new AdvanceFrameIntent { FrameID = 1, FixedDelta = 0.016f, TargetSimTime = 0.016 });
            bus.SwapBuffers();
            ctrl.Update(); // should PROCESS (not drain) the intent

            Assert.True(ctrl.GetCurrentState().TotalTime > 0.0, "Intent should have been processed, not drained");

            // ACK should have been published
            bus.SwapBuffers();
            var acks = bus.ConsumeManaged<FrameStepCompletedEvent>();
            Assert.Single(acks);
            Assert.Equal(1L, acks[0].FrameID);
        }

        // ── Virtual wall clock determinism tests (new design) ──────────────────

        /// <summary>
        /// KEY FAILING TEST — After a slave completes NTP sync and discovers the master
        /// is N ticks ahead, its SimTime must immediately reflect the master's virtual
        /// wall time without requiring any TimePulse.
        ///
        /// NEW DESIGN (pure wall clock formula):
        ///   SimTime = _baselineSimTime + (SyncedWallTicks - _baselineWallTicks) / Freq * scale
        ///           = 0 + (slaveTick + masterOffset - 0) / Freq
        ///           = (slaveTick + masterOffset) / Freq  ≈ master's SimTime ✓
        ///
        /// OLD DESIGN (incremental PLL, no TimePulse correction):
        ///   SimTime ≈ slaveTick / Freq  (only local accumulation, offset ignored) ✗
        ///
        /// FAILS with the old implementation.
        /// </summary>
        [Fact]
        public void SlaveSyncController_SimTime_AutoCorrectedByNtpOffset_WithoutTimePulse()
        {
            // Master is 0.3 s ahead of the slave's local clock.
            long masterOffset = TicksFromSeconds(0.3);
            long slaveTick    = 0;
            var  bus          = CreateController_Bus(out var ctrl, () => slaveTick);

            // Drain the initial TimeSyncRequest emitted by the constructor.
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>();

            // ── Slave runs for 0.5 s without NTP ────────────────────────────
            slaveTick += TicksFromSeconds(0.5);
            ctrl.Update();
            bus.SwapBuffers();
            bus.Consume<TimeSyncRequest>(); // drain any periodic re-sync

            // ── NTP handshake arrives ────────────────────────────────────────
            // Inject pre-computed offset = masterOffset (what the translator produces for zero-RTT).
            bus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = masterOffset });
            bus.SwapBuffers();
            ctrl.Update(); // Processes offset: _masterWallClockOffset = masterOffset
            bus.SwapBuffers();

            // ── Advance another 0.1 s ────────────────────────────────────────
            slaveTick += TicksFromSeconds(0.1);
            ctrl.Update();

            // Expected SimTime (new design):
            //   = (slaveTick + masterOffset) / Freq = (0.6 + 0.3) = 0.9 s
            // Old design gives only ≈ 0.6 s (no correction without TimePulse).
            double expected = (slaveTick + masterOffset) / (double)Stopwatch.Frequency;
            Assert.Equal(expected, ctrl.GetCurrentState().TotalTime, 3);
        }

        // ── Bug 5 regression ─────────────────────────────────────────────────

        /// <summary>
        [Fact]
        public void SlaveSyncController_Resume_BaselineAnchorsTo_BarrierWallTicks_Not_LocalPrevFrame()
        {
            // Scenario: master sent the Resume at tick T_barrier = 3*frameTicks (in the past).
            // Slave's _prevFrameStartTicks is at 5*frameTicks (more recent than T_barrier).
            // After receiving Resume, slave advances to 6*frameTicks.
            //
            // With fix:  baseline = T_barrier = 3*frameTicks
            //             elapsed = 6*frameTicks - 3*frameTicks = 3*frameTicks → sim = 10.0 + 3*0.016 = 10.048
            // With bug:  baseline = _prevFrameStartTicks = 5*frameTicks
            //             elapsed = 6*frameTicks - 5*frameTicks = 1*frameTicks → sim = 10.0 + 1*0.016 = 10.016

            long ticks      = 0L;
            long frameTicks = TicksFromSeconds(0.016);

            var bus = CreateController_Bus(out var ctrl, () => ticks);
            bus.SwapBuffers(); bus.Consume<TimeSyncRequest>();

            // Run 5 frames to build up _prevFrameStartTicks = 5*frameTicks.
            for (int i = 0; i < 5; i++)
            {
                ticks += frameTicks;
                ctrl.Update();
                bus.SwapBuffers();
            }

            // Master's Resume was captured at 3*frameTicks (earlier than _prevFrameStartTicks).
            long masterBarrierTick = 3 * frameTicks;

            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Continuous,
                BarrierWallTicks = masterBarrierTick,
                SimTimeSnapshot  = 10.0,
                TimeScale        = 1.0f,
                FixedDelta       = 0f,
            });

            ticks += frameTicks; // SyncedWallTicks = 6*frameTicks > masterBarrierTick = 3*frameTicks
            bus.SwapBuffers();
            ctrl.Update(); // ApplyResume

            double fixedExpected  = 10.0 + 3.0 * frameTicks / Stopwatch.Frequency; // = 10.048
            double buggedExpected = 10.0 + 1.0 * frameTicks / Stopwatch.Frequency; // = 10.016

            double actual = ctrl.GetCurrentState().TotalTime;
            Assert.True(Math.Abs(actual - fixedExpected) < 0.001,
                $"Expected sim ≈ {fixedExpected:F4}s (baseline=BarrierWallTicks), " +
                $"got {actual:F4}s. Buggy value would be ≈ {buggedExpected:F4}s.");
        }

    }
}
