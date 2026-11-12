using System;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Toolkit.Diagnostics;
using Xunit;

namespace Fdp.Toolkits.Tests.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3427</c> (docs/DESIGN_Scale_Rig.md §6) — the world-query timer: a call is counted on its slot from any thread, a steady
    /// call allocates nothing (R-2084), and a reset zeroes the counts but keeps the names.
    /// </summary>
    [Collection("QueryTimings")]   // process-wide counters: never two of these at once
    public class QueryTimingsTests : IDisposable
    {
        // ⭐ R-4213 — the timer is OFF by default; these rails test the timer itself, so they switch it on for their own duration
        private readonly bool _wasEnabled = QueryTimings.Enabled;
        public QueryTimingsTests() => QueryTimings.Enabled = true;
        public void Dispose() => QueryTimings.Enabled = _wasEnabled;

        /// <summary>⭐ R-4213 (CE-4450 K-4) — off by default: with <c>HROT_QUERY_TIMINGS</c> unset, a host starts with the timer OFF, and an off
        /// timer counts nothing. 🔴 Red-proof: the default back to <c>!= "0"</c> ⇒ the default reads on.</summary>
        [Fact]
        public void R4213_TheTimerIsOffByDefault_AndOffItCountsNothing()
        {
            if (Environment.GetEnvironmentVariable("HROT_QUERY_TIMINGS") is { Length: > 0 }) return;   // someone set it for this run
            Assert.False(_wasEnabled);
            QueryTimings.Enabled = false;
            int slot = QueryTimings.Slot("test.off");
            QueryTimings.Reset();
            for (int i = 0; i < 10; i++) QueryTimings.End(slot, QueryTimings.Begin());
            Assert.DoesNotContain(QueryTimings.Snapshot().Rows, r => r.Name == "test.off" && r.Calls > 0);
        }

        /// <summary>
        /// ⭐ R-4209 (POC 3 T-1) — the SAMPLED timer: every call still counted exactly; the total time, measured on 1 call in 16 and scaled,
        /// within 30 % of timing every call (8 000 calls of a ~5 µs busy wait — ~500 samples); a steady sampled call allocates nothing.
        /// 🔴 Red-proof: the scale-up dropped ⇒ the sampled total is ~1/16 of the full one.
        /// </summary>
        [Fact]
        public void R4209_TheSampledTimer_CountsEveryCall_AndItsTimeIsWithinItsErrorOfTimingEveryCall()
        {
            int was = QueryTimings.SampleOneIn;
            try
            {
                double Run(int oneIn, string name)
                {
                    QueryTimings.SampleOneIn = oneIn;
                    int slot = QueryTimings.Slot(name);
                    QueryTimings.Reset();
                    for (int i = 0; i < 8000; i++)
                    {
                        long t = QueryTimings.Begin();
                        long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 200_000;   // ~5 µs
                        while (System.Diagnostics.Stopwatch.GetTimestamp() < until) { }
                        QueryTimings.End(slot, t);
                    }
                    var row = QueryTimings.Snapshot().Rows.Single(r => r.Name == name);
                    Assert.Equal(8000, row.Calls);   // exact, sampled or not
                    return row.TotalMs;
                }
                double full = Run(1, "test.sampling.full");
                double sampled = Run(16, "test.sampling.sampled");
                Assert.InRange(sampled / full, 0.7, 1.3);

                QueryTimings.SampleOneIn = 16;
                int s2 = QueryTimings.Slot("test.sampling.alloc");
                for (int i = 0; i < 100; i++) QueryTimings.End(s2, QueryTimings.Begin(), QueryTimings.Bytes());   // warm
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) QueryTimings.End(s2, QueryTimings.Begin(), QueryTimings.Bytes());
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally { QueryTimings.SampleOneIn = was; }
        }

        [Fact]
        public void CE3427_CallsFromManyThreads_AreAllCounted_OnTheirSlot()
        {
            int slot = QueryTimings.Slot("test.manyThreads");
            Assert.Equal(slot, QueryTimings.Slot("test.manyThreads"));   // one name, one slot
            QueryTimings.Reset();
            Parallel.For(0, 8, _ =>
            {
                for (int i = 0; i < 1000; i++) QueryTimings.End(slot, QueryTimings.Begin());
            });
            var row = QueryTimings.Snapshot().Rows.Single(r => r.Name == "test.manyThreads");
            Assert.Equal(8000, row.Calls);
            Assert.True(row.MaxUs >= row.AvgUs);
        }

        [Fact]
        public void CE3427_ASteadyCall_AllocatesNothing()
        {
            int slot = QueryTimings.Slot("test.alloc");
            int byType = QueryTimings.SlotFor("test.type", typeof(QueryTimingsTests));
            int byId = QueryTimings.SlotFor("test.id", 7u, this);
            void Steady()
            {
                for (int i = 0; i < 1000; i++)
                {
                    QueryTimings.End(slot, QueryTimings.Begin());
                    QueryTimings.End(QueryTimings.SlotFor("test.type", typeof(QueryTimingsTests)), QueryTimings.Begin());
                    QueryTimings.End(QueryTimings.SlotFor("test.id", 7u, this), QueryTimings.Begin());
                }
            }
            Steady();   // warm: this thread's buffer, the lookups' first use (176 B once, measured)
            long before = GC.GetAllocatedBytesForCurrentThread();
            Steady();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(byType, QueryTimings.SlotFor("test.type", typeof(QueryTimingsTests)));
            Assert.Equal(byId, QueryTimings.SlotFor("test.id", 7u, new object()));   // the label is read only when the slot is new
        }

        [Fact]
        public void CE3427_Reset_ZeroesTheCounts_AndKeepsTheSlot()
        {
            int slot = QueryTimings.Slot("test.reset");
            QueryTimings.End(slot, QueryTimings.Begin());
            QueryTimings.Reset();
            Assert.DoesNotContain(QueryTimings.Snapshot().Rows, r => r.Name == "test.reset");
            QueryTimings.End(slot, QueryTimings.Begin());
            Assert.Equal(1, QueryTimings.Snapshot().Rows.Single(r => r.Name == "test.reset").Calls);
        }
    }
}
