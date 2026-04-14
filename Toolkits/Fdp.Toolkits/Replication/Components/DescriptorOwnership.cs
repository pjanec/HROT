using System;
using System.Collections.Generic;
using Fdp.Kernel;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Managed component that tracks ownership of individual data descriptors (fields/components).
    /// Used for split-authority scenarios where different nodes simulate different parts of an entity.
    /// </summary>
    [ComponentId(GlobalComponentIds.DescriptorOwnership)]
    public class DescriptorOwnership
    {
        /// <summary>
        /// Mapping from Descriptor PackedKey to Owner Node ID.
        /// </summary>
        public Dictionary<long, int> Map { get; set; } = new Dictionary<long, int>();

        /// <summary>
        /// Get the owner of a specific descriptor.
        /// </summary>
        public bool TryGetOwner(long packedKey, out int ownerId)
        {
            return Map.TryGetValue(packedKey, out ownerId);
        }

        /// <summary>
        /// Set the owner for a specific descriptor.
        /// </summary>
        public void SetOwner(long packedKey, int ownerId)
        {
            Map[packedKey] = ownerId;
        }
    }
}