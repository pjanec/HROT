using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// Zero-alloc query helper for multi-mount weapon entities.
    /// Collects all weapon mount entities for an owner into a caller-supplied span.
    /// </summary>
    public static class WeaponMountQuery
    {
        /// <summary>
        /// Collects all weapon mount entities for <paramref name="owner"/> into <paramref name="dest"/>.
        /// Index 0 is always the owner entity itself (primary mount) if it carries WeaponState.
        /// Subsequent slots are child entities carrying WeaponMountInfo in MountIndex order.
        /// Returns the count written (min(dest.Length, total mounts)).
        /// Zero-alloc: iterates PartMetadata-bearing entities using the QueryBuilder API.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EnumerateMounts(EntityRepository repo, Entity owner, Span<Entity> dest)
        {
            if (dest.IsEmpty) return 0;
            int count = 0;

            // Candidate 0: the owner itself (primary mount).
            if (repo.HasComponent<WeaponState>(owner))
            {
                dest[count++] = owner;
                if (count >= dest.Length) return count;
            }

            // Candidates 1+: children whose PartMetadata.ParentEntity == owner.
            // Use a fixed-size stack buffer to collect then sort by MountIndex.
            Span<(int idx, Entity e)> scratch = stackalloc (int, Entity)[16];
            int scratchCount = 0;

            var query = repo.Query()
                .With<WeaponMountInfo>()
                .With<PartMetadata>()
                .Build();

            foreach (var e in query)
            {
                ref readonly var pm = ref repo.GetComponentRO<PartMetadata>(e);
                if (!pm.ParentEntity.Equals(owner)) continue;
                ref readonly var mi = ref repo.GetComponentRO<WeaponMountInfo>(e);
                if (scratchCount < scratch.Length)
                    scratch[scratchCount++] = (mi.MountIndex, e);
            }

            // Sort by MountIndex (insertion sort — typically ≤4 mounts).
            for (int i = 1; i < scratchCount; i++)
            {
                var key = scratch[i];
                int j = i - 1;
                while (j >= 0 && scratch[j].idx > key.idx) { scratch[j + 1] = scratch[j]; j--; }
                scratch[j + 1] = key;
            }

            for (int i = 0; i < scratchCount && count < dest.Length; i++)
                dest[count++] = scratch[i].e;

            return count;
        }
    }
}
