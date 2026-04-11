using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Simple debug test to isolate event bus recording/playback issues
    /// </summary>
    public class EventBusDebugTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testFilePath;
        
        public EventBusDebugTests(ITestOutputHelper output)
        {
            _output = output;
            _testFilePath = Path.Combine(Path.GetTempPath(), $"debug_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [EventId(301)]
        public struct SimpleEvent
        {
            public int Value;
        }

        [Fact]
        public void Debug_EventBusBasics()
        {
            _output.WriteLine("=== Testing Event Bus Basics ===\n");
            
            using var eventBus = new FdpEventBus();
            
            // 1. Publish
            _output.WriteLine("1. Publishing event with Value=42");
            eventBus.Publish(new SimpleEvent { Value = 42 });
            
            // 2. Check pending streams
            var pendingStreams = eventBus.GetAllPendingStreams();
            int count = 0;
            foreach (var stream in pendingStreams)
            {
                count++;
                _output.WriteLine($"   Pending stream: TypeID={stream.EventTypeId}, ElementSize={stream.ElementSize}");
                var bytes = stream.GetPendingBytes();
                _output.WriteLine($"   Pending bytes: {bytes.Length} bytes");
                
                // Decode the value
                if (bytes.Length == sizeof(int))
                {
                    int value = BitConverter.ToInt32(bytes);
                    _output.WriteLine($"   Decoded value: {value}");
                }
            }
            
            _output.WriteLine($"   Total pending streams: {count}");
            Assert.Equal(1, count);
            
            // 3. Swap buffers
            _output.WriteLine("\n2. Swapping buffers...");
            eventBus.SwapBuffers();
            
            // 4. Consume
            _output.WriteLine("3. Consuming from Current buffer...");
            var events = eventBus.Consume<SimpleEvent>();
            _output.WriteLine($"   Consumed {events.Length} events");
            if (events.Length > 0)
            {
                _output.WriteLine($"   Event value: {events[0].Value}");
            }
            
            Assert.Equal(1, events.Length);
            Assert.Equal(42, events[0].Value);
            
            _output.WriteLine("\n✅ Event bus basics work!");
        }

        [Fact]
        public void Debug_DirectInjection()
        {
            _output.WriteLine("=== Testing Direct Injection (No Pre-Registration!) ===\n");
            
            using var eventBus = new FdpEventBus();
            
            // NO dummy Publish needed! Stream created automatically!
            
            // Create event data manually
            var evt = new SimpleEvent { Value = 99 };
            byte[] data;
            unsafe
            {
                byte* ptr = (byte*)&evt;
                data = new byte[sizeof(SimpleEvent)];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = ptr[i];
                }
            }
            
            _output.WriteLine($"\n1. Created event data: {data.Length} bytes, Value=99");
            _output.WriteLine($"   Bytes: {BitConverter.ToString(data)}");
            _output.WriteLine($"   Decoded: {BitConverter.ToInt32(data, 0)}");
            
            // Clear and inject
            _output.WriteLine("\n2. Clearing current buffers...");
            eventBus.ClearCurrentBuffers();
            
            _output.WriteLine("3. Injecting into current using BySize (auto-creates stream)...");
            eventBus.InjectIntoCurrentBySize(EventType<SimpleEvent>.Id, sizeof(int), data);

            
            // Consume
            _output.WriteLine("4. Consuming...");
            var events = eventBus.Consume<SimpleEvent>();
            _output.WriteLine($"   Consumed {events.Length} events");
            if (events.Length > 0)
            {
                _output.WriteLine($"   Event value: {events[0].Value}");
            }
            
            Assert.Equal(1, events.Length);
            Assert.Equal(99, events[0].Value);
            
            _output.WriteLine("\n✅ Direct injection works!");
        }

        [Fact]
        public void Debug_RecordAndReplay_Minimal()
        {
            _output.WriteLine("=== Minimal Record/Replay Test ===\n");
            
            // RECORD
            _output.WriteLine("--- RECORDING ---");
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var recorder = new RecorderSystem();
                
                // Publish event
                _output.WriteLine("1. Publishing event Value=77");
                eventBus.Publish(new SimpleEvent { Value = 77 });
                
                // Check what's in pending
                var pending = eventBus.GetAllPendingStreams();
                int streamCount = 0;
                foreach (var stream in pending)
                {
                    streamCount++;
                    var bytes = stream.GetPendingBytes();
                    _output.WriteLine($"   Pending stream: TypeID={stream.EventTypeId}, Bytes={bytes.Length}");
                    _output.WriteLine($"   Data: {BitConverter.ToString(bytes.ToArray())}");
                }
                _output.WriteLine($"   Total streams: {streamCount}");
                
                // Tick and record
                repo.Tick();
                _output.WriteLine($"\n2. Recording delta frame (version={repo.GlobalVersion})...");
                recorder.RecordDeltaFrame(repo, 0, writer, 0L, eventBus);
                
                // Show what was written
                byte[] recorded = ms.ToArray();
                _output.WriteLine($"   Recorded {recorded.Length} bytes total");
                _output.WriteLine($"   First 50 bytes: {BitConverter.ToString(recorded, 0, Math.Min(50, recorded.Length))}");
                
                // Save for replay
                File.WriteAllBytes(_testFilePath, recorded);
            }
            
            // REPLAY
            _output.WriteLine("\n--- REPLAYING ---");
            using (var fs = File.OpenRead(_testFilePath))
            using (var reader = new BinaryReader(fs))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var playback = new PlaybackSystem();
                
                // NO registration needed! Stream auto-created during injection!
                
                _output.WriteLine("\n1. Applying frame...");
                long startPos = fs.Position;
                playback.ApplyFrame(repo, reader, eventBus);
                long endPos = fs.Position;
                _output.WriteLine($"   Read {endPos - startPos} bytes from stream");
                
                // Try to consume
                _output.WriteLine("\n2. Consuming events...");
                var events = eventBus.Consume<SimpleEvent>();
                _output.WriteLine($"   Consumed {events.Length} events");
                
                if (events.Length > 0)
                {
                    _output.WriteLine($"   Event[0].Value = {events[0].Value}");
                    Assert.Equal(77, events[0].Value);
                    _output.WriteLine("\n✅ SUCCESS!");
                }
                else
                {
                    _output.WriteLine("\n❌ FAIL: No events consumed!");
                    Assert.Fail("Expected 1 event but got 0");
                }
            }
        }
    }
}
