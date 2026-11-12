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

        /// <summary>⭐ A-3 (threaded-analysis instruments) — named masks to copy as well, read from <c>HROT_PUBLISH_PROBE_MASKS</c>: a JSON object
        /// <c>{"name": ["Full.Type.Name", …]}</c> (made from the A-1 access trace: what the presentation actually reads). Each mask is copied WARM and COLD
        /// like the built-in two; a type the process does not know or has not registered is counted, not fatal.</summary>
        private static readonly string? MasksPath = Environment.GetEnvironmentVariable("HROT_PUBLISH_PROBE_MASKS") is { Length: > 0 } m ? m : null;
        private static List<(string Name, BitMask512 Mask, int Types, int Missing, EntityRepository Warm, EntityRepository Cold)>? _masks;

        /// <summary>⭐ A-3 — other treatments a host wants costed at the probe frame: each gets the LIVE world and the warm ALL-types copy and returns an item
        /// count; the probe times it and counts its allocation. Registered by the host (the editor) — this assembly cannot see the toolkits.</summary>
        public static readonly List<(string Name, Func<EntityRepository, EntityRepository, int> Run)> Extras = new();

        private static List<(string Name, BitMask512 Mask, int Types, int Missing, EntityRepository Warm, EntityRepository Cold)> Masks()
        {
            if (_masks != null) return _masks;
            _masks = new();
            if (MasksPath == null || !File.Exists(MasksPath)) return _masks;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(MasksPath));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var (mask, types, missing) = MaskOf(p.Value);
                _masks.Add((p.Name, mask, types, missing, new EntityRepository(), new EntityRepository()));
            }
            return _masks;
        }

        /// <summary>⭐ POC 2 (CE-4439) — one named mask of a masks file (<c>scripts/access-trace-masks.py</c>): the component ids of the listed types the
        /// process has registered. Null when the file or the name is missing.</summary>
        public static (BitMask512 Mask, int Types, int Missing)? LoadMask(string path, string name)
        {
            if (!File.Exists(path)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty(name, out var list) ? MaskOf(list) : null;
        }

        private static (BitMask512, int, int) MaskOf(System.Text.Json.JsonElement list)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            var mask = new BitMask512();
            int types = 0, missing = 0;
            foreach (var t in list.EnumerateArray())
            {
                string name = t.GetString() ?? "";
                Type? type = null;
                foreach (var a in asms) { type = a.GetType(name, false); if (type != null) break; }
                int id = ComponentTypeRegistry.GetId(type);
                if (id < 0) { missing++; continue; }
                mask.SetBit(id); types++;
            }
            return (mask, types, missing);
        }

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
                foreach (var mk in Masks()) mk.Warm.SyncFrom(live, mk.Mask, includeTransient: true);   // ⭐ A-3
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
            // ⭐ A-3 — the A-1 masks, warm and cold, and the host's extra treatments (timed against the warm ALL-types copy)
            var maskJson = new System.Text.StringBuilder();
            foreach (var mk in Masks())
            {
                var mw = mk.Warm.ProbeSyncFrom(live, mk.Mask, null);
                mk.Cold.SoftClear();
                var mc = mk.Cold.ProbeSyncFrom(live, mk.Mask, null);
                maskJson.Append(string.Create(CultureInfo.InvariantCulture, $",\"mask:{mk.Name}\":{{\"types\":{mk.Types},\"missing\":{mk.Missing},\"warm\":{Json(mw)},\"cold\":{Json(mc)}}}"));
            }
            foreach (var (name, run) in Extras)
            {
                long xa0 = GC.GetAllocatedBytesForCurrentThread();
                var xsw = System.Diagnostics.Stopwatch.StartNew();
                int items;
                try { items = run(live, _allWarm); } catch { items = -1; }   // an instrument never takes the frame down; -1 marks the failure in the JSON
                double xms = xsw.Elapsed.TotalMilliseconds;
                long xal = GC.GetAllocatedBytesForCurrentThread() - xa0;
                maskJson.Append(string.Create(CultureInfo.InvariantCulture, $",\"extra:{name}\":{{\"ms\":{xms:F3},\"allocBytes\":{xal},\"items\":{items}}}"));
            }
            _samples++;
            var (filled, _, names) = live.SingletonCensus();
            using var w = new StreamWriter(Out, append: true);
            w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{{\"frame\":{_frame},\"tick\":{live.SimulationTick},\"entities\":{a.Entities},\"singletons\":{filled},\"prodMs\":{prodMs:F3},\"prodAlloc\":{prodAlloc},\"bgWarm\":{Json(a)},\"bgCold\":{Json(b)},\"allWarm\":{Json(c)},\"allCold\":{Json(d)}{maskJson}}}"));
            if (rows != null)
            {
                foreach (var r in rows.OrderByDescending(r => r.Ms))
                    w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{{\"row\":\"{r.Type}\",\"managed\":{(r.Managed ? "true" : "false")},\"ms\":{r.Ms:F3},\"dirtyChunks\":{r.DirtyChunks},\"bytes\":{r.Bytes},\"entries\":{r.Entries}}}"));
                if (_samples == 1) w.WriteLine("{\"singletonNames\":\"" + string.Join(",", names) + "\"}");
            }
        }

        private static string Json(PublishProbeResult r) => string.Create(CultureInfo.InvariantCulture,
            $"{{\"ms\":{r.TotalMs:F3},\"indexMs\":{r.IndexMs:F3},\"tables\":{r.Tables},\"managedTables\":{r.ManagedTables},\"dirtyBytes\":{r.DirtyBytes},\"committedBytes\":{r.CommittedBytes},\"managedEntries\":{r.ManagedEntries},\"allocBytes\":{r.AllocatedBytes}}}");
    }
}
