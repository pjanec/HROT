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

        private int _ruleSeq = (int)(System.DateTime.UtcNow.Ticks / 10000 & 0x3FFFFFFF);

        /// <summary>
        /// ⭐ rules V-4 (§00.10) — <c>POST /objectives/{networkId}/control {"request":"Disarm"}</c>: writes the rule entity's <see cref="RuleControl"/> (<c>Rule.Request</c> / <c>Rule.Seq</c> /
        /// <c>Rule.Origin</c>, ONE change list) through <see cref="Fdp.Toolkit.Replication.Attributes.EntityWriteRouter"/> — applied here when this node owns the entity, a change-request to
        /// the owner otherwise (the instructor's UI uses the same path). <c>Seq</c> = the last one seen here + 1 (a node that holds no <see cref="RuleControl"/> keeps its own counter: the owner
        /// acts on any Seq it has not yet seen). The answer says how it was routed (<c>direct</c> / <c>requested</c>); a <c>requested</c> one acts after the owner's next tick.
        /// </summary>
        public (JsonNode? result, string? error) ControlObjective(int networkId, string? request)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found. List the rule entities with GET /objectives.");
            if (!System.Enum.TryParse<RuleRequest>(request, ignoreCase: true, out var what) || what == RuleRequest.NoRequest || !System.Enum.IsDefined(what))
                return (null, $"'request' must be one of: {string.Join(", ", System.Enum.GetNames<RuleRequest>().Skip(1))}.");
            // ⭐ CE-4216 (RV-5) — the ONE shared write: RuleCommands (the instructor's rules view asks through it too)
            var route = RuleCommands.Control(_world, entity, what, RuleOrigin.DebugApi, ref _ruleSeq, out int seq);
            if (route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused)
                return (null, $"Entity {networkId}: nothing was written (this node has no way to ask the owner). Nothing was requested.");
            return (new JsonObject { ["networkId"] = networkId, ["request"] = what.ToString(), ["seq"] = seq, ["route"] = route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "direct" : "requested" }, null);
        }

        /// <summary>
        /// ⭐ rules ② (§00.11a) — <c>POST /objectives/{networkId}/edit {"params": {…}}</c>: a LIVE EDIT of a rule's params that keeps its state, through the SAME write path as <see cref="ControlObjective"/>:
        /// <see cref="RuleEdit"/> (<c>Rule.Params</c> / <c>Rule.EditSeq</c> / <c>Rule.EditOrigin</c>, ONE change list) — applied here when this node owns the entity, a change-request to the owner otherwise.
        /// The owner's <c>RuleEditSystem</c> applies it at its next tick (a refusal shows as the status <c>Reason</c> "edit refused: …" and in the journal); the answer says how it was routed.
        /// </summary>
        public (JsonNode? result, string? error) EditObjective(int networkId, JsonNode? @params)
        {
            if (!_entityMap.TryGetEntity(networkId, out var entity) || !_world.IsAlive(entity))
                return (null, $"Entity {networkId} not found. List the rule entities with GET /objectives.");
            if (@params is not JsonObject) return (null, "'params' must be a JSON object (the rule's new params, the shape a scenario carries).");
            var route = RuleCommands.Edit(_world, entity, @params.ToJsonString(), (int)Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Operator, ref _ruleSeq, out int seq);
            if (route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Refused)
                return (null, $"Entity {networkId}: nothing was written (this node has no way to ask the owner). Nothing was edited.");
            return (new JsonObject { ["networkId"] = networkId, ["seq"] = seq, ["route"] = route == Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct ? "direct" : "requested" }, null);
        }

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
                var row = new JsonObject
                {
                    ["networkId"] = id, ["title"] = s.Title.ToString(), ["kind"] = s.Kind.ToString(), ["group"] = s.Group.ToString(), ["role"] = s.Role.ToString(),
                    ["state"] = s.State.ToString(), ["closed"] = s.Closed != 0, ["owner"] = s.Owner.ToString(),
                    ["progress"] = new JsonObject { ["cur"] = s.ProgressCur, ["need"] = s.ProgressNeed },
                    ["value"] = s.Value, ["target"] = s.Target, ["score"] = s.Score, ["pass"] = s.Pass, ["stepCur"] = (int)s.StepCur, ["stepCount"] = (int)s.StepCount,
                    ["capture"] = s.Capture != 0, ["challenger"] = s.Challenger.ToString(), ["areaCenter"] = new JsonArray(s.AreaCenter.X, s.AreaCenter.Y, s.AreaCenter.Z), ["areaRadius"] = s.AreaRadius,   // ⭐ G-8 (CE-4230)
                    ["points"] = s.Points, ["weight"] = s.Weight,
                    ["required"] = s.Required != 0, ["scored"] = s.Scored != 0, ["changedAt"] = s.ChangedAt, ["reason"] = s.Reason.ToString(),
                    ["subject"] = subject != 0 ? subject : null, ["parent"] = parent != 0 && rows.ContainsKey(parent) ? parent : null,
                    ["children"] = new JsonArray(children.TryGetValue(id, out var kids) ? kids.Select(k => (JsonNode)k).ToArray() : System.Array.Empty<JsonNode>()),
                };
                // ⭐ rules V-1 / V-2 (§00.10): what the rule names, and its timing — on the status, so ANY node answers the same
                var dep = new JsonArray(); for (int i = 0; i < s.DependsOnCount && i < 8; i++) dep.Add(s.DependsOn[i]);
                var act = new JsonArray(); for (int i = 0; i < s.ActsOnCount && i < 8; i++) act.Add(s.ActsOn[i]);
                row["dependsOn"] = dep; row["actsOn"] = act;
                row["timing"] = new JsonObject { ["holdFor"] = s.HoldFor, ["delay"] = s.Delay, ["cooldown"] = s.Cooldown, ["deadline"] = s.Deadline, ["expectedAt"] = s.ExpectedAt };
                if (s.Role == ObjectiveRole.Trigger)   // ⭐ rules S2-c (T8): the §10.11 machine's state, as a string, and how often it fired
                {
                    row["triggerState"] = s.TriggerState.ToString();
                    row["fireCount"] = s.FireCount;
                    row["lastFired"] = s.LastFired;
                    // ⭐ rules V-3: the trigger's when-leaves with their live values — present only once the owner has sampled them (feature ui.rules-live-values on there); the last sample stays when it goes off
                    if (_world.IsComponentTypeRegistered<RuleLeaves>() && _world.HasComponent<RuleLeaves>(e))
                    {
                        ref readonly var rl = ref _world.GetComponentRO<RuleLeaves>(e);
                        var leaves = new JsonArray();
                        for (int i = 0; i < rl.Count && i < RuleLeafPlan.MaxLeaves; i++)
                            leaves.Add(new JsonObject { ["label"] = rl.Leaves[i].Label.ToString(), ["value"] = rl.Leaves[i].Value, ["measured"] = rl.Leaves[i].Measured });
                        row["leaves"] = leaves;
                    }
                }
                return row;
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
