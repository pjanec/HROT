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
    }

    /// <summary>When a warhead detonates. v1: impact and time (W-4); airburst is later.</summary>
    public enum FuzeKind : byte
    {
        /// <summary>On the first contact — an entity or the terrain.</summary>
        Impact = 0,
        /// <summary><see cref="WarheadDto.FuzeDelayS"/> after it is fired, or on contact if it stops first.</summary>
        Time = 1,
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

        public FuzeKind Fuze { get; init; }

        [EditUnit("s")]
        [Description("Time fuze: seconds from firing to detonation.")]
        public float FuzeDelayS { get; init; }

        [Description("Fired on a high arc at a point (mortar, thrown grenade), not straight at a target.")]
        public bool Indirect { get; init; }

        /// <summary>True when the warhead has any area effect.</summary>
        [JsonIgnore]
        public bool HasAreaEffect => BlastInjuryRadiusM > 0f || FragmentRadiusM > 0f;
    }
}
