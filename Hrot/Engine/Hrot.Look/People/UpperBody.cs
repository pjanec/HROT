using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Perception.Look;

namespace Hrot.Look.People;

/// <summary>⭐ CE-11002 (RR-2) — where a person's upper body points this frame, as TARGETS (before any easing): the torso's turn off his heading and its
/// pitch (both clamped), and the head's turn off the torso. 0 everywhere = upright, facing his heading.</summary>
public struct AimTargets
{
    /// <summary>The torso's turn off the body's (or the seat's) forward, counter-clockwise, radians.</summary>
    public float Twist;
    /// <summary>The torso's pitch, up positive, radians (the map draws it as a backward lean).</summary>
    public float Pitch;
    /// <summary>The head's turn off the torso (where his eyes point less the torso's turn), radians.</summary>
    public float Head;
    /// <summary>True when the torso turns to an aim (he aims at something he can place).</summary>
    public bool Aiming;
}

/// <summary>
/// ⭐ CE-11002 (RR-2, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — the AIMED UPPER BODY, from replicated state: the weapon
/// posture's aim point (<see cref="WeaponPostureOf.TryAimPoint"/>), the stance, the eyes (<see cref="LookOf.HeadYaw"/>). Moved verbatim from the map
/// (<c>EntityBodyLayer3D.TryAim</c> / <c>AimInSeat</c> / <c>HeadTurnOf</c>, WL-6, CE-1204, CE-4621), which now forwards here, so the map and an external
/// renderer turn a man the same way. The EASING toward these targets is <see cref="PostureBlend"/> (each consumer owns one). Pure, 0 B.
/// </summary>
public static class UpperBody
{
    /// <summary>The most an aimed upper body turns toward its target (the person body's own limit, <c>HumanGait.MaxAimTwist</c> — the fire arc
    /// reads the same value, CE-3195), and pitches (radians).</summary>
    public const float MaxAimTwist = global::CarKinem.Controllers.HumanGait.MaxAimTwist, MaxAimPitch = 35f * MathF.PI / 180f;

    /// <summary>The most a rider on a firing seat turns his upper body off the seat's forward (radians, ~160°) — the seat's own arc is the limit (PF-1).</summary>
    public const float MaxSeatTwist = 160f * MathF.PI / 180f;

    /// <summary>⭐ WL-6c — how much lower the hips are in the athletic (aimed) stance, metres at a 1.8 m figure.</summary>
    public const float AthleticDrop = 0.10f;

    /// <summary>The shoulder over the feet at a 1.8 m figure: standing, crouched (metres); a seated rider's over his hips.</summary>
    public const float StandingShoulder = 1.41f, CrouchedShoulder = 0.98f, ReferenceHeight = 1.8f, SeatShoulder = 0.53f;

    /// <summary>The shoulder's height over the feet of an AIMED man of <paramref name="height"/> metres in <paramref name="stance"/> (the athletic stance).</summary>
    public static float ShoulderHeight(StanceId stance, float height)
        => (stance == StanceId.Crouched ? CrouchedShoulder : StandingShoulder - AthleticDrop) / ReferenceHeight * height;

    /// <summary>The aim of an AIMED unit relative to its body: turn (counter-clockwise) and pitch (up), both clamped. False when it is not aimed or
    /// aims at nothing it can place (or the point is within 10 cm of straight above / below his shoulder).</summary>
    public static bool TryAim(EntityRepository world, Entity e, in SimTransform tf, float height, StanceId stance, out float twist, out float pitch)
    {
        twist = pitch = 0f;
        if (!WeaponPostureOf.TryAimPoint(world, e, out var at)) return false;   // ⭐ CE-3195 — the ONE reading of what the posture aims at
        var toward = at - (tf.Position + new Vector3(0f, 0f, ShoulderHeight(stance, height)));
        var local = Vector3.Transform(toward, Quaternion.Inverse(tf.Rotation));
        float horizontal = MathF.Sqrt(local.X * local.X + local.Y * local.Y);
        if (horizontal < 0.1f) return false;
        twist = Math.Clamp(MathF.Atan2(local.Y, local.X), -MaxAimTwist, MaxAimTwist);
        pitch = Math.Clamp(MathF.Atan2(local.Z, horizontal), -MaxAimPitch, MaxAimPitch);
        return true;
    }

    /// <summary>⭐ CE-4621 — a rider's turn (counter-clockwise) and pitch (up) from the shoulder (<see cref="SeatShoulder"/> over the hips) to
    /// <paramref name="local"/> (an aim point in the seat's frame), the turn limited to <see cref="MaxSeatTwist"/>. False under 10 cm.</summary>
    public static bool AimInSeat(Vector3 local, out float twist, out float pitch)
    {
        twist = pitch = 0f;
        var d = local - new Vector3(0f, 0f, SeatShoulder);
        float horizontal = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (horizontal < 0.1f) return false;
        twist = Math.Clamp(MathF.Atan2(d.Y, d.X), -MaxSeatTwist, MaxSeatTwist);
        pitch = Math.Clamp(MathF.Atan2(d.Z, horizontal), -MaxAimPitch, MaxAimPitch);
        return true;
    }

    /// <summary>
    /// ⭐ CE-1204 (LK-5, R-1017) — the head's turn off the torso: where his eyes point off his body (<see cref="LookOf.HeadYaw"/>, replicated) less the
    /// torso's drawn <paramref name="drawnTwist"/>, within his neck (<see cref="LookOf.HeadLimit"/>). 0 with no look. ⚠ Pass the torso's DRAWN (eased) twist
    /// — the map does — so the head holds still while the torso catches up.
    /// </summary>
    public static float HeadTurnOf(EntityRepository world, Entity e, float drawnTwist)
    {
        float eyes = LookOf.HeadYaw(world, e);
        if (eyes == 0f && drawnTwist == 0f) return 0f;
        float neck = LookOf.HeadLimit(world, e);
        return Math.Clamp(LookOf.Wrap(eyes - drawnTwist), -neck, neck);
    }

    /// <summary>
    /// ⭐ CE-11002 (RR-2) — a standing / crouching person's upper-body TARGETS, exactly the map's rule (<c>EntityBodyLayer3D.FigureMotion</c>): prone or
    /// destroyed ⇒ all zero; not <paramref name="armed"/> (<see cref="PersonFacts.Soldier"/>) ⇒ no torso turn, the head to his eyes; armed ⇒ the clamped aim
    /// (<see cref="TryAim"/>, none when he aims at nothing) and the head off THAT twist. A renderer eases them with its own <see cref="PostureBlend"/>
    /// (<see cref="PostureBlend.Aim"/>, then <see cref="HeadTurnOf"/> over the eased twist and <see cref="PostureBlend.Head"/>) — the map's order.
    /// </summary>
    public static void Targets(EntityRepository world, Entity e, float height, bool armed, out AimTargets t)
    {
        t = default;
        if (!world.IsAlive(e) || !world.HasComponent<SimTransform>(e)) return;
        var stance = LogicalStance.Of(world, e);
        if (stance == StanceId.Prone || CombatLife.IsDestroyed(world, e)) return;
        if (!armed) { t.Head = HeadTurnOf(world, e, 0f); return; }
        ref readonly var tf = ref world.GetComponentRO<SimTransform>(e);
        t.Aiming = TryAim(world, e, in tf, height, stance, out t.Twist, out t.Pitch);
        t.Head = HeadTurnOf(world, e, t.Twist);
    }
}
