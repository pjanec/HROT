using Fdp.Kernel;
using Hrot.Common;
using Hrot.NED.Descriptors;
using Fdp.ModuleHost.Network.Interfaces;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// Role-based ownership distribution strategy that delegates physics/kinematic
    /// descriptors to the least-loaded <see cref="NodeRole.MuscleGround"/> node while
    /// retaining cognitive descriptors (missions, intents) on the Brain creator.
    ///
    /// <para>
    /// Descriptor routing table (CODE-STANDARDS §1 — no magic numbers):
    /// <list type="bullet">
    ///   <item><see cref="EDescriptorType.dtWorldPos"/> → <see cref="NodeRole.MuscleGround"/>.</item>
    ///   <item><see cref="EDescriptorType.dtEntityMission"/> → creator (Brain), returns <c>null</c>.</item>
    ///   <item><see cref="EDescriptorType.dtNavigationIntent"/> → creator (Brain), returns <c>null</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Safe fallbacks: when no Muscle node is available, returns <c>null</c> so the
    /// Brain retains physics authority and avoids a cluster-wide failure.
    /// </para>
    /// </summary>
    public sealed class BrainMuscleOwnershipStrategy : IOwnershipDistributionStrategy
    {
        private readonly IClusterStateCache _clusterCache;

        public BrainMuscleOwnershipStrategy(IClusterStateCache clusterCache)
        {
            _clusterCache = clusterCache ?? throw new System.ArgumentNullException(nameof(clusterCache));
        }

        /// <inheritdoc/>
        public int? GetInitialOwner(
            long descriptorTypeId,
            DISEntityType entityType,
            int masterNodeId,
            long instanceId)
        {
            // Physics descriptors: delegate to the least-loaded MuscleGround node.
            // Using EDescriptorType enum constants (CODE-STANDARDS §1 — no magic numbers).
            bool isPhysicsDescriptor =
                descriptorTypeId == (long)EDescriptorType.dtWorldPos;

            if (isPhysicsDescriptor)
            {
                // O(1) O(1) query — evaluates CpuUsagePercent across known Muscle nodes.
                return _clusterCache.GetLeastLoadedNode(NodeRole.MuscleGround);
            }

            // Cognitive descriptors remain with the Brain creator (return null = masterNodeId).
            return null;
        }
    }
}
