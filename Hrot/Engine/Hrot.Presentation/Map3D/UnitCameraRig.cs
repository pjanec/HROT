using System.Numerics;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md §4, UC-1, R-1032) — ONE camera that follows a unit, from far away to its eyes: a distance
/// continuum. Far, the camera orbits the unit's centre and looks at it; nearer, the pivot slides to his shoulder, so the view looks where
/// the camera points (over the shoulder); at zero it is his eye. Pure — the station (possessed) and the follow camera (watching) ask it,
/// and the rails run it. Distances are a person's; a vehicle's are <see cref="ScaleOf"/> times longer.
/// </summary>
public static class UnitCameraRig
{
    /// <summary>A person's distances (m): beyond <see cref="Far"/> the camera orbits his centre; nearer than <see cref="Near"/> it is over
    /// his shoulder; between, the pivot blends.</summary>
    public const float Far = 12f, Near = 3f;

    /// <summary>The over-the-shoulder offset at full distance (a person): to the side and up (m), reached at <see cref="SideFullAt"/>.</summary>
    public const float Side = 0.55f, Lift = 0.2f, SideFullAt = 1.5f;

    /// <summary>The default distances (m) when the shoulder view is switched on: a person, a vehicle (a chase camera), a flyer.</summary>
    public const float PersonDefault = 2.2f, VehicleDefault = 9f, FlyerDefault = 18f;

    /// <summary>A chase camera's rise per metre back (a vehicle 2.5 m at 9 m, a flyer 4.5 m at 18 m).</summary>
    public const float VehicleLiftPerMetre = 2.5f / 9f, FlyerLiftPerMetre = 4.5f / 18f;

    /// <summary>How far a camera may go back while playing (m): a person a short chase, a vehicle and a flyer their chase and some.</summary>
    public static float MaxPlayDistance(ControlScheme scheme) => scheme switch
    {
        ControlScheme.Person => 5f,
        ControlScheme.Vehicle => 24f,
        _ => 45f,
    };

    /// <summary>The default distance for <paramref name="scheme"/> when the shoulder view is on.</summary>
    public static float DefaultDistance(ControlScheme scheme) => scheme switch
    {
        ControlScheme.Person => PersonDefault,
        ControlScheme.Vehicle => VehicleDefault,
        _ => FlyerDefault,
    };

    /// <summary>How much longer a kind's distances are than a person's.</summary>
    public static float ScaleOf(ControlScheme scheme) => scheme switch
    {
        ControlScheme.Person => 1f,
        ControlScheme.Vehicle => 4f,
        _ => 8f,
    };

    /// <summary>0 far away (orbit the centre) … 1 near (over the shoulder), smooth between <see cref="Far"/> and <see cref="Near"/>.</summary>
    public static float NearBlend(float distance, ControlScheme scheme)
    {
        float s = ScaleOf(scheme);
        float t = Math.Clamp((Far * s - distance) / ((Far - Near) * s), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The point the camera hangs from at <paramref name="distance"/>: near, his eye moved to the <paramref name="side"/> shoulder
    /// (+1 right, −1 left) and up (a person), or up only (a vehicle's chase); far, his <paramref name="centre"/>; blended between.
    /// <paramref name="heading"/> is the level look (its right is the shoulder's side).
    /// </summary>
    public static Vector3 Pivot(Vector3 centre, Vector3 eye, Vector3 heading, float distance, ControlScheme scheme, int side)
    {
        var level = Level(heading);
        var right = new Vector3(level.Y, -level.X, 0f);   // Z up, Y left: the right of a heading
        Vector3 near = scheme switch
        {
            ControlScheme.Person => eye + right * (Side * side * Math.Clamp(distance / SideFullAt, 0f, 1f))
                                        + Vector3.UnitZ * (Lift * Math.Clamp(distance / SideFullAt, 0f, 1f)),
            ControlScheme.Vehicle => eye + Vector3.UnitZ * (VehicleLiftPerMetre * distance),
            _ => eye + Vector3.UnitZ * (FlyerLiftPerMetre * distance),
        };
        return Vector3.Lerp(centre, near, NearBlend(distance, scheme));
    }

    /// <summary>
    /// Where the camera stands: <paramref name="distance"/> behind <paramref name="pivot"/> along <paramref name="look"/>. Playing
    /// (<paramref name="orbitPitch"/> false) it goes back along the LEVEL look, so looking down does not swing the camera up; watching
    /// (true) it orbits — looking down lifts it.
    /// </summary>
    public static Vector3 Camera(Vector3 pivot, Vector3 look, float distance, bool orbitPitch)
        => pivot - (orbitPitch ? SafeNormal(look) : Level(look)) * distance;

    /// <summary>
    /// ⭐ UC-4 — the spring arm: the camera is pulled in front of the first thing between the pivot and where it wants to stand
    /// (<paramref name="firstHit"/> returns that crossing's fraction along the line, 0 = the pivot, or null when nothing is there), by
    /// <paramref name="margin"/> metres. Nothing in the way ⇒ <paramref name="wanted"/>.
    /// </summary>
    public static Vector3 SpringArm(Vector3 pivot, Vector3 wanted, System.Func<Vector3, Vector3, float?>? firstHit, float margin = ArmMargin)
    {
        var arm = wanted - pivot;
        float length = arm.Length();
        if (firstHit is null || length < 1e-3f || firstHit(pivot, wanted) is not { } t) return wanted;
        float reach = Math.Clamp(t * length - margin, 0f, length);
        return pivot + arm / length * reach;
    }

    /// <summary>How far in front of a wall the spring arm stops the camera (m).</summary>
    public const float ArmMargin = 0.25f;

    /// <summary>How fast the arm lengthens again after it was pulled in (m/s) — in at once, out slowly (UC-4).</summary>
    public const float ArmOutRate = 4f;

    /// <summary>The arm length this frame: shorter at once (a wall came between), longer by at most <see cref="ArmOutRate"/> × dt.</summary>
    public static float EaseArm(float current, float wanted, float dt)
        => wanted <= current ? wanted : MathF.Min(wanted, current + ArmOutRate * dt);

    private static Vector3 Level(Vector3 v)
    {
        var level = new Vector3(v.X, v.Y, 0f);
        return level.LengthSquared() > 1e-6f ? Vector3.Normalize(level) : Vector3.UnitX;
    }

    private static Vector3 SafeNormal(Vector3 v) => v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitX;
}
