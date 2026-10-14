using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ WL-1 (docs/DESIGN_Weapon_Layers.md W-9, M25) — a platform's TURRETS. Each becomes a part child entity on every node
    /// (<c>TurretTkbTranslator</c>) carrying a <c>TurretPose</c>; weapon mounts name the turret they sit on
    /// (<see cref="WeaponMountDto.TurretIndex"/>), so two mounts can share one turret (the Bradley's 25 mm and TOW).
    /// </summary>
    [TkbDescriptor("Combat.Turrets")]
    public record TurretSuiteDto
    {
        /// <summary>The turrets, in index order (index 0 = the main turret).</summary>
        public List<TurretDto> Turrets { get; init; } = new();
    }

    /// <summary>One turret: where it turns, how far, how fast. Angles in degrees; azimuth counter-clockwise from the hull's forward,
    /// elevation up from the turret plane (M23).</summary>
    public record TurretDto
    {
        /// <summary>The traverse pivot relative to the hull's reference point (m; X forward, Y left, Z up).</summary>
        public float PivotX { get; init; }
        public float PivotY { get; init; }
        public float PivotZ { get; init; }

        /// <summary>⭐ E4 (DESIGN_Visual_Effects §6b) — the gun's trunnion (its elevation axis) relative to the pivot: forward along
        /// the turret's facing and up (m). The muzzle offset of a mount on this turret is measured from here.</summary>
        public float TrunnionX { get; init; }
        public float TrunnionZ { get; init; }

        /// <summary>Traverse limits; −180 … 180 = all round.</summary>
        public float TraverseMinDeg { get; init; } = -180f;
        public float TraverseMaxDeg { get; init; } = 180f;

        /// <summary>Gun elevation limits (depression negative).</summary>
        public float ElevationMinDeg { get; init; } = -8f;
        public float ElevationMaxDeg { get; init; } = 20f;

        /// <summary>Slew rates (degrees per second).</summary>
        public float TraverseRateDegPerSec { get; init; } = 40f;
        public float ElevationRateDegPerSec { get; init; } = 10f;
    }

    /// <summary>
    /// ⭐ WL (docs/DESIGN_Weapon_Layers.md W-5) — a weapon's CATEGORY: it selects the hardcoded weapon-brain profile that drives the
    /// weapon. <see cref="Unknown"/> takes the default profile (today's behaviour).
    /// </summary>
    public enum WeaponCategory : byte
    {
        Unknown = 0,
        SmallArms = 1,
        MachineGun = 2,
        Autocannon = 3,
        TankGun = 4,
        Atgm = 5,
        Mortar = 6,
        Grenade = 7,
        /// <summary>⭐ Air Ops slice 4 (DESIGN_Air_Operations §8) — a free-fall bomb: released from an aircraft at the weapon brain's
        /// cue (the Release profile), it leaves with the carrier's velocity and falls.</summary>
        Bomb = 8,
    }
}
