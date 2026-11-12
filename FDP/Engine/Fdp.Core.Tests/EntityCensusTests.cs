using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Core;
using Xunit;
using A = Fdp.Tests.RecorderDeltaLogicTests.Position;
using B = Fdp.Tests.RecorderDeltaLogicTests.Velocity;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ N-1 (the cross-entity access census; docs/DESIGN_Parallel_Simulation.md §18) — <see cref="EntityCensus"/> classifies every access against the
    /// entity the thread is PROCESSING (the outermost <see cref="EntityQuery"/> walk): own / other-scan (an inner walk's entity) / other / none (no walk).
    /// The ON rail runs only with <c>HROT_ENTITY_CENSUS=1</c> (the switch is read once per process):
    /// <c>HROT_ENTITY_CENSUS=1 dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter FullyQualifiedName~EntityCensusTests</c>.
    /// 🔴 Red-proof (run): <c>Classify</c> comparing against the INNERMOST slot instead of the outermost ⇒ the neighbour read inside the inner walk
    /// counts as own and the "other-scan" row vanishes.
    /// </summary>
    public class EntityCensusTests
    {
        [Fact]
        public void TheCensusIsOffByDefault_AndAWalkPushesNothing()
        {
            if (EntityCensus.On) return;   // someone is running the census on purpose
            using var w = new EntityRepository();
            w.RegisterComponent<A>();
            for (int i = 0; i < 3; i++) w.AddComponent(w.CreateEntity(), new A { X = i });
            var q = w.Query().With<A>().Build();
            foreach (var e in q) { }                         // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int r = 0; r < 100; r++) foreach (var e in q) w.GetComponentRW<A>(e).X++;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(EntityCensus.Rel.None, EntityCensus.Classify(0));
        }

        [SkippableFact]
        public void OwnOtherScanOtherAndNone_AreClassifiedExactly()
        {
            Skip.IfNot(EntityCensus.On, "run with HROT_ENTITY_CENSUS=1");
            string reader = "system:CensusRail-" + Guid.NewGuid().ToString("N");
            using var w = new EntityRepository();
            w.RegisterComponent<A>(); w.RegisterComponent<B>();
            var e0 = w.CreateEntity(); var e1 = w.CreateEntity(); var e2 = w.CreateEntity();
            foreach (var e in new[] { e0, e1, e2 }) { w.AddComponent(e, new A { X = 1 }); w.AddComponent(e, new B { VX = 1 }); }
            var q = w.Query().With<A>().Build();
            EntityCensus.ResetThread();
            using (AccessTrace.Scope(reader, TraceContext.Tick))
            {
                foreach (var e in q)                                   // processing e
                {
                    _ = w.GetComponentRO<A>(e);                         // own read
                    w.GetComponentRW<A>(e).X++;                         // own write
                    var next = e.Index == e0.Index ? e1 : e0;
                    _ = w.GetComponentRO<B>(next);                      // other read (a looked-up neighbour)
                    w.GetComponentRW<B>(next).VX++;                      // other write
                    foreach (var f in q)                                // an inner scan
                        if (f.Index != e.Index) _ = w.GetComponentRO<A>(f);   // other-scan read
                }
                w.GetComponentRW<A>(e2).X++;                            // no walk open: none
            }
            EntityCensus.Dump();
            var path = Environment.GetEnvironmentVariable("HROT_ENTITY_CENSUS_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "hrot-entity-census.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            long N(string kind, string type, string rel) => doc.RootElement.GetProperty("rows").EnumerateArray()
                .Where(r => r.GetProperty("reader").GetString() == reader && r.GetProperty("kind").GetString() == kind
                            && r.GetProperty("type").GetString()!.EndsWith("+" + type) && r.GetProperty("rel").GetString() == rel)
                .Sum(r => r.GetProperty("n").GetInt64());
            Assert.Equal(3, N("Read", "Position", "Own"));
            Assert.Equal(3, N("Write", "Position", "Own"));
            Assert.Equal(3, N("Read", "Velocity", "Other"));
            Assert.Equal(3, N("Write", "Velocity", "Other"));
            Assert.Equal(6, N("Read", "Position", "OtherScan"));       // 3 entities × the 2 others each
            Assert.Equal(1, N("Write", "Position", "None"));
            Assert.Equal(0, N("Read", "Position", "Other"));
            Assert.Equal(0, N("Write", "Velocity", "Own"));
        }
    }
}
