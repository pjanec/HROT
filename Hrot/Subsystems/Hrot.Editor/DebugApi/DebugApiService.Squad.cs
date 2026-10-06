using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Squad;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-3087</c> (G3) — <c>GET /entities/{networkId}/squad</c>: a squad as its commander sees it — the members with
    /// the target each is assigned (the fire distribution, <c>CE-3088</c>), and the merged contact pool that assignment reads.
    /// On a MEMBER it names the commander and the member's own slot. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §11 (D5).
    /// <para>Read it on the BRAIN perspective (<c>Scenario</c> on a cluster) — the squad layer runs there.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary><c>GET /entities/{networkId}/squad</c>.</summary>
        public JsonNode GetEntitySquad(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };

            bool registered = _world.IsComponentTypeRegistered<UnitRoster>() && _world.IsComponentTypeRegistered<SquadCognitiveState>();
            if (registered && _world.HasComponent<UnitRoster>(unit) && _world.HasComponent<SquadCognitiveState>(unit))
                return DescribeSquad(networkId, unit);

            if (_world.IsComponentTypeRegistered<UnitSubordinate>() && _world.HasComponent<UnitSubordinate>(unit))
            {
                var commander = _world.GetComponentRO<UnitSubordinate>(unit).Commander;
                var node = new JsonObject { ["networkId"] = networkId, ["role"] = "member", ["commander"] = UnitNode(commander) };
                if (registered && !commander.IsNull && _world.IsAlive(commander)
                    && _world.HasComponent<UnitRoster>(commander) && _world.HasComponent<SquadCognitiveState>(commander))
                {
                    var roster = _world.GetComponentRO<UnitRoster>(commander);
                    int idx = UnitRoster.IndexOf(ref roster, unit);
                    if (idx >= 0) node["assignment"] = SlotNode(in _world.GetComponentRO<SquadCognitiveState>(commander), idx);
                }
                return node;
            }

            return new JsonObject { ["networkId"] = networkId, ["role"] = "none", ["note"] = "Not a squad commander or member." };
        }

        private JsonObject DescribeSquad(long networkId, Entity commander)
        {
            ref readonly var roster = ref _world.GetComponentRO<UnitRoster>(commander);
            ref readonly var state  = ref _world.GetComponentRO<SquadCognitiveState>(commander);

            var members = new JsonArray();
            for (int i = 0; i < roster.Count && i < 16; i++)
            {
                var m = UnitNode(roster.SubordinateEntities[i]);
                m["slot"] = i;
                m["assignment"] = SlotNode(in state, i);
                members.Add(m);
            }

            var contacts = new JsonArray();
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);
            for (int i = 0; i < state.Contacts.Count && i < 16; i++)
            {
                var c = pool[i];
                bool heard = (c.Flags & SquadContact.AnonymousFlag) != 0;
                var n = heard ? new JsonObject { ["heard"] = true } : UnitNode(new Entity((ulong)c.EntityId));
                n["threat"]   = System.Math.Round(c.ThreatScore, 3);
                n["position"] = new JsonArray(System.Math.Round(c.PositionX, 1), System.Math.Round(c.PositionY, 1), System.Math.Round(c.PositionZ, 1));
                n["sources"]  = c.SourceMembersMask;
                n["lastSeenTick"] = c.LastSeenTick;
                contacts.Add(n);
            }

            return new JsonObject
            {
                ["networkId"]     = networkId,
                ["role"]          = "commander",
                ["memberCount"]   = roster.Count,
                ["members"]       = members,
                ["lastMergeTick"] = state.Contacts.LastMergeTick,
                ["contactCount"]  = state.Contacts.Count,
                ["contacts"]      = contacts,
            };
        }

        private JsonNode? SlotNode(in SquadCognitiveState state, int slot)
        {
            long handle = state.Assignment.GetAssignedTarget(slot);
            if (handle == 0) return null;
            var t = UnitNode(new Entity((ulong)handle));
            ref readonly var s = ref Unsafe.AsRef(in state).Assignment.GetSlot(slot);
            t["score"] = System.Math.Round(s.AssignmentScore, 3);
            t["focusFireCount"] = s.FocusFireCount;
            return t;
        }

        /// <summary>An entity as {networkId, name} (or {gone:true}).</summary>
        private JsonObject UnitNode(Entity e)
        {
            var node = new JsonObject();
            if (e.IsNull || !_world.IsAlive(e)) { node["gone"] = true; return node; }
            if (_entityMap.TryGetNetworkId(e, out long netId)) node["networkId"] = netId;
            if (_world.HasComponent<EntityInfo>(e))
                node["name"] = _world.GetComponentRO<EntityInfo>(e).Name.ToString();
            return node;
        }
    }
}
