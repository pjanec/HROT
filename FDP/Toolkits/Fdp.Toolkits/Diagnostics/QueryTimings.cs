using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3427</c> (large terrain, step 1 — docs/DESIGN_Scale_Rig.md §6) — what each WORLD QUERY costs, apart from the system that asks
    /// it: calls, total and worst time per named slot (a terrain trace, a navmesh path, an EQS template or test, a cover lookup …).
    /// The scheduler's profiler times whole systems, so a query asked from a brain or a sensor disappears into that system's time; this
    /// separates the terrain's cost from the logic's.
    /// <para>⭐ Allocation-free and contention-free on the hot path (R-2084): each thread accumulates into its OWN buffer (registered once
    /// per thread); <see cref="Snapshot"/> sums them. A slot is named once (<see cref="Slot"/>, cached by the caller) — the per-call cost is
    /// two <see cref="Stopwatch.GetTimestamp"/> reads. Off with <c>HROT_QUERY_TIMINGS=0</c>. Process-wide: one node per process.</para>
    /// </summary>
    public static class QueryTimings
    {
        /// <summary>The most slots a process names.</summary>
        public const int MaxSlots = 512;

        /// <summary>False ⇒ <see cref="Begin"/> returns 0 and <see cref="End"/> records nothing.</summary>
        public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("HROT_QUERY_TIMINGS") != "0";

        private static readonly object Gate = new();
        private static readonly Dictionary<string, int> ByName = new(StringComparer.Ordinal);
        private static readonly string[] Names = new string[MaxSlots];
        private static int _slotCount;
        private static readonly List<Buffer> Buffers = new();
        [ThreadStatic] private static Buffer? t_buffer;
        private static long _resetAt = Stopwatch.GetTimestamp();

        private sealed class Buffer
        {
            public readonly long[] Calls = new long[MaxSlots];
            public readonly long[] Ticks = new long[MaxSlots];
            public readonly long[] Max = new long[MaxSlots];
            public readonly long[] Bytes = new long[MaxSlots];
        }

        /// <summary>The slot named <paramref name="name"/> (created on first use) — call once and keep the id; a name is a dotted
        /// family + item, e.g. <c>terrain.SegmentBlocked</c>, <c>nav.PlanPath</c>, <c>eqs.template:FindCover</c>.</summary>
        public static int Slot(string name)
        {
            lock (Gate)
            {
                if (ByName.TryGetValue(name, out int id)) return id;
                if (_slotCount >= MaxSlots) return MaxSlots - 1;   // the last slot collects the overflow
                id = _slotCount++;
                Names[id] = name;
                ByName[name] = id;
                return id;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, Type), int> ByType = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, uint), int> ById = new();

        /// <summary>The slot <c>&lt;family&gt;:&lt;type name&gt;</c> — an EQS generator or test, keyed by its type (a lookup, no allocation once named).</summary>
        public static int SlotFor(string family, Type type)
        {
            if (ByType.TryGetValue((family, type), out int s)) return s;
            s = Slot($"{family}:{type.Name}");
            return ByType.GetOrAdd((family, type), s);
        }

        /// <summary>The slot <c>&lt;family&gt;:&lt;labelSource's type&gt;#&lt;id&gt;</c> — one EQS template by its id (labelled by its generator; read
        /// only when the slot is new, so a call allocates nothing once named).</summary>
        public static int SlotFor(string family, uint id, object labelSource)
        {
            if (ById.TryGetValue((family, id), out int s)) return s;
            s = Slot($"{family}:{labelSource.GetType().Name}#{id:x8}");
            return ById.GetOrAdd((family, id), s);
        }

        /// <summary>
        /// ⭐ R-4209 (DESIGN_Parallel_Simulation §12; POC 3 T-1) — the timer is SAMPLED: one call in <see cref="SampleOneIn"/> is timed and its time
        /// (and garbage) scaled up by the same factor; the CALL COUNTS stay exact. 📐 POC 2 D-1: always-timed it cost ~13 ms of the brain at
        /// 1 000 men (37 000 timed calls / frame). The choice is a per-thread xorshift draw — a fixed every-n-th counter would alias with a loop
        /// that times n calls in a fixed order. 1 = time every call (a rail compares the two). Max is the max of the SAMPLED calls.
        /// </summary>
        public static int SampleOneIn { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("HROT_QUERY_SAMPLE"), out int n) && n >= 1 ? n : 16;   // the measurement knob: 1 = time every call
        [ThreadStatic] private static uint t_rng;
        private const long CountOnly = -1L;

        /// <summary>The start of a timed call (0 when disabled; a count-only marker when this call is not sampled). Pair with <see cref="End"/>.</summary>
        public static long Begin()
        {
            if (!Enabled) return 0L;
            int n = SampleOneIn;
            if (n > 1)
            {
                uint x = t_rng;
                if (x == 0) x = (uint)Environment.CurrentManagedThreadId * 2654435761u | 1u;
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                t_rng = x;
                if (x % (uint)n != 0) return CountOnly;
            }
            return Stopwatch.GetTimestamp();
        }

        /// <summary>Records one call of <paramref name="slot"/> that began at <paramref name="start"/>.</summary>
        public static void End(int slot, long start)
        {
            if (start == 0L) return;
            var b = t_buffer ?? Register();
            b.Calls[slot]++;
            if (start == CountOnly) return;   // ⭐ R-4209 — counted, not timed
            long dt = Stopwatch.GetTimestamp() - start;
            b.Ticks[slot] += dt * Math.Max(1, SampleOneIn);
            if (dt > b.Max[slot]) b.Max[slot] = dt;
        }

        /// <summary>⭐ The thread's allocated bytes now (0 when disabled) — pass it to <see cref="End(int, long, long)"/> to count a call's garbage
        /// (scaled like the time: only a sampled call's garbage is measured).</summary>
        public static long Bytes() => Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0L;

        /// <summary>As <see cref="End(int, long)"/>, and the bytes this thread allocated since <paramref name="bytesAtStart"/> (<see cref="Bytes"/>).</summary>
        public static void End(int slot, long start, long bytesAtStart)
        {
            if (start == 0L) return;
            if (start == CountOnly) { End(slot, start); return; }   // ⭐ R-4209 — counted, not timed
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesAtStart;
            End(slot, start);
            t_buffer!.Bytes[slot] += allocated * Math.Max(1, SampleOneIn);
        }

        /// <summary>Adds <paramref name="count"/> to <paramref name="slot"/>'s calls without a time — a counter (cells walked, entries
        /// visited …) beside the timed slots, at the cost of one add.</summary>
        public static void Add(int slot, long count)
        {
            if (!Enabled || count == 0) return;
            var b = t_buffer ?? Register();
            b.Calls[slot] += count;
        }

        private static Buffer Register()
        {
            var b = new Buffer();
            lock (Gate) Buffers.Add(b);
            t_buffer = b;
            return b;
        }

        /// <summary>One slot's totals since the last <see cref="Reset"/>.</summary>
        public readonly record struct Row(string Name, long Calls, double TotalMs, double AvgUs, double MaxUs, long Bytes = 0);

        /// <summary>Every slot with at least one call, the most total time first, and the seconds since the last reset. ⚠ Reads other
        /// threads' counters without a lock — a diagnostic, not an exact cut. Allocates (an API answer, not the hot path).</summary>
        public static (List<Row> Rows, double Seconds) Snapshot()
        {
            var rows = new List<Row>();
            double tickMs = 1000.0 / Stopwatch.Frequency;
            lock (Gate)
            {
                for (int s = 0; s < _slotCount; s++)
                {
                    long calls = 0, ticks = 0, max = 0, bytes = 0;
                    foreach (var b in Buffers) { calls += Volatile.Read(ref b.Calls[s]); ticks += Volatile.Read(ref b.Ticks[s]); max = Math.Max(max, Volatile.Read(ref b.Max[s])); bytes += Volatile.Read(ref b.Bytes[s]); }
                    if (calls == 0) continue;
                    rows.Add(new Row(Names[s], calls, ticks * tickMs, ticks * tickMs * 1000.0 / calls, max * tickMs * 1000.0, bytes));
                }
            }
            rows.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs));
            return (rows, (Stopwatch.GetTimestamp() - Volatile.Read(ref _resetAt)) / (double)Stopwatch.Frequency);
        }

        /// <summary>Zeroes every counter (the slots stay named). ⚠ A call in flight on another thread may land half before, half after.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                foreach (var b in Buffers) { Array.Clear(b.Calls); Array.Clear(b.Ticks); Array.Clear(b.Max); Array.Clear(b.Bytes); }
                Volatile.Write(ref _resetAt, Stopwatch.GetTimestamp());
            }
        }
    }
}
