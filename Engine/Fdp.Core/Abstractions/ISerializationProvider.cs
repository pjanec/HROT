using System;
using Fdp.Kernel;
using Fdp.Interfaces;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Provides binary serialization for descriptor types.
    /// Used for zero-allocation ghost stashing.
    /// </summary>
    public interface ISerializationProvider
    {
        /// <summary>
        /// Gets the serialized size in bytes for a descriptor.
        /// </summary>
        int GetSize(object descriptor);
        
        /// <summary>
        /// Encodes descriptor to binary buffer.
        /// </summary>
        void Encode(object descriptor, Span<byte> buffer);
        
        /// <summary>
        /// Applies serialized descriptor data to an entity.
        /// </summary>
        void Apply(Entity entity, ReadOnlySpan<byte> buffer, IEntityCommandBuffer cmd);
    }
    
    /// <summary>
    /// Registry mapping descriptor ordinals to serialization providers.
    /// </summary>
    [ComponentId(GlobalComponentIds.ISerializationRegistry)]
    public interface ISerializationRegistry
    {
        void Register(long descriptorOrdinal, ISerializationProvider provider);
        ISerializationProvider Get(long descriptorOrdinal);
        bool TryGet(long descriptorOrdinal, out ISerializationProvider provider);
    }
}
