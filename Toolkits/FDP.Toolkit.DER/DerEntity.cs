using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using FDP.Toolkit.DER; // Ensure namespace correct

namespace FDP.Toolkit.DER
{
    public class DerEntity : IDerEntity
    {
        private readonly ConcurrentDictionary<Tuple<Type, int>, object> _descriptors = new();

        public int EntityId { get; }
        public long TkbType { get; }

        public DerEntity(int entityId, long tkbType)
        {
            EntityId = entityId;
            TkbType = tkbType;
        }

        public T? GetDescriptor<T>(int partId = 0)
        {
            if (_descriptors.TryGetValue(Tuple.Create(typeof(T), partId), out var descriptor))
            {
                return (T)descriptor;
            }
            return default;
        }

        public void SetDescriptor<T>(T descriptor, int partId = 0)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            
            _descriptors.AddOrUpdate(Tuple.Create(typeof(T), partId), descriptor, (key, oldValue) => descriptor);
        }

        public bool HasDescriptor<T>(int partId = 0)
        {
            return _descriptors.ContainsKey(Tuple.Create(typeof(T), partId));
        }

        public IEnumerable<Type> GetAllDescriptorTypes()
        {
            return _descriptors.Keys.Select(k => k.Item1).Distinct().ToList(); // Return unique descriptor types
        }
    }
}
