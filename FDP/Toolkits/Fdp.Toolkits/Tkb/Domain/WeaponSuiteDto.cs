using System.Collections.Generic;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Weapon suite descriptor for a TKB entity.
    /// Drives projection of <c>WeaponState</c> by <c>CombatTkbTranslator</c>.
    /// Keep <see cref="WeaponMountDto"/> in the same file as the root aggregate
    /// to avoid namespace clutter.
    /// </summary>
    [TkbDescriptor("Combat.WeaponSuite")]
    public record WeaponSuiteDto
    {
        /// <summary>Ordered list of weapon mounts on this platform.</summary>
        public List<WeaponMountDto> Mounts { get; init; } = new();
    }

    /// <summary>One weapon mount on a platform.</summary>
    public record WeaponMountDto
    {
        /// <summary>TKB GUID of the weapon entity mounted here. Zero = no weapon linked.</summary>
        [WeaponRef]
        public ulong WeaponGuid { get; init; }

        /// <summary>Initial ammunition count loaded into this mount at spawn.</summary>
        public int InitialAmmunition { get; init; }

        /// <summary>Muzzle velocity in m/s. Used directly when WeaponGuid is zero.</summary>
        public float MuzzleVelocity { get; init; }

        /// <summary>⭐ <c>CE-3071</c> (A4) — this mount's effective range in metres; 0 = not declared.</summary>
        [EditUnit("m")]
        public float Range { get; init; }

        /// <summary>⭐ <c>CE-3071</c> — armour penetration of this mount's round, mm RHA (<c>ArmorModel</c>).</summary>
        [EditUnit("mm RHA")]
        public float Penetration { get; init; }

        /// <summary>⭐ <c>CE-3071</c> — damage of one penetrating hit; 0 = unknown munition (the flat default applies).</summary>
        public float DamagePerHit { get; init; }
    }
}
