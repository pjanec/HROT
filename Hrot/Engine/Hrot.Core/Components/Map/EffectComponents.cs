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

/// <summary>
/// ⭐ CE-1115 (docs/DESIGN_Visual_Effects.md §9 DV-3) — an effect that MOVES: a decoy flare or a chaff cloud follows the path its release
/// fixed — <c>Fdp.Toolkit.Combat.DecoyFlight.At(Origin, Velocity, SinkMps, now − FiredAt)</c>, the one function the SimHost's decoy uses
/// too, so the drawn flare is where the seeker sees it. The spawn system writes the effect's <c>SimTransform</c> from it every frame.
/// <para>⭐ RECORDED, never saved (VE-R): a replay replays the recorded positions.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.EffectFlight)]
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
public struct EffectFlight
{
    /// <summary>Where it left (the aircraft at the release), world metres.</summary>
    public Vector3 Origin;

    /// <summary>The velocity it left with (the aircraft's), m/s.</summary>
    public Vector3 Velocity;

    /// <summary>How fast it sinks, m/s (the expendable's <c>ExpendableDto.SinkMps</c>).</summary>
    public float SinkMps;

    /// <summary>The synced sim time (s) it was released.</summary>
    public double FiredAt;
}

/// <summary>⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P) — where an attached effect sits on its entity, found every frame.</summary>
public enum EffectAnchorKind : byte
{
    /// <summary>The muzzle of weapon <see cref="EffectAnchor.WeaponIndex"/> (<c>Muzzle.TryOf</c>, E4).</summary>
    Muzzle = 0,

    /// <summary>The top centre of the entity's drawn body.</summary>
    Body = 1,
}
