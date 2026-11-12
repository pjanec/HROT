using Fdp.Core;
using Fdp.Core.Jobs;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ POC 3 T-5 — <see cref="EntityRepository.ParallelSync"/>: a replica synced with the per-table copies on the worker pool holds the same
/// data and the same chunk versions as one synced serially, at 1 / 2 / 4 / 8 threads, across frames of writes; a steady parallel sync allocates
/// no more than the serial one. 🔴 Red-proof: a batch that skips its table ⇒ the replicas differ.
/// </summary>
[Collection("FdpWorkerPool")]
public sealed class ParallelSyncTests
{
    [ComponentId(320)] private struct A { public int V; }
    [ComponentId(321)] private struct B { public long V; public float W; }
    [ComponentId(322)] private struct C { public double V; }

    private static EntityRepository World(out Entity[] es)
    {
        var w = new EntityRepository();
        w.RegisterComponent<A>(); w.RegisterComponent<B>(); w.RegisterComponent<C>();
        es = new Entity[3000];
        for (int i = 0; i < es.Length; i++)
        {
            es[i] = w.CreateEntity();
            w.AddComponent(es[i], new A { V = i });
            if (i % 2 == 0) w.AddComponent(es[i], new B { V = i * 7, W = i });
            if (i % 3 == 0) w.AddComponent(es[i], new C { V = i * 0.5 });
        }
        return w;
    }

    private static string Dump(EntityRepository r, Entity[] es)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in es)
        {
            sb.Append(r.HasComponent<A>(e) ? r.GetComponentRO<A>(e).V : -1).Append(',');
            sb.Append(r.HasComponent<B>(e) ? r.GetComponentRO<B>(e).V : -1).Append(',');
            sb.Append(r.HasComponent<C>(e) ? r.GetComponentRO<C>(e).V : -1).Append(';');
        }
        foreach (var t in new[] { r.GetComponentTable<A>(), (IComponentTable)r.GetComponentTable<B>(), r.GetComponentTable<C>() })
            sb.Append(t.GetVersionForEntity(0)).Append('/').Append(t.GetVersionForEntity(2999)).Append(' ');
        return sb.ToString();
    }

    [Fact]
    public void AReplicaSyncedInParallel_IsTheReplicaSyncedSerially_AtEveryThreadCount()
    {
        bool was = EntityRepository.ParallelSync;
        try
        {
            foreach (int threads in new[] { 1, 2, 4, 8 })
            {
                FdpWorkerPool.Resize(threads);
                using var live = World(out var es);
                using var serial = new EntityRepository();
                using var parallel = new EntityRepository();
                for (int f = 0; f < 4; f++)
                {
                    live.Tick();
                    for (int i = f; i < es.Length; i += 5) live.GetComponentRW<A>(es[i]).V += f + 1;
                    if (f == 2) for (int i = 0; i < es.Length; i += 4) if (live.HasComponent<B>(es[i])) live.GetComponentRW<B>(es[i]).W = -f;
                    EntityRepository.ParallelSync = false; serial.SyncFrom(live);
                    EntityRepository.ParallelSync = true; parallel.SyncFrom(live);
                    Assert.Equal(Dump(serial, es), Dump(parallel, es));
                }
                // the parallel path adds no garbage of its own (the serial sync's own, if any, is the baseline)
                EntityRepository.ParallelSync = false;
                for (int k = 0; k < 3; k++) { live.Tick(); serial.SyncFrom(live); }
                long s0 = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 10; k++) { live.Tick(); serial.SyncFrom(live); }
                long serialBytes = GC.GetAllocatedBytesForCurrentThread() - s0;
                EntityRepository.ParallelSync = true;
                for (int k = 0; k < 3; k++) { live.Tick(); parallel.SyncFrom(live); }   // warm
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 0; k < 10; k++) { live.Tick(); parallel.SyncFrom(live); }
                Assert.True(GC.GetAllocatedBytesForCurrentThread() - before <= serialBytes, $"parallel sync allocated more than the serial {serialBytes} B");
            }
        }
        finally
        {
            EntityRepository.ParallelSync = was;
            FdpWorkerPool.Resize(FdpWorkerPool.Budget());
        }
    }
}
