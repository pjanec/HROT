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
