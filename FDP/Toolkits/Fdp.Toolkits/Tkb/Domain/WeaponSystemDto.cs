using System;
using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-1 (R-2016, docs/designs/weapons/DESIGN_Weapon_Systems.md §3, §5) — the TKB descriptor <c>Gen.WeaponSystem</c>:
    /// a weapon that is an ENTITY of its own (an HMG, a minigun, a missile launcher, a sentry gun) — placed on the ground, mounted in a host's
    /// <see cref="SeatRole.Station"/> seat, carried by a soldier, deployed or stowed, manned. Its gun is the type's ordinary
    /// <c>Combat.WeaponSuite</c> mount 0, its pintle the ordinary <c>Combat.Turrets</c>; this descriptor says only how it is HANDLED.
    /// 🔒 User: <i>"Automatic turret is required. This is no different from a tank or something, Entity having a weapon."</i>
    /// </summary>
    [TkbDescriptor("Gen.WeaponSystem")]
    public record WeaponSystemDto
    {
        /// <summary>True: it fires only when someone operates it (a gunner, its host). False: an AUTOMATIC weapon — unoperated, it fights
        /// on its own with its own sensors and SOP (W-E).</summary>
        public bool NeedsOperator { get; init; } = true;

        /// <summary>Who can carry it: nobody (<see cref="WeaponPortability.Static"/>), one man, or two (W-G).</summary>
        public WeaponPortability Portable { get; init; }

        /// <summary>Top-speed factor of a soldier carrying it.</summary>
        public float CarrySpeedFactor { get; init; } = 0.7f;

        /// <summary>It fires while carried (a LAW, a Javelin on the shoulder); otherwise only deployed.</summary>
        public bool FireWhileCarried { get; init; }

        /// <summary>How long setting it up takes (Stowed → Deployed), seconds.</summary>
        public float DeploySeconds { get; init; } = 5f;

        /// <summary>How long packing it up takes (Deployed → Stowed), seconds.</summary>
        public float PackSeconds { get; init; } = 5f;

        /// <summary>Where it can be mounted: on the ground (a tripod), a pintle, a helicopter door, on a soldier.</summary>
        public MountKinds MountKinds { get; init; } = MountKinds.Ground;
    }

    /// <summary>⭐ WS-1 — who can carry a weapon system.</summary>
    public enum WeaponPortability : byte
    {
        /// <summary>Nobody carries it (a sentry gun, a turret). ⚠ Not "None" (the IDL generator puts every enum member of a descriptor's
        /// file in one scope, and <see cref="MountKinds.None"/> is there too) and not "Fixed" (an IDL keyword).</summary>
        Static = 0,
        OneMan = 1,
        TwoMan = 2,
    }

    /// <summary>⭐ WS-1 — where a weapon system can be mounted (its <see cref="WeaponSystemDto.MountKinds"/>) and what a station accepts
    /// (<see cref="SeatDto.Accepts"/>): it boards a station only when the two share a kind.</summary>
    [Flags]
    public enum MountKinds : byte
    {
        None = 0,
        /// <summary>On the ground: a tripod, a base plate.</summary>
        Ground = 1,
        /// <summary>A pintle on a vehicle (a pickup's bed, a turret ring).</summary>
        Pintle = 2,
        /// <summary>A helicopter's door mount.</summary>
        Door = 4,
        /// <summary>On a soldier — his carry station.</summary>
        Carried = 8,
    }
}
