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

        /// <summary>
        /// ⭐ <c>AQ85</c> C (R-216) — the weapon's aim dispersion, mils (the half-width of a uniform spread). <b>0 = exact aim</b> (every
        /// round flies at the target, as before); a type opts in. Scaled by the shooter's state in <c>HitModel.Sigma</c>.
        /// </summary>
        [EditUnit("mils")]
        public float DispersionMils { get; init; }

        /// <summary>
        /// ⭐ <c>CE-3136</c> P-3 (peek-and-fire D3) — the AIM TIME: an aimed shot leaves only after the shooter has seen its target
        /// continuously this long. <b>0 = not declared</b> ⇒ <c>EngineFallbacks.AimSeconds</c> (0.8 s). Blind fire has none.
        /// </summary>
        [EditUnit("s")]
        public float AimSeconds { get; init; }

        /// <summary>
        /// ⭐ <c>CE-3136</c> P-5 (peek-and-fire D9) — rounds per MAGAZINE. <b>0 = no magazine</b>: the mount fires its whole
        /// <see cref="InitialAmmunition"/> without reloading (every mount before P-5). <see cref="InitialAmmunition"/> stays the total carried.
        /// </summary>
        public int MagazineSize { get; init; }

        /// <summary>⭐ <c>CE-3136</c> P-5 — seconds to change a magazine. <b>0 = not declared</b> ⇒ <c>EngineFallbacks.ReloadSeconds</c> (3 s).</summary>
        [EditUnit("s")]
        public float ReloadSeconds { get; init; }

        /// <summary>
        /// ⭐ Buildings §3d P1b (R-217) — the TKB GUID of the AMMUNITION type loaded in this mount (one of the weapon's supported
        /// ammo). With it, the round's penetration comes from the launcher × ammo pair (<see cref="AmmoWeaponBallisticsDto"/>);
        /// <b>0 = not declared</b> ⇒ this mount's own <see cref="Penetration"/> applies, as before. Ammo switching is later.
        /// </summary>
        [AmmoRef]
        public ulong AmmoGuid { get; init; }
    }
}
