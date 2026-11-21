using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Managed component that tracks ownership of individual data descriptors (fields/components).
    /// Used for split-authority scenarios where different nodes simulate different parts of an entity.
    /// </summary>
    [ComponentId(GlobalComponentIds.DescriptorOwnership)]
    // CE-277(e): per-descriptor network ownership, reconstructed from the live topology on load;
    // declarative replacement for StagingEntityExtractor's hardcoded static-exclusion bit.
    [DataPolicy(DataPolicy.NoScenario)]
    public record DescriptorOwnership
    {
        /// <summary>
        /// Mapping from Descriptor PackedKey to Owner Node ID.
        /// </summary>
        public IReadOnlyDictionary<long, int> Map { get; init; } = new Dictionary<long, int>();

        /// <summary>
        /// Get the owner of a specific descriptor.
        /// </summary>
        public bool TryGetOwner(long packedKey, out int ownerId)
        {
            return Map.TryGetValue(packedKey, out ownerId);
        }

        /// <summary>
        /// A NEW value with the owner of a specific descriptor set; this instance is unchanged.
        /// </summary>
        public DescriptorOwnership WithOwner(long packedKey, int ownerId)
        {
            var map = new Dictionary<long, int>(Map) { [packedKey] = ownerId };
            return this with { Map = map };
        }
    }
}