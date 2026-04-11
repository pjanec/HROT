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

        /// <summary>
        /// Gets all currently attached descriptors as raw boxed objects, including the type
        /// and part ID for each.  This is the zero-reflection path used by live-updating UI
        /// panels: because <see cref="SetDescriptor{T}"/> boxes each struct into a new heap
        /// object on every write, callers can use <c>ReferenceEquals</c> to detect staleness
        /// without any allocation.
        /// </summary>
        IEnumerable<(Type Type, int PartId, object Data)> GetAllRawDescriptors();
    }
}
