using Xunit;
using Fdp.Kernel;
using System.Collections.Generic;

namespace Fdp.Tests
{
    public class EventAccumulationIntegrationTests
    {
        [EventId(9201)]
        public struct PositionEvent { public int X, Y; public uint Frame; }

        public class ChatMessageEvent { public string Text { get; set; } = string.Empty; public uint Frame; }

        [Fact]
        public void Integration_AccumulatorReplaysHistoryToReplica()
        {
            // 1. Setup "Server" / Live Simulation
            using var serverRepo = new EntityRepository();
            // serverRepo.RegisterUnmanagedComponent<...>(); // Not components, just events

            var accumulator = new EventAccumulator(maxHistoryFrames: 10);
            
            // 2. Simulate 5 frames
            for (uint frame = 1; frame <= 5; frame++)
            {
                serverRepo.SetGlobalVersion(frame);
                
                // Publish events
                serverRepo.Bus.Publish(new PositionEvent { X = 10, Y = (int)frame, Frame = frame });
                serverRepo.Bus.PublishManaged(new ChatMessageEvent { Text = $"Msg {frame}", Frame = frame });
                
                // End of frame (Swap)
                serverRepo.Bus.SwapBuffers();
                
                // Capture history
                accumulator.CaptureFrame(serverRepo.Bus, frame);
            }
            
            // 3. Setup "Client" / Replica
            // Client joins nicely at frame 6, needs history from 0 to 5
            using var clientRepo = new EntityRepository();
            
            // IMPORTANT: Client must know about event types to consume them?
            // Actually, InjectEvents injects bytes/objects.
            // But to Consume<T>, the stream must be registered/created.
            // Publish/Consume logic in FdpEventBus creates streams lazily on First Use.
            // InjectEvents -> InjectIntoCurrentBySize -> GetStream(create=true).
            // But we need to make sure the TypeId mapping is correct. 
            // Since we share the codebase, TypeIds are stable.
            
            // 4. Flush History
            // Client says "I have seen nothing (Tick 0)"
            accumulator.FlushToReplica(clientRepo.Bus, 0);
            
            // 5. Verify Client State
            // Native Events
            var posEvents = clientRepo.Bus.Consume<PositionEvent>();
            Assert.Equal(5, posEvents.Length);
            for(int i=0; i<5; i++)
            {
                Assert.Equal((uint)(i + 1), posEvents[i].Frame);
                Assert.Equal(i + 1, posEvents[i].Y);
            }
            
            // Managed Events
            var msgEvents = clientRepo.Bus.ConsumeManaged<ChatMessageEvent>();
            Assert.Equal(5, msgEvents.Count);
            for(int i=0; i<5; i++)
            {
                Assert.Equal((uint)(i + 1), msgEvents[i].Frame);
                Assert.Equal($"Msg {i+1}", msgEvents[i].Text);
            }
        }
        
        [Fact]
        public void Integration_LateJoiner_ReceivesOnlyNewHistory()
        {
            using var serverRepo = new EntityRepository();
            var accumulator = new EventAccumulator(maxHistoryFrames: 10);
            
            // Frames 1-3
            for (uint frame = 1; frame <= 3; frame++)
            {
                serverRepo.Bus.Publish(new PositionEvent { Frame = frame });
                serverRepo.Bus.SwapBuffers();
                accumulator.CaptureFrame(serverRepo.Bus, frame);
            }
            
            using var clientRepo = new EntityRepository();
            
            // Client already saw up to frame 2
            accumulator.FlushToReplica(clientRepo.Bus, 2);
            
            var events = clientRepo.Bus.Consume<PositionEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(3u, events[0].Frame);
        }
    }
}
