using System;
using System.Collections.Generic;

namespace Fdp.Core.Jobs
{
    /// <summary>
    /// ⭐ R-4207 (docs/DESIGN_Parallel_Simulation.md §7) — determinism is a MODE. <see cref="Deterministic"/>: the result does not depend on the
    /// thread count (fixed batch boundaries, per-batch buffers merged in batch order). <see cref="Throughput"/>: the same merge, but batches sized
    /// by the thread count (fewer, larger batches) — still CORRECT, not comparable byte for byte across machines. Chosen by what is running
    /// (stepped mode, Run-to, filming, regression, tests ⇒ Deterministic; continuous live play ⇒ Throughput), never by hand.
    /// </summary>
    public enum SimulationMode : byte { Deterministic = 0, Throughput = 1 }

    /// <summary>
    /// ⭐ POC 2 Q-4 — the Debug guard for a READ-ONLY parallel body (the brains' pre-score): while <see cref="ReadOnly"/> is set on a thread,
    /// a Debug build throws on any writable component reference taken there (<c>NativeChunkTable.GetRefRW</c>). ⭐ It proves the premise the
    /// parallel scoring rests on — "scoring only reads" — on every rail that runs it. Release: never checked.
    /// </summary>
    public static class ParallelGuard
    {
        [ThreadStatic] private static bool t_readOnly;
        /// <summary>This thread is inside a read-only parallel body.</summary>
        public static bool ReadOnly { get => t_readOnly; set => t_readOnly = value; }
    }

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
        internal void Reset()
        {
            DeferredCount = 0; _laterCount = 0; Commands.Clear();
            Array.Clear(_ownManaged, 0, _ownManagedCount); _ownManagedCount = 0;
            Array.Clear(_ownUnmanaged, 0, _ownUnmanagedCount); _ownUnmanagedCount = 0;
        }

        // ⭐⭐ E-3 (CE-4509) — "own entity: direct access" holds for a managed component the job ADDS to its own entity: the add is structural,
        //   so it is deferred to the merge, and the job reads its own pending value until then (EntityRepository's managed read path).
        //   📐 Measured before: SubordinateOrders.Record's read-modify-write on a commander that held none yet saw "none" at every order of
        //   the tick, so only the last member's entry survived the merge and the plays re-sent the others' orders (the squad drills under
        //   parallel brains: an order counted twice). Reused: 0 B once warm.
        private (int Index, int TypeId, object? Value)[] _ownManaged = new (int, int, object?)[4];
        private int _ownManagedCount;

        internal void RememberOwnManaged(int index, int typeId, object? value)
        {
            for (int i = 0; i < _ownManagedCount; i++)
                if (_ownManaged[i].Index == index && _ownManaged[i].TypeId == typeId) { _ownManaged[i].Value = value; return; }
            if (_ownManagedCount == _ownManaged.Length) Array.Resize(ref _ownManaged, _ownManaged.Length * 2);
            _ownManaged[_ownManagedCount++] = (index, typeId, value);
        }

        // ⭐ E-3 (CE-4509) — the same for an UNMANAGED add to the job's own entity (boxed: an add happens once, never every frame); read back with
        //   EntityRepository.TryGetOwnPendingAdd — never by ref (the value is not in the world's storage until the merge).
        private (int Index, int TypeId, object Value)[] _ownUnmanaged = new (int, int, object)[4];
        private int _ownUnmanagedCount;

        internal void RememberOwnUnmanaged(int index, int typeId, object value)
        {
            for (int i = 0; i < _ownUnmanagedCount; i++)
                if (_ownUnmanaged[i].Index == index && _ownUnmanaged[i].TypeId == typeId) { _ownUnmanaged[i].Value = value; return; }
            if (_ownUnmanagedCount == _ownUnmanaged.Length) Array.Resize(ref _ownUnmanaged, _ownUnmanaged.Length * 2);
            _ownUnmanaged[_ownUnmanagedCount++] = (index, typeId, value);
        }

