using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Messages;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.Export)]
    public class OwnershipEgressSystem : IEcsModuleSystem
    {
        // Cache to track changes: Entity -> (PackedKey -> OwnerNodeId)
        private readonly Dictionary<Entity, Dictionary<long, int>> _lastKnownOwnership = new();

        // Used for cleanup
        private readonly List<Entity> _deadEntities = new();

        public void Execute(ISimulationView view, float dt)
        {
            if (view is not EntityRepository repo) return;
            
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
                            NewOwnerNodeId = newOwner
                        });
                    }
                }
            }
            
            // 2. Cleanup Dead Entities from Cache
            _deadEntities.Clear();
            foreach (var entity in _lastKnownOwnership.Keys)
            {
                if (!repo.IsAlive(entity))
                {
                    _deadEntities.Add(entity);
                }
            }
            
            foreach (var dead in _deadEntities)
            {
                _lastKnownOwnership.Remove(dead);
            }
        }
    }
}
