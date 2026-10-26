using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Utility;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md §2 "the same over the API") — what a utility decision IS, and which node binds
    /// which: the Decision tab's data as JSON, from the RUNNING definitions (a C#-built behaviour as an authored one).
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary><c>GET /utility/decisions</c> — every registered decision.</summary>
        public JsonNode ListUtilityDecisions()
        {
            UtilityDecisionCatalog.EnsureRegistered();
            var arr = new JsonArray();
            foreach (var (id, def) in UtilityDecisionCatalog.Shared.Entries.OrderBy(e => e.Def.DebugName, StringComparer.Ordinal))
                arr.Add(new JsonObject
                {
                    ["decisionId"] = id, ["name"] = def.DebugName, ["assetId"] = def.AssetId, ["kind"] = def.Kind.ToString(),
                    ["options"] = def.Options.Length,
                });
            return arr;
        }

        /// <summary><c>GET /utility/decisions/{key}?networkId=</c> — a decision's settings (key = its asset id, its name or its id), and with
        /// <paramref name="networkId"/> that unit's values. Null when no decision matches.</summary>
        public JsonNode? GetUtilityDecision(string key, long? networkId)
        {
            UtilityDecisionCatalog.EnsureRegistered();
            int id = 0;
            foreach (var (i, def) in UtilityDecisionCatalog.Shared.Entries)
                if (string.Equals(def.AssetId, key, StringComparison.OrdinalIgnoreCase) || string.Equals(def.DebugName, key, StringComparison.OrdinalIgnoreCase)
                    || i.ToString() == key) { id = i; break; }
            if (UtilityDecisionSheet.Build(id) is not { } sheet) return null;
            if (networkId is { } n && _entityMap.TryGetEntity(n, out var unit) && _world.IsAlive(unit)) sheet = sheet.WithLive(_world, unit);
            return sheet.ToJson(UnitJson);
        }

        /// <summary><c>GET /behaviours/{name}/decisions</c> — every node / state / transition of a behaviour that binds a decision, and how.</summary>
        public JsonNode GetBehaviourDecisions(string name)
        {
            var registry = _behaviorRegistry;
            var nodes = new JsonArray();
            if (registry == null || !registry.TryGetId(name, out int id) || !registry.TryGetDefinition(id, out var def))
                return new JsonObject { ["behaviour"] = name, ["error"] = $"behaviour '{name}' is not registered" };
            void Add(string kind, Guid element, string label, IAssetSubSelection sel)
            {
                foreach (var b in UtilityBindings.Of(registry, name, sel))
                {
                    var sheet = UtilityDecisionSheet.Build(b.DecisionId);
                    nodes.Add(new JsonObject
                    {
                        ["kind"] = kind, ["element"] = element.ToString(), ["label"] = label, ["role"] = b.Role.ToString(), ["via"] = b.Via,
                        ["decision"] = sheet?.Name, ["assetId"] = sheet?.AssetId, ["decisionId"] = b.DecisionId,
                        ["option"] = b.Role == DecisionRole.Reads ? sheet?.Options.FirstOrDefault(o => o.OptionId == b.Option)?.Name ?? b.Option.ToString() : null,
                    });
                }
            }
            if (registry.TryGetTreeBlob(id, out var tree) && tree.DebugMetadata is { } meta)
                foreach (var m in meta)
                    if (m != null && Guid.TryParse(m.VisualId, out var v)) Add("node", v, m.Label, new BTreeNodeSelection(v));
            if (def.HsmMetadata is { } hm)
            {
                foreach (var (i, sid) in hm.StateStableIds) Add("state", sid, hm.GetStateName(i), new HsmStateSelection(sid));
                foreach (var (_, vid) in hm.TransitionVisualIds) Add("transition", vid, vid.ToString("N")[..8], new HsmTransitionSelection(vid));
            }
            return new JsonObject { ["behaviour"] = name, ["bindings"] = nodes };
        }

        private JsonNode? UnitJson(Entity e)
        {
            var o = new JsonObject();
            if (_entityMap.TryGetNetworkId(e, out long n)) o["networkId"] = n;
            if (_world.IsAlive(e) && _world.HasComponent<EntityInfo>(e)) o["name"] = _world.GetComponentRO<EntityInfo>(e).Name.ToString();
            return o;
        }
    }
}
