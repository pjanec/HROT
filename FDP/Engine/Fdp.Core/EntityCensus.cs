using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <b>N-1 — the cross-entity access census</b> (📄 docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md;
    /// docs/DESIGN_Parallel_Simulation.md §18). Opt-in <c>HROT_ENTITY_CENSUS=1</c> (it switches the A-1 tracer on too: the census needs its
    /// readers — the system scopes). Every access made through a hook that knows its entity is classified against the entity the thread is
    /// PROCESSING:
    /// <list type="bullet">
    /// <item><b>own</b> — the entity of the system's OUTERMOST entity loop (the one a per-entity job would be given);</item>
    /// <item><b>other-scan</b> — not the processed entity, but the entity of an INNER loop the system runs while processing (a scan over neighbours);</item>
    /// <item><b>other</b> — any other entity (a target, a squad member, a looked-up id);</item>
    /// <item><b>own-part</b> — a PART of the processed entity (its <c>PartMetadata.ParentEntity</c> is the processed entity: a child sensor, an owned
    /// part) — exclusive to that entity, so a per-entity job may write it directly;</item>
    /// <item><b>none</b> — no entity loop is running (a global system, an event-driven pass, or a loop the hooks do not see — see "blind spots").</item>
    /// </list>
    /// The current entity is a per-thread STACK: an <see cref="EntityQuery"/> walk pushes a slot when it starts, sets it per entity, and pops it when
    /// the <c>foreach</c> ends (<c>Dispose</c>); the scheduler clears the stack when a system starts. Counted per (reader, kind, type, repository,
    /// class), with up to 3 sampled call sites per key for the cross-entity and structural ones; plus reads of an entity created in the same frame.
    /// JSON to <c>HROT_ENTITY_CENSUS_OUT</c> (default the temp folder) every 5 s and at exit.
    /// <para>⛔ <b>Blind spots</b> (stated so a zero is never read as "none"): an entity loop that is not an <see cref="EntityQuery"/> walk (an array
    /// of entities, an event list) runs as <b>none</b>; a raw pointer / span taken once and written many times counts once; a table reached through
    /// <c>GetComponentTable</c> is not per entity; a pool worker (the parallel path) has no system reader — run the census SERIAL.</para>
    /// ⛔ An instrument: OFF, every hook is a <c>static readonly</c> test the JIT folds away (0 B).
    /// </summary>
    public static class EntityCensus
    {
        /// <summary>The switch: <c>HROT_ENTITY_CENSUS=1</c>. Read once.</summary>
        public static readonly bool On = EnvVars.Read(EnvVars.HrotEntityCensus) == "1";

        /// <summary>Where the JSON goes: <c>HROT_ENTITY_CENSUS_OUT</c> (default the temp folder) + <c>.&lt;pid&gt;.json</c> — one file per node process (a rig
        /// runs several nodes with one environment).</summary>
        public static readonly string OutputPath = (EnvVars.Read(EnvVars.HrotEntityCensusOut) is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "hrot-entity-census")) + "." + Environment.ProcessId + ".json";
        private static readonly string OutPath = OutputPath;

        /// <summary>The class of an access relative to the entity being processed.</summary>
        public enum Rel : byte { Own = 0, OtherScan = 1, Other = 2, None = 3, OwnPart = 4 }

        // ── the per-thread stack of the entities being walked ──
        [ThreadStatic] private static int[]? t_stack;
        [ThreadStatic] private static int t_depth;

        /// <summary>A walk starts: a new slot (no entity yet). Returns the slot for <see cref="Set"/> / <see cref="Pop"/>.</summary>
        public static int Push()
        {
            var s = t_stack ??= new int[16];
            if (t_depth == s.Length) { Array.Resize(ref t_stack, s.Length * 2); s = t_stack; }
            s[t_depth] = -1;
            return t_depth++;
        }

        /// <summary>The walk at <paramref name="slot"/> yields entity <paramref name="index"/>.</summary>
        public static void Set(int slot, int index) { if (t_stack != null && slot >= 0 && slot < t_depth) t_stack[slot] = index; }

        /// <summary>The walk at <paramref name="slot"/> ended (and every walk it left open).</summary>
        public static void Pop(int slot) { if (slot >= 0 && slot < t_depth) t_depth = slot; }

        /// <summary>A system starts: no walk is open on this thread.</summary>
        public static void ResetThread() => t_depth = 0;

        /// <summary>The class of an access to <paramref name="index"/> on this thread now.</summary>
        public static Rel Classify(int index)
        {
            var s = t_stack;
            if (s == null || t_depth == 0 || s[0] < 0) return Rel.None;
            if (s[0] == index) return Rel.Own;
            for (int d = 1; d < t_depth; d++) if (s[d] == index) return Rel.OtherScan;
            return Rel.Other;
        }

        // a part's parent: PartMetadata (GlobalComponentIds.PartMetadata) starts with its ParentEntity — read raw, the tracer muted (not an access of the system)
        private static unsafe int ParentIndex(int repoId, int index)
        {
            var repo = AccessTrace.RepoOf(repoId);
            if (repo == null) return -1;
            AccessTrace.Mute();
            try
            {
                var idx = repo.EntityIndexUntraced;
                if (index < 0 || index > idx.MaxIssuedIndex) return -1;
                var e = new Entity(index, idx.GetMetadataUnsafe(index).Generation);
                if (!repo.IsAlive(e) || !repo.HasComponentByTypeId(e, GlobalComponentIds.PartMetadata)) return -1;
                return ((Entity*)repo.GetComponentPointer(e, GlobalComponentIds.PartMetadata))->Index;
            }
            catch { return -1; }
            finally { AccessTrace.Unmute(); }
        }

        // ── frames and creations ──
        private static int s_frame;
        private static readonly ConcurrentDictionary<long, int> s_createdAt = new();

        /// <summary>The node frame starts (the kernel's update).</summary>
        public static void NextFrame() => Interlocked.Increment(ref s_frame);

        /// <summary>Entity <paramref name="index"/> was created in <paramref name="repo"/> this frame.</summary>
        public static void Created(int repo, int index) => s_createdAt[((long)repo << 32) | (uint)index] = Volatile.Read(ref s_frame);

        // ── the counts ──
        private readonly struct Key : IEquatable<Key>
        {
            public readonly int Reader, Repo; public readonly TraceKind Kind; public readonly object Type; public readonly Rel Rel;
            public Key(int reader, TraceKind kind, object type, int repo, Rel rel) { Reader = reader; Kind = kind; Type = type; Repo = repo; Rel = rel; }
            public bool Equals(Key o) => Reader == o.Reader && Kind == o.Kind && Repo == o.Repo && Rel == o.Rel && Equals(Type, o.Type);
            public override bool Equals(object? obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Reader, Kind, Type, Repo, Rel);
        }

        private sealed class Cell { public long Count; public long ReadAfterCreate; public readonly List<string> Sites = new(3); }

        private static readonly ConcurrentDictionary<Key, Cell> s_cells = new();
        private static Thread? s_pump;

        /// <summary>One access of <paramref name="kind"/> to <paramref name="type"/> on entity <paramref name="index"/> of repository <paramref name="repo"/>,
        /// by <paramref name="reader"/> (the tracer's current scope).</summary>
        public static void Note(TraceKind kind, object type, int repo, int index, int reader)
        {
            if (s_pump == null) Start();
            var rel = Classify(index);
            if (rel is Rel.Other or Rel.OtherScan && t_stack != null && ParentIndex(repo, index) == t_stack[0]) rel = Rel.OwnPart;
            var cell = s_cells.GetOrAdd(new Key(reader, kind, type, repo, rel), static _ => new Cell());
            long n = Interlocked.Increment(ref cell.Count);
            if (!IsWrite(kind) && s_createdAt.TryGetValue(((long)repo << 32) | (uint)index, out int f) && f == Volatile.Read(ref s_frame))
                Interlocked.Increment(ref cell.ReadAfterCreate);
            bool sample = rel != Rel.Own && (IsWrite(kind) || kind is TraceKind.EntityDestroy) && (n <= 3 || (n & 4095) == 0);
            if (sample)
                lock (cell.Sites) if (cell.Sites.Count < 3) { var site = CallSite(); if (!cell.Sites.Contains(site)) cell.Sites.Add(site); }
        }

        private static bool IsWrite(TraceKind k) => k is TraceKind.Write or TraceKind.StructAdd or TraceKind.StructRemove or TraceKind.RawWrite
            or TraceKind.TryGetRW or TraceKind.EntityDestroy or TraceKind.ReadViaRW;

        // the first frames outside the engine core (who asked)
        private static string CallSite()
        {
            var sb = new StringBuilder(); int taken = 0;
            foreach (var f in new System.Diagnostics.StackTrace(2, false).GetFrames())
            {
                var t = f.GetMethod()?.DeclaringType;
                if (t == null || t.Namespace is "Fdp.Core" or "Fdp.Core.Internal") continue;
                sb.Append(taken++ > 0 ? " < " : "").Append(t.Name).Append('.').Append(f.GetMethod()!.Name);
                if (taken >= 4) break;
            }
            return sb.ToString();
        }

        private static void Start()
        {
            lock (s_cells)
            {
                if (s_pump != null) return;
                s_pump = new Thread(() => { while (true) { Thread.Sleep(5000); try { Dump(); } catch { } } }) { IsBackground = true, Name = "EntityCensus dump" };
                s_pump.Start();
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Dump(); } catch { } };
            }
        }

        /// <summary>Drops every count (the tracer's control line <c>reset</c> — a run resets after its warm-up, so the load is not counted).</summary>
        public static void Reset() { s_cells.Clear(); s_createdAt.Clear(); }

        /// <summary>Writes the counts as JSON: one row per (reader, kind, type, repo, class).</summary>
        public static void Dump()
        {
            var sb = new StringBuilder();
            sb.Append("{\"frames\":").Append(Volatile.Read(ref s_frame)).Append(",\"rows\":[");
            bool first = true;
            foreach (var (k, c) in s_cells)
            {
                if (!first) sb.Append(','); first = false;
                string type = k.Type is Type t ? t.FullName ?? t.Name : k.Type.ToString() ?? "?";
                string[] sites; lock (c.Sites) sites = c.Sites.ToArray();
                sb.Append("{\"reader\":").Append(Q(AccessTrace.ReaderName(k.Reader))).Append(",\"kind\":\"").Append(k.Kind)
                  .Append("\",\"type\":").Append(Q(type)).Append(",\"repo\":").Append(k.Repo).Append(",\"rel\":\"").Append(k.Rel)
                  .Append("\",\"n\":").Append(Interlocked.Read(ref c.Count)).Append(",\"readAfterCreate\":").Append(Interlocked.Read(ref c.ReadAfterCreate))
                  .Append(",\"sites\":[");
                for (int i = 0; i < sites.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Q(sites[i])); }
                sb.Append("]}");
            }
            sb.Append("]}");
            var tmp = OutPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, OutPath, overwrite: true);
        }

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in s) { if (ch is '"' or '\\') sb.Append('\\'); if (ch >= ' ') sb.Append(ch); }
            return sb.Append('"').ToString();
        }
    }
}
