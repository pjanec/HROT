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

            // Candidates 1+: children whose PartMetadata.ParentEntity == owner, in MountIndex order.
            // ⭐ CE-2410 — read from a per-world owner → mounts index built ONCE per frame (it scanned EVERY mount part of the world per
            //   call: 📐 44 % of the CGF node's thread on the CE-2408 stress world, CalloutSystem asking each of ~1000 men every frame).
            var index = MountIndex.Of(repo);
            lock (index)
            {
                index.EnsureFresh(repo);
                if ((uint)owner.Index < (uint)index.Start.Length)   // the slot's slice; each item's parent is checked below (a reused slot may hold a dead owner's parts)
                {
                    int from = index.Start[owner.Index], to = from + index.Count[owner.Index];
                    for (int k = from; k < to && count < dest.Length; k++)
                    {
                        var e = index.Items[k];
                        // ⭐ re-checked on every read: a mount removed or destroyed after the build never shows
                        if (!repo.IsAlive(e) || !repo.HasComponent<WeaponMountInfo>(e) || !repo.HasComponent<PartMetadata>(e)) continue;
                        if (!repo.GetComponentRO<PartMetadata>(e).ParentEntity.Equals(owner)) continue;
                        dest[count++] = e;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// ⭐ CE-2410 — owner (by entity index) → its mount parts, sorted by <see cref="WeaponMountInfo.MountIndex"/> (at most 16 each, as the
        /// scan kept). Rebuilt when the frame moved (<see cref="EntityRepository.GlobalVersion"/>) or when a mount or part table was written in
        /// THIS frame (an add, a re-parent, a new mount index — those frames rebuild per call, today's cost, never more). A removal or a destroy
        /// is caught on read. One pass over the mount parts per frame instead of one per call. No allocation once grown.
        /// </summary>
        private sealed class MountIndex
        {
            private static readonly ConditionalWeakTable<EntityRepository, MountIndex> s_of = new();
            public static MountIndex Of(EntityRepository repo) => s_of.GetValue(repo, static _ => new MountIndex());

            public int[] Start = Array.Empty<int>(), Count = Array.Empty<int>();
            public Entity[] Items = Array.Empty<Entity>();
            private (int Owner, int Mount, Entity E)[] _scratch = Array.Empty<(int, int, Entity)>();
            private int[] _mountOf = Array.Empty<int>();
            private uint _builtAt;
            private bool _built;
            private EntityQuery? _query;

            public void EnsureFresh(EntityRepository repo)
            {
                uint now = repo.GlobalVersion;
                if (_built && now == _builtAt && !WrittenSince(repo, now)) return;
                Build(repo);
                _builtAt = now;
                _built = true;
            }

            // a chunk of either table stamped at (or after) this frame's version ⇒ something was added / re-parented / re-indexed this frame
            private static bool WrittenSince(EntityRepository repo, uint now)
                => (repo.TryGetTable(typeof(WeaponMountInfo), out var a) && a.HasChanges(now == 0 ? 0 : now - 1))
                || (repo.TryGetTable(typeof(PartMetadata), out var b) && b.HasChanges(now == 0 ? 0 : now - 1));

            private void Build(EntityRepository repo)
            {
                int owners = repo.GetEntityIndex().MaxIssuedIndex + 1;
                if (Start.Length < owners) { Array.Resize(ref Start, owners); Array.Resize(ref Count, owners); }
                Array.Clear(Count, 0, Count.Length);
                int n = 0;
                if (repo.IsComponentTypeRegistered<WeaponMountInfo>() && repo.IsComponentTypeRegistered<PartMetadata>())
                {
                    _query ??= repo.Query().With<WeaponMountInfo>().With<PartMetadata>().Build();
                    foreach (var e in _query)
                    {
                        var parent = repo.GetComponentRO<PartMetadata>(e).ParentEntity;
                        if (parent.IsNull || (uint)parent.Index >= (uint)owners) continue;
                        if (n == _scratch.Length) Array.Resize(ref _scratch, Math.Max(64, n * 2));
                        _scratch[n++] = (parent.Index, repo.GetComponentRO<WeaponMountInfo>(e).MountIndex, e);
                        Count[parent.Index]++;
                    }
                }
                // owner slices (counting sort by owner index), then each slice by mount index
                int at = 0;
                for (int o = 0; o < owners; o++) { Start[o] = at; at += Count[o]; Count[o] = 0; }
                if (Items.Length < n) Array.Resize(ref Items, Math.Max(n, Items.Length * 2));
                if (_mountOf.Length < n) Array.Resize(ref _mountOf, Math.Max(n, _mountOf.Length * 2));
                var mountOf = _mountOf;
                for (int k = 0; k < n; k++)
                {
                    var (o, mount, e) = _scratch[k];
                    int slot = Start[o] + Count[o]++;
                    int j = slot;   // insertion into the owner's slice by mount index (a handful each)
                    while (j > Start[o] && mountOf[j - 1] > mount) { Items[j] = Items[j - 1]; mountOf[j] = mountOf[j - 1]; j--; }
                    Items[j] = e; mountOf[j] = mount;
                }
                for (int o = 0; o < owners; o++) if (Count[o] > 16) Count[o] = 16;   // the scan kept at most 16 per owner
            }
        }

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
