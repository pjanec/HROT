using System;
using System.Linq;
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
        
        // ⭐ CE-3335 — ONE destruction log, a mark per reader, trimmed by Tick() (docs/DESIGN_Replay_Time_Axis_And_Review.md §12o)

        [Fact]
        public void CE3335_TwoReaders_EachSeeEveryDeath_AndNeitherEmptiesTheLog()
        {
            using var repo = new EntityRepository();
            var e1 = repo.CreateEntity();
            long a = repo.DestructionMark, b = repo.DestructionMark;

            repo.DestroyEntity(e1);

            Assert.Equal(new[] { e1 }, repo.ReadDestructions(ref a).ToArray());
            Assert.Equal(new[] { e1 }, repo.ReadDestructions(ref b).ToArray());   // A's read did not take it from B
            Assert.Equal(0, repo.ReadDestructions(ref a).Length);                  // and A sees each death once
        }

        [Fact]
        public void CE3335_ADeathAfterAReaderInItsFrame_ReachesItTheNextFrame()
        {
            using var repo = new EntityRepository();
            var e1 = repo.CreateEntity();
            long mark = repo.DestructionMark;

            repo.Tick();
            Assert.Equal(0, repo.ReadDestructions(ref mark).Length);   // the reader ran (mid-frame) …
            repo.DestroyEntity(e1);                                     // … and the death comes after it
            repo.Tick();                                                // the next frame starts

            Assert.Equal(new[] { e1 }, repo.ReadDestructions(ref mark).ToArray());
        }

        [Fact]
        public void CE3335_TheLogStaysBounded_WhenNothingRecords()
        {
            using var repo = new EntityRepository();
            for (int frame = 0; frame < 1000; frame++)
            {
                repo.Tick();
                repo.DestroyEntity(repo.CreateEntity());
            }
            Assert.True(repo.GetDestructionLog().Count <= 2, $"the log held {repo.GetDestructionLog().Count} deaths after 1000 frames");
        }

        [Fact]
        public void CE3335_TheRecorderWritesEachDeathOnce_AndLeavesItForTheOtherReaders()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var recorder = new RecorderSystem();
            long breakpoints = repo.DestructionMark;

            repo.Tick();
            repo.DestroyEntity(e1);
            Assert.Equal(1, DestroyedCount(recorder, repo));
            Assert.Equal(new[] { e1 }, repo.ReadDestructions(ref breakpoints).ToArray());   // a reader AFTER the recorder still sees it
            repo.DestroyEntity(e2);                                                         // a death after the recorder, same frame

            repo.Tick();
            Assert.Equal(1, DestroyedCount(recorder, repo));   // e2 in the next frame; e1 is not written twice
        }

        /// <summary>⭐ CE-3332 — the frame index's Changed flag means "a recorded byte moved": a system that WRITES a component with the
        /// value it already had (measured on a paused editor frame: SimTransform and four presentation tables) does not mark the frame.</summary>
        [Fact]
        public void CE3332_A_write_of_the_same_value_does_not_mark_the_frame_changed()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1, Y = 2, Z = 3 });
            var recorder = new RecorderSystem();
            using (var w = new BinaryWriter(new MemoryStream())) recorder.RecordKeyframe(repo, w, 0L);

            Delta(() => repo.SetComponent(e, new Position { X = 1, Y = 2, Z = 3 }));
            Assert.True(recorder.LastFrameChanged);    // the first delta after a keyframe has nothing to compare with

            Delta(() => repo.SetComponent(e, new Position { X = 1, Y = 2, Z = 3 }));
            Assert.False(recorder.LastFrameChanged);   // written again, the same bytes: a quiet frame

            Delta(() => repo.SetComponent(e, new Position { X = 9, Y = 2, Z = 3 }));
            Assert.True(recorder.LastFrameChanged);    // a real change

            void Delta(Action write)
            {
                uint before = repo.GlobalVersion;
                repo.Tick();
                write();
                using var w = new BinaryWriter(new MemoryStream());
                recorder.RecordDeltaFrame(repo, before, w, 0);
            }
        }

        /// <summary>⭐ CE-4050 — the recording names the assembly each recorded type lives in, and a replaying process loads the named ones it
        /// has not loaded yet (assemblies load lazily); a name this install does not carry is reported, not thrown.</summary>
        [Fact]
        public void CE4050_The_manifest_names_each_types_assembly_and_a_replay_loads_them()
        {
            using (var repo = new EntityRepository())
            {
                repo.RegisterComponent<Position>();
                using var recorder = new AsyncRecorder(_testFilePath);
                recorder.CaptureKeyframe(repo, 1_000_000L, blocking: true);
            }
            var meta = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(File.ReadAllText(_testFilePath + ".meta.json"));
            var position = meta.SchemaManifest!.Values.Single(i => i.Name == typeof(Position).FullName);
            Assert.Equal(typeof(Position).Assembly.GetName().Name, position.Assembly);

            Assert.Empty(RecordedAssemblies.EnsureLoaded(meta));   // everything named is loadable here
            meta.SchemaManifest[987_654] = new ComponentSchemaInfo { Name = "Gone.Type", Assembly = "Not.An.Assembly.CE4050" };
            Assert.Equal(new[] { "Not.An.Assembly.CE4050" }, RecordedAssemblies.EnsureLoaded(meta));   // said, not thrown
        }

        /// <summary>⭐ CE-4050 — a recorded type no loaded assembly declares no longer STOPS playback: its data is skipped and named.</summary>
        [Fact]
        public void CE4050_A_type_nobody_declares_is_skipped_not_thrown()
        {
            using var repo = new EntityRepository();
            var playback = new PlaybackSystem();
            var apply = typeof(PlaybackSystem).GetMethod("ApplyChunkData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            apply.Invoke(playback, new object[] { repo, 987_655, 0, new byte[16] });
            Assert.Contains(987_655, playback.UnresolvedTypes);
        }

        private static int DestroyedCount(RecorderSystem recorder, EntityRepository repo)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            recorder.RecordDeltaFrame(repo, repo.GlobalVersion - 1, writer, 0);
            writer.Flush();
            return BitConverter.ToInt32(stream.ToArray(), sizeof(ulong) + sizeof(byte) + sizeof(long));   // tick · type · wall · destroyed count
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
