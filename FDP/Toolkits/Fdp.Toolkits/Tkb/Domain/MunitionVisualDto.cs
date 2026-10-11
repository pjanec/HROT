using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>⭐ P1 (Hrot docs/DESIGN_Visual_Effects.md §8 PR-A) — the form a round is drawn in while it flies or lies.</summary>
    public enum MunitionShape : byte
    {
        /// <summary>A hand grenade: a short, round-ended body that tumbles in flight.</summary>
        Grenade = 0,

        /// <summary>A mortar bomb / aircraft bomb: a body with a fin tail, nose along the flight.</summary>
        Bomb = 1,

        /// <summary>A gun shell: a pointed cylinder, nose along the flight.</summary>
        Shell = 2,

        /// <summary>An unguided rocket: a long thin body, nose along the flight, usually with a smoke trail.</summary>
        Rocket = 3,

        /// <summary>A guided missile: a body with mid fins, nose along the flight, usually with a smoke trail.</summary>
        Missile = 4,
    }

    /// <summary>
    /// ⭐ P1 (Hrot docs/DESIGN_Visual_Effects.md §8 PR-A) — <b>how a munition's round looks</b> in flight and lying on the ground, on the
    /// MUNITION type (the round's <c>BallisticProjectile.Ammo</c>): "the TKB type defines it", as <c>Effect.Visual</c> does for effects.
    /// Optional — a munition with none draws no body (a bullet: its tracer is the effect).
    /// </summary>
    [TkbDescriptor("Munition.Visual")]
    public record MunitionVisualDto
    {
        /// <summary>The form.</summary>
        public MunitionShape Shape { get; init; }

        /// <summary>Body length (m).</summary>
        [EditUnit("m")] public float Length { get; init; } = 0.1f;

        /// <summary>Body diameter (m).</summary>
        [EditUnit("m")] public float Diameter { get; init; } = 0.06f;

        /// <summary><c>#RRGGBB</c>.</summary>
        public string ColorHex { get; init; } = "#4B5320";

        /// <summary>Trails smoke back toward where it was fired from (a rocket, a missile).</summary>
        public bool SmokeTrail { get; init; }
    }
}
