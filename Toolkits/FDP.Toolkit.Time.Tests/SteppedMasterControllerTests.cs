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
    }
}
