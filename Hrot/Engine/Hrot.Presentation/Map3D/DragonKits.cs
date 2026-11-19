using System.Numerics;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4634</c> D-4 (docs/designs/fun/DESIGN_Dragon.md §8, DR-11; mockup <c>docs/UX/dragon-look.svg</c>) — the DRAGON's two shape kits, built in METRES on the
/// TKB's own numbers (type 411, <c>FunTkbCatalog.DefineDragon</c>: a centre-of-gravity body 20 × 26 × 6 m whose feet are 3 m below the CG; the NECK on turret 0 —
/// its pivot 5 m ahead of the CG at shoulder height, the head's trunnion 4 m further, the MOUTH 1.5 m beyond it, where the breath leaves), so the drawn mouth IS the
/// flame jet's origin. Metres here are from the FEET (the CG at z 3), x forward, y left.
/// <para>The FLYING kit spreads its wings (<see cref="PartRole.WingLeft"/> / <see cref="PartRole.WingRight"/>, beaten by <see cref="DragonPoses"/>) and swings its legs
/// (the quadruped's roles, tucked in flight); the LANDED kit folds the wings along the flanks (a rotation about the body's long axis cannot fold a 12 m wing — it
/// would stab the ground — so the folded wing is a second kit, the perched bird's precedent). The neck is <see cref="PartRole.Neck"/> (it yaws with turret 0 only,
/// as the simulation's neck does); the head, jaw and horns are <see cref="PartRole.Gun"/> boxes (they yaw AND nod about the trunnion — the sim's mouth).</para>
/// </summary>
public static class DragonKits
{
    /// <summary>The TKB's box: 20 m nose to tail, 26 m wingspan, 6 m tall (the kit's fractions are of it).</summary>
    public static readonly Vector3 Size = new(20f, 26f, 6f);

    /// <summary>The CG above the feet (m) — the TKB's legs reach 3 m below it.</summary>
    public const float CgZ = 3f;

    /// <summary>Turret 0 in kit metres: the neck's base (pivot) and the head's trunnion — the TKB's (5, 1.5) and +4 m, from the CG.</summary>
    public static readonly Vector3 NeckBase = new(5f, 0f, CgZ + 1.5f), Trunnion = new(9f, 0f, CgZ + 1.5f);

    /// <summary>The mouth (the breath's muzzle: the trunnion + 1.5 m).</summary>
    public static readonly Vector3 Mouth = new(10.5f, 0f, CgZ + 1.5f);

    /// <summary>A wing's root (the left one; the right mirrors y) and its span (m).</summary>
    public static readonly Vector3 WingRoot = new(1.5f, 1.4f, 4.7f);
    public const float WingSpan = 11.6f, WingChord = 4.6f;

    /// <summary>The four legs (front left, front right, hind left, hind right — the quadruped's order) at the TKB's contact points; their hips (z).</summary>
    public static readonly Vector2[] Legs = [new(2.5f, 1.6f), new(2.5f, -1.6f), new(-2.5f, 1.8f), new(-2.5f, -1.8f)];
    public const float HipZ = 2.2f;

    /// <summary>Spread wings, legs swinging (in flight; dead, lying wings out).</summary>
    public static ShapeKit Flying { get; } = Build(spread: true);

    /// <summary>Wings folded along the flanks, legs planted (landed).</summary>
    public static ShapeKit Landed { get; } = Build(spread: false);

    /// <summary>The kit for the dragon's state: folded when it stands on its legs (the flight state's <c>Airborne</c> = 0 and alive).</summary>
    public static ShapeKit For(bool airborne, bool dead) => airborne || dead ? Flying : Landed;

