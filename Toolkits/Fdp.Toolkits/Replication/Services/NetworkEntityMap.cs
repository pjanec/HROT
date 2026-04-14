using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Services
{
    [ComponentId(GlobalComponentIds.NetworkEntityMap)]
    public class NetworkEntityMap
    {
        private readonly Dictionary<long, Entity> _netToEntity = new();
        private readonly Dictionary<Entity, long> _entityToNet = new();
        
        private struct GraveyardEntry
        {
            public long NetworkId;
            public uint DeathFrame;
        }

        private readonly List<GraveyardEntry> _graveyard = new();
        private readonly uint _graveyardDurationFrames;

        /// <summary>
        /// Raised immediately after a new (netId, entity) pair is registered.
        /// Subscribers can use this to drain deferred retry queues keyed by network ID
        /// without having to poll on every simulation tick.
        /// </summary>
        public event Action<long, Entity>? EntityRegistered;

        public NetworkEntityMap(uint graveyardDurationFrames = 60)
        {
            _graveyardDurationFrames = graveyardDurationFrames;
        }

        public void Register(long netId, Entity entity)
        {
            if (_netToEntity.ContainsKey(netId))
                 throw new InvalidOperationException($"NetworkId {netId} already registered");
            
            _netToEntity[netId] = entity;
            _entityToNet[entity] = netId;
            
            // Remove from graveyard if ID is reused
            _graveyard.RemoveAll(g => g.NetworkId == netId);

            EntityRegistered?.Invoke(netId, entity);
        }

        public void Unregister(long netId, uint currentFrame)
        {
            if (_netToEntity.TryGetValue(netId, out var entity))
            {
                _netToEntity.Remove(netId);
                _entityToNet.Remove(entity);
                
                AddToGraveyard(netId, currentFrame);
            }
        }

        public bool TryGetEntity(long netId, out Entity entity)
        {
            return _netToEntity.TryGetValue(netId, out entity);
        }
        
        public bool TryGetNetworkId(Entity entity, out long netId)
        {
            return _entityToNet.TryGetValue(entity, out netId);
        }

        public bool IsGraveyard(long id)
        {
             foreach(var entry in _graveyard)
             {
                 if (entry.NetworkId == id) return true;
             }
             return false;
        }

        private void AddToGraveyard(long id, uint currentFrame)
        {
            _graveyard.Add(new GraveyardEntry { NetworkId = id, DeathFrame = currentFrame });
        }

        public void PruneGraveyard(uint currentFrame)
        {
             _graveyard.RemoveAll(e => (currentFrame - e.DeathFrame) > _graveyardDurationFrames);
        }

        public void PruneDeadEntities(EntityRepository repo)
        {
            var toRemove = new List<long>();
            foreach (var kvp in _netToEntity)
            {
                if (!repo.IsAlive(kvp.Value))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            
            foreach (var netId in toRemove)
            {
                Unregister(netId, repo.GlobalVersion);
            }
        }

    }
}
