using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Fdp.Core;

namespace Fdp.ModuleHost
{
    /// <summary>
    /// ⭐ EXPERIMENT (threaded kernel X-1, exp/threaded-kernel; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §11 F-5) — what a PUBLISHED
    /// snapshot of the live world costs in the real editor. Opt-in: <c>HROT_PUBLISH_PROBE=1</c> (every 30th frame; <c>HROT_PUBLISH_PROBE_EVERY</c>),
    /// JSON lines to <c>HROT_PUBLISH_PROBE_OUT</c> (default the temp folder). Four copies of World A each probe: the background modules' mask
    /// (snapshotable) and ALL types (incl. transient / NoPreview), each WARM (a kept repository, only dirty chunks) and COLD (the repository
    /// <c>SoftClear</c>ed first, as <c>SnapshotPool.Return</c> leaves it). ⛔ An instrument, not a production path: it allocates and it costs.
    /// </summary>
    public static class PublishProbe
    {
        private static readonly bool On = Environment.GetEnvironmentVariable("HROT_PUBLISH_PROBE") == "1";
        private static readonly int Every = int.TryParse(Environment.GetEnvironmentVariable("HROT_PUBLISH_PROBE_EVERY"), out var e) && e > 0 ? e : 30;
        private static readonly string Out = Environment.GetEnvironmentVariable("HROT_PUBLISH_PROBE_OUT") is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "hrot-publish-probe.jsonl");
        private static EntityRepository? _bgWarm, _bgCold, _allWarm, _allCold, _prod;
        private static long _frame;
        private static int _samples;

        public static bool Enabled => On;

        /// <summary>Called by a host once per frame after its node frame, on the thread that owns <paramref name="live"/>.</summary>
        public static void AfterFrame(EntityRepository? live)
        {
            if (!On || live == null) return;
            ++_frame;
            _bgWarm ??= new EntityRepository(); _bgCold ??= new EntityRepository(); _allWarm ??= new EntityRepository(); _allCold ??= new EntityRepository();
            if (_frame % Every == Every - 1)
            {
                // ⭐ the frame BEFORE a probe: bring the warm copies up to date, so the probe's warm copy carries ONE tick's changes (a publish every tick)
                _bgWarm.SyncFrom(live, live.GetSnapshotableMask(false), includeTransient: true);
                _allWarm.SyncFrom(live, live.GetSnapshotableMask(true), includeTransient: true);
                return;
            }
            if (_frame % Every != 0) return;
            // ⭐ the production call, untouched (what OnDemandProvider does): SyncFrom + CopyClockFrom into a kept repository
            _prod ??= new EntityRepository();
            long pa0 = GC.GetAllocatedBytesForCurrentThread();
            var psw = System.Diagnostics.Stopwatch.StartNew();
            _prod.SyncFrom(live);
            _prod.CopyClockFrom(live);
            double prodMs = psw.Elapsed.TotalMilliseconds;
            long prodAlloc = GC.GetAllocatedBytesForCurrentThread() - pa0;
            var bg = live.GetSnapshotableMask(false);
            var all = live.GetSnapshotableMask(true);
            var rows = _samples % 10 == 0 ? new List<PublishProbeRow>() : null;
            var a = _bgWarm.ProbeSyncFrom(live, bg, null);
            _bgCold.SoftClear();
            var b = _bgCold.ProbeSyncFrom(live, bg, null);
            var c = _allWarm.ProbeSyncFrom(live, all, rows);
            _allCold.SoftClear();
            var d = _allCold.ProbeSyncFrom(live, all, null);
            _samples++;
            var (filled, _, names) = live.SingletonCensus();
            using var w = new StreamWriter(Out, append: true);
            w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{{\"frame\":{_frame},\"tick\":{live.SimulationTick},\"entities\":{a.Entities},\"singletons\":{filled},\"prodMs\":{prodMs:F3},\"prodAlloc\":{prodAlloc},\"bgWarm\":{Json(a)},\"bgCold\":{Json(b)},\"allWarm\":{Json(c)},\"allCold\":{Json(d)}}}"));
            if (rows != null)
            {
                foreach (var r in rows.OrderByDescending(r => r.Ms).Take(40))
                    w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{{\"row\":\"{r.Type}\",\"managed\":{(r.Managed ? "true" : "false")},\"ms\":{r.Ms:F3},\"dirtyChunks\":{r.DirtyChunks},\"bytes\":{r.Bytes},\"entries\":{r.Entries}}}"));
                if (_samples == 1) w.WriteLine("{\"singletonNames\":\"" + string.Join(",", names) + "\"}");
            }
        }

        private static string Json(PublishProbeResult r) => string.Create(CultureInfo.InvariantCulture,
            $"{{\"ms\":{r.TotalMs:F3},\"indexMs\":{r.IndexMs:F3},\"tables\":{r.Tables},\"managedTables\":{r.ManagedTables},\"dirtyBytes\":{r.DirtyBytes},\"committedBytes\":{r.CommittedBytes},\"managedEntries\":{r.ManagedEntries},\"allocBytes\":{r.AllocatedBytes}}}");
    }
}
