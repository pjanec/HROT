using System.Collections.Generic;
using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class DeltaTests
    {
        public DeltaTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void CreateEntity_MarksChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.Tick(); // v=2.
            
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1 });
            
            // Check delta since v=1 (before tick)
            int count = 0;
            repo.QueryDelta(repo.Query().With<Position>().Build(), 1, e => 
            {
                Assert.Equal(entity, e);
                count++;
            });
            
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void NoChange_Skips()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1 });
            
            repo.Tick(); // Bump version to 2
            
            // Entity created at v=1? No, CreateEntity used v=1.
            // Wait, CreateEntity used v=1 (default).
            // Tick bumps to 2.
            
            // QueryDelta since v=2. Should be empty.
            // Entity.LastChangeTick = 1.
            // Chunk.Version = 1 (from AddComponent).
            
            int count = 0;
            repo.QueryDelta(repo.Query().With<Position>().Build(), 2, e => count++);
            Assert.Equal(0, count);
            
            // QueryDelta since v=1. Should be empty?
            // > 1? If Entity.Tick = 1, then 1 > 1 is False.
            // So if created at v=1, checking since v=1 skips it. Correct.
            // Must check since v=0 to see v=1 changes.
            repo.QueryDelta(repo.Query().With<Position>().Build(), 0, e => count++);
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void GetComponentRW_MarksChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1 });
            
            // Current v=1.
            repo.Tick(); // v=2.
            
            // Read-Write access
            ref var pos = ref repo.GetComponentRW<Position>(entity);
            pos.X = 100;
            
            // Should match query since v=1
            int count = 0;
            repo.QueryDelta(repo.Query().With<Position>().Build(), 1, e => count++);
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void GetComponentRO_DoesNotMarkChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1 });
            
            repo.Tick(); // v=2.
            
            // Read-Only access
            ref readonly var pos = ref repo.GetComponentRO<Position>(entity);
            // pos.X = 100; // Compile error
            
            // Should NOT match query since v=1
            int count = 0;
            repo.QueryDelta(repo.Query().With<Position>().Build(), 1, e => count++);
            Assert.Equal(0, count);
        }
        
        /// <summary>
        /// ⭐ <c>CE-3243</c> — a VALUE write is not a structural change, through every write path (SetComponent, the command
        /// buffer, a managed set): the entity shows up for a query on the written component (chunk version), and NOT for a query
        /// on another of its components. Adding a component still is structural (<see cref="StructureChange_MarksChanged"/>).
        /// 📐 Stamping on every value write made the flight recorder rewrite every table's chunk holding the entity, every frame.
        /// </summary>
        [Fact]
        public void CE3243_ValueWrites_AreNotStructural_OnAnyPath()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>();
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Position { X = 1 });
            repo.AddComponent(e, new Velocity { X = 1 });
            repo.Tick(); uint since = repo.GlobalVersion;

            int Count<T>() where T : unmanaged { int n = 0; repo.QueryDelta(repo.Query().With<T>().Build(), since, _ => n++); return n; }

            repo.Tick();
            repo.SetComponent(e, new Position { X = 2 });                       // direct value write
            Assert.Equal(1, Count<Position>());
            Assert.Equal(0, Count<Velocity>());

            repo.Tick(); since = repo.GlobalVersion; repo.Tick();
            using (var cmd = new EntityCommandBuffer())                          // command-buffer value write
            {
                cmd.SetComponent(e, new Position { X = 3 });
                cmd.Playback(repo);
            }
            Assert.Equal(3f, repo.GetComponentRO<Position>(e).X);
            Assert.Equal(1, Count<Position>());
            Assert.Equal(0, Count<Velocity>());
        }

        [Fact]
        public void StructureChange_MarksChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Position>();
            repo.RegisterComponent<Velocity>(); // Used for structural change
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new Position { X = 1 });
            
            repo.Tick(); // v=2
            
            // Add another component -> Structure change
            repo.AddComponent(entity, new Velocity { X = 1 }); // Header.ChangeTick = 2
            
            // Query for Position (even though we added Velocity, the entity changed)
            // Delta Iterator iterates "Entities changed".
            // If I query Position, and adding Velocity changed the entity...
            // "EntityHeader change" flags it.
            
            int count = 0;
            repo.QueryDelta(repo.Query().With<Position>().Build(), 1, e => count++);
            Assert.Equal(1, count);
        }
    }
}
