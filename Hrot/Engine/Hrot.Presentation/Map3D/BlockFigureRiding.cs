using System.Numerics;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4621 (docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2a, U-2; mockup <c>motorcycle-visuals.svg</c>) — a man ASTRIDE a two-wheeler
/// (<see cref="Fdp.Toolkit.Tkb.Domain.SeatPose.Ride"/>): the seated figure's parts in the SAME order (four leg parts, torso, head, four arm parts,
/// a soldier's rifle — so the outfit, the blood and the rails read it like every figure), the hips the origin, legs apart with the feet on the
/// pegs. The driver's hands are on the bars (his upper body leans forward by <see cref="DriverLean"/> through the motion pose), a pillion's on
/// the man's waist ahead (<see cref="PillionLean"/>); a soldier's rifle is slung across his back. ⭐ <see cref="RidingAiming"/> — a pillion who FIRES:
/// the rifle shouldered, level; the layer turns his upper body to his aim (the motion pose's twist).
/// </summary>
public static partial class BlockFigure
{
    /// <summary>The upper body's forward lean (radians) of a driver over the bars (~17°) and of a pillion (~7°).</summary>
    public const float DriverLean = 0.30f, PillionLean = 0.12f;

    /// <summary>The bars' hands, relative to the driver's hips (m): forward, either side, and a little above the hips; a pillion's on the driver's waist.</summary>
    private static readonly Vector3 BarsHand = new(0.53f, 0.36f, 0.20f), WaistHand = new(0.44f, 0.22f, 0.12f);

    private static KitPart[] RideLegs()
        => Legs(new(0f, 0.11f, 0f), new(0.16f, 0.24f, -0.46f), new(0f, -0.11f, 0f), new(0.16f, -0.24f, -0.46f), swings: false, bend: new Vector3(1f, 0f, 0.2f));

    private static KitPart[] RideBody()
    {
        return
        [
            .. RideLegs(),
            P(-0.02f, 0f, 0.28f, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),
            Head(0f, 0f, 0.70f, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),
        ];
    }

    /// <summary>A hand target (relative to the hips, in the FIGURE's frame after its lean) as the figure-frame point that leans onto it (the lean turns the figure about the hip).</summary>
    private static Vector3 Unlean(Vector3 target, float lean) => Vector3.Transform(target, Matrix4x4.CreateRotationY(-lean));

    private static KitPart[] ArmsOn(Vector3 handL, Vector3 handR)
    {
        var shoulderL = new Vector3(0f, 0.26f, 0.53f);
        var shoulderR = new Vector3(0f, -0.26f, 0.53f);
        var elbowL = (shoulderL + handL) / 2f + new Vector3(0f, 0.07f, -0.10f);
        var elbowR = (shoulderR + handR) / 2f + new Vector3(0f, -0.07f, -0.10f);
        return [.. Arm(shoulderL, elbowL, handL, PartRole.Hull), .. Arm(shoulderR, elbowR, handR, PartRole.Hull)];
    }

    /// <summary>
    /// A rider astride and NOT firing: the driver (<paramref name="driver"/>) with his hands on the bars, or a pillion holding the man ahead; a soldier's
    /// rifle slung on his back. Draw it with <c>Lean = DriverLean / PillionLean</c> and the hips at the figure's origin.
    /// </summary>
    public static KitPart[] Riding(bool driver, bool soldier)
    {
        float lean = driver ? DriverLean : PillionLean;
        var hand = driver ? BarsHand : WaistHand;
        KitPart[] body = [.. RideBody(), .. ArmsOn(Unlean(hand, lean), Unlean(new Vector3(hand.X, -hand.Y, hand.Z), lean))];
        if (!soldier) return body;
        return [.. body, Limb(new(-0.16f, -0.14f, 0.02f), new(-0.18f, 0.16f, 0.52f), 0.05f, 0.08f, ColourRole.Metal)];   // the rifle slung across the back
    }

    /// <summary>
    /// A pillion FIRING: his body upright, the rifle shouldered and level along the figure's +x (the layer twists the upper body to the aim and
    /// pitches it), both hands on it. A rider without a weapon (<paramref name="soldier"/> false) is the plain pillion.
    /// </summary>
    public static KitPart[] RidingAiming(bool soldier)
    {
        if (!soldier) return Riding(false, false);
        var stock = new Vector3(0.10f, -0.14f, 0.50f);
        var muzzle = stock + new Vector3(RifleLength, 0f, 0.02f);
        var gripR = new Vector3(0.20f, -0.14f, 0.46f);
        var gripL = new Vector3(0.52f, -0.06f, 0.46f);
        return [.. RideBody(), .. ArmsOn(gripL, gripR), Limb(stock, muzzle, 0.05f, 0.08f, ColourRole.Metal)];
    }
}
