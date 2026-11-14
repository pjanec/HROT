using System;
using System.Collections.Generic;

namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// ⭐ M-1 (docs/DESIGN_Parallel_Simulation.md §8.3; the parallel experiment E-1) — the WRITE instrument: per component table, per frame,
    /// how many writable references were handed out (<c>GetRefRW</c>), how many chunks ended the frame stamped (dirty), and how many of those
    /// dirty chunks hold EXACTLY the bytes they held at the previous frame's end (written, nothing changed). ⇒ it names the tables where
    /// check-before-write or quantizing pays (§8.2). Opt-in: <c>HROT_WRITE_STATS=1</c> ONLY, read once — off it costs a static readonly branch the
    /// JIT removes. On, it copies every committed chunk once per frame (an instrument, not a production path).
    /// ⭐ CE-4566 (X-11) — ⛔ <c>HROT_FRAME_STATS=1</c> no longer turns it on: 📐 it cost ~13 ms per tick, so every frame-time measurement (which sets
    /// FRAME_STATS) measured the instrument too. A measuring script sets only what it measures.
    /// </summary>
    public static class TableWriteStats
    {
        public static readonly bool On = SwitchedOn(Environment.GetEnvironmentVariable);

        /// <summary>Is the instrument on under <paramref name="env"/>? Its own switch, <c>HROT_WRITE_STATS=1</c>, and nothing else.</summary>
        public static bool SwitchedOn(Func<string, string?> env) => env("HROT_WRITE_STATS") == "1";

        /// <summary>One table's sample for one frame.</summary>
        public readonly record struct Sample(long Refs, int Dirty, int Identical, int Committed);

        internal interface ISampled { Sample SampleWrites(uint frameVersion); int CommitByContent(uint frameVersion); }

        private sealed class Acc { public long Refs, Dirty, Identical, Committed; }

        // ⭐ POC 2 — HROT_WRITE_STATS_STACKS=<table type name>: every 32nd writable reference of that table records its caller (the first
        //   frames outside Fdp.Core), printed with the report. A diagnostic: it allocates and locks; off unless named.
        private static readonly Dictionary<string, int> _callers = new();
        /// <summary>⭐ POC 3 T-4 — a free-form diagnostic counter printed with the callers (any assembly may name what it saw).</summary>
        public static void Note(string key)
        {
            if (!On) return;
            lock (_callers) _callers[key] = _callers.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        internal static void SampleCaller(string table)
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            var sb = new System.Text.StringBuilder();
            int taken = 0;
            for (int i = 0; i < st.FrameCount && taken < 4; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                string owner = m.DeclaringType?.Name ?? "?";
                if (m.DeclaringType?.Namespace == "Fdp.Core" && taken == 0) continue;   // skip the repository's own accessors
                if (taken++ > 0) sb.Append(" < ");
                sb.Append(owner).Append('.').Append(m.Name);
            }
            string key = sb.ToString();
            lock (_callers) _callers[key] = _callers.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        private static readonly Dictionary<Type, Acc> _acc = new();
        private static int _frames;

        /// <summary>Samples every table of <paramref name="world"/> at the end of a frame; every <paramref name="reportEvery"/> frames prints the
        /// top tables and resets. Instrument only — call it only when <see cref="On"/>.</summary>
        public static void EndFrame(EntityRepository world, int reportEvery = 300)
        {
            uint v = world.GlobalVersion;
            foreach (var (type, table) in world.TablesForStats())
            {
                if (table is not ISampled s) continue;
                var x = s.SampleWrites(v);
                if (!_acc.TryGetValue(type, out var a)) _acc[type] = a = new Acc();
                a.Refs += x.Refs; a.Dirty += x.Dirty; a.Identical += x.Identical; a.Committed += x.Committed;
            }
            if (++_frames < reportEvery) return;
            var rows = new List<(string Name, Acc A)>();
            foreach (var (t, a) in _acc) if (a.Refs > 0 || a.Dirty > 0) rows.Add((t.Name, a));
            rows.Sort((p, q) => q.A.Dirty.CompareTo(p.A.Dirty));
            Console.WriteLine($"[WriteStats] {_frames} frames — per table, per frame: writable refs · dirty chunks / committed · of the dirty, unchanged");
            for (int i = 0; i < rows.Count && i < 25; i++)
            {
                var (n, a) = rows[i];
                Console.WriteLine($"[WriteStats]   {n,-34} refs {a.Refs / (double)_frames,9:0.0} · dirty {a.Dirty / (double)_frames,6:0.0} / {a.Committed / (double)_frames,6:0.0} · unchanged {a.Identical / (double)_frames,6:0.0} ({(a.Dirty == 0 ? 0 : 100.0 * a.Identical / a.Dirty):0}%)");
            }
            lock (_callers)
            {
                if (_callers.Count > 0)
                {
                    var top = new List<KeyValuePair<string, int>>(_callers);
                    top.Sort((p, q) => q.Value.CompareTo(p.Value));
                    Console.WriteLine("[WriteStats] writable-ref callers (sampled 1 in 32) and notes:");
                    for (int i = 0; i < top.Count && i < 30; i++) Console.WriteLine($"[WriteStats]   {top[i].Value,6}  {top[i].Key}");
                    _callers.Clear();
                }
            }
            _acc.Clear(); _frames = 0;
        }
    }

    /// <summary>
    /// ⭐ D-5 (the parallel experiment, E-4; docs/DESIGN_Parallel_Simulation.md §8.2) — COMMIT BY CONTENT, opt-in <c>HROT_CONTENT_VERSION=1</c>:
    /// at each frame's end, a chunk stamped this frame that holds the same bytes as before keeps its previous version. Run BEFORE the M-1 sample,
    /// so M-1's "dirty" then counts real changes only.
    /// </summary>
    public static class ContentVersioning
    {
        /// <summary>Read when a table is CREATED (it then keeps a pre-stamp array); set it before building the world.</summary>
        public static bool On { get; set; } = Environment.GetEnvironmentVariable("HROT_CONTENT_VERSION") == "1";

        /// <summary>The chunks reverted at the last frame's end, all tables.</summary>
        public static int LastReverted { get; private set; }

        public static void EndFrame(EntityRepository world)
        {
            uint v = world.GlobalVersion;
            int n = 0;
            foreach (var (_, table) in world.TablesForStats())
                if (table is TableWriteStats.ISampled s) n += s.CommitByContent(v);
            LastReverted = n;
        }
    }
}
