using System.Numerics;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1119 (docs/DESIGN_Map_3D_Mode.md §6w AB-V2; docs/designs/airborne/DESIGN_Airborne_Insertion.md U-1) — the figures of a man coming
/// down a rope or under a parachute, one per <see cref="DescentPose"/>, every one in the shared part order (four leg parts, torso, head, four
/// arm parts, a soldier's weapon) so <see cref="DescentDraw"/> can blend one phase into the next. The feet are the figure's origin while he
/// hangs (rope, canopy); the free-fall arch floats at his middle. ⛔ NEW poses beside the stances — the 11 hit-location poses
/// (<c>PersonBodyParityTests</c>) are untouched.
/// </summary>
public static partial class BlockFigure
{
    /// <summary>⭐ CE-1119 — the figure for <paramref name="pose"/>, with a soldier's weapon where that phase carries it.</summary>
    public static KitPart[] Descending(DescentPose pose, bool soldier) => DescentPoses.Of(pose, soldier);

    /// <summary>⭐ CE-1119 — part-way from one descent figure to the next (<paramref name="t"/> 0..1), every part interpolated (the shared order).</summary>
    public static KitPart[] BlendDescent(KitPart[] from, KitPart[] to, float t) => Lerp(from, to, t);

    /// <summary>⚠ A NESTED class on purpose: C# does not order static initialisers across the files of a partial class, so these poses (which
    /// read <c>S</c> and the shoulders from ShapeKit.cs) would be built from zeros. A nested class initialises on its first use, after
    /// <see cref="BlockFigure"/>'s own fields.</summary>
    private static class DescentPoses
    {
    private static readonly Vector3 SlungTop = new(-0.16f, 0.22f, 1.55f), SlungBottom = new(-0.14f, -0.24f, 0.85f);

    /// <summary>The rope slide: upright, both hands above his head on the rope (which runs 0.17 m in front of him), the left above the right,
    /// legs together and a little forward with the knees bent — the feet clamp the rope; a soldier's rifle slung across his back.</summary>
    private static readonly KitPart[] RopeSlideBody =
    [
        .. Legs(new(0f, 0.10f, 0.90f), new(0.13f, 0.04f, 0.10f), new(0f, -0.10f, 0.90f), new(0.13f, -0.04f, 0.10f), swings: false),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body),
        Head(0.02f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, FaceOn.Front),
        .. Arm(StandShoulderL, new(0.12f, 0.30f, 1.66f), new(0.17f, 0.03f, 1.92f), PartRole.Hull),
        .. Arm(StandShoulderR, new(0.10f, -0.32f, 1.50f), new(0.17f, -0.03f, 1.70f), PartRole.Hull),
    ];

    /// <summary>The exit TUCK (a static-line exit, the moment before the canopy): upright, knees drawn up and together, chin down, both hands
    /// on the reserve at his belly; a soldier's weapon in its case down his left side.</summary>
    private static readonly KitPart[] TuckBody =
    [
        .. Legs(new(0f, 0.10f, 0.86f), new(0.10f, 0.08f, 0.12f), new(0f, -0.10f, 0.86f), new(0.10f, -0.08f, 0.12f), swings: false),
        P(0.02f, 0f, 1.15f, 0.26f, 0.40f, 0.56f, ColourRole.Body),
        Head(0.07f, 0f, 1.55f, 0.21f, 0.20f, 0.24f, FaceOn.Front),
        .. Arm(StandShoulderL, new(0.08f, 0.31f, 1.18f), new(0.22f, 0.06f, 1.05f), PartRole.Hull),
        .. Arm(StandShoulderR, new(0.08f, -0.31f, 1.18f), new(0.22f, -0.06f, 1.05f), PartRole.Hull),
    ];

    /// <summary>The free-fall ARCH: belly to earth, face down, head forward; arms forward and out a little above the shoulders, the lower
    /// legs bent up behind — the stable position. His middle is 0.9 m above the figure's origin.</summary>
    private static readonly KitPart[] ArchBody =
    [
        .. Leg(new(-0.20f, 0.12f, 0.92f), new(-0.78f, 0.24f, 1.24f), true, swings: false, bend: -Vector3.UnitZ),
        .. Leg(new(-0.20f, -0.12f, 0.92f), new(-0.78f, -0.24f, 1.24f), false, swings: false, bend: -Vector3.UnitZ),
        P(0.08f, 0f, 0.94f, 0.60f, 0.42f, 0.24f, ColourRole.Body),
        Head(0.52f, 0f, 0.97f, 0.24f, 0.20f, 0.21f, FaceOn.Down),
        .. Arm(new(0.32f, 0.21f, 0.98f), new(0.36f, 0.50f, 1.04f), new(0.60f, 0.60f, 1.08f), PartRole.Hull),
        .. Arm(new(0.32f, -0.21f, 0.98f), new(0.36f, -0.50f, 1.04f), new(0.60f, -0.60f, 1.08f), PartRole.Hull),
    ];

