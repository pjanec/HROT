using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
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
                if (AirCapable(world, unit, m, minElevationDeg) && HasRounds(world, unit, i)) return true;   // ⭐ CE-4617 (RV-11) — an empty launcher does not count
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, unit, controlled);
            for (int k = 0; k < n; k++)
            {
                if (WeaponSystems.IsBroken(world, controlled[k])) continue;
                var m = CombatTkb.MountOf(world, controlled[k], 0);
                if (m != null && AirCapable(world, controlled[k], m, minElevationDeg) && HasRounds(world, controlled[k], 0)) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-4617</c> (B-3, RV-11; docs/designs/air-attack/DESIGN_Radar_SAM.md) — does mount <paramref name="mountIndex"/> of <paramref name="owner"/>
        /// have a round left: its <see cref="WeaponState.Ammo"/> (mount 0 on the owner, another on its mount child). A unit whose world keeps no
        /// weapon state for it answers true (nothing can say it is empty — the weapon brain decides, as before).
        /// </summary>
        public static bool HasRounds(EntityRepository world, Entity owner, int mountIndex)
        {
            if (!world.IsComponentTypeRegistered<WeaponState>()) return true;
            if (mountIndex == 0) return !world.HasComponent<WeaponState>(owner) || world.GetComponentRO<WeaponState>(owner).Ammo != 0;
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return true;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == mountIndex)
                    return !world.HasComponent<WeaponState>(mounts[i]) || world.GetComponentRO<WeaponState>(mounts[i]).Ammo != 0;
            return true;
        }

        /// <summary>⭐ <c>CE-2436</c> AU-7 (A-10b) — inside this range (m) EVERY man fires at a drone, a rifle too (the last moments before it
        /// strikes); beyond it a rifle holds its fire (the drone gunners engage, the rest take cover).</summary>
        public const float EveryoneFiresMetres = 20f;

        /// <summary>
        /// ⭐⭐ <c>CE-2436</c> AU-7 (A-10b; R-2110) — may <paramref name="unit"/> fire at a DRONE <paramref name="range"/> metres away? Beside
        /// <see cref="CanEngageAir"/> (aircraft: only a real air-defence weapon): a shotgun within its range, a machine gun or an autocannon
        /// (within its range when declared), anything <see cref="CanEngageAir"/> counts, and inside <see cref="EveryoneFiresMetres"/> any
        /// small arm. Its own mounts and the weapons it controls. Whether a round HITS stays the hit model's (<c>HitModel.ShotHitChance</c>).
        /// </summary>
        public static bool CanEngageDrone(EntityRepository world, Entity unit, float range)
        {
            if (!world.IsAlive(unit)) return false;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                if (DroneCapable(world, unit, m, range)) return true;
            }
            Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, unit, controlled);
            for (int k = 0; k < n; k++)
            {
                if (WeaponSystems.IsBroken(world, controlled[k])) continue;
                var m = CombatTkb.MountOf(world, controlled[k], 0);
                if (m != null && DroneCapable(world, controlled[k], m, range)) return true;
            }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-2436</c> AU-7 (A-10c) — is <paramref name="unit"/> one of the squad's DRONE GUNNERS (he engages a drone where the others take
        /// cover): he carries a shotgun (the anti-drone man) or a machine gun (the MG). His own mounts only.
        /// </summary>
        public static bool IsDroneGunner(EntityRepository world, Entity unit)
        {
            if (!world.IsAlive(unit)) return false;
            for (int i = 0; i < MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, unit, i);
                if (m == null) break;
                if (m.Pellets > 1 || m.Category == WeaponCategory.MachineGun) return true;
            }
            return false;
        }

        private static bool DroneCapable(EntityRepository world, Entity owner, WeaponMountDto m, float range)
        {
            if (m.Hands || m.Category is WeaponCategory.Countermeasure or WeaponCategory.Bomb or WeaponCategory.Grenade or WeaponCategory.Mortar
                or WeaponCategory.Howitzer or WeaponCategory.Atgm or WeaponCategory.TankGun) return false;
            bool inRange = m.Range <= 0f || range <= m.Range;
            if (m.Pellets > 1) return inRange;
            if (m.Category is WeaponCategory.MachineGun or WeaponCategory.Autocannon) return inRange;
            if (AirCapable(world, owner, m, 80f)) return true;
            return m.Category == WeaponCategory.SmallArms && range <= EveryoneFiresMetres;
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
