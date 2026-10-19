using System;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2191</c> (moved here by <c>CE-2309</c>, Command Chain §8d HQ-2) — can a unit fire at an aircraft? One rule, read by the SOPs'
    /// air-defence row (<c>AirDefenceNodes.CanEngageAir</c> forwards here) and by the staff's SPOT, which flags an element <c>AirDefence</c>
    /// when one of its identified contacts can.
    /// </summary>
    public static class AirDefenceWeapons
    {
        /// <summary>The most built-in mounts looked at.</summary>
        public const int MaxMounts = 16;

        /// <summary>
        /// True when <paramref name="unit"/> carries or controls (<see cref="WeaponSystems.Controlled"/>) a weapon able to engage aircraft: a
        /// <see cref="WeaponCategory.Sam"/> launcher whose missile may be fired at the Air domain (Air or Either), or a gun whose turret
        /// elevates to <paramref name="minElevationDeg"/>. A dispenser, a rifle, an ATGM, an M2 on its 70° pintle do not count.
        /// </summary>
        public static bool CanEngageAir(EntityRepository world, Entity unit, float minElevationDeg)
        {
            if (!world.IsAlive(unit)) return false;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                if (AirCapable(world, unit, m, minElevationDeg)) return true;
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, unit, controlled);
            for (int k = 0; k < n; k++)
            {
                if (WeaponSystems.IsBroken(world, controlled[k])) continue;
                var m = CombatTkb.MountOf(world, controlled[k], 0);
                if (m != null && AirCapable(world, controlled[k], m, minElevationDeg)) return true;
            }
            return false;
        }

        private static bool AirCapable(EntityRepository world, Entity owner, WeaponMountDto m, float minElevationDeg)
        {
            if (m.Category == WeaponCategory.Countermeasure) return false;
            if (m.Category == WeaponCategory.Sam)
                return CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid)) is not { } missile || missile.TargetDomain != TargetDomain.Ground;
            if (m.Category is not (WeaponCategory.MachineGun or WeaponCategory.Autocannon)) return false;
            return m.TurretIndex >= 0 && CombatTkb.TurretOf(world, owner, m.TurretIndex) is { } turret && turret.ElevationMaxDeg >= minElevationDeg;
        }
    }
}
