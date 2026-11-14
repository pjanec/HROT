using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐⭐ <c>CE-2155</c> (R-263 D1 / R-2011, docs/DESIGN_Injury_States.md) — a type whose people get HURT before they die: the health
    /// fractions where they become wounded and incapacitated, and how long an incapacitated one takes to bleed out.
    /// </summary>
    /// <remarks>
    /// ⭐ The state itself is never stored — <c>Injury.StateOf</c> reads it off the replicated <c>Health</c> and these thresholds, so every
    /// node agrees by construction (I1). ⭐ A type without this descriptor has no wounded / incapacitated band: ok until dead, as before (I2).
    /// </remarks>
    [TkbDescriptor("Gen.Injury")]
    public record InjuryDefDto
    {
        /// <summary>Below this fraction of max health the person is WOUNDED.</summary>
        public float WoundedBelow { get; init; } = 0.6f;

        /// <summary>Below this fraction of max health the person is INCAPACITATED: cannot move or fire, and bleeds out. ⭐ 0.3 (user,
        /// 2026-10-13: "go with 0.3") — at 0.25 a 100-HP soldier under 25-damage rifle fire went 75 → 50 → 25 → dead and was never down.</summary>
        public float IncapacitatedBelow { get; init; } = 0.3f;

        /// <summary>Seconds an incapacitated person takes to bleed from <see cref="IncapacitatedBelow"/> to death. 0 = never bleeds out.</summary>
        public float BleedOutSeconds { get; init; } = 120f;

        /// <summary>⭐ CE-1082 (Injury slice I-b; docs/DESIGN_Map_3D_Mode.md §6m C-2) — a WOUNDED person's top speed as a share of his
        /// healthy one. 🔒 User, 2026-10-14: "when soldier is wounded his motion pattern could be degraded" — 0.6, approved.</summary>
        public float WoundedSpeedFactor { get; init; } = 0.6f;
    }
}
