using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Extensions
{
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
            if (!view.HasComponent<NetworkAuthority>(entity)) return false;

            var ownership = view.GetComponentRO<NetworkAuthority>(entity);

            // NOTE: Detailed per-descriptor ownership map was removed as part of Core simplification (BATCH-07).
            // Logic now falls back to Primary Owner. Implement custom logic in modules if per-descriptor ownership is needed again.
            // CE-281: repointed from the retired NetworkOwnership to NetworkAuthority (identical shape).

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
             if (!view.HasComponent<NetworkAuthority>(entity)) return 0;

            var ownership = view.GetComponentRO<NetworkAuthority>(entity);

             // NOTE: Detailed per-descriptor ownership map was removed.
             // CE-281: repointed from the retired NetworkOwnership to NetworkAuthority (identical shape).

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
