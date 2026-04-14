using Fdp.Kernel;
using Fdp.ModuleHost_Core.Network.Interfaces;

namespace Fdp.ModuleHost_Core.Network
{
    /// <summary>
    /// Default ownership strategy that assigns all descriptors to the master node.
    /// Returns null for all queries, causing fallback to PrimaryOwnerId.
    /// </summary>
    public class DefaultOwnershipStrategy : IOwnershipDistributionStrategy
    {
        public int? GetInitialOwner(
            long descriptorTypeId,
            DISEntityType entityType,
            int masterNodeId,
            long instanceId)
        {
            // Return null = use master as default owner for all descriptors
            return null;
        }
    }
}
