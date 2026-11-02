using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ rules S1-g (📄 docs/DESIGN_Scenario_Rules.md §00.8) — <c>GET /objectives</c>, the minimal mission summary: every <see cref="ObjectiveStatus"/>
    /// on THIS node's world (on a non-Brain node these are the REPLICATED statuses — <c>dtObjectiveStatus</c>), the tree by
    /// <see cref="ObjectiveLinks.Parent"/>. The HUD / rules view are S4 / S5.
    /// </summary>
    public sealed partial class DebugApiService
    {
        private sealed record ObjRow(int NetId, Entity Entity);

        /// <summary><c>GET /objectives[?tree=1]</c>.</summary>
        public JsonNode GetObjectives(bool tree = false)
        {
            if (!_world.IsComponentTypeRegistered<ObjectiveStatus>())
                return new JsonObject { ["objectives"] = new JsonArray(), ["roots"] = new JsonArray(), ["note"] = "no objective component on this node" };
            bool links = _world.IsComponentTypeRegistered<ObjectiveLinks>();

            var rows = new Dictionary<int, Entity>();
            foreach (var e in _world.Query().With<ObjectiveStatus>().Build())
            {
                int id = NetIdOf(e);
                if (id != 0) rows[id] = e;
            }
            int ParentOf(Entity e) => links && _world.HasComponent<ObjectiveLinks>(e) ? _world.GetComponentRO<ObjectiveLinks>(e).Parent.NetworkId : 0;
            int SubjectOf(Entity e) => links && _world.HasComponent<ObjectiveLinks>(e) ? _world.GetComponentRO<ObjectiveLinks>(e).Subject.NetworkId : 0;
            string TitleOf(int id) => _world.GetComponentRO<ObjectiveStatus>(rows[id]).Title.ToString();

            var children = new Dictionary<int, List<int>>();
            var roots = new List<int>();
            foreach (var id in rows.Keys)
            {
                int p = ParentOf(rows[id]);
                if (p != 0 && p != id && rows.ContainsKey(p)) { if (!children.TryGetValue(p, out var l)) children[p] = l = new List<int>(); l.Add(id); }
                else roots.Add(id);
            }
            int ByTitle(int a, int b) { int c = string.CompareOrdinal(TitleOf(a), TitleOf(b)); return c != 0 ? c : a.CompareTo(b); }
            roots.Sort(ByTitle);
            foreach (var l in children.Values) l.Sort(ByTitle);

            JsonObject Row(int id)
            {
                var e = rows[id];
                ref readonly var s = ref _world.GetComponentRO<ObjectiveStatus>(e);
                int subject = SubjectOf(e), parent = ParentOf(e);
                return new JsonObject
                {
                    ["networkId"] = id, ["title"] = s.Title.ToString(), ["kind"] = s.Kind.ToString(), ["role"] = s.Role.ToString(),
                    ["state"] = s.State.ToString(), ["closed"] = s.Closed != 0, ["owner"] = s.Owner.ToString(),
                    ["progress"] = new JsonObject { ["cur"] = s.ProgressCur, ["need"] = s.ProgressNeed },
                    ["value"] = s.Value, ["target"] = s.Target, ["score"] = s.Score, ["points"] = s.Points, ["weight"] = s.Weight,
                    ["required"] = s.Required != 0, ["scored"] = s.Scored != 0, ["changedAt"] = s.ChangedAt, ["reason"] = s.Reason.ToString(),
                    ["subject"] = subject != 0 ? subject : null, ["parent"] = parent != 0 && rows.ContainsKey(parent) ? parent : null,
                    ["children"] = new JsonArray(children.TryGetValue(id, out var kids) ? kids.Select(k => (JsonNode)k).ToArray() : System.Array.Empty<JsonNode>()),
                };
            }

            JsonObject Nested(int id)
            {
                var n = Row(id);
                n["children"] = new JsonArray(children.TryGetValue(id, out var kids) ? kids.Select(k => (JsonNode)Nested(k)).ToArray() : System.Array.Empty<JsonNode>());
                return n;
            }

            var rootIds = new JsonArray(roots.Select(r => (JsonNode)r).ToArray());
            if (tree) return new JsonObject { ["objectives"] = new JsonArray(roots.Select(r => (JsonNode)Nested(r)).ToArray()), ["roots"] = rootIds };

            var ordered = new List<int>();
            void Walk(int id) { ordered.Add(id); if (children.TryGetValue(id, out var kids)) foreach (var k in kids) Walk(k); }
            foreach (var r in roots) Walk(r);
            return new JsonObject { ["objectives"] = new JsonArray(ordered.Select(i => (JsonNode)Row(i)).ToArray()), ["roots"] = rootIds };
        }

        private int NetIdOf(Entity e)
            => _world.IsComponentTypeRegistered<NetworkIdentity>() && _world.HasComponent<NetworkIdentity>(e)
                ? _world.GetComponentRO<NetworkIdentity>(e).Value
                : _entityMap.TryGetNetworkId(e, out var id) ? (int)id : 0;
    }
}
