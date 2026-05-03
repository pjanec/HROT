using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class OwnershipEgressSystem : IEcsModuleSystem
    {
        private readonly int _localNodeId;

        // Cache to track changes: Entity -> (PackedKey -> OwnerNodeId)
        private readonly Dictionary<Entity, Dictionary<long, int>> _lastKnownOwnership = new();

        public OwnershipEgressSystem(
            global::Fdp.Toolkit.Replication.INetworkTopology? topology = null)
        {
            _localNodeId = topology?.LocalNodeId ?? 0;
        }

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;

            // Remove destroyed entities from the ownership cache.
            foreach (var evt in view.ReadEvents<DestructionOrder>())
                _lastKnownOwnership.Remove(evt.Entity);

            // 1. Process active entities with DescriptorOwnership
            var query = repo.Query().WithManaged<DescriptorOwnership>().With<NetworkIdentity>().Build();
            
            foreach (var entity in query)
            {
                var currentOwnership = repo.GetComponent<DescriptorOwnership>(entity);
                var netId = repo.GetComponent<NetworkIdentity>(entity);
                
                if (!_lastKnownOwnership.TryGetValue(entity, out var lastMap))
                {
                    lastMap = new Dictionary<long, int>();
                    _lastKnownOwnership[entity] = lastMap;
                }

                int originNodeId = _localNodeId;
                
                // Check all current ownerships
                foreach (var kvp in currentOwnership.Map)
                {
                    long key = kvp.Key;
                    int newOwner = kvp.Value;
                    
                    bool changed = false;
                    if (!lastMap.TryGetValue(key, out int oldOwner))
                    {
                        changed = true;
                    }
                    else if (oldOwner != newOwner)
                    {
                        changed = true;
                    }
                    
                    if (changed)
                    {
                        lastMap[key] = newOwner;
                        
                        // Publish update event
                        repo.Bus.Publish(new OwnershipUpdate
                        {
                            NetworkId = netId,
                            PackedKey = key,
                            NewOwnerNodeId = newOwner,
                            OriginNodeId = originNodeId
                        });
                    }
                }
            }
        }
    }
}
