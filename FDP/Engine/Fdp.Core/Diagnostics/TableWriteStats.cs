using System;
using System.Collections.Generic;

namespace Fdp.Core.Diagnostics
{
    /// <summary>
    /// ⭐ M-1 (docs/DESIGN_Parallel_Simulation.md §8.3; the parallel experiment E-1) — the WRITE instrument: per component table, per frame,
    /// how many writable references were handed out (<c>GetRefRW</c>), how many chunks ended the frame stamped (dirty), and how many of those
    /// dirty chunks hold EXACTLY the bytes they held at the previous frame's end (written, nothing changed). ⇒ it names the tables where
    /// check-before-write or quantizing pays (§8.2). Opt-in: <c>HROT_WRITE_STATS=1</c> or <c>HROT_FRAME_STATS=1</c>, read once — off it costs a
    /// static readonly branch the JIT removes. On, it copies every committed chunk once per frame (an instrument, not a production path).
    /// </summary>
    public static class TableWriteStats
    {
        public static readonly bool On =
            Environment.GetEnvironmentVariable("HROT_WRITE_STATS") == "1" || Environment.GetEnvironmentVariable("HROT_FRAME_STATS") == "1";

        /// <summary>One table's sample for one frame.</summary>
        public readonly record struct Sample(long Refs, int Dirty, int Identical, int Committed);

        internal interface ISampled { Sample SampleWrites(uint frameVersion); }

        private sealed class Acc { public long Refs, Dirty, Identical, Committed; }
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
            _acc.Clear(); _frames = 0;
        }
    }
}
