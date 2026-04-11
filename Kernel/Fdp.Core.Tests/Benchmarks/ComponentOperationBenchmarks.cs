using System;
using System.Diagnostics;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests.Benchmarks
{
    public class ComponentOperationBenchmarks
    {
        [ComponentId(240)]
        private struct TestComponent
        {
            public int Value;
        }

        [ComponentId(245)]
        private record BenchStringComp(string Value);
        
        [Fact]
        public void Benchmark_SetRawObject_Performance()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponent>();
            
            const int iterations = 100_000;
            var entities = new Entity[iterations];
            
            // Create entities
            for (int i = 0; i < iterations; i++)
            {
                entities[i] = repo.CreateEntity();
            }
            
            // Benchmark SetRawObject
            var sw = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++)
            {
                var component = new TestComponent { Value = i };
                repo.SetComponent(entities[i], component);  
            }
             
            sw.Stop();
            
            var avgMicroseconds = (sw.Elapsed.TotalMicroseconds / iterations);
            Console.WriteLine($"Average time per SetComponent (Direct): {avgMicroseconds:F3} μs");
            
            repo.Dispose();
        }

        [Fact]
        public void Benchmark_SetManagedComponent_Performance()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<BenchStringComp>(DataPolicy.Transient);
            
            const int iterations = 100_000;
            var entities = new Entity[iterations];
            
            // Create entities
            for (int i = 0; i < iterations; i++)
            {
                entities[i] = repo.CreateEntity();
            }
            
            // Benchmark SetManagedComponent (optimized path)
            var sw = Stopwatch.StartNew();
            
            var testStr = new BenchStringComp("TestValue");
            for (int i = 0; i < iterations; i++)
            {
                repo.AddManagedComponent(entities[i], testStr);  
            }
             
            sw.Stop();
            
            var avgMicroseconds = (sw.Elapsed.TotalMicroseconds / iterations);
            Console.WriteLine($"Average time per AddManagedComponent (Direct): {avgMicroseconds:F3} μs");
            
            repo.Dispose();
        }
        
        [Fact]
        public void Benchmark_CommandBuffer_Playback()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TestComponent>(); // Unmanaged
            repo.RegisterManagedComponent<BenchStringComp>(DataPolicy.Transient); // Managed
            
            const int batchSize = 1000;
            var entity = repo.CreateEntity();
            
            var sw = Stopwatch.StartNew();
            
            // Re-use same buffer logic to simulate batching
            // We create one command buffer and add many ops
            // We create one command buffer and add many ops
            using var cmd = new EntityCommandBuffer();
            
            // Note: In real scenarios, CommandBuffers are per-module-turn.
            // Here we just spam one buffer.
            for (int i = 0; i < batchSize; i++)
            {
                cmd.SetComponent(entity, new TestComponent { Value = i }); 
                cmd.SetManagedComponent(entity, new BenchStringComp("TestString"));
            }
            
            // Playback
            cmd.Playback(repo);
            
            sw.Stop();
            
            // batchSize iterations. Each has 2 SetComponent calls (1 unmanaged, 1 managed).
            // Total operations = batchSize * 2.
            var avgMicroseconds = sw.Elapsed.TotalMicroseconds / (batchSize * 2);
            
            Console.WriteLine($"Average command buffer playback (Mixed): {avgMicroseconds:F3} μs");
            
            // Expectation: < 5.0 us.
            Assert.True(avgMicroseconds < 5.0,
                $"Command buffer too slow: {avgMicroseconds:F3} μs");
            
            repo.Dispose();
        }
    }
}
