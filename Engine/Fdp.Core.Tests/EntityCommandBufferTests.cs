using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class EntityCommandBufferTests
    {
        [Fact]
        public void CreateEntity_RecordsAndPlaysback()
        {
            using var repo = new EntityRepository();
            using var ecb = new EntityCommandBuffer();
            
            // Record creation
            ecb.CreateEntity();
            ecb.CreateEntity();
            
            Assert.Equal(0, repo.Query().Build().Count()); // No entities yet
            
            // Playback
            ecb.Playback(repo);
            
            Assert.Equal(2, repo.Query().Build().Count()); // 2 entities created
        }
        
        [Fact]
        public void DestroyEntity_RecordsAndPlaysback()
        {
            using var repo = new EntityRepository();
            using var ecb = new EntityCommandBuffer();
            
            var entity = repo.CreateEntity();
            
            // Record destruction
            ecb.DestroyEntity(entity);
            
            Assert.True(repo.IsAlive(entity)); // Still alive
            
            // Playback
            ecb.Playback(repo);
            
            Assert.False(repo.IsAlive(entity)); // Now destroyed
        }
        
        [Fact]
        public void AddComponent_RecordsAndPlaysback()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb = new EntityCommandBuffer();
            
            var entity = repo.CreateEntity();
            
            // Record add
            ecb.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            Assert.False(repo.HasUnmanagedComponent<Position>(entity)); // Not added yet
            
            // Playback
            ecb.Playback(repo);
            
            Assert.True(repo.HasUnmanagedComponent<Position>(entity)); // Now added
            ref readonly var pos = ref repo.GetComponentRO<Position>(entity);
            Assert.Equal(10f, pos.X);
            Assert.Equal(20f, pos.Y);
            Assert.Equal(30f, pos.Z);
        }
        
        [Fact]
        public void SetComponent_RecordsAndPlaysback()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb = new EntityCommandBuffer();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1, Y = 2, Z = 3 });
            
            // Record set
            ecb.SetComponent(entity, new Position { X = 100, Y = 200, Z = 300 });
            
            ref readonly var oldPos = ref repo.GetComponentRO<Position>(entity);
            Assert.Equal(1f, oldPos.X); // Still old value
            
            // Playback
            ecb.Playback(repo);
            
            ref readonly var newPos = ref repo.GetComponentRO<Position>(entity);
            Assert.Equal(100f, newPos.X);
            Assert.Equal(200f, newPos.Y);
            Assert.Equal(300f, newPos.Z);
        }
        
        [Fact]
        public void RemoveComponent_RecordsAndPlaysback()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb = new EntityCommandBuffer();
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 10, Y = 20, Z = 30 });
            
            // Record remove
            ecb.RemoveComponent<Position>(entity);
            
            Assert.True(repo.HasUnmanagedComponent<Position>(entity)); // Still has it
            
            // Playback
            ecb.Playback(repo);
            
            Assert.False(repo.HasUnmanagedComponent<Position>(entity)); // Now removed
        }
        
        [Fact]
        public void MultipleOperations_PlaybackInOrder()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            
            using var ecb = new EntityCommandBuffer();
            
            // Record complex sequence
            var e1 = repo.CreateEntity();
            ecb.AddComponent(e1, new Position { X = 1, Y = 2, Z = 3 });
            ecb.AddComponent(e1, new Velocity { X = 10, Y = 20, Z = 30 });
            
            var e2 = repo.CreateEntity();
            ecb.AddComponent(e2, new Position { X = 100, Y = 200, Z = 300 });
            ecb.DestroyEntity(e2);
            
            // Playback
            ecb.Playback(repo);
            
            // E1 should have both components
            Assert.True(repo.HasUnmanagedComponent<Position>(e1));
            Assert.True(repo.HasUnmanagedComponent<Velocity>(e1));
            
            // E2 should be destroyed
            Assert.False(repo.IsAlive(e2));
        }
        
        [Fact]
        public void Playback_ClearsBuffer()
        {
            using var repo = new EntityRepository();
            using var ecb = new EntityCommandBuffer();
            
            ecb.CreateEntity();
            
            Assert.False(ecb.IsEmpty);
            
            ecb.Playback(repo);
            
            Assert.True(ecb.IsEmpty); // Buffer cleared
            Assert.Equal(0, ecb.Size);
        }
        
        [Fact]
        public void Clear_EmptiesBuffer()
        {
            using var repo = new EntityRepository();
            using var ecb = new EntityCommandBuffer();
            
            ecb.CreateEntity();
            ecb.CreateEntity();
            
            Assert.False(ecb.IsEmpty);
            
            ecb.Clear();
            
            Assert.True(ecb.IsEmpty);
            Assert.Equal(0, ecb.Size);
            
            // Playback should do nothing
            ecb.Playback(repo);
            Assert.Equal(0, repo.Query().Build().Count());
        }
        
        [Fact]
        public void CreateEntity_WithPlaceholder_RemapsOnPlayback()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb = new EntityCommandBuffer();
            
            // Create entity via ECB (returns placeholder)
            Entity placeholder = ecb.CreateEntity();
            ecb.AddComponent(placeholder, new Position { X = 42, Y = 0, Z = 0 });
            
            // Placeholder has negative index
            Assert.True(placeholder.Index < 0);
            
            // Playback
            ecb.Playback(repo);
            
            // A real entity should be created with the component
            var realEntity = repo.Query().With<Position>().Build().FirstOrNull();
            Assert.False(realEntity.IsNull);
            
            ref readonly var pos = ref repo.GetComponentRO<Position>(realEntity);
            Assert.Equal(42f, pos.X);
        }
        
        [Fact]
        public async System.Threading.Tasks.Task ECB_ThreadSafeRecording_NotThreadSafePlayback()
        {
            // This test documents the threading model:
            // Recording can be done from multiple threads if each has its own ECB
            // Playback MUST be done on the main thread
            
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb1 = new EntityCommandBuffer();
            using var ecb2 = new EntityCommandBuffer();
            
            // Simulate parallel recording (each thread has its own buffer)
            await System.Threading.Tasks.Task.Run(() =>
            {
                ecb1.CreateEntity();
                ecb1.CreateEntity();
            });
            
            await System.Threading.Tasks.Task.Run(() =>
            {
                ecb2.CreateEntity();
            });
            
            // Playback on main thread (one at a time)
            ecb1.Playback(repo);
            ecb2.Playback(repo);
            
            Assert.Equal(3, repo.Query().Build().Count());
        }
        
        [Fact]
        public void LargeBuffer_HandlesResize()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            
            using var ecb = new EntityCommandBuffer(initialCapacity: 64); // Start small
            
            // Record many operations to force resize
            for (int i = 0; i < 1000; i++)
            {
                var e = new Entity(i, 1); // Fake entity
                ecb.AddComponent(e, new Position { X = i, Y = i * 2, Z = i * 3 });
            }
            
            Assert.True(ecb.Size > 64); // Buffer resized
        }
    }
}
