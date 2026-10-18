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

        /// <summary>
        /// ⭐ D-L (docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §4) — may this weapon fire while its unit moves? <b>null = by
        /// <see cref="Category"/></b>: an ATGM or a mortar stands to fire; everything else fires on the move (an old, unstabilised tank
        /// gun sets <c>false</c>).
        /// </summary>
        public bool? FireOnMove { get; init; }

        /// <summary>
        /// ⭐ Air Ops slice 6a (AW-1; docs/designs/air-attack/DESIGN_Air_Operations.md §7a) — the NOSE CONE of a FIXED mount (no turret):
        /// the half-angle, in degrees, around the unit body's 3-D forward (pitch included) inside which the AI may fire it — an A-10's
        /// cannon, a helicopter's rocket pod. The weapon brain holds a round aimed outside it (<c>OutOfArc</c>; the aircraft must point).
        /// <b>0 = no cone</b>: the mount fires in any direction, as before. The operator is never held (he points the aircraft).
        /// </summary>
        [EditUnit("deg")]
        public float FixedConeDeg { get; init; }

        /// <summary>
        /// ⭐ Indirect fire IF-1 (I2, IF-C; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.3) — which arcs an INDIRECT round from this
        /// mount may fly: a mortar <see cref="IndirectArc.HighOnly"/>, a howitzer <see cref="IndirectArc.Both"/>. <b><see cref="IndirectArc.Unset"/>
        /// = today's rule</b> (<c>ThrowArc</c>: a launcher high, a throw low unless masked) and no fire solution — a hand throw and every mount
        /// before IF-1 are unchanged. Any other value makes the weapon brain solve the charge and the arc (<c>FireSolution.For</c>).
        /// </summary>
        public IndirectArc Arc { get; init; }

        /// <summary>⭐ IF-1 (I2) — the barrel's elevation limits for an indirect solution, degrees above the horizontal; 0 = no limit.</summary>
        [EditUnit("deg")]
        public float MinElevationDeg { get; init; }
        /// <inheritdoc cref="MinElevationDeg"/>
        [EditUnit("deg")]
        public float MaxElevationDeg { get; init; }

        /// <summary>⭐ <c>CE-2188</c> (BS-1-DESIGN §5.4c) — this mount's rate of fire, rounds per minute; 0 = not declared. A flare / chaff
        /// DISPENSER's release interval is 60 ÷ this (<c>Countermeasures.Release</c>).</summary>
        [EditUnit("rpm")]
        public float RateOfFire { get; init; }

        /// <summary>⭐ <c>CE-3229</c> (B-7, AW-15 / AW-24; docs/DESIGN_Civilians_And_Crowd_Control.md §7.8 A-3) — what this weapon LOOKS like
        /// to an observer judging a civilian. <b>null = by <see cref="Category"/></b> (<c>ItemAppearances.Of</c>): only an odd item sets it — a toy
        /// gun (looks like a rifle, fires nothing), a disguised weapon. Appended.</summary>
        public ItemAppearanceDto? Appearance { get; init; }

        /// <summary>⭐ <c>CE-3230</c> (B-8, AW-7; Civilians §7.9) — this mount is a PERSON's HANDS: appended last to every person type by
        /// <c>PersonHands.EnsureOnPersons</c>, empty at start, its loadout what he picked up. Appended.</summary>
        public bool Hands { get; init; }

        /// <summary>⭐ <c>CE-3254</c> (BR-9, DA-13; docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §12b) — a SCOPE on this
        /// mount: while its man AIMS it, his visual sensor reaches this many times further through <see cref="ScopeFovDegrees"/> and he
        /// identifies this many times further — the binocular rule on a weapon (<c>HeldItems.ScopeUp</c>). 0 or 1 = none. Appended.</summary>
        public float ScopeMagnification { get; init; }

        /// <summary>⭐ <c>CE-3254</c> — the scope's field of view (degrees); 0 = <c>HeldItems.ScopeFallbackFovDegrees</c>. Appended.</summary>
        [EditUnit("deg")]
        public float ScopeFovDegrees { get; init; }
    }

    /// <summary>⭐ <c>CE-3229</c> (AW-15) — the shape an observer may see in a man's hands.</summary>
    public enum ItemLook : byte
    {
        /// <summary>Nothing to see.</summary>
        None = 0,
        Rifle = 1,
        Pistol = 2,
        /// <summary>Something long he cannot name from afar (a stick, a tool, a rifle in the dark).</summary>
        LongObject = 3,
        /// <summary>Something small in the hand (a phone, a stone, a grenade).</summary>
        SmallObject = 4,
    }

    /// <summary>
    /// ⭐ <c>CE-3229</c> (B-7, AW-15 / AW-19; Civilians §7.8 A-3, A-5) — an item's APPEARANCE: what an observer concludes once he can identify
    /// it, which is not always what it is (a toy gun looks lethal). Named so beside <c>Fdp.Core.EntityAppearance</c> (a body's colour).
    /// </summary>
    public record ItemAppearanceDto
    {
        public ItemLook Look { get; init; }

        /// <summary>Its size, m — the identification range grows with it (§7.8 A-4). 0 = by <see cref="Look"/>.</summary>
        [EditUnit("m")]
        public float SizeM { get; init; }

        /// <summary>Does it LOOK lethal once identified? null = by the rounds the mount fires (lethal unless every one is Blunt, Irritant,
        /// Stun or Smoke).</summary>
        public bool? Lethal { get; init; }

        /// <summary>A REACH weapon (a knife, a machete, AW-19): lethal only within this many metres; 0 = a ranged weapon.</summary>
        [EditUnit("m")]
        public float ReachM { get; init; }
    }

    /// <summary>⭐ Indirect fire IF-1 (I2, IF-C) — the arcs a mount's indirect round may fly.</summary>
    public enum IndirectArc : byte
    {
        /// <summary>Today's rule (<c>ThrowArc</c>), no fire solution: every mount declared before IF-1, a hand throw. ⚠ Not "Default": the
        /// IDL generator scopes a descriptor file's enum members together, and <c>default</c> is an IDL keyword.</summary>
        Unset = 0,
        /// <summary>A mortar: the high arc only.</summary>
        HighOnly = 1,
        /// <summary>A howitzer, a grenade launcher: low when it clears, else high.</summary>
        Both = 2,
        /// <summary>A flat-trajectory gun firing indirect: the low arc only.</summary>
        LowOnly = 3,
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