        internal bool TryOwnUnmanaged(int index, int typeId, out object? value)
        {
            for (int i = 0; i < _ownUnmanagedCount; i++)
                if (_ownUnmanaged[i].Index == index && _ownUnmanaged[i].TypeId == typeId) { value = _ownUnmanaged[i].Value; return true; }
            value = null;
            return false;
        }

        internal bool TryOwnManaged(int index, int typeId, out object? value)
        {
            for (int i = 0; i < _ownManagedCount; i++)
                if (_ownManaged[i].Index == index && _ownManaged[i].TypeId == typeId) { value = _ownManaged[i].Value; return true; }
            value = null;
            return false;
        }

        // ⭐ N-11 / N-12 (CE-4473 / CE-4474; R-4212 ②) — FOLLOW-UPS a helper inside the job needs done serially (a callout to the squad, an order
        //   to a member): played after the phase in batch order, after the command buffers and before the job's own deferred step. The handler
        //   is a long-lived singleton; the record is three values — no closure, 0 B once warm.
        private (IPhaseFollowUp Handler, Entity Entity, int Code, long Arg)[] _later = new (IPhaseFollowUp, Entity, int, long)[8];
        private int _laterCount;

        /// <summary>Queues <paramref name="handler"/>.Apply(repo, e, code, arg) for the serial step after the phase, in this batch's order.</summary>
        public void Later(IPhaseFollowUp handler, Entity e, int code, long arg)
        {
            if (_laterCount == _later.Length) Array.Resize(ref _later, _later.Length * 2);
            _later[_laterCount++] = (handler, e, code, arg);
        }

