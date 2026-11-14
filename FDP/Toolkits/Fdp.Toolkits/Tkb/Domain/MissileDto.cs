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
        /// <summary>⭐ <c>CE-2187</c> (DESIGN_Missiles_And_Ammo §3c, AD1) — a HEAT seeker (Stinger, Igla): steers like
        /// <see cref="FireAndForget"/> but sees only inside its cone, and a flare in that cone can seduce it (<c>CE-2188</c>).</summary>
        InfraRed = 3,
        /// <summary>⭐ <c>CE-2188</c> (§3c, AD6) — a RADAR seeker: chaff can seduce it. ⚠ No munition uses it yet (the radar SAM is a
        /// later design question); it exists so chaff has something to decoy.</summary>
        Radar = 4,
    }

    /// <summary>⭐ <c>CE-2187</c> (§3c, AD1) — what a guided munition may be fired at: the weapon choice values it at 0 against the
    /// other domain (<c>AmmoChoice</c>) — a MANPADS is never chosen against a truck, an ATGM never against a flying helicopter.</summary>
    public enum TargetDomain : byte
    {
        /// <summary>Either domain (the default — every munition before CE-2187). ⚠ Not "Any": the DDS code generator emits this enum as
        /// IDL, where <c>any</c> is a keyword.</summary>
        Either = 0,
        /// <summary>A target on the ground — anything whose locomotion is not an aircraft's.</summary>
        Ground = 1,
        /// <summary>An aircraft (<c>Locomotions.InAir</c>: by its locomotion class — a helicopter on its pad is still one).</summary>
        Air = 2,
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

        /// <summary>⭐ <c>CE-2222</c> — how fast the round turns (°/s); 0 = the engine's <c>GuidedRoundSystem.TurnRateDegPerSec</c>. A missile
        /// turns far tighter than an airframe: the turn radius (speed / rate) sets the shortest range its climb-and-dive fits.</summary>
        [EditUnit("deg/s")]
        [Description("How fast the round turns; 0 = the engine default (30 deg/s). Sets the shortest range a top attack fits.")]
        public float TurnRateDegPerSec { get; init; }

        /// <summary>⭐ <c>CE-2222</c> — the top attack's climb angle (°); 0 = the engine's <c>GuidedRoundSystem.ClimbAngleDeg</c>.</summary>
        [EditUnit("deg")]
        [Description("Top attack: the climb angle; 0 = the engine default (30 deg).")]
        public float ClimbAngleDeg { get; init; }

        /// <summary>⭐ <c>CE-3198</c> (DESIGN_Missiles_And_Ammo §3b) — the NLAW's OVERFLY top attack: fly just above the
        /// target's roof and fire the charge DOWN as it passes over (a hull only; a man is attacked direct).</summary>
        [Description("Fire-and-forget only: fly over a vehicle's roof and fire the charge down as it passes (the NLAW).")]
        public bool Overfly { get; init; }

        [EditUnit("m")]
        [Description("Overfly: the height above the roof the round passes at.")]
        public float OverflyHeightM { get; init; } = 1f;

        /// <summary>⭐ <c>CE-2187</c> (§3c) — the domain it may be fired at.</summary>
        [Description("What it may be fired at: Either, Ground or Air (the weapon choice never picks it against the other).")]
        public TargetDomain TargetDomain { get; init; }

        /// <summary>⭐ <c>CE-2187</c> (§3c) — an IR / radar seeker's field of view, the full angle about the round's flight; 0 = it sees
        /// all round (a fire-and-forget round before CE-2187). Out of it the round flies to where it last saw its target; a decoy inside
        /// it may seduce it.</summary>
        [EditUnit("deg")]
        [Description("Seeker field of view, full angle about the flight direction (0 = all round).")]
        public float SeekerConeDeg { get; init; }

        /// <summary>⭐ <c>CE-2188</c> (§3c, AD3) — the chance an IR seeker IGNORES a flare salvo in its cone (0 = always seduced).</summary>
        [Description("Chance the IR seeker ignores a flare salvo in its cone (0..1).")]
        public float FlareRejection { get; init; }

        /// <summary>⭐ <c>CE-2188</c> — the chance a radar seeker ignores a chaff salvo in its cone (0 = always seduced).</summary>
        [Description("Chance the radar seeker ignores a chaff salvo in its cone (0..1).")]
        public float ChaffRejection { get; init; }
    }
}
