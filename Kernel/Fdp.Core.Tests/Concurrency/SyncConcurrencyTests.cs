using Xunit;
using Fdp.Kernel;
using System.Threading.Tasks;

namespace Fdp.Tests.Concurrency
{
    public class SyncConcurrencyTests
    {
        [ComponentId(244)]
        struct Pos { public float X; }
        
        [Fact]
        public void ConcurrentSyncFrom_NoCorruption()
        {
            using var source = new EntityRepository();
            using var dest1 = new EntityRepository();
            using var dest2 = new EntityRepository();
            
            source.RegisterComponent<Pos>();
            dest1.RegisterComponent<Pos>();
            dest2.RegisterComponent<Pos>();
            
            // Create 1000 entities in source
            for (int i = 0; i < 1000; i++)
            {
                var e = source.CreateEntity();
                source.AddComponent(e, new Pos { X = i });
            }
            
            // Concurrent sync from two threads
            var task1 = Task.Run(() => dest1.SyncFrom(source));
            var task2 = Task.Run(() => dest2.SyncFrom(source));
            
            Task.WaitAll(task1, task2);
            
            // Verify both destinations have correct data
            Assert.Equal(1000, dest1.EntityCount);
            Assert.Equal(1000, dest2.EntityCount);
            
            // Spot check some entities
            for (int i = 0; i < 100; i += 10)
            {
                var e = new Entity(i, 1);
                Assert.Equal(i, dest1.GetComponentRO<Pos>(e).X);
                Assert.Equal(i, dest2.GetComponentRO<Pos>(e).X);
            }
        }
        
        [Fact]
        public void ConcurrentSyncFrom_Stress_100Iterations()
        {
            // Run concurrent sync test 100 times to detect races
            for (int iteration = 0; iteration < 100; iteration++)
            {
                ConcurrentSyncFrom_NoCorruption();
            }
        }
    }
}
