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
    [EventId(9512)] private struct Unregistered { public int From; }

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

    /// <summary>Reads its left neighbour's <see cref="Doomed"/> (nobody writes it in the phase) — declared STABLE by the job (N-11, D-6).</summary>
    private struct StableJob : IEntityBatchJob, IStableReads
    {
        public EntityRepository Repo; public Entity[] Ring; public int N;
        public void DeclareStableReads(PhaseReadSet reads) => reads.Unmanaged<Doomed>();
        public void Execute(Entity e, int index, BatchContext ctx)
            => Repo.GetComponentRW<Val>(e).V = Repo.GetComponentRO<Doomed>(Ring[(index + N - 1) % N]).X;
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>The same read with no declaration.</summary>
    private struct UndeclaredJob : IEntityBatchJob
    {
        public EntityRepository Repo; public Entity[] Ring; public int N;
        public void Execute(Entity e, int index, BatchContext ctx)
            => Repo.GetComponentRW<Val>(e).V = Repo.GetComponentRO<Doomed>(Ring[(index + N - 1) % N]).X;
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐ N-11 (D-6) — with the checks on (<c>HROT_READ_CHECK=1</c>): a cross read of a type the JOB declares stable is served live
    /// (no throw, the neighbour's value); the same read undeclared throws. 🔴 Red-proof: the stable test removed from the read path ⇒ the
    /// declared job throws too.</summary>
    [SkippableFact]
    public void N11_AJobsStableRead_IsServedLive_AnUndeclaredOneThrows_ChecksOn()
    {
        Skip.IfNot(ParallelReadCheck.Compiled && !ParallelReadCheck.Survey, "run with HROT_READ_CHECK=1");
        try
        {
            FdpWorkerPool.Resize(2);
            using var w = World(256, out var list);
            ParallelEntities.Run(w, list, list.Length, new StableJob { Repo = w, Ring = list, N = list.Length });
            Assert.Equal((long)(list.Length - 1), w.GetComponentRO<Val>(list[0]).V);   // the left neighbour of 0 is the last: X = its index
            Assert.ThrowsAny<Exception>(() => ParallelEntities.Run(w, list, list.Length, new UndeclaredJob { Repo = w, Ring = list, N = list.Length }));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    /// <summary>A "squad" job: every 4th entity leads the next three — it owns them (F-11), writes each one's <see cref="Val"/> through the
    /// upsert a squad's step uses (<c>AddComponent</c> on a present component) and reads it back.</summary>
    private struct SquadLikeJob : IEntityBatchJob
    {
        public EntityRepository Repo; public Entity[] All; public bool Own;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            int at = index * 4;
            if (Own) for (int k = 1; k <= 3; k++) ParallelReadCheck.OwnAlso(All[at + k]);
            long seen = 0;
            for (int k = 1; k <= 3; k++)
            {
                Repo.AddComponent(All[at + k], new Val { V = index * 10 + k });   // an upsert of a member's present component
                seen += Repo.GetComponentRO<Val>(All[at + k]).V;                 // owned ⇒ the write is already there
            }
            Repo.GetComponentRW<Val>(e).V = seen;
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    private static string SquadLikeAt(int threads, bool own)
    {
        FdpWorkerPool.Resize(threads);
        using var w = World(1600, out var all);
        var leaders = new Entity[all.Length / 4];
        for (int i = 0; i < leaders.Length; i++) leaders[i] = all[i * 4];
        ParallelEntities.Run(w, leaders, leaders.Length, new SquadLikeJob { Repo = w, All = all, Own = own });
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < all.Length; i++) sb.Append(w.GetComponentRO<Val>(all[i]).V).Append(',');
        return sb.ToString();
    }

    /// <summary>⭐ F-11 (CE-4474; R-4212 ③) — a job that OWNS other entities (<see cref="ParallelReadCheck.OwnAlso"/>: a squad's members) reads and
    /// writes them directly — its own write is visible to it in the same Execute — and the result is the same at 1 / 2 / 4 / 8 threads; the
    /// ownership ends with the entity (the next leader's members are not the previous one's). 🔴 Red-proof: <c>Own = false</c> (or the
    /// <c>OwnsAlso</c> test removed from <c>IsOwnPartOfCurrent</c>) ⇒ the upsert is deferred as a command ⇒ the read-back sees the old value.</summary>
    [Fact]
    public void F11_AJobOwnsItsMembers_ReadsAndWritesThemDirectly_TheSameAtAnyThreadCount()
    {
        try
        {
            var one = SquadLikeAt(1, own: true);
            var first = one.Split(',');
            Assert.Equal((0 * 10 + 1) + (0 * 10 + 2) + (0 * 10 + 3), long.Parse(first[0]));   // leader 0 saw its three writes
            Assert.Equal(7 * 10 + 2, long.Parse(first[7 * 4 + 2]));                          // leader 7's second member holds its write
            foreach (int t in new[] { 2, 4, 8 }) Assert.Equal(one, SquadLikeAt(t, own: true));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    /// <summary>⭐ M-7 (CE-4486) — a phase publishing an event its world never REGISTERED behaves as a serial publish: the stream is made at the
    /// publish and the event arrives (in batch order). 📐 Measured: the squad phase's publish threw at the batch's playback ("not registered … Cannot
    /// playback command") where the serial publish worked. 🔴 Red-proof: the stream not made on the deferred path ⇒ the playback throws.</summary>
    [Fact]
    public void M7_APhasePublishesAnUnregisteredEvent_LikeASerialPublish()
    {
        try
        {
            FdpWorkerPool.Resize(2);
            using var w = World(100, out var list);
            ParallelEntities.Run(w, list, list.Length, new PublishUnregisteredJob { Repo = w });
            w.Bus.SwapBuffers();
            var got = w.Bus.Read<Unregistered>();
            Assert.Equal(list.Length, got.Length);
            for (int i = 0; i < got.Length; i++) Assert.Equal(i, got[i].From);   // batch order = entity order
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    private struct PublishUnregisteredJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public void Execute(Entity e, int index, BatchContext ctx) => Repo.Bus.Publish(new Unregistered { From = index });
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐ M-7 (CE-4486) — a type declared AFTER this tick's view was taken is still served from the tick start: the phase takes the view
    /// again (the declaration epoch moved). 📐 Measured (the read-check survey): movement declares its neighbour reads just before its first run,
    /// so on the first frame they were read LIVE. On one thread the batches run in order, so a live read sees the left neighbour's NEW value.
    /// 🔴 Red-proof: the epoch test dropped from <c>EnsureLastTickView</c> ⇒ entity i reads i−1's in-phase write.</summary>
    [Fact]
    public void M7_ATypeDeclaredAfterTheViewWasTaken_IsReadFromTheTickStart()
    {
        try
        {
            FdpWorkerPool.Resize(1);
            ParallelReadCheck.ClearDeclarations();
            using var w = World(64, out var list);
            for (int i = 0; i < list.Length; i++) w.AddComponent(list[i], new Tag { From = i });
            ParallelReadCheck.DeclareUnmanaged<Val>();
            w.CaptureLastTick();                                          // this tick's view — without Tag
            ParallelReadCheck.DeclareUnmanaged<Tag>();                    // declared after it (as movement does on its first frame)
            ParallelEntities.Run(w, list, list.Length, new LeftTagJob { Repo = w, List = list });
            for (int i = 1; i < list.Length; i++) Assert.Equal(i - 1, w.GetComponentRO<Val>(list[i]).V);   // the left one's tick-start Tag
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); ParallelReadCheck.ClearDeclarations(); }
    }

    private struct LeftTagJob : IEntityBatchJob
    {
        public EntityRepository Repo; public Entity[] List;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            Repo.GetComponentRW<Tag>(e).From = 1000 + index;                                   // its own: written in the phase
            if (index > 0) Repo.GetComponentRW<Val>(e).V = Repo.GetComponentRO<Tag>(List[index - 1]).From;   // the left one's
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐ M-6 (CE-4485) — owning an entity owns its PARTS: a job that claims another entity (<see cref="ParallelReadCheck.OwnAlso"/>: a weapon
    /// system the unit controls) writes that entity's part (its turret) directly — visible in the same Execute — the same at 1 / 2 / 4 / 8 threads.
    /// 🔴 Red-proof: the owner test dropped from <c>IsOwnPartOfCurrent</c> ⇒ the part's write is deferred ⇒ the read-back sees the old value.</summary>
    [Fact]
    public void M6_AJobOwnsThePartsOfWhatItClaims_AndWritesThemDirectly()
    {
        try
        {
            string one = ClaimedPartsAt(1);
            Assert.StartsWith("7,", one);                                         // owner 0 read back its write to the claimed entity's part
            foreach (int t in new[] { 2, 4, 8 }) Assert.Equal(one, ClaimedPartsAt(t));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    private static string ClaimedPartsAt(int threads)
    {
        FdpWorkerPool.Resize(threads);
        using var w = World(300, out var owners);
        w.RegisterComponent<Fdp.Tests.EntityCensusTests.CensusPart>();
        var claimed = new Entity[owners.Length];
        var parts = new Entity[owners.Length];
        for (int i = 0; i < owners.Length; i++)
        {
            claimed[i] = w.CreateEntity();                                        // the weapon system the owner controls
            parts[i] = w.CreateEntity();                                          // … and its turret part
            w.AddComponent(parts[i], new Fdp.Tests.EntityCensusTests.CensusPart { ParentEntity = claimed[i] });
            w.AddComponent(parts[i], new Val { V = -1 });
        }
        ParallelEntities.Run(w, owners, owners.Length, new ClaimedPartJob { Repo = w, Claimed = claimed, Parts = parts });
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < owners.Length; i++) sb.Append(w.GetComponentRO<Val>(owners[i]).V).Append(',');
        return sb.ToString();
    }

    private struct ClaimedPartJob : IEntityBatchJob
    {
        public EntityRepository Repo; public Entity[] Claimed, Parts;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            ParallelReadCheck.OwnAlso(Claimed[index]);
            Repo.AddComponent(Parts[index], new Val { V = index + 7 });          // an upsert of the claimed entity's part
            Repo.GetComponentRW<Val>(e).V = Repo.GetComponentRO<Val>(Parts[index]).V;   // owned ⇒ already there
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐⭐ E-3 (CE-4509) — "own entity: direct access" holds for a managed component a job ADDS to its own entity: the add is
    /// deferred to the merge (structural), and the job reads its own pending value until then — a read-modify-write three times in one Execute
    /// counts three, at 1 / 2 / 4 / 8 threads. 📐 Measured before: SubordinateOrders.Record on a commander that held none kept only the
    /// LAST order of the tick (the squad drills under parallel brains re-sent the others). 🔴 Red-proof: drop the own-pending read
    /// (EntityRepository.OwnPendingManaged / HasManagedCore) ⇒ every owner counts 1.</summary>
    [Fact]
    public void E3_AJobReadsItsOwnPendingManagedAdd_AReadModifyWriteKeepsEveryStep()
    {
        try
        {
            foreach (int t in new[] { 1, 2, 4, 8 }) Assert.Equal(300 * 3, OwnPendingCountAt(t));
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    [ComponentId(502)] public sealed class Tally { public int N; }   // a test-local id (502: free in production and in this project)

    /// <summary>⭐ E-3 (CE-4509) — the unmanaged twin: an "add if absent, else build on the pending add" helper (ReportQueue.Enqueue's shape)
    /// called three times in one Execute keeps all three, at 1 / 2 / 4 / 8 threads. 🔴 Red-proof: TryGetOwnPendingAdd always false ⇒ 1.</summary>
    [Fact]
    public void E3_AnAddIfAbsentHelper_BuildsOnItsOwnPendingAdd()
    {
        try
        {
            foreach (int t in new[] { 1, 2, 4, 8 })
            {
                FdpWorkerPool.Resize(t);
                using var w = World(300, out var owners);
                ParallelEntities.Run(w, owners, owners.Length, new PendingAddJob { Repo = w });
                long sum = 0;
                foreach (var e in owners) sum += w.HasComponent<Tag>(e) ? w.GetComponentRO<Tag>(e).From : 0;
                Assert.Equal(300L * 3, sum);
            }
        }
        finally { FdpWorkerPool.Resize(FdpWorkerPool.Budget()); }
    }

    private struct PendingAddJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            for (int k = 0; k < 3; k++)
            {
                if (Repo.HasComponent<Tag>(e)) { Repo.GetComponentRW<Tag>(e).From++; continue; }
                var fresh = Repo.TryGetOwnPendingAdd<Tag>(e, out var pending) ? pending : default;
                fresh.From++;
                Repo.AddComponent(e, fresh);                                     // deferred: the pending add the next call builds on
            }
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }   // a test-local id (502: free in production and in this project)

    private static int OwnPendingCountAt(int threads)
    {
        FdpWorkerPool.Resize(threads);
        using var w = World(300, out var owners);
        w.RegisterManagedComponent<Tally>();
        ParallelEntities.Run(w, owners, owners.Length, new TallyJob { Repo = w });
        int sum = 0;
        foreach (var e in owners) sum += w.HasManagedComponent<Tally>(e) ? ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<Tally>(e).N : 0;
        return sum;
    }

    private struct TallyJob : IEntityBatchJob
    {
        public EntityRepository Repo;
        public void Execute(Entity e, int index, BatchContext ctx)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = Repo.HasManagedComponent<Tally>(e) ? ((Fdp.ModuleHost.Abstractions.ISimulationView)Repo).GetManagedComponentRO<Tally>(e).N : 0;
                Repo.SetManagedComponent(e, new Tally { N = n + 1 });          // the first is an ADD: deferred to the merge
            }
        }
        public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
    }

    /// <summary>⭐ N-15 (CE-4477) — a phase reads THIS tick's view even on a world no kernel captures: a view taken last tick, then the world ticks
    /// and its rows change, then a phase runs with no capture of its own ⇒ the cross reads see the NEW rows (the phase takes the view — once a tick).
    /// 📌 Measured: the editor's preview world kept its FIRST view; entities spawned later were missing and read live ⇒ a thread-count-dependent hash.
    /// 🔴 Red-proof: drop <c>EnsureLastTickView</c> from <c>ParallelEntities.Run</c> ⇒ the ring reads last tick's stale values ⇒ red.</summary>
    [Fact]
    public void N15_APhaseReadsThisTicksView_EvenWhenNoKernelTookIt()
    {
        try
        {
            FdpWorkerPool.Resize(2);
            using var w = World(256, out var list);
            ParallelReadCheck.DeclareUnmanaged<Val>();
            w.CaptureLastTick();                                                    // last tick's view
            w.Tick();                                                               // a new tick …
            for (int i = 0; i < list.Length; i++) w.GetComponentRW<Val>(list[i]).V = 1000 + i;   // … whose rows changed before the phase
            ParallelEntities.Run(w, list, list.Length, new RingJob { Repo = w, Ring = list, N = list.Length });
            long l = 1000 + (list.Length - 1);                                     // entity 0 reads its left neighbour (the last) at the NEW value
            for (int k = 0; k < 50; k++) l = unchecked(l * 31 + k);
            Assert.Equal(l + 0, w.GetComponentRO<Val>(list[0]).V);
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
