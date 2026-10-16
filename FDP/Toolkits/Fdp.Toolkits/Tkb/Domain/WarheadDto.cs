using System.ComponentModel;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>What a round does beyond its direct hit. 📄 docs/DESIGN_Building_Interiors.md §3k (W-1).</summary>
    public enum WarheadKind : byte
    {
        /// <summary>No explosive: the direct hit only (a bullet, a kinetic penetrator).</summary>
        Kinetic = 0,
        /// <summary>High explosive: blast and fragments.</summary>
        HighExplosive = 1,
        /// <summary>Fragmentation (a grenade): fragments first, a small blast.</summary>
        Fragmentation = 2,
        /// <summary>Shaped charge (HEAT/HEDP): the jet is the direct hit; a small blast and fragments around it.</summary>
        ShapedCharge = 3,
        /// <summary>⭐ <c>CE-3219</c> (civilians B-1, CV-6 ⓓ) — non-lethal blunt: a baton round, a stone; the hit knocks the man down.</summary>
        Blunt = 4,
        /// <summary>⭐ <c>CE-3219</c> — an irritant (tear gas): its burst leaves a cloud that irritates those inside.</summary>
        Irritant = 5,
        /// <summary>⭐ <c>CE-3219</c> — a stun grenade: one stun hit to everyone in its radius.</summary>
        Stun = 6,
    }

    /// <summary>When a warhead detonates. v1: impact and time (W-4); airburst is later.</summary>
    public enum FuzeKind : byte
    {
        /// <summary>On the first contact — an entity or the terrain.</summary>
        Impact = 0,
        /// <summary><see cref="WarheadDto.FuzeDelayS"/> after it is fired, or on contact if it stops first.</summary>
        Time = 1,
        /// <summary>⭐ <c>CE-2187</c> (DESIGN_Missiles_And_Ammo §3c, AD2) — a guided round bursts when it passes within
        /// <see cref="WarheadDto.ProximityFuzeM"/> of its target (or on contact): a SAM rarely strikes the hull.</summary>
        Proximity = 2,
    }

    /// <summary>
    /// ⭐⭐ Buildings programme Stage 6 (<c>CE-1032</c>, R-225 W-1) — the warhead of a MUNITION type: the TKB entry of the round
    /// (<see cref="WeaponMountDto.AmmoGuid"/>) carries it; a detonation carries only the munition's type id and every node looks the
    /// numbers up here (§3e — one source). A munition with no <see cref="WarheadDto"/> falls back to the reference library by the
    /// ammo's NAME (<c>ParameterResolver.Warhead</c>), then to none (kinetic).
    /// <para>Ranges are where the effect ENDS: blast does <see cref="BlastLethalDamage"/> inside <see cref="BlastLethalRadiusM"/>,
    /// falling to 0 at <see cref="BlastInjuryRadiusM"/>; fragments do <see cref="FragmentDamage"/> at the burst, falling to 0 at
    /// <see cref="FragmentRadiusM"/>, and each wall or body on the line takes its share (§3k W-6′, W-7′).</para>
    /// </summary>
    [TkbDescriptor("Gen.Warhead")]
    public record WarheadDto
    {
        public WarheadKind Kind { get; init; }

        [EditUnit("kg TNT")]
        [Description("Explosive mass, TNT-equivalent kg.")]
        public float ExplosiveKg { get; init; }

        [EditUnit("m")]
        [Description("Blast: full damage inside this radius.")]
        public float BlastLethalRadiusM { get; init; }

        [EditUnit("m")]
        [Description("Blast: no damage beyond this radius.")]
        public float BlastInjuryRadiusM { get; init; }

        [Description("Blast damage inside the lethal radius.")]
        public float BlastLethalDamage { get; init; }

        [EditUnit("m")]
        [Description("Fragments: no damage beyond this radius.")]
        public float FragmentRadiusM { get; init; }

        [EditUnit("mm RHA")]
        [Description("Penetration of one fragment, mm RHA (against walls and armour).")]
        public float FragmentPenetrationMm { get; init; }

        [Description("Fragment damage to a fully exposed target at the burst.")]
        public float FragmentDamage { get; init; }

        /// <summary>⭐ AE5 (DESIGN_Armour_Hit_Model §7.7) — how many fragments the casing breaks into; with it a HULL counts the
        /// fragments that strike it. 0 = not stated: a hull takes the person rule (<see cref="FragmentDamage"/>).</summary>
        [Description("Fragments the casing breaks into (a hull counts the ones that strike it). 0 = not stated.")]
        public float FragmentCount { get; init; }

        /// <summary>⭐ AE5 — the damage ONE fragment that penetrates does to a hull: the kinetic law every bullet uses
        /// (perSqrtJoule · √(½ m v₀²)).</summary>
        [Description("Damage of one penetrating fragment on a hull (the kinetic law of a bullet).")]
        public float FragmentHitDamage { get; init; }

        /// <summary>⭐ CE-3203 (DESIGN_Building_Interiors §3k.1, W-6″) — the damage ONE fragment does to a MAN at the burst's speed
        /// (woundPerSqrtJoule · √(½ m v₀²)); it falls with the fragment's speed (<see cref="FragmentDragM"/>). 0 = not stated.</summary>
        [Description("Damage of one fragment hitting a man, at the burst (falls with its speed).")]
        public float FragmentWoundDamage { get; init; }

        /// <summary>⭐ CE-3203 — the distance over which a fragment's speed falls to 1/e (drag; a heavier fragment carries further). Its
        /// per-hit damage and its penetration fall with that speed. 0 = no drag.</summary>
        [EditUnit("m")]
        [Description("Distance over which a fragment's speed falls to 1/e (drag).")]
        public float FragmentDragM { get; init; }

        public FuzeKind Fuze { get; init; }

        [EditUnit("s")]
        [Description("Time fuze: seconds from firing to detonation.")]
        public float FuzeDelayS { get; init; }

        /// <summary>⭐ <c>CE-2187</c> (§3c, AD2) — a proximity fuze's reach: the guided round bursts the tick it passes this close to its
        /// target's middle.</summary>
        [EditUnit("m")]
        [Description("Proximity fuze: bursts when the guided round passes this close to its target.")]
        public float ProximityFuzeM { get; init; }

        [Description("Fired on a high arc at a point (mortar, thrown grenade), not straight at a target.")]
        public bool Indirect { get; init; }

        /// <summary>⭐ M3 (D-C) — a TANDEM shaped charge (a precursor for reactive armour). Reads the same armour column as a single
        /// shaped charge until ERA exists (<c>ArmorModel.ArmourFor</c>). Meaningful only with <see cref="WarheadKind.ShapedCharge"/>.</summary>
        [Description("Tandem shaped charge (defeats reactive armour once ERA exists).")]
        public bool Tandem { get; init; }

        /// <summary>True when the warhead has any area effect.</summary>
        [JsonIgnore]
        public bool HasAreaEffect => BlastInjuryRadiusM > 0f || FragmentRadiusM > 0f;
    }
}
