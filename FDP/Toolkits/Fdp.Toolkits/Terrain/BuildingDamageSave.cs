using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-4 (UTM §8.2 B-7, Q-D3) — one building's saved damage, as the scenario's <c>TerrainObjects</c> section holds it: its breaches (the panel as its
    /// index WITHIN the building — <see cref="BuildingPanelIndex"/>) and whether it collapsed. The ONE shape the save, the distributed merge and the load all use.
    /// </summary>
    public sealed class SavedBuildingDamage
    {
        /// <summary>Breaches with <see cref="WallBreach.Panel"/> = the index within the building, sorted (the file order).</summary>
        public List<WallBreach> Breaches { get; } = new();
        public bool Collapsed { get; set; }

        private static int Order(in WallBreach a, in WallBreach b)
        {
            int c = a.Panel.CompareTo(b.Panel); if (c != 0) return c;
            c = a.From.CompareTo(b.From); if (c != 0) return c;
            c = a.Bottom.CompareTo(b.Bottom); if (c != 0) return c;
            c = a.To.CompareTo(b.To); if (c != 0) return c;
            c = a.Top.CompareTo(b.Top); if (c != 0) return c;
            return ((byte)a.Kind).CompareTo((byte)b.Kind);
        }

        /// <summary>Adds <paramref name="b"/> unless an equal breach is already there (the union of two slices holding the same one).</summary>
        public void AddDistinct(in WallBreach b) { if (!Breaches.Contains(b)) Breaches.Add(b); }

        /// <summary>The union of <paramref name="other"/> into this: breaches distinct, collapsed OR-ed.</summary>
        public void UnionWith(SavedBuildingDamage other)
        {
            foreach (var b in other.Breaches) AddDistinct(b);
            Collapsed |= other.Collapsed;
        }

        public void Sort() => Breaches.Sort(static (a, b) => Order(a, b));

        public bool IsEmpty => Breaches.Count == 0 && !Collapsed;
    }

    /// <summary>⭐ <c>CE-4391</c> S-4 — a panel's index WITHIN its building (rank among the panels with the same <see cref="TerrainWallPanel.Building"/>, in <see cref="TerrainWorld.Panels"/> order) ⇄ its global index. Written once, used by the save and the load.</summary>
    public static class BuildingPanelIndex
    {
        /// <summary>⭐ CE-4427 R-3 — per world, built ONCE (a world is immutable): each panel's rank within its building, and each building's panels (global indices) in order.</summary>
        private sealed class Table
        {
            public int[] Rank = Array.Empty<int>();
            public int[][] PanelsOf = Array.Empty<int[]>();
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TerrainWorld, Table> s_tables = new();
        private static int s_built;

        /// <summary>How many rank tables have been built (diagnostics / rails: a steady caller must not rebuild).</summary>
        public static int TablesBuilt => System.Threading.Volatile.Read(ref s_built);

        private static Table TableOf(TerrainWorld world) => s_tables.GetValue(world, static w =>
        {
            System.Threading.Interlocked.Increment(ref s_built);
            var t = new Table { Rank = new int[w.Panels.Count] };
            int max = -1;
            for (int i = 0; i < w.Panels.Count; i++) if (w.Panels[i].Building > max) max = w.Panels[i].Building;
            var counts = new int[max + 1];
            for (int i = 0; i < w.Panels.Count; i++)
            {
                int b = w.Panels[i].Building;
                if (b < 0) { t.Rank[i] = -1; continue; }
                t.Rank[i] = counts[b]++;
            }
            t.PanelsOf = new int[max + 1][];
            for (int b = 0; b <= max; b++) t.PanelsOf[b] = new int[counts[b]];
            var fill = new int[max + 1];
            for (int i = 0; i < w.Panels.Count; i++)
            {
                int b = w.Panels[i].Building;
                if (b >= 0) t.PanelsOf[b][fill[b]++] = i;
            }
            return t;
        });

        /// <summary>The index of global panel <paramref name="panel"/> within its building, or −1 when it is not a building's panel (or out of range). O(1) after the world's table exists (CE-4427 R-3).</summary>
        public static int ToLocal(TerrainWorld world, int panel)
        {
            if ((uint)panel >= (uint)world.Panels.Count) return -1;
            return TableOf(world).Rank[panel];
        }

        /// <summary>The global index of panel <paramref name="local"/> of building <paramref name="building"/>, or −1 when the building has no such panel.</summary>
        public static int ToGlobal(TerrainWorld world, int building, int local)
        {
            if (local < 0 || building < 0) return -1;
            var of = TableOf(world).PanelsOf;
            if (building >= of.Length) return -1;
            var list = of[building];
            return local < list.Length ? list[local] : -1;
        }
    }

    /// <summary>⭐ <c>CE-4391</c> S-4 — the JSON of one building's damage entry; <see cref="TerrainObjectsSection"/> owns the section around it.</summary>
    internal static class BuildingDamageJson
    {
        public const string BreachesProperty = "breaches";
        public const string CollapsedProperty = "collapsed";

        public static bool IsEntry(JsonObject entry) => entry.ContainsKey(BreachesProperty) || entry.ContainsKey(CollapsedProperty);

        public static JsonObject Write(SavedBuildingDamage d)
        {
            d.Sort();
            var o = new JsonObject();
            if (d.Breaches.Count > 0)
            {
                var rows = new JsonArray();
                foreach (var b in d.Breaches)
                    rows.Add(new JsonArray(JsonValue.Create(b.Panel), JsonValue.Create(b.From), JsonValue.Create(b.To), JsonValue.Create(b.Bottom), JsonValue.Create(b.Top),
                        JsonValue.Create(b.Kind == BreachKind.Passage ? "passage" : "hole")));
                o[BreachesProperty] = rows;
            }
            if (d.Collapsed) o[CollapsedProperty] = true;
            return o;
        }

        private static bool Num(JsonNode? n, out double d)
        {
            d = 0;
            if (n is not JsonValue v) return false;
            if (v.TryGetValue(out double x)) { d = x; return double.IsFinite(x); }
            if (v.TryGetValue(out float f)) { d = f; return double.IsFinite(f); }
            if (v.TryGetValue(out int i)) { d = i; return true; }
            return false;
        }

        /// <summary>⛔ Fails LOUDLY on a malformed entry — a scenario that silently loses a breach or a collapse is what the section exists to prevent.</summary>
        public static SavedBuildingDamage Read(string key, JsonObject entry)
        {
            string at = $"[Scenario] {TerrainObjectsSection.Name}['{key}']";
            var d = new SavedBuildingDamage();
            if (entry[BreachesProperty] is { } br)
            {
                if (br is not JsonArray rows) throw new InvalidOperationException($"{at}.breaches is not an array.");
                int n = 0;
                foreach (var row in rows)
                {
                    string r = $"{at}.breaches[{n++}]";
                    if (row is not JsonArray a || a.Count != 6) throw new InvalidOperationException($"{r} is not [panel, from, to, bottom, top, kind].");
                    if (!Num(a[0], out double p) || p < 0 || p != Math.Floor(p) || p > int.MaxValue) throw new InvalidOperationException($"{r}: the panel '{a[0]}' is not a panel index.");
                    if (!Num(a[1], out double from) || !Num(a[2], out double to) || !Num(a[3], out double bottom) || !Num(a[4], out double top))
                        throw new InvalidOperationException($"{r}: from, to, bottom and top must be numbers.");
                    if (from > to || bottom > top) throw new InvalidOperationException($"{r}: the rectangle is inverted (from > to or bottom > top).");
                    if (a[5] is not JsonValue kv || !kv.TryGetValue(out string? k)
                        || !(string.Equals(k, "hole", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "passage", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"{r}: the kind '{a[5]}' is not 'hole' or 'passage'.");
                    d.AddDistinct(new WallBreach((int)p, (float)from, (float)to, (float)bottom, (float)top,
                        string.Equals(k, "passage", StringComparison.OrdinalIgnoreCase) ? BreachKind.Passage : BreachKind.Hole));
                }
            }
            if (entry[CollapsedProperty] is { } cn)
            {
                if (cn is not JsonValue cv || !cv.TryGetValue(out bool c)) throw new InvalidOperationException($"{at}.collapsed is not true or false.");
                d.Collapsed = c;
            }
            d.Sort();
            return d;
        }
    }
}

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4391</c> S-4 (B-7) — saved building damage → the <see cref="BuildingDamage"/> components a load creates; the toolkit half of <c>TerrainObjectRequests.ForBuildingDamage</c>.</summary>
    public static class BuildingDamageLoad
    {
        /// <summary>
        /// One entry per saved building in key order: its building index and the components to create — breaches in the saved (sorted) order, <see cref="BuildingDamage.Capacity"/> to an entity,
        /// the collapsed byte on the first. A saved building or panel this terrain does not have is reported through <paramref name="warn"/> and ignored (the rest of the building still loads).
        /// </summary>
        public static List<(string Key, int Building, List<BuildingDamage> Entities)> Resolve(
            TerrainWorld world, IReadOnlyDictionary<string, SavedBuildingDamage> saved, Action<string>? warn)
        {
            var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < world.Buildings.Count; i++) byKey[BuildingDamages.KeyOf(world, i)] = i;

            var keys = new List<string>(saved.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new List<(string, int, List<BuildingDamage>)>();
            foreach (var key in keys)
            {
                if (!byKey.TryGetValue(key, out int building))
                {
                    warn?.Invoke($"the scenario saves damage for building '{key}', which terrain '{world.Name ?? "(unnamed)"}' does not have — ignored.");
                    continue;
                }
                var s = saved[key];
                var entities = new List<BuildingDamage>();
                var d = new BuildingDamage { Building = building, Collapsed = (byte)(s.Collapsed ? 1 : 0) };
                bool any = s.Collapsed;
                foreach (var b in s.Breaches)
                {
                    int global = BuildingPanelIndex.ToGlobal(world, building, b.Panel);
                    if (global < 0)
                    {
                        warn?.Invoke($"the scenario saves a breach on panel {b.Panel} of building '{key}', which has no such panel — ignored.");
                        continue;
                    }
                    if (d.Count == BuildingDamage.Capacity) { entities.Add(d); d = new BuildingDamage { Building = building }; }
                    d.Set(d.Count, b with { Panel = global });
                    d.Count++;
                    any = true;
                }
                if (any) entities.Add(d);
                if (entities.Count > 0) result.Add((key, building, entities));
            }
            return result;
        }
    }
}
