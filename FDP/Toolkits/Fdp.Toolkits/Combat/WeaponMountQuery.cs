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
        /// Zero-alloc: reads the shared parts index (CE-2412).
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

            // Candidates 1+: children whose PartMetadata.ParentEntity == owner, in MountIndex order (at most 16, as the scan kept).
            // ⭐ CE-2410 / CE-2412 — read from the shared parts index (it scanned EVERY mount part of the world per call: 📐 44 % of the CGF
            //   node's thread on the CE-2408 stress world); the mount order is taken live, so a re-index within the frame is seen.
            Span<Entity> parts = stackalloc Entity[64];
            int n = Fdp.Toolkit.Replication.Utilities.PartIndex.PartsOf(repo, owner, parts);
            if (n > parts.Length) { parts = new Entity[n]; n = Fdp.Toolkit.Replication.Utilities.PartIndex.PartsOf(repo, owner, parts); }
            Span<Entity> mounts = stackalloc Entity[MaxMountChildren];
            Span<int> order = stackalloc int[MaxMountChildren];
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                var e = parts[k];
                if (!repo.HasComponent<WeaponMountInfo>(e)) continue;
                int mount = repo.GetComponentRO<WeaponMountInfo>(e).MountIndex, j = m;
                if (m < MaxMountChildren) m++;
                else if (!Before(mount, e, order[m - 1], mounts[m - 1])) continue;
                else j = m - 1;
                while (j > 0 && Before(mount, e, order[j - 1], mounts[j - 1])) { mounts[j] = mounts[j - 1]; order[j] = order[j - 1]; j--; }
                mounts[j] = e; order[j] = mount;
            }
            for (int k = 0; k < m && count < dest.Length; k++) dest[count++] = mounts[k];

            return count;
        }

        /// <summary>Mount order: by mount index, ties in entity order (the scan met mounts in entity order and sorted stably).</summary>
        private static bool Before(int mount, Entity e, int otherMount, Entity other)
            => mount < otherMount || (mount == otherMount && e.Index < other.Index);

        /// <summary>The scan kept at most this many mount children per owner (lowest mount index first).</summary>
        private const int MaxMountChildren = 16;

        /// <summary>
        /// ⭐⭐ Weapon systems WS-3 (W-D, R-2016; DESIGN_Weapon_Systems §3) — the mounts the WEAPON BRAIN fights with: the unit's own
        /// (<see cref="EnumerateMounts"/>) followed by the weapon systems it controls (<see cref="WeaponSystems.Controlled"/> — its station
        /// weapons, the one it carries, the one it mans). A controlled weapon's mount index is
        /// <see cref="WeaponSystems.ControlledMountBase"/> + its place in that list; it fires as itself, on its own mount 0.
        /// <para>⚠ A sibling of <see cref="EnumerateMounts"/>, not a widening of it: that one also counts a type's own mount children
        /// (<c>CombatTkbTranslator</c> creates and prunes them by it), which a boarded weapon must never join.</para>
        /// </summary>
        public static int EnumerateFiringMounts(EntityRepository repo, Entity owner, Span<Entity> dest)
        {
            int n = EnumerateMounts(repo, owner, dest);
            return n >= dest.Length ? n : n + WeaponSystems.Controlled(repo, owner, dest.Slice(n));
        }

        /// <summary>
        /// The entity holding mount <paramref name="index"/>'s <see cref="WeaponState"/>: the owner for mount 0, the mount child
        /// otherwise; the owner when no such child exists. ⭐ CE-1079 — public so the operator behaviour finds the mount whose round
        /// it cycles (it was <c>FireAtPointExecutor</c>'s private helper; that one now calls this).
        /// </summary>
        public static Entity MountEntity(EntityRepository world, Entity owner, int index)
        {
            if (index == 0 || !world.IsComponentTypeRegistered<WeaponMountInfo>()) return owner;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index)
                    return mounts[i];
            return owner;
        }
    }
}
