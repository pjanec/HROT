using System;
using System.Collections.Generic;

namespace Fdp.Kernel
{
    /// <summary>
    /// Stores per-component metadata including authority and part descriptors.
    /// Used for network synchronization and distributed authority.
    /// </summary>
    public sealed class ComponentMetadataTable : IDisposable
    {
        // Stores part descriptors per entity per component type
        private readonly Dictionary<int, Dictionary<int, PartDescriptor>> _partDescriptors;
        private readonly object _lock = new object();
        private bool _disposed;
        
        public ComponentMetadataTable()
        {
            _partDescriptors = new Dictionary<int, Dictionary<int, PartDescriptor>>();
        }
        
        /// <summary>
        /// Sets the part descriptor for a specific component on an entity.
        /// Indicates which parts of the component are present/modified.
        /// </summary>
        public void SetPartDescriptor(int entityIndex, int componentTypeId, PartDescriptor descriptor)
        {
            lock (_lock)
            {
                if (!_partDescriptors.TryGetValue(entityIndex, out var componentMap))
                {
                    componentMap = new Dictionary<int, PartDescriptor>();
                    _partDescriptors[entityIndex] = componentMap;
                }
                
                componentMap[componentTypeId] = descriptor;
            }
        }
        
        /// <summary>
        /// Gets the part descriptor for a component, or returns full descriptor if not set.
        /// </summary>
        public PartDescriptor GetPartDescriptor(int entityIndex, int componentTypeId)
        {
            lock (_lock)
            {
                if (_partDescriptors.TryGetValue(entityIndex, out var componentMap))
                {
                    if (componentMap.TryGetValue(componentTypeId, out var descriptor))
                    {
                        return descriptor;
                    }
                }
                
                // Default: all parts present
                return PartDescriptor.All();
            }
        }
        
        /// <summary>
        /// Checks if a specific part is present for a component.
        /// </summary>
        public bool HasPart(int entityIndex, int componentTypeId, int partIndex)
        {
            var descriptor = GetPartDescriptor(entityIndex, componentTypeId);
            return descriptor.HasPart(partIndex);
        }
        
        /// <summary>
        /// Clears all metadata for an entity (on destroy).
        /// </summary>
        public void ClearEntity(int entityIndex)
        {
            lock (_lock)
            {
                _partDescriptors.Remove(entityIndex);
            }
        }
        
        /// <summary>
        /// Clears metadata for a specific component on an entity.
        /// </summary>
        public void ClearComponent(int entityIndex, int componentTypeId)
        {
            lock (_lock)
            {
                if (_partDescriptors.TryGetValue(entityIndex, out var componentMap))
                {
                    componentMap.Remove(componentTypeId);
                    
                    // Remove entity entry if no components left
                    if (componentMap.Count == 0)
                    {
                        _partDescriptors.Remove(entityIndex);
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the total number of entities with metadata.
        /// </summary>
        public int EntityCount
        {
            get
            {
                lock (_lock)
                {
                    return _partDescriptors.Count;
                }
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_lock)
            {
                _partDescriptors.Clear();
            }
            
            _disposed = true;
        }
    }
}
