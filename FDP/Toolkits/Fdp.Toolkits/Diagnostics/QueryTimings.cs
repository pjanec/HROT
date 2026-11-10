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

        /// <summary>The start of a timed call (0 when disabled). Pair with <see cref="End"/>.</summary>
        public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>Records one call of <paramref name="slot"/> that began at <paramref name="start"/>.</summary>
        public static void End(int slot, long start)
        {
            if (start == 0L) return;
            long dt = Stopwatch.GetTimestamp() - start;
            var b = t_buffer ?? Register();
            b.Calls[slot]++;
            b.Ticks[slot] += dt;
            if (dt > b.Max[slot]) b.Max[slot] = dt;
        }

        private static Buffer Register()
        {
            var b = new Buffer();
            lock (Gate) Buffers.Add(b);
            t_buffer = b;
            return b;
        }

        /// <summary>One slot's totals since the last <see cref="Reset"/>.</summary>
        public readonly record struct Row(string Name, long Calls, double TotalMs, double AvgUs, double MaxUs);

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
                    long calls = 0, ticks = 0, max = 0;
                    foreach (var b in Buffers) { calls += Volatile.Read(ref b.Calls[s]); ticks += Volatile.Read(ref b.Ticks[s]); max = Math.Max(max, Volatile.Read(ref b.Max[s])); }
                    if (calls == 0) continue;
                    rows.Add(new Row(Names[s], calls, ticks * tickMs, ticks * tickMs * 1000.0 / calls, max * tickMs * 1000.0));
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
                foreach (var b in Buffers) { Array.Clear(b.Calls); Array.Clear(b.Ticks); Array.Clear(b.Max); }
                Volatile.Write(ref _resetAt, Stopwatch.GetTimestamp());
            }
        }
    }
}
