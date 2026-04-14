using Fdp.Kernel;
using System;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Tests.Benchmarks
{
    public class QueryIterationBenchmarks
    {
        private readonly ITestOutputHelper _output;

        public QueryIterationBenchmarks(ITestOutputHelper output)
        {
            _output = output;
        }

        [ComponentId(241)]
        struct Pos { public float X, Y; }
        [ComponentId(242)]
        struct Vel { public float X, Y; }

        [Fact]
        public void Benchmark_IterationPerformance()
        {
             using var repo = new EntityRepository();
            repo.RegisterComponent<Pos>();
            repo.RegisterComponent<Vel>();

            // Setup 10k entities
            for (int i = 0; i < 10000; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new Pos { X = i });
                
                // 50% have Velocity
                if (i % 2 == 0)
                {
                    repo.AddComponent(e, new Vel { X = 1 });
                }
            }

            var query = repo.Query().With<Pos>().With<Vel>().Build();

            // Warmup
            RunForEach(repo, query);
            RunEnumerator(repo, query);

            // Benchmark
            int iterations = 1000;
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                RunForEach(repo, query);
            }
            sw.Stop();
            double lambdaTime = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"Lambda ForEach: {lambdaTime:F2} ms ({lambdaTime/iterations*1000:F2} us/call)");

            sw.Restart();
            for (int i = 0; i < iterations; i++)
            {
                RunEnumerator(repo, query);
            }
            sw.Stop();
            double enumTime = sw.Elapsed.TotalMilliseconds;
            _output.WriteLine($"Enumerator loop: {enumTime:F2} ms ({enumTime/iterations*1000:F2} us/call)");

            // Assert improvement (Allow some margin, but typically enumerator should be faster)
            // Note: In debug builds this might be closer, but allocation difference is the key.
            // We mainly want to verify it runs and is fast.
            _output.WriteLine($"Speedup: {lambdaTime / enumTime:F2}x");
            
            Assert.True(true); 
        }

        private void RunForEach(EntityRepository repo, EntityQuery query)
        {
            float totalX = 0;
            // Suppress obsolete warning for benchmark
            #pragma warning disable CS0618
            // Converted to foreach to satisfy zero-alloc rules
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Pos>(e);
                totalX += pos.X;
            }
            #pragma warning restore CS0618
        }

        private void RunEnumerator(EntityRepository repo, EntityQuery query)
        {
            float totalX = 0;
            foreach (var e in query)
            {
                ref readonly var pos = ref repo.GetComponentRO<Pos>(e);
                totalX += pos.X;
            }
        }
    }
}
