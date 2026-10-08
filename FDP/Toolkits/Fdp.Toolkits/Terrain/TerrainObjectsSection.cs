using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Extensions;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ Buildings Stage 5e (📄 docs/DESIGN_Building_Interiors.md §3b K5, §3j "5e") — the scenario's <c>TerrainObjects</c> section:
    /// the state of the terrain's objects (today: its doors), keyed by <see cref="TerrainObjectKey"/>. Door ENTITIES are never saved
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
            if (!repo.IsComponentTypeRegistered<DoorState>()) return null;
            var terrain = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;

            var doors = new SortedDictionary<string, TerrainDoorState>(StringComparer.Ordinal);   // stable file order
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
            if (doors.Count == 0) return null;

            var section = new JsonObject();
            foreach (var (key, state) in doors)
                section[key] = new JsonObject { [DoorProperty] = JsonValue.Create(state.ToString()) };
            return section;
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
