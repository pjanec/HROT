using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ EXPERIMENT — POC 2 of the threaded editor (CE-4439, P-7; 📄 docs/DESIGN_Threaded_Simulation_Presentation.md §14.1): the guard of the
    /// "one writer per world" rule. Opt-in <c>HROT_WORLD_GUARD=1</c> (record) or <c>=throw</c> (a write to S off the sim thread throws). It rides
    /// the access tracer's hooks (<see cref="AccessTrace"/>: every world write already reports its kind and repository) without the tracer's counting.
    /// <list type="bullet">
    /// <item>⛔ <b>S written off the simulation thread</b> — a UI write that should be a COMMAND.</item>
    /// <item>⚠ <b>A written by the simulation thread</b> outside the sync — a system that captured the presentation's world at construction instead of
    /// using the view the kernel passes it: it writes the copy, and the next sync overwrites it.</item>
    /// </list>
    /// Each distinct (violation, kind, type, reader) is kept once with the first stack, written to <c>HROT_WORLD_GUARD_OUT</c> (default the temp
    /// folder) every few seconds and at exit. The list is the measured cost to finish the migration.
    /// </summary>
    public static class WorldGuard
    {
        private static readonly string? Mode = Environment.GetEnvironmentVariable("HROT_WORLD_GUARD");

        /// <summary>The switch. Read once.</summary>
        public static readonly bool On = Mode == "1" || Mode == "throw";
        private static readonly bool Throw = Mode == "throw";
        private static readonly string OutPath = Environment.GetEnvironmentVariable("HROT_WORLD_GUARD_OUT") is { Length: > 0 } o
            ? o : Path.Combine(Path.GetTempPath(), "hrot-world-guard.txt");

        private static volatile int s_simRepo, s_presRepo, s_simThread;
        private static readonly object s_lock = new();
        private static readonly Dictionary<string, (long Count, string Stack)> s_hits = new();
        private static long s_dumpAt;

        /// <summary>Arms the guard for one pair of worlds and the thread that ticks <paramref name="sim"/>.</summary>
        public static void Arm(EntityRepository sim, EntityRepository presentation, int simThreadId)
        {
            if (!On) return;
            s_simRepo = sim.TraceId; s_presRepo = presentation.TraceId; s_simThread = simThreadId;
            s_simBus = sim.Bus.TraceId; s_presBus = presentation.Bus.TraceId;
            AppDomain.CurrentDomain.ProcessExit -= OnExit;
            AppDomain.CurrentDomain.ProcessExit += OnExit;
        }

        /// <summary>Stops checking (the threaded mode ended).</summary>
        public static void Disarm() { s_simRepo = 0; s_presRepo = 0; s_simThread = 0; s_simBus = 0; s_presBus = 0; }

        private static void OnExit(object? _, EventArgs __) { try { Dump(); } catch { /* an instrument never throws at exit */ } }

        private static bool IsWrite(TraceKind k) => k is TraceKind.Write or TraceKind.StructAdd or TraceKind.StructRemove or TraceKind.SingletonSet
            or TraceKind.EntityCreate or TraceKind.EntityDestroy or TraceKind.RawWrite or TraceKind.BusPublish;

        internal static void Check(TraceKind kind, object key, int repo)
        {
            int sim = s_simRepo;
            if (sim == 0 || !IsWrite(kind)) return;
            bool onSim = Environment.CurrentManagedThreadId == s_simThread;
            string? violation = null;
            if (kind == TraceKind.BusPublish)   // `repo` is a BUS id here (a separate counter)
            {
                if (!onSim && repo == s_simBus && repo != 0) violation = "S's bus published off the sim thread";
                else if (onSim && repo == s_presBus && repo != 0) violation = "A's bus published by the sim thread";
            }
            else if (repo == sim && !onSim) violation = "S written off the sim thread";
            else if (repo == s_presRepo && onSim) violation = "A written by the sim thread";
            if (violation == null) return;
            string type = key is Type t ? t.FullName ?? t.Name : key.ToString() ?? "?";
            string id = violation + " | " + kind + " | " + type + " | " + AccessTrace.ReaderName(AccessTrace.CurrentReaderId);
            lock (s_lock)
            {
                if (s_hits.TryGetValue(id, out var h)) s_hits[id] = (h.Count + 1, h.Stack);
                else s_hits[id] = (1, Environment.StackTrace);
            }
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref s_dumpAt) > 5000) { Interlocked.Exchange(ref s_dumpAt, now); try { Dump(); } catch { } }
            if (Throw && violation.StartsWith("S written", StringComparison.Ordinal)) throw new InvalidOperationException("[WorldGuard] " + id);
        }

        private static volatile int s_simBus, s_presBus;

        /// <summary>Distinct hits so far.</summary>
        public static int DistinctHits { get { lock (s_lock) return s_hits.Count; } }

        /// <summary>Writes the hits, most frequent first.</summary>
        public static void Dump()
        {
            List<KeyValuePair<string, (long Count, string Stack)>> rows;
            lock (s_lock) rows = new(s_hits);
            rows.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            var sb = new StringBuilder();
            sb.Append("# WorldGuard — ").Append(rows.Count).Append(" distinct hits (violation | kind | type | reader)\n");
            foreach (var r in rows) sb.Append(r.Value.Count).Append("  ").Append(r.Key).Append('\n');
            sb.Append("\n# first stack of each\n");
            foreach (var r in rows) sb.Append("## ").Append(r.Key).Append('\n').Append(r.Value.Stack).Append("\n\n");
            File.WriteAllText(OutPath, sb.ToString());
        }
    }
}
