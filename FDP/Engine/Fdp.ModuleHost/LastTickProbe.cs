using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;

namespace Fdp.ModuleHost
{
    /// <summary>
    /// ⭐ N-5 of the cross-entity access census (docs/DESIGN_Parallel_Simulation.md §18) — what a LAST-TICK view would cost: at each kernel frame start,
    /// copy the live world's tables of the given types into a shadow world (the view a per-entity job would read other entities from), and — on the
    /// alternate frame — every snapshotable table (the replica-sized copy, for comparison). Opt-in <c>HROT_LASTTICK_TYPES=Name1,Name2,…</c> (short or full
    /// type names); prints <c>[LASTTICK]</c> means every 100 frames. ⛔ An instrument: it adds the copy to the frame it measures; nothing reads the shadow.
    /// </summary>
    public static class LastTickProbe
    {
        private static readonly string? Spec = Environment.GetEnvironmentVariable("HROT_LASTTICK_TYPES");
        public static readonly bool On = !string.IsNullOrEmpty(Spec);

        private static EntityRepository? _masked, _full;
        private static BitMask512 _mask;
        private static int _types;
        private static bool _built;
        private static long _frame;
        private static double _maskedSum, _fullSum; private static int _maskedN, _fullN;

        public static void BeforeFrame(EntityRepository live)
        {
            if (!On) return;
            if (!_built) Build();
            var sw = Stopwatch.StartNew();
            if ((_frame++ & 1) == 0) { (_masked ??= new EntityRepository()).SyncFrom(live, _mask); _maskedSum += sw.Elapsed.TotalMilliseconds; _maskedN++; }
            else { (_full ??= new EntityRepository()).SyncFrom(live); _fullSum += sw.Elapsed.TotalMilliseconds; _fullN++; }
            if (_frame % 100 == 0 && _maskedN > 0 && _fullN > 0)
            {
                Console.WriteLine($"[LASTTICK] {_types} types: masked copy mean {_maskedSum / _maskedN:F2} ms · full (snapshotable) copy mean {_fullSum / _fullN:F2} ms "
                                  + $"({_maskedN}+{_fullN} frames) · managed heap {GC.GetTotalMemory(false) / 1048576.0:F0} MB");
                _maskedSum = _fullSum = 0; _maskedN = _fullN = 0;
            }
        }

        private static void Build()
        {
            _built = true;
            var want = new HashSet<string>(Spec!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var found = new List<string>();
            for (int id = 0; id < 512; id++)
            {
                var t = ComponentTypeRegistry.GetType(id);
                if (t == null) continue;
                if (want.Contains(t.Name) || (t.FullName != null && want.Contains(t.FullName))) { _mask.SetBit(id); found.Add(t.Name); }
            }
            _types = found.Count;
            Console.WriteLine($"[LASTTICK] the last-tick set: {found.Count} of {want.Count} named types found: {string.Join(", ", found)}");
        }
    }
}
