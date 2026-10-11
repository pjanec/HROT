using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Map.Common.Components;

// ⛔ RETIRED (W1, docs/DESIGN_Visual_Effects.md §7 VE-Q): `EffectLifetime { Age, Duration }` — an age summed per frame. Effects carry
//    the generic Fdp.Toolkit.Time.Lifespan now (made at, lives for; the age is read from the sim clock). Id 124 stays reserved.

/// <summary>
/// ⭐ CE-1042 E2 (VE-J) — what an effect is ATTACHED to: a muzzle flash or a tracer belongs to the shooter's weapon, so the renderer
/// finds the muzzle EVERY FRAME and the flash rides the barrel while the hull drives and the turret slews. The shooter gone ⇒ the
/// effect ends. The muzzle is <c>Muzzle.TryOf</c> (E4, VE-K).
/// <para>⭐ RECORDED, never saved (W1, VE-R): a replay draws the recorded flash on the recorded barrel.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.EffectAnchor)]
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
public struct EffectAnchor
{
    /// <summary>The entity it is attached to: the one that FIRED (<see cref="EffectAnchorKind.Muzzle"/>) or the burning WRECK
    /// (<see cref="EffectAnchorKind.Body"/>). Gone ⇒ the effect ends.</summary>
    public Entity Shooter;

    /// <summary>Which of its weapon mounts fired.</summary>
    public int WeaponIndex;

    /// <summary>Where the shot was aimed when it was fired (a tracer streaks toward it); zero when unknown.</summary>
    public Vector3 Toward;

    /// <summary>⭐ W3 (VE-P) — WHERE on <see cref="Shooter"/>: its weapon's muzzle (a flash, a tracer) or the top of its body
    /// (flames and smoke on a wreck).</summary>
    public EffectAnchorKind Kind;
}

/// <summary>⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P) — where an attached effect sits on its entity, found every frame.</summary>
public enum EffectAnchorKind : byte
{
    /// <summary>The muzzle of weapon <see cref="EffectAnchor.WeaponIndex"/> (<c>Muzzle.TryOf</c>, E4).</summary>
    Muzzle = 0,

    /// <summary>The top centre of the entity's drawn body.</summary>
    Body = 1,
}