    private static ShapeKit Build(bool spread)
    {
        var k = new MetreKit();
        // the body: the barrel, the belly, the shoulders' hump (the saddle sits on it)
        k.Add(0f, 0f, 3.2f, 8.4f, 3.0f, 2.6f, ColourRole.Body);
        k.Add(0f, 0f, 1.95f, 7.6f, 2.6f, 0.5f, ColourRole.Light);
        k.Add(2.8f, 0f, 4.1f, 3.4f, 2.8f, 1.9f, ColourRole.Body);
        k.Add(2.6f, 0f, 5.12f, 2.4f, 1.3f, 0.25f, ColourRole.Dark);                                    // the saddle
        k.Add(-1.2f, 0f, 4.7f, 6.5f, 0.25f, 0.45f, ColourRole.Dark);                                   // the ridge along the back
        // the neck: from the shoulders to the trunnion, yawing with turret 0 about its base
        var neckFrom = new Vector3(4.2f, 0f, 4.6f);
        var neck = Trunnion - neckFrom;
        float neckPitch = -MathF.Atan2(neck.Z, neck.X) * 180f / MathF.PI;
        var nc = (neckFrom + Trunnion) / 2f;
        k.Add(nc.X, 0f, nc.Z, neck.Length() + 0.4f, 1.15f, 1.15f, ColourRole.Body, pitch: neckPitch, role: PartRole.Neck);
        k.Add(nc.X, 0f, nc.Z + 0.62f, neck.Length(), 0.2f, 0.3f, ColourRole.Dark, pitch: neckPitch, role: PartRole.Neck);   // the neck's ridge
        // the head (on the trunnion, to the mouth), the jaw, two horns sweeping back — they NOD with the breath's elevation
        k.Add(Trunnion.X + 0.6f, 0f, Trunnion.Z + 0.08f, 1.9f, 1.05f, 0.95f, ColourRole.Body, role: PartRole.Gun);   // its snout ends AT the mouth (the TKB's muzzle)
        k.Add(Trunnion.X + 0.7f, 0f, Trunnion.Z - 0.42f, 1.6f, 0.85f, 0.28f, ColourRole.Dark, role: PartRole.Gun);    // the jaw, just short of it
        foreach (float sy in new[] { 1f, -1f })
            k.Add(Trunnion.X - 0.25f, sy * 0.35f, Trunnion.Z + 0.75f, 1.0f, 0.16f, 0.16f, ColourRole.Bone, pitch: 35f, role: PartRole.Gun);
        // the tail: two tapering boxes drooping to the rear, and its spade
        k.Add(-6.4f, 0f, 3.3f, 5.4f, 1.3f, 1.1f, ColourRole.Body, pitch: -6f);
        k.Add(-10.6f, 0f, 2.75f, 4.2f, 0.7f, 0.6f, ColourRole.Body, pitch: -8f);
        k.Add(-13.0f, 0f, 2.5f, 1.2f, 0.12f, 0.9f, ColourRole.Dark);
        // the legs: a leg and a clawed foot each, swinging about the hip (QuadFrontLeft, QuadFrontRight, QuadHindLeft, QuadHindRight in that order)
        for (int i = 0; i < 4; i++)
        {
            var role = (PartRole)((int)PartRole.QuadFrontLeft + i);
            var pivot = new Vector3(Legs[i].X, Legs[i].Y, HipZ);
            k.Add(Legs[i].X, Legs[i].Y, 0.3f + (HipZ - 0.3f) / 2f, 0.85f, 0.85f, HipZ - 0.3f, ColourRole.Body, role: role, pivot: pivot);
            k.Add(Legs[i].X + 0.3f, Legs[i].Y, 0.15f, 1.2f, 0.95f, 0.3f, ColourRole.Dark, role: role, pivot: pivot);
        }
        // the wings
        foreach (float sy in new[] { 1f, -1f })
        {
            var root = new Vector3(WingRoot.X, sy * WingRoot.Y, WingRoot.Z);
            if (spread)
            {
                var role = sy > 0 ? PartRole.WingLeft : PartRole.WingRight;
                k.Add(root.X - 0.8f, sy * (WingRoot.Y + WingSpan / 2f), root.Z, WingChord, WingSpan, 0.15f, ColourRole.Light, role: role, pivot: root);   // the membrane
                k.Add(root.X + 1.3f, sy * (WingRoot.Y + WingSpan / 2f), root.Z + 0.1f, 0.35f, WingSpan, 0.35f, ColourRole.Dark, role: role, pivot: root);   // its leading bone
            }
            else
            {
                k.Add(-1.6f, sy * 1.65f, 4.0f, 7.2f, 0.2f, 1.7f, ColourRole.Light, pitch: 8f);   // folded back along the flank
                k.Add(1.0f, sy * 1.7f, 4.75f, 2.2f, 0.35f, 0.35f, ColourRole.Dark);              // the folded wing's knuckle at the shoulder
            }
        }
        // the kit's own pivots (no TKB): the neck's base and the head's trunnion — KitFromTkb puts them at the same place from turret 0
        return k.Build(out _, out _, 0f, sizeOverride: Size, shiftOverride: 0f)
            with { Pivots = new KitPivots(new Vector2(NeckBase.X / Size.X, 0f), new Vector2(Trunnion.X / Size.X, Trunnion.Z / Size.Z)) };
    }
}

