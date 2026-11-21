using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ CE-4445 M-2 — the RUNTIME detector of an in-place mutation of a managed component (opt-in <c>HROT_MANAGED_MUTATION_CHECK=1</c>).
    /// CLAUDE.md rules a managed component immutable, replaced whole by <c>SetManagedComponent</c>; code that READS one and mutates the object it got
    /// is recorded today only because the read stamps the chunk version (A1-2) — and not at all once reads stop stamping (R-4210).
    /// <para>Each managed component handed out by a READ is fingerprinted (its fields, two levels deep, collections by count and elements) together
    /// with the table's version for that entity and the system reading it. At the end of the kernel frame (<see cref="EndFrame"/>) an object that is
    /// still the one in its slot, whose fingerprint CHANGED while the version did NOT move since the read, is a hit: it was mutated in place and only a
    /// stamping read was recording it. Hits go to <c>HROT_MANAGED_MUTATION_OUT</c>/<c>managed-mutations-&lt;pid&gt;.txt</c> (default the temp folder), one line per (type, reader).</para>
    /// ⛔ An instrument: reflection, allocation — only with the switch ON. OFF: one <c>static readonly bool</c> test per managed read.
    /// </summary>
    public static class ManagedMutationCheck
    {
        public static readonly bool On = EnvVars.Read(EnvVars.HrotManagedMutationCheck) == "1";
        // one file per PROCESS (a cluster runs a process per node): <HROT_MANAGED_MUTATION_OUT or temp>/managed-mutations-<pid>.txt
        private static readonly string OutPath = Path.Combine(
            EnvVars.Read(EnvVars.HrotManagedMutationOut) is { Length: > 0 } o ? o : Path.GetTempPath(),
            $"managed-mutations-{Environment.ProcessId}.txt");

        /// <summary>The system the kernel is executing now (set by the scheduler while the check is on), for the report.</summary>
        [ThreadStatic] public static string? CurrentReader;

        private sealed class Read { public object Obj = null!; public IComponentTable Table = null!; public int Index; public uint Version; public long Print; public string Reader = ""; public Type Type = null!; }
        private static readonly object s_lock = new();
        private static readonly Dictionary<object, Read> s_reads = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<string, (long Count, string Example)> s_hits = new();
        private static long s_checked, s_frames;

        /// <summary>A managed read handed out <paramref name="obj"/> from <paramref name="table"/>'s slot <paramref name="index"/>.</summary>
        public static void OnRead(IComponentTable table, int index, object? obj)
        {
            if (obj == null) return;
            lock (s_lock)
            {
                if (s_reads.ContainsKey(obj)) return;   // the first read of the frame is the baseline
                s_reads[obj] = new Read
                {
                    Obj = obj, Table = table, Index = index, Version = table.GetVersionForEntity(index), Print = Fingerprint(obj, 2),
                    Reader = CurrentReader ?? "(outside a system)", Type = obj.GetType(),
                };
            }
        }

        /// <summary>The kernel frame's end: compare every object read this frame with its baseline.</summary>
        public static void EndFrame()
        {
            if (!On) return;
            EndFrameCore();
        }

        /// <summary>The comparison itself (the rails call it directly: the switch is read once per process).</summary>
        internal static void EndFrameCore()
        {
            lock (s_lock)
            {
                s_frames++;
                foreach (var r in s_reads.Values)
                {
                    s_checked++;
                    object? now;
                    try { now = r.Table.GetRawObject(r.Index); } catch { continue; }
                    if (!ReferenceEquals(now, r.Obj)) continue;                      // replaced whole (or gone): correct use
                    if (r.Table.GetVersionForEntity(r.Index) != r.Version) continue;  // stamped since the read: recorded
                    if (Fingerprint(r.Obj, 2) == r.Print) continue;                   // unchanged
                    string key = r.Type.FullName + (IsTransient(r.Type) ? " [NoReplay: not recorded]" : "") + " | " + r.Reader;
                    s_hits[key] = s_hits.TryGetValue(key, out var h) ? (h.Count + 1, h.Example) : (1, $"entity index {r.Index}");
                }
                s_reads.Clear();
                if (s_frames % 300 == 0) DumpLocked();
            }
        }

        /// <summary>Writes the hits now (also every 300 frames and at exit).</summary>
        public static void Dump() { lock (s_lock) DumpLocked(); }

        private static void DumpLocked()
        {
            var sb = new StringBuilder();
            sb.Append($"# ManagedMutationCheck — {s_hits.Count} distinct (type | reader); {s_frames} frames, {s_checked} reads checked\n");
            sb.Append("# process: ").Append(string.Join(' ', Environment.GetCommandLineArgs())).Append('\n');
            var rows = new List<KeyValuePair<string, (long Count, string Example)>>(s_hits);
            rows.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            foreach (var kv in rows) sb.Append(kv.Value.Count).Append("  ").Append(kv.Key).Append("  (e.g. ").Append(kv.Value.Example).Append(")\n");
            try { Directory.CreateDirectory(Path.GetDirectoryName(OutPath)!); File.WriteAllText(OutPath, sb.ToString()); } catch { /* an instrument never throws */ }
        }

        /// <summary>Distinct hits so far (rails).</summary>
        public static int DistinctHits { get { lock (s_lock) return s_hits.Count; } }

        /// <summary>Forgets everything (rails).</summary>
        public static void Reset() { lock (s_lock) { s_reads.Clear(); s_hits.Clear(); s_checked = 0; s_frames = 0; } }

        private static readonly bool s_exitHooked = HookExit();
        private static bool HookExit()
        {
            if (!On) return false;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dump();
            return true;
        }

        private static readonly Dictionary<Type, bool> s_transient = new();
        private static bool IsTransient(Type t)
        {
            lock (s_transient)
            {
                if (s_transient.TryGetValue(t, out var b)) return b;
                var a = t.GetCustomAttribute<DataPolicyAttribute>();
                b = a != null && (a.Policy & DataPolicy.NoReplay) != 0;   // "not recorded" — NoPreview alone (kept out of the snapshot) IS recorded
                s_transient[t] = b;
                return b;
            }
        }

        // ── the fingerprint: fields, depth-limited; a collection by its count and its elements' prints ──
        private static readonly Dictionary<Type, FieldInfo[]> s_fields = new();

        internal static long Fingerprint(object? o, int depth)
        {
            if (o == null) return 17;
            var t = o.GetType();
            if (t.IsPrimitive || t.IsEnum || o is string || o is decimal || o is DateTime || o is Guid) return o.GetHashCode();
            // past the depth limit: a reference by identity; a VALUE by its value (a boxed struct is a new box per read — its identity means nothing)
            if (depth < 0) return t.IsValueType ? o.GetHashCode() : RuntimeHelpers.GetHashCode(o);
            unchecked
            {
                long h = 1469598103934665603;
                if (o is IEnumerable e && o is not string)
                {
                    int n = 0;
                    foreach (var x in e) { h = (h ^ Fingerprint(x, depth - 1)) * 1099511628211; if (++n > 4096) break; }
                    return (h ^ n) * 1099511628211;
                }
                if (t.IsValueType && !t.IsPrimitive && !HasRefOrMutableFields(t)) return o.GetHashCode();
                foreach (var f in FieldsOf(t))
                {
                    object? v;
                    try { v = f.GetValue(o); } catch { continue; }
                    h = (h ^ Fingerprint(v, depth - 1)) * 1099511628211;
                }
                return h;
            }
        }

        private static bool HasRefOrMutableFields(Type t) => FieldsOf(t).Length > 0 && Array.Exists(FieldsOf(t), f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum);

        private static FieldInfo[] FieldsOf(Type t)
        {
            lock (s_fields)
            {
                if (s_fields.TryGetValue(t, out var f)) return f;
                var list = new List<FieldInfo>();
                for (var ty = t; ty != null && ty != typeof(object); ty = ty.BaseType)
                    foreach (var fi in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!fi.FieldType.IsPointer && !typeof(Delegate).IsAssignableFrom(fi.FieldType)) list.Add(fi);
                f = list.ToArray();
                s_fields[t] = f;
                return f;
            }
        }
    }
}
