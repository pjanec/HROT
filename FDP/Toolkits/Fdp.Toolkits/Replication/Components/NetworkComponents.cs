using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Replication.Components
{
    // === DESCRIPTOR DEFINITIONS ===
    
    // EntityStateDescriptor removed in BATCH-09 (moved to Network.Cyclone/Descriptors/EntityStateDescriptor.cs in earlier batches)

    
    // === FDP COMPONENTS ===
    
    // NetworkOwnership RETIRED (CE-281) — it was a byte-for-byte duplicate of NetworkAuthority
    // (same PrimaryOwnerId/LocalNodeId/HasAuthority) written on the same line at
    // NetworkSpawningSystem, and every reader repoints to NetworkAuthority with identical
    // behaviour. Component id 140 stays RESERVED (see GlobalComponentIds.NetworkOwnership) and
    // is not reused. Ownership is now the single NetworkAuthority component
    // (docs/DESIGN_Distributed_Scenario_Persistence.md §7; UX_Authority_Aware_Writes §342).

    /// <summary>
    /// Transient tag component for entities awaiting network acknowledgment
    /// in reliable initialization mode. Removed after publishing lifecycle status.
    /// </summary>
    [ComponentId(GlobalComponentIds.PendingNetworkAck)]
    [DataPolicy(DataPolicy.Transient)]
    public struct PendingNetworkAck
    {
        /// <summary>Reliable Init type required to determine expected peers</summary>
        public ReliableInitType ExpectedType;
    }

    /// <summary>
    /// Managed sibling of <see cref="PendingNetworkAck"/> carrying the immutable snapshot of
    /// peer node ids the creator must collect an <c>Active</c> ack from before its own entity
    /// leaves <c>Constructing</c> (reliable-init cross-node barrier). Stamped at spawn from the
    /// membership set (<c>NodeRoster.NodesWithRole</c> / the NED cluster cache, minus the local
    /// node); read once by <see cref="Systems.NetworkGatewaySystem"/> when it defers the entity.
    /// Kept a managed component (an <c>int[]</c>) so the peer count is unbounded without a fixed
    /// inline buffer; the value is what matters, not the carrier
    /// (DESIGN_Cross_Node_Construction_Barrier.md §3a.3).
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkAckPeerSet)]
    public class NetworkAckPeerSet
    {
        /// <summary>The peer node ids to wait for. Empty ⇒ no cross-node wait (immediate ack).</summary>
        public int[] ExpectedAckPeers = Array.Empty<int>();
    }

    /// <summary>
    /// Transient tag placed on a REMOTE ghost whose <see cref="EntityMaster"/> arrived with the
    /// reliable-init <c>WaitForAcks</c> flag set. It marks the ghost <i>report-on-Active</i>: when
    /// the ghost's local construction completes (<c>Constructing → Active</c>), the peer publishes
    /// an <c>EntityLifecycleStatusDescriptor</c> so the creator can release its barrier
    /// (docs/DESIGN_Cross_Node_Construction_Barrier.md §2/§3a.2). Only reliable remote ghosts carry
    /// it — never a locally-owned entity or a fast-mode ghost.
    /// </summary>
    [ComponentId(GlobalComponentIds.ReportLifecycleOnActive)]
    [DataPolicy(DataPolicy.Transient)]
    public struct ReportLifecycleOnActive { }

    /// <summary>
    /// Tag component to force immediate network publication of owned descriptors,
    /// bypassing normal change detection. Used for ownership transfer confirmations.
    /// </summary>
    [ComponentId(GlobalComponentIds.ForceNetworkPublish)]
    [DataPolicy(DataPolicy.Transient)]
    public struct ForceNetworkPublish { }

    /// <summary>
    /// Event emitted when descriptor ownership changes (via OwnershipUpdate message).
    /// Allows modules to react to ownership transfers.
    /// </summary>
    [EventId(9010)]
    public struct DescriptorAuthorityChanged
    {
        public Entity Entity;
        public long DescriptorTypeId;
        
        /// <summary>True if this node acquired ownership, false if lost</summary>
        public bool IsNowOwner;
        
        /// <summary>New owner node ID</summary>
        public int NewOwnerId;
    }
}
