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
    public class QueryTimingsTests
    {
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
