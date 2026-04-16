using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Extensions
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

            // 2. Component authority from EntityHeader.AuthorityMask (single source of truth).
            if (packedKey != 0 && view is EntityRepository repo)
            {
                var (componentTypeId, _) = OwnershipExtensions.UnpackKey(packedKey);
                if (componentTypeId >= 0 &&
                    componentTypeId < FdpConfig.MAX_COMPONENT_TYPES &&
                    repo.HasComponentByTypeId(rootEntity, (int)componentTypeId))
                {
                    return repo.HasAuthority(rootEntity, (int)componentTypeId);
                }
            }

            // 3. Fallback to Primary Entity Authority
            return netAuth.HasAuthority;
        }
    }
}
