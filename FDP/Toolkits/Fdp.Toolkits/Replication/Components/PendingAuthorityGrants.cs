using System.Collections.Generic;
using Fdp.Core;

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
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public record PendingAuthorityGrants
    {
        /// <summary>
        /// Descriptor type ID → owner node ID.  The local node should process only
        /// entries whose value equals its own node ID.
        /// </summary>
        // ⭐ CE-3305 — `private set`, not get-only: a get-only member was LOST from every recording (the recorder writes settable members).
        public IReadOnlyDictionary<long, int> GrantsByDescriptor { get; init; } = new Dictionary<long, int>();

        /// <summary>The node ID that sent the <c>DeferredTakeOwnership</c> message (the original creator).</summary>
        public int CreatorNodeId { get; init; }

        /// <summary>A NEW value with the grant merged in (last-write-wins per descriptor); this instance is unchanged.</summary>
        public PendingAuthorityGrants WithGrant(long descriptorTypeId, int nodeId)
        {
            var map = new Dictionary<long, int>(GrantsByDescriptor) { [descriptorTypeId] = nodeId };
            return this with { GrantsByDescriptor = map };
        }
    }
}
