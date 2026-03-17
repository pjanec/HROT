using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Integration tests for Flight Recorder components working together.
    /// Tests end-to-end scenarios combining RecorderSystem, AsyncRecorder, PlaybackSystem, and RecordingReader.
    /// </summary>
    public class FlightRecorderIntegrationTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public FlightRecorderIntegrationTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        #region Simple Integration Tests (Minimal Components)
        
        [Fact]
        public void SimpleRecordPlayback_SingleFrame_PreservesData()
        {
            // Test the most basic record Ä‚ËĂ˘â‚¬Â Ă˘â‚¬â„˘ playback cycle
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            // Create initial state
            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 42 });
            var e2 = sourceRepo.CreateEntity(); 
            sourceRepo.AddComponent(e2, new IntComponent { Value = 100 });
            sourceRepo.Tick(); // V=2
            
            // Record keyframe
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback to fresh repo
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            bool frameLoaded = reader.ReadNextFrame(targetRepo);
            
            // Verify
            Assert.True(frameLoaded);
            Assert.Equal(2, targetRepo.GetEntityIndex().ActiveCount);
            Assert.Equal(42, targetRepo.GetComponentRO<IntComponent>(e1).Value);
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(e2).Value);
        }
        
        [Fact]
        public void SimpleRecordPlayback_DeltaFrame_PreservesChanges()
        {
            // Test keyframe + delta record Ä‚ËĂ˘â‚¬Â Ă˘â‚¬â„˘ playback cycle
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 42 });
            sourceRepo.Tick();
            
            // Record keyframe + delta
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Make changes
                sourceRepo.Tick();
                sourceRepo.SetUnmanagedComponent(e1, new IntComponent { Value = 200 });
                
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback sequence
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 1 (keyframe)
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(42, targetRepo.GetComponentRO<IntComponent>(e1).Value);
            
            // Frame 2 (delta)
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(e1).Value);
        }
        
        [Fact]
        public void SimpleRecordPlayback_EntityDestruction_ReflectedInPlayback()
        {
            // Test entity destruction recording and playback
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            var e1 = sourceRepo.CreateEntity();
            var e2 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 10 });
            sourceRepo.AddComponent(e2, new IntComponent { Value = 20 });
            sourceRepo.Tick();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Keyframe with 2 entities
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Destroy one entity
                sourceRepo.Tick();
                sourceRepo.DestroyEntity(e2);
                
                // Record delta
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 1: Both entities exist
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(2, targetRepo.GetEntityIndex().ActiveCount);
            Assert.True(targetRepo.IsAlive(e1));
            Assert.True(targetRepo.IsAlive(e2));
            
            // Frame 2: e2 destroyed
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(1, targetRepo.GetEntityIndex().ActiveCount);
            Assert.True(targetRepo.IsAlive(e1));
            Assert.False(targetRepo.IsAlive(e2));
        }

        #endregion

        #region Medium Complexity Integration Tests
        
        [Fact]
        public void MultiFrameSequence_MixedOperations_MaintainsConsistency()
        {
            // Test a longer sequence with create, modify, destroy operations
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            sourceRepo.RegisterComponent<FloatComponent>();
            
            // Declare entities at method scope for use in both recording and playback
            Entity e1, e2, e3;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 1: Initial entities
                e1 = sourceRepo.CreateEntity();
                e2 = sourceRepo.CreateEntity();
                sourceRepo.AddComponent(e1, new IntComponent { Value = 100 });
                sourceRepo.AddComponent(e2, new IntComponent { Value = 200 });
                sourceRepo.Tick();
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 2: Add component to e1
                sourceRepo.Tick();
                sourceRepo.AddComponent(e1, new FloatComponent { Value = 1.5f });
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 3: Create new entity
                sourceRepo.Tick(); 
                e3 = sourceRepo.CreateEntity();
                sourceRepo.AddComponent(e3, new IntComponent { Value = 300 });
                sourceRepo.AddComponent(e3, new FloatComponent { Value = 3.14f });
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 4: Modify and destroy
                sourceRepo.Tick();
                sourceRepo.SetUnmanagedComponent(e1, new IntComponent { Value = 150 });
                sourceRepo.DestroyEntity(e2);
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback and verify each frame
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            targetRepo.RegisterComponent<FloatComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 1: Initial state
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(2, targetRepo.GetEntityIndex().ActiveCount);
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(e1).Value);
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(e2).Value);
            Assert.False(targetRepo.HasUnmanagedComponent<FloatComponent>(e1));
            
            // Frame 2: e1 gets float component
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(2, targetRepo.GetEntityIndex().ActiveCount);
            Assert.True(targetRepo.HasUnmanagedComponent<FloatComponent>(e1));
            Assert.Equal(1.5f, targetRepo.GetComponentRO<FloatComponent>(e1).Value);
            
            // Frame 3: e3 created
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(3, targetRepo.GetEntityIndex().ActiveCount);
            Assert.True(targetRepo.IsAlive(e3));
            Assert.Equal(300, targetRepo.GetComponentRO<IntComponent>(e3).Value);
            Assert.Equal(3.14f, targetRepo.GetComponentRO<FloatComponent>(e3).Value);
            
            // Frame 4: e1 modified, e2 destroyed
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(2, targetRepo.GetEntityIndex().ActiveCount);
            Assert.Equal(150, targetRepo.GetComponentRO<IntComponent>(e1).Value);
            Assert.False(targetRepo.IsAlive(e2));
            Assert.True(targetRepo.IsAlive(e1));
            Assert.True(targetRepo.IsAlive(e3));
        }
        
        [Fact]
        public void IndexRepairIntegration_ImplicitEntityCreation_WorksEndToEnd()
        {
            // Test that PlaybackSystem properly handles implicit entity creation
            // when component data is received without explicit entity headers
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            // Create entities with gaps (to test sparse scenarios)
            var entities = new Entity[10];
            for (int i = 0; i < 10; i += 2) // 0, 2, 4, 6, 8
            {
                entities[i] = sourceRepo.CreateEntity();
                sourceRepo.AddComponent(entities[i], new IntComponent { Value = i * 10 });
            }
            sourceRepo.Tick();
            
            // Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback to empty repo
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            Assert.True(reader.ReadNextFrame(targetRepo));
            
            // Verify sparse entities are correctly restored
            Assert.Equal(5, targetRepo.GetEntityIndex().ActiveCount);
            for (int i = 0; i < 10; i += 2)
            {
                Assert.True(targetRepo.IsAlive(entities[i]));
                Assert.Equal(i * 10, targetRepo.GetComponentRO<IntComponent>(entities[i]).Value);
            }
            
            // Verify free list works (odd indices should be available)
            var newEntity = targetRepo.CreateEntity();
            Assert.True(newEntity.Index % 2 == 1, "New entity should use free slot from odd indices");
        }

        #endregion

        #region Error Handling Integration Tests
        
        [Fact]
        public void RecorderError_PlaybackAttempt_HandlesGracefully()
        {
            // Test what happens when playback tries to read corrupted/incomplete data
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 42 });
            sourceRepo.Tick();
            
            // Record partial frame then corrupt file
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Corrupt the file by truncating it
            var originalBytes = File.ReadAllBytes(_testFilePath);
            File.WriteAllBytes(_testFilePath, originalBytes.AsSpan(0, originalBytes.Length / 2).ToArray());
            
            // Attempt playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Should handle corruption gracefully by returning false (not throwing exception)
            bool frameLoaded = reader.ReadNextFrame(targetRepo);
            
            Assert.False(frameLoaded, "Should return false when file is corrupted/truncated");
        }

        #endregion

        #region Performance Integration Tests
        
        [Fact]
        public void HighVolumeRecordPlayback_MaintainsPerformance()
        {
            // Test recording and playback with many entities and frames
            const int entityCount = 1000;
            const int frameCount = 10;
            
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            // Create many entities
            var entities = new Entity[entityCount];
            for (int i = 0; i < entityCount; i++)
            {
                entities[i] = sourceRepo.CreateEntity();
                sourceRepo.AddComponent(entities[i], new IntComponent { Value = i });
            }
            sourceRepo.Tick();
            
            var startTime = DateTime.UtcNow;
            
            // Record many frames
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
                
                for (int frame = 1; frame < frameCount; frame++)
                {
                    sourceRepo.Tick();
                    
                    // Modify some entities
                    for (int i = 0; i < entityCount; i += 10)
                    {
                        sourceRepo.SetUnmanagedComponent(entities[i], new IntComponent { Value = entities[i].Index * frame });
                    }
                    
                    recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            var recordTime = DateTime.UtcNow - startTime;
            Assert.True(recordTime.TotalSeconds < 5.0, $"Recording took too long: {recordTime.TotalSeconds}s");
            
            // Verify playback performance
            startTime = DateTime.UtcNow;
            
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            int framesRead = 0;
            while (reader.ReadNextFrame(targetRepo))
            {
                framesRead++;
                if (framesRead >= frameCount) break; // Safety limit
            }
            
            var playbackTime = DateTime.UtcNow - startTime;
            Assert.True(playbackTime.TotalSeconds < 2.0, $"Playback took too long: {playbackTime.TotalSeconds}s");
            Assert.Equal(frameCount, framesRead);
            Assert.Equal(entityCount, targetRepo.GetEntityIndex().ActiveCount);
        }

        #endregion

        #region Cross-Component Integration Tests
        
        [Fact]
        public void ManagedComponentIntegration_RecordPlayback_PreservesComplexData()
        {
            // Test with both unmanaged and managed components
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            sourceRepo.RegisterComponent<TestManagedComponent>();
            
            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 42 });
            sourceRepo.AddManagedComponent(e1, new TestManagedComponent { Value = "Hello", Count = 123 });
            sourceRepo.Tick();
            
            // Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Modify managed component
                sourceRepo.Tick();
                var managed = sourceRepo.GetComponentRW<TestManagedComponent>(e1);
                managed.Value = "Modified";
                managed.Count = 456;
                
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            targetRepo.RegisterComponent<TestManagedComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 1: Initial values
            Assert.True(reader.ReadNextFrame(targetRepo));
            Assert.Equal(42, targetRepo.GetComponentRO<IntComponent>(e1).Value);
            var managedComp1 = targetRepo.GetComponentRO<TestManagedComponent>(e1);
            Assert.Equal("Hello", managedComp1.Value);
            Assert.Equal(123, managedComp1.Count);
            
            // Frame 2: Modified values  
            Assert.True(reader.ReadNextFrame(targetRepo));
            var managedComp2 = targetRepo.GetComponentRO<TestManagedComponent>(e1);
            Assert.Equal("Modified", managedComp2.Value);
            Assert.Equal(456, managedComp2.Count);
        }

        [Fact]
        public void ManagedEventSkipping_SkipsDataCorrectly()
        {
            // Verify that we can correctly skip over managed events (using the new BlockSize feature)
            // and successfully read the subsequent data in the stream.
            
            using var sourceRepo = new EntityRepository();
            sourceRepo.RegisterComponent<IntComponent>();
            
            var e1 = sourceRepo.CreateEntity();
            sourceRepo.AddComponent(e1, new IntComponent { Value = 42 });
            
            // Generate some events
            var bus = new FdpEventBus();
            
            // Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                sourceRepo.Tick();
                // Inject managed events
                bus.PublishManaged(new TestManagedComponent { Value = "Event1", Count = 1 });
                bus.PublishManaged(new TestManagedComponent { Value = "Event2", Count = 2 });
                
                // Frame 0: Has Managed Events
                recorder.CaptureKeyframe(sourceRepo, DateTime.UtcNow.Ticks, blocking: true, eventBus: bus);
                
                // Frame 1: Simple delta to verify we landed correctly
                sourceRepo.Tick();
                sourceRepo.SetUnmanagedComponent(e1, new IntComponent { Value = 100 });
                recorder.CaptureFrame(sourceRepo, sourceRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true, eventBus: bus);
            }
            
            // Playback with skipping
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            // Note: We intentionally DON'T register TestManagedComponent if we want to test "unknown type" scenario,
            // but for "processEvents=false" it shouldn't matter if we know the type or not.
            

            
            // Access private _playback field or use a public method that exposes 'processEvents'
            // The RecordingReader.ReadNextFrame calls ApplyFrame(..., processEvents: true) by default.
            // We need to bypass Reader wrapper to test specific PlaybackSystem flag, 
            // OR we rely on the fact that Seeking logic (not yet exposed in Reader) would use this.
            
            // Since RecordingReader doesn't expose 'processEvents', we'll reconstruct the flow manually with PlaybackSystem
            // to verify the skipping logic specifically.
            
            using (var fs = new FileStream(_testFilePath, FileMode.Open))
            using (var binaryReader = new BinaryReader(fs))
            {
                // Skip Global Header
                fs.Position = 18; // Magic(6) + Version(4) + Timestamp(8)
                
                var playback = new PlaybackSystem();
                
                // --- READ FRAME 0 (Skip Events) ---
                // Manually read frame wrapper like RecordingReader does
                int f0CompSize = binaryReader.ReadInt32();
                int f0UncompSize = binaryReader.ReadInt32();
                fs.Position += 17; // Skip Tick/Type/WallClockTicks in outer header
                
                byte[] f0Data = binaryReader.ReadBytes(f0CompSize);
                byte[] f0Raw = new byte[f0UncompSize];
                K4os.Compression.LZ4.LZ4Codec.Decode(f0Data, 0, f0Data.Length, f0Raw, 0, f0UncompSize);
                
                using (var ms0 = new MemoryStream(f0Raw))
                using (var br0 = new BinaryReader(ms0))
                {
                    // Call ApplyFrame with processEvents = false
                    playback.ApplyFrame(targetRepo, br0, eventBus: null, processEvents: false);
                    
                    // Verify: If skipping worked, br0 position should be at end of stream or valid end of data
                    // Actually, ApplyFrame consumes the whole inner stream.
                    // The real test is: Did it crash? And did it verify correct pointer math?
                }
                
                // --- READ FRAME 1 (Verify Sync) ---
                // If we messed up skipping in Frame 0 (which was inside the compressed payload),
                // it wouldn't affect the FileStream position for Frame 1, because Frame 0 was fully read into 'f0Data'.
                //
                // WAIT! The skipping happens inside the *decompressed* stream (MemoryStream).
                // If skipping is broken, 'br0' would be at the wrong position, causing the REST of Frame 0 to be read incorrectly.
                // Frame 0 contains: [Events] [Singletons] [Chunks] [IndexRepair]
                // If we skip events incorrectly, Singletons/Chunks will be read as garbage.
                
                // So checking basic entity state after Frame 0 is sufficient proof!
                Assert.Equal(1, targetRepo.GetEntityIndex().ActiveCount);
                Assert.Equal(42, targetRepo.GetComponentRO<IntComponent>(e1).Value);
                
                // Verify Frame 1 works too just in case
                int f1CompSize = binaryReader.ReadInt32();
                Assert.True(f1CompSize > 0);
            }
        }
        #endregion
    }
    
    // Test component for managed component integration tests
    [ComponentId(247)]
    [MessagePack.MessagePackObject]
    public record TestManagedComponent
    {
        [MessagePack.Key(0)]
        public string Value { get; set; } = string.Empty;
        
        [MessagePack.Key(1)] 
        public int Count { get; set; }
    }
}
