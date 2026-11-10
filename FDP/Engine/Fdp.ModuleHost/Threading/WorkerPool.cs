using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Fdp.ModuleHost.Threading
{
    /// <summary>
    /// ⭐ CE-3427 — the process's ONE worker pool (shared + unified): the EQS solver's windows (P-1) and the off-frame egress scan (N-1,
    /// docs/DESIGN_Egress_Change_Detection.md §7) both use it. Moved here from Hrot.SimHost (it was <c>EqsSolveWorkers</c>).
    /// Built for the EQS solver (docs/DESIGN_Sensors_And_Doctrine.md §5.8, P1-C): <c>N − 1</c> persistent threads
    /// plus the calling thread work through <c>0 … count − 1</c> of one window and <see cref="Run"/> returns when every index is done.
    /// ⭐ No allocation per run (R-2084): the work is a delegate the caller keeps, the hand-off a semaphore and a countdown. A work item
    /// that throws is rethrown on the calling thread after the window.
    /// <para>⭐ ONE pool per process (<see cref="Shared"/>): every solver of the process (a test's, a second node's in one process) uses it,
    /// so the threads are bounded and never leak; a solver that finds it busy solves its window itself (<see cref="TryRun"/> false) —
    /// the result is the same either way, only the time differs.</para>
    /// </summary>
    public sealed class WorkerPool : IDisposable
    {
        private static readonly object s_lock = new();
        private static readonly System.Collections.Generic.Dictionary<int, WorkerPool> s_shared = new();

        /// <summary>
        /// The process's pool with <paramref name="participants"/> threads (the caller included). One per distinct count, kept for the
        /// process — ⛔ never disposed on a count change: another solver may be inside a window on it.
        /// </summary>
        public static WorkerPool Shared(int participants)
        {
            lock (s_lock)
            {
                if (!s_shared.TryGetValue(participants, out var pool)) s_shared[participants] = pool = new WorkerPool(participants);
                return pool;
            }
        }

        private readonly object _gate = new();
        private static readonly Action<int> s_idle = static _ => { };
        private Action<int> _work = s_idle;
        private readonly Thread[] _threads;
        private readonly SemaphoreSlim _go = new(0);
        private readonly ManualResetEventSlim _done = new(false);
        private int _count, _next, _pending;
        private Exception? _error;
        private volatile bool _stop;

        /// <param name="participants">Threads working a window, the caller included (≥ 2).</param>
        private WorkerPool(int participants)
        {
            if (participants < 2) throw new ArgumentOutOfRangeException(nameof(participants), "one participant is the caller's own loop — no pool");
            _threads = new Thread[participants - 1];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(Loop) { IsBackground = true, Name = $"Worker pool {participants}.{i + 1}" };
                _threads[i].Start();
            }
        }

        /// <summary>Threads working a window, the caller included.</summary>
        public int Participants => _threads.Length + 1;

        /// <summary>
        /// Runs <paramref name="work"/> for indices <c>0 … count − 1</c> on every participant and returns true when all are done — or
        /// returns false at once when another caller holds the pool (the caller then does the work itself).
        /// </summary>
        public bool TryRun(int count, Action<int> work)
        {
            if (count <= 0) return true;
            if (!Monitor.TryEnter(_gate)) return false;
            try { Run(count, work); }
            finally { Monitor.Exit(_gate); }
            return true;
        }

        private void Run(int count, Action<int> work)
        {
            _work  = work;
            _count = count;
            Volatile.Write(ref _next, -1);
            Volatile.Write(ref _pending, _threads.Length + 1);
            _done.Reset();
            _go.Release(_threads.Length);
            Participate();
            _done.Wait();
            _work = s_idle;   // ⭐ never keep the last caller's work: it roots the caller (a solver, an egress pipeline) for the process's life
            var error = Interlocked.Exchange(ref _error, null);
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private void Loop()
        {
            while (true)
            {
                _go.Wait();
                if (_stop) return;
                Participate();
            }
        }

        private void Participate()
        {
            int k;
            while ((k = Interlocked.Increment(ref _next)) < _count)
            {
                try { _work(k); }
                catch (Exception e) { Interlocked.CompareExchange(ref _error, e, null); }
            }
            if (Interlocked.Decrement(ref _pending) == 0) _done.Set();
        }

        public void Dispose()
        {
            _stop = true;
            _go.Release(_threads.Length);
        }
    }
}
