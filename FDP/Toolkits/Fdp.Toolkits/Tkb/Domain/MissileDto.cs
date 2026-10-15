using System.ComponentModel;
using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ M4 (D-D) — how a guided round finds its target. 📄 docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md §3.</summary>
    public enum GuidanceKind : byte
    {
        /// <summary>A straight round (RPG, LAW) — only the aim time and the dispersion.</summary>
        Unguided = 0,
        /// <summary>Steered onto the LAUNCHER's sight line while the gunner sees the target (TOW-like): break the sight and it flies on.</summary>
        Command = 1,
        /// <summary>The round's own seeker, locked before launch (Javelin, NLAW): it follows the target it sees.</summary>
        FireAndForget = 2,
    }

    /// <summary>
    /// ⭐⭐ M4 (D-D; DESIGN_Missiles_And_Ammo §3) — a GUIDED munition: on the MUNITION type (beside its <see cref="WarheadDto"/>). The round
    /// flies at <see cref="SpeedMps"/> for up to <see cref="MaxFlightSeconds"/>, steered by <c>GuidedRoundSystem</c> on the Muscle; the
    /// weapon brain refuses below <see cref="MinRangeM"/>. No aerodynamics: a fixed speed and a turn-rate limit.
    /// </summary>
    [TkbDescriptor("Combat.Missile")]
    public record MissileDto
    {
        public GuidanceKind Guidance { get; init; }

        [EditUnit("m/s")]
        [Description("Flight speed (replaces the generated muzzle speed).")]
        public float SpeedMps { get; init; }

        [EditUnit("s")]
        [Description("The longest flight; the round is gone after it.")]
        public float MaxFlightSeconds { get; init; }

        [EditUnit("m")]
        [Description("Below this range the weapon brain does not fire (it cannot arm or turn in time).")]
        public float MinRangeM { get; init; }

        [Description("Fire-and-forget only: climb, cruise and dive onto the target's roof.")]
        public bool TopAttack { get; init; }

        [EditUnit("m")]
        [Description("Top attack only beyond this range; closer, the round flies direct.")]
        public float TopAttackMinRangeM { get; init; }

        [EditUnit("m")]
        [Description("Top attack: the cruise height above the launch point.")]
        public float CruiseAltitudeM { get; init; }

        [EditUnit("deg")]
        [Description("Top attack: the dive angle.")]
        public float DiveAngleDeg { get; init; }

        /// <summary>⭐ <c>CE-3198</c> (DESIGN_Missiles_And_Ammo §3b) — the NLAW's OVERFLY top attack: fly just above the
        /// target's roof and fire the charge DOWN as it passes over (a hull only; a man is attacked direct).</summary>
        [Description("Fire-and-forget only: fly over a vehicle's roof and fire the charge down as it passes (the NLAW).")]
        public bool Overfly { get; init; }

        [EditUnit("m")]
        [Description("Overfly: the height above the roof the round passes at.")]
        public float OverflyHeightM { get; init; } = 1f;
    }
}
