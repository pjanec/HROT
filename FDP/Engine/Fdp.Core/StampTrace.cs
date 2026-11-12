using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ CE-4445 M-4 — WHO stamps a table's chunk version (opt-in <c>HROT_STAMP_TRACE=DescriptorOwnership,NetworkAuthority</c>, type short names).
    /// Every stamp of a traced type is counted by the running system (<see cref="ManagedMutationCheck.CurrentReader"/>, set by the scheduler
    /// while this or the mutation check is on), or — outside a system — by the first calling method outside the table code. Written to
    /// <c>HROT_MANAGED_MUTATION_OUT</c>/<c>stamp-trace-&lt;pid&gt;.txt</c> every 300 frames and at exit.
    /// ⛔ An instrument (a stack walk outside systems): only with the switch on. OFF: one <c>static readonly bool</c> test per stamp.
    /// </summary>
    public static class StampTrace
    {
        private static readonly HashSet<string> s_types = Parse(Environment.GetEnvironmentVariable("HROT_STAMP_TRACE"));
        public static readonly bool On = s_types.Count > 0;
        private static readonly string OutPath = Path.Combine(
            Environment.GetEnvironmentVariable("HROT_MANAGED_MUTATION_OUT") is { Length: > 0 } o ? o : Path.GetTempPath(),
            $"stamp-trace-{Environment.ProcessId}.txt");
        private static readonly object s_lock = new();
        private static readonly Dictionary<string, long> s_counts = new();
        private static readonly Dictionary<Type, bool> s_traced = new();
        private static long s_frames;

        private static HashSet<string> Parse(string? v)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(v)) return set;
            foreach (var s in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) set.Add(s);
            return set;
        }

        /// <summary>A chunk of <paramref name="t"/>'s table was stamped.</summary>
        public static void Note(Type t)
        {
            lock (s_lock)
            {
                if (!s_traced.TryGetValue(t, out bool traced)) s_traced[t] = traced = s_types.Contains(t.Name);
                if (!traced) return;
                string who = ManagedMutationCheck.CurrentReader ?? ("(outside a system) " + Caller());
                string key = t.Name + " | " + who;
                s_counts[key] = s_counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }
        }

        private static string Caller()
        {
            var st = new StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 12; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                var dt = m?.DeclaringType;
                if (dt == null) continue;
                string ns = dt.Namespace ?? "";
                if (dt.Name.Contains("ComponentTable") || dt.Name.Contains("NativeChunkTable") || dt.Name.StartsWith("EntityRepository")
                    || dt.Name.Contains("UnsafeShim") || dt.Name.Contains("Accessor") || dt.Name == nameof(StampTrace)) continue;
                return dt.FullName + "." + m!.Name;
            }
            return "?";
        }

        /// <summary>The kernel frame's end.</summary>
        public static void EndFrame()
        {
            lock (s_lock) { if (++s_frames % 300 == 0) DumpLocked(); }
        }

        public static void Dump() { lock (s_lock) DumpLocked(); }

        private static void DumpLocked()
        {
            var sb = new StringBuilder();
            sb.Append($"# StampTrace — {s_frames} frames; types: {string.Join(",", s_types)}\n");
            sb.Append("# process: ").Append(string.Join(' ', Environment.GetCommandLineArgs())).Append('\n');
            var rows = new List<KeyValuePair<string, long>>(s_counts);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in rows) sb.Append(kv.Value).Append("  ").Append(kv.Key).Append('\n');
            try { Directory.CreateDirectory(Path.GetDirectoryName(OutPath)!); File.WriteAllText(OutPath, sb.ToString()); } catch { }
        }

        private static readonly bool s_exitHooked = HookExit();
        private static bool HookExit()
        {
            if (!On) return false;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dump();
            return true;
        }
    }
}
