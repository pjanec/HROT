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
        private readonly OwnershipApplier      _applier;

        public OwnershipIngressSystem(
            NetworkEntityMap       entityMap,
            INetworkTopology?      topology      = null,
            DescriptorOwnershipMap? descriptorMap = null)
        {
            _entityMap    = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _applier      = new OwnershipApplier(topology?.LocalNodeId ?? 0, descriptorMap);
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
            _applier       = new OwnershipApplier(localNodeId, descriptorMap);
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            var updates = view.ReadEvents<OwnershipUpdate>();
            foreach (var update in updates)
            {
                if (!_entityMap.TryGetEntity(update.NetworkId.Value, out Entity entity))
                    continue;
                if (!repo.IsAlive(entity)) continue;

                // ⭐ S5 — the apply logic lives in OwnershipApplier, shared with the transfer initiation and the
                //   partial-owner reclaim (R-167). 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S5.
                _applier.Apply(repo, entity, update.PackedKey, update.NewOwnerNodeId);
            }
        }
    }
}
