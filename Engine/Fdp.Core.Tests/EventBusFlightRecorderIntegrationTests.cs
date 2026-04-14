using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// End-to-end integration test for Event Bus + Flight Recorder.
    /// Verifies that events can be recorded and replayed correctly using
    /// the integrated RecorderSystem and PlaybackSystem.
    /// </summary>
    public class EventBusFlightRecorderIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testFilePath;
        
        public EventBusFlightRecorderIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _testFilePath = Path.Combine(Path.GetTempPath(), $"integration_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [EventId(201)]
        public struct TestEvent
        {
            public int Value;
            public float Amount;
        }

        [EventId(202)]
        public struct CombatEvent
        {
            public Entity Attacker;
            public Entity Target;
            public int Damage;
        }

        [Fact]
        public void EndToEnd_RecordAndReplay_WithIntegratedSystems()
        {
            // This test uses the actual RecorderSystem and PlaybackSystem
            // to verify complete integration
            
            _output.WriteLine("=== End-to-End Integration Test ===");
            
            const int frameCount = 10;
            
            // ===== RECORDING PHASE =====
            _output.WriteLine("\n--- Recording Phase ---");
            
            using (var fileStream = File.Create(_testFilePath))
            using (var writer = new BinaryWriter(fileStream))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var recorderSystem = new RecorderSystem();
                
                // Write global header
                writer.Write((uint)1); // Version
                writer.Write((ulong)0);  // StartTick
                
                for (int frame = 0; frame < frameCount; frame++)
                {
                    // Publish events this frame
                    eventBus.Publish(new TestEvent 
                    { 
                        Value = frame,
                        Amount = frame * 1.5f
                    });
                    
                    if (frame % 3 == 0)
                    {
                        eventBus.Publish(new CombatEvent
                        {
                            Attacker = new Entity(frame, 1),
                            Target = new Entity(frame + 1, 1),
                            Damage = frame * 10
                        });
                    }
                    
                    // Tick repository
                    repo.Tick();
                    
                    // Record frame WITH events
                    bool isKeyframe = (frame % 5 == 0);
                    if (isKeyframe)
                    {
                        recorderSystem.RecordKeyframe(repo, writer, 0L, eventBus);
                        _output.WriteLine($"Frame {frame}: Keyframe recorded with events");
                    }
                    else
                    {
                        recorderSystem.RecordDeltaFrame(repo, (uint)(repo.GlobalVersion - 1), writer, 0L, eventBus);
                        _output.WriteLine($"Frame {frame}: Delta recorded with events");
                    }
                    
                    // Swap buffers (normal simulation flow)
                    eventBus.SwapBuffers();
                }
            }
            
            _output.WriteLine($"\nRecording complete: {new FileInfo(_testFilePath).Length} bytes");
            
            // ===== PLAYBACK PHASE =====
            _output.WriteLine("\n--- Playback Phase ---");
            
            using (var fileStream = File.OpenRead(_testFilePath))
            using (var reader = new BinaryReader(fileStream))
            using (var replayRepo = new EntityRepository())
            using (var replayBus = new FdpEventBus())
            {
                var playbackSystem = new PlaybackSystem();
                
                // Read global header
                uint version = reader.ReadUInt32();
                ulong startTick = reader.ReadUInt64();
                
                _output.WriteLine($"File version: {version}, Start tick: {startTick}");
                
                int framesReplayed = 0;
                
                while (fileStream.Position < fileStream.Length)
                {
                    // Apply frame WITH events
                    playbackSystem.ApplyFrame(replayRepo, reader, replayBus);
                    
                    // Verify events were injected
                    var testEvents = replayBus.Consume<TestEvent>();
                    Assert.Equal(1, testEvents.Length);
                    Assert.Equal(framesReplayed, testEvents[0].Value);
                    Assert.Equal(framesReplayed * 1.5f, testEvents[0].Amount, precision: 2);
                    
                    // Check combat events (every 3rd frame)
                    var combatEvents = replayBus.Consume<CombatEvent>();
                    if (framesReplayed % 3 == 0)
                    {
                        Assert.Equal(1, combatEvents.Length);
                        Assert.Equal(framesReplayed, combatEvents[0].Attacker.Index);
                        Assert.Equal(framesReplayed * 10, combatEvents[0].Damage);
                        _output.WriteLine($"Frame {framesReplayed}: ✓ TestEvent + CombatEvent");
                    }
                    else
                    {
                        Assert.Equal(0, combatEvents.Length);
                        _output.WriteLine($"Frame {framesReplayed}: ✓ TestEvent only");
                    }
                    
                    framesReplayed++;
                }
                
                Assert.Equal(frameCount, framesReplayed);
                _output.WriteLine($"\n✅ All {framesReplayed} frames replayed successfully!");
            }
        }

        [Fact]
        public void MultipleEventTypes_RecordedAndReplayedCorrectly()
        {
            _output.WriteLine("=== Multiple Event Types Test ===");
            
            const int frameCount = 5;
            
            // Record
            using (var fs = File.Create(_testFilePath))
            using (var writer = new BinaryWriter(fs))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var recorder = new RecorderSystem();
                
                writer.Write((uint)1);
                writer.Write((ulong)0);
                
                for (int frame = 0; frame < frameCount; frame++)
                {
                    // Publish BOTH event types every frame
                    eventBus.Publish(new TestEvent { Value = frame });
                    eventBus.Publish(new CombatEvent 
                    { 
                        Attacker = new Entity(frame, 1) 
                    });
                    
                    repo.Tick();
                    recorder.RecordDeltaFrame(repo, (uint)(repo.GlobalVersion - 1), writer, 0L, eventBus);
                    eventBus.SwapBuffers();
                }
            }
            
            // Replay
            using (var fs = File.OpenRead(_testFilePath))
            using (var reader = new BinaryReader(fs))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var playback = new PlaybackSystem();
                
                reader.ReadUInt32();
                reader.ReadUInt64();
                
                for (int frame = 0; frame < frameCount; frame++)
                {
                    playback.ApplyFrame(repo, reader, eventBus);
                    
                    var testEvents = eventBus.Consume<TestEvent>();
                    var combatEvents = eventBus.Consume<CombatEvent>();
                    
                    Assert.Equal(1, testEvents.Length);
                    Assert.Equal(1, combatEvents.Length);
                    
                    _output.WriteLine($"Frame {frame}: Both event types present ✓");
                }
            }
            
            _output.WriteLine("✅ Multiple event types work correctly");
        }

        [Fact]
        public void NoEventBus_StillRecordsAndReplaysComponents()
        {
            // Verify backward compatibility - without eventBus parameter,
            // system should still work (just without events)
            
            _output.WriteLine("=== Backward Compatibility Test ===");
            
            using (var fs = File.Create(_testFilePath))
            using (var writer = new BinaryWriter(fs))
            using (var repo = new EntityRepository())
            {
                var recorder = new RecorderSystem();
                
                writer.Write((uint)1);
                writer.Write((ulong)0);
                
                // Record WITHOUT eventBus parameter (null by default)
                repo.Tick();
                recorder.RecordDeltaFrame(repo, 0, writer, 0L); // No eventBus!
            }
            
            using (var fs = File.OpenRead(_testFilePath))
            using (var reader = new BinaryReader(fs))
            using (var repo = new EntityRepository())
            {
                var playback = new PlaybackSystem();
                
                reader.ReadUInt32();
                reader.ReadUInt64();
                
                // Replay WITHOUT eventBus parameter
                playback.ApplyFrame(repo, reader); // No eventBus!
                
                _output.WriteLine("✅ Backward compatibility maintained");
            }
        }

        [Fact]
        public void EventBus_ClearsBeforeInjection()
        {
            // Verify that ClearCurrentBuffers is called to prevent mixing
            
            using var eventBus = new FdpEventBus();
            
            // Publish and swap to get events in Current
            eventBus.Publish(new TestEvent { Value = 999 });
            eventBus.SwapBuffers();
            
            // Verify old event is there
            var oldEvents = eventBus.Consume<TestEvent>();
            Assert.Equal(1, oldEvents.Length);
            Assert.Equal(999, oldEvents[0].Value);
            
            // Now simulate playback frame restoration
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Write event data for a new frame
            writer.Write(1); // 1 stream
            writer.Write(EventType<TestEvent>.Id);
            var newEvent = new TestEvent { Value = 42 };
            unsafe
            {
                byte* ptr = (byte*)&newEvent;
                var span = new ReadOnlySpan<byte>(ptr, sizeof(TestEvent));
                writer.Write(span.Length);
                writer.Write(span);
            }
            
            ms.Position = 0;
            using var reader = new BinaryReader(ms);
            
            // Simulate ReadAndInjectEvents
            eventBus.ClearCurrentBuffers();
            
            int streamCount = reader.ReadInt32();
            for (int i = 0; i < streamCount; i++)
            {
                int typeId = reader.ReadInt32();
                int byteCount = reader.ReadInt32();
                byte[] data = reader.ReadBytes(byteCount);
                eventBus.InjectIntoCurrent(typeId, data);
            }
            
            // Should only see NEW event, not old one
            var finalEvents = eventBus.Consume<TestEvent>();
            Assert.Equal(1, finalEvents.Length);
            Assert.Equal(42, finalEvents[0].Value);
            
            _output.WriteLine("✅ ClearCurrentBuffers prevents event mixing");
        }

    [MessagePack.MessagePackObject]
    public class TestManagedEvent
    {
        [MessagePack.Key(0)]
        public string Message { get; set; } = "";
        
        [MessagePack.Key(1)]
        public int Priority { get; set; }
        
        [MessagePack.Key(2)]
        public System.Collections.Generic.List<string> Tags { get; set; } = new();
    }

        [Fact]
        public void ManagedEvents_RecordAndReplay_Correctly()
        {
            _output.WriteLine("=== Managed Events Integration Test ===");
            
            // Record
            using (var fs = File.Create(_testFilePath))
            using (var writer = new BinaryWriter(fs))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var recorder = new RecorderSystem();
                
                writer.Write((uint)1); // Version
                writer.Write((ulong)0); // StartTick
                
                repo.Tick(); // Tick 1
                
                // Publish managed event
                eventBus.PublishManaged(new TestManagedEvent 
                { 
                    Message = "Hello World", 
                    Priority = 1,
                    Tags = new System.Collections.Generic.List<string> { "tag1", "tag2" }
                });
                
                // Record
                recorder.RecordDeltaFrame(repo, 0, writer, 0L, eventBus);
                eventBus.SwapBuffers();
            }
            
            // Replay
            using (var fs = File.OpenRead(_testFilePath))
            using (var reader = new BinaryReader(fs))
            using (var repo = new EntityRepository())
            using (var eventBus = new FdpEventBus())
            {
                var playback = new PlaybackSystem();
                
                // Skip header
                reader.ReadUInt32(); 
                reader.ReadUInt64();
                
                // Apply frame
                playback.ApplyFrame(repo, reader, eventBus);
                
                // Verify
                var events = eventBus.ConsumeManaged<TestManagedEvent>();
                var evt = Assert.Single(events);
                Assert.Equal("Hello World", evt.Message);
                Assert.Equal(1, evt.Priority);
                Assert.Equal(2, evt.Tags.Count);
                Assert.Equal("tag1", evt.Tags[0]);
                
                _output.WriteLine("✅ Managed event recorded and replayed successfully");
            }
        }
    }
}
