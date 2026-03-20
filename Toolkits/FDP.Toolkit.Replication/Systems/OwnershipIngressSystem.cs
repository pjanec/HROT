using System;
using Fdp.Kernel;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Messages;
using FDP.Toolkit.Replication.Services;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class OwnershipIngressSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly INetworkTopology? _topology;

        public OwnershipIngressSystem(NetworkEntityMap entityMap, INetworkTopology? topology = null)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _topology = topology;
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            int localNodeId = _topology?.LocalNodeId ?? 0;

            // Consume events (destructive read)
            var updates = view.ConsumeEvents<OwnershipUpdate>();
            foreach (var update in updates)
            {
                if (!_entityMap.TryGetEntity(update.NetworkId.Value, out Entity entity))
                    continue;

                if (!repo.IsAlive(entity)) continue;

                DescriptorOwnership ownership;
                if (repo.HasManagedComponent<DescriptorOwnership>(entity))
                    ownership = repo.GetComponent<DescriptorOwnership>(entity);
                else
                {
                    ownership = new DescriptorOwnership();
                    repo.SetManagedComponent(entity, ownership);
                }

                ownership.Map[update.PackedKey] = update.NewOwnerNodeId;

                var (typeId, _) = ModuleHost.Core.Network.OwnershipExtensions.UnpackKey(update.PackedKey);
                bool isAuth = localNodeId != 0 && update.NewOwnerNodeId == localNodeId;

                try { repo.SetAuthority(entity, (int)typeId, isAuth); }
                catch (Exception) { }

                if (isAuth)
                {
                    repo.Bus.Publish(new FDP.Toolkit.Replication.Messages.DescriptorAuthorityChanged
                    {
                        Entity = entity,
                        PackedKey = update.PackedKey,
                        IsAuthoritative = true
                    });
                }
            }
        }
    }
}
