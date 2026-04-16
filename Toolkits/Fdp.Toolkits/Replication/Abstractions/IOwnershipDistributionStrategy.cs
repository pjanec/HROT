using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// Strategy interface for determining initial descriptor ownership
    /// in partial ownership scenarios.
    /// </summary>
    public interface IOwnershipDistributionStrategy
    {
        /// <summary>
        /// Returns the complete set of descriptor grants for a newly created entity.
        /// Each grant specifies a descriptor type ID and the non-master node that should own it.
        /// Descriptors absent from the returned list remain on the creator (masterNodeId).
        /// </summary>
        /// <param name="entityType">DIS entity type from EntityMaster.</param>
        /// <param name="masterNodeId">Primary owner (EntityMaster owner / creator node ID).</param>
        /// <returns>
        /// List of descriptor grants for non-master nodes.
        /// Returns an empty list when all descriptors remain on the creator.
        /// </returns>
        IReadOnlyList<DescriptorGrant> GetInitialGrants(DISEntityType entityType, int masterNodeId);
    }
}
