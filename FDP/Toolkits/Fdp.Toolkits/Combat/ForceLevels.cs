using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2239</c> part 1 (CV-8, AW-18; docs/DESIGN_Civilians_And_Crowd_Control.md §5j FL-2…FL-4) — the FORCE LEVEL a unit's ROE caps
    /// its weapons at, asked at the two places a round can leave (<c>AimAndFireExecutor.HoldOf</c>, <c>FireAtPointExecutor.ShootAtPoint</c>)
    /// and at the one place a weapon is chosen (the WeaponSelection candidates) — so no behaviour can forget it. Warn ⇒ no round at all;
    /// NonLethal ⇒ no lethal round; Lethal ⇒ anything. ⭐ Self-defence (AW-18) lifts the cap against THAT target only.
    /// </summary>
    public static class ForceLevels
    {
        /// <summary>May <paramref name="unit"/> fire a round of munition <paramref name="ammo"/> at <paramref name="target"/>
        /// (<see cref="Entity.Null"/> for a point)? <paramref name="ammo"/> 0 = no round known ⇒ judged lethal under a NonLethal cap
        /// only when <paramref name="mount"/> fires lethal.</summary>
        public static bool Permits(EntityRepository world, Entity unit, Entity target, long ammo, WeaponMountDto? mount = null)
        {
            if (!world.IsComponentTypeRegistered<Roe>()) return true;
            var level = RoeOf.ForceLevel(world, unit);
            if (level == RoeForceLevel.Lethal || SelfDefence(world, unit, target)) return true;
            if (level == RoeForceLevel.Warn) return false;
            return !Lethal(world, ammo, mount);
        }

        /// <summary>FL-4 — may <paramref name="unit"/> CHOOSE <paramref name="mount"/> against <paramref name="target"/>? (the weapon choice
        /// drops a mount above the cap, so a unit with a non-lethal weapon uses it rather than hold).</summary>
        public static bool AllowsMount(EntityRepository world, Entity unit, Entity target, WeaponMountDto? mount)
        {
            if (!world.IsComponentTypeRegistered<Roe>()) return true;
            var level = RoeOf.ForceLevel(world, unit);
            if (level == RoeForceLevel.Lethal || SelfDefence(world, unit, target)) return true;
            if (level == RoeForceLevel.Warn) return false;
            return mount == null || !ItemAppearances.FiresLethal(world, mount);
        }

        /// <summary>FL-3 (AW-18) — is firing on <paramref name="target"/> SELF-DEFENCE: he aims a lethal weapon at <paramref name="unit"/>
        /// (his replicated <see cref="WeaponPosture"/>), or he committed a LETHAL hostile act (B-16)? A point has no target ⇒ no.</summary>
        public static bool SelfDefence(EntityRepository world, Entity unit, Entity target)
        {
            if (target.IsNull || !world.IsAlive(target)) return false;
            if (Fdp.Toolkit.Civilians.HostileActs.Of(world, target) == Fdp.Toolkit.Civilians.HostileActKind.Lethal) return true;
            if (!world.IsComponentTypeRegistered<WeaponPosture>() || !world.HasComponent<WeaponPosture>(target)) return false;
            ref readonly var posture = ref world.GetComponentRO<WeaponPosture>(target);
            return posture.Kind == WeaponPostureKind.Aimed && posture.Target == unit && Armament.LethalInHand(world, target);
        }

        private static bool Lethal(EntityRepository world, long ammo, WeaponMountDto? mount)
            => ammo != 0 ? ItemAppearances.IsLethal(world, ammo) : mount == null || ItemAppearances.FiresLethal(world, mount);
    }
}
