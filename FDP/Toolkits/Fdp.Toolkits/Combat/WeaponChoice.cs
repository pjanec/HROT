using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3089</c> (G7) — which of a unit's weapons to fire at a target: the starter <see cref="WeaponSelectionDecision"/>
    /// ranks the unit's MOUNTS (the owner = mount 0, then the mount children) against the target — effectiveness vs its armour
    /// (CE-3071's one model), range band, ammunition, readiness. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §12 W3.
    /// <para>Called per shot by <see cref="Executors.AimAndFireExecutor"/> when its params say <see cref="Executors.AimAndFireParams.MountAuto"/>.</para>
    /// </summary>
    public static class WeaponChoice
    {
        [System.ThreadStatic] private static UtilityScorer? _scorer;

        /// <summary>⭐ <c>CE-2451</c> D-9 — an ANTI-ARMOUR mount: an ATGM, or a round whose warhead is a shaped charge (the RPG-7).</summary>
        public static bool IsAntiArmour(EntityRepository repo, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount)
            => mount != null && (mount.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm
                                 || CombatTkb.WarheadOf(repo, mount)?.Kind == Fdp.Toolkit.Tkb.Domain.WarheadKind.ShapedCharge);

        /// <summary>⭐ <c>CE-2451</c> D-9 — an UNARMOURED target: a person or an animal (the one locomotion class rule); a vehicle — soft or not —
        /// is the anti-armour round's business (Aggregate Squad R-3: "the RPG goes to vehicles").</summary>
        public static bool IsUnarmoured(EntityRepository repo, Entity target)
            => Fdp.Toolkit.Motion.Locomotions.Of(repo, target) is Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Quadruped;

        /// <summary>
        /// ⭐⭐ <c>CE-2451</c> D-9 (user-approved `2026-11-19`; makes Aggregate Squad R-3 TRUE: <i>"WeaponChoice scores it 0 against men"</i>) —
        /// <paramref name="mount"/> is held back from <paramref name="target"/>: it is anti-armour, the target is unarmoured, and
        /// <paramref name="owner"/> has ANOTHER loaded mount that can hurt him (not anti-armour, not the bare hands, a lethal round). His only
        /// loaded mount is never held back — he does not stand idle. 📐 Before: an insurgent's RPG-7 won a near tie against a man 13 m off
        /// (`ts-house-defenders`: one rocket into the stacked squad).
        /// </summary>
        public static bool HeldForArmour(EntityRepository repo, Entity owner, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount, Entity target)
        {
            if (!IsAntiArmour(repo, mount) || target.IsNull || !repo.IsAlive(target) || !IsUnarmoured(repo, target)) return false;
            if (CanHurtAMan(repo, owner, owner, 0, mount)) return true;
            if (!repo.IsComponentTypeRegistered<WeaponMountInfo>()) return false;
            System.Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(repo, owner, mounts);
            for (int i = 0; i < n; i++)
                if (repo.HasComponent<WeaponMountInfo>(mounts[i])
                    && CanHurtAMan(repo, owner, mounts[i], repo.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex, mount)) return true;
            return false;
        }

        private static bool CanHurtAMan(EntityRepository repo, Entity owner, Entity weapon, int index, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? held)
        {
            if (!repo.HasComponent<WeaponState>(weapon) || repo.GetComponentRO<WeaponState>(weapon).Ammo <= 0) return false;
            var dto = CombatTkb.MountOf(repo, owner, index);
            if (dto == null || ReferenceEquals(dto, held) || IsAntiArmour(repo, dto) || PersonHands.IsHands(dto)) return false;
            var warhead = CombatTkb.WarheadOf(repo, dto);
            return warhead == null
                ? dto.DamagePerHit > 0f || dto.AmmoGuid == 0
                : warhead.Kind is Fdp.Toolkit.Tkb.Domain.WarheadKind.Kinetic or Fdp.Toolkit.Tkb.Domain.WarheadKind.HighExplosive
                                  or Fdp.Toolkit.Tkb.Domain.WarheadKind.Fragmentation;
        }

        /// <summary>
        /// The mount index to fire at <paramref name="target"/>, and the entity holding that mount's <see cref="WeaponState"/>
        /// (<paramref name="owner"/> for mount 0, a mount child otherwise). Falls back to mount 0 on the owner when the decision
        /// is not registered, the unit has no ranked mount, or every mount scores 0 (out of range / no ammo everywhere).
        /// </summary>
        public static int Choose(EntityRepository repo, Entity owner, Entity target, out Entity mount)
        {
            int primary = WeaponSystems.PrimaryMount(repo, owner, out mount);   // ⭐ WS-3 — the fallback: a bare host's station weapon
            var scorer = UtilityDecisionCatalog.ScorerOverShared(ref _scorer);
            if (!scorer.TopCandidate(repo, owner, WeaponSelectionDecision.Id, target, 0, out var top, out float score)
                || score <= 0f || top.IsNull || !repo.IsAlive(top) || top.Equals(owner))
                return primary;
            // ⭐ WS-3 (W-D) — a weapon system the unit controls: its place in the unit's mount list (it fires as itself, mount 0)
            //   ⭐ CE-3203 — a BROKEN one (health 0) is never chosen: the primary (the next working one) fires instead
            if (WeaponSystems.IsWeaponSystem(repo, top) && WeaponSystems.IsBroken(repo, top)) return primary;
            if (WeaponSystems.IsWeaponSystem(repo, top) && repo.HasComponent<WeaponState>(top)
                && WeaponSystems.IndexOf(repo, owner, top) is var controlled and >= 0)
            {
                mount = top;
                return controlled;
            }
            if (!repo.IsComponentTypeRegistered<WeaponMountInfo>() || !repo.HasComponent<WeaponMountInfo>(top)
                || !repo.HasComponent<WeaponState>(top))
                return primary;
            mount = top;
            return repo.GetComponentRO<WeaponMountInfo>(top).MountIndex;
        }
    }
}
