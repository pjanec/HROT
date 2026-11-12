using System;
using System.Collections.Generic;

namespace Fdp.Core.Parallel
{
    /// <summary>
    /// ⭐ R-4207 (docs/DESIGN_Parallel_Simulation.md §7) — determinism is a MODE. <see cref="Deterministic"/>: the result does not depend on the
    /// thread count (fixed batch boundaries, per-batch buffers merged in batch order). <see cref="Throughput"/>: the same merge, but batches sized
    /// by the thread count (fewer, larger batches) — still CORRECT, not comparable byte for byte across machines. Chosen by what is running
    /// (stepped mode, Run-to, filming, regression, tests ⇒ Deterministic; continuous live play ⇒ Throughput), never by hand.
    /// </summary>
    public enum SimulationMode : byte { Deterministic = 0, Throughput = 1 }

    /// <summary>What a parallel body may not do itself and hands to the serial merge: a per-entity follow-up for the job's own serial step.</summary>
    public readonly struct Deferred
    {
        public readonly Entity Entity;
        public readonly int Code;
        public readonly float Value;
        public Deferred(Entity entity, int code, float value) { Entity = entity; Code = code; Value = value; }
    }

    /// <summary>
    /// ⭐ One batch's private outputs: a command buffer (writes to OTHER entities, structural changes, events — <see cref="EntityCommandBuffer.PublishEvent{T}"/>)
    /// and a deferred list (follow-ups the job applies serially). Pooled per batch index; 0 B once warm.
    /// </summary>
    public sealed class BatchContext
    {
        public readonly EntityCommandBuffer Commands = new(64);
        private Deferred[] _deferred = new Deferred[16];
        internal int DeferredCount;

        /// <summary>The batch's index (the merge order).</summary>
        public int Batch { get; internal set; }

        /// <summary>Hands <paramref name="e"/> to the job's serial step, in this batch's order.</summary>
        public void Defer(Entity e, int code = 0, float value = 0f)
        {
            if (DeferredCount == _deferred.Length) Array.Resize(ref _deferred, _deferred.Length * 2);
            _deferred[DeferredCount++] = new Deferred(e, code, value);
        }

        internal ref readonly Deferred DeferredAt(int i) => ref _deferred[i];
        internal void Reset() { DeferredCount = 0; Commands.Clear(); }
    }

    /// <summary>
    /// ⭐ A data-parallel per-entity job. <see cref="Execute"/> runs on many threads: it may READ anything and WRITE only the entity it is given
    /// (anything else — another entity, a structural change, an event — goes into <c>ctx.Commands</c> or <c>ctx.Defer</c>).
    /// <see cref="ApplyDeferred"/> runs SERIALLY on the calling thread after the loop, batch by batch, in order. A struct: no closure, no allocation.
    /// ⛔ A job's own fields are shared by every thread — read them, never write them in <see cref="Execute"/>.
    /// </summary>
    public interface IEntityBatchJob
    {
        void Execute(Entity e, BatchContext ctx);
        void ApplyDeferred(EntityRepository repo, in Deferred d);
    }

    /// <summary>
    /// ⭐ P-0 (design §5, §7.1) — the deterministic data-parallel loop: entities split into batches by INDEX (the boundaries depend on the entity
    /// count, never the thread count, in <see cref="SimulationMode.Deterministic"/>), the batches run on <see cref="FdpWorkerPool"/>, then — on the
    /// caller, in batch order — every batch's command buffer is played back, then every batch's deferred list is applied. ⇒ the result is the
    /// same at 1, 2, 4 or 8 threads. 0 B per steady run (pooled contexts, a runner cached per job type).
    /// </summary>
    public static class ParallelEntities
    {
        /// <summary>The mode the loops run in (set by whoever owns the run: the kernel from the time mode, the tests). Default Deterministic.</summary>
        public static SimulationMode Mode { get; set; } = SimulationMode.Deterministic;

        /// <summary>Entities per batch in Deterministic mode — fixed, so a batch is the same set of entities whatever the thread count.</summary>
        public const int DeterministicBatchSize = 32;

        /// <summary>For the merge-cost measurement (E-2): ticks spent in the last run's serial merge (playback + deferred).</summary>
        public static long LastMergeTicks { get; private set; }

        /// <summary>Runs <paramref name="job"/> over <paramref name="entities"/>[0 … <paramref name="count"/>).</summary>
        public static void Run<TJob>(EntityRepository repo, Entity[] entities, int count, in TJob job) where TJob : struct, IEntityBatchJob
            => Runner<TJob>.Instance.Run(repo, entities, count, job);

        /// <summary>Runs <paramref name="job"/> over every entity <paramref name="query"/> matches (collected first, in index order).</summary>
        public static void Run<TJob>(EntityRepository repo, EntityQuery query, in TJob job) where TJob : struct, IEntityBatchJob
        {
            var r = Runner<TJob>.Instance;
            int n = 0;
            foreach (var e in query)
            {
                if (n == r.Scratch.Length) Array.Resize(ref r.Scratch, Math.Max(64, r.Scratch.Length * 2));
                r.Scratch[n++] = e;
            }
            r.Run(repo, r.Scratch, n, job);
        }

        internal static int BatchSize(int count, int threads)
            => Mode == SimulationMode.Deterministic ? DeterministicBatchSize : Math.Max(DeterministicBatchSize, count / Math.Max(1, threads * 4));

        private sealed class Runner<TJob> : FdpWorkerPool.IBatchWork where TJob : struct, IEntityBatchJob
        {
            public static readonly Runner<TJob> Instance = new();
            public Entity[] Scratch = new Entity[64];
            private readonly List<BatchContext> _contexts = new();
            private TJob _job;
            private Entity[] _entities = Array.Empty<Entity>();
            private int _count, _batchSize;

            public void Run(EntityRepository repo, Entity[] entities, int count, in TJob job)
            {
                if (count <= 0) return;
                var pool = FdpWorkerPool.Shared;
                _job = job; _entities = entities; _count = count;
                _batchSize = BatchSize(count, pool.Threads);
                int batches = (count + _batchSize - 1) / _batchSize;
                while (_contexts.Count < batches) _contexts.Add(new BatchContext());
                for (int b = 0; b < batches; b++) { _contexts[b].Reset(); _contexts[b].Batch = b; }

                pool.Run(this, batches);

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int b = 0; b < batches; b++) _contexts[b].Commands.Playback(repo);
                for (int b = 0; b < batches; b++)
                {
                    var ctx = _contexts[b];
                    for (int i = 0; i < ctx.DeferredCount; i++) _job.ApplyDeferred(repo, in ctx.DeferredAt(i));
                }
                LastMergeTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                _entities = Array.Empty<Entity>();
            }

            public void RunBatch(int batch)
            {
                int from = batch * _batchSize, to = Math.Min(_count, from + _batchSize);
                var ctx = _contexts[batch];
                for (int i = from; i < to; i++) _job.Execute(_entities[i], ctx);
            }
        }
    }
}
