using Xunit;
using Fdp.Kernel;
using System;
using System.Collections.Generic;

namespace Fdp.Tests
{
    [EventId(101)]
    public struct TestEventA { public int Value; }
    
    public class TestManagedEventA { public string Message { get; set; } = string.Empty; }

    public class EventAccumulatorTests
    {
        [Fact]
        public void CaptureFrame_StoresEventData()
        {
            using var bus = new FdpEventBus();
            bus.Publish(new TestEventA { Value = 42 });
            bus.SwapBuffers(); // Move to Read buffer
            
            var accum = new EventAccumulator();
            accum.CaptureFrame(bus, 10);
            
            // Verify via Flush
            using var replicaBus = new FdpEventBus();
            accum.FlushToReplica(replicaBus, 9); // Last seen 9, should see 10
            
            var events = replicaBus.Consume<TestEventA>();
            Assert.Equal(1, events.Length);
            Assert.Equal(42, events[0].Value);
        }
        
        [Fact]
        public void CaptureMultipleFrames_MaintainsOrder()
        {
            using var bus = new FdpEventBus();
            var accum = new EventAccumulator();
            
            // Frame 1
            bus.Publish(new TestEventA { Value = 1 });
            bus.SwapBuffers(); 
            accum.CaptureFrame(bus, 1);
            
            // Frame 2
            bus.Publish(new TestEventA { Value = 2 });
            bus.SwapBuffers();
            accum.CaptureFrame(bus, 2);
            
            using var replicaBus = new FdpEventBus();
            accum.FlushToReplica(replicaBus, 0); // Replay all
            
            var events = replicaBus.Consume<TestEventA>();
            Assert.Equal(2, events.Length);
            Assert.Equal(1, events[0].Value);
            Assert.Equal(2, events[1].Value);
        }

        [Fact]
        public void FlushToReplica_FiltersByLastSeenTick()
        {
            using var bus = new FdpEventBus();
            var accum = new EventAccumulator();
            
            // Frame 1
            bus.Publish(new TestEventA { Value = 1 });
            bus.SwapBuffers();
            accum.CaptureFrame(bus, 1);
            
            // Frame 2
            bus.Publish(new TestEventA { Value = 2 });
            bus.SwapBuffers();
            accum.CaptureFrame(bus, 2);
            
            using var replicaBus = new FdpEventBus();
            accum.FlushToReplica(replicaBus, 1); // Seen 1, expect 2
            
            var events = replicaBus.Consume<TestEventA>();
            Assert.Equal(1, events.Length);
            Assert.Equal(2, events[0].Value);
        }

        [Fact]
        public void FlushToReplica_HandlesManagedEvents()
        {
            using var bus = new FdpEventBus();
            var accum = new EventAccumulator();
            
            bus.PublishManaged(new TestManagedEventA { Message = "Hello" });
            bus.SwapBuffers();
            accum.CaptureFrame(bus, 1);
            
            using var replicaBus = new FdpEventBus();
            accum.FlushToReplica(replicaBus, 0);
            
            var events = replicaBus.ConsumeManaged<TestManagedEventA>();
            Assert.Single(events);
            Assert.Equal("Hello", events[0].Message);
        }
        
        [Fact]
        public void Performance_FlushSixFrames_UnderTarget()
        {
            using var bus = new FdpEventBus();
            // Pre-warm type
            bus.Publish(new TestEventA { Value = 0 });
            bus.SwapBuffers();
            bus.Consume<TestEventA>();
            
            // Create 6 frames of data
            var accum = new EventAccumulator();
            for (uint i = 1; i <= 6; i++)
            {
                // 1000 events per frame
                for (int j = 0; j < 1000; j++) bus.Publish(new TestEventA { Value = j });
                bus.SwapBuffers();
                accum.CaptureFrame(bus, i);
            }
            
            using var replicaBus = new FdpEventBus();
            // Pre-warm replica (register type)
            replicaBus.Publish(new TestEventA()); 
            replicaBus.SwapBuffers();
            replicaBus.ClearCurrentBuffers(); // Clear pre-warm
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            accum.FlushToReplica(replicaBus, 0);
            sw.Stop();
            
            // Verify
            var events = replicaBus.Consume<TestEventA>();
            Assert.Equal(6000, events.Length);
            
            // Benchmark
            Assert.True(sw.Elapsed.TotalMilliseconds < 5.0, $"Flush took {sw.Elapsed.TotalMilliseconds}ms");
        }
    }
}
