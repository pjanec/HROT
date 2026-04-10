using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Kernel;

namespace FDP.Toolkit.NetworkSpawning.Events
{
    /// <summary>
    /// A single (descriptorTypeId, nodeId) grant pair carried in
    /// <see cref="DeferredTakeOwnershipCommand"/>.
    /// Mirrors <c>Hrot.NED.Messages.DescriptorOwnerEntry</c> at the bus layer
    /// without introducing a cross-project dependency.
    /// </summary>
    public struct DescriptorGrant
    {
        /// <summary>Descriptor type ID (matches the <c>EDescriptorType</c> ordinal).</summary>
        public long DescriptorTypeId;

        /// <summary>Node ID that will own this descriptor.</summary>
        public int NodeId;
    }

    /// <summary>
    /// Managed event published on the local <see cref="FdpEventBus"/> when the default
    /// processor (Brain/CGF) allocates a pre-genesis routing table for a newly created entity.
    ///
    /// <para>
    /// <c>DeferredTakeOwnershipEgressTranslator</c> consumes this event and broadcasts a
    /// <c>DeferredTakeOwnership</c> DDS sample to all peers <em>before</em> the
    /// <c>EntityMaster</c> sample is published, guaranteeing strict egress ordering.
    /// </para>
    ///
    /// <para>
    /// Using a managed class (rather than a struct) allows the <see cref="Grants"/> list
    /// to be unbounded — there is no constraint on the number of known descriptor types.
    /// </para>
    /// </summary>
    [EventId(9040)]
    public class DeferredTakeOwnershipCommand
    {
        /// <summary>Network entity ID the routing table applies to.</summary>
        public long NetworkId;

        /// <summary>
        /// TKB entity type ID forwarded to Muscle nodes so they can attach
        /// <c>TkbIdentity</c> to the ghost entity.
        /// </summary>
        public long TkbType;

        /// <summary>
        /// Per-descriptor ownership assignments.  Each entry states which node ID should
        /// own a given descriptor type for this entity.
        /// </summary>
        public List<DescriptorGrant> Grants { get; } = new();
    }
}
