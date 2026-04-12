using System.Collections.Concurrent;
using System.Collections.Generic;
using Fdp.Kernel;

namespace ModuleHost.Network.Cyclone.Services
{
    public class NetworkEntityMap
    {
        private readonly ConcurrentDictionary<long, Entity> _map = new();

        public void Register(long networkId, Entity entity)
        {
            _map[networkId] = entity;
        }

        public void Unregister(long networkId)
        {
            _map.TryRemove(networkId, out _);
        }

        public bool TryGet(long networkId, out Entity entity)
        {
            return _map.TryGetValue(networkId, out entity);
        }

        public void Clear()
        {
            _map.Clear();
        }
    }
}
