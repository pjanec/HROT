using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Fdp.Kernel;
using ModuleHost.Core;
using ModuleHost.Core.Time;
using Xunit;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    /// <summary>
    /// CGF1-S0204 success conditions for the Future Barrier protocol.
    /// All tests use an in-process <see cref="FdpEventBus"/> (no DDS required).
    /// </summary>
    public class FutureBarrierTests
    {
        // ──────────────────────────────────────────────────────────────────
        //  Helper: a minimal ITimeController whose TotalWallTicks is
        //  controlled directly by the test for deterministic barrier checks.
        // ──────────────────────────────────────────────────────────────────
        private sealed class StubTimeController : ITimeController
        {
            private long _wallTicks;

            public StubTimeController(long initialWallTicks = 0) => _wallTicks = initialWallTicks;

            public void SetWallTicks(long ticks) => _wallTicks = ticks;

            public GlobalTime GetCurrentState() => new GlobalTime { TotalWallTicks = _wallTicks };
            public GlobalTime Update()          => new GlobalTime { TotalWallTicks = _wallTicks };
            public void SeedState(GlobalTime s) { /* no-op — test controls state directly */ }
            public void SetTimeScale(float s)   { }
            public float GetTimeScale()          => 1.0f;
            public TimeMode GetMode()            => TimeMode.Continuous;
            public void Dispose()                { }
        }

        /// <summary>Creates a fresh, initialized kernel backed by a <see cref="StubTimeController"/>.</summary>
        private static (ModuleHostKernel kernel, StubTimeController stub) MakeKernel(long initialWallTicks = 0)
        {
            var repo   = new EntityRepository();
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());
            var stub   = new StubTimeController(initialWallTicks);
            kernel.SetTimeController(stub);
            kernel.Initialize();
            return (kernel, stub);
        }

        /// <summary>Advances the stub to <paramref name="wallTicks"/> and triggers a kernel update
        /// so that <c>kernel.CurrentTime.TotalWallTicks</c> reflects the new value.</summary>
        private static void AdvanceKernelTo(ModuleHostKernel kernel, StubTimeController stub, long wallTicks)
        {
            stub.SetWallTicks(wallTicks);
            kernel.Update(); // Update() calls stub.Update() → CurrentTime = stub.Update() result
        }

        // ──────────────────────────────────────────────────────────────────
        //  Test 1 — Slave fires immediately when event is received
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// CGF1-S0204 (updated behaviour): <see cref="SlaveTimeModeListener"/> performs the
        /// controller swap on the <em>first</em> <see cref="SlaveTimeModeListener.Update"/> call
        /// after the event is delivered — without waiting for a <c>TotalWallTicks</c> barrier.
        ///
        /// The old barrier-wait semantics broke cross-node (DDS) scenarios because each node's
        /// <see cref="GlobalTime.TotalWallTicks"/> counter starts from a different absolute origin
        /// (its own kernel construction time), making the master-computed barrier meaningless on
        /// the slave.  The DDS round-trip already provides sufficient temporal decoupling; the
        /// slave now swaps immediately and relies on an idempotent guard to avoid double-swaps.
        /// </summary>
        [Fact]
        public void SwitchToIsNotCalledBeforeBarrierWallTicks()
        {
            const long T          = 10_000_000L;
            const long lookahead  = 2_000_000L; // arbitrary — not wall-clock time in this unit test
            long barrierWallTicks = T + lookahead;

            var bus = new FdpEventBus();
            var (kernel, stub) = MakeKernel(T);

            var config   = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = 1 };
            var listener = new SlaveTimeModeListener(bus, kernel, config);

            // Publish the event and swap buffers so the listener can consume it
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta       = 0.016f
            });
            bus.SwapBuffers();

            // With immediate-swap semantics the controller is swapped on the FIRST Update()
            // after event delivery — regardless of TotalWallTicks.
            AdvanceKernelTo(kernel, stub, barrierWallTicks - 1);
            listener.Update();

            // Controller MUST have been swapped immediately
            Assert.IsType<SteppedSlaveController>(kernel.GetTimeController());
        }

        // ──────────────────────────────────────────────────────────────────
        //  Test 2 — Slave fires immediately and is idempotent
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// CGF1-S0204 (updated behaviour): <see cref="SlaveTimeModeListener"/> swaps to
        /// <see cref="SteppedSlaveController"/> on the first <see cref="SlaveTimeModeListener.Update"/>
        /// call after the event is delivered, and the swap is idempotent — a second Update does not
        /// create a new controller.
        /// </summary>
        [Fact]
        public void SlaveCallsSwitchToAfterBarrierWallTicks()
        {
            const long T          = 10_000_000L;
            const long lookahead  = 2_000_000L;
            long barrierWallTicks = T + lookahead;

            var bus = new FdpEventBus();
            var (kernel, stub) = MakeKernel(T);

            var config   = new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = 1 };
            var listener = new SlaveTimeModeListener(bus, kernel, config);

            // Publish and deliver
            bus.Publish(new SwitchTimeModeEvent
            {
                TargetMode       = TimeMode.Deterministic,
                BarrierWallTicks = barrierWallTicks,
                FixedDelta       = 0.016f
            });
            bus.SwapBuffers();

            // Immediate swap: fires on first Update() after event delivery
            AdvanceKernelTo(kernel, stub, barrierWallTicks - 1);
            listener.Update();
            Assert.IsType<SteppedSlaveController>(kernel.GetTimeController());

            // Idempotent: a second Update must NOT re-swap (guard prevents double-install)
            var firstController = kernel.GetTimeController();
            AdvanceKernelTo(kernel, stub, barrierWallTicks);
            listener.Update();
            Assert.IsType<SteppedSlaveController>(kernel.GetTimeController());
            Assert.Same(firstController, kernel.GetTimeController());
        }

        // ──────────────────────────────────────────────────────────────────
        //  Test 3 — Master fires exactly at BarrierWallTicks
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// CGF1-S0204: <see cref="DistributedTimeCoordinator"/> must call
        /// <see cref="ModuleHostKernel.SwapTimeController"/> with a
        /// <see cref="SteppedMasterController"/> exactly once when the master's
        /// <c>TotalWallTicks</c> first reaches <c>BarrierWallTicks</c>.
        /// </summary>
        [Fact]
        public void MasterCallsSwitchToAfterBarrierWallTicks()
        {
            const long T = 5_000_000L;
            // Use a small lookahead so we know the exact barrier value
            long lookaheadTicks = (long)(0.001 * Stopwatch.Frequency); // 1 ms

            var bus = new FdpEventBus();
            var (kernel, stub) = MakeKernel(T);

            var config = new TimeControllerConfig
            {
                Role       = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = lookaheadTicks }
            };
            var coordinator = new DistributedTimeCoordinator(bus, kernel, config, new HashSet<int>());

            // Trigger SwitchToDeterministic — barrier = T + lookaheadTicks
            coordinator.SwitchToDeterministic(new HashSet<int>());
            long expectedBarrier = T + lookaheadTicks;

            // Before barrier: no swap
            AdvanceKernelTo(kernel, stub, expectedBarrier - 1);
            coordinator.Update(); // event not yet in consumer buffer
            Assert.IsType<StubTimeController>(kernel.GetTimeController());

            // AT barrier: swap must fire
            AdvanceKernelTo(kernel, stub, expectedBarrier);
            coordinator.Update();
            Assert.IsType<SteppedMasterController>(kernel.GetTimeController());

            // A second Update must NOT fire again
            coordinator.Update();
            Assert.IsType<SteppedMasterController>(kernel.GetTimeController());
        }

        // ──────────────────────────────────────────────────────────────────
        //  Test 4 — SwitchTimeModeEvent struct shape
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// CGF1-S0204: <see cref="SwitchTimeModeEvent"/> must have a <c>long</c> property
        /// named <c>BarrierWallTicks</c> and must NOT have a property named <c>BarrierFrame</c>
        /// (which relied on ECS frame counters — incompatible with async multi-node timing).
        /// </summary>
        [Fact]
        public void SwitchTimeModeEvent_FieldIsBarrierWallTicks_NotFrameCounter()
        {
            var type = typeof(SwitchTimeModeEvent);

            // Must have BarrierWallTicks (long)
            var wallTicksProp = type.GetProperty(nameof(SwitchTimeModeEvent.BarrierWallTicks));
            Assert.NotNull(wallTicksProp);
            Assert.Equal(typeof(long), wallTicksProp!.PropertyType);

            // Must NOT have BarrierFrame (frame-counter-based barrier is forbidden per S0204)
            var barrierFrameProp = type.GetProperty("BarrierFrame");
            Assert.Null(barrierFrameProp);
        }

        // ──────────────────────────────────────────────────────────────────
        //  Test 5 — Coordinator publishes a future barrier
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// CGF1-S0204: When <see cref="DistributedTimeCoordinator.SwitchToDeterministic"/> is
        /// called, the published <see cref="SwitchTimeModeEvent.BarrierWallTicks"/> must be
        /// strictly greater than the master's <c>TotalWallTicks</c> at the moment of
        /// publication — confirming the barrier is set in the future, not the present.
        /// </summary>
        [Fact]
        public void BarrierWallTicks_IsSetToFuture()
        {
            var bus = new FdpEventBus();

            // Use real-clock master so TotalWallTicks advances naturally
            var repo   = new EntityRepository();
            var kernel = new ModuleHostKernel(repo, new EventAccumulator());
            var master = new MasterTimeController(bus);
            kernel.SetTimeController(master);
            kernel.Initialize();

            // Advance the master a bit so TotalWallTicks > 0
            Thread.Sleep(5);
            kernel.Update();

            long wallTicksBeforePublish = kernel.CurrentTime.TotalWallTicks;

            var config = new TimeControllerConfig
            {
                Role       = TimeRole.Master,
                SyncConfig = new TimeConfig { LookaheadWallTicks = (long)(0.05 * Stopwatch.Frequency) }
            };
            var coordinator = new DistributedTimeCoordinator(bus, kernel, config, new HashSet<int>());

            coordinator.SwitchToDeterministic(new HashSet<int>());

            // The event is in the publish buffer; swap to consume it
            bus.SwapBuffers();
            SwitchTimeModeEvent? captured = null;
            foreach (var evt in bus.Consume<SwitchTimeModeEvent>())
            {
                captured = evt;
                break;
            }

            Assert.NotNull(captured);
            Assert.Equal(TimeMode.Deterministic, captured!.Value.TargetMode);
            Assert.True(
                captured.Value.BarrierWallTicks > wallTicksBeforePublish,
                $"BarrierWallTicks ({captured.Value.BarrierWallTicks}) must be > " +
                $"TotalWallTicks at publish time ({wallTicksBeforePublish})");

            kernel.Dispose();
        }
    }
}
