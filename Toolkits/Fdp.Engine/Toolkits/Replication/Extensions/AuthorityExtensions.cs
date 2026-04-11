using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Extensions
{
    public static class AuthorityExtensions
    {
        public static bool HasAuthority(this ISimulationView view, Entity entity)
        {
            // For general entity authority, we use key 0 (or ignore key overrides unless Master is a descriptor?)
            // Usually HasAuthority(e) means "Do I own this entity's lifecycle/main simulation?"
            return HasAuthority(view, entity, 0);
        }

        public static bool HasAuthority(this ISimulationView view, Entity entity, long packedKey)
        {
            if (!view.IsAlive(entity)) return false;

            // 1. Hierarchical resolution (FDP-REP-306)
            Entity rootEntity = entity;

            if (view.HasComponent<PartMetadata>(entity))
            {
                var part = view.GetComponentRO<PartMetadata>(entity);
                rootEntity = part.ParentEntity;
                
                if (!view.IsAlive(rootEntity)) return false;
            }

            // We need to know who WE are (LocalNodeId).
            // NetworkAuthority component contains LocalNodeId + PrimaryOwnerId.
            if (!view.HasComponent<NetworkAuthority>(rootEntity))
            {
                // No NetworkAuthority component means the entity was created in an AllInOne or
                // unit-test context where there is no distributed authority tracking.
                // Treat as locally authoritative so systems and translators behave correctly
                // without a live network stack (e.g. headless demo, single-process integration tests).
                return true;
            }

            var netAuth = view.GetComponentRO<NetworkAuthority>(rootEntity);

            // 2. Specific Descriptor Ownership Override (Granular Authority)
            // Fix: HasManagedComponent now handles BitMask overflow internally (via Fallback).
            if (packedKey != 0 && view.HasManagedComponent<DescriptorOwnership>(rootEntity))
            {
                var ownership = view.GetManagedComponentRO<DescriptorOwnership>(rootEntity);
                if (ownership.TryGetOwner(packedKey, out int specificOwner))
                {
                    return specificOwner == netAuth.LocalNodeId;
                }
            }

            // 3. Fallback to Primary Entity Authority
            return netAuth.HasAuthority;
        }
    }
}
