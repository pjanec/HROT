using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// A single (descriptorTypeId, nodeId) routing entry inside a
    /// <see cref="DeferredTakeOwnership"/> message.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-generic-msgs")]
    public partial struct DescriptorOwnerEntry
    {
        /// <summary>Network descriptor type ID (matches the <c>EDescriptorType</c> ordinal).</summary>
        public long DescriptorTypeId;

        /// <summary>Node ID that will own this descriptor once the entity is Constructing.</summary>
        public int NodeId;
    }

    /// <summary>
    /// Pre-genesis routing table broadcast by the Creator (Brain/CGF) node
    /// before publishing <c>EntityMaster</c>.
    /// Exists to leave the ownership on the sender until the target receives all mandatory initial descriptors.
    ///
    /// <para>By arriving on the receiving Muscle (SimHost) before the <c>EntityMaster</c>
    /// packet, this message lets the ingress pipeline materialise a bare ghost and attach
    /// a <c>PendingAuthorityGrants</c> managed component BEFORE the ELM state machine can
    /// fire, elegantly solving the "unowned creation" race condition.</para>
    ///
    /// <para>
    /// A single broadcast covers <em>all</em> descriptor assignments for one entity.
    /// Each <see cref="DescriptorOwnerEntry"/> pairs a descriptor type ID with the node
    /// that should own it.  Receivers filter entries whose
    /// <see cref="DescriptorOwnerEntry.NodeId"/> equals their own local node ID.
    /// Because the list is unbounded there is no constraint on the number of descriptors.
    /// </para>
    /// </summary>
    [DdsTopic("DeferredTakeOwnership")]
    [DdsIdlFile("hrot-generic-msgs")]
    [DdsQos(
        Reliability   = DdsReliability.Reliable,
        Durability    = DdsDurability.Volatile,
        HistoryKind   = DdsHistoryKind.KeepAll,
        HistoryDepth  = 100)]
    public partial struct DeferredTakeOwnership
    {
        /// <summary>Network entity ID that this routing table targets.</summary>
        [DdsKey]
        public long EntityId;

        /// <summary>
        /// Per-descriptor ownership assignments for this entity.
        /// Each entry states which node ID should own a given descriptor type.
        /// A single message covers all nodes — each receiver extracts only the entries
        /// whose <see cref="DescriptorOwnerEntry.NodeId"/> matches its local node ID.
        /// </summary>
        [DdsManaged]
        public List<DescriptorOwnerEntry> Grants;
    }
}
