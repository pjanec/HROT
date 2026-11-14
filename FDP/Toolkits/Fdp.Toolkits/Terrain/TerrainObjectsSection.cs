using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Extensions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings Stage 5e (📄 docs/DESIGN_Building_Interiors.md §3b K5, §3j "5e") — the scenario's <c>TerrainObjects</c> section:
    /// the state of the terrain's objects (its doors; ⭐ S-4: the damage of its buildings — <c>"breaches": [[panelInBuilding, from, to, bottom, top, "hole"|"passage"]]</c>, <c>"collapsed": true</c>), keyed by <see cref="TerrainObjectKey"/>. Door ENTITIES are never saved
    /// (<c>ScenarioIgnoreTag</c>, K5 — the terrain recreates them at load); their STATE is, here:
    /// <code>"TerrainObjects": { "test-town/Block D/front": { "door": "Locked" } }</code>
    /// <para>⭐ ONE writer and ONE reader for the three places that touch the section — the save (<c>ScenarioSerializer</c>), the
    /// distributed merge (<c>ScenarioMergeCore</c>) and the load (<c>ScenarioLoadStep</c> → <c>TerrainObjectRequests</c>).</para>
    /// <para>⭐ Only a door whose state DIFFERS from what the terrain authored is written: an untouched door says nothing, so a later
    /// change to the terrain's authored state still reaches a scenario that never touched that door.</para>
    /// </summary>
    public static class TerrainObjectsSection
    {
        /// <summary>The section's name in the scenario DOM (PascalCase like <c>Header</c>/<c>Entities</c>; the reader also accepts camelCase).</summary>
        public const string Name = "TerrainObjects";

        private const string DoorProperty = "door";

        /// <summary>
        /// The section for <paramref name="repo"/>, or null when there is nothing to say. Same gate as an entity's save: only a door
        /// this host is the primary OWNER of (<c>HasAuthority</c>; no authority component ⇒ owned, so the editor saves every door), so
        /// in a distributed save each door appears in exactly one slice.
        /// </summary>
        public static JsonObject? Write(EntityRepository repo)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            var terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;

            var doors = new SortedDictionary<string, TerrainDoorState>(StringComparer.Ordinal);   // stable file order
            if (repo.IsComponentTypeRegistered<DoorState>())
                foreach (var e in repo.Query().With<DoorState>().WithManaged<TerrainObjectKey>().Build())
                {
                    if (!repo.HasAuthority(e)) continue;
                    string key = repo.GetComponent<TerrainObjectKey>(e).Key;
                    if (string.IsNullOrEmpty(key)) continue;
                    var state = repo.GetComponentRO<DoorState>(e).State;
                    int i = terrain?.DoorIndexOf(key) ?? -1;
                    if (i >= 0 && terrain!.Doors[i].Initial == state) continue;   // as authored — nothing to say
                    doors[key] = state;
                }
            var damage = WriteBuildingDamage(repo, terrain);
            var craters = WriteCraters(repo);
            if (doors.Count == 0 && damage.Count == 0 && craters.Count == 0) return null;

            var section = new JsonObject();
            foreach (var (key, state) in doors)
                section[key] = new JsonObject { [DoorProperty] = JsonValue.Create(state.ToString()) };
            foreach (var (key, d) in damage)
                section[key] = BuildingDamageJson.Write(d);
            if (craters.Count > 0) section[GroundKey] = CratersJson(craters);
            return section;
        }

        /// <summary>
        /// ⭐ <c>CE-4591</c> (P-4 part 3, G-7) — the entry that holds the scenario's CRATERS: <c>"#ground": { "craters": [[x, y, radius, depth, rim, bornAt, kind], …] }</c>
        /// (a '#' no terrain-object key starts with). One entry for the whole terrain: a crater is terrain-local, not an object.
        /// </summary>
        public const string GroundKey = "#ground";

        private const string CratersProperty = "craters";

        /// <summary>⭐ CE-4591 — every crater of every <see cref="GroundDamage"/> entity this host OWNS (the breaches' gate: a distributed save writes each once), content-sorted, distinct.</summary>
        private static List<Crater> WriteCraters(EntityRepository repo)
        {
            var list = new List<Crater>();
            if (!repo.IsComponentTypeRegistered<GroundDamage>()) return list;
            foreach (var e in repo.Query().With<GroundDamage>().Build())
            {
                if (!GroundDamageSystem.OwnsDamage(repo, e)) continue;
                var d = repo.GetComponentRO<GroundDamage>(e);
                for (int i = 0; i < d.Count && i < GroundDamage.Capacity; i++) list.Add(d.Get(i));
            }
            return SortDistinct(list);
        }

        private static List<Crater> SortDistinct(List<Crater> list)
        {
            list.Sort(static (p, q) =>
            {
                int c = p.Centre.X.CompareTo(q.Centre.X); if (c != 0) return c;
                c = p.Centre.Y.CompareTo(q.Centre.Y); if (c != 0) return c;
                c = p.Radius.CompareTo(q.Radius); if (c != 0) return c;
                c = p.Depth.CompareTo(q.Depth); if (c != 0) return c;
                c = p.Rim.CompareTo(q.Rim); if (c != 0) return c;
                c = p.BornAt.CompareTo(q.BornAt); if (c != 0) return c;
                return p.Kind.CompareTo(q.Kind);
            });
            for (int i = list.Count - 1; i > 0; i--) if (list[i] == list[i - 1]) list.RemoveAt(i);
            return list;
        }

        private static JsonObject CratersJson(List<Crater> craters)
        {
            var arr = new JsonArray();
            foreach (var c in craters)
                arr.Add(new JsonArray(c.Centre.X, c.Centre.Y, c.Radius, c.Depth, c.Rim, (double)c.BornAt, (int)c.Kind));
            return new JsonObject { [CratersProperty] = arr };
        }

        /// <summary>True when <paramref name="entry"/> is the craters entry.</summary>
        public static bool IsCraterEntry(JsonNode? entry) => entry is JsonObject o && o[CratersProperty] is JsonArray;

        /// <summary>
        /// ⭐ <c>CE-4591</c> — the craters saved in <paramref name="dom"/> (content-sorted). ⛔ A malformed row fails LOUDLY: a scenario that silently loses its craters
        /// reloads with the ground the shelling had already changed.
        /// </summary>
        public static IReadOnlyList<Crater> ReadCraters(JsonObject dom)
        {
            var list = new List<Crater>();
            if (Of(dom) is not { } section || section[GroundKey] is not JsonObject entry || entry[CratersProperty] is not JsonArray rows) return list;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is not JsonArray r || r.Count != 7)
                    throw new InvalidOperationException($"[Scenario] {Name}['{GroundKey}'].craters[{i}] is not [x, y, radius, depth, rim, bornAt, kind].");
                try
                {
                    var c = new Crater(new System.Numerics.Vector2(r[0]!.GetValue<float>(), r[1]!.GetValue<float>()), r[2]!.GetValue<float>(), r[3]!.GetValue<float>(),
                        r[4]!.GetValue<float>(), (SimInstant)r[5]!.GetValue<double>(), (CraterKind)r[6]!.GetValue<int>());
                    if (!(c.Radius > 0f) || c.Kind is not (CraterKind.Small or CraterKind.Large))
                        throw new InvalidOperationException($"[Scenario] {Name}['{GroundKey}'].craters[{i}]: radius must be > 0 and kind 1 or 2.");
                    list.Add(c);
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or NullReferenceException)
                {
                    throw new InvalidOperationException($"[Scenario] {Name}['{GroundKey}'].craters[{i}] is malformed: {e.Message}", e);
                }
            }
            return SortDistinct(list);
        }

        /// <summary><see cref="ReadCraters(JsonObject)"/> from scenario JSON text.</summary>
        public static IReadOnlyList<Crater> ReadCraters(string json)
            => JsonNode.Parse(json) is JsonObject dom ? ReadCraters(dom) : Array.Empty<Crater>();

        /// <summary>⭐ CE-4591 — the merge of two slices' craters entries (two deciding nodes each saved their own): the union, sorted, distinct.</summary>
        public static JsonObject MergeCraters(JsonObject a, JsonObject b)
        {
            var list = new List<Crater>();
            list.AddRange(ReadCraters(new JsonObject { [Name] = new JsonObject { [GroundKey] = a.DeepClone() } }));
            list.AddRange(ReadCraters(new JsonObject { [Name] = new JsonObject { [GroundKey] = b.DeepClone() } }));
            return CratersJson(SortDistinct(list));
        }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-4 (UTM §8.2 B-7) — the damage of every building: the UNION of every <see cref="BuildingDamage"/> entity this host OWNS for it
        /// (<see cref="BuildingDamageSystem.OwnsDamage"/> — the doors' gate), so in a distributed save each entity is written by exactly one slice and a building
        /// hit by two deciding nodes is merged by <c>ScenarioMergeCore</c>. Key = <see cref="BuildingDamages.KeyOf"/>, sorted.
        /// </summary>
        private static SortedDictionary<string, SavedBuildingDamage> WriteBuildingDamage(EntityRepository repo, TerrainWorld? terrain)
        {
            var result = new SortedDictionary<string, SavedBuildingDamage>(StringComparer.Ordinal);
            if (terrain == null || !repo.IsComponentTypeRegistered<BuildingDamage>()) return result;
            foreach (var e in repo.Query().With<BuildingDamage>().Build())
            {
                if (!BuildingDamageSystem.OwnsDamage(repo, e)) continue;
                var d = repo.GetComponentRO<BuildingDamage>(e);
                if ((uint)d.Building >= (uint)WallGroups.Count(terrain)) continue;   // ⭐ CE-4599 — free-wall lines are saved too
                string key = BuildingDamages.KeyOf(terrain, d.Building);
                if (!result.TryGetValue(key, out var saved)) result[key] = saved = new SavedBuildingDamage();
                for (int i = 0; i < d.Count; i++)
                {
                    var b = d.Get(i);
                    int local = BuildingPanelIndex.ToLocal(terrain, b.Panel);
                    if (local < 0 || WallGroups.Of(terrain, b.Panel) != d.Building) continue;
                    saved.AddDistinct(b with { Panel = local });
                }
                if (d.Collapsed != 0) saved.Collapsed = true;
            }
            foreach (var key in new List<string>(result.Keys)) if (result[key].IsEmpty) result.Remove(key);
            return result;
        }

        /// <summary>
        /// ⭐ <c>CE-4391</c> S-4 — the saved building damage in <paramref name="dom"/>: key → breaches (panel = index within the building) + collapsed. An entry with
        /// neither key (a door, a future object kind) is ignored; ⛔ a malformed damage entry fails LOUDLY, like <see cref="ReadDoors(JsonObject)"/>.
        /// </summary>
        public static IReadOnlyDictionary<string, SavedBuildingDamage> ReadBuildingDamage(JsonObject dom)
        {
            var result = new Dictionary<string, SavedBuildingDamage>(StringComparer.Ordinal);
            if (Of(dom) is not { } section) return result;
            foreach (var (key, node) in section)
                if (node is JsonObject entry && BuildingDamageJson.IsEntry(entry))
                    result[key] = BuildingDamageJson.Read(key, entry);
            return result;
        }

        /// <summary><see cref="ReadBuildingDamage(JsonObject)"/> from scenario JSON text.</summary>
        public static IReadOnlyDictionary<string, SavedBuildingDamage> ReadBuildingDamage(string json)
            => JsonNode.Parse(json) is JsonObject dom ? ReadBuildingDamage(dom) : new Dictionary<string, SavedBuildingDamage>();

        /// <summary>True when both entries are building-damage entries (the distributed merge unions those; two DOOR entries for one key stay an error).</summary>
        public static bool IsBuildingDamageEntry(JsonNode? entry) => entry is JsonObject o && BuildingDamageJson.IsEntry(o);

        /// <summary>⭐ The merge of two slices' entries for ONE building: breaches distinct, collapsed OR-ed, file order (a building hit by two deciding nodes, B-2).</summary>
        public static JsonObject MergeBuildingDamage(string key, JsonObject a, JsonObject b)
        {
            var d = BuildingDamageJson.Read(key, a);
            d.UnionWith(BuildingDamageJson.Read(key, b));
            return BuildingDamageJson.Write(d);
        }

        /// <summary>The section of <paramref name="dom"/> (either casing), or null.</summary>
        public static JsonObject? Of(JsonObject dom)
            => (dom[Name] ?? dom["terrainObjects"]) as JsonObject;

        /// <summary>
        /// The saved door states in <paramref name="dom"/>: key → state. ⛔ An entry that is not a door state fails LOUDLY — a scenario
        /// that silently loses a locked door is the failure this section exists to prevent.
        /// </summary>
        public static IReadOnlyDictionary<string, TerrainDoorState> ReadDoors(JsonObject dom)
        {
            var result = new Dictionary<string, TerrainDoorState>(StringComparer.Ordinal);
            if (Of(dom) is not { } section) return result;
            foreach (var (key, node) in section)
            {
                if (node is not JsonObject entry || entry[DoorProperty] is not JsonValue v || !v.TryGetValue(out string? text)) continue;   // a future object kind
                if (!Enum.TryParse(text, ignoreCase: true, out TerrainDoorState state) || !Enum.IsDefined(state))
                    throw new InvalidOperationException($"[Scenario] {Name}['{key}'].door = '{text}' is not a door state (Open, Closed, Locked, Destroyed).");
                result[key] = state;
            }
            return result;
        }

        /// <summary><see cref="ReadDoors(JsonObject)"/> from scenario JSON text.</summary>
        public static IReadOnlyDictionary<string, TerrainDoorState> ReadDoors(string json)
            => JsonNode.Parse(json) is JsonObject dom ? ReadDoors(dom) : new Dictionary<string, TerrainDoorState>();
    }
}
