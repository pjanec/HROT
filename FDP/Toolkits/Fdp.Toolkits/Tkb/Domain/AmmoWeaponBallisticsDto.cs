using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Ballistic profile for an ammunition round against a specific weapon system.
    /// Supports multi-instance via TkbTemplate PartId when the same ammo type
    /// has distinct profiles for different weapons.
    /// </summary>
    [TkbDescriptor("Gen.AmmoWeaponBallistics")]
    public record AmmoWeaponBallisticsDto
    {
        /// <summary>TKB GUID of the weapon this ballistic profile applies to. 0 = Generic.</summary>
        [WeaponRef]
        [Description("Weapon TKB GUID this ballistic profile applies to. 0 = Generic.")]
        public long WeaponGuid { get; init; }

        /// <summary>Projectile velocity at muzzle exit.</summary>
        [EditUnit("m/s")]
        public float MuzzleSpeed { get; init; }

        /// <summary>Base damage applied on hit.</summary>
        [Description("Base damage applied on hit.")]
        public float Damage { get; init; }

        /// <summary>
        /// ⭐ Buildings §3d P1 (R-217) — armour penetration of this round FIRED FROM this weapon, mm RHA (the same round from a
        /// longer barrel penetrates more). 0 = not stated ⇒ the next step of the lookup applies (the generic profile, then the
        /// mount's own <c>WeaponMountDto.Penetration</c>). v1 is one constant per pair; a penetration-vs-range table is later (P1c).
        /// </summary>
        [EditUnit("mm RHA")]
        [Description("Armour penetration of this round from this weapon, mm RHA. 0 = not stated.")]
        public float PenetrationMm { get; init; }
    }
}
