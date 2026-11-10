using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4391</c> S-1 (UTM §8.2 B-3, B-8) — the breaches of every building AS ONE VIEW SEES THEM: the UNION of every <see cref="BuildingDamage"/>
    /// entity in that view (one per building per deciding node, B-2), immutable, carried on <see cref="DoorStates.Damage"/> so the ~30 production readers
    /// of a <see cref="DoorStates"/> get breaches with no signature change. The <see cref="DoorStates"/> shape exactly (R-219 / R-220): ⛔ not a field on
    /// <see cref="TerrainWorld"/> (shared by reference into every background snapshot) and ⛔ not a singleton; built from the view it is asked on, per
    /// thread, and the previous table is handed back — allocating nothing — while no <see cref="BuildingDamage"/> was written and no such entity was
    /// added, removed or replaced. A new table is allocated only when the damage changes.
    /// </summary>
    public sealed class BuildingDamageTable
    {
        private readonly WallBreach[] _breaches;     // sorted by (Panel, From, Bottom, To, Top, Kind): equal content ⇒ equal arrays, whatever the entity order
        private readonly ulong[] _panelBits;         // one bit per global panel index that has a breach
        private readonly int[] _firstOfPanel;        // _breaches index of each present panel's first breach — parallel to _presentPanels
        private readonly int[] _presentPanels;       // ascending

        private BuildingDamageTable(WallBreach[] sorted, int panelCount)
        {
            _breaches = sorted;
            _panelBits = new ulong[(panelCount + 63) >> 6];
            var present = new List<int>();
            var first = new List<int>();
            for (int i = 0; i < sorted.Length; i++)
            {
                int p = sorted[i].Panel;
                _panelBits[p >> 6] |= 1UL << (p & 63);
                if (i == 0 || sorted[i - 1].Panel != p) { present.Add(p); first.Add(i); }
            }
            _presentPanels = present.ToArray();
            _firstOfPanel = first.ToArray();
        }

        /// <summary>How many breaches the view holds.</summary>
        public int Count => _breaches.Length;

        /// <summary>The breach at sorted position <paramref name="i"/> (diagnostics, the save, S-3's overlay).</summary>
        public WallBreach this[int i] => _breaches[i];

        /// <summary>How many of them are PASSAGES (S-3's navmesh input).</summary>
        public int PassageCount
        {
            get { int n = 0; foreach (var b in _breaches) if (b.Kind == BreachKind.Passage) n++; return n; }
        }

        /// <summary>True when panel <paramref name="panel"/> (global index) has at least one breach. One bit test; ⛔ no allocation.</summary>
        public bool HasBreaches(int panel)
            => (uint)(panel >> 6) < (uint)_panelBits.Length && (_panelBits[panel >> 6] & (1UL << (panel & 63))) != 0;

        /// <summary>True when the point (<paramref name="along"/> m from the panel's A end, absolute <paramref name="z"/>) lies inside a breach of the panel.</summary>
        public bool Opens(int panel, float along, float z)
        {
            if (!HasBreaches(panel)) return false;
            int k = Array.BinarySearch(_presentPanels, panel);
            if (k < 0) return false;
            for (int i = _firstOfPanel[k]; i < _breaches.Length && _breaches[i].Panel == panel; i++)
                if (_breaches[i].Contains(along, z)) return true;
            return false;
        }

        /// <summary>The panel's breaches (a slice of the table's own array; empty when none).</summary>
        public ReadOnlySpan<WallBreach> BreachesOf(int panel)
        {
            if (!HasBreaches(panel)) return default;
            int k = Array.BinarySearch(_presentPanels, panel);
            if (k < 0) return default;
            int s = _firstOfPanel[k], e = s;
            while (e < _breaches.Length && _breaches[e].Panel == panel) e++;
            return new ReadOnlySpan<WallBreach>(_breaches, s, e - s);
        }

        // ── building the table from a view ────────────────────────────────────────────────────

        private static readonly Comparison<WallBreach> Order = static (a, b) =>
        {
            int c = a.Panel.CompareTo(b.Panel); if (c != 0) return c;
            c = a.From.CompareTo(b.From); if (c != 0) return c;
            c = a.Bottom.CompareTo(b.Bottom); if (c != 0) return c;
            c = a.To.CompareTo(b.To); if (c != 0) return c;
            c = a.Top.CompareTo(b.Top); if (c != 0) return c;
            return ((byte)a.Kind).CompareTo((byte)b.Kind);
        };

        /// <summary>
        /// The breaches <paramref name="view"/> holds on <paramref name="world"/>, or null when it holds none (the common case — no cost to a query).
        /// ⭐ R-220 — per thread: the table built before is handed back while nothing of <see cref="BuildingDamage"/> was written since it was taken
        /// (<c>FrameStamp</c>), no such entity was added (the add counter), and every entity it read is still alive and still holds the component (a walk,
        /// skipped while <see cref="EntityRepository.StructuralChanges"/> is unmoved).
        /// </summary>
        public static BuildingDamageTable? Of(ISimulationView view, TerrainWorld world)
        {
            if (view is not EntityRepository repo)   // a non-repository view (a test double): built each time, never cached
            {
                var q = view.Query().With<BuildingDamage>().WithLifecycle(EntityLifecycle.All).Build();
                var scratch = new List<WallBreach>();
                foreach (var e in q) Collect(view.GetComponentRO<BuildingDamage>(e), world, scratch);
                return Make(scratch, world);
            }
            if (!repo.IsComponentTypeRegistered<BuildingDamage>()) return null;

            if (ReferenceEquals(repo, t_repo) && ReferenceEquals(world, t_world) && t_stamp.HoldsSinceTaken(repo, typeof(BuildingDamage))
                && repo.ComponentsAddedOf(ComponentType<BuildingDamage>.ID) == t_added)
            {
                if (repo.StructuralChanges == t_structural) return t_last;
                if (StillAlive(repo)) { t_structural = repo.StructuralChanges; return t_last; }
            }

            var list = t_scratch ??= new List<WallBreach>(32);
            list.Clear();
            t_entityCount = 0;
            var query = ReferenceEquals(repo, t_queryRepo) ? t_query!
                : (t_query = repo.Query().With<BuildingDamage>().WithLifecycle(EntityLifecycle.All).Build());
            t_queryRepo = repo;
            foreach (var e in query)
            {
                Collect(repo.GetComponentRO<BuildingDamage>(e), world, list);
                var ents = t_entities ??= new Entity[8];
                if (t_entityCount == ents.Length) Array.Resize(ref t_entities, ents.Length * 2);
                t_entities![t_entityCount++] = e;
            }
            t_stamp.Take(repo); t_repo = repo; t_world = world; t_structural = repo.StructuralChanges;
            t_added = repo.ComponentsAddedOf(ComponentType<BuildingDamage>.ID);

            list.Sort(Order);
            if (t_last is { } last && last.SameContent(list)) return last;   // rebuilt, but the same breaches: keep the instance
            t_builds++;
            return t_last = Make(list, world);
        }

        private bool SameContent(List<WallBreach> sorted)
        {
            if (sorted.Count != _breaches.Length) return false;
            for (int i = 0; i < _breaches.Length; i++) if (_breaches[i] != sorted[i]) return false;
            return true;
        }

        private static void Collect(in BuildingDamage d, TerrainWorld world, List<WallBreach> into)
        {
            for (int i = 0; i < d.Count; i++)
            {
                var b = d.Get(i);
                if ((uint)b.Panel < (uint)world.Panels.Count) into.Add(b);   // a breach of a panel this terrain does not have (another terrain's) is ignored
            }
        }

        private static BuildingDamageTable? Make(List<WallBreach> sorted, TerrainWorld world)
        {
            if (sorted.Count == 0) return null;
            sorted.Sort(Order);
            return new BuildingDamageTable(sorted.ToArray(), world.Panels.Count);
        }

        private static bool StillAlive(EntityRepository repo)
        {
            for (int k = 0; k < t_entityCount; k++)
                if (!repo.IsAlive(t_entities![k]) || !repo.HasComponent<BuildingDamage>(t_entities[k])) return false;
            return true;
        }

        [ThreadStatic] private static Fdp.Toolkit.Replication.Utilities.FrameStamp t_stamp;
        [ThreadStatic] private static EntityRepository? t_repo;
        [ThreadStatic] private static TerrainWorld? t_world;
        [ThreadStatic] private static BuildingDamageTable? t_last;
        [ThreadStatic] private static List<WallBreach>? t_scratch;
        [ThreadStatic] private static Entity[]? t_entities;
        [ThreadStatic] private static int t_entityCount;
        [ThreadStatic] private static long t_structural;
        [ThreadStatic] private static long t_added;
        [ThreadStatic] private static int t_builds;
        [ThreadStatic] private static EntityRepository? t_queryRepo;
        [ThreadStatic] private static EntityQuery? t_query;

        /// <summary>How many times THIS thread rebuilt the table from the damage entities and found it changed (rails).</summary>
        internal static int TableBuildsOnThisThread => t_builds;
    }
}
