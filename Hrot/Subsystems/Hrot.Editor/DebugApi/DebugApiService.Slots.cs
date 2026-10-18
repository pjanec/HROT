using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Behavior;

namespace Hrot.Editor.DebugApi
{
    public sealed partial class DebugApiService
    {
        /// <summary>
        /// ⭐⭐ <c>CE-2288</c> (CC-16, R-2050) — <c>GET /entities/{networkId}/slots</c>: EVERY occurrence slot the unit carries — the running
        /// task's, the SOP's, a paused task's, their hosted children's and working-state slots — named by its owner and role, its value
        /// decoded through the layout the root dump already uses (<see cref="OccurrenceSlotCatalog"/>). An unnamed slot is still listed.
        /// </summary>
        public JsonNode GetEntitySlots(long networkId)
        {
            if (!_entityMap.TryGetEntity(networkId, out var unit) || !_world.IsAlive(unit))
                return new JsonObject { ["error"] = $"Entity {networkId} not found." };
            var registry = _behaviorRegistry;
            if (registry == null)
                return new JsonObject { ["networkId"] = networkId, ["error"] = "This node has no behaviour registry (not a brain host)." };
            var slots = new JsonArray();
            foreach (var s in OccurrenceSlotCatalog.Read(_world, unit, registry))
            {
                slots.Add(new JsonObject
                {
                    ["key"] = s.Key, ["kind"] = s.Kind.ToString(), ["payloadSize"] = s.PayloadSize, ["structureHash"] = s.StructureHash,
                    ["owner"] = s.Owner.ToString(), ["behaviour"] = s.Behaviour, ["role"] = s.Role.ToString(), ["label"] = s.Label,
                    ["layout"] = s.LayoutType,
                    ["value"] = s.Value != null ? JsonSerializer.SerializeToNode(s.Value, FdpJsonOptionsRegistry.DefaultRelaxed) : null,
                });
            }
            return new JsonObject { ["networkId"] = networkId, ["count"] = slots.Count, ["slots"] = slots };
        }
    }
}
