using System.Threading;
using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Jobs;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ P-0 (docs/DESIGN_Parallel_Simulation.md §5, §7.1; the parallel experiment E-2) — the deterministic data-parallel loop: the same world, run at
/// 1 / 2 / 4 / 8 threads, ends byte for byte the same — own writes, command-buffer writes to another entity, created entities, and the deferred
/// serial step in batch order; 0 B per steady run; the merge's own cost measured.
/// 🔴 Red-proof: the merge in THREAD order (the old per-thread buffers) ⇒ the cross-entity last-writer and the deferred log differ across thread counts.
/// </summary>
[Collection("FdpWorkerPool")]
public sealed class ParallelEntitiesTests
{
    private readonly ITestOutputHelper _out;
    public ParallelEntitiesTests(ITestOutputHelper output) => _out = output;

    [ComponentId(503)] private struct Input { public int Seed; }
    [ComponentId(504)] private struct Counter { public int V; public int Written; }

    private struct TheJob : IEntityBatchJob
    {
        public EntityRepository Repo;  // read-only to the job (a reference, shared by every thread)
        public Entity Target;          // every 11th entity writes this one THROUGH the command buffer: last writer in batch order wins
        public List<int> Log;          // the deferred step's record (serial, so a plain list is fine)

        public void Execute(Entity e, int index, BatchContext ctx)
        {
            // ⭐ the parallel body reads only what nobody writes this run (Input) and writes only its own entity in place
            var repo = Repo;
            int seed = repo.GetComponentRO<Input>(e).Seed;
            int v = 0;
            for (int i = 0; i < 200; i++) v = unchecked(v * 31 + seed + i);   // some work
            ref var c = ref repo.GetComponentRW<Counter>(e);
            c.V = v;
            if (e.Index % 11 == 0) ctx.Commands.SetComponent(Target, new Counter { V = e.Index, Written = 1 });
            if (e.Index % 97 == 0) { var made = ctx.Commands.CreateEntity(); ctx.Commands.AddComponent(made, new Counter { V = -e.Index }); }
            if (e.Index % 13 == 0) ctx.Defer(e, 1, v);
        }

        public void ApplyDeferred(EntityRepository repo, in Deferred d) => Log.Add(d.Entity.Index);
    }

