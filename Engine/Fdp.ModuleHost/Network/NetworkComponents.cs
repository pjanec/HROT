using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.ModuleHost_Core.Network.Messages;
using Fdp.ModuleHost_Core.Network.Interfaces;

namespace Fdp.ModuleHost_Core.Network
{
    // === DESCRIPTOR DEFINITIONS ===
    
    // EntityStateDescriptor removed in BATCH-09 (moved to ModuleHost.Network.Cyclone/Descriptors/EntityStateDescriptor.cs in earlier batches)

    
    // === FDP COMPONENTS ===
    
    /// <summary>
    /// Tracks primary network type ownership.
    /// Unmanaged component (can be used in Queries).
    /// </summary>
    [ComponentId(GlobalComponentIds.NetworkOwnership)]
    public struct NetworkOwnership
    {
        public int PrimaryOwnerId; // Default owner (EntityMaster)
        public int LocalNodeId;    // To verify ownership quickly

        /// <summary>
        /// True if the local node has authority over this entity.
        /// Replaces direct <c>PrimaryOwnerId == LocalNodeId</c> comparisons in systems
        /// (DB-MOD1-03 — standardize to the authority-check API).
        /// </summary>
        public bool HasAuthority => PrimaryOwnerId == LocalNodeId;
    }
    
    /// <summary>
    /// Transient tag component for entities awaiting network acknowledgment
    /// in reliable initialization mode. Removed after publishing lifecycle status.
    /// </summary>
    [ComponentId(GlobalComponentIds.PendingNetworkAck)]
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

    /// <summary>
    /// Helper extension methods to simplify ownership checks.
    /// </summary>
    public static class OwnershipExtensions
    {
        /// <summary>
        /// Packs descriptor type ID and instance ID into a single long key.
        /// Format: [TypeId: bits 63-32][InstanceId: bits 31-0]
        /// </summary>
        public static long PackKey(long descriptorTypeId, long instanceId)
        {
            return (descriptorTypeId << 32) | (uint)instanceId;
        }

        /// <summary>
        /// Unpacks a composite key into descriptor type ID and instance ID.
        /// </summary>
        public static (long TypeId, long InstanceId) UnpackKey(long packedKey)
        {
            long typeId = packedKey >> 32;
            long instanceId = (uint)(packedKey & 0xFFFFFFFF);
            return (typeId, instanceId);
        }

        /// <summary>
        /// Checks if this node owns the descriptor identified by the packed key.
        /// </summary>
        public static bool OwnsDescriptorKey(this ISimulationView view, Entity entity, long packedKey)
        {
            if (!view.HasComponent<NetworkOwnership>(entity)) return false;
            
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            
            // NOTE: Detailed per-descriptor ownership map was removed as part of Core simplification (BATCH-07).
            // Logic now falls back to Primary Owner. Implement custom logic in modules if per-descriptor ownership is needed again.
            
            // Fallback to Primary
            return ownership.PrimaryOwnerId == ownership.LocalNodeId;
        }

        /// <summary>
        /// Overload of OwnsDescriptor that accepts separate typeId and instanceId.
        /// Packs them internally before lookup.
        /// </summary>
        public static bool OwnsDescriptor(this ISimulationView view, Entity entity, 
            long descriptorTypeId, long instanceId)
        {
            long packedKey = PackKey(descriptorTypeId, instanceId);
            return OwnsDescriptorKey(view, entity, packedKey);
        }

        /// <summary>
        /// Checks ownership assuming Instance 0.
        /// </summary>
        public static bool OwnsDescriptor(this ISimulationView view, Entity entity, long descriptorTypeId)
        {
            // Assume Instance 0 if not specified (legacy/simple behavior)
            return OwnsDescriptor(view, entity, descriptorTypeId, 0);
        }

        public static int GetDescriptorOwnerKey(this ISimulationView view, Entity entity, long packedKey)
        {
             if (!view.HasComponent<NetworkOwnership>(entity)) return 0;
            
            var ownership = view.GetComponentRO<NetworkOwnership>(entity);
            
             // NOTE: Detailed per-descriptor ownership map was removed.
            
            return ownership.PrimaryOwnerId;
        }

        public static int GetDescriptorOwner(this ISimulationView view, Entity entity, 
            long descriptorTypeId, long instanceId)
        {
            long packedKey = PackKey(descriptorTypeId, instanceId);
            return GetDescriptorOwnerKey(view, entity, packedKey);
        }

        public static int GetDescriptorOwner(this ISimulationView view, Entity entity, long descriptorTypeId)
        {
            return GetDescriptorOwner(view, entity, descriptorTypeId, 0);
        }
    }
}
