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
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") == "1";
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
            if (!Enabled) { MainHasWorld = true; return true; }
            double now = FrameClock.Elapsed.TotalMilliseconds;
            if (_lastFrameAt >= 0) lock (StatLock) (_lastWasWorld ? WorldMs : MapMs).Add(now - _lastFrameAt);
            _lastFrameAt = now;
            MainHasWorld = Monitor.TryEnter(WorldLock, 2);
            if (MainHasWorld) { _mainWants = false; WorldFrames++; } else { _mainWants = true; MapOnlyFrames++; }
            _lastWasWorld = MainHasWorld;
            return MainHasWorld;
        }

        /// <summary>The drawing thread, at its frame's end.</summary>
        public static void MainExit()
        {
            if (!Enabled || !MainHasWorld) return;
            MainHasWorld = false;
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
