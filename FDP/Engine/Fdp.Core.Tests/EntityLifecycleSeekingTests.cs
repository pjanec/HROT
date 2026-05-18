using System;
using System.IO;
using Xunit;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using MessagePack;

namespace Fdp.Tests
{
    /// <summary>
    /// Comprehensive tests for seeking through entity lifecycle states during playback.
    /// Tests seeking to frames where entities:
    /// - Don't exist yet
    /// - Are alive
    /// - Have been deleted
    /// - Have been recreated in the same slot with different generation
    /// </summary>
    public class EntityLifecycleSeekingTests : IDisposable
    {
        private readonly string _testFilePath;
        
        public EntityLifecycleSeekingTests()
        {
            _testFilePath = Path.Combine(Path.GetTempPath(), $"lifecycle_seek_{Guid.NewGuid()}.fdp");
        }
        
        public void Dispose()
        {
            try { File.Delete(_testFilePath); } catch {}
        }

        [ComponentId(215)]
        public struct Position { public float X, Y, Z; }
        
        [MessagePackObject]
        [ComponentId(216)]
        public record UnitName
        {
            [Key(0)]
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void SeekToFrames_BeforeEntityExists()
        {
            // Test seeking to frames before an entity was created
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            Entity entity;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frames 0-4: Entity doesn't exist
                for (int i = 0; i < 5; i++)
                {
                    repo.Tick();
                    if (i == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
                
                // Frame 5: Entity created
                repo.Tick();
                entity = repo.CreateEntity();
                repo.AddComponent(entity, new IntComponent { Value = 100 });
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frames 6-10: Entity exists
                for (int i = 6; i <= 10; i++)
                {
                    repo.Tick();
                    repo.SetUnmanagedComponent(entity, new IntComponent { Value = i * 10 });
                    recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            // Seek to frame 2: Entity should NOT exist
            controller.SeekToFrame(targetRepo, 2);
            Assert.False(targetRepo.IsAlive(entity));
            Assert.Equal(0, targetRepo.EntityCount);
            
            // Seek to frame 5: Entity should NOW exist
            controller.SeekToFrame(targetRepo, 5);
            Assert.True(targetRepo.IsAlive(entity));
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(entity).Value);
            
            // Seek back to frame 0: Entity should NOT exist again
            controller.SeekToFrame(targetRepo, 0);
            Assert.False(targetRepo.IsAlive(entity));
            
            // Seek forward to frame 8: Entity should exist
            controller.SeekToFrame(targetRepo, 8);
            Assert.True(targetRepo.IsAlive(entity));
            Assert.Equal(80, targetRepo.GetComponentRO<IntComponent>(entity).Value);
        }

        [Fact]
        public void SeekToFrames_AfterEntityDeleted()
        {
            // Test seeking to frames after an entity has been deleted
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            Entity entity;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                // Frame 0: Empty
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 1: Create entity
                repo.Tick();
                entity = repo.CreateEntity();
                repo.AddComponent(entity, new IntComponent { Value = 100 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frames 2-4: Entity exists
                for (int i = 2; i <= 4; i++)
                {
                    repo.Tick();
                    repo.SetUnmanagedComponent(entity, new IntComponent { Value = i * 100 });
                    recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
                
                // Frame 5: Destroy entity
                repo.Tick();
                repo.DestroyEntity(entity);
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frames 6-10: Entity is dead
                for (int i = 6; i <= 10; i++)
                {
                    repo.Tick();
                    recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            // Seek to frame 3: Entity should be ALIVE
            controller.SeekToFrame(targetRepo, 3);
            Assert.True(targetRepo.IsAlive(entity));
            Assert.Equal(300, targetRepo.GetComponentRO<IntComponent>(entity).Value);
            
            // Seek to frame 7: Entity should be DEAD
            controller.SeekToFrame(targetRepo, 7);
            Assert.False(targetRepo.IsAlive(entity));
            Assert.Equal(0, targetRepo.EntityCount);
            
            // Seek back to frame 2: Entity should be alive again
            controller.SeekToFrame(targetRepo, 2);
            Assert.True(targetRepo.IsAlive(entity));
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(entity).Value);
            
            // Seek forward to frame 10: Entity should be dead
            controller.SeekToFrame(targetRepo, 10);
            Assert.False(targetRepo.IsAlive(entity));
        }

        [Fact]
        public void SeekToFrames_ThroughCreationDeletionRecreation()
        {
            // THE KEY TEST: Seek through the complete lifecycle of slot reuse
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            repo.RegisterComponent<UnitName>();
            
            Entity firstGen, secondGen, thirdGen;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Cycle 1: Create, use, delete
                repo.Tick(); // Frame 1
                firstGen = repo.CreateEntity();
                repo.AddComponent(firstGen, new IntComponent { Value = 100 });
                repo.AddManagedComponent(firstGen, new UnitName { Name = "FirstGen" });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                repo.Tick(); // Frame 2
                repo.SetUnmanagedComponent(firstGen, new IntComponent { Value = 200 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                repo.Tick(); // Frame 3
                repo.DestroyEntity(firstGen);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Cycle 2: Recreate same slot (higher generation)
                repo.Tick(); // Frame 4
                secondGen = repo.CreateEntity();
                repo.AddComponent(secondGen, new IntComponent { Value = 300 });
                repo.AddManagedComponent(secondGen, new UnitName { Name = "SecondGen" });
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                repo.Tick(); // Frame 5
                repo.SetUnmanagedComponent(secondGen, new IntComponent { Value = 400 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                repo.Tick(); // Frame 6
                repo.DestroyEntity(secondGen);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Cycle 3: Recreate again
                repo.Tick(); // Frame 7
                thirdGen = repo.CreateEntity();
                repo.AddComponent(thirdGen, new IntComponent { Value = 500 });
                repo.AddManagedComponent(thirdGen, new UnitName { Name = "ThirdGen" });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            // Verify all three use the same slot but different generations
            Assert.Equal(firstGen.Index, secondGen.Index);
            Assert.Equal(secondGen.Index, thirdGen.Index);
            Assert.True(secondGen.Generation > firstGen.Generation);
            Assert.True(thirdGen.Generation > secondGen.Generation);
            
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            targetRepo.RegisterComponent<UnitName>();
            
            // Frame 0: Nothing exists
            controller.SeekToFrame(targetRepo, 0);
            Assert.Equal(0, targetRepo.EntityCount);
            Assert.False(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            Assert.False(targetRepo.IsAlive(thirdGen));
            
            // Frame 1: FirstGen exists
            controller.SeekToFrame(targetRepo, 1);
            Assert.Equal(1, targetRepo.EntityCount);
            Assert.True(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            Assert.False(targetRepo.IsAlive(thirdGen));
            Assert.Equal(100, targetRepo.GetComponentRO<IntComponent>(firstGen).Value);
            Assert.Equal("FirstGen", targetRepo.GetComponentRO<UnitName>(firstGen).Name);
            
            // Frame 2: FirstGen modified
            controller.SeekToFrame(targetRepo, 2);
            Assert.True(targetRepo.IsAlive(firstGen));
            Assert.Equal(200, targetRepo.GetComponentRO<IntComponent>(firstGen).Value);
            
            // Frame 3: FirstGen deleted
            controller.SeekToFrame(targetRepo, 3);
            Assert.Equal(0, targetRepo.EntityCount);
            Assert.False(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            
            // Frame 4: SecondGen exists (FirstGen still dead)
            controller.SeekToFrame(targetRepo, 4);
            Assert.Equal(1, targetRepo.EntityCount);
            Assert.False(targetRepo.IsAlive(firstGen)); // OLD gen still dead
            Assert.True(targetRepo.IsAlive(secondGen)); // NEW gen alive
            Assert.Equal(300, targetRepo.GetComponentRO<IntComponent>(secondGen).Value);
            Assert.Equal("SecondGen", targetRepo.GetComponentRO<UnitName>(secondGen).Name);
            
            // Frame 5: SecondGen modified
            controller.SeekToFrame(targetRepo, 5);
            Assert.True(targetRepo.IsAlive(secondGen));
            Assert.Equal(400, targetRepo.GetComponentRO<IntComponent>(secondGen).Value);
            
            // Frame 6: SecondGen deleted
            controller.SeekToFrame(targetRepo, 6);
            Assert.Equal(0, targetRepo.EntityCount);
            Assert.False(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            Assert.False(targetRepo.IsAlive(thirdGen));
            
            // Frame 7: ThirdGen exists (both previous gens still dead)
            controller.SeekToFrame(targetRepo, 7);
            Assert.Equal(1, targetRepo.EntityCount);
            Assert.False(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            Assert.True(targetRepo.IsAlive(thirdGen));
            Assert.Equal(500, targetRepo.GetComponentRO<IntComponent>(thirdGen).Value);
            Assert.Equal("ThirdGen", targetRepo.GetComponentRO<UnitName>(thirdGen).Name);
            
            // Seek backward through critical points
            controller.SeekToFrame(targetRepo, 5); // SecondGen alive
            Assert.True(targetRepo.IsAlive(secondGen));
            Assert.False(targetRepo.IsAlive(thirdGen));
            
            controller.SeekToFrame(targetRepo, 2); // FirstGen alive
            Assert.True(targetRepo.IsAlive(firstGen));
            Assert.False(targetRepo.IsAlive(secondGen));
            Assert.False(targetRepo.IsAlive(thirdGen));
        }

        [Fact]
        public void SeekToFrames_MultipleEntities_DifferentLifecycles()
        {
            // Realistic test: Multiple entities with overlapping but different lifecycles
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            Entity knight, archer, mage;
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick(); // Frame 0
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 1: Knight spawns
                repo.Tick();
                knight = repo.CreateEntity();
                repo.AddComponent(knight, new Position { X = 1, Y = 0, Z = 0 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 2: Archer spawns
                repo.Tick();
                archer = repo.CreateEntity();
                repo.AddComponent(archer, new Position { X = 2, Y = 0, Z = 0 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 3: Both move
                repo.Tick();
                repo.SetUnmanagedComponent(knight, new Position { X = 1, Y = 1, Z = 0 });
                repo.SetUnmanagedComponent(archer, new Position { X = 2, Y = 1, Z = 0 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 4: Knight dies
                repo.Tick();
                repo.DestroyEntity(knight);
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 5: Mage spawns (reuses knight's slot)
                repo.Tick();
                mage = repo.CreateEntity();
                repo.AddComponent(mage, new Position { X = 3, Y = 0, Z = 0 });
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Frame 6: Archer and Mage move
                repo.Tick();
                repo.SetUnmanagedComponent(archer, new Position { X = 2, Y = 2, Z = 0 });
                repo.SetUnmanagedComponent(mage, new Position { X = 3, Y = 1, Z = 0 });
                recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
            }
            
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<Position>();
            
            // Frame 1: Only knight
            controller.SeekToFrame(targetRepo, 1);
            Assert.True(targetRepo.IsAlive(knight));
            Assert.False(targetRepo.IsAlive(archer));
            Assert.False(targetRepo.IsAlive(mage));
            Assert.Equal(1, targetRepo.EntityCount);
            
            // Frame 2: Knight and archer
            controller.SeekToFrame(targetRepo, 2);
            Assert.True(targetRepo.IsAlive(knight));
            Assert.True(targetRepo.IsAlive(archer));
            Assert.False(targetRepo.IsAlive(mage));
            Assert.Equal(2, targetRepo.EntityCount);
            
            // Frame 3: Both alive and moved
            controller.SeekToFrame(targetRepo, 3);
            Assert.Equal(1f, targetRepo.GetComponentRO<Position>(knight).Y);
            Assert.Equal(1f, targetRepo.GetComponentRO<Position>(archer).Y);
            
            // Frame 4: Knight dead, archer alive
            controller.SeekToFrame(targetRepo, 4);
            Assert.False(targetRepo.IsAlive(knight));
            Assert.True(targetRepo.IsAlive(archer));
            Assert.False(targetRepo.IsAlive(mage));
            Assert.Equal(1, targetRepo.EntityCount);
            
            // Frame 5: Mage spawned (knight's slot reused)
            controller.SeekToFrame(targetRepo, 5);
            Assert.False(targetRepo.IsAlive(knight)); // Old gen
            Assert.True(targetRepo.IsAlive(archer));
            Assert.True(targetRepo.IsAlive(mage)); // New gen in knight's slot
            Assert.Equal(2, targetRepo.EntityCount);
            
            // Frame 6: Archer and mage moved
            controller.SeekToFrame(targetRepo, 6);
            Assert.Equal(2f, targetRepo.GetComponentRO<Position>(archer).Y);
            Assert.Equal(1f, targetRepo.GetComponentRO<Position>(mage).Y);
            
            // Seek backwards to validate state integrity
            controller.SeekToFrame(targetRepo, 3);
            Assert.True(targetRepo.IsAlive(knight));
            Assert.True(targetRepo.IsAlive(archer));
            Assert.False(targetRepo.IsAlive(mage));
            
            controller.SeekToFrame(targetRepo, 0);
            Assert.Equal(0, targetRepo.EntityCount);
        }

        [Fact]
        public void RandomSeekPattern_StressTest()
        {
            // Stress test: Random seeking through complex lifecycle
            using var repo = new EntityRepository();
            repo.RegisterComponent<IntComponent>();
            
            const int totalFrames = 20;
            var entities = new Entity[10];
            
            using (var recorder = new AsyncRecorder(_testFilePath))
            {
                repo.Tick();
                recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                
                // Create complex pattern: spawn, modify, destroy entities
                for (int frame = 1; frame <= totalFrames; frame++)
                {
                    repo.Tick();
                    
                    if (frame % 3 == 1 && frame / 3 < entities.Length)
                    {
                        // Create entity
                        entities[frame / 3] = repo.CreateEntity();
                        repo.AddComponent(entities[frame / 3], new IntComponent { Value = frame * 10 });
                    }
                    else if (frame % 3 == 0 && frame / 3 - 1 >= 0 && frame / 3 - 1 < entities.Length)
                    {
                        // Destroy entity
                        if (repo.IsAlive(entities[frame / 3 - 1]))
                        {
                            repo.DestroyEntity(entities[frame / 3 - 1]);
                        }
                    }
                    
                    if (frame % 5 == 0)
                        recorder.CaptureKeyframe(repo, DateTime.UtcNow.Ticks, blocking: true);
                    else
                        recorder.CaptureFrame(repo, repo.GlobalVersion - 1, DateTime.UtcNow.Ticks, blocking: true);
                }
            }
            
            using var controller = new PlaybackController(_testFilePath);
            using var targetRepo = new EntityRepository();
            targetRepo.RegisterComponent<IntComponent>();
            
            // Random seek pattern
            var random = new Random(42); // Fixed seed for reproducibility
            for (int i = 0; i < 50; i++)
            {
                int targetFrame = random.Next(0, totalFrames + 1);
                controller.SeekToFrame(targetRepo, targetFrame);
                
                // Just verify we can seek without crashing
                // Entity count should be reasonable
                Assert.InRange(targetRepo.EntityCount, 0, 10);
            }
            
            // Verify we can still seek sequentially after random access
            controller.SeekToFrame(targetRepo, 0);
            Assert.Equal(0, targetRepo.EntityCount);
            
            controller.SeekToFrame(targetRepo, totalFrames);
            // Should have some entities but not all (some were destroyed)
            Assert.True(targetRepo.EntityCount < 10);
        }
    }
}