    /// <summary>UNDER THE CANOPY: hanging in the harness, legs together and a little forward, both hands up on the steering toggles beside
    /// the risers; a soldier's weapon in its case down his left side.</summary>
    private static readonly KitPart[] UnderCanopyBody =
    [
        .. Legs(new(0f, 0.10f, 0.90f), new(0.12f, 0.07f, 0.04f), new(0f, -0.10f, 0.90f), new(0.12f, -0.07f, 0.04f), swings: false),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body),
        Head(0f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, FaceOn.Front),
        .. Arm(StandShoulderL, new(0.03f, 0.36f, 1.72f), new(0.05f, 0.30f, 2.04f), PartRole.Hull),
        .. Arm(StandShoulderR, new(0.03f, -0.36f, 1.72f), new(0.05f, -0.30f, 2.04f), PartRole.Hull),
    ];

    /// <summary>The FLARE: both toggles pulled down to his hips, legs forward, feet and knees together and bent for the landing.</summary>
    private static readonly KitPart[] FlareBody =
    [
        .. Legs(new(0f, 0.10f, 0.88f), new(0.30f, 0.07f, 0.10f), new(0f, -0.10f, 0.88f), new(0.30f, -0.07f, 0.10f), swings: false),
        P(-0.02f, 0f, 1.16f, 0.24f, 0.40f, 0.56f, ColourRole.Body),
        Head(0f, 0f, 1.58f, 0.21f, 0.20f, 0.24f, FaceOn.Front),
        .. Arm(StandShoulderL, new(0.00f, 0.34f, 1.20f), new(0.10f, 0.28f, 1.00f), PartRole.Hull),
        .. Arm(StandShoulderR, new(0.00f, -0.34f, 1.20f), new(0.10f, -0.28f, 1.00f), PartRole.Hull),
    ];

    /// <summary>The end of the parachute landing fall: rolled onto his back, knees drawn up together, hands up at his chest on the risers.</summary>
    private static readonly KitPart[] RolledBody =
    [
        .. Leg(new(-0.17f, 0.12f, 0.10f), new(-0.56f, 0.14f, 0.08f), true, swings: false, bend: Vector3.UnitZ),
        .. Leg(new(-0.17f, -0.12f, 0.10f), new(-0.56f, -0.14f, 0.08f), false, swings: false, bend: Vector3.UnitZ),
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        Head(0.46f, 0f, 0.13f, 0.22f, 0.20f, 0.21f, FaceOn.Up),
        .. Arm(new(0.28f, 0.21f, 0.12f), new(0.20f, 0.28f, 0.32f), new(0.32f, 0.10f, 0.40f), PartRole.Hull),
        .. Arm(new(0.28f, -0.21f, 0.12f), new(0.20f, -0.28f, 0.32f), new(0.32f, -0.10f, 0.40f), PartRole.Hull),
    ];

    /// <summary>LANDED: sitting up on the ground, legs out in front, his hands at his chest on the harness buckles (getting out of it); a
    /// soldier's rifle across his knees.</summary>
    private static readonly KitPart[] SittingOutBody =
    [
        .. Leg(new(0f, 0.11f, 0.12f), new(0.80f, 0.15f, 0.06f), true, swings: false, bend: Vector3.UnitZ),
        .. Leg(new(0f, -0.11f, 0.12f), new(0.80f, -0.15f, 0.06f), false, swings: false, bend: Vector3.UnitZ),
        P(-0.02f, 0f, 0.40f, 0.24f, 0.40f, 0.56f, ColourRole.Body),
        Head(0f, 0f, 0.82f, 0.21f, 0.20f, 0.24f, FaceOn.Front),
        .. Arm(new(0f, 0.26f, 0.65f), new(0.08f, 0.30f, 0.44f), new(0.20f, 0.06f, 0.55f), PartRole.Hull),
        .. Arm(new(0f, -0.26f, 0.65f), new(0.08f, -0.30f, 0.44f), new(0.20f, -0.06f, 0.55f), PartRole.Hull),
    ];

    private static readonly KitPart CasedLeft = Limb(new(0.02f, 0.26f, 1.22f), new(0f, 0.28f, 0.45f), 0.06f, 0.12f, ColourRole.Dark);
    private static readonly KitPart RifleSlung = Limb(SlungTop, SlungBottom, 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart RifleOnBackUp = Limb(new(-0.20f, 0.10f, 1.08f), new(0.50f, -0.05f, 1.08f), 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart RifleBeside = Limb(new(-0.10f, 0.52f, 0.05f), new(0.65f, 0.40f, 0.05f), 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart RifleOnKnees = Limb(new(0.30f, 0.32f, 0.26f), new(0.48f, -0.42f, 0.26f), 0.05f, 0.08f, ColourRole.Metal);

    public static KitPart[] Of(DescentPose pose, bool soldier) => pose switch
    {
        DescentPose.RopeSlide => soldier ? [.. RopeSlideBody, RifleSlung] : RopeSlideBody,
        DescentPose.Tuck => soldier ? [.. TuckBody, CasedLeft] : TuckBody,
        DescentPose.Arch => soldier ? [.. ArchBody, RifleOnBackUp] : ArchBody,
        DescentPose.Flare => soldier ? [.. FlareBody, CasedLeft] : FlareBody,
        DescentPose.Rolled => soldier ? [.. RolledBody, RifleBeside] : RolledBody,
        DescentPose.SittingOut => soldier ? [.. SittingOutBody, RifleOnKnees] : SittingOutBody,
        _ => soldier ? [.. UnderCanopyBody, CasedLeft] : UnderCanopyBody,
    };
    }
}
