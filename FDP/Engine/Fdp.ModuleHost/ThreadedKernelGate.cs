using System;
using System.Threading;

namespace Fdp.ModuleHost
{
    /// <summary>
    /// ⭐ EXPERIMENT (threaded kernel X-4, exp/threaded-kernel; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §12) — the ONE world lock between
    /// the kernel's worker thread and the drawing thread, opt-in <c>HROT_THREADED_KERNEL=1</c> (default OFF ⇒ nothing changes).
    /// <para>The worker holds it for a whole node frame. The drawing thread tries it once per frame (<see cref="MainTryEnter"/>): with it, the frame does
    /// everything that reads or writes the world (input, the API's jobs, the panels); without it, the frame only draws the map from the published
    /// gizmo frame and moves the camera. When the drawing thread missed it, the worker yields at its next tick boundary (<see cref="WorkerYield"/>) so
    /// the panels refresh once per tick.</para>
    /// </summary>
    public static class ThreadedKernelGate
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") is "1" or "2" or "3";

        /// <summary>⭐ POC 3 (CE-4442) — <c>HROT_THREADED_KERNEL=3</c>: the NODE is COMPOSED over World S (its own repository, built by the bootstrapper)
        /// and the PRESENTATION over World A from the start; A takes S after each node frame. <c>HROT_THREADED_SINGLE=1</c>: the node frame stays on the
        /// main thread (the composition proven before the threading).</summary>
        public static readonly bool ComposedWorlds = Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") == "3";
        public static readonly bool ComposedSingleThread = Environment.GetEnvironmentVariable("HROT_THREADED_SINGLE") == "1";

        /// <summary>⭐ POC 2 (CE-4439) — <c>HROT_THREADED_KERNEL=2</c>: TWO worlds — the simulation ticks World S on the worker, the presentation keeps
        /// World A, synced S→A after each tick (📄 DESIGN_Threaded_Simulation_Presentation.md §14). The worker runs only while <see cref="Engaged"/>.</summary>
        public static readonly bool TwoWorlds = Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") == "2";

        /// <summary>⭐ POC 2 — the worker HOLDS the lock for its whole tick as well as the sync (<c>HROT_TWO_WORLDS_LOCK_TICK=1</c>): the safe
        /// variant while systems still capture A. OFF: the lock is taken for the S→A sync only, and the tick runs beside the drawing thread.</summary>
        public static readonly bool LockWholeTick = Environment.GetEnvironmentVariable("HROT_TWO_WORLDS_LOCK_TICK") == "1";

        /// <summary>Whether a worker is running (mode 1: always; mode 2: set by the host while S is engaged).</summary>
        public static volatile bool Engaged = Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") == "1";
        private static bool _locked;
        public static readonly object WorldLock = new();
        private static volatile bool _mainWants;
        private static readonly AutoResetEvent MainDone = new(false);

        /// <summary>Whether the drawing thread holds the world this frame.</summary>
        public static bool MainHasWorld { get; private set; }

        /// <summary>Frames the drawing thread had the world / did not (the experiment's counters).</summary>
        public static long WorldFrames, MapOnlyFrames, Ticks, YieldWaits;

        /// <summary>The drawing thread, at its frame's start: take the world if the worker is between ticks (never wait for a tick).</summary>
        // the drawing thread's own frame clock, split by the kind of the frame that just ended (X-4's measurement)
        private static readonly System.Diagnostics.Stopwatch FrameClock = System.Diagnostics.Stopwatch.StartNew();
        private static double _lastFrameAt = -1; private static bool _lastWasWorld;
        private static readonly object StatLock = new();
        private static readonly System.Collections.Generic.List<double> WorldMs = new(), MapMs = new();

