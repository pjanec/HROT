using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive test suite for Flight Recorder system.
    /// Tests record/replay correctness, edge cases, and performance characteristics.
    /// </summary>
    public class FlightRecorderTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public FlightRecorderTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"test_recording_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        
        // ================================================
        // BASIC FUNCTIONALITY TESTS
        // ================================================
        
        [Fact]
        public void RecordAndReplay_SingleEntity_RestoresCorrectly()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                bool hasFrame = reader.ReadNextFrame(replayRepo);
                Assert.True(hasFrame);
            }
            
            // Assert
            Assert.Equal(1, replayRepo.EntityCount);
            
            var query = replayRepo.Query().With<Position>().Build();
            bool found = false;
            foreach (var e in query)
            {
                ref readonly var pos = ref replayRepo.GetComponentRO<Position>(e);
                Assert.Equal(10f, pos.X);
                Assert.Equal(20f, pos.Y);
                Assert.Equal(30f, pos.Z);
                found = true;
            }
            Assert.True(found, "Entity with Position component should exist");
        }
        
        [Fact]
        public void RecordAndReplay_MultipleEntities_RestoresAll()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            const int entityCount = 100;
            for (int i = 0; i < entityCount; i++)
            {
                var e = recordRepo.CreateEntity();
                recordRepo.AddComponent(e, new Position { X = i, Y = i * 2, Z = i * 3 });
            }
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.Equal(entityCount, replayRepo.EntityCount);
        }
        
        [Fact]
        public void RecordDelta_OnlyChangedEntities_RecordsCorrectly()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var e1 = recordRepo.CreateEntity();
            var e2 = recordRepo.CreateEntity();
            recordRepo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            recordRepo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            
            // Act - Record keyframe, then modify only e1, then record delta
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
                
                recordRepo.Tick();
                ref var pos = ref recordRepo.GetComponentRW<Position>(e1);
                pos.X = 100;
                
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo); // Keyframe
                reader.ReadNextFrame(replayRepo); // Delta
            }
            
            // Assert - e1 should have updated position
            var query = replayRepo.Query().With<Position>().Build();
            int count = 0;
            foreach (var e in query)
            {
                ref readonly var pos = ref replayRepo.GetComponentRO<Position>(e);
                if (e.Index == e1.Index)
                {
                    Assert.Equal(100f, pos.X);
                }
                count++;
            }
            Assert.Equal(2, count);
        }
        
        // ================================================
        // DESTRUCTION LOGGING TESTS
        // ================================================
        
        [Fact]
        public void DestructionLog_CapturesDestroyedEntities()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            repo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            
            // Act
            repo.DestroyEntity(e1);
            
            // Assert
            var log = repo.GetDestructionLog();
            Assert.Single(log);
            Assert.Equal(e1.Index, log[0].Index);
            Assert.Equal(e1.Generation, log[0].Generation);
            
            // Clear and verify
            repo.ClearDestructionLog();
            Assert.Empty(repo.GetDestructionLog());
        }
        
        [Fact]
        public void RecordAndReplay_EntityDestruction_RemovesEntity()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var e1 = recordRepo.CreateEntity();
            var e2 = recordRepo.CreateEntity();
            recordRepo.AddComponent(e1, new Position { X = 1, Y = 1, Z = 1 });
            recordRepo.AddComponent(e2, new Position { X = 2, Y = 2, Z = 2 });
            
            // Act - Record keyframe, destroy e1, record delta
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
                
                recordRepo.Tick();
                recordRepo.DestroyEntity(e1);
                
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo); // Keyframe - 2 entities
                reader.ReadNextFrame(replayRepo); // Delta - destroy e1
            }
            
            // Assert
            Assert.Equal(1, replayRepo.EntityCount);
            Assert.False(replayRepo.IsAlive(e1));
            Assert.True(replayRepo.IsAlive(e2));
        }
        
        [Fact]
        public void RecordAndReplay_MultipleDestructions_AllRemoved()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var entities = new Entity[10];
            for (int i = 0; i < 10; i++)
            {
                entities[i] = recordRepo.CreateEntity();
                recordRepo.AddComponent(entities[i], new Position { X = i, Y = i, Z = i });
            }
            
            // Act - Record keyframe, destroy half, record delta
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
                
                recordRepo.Tick();
                for (int i = 0; i < 5; i++)
                {
                    recordRepo.DestroyEntity(entities[i]);
                }
                
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.Equal(5, replayRepo.EntityCount);
        }
        
        // ================================================
        // SANITIZATION TESTS
        // ================================================
        
        [Fact]
        public void SanitizeChunk_DeadEntities_ZeroedOut()
        {
            // Arrange
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            var e1 = repo.CreateEntity();
            repo.AddComponent(e1, new Position { X = 123, Y = 456, Z = 789 });
            
            var table = repo.GetComponentTable<Position>();
            var chunkTable = table.GetChunkTable();
            
            // Destroy entity (leaves garbage in memory)
            repo.DestroyEntity(e1);
            
            // Act - Sanitize
            var entityIndex = repo.GetEntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, liveness);
            
            chunkTable.SanitizeChunk(0, liveness);
            
            // Assert - Data should be zeroed
            ref readonly var pos = ref chunkTable.GetRefRO(e1.Index);
            Assert.Equal(0f, pos.X);
            Assert.Equal(0f, pos.Y);
            Assert.Equal(0f, pos.Z);
        }
        
        [Fact]
        public void GetChunkLiveness_MixedStates_ReturnsCorrectMask()
        {
            // Arrange
            using var repo = new EntityRepository();
            var entityIndex = repo.GetEntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            var e1 = repo.CreateEntity(); // Alive
            var e2 = repo.CreateEntity(); // Will be dead
            var e3 = repo.CreateEntity(); // Alive
            
            repo.DestroyEntity(e2);
            
            // Act
            Span<bool> liveness = stackalloc bool[chunkCapacity];
            entityIndex.GetChunkLiveness(0, liveness);
            
            // Assert
            Assert.True(liveness[e1.Index]);
            Assert.False(liveness[e2.Index]);
            Assert.True(liveness[e3.Index]);
            
            // All other slots should be false
            for (int i = e3.Index + 1; i < chunkCapacity; i++)
            {
                Assert.False(liveness[i]);
            }
        }
        
        // ================================================
        // EDGE CASES
        // ================================================
        
        [Fact]
        public void RecordAndReplay_EmptyRepository_HandlesGracefully()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            // Act - Record empty state
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                bool hasFrame = reader.ReadNextFrame(replayRepo);
                Assert.True(hasFrame);
            }
            
            // Assert
            Assert.Equal(0, replayRepo.EntityCount);
        }
        
        [Fact]
        public void RecordAndReplay_EntityCreatedAndDestroyedInSameFrame_HandlesCorrectly()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            // Act - Record keyframe, then create and destroy in same frame
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
                
                recordRepo.Tick();
                var temp = recordRepo.CreateEntity();
                recordRepo.AddComponent(temp, new Position { X = 999, Y = 999, Z = 999 });
                recordRepo.DestroyEntity(temp);
                
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert - Should have no entities
            Assert.Equal(0, replayRepo.EntityCount);
        }
        
        [Fact]
        public void RecordAndReplay_SparseEntityIds_HandlesCorrectly()
        {
            // Arrange - Create entities, destroy some to create gaps
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var entities = new Entity[10];
            for (int i = 0; i < 10; i++)
            {
                entities[i] = recordRepo.CreateEntity();
                recordRepo.AddComponent(entities[i], new Position { X = i, Y = i, Z = i });
            }
            
            // Destroy every other entity to create sparse IDs
            for (int i = 0; i < 10; i += 2)
            {
                recordRepo.DestroyEntity(entities[i]);
            }
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.Equal(5, replayRepo.EntityCount);
            
            // Verify correct entities survived
            for (int i = 1; i < 10; i += 2)
            {
                Assert.True(replayRepo.IsAlive(entities[i]));
            }
        }
        
        [Fact]
        public void RecordAndReplay_MultipleComponents_AllRestored()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            recordRepo.RegisterComponent<Velocity>();
            recordRepo.RegisterComponent<Health>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            recordRepo.AddComponent(entity, new Velocity { X = 1, Y = 2, Z = 3 });
            recordRepo.AddComponent(entity, new Health { Value = 100 });
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            replayRepo.RegisterComponent<Velocity>();
            replayRepo.RegisterComponent<Health>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.True(replayRepo.HasUnmanagedComponent<Position>(entity));
            Assert.True(replayRepo.HasUnmanagedComponent<Velocity>(entity));
            Assert.True(replayRepo.HasUnmanagedComponent<Health>(entity));
            
            ref readonly var pos = ref replayRepo.GetComponentRO<Position>(entity);
            ref readonly var vel = ref replayRepo.GetComponentRO<Velocity>(entity);
            ref readonly var hp = ref replayRepo.GetComponentRO<Health>(entity);
            
            Assert.Equal(10f, pos.X);
            Assert.Equal(1f, vel.X);
            Assert.Equal(100, hp.Value);
        }
        
        [Fact]
        public void RecordAndReplay_ComponentAddedAndRemoved_TracksCorrectly()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            recordRepo.RegisterComponent<Velocity>();
            
            var entity = recordRepo.CreateEntity();
            recordRepo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            // Act - Record keyframe, add velocity, record delta, remove velocity, record delta
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
                
                recordRepo.Tick();
                recordRepo.AddComponent(entity, new Velocity { X = 1, Y = 2, Z = 3 });
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                recordRepo.Tick();
                recordRepo.RemoveUnmanagedComponent<Velocity>(entity);
                recorder.CaptureFrame(recordRepo, recordRepo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Act - Replay all frames
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            replayRepo.RegisterComponent<Velocity>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo); // Frame 0: Position only
                Assert.True(replayRepo.HasUnmanagedComponent<Position>(entity));
                Assert.False(replayRepo.HasUnmanagedComponent<Velocity>(entity));
                
                reader.ReadNextFrame(replayRepo); // Frame 1: Position + Velocity
                Assert.True(replayRepo.HasUnmanagedComponent<Position>(entity));
                Assert.True(replayRepo.HasUnmanagedComponent<Velocity>(entity));
                
                reader.ReadNextFrame(replayRepo); // Frame 2: Position only
                Assert.True(replayRepo.HasUnmanagedComponent<Position>(entity));
                Assert.False(replayRepo.HasUnmanagedComponent<Velocity>(entity));
            }
        }
        
        // ================================================
        // CHUNK BOUNDARY TESTS
        // ================================================
        
        [Fact]
        public void RecordAndReplay_EntitiesSpanningMultipleChunks_AllRestored()
        {
            // Arrange - Create enough entities to span multiple chunks
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var entityIndex = recordRepo.GetEntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            int entityCount = chunkCapacity * 2 + 10; // Span 3 chunks
            
            for (int i = 0; i < entityCount; i++)
            {
                var e = recordRepo.CreateEntity();
                recordRepo.AddComponent(e, new Position { X = i, Y = i * 2, Z = i * 3 });
            }
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.Equal(entityCount, replayRepo.EntityCount);
        }
        
        [Fact]
        public void RecordAndReplay_OnlyLastChunkPopulated_HandlesCorrectly()
        {
            // Arrange - Create entities only in a high chunk index
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            var entityIndex = recordRepo.GetEntityIndex();
            int chunkCapacity = entityIndex.GetChunkCapacity();
            
            // Create and destroy entities to advance the max index
            for (int i = 0; i < chunkCapacity * 5; i++)
            {
                var temp = recordRepo.CreateEntity();
                recordRepo.DestroyEntity(temp);
            }
            
            // Now create actual entities
            var e1 = recordRepo.CreateEntity();
            var e2 = recordRepo.CreateEntity();
            recordRepo.AddComponent(e1, new Position { X = 100, Y = 200, Z = 300 });
            recordRepo.AddComponent(e2, new Position { X = 400, Y = 500, Z = 600 });
            
            // Act - Record
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act - Replay
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            // Assert
            Assert.Equal(2, replayRepo.EntityCount);
        }
        
        // ================================================
        // FILE FORMAT TESTS
        // ================================================
        
        [Fact]
        public void RecordingReader_ValidFile_ReadsHeaderCorrectly()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act
            using var reader = new RecordingReader(_testFilePath);
            
            // Assert
            Assert.Equal(FdpConfig.FORMAT_VERSION, reader.FormatVersion);
            Assert.True(reader.RecordingTimestamp > 0);
        }
        
        [Fact]
        public void RecordingReader_InvalidMagic_ThrowsException()
        {
            // Arrange - Create file with invalid magic
            using (var fs = new FileStream(_testFilePath, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("BADMAG"));
                writer.Write((uint)1);
                writer.Write((long)0);
            }
            
            // Act & Assert
            Assert.Throws<InvalidDataException>(() => new RecordingReader(_testFilePath));
        }
        
        [Fact]
        public void RecordingReader_WrongVersion_ThrowsException()
        {
            // Arrange - Create file with wrong version
            using (var fs = new FileStream(_testFilePath, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("FDPREC"));
                writer.Write((uint)999); // Wrong version
                writer.Write((long)0);
            }
            
            // Act & Assert
            Assert.Throws<InvalidDataException>(() => new RecordingReader(_testFilePath));
        }
        
        [Fact]
        public void RecordingReader_EndOfFile_ReturnsFalse()
        {
            // Arrange
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Act
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            bool frame1 = reader.ReadNextFrame(replayRepo);
            bool frame2 = reader.ReadNextFrame(replayRepo);
            
            // Assert
            Assert.True(frame1);
            Assert.False(frame2); // No more frames
        }
        
        // ================================================
        // PERFORMANCE TESTS
        // ================================================
        
        [Fact]
        public void AsyncRecorder_FrameDropTracking_CountsCorrectly()
        {
            // This test would need to simulate a slow disk to trigger drops
            // For now, just verify the counter exists and starts at 0
            using var recorder = new AsyncRecorder(_testFilePath);
            Assert.Equal(0, recorder.DroppedFrames);
            Assert.Equal(0, recorder.RecordedFrames);
        }
        
        [Fact]
        public void RecordKeyframe_LargeEntityCount_CompletesSuccessfully()
        {
            // Arrange - Create many entities
            using var recordRepo = new EntityRepository();
            recordRepo.RegisterComponent<Position>();
            recordRepo.RegisterComponent<Velocity>();
            
            const int entityCount = 1000;
            for (int i = 0; i < entityCount; i++)
            {
                var e = recordRepo.CreateEntity();
                recordRepo.AddComponent(e, new Position { X = i, Y = i, Z = i });
                recordRepo.AddComponent(e, new Velocity { X = 1, Y = 1, Z = 1 });
            }
            
            // Act
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                recordRepo.Tick();
                recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
            }
            
            // Assert - Replay and verify
            using var replayRepo = new EntityRepository();
            replayRepo.RegisterComponent<Position>();
            replayRepo.RegisterComponent<Velocity>();
            
            using (var reader = new RecordingReader(_testFilePath))
            {
                reader.ReadNextFrame(replayRepo);
            }
            
            Assert.Equal(entityCount, replayRepo.EntityCount);
        }
        [DataPolicy(DataPolicy.Transient)]
        [ComponentId(249)]
        struct TransientPosAttr { public float X; }

        [ComponentId(250)]
        struct TransientPosExplicit { public float X; }

        [ComponentId(251)]
        struct PersistentPos { public float X; }

        [Fact]
        public void RecordFrame_ExcludesTransient_ViaAttribute()
        {
             using var recordRepo = new EntityRepository();
             recordRepo.RegisterComponent<TransientPosAttr>();
             recordRepo.RegisterComponent<PersistentPos>();
             
             // Verify Attribute Detection Logic matches assumption
             Assert.True(typeof(TransientPosAttr).IsDefined(typeof(DataPolicyAttribute), false), "Attribute Check Failed");
             var id = ComponentType<TransientPosAttr>.ID;
             // Ensure it is false BEFORE recording
             Assert.False(ComponentTypeRegistry.IsRecordable(id), "Recordable Flag Failed");
             
             var e = recordRepo.CreateEntity();
             recordRepo.AddComponent(e, new TransientPosAttr { X=1 });
             recordRepo.AddComponent(e, new PersistentPos { X=2 });
             recordRepo.Tick();
             
             using (var recorder = new AsyncRecorder(_testFilePath))
             {
                 recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
             }
             
             using var replayRepo = new EntityRepository();
             replayRepo.RegisterComponent<TransientPosAttr>();
             replayRepo.RegisterComponent<PersistentPos>();
             
             using (var reader = new RecordingReader(_testFilePath))
             {
                 reader.ReadNextFrame(replayRepo);
             }
             
             var replayE = new Entity(e.Index, e.Generation);
             Assert.True(replayRepo.HasUnmanagedComponent<PersistentPos>(replayE));
             Assert.False(replayRepo.HasUnmanagedComponent<TransientPosAttr>(replayE));
        }

        [Fact]
        public void RecordFrame_ExcludesTransient_ViaExplicit()
        {
             using var recordRepo = new EntityRepository();
             recordRepo.RegisterComponent<TransientPosExplicit>(DataPolicy.Transient);
             recordRepo.RegisterComponent<PersistentPos>();
             
             var e = recordRepo.CreateEntity();
             recordRepo.AddComponent(e, new TransientPosExplicit { X=1 });
             recordRepo.AddComponent(e, new PersistentPos { X=2 });
             recordRepo.Tick();
             
             using (var recorder = new AsyncRecorder(_testFilePath))
             {
                 recorder.CaptureKeyframe(recordRepo, DateTime.UtcNow.Ticks);
             }
             
             using var replayRepo = new EntityRepository();
             replayRepo.RegisterComponent<TransientPosExplicit>();
             replayRepo.RegisterComponent<PersistentPos>();
             
             using (var reader = new RecordingReader(_testFilePath))
             {
                 reader.ReadNextFrame(replayRepo);
             }
             
             var replayE = new Entity(e.Index, e.Generation);
             Assert.True(replayRepo.HasUnmanagedComponent<PersistentPos>(replayE));
             Assert.False(replayRepo.HasUnmanagedComponent<TransientPosExplicit>(replayE));
        }
    }
}
