using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Abstractions;
using Hrot.Common;
using Hrot.NED.Descriptors;

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
    ///   <item><see cref="EDescriptorType.dtNavigationStatus"/> → <see cref="NodeRole.MuscleGround"/>.</item>
    ///   <item><see cref="EDescriptorType.dtEntityMission"/> → creator (Brain), not included in grants.</item>
    ///   <item><see cref="EDescriptorType.dtNavigationIntent"/> → creator (Brain), not included in grants.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Safe fallbacks: when no Muscle node is available, returns an empty list so the
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
        public IReadOnlyList<DescriptorGrant> GetInitialGrants(DISEntityType entityType, int masterNodeId)
        {
            // O(1) query — evaluates CpuUsagePercent across known Muscle nodes.
            int? muscleNode = _clusterCache.GetLeastLoadedNode(NodeRole.MuscleGround);

            if (!muscleNode.HasValue || muscleNode.Value == masterNodeId)
                return System.Array.Empty<DescriptorGrant>();

            // Physics descriptors: delegate to the least-loaded MuscleGround node.
            // Using EDescriptorType enum constants (CODE-STANDARDS §1 — no magic numbers).
            return new DescriptorGrant[]
            {
                new DescriptorGrant { DescriptorTypeId = (long)EDescriptorType.dtWorldPos,          NodeId = muscleNode.Value },
                new DescriptorGrant { DescriptorTypeId = (long)EDescriptorType.dtNavigationStatus,  NodeId = muscleNode.Value },
            };
        }
    }
}
