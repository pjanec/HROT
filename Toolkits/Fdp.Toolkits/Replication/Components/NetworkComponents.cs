using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Replication.Components
{
    // === DESCRIPTOR DEFINITIONS ===
    
    // EntityStateDescriptor removed in BATCH-09 (moved to Network.Cyclone/Descriptors/EntityStateDescriptor.cs in earlier batches)

    
    // === FDP COMPONENTS ===
    
    /// <summary>
    /// Tracks primary network type ownership.
    /// Unmanaged component (can be used in Queries).
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkOwnership)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct NetworkOwnership
    {
        public int PrimaryOwnerId; // Default owner (EntityMaster)
        public int LocalNodeId;    // To verify ownership quickly

        /// <summary>
        /// True if the local node has authority over this entity.
        /// Replaces direct <c>PrimaryOwnerId == LocalNodeId</c> comparisons in systems
        /// (DB-MOD1-03 -- standardize to the authority-check API).
        /// </summary>
        public bool HasAuthority => PrimaryOwnerId == LocalNodeId;
    }
    
    /// <summary>
    /// Transient tag component for entities awaiting network acknowledgment
    /// in reliable initialization mode. Removed after publishing lifecycle status.
    /// </summary>
    [ComponentId(GlobalComponentIds.PendingNetworkAck)]
    [DataPolicy(DataPolicy.NoSave)]
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