        internal void PlayLater(EntityRepository repo)
        {
            for (int i = 0; i < _laterCount; i++)
            {
                var l = _later[i];
                _later[i] = default;   // no handler held past the tick
                l.Handler.Apply(repo, l.Entity, l.Code, l.Arg);
            }
            _laterCount = 0;
        }
    }

    /// <summary>
    /// ⭐ N-11 (CE-4473; docs/DESIGN_Parallel_Simulation.md §18.2, D-6 "a declaration on the system") — a per-entity job declares the component
    /// types it reads on OTHER entities that NOTHING writes during its own phase: such a read is served LIVE (correct: the value cannot move
    /// under it) and is never copied into the last-tick view. ⚠ Per JOB, not process-wide: a type stable in one phase (the threat ranking reads
    /// stances) may be written in another (every brain writes its own stance).
    /// </summary>
    public interface IStableReads
    {
        /// <summary>Called once per job type (on a default instance — declare types, read no fields).</summary>
        void DeclareStableReads(PhaseReadSet reads);
    }

    /// <summary>A set of component type ids (unmanaged and managed share one id space).</summary>
    public sealed class PhaseReadSet
    {
        internal ulong[] Bits = new ulong[8];
        public PhaseReadSet Unmanaged<T>() where T : unmanaged => Set(ComponentType<T>.ID);
        public PhaseReadSet Managed<T>() where T : class => Set(ManagedComponentType<T>.ID);
        private PhaseReadSet Set(int id)
        {
            if ((id >> 6) >= Bits.Length) Array.Resize(ref Bits, (id >> 6) + 1);
            Bits[id >> 6] |= 1UL << (id & 63);
            return this;
        }
        internal bool Has(int id) => (id >> 6) < Bits.Length && (Bits[id >> 6] & (1UL << (id & 63))) != 0;
    }

    /// <summary>
    /// ⭐ R-4218 (CE-4481; implements R-4207, R-4215) — the simulation mode chosen by WHAT IS RUNNING, one value for the whole process: each kernel
    /// VOTES at its tick boundary (its clock stepped ⇒ Deterministic — stepping, Run-to, filming and the scenario regression all step the clock);
    /// a harness that needs repeatability PINS Deterministic (<see cref="RequireDeterministic"/>); the process runs Deterministic when ANY kernel
    /// votes for it or any pin is held, else Throughput. ⇒ every kernel of a <c>--mode all</c> process sees the same value, changed only at a tick
    /// boundary. 0 B per tick (the votes are a grown array).
    /// </summary>
    public static class SimulationModeSelector
    {
        private static readonly object s_lock = new();
        private static bool[] s_votes = new bool[8];
        private static bool[] s_used = new bool[8];
        private static int s_pins;

        /// <summary>A kernel takes a voting slot (once, at construction).</summary>
        public static int Register()
        {
            lock (s_lock)
            {
                for (int i = 0; i < s_used.Length; i++)
                    if (!s_used[i]) { s_used[i] = true; s_votes[i] = false; return i; }
                int n = s_used.Length;
                Array.Resize(ref s_used, n * 2); Array.Resize(ref s_votes, n * 2);
                s_used[n] = true;
                return n;
            }
        }

        /// <summary>A kernel gives its slot back (at dispose).</summary>
        public static void Unregister(int slot)
        {
            lock (s_lock) { if (slot >= 0 && slot < s_used.Length) { s_used[slot] = false; s_votes[slot] = false; } }
        }

        /// <summary>Forgets every vote and pin (a rail isolates itself from kernels other tests left registered).</summary>
        internal static void ResetForTest()
        {
            lock (s_lock) { Array.Clear(s_used); Array.Clear(s_votes); }
            System.Threading.Volatile.Write(ref s_pins, 0);
        }

        /// <summary>A harness that needs repeatability holds this while it runs (tests, a regression runner); dispose to release.</summary>
        public static Pin RequireDeterministic()
        {
            System.Threading.Interlocked.Increment(ref s_pins);
            return new Pin(true);
        }

        /// <summary>The pin's scope (a struct: no allocation).</summary>
        public readonly struct Pin : IDisposable
        {
            private readonly bool _held;
            internal Pin(bool held) => _held = held;
            public void Dispose() { if (_held) System.Threading.Interlocked.Decrement(ref s_pins); }
        }

        /// <summary>The kernel at <paramref name="slot"/> votes (<paramref name="deterministic"/>: its clock is stepped) and the process mode is set
        /// from every vote and pin — called at the kernel's tick boundary, before its phases run.</summary>
        public static SimulationMode Vote(int slot, bool deterministic)
        {
            bool any = System.Threading.Volatile.Read(ref s_pins) > 0;
            lock (s_lock)
            {
                if (slot >= 0 && slot < s_votes.Length) s_votes[slot] = deterministic;
                for (int i = 0; i < s_votes.Length && !any; i++) any = s_used[i] && s_votes[i];
            }
            var mode = any ? SimulationMode.Deterministic : SimulationMode.Throughput;
            ParallelEntities.Mode = mode;
            return mode;
        }
    }

    /// <summary>
    /// ⭐ CE-4517 (X-2) — a COARSE job (one squad, one commander: a whole unit's work per entity) names its own batch size. 📐 Measured: the squad
    /// phase's widest level is ~100 commanders = 4 batches of the fixed 32 ⇒ at most 4 threads, the levels above it ONE batch; the staff phase the
    /// same. ⭐ Result-neutral (§18.7, M-1): contiguous batches merged in batch order = entity order, whatever the size — so it may be small in
    /// Deterministic mode too; it is a constant of the job type, never of the thread count.
    /// </summary>
    public interface ICoarseJob
    {
        /// <summary>Entities per batch (≥ 1). Read once per job type, on a default instance.</summary>
        int BatchSize { get; }
    }

    /// <summary>⭐ N-11 — an effect a per-entity job's helper defers to the serial step after the phase (<see cref="BatchContext.Later"/>).</summary>
    public interface IPhaseFollowUp
    {
        void Apply(EntityRepository repo, Entity e, int code, long arg);
    }

    /// <summary>
    /// ⭐ A data-parallel per-entity job. <see cref="Execute"/> runs on many threads: it may READ anything and WRITE only the entity it is given
    /// (anything else — another entity, a structural change, an event — goes into <c>ctx.Commands</c> or <c>ctx.Defer</c>).
    /// <see cref="ApplyDeferred"/> runs SERIALLY on the calling thread after the loop, batch by batch, in order. A struct: no closure, no allocation.
    /// ⛔ A job's own fields are shared by every thread — read them, never write them in <see cref="Execute"/>.
    /// </summary>
    public interface IEntityBatchJob
    {
        /// <param name="index">The entity's position in the list being run (a slot of the job's own per-entity output array, written by this entity only).</param>
        void Execute(Entity e, int index, BatchContext ctx);
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
        /// <summary>⭐ The experiment's switch (branch exp/parallel-sim): converted systems take their parallel path only when ON —
        /// <c>HROT_PARALLEL_SIM=1</c>, read once; tests set it. Off ⇒ every converted system runs its serial path, so before / after is one binary.</summary>
        public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HROT_PARALLEL_SIM") == "1";

        /// <summary>
        /// ⭐ R-4218 (CE-4481) — the mode the loops run in: ONE value for the process, set by every kernel at its tick boundary from what is running
        /// (<see cref="SimulationModeSelector"/>): stepped / Run-to / filming / the regression (the clock stepped) or a pinned harness ⇒ Deterministic,
        /// continuous live play ⇒ Throughput. Default Deterministic (no kernel has ticked yet). ⛔ No environment switch (<c>HROT_SIM_MODE</c> removed).
        /// A test that drives <see cref="Run{TJob}(EntityRepository, Entity[], int, in TJob)"/> without a kernel sets it directly.
        /// </summary>
        public static SimulationMode Mode { get; set; } = SimulationMode.Deterministic;

        /// <summary>Entities per batch in Deterministic mode — fixed, so a batch is the same set of entities whatever the thread count.</summary>
        public const int DeterministicBatchSize = 32;

        /// <summary>For the merge-cost measurement (E-2): ticks spent in the last run's serial merge (playback + deferred).</summary>
        public static long LastMergeTicks { get; private set; }

        /// <summary>Runs <paramref name="job"/> over <paramref name="entities"/>[0 … <paramref name="count"/>).</summary>
        public static void Run<TJob>(EntityRepository repo, Entity[] entities, int count, in TJob job) where TJob : struct, IEntityBatchJob
            => Runner<TJob>.Rent().Run(repo, entities, count, job);

        /// <summary>Runs <paramref name="job"/> over every entity <paramref name="query"/> matches (collected first, in index order).</summary>
        public static void Run<TJob>(EntityRepository repo, EntityQuery query, in TJob job) where TJob : struct, IEntityBatchJob
        {
            var r = Runner<TJob>.Rent();
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

        /// <summary>⭐ CE-4517 — a coarse job's own size (<see cref="ICoarseJob"/>) in either mode; else <see cref="BatchSize(int, int)"/>.</summary>
        internal static int BatchSize(int count, int threads, int coarse)
            => coarse > 0 ? coarse : BatchSize(count, threads);

        private sealed class Runner<TJob> : FdpWorkerPool.IBatchWork where TJob : struct, IEntityBatchJob
        {
            // ⭐⭐ CE-7036 (X-2) — ONE RUNNER PER CALLING THREAD, never one per process: the runner holds the running phase's job, entities and
            //   batch contexts, so two threads running the same job type at once (two worlds ticked on two threads — two test classes, a second
            //   in-process node) overwrote each other mid-phase. 📐 Measured: the drill and SOP suites under HROT_PARALLEL_SIM=1 in one run, 10–23
            //   rotating reds, each class alone green. The pool already copes with a concurrent caller (it runs that phase's batches inline).
            //   0 B in the steady state: each thread keeps its runner; a phase started from inside another of the same type gets a fresh one.
            [ThreadStatic] private static Runner<TJob>? t_instance;
            private bool _busy;
            public static Runner<TJob> Rent()
            {
                var r = t_instance ??= new Runner<TJob>();
                return r._busy ? new Runner<TJob>() : r;
            }
            // ⭐ N-11 — the job type's declared stable reads (null: none), built once
            private static readonly PhaseReadSet? s_stable = BuildStable();
            private static PhaseReadSet? BuildStable()
            {
                if (default(TJob) is not IStableReads declares) return null;
                var set = new PhaseReadSet();
                declares.DeclareStableReads(set);
                return set;
            }
            // ⭐ CE-4517 — the job type's coarse batch size (0: the default sizing), read once
            private static readonly int s_coarse = default(TJob) is ICoarseJob c ? Math.Max(1, c.BatchSize) : 0;
            public Entity[] Scratch = new Entity[64];
            private readonly List<BatchContext> _contexts = new();
            private TJob _job;
            private Entity[] _entities = Array.Empty<Entity>();
            private int _count, _batchSize;
            private EntityRepository? _repo;

            public void Run(EntityRepository repo, Entity[] entities, int count, in TJob job)
            {
                if (count <= 0) return;
                _busy = true;
                try { RunCore(repo, entities, count, in job); }
                finally { _busy = false; }
            }

            private void RunCore(EntityRepository repo, Entity[] entities, int count, in TJob job)
            {
                // ⭐ N-15 (CE-4477) — every phase reads other entities' declared types from THIS tick's view: taken here when the kernel has not
                //   taken it since the world last ticked (a world no kernel captures — the editor's preview copy, a rail); free when it has
                if (ParallelReadCheck.HasDeclarations) repo.EnsureLastTickView();
                var pool = FdpWorkerPool.Shared;
                _job = job; _entities = entities; _count = count;
                _batchSize = BatchSize(count, pool.Threads, s_coarse);
                int batches = (count + _batchSize - 1) / _batchSize;
                while (_contexts.Count < batches) _contexts.Add(new BatchContext());
                for (int b = 0; b < batches; b++) { _contexts[b].Reset(); _contexts[b].Batch = b; }

                _repo = repo;
                // ⭐ N-10 (CE-4472) — the phase: while it runs, a batch thread's cross-entity read of a declared type is served from the last-tick
                //   view, and its structural changes / bus publishes on THIS world go into its batch's buffer (merged below, in batch order)
                ParallelReadCheck.EnterPhase();
                try { pool.Run(this, batches); }
                finally { ParallelReadCheck.ExitPhase(); _repo = null; }

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int b = 0; b < batches; b++) _contexts[b].Commands.Playback(repo);
                for (int b = 0; b < batches; b++) _contexts[b].PlayLater(repo);
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
                // ⭐ N-2 / N-10 — the batch is a phase on THIS thread over ITS world: each entity is the current one (the per-entity rule's own vs
                //   other), and the batch's buffer takes the deferred effects. The census keeps its own walk stack.
                bool was = ParallelReadCheck.Active;
                ParallelReadCheck.Active = true;
                ParallelReadCheck.SetBatch(ctx, _repo, s_stable);
                int slot = EntityCensus.On ? EntityCensus.Push() : -1;
                try
                {
                    for (int i = from; i < to; i++)
                    {
                        ParallelReadCheck.Current = _entities[i].Index;
                        ParallelReadCheck.ClearOwned();   // F-11 — an extra ownership lasts one entity's Execute
                        if (slot >= 0) EntityCensus.Set(slot, _entities[i].Index);
                        _job.Execute(_entities[i], i, ctx);
                    }
                }
                finally
                {
                    if (slot >= 0) EntityCensus.Pop(slot);
                    ParallelReadCheck.Current = -1;
                    ParallelReadCheck.ClearOwned();
                    ParallelReadCheck.SetBatch(null, null, null);
                    ParallelReadCheck.Active = was;
                }
            }
        }
    }
}
