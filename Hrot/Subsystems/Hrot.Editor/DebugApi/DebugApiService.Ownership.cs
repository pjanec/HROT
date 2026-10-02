using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ CE-276 — the ai-debug ownership surface: read an entity's per-descriptor ownership and INITIATE a
    /// descriptor-ownership transfer to another node. Both are perspective-scoped (they act on the node whose
    /// world the active perspective reads) and capability-gated on NED presence — a host with no NED transport
    /// (editor / AllInOne) has a null <c>DescriptorMap</c> / <c>RequestOwnershipTransfer</c> and the endpoints
    /// answer 503. 📄 docs/DESIGN_Entity_Ownership_Transfer.md §5a.
    /// </summary>
    public sealed partial class DebugApiService
    {
        private const string NoNedTransport = "This host wires no NED transport (no descriptor ownership).";

        /// <summary>The role group a descriptor is bound to (design §2), or <c>"creator"</c> when it is in none.</summary>
        private static string GroupOf(Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap map, long ordinal)
        {
            foreach (NodeRole role in new[] { NodeRole.Brain, NodeRole.MuscleGround, NodeRole.Perception,
                                              NodeRole.NavigationSolver, NodeRole.Map2D })
                foreach (long d in map.DescriptorsOf(role))
                    if (d == ordinal) return role.ToString();
            return "creator";
        }

        /// <summary>
        /// <c>GET /entities/{id}/ownership</c> — the entity's descriptors, each with its owner and the
        /// components it binds, plus the entity-level primary/save owner. Read side + verification surface for
        /// the transfer. Returns <c>(null, error)</c> ⇒ 503 when no NED, <c>(null, null)</c> ⇒ 404 when the
        /// entity is unknown.
        /// </summary>
        public (JsonNode? result, string? error) GetEntityOwnership(long networkId)
        {
            var map       = _dispatcher?.DescriptorMap;
            var entityMap = _dispatcher?.EntityMap;
            var world     = _dispatcher?.World;
            if (map is null || entityMap is null || world is null)
                return (null, NoNedTransport);

            if (!entityMap.TryGetEntity(networkId, out Entity entity) || !world.IsAlive(entity))
                return (null, null);

            int primaryOwnerId = -1, localNodeId = -1;
            if (world.HasComponent<NetworkAuthority>(entity))
            {
                var na = world.GetComponentRO<NetworkAuthority>(entity);
                primaryOwnerId = na.PrimaryOwnerId;
                localNodeId    = na.LocalNodeId;
            }

            DescriptorOwnership? overrides = world.HasManagedComponent<DescriptorOwnership>(entity)
                ? world.GetComponent<DescriptorOwnership>(entity)
                : null;

            long? masterOrdinal = map.PrimaryOwnerDescriptorOrdinal;

            var descriptors = new JsonArray();
            foreach (long ordinal in map.RegisteredDescriptors)
            {
                long packedKey = OwnershipExtensions.PackKey(ordinal, 0);

                int? explicitOwner = null;
                if (overrides != null && overrides.TryGetOwner(packedKey, out int o))
                    explicitOwner = o;

                // Prefer component TYPE NAMES when the map has them (manual RegisterMapping); translators
                // register component IDS only, so fall back to the ids so the field is never silently empty.
                var components = new JsonArray();
                var types = map.GetComponentsForDescriptor(ordinal);
                if (types.Length > 0)
                    foreach (var t in types) components.Add(t.Name);
                else
                    foreach (int cid in map.GetComponentIdsForDescriptor(ordinal)) components.Add(cid);

                // ⭐⭐ CE-515 — the CLAIM beside the RECORD. The record says who owns the descriptor; the claim is
                //   whether THIS node holds authority over each of its components. One ownership truth (Q79 §0.7)
                //   means they agree on every node — `claimMatchesRecord` is that check, per descriptor, so an
                //   end-to-end run can assert it from every perspective (design §5.7).
                bool ownedByThisNode = ((ISimulationView)world).HasAuthority(entity, packedKey);
                var claims = new JsonArray();
                bool claimMatchesRecord = true;
                foreach (int cid in map.GetComponentIdsForDescriptor(ordinal))
                {
                    if (!world.HasComponentByTypeId(entity, cid)) continue;   // a component this entity does not carry
                    bool claimed = world.HasAuthority(entity, cid);
                    claimMatchesRecord &= claimed == ownedByThisNode;
                    claims.Add(new JsonObject { ["componentId"] = cid, ["claimedByThisNode"] = claimed });
                }

                descriptors.Add(new JsonObject
                {
                    ["descriptorTypeId"]   = ordinal,
                    ["isMaster"]           = masterOrdinal.HasValue && masterOrdinal.Value == ordinal,
                    ["group"]              = GroupOf(map, ordinal),
                    ["ownedByThisNode"]    = ownedByThisNode,
                    ["ownerNodeId"]        = explicitOwner.HasValue ? explicitOwner.Value : (JsonNode?)null,
                    ["components"]         = components,
                    ["claims"]             = claims,
                    ["claimMatchesRecord"] = claimMatchesRecord,
                });
            }

            return (new JsonObject
            {
                ["networkId"]      = networkId,
                ["primaryOwnerId"] = primaryOwnerId,
                ["localNodeId"]    = localNodeId,
                ["descriptors"]    = descriptors,
            }, null);
        }

        /// <summary>
        /// <c>POST /entities/{id}/ownership/transfer</c> — hand the entity (or a subset of its descriptors) to
        /// another node by publishing a <see cref="TransferEntityOwnershipRequest"/> on this node's bus.
        /// Body: <c>{ "newOwnerNodeId": int, "scope": "MasterOnly"|"AllOwnedByThisNode"|"SpecificDescriptors",
        /// "descriptors": [long, …] }</c> — <c>descriptors</c> are NED descriptor type ids (required only for
        /// <c>SpecificDescriptors</c>). The publish is fire-and-forget; verify with <c>GET …/ownership</c>.
        /// </summary>
        public (JsonNode? result, string? error) TransferEntityOwnership(long networkId, JsonNode? body)
        {
            var publish   = _dispatcher?.RequestOwnershipTransfer;
            var entityMap = _dispatcher?.EntityMap;
            if (publish is null || entityMap is null)
                return (null, NoNedTransport);

            if (!entityMap.TryGetEntity(networkId, out _))
                return (null, null);   // 404

            if (body is not JsonObject dom)
                return (null, "Body must be a JSON object.");

            if (dom["newOwnerNodeId"] is null || !int.TryParse(dom["newOwnerNodeId"]!.ToString(), out int newOwner))
                return (null, "Body requires an integer 'newOwnerNodeId'.");

            var scopeStr = dom["scope"]?.GetValue<string>() ?? nameof(TransferScope.AllOwnedByThisNode);
            if (!Enum.TryParse<TransferScope>(scopeStr, ignoreCase: true, out var scope))
                return (null, $"Unknown scope '{scopeStr}'. Use MasterOnly, AllOwnedByThisNode, or SpecificDescriptors.");

            long[]? descriptorIds = null;
            if (dom["descriptors"] is JsonArray arr)
            {
                var list = new System.Collections.Generic.List<long>(arr.Count);
                foreach (var n in arr)
                    if (n != null && long.TryParse(n.ToString(), out long d)) list.Add(d);
                descriptorIds = list.ToArray();
            }
            if (scope == TransferScope.SpecificDescriptors && (descriptorIds is null || descriptorIds.Length == 0))
                return (null, "SpecificDescriptors requires a non-empty 'descriptors' array of descriptor type ids.");

            publish(new TransferEntityOwnershipRequest
            {
                NetworkId         = networkId,
                NewOwnerNodeId    = newOwner,
                Scope             = scope,
                DescriptorTypeIds = descriptorIds,
            });

            return (new JsonObject
            {
                ["requested"]      = true,
                ["networkId"]      = networkId,
                ["newOwnerNodeId"] = newOwner,
                ["scope"]          = scope.ToString(),
                ["note"]           = "Transfer requested — verify with GET /entities/{id}/ownership.",
            }, null);
        }
    }
}