    private static EntityRepository World(int n, out Entity target, out Entity[] list)
    {
        var w = new EntityRepository();
        w.RegisterComponent<Input>(); w.RegisterComponent<Counter>();
        list = new Entity[n];
        for (int i = 0; i < n; i++)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new Input { Seed = i * 7919 });
            w.AddComponent(e, new Counter());
            list[i] = e;
        }
        target = w.CreateEntity(); w.AddComponent(target, new Counter());
        return w;
    }

    private static (string State, string Log) RunAt(int threads, SimulationMode mode, int n = 3000)
    {
        FdpWorkerPool.Resize(threads);
        ParallelEntities.Mode = mode;
        using var w = World(n, out var target, out var list);
        var log = new List<int>();
        var job = new TheJob { Repo = w, Target = target, Log = log };
        ParallelEntities.Run(w, list, n, job);
        var sb = new System.Text.StringBuilder();
        foreach (var e in w.Query().With<Counter>().Build()) sb.Append(e.Index).Append(':').Append(w.GetComponentRO<Counter>(e).V).Append(',');
        return (sb.ToString(), string.Join(",", log));
    }

    [Fact]
    public void P0_TheResult_IsTheSame_At1_2_4_8Threads_InDeterministicMode()
    {
        try
        {
            var one = RunAt(1, SimulationMode.Deterministic);
            foreach (int t in new[] { 2, 4, 8 })
            {
                var at = RunAt(t, SimulationMode.Deterministic);
                Assert.Equal(one.State, at.State);
                Assert.Equal(one.Log, at.Log);
            }
            Assert.NotEmpty(one.Log);
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); ParallelEntities.Mode = SimulationMode.Deterministic; }
    }

    [Fact]
    public void P0_ThroughputMode_IsStillCorrect_OwnWritesEqual()
    {
        try
        {
            var det = RunAt(4, SimulationMode.Deterministic);
            var thr = RunAt(4, SimulationMode.Throughput);
            Assert.Equal(det.State, thr.State);   // every command still applies; with one target the batch order keeps the same last writer here
            Assert.Equal(det.Log.Split(',').Length, thr.Log.Split(',').Length);
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); ParallelEntities.Mode = SimulationMode.Deterministic; }
    }

    [Fact]
    public void P0_ASteadyRun_AllocatesNothing_AndTheMergeIsCheap()
    {
        try
        {
            FdpWorkerPool.Resize(4);
            ParallelEntities.Mode = SimulationMode.Deterministic;
            using var w = World(3000, out var target, out var list);
            var log = new List<int>(4096);
            var job = new TheJob { Repo = w, Target = target, Log = log };
            for (int i = 0; i < 30; i++) { log.Clear(); ParallelEntities.Run(w, list, list.Length, job); }   // warm (long enough that the caller has BLOCKED in the pool's wait once — its one-time wait bookkeeping is not steady-state garbage)
            log.Clear();
            // ⭐ POC 3 — the CALLING thread's allocations (it runs batches and the whole merge). ⚠ It was the process-wide
            //   GC.GetTotalAllocatedBytes: 24–192 B from other test-host threads in 4 of 6 runs, on POC 1's own commit too — a flaky rail.
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { log.Clear(); ParallelEntities.Run(w, list, list.Length, job); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            double mergeUs = ParallelEntities.LastMergeTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency;
            _out.WriteLine($"20 runs over 3000 entities: {after - before} B allocated; last merge {mergeUs:F1} µs ({log.Count} deferred, {3000 / 11 + 1} commands)");
            Assert.Equal(0, after - before);
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    private sealed class SpinWork : FdpWorkerPool.IBatchWork
    {
        private readonly double _ms;
        public SpinWork(double ms) => _ms = ms;
        public void RunBatch(int batch) => Spin(_ms);
    }

    private static void Spin(double ms)
    {
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(ms * System.Diagnostics.Stopwatch.Frequency / 1000.0);
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until) { }
    }

    /// <summary>
    /// ⭐ POC 4 J-1 — the ONE pool's priority: while a BACKGROUND window keeps every worker busy (60 items × 20 ms, an egress scan / EQS
    /// window), a FRAME run (48 batches × 4 ms) is served by the workers as soon as each finishes its current item — the frame run finishes
    /// far sooner than the caller alone would (192 ms) and worker threads ran frame batches; the background window still completes.
    /// 🔴 Red-proof: workers that drain the background window before looking at the frame lane ⇒ no frame batch on a worker, ~192 ms.
    /// </summary>
    [Fact]
    public void J1_ABackgroundWindow_DelaysAFrameRun_ByAtMostOneItem()
    {
        FdpWorkerPool.Resize(3);
        try
        {
            var pool = FdpWorkerPool.Shared;
            bool bgDone = false;
            var bg = new Thread(() => bgDone = pool.TryRun(60, _ => Spin(20)));
            bg.Start();
            Thread.Sleep(40);                                   // the workers are inside background items now
            long before = pool.FrameBatchesOnWorkers;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pool.Run(new SpinWork(4.0), 48);
            double ms = sw.Elapsed.TotalMilliseconds;
            long onWorkers = pool.FrameBatchesOnWorkers - before;
            bg.Join();
            _out.WriteLine($"frame run {ms:F0} ms, {onWorkers} of 48 batches on workers; background done {bgDone}");
            Assert.True(onWorkers > 0, "no worker served the frame run while a background window was open");
            Assert.True(ms < 160, $"the frame run took {ms:F0} ms (the caller alone: ~192)");
            Assert.True(bgDone);
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    /// <summary>⭐ J-1 — the background lane's contract kept from the former ModuleHost pool: every index once, an item's exception
    /// rethrown on the caller, a second concurrent window refused (the caller then works it itself).</summary>
    [Fact]
    public void J1_TheBackgroundLane_RunsEveryIndexOnce_RethrowsAFault_AndRefusesASecondWindow()
    {
        FdpWorkerPool.Resize(4);
        try
        {
            var pool = FdpWorkerPool.Shared;
            var hits = new int[500];
            Assert.True(pool.TryRun(500, k => Interlocked.Increment(ref hits[k])));
            Assert.All(hits, h => Assert.Equal(1, h));
            Assert.ThrowsAny<Exception>(() => pool.TryRun(50, k => { if (k == 17) throw new InvalidOperationException("17"); }));

            using var inside = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var holder = new Thread(() => pool.TryRun(4, _ => { inside.Set(); release.Wait(); }));
            holder.Start();
            inside.Wait();
            Assert.False(pool.TryRun(3, _ => { }));          // the lane is held: refused at once
            release.Set();
            holder.Join();
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    [Fact]
    public void P0_AFailingBatch_Throws_OnTheCaller()
    {
        FdpWorkerPool.Resize(4);
        try
        {
            using var w = World(500, out _, out var list);
            Assert.ThrowsAny<Exception>(() => ParallelEntities.Run(w, list, list.Length, new Throws()));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    private struct Throws : IEntityBatchJob
    {
        public void Execute(Entity e, int index, BatchContext ctx) { if (e.Index == 333) throw new InvalidOperationException("boom"); }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

}
