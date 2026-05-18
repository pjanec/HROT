using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Fhsm.Kernel.Data;

namespace Fhsm.Kernel
{
    /// <summary>
    /// Thread-safe registry for HSM definitions.
    /// Maps Machine Structure Hash to Definition Blobs.
    /// Helper for managing multiple state machine types in an application.
    /// Implements Task 9: Definition Registry.
    /// </summary>
    public class HsmDefinitionRegistry
    {
        private readonly ConcurrentDictionary<uint, HsmDefinitionBlob> _definitions 
            = new ConcurrentDictionary<uint, HsmDefinitionBlob>();

        /// <summary>
        /// Register a definition blob.
        /// The MachineId derived from the blob's StructureHash is used as the key.
        /// </summary>
        public void Register(HsmDefinitionBlob blob)
        {
            uint id = blob.Header.StructureHash;
            if (id == 0) throw new ArgumentException("Invalid MachineId (0). Definition might not be initialized.", nameof(blob));
            
            _definitions.AddOrUpdate(id, blob, (key, existing) => 
            {
                // If we are updating, it might be a Hot Reload scenario.
                // We just accept the new blob.
                return blob;
            });
        }

        /// <summary>
        /// Try to get a definition by Machine ID (StructureHash).
        /// </summary>
        public bool TryGet(uint machineId, out HsmDefinitionBlob? blob)
        {
            return _definitions.TryGetValue(machineId, out blob);
        }
        
        /// <summary>
        /// Get a definition by Machine ID or throw if not found.
        /// </summary>
        public HsmDefinitionBlob Get(uint machineId)
        {
            if (!_definitions.TryGetValue(machineId, out var blob))
            {
                throw new KeyNotFoundException($"HSM Definition 0x{machineId:X8} not found in registry.");
            }
            return blob;
        }

        /// <summary>
        /// Remove a definition.
        /// </summary>
        public bool Unregister(uint machineId)
        {
            return _definitions.TryRemove(machineId, out _);
        }

        /// <summary>
        /// Clear all definitions.
        /// </summary>
        public void Clear()
        {
            _definitions.Clear();
        }
    }
}
