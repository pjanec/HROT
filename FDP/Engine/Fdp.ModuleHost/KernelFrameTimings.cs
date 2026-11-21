using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Fdp.ModuleHost
{
    /// <summary>
    /// ⭐ CE-4433 (P-1 / P-2, measuring the preview frame on Windows) — opt-in timing of the kernel's frame BY PART: each phase, the sync points (the snapshot copies
    /// for background modules), each module's harvest (its command playback), each synchronous module's run (systems + Tick + playback), the frame-synced wait.
    /// ⭐ The system scheduler already times every SYSTEM; this names what lies BETWEEN them, so "update 115 ms" can be accounted for. Main thread only.
    /// Off (the default): one bool test per part. On: 0 B per frame once every part has been seen (R-2084) — a part's label is built once per module and kept.
    /// Turned on by <c>HROT_FRAME_STATS=1</c> (the runner's FrameStats) and drained by it every window.
    /// </summary>
    public static class KernelFrameTimings
    {
        public static bool Enabled;

        private static readonly Dictionary<string, int> Index = new();
        private static string[] _names = new string[64];
        private static long[] _ticks = new long[64], _bytes = new long[64];
        private static int[] _calls = new int[64];
        private static int _count;

        /// <summary>A timestamp to pass to <see cref="Stop"/> (0 when off).</summary>
        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

        /// <summary>The thread's allocation counter to pass to <see cref="Stop"/> (0 when off).</summary>
        public static long StartBytes() => Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;

        public static void Stop(string part, long t0, long b0 = 0)
        {
            if (!Enabled || t0 == 0) return;
            long dt = Stopwatch.GetTimestamp() - t0;
            if (!Index.TryGetValue(part, out int i))
            {
                i = _count++;
                if (i == _names.Length)
                {
                    Array.Resize(ref _names, i * 2); Array.Resize(ref _ticks, i * 2); Array.Resize(ref _bytes, i * 2); Array.Resize(ref _calls, i * 2);
                }
                _names[i] = part; Index[part] = i;
            }
            _ticks[i] += dt; _calls[i]++;
            if (b0 != 0) _bytes[i] += GC.GetAllocatedBytesForCurrentThread() - b0;
        }

        public readonly record struct Part(string Name, double Ms, int Calls, long Bytes);

        /// <summary>Every part timed since the last drain, biggest first, and zeroes them (allocates — called once per stats window, not per frame).</summary>
        public static List<Part> Drain()
        {
            var list = new List<Part>(_count);
            for (int i = 0; i < _count; i++)
            {
                if (_calls[i] > 0) list.Add(new Part(_names[i], _ticks[i] * 1000.0 / Stopwatch.Frequency, _calls[i], _bytes[i]));
                _ticks[i] = 0; _calls[i] = 0; _bytes[i] = 0;
            }
            list.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            return list;
        }
    }
}
