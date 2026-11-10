using System;
using System.Collections.Generic;
using System.Numerics;
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

        private readonly TerrainWorld _world;        // the world this table was built for (a baked-collapse set differs per world)
        private readonly int[] _collapsed;           // ascending building indices that have collapsed (S-2)
        private readonly ulong[] _collapsedBits;
        private readonly ulong[] _doorBits;          // doors of a collapsed building (they read Destroyed)
        private readonly TerrainPrism[] _rubble;     // one solid mound per collapsed building

        private BuildingDamageTable(WallBreach[] sorted, int[] collapsed, TerrainWorld world)
        {
            int panelCount = world.Panels.Count;
            _world = world;
            _breaches = sorted;
            _collapsed = collapsed;
            _collapsedBits = new ulong[(world.Buildings.Count + 63) >> 6];
            foreach (int b in collapsed) _collapsedBits[b >> 6] |= 1UL << (b & 63);
            _doorBits = new ulong[(world.Doors.Count + 63) >> 6];
            if (collapsed.Length > 0)
                for (int d = 0; d < world.Doors.Count; d++)
                    if (Collapsed(world.Panels[world.Doors[d].Panel].Building)) _doorBits[d >> 6] |= 1UL << (d & 63);
            // a collapse already baked into THIS world's geometry (a derived world) carries its own rubble prism: not crossed twice
            var rubble = new List<TerrainPrism>(collapsed.Length);
            foreach (int b in collapsed) if (!System.Linq.Enumerable.Contains(world.BakedCollapsed, b)) rubble.Add(MakeRubble(world, b));
            _rubble = rubble.ToArray();
            NavHash = ComputeNavHash(sorted, collapsed);
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

        /// <summary>⭐ S-2 — true when building <paramref name="building"/> (index into <see cref="TerrainWorld.Buildings"/>) has collapsed in this view. One bit test; no allocation.</summary>
        public bool Collapsed(int building)
            => (uint)(building >> 6) < (uint)_collapsedBits.Length && building >= 0 && (_collapsedBits[building >> 6] & (1UL << (building & 63))) != 0;

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-3 (B-10) — a hash of what changes the NAVMESH: every PASSAGE breach and the set of collapsed buildings. A hole changes no walkable, so it does
        /// not move this hash. 0 = nothing the navmesh must follow. Equal damage ⇒ equal hash (the breaches are sorted).
        /// </summary>
        public ulong NavHash { get; }

        private static ulong ComputeNavHash(WallBreach[] sorted, int[] collapsed)
        {
            ulong h = 1469598103934665603UL;
            bool any = false;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            void MixF(float f) => Mix((long)MathF.Round(f * 100f));
            foreach (var b in sorted)
            {
                if (b.Kind != BreachKind.Passage) continue;
                any = true;
                Mix(b.Panel); MixF(b.From); MixF(b.To); MixF(b.Bottom); MixF(b.Top);
            }
            foreach (int c in collapsed) { any = true; Mix(-1 - c); }
            return any ? h : 0UL;
        }

        /// <summary>The PASSAGE breaches of the view, in table order (the overlay's input; a fresh list — ⛔ never per frame).</summary>
        public List<WallBreach> Passages()
        {
            var l = new List<WallBreach>();
            foreach (var b in _breaches) if (b.Kind == BreachKind.Passage) l.Add(b);
            return l;
        }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-3 (P3-e) — true when <paramref name="p"/> stands inside the footprint of a COLLAPSED building, between its base and roof (+ 0.5 m): whoever stands there is a
        /// casualty and is given no path. Loops the collapsed set (small); no allocation.
        /// </summary>
        public bool InsideCollapsed(TerrainWorld world, Vector3 p)
        {
            for (int i = 0; i < _collapsed.Length; i++)
            {
                var b = world.Buildings[_collapsed[i]];
                float baseZ = b.StoreyZ.Count > 0 ? b.StoreyZ[0] : b.BaseZ, roof = b.StoreyZ.Count > 1 ? b.StoreyZ[b.StoreyZ.Count - 1] : baseZ + 3f;
                if (p.Z >= baseZ - 0.5f && p.Z <= roof + 0.5f && PolygonMath.Contains(b.Footprint, new Vector2(p.X, p.Y))) return true;
            }
            return false;
        }

        /// <summary>The collapsed buildings' indices, ascending.</summary>
        public IReadOnlyList<int> CollapsedBuildings => _collapsed;

        /// <summary>How many buildings have collapsed.</summary>
        public int CollapsedCount => _collapsed.Length;

        /// <summary>⭐ S-2 — the rubble mounds, one per collapsed building: the EXISTING solid prism kind (<c>Kind = Building</c>, the footprint, base → base + ⅓ of the
        /// height, material <c>rubble</c>, <see cref="TerrainPrism.Building"/> set). Built once per change of the collapse set.</summary>
        public IReadOnlyList<TerrainPrism> Rubble => _rubble;
        internal TerrainPrism[] RubbleArray => _rubble;

        /// <summary>True when door <paramref name="doorIndex"/> belongs to a collapsed building (<see cref="DoorStates"/> reads it as Destroyed).</summary>
        public bool DoorDestroyed(int doorIndex)
            => _collapsed.Length > 0 && (uint)(doorIndex >> 6) < (uint)_doorBits.Length && (_doorBits[doorIndex >> 6] & (1UL << (doorIndex & 63))) != 0;

        /// <summary>The rubble of a building: its footprint, a third of its height, in the rubble material.</summary>
        public const float RubbleHeightShare = 1f / 3f;

        /// <summary>The rubble prism of collapsed building <paramref name="building"/> (the overlay's copy source; built fresh).</summary>
        public TerrainPrism RubbleOf(TerrainWorld world, int building) => MakeRubble(world, building);

        private static TerrainPrism MakeRubble(TerrainWorld world, int building)
        {
            var b = world.Buildings[building];
            float baseZ = b.StoreyZ.Count > 0 ? b.StoreyZ[0] : b.BaseZ, roof = b.StoreyZ.Count > 1 ? b.StoreyZ[b.StoreyZ.Count - 1] : baseZ + 3f;
            var fp = b.Footprint;
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var v in fp) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            world.Materials.TryGet(RubbleMaterial, out var material);
            return new TerrainPrism
            {
                Kind = TerrainPrismKind.Building, Footprint = fp, BaseZ = baseZ, TopZ = baseZ + (roof - baseZ) * RubbleHeightShare, Label = b.Label + " (rubble)",
                Triangles = PolygonMath.Triangulate(fp), Min = min, Max = max, Material = material, Building = building,
            };
        }

        /// <summary>The material name of a rubble mound (a row of <c>materials.json</c>).</summary>
        public const string RubbleMaterial = "rubble";

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
                var scratch = new List<WallBreach>(); var coll = new List<int>();
                foreach (var e in q) Collect(view.GetComponentRO<BuildingDamage>(e), world, scratch, coll);
                return Make(scratch, coll, world);
            }
            if (!repo.IsComponentTypeRegistered<BuildingDamage>()) return null;

            if (ReferenceEquals(repo, t_repo) && ReferenceEquals(world, t_world) && t_stamp.HoldsSinceTaken(repo, typeof(BuildingDamage))
                && repo.ComponentsAddedOf(ComponentType<BuildingDamage>.ID) == t_added)
            {
                if (repo.StructuralChanges == t_structural) return t_last;
                if (StillAlive(repo)) { t_structural = repo.StructuralChanges; return t_last; }
            }

            var list = t_scratch ??= new List<WallBreach>(32);
            var colls = t_collScratch ??= new List<int>(8);
            list.Clear(); colls.Clear();
            t_entityCount = 0;
            var query = ReferenceEquals(repo, t_queryRepo) ? t_query!
                : (t_query = repo.Query().With<BuildingDamage>().WithLifecycle(EntityLifecycle.All).Build());
            t_queryRepo = repo;
            foreach (var e in query)
            {
                Collect(repo.GetComponentRO<BuildingDamage>(e), world, list, colls);
                var ents = t_entities ??= new Entity[8];
                if (t_entityCount == ents.Length) Array.Resize(ref t_entities, ents.Length * 2);
                t_entities![t_entityCount++] = e;
            }
            t_stamp.Take(repo); t_repo = repo; t_world = world; t_structural = repo.StructuralChanges;
            t_added = repo.ComponentsAddedOf(ComponentType<BuildingDamage>.ID);

            list.Sort(Order);
            colls.Sort();
            for (int i = colls.Count - 1; i > 0; i--) if (colls[i] == colls[i - 1]) colls.RemoveAt(i);   // two nodes' entities may both say collapsed
            if (t_last is { } last && last.SameContent(list, colls, world)) return last;   // rebuilt, but the same damage: keep the instance
            if (list.Count == 0 && colls.Count == 0 && t_last == null) return null;
            t_builds++;
            return t_last = Make(list, colls, world);
        }

        private bool SameContent(List<WallBreach> sorted, List<int> collapsed, TerrainWorld world)
        {
            if (!ReferenceEquals(world, _world)) return false;
            if (sorted.Count != _breaches.Length || collapsed.Count != _collapsed.Length) return false;
            for (int i = 0; i < _collapsed.Length; i++) if (_collapsed[i] != collapsed[i]) return false;
            for (int i = 0; i < _breaches.Length; i++) if (_breaches[i] != sorted[i]) return false;
            return true;
        }

        private static void Collect(in BuildingDamage d, TerrainWorld world, List<WallBreach> into, List<int> collapsed)
        {
            if (d.Collapsed != 0 && (uint)d.Building < (uint)world.Buildings.Count) collapsed.Add(d.Building);
            for (int i = 0; i < d.Count; i++)
            {
                var b = d.Get(i);
                if ((uint)b.Panel < (uint)world.Panels.Count) into.Add(b);   // a breach of a panel this terrain does not have (another terrain's) is ignored
            }
        }

        private static BuildingDamageTable? Make(List<WallBreach> sorted, List<int> collapsed, TerrainWorld world)
        {
            if (sorted.Count == 0 && collapsed.Count == 0) return null;
            sorted.Sort(Order); collapsed.Sort();
            for (int i = collapsed.Count - 1; i > 0; i--) if (collapsed[i] == collapsed[i - 1]) collapsed.RemoveAt(i);
            return new BuildingDamageTable(sorted.ToArray(), collapsed.ToArray(), world);
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
        [ThreadStatic] private static List<int>? t_collScratch;
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