/// <summary>
/// ⭐ <c>CE-4634</c> D-4 (DESIGN_Dragon §8.2 W, L) — the dragon's pose curves, pure (the look and the sound read the same): a slow, heavy wing BEAT (≈ 1 Hz, ±33°) while
/// it climbs or flies slowly, a GLIDE (+6°, still) at speed, and the legs tucked in flight. The beat's phase runs on SIM time, so every host draws the same stroke and a
/// paused sim holds it. 0 B.
/// </summary>
public static class DragonPoses
{
    public const float BeatHz = 1f, BeatAmplitude = 0.58f, GlideAngle = 0.1f;
    /// <summary>Slower than this (m/s) — or climbing faster than <see cref="ClimbBeats"/> — it beats; faster, it glides.</summary>
    public const float GlideFrom = 10f, ClimbBeats = 1f;
    /// <summary>How long (s) the beat, the glide and the legs blend into each other.</summary>
    public const float BlendSeconds = 1f;
    /// <summary>One walking stride cycle of the landed dragon (m) — its legs are ~2 m long.</summary>
    public const float StrideMetres = 5f;

    /// <summary>1 when it should beat its wings, 0 when it should glide (airborne).</summary>
    public static float BeatTarget(float speed, float climb) => climb > ClimbBeats || speed < GlideFrom ? 1f : 0f;

    /// <summary>The wing angle (rad, + = the tips up) for a beat share <paramref name="beat"/> (0 glide … 1 beating) at <paramref name="simTime"/>.</summary>
    public static float Wing(double simTime, float beat)
    {
        float stroke = BeatAmplitude * MathF.Sin((float)(simTime * BeatHz % 1.0) * MathF.PI * 2f);
        return GlideAngle + beat * (stroke - GlideAngle * 0.5f);
    }

    /// <summary>⭐ How far (m) the body rises and sinks with a full beat — a bird's flight bobs, a helicopter's does not (user, `2026-11-19`). A DRAWING only: the sim's altitude is steady.</summary>
    public const float HeaveM = 0.4f;

    /// <summary>⭐ The body's visual rise above its sim altitude (m) for a beat share <paramref name="beat"/> at <paramref name="simTime"/>: OPPOSITE the wings — they sweep down and lift
    /// it, they rise and it sinks (−sin of <see cref="Wing"/>'s phase), so the bob is in step with the drawn stroke. 0 in a glide.</summary>
    public static float Heave(double simTime, float beat) => -HeaveM * beat * MathF.Sin((float)(simTime * BeatHz % 1.0) * MathF.PI * 2f);

    /// <summary>True while the wings come DOWN (the whoosh's stroke) at <paramref name="simTime"/> — the derivative of <see cref="Wing"/>'s sine is negative.</summary>
    public static bool DownStroke(double simTime) => MathF.Cos((float)(simTime * BeatHz % 1.0) * MathF.PI * 2f) < 0f;

    /// <summary>Eases <paramref name="current"/> toward <paramref name="target"/> over <see cref="BlendSeconds"/> for a step of <paramref name="dt"/> s.</summary>
    public static float Ease(float current, float target, float dt) => current + (target - current) * (1f - MathF.Exp(-dt / (BlendSeconds / 3f)));
}
