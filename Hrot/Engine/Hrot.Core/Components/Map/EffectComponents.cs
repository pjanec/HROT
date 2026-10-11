using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.Map.Common.Components;

/// <summary>
/// ⭐ CE-1042 E2 — the age of a realism EFFECT entity (docs/DESIGN_Visual_Effects.md VE-B): a muzzle flash, an explosion, a decal
/// or a tracer. The entity's <c>TkbIdentity</c> is its effect type (how it looks — <c>Effect.Visual</c>); this says how far through
/// its life it is, in SIMULATION time (VE-G), so a paused sim freezes it mid-phase. Local presentation: never replicated, never
/// saved, never recorded — every map host makes its own from the fire / hit events (VE-A).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.EffectLifetime)]
[DataPolicy(DataPolicy.Transient)]   // local presentation: not snapshotted, not recorded, not saved (VE-B)
public struct EffectLifetime
{
    /// <summary>Seconds since it was made (simulation time).</summary>
    public float Age;

    /// <summary>Its life (s), copied from the type's <c>Effect.Visual.Duration</c> when it was made.</summary>
    public float Duration;

    /// <summary>The phase the layers draw: 0 when made, 1 when it ends.</summary>
    public readonly float Phase => Duration > 0f ? System.Math.Clamp(Age / Duration, 0f, 1f) : 1f;
}

/// <summary>
/// ⭐ CE-1042 E2 (VE-J) — what an effect is ATTACHED to: a muzzle flash or a tracer belongs to the shooter's weapon, so the renderer
/// finds the muzzle EVERY FRAME and the flash rides the barrel while the hull drives and the turret slews. The shooter gone ⇒ the
/// effect ends. ⚠ Until <c>Muzzle.Of</c> (E4, VE-K) the muzzle is the kit's neutral barrel.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.EffectAnchor)]
[DataPolicy(DataPolicy.Transient)]
public struct EffectAnchor
{
    /// <summary>The entity that fired.</summary>
    public Entity Shooter;

    /// <summary>Which of its weapon mounts fired.</summary>
    public int WeaponIndex;

    /// <summary>Where the shot was aimed when it was fired (a tracer streaks toward it); zero when unknown.</summary>
    public Vector3 Toward;
}
