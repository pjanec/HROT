using CycloneDDS.Schema;

namespace Fdp.Network.Cyclone.Topics
{
    /// <summary>
    /// Durable per-(entity, node) readiness descriptor for the reliable-init cross-node
    /// construction barrier. A peer publishes its <c>Active</c> status for a reliable entity
    /// once its local copy has finished construction; the creator collects these to release
    /// its own entity from <c>Constructing</c>, and a LATE JOINER reads the retained instances
    /// to learn every present node's readiness with no live transition
    /// (docs/DESIGN_Cross_Node_Construction_Barrier.md §2a/§3a.1).
    ///
    /// <para>Lives in the generic <c>Fdp.Network.Cyclone</c> layer so both the NED and BDC
    /// transports publish/subscribe the one type (§2c). QoS is Reliable + TransientLocal +
    /// KeepLast 1 so the LAST status per instance is retained for late joiners; the composite
    /// key <c>(EntityId, NodeId)</c> gives one retained instance per reporting node.</para>
    ///
    /// <para><c>NodeId</c> is the OWNERSHIP node id (the same value the node stamps on
    /// <c>OwnershipUpdate.NewOwner</c>), so a joiner can correlate the owner it learns from
    /// ownership with the owner's readiness instance (§2b). <c>StateValue</c> carries
    /// <c>Fdp.Core.EntityLifecycle</c> as an <c>int</c> to keep the DDS codegen free of a
    /// cross-assembly enum dependency — the CE-282 <c>RolesMask</c> wire precedent.</para>
    /// </summary>
    [DdsTopic("EntityLifecycleStatus")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1
    )]
    public partial struct EntityLifecycleStatusDescriptor
    {
        /// <summary>Network entity id (same space as <c>EntityMaster.EntityId</c>).</summary>
        [DdsKey, DdsId(0)] public long EntityId;

        /// <summary>Reporting node's OWNERSHIP node id (§2b — matches <c>OwnershipUpdate.NewOwner</c>).</summary>
        [DdsKey, DdsId(1)] public int NodeId;

        /// <summary><c>Fdp.Core.EntityLifecycle</c> as an int (Constructing=0, Active=1, …).</summary>
        [DdsId(2)] public int StateValue;

        /// <summary>Wall-clock / frame version of the status update.</summary>
        [DdsId(3)] public long Timestamp;
    }
}
