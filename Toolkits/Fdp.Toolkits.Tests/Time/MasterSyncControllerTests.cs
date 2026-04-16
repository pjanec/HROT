using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Fdp.ModuleHost.Time;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for TCU-MC001 (MasterSyncController) and TCU-T001 (coverage spec).
    /// Covers all 9 required success conditions plus 3 edge cases.
    /// </summary>
    public class MasterSyncControllerTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────

        private static long TicksFromSeconds(double seconds)
            => (long)(seconds * Stopwatch.Frequency);

        /// <summary>Creates a controller under test with a controllable tick source.</summary>
        private static MasterSyncController CreateController(
            FdpEventBus   bus,
            HashSet<int>? slaves     = null,
            TimeConfig?   config     = null,
            Func<long>?   tickSource = null)
        {
            return new MasterSyncController(
                bus,
                slaves ?? new HashSet<int>(),
                config,
                tickSource);
        }

        /// <summary>
        /// Transitions the controller from Continuous directly to Stepping by setting
        /// LookaheadWallTicks = 0 and driving one Update() after SwitchToDeterministic.
        /// </summary>
        private static void TransitionToStepping(
            MasterSyncController ctrl,
            FdpEventBus          bus,
            ref long             ticks)
        {
            var cfg = new TimeConfig { LookaheadWallTicks = 0 };
            // SwitchToDeterministic uses the controller's current config; we work around this
            // by advancing ticks slightly so the barrier (= _totalWallTicks) is crossed on Update.
            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks += 1; // cross the barrier (LookaheadWallTicks = 0 so barrier = 0)
            ctrl.Update();
        }

        // ── Required success conditions ──────────────────────────────────────

        /// <summary>TCU-MC001 §1 — Continuous mode accumulates time and increments frame count.</summary>
        [Fact]
        public void MasterSyncController_ContinuousMode_AdvancesTime()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var ctrl   = CreateController(bus, tickSource: () => ticks);

            ticks = TicksFromSeconds(0.016);
            ctrl.Update(); // frame 1

            ticks = TicksFromSeconds(0.032);
            var state = ctrl.Update(); // frame 2

            Assert.True(state.TotalTime > 0, "TotalTime must be positive after two updates.");
            Assert.Equal(2L, state.FrameNumber);
        }

        /// <summary>TCU-MC001 §2 — SwitchToDeterministic publishes a barrier event.</summary>
        [Fact]
        public void MasterSyncController_SwitchToDeterministic_PublishesBarrierEvent()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = TicksFromSeconds(0.2) };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // Drain the initial baseline event published by the constructor (Bug 3 fix).
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();

            long wallTicksBefore = ctrl.GetCurrentState().TotalWallTicks; // 0 — no Updates yet
            ctrl.SwitchToDeterministic(new HashSet<int>());

            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Deterministic, events[0].TargetMode);
            Assert.True(events[0].BarrierWallTicks > wallTicksBefore,
                "BarrierWallTicks must be in the future relative to current wall ticks.");
        }

        /// <summary>
        /// TCU-MC001 §3 — With LookaheadWallTicks = 0, the barrier is crossed immediately
        /// on the first Update() call, transitioning the mode to Deterministic.
        /// </summary>
        [Fact]
        public void MasterSyncController_BarrierPending_TransitionsToStepping()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int>());

            // Advance a tiny amount so the barrier is crossed (_totalWallTicks >= 0)
            ticks = TicksFromSeconds(0.001);
            ctrl.Update();

            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        /// <summary>
        /// TCU-MC001 §4 — Step() in Stepping mode publishes an AdvanceFrameIntent
        /// with FrameID == 1 and the correct FixedDelta.
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_PublishesAdvanceFrameIntent()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            // No slaves — _pendingAcks stays empty so Step() is never blocked.
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks = 1;
            ctrl.Update(); // crosses barrier → Stepping

            ctrl.Step(0.016f);

            bus.SwapBuffers();
            var intents = bus.ReadManaged<AdvanceFrameIntent>();

            Assert.Single(intents);
            Assert.Equal(1L,    intents[0].FrameID);
            Assert.Equal(0.016f, intents[0].FixedDelta, 4);
        }

        /// <summary>
        /// TCU-MC001 §5 — Step() is blocked while _pendingAcks is non-empty;
        /// it unblocks only after all slave ACKs are drained by Update().
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_BlocksUntilAllAcksReceived()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            ticks = 1;
            ctrl.Update(); // → Stepping, _expectedSlaves = {1, 2}

            // 1st Step succeeds: frame 0 → 1, _pendingAcks = {1, 2}
            var frame1 = ctrl.Step(0.016f);
            Assert.Equal(1L, frame1.FrameNumber);

            // 2nd Step blocked: pending ACKs from both slaves outstanding
            var blocked1 = ctrl.Step(0.016f);
            Assert.Equal(1L, blocked1.FrameNumber);

            // Slave 1 ACKs — still waiting for slave 2
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();
            ctrl.Update();

            var blocked2 = ctrl.Step(0.016f);
            Assert.Equal(1L, blocked2.FrameNumber);

            // Slave 2 ACKs — now unlocked
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();
            ctrl.Update();

            // 3rd Step advances to frame 2
            var frame2 = ctrl.Step(0.016f);
            Assert.Equal(2L, frame2.FrameNumber);
        }

        /// <summary>
        /// TCU-MC001 §6 — SwitchToContinuous() publishes a Continuous event whose
        /// SimTimeSnapshot matches the controller's sim time at the moment of the call,
        /// and BarrierWallTicks is non-zero (so slaves can anchor their baseline).
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToContinuous_PublishesSnapshotEvent()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // Seed a known sim-time.
            ctrl.SeedState(new GlobalTime { TotalTime = 42.0, FrameNumber = 100 });

            // Transition to Stepping. bus.Consume drains both constructor event and barrier event.
            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // discard (constructor + barrier events)

            ticks = 1;
            ctrl.Update(); // crosses barrier → Stepping (adds negligible delta)

            // Switch back to Continuous and inspect the published snapshot.
            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Continuous, events[0].TargetMode);
            Assert.Equal(42.0, events[0].SimTimeSnapshot, 2); // ≈ 42.0; tiny delta from Update OK
            // Bug 4 regression: BarrierWallTicks must be non-zero so slave can anchor its baseline.
            Assert.True(events[0].BarrierWallTicks > 0,
                "BarrierWallTicks must be non-zero on Resume so slaves anchor their wall-clock baseline.");
        }

        /// <summary>
        /// TCU-MC001 §7 — SwitchToContinuous() is idempotent when already in
        /// Continuous mode with no pending barrier.
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToContinuous_IdempotentWhenAlreadyContinuous()
        {
            var bus  = new FdpEventBus();
            var ctrl = CreateController(bus);

            // Drain the initial baseline event published by the constructor (Bug 3 fix).
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();

            // Both calls should be no-ops (already Continuous, no pending barrier).
            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            var firstCallEvents = bus.Read<SwitchTimeModeEvent>().ToArray();

            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            var secondCallEvents = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Empty(firstCallEvents);
            Assert.Empty(secondCallEvents);
        }

        /// <summary>TCU-MC001 §8 — SeedState() restores TotalTime and FrameNumber.</summary>
        [Fact]
        public void MasterSyncController_SeedState_RestoresTotalTime()
        {
            var bus  = new FdpEventBus();
            var ctrl = CreateController(bus);

            ctrl.SeedState(new GlobalTime { TotalTime = 99.0, FrameNumber = 500 });

            var state = ctrl.GetCurrentState();
            Assert.Equal(99.0, state.TotalTime, 4);
            Assert.Equal(500L, state.FrameNumber);
        }

        /// <summary>
        /// TCU-MC001 §9 — MasterSyncController TotalTime advances monotonically over 70 continuous frames.
        /// </summary>
        [Fact]
        public void MasterSyncController_TotalTime_AdvancesMonotonically_OverManyFrames()
        {
            long ticks          = 0;
            var bus             = new FdpEventBus();
            var ctrl            = CreateController(bus, tickSource: () => ticks);
            long frameTickDelta = TicksFromSeconds(0.016);

            double prev = 0.0;
            for (int i = 0; i < 70; i++)
            {
                ticks += frameTickDelta;
                var state = ctrl.Update();
                bus.SwapBuffers();
                Assert.True(state.TotalTime >= prev,
                    $"Frame {i}: TotalTime regressed from {prev:F4} to {state.TotalTime:F4}");
                prev = state.TotalTime;
            }
        }

        // ── Edge cases (TCU-T001) ────────────────────────────────────────────

        /// <summary>
        /// Edge case 1 — Step() called while in Continuous mode is a no-op;
        /// FrameNumber must not advance.
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_InContinuousMode_IsNoOp()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var ctrl   = CreateController(bus, tickSource: () => ticks);

            long frameBefore = ctrl.GetCurrentState().FrameNumber;
            var  result      = ctrl.Step(0.016f);
            long frameAfter  = ctrl.GetCurrentState().FrameNumber;

            Assert.Equal(frameBefore, result.FrameNumber);
            Assert.Equal(frameBefore, frameAfter);
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());
        }

        /// <summary>
        /// Edge case 2 — An ACK from an unrecognised node ID must be silently
        /// ignored; _pendingAcks must remain non-empty (step still blocked).
        /// </summary>
        [Fact]
        public void MasterSyncController_AckFromUnknownNode_IsIgnored()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int> { 1 });
            ticks = 1;
            ctrl.Update(); // → Stepping

            ctrl.Step(0.016f); // _pendingAcks = {1}
            bus.SwapBuffers();

            // ACK from an unknown node (99 is not in _expectedSlaves)
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 99 });
            bus.SwapBuffers();
            ctrl.Update(); // drains unknown ACK — should be silently discarded

            // Step is still blocked because node 1 has not ACK'd yet
            var blocked = ctrl.Step(0.016f);
            Assert.Equal(1L, blocked.FrameNumber);
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());
        }

        /// <summary>
        /// Edge case 3 — Two complete Continuous → Stepping → Continuous cycles
        /// must leave the controller in Continuous mode with positive TotalTime.
        /// </summary>
        [Fact]
        public void MasterSyncController_TwoFullPauseCycles_WorkCorrectly()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // ── First pause cycle ────────────────────────────────────────────
            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks += 1;
            ctrl.Update(); // → Stepping
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            ctrl.Step(0.016f); // advance one step
            bus.SwapBuffers();

            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());

            // Run a few Continuous frames to accumulate some time
            for (int i = 0; i < 5; i++)
            {
                ticks += TicksFromSeconds(0.016);
                ctrl.Update();
                bus.SwapBuffers();
            }

            double timeMidpoint = ctrl.GetCurrentState().TotalTime;
            Assert.True(timeMidpoint > 0.0);

            // ── Second pause cycle ───────────────────────────────────────────
            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks += 1;
            ctrl.Update(); // → Stepping again
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            ctrl.Step(0.016f); // advance one more step
            bus.SwapBuffers();

            ctrl.SwitchToContinuous();
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());

            double timeFinal = ctrl.GetCurrentState().TotalTime;
            Assert.True(timeFinal > timeMidpoint,
                "TotalTime must have advanced through both pause cycles.");
        }

        // ── TC2-P1-T1: Runtime slave set tests ───────────────────────────────

        /// <summary>
        /// TC2-P1-T1-SC1 — Controller built with empty slaves; SwitchToDeterministic
        /// is called with {1,2}. The second Step() call must not advance FrameNumber
        /// while ACKs from nodes 1 and 2 are outstanding.
        /// </summary>
        [Fact]
        public void MasterSyncController_RuntimeSlaveSet_BlocksUntilRuntimeAcks()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var config = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl   = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            ticks = 1;
            ctrl.Update(); // crosses barrier → Stepping

            // 1st Step: frame 0 → 1, re-arms _pendingAcks = {1, 2}
            var frame1 = ctrl.Step(0.016f);
            Assert.Equal(1L, frame1.FrameNumber);

            // 2nd Step: blocked — ACKs from 1 and 2 not yet received
            var blocked = ctrl.Step(0.016f);
            Assert.Equal(1L, blocked.FrameNumber);
        }

        /// <summary>
        /// TC2-P1-T1-SC2 — After SC1 state, publishing ACKs for both nodes and calling
        /// Update() must unblock the next Step(), which must advance FrameNumber to 2.
        /// </summary>
        [Fact]
        public void MasterSyncController_RuntimeSlaveSet_StepAdvancesAfterAcks()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var config = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl   = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            ticks = 1;
            ctrl.Update(); // → Stepping

            ctrl.Step(0.016f); // frame → 1, _pendingAcks = {1, 2}

            // Publish ACKs for both nodes
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 1 });
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();
            ctrl.Update(); // drains ACKs → _pendingAcks = {}

            // Next Step must advance
            var frame2 = ctrl.Step(0.016f);
            Assert.Equal(2L, frame2.FrameNumber);
        }

        /// <summary>
        /// TC2-P1-T1-SC3 — A second call to SwitchToDeterministic with a different set {3}
        /// replaces the first set {1,2}. Only the ACK from node 3 must unblock the step.
        /// </summary>
        [Fact]
        public void MasterSyncController_RuntimeSlaveSet_SecondCallReplacesFirstSet()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var config = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl   = CreateController(bus, config: config, tickSource: () => ticks);

            // First call with {1, 2}
            ctrl.SwitchToDeterministic(new HashSet<int> { 1, 2 });
            ticks = 1;
            ctrl.Update(); // → Stepping

            ctrl.Step(0.016f); // frame → 1, _pendingAcks = {1, 2}
            // ACK both so we can step again
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 1 });
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();
            ctrl.Update();

            // Resume to Continuous, then switch again with {3}
            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // discard

            ctrl.SwitchToDeterministic(new HashSet<int> { 3 });
            ticks += 1;
            ctrl.Update(); // → Stepping again

            ctrl.Step(0.016f); // frame → 2, _pendingAcks = {3}

            // ACKs from old nodes (1, 2) must be silently discarded
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 2, NodeID = 1 });
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 2, NodeID = 2 });
            bus.SwapBuffers();
            ctrl.Update();

            // Still blocked — node 3 hasn't ACK'd
            var blocked = ctrl.Step(0.016f);
            Assert.Equal(2L, blocked.FrameNumber);

            // Now ACK from node 3 unblocks
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 2, NodeID = 3 });
            bus.SwapBuffers();
            ctrl.Update();

            var frame3 = ctrl.Step(0.016f);
            Assert.Equal(3L, frame3.FrameNumber);
        }

        // ── TC3-P2-T01: Constructor initialises _totalWallTicks ─────────────

        /// <summary>
        /// TC3-P2-T01-SC1 — Constructing with tick source at 1_000_000 must initialise
        /// TotalWallTicks to 1_000_000, not 0.
        /// </summary>
        [Fact]
        public void MasterSyncController_Constructor_TotalWallTicks_InitialisedToNow()
        {
            long ticks = 1_000_000L;
            var bus    = new FdpEventBus();
            var ctrl   = CreateController(bus, tickSource: () => ticks);

            Assert.Equal(1_000_000L, ctrl.GetCurrentState().TotalWallTicks);
        }

        /// <summary>
        /// TC3-P2-T01-SC2 — SwitchToDeterministic with tick source at 1_000_000 and
        /// LookaheadWallTicks 500_000 must produce BarrierWallTicks >= 1_500_000.
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToDeterministic_BarrierIsAbsoluteNowPlusLookahead()
        {
            long ticks  = 1_000_000L;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 500_000L };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // Drain initial baseline published by constructor (Bug 3 fix).
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();

            ctrl.SwitchToDeterministic(new HashSet<int>());

            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.True(events[0].BarrierWallTicks >= 1_500_000L,
                $"Expected BarrierWallTicks >= 1_500_000, got {events[0].BarrierWallTicks}");
        }

        /// <summary>
        /// TC3-P2-T01-SC3 — Slave with same tick source transitions to Stepping when
        /// virtual wall ticks exceed the absolute barrier emitted by the master.
        /// </summary>
        [Fact]
        public void MasterSyncController_BarrierFix_SlaveEntersStepping_AfterLookahead()
        {
            long ticks     = 1_000_000L;
            var masterBus  = new FdpEventBus();
            var slaveBus   = new FdpEventBus();
            var config     = new TimeConfig { LookaheadWallTicks = 500_000L };

            var master = CreateController(masterBus, config: config, tickSource: () => ticks);
            var slave  = new SlaveSyncController(slaveBus, 1, config: config, tickSource: () => ticks);

            // Sync slave.
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();

            // Drain constructor event from masterBus before SwitchToDeterministic.
            masterBus.SwapBuffers();
            masterBus.Read<SwitchTimeModeEvent>();

            master.SwitchToDeterministic(new HashSet<int>());
            masterBus.SwapBuffers();
            var events = masterBus.Read<SwitchTimeModeEvent>().ToArray();
            Assert.Single(events);
            Assert.True(events[0].BarrierWallTicks >= 1_500_000L);

            // Relay to slave bus
            slaveBus.Publish(events[0]);
            slaveBus.SwapBuffers();

            // Advance to just below barrier
            ticks = 1_000_000L + 499_000L; // 1_499_000 < 1_500_000
            slave.Update(); // → BarrierPending (barrier not yet crossed)
            Assert.Equal(TimeMode.Continuous, slave.GetMode()); // BarrierPending appears as Continuous

            // Advance past barrier
            ticks += 1_100L;  // 1_500_100 >= 1_500_000
            slave.Update(); // → Stepping
            Assert.Equal(TimeMode.Deterministic, slave.GetMode());
        }

        // ── TC3-P2-T02: Step() populates TargetSimTime ──────────────────────

        /// <summary>
        /// TC3-P2-T02-SC1 — After one Step(0.016f), AdvanceFrameIntent.TargetSimTime must be > 0.
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_TargetSimTime_IsPopulated()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks = 1;
            ctrl.Update(); // → Stepping

            ctrl.Step(0.016f);

            bus.SwapBuffers();
            var intents = bus.ReadManaged<AdvanceFrameIntent>();

            Assert.Single(intents);
            Assert.True(intents[0].TargetSimTime > 0.0,
                $"TargetSimTime must be positive, got {intents[0].TargetSimTime}");
        }

        /// <summary>
        /// TC3-P2-T02-SC2 — After two consecutive steps, the second intent's TargetSimTime
        /// must equal the accumulated total (approx 0.032).
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_TargetSimTime_Accumulates()
        {
            long ticks  = 0;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int>());
            ticks = 1;
            ctrl.Update(); // → Stepping

            // First step
            ctrl.Step(0.016f);
            bus.SwapBuffers();
            var intents1 = bus.ReadManaged<AdvanceFrameIntent>();
            Assert.Single(intents1);
            Assert.Equal(0.016f, (float)intents1[0].TargetSimTime, 5);

            // Second step (no slaves, so no ACK wait)
            ctrl.Step(0.016f);
            bus.SwapBuffers();
            var intents2 = bus.ReadManaged<AdvanceFrameIntent>();
            Assert.Single(intents2);
            Assert.Equal(0.032f, (float)intents2[0].TargetSimTime, 4);
        }

        /// <summary>
        /// TC3-P2-T02-SC3 — Slave snaps TotalTime to master's TargetSimTime after one step.
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_SlaveSnapsToMasterSimTime()
        {
            long ticks     = 0;
            var masterBus  = new FdpEventBus();
            var slaveBus   = new FdpEventBus();
            var config     = new TimeConfig { LookaheadWallTicks = 0 };

            var master = CreateController(masterBus, config: config, tickSource: () => ticks);
            var slave  = new SlaveSyncController(slaveBus, 1, config: config, tickSource: () => ticks);

            // Sync slave so _isTimeSynced = true.
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();
            slaveBus.Publish(new TimeSyncOffsetCalculatedEvent { Rtt = 0L, NewOffset = 0L });
            slaveBus.SwapBuffers();
            slave.Update();
            slaveBus.SwapBuffers();
            slaveBus.Read<TimeSyncRequest>();

            // Transition master to Stepping
            // Drain constructor event first so only the Deterministic event is relayed.
            masterBus.SwapBuffers();
            masterBus.Read<SwitchTimeModeEvent>(); // drain constructor event

            master.SwitchToDeterministic(new HashSet<int>());
            masterBus.SwapBuffers();
            var switchEvents = masterBus.Read<SwitchTimeModeEvent>().ToArray();

            // Relay switch event to slave → BarrierPending
            slaveBus.Publish(switchEvents[0]);
            slaveBus.SwapBuffers();

            // Advance past barrier (LookaheadWallTicks=0, barrier=_getTick()@0 + 0 = 0)
            ticks = 1;
            slave.Update();   // → BarrierPending → Stepping (barrier=0 already passed)
            master.Update();  // → Stepping

            // Step master
            master.Step(0.016f);
            masterBus.SwapBuffers();
            var intents = masterBus.ReadManaged<AdvanceFrameIntent>().ToArray();

            // Relay intent to slave
            slaveBus.PublishManaged(intents[0]);
            slaveBus.SwapBuffers();
            slave.Update();

            double masterTime = master.GetCurrentState().TotalTime;
            double slaveTime  = slave.GetCurrentState().TotalTime;
            Assert.Equal(masterTime, slaveTime, 10);
        }

        // ── TC3-P2-T04: Barrier uses physical clock ──────────────────────────

        /// <summary>
        /// TC3-P2-T04-SC1 — Barrier equals getTick() + LookaheadWallTicks on first pause.
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToDeterministic_BarrierBasedOnPhysicalClock()
        {
            long ticks  = 5_000_000L;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 1_000_000L };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // Drain initial baseline published by constructor (Bug 3 fix).
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();

            ctrl.SwitchToDeterministic(new HashSet<int>());

            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            long expectedBarrier = ticks + config.LookaheadWallTicks; // 6_000_000
            Assert.Equal(expectedBarrier, events[0].BarrierWallTicks);
        }

        /// <summary>
        /// TC3-P2-T04-SC2 — After 10 fast steps, second pause barrier still equals
        /// getTick() + Lookahead (not corrupted by synthetic wall-tick increments).
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToDeterministic_BarrierCorrectAfterStepping()
        {
            long ticks  = 1_000_000L;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 500_000L };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            // First pause → barrier issued
            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // discard

            // Cross barrier → Stepping
            ticks += 500_001L; // now = 1_500_001 >= 1_500_000
            ctrl.Update();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode());

            // 10 fast steps — synthetic _totalWallTicks increases by 10 * Stopwatch.Frequency
            for (int i = 0; i < 10; i++)
                ctrl.Step(1.0f);
            bus.SwapBuffers();
            for (int i = 0; i < 10; i++)
                bus.ReadManaged<AdvanceFrameIntent>();

            // Resume
            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // discard resume

            // Second pause — ticks have NOT advanced physically
            long ticksAtSecondPause = ticks; // still 1_500_001L
            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            long expectedBarrier = ticksAtSecondPause + config.LookaheadWallTicks; // 2_000_001
            Assert.Equal(expectedBarrier, events[0].BarrierWallTicks);
        }

        /// <summary>
        /// TC3-P2-T04-SC3 — Master stays in BarrierPending while getTick() < barrier and
        /// transitions to Stepping exactly when getTick() >= barrier.
        /// </summary>
        [Fact]
        public void MasterSyncController_UpdateBarrierPending_UsesPhysicalClock()
        {
            long ticks  = 1_000_000L;
            var bus     = new FdpEventBus();
            var config  = new TimeConfig { LookaheadWallTicks = 100L };
            var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

            ctrl.SwitchToDeterministic(new HashSet<int>());
            long expectedBarrier = ticks + config.LookaheadWallTicks; // 1_000_100

            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // drain

            // One tick below barrier
            ticks = expectedBarrier - 1; // 1_000_099
            ctrl.Update();
            Assert.Equal(TimeMode.Continuous, ctrl.GetMode()); // BarrierPending

            // Exactly at barrier
            ticks = expectedBarrier; // 1_000_100
            ctrl.Update();
            Assert.Equal(TimeMode.Deterministic, ctrl.GetMode()); // Stepping
        }

        // ── TC3-P2-T03: Debug log emission ───────────────────────────────────

        /// <summary>
        /// TC3-P2-T03-SC2 — After Step(), a debug message containing "[TC3][Master] STEP"
        /// must have been emitted.
        /// </summary>
        [Fact]
        public void MasterSyncController_Step_EmitsDebugLog()
        {
            var memTarget = new NLog.Targets.MemoryTarget("test_master_step_debug");
            memTarget.Layout = "${message}";
            var logConfig = new NLog.Config.LoggingConfiguration();
            logConfig.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, memTarget, "*");
            var prevConfig = NLog.LogManager.Configuration;
            NLog.LogManager.Configuration = logConfig;

            try
            {
                long ticks  = 0;
                var bus     = new FdpEventBus();
                var config  = new TimeConfig { LookaheadWallTicks = 0 };
                var ctrl    = CreateController(bus, config: config, tickSource: () => ticks);

                ctrl.SwitchToDeterministic(new HashSet<int>());
                ticks = 1;
                ctrl.Update(); // → Stepping

                ctrl.Step(0.016f);

                Assert.Contains(memTarget.Logs, m => m.Contains("[TC3][Master] STEP"));
            }
            finally
            {
                NLog.LogManager.Configuration = prevConfig;
            }
        }

        // ── Bug regression tests (Bugs 1, 3, 4) ─────────────────────────────

        /// <summary>
        /// Bug 3 regression — Constructor must publish an initial SwitchTimeModeEvent(Continuous)
        /// into the DDS TransientLocal buffer so late-joining slaves receive the t=0 baseline.
        /// </summary>
        [Fact]
        public void MasterSyncController_Constructor_PublishesInitialBaseline()
        {
            long ticks = 1_000L;
            var bus    = new FdpEventBus();
            var ctrl   = new MasterSyncController(bus, config: TimeConfig.Default, tickSource: () => ticks);

            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Continuous, events[0].TargetMode);
            Assert.Equal(ticks, events[0].BarrierWallTicks);
            Assert.Equal(0.0, events[0].SimTimeSnapshot);
        }

        /// <summary>
        /// Bug 1 regression — SwitchToDeterministic must carry the master's current
        /// SimTimeSnapshot (not 0) so slaves know the authoritative sim time on Pause.
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToDeterministic_CarriesCurrentSimTime()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var config = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl   = CreateController(bus, config: config, tickSource: () => ticks);

            // Run 10 frames to build up non-zero sim time.
            for (int i = 0; i < 10; i++)
            {
                ticks += TicksFromSeconds(0.016);
                ctrl.Update();
                bus.SwapBuffers();
                bus.Read<SwitchTimeModeEvent>(); // drain initial + any events
            }

            double simBefore = ctrl.GetCurrentState().TotalTime;
            Assert.True(simBefore > 0, "sim time must be non-zero before pause");

            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Deterministic, events[0].TargetMode);
            Assert.Equal(simBefore, events[0].SimTimeSnapshot, 4);
        }

        /// <summary>
        /// Bug 4 regression — SwitchToContinuous must carry non-zero BarrierWallTicks
        /// so the slave can anchor its continuous-mode baseline to the master's exact
        /// virtual wall-clock snapshot rather than its local prev-frame tick.
        /// </summary>
        [Fact]
        public void MasterSyncController_SwitchToContinuous_CarriesCurrentWallTicks()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var config = new TimeConfig { LookaheadWallTicks = 0 };
            var ctrl   = CreateController(bus, config: config, tickSource: () => ticks);

            // Transition into Stepping (drains all prior events).
            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();
            ticks = 1;
            ctrl.Update(); // → Stepping

            ctrl.SwitchToContinuous();
            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Continuous, events[0].TargetMode);
            Assert.True(events[0].BarrierWallTicks > 0,
                "BarrierWallTicks must be non-zero on Resume so slaves can anchor their baseline.");
        }

        // ── Bus-drain intent tests (HEXAG2-S011) ─────────────────────────────

        /// <summary>
        /// Publishing a <see cref="PauseTimeIntent"/> to the bus and calling Update()
        /// causes MasterSyncController to call SwitchToDeterministic, which publishes
        /// a <see cref="SwitchTimeModeEvent"/> with <see cref="TimeMode.Deterministic"/>.
        /// </summary>
        [Fact]
        public void MasterSyncController_DrainsPauseTimeIntent_SwitchesToDeterministic()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var ctrl   = CreateController(bus, tickSource: () => ticks);

            // Drain the initial baseline event published by the constructor.
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>();

            // Publish SlaveNodeSetUpdatedEvent + PauseTimeIntent to the WRITE buffer.
            bus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = new HashSet<int> { 1, 2 } });
            bus.PublishManaged(new PauseTimeIntent());

            // Promote to READ so Update() can drain them.
            bus.SwapBuffers();
            ctrl.Update();

            // SwitchToDeterministic publishes SwitchTimeModeEvent to WRITE; promote to READ.
            bus.SwapBuffers();
            var events = bus.Read<SwitchTimeModeEvent>().ToArray();

            Assert.Single(events);
            Assert.Equal(TimeMode.Deterministic, events[0].TargetMode);
        }

        /// <summary>
        /// Publishing a <see cref="ResumeTimeIntent"/> to the bus after pausing and
        /// calling Update() causes MasterSyncController to call SwitchToContinuous, so
        /// <see cref="MasterSyncController.GetMode"/> returns <see cref="TimeMode.Continuous"/>.
        /// </summary>
        [Fact]
        public void MasterSyncController_DrainsResumeTimeIntent_SwitchesToContinuous()
        {
            long ticks = 0;
            var bus    = new FdpEventBus();
            var ctrl   = CreateController(bus, tickSource: () => ticks);

            // Switch to deterministic directly (tests the bus-drain resume path, not the pause path).
            ctrl.SwitchToDeterministic(new HashSet<int>());
            bus.SwapBuffers();
            bus.Read<SwitchTimeModeEvent>(); // drain that event

            // Publish ResumeTimeIntent to the WRITE buffer.
            bus.PublishManaged(new ResumeTimeIntent());

            // Promote to READ so Update() can drain it.
            bus.SwapBuffers();
            ctrl.Update();

            Assert.Equal(TimeMode.Continuous, ctrl.GetMode());
        }
    }
}
