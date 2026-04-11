using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class EntityQueryEnumeratorTests
    {
        [ComponentId(217)]
        struct Pos { public float X; }
        [ComponentId(218)]
        struct Vel { public float X; }
        [ComponentId(219)]
        struct Tag { }

        [Fact]
        public void Enumerator_IteratesAllMatches()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            
            // Create 100 entities
            for (int i = 0; i < 100; i++)
            {
                var e = repo.CreateEntity();
                
                // 50 match (even indices)
                if (i % 2 == 0)
                {
                    repo.AddComponent(e, new Pos { X = i });
                }
            }
            
            var query = repo.Query().With<Pos>().Build();
            
            int count = 0;
            foreach (var e in query)
            {
                Assert.True(repo.HasComponent<Pos>(e));
                count++;
            }
            
            Assert.Equal(50, count);
        }

        [Fact]
        public void Enumerator_SkipsNonMatches()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            repo.RegisterComponent<Vel>();
            
            // 1. With Pos (Should match)
            repo.AddComponent(repo.CreateEntity(), new Pos());
            
            // 2. With Vel (Should NOT match)
            repo.AddComponent(repo.CreateEntity(), new Vel());
            
            // 3. With Pos + Vel (Should match)
            var e3 = repo.CreateEntity();
            repo.AddComponent(e3, new Pos());
            repo.AddComponent(e3, new Vel());
            
            // 4. Empty (Should NOT match)
            repo.CreateEntity();
            
            var query = repo.Query().With<Pos>().Build();
            
            int count = 0;
            foreach (var e in query)
            {
                Assert.True(repo.HasComponent<Pos>(e));
                count++;
            }
            
            Assert.Equal(2, count);
        }

        [Fact]
        public void Enumerator_EmptyQuery_ReturnsFalseImmediately()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            
            var query = repo.Query().With<Pos>().Build();
            
            foreach (var e in query)
            {
                Assert.Fail("Should not iterate");
            }
        }

        [Fact]
        public void Enumerator_HandlesGaps()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            
            // Create 10 entities
            var entities = new Entity[10];
            for (int i = 0; i < 10; i++)
            {
                entities[i] = repo.CreateEntity();
                repo.AddComponent(entities[i], new Pos { X = i });
            }
            
            // Destroy indices 2, 5, 8
            repo.DestroyEntity(entities[2]);
            repo.DestroyEntity(entities[5]);
            repo.DestroyEntity(entities[8]);
            
            var query = repo.Query().With<Pos>().Build();
            
            int count = 0;
            foreach (var e in query)
            {
                Assert.True(repo.IsAlive(e));
                Assert.True(repo.HasComponent<Pos>(e));
                
                // Ensure destroyed ones are skipped
                int index = e.Index;
                Assert.NotEqual(2, index);
                Assert.NotEqual(5, index);
                Assert.NotEqual(8, index);
                
                count++;
            }
            
            Assert.Equal(7, count);
        }
    }
}
