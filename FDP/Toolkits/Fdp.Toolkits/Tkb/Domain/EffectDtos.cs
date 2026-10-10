using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>What kind of realism effect a TKB effect type is — how the map layers draw it.</summary>
    public enum EffectKind : byte
    {
        /// <summary>Fire from a barrel: a short bright star / camera-facing flash, bound to the shooter's muzzle (VE-J).</summary>
        MuzzleFlash = 0,
        /// <summary>A burst at a hit point: an expanding, fading fireball / disc, world-fixed (VE-M).</summary>
        Explosion = 1,
        /// <summary>A mark left on the static world under a hit: a dark blot draped on the ground, long-lived (VE-H, VE-I).</summary>
        Decal = 2,
        /// <summary>A short glowing streak along the shot's line.</summary>
        Tracer = 3,
    }

    /// <summary>
    /// ⭐ CE-1042 E1 — what an EFFECT type looks like (TKB descriptor <c>"Effect.Visual"</c>). 📄 docs/DESIGN_Visual_Effects.md VE-C.
    /// 🔒 User, 2026-10-10: <i>"each effect entity has its TKB type of course defining the effect"</i>. An effect entity carries
    /// only its type and its age; every map layer draws it from here, at the phase <c>Age / Duration</c>.
    /// </summary>
    [TkbDescriptor("Effect.Visual")]
    public record EffectVisualDto
    {
        public EffectKind Kind { get; init; }

        /// <summary>How long the effect lives (s, simulation time — VE-G).</summary>
        [EditUnit("s")] public float Duration { get; init; } = 0.5f;

        /// <summary>Its size at full bloom (m): a flash's / fireball's diameter, a decal's diameter, a tracer's streak width.</summary>
        [EditUnit("m")] public float Size { get; init; } = 1f;

        /// <summary>Its colour, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
        public string ColorHex { get; init; } = "#FFFFFF";

        /// <summary>How long it fades out at the end of its life (s); 0 ⇒ it vanishes at once.</summary>
        [EditUnit("s")] public float FadeSeconds { get; init; }
    }

    /// <summary>
    /// ⭐ CE-1042 E1 — which effects an AMMO type makes (TKB descriptor <c>"Effect.Set"</c>): references to effect types, 0 = none.
    /// 📄 docs/DESIGN_Visual_Effects.md VE-D. 🔒 User: <i>"multiple sizes based on ammo type"</i>. An ammo without one gets the
    /// default for its calibre class (<c>Hrot.Core.Tkb.EffectTkbCatalog.EffectsFor</c>).
    /// </summary>
    [TkbDescriptor("Effect.Set")]
    public record MunitionEffectsDto
    {
        /// <summary>The flash at the muzzle when it is fired.</summary>
        public long MuzzleFlash { get; init; }

        /// <summary>The burst where it hits.</summary>
        public long Explosion { get; init; }

        /// <summary>The mark it leaves on the ground under the hit (none when it hits a vehicle — VE-M).</summary>
        public long Decal { get; init; }

        /// <summary>The streak along its flight; 0 ⇒ no tracer.</summary>
        public long Tracer { get; init; }
    }
}
