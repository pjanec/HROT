using System;
using System.Collections.Generic;

namespace FDP.Toolkit.DER
{
    /// <summary>
    /// DER entity with descriptor storage.
    /// </summary>
    public interface IDerEntity
    {
        /// <summary>
        /// Network entity ID (from EntityMaster).
        /// </summary>
        int EntityId { get; }
        
        /// <summary>
        /// TKB entity type ID (from EntityMaster).
        /// </summary>
        long TkbType { get; }
        
        /// <summary>
        /// Get descriptor of type T. Returns default if not present.
        /// </summary>
        T? GetDescriptor<T>(int partId = 0);
        
        /// <summary>
        /// Set descriptor of type T. Replaces existing if present.
        /// </summary>
        void SetDescriptor<T>(T descriptor, int partId = 0);
        
        /// <summary>
        /// Check if entity has descriptor of type T.
        /// </summary>
        bool HasDescriptor<T>(int partId = 0);
        
        /// <summary>
        /// Get types of all descriptors currently attached.
        /// </summary>
        IEnumerable<Type> GetAllDescriptorTypes();
    }
}