        /// <summary>The drawing thread's frame times since the last call, by kind: (world mean, world p95, map-only mean, map-only p95, counts).</summary>
        public static (double WMean, double WP95, int WN, double MMean, double MP95, int MN) TakeFrameStats()
        {
            lock (StatLock)
            {
                static (double, double) Of(System.Collections.Generic.List<double> l)
                {
                    if (l.Count == 0) return (0, 0);
                    var a = l.ToArray(); Array.Sort(a); double sum = 0; foreach (var x in a) sum += x;
                    return (sum / a.Length, a[(int)(a.Length * 0.95)]);
                }
                var (wm, wp) = Of(WorldMs); var (mm, mp) = Of(MapMs);
                var r = (wm, wp, WorldMs.Count, mm, mp, MapMs.Count);
                WorldMs.Clear(); MapMs.Clear();
                return r;
            }
        }

        public static bool MainTryEnter()
        {
            if (!Enabled || !Engaged) { MainHasWorld = true; _locked = false; return true; }
            double now = FrameClock.Elapsed.TotalMilliseconds;
            if (_lastFrameAt >= 0) lock (StatLock) (_lastWasWorld ? WorldMs : MapMs).Add(now - _lastFrameAt);
            _lastFrameAt = now;
            if (FairHandover && _workerWants) { MainHandoverWaits++; WorkerSyncDone.WaitOne(2); }   // ⭐ K-1 — let the waiting worker sync first
            MainHasWorld = Monitor.TryEnter(WorldLock, 2);
            _locked = MainHasWorld;
            if (MainHasWorld) { _mainWants = false; WorldFrames++; } else { _mainWants = true; MapOnlyFrames++; }
            _lastWasWorld = MainHasWorld;
            return MainHasWorld;
        }

        /// <summary>⭐ POC 2 — the drawing thread takes the lock in the MIDDLE of its frame (two worlds engage this frame: its world work so far ran
        /// unlocked because no worker existed); <see cref="MainExit"/> releases it as usual.</summary>
        public static void MainAdoptLock()
        {
            if (_locked) return;
            Monitor.Enter(WorldLock);
            _locked = true; MainHasWorld = true;
        }

        /// <summary>⭐ POC 2 — the drawing thread lets go of the lock so a stopping worker can finish its sync (then <see cref="MainAdoptLock"/>).</summary>
        public static void MainReleaseForStop()
        {
            if (!_locked) return;
            _locked = false;
            Monitor.Exit(WorldLock);
        }

        /// <summary>The drawing thread, at its frame's end.</summary>
        // ⭐ K-1 (CE-4450) — the hand-over measured: the worker's want, the main thread's exits while it waits, and the FAIR hand-over prototype
        /// <summary><c>HROT_FAIR_HANDOVER=1</c>: while the worker waits for the world to sync, the drawing thread waits ≤ 2 ms for that sync before it
        /// takes the lock again (otherwise it re-enters at once and the unfair monitor starves the worker).</summary>
        public static readonly bool FairHandover = Environment.GetEnvironmentVariable("HROT_FAIR_HANDOVER") == "1";
        private static volatile bool _workerWants;
        private static readonly AutoResetEvent WorkerSyncDone = new(false);
        public static long MainExitsWhileWorkerWaits, MainHandoverWaits;

        /// <summary>The worker takes the world for its sync (and says so while it waits).</summary>
        public static void WorkerEnter() { _workerWants = true; Monitor.Enter(WorldLock); _workerWants = false; }

        /// <summary>The worker's sync is done.</summary>
        public static void WorkerExit() { Monitor.Exit(WorldLock); WorkerSyncDone.Set(); }

        public static void MainExit()
        {
            if (_workerWants) MainExitsWhileWorkerWaits++;
            if (!Enabled || !MainHasWorld) return;
            MainHasWorld = false;
            if (!_locked) return;   // mode 2 disengaged at this frame's start: nothing was taken
            _locked = false;
            Monitor.Exit(WorldLock);
            MainDone.Set();
        }

        /// <summary>The worker, between two ticks (not holding the lock): if the drawing thread asked for the world, wait until it took and released it.</summary>
        public static void WorkerYield()
        {
            if (!_mainWants) return;
            YieldWaits++;
            MainDone.WaitOne(50);   // the drawing thread takes the lock on its next frame; a bounded wait (it may be minimised)
        }
    }
}
