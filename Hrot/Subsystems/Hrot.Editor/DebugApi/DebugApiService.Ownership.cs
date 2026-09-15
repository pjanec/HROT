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

                var components = new JsonArray();
                foreach (var t in map.GetComponentsForDescriptor(ordinal))
                    components.Add(t.Name);

                descriptors.Add(new JsonObject
                {
                    ["descriptorTypeId"] = ordinal,
                    ["isMaster"]         = masterOrdinal.HasValue && masterOrdinal.Value == ordinal,
                    ["ownedByThisNode"]  = ((ISimulationView)world).HasAuthority(entity, packedKey),
                    ["ownerNodeId"]      = explicitOwner.HasValue ? explicitOwner.Value : (JsonNode?)null,
                    ["components"]       = components,
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
