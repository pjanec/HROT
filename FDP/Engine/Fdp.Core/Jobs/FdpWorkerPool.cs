using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Fdp.Core.Jobs
{
    /// <summary>
    /// ⭐ The process's ONE worker pool — POC 4 J-1 (docs/DESIGN_Parallel_Simulation.md §13): it merges the simulation's P-0 pool (this class,
    /// the parallel systems) with the former <c>Fdp.ModuleHost.Threading.WorkerPool</c> (the EQS solver's windows, the off-frame egress scan),
    /// which were two independent pools each sized to the machine. ⭐ One budget by construction (<see cref="Budget"/>: cores − 2).
    /// <para><b>Two lanes, one set of threads.</b></para>
    /// <list type="bullet">
    /// <item>⭐ the FRAME lane — <see cref="Run"/>: the main thread's data-parallel loops (the parallel systems, the replica sync). Batches from one
    /// cursor tagged with the run's generation (a late worker of an older run can never take a batch of a newer one); the caller takes batches
    /// too; the per-batch ordered merge stays in <see cref="ParallelEntities"/>. One frame run at a time; a nested / concurrent one runs inline.</item>
    /// <item>the BACKGROUND lane — <see cref="TryRun"/>: a window of independent items from a background caller (EQS, the egress scan). A worker
    /// checks the frame lane BEFORE every background item ⇒ ⭐ a background window delays a frame batch by at most the one item a worker is in
    /// the middle of. The caller works its own window too; completion counts ITEMS (a worker may be busy with frame work). One window at a time
    /// (<see cref="TryRun"/> returns false at once when another holds it — that caller then does the work itself).</item>
    /// </list>
    /// ⭐ 0 B per run once warm (R-2084): no Task, no closure per run — a semaphore hands out wake-ups, the work is an object the caller keeps.
    /// </summary>
    public sealed class FdpWorkerPool : IDisposable
    {
        /// <summary>The one pool, created on first use with the budget at that moment; <see cref="Resize"/> changes it (tests, the harness).</summary>
        public static FdpWorkerPool Shared => _shared ??= new FdpWorkerPool(Budget());
        private static FdpWorkerPool? _shared;

        /// <summary>The thread budget: <c>HROT_SIM_THREADS</c> when set (the measurement sweep), else <see cref="FdpConfig.MaxDegreeOfParallelism"/>
        /// when positive, else cores − 2 (at least 1).</summary>
        public static int Budget()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("HROT_SIM_THREADS"), out int env) && env > 0) return env;
            return FdpConfig.MaxDegreeOfParallelism > 0 ? FdpConfig.MaxDegreeOfParallelism : Math.Max(1, Environment.ProcessorCount - 2);
        }

        /// <summary>Replaces the shared pool with one of <paramref name="threads"/> threads (the caller counts as one). Tests and the harness.
        /// ⚠ The old pool's threads end after their current work; a caller holding the old instance finishes its run on it.</summary>
        public static void Resize(int threads)
        {
            var old = _shared;
            _shared = new FdpWorkerPool(Math.Max(1, threads));
            old?.Dispose();
        }

        /// <summary>The work of one frame run: batch i of n. Implemented once per job type (no allocation per run).</summary>
        public interface IBatchWork { void RunBatch(int batch); }

        private readonly Thread[] _threads;
        private readonly SemaphoreSlim _wake = new(0);
        private volatile bool _stop;

        // ── the FRAME lane ──
        private readonly ManualResetEventSlim _frameDone = new(false);
        private IBatchWork? _frameWork;
        private long _frameCursor;          // (generation << 32) | next batch
        private int _frameBatches, _frameFinished, _frameGeneration, _frameRunning;
        private Exception? _frameFault;
        private object? _frameFlow;          // ⭐ N-2 — the caller's tracer reader / context, restored on a worker for each of its batches

        // ── the BACKGROUND lane ──
        private readonly object _bgGate = new();
        private readonly ManualResetEventSlim _bgDone = new(false);
        private static readonly Action<int> s_idle = static _ => { };
        private Action<int> _bgWork = s_idle;
        private long _bgCursor;             // (generation << 32) | next item
        private int _bgCount, _bgFinished, _bgGeneration;
        private Exception? _bgFault;
        private object? _bgFlow;

        /// <summary>The threads that execute work, the caller included.</summary>
        public int Threads { get; }

        /// <summary>Same as <see cref="Threads"/> — the name the EQS windows use.</summary>
        public int Participants => Threads;

        /// <summary>Frame batches run by a worker thread (not the caller) since creation — a rail reads it.</summary>
        public long FrameBatchesOnWorkers => Interlocked.Read(ref _frameOnWorkers);
        private long _frameOnWorkers;

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

        // ════ the FRAME lane ════

        /// <summary>Runs batches 0 … <paramref name="batches"/>−1 of <paramref name="work"/> across the pool; returns when all are done.
        /// ⛔ The order batches RUN in is not deterministic — what is, is that each batch is the same set of entities whatever the thread count.</summary>
        public void Run(IBatchWork work, int batches)
        {
            if (batches <= 0) return;
            if (_threads.Length == 0 || batches == 1 || Interlocked.Exchange(ref _frameRunning, 1) == 1)
            {
                for (int b = 0; b < batches; b++) work.RunBatch(b);   // single-threaded, or nested: inline
                return;
            }
            try
            {
                int gen = _frameGeneration + 1;
                _frameWork = work; _frameBatches = batches; _frameFinished = 0; _frameFault = null;
                _frameFlow = AccessTrace.CaptureFlow();
                _frameDone.Reset();
                Volatile.Write(ref _frameCursor, (long)gen << 32);
                Volatile.Write(ref _frameGeneration, gen);
                _wake.Release(_threads.Length);
                DrainFrame(gen, work, onWorker: false);
                _frameDone.Wait();
                if (_frameFault is { } f) throw new AggregateException("a parallel batch failed", f);
            }
            finally { _frameWork = null; _frameFlow = null; Volatile.Write(ref _frameRunning, 0); }
        }

        // true when it ran at least one batch
        private bool DrainFrame(int gen, IBatchWork work, bool onWorker)
        {
            bool ran = false;
            while (true)
            {
                long c = Volatile.Read(ref _frameCursor);
                if ((int)(c >> 32) != gen) return ran;             // another run now
                int b = (int)(c & 0xFFFFFFFF);
                if (b >= Volatile.Read(ref _frameBatches)) return ran;
                if (Interlocked.CompareExchange(ref _frameCursor, c + 1, c) != c) continue;
                ran = true;
                if (onWorker) Interlocked.Increment(ref _frameOnWorkers);
                object? prevFlow = onWorker && AccessTrace.On ? AccessTrace.SwapFlow(_frameFlow) : null;   // ⭐ N-2 (F-1)
                try { work.RunBatch(b); }
                catch (Exception ex) { Interlocked.CompareExchange(ref _frameFault, ex, null); }
                finally { if (onWorker && AccessTrace.On) AccessTrace.SwapFlow(prevFlow); }
                if (Interlocked.Increment(ref _frameFinished) == _frameBatches) _frameDone.Set();
            }
        }

        // a worker: the CURRENT frame run's batches, if any are left
        private bool TryFrame()
        {
            if (Volatile.Read(ref _frameRunning) == 0) return false;
            int gen = Volatile.Read(ref _frameGeneration);
            var work = Volatile.Read(ref _frameWork);
            return work != null && DrainFrame(gen, work, onWorker: true);
        }

        // ════ the BACKGROUND lane ════

        /// <summary>
        /// Runs <paramref name="work"/> for indices <c>0 … count − 1</c> across the pool and returns true when all are done — or returns false at
        /// once when another caller holds the background lane (the caller then does the work itself). Frame work always goes first: a worker
        /// finishes its current item, then serves a waiting frame run. A work item that throws is rethrown here after the window.
        /// </summary>
        public bool TryRun(int count, Action<int> work)
        {
            if (count <= 0) return true;
            if (_threads.Length == 0) { for (int k = 0; k < count; k++) work(k); return true; }
            if (!Monitor.TryEnter(_bgGate)) return false;
            try
            {
                int gen = _bgGeneration + 1;
                _bgWork = work; _bgCount = count; _bgFinished = 0; _bgFault = null;
                _bgFlow = AccessTrace.CaptureFlow();
                _bgDone.Reset();
                Volatile.Write(ref _bgCursor, (long)gen << 32);
                Volatile.Write(ref _bgGeneration, gen);
                _wake.Release(_threads.Length);
                while (TakeBackground(gen, work, onWorker: false)) { }   // the caller works its own window (it is not a frame worker)
                _bgDone.Wait();
                _bgWork = s_idle; _bgFlow = null;   // ⭐ never keep the last caller's work: it roots the caller (a solver, an egress pipeline)
                var error = Interlocked.Exchange(ref _bgFault, null);
                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
                return true;
            }
            finally { Monitor.Exit(_bgGate); }
        }

        // ONE background item of the window `gen`; false when none is left
        private bool TakeBackground(int gen, Action<int> work, bool onWorker)
        {
            while (true)
            {
                long c = Volatile.Read(ref _bgCursor);
                if ((int)(c >> 32) != gen) return false;
                int k = (int)(c & 0xFFFFFFFF);
                if (k >= Volatile.Read(ref _bgCount)) return false;
                if (Interlocked.CompareExchange(ref _bgCursor, c + 1, c) != c) continue;
                object? prevFlow = onWorker && AccessTrace.On ? AccessTrace.SwapFlow(_bgFlow) : null;   // ⭐ N-2 (F-1)
                try { work(k); }
                catch (Exception e) { Interlocked.CompareExchange(ref _bgFault, e, null); }
                finally { if (onWorker && AccessTrace.On) AccessTrace.SwapFlow(prevFlow); }
                if (Interlocked.Increment(ref _bgFinished) == _bgCount) _bgDone.Set();
                return true;
            }
        }

        private bool TryBackground()
        {
            int gen = Volatile.Read(ref _bgGeneration);
            var work = Volatile.Read(ref _bgWork);
            return !ReferenceEquals(work, s_idle) && TakeBackground(gen, work, onWorker: true);
        }

        // ════ the workers ════

        private void WorkerLoop()
        {
            Threading.WindowsScheduling.OptOutCurrentThread();   // ⭐ N-14 — a pool thread is simulation work (a no-op off Windows)
            while (true)
            {
                _wake.Wait();
                if (_stop) return;
                // ⭐ the priority: drain the frame lane, then take ONE background item, then look at the frame lane again
                while (!_stop)
                {
                    bool did = TryFrame();
                    if (TryBackground()) continue;
                    if (!did) break;
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            if (_threads.Length > 0) _wake.Release(_threads.Length);
            foreach (var t in _threads) t.Join(1000);
        }
    }
}
