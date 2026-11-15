using System;
using System.Threading;
using Fdp.Core.Jobs;
using Xunit;

namespace Fdp.Core.Tests.Jobs
{
    /// <summary>
    /// ⭐ The frame lane of <see cref="FdpWorkerPool"/>: a batch runs exactly once, inside ITS run.
    /// 📌 Found live (cc-battalion-stall, 2026-11-15): a late worker that had read the previous run's generation and work took a batch of that
    /// FINISHED run once the next run had published a larger batch count — the finished <c>ParallelEntities</c> runner indexed its emptied entity
    /// array (<c>IndexOutOfRangeException</c>), and the fault surfaced in the next, unrelated run (<c>LinearKinematicsSystem</c>), aborting the process.
    /// </summary>
    [Collection("FdpWorkerPool")]
    public class FdpWorkerPoolTests
    {
        private sealed class Work : FdpWorkerPool.IBatchWork
        {
            public volatile bool Open;
            public int[] Hits = new int[64];
            public int Stray;
            public void RunBatch(int batch)
            {
                if (!Open || batch >= Hits.Length) { Interlocked.Increment(ref Stray); return; }
                Interlocked.Increment(ref Hits[batch]);
                Thread.SpinWait(20);
            }
        }

        [Fact]
        public void ABatch_RunsOnce_InsideItsOwnRun_WhenRunsOfDifferentSizesFollowEachOther()
        {
            var pool = new FdpWorkerPool(Math.Max(8, Environment.ProcessorCount * 2));
            try
            {
                var small = new Work(); var big = new Work();
                for (int iter = 0; iter < 20000; iter++)
                {
                    var w = (iter & 1) == 0 ? small : big;
                    int n = w == small ? 2 : 40;
                    Array.Clear(w.Hits);
                    w.Open = true;
                    pool.Run(w, n);
                    w.Open = false;
                    for (int b = 0; b < n; b++)
                        Assert.True(w.Hits[b] == 1, $"run {iter}: batch {b} ran {w.Hits[b]} times");
                }
                Assert.Equal(0, small.Stray);
                Assert.Equal(0, big.Stray);
            }
            finally { pool.Dispose(); }
        }
    }
}
