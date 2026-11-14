using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <b>CE-4456 N-1 — memory per node, by owner</b> (📄 docs/blueprints/batches/STATUS_Memory_And_Contact.md). Opt-in
    /// <c>HROT_MEMORY_CENSUS=1</c>: every <see cref="EntityRepository"/> is tracked from its construction (a weak reference + its CREATOR, the
    /// first two frames of the constructing stack outside <c>EntityRepository</c>) — so the live world S, the presentation world A, the replica R,
    /// the last-tick copies, the snapshot pool's worlds and every other world are told apart by who made them. Other owners register a size
    /// function (<see cref="RegisterOwner"/>: the undo ring). Every 5 s a JSON goes to <c>HROT_MEMORY_CENSUS_OUT</c> (default the temp folder)
    /// + <c>.&lt;pid&gt;.json</c>: the process (working set, peak, GC heap, GC committed), and per creator the worlds' count, entities, NATIVE
    /// committed bytes (component chunks + the entity index) and MANAGED slot bytes (the reference arrays of managed tables — the objects
    /// themselves are in the GC heap, not attributed).
    /// ⛔ An instrument: off, the one hook is a <c>static readonly</c> test the JIT folds away.
    /// </summary>
    public static class MemoryCensus
    {
        /// <summary>The switch: <c>HROT_MEMORY_CENSUS=1</c>. Read once.</summary>
        public static readonly bool On = EnvVars.Read(EnvVars.HrotMemoryCensus) == "1";

        private static readonly string OutPath = (EnvVars.Read(EnvVars.HrotMemoryCensusOut) is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "hrot-memory-census")) + "." + Environment.ProcessId + ".json";

        private static readonly object s_lock = new();
        private static readonly List<(WeakReference<EntityRepository> Repo, string Creator)> s_repos = new();
        private static readonly List<(string Name, WeakReference<object> Who, Func<object, long> Bytes)> s_owners = new();
        private static Thread? s_pump;

        /// <summary>A world was constructed (called from the constructor while <see cref="On"/>).</summary>
        internal static void Track(EntityRepository repo)
        {
            string creator = Creator();
            lock (s_lock) s_repos.Add((new WeakReference<EntityRepository>(repo), creator));
            EnsurePump();
        }

        /// <summary>An owner that is not a world reports its size (e.g. the undo ring's used bytes).</summary>
        public static void RegisterOwner(string name, object who, Func<object, long> bytes)
        {
            if (!On) return;
            lock (s_lock) s_owners.Add((name, new WeakReference<object>(who), bytes));
            EnsurePump();
        }

        private static string Creator()
        {
            var st = new StackTrace(2, false);
            var parts = new List<string>(2);
            for (int i = 0; i < st.FrameCount && parts.Count < 2; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                var t = m?.DeclaringType;
                if (t == null || t == typeof(EntityRepository) || t == typeof(MemoryCensus)) continue;
                var name = t.Name;
                int lt = name.IndexOf('<'); if (lt > 0) name = name[..lt];
                if (t.DeclaringType != null) name = t.DeclaringType.Name + "." + name;
                parts.Add(name + "." + m!.Name);
            }
            return parts.Count == 0 ? "(unknown)" : string.Join(" <- ", parts);
        }

        private static void EnsurePump()
        {
            if (s_pump != null) return;
            lock (s_lock)
            {
                if (s_pump != null) return;
                s_pump = new Thread(() => { while (true) { Thread.Sleep(5000); try { Write(); } catch { } } })
                { IsBackground = true, Name = "MemoryCensus", Priority = ThreadPriority.BelowNormal };
                s_pump.Start();
            }
        }

        /// <summary>Write the census now (also called by the pump every 5 s).</summary>
        public static void Write()
        {
            var by = new Dictionary<string, (int Count, long Entities, long Native, long Managed, int Tables)>();
            List<(WeakReference<EntityRepository>, string)> repos;
            List<(string, WeakReference<object>, Func<object, long>)> owners;
            lock (s_lock) { s_repos.RemoveAll(r => !r.Repo.TryGetTarget(out _)); repos = new(s_repos); owners = new(s_owners); }
            foreach (var (wr, creator) in repos)
            {
                if (!wr.TryGetTarget(out var repo)) continue;
                try
                {
                    var f = repo.MemoryFootprint();
                    by.TryGetValue(creator, out var a);
                    by[creator] = (a.Count + 1, a.Entities + f.Entities, a.Native + f.Native, a.Managed + f.ManagedSlots, a.Tables + f.Tables);
                }
                catch { /* a world being disposed */ }
            }
            using var p = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            var args = Environment.GetCommandLineArgs(); int mi = Array.IndexOf(args, "--mode");
            string mode = mi >= 0 && mi + 1 < args.Length ? args[mi + 1] : "(none)";
            sb.Append("{\"pid\":").Append(Environment.ProcessId).Append(",\"mode\":\"").Append(mode).Append('"')
              .Append(",\"process\":{\"workingSet\":").Append(p.WorkingSet64).Append(",\"peakWorkingSet\":").Append(p.PeakWorkingSet64)
              .Append(",\"privateBytes\":").Append(p.PrivateMemorySize64).Append(",\"gcHeap\":").Append(gc.HeapSizeBytes)
              .Append(",\"gcCommitted\":").Append(gc.TotalCommittedBytes).Append(",\"gcTotalMemory\":").Append(GC.GetTotalMemory(false)).Append("},\"worlds\":[");
            bool first = true;
            foreach (var kv in by)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"creator\":\"").Append(kv.Key.Replace("\"", "'")).Append("\",\"count\":").Append(kv.Value.Count)
                  .Append(",\"entities\":").Append(kv.Value.Entities).Append(",\"tables\":").Append(kv.Value.Tables)
                  .Append(",\"nativeBytes\":").Append(kv.Value.Native).Append(",\"managedSlotBytes\":").Append(kv.Value.Managed).Append('}');
            }
            sb.Append("],\"owners\":[");
            first = true;
            foreach (var (name, who, bytes) in owners)
            {
                if (!who.TryGetTarget(out var o)) continue;
                long b; try { b = bytes(o); } catch { continue; }
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"name\":\"").Append(name).Append("\",\"bytes\":").Append(b.ToString(inv)).Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(OutPath, sb.ToString());
        }
    }
}
