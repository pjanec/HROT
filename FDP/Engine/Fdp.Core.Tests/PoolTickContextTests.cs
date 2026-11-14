using System;
using System.Threading;
using Fdp.Core;
using Fdp.Core.Jobs;
using Xunit;
using Pos = Fdp.Tests.RecorderDeltaLogicTests.Position;

namespace Fdp.Tests
{
    /// <summary>
    /// ⭐ N-2 (CE-4464, F-1; docs/DESIGN_Threaded_Work_Inventory.md §7.2) — the ONE pool carries the tick context and the current entity into every
    /// batch: a per-entity job running on a pool WORKER sees the reader / context of the system that started it (the tracer, the world guard and the
    /// census read it) and its own entity as the current one; inside a parallel phase an access with NO current entity throws.
    /// Runs with <c>HROT_ACCESS_TRACE=1 HROT_READ_CHECK=1</c> (both read once per process):
    /// <c>HROT_ACCESS_TRACE=1 HROT_READ_CHECK=1 HROT_ACCESS_TRACE_OUT=/tmp/n2.json dotnet test FDP/Engine/Fdp.Core.Tests --no-build --filter FullyQualifiedName~PoolTickContextTests</c>.
    /// 🔴 Red-proof (run): the worker's <c>AccessTrace.SwapFlow(_frameFlow)</c> removed ⇒ batches on workers report reader "(none)" and the first
    /// assertion fails.
    /// </summary>
    public class PoolTickContextTests
    {
        private struct RecordJob : IEntityBatchJob
        {
            public int[] Readers; public int[] Currents; public int[] Threads; public bool[] Threw; public int ClearCurrentAt; public EntityRepository Repo;
            public void Execute(Entity e, int index, BatchContext ctx)
            {
                Readers[index] = AccessTrace.CurrentReaderId;
                Currents[index] = ParallelReadCheck.Current;
                Threads[index] = Environment.CurrentManagedThreadId;
                if (index == ClearCurrentAt)
                {
                    ParallelReadCheck.Current = -1;   // a defective job: work not attributed to an entity
                    try { _ = Repo.GetComponentRO<Pos>(e); } catch (InvalidOperationException) { Threw[index] = true; }
                    ParallelReadCheck.Current = e.Index;
                }
                Thread.SpinWait(200);   // long enough that the workers take batches too
            }
            public void ApplyDeferred(EntityRepository repo, in Deferred d) { }
        }

        [SkippableFact]
        public void APoolWorker_SeesTheStartersReaderAndContext_AndItsOwnEntity_AndNoCurrentEntityThrows()
        {
            Skip.IfNot(AccessTrace.On && ParallelReadCheck.Compiled, "run with HROT_ACCESS_TRACE=1 HROT_READ_CHECK=1");
            FdpWorkerPool.Resize(4);
            using var w = new EntityRepository();
            w.RegisterComponent<Pos>();
            const int N = 2048;
            var es = new Entity[N];
            for (int i = 0; i < N; i++) { es[i] = w.CreateEntity(); w.AddComponent(es[i], new Pos { X = i }); }
            var job = new RecordJob { Readers = new int[N], Currents = new int[N], Threads = new int[N], Threw = new bool[N], ClearCurrentAt = 1500, Repo = w };
            int expectedReader;
            long before = FdpWorkerPool.Shared.FrameBatchesOnWorkers;
            using (AccessTrace.Scope("system:N2Rail", TraceContext.Tick))
            {
                expectedReader = AccessTrace.CurrentReaderId;
                ParallelEntities.Run(w, es, N, job);
            }
            Assert.True(FdpWorkerPool.Shared.FrameBatchesOnWorkers > before, "no batch ran on a worker — the rail proves nothing");
            int caller = Environment.CurrentManagedThreadId, onWorkers = 0;
            for (int i = 0; i < N; i++)
            {
                Assert.Equal(expectedReader, job.Readers[i]);
                Assert.Equal(es[i].Index, job.Currents[i]);
                if (job.Threads[i] != caller) onWorkers++;
            }
            Assert.True(onWorkers > 0);
            Assert.True(job.Threw[1500], "an access with no current entity inside a parallel phase must throw");
            Assert.False(ParallelReadCheck.Active);          // the phase ends with the batch
            Assert.Equal(-1, ParallelReadCheck.Current);
        }
    }
}
