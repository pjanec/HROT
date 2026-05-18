using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for realistic dynamic entity lifecycle scenarios in Flight Recorder.
    /// Covers creation, deletion, slot reuse, generation tracking, and complex playback.
    /// Simulates real-world game/simulation patterns.
    /// </summary>
    public class DynamicEntityLifecycleTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public DynamicEntityLifecycleTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"dynamic_entities_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [ComponentId(207)]
        public struct Health { public int Value; }
        
        [MessagePackObject]
        [ComponentId(208)]
        public record UnitData
        {
            [Key(0)]
            public string UnitType { get; set; } = string.Empty;
            
            [Key(1)]
            public int Level { get; set; }
        }

        [Fact]
        public void DynamicEntities_CreateAndDestroy_TrackedCorrectly()
        {
            // Simulate a wave-based game: spawn enemies, kill them, spawn more
            using var repo = new EntityRepository();
            repo.RegisterComponent<Health>();
            
            var spawnedEntities = new List<(int frame, Entity entity, int health)>();
            var destroyedEntities = new List<(int frame, Entity entity)>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Initial keyframe
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frames 1-5: Spawn wave 1 (5 enemies)
                for (int frame = 1; frame <= 5; frame++)
                {
                    repo.Tick();
                    var enemy = repo.CreateEntity();
                    repo.AddComponent(enemy, new Health { Value = frame * 10 });
                    spawnedEntities.Add((frame, enemy, frame * 10));
                    
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
                
                // Frame 6: Kill first 3 enemies
                repo.Tick();
                for (int i = 0; i < 3; i++)
                {
                    repo.DestroyEntity(spawnedEntities[i].entity);
                    destroyedEntities.Add((6, spawnedEntities[i].entity));
                }
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frames 7-9: Spawn wave 2 (reusing slots from first 3)
                for (int frame = 7; frame <= 9; frame++)
                {
                    repo.Tick();
                    var enemy = repo.CreateEntity();
                    repo.AddComponent(enemy, new Health { Value = frame * 20 });
                    spawnedEntities.Add((frame, enemy, frame * 20));
                    
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
            }
            
            // Playback: Verify entity lifecycle
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Health>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 0: Empty
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(0, targetRepo.EntityCount);
            
            // Frames 1-5: Wave 1 spawns
            for (int frame = 1; frame <= 5; frame++)
            {
                reader.ReadNextFrame(targetRepo);
                Assert.Equal(frame, targetRepo.EntityCount);
            }
            
            // Frame 6: 3 enemies killed
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(2, targetRepo.EntityCount);
            
            // Frames 7-9: Wave 2 spawns (using recycled slots)
            for (int frame = 7; frame <= 9; frame++)
            {
                reader.ReadNextFrame(targetRepo);
                int expectedCount = 2 + (frame - 6); // 2 survivors + new spawns
                Assert.Equal(expectedCount, targetRepo.EntityCount);
            }
            
            // Final state: Should have 5 entities (2 from wave 1, 3 from wave 2)
            Assert.Equal(5, targetRepo.EntityCount);
        }

        [Fact]
        public void EntitySlotReuse_DifferentGenerations_PreservedDuringPlayback()
        {
            // Test that entity generations are correctly tracked when slots are reused
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            Entity firstEntity, secondEntity;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frame 1: Create entity at slot 0
                repo.Tick();
                firstEntity = repo.CreateEntity();
                repo.AddComponent(firstEntity, new IntComponent { Value = 100 });
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 2: Destroy entity
                repo.Tick();
                repo.DestroyEntity(firstEntity);
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 3: Create new entity (should reuse slot 0 with higher generation)
                repo.Tick();
                secondEntity = repo.CreateEntity();
                repo.AddComponent(secondEntity, new IntComponent { Value = 200 });
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Verify they share the same index but different generations
            Assert.Equal(firstEntity.Index, secondEntity.Index);
            Assert.True(secondEntity.Generation > firstEntity.Generation,
                $"Second entity gen ({secondEntity.Generation}) should be > first ({firstEntity.Generation})");
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 0: Empty
            reader.ReadNextFrame(targetRepo);
            
            // Frame 1: First entity exists
            reader.ReadNextFrame(targetRepo);
            Assert.True(targetRepo.IsAlive(firstEntity));
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(firstEntity).Value);
            
            // Frame 2: First entity destroyed
            reader.ReadNextFrame(targetRepo);
            Assert.False(targetRepo.IsAlive(firstEntity));
            
            // Frame 3: Second entity (different generation, same slot)
            reader.ReadNextFrame(targetRepo);
            Assert.False(targetRepo.IsAlive(firstEntity)); // Old gen should still be dead
            Assert.True(targetRepo.IsAlive(secondEntity)); // New gen should be alive
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(secondEntity).Value);
        }

        [Fact]
        public void ComplexLifecycle_MultipleWaves_WithSeek()
        {
            // Realistic scenario: Spawn 3 waves, destroy some, with keyframes
            using var repo = new EntityRepository();
            repo.RegisterComponent<Health>();
            repo.RegisterComponent<UnitData>();
            
            var wave1 = new List<Entity>();
            var wave2 = new List<Entity>();
            var wave3 = new List<Entity>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Keyframe
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frames 1-10: Wave 1 (10 units)
                for (int i = 1; i <= 10; i++)
                {
                    repo.Tick();
                    var unit = repo.CreateEntity();
                    repo.AddComponent(unit, new Health { Value = 100 });
                    repo.AddManagedComponent(unit, new UnitData { UnitType = "Infantry", Level = 1 });
                    wave1.Add(unit);
                    
                    if (i % 5 == 0) // Keyframes at 5, 10
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
                
                // Frames 11-15: Destroy half of wave 1
                for (int i = 11; i <= 15; i++)
                {
                    repo.Tick();
                    if (i <= 15 && (i - 11) < wave1.Count / 2)
                    {
                        repo.DestroyEntity(wave1[i - 11]);
                    }
                    
                    if (i % 5 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
                
                // Frames 16-25: Wave 2 (10 cavalry units, reusing some slots)
                for (int i = 16; i <= 25; i++)
                {
                    repo.Tick();
                    var unit = repo.CreateEntity();
                    repo.AddComponent(unit, new Health { Value = 80 });
                    repo.AddManagedComponent(unit, new UnitData { UnitType = "Cavalry", Level = 2 });
                    wave2.Add(unit);
                    
                    if (i % 5 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
                
                // Frames 26-30: Damage some units
                for (int i = 26; i <= 30; i++)
                {
                    repo.Tick();
                    // Damage wave 2 units
                    if (i - 26 < wave2.Count)
                    {
                        ref var health = ref repo.GetComponentRW<Health>(wave2[i - 26]);
                        health.Value = 50;
                    }
                    
                    if (i % 5 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
            }
            
            // Playback with seeking
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Health>();
            targetRepo.RegisterComponent<UnitData>();
            
            // Seek to frame 10: Should have 10 wave 1 units
            controller.SeekToFrame(targetRepo, 10);
            Assert.Equal(10, targetRepo.EntityCount);
            VerifyUnitsExist(targetRepo, wave1, "Infantry", 100);
            
            // Seek to frame 15: Should have 5 wave 1 units (half destroyed)
            controller.SeekToFrame(targetRepo, 15);
            Assert.Equal(5, targetRepo.EntityCount);
            
            // Seek to frame 25: Should have 5 wave 1 + 10 wave 2 = 15 total
            controller.SeekToFrame(targetRepo, 25);
            Assert.Equal(15, targetRepo.EntityCount);
            
            // Seek to frame 30: Units should be damaged
            controller.SeekToFrame(targetRepo, 30);
            Assert.Equal(15, targetRepo.EntityCount);
            // First 5 wave2 units should have health 50 (damaged)
            for (int i = 0; i < 5 && i < wave2.Count; i++)
            {
                if (targetRepo.IsAlive(wave2[i]))
                {
                    var health = targetRepo.GetComponentRO<Health>(wave2[i]);
                    Assert.Equal(50, health.Value);
                }
            }
        }

        [Fact]
        public void RapidCreateDestroy_StressTest()
        {
            // Stress test: Create and destroy many entities rapidly
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            const int frames = 50;
            const int entitiesPerFrame = 10;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                var activeEntities = new List<Entity>();
                
                for (int frame = 1; frame < frames; frame++)
                {
                    repo.Tick();
                    
                    // Create new entities
                    for (int i = 0; i < entitiesPerFrame; i++)
                    {
                        var e = repo.CreateEntity();
                        repo.AddComponent(e, new IntComponent { Value = frame * 100 + i });
                        activeEntities.Add(e);
                    }
                    
                    // Destroy some old entities (keep it from growing unbounded)
                    if (activeEntities.Count > 50)
                    {
                        for (int i = 0; i < entitiesPerFrame; i++)
                        {
                            repo.DestroyEntity(activeEntities[0]);
                            activeEntities.RemoveAt(0);
                        }
                    }
                    
                    if (frame % 10 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
            }
            
            // Playback: Verify we can play through without errors
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var controller = new PlaybackController(_testFilePath);
            
            // Play to end
            int framesPlayed = 0;
            controller.PlayToEnd(targetRepo, (current, total) =>
            {
                framesPlayed++;
            });
            
            Assert.Equal(frames, framesPlayed);
            Assert.True(controller.IsAtEnd);
            
            // Entity count should stabilize around 50 (allow some variance)
            Assert.InRange(targetRepo.EntityCount, 40, 70);
        }

        [Fact]
        public void EntityRecreation_SameSlotMultipleTimes_MaintainsIntegrity()
        {
            // Create, destroy, recreate the same slot multiple times
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            var generations = new List<Entity>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Create and destroy slot 0 five times
                for (int cycle = 0; cycle < 5; cycle++)
                {
                    // Create
                    repo.Tick();
                    var entity = repo.CreateEntity();
                    repo.AddComponent(entity, new IntComponent { Value = cycle * 100 });
                    generations.Add(entity);
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                    
                    // Destroy
                    repo.Tick();
                    repo.DestroyEntity(entity);
                    recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                    prevTick = repo.GlobalVersion;
                }
            }
            
            // All should share index 0 but have increasing generations
            for (int i = 0; i < generations.Count; i++)
            {
                Assert.Equal(0, generations[i].Index);
                if (i > 0)
                {
                    Assert.True(generations[i].Generation > generations[i - 1].Generation,
                        $"Gen[{i}] ({generations[i].Generation}) should be > Gen[{i-1}] ({generations[i-1].Generation})");
                }
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            using var reader = new RecordingReader(_testFilePath);
            reader.ReadNextFrame(targetRepo); // Frame 0: empty
            
            for (int cycle = 0; cycle < 5; cycle++)
            {
                // Create frame
                reader.ReadNextFrame(targetRepo);
                Assert.Equal(1, targetRepo.EntityCount);
                Assert.True(targetRepo.IsAlive(generations[cycle]));
                Assert.Equal(cycle * 100, targetRepo.GetComponentRO<IntComponent>(generations[cycle]).Value);
                
                // Destroy frame
                reader.ReadNextFrame(targetRepo);
                Assert.Equal(0, targetRepo.EntityCount);
                Assert.False(targetRepo.IsAlive(generations[cycle]));
            }
        }

        [Fact]
        public void BatchOperations_CreateDestroyMany_AtOnce()
        {
            // Realistic scenario: Spawn entire armies, destroy entire armies
            using var repo = new EntityRepository();
            repo.RegisterComponent<Health>();
            repo.RegisterComponent<UnitData>();
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                uint prevTick = repo.GlobalVersion;
                
                // Frame 1: Spawn 100 units at once
                repo.Tick();
                var army = new List<Entity>();
                for (int i = 0; i < 100; i++)
                {
                    var unit = repo.CreateEntity();
                    repo.AddComponent(unit, new Health { Value = 100 });
                    repo.AddManagedComponent(unit, new UnitData { UnitType = "Soldier", Level = i % 10 });
                    army.Add(unit);
                }
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 2: Destroy 50 units
                repo.Tick();
                for (int i = 0; i < 50; i++)
                {
                    repo.DestroyEntity(army[i]);
                }
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
                prevTick = repo.GlobalVersion;
                
                // Frame 3: Spawn 75 more (reusing 50 slots + 25 new)
                repo.Tick();
                for (int i = 0; i < 75; i++)
                {
                    var unit = repo.CreateEntity();
                    repo.AddComponent(unit, new Health { Value = 120 });
                    repo.AddManagedComponent(unit, new UnitData { UnitType = "Elite", Level = 5 });
                }
                recorder.CaptureFrame(repo, prevTick, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Playback
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Health>();
            targetRepo.RegisterComponent<UnitData>();
            
            using var reader = new RecordingReader(_testFilePath);
            
            // Frame 0: Empty
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(0, targetRepo.EntityCount);
            
            // Frame 1: 100 units
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(100, targetRepo.EntityCount);
            
            // Frame 2: 50 units (50 destroyed)
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(50, targetRepo.EntityCount);
            
            // Frame 3: 125 units (50 survivors + 75 new)
            reader.ReadNextFrame(targetRepo);
            Assert.Equal(125, targetRepo.EntityCount);
        }

        // Helper method
        private void VerifyUnitsExist(EntityRepository repo, List<Entity> entities, string expectedType, int expectedHealth)
        {
            foreach (var entity in entities)
            {
                if (repo.IsAlive(entity))
                {
                    var unitData = repo.GetComponentRO<UnitData>(entity);
                    Assert.Equal(expectedType, unitData.UnitType);
                    
                    var health = repo.GetComponentRO<Health>(entity);
                    Assert.Equal(expectedHealth, health.Value);
                }
            }
        }
    }
}
