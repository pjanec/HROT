using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Fdp.Toolkit.DER
{
    public class DerRepo : IDerRepo
    {
        private readonly ConcurrentDictionary<long, DerEntity> _entities = new();

        public int LocalNodeId { get; }

        public DerRepo(int localNodeId = 0)
        {
            LocalNodeId = localNodeId;
        }


        public event Action<IDerEntity>? EntityCreated;
        public event Action<IDerEntity>? EntityDeleted;

        public IDerEntity? GetEntity(long entityId)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                return entity;
            }
            return null;
        }

        public IEnumerable<IDerEntity> GetAllEntities()
        {
            return _entities.Values.ToList(); // Return snapshot to avoid modification while iterating
        }

        public IDerEntity CreateEntity(long entityId, long tkbType)
        {
            var entity = new DerEntity(entityId, tkbType);
            if (!_entities.TryAdd(entityId, entity))
            {
                throw new InvalidOperationException($"Entity {entityId} already exists");
            }

            EntityCreated?.Invoke(entity);
            return entity;
        }

        public void DeleteEntity(long entityId)
        {
            if (_entities.TryRemove(entityId, out var entity))
            {
                EntityDeleted?.Invoke(entity);
            }
        }
    }
}
