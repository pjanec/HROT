using Xunit;
using ModuleHost.Core.Time;
using Fdp.Kernel;
using System.Collections.Generic;

using FDP.Toolkit.Time.Controllers;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time.Tests
{
    public class SteppedMasterControllerTests
    {
        /// <summary>
        /// After all slave ACKs arrive, Update() must NOT auto-advance —
        /// the simulation only moves forward when Step() is called explicitly.
        /// This is the core fix for the "keeps advancing after Pause" bug.
        /// </summary>
        [Fact]
        public void Update_DoesNotAutoStep_EvenWhenAllAcksReceived()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(bus, new HashSet<int>{1}, new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Issue frame 1 manually.
            master.Step(0.016f);

            // All ACKs arrive.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();

            // Multiple Update() calls must not auto-advance beyond frame 1.
            for (int i = 0; i < 5; i++)
            {
                var t = master.Update();
                Assert.Equal(1, t.FrameNumber);
                Assert.Equal(0.0f, t.DeltaTime);
            }
        }

        /// <summary>
        /// Update() processes ACKs. Until all ACKs for the current frame arrive,
        /// the next Step() call must be ignored (returns current time unchanged).
        /// </summary>
        [Fact]
        public void Update_WaitsForAllAcks_BeforeNextStepIsAccepted()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(bus, new HashSet<int>{1, 2}, new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Issue frame 1 manually.
            master.Step(0.016f);

            // Only one of two ACKs arrives.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();
            master.Update();

            // Step() while still waiting must be ignored.
            var blocked = master.Step(0.016f);
            Assert.Equal(1, blocked.FrameNumber);
            Assert.Equal(0.0f, blocked.DeltaTime);

            // Second ACK arrives — now unlocked.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();
            master.Update();

            // Explicit Step() now advances to frame 2.
            var t = master.Step(0.016f);
            Assert.Equal(2, t.FrameNumber);
            Assert.Equal(0.016f, t.DeltaTime, precision: 3);
        }

        [Fact]
        public void OnFrameAck_IgnoresOldFrames()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(bus, new HashSet<int>{1}, new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Issue frame 1.
            master.Step(0.016f);

            // Publish ACK for Frame 0 (stale).
            bus.Publish(new FrameAckDescriptor { FrameID = 0, NodeID = 1 });
            bus.SwapBuffers();

            // Update processes stale ACK — still waiting for frame 1 ACK.
            var time = master.Update();
            Assert.Equal(0.0f, time.DeltaTime);

            // Step() still blocked.
            var blocked = master.Step(0.016f);
            Assert.Equal(1, blocked.FrameNumber);

            // Valid ACK 1 arrives.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();
            master.Update();

            // Now unlocked — Step() advances to frame 2.
            time = master.Step(0.016f);
            Assert.Equal(2, time.FrameNumber);
        }

        [Fact]
        public void Master_HandlesMultipleConcurrentAcks()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(
                bus,
                new HashSet<int> { 1, 2, 3 },
                new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Issue frame 1.
            master.Step(0.016f);

            // All 3 slaves ACK simultaneously.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 3 });
            bus.SwapBuffers();
            master.Update(); // process all ACKs — unlocked

            // Explicit Step() advances to frame 2.
            var time = master.Step(0.016f);
            Assert.Equal(2, time.FrameNumber);
            Assert.Equal(0.016f, time.DeltaTime, precision: 3);
        }

        /// <summary>
        /// With no slave nodes, Step() never sets _waitingForAcks, so consecutive
        /// Step() calls advance freely (single-process / zero-network scenario).
        /// </summary>
        [Fact]
        public void Step_NoSlaves_AdvancesEveryCall()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(bus, new HashSet<int>(), new TimeConfig { FixedDeltaSeconds = 0.016f });

            for (int i = 1; i <= 3; i++)
            {
                var t = master.Step(0.016f);
                Assert.Equal(i, t.FrameNumber);
                Assert.Equal(0.016f, t.DeltaTime, precision: 3);
            }
        }

        /// <summary>
        /// DEBT-WCR-01: Verifies that mutating the original HashSet after construction
        /// does not affect the controller's internal slave node set.
        /// </summary>
        [Fact]
        public void Constructor_MakesDefensiveCopy_ExternalMutationIgnored()
        {
            var bus = new FdpEventBus();
            var originalIds = new HashSet<int> { 1, 2 };
            var master = new SteppedMasterController(bus, originalIds, new TimeConfig { FixedDeltaSeconds = 0.016f });

            // Mutate after construction.
            originalIds.Add(99);
            originalIds.Remove(1);

            // Issue frame 1 — controller uses its own copy {1, 2}.
            master.Step(0.016f);

            // ACK from node 1 only.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();

            // Still waiting — controller expects ACK from node 2 as well.
            var timeAfterPartial = master.Update();
            Assert.Equal(0.0f, timeAfterPartial.DeltaTime);

            // Final ACK.
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();
            master.Update(); // unlocked

            // Now Step() advances to frame 2.
            var timeAfterFull = master.Step(0.016f);
            Assert.Equal(2, timeAfterFull.FrameNumber);
        }

        /// <summary>
        /// Step() called with a custom delta must use exactly that delta — 
        /// verifies VarDelta stepping path used by OrchestratorSubsystem.StepTime.
        /// </summary>
        [Fact]
        public void Step_UsesCustomDelta_NotConfigDefault()
        {
            var bus = new FdpEventBus();
            var master = new SteppedMasterController(bus, new HashSet<int>(), new TimeConfig { FixedDeltaSeconds = 0.016f });

            var t = master.Step(0.1f); // 10× larger than default
            Assert.Equal(0.1f, t.DeltaTime, precision: 4);
            Assert.Equal(0.1, t.TotalTime,  precision: 4);
        }
    }
}
