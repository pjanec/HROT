using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace FDP.Toolkit.DER
{
    public class DerRepo : IDerRepo
    {
        private readonly ConcurrentDictionary<int, DerEntity> _entities = new();

        public event Action<IDerEntity>? EntityCreated;
        public event Action<IDerEntity>? EntityDeleted;

        public IDerEntity? GetEntity(int entityId)
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

        public IDerEntity CreateEntity(int entityId, long tkbType)
        {
            var entity = new DerEntity(entityId, tkbType);
            if (!_entities.TryAdd(entityId, entity))
            {
                throw new InvalidOperationException($"Entity {entityId} already exists");
            }

            EntityCreated?.Invoke(entity);
            return entity;
        }

        public void DeleteEntity(int entityId)
        {
            if (_entities.TryRemove(entityId, out var entity))
            {
                EntityDeleted?.Invoke(entity);
            }
        }
    }
}
