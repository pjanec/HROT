using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// ⭐ What the creator knows about a new entity when it decides whom to grant its role groups to.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.5 D-6.
    /// </summary>
    public readonly struct GrantRequest
    {
        public GrantRequest(DISEntityType entityType, TkbTemplate? template, int masterNodeId)
        {
            EntityType   = entityType;
            Template     = template;
            MasterNodeId = masterNodeId;
        }

        /// <summary>DIS entity type from EntityMaster.</summary>
        public DISEntityType EntityType { get; }

        /// <summary>
        /// The entity's TKB template — what decides which role groups APPLY (G-4: a brain, kinematics,
        /// perception). <c>null</c> when the creator has none, and then no group applies.
        /// </summary>
        public TkbTemplate? Template { get; }

        /// <summary>Primary owner (EntityMaster owner / creator node ID).</summary>
        public int MasterNodeId { get; }
    }

    /// <summary>
    /// Strategy interface for determining initial descriptor ownership
    /// in partial ownership scenarios.
    /// </summary>
    public interface IOwnershipDistributionStrategy
    {
        /// <summary>
        /// Returns the complete set of descriptor grants for a newly created entity.
        /// Each grant specifies a descriptor type ID and the non-master node that should own it.
        /// Descriptors absent from the returned list remain on the creator (<see cref="GrantRequest.MasterNodeId"/>).
        /// </summary>
        /// <returns>
        /// List of descriptor grants for non-master nodes.
        /// Returns an empty list when all descriptors remain on the creator.
        /// </returns>
        IReadOnlyList<DescriptorGrant> GetInitialGrants(in GrantRequest request);
    }
}
