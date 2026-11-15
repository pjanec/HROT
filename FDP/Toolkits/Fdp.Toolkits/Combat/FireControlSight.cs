using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-3, RS-4 / RV-1; docs/designs/air-attack/DESIGN_Radar_SAM.md) — THE one rule for "is the target seen by THIS MOUNT":
    /// seen by the eyes now (<see cref="Perception.SightNow"/>), OR held by the unit's RADAR now when the round the mount fires is radar-guided
    /// (its <see cref="MissileDto.Guidance"/> is <see cref="GuidanceKind.Radar"/>). Asked by every site that decides whether a mount may fire:
    /// the weapon brain's per-shot and turret paths (<c>AimAndFireExecutor</c>) and the air-defence fire node (<c>AirSense.FireAt</c>).
    /// ⛔ <see cref="Perception.SightNow"/> itself is untouched (~30 readers): an MG, a cannon, a MANPADS never fire at a radar-only contact.
    /// </summary>
    public static class FireControlSight
    {
        /// <summary>Does <paramref name="self"/> see <paramref name="target"/> for <paramref name="mount"/> firing <paramref name="loadedAmmo"/>
        /// (0 = the mount's own round)?</summary>
        public static bool Sees(EntityRepository world, Entity self, Entity target, WeaponMountDto? mount, long loadedAmmo = 0)
            => Perception.SightNow.Sees(world, self, target)
               || (RadarGuided(world, mount, loadedAmmo) && Perception.SightNow.Holds(world, self, target, SensorModality.Radar));

        /// <summary>
        /// ⭐ <c>CE-4617</c> (RS-4) — the same rule before the weapon brain has picked a mount (<c>AirSense.FireAt</c>): seen by the eyes, or held
        /// by the radar when ANY of the unit's own mounts fires a radar-guided round. The executor still asks <see cref="Sees"/> per mount, so a
        /// machine gun beside the launcher never fires at the radar-only contact.
        /// </summary>
        public static bool SeesForAnyMount(EntityRepository world, Entity self, Entity target)
        {
            if (Perception.SightNow.Sees(world, self, target)) return true;
            if (!Perception.SightNow.Holds(world, self, target, SensorModality.Radar)) return false;
            for (int i = 0; i < AirDefenceWeapons.MaxMounts; i++)
            {
                var m = CombatTkb.MountOf(world, self, i);
                if (m == null) break;
                if (RadarGuided(world, m)) return true;
            }
            return false;
        }

        /// <summary>Is the round <paramref name="mount"/> fires (<paramref name="loadedAmmo"/>, else its own) a radar-guided missile?</summary>
        public static bool RadarGuided(EntityRepository world, WeaponMountDto? mount, long loadedAmmo = 0)
            => mount != null
               && CombatTkb.MissileOf(world, loadedAmmo != 0 ? loadedAmmo : unchecked((long)mount.AmmoGuid)) is { Guidance: GuidanceKind.Radar };
    }
}
