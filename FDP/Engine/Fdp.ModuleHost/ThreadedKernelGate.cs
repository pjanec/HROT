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
        public static bool MainTryEnter()
        {
            if (!Enabled) { MainHasWorld = true; return true; }
            MainHasWorld = Monitor.TryEnter(WorldLock, 2);
            if (MainHasWorld) { _mainWants = false; WorldFrames++; } else { _mainWants = true; MapOnlyFrames++; }
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
