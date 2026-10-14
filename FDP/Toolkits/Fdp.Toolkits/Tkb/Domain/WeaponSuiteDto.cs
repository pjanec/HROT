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
        /// <summary>⭐ WL (W-5) — what kind of weapon this is; selects the weapon-brain profile. Unknown = the default profile.</summary>
        public WeaponCategory Category { get; init; }

        /// <summary>⭐ WL-1 (W-9) — the turret this mount sits on (<see cref="TurretSuiteDto.Turrets"/> index); −1 = fixed to the hull.</summary>
        public int TurretIndex { get; init; } = -1;

        /// <summary>⭐ WL-1 (VE-K) — the muzzle relative to its turret's trunnion, or to the hull's reference point when fixed (m;
        /// X forward, Y left, Z up). Read by <c>Muzzle.Of</c> (B5).</summary>
        public float MuzzleOffsetX { get; init; }
        public float MuzzleOffsetY { get; init; }
        public float MuzzleOffsetZ { get; init; }

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

        /// <summary>
        /// ⭐ LR-1 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — the TRACER round mixed into this mount's belt / magazine: every
        /// <see cref="TracerEvery"/>-th shot carries this munition type instead of <see cref="AmmoGuid"/>. It changes WHAT the round is
        /// (so it is drawn and replicated, PR-F) — never its damage, penetration or warhead, which stay the mount's. 0 = no tracers.
        /// </summary>
        [AmmoRef]
        public ulong TracerAmmoGuid { get; init; }

        /// <summary>⭐ LR-1 — one tracer in this many shots (5 = the common 1-in-5 mix); 0 = none.</summary>
        public int TracerEvery { get; init; }

        /// <summary>
        /// ⭐ M8 (D-I; docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §2) — the ROUNDS this mount carries by type (a tank gun:
        /// APFSDS, HEAT-MP, HE). <b>Empty = the one-round mount</b>: <see cref="AmmoGuid"/> and <see cref="InitialAmmunition"/>, as before.
        /// With a loadout the total carried is the sum of its <c>Rounds</c> and the weapon brain picks the round per target (<c>AmmoChoice</c>);
        /// the first entry is loaded at spawn. Up to 4 types.
        /// </summary>
        public List<AmmoLoadDto> Loadout { get; init; } = new();
    }

    /// <summary>⭐ M8 — one munition type in a mount's <see cref="WeaponMountDto.Loadout"/> and how many rounds of it.</summary>
    public record AmmoLoadDto
    {
        /// <summary>The TKB type of the munition.</summary>
        [AmmoRef]
        public ulong AmmoGuid { get; init; }

        /// <summary>Rounds of it carried at spawn.</summary>
        public int Rounds { get; init; }
    }
}
