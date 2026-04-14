using System.Collections.Concurrent;

namespace ModuleHost.Network.Cyclone.Services
{
    /// <summary>
    /// Maps between DDS's ulong DisTypeValue (external) and Core's simple int TypeId (internal).
    /// This allows Core to remain ignorant of DDS-specific type encoding (e.g., DIS Entity Type).
    /// Thread-safe for concurrent access.
    /// 
    /// TODO: DETERMINISM LIMITATION - TypeId assignment depends on packet arrival order.
    /// This means TypeIds may differ between sessions (e.g., live vs replay), which breaks
    /// save game compatibility. For production use, consider:
    /// 1. Pre-registering known types with fixed IDs at startup
    /// 2. Persisting the mapping table in save games
    /// 3. Using a deterministic hash function
    /// See EXTRACTION-REFINEMENTS.md § Warning 2 for details.
    /// </summary>
    public class TypeIdMapper
    {
        private readonly ConcurrentDictionary<ulong, int> _disToCore = new();
        private readonly ConcurrentDictionary<int, ulong> _coreToDis = new();
        private int _nextId = 1;
        private readonly object _lock = new();

        /// <summary>
        /// Gets or registers a Core TypeId for a DDS DisTypeValue.
        /// If the type is already registered, returns the existing mapping.
        /// If not, creates a new unique TypeId.
        /// </summary>
        public int GetCoreTypeId(ulong disType)
        {
            if (_disToCore.TryGetValue(disType, out int existingId))
            {
                return existingId;
            }

            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_disToCore.TryGetValue(disType, out existingId))
                {
                    return existingId;
                }

                int newId = _nextId++;
                RegisterMapping(disType, newId);
                return newId;
            }
        }

        /// <summary>
        /// Gets the DDS DisTypeValue for a given Core TypeId.
        /// </summary>
        /// <exception cref="System.ArgumentException">Thrown if the TypeId is not registered</exception>
        public ulong GetDISType(int coreTypeId)
        {
            if (_coreToDis.TryGetValue(coreTypeId, out var disType))
            {
                return disType;
            }

            throw new System.ArgumentException($"Core TypeId {coreTypeId} is not registered", nameof(coreTypeId));
        }

        /// <summary>
        /// Checks if a Core TypeId is registered.
        /// </summary>
        public bool HasCoreTypeId(int coreTypeId)
        {
            return _coreToDis.ContainsKey(coreTypeId);
        }

        /// <summary>
        /// Registers a bidirectional mapping between DIS type and Core TypeId.
        /// </summary>
        private void RegisterMapping(ulong disType, int coreTypeId)
        {
            _disToCore[disType] = coreTypeId;
            _coreToDis[coreTypeId] = disType;
        }
    }
}
