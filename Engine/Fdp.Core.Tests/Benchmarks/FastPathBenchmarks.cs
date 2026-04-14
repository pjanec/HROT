using System;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using Fdp.Core;

namespace Fdp.Tests.Benchmarks
{
    public class FastPathBenchmarks
    {
        private readonly ITestOutputHelper _output;

        public FastPathBenchmarks(ITestOutputHelper output)
        {
            _output = output;
        }

        [ComponentId(243)]
        struct Pos { public float X, Y; }

        [Fact]
        public void Benchmark_HotPathOptimization()
        {
             // Setup
             using var repo = new EntityRepository();
             repo.RegisterComponent<Pos>();
             
             // Create 100k entities
             int count = 100_000;
             for(int i=0; i<count; i++) {
                 var e = repo.CreateEntity();
                 repo.AddComponent(e, new Pos());
             }
             
             var query = repo.Query().With<Pos>().Build();
             
             // Warmup
             RunStandard(repo, query);
             RunHoisted(repo, query);
             
             // Run Standard (5 iterations to average)
             int iterations = 5;
             var sw = Stopwatch.StartNew();
             for(int i=0; i<iterations; i++) RunStandard(repo, query);
             sw.Stop();
             double standardAvg = sw.Elapsed.TotalMilliseconds / iterations;
             
             // Run Hoisted
             sw.Restart();
             for(int i=0; i<iterations; i++) RunHoisted(repo, query);
             sw.Stop();
             double hoistedAvg = sw.Elapsed.TotalMilliseconds / iterations;
             
             _output.WriteLine($"Standard: {standardAvg:F3} ms");
             _output.WriteLine($"Hoisted:  {hoistedAvg:F3} ms");
             double speedup = standardAvg / hoistedAvg;
             _output.WriteLine($"Speedup:  {speedup:F2}x");
             
             // In Debug mode, inlining might not happen, reducing the speedup.
             // We assert functionality and some improvement.
             Assert.True(speedup > 1.5, $"Expected significant speedup, got {speedup:F2}x");
        }
        
        private void RunStandard(EntityRepository repo, EntityQuery query)
        {
            foreach(var e in query) {
                // This does Dictionary lookup + WriteAccess validation + Version increment
                ref var pos = ref repo.GetComponentRW<Pos>(e);
                 pos.X++;
            }
        }
        
        private void RunHoisted(EntityRepository repo, EntityQuery query)
        {
            // Hoist table lookup
            var table = repo.GetComponentTable<Pos>();
            
            foreach(var e in query) {
                // Direct array access (no validation, no version increment)
                ref var pos = ref table.Get(e.Index);
                pos.X++;
            }
        }
    }
}
