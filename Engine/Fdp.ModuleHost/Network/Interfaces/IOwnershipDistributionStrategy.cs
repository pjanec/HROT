using Fdp.Kernel;

namespace Fdp.ModuleHost.Network.Interfaces
{
    /// <summary>
    /// Strategy interface for determining initial descriptor ownership
    /// in partial ownership scenarios.
    /// </summary>
    public interface IOwnershipDistributionStrategy
    {
        /// <summary>
        /// Determines the initial owner for a specific descriptor on a newly created entity.
        /// </summary>
        /// <param name="descriptorTypeId">DDS descriptor type ID (e.g. <c>(long)EDescriptorType.dtWorldPos</c>).</param>
        /// <param name="entityType">DIS entity type from EntityMaster.</param>
        /// <param name="masterNodeId">Primary owner (EntityMaster owner / creator node ID).</param>
        /// <param name="instanceId">Descriptor instance ID (0 for single-instance).</param>
        /// <returns>
        /// Node ID that should own this descriptor, or <c>null</c> to retain ownership on
        /// the creator node (masterNodeId).
        /// </returns>
        int? GetInitialOwner(
            long descriptorTypeId,
            DISEntityType entityType,
            int masterNodeId,
            long instanceId);
    }
}
