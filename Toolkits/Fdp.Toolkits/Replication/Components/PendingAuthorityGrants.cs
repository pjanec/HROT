using System.Collections.Generic;
using Fdp.Kernel;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Transient <em>managed</em> component attached to a ghost entity by
    /// <c>DeferredTakeOwnershipIngressTranslator</c> when a <c>DeferredTakeOwnership</c>
    /// pre-genesis routing table arrives before the <c>EntityMaster</c>.
    ///
    /// <para>
    /// The dictionary maps each descriptor type ID to the node ID that will own it.
    /// Storing the intent here (rather than immediately flipping
    /// <see cref="EntityHeader.AuthorityMask"/>) keeps local memory open for the creator's
    /// seed data during the Ghost phase.  <c>DeferredTakeoverSystem</c> consumes this
    /// component on the Constructing transition, claims the relevant authority bits, and
    /// removes the component so it does not pollute long-term state.
    /// </para>
    /// </summary>
    [ComponentId(GlobalComponentIds.PendingAuthorityGrants)]
    public class PendingAuthorityGrants
    {
        /// <summary>
        /// Descriptor type ID → owner node ID.  The local node should process only
        /// entries whose value equals its own node ID.
        /// </summary>
        public Dictionary<long, int> GrantsByDescriptor { get; } = new();

        /// <summary>The node ID that sent the <c>DeferredTakeOwnership</c> message (the original creator).</summary>
        public int CreatorNodeId;

        /// <summary>Merges a grant into the dictionary (last-write-wins per descriptor).</summary>
        public void Merge(long descriptorTypeId, int nodeId)
            => GrantsByDescriptor[descriptorTypeId] = nodeId;
    }
}
