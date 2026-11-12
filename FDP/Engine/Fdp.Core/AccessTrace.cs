using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>Which half of the frame an access ran in (⭐ A-1: 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §13).</summary>
    public enum TraceContext : byte
    {
        /// <summary>A thread nobody tagged — the HTTP API, a pool thread, startup.</summary>
        Other = 0,
        /// <summary>The node frame / tick: the kernel, its systems and modules, the afterKernel hooks, the orchestrator core.</summary>
        Tick = 1,
        /// <summary>The presentation half: ImGui windows, map layers, the 3-D view, input, the editor's own per-frame logic.</summary>
        Presentation = 2,
        /// <summary>Scope argument only: keep the context the thread already has.</summary>
        Inherit = 255,
    }

    /// <summary>What an access did. A scope-less (no reader) access lands in reader "(none)".</summary>
    public enum TraceKind : byte
    {
        Read, Write, StructAdd, StructRemove, Has,
        SingletonGet, SingletonSet, SingletonHas, SingletonEnumerate,
        EntityCreate, EntityDestroy,
        BusPublish, BusRead, BusSwap,
        QueryInclude, QueryExclude,
        TryGetRW, RawRead, RawWrite, TableAccess, EntityIndexRead, WorldCopy,
        /// <summary>A READ that takes the RW path and so bumps the table's chunk version — a managed <c>GetComponentRO</c> / <c>GetComponent</c> does (<c>UnsafeShim.ManagedAccessor.GetRO</c> returns the RW reference).</summary>
        ReadViaRW,
    }

    /// <summary>
    /// ⭐ <b>A-1 — the runtime access tracer</b> (📄 docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Threaded_Analysis_Instruments.md, CE-4436).
    /// Opt-in, <c>HROT_ACCESS_TRACE=1</c>. Every access to a world — component read / write by type, singleton get / set, entity create / destroy,
    /// event-bus publish / read, query inclusion — is counted under (context, reader, kind, type, repository, phase): the CONTEXT is the half of
    /// the frame (<see cref="TraceContext"/>), the READER a scope name (a system, a panel, a layer, a gizmo rule …) kept on a per-thread stack.
    /// JSON is written every 5 s, at exit and on a <c>dump</c> command to <c>HROT_ACCESS_TRACE_OUT</c> (default the temp folder); a driver steers
    /// the run through the control file <c>HROT_ACCESS_TRACE_CTL</c> (default <c>&lt;out&gt;.ctl</c>): lines <c>phase NAME</c> · <c>dump</c> · <c>reset</c>.
    /// ⛔ An instrument: with the switch OFF every hook is <c>if (AccessTrace.On)</c> on a <c>static readonly bool</c> the JIT folds away (0 B, ~0 ns);
    /// ON it is slow and allocates (a scope concatenates its name). It changes no behaviour.
    /// </summary>
    public static class AccessTrace
    {
        /// <summary>The switch: <c>HROT_ACCESS_TRACE=1</c>. Read once.</summary>
        public static readonly bool On = Environment.GetEnvironmentVariable("HROT_ACCESS_TRACE") == "1";

        private static readonly string OutPath = Environment.GetEnvironmentVariable("HROT_ACCESS_TRACE_OUT") is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "hrot-access-trace.json");
        private static readonly string CtlPath = Environment.GetEnvironmentVariable("HROT_ACCESS_TRACE_CTL") is { Length: > 0 } c ? c : OutPath + ".ctl";

        // ── the key: meta = ctx(2) | kind(6) | reader(20) | repo(12) | phase(12) ──
        private const int ReaderBits = 20, RepoBits = 12, PhaseBits = 12;
        private static long Meta(TraceContext ctx, TraceKind kind, int reader, int repo, int phase)
            => (long)ctx | ((long)kind << 2) | ((long)reader << 8) | ((long)(repo & ((1 << RepoBits) - 1)) << (8 + ReaderBits)) | ((long)phase << (8 + ReaderBits + RepoBits));

        private sealed class Agg
        {
            public readonly Dictionary<(long, object), long> Map = new();
            public readonly Dictionary<long, long> Entries = new();   // scope entries: (ctx, reader, phase)
        }

        private static readonly ConcurrentBag<Agg> s_aggs = new();
        [ThreadStatic] private static Agg? t_agg;
        [ThreadStatic] private static int t_muted;

        // ⭐ the reader / context stack is an immutable linked list in an AsyncLocal, not a [ThreadStatic] array: it FLOWS with the
        //   ExecutionContext into Parallel.For workers, Task.Run and new threads started from inside a scope — a system's parallel loop
        //   keeps its reader (a thread-static would have dropped every worker's accesses into "(none)").
        private sealed class Node
        {
            public readonly Node? Parent; public readonly int Reader; public readonly TraceContext Ctx;
            public Node(Node? parent, int reader, TraceContext ctx) { Parent = parent; Reader = reader; Ctx = ctx; }
        }
        private static readonly AsyncLocal<Node?> s_node = new();
        private static TraceContext CurrentContext => s_node.Value?.Ctx ?? TraceContext.Other;

        private static readonly object s_nameLock = new();
        private static readonly Dictionary<string, int> s_readerIds = new() { ["(none)"] = 0 };
        private static readonly List<string> s_readerNames = new() { "(none)" };
        private static readonly Dictionary<string, int> s_phaseIds = new() { ["(start)"] = 0 };
        private static readonly List<string> s_phaseNames = new() { "(start)" };
        private static readonly ConcurrentDictionary<(Type, string), string> s_typeNames = new();
        private static readonly HashSet<string> s_declared = new();
        private static volatile int s_phase;
        private static int s_repoSeq, s_busSeq;
        private static readonly List<string> s_repoLegend = new();
        private static readonly List<string> s_busLegend = new();

        // ───────────────────────── scopes ─────────────────────────

        /// <summary>A pushed reader (and optionally context); <c>using</c> it pops. The <c>default</c> value (switch Off) is a no-op.</summary>
        public readonly struct ScopeHandle : IDisposable
        {
            private readonly object? _prev; private readonly bool _live;
            internal ScopeHandle(object? prev) { _prev = prev; _live = true; }
            public void Dispose() { if (_live) s_node.Value = (Node?)_prev; }
        }

        /// <summary>Pushes the reader <paramref name="name"/> (and, unless <see cref="TraceContext.Inherit"/>, the context) until the handle is disposed.</summary>
        public static ScopeHandle Scope(string name, TraceContext ctx = TraceContext.Inherit)
        {
            if (!On) return default;
            return Push(Intern(name), ctx);
        }

        /// <summary>A reader named <paramref name="prefix"/> + the type name of <paramref name="who"/>.</summary>
        public static ScopeHandle Scope(object? who, string prefix, TraceContext ctx = TraceContext.Inherit)
        {
            if (!On) return default;
            return Push(Intern(who == null ? prefix + "(null)" : prefix + TypeName(who.GetType(), prefix)), ctx);
        }

        /// <summary>Sets the context only (no reader).</summary>
        public static ScopeHandle Context(TraceContext ctx)
        {
            if (!On) return default;
            var prev = s_node.Value;
            s_node.Value = new Node(prev, prev?.Reader ?? 0, ctx == TraceContext.Inherit ? prev?.Ctx ?? TraceContext.Other : ctx);
            return new ScopeHandle(prev);
        }

        private static ScopeHandle Push(int reader, TraceContext ctx)
        {
            var prev = s_node.Value;
            var ctxNow = ctx == TraceContext.Inherit ? prev?.Ctx ?? TraceContext.Other : ctx;
            s_node.Value = new Node(prev, reader, ctxNow);
            var a = t_agg ?? NewAgg();
            long key = Meta(ctxNow, 0, reader, 0, s_phase);
            lock (a) { a.Entries[key] = a.Entries.TryGetValue(key, out var n) ? n + 1 : 1; }
            return new ScopeHandle(prev);
        }

        private static int CurrentReader() => s_node.Value?.Reader ?? 0;

        private static string TypeName(Type t, string prefix) => s_typeNames.GetOrAdd((t, prefix), static k => k.Item1.Name);

        private static int Intern(string name)
        {
            lock (s_nameLock)
            {
                if (s_readerIds.TryGetValue(name, out var id)) return id;
                id = s_readerNames.Count;
                if (id >= (1 << ReaderBits)) return 0;
                s_readerNames.Add(name); s_readerIds[name] = id;
                return id;
            }
        }

        /// <summary>Registers a reader that EXISTS (a window the editor offers) so the report can list those the run never entered.</summary>
        public static void Declare(string name) { if (!On) return; lock (s_nameLock) s_declared.Add(name); }

        /// <summary>Records ONE event of <paramref name="kind"/> and hides the accesses made inside until the handle is disposed (an access that calls another hooked access).</summary>
        public static MuteHandle MuteScope(TraceKind kind, Type type, int repo)
        {
            RecordObj(kind, type, repo);
            t_muted++;
            return new MuteHandle(true);
        }

        public readonly struct MuteHandle : IDisposable
        {
            private readonly bool _live;
            internal MuteHandle(bool live) { _live = live; }
            public void Dispose() { if (_live) Unmute(); }
        }

        /// <summary>Hides the accesses made inside until <see cref="Unmute"/> (a TryGet that calls RW inside is ONE event, recorded by the outer).</summary>
        public static void Mute() { t_muted++; }
        public static void Unmute() { if (t_muted > 0) t_muted--; }

        // ───────────────────────── identities ─────────────────────────

        /// <summary>A new repository id (1-based). ⛔ ON only.</summary>
        public static int NextRepoId()
        {
            int id = Interlocked.Increment(ref s_repoSeq);
            var sb = new StringBuilder();
            int taken = 0;
            foreach (var f in new System.Diagnostics.StackTrace(1, false).GetFrames())   // who built it: the first frames outside EntityRepository
            {
                var m = f.GetMethod();
                if (m?.DeclaringType == typeof(EntityRepository) || m?.DeclaringType == typeof(AccessTrace)) continue;
                sb.Append(taken++ > 0 ? " < " : "").Append(m?.DeclaringType?.Name).Append('.').Append(m?.Name);
                if (taken >= 4) break;
            }
            lock (s_nameLock) s_repoLegend.Add($"r{id}: built by {sb} (context {CurrentContext}, reader {s_readerNames[CurrentReader()]}, thread {Environment.CurrentManagedThreadId})");
            return id;
        }

        private static readonly Dictionary<int, WeakReference<EntityRepository>> s_repoRefs = new();

        /// <summary>Remembers repository <paramref name="id"/> so the dump can say how many entities it holds. ⛔ ON only.</summary>
        public static void RegisterRepo(int id, EntityRepository repo) { lock (s_nameLock) s_repoRefs[id] = new WeakReference<EntityRepository>(repo); }

        /// <summary>A new event-bus id (1-based). ⛔ ON only.</summary>
        public static int NextBusId()
        {
            int id = Interlocked.Increment(ref s_busSeq);
            lock (s_nameLock) s_busLegend.Add($"bus{id}: free-standing, created in {CurrentContext} by {s_readerNames[CurrentReader()]}");
            return id;
        }

        /// <summary>Says bus <paramref name="busId"/> is repository <paramref name="repoId"/>'s own. ⛔ ON only.</summary>
        public static void LabelBus(int busId, int repoId)
        {
            lock (s_nameLock) if (busId >= 1 && busId <= s_busLegend.Count) s_busLegend[busId - 1] = $"bus{busId}: world r{repoId}'s own bus (created in {CurrentContext} by {s_readerNames[CurrentReader()]})";
        }

        // ───────────────────────── recording ─────────────────────────

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Record<T>(TraceKind kind, int repo) => RecordObj(kind, typeof(T), repo);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RecordType(TraceKind kind, Type? type, int repo) => RecordObj(kind, (object?)type ?? "(unknown type)", repo);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RecordName(TraceKind kind, string name, int repo) => RecordObj(kind, name, repo);

        /// <summary>By component id (the raw / reflection paths).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RecordId(TraceKind kind, int typeId, int repo)
            => RecordObj(kind, (object?)ComponentTypeRegistry.GetType(typeId) ?? "(component id " + typeId + ")", repo);

        /// <summary>One record per set bit of a query mask.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RecordMask(TraceKind kind, in BitMask512 mask, int repo)
        {
            for (int id = 0; id < 512; id++)
                if (mask.IsSet(id)) RecordId(kind, id, repo);
        }

        private static void RecordObj(TraceKind kind, object key, int repo)
        {
            if (t_muted > 0) return;
            var a = t_agg ?? NewAgg();
            var k = (Meta(CurrentContext, kind, CurrentReader(), repo, s_phase), key);
            lock (a) a.Map[k] = a.Map.TryGetValue(k, out var n) ? n + 1 : 1;
        }

        private static Agg NewAgg() { var a = new Agg(); t_agg = a; s_aggs.Add(a); return a; }

        // ───────────────────────── control ─────────────────────────

        /// <summary>Starts a named phase of the run: later accesses are counted under it.</summary>
        public static void SetPhase(string name)
        {
            if (!On) return;
            lock (s_nameLock)
            {
                if (!s_phaseIds.TryGetValue(name, out var id))
                {
                    id = s_phaseNames.Count;
                    if (id >= (1 << PhaseBits)) id = 0; else { s_phaseNames.Add(name); s_phaseIds[name] = id; }
                }
                s_phase = id;
            }
        }

        /// <summary>Drops every count (the names and the phase stay).</summary>
        public static void Reset()
        {
            foreach (var a in s_aggs) lock (a) { a.Map.Clear(); a.Entries.Clear(); }
        }

        private static DateTime s_ctlStamp;
        private static void PumpLoop()
        {
            long lastDump = Environment.TickCount64;
            while (true)
            {
                Thread.Sleep(200);
                try
                {
                    var info = new FileInfo(CtlPath);
                    if (info.Exists && info.LastWriteTimeUtc != s_ctlStamp)
                    {
                        s_ctlStamp = info.LastWriteTimeUtc;
                        foreach (var line in File.ReadAllLines(CtlPath))
                        {
                            var t = line.Trim();
                            if (t.StartsWith("phase ", StringComparison.Ordinal)) SetPhase(t.Substring(6).Trim());
                            else if (t == "dump") { Dump(); lastDump = Environment.TickCount64; }
                            else if (t == "reset") Reset();
                        }
                    }
                    if (Environment.TickCount64 - lastDump > 5_000) { Dump(); lastDump = Environment.TickCount64; }
                }
                catch { /* an instrument never throws */ }
            }
        }

        // ───────────────────────── output ─────────────────────────

        private static readonly object s_dumpLock = new();

        /// <summary>Writes the aggregates as JSON to <c>HROT_ACCESS_TRACE_OUT</c>.</summary>
        public static void Dump()
        {
            if (!On) return;
            lock (s_dumpLock)
            {
                var merged = new Dictionary<(long, string), long>();
                var entries = new Dictionary<long, long>();
                foreach (var a in s_aggs)
                    lock (a)
                    {
                        foreach (var kv in a.Map)
                        {
                            var name = kv.Key.Item2 is Type ty ? (ty.FullName ?? ty.Name) : (string)kv.Key.Item2;
                            var k = (kv.Key.Item1, name);
                            merged[k] = merged.TryGetValue(k, out var n) ? n + kv.Value : kv.Value;
                        }
                        foreach (var kv in a.Entries) entries[kv.Key] = entries.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
                    }

                string[] readers, phases, declared, repos, buses;
                lock (s_nameLock)
                {
                    readers = s_readerNames.ToArray(); phases = s_phaseNames.ToArray();
                    declared = new string[s_declared.Count]; s_declared.CopyTo(declared);
                    repos = s_repoLegend.ToArray(); buses = s_busLegend.ToArray();
                    for (int i = 0; i < repos.Length; i++)
                        repos[i] += s_repoRefs.TryGetValue(i + 1, out var wr) && wr.TryGetTarget(out var live) ? $" — {live.EntityIndexUntraced.ActiveCount} live entities at the dump" : " — collected";
                }

                var sb = new StringBuilder(1 << 20);
                sb.Append("{\"readers\":"); Arr(sb, readers);
                sb.Append(",\"phases\":"); Arr(sb, phases);
                sb.Append(",\"declared\":"); Arr(sb, declared);
                sb.Append(",\"repos\":"); Arr(sb, repos);
                sb.Append(",\"buses\":"); Arr(sb, buses);
                sb.Append(",\"kinds\":"); Arr(sb, Enum.GetNames(typeof(TraceKind)));
                sb.Append(",\"contexts\":[\"Other\",\"Tick\",\"Presentation\"]");
                sb.Append(",\"entries\":[");
                bool first = true;
                foreach (var kv in entries)
                {
                    if (!first) sb.Append(','); first = false;
                    Unpack(kv.Key, out var ctx, out _, out var rd, out _, out var ph);
                    sb.Append(CultureInfo.InvariantCulture, $"[{ctx},{rd},{ph},{kv.Value}]");
                }
                sb.Append("],\"rows\":[");
                first = true;
                foreach (var kv in merged)
                {
                    if (!first) sb.Append(','); first = false;
                    Unpack(kv.Key.Item1, out var ctx, out var kind, out var rd, out var repo, out var ph);
                    sb.Append(CultureInfo.InvariantCulture, $"[{ctx},{kind},{rd},{repo},{ph},");
                    Str(sb, kv.Key.Item2);
                    sb.Append(CultureInfo.InvariantCulture, $",{kv.Value}]");
                }
                sb.Append("]}");
                var tmp = OutPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Move(tmp, OutPath, true);
            }
        }

        private static void Unpack(long meta, out int ctx, out int kind, out int reader, out int repo, out int phase)
        {
            ctx = (int)(meta & 3); kind = (int)((meta >> 2) & 63); reader = (int)((meta >> 8) & ((1 << ReaderBits) - 1));
            repo = (int)((meta >> (8 + ReaderBits)) & ((1 << RepoBits) - 1)); phase = (int)((meta >> (8 + ReaderBits + RepoBits)) & ((1 << PhaseBits) - 1));
        }

        private static void Arr(StringBuilder sb, string[] items)
        {
            sb.Append('[');
            for (int i = 0; i < items.Length; i++) { if (i > 0) sb.Append(','); Str(sb, items[i]); }
            sb.Append(']');
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // ⭐ NOT a static constructor: a class with one is not beforefieldinit, and then every `On` read in a hot, inlined method carries a
        //   class-init check. A field initializer keeps `On` a constant the JIT folds (the Off cost is ~0).
        private static readonly bool s_started = StartIfOn();

        private static bool StartIfOn()
        {
            if (!On) return false;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Dump(); } catch { /* an instrument never throws at exit */ } };
            var t = new Thread(PumpLoop) { IsBackground = true, Name = "HROT access trace" };
            t.Start();
            return true;
        }
    }
}
