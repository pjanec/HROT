using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class OwnershipIngressSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap      _entityMap;
        private readonly int                   _localNodeId;
        private readonly DescriptorOwnershipMap? _descriptorMap;

        public OwnershipIngressSystem(
            NetworkEntityMap       entityMap,
            INetworkTopology?      topology      = null,
            DescriptorOwnershipMap? descriptorMap = null)
        {
            _entityMap    = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId  = topology?.LocalNodeId ?? 0;
            _descriptorMap = descriptorMap;
        }

        /// <summary>
        /// Convenience constructor accepting a plain node ID rather than a full topology.
        /// </summary>
        public OwnershipIngressSystem(
            NetworkEntityMap       entityMap,
            int                    localNodeId,
            DescriptorOwnershipMap? descriptorMap = null)
        {
            _entityMap     = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId   = localNodeId;
            _descriptorMap = descriptorMap;
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            int localNodeId = _localNodeId;

            var updates = view.ConsumeEvents<OwnershipUpdate>();
            foreach (var update in updates)
            {
                if (!_entityMap.TryGetEntity(update.NetworkId.Value, out Entity entity))
                    continue;
                if (!repo.IsAlive(entity)) continue;

                // ── 1. Update the managed DescriptorOwnership dictionary ─────────
                DescriptorOwnership ownership;
                if (repo.HasManagedComponent<DescriptorOwnership>(entity))
                    ownership = repo.GetComponent<DescriptorOwnership>(entity);
                else
                {
                    ownership = new DescriptorOwnership();
                    repo.SetManagedComponent(entity, ownership);
                }

                ownership.Map[update.PackedKey] = update.NewOwnerNodeId;

                var (typeId, _) = OwnershipExtensions.UnpackKey(update.PackedKey);
                bool isAuth = localNodeId != 0 && update.NewOwnerNodeId == localNodeId;

                // ── 2. Update the native AuthorityMask using the exact component IDs ─
                // Use DescriptorOwnershipMap if available (populated from IDescriptorTranslator.TargetComponentIds).
                // This eliminates the legacy try/catch approach where descriptor ordinals were
                // blindly cast to component IDs, which fails whenever ordinal ≠ component ID.
                if (_descriptorMap != null)
                {
                    var componentIds = _descriptorMap.GetComponentIdsForDescriptor(typeId);
                    foreach (int componentId in componentIds)
                    {
                        if (repo.HasComponentByTypeId(entity, componentId))
                            repo.SetAuthority(entity, componentId, isAuth);
                    }
                }
                // No fallback try/catch — the map is the source of truth.
                // If the map has no entry, the AuthorityMask is not touched (safe default).

                if (isAuth)
                {
                    repo.Bus.Publish(new Fdp.Toolkit.Replication.Messages.DescriptorAuthorityChanged
                    {
                        Entity          = entity,
                        PackedKey       = update.PackedKey,
                        IsAuthoritative = true
                    });
                }
            }
        }
    }
}
