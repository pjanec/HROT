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

            // 2. Specific Descriptor Ownership Override (Granular Authority)
            // Fix: HasManagedComponent now handles BitMask overflow internally (via Fallback).
            // ⭐ S6 (Q79 §0.10, R-168): an instance key (d,i) with no entry of its own falls back to the descriptor
            //   type's entry (d,0) — the group's record covers every instance unless one was moved on its own — and only
            //   then to the primary owner. 📄 docs/DESIGN_Ownership_Groups_And_Grants.md §5.6 S6.
            if (packedKey != 0 && view.HasManagedComponent<DescriptorOwnership>(rootEntity))
            {
                var ownership = view.GetManagedComponentRO<DescriptorOwnership>(rootEntity);
                if (ownership.TryGetOwner(packedKey, out int specificOwner))
                {
                    return specificOwner == netAuth.LocalNodeId;
                }

                var (typeId, instanceId) = OwnershipExtensions.UnpackKey(packedKey);
                if (instanceId != 0 && typeId != 0 &&
                    ownership.TryGetOwner(OwnershipExtensions.PackKey(typeId, 0), out int typeOwner))
                {
                    return typeOwner == netAuth.LocalNodeId;
                }
            }

            // 3. Fallback to Primary Entity Authority
            return netAuth.HasAuthority;
        }

        /// <summary>
        /// ⭐ The INGRESS form of <see cref="HasAuthority(ISimulationView, Entity, long)"/>: true only when this node's
        /// ownership of the entity is RECORDED (a <see cref="NetworkAuthority"/> exists, resolved through
        /// <see cref="PartMetadata"/> like the gate) and says this node owns <paramref name="packedKey"/>.
        /// <para>⚠ <see cref="HasAuthority(ISimulationView, Entity, long)"/> treats an entity with no
        /// <see cref="NetworkAuthority"/> as locally owned, which is right for a node with no network. For a translator
        /// applying a received sample it is wrong: an entity without a record there is a replica still being built
        /// from the wire (a ghost created by this very sample), and treating it as owned drops the sample. 📌 Measured
        /// on <c>EntityDamageIngressTranslator</c> (the first health of an unknown entity was dropped) and
        /// <c>EntityInfoIngressTranslator</c> (a ghost's commander assignment was dropped).</para>
        /// </summary>
        public static bool IsRecordedOwner(this ISimulationView view, Entity entity, long packedKey = 0)
        {
            if (!view.IsAlive(entity)) return false;
            Entity root = view.HasComponent<PartMetadata>(entity) ? view.GetComponentRO<PartMetadata>(entity).ParentEntity : entity;
            if (!view.IsAlive(root) || !view.HasComponent<NetworkAuthority>(root)) return false;
            return HasAuthority(view, entity, packedKey);
        }
    }
}
