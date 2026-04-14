using System;
using System.Collections.Concurrent;
using ModuleHost.Network.Cyclone.Topics;

namespace ModuleHost.Network.Cyclone.Services
{
    /// <summary>
    /// Maps between DDS NetworkAppId (external) and Core's simple int IDs (internal).
    /// This allows Core to remain ignorant of DDS-specific ID structures.
    /// Thread-safe for concurrent access.
    /// </summary>
    public class NodeIdMapper
    {
        private readonly ConcurrentDictionary<NetworkAppId, int> _externalToInternal = new();
        private readonly ConcurrentDictionary<int, NetworkAppId> _internalToExternal = new();
        private int _nextId = 2; // Start at 2 since 1 is reserved for local node
        private readonly object _lock = new();

        public int LocalNodeId => 1;

        /// <summary>
        /// Creates a new NodeIdMapper with the local node reserved as ID 1.
        /// </summary>
        /// <param name="localDomain">Local application domain ID</param>
        /// <param name="localInstance">Local application instance ID</param>
        public NodeIdMapper(int localDomain, int localInstance)
        {
            var localId = new NetworkAppId 
            { 
                AppDomainId = localDomain, 
                AppInstanceId = localInstance 
            };
            RegisterMapping(localId, 1); // Reserve 1 for local node
        }

        /// <summary>
        /// Gets or registers an internal ID for an external NetworkAppId.
        /// If the ID is already registered, returns the existing mapping.
        /// If not, creates a new unique internal ID.
        /// </summary>
        public int GetOrRegisterInternalId(NetworkAppId externalId)
        {
            if (_externalToInternal.TryGetValue(externalId, out int existingId))
            {
                return existingId;
            }

            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_externalToInternal.TryGetValue(externalId, out existingId))
                {
                    return existingId;
                }

                int newId = _nextId++;
                RegisterMapping(externalId, newId);
                return newId;
            }
        }

        /// <summary>
        /// Gets the external NetworkAppId for a given internal ID.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the internal ID is not registered</exception>
        public NetworkAppId GetExternalId(int internalId)
        {
            if (_internalToExternal.TryGetValue(internalId, out var externalId))
            {
                return externalId;
            }

            throw new ArgumentException($"Internal ID {internalId} is not registered", nameof(internalId));
        }

        /// <summary>
        /// Checks if an internal ID is registered.
        /// </summary>
        public bool HasInternalId(int internalId)
        {
            return _internalToExternal.ContainsKey(internalId);
        }

        /// <summary>
        /// Registers a bidirectional mapping between external and internal IDs.
        /// </summary>
        private void RegisterMapping(NetworkAppId externalId, int internalId)
        {
            _externalToInternal[externalId] = internalId;
            _internalToExternal[internalId] = externalId;
        }
    }
}
