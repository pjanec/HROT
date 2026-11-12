using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Jobs;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐⭐ N-10 (CE-4472; R-4214 ④⑤, docs/DESIGN_Parallel_Simulation.md §18) — the per-entity rule IN THE REPOSITORY, one rail per path:
/// <list type="bullet">
/// <item>a read of ANOTHER entity's declared type during a phase is served from the LAST-TICK view (never a value written this phase) ⇒ the result
/// is the same at 1 / 2 / 4 / 8 threads and equals the serial "everyone reads last tick" answer;</item>
/// <item>a bus publish on the phase's world goes into the batch's buffer and lands in BATCH order (F-2);</item>
/// <item>a component ADD, a REMOVE, a DESTROY and a CREATE in the phase are deferred and merged in batch order (F-6 / F-8);</item>
/// <item>a steady run with the rule in place allocates nothing on the calling thread (R-2084).</item>
/// </list>
/// 🔴 Red-proofs: the cross read served live (no last-tick view) ⇒ the ring result depends on the thread count; a publish not deferred ⇒ the event
/// order follows the threads.
/// </summary>
[Collection("FdpWorkerPool")]
public sealed class PerEntityRuleTests
{
    private readonly ITestOutputHelper _out;
    public PerEntityRuleTests(ITestOutputHelper output) => _out = output;

    [ComponentId(508)] private struct Val { public long V; }
    [ComponentId(509)] private struct Tag { public int From; }
    [ComponentId(510)] private struct Doomed { public int X; }
    [EventId(9511)] private struct Ping { public int From; }

    /// <summary>Each entity reads its LEFT neighbour's <see cref="Val"/> (another entity, declared) and writes its own = neighbour + own index.
    /// Read live, a neighbour in an earlier slot of the same batch has already moved on ⇒ the answer would depend on the batching.</summary>
    private struct RingJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public Entity[] Ring;
        public int N;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            var left = Ring[(index + N - 1) % N];
            long l = Repo.GetComponentRO<Val>(left).V;
            for (int k = 0; k < 50; k++) l = unchecked(l * 31 + k);   // some work, so batches overlap
            Repo.GetComponentRW<Val>(e).V = l + index;
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>Plain repository calls inside the phase — publish, add, remove, destroy, create — all deferred into the batch.</summary>
    private struct StructuralJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            Repo.Bus.Publish(new Ping { From = e.Index });
            if (index % 3 == 0) Repo.AddComponent(e, new Tag { From = index });      // own, absent ⇒ a structural add: deferred
            if (index % 5 == 0) Repo.RemoveComponent<Doomed>(e);                      // deferred
            if (index % 7 == 0) { var made = Repo.CreateEntity(); Repo.AddComponent(made, new Tag { From = -index }); }   // placeholder + its add
            if (index == 1) Repo.DestroyEntity(e);                                     // deferred
            if (Repo.HasComponent<Tag>(e)) throw new InvalidOperationException("the add was applied inside the phase");
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    private static EntityRepository World(int n, out Entity[] list)
    {
        var w = new EntityRepository();
        w.RegisterComponent<Val>(); w.RegisterComponent<Tag>(); w.RegisterComponent<Doomed>();
        w.Bus.Register<Ping>();
        list = new Entity[n];
        for (int i = 0; i < n; i++)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new Val { V = i * 7 + 1 });
            w.AddComponent(e, new Doomed { X = i });
            list[i] = e;
        }
        return w;
    }

    private static string RingAt(int threads, int n = 2000)
    {
        FdpWorkerPool.Resize(threads);
        using var w = World(n, out var list);
        ParallelReadCheck.DeclareUnmanaged<Val>();
        w.CaptureLastTick();   // the kernel takes it before the Simulation phase; here the test does
        ParallelEntities.Run(w, list, n, new RingJob { Repo = w, Ring = list, N = n });
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(w.GetComponentRO<Val>(list[i]).V).Append(',');
        return sb.ToString();
    }

    [Fact]
    public void N10_ACrossRead_IsTheLastTick_TheSameAt1_2_4_8Threads_AndEqualToTheSerialAnswer()
    {
        try
        {
            var one = RingAt(1);
            // the serial answer, computed from the PRE-run values
            const int n = 2000;
            var expect = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                long l = ((i + n - 1) % n) * 7 + 1;
                for (int k = 0; k < 50; k++) l = unchecked(l * 31 + k);
                expect.Append(l + i).Append(',');
            }
            Assert.Equal(expect.ToString(), one);
            foreach (int t in new[] { 2, 4, 8 }) Assert.Equal(one, RingAt(t));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); ParallelReadCheck.ClearDeclarations(); }
    }

    private static (string Events, string State) StructuralAt(int threads, int n = 1500)
    {
        FdpWorkerPool.Resize(threads);
        using var w = World(n, out var list);
        ParallelEntities.Run(w, list, n, new StructuralJob { Repo = w });
        w.Bus.SwapBuffers();
        var ev = new System.Text.StringBuilder();
        foreach (var p in w.Bus.Read<Ping>()) ev.Append(p.From).Append(',');
        var st = new System.Text.StringBuilder();
        foreach (var e in w.Query().With<Tag>().Build()) st.Append(e.Index).Append('=').Append(w.GetComponentRO<Tag>(e).From).Append(',');
        st.Append('|');
        for (int i = 0; i < n; i++) st.Append(w.IsAlive(list[i]) ? (w.HasComponent<Doomed>(list[i]) ? 'D' : 'r') : 'x');
        return (ev.ToString(), st.ToString());
    }

    [Fact]
    public void N10_PublishAddRemoveCreateDestroy_InAPhase_AreDeferred_AndLandInBatchOrder()
    {
        try
        {
            var one = StructuralAt(1);
            // every entity published once, in entity (= batch) order
            var expectEvents = new System.Text.StringBuilder();
            using (var probe = World(1500, out var l0)) for (int i = 0; i < 1500; i++) expectEvents.Append(l0[i].Index).Append(',');
            Assert.Equal(expectEvents.ToString(), one.Events);
            Assert.Contains("=-7,", one.State);       // a created entity got its component
            Assert.Equal('x', one.State[one.State.IndexOf('|') + 2]);   // entity 1 destroyed
            Assert.Equal('r', one.State[one.State.IndexOf('|') + 1]);   // entity 0: Doomed removed (0 % 5 == 0)
            foreach (int t in new[] { 2, 4, 8 }) Assert.Equal(one, StructuralAt(t));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    [Fact]
    public void N10_ASteadyRun_WithTheRuleInPlace_AllocatesNothing()
    {
        try
        {
            FdpWorkerPool.Resize(4);
            using var w = World(3000, out var list);
            ParallelReadCheck.DeclareUnmanaged<Val>();
            var job = new RingJob { Repo = w, Ring = list, N = list.Length };
            for (int i = 0; i < 30; i++) { w.CaptureLastTick(); ParallelEntities.Run(w, list, list.Length, job); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { w.CaptureLastTick(); ParallelEntities.Run(w, list, list.Length, job); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            _out.WriteLine($"20 ticks (last-tick capture + phase) over 3000 entities: {after - before} B");
            Assert.Equal(0, after - before);
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); ParallelReadCheck.ClearDeclarations(); }
    }
}
