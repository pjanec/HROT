using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ CE-2468 UF-4 (DESIGN_Minefields.md §9b, §9b.1 K4, K11; the contract — NOTE_Backend_To_Behaviors_UnderFire_Contract.md) — what every SIDE knows of places
    /// under fire, as ONE view sees it: the UNION of every node's <see cref="UnderFireKnowledge"/> records (per (subject, side) the NEWEST evidence), ACTIVE records only
    /// (a Cooled or a just-loaded record is not knowledge — the writer's cooling is a record CHANGE, so this table needs no clock, K4). ⭐ Its OWN cache (K11), not the
    /// minefield table's: rebuilt only when an under-fire record changed (<see cref="ViewTableCache{TComp}"/>), otherwise this thread's previous table — 0 B a steady frame
    /// (R-2084). Rides on <see cref="DoorStates.UnderFire"/>. Immutable once returned.
    /// </summary>
    public sealed class UnderFireTable
    {
        public static readonly UnderFireTable Empty = new(Array.Empty<Entry>());

        /// <summary>One side's knowledge of one place (the union of its records).</summary>
        public readonly struct Entry
        {
            public Entry(Guid subject, byte sidePlus1, in UnderFireRecord record) { Subject = subject; SidePlus1 = sidePlus1; Record = record; }
            public Guid Subject { get; }
            public byte SidePlus1 { get; }
            public UnderFireRecord Record { get; }
        }

        private readonly Entry[] _entries;

        private UnderFireTable(Entry[] entries)
        {
            _entries = entries;
            for (int i = 0; i < entries.Length; i++) if (entries[i].Record.Cause == UnderFireCause.Direct) DirectCount++;
        }

        /// <summary>How many (place, side) entries the union holds.</summary>
        public int Count => _entries.Length;

        /// <summary>How many of them are <see cref="UnderFireCause.Direct"/> (0 lets the route cost skip the work).</summary>
        public int DirectCount { get; }

        /// <summary>Entry <paramref name="index"/> (0 ≤ index &lt; <see cref="Count"/>), sorted by (side, subject) — the same order on every node.</summary>
        public ref readonly Entry this[int index] => ref _entries[index];

        [ThreadStatic] private static UnderFireTable? t_last;

        /// <summary>The table of <paramref name="view"/>'s world (the same instance while no record changed).</summary>
        public static UnderFireTable Of(ISimulationView view)
        {
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<UnderFireKnowledge>()) return Empty;
                if (ViewTableCache<UnderFireKnowledge>.Holds(repo) && t_last is { } last) return last;
                return t_last = Build(view, repo);
            }
            return Build(view, null);
        }

        private static UnderFireTable Build(ISimulationView view, EntityRepository? repo)
        {
            var byKey = new Dictionary<KnowledgeKey, Entry>();
            var q = repo != null ? ViewTableCache<UnderFireKnowledge>.Begin(repo) : view.Query().With<UnderFireKnowledge>().WithLifecycle(EntityLifecycle.AllButTearDown).Build();
            foreach (var e in q)
            {
                if (repo != null) ViewTableCache<UnderFireKnowledge>.Read(e);
                ref readonly var r = ref view.GetComponentRO<UnderFireKnowledge>(e);
                if (r.Header.SidePlus1 == 0 || r.Cause == 0 || r.Header.State != UnderFireKnowledge.StateActive) continue;
                var key = new KnowledgeKey(r.Header.Subject, r.Header.SidePlus1);
                if (byKey.TryGetValue(key, out var have) && have.Record.LearnedAt >= r.Header.LearnedAt) continue;   // K13 — several nodes' records of one key: the newest
                byKey[key] = new Entry(r.Header.Subject, r.Header.SidePlus1, new UnderFireRecord(r.Cause, r.Point, r.RangeM, r.EnemyStrength, r.Header.LearnedAt));
            }
            if (repo != null) ViewTableCache<UnderFireKnowledge>.Taken(repo);
            if (byKey.Count == 0) return Empty;
            var entries = new Entry[byKey.Count];
            byKey.Values.CopyTo(entries, 0);
            Array.Sort(entries, static (a, b) => a.SidePlus1 != b.SidePlus1 ? a.SidePlus1.CompareTo(b.SidePlus1) : a.Subject.CompareTo(b.Subject));
            return new UnderFireTable(entries);
        }

        /// <summary>
        /// ⭐ the contract — the NEAREST fresh record of side <paramref name="side"/> (<c>ForceId</c>) whose frozen point lies within <paramref name="withinM"/> of
        /// <paramref name="point"/> (2-D), or false. Allocation-free.
        /// </summary>
        public bool NearestFresh(int side, Vector3 point, float withinM, out UnderFireRecord record)
        {
            record = default;
            if (side < 0 || _entries.Length == 0) return false;
            byte sidePlus1 = (byte)(side + 1);
            float best = withinM * withinM;
            bool found = false;
            var p = new Vector2(point.X, point.Y);
            for (int i = 0; i < _entries.Length; i++)
            {
                ref readonly var e = ref _entries[i];
                if (e.SidePlus1 != sidePlus1) continue;
                float d = Vector2.DistanceSquared(new Vector2(e.Record.Point.X, e.Record.Point.Y), p);
                if (d <= best) { best = d; record = e.Record; found = true; }
            }
            return found;
        }
    }
}
