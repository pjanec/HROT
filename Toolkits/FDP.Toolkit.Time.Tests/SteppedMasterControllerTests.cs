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
        [Fact]
        public void Update_WaitsForAllAcks_BeforeAdvancing()
        {
             var bus = new FdpEventBus();
             var master = new SteppedMasterController(bus, new HashSet<int>{1, 2}, new TimeConfig { FixedDeltaSeconds = 0.016f });
             
             // Frame 1 start (waiting for ACKs)
             master.Update();
             
             // Master issues Frame 1 order
             // Publish only ACK for Frame 1 from Node 1
             bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
             bus.SwapBuffers();
             
             // Should wait (need 2 acks)
             var time = master.Update();
             Assert.Equal(0.0f, time.DeltaTime);
             
             // Publish ACK for Frame 1 from Node 2
             bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
             bus.SwapBuffers();
             
             // Should advance to Frame 2
             time = master.Update();
             Assert.Equal(2, time.FrameNumber);
             Assert.Equal(0.016f, time.DeltaTime, precision: 3);
        }

        [Fact]
        public void OnFrameAck_IgnoresOldFrames()
        {
             var bus = new FdpEventBus();
             var master = new SteppedMasterController(bus, new HashSet<int>{1}, new TimeConfig { FixedDeltaSeconds = 0.016f });
             
             master.Update(); // Frame 1 issued, waiting for ACK 1
             
             // Publish ACK for Frame 0 (old)
             bus.Publish(new FrameAckDescriptor { FrameID = 0, NodeID = 1 });
             bus.SwapBuffers();
             
             var time = master.Update();
             Assert.Equal(0.0f, time.DeltaTime); // Still waiting for ACK 1
             
             // Send ACK 1
             bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
             bus.SwapBuffers();
             
             time = master.Update();
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
            
            // Frame 1 start
            master.Update();
            
            // All 3 slaves send ACKs for Frame 1
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 3 });
            bus.SwapBuffers();
            
            // Should advance to Frame 2 (all ACKs received)
            var time = master.Update();
            Assert.Equal(2, time.FrameNumber);
            Assert.Equal(0.016f, time.DeltaTime, precision: 3);
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

            // Mutate the original set after construction
            originalIds.Add(99);
            originalIds.Remove(1);

            // Frame 1: both nodes 1 and 2 must ACK (not the externally-mutated set)
            master.Update(); // issues frame 1 order

            // Send ACK only from node 1 (the one removed from the original)
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 1 });
            bus.SwapBuffers();

            // Still waiting — controller expects ACK from node 2 as well (its internal copy is {1,2})
            var timeAfterPartial = master.Update();
            Assert.Equal(0.0f, timeAfterPartial.DeltaTime);

            // Send ACK from node 2 — now all ACKs are satisfied
            bus.Publish(new FrameAckDescriptor { FrameID = 1, NodeID = 2 });
            bus.SwapBuffers();

            var timeAfterFull = master.Update();
            Assert.Equal(2, timeAfterFull.FrameNumber);
        }
    }
}
