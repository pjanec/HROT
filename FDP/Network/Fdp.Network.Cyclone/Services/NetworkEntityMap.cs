using System.Collections.Concurrent;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Network.Cyclone.Services
{
    public class NetworkEntityMap
    {
        private readonly ConcurrentDictionary<int, Entity> _map = new();

        public void Register(int networkId, Entity entity)
        {
            _map[networkId] = entity;
        }

        public void Unregister(int networkId)
        {
            _map.TryRemove(networkId, out _);
        }

        public bool TryGet(int networkId, out Entity entity)
        {
            return _map.TryGetValue(networkId, out entity);
        }

        public void Clear()
        {
            _map.Clear();
        }
    }
}
