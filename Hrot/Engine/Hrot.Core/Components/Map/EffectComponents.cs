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
/// ⭐ CE-11010 (RR-10, R-11004; docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md D-6) — what an effect made from a HIT struck, written by the
/// spawner from the shared rule's spec (<c>Hrot.Look.Effects.EffectRules</c>): so the effect ENTITY alone (live, and recorded for a replay) says a hit on a unit
/// from a hit on the ground, and on what. ⭐ RECORDED, never saved (VE-R).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.EffectCause)]
[DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
public struct EffectCause
{
    /// <summary>What it struck: <c>Hrot.Look.Effects.EffectSurface</c> as a byte (Ground, Water, Building, Vehicle, Person, Air).</summary>
    public byte Surface;
    /// <summary>The material of a surface it struck: <c>Fdp.Toolkit.Combat.ImpactMaterial</c> as a byte (0 = unknown, or a unit).</summary>
    public byte Material;
    /// <summary>The unit it struck (Null = the ground, a wall, the air).</summary>
    public Entity Target;
    /// <summary>The damage of the hit (0 = unknown).</summary>
    public float Damage;
    /// <summary>The munition's TKB type (0 = unknown).</summary>
    public long Ammo;
    // ⭐ CE-11024 — APPENDED (the layout above is unchanged, so a recording made before keeps its meaning for those fields).
    /// <summary>What a ROUND did at this surface: <c>Fdp.Toolkit.Combat.ImpactKind</c> + 1 — 1 Entry, 2 Exit, 3 Stop; ⭐ 0 = not a round's impact (a
    /// detonation), so a zero never reads as "entered". ⭐ CE-11030 — set on ONE effect of a round's impact (the first the rule makes: its puff), 0 on the others (its mark),
    /// so a renderer that makes one impact per entity with a non-zero <c>Impact</c> sees ONE.</summary>
    public byte Impact;
    /// <summary>Who fired it (Null = unknown, or a hit with no shooter) — from the impact's or the detonation's <c>Shooter</c>.</summary>
    public Entity Shooter;
    // ⭐ CE-11030 — APPENDED again (the layout above is unchanged). The layout hash of this COMPONENT changes: a recording made before only WARNS and skips this component's data (SchemaValidator), it still plays.
    /// <summary>The outward normal of the face an impact struck, as the round's <c>RoundImpactNotification</c> said it (all 0 = not stated); 0 for a detonation's effects.</summary>
    public System.Numerics.Vector3 Normal;
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
    public SimInstant FiredAt;
}

/// <summary>⭐ W3 (docs/DESIGN_Visual_Effects.md §7 VE-P) — where an attached effect sits on its entity, found every frame.</summary>
public enum EffectAnchorKind : byte
{
    /// <summary>The muzzle of weapon <see cref="EffectAnchor.WeaponIndex"/> (<c>Muzzle.TryOf</c>, E4).</summary>
    Muzzle = 0,

    /// <summary>The top centre of the entity's drawn body.</summary>
    Body = 1,
}
