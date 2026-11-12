using System;
using System.Threading;

namespace Fdp.Core.Jobs
{
    /// <summary>
    /// ⭐ P-0 (docs/DESIGN_Parallel_Simulation.md §5, §7) — ONE persistent pool of worker threads for the simulation's data-parallel loops.
    /// ⭐ 0 B per run once warm (R-2084): no <c>Parallel.ForEach</c>, no closure, no Task — the workers wait on one event and take batches from
    /// ONE cursor tagged with the run's generation (a late worker of an older run can never take a batch of a newer one); the calling thread
    /// takes batches too. ⭐ One core budget: <see cref="FdpConfig.MaxDegreeOfParallelism"/> (−1 ⇒ cores − 2, at least 1 — the presentation and
    /// the background modules keep their cores, design §4). One run at a time; a nested or concurrent run executes inline on its caller.
    /// </summary>
    public sealed class FdpWorkerPool : IDisposable
    {
        /// <summary>The one pool, created on first use with the budget at that moment; <see cref="Resize"/> changes it (tests, the harness).</summary>
        public static FdpWorkerPool Shared => _shared ??= new FdpWorkerPool(Budget());
        private static FdpWorkerPool? _shared;

        /// <summary>The thread budget: <see cref="FdpConfig.MaxDegreeOfParallelism"/> when positive, else cores − 2 (at least 1).</summary>
        public static int Budget() => FdpConfig.MaxDegreeOfParallelism > 0 ? FdpConfig.MaxDegreeOfParallelism : Math.Max(1, Environment.ProcessorCount - 2);

        /// <summary>Replaces the shared pool with one of <paramref name="threads"/> threads (the caller counts as one). Tests and the harness.</summary>
        public static void Resize(int threads)
        {
            var old = _shared;
            _shared = new FdpWorkerPool(Math.Max(1, threads));
            old?.Dispose();
        }

        /// <summary>The work of one run: batch i of n. Implemented once per job type (no allocation per run).</summary>
        public interface IBatchWork { void RunBatch(int batch); }

        private readonly Thread[] _threads;
        private readonly ManualResetEventSlim _go = new(false);
        private readonly ManualResetEventSlim _done = new(false);
        private IBatchWork? _work;
        private long _cursor;          // (generation << 32) | next batch
        private int _batches, _finished, _generation, _running;
        private volatile bool _stop;
        private Exception? _fault;

        /// <summary>The threads that execute batches, the caller included.</summary>
        public int Threads { get; }

        public FdpWorkerPool(int threads)
        {
            Threads = Math.Max(1, threads);
            _threads = new Thread[Threads - 1];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"FdpWorker{i + 1}" };
                _threads[i].Start();
            }
        }

        /// <summary>Runs batches 0 … <paramref name="batches"/>−1 of <paramref name="work"/> across the pool; returns when all are done.
        /// ⛔ The order batches RUN in is not deterministic — what is, is that each batch is the same set of entities whatever the thread count.</summary>
        public void Run(IBatchWork work, int batches)
        {
            if (batches <= 0) return;
            if (_threads.Length == 0 || batches == 1 || Interlocked.Exchange(ref _running, 1) == 1)
            {
                for (int b = 0; b < batches; b++) work.RunBatch(b);   // single-threaded, or nested: inline
                return;
            }
            try
            {
                int gen = _generation + 1;
                _work = work; _batches = batches; _finished = 0; _fault = null;
                _done.Reset();
                Volatile.Write(ref _cursor, (long)gen << 32);
                Volatile.Write(ref _generation, gen);
                _go.Set();
                Drain(gen, work);
                _done.Wait();
                _go.Reset();
                if (_fault is { } f) throw new AggregateException("a parallel batch failed", f);
            }
            finally { _work = null; Volatile.Write(ref _running, 0); }
        }

        private void Drain(int gen, IBatchWork work)
        {
            while (true)
            {
                long c = Volatile.Read(ref _cursor);
                if ((int)(c >> 32) != gen) return;                 // another run now
                int b = (int)(c & 0xFFFFFFFF);
                if (b >= _batches) return;
                if (Interlocked.CompareExchange(ref _cursor, c + 1, c) != c) continue;
                try { work.RunBatch(b); }
                catch (Exception ex) { Interlocked.CompareExchange(ref _fault, ex, null); }
                if (Interlocked.Increment(ref _finished) == _batches) _done.Set();
            }
        }

        private void WorkerLoop()
        {
            int seen = 0;
            while (!_stop)
            {
                _go.Wait();
                if (_stop) return;
                int gen = Volatile.Read(ref _generation);
                if (gen == seen) { Thread.Yield(); continue; }    // drained this run already; the caller resets _go
                seen = gen;
                if (Volatile.Read(ref _work) is { } work) Drain(gen, work);
            }
        }

        public void Dispose()
        {
            _stop = true;
            _go.Set();
            foreach (var t in _threads) t.Join(1000);
            _go.Dispose(); _done.Dispose();
        }
    }
}
