using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Fdp.Toolkit.Combat.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>The primitive a kit part is drawn with (one shared unit mesh each; a cone's apex points along its axis).</summary>
public enum PartShape : byte { Box, Cylinder, Cone }

/// <summary>Which body axis a cylinder or cone runs along (x = forward, y = left, z = up).</summary>
public enum PartAxis : byte { X, Y, Z }

/// <summary>Which colour a part takes: the entity's colour, or a fixed material shade.</summary>
public enum ColourRole : byte { Body, Dark, Glass, Metal }

/// <summary>
/// ⭐ CE-1033 — which articulated assembly a part belongs to. A <see cref="Turret"/> part turns with the turret's azimuth about the
/// kit's turret pivot; a <see cref="Gun"/> part also lifts with the gun's elevation about the trunnion. Hull parts never move.
/// A <see cref="Gear"/> part is the kit's GENERIC landing gear — replaced by the gear at the TKB's contact points when the type has
/// a <c>Body.Geometry</c> (CE-1041).
/// </summary>
public enum PartRole : byte
{
    Hull, Turret, Gun, Gear,
    /// <summary>⭐ CE-1033 S4 — a main-rotor blade: spins about its centre's vertical axis (<see cref="MotionPose.Rotor"/>).</summary>
    Rotor,
    /// <summary>⭐ CE-1033 S4 — a wheel's spoke: rolls about its centre's sideways axis (<see cref="MotionPose.WheelRoll"/>), so a
    /// wheel — a round disc that looks the same at any angle — shows that it turns.</summary>
    WheelSpoke,
    /// <summary>⭐ CE-1033 S4 — a block figure's limbs: swing about their TOP (hip, shoulder); left and right in opposite phase,
    /// each arm against its leg (<see cref="MotionPose.Swing"/>).</summary>
    LegLeft, LegRight, ArmLeft, ArmRight,
}

/// <summary>
/// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §6, "things come alive") — the moving parts' pose for one frame: the main-rotor
/// angle, the wheels' roll angle (radians) and the limb swing (radians, + = the left leg forward). Default: all still.
/// </summary>
/// <para>⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13e) — and the block figure's UPPER BODY (<see cref="KitPart.Upper"/> parts): <paramref name="Lean"/>
/// tilts it forward about the hip (radians; a run, and an aim pitched down), <paramref name="Twist"/> turns it about the vertical
/// (radians, counter-clockwise; an aim to the side), about a hip at <paramref name="HipZ"/> of the figure's height.</para>
public readonly record struct MotionPose(float Rotor, float WheelRoll, float Swing, float Lean = 0f, float Twist = 0f, float HipZ = 0.5f)
{
    public static readonly MotionPose Still = default;
}

/// <summary>
/// ⭐ CE-1033 — the pose of a kit's articulated parts: turret azimuth RELATIVE TO THE HULL (radians, counter-clockwise seen from
/// above, 0 = forward) and gun elevation RELATIVE TO THE TURRET (radians, positive up). Default: everything forward and level.
/// </summary>
public readonly record struct ArticulationPose(float TurretAzimuth, float GunElevation)
{
    public static readonly ArticulationPose Neutral = default;
}

/// <summary>Where a kit's articulated parts turn: the turret's vertical axis (x, y) and the gun's trunnion (x, z), as fractions of
/// the type's size like every part.</summary>
public readonly record struct KitPivots(Vector2 Turret, Vector2 GunTrunnionXZ);

/// <summary>
/// One part of a shape kit. <paramref name="Centre"/> and <paramref name="Extent"/> are FRACTIONS of the type's length (x,
/// forward), width or span (y, left) and height (z, up), measured from the footprint centre on the ground. A cylinder's or cone's
/// length is its extent along <paramref name="Axis"/>; its diameter is the height fraction (or the length fraction for an upright
/// one). <paramref name="PitchDegrees"/> tilts the part nose-down about the body's y axis (a glacis, a swept fin);
/// <paramref name="YawDegrees"/> turns it about the up axis (a swept wing).
/// </summary>
/// <para>⭐ WL-6 — <paramref name="Upper"/> marks a block figure's upper-body part (torso, head, arms, rifle), which leans and twists
/// with <see cref="MotionPose.Lean"/> / <see cref="MotionPose.Twist"/>; <paramref name="SwingPivot"/> (a fraction, like the centre) is
/// where a limb swings from when it is not its own top — a forearm swings from the shoulder with its upper arm.</para>
public readonly record struct KitPart(
    PartShape Shape, Vector3 Centre, Vector3 Extent, ColourRole Colour, PartAxis Axis = PartAxis.Z, float PitchDegrees = 0f,
    float YawDegrees = 0f, PartRole Role = PartRole.Hull, bool Upper = false, Vector3? SwingPivot = null)
{
    /// <summary>The part's transform in BODY space (metres, before the entity's own rotation and position), unarticulated.</summary>
    public Matrix4x4 BodyTransform(Vector3 size) => BodyTransform(size, default, ArticulationPose.Neutral);

    /// <summary>The part's transform in BODY space, with the turret and gun posed by <paramref name="pose"/> about <paramref name="pivots"/>.</summary>
    public Matrix4x4 BodyTransform(Vector3 size, KitPivots pivots, ArticulationPose pose) => BodyTransform(size, pivots, pose, MotionPose.Still);

    /// <summary>The part's transform in BODY space, articulated by <paramref name="pose"/> and moving by <paramref name="motion"/>.</summary>
    public Matrix4x4 BodyTransform(Vector3 size, KitPivots pivots, ArticulationPose pose, MotionPose motion)
    {
        var centre = Centre * size;
        Matrix4x4 local;
        if (Shape == PartShape.Box)
        {
            local = Matrix4x4.CreateScale(Extent * size);
        }
        else
        {
            // The unit cylinder / cone runs along +Z from 0 to 1 with radius 0.5: centre it, size it, lay it along its axis.
            float length, diameter;
            Matrix4x4 lay;
            switch (Axis)
            {
                case PartAxis.X: length = Extent.X * size.X; diameter = Extent.Z * size.Z; lay = Matrix4x4.CreateRotationY(MathF.PI / 2f); break;
                case PartAxis.Y: length = Extent.Y * size.Y; diameter = Extent.Z * size.Z; lay = Matrix4x4.CreateRotationX(-MathF.PI / 2f); break;
                default: length = Extent.Z * size.Z; diameter = Extent.X * size.X; lay = Matrix4x4.Identity; break;
            }
            local = Matrix4x4.CreateTranslation(0, 0, -0.5f) * Matrix4x4.CreateScale(diameter, diameter, length) * lay;
        }
        if (PitchDegrees != 0f) local *= Matrix4x4.CreateRotationY(PitchDegrees * MathF.PI / 180f);
        if (YawDegrees != 0f) local *= Matrix4x4.CreateRotationZ(YawDegrees * MathF.PI / 180f);
        var m = local * Matrix4x4.CreateTranslation(centre);

        // ⭐ CE-1033 S4 — the moving parts, each about its own pivot.
        switch (Role)
        {
            case PartRole.Rotor when motion.Rotor != 0f:
            {
                var axis = new Vector3(centre.X, centre.Y, 0f);
                m *= Matrix4x4.CreateTranslation(-axis) * Matrix4x4.CreateRotationZ(motion.Rotor) * Matrix4x4.CreateTranslation(axis);
                break;
            }
            case PartRole.WheelSpoke when motion.WheelRoll != 0f:
                // rolling forward turns the wheel's top forward: about +y, the top (+z) toward +x
                m *= Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(motion.WheelRoll) * Matrix4x4.CreateTranslation(centre);
                break;
            case PartRole.LegLeft or PartRole.LegRight or PartRole.ArmLeft or PartRole.ArmRight when motion.Swing != 0f:
            {
                // + swing = the left leg forward (its foot toward +x), so the right leg and the left arm go back
                float sign = Role is PartRole.LegLeft or PartRole.ArmRight ? 1f : -1f;
                var pivot = SwingPivot is { } sp ? sp * size : centre + new Vector3(0f, 0f, Extent.Z * size.Z / 2f);
                m *= Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationY(-sign * motion.Swing) * Matrix4x4.CreateTranslation(pivot);
                break;
            }
        }

        // ⭐ WL-6 (W-13e) — the upper body leans (a run, an aim pitched down) and twists (an aim to the side) about the hip
        if (Upper && (motion.Lean != 0f || motion.Twist != 0f))
        {
            var hip = new Vector3(0f, 0f, motion.HipZ * size.Z);
            m *= Matrix4x4.CreateTranslation(-hip) * Matrix4x4.CreateRotationY(motion.Lean) * Matrix4x4.CreateRotationZ(motion.Twist)
               * Matrix4x4.CreateTranslation(hip);
        }

        if (Role == PartRole.Gun && pose.GunElevation != 0f)
        {
            // Elevation lifts the muzzle: about the trunnion's y axis, nose UP (CreateRotationY turns +X toward −Z for a negative angle).
            var trunnion = new Vector3(pivots.GunTrunnionXZ.X * size.X, 0f, pivots.GunTrunnionXZ.Y * size.Z);
            m *= Matrix4x4.CreateTranslation(-trunnion) * Matrix4x4.CreateRotationY(-pose.GunElevation) * Matrix4x4.CreateTranslation(trunnion);
        }
        if (Role is PartRole.Turret or PartRole.Gun && pose.TurretAzimuth != 0f)
        {
            var axis = new Vector3(pivots.Turret.X * size.X, pivots.Turret.Y * size.Y, 0f);
            m *= Matrix4x4.CreateTranslation(-axis) * Matrix4x4.CreateRotationZ(pose.TurretAzimuth) * Matrix4x4.CreateTranslation(axis);
        }
        return m;
    }
}

/// <summary>A family's kit: its parts and where its articulated parts turn.</summary>
public sealed record ShapeKit(KitPart[] Parts, KitPivots Pivots)
{
    /// <summary>True when the kit has a turret or gun (computed once — the layer asks per entity per frame).</summary>
    public bool IsArticulated { get; } = Parts.Any(p => p.Role is PartRole.Turret or PartRole.Gun);
}

/// <summary>
/// ⭐ CE-1033 — what each kind of entity looks like in 3-D (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7): a short list of parts as
/// fractions of the type's size, chosen by <see cref="VisualFamily"/>. The tank and AFV turret and gun are ARTICULATED parts
/// (§3.11); the rotor spins, the wheels roll and the limbs swing (S4, <see cref="MotionPose"/>). For aircraft the y extent is the SPAN (wings, main rotor), so parts may
/// reach the span's edge.
/// </summary>
public static class ShapeKits
{
    private static KitPart Box(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, float pitch = 0f,
                               float yaw = 0f, PartRole role = PartRole.Hull)
        => new(PartShape.Box, new Vector3(cx, cy, cz), new Vector3(ex, ey, ez), c, PartAxis.Z, pitch, yaw, role);

    private static KitPart Cyl(PartAxis axis, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c,
                               PartRole role = PartRole.Hull)
        => new(PartShape.Cylinder, new Vector3(cx, cy, cz), new Vector3(ex, ey, ez), c, axis, Role: role);

    /// <summary>A wheel's spoke: a thin bar across the wheel at (cx, cy, cz), a little wider than the wheel (<paramref name="width"/>)
    /// so it shows on its face. Its length is 0.13 of the length — about the wheel's diameter on the wheeled kits' proportions.</summary>
    private static KitPart Spoke(float cx, float cy, float cz, float width)
        => new(PartShape.Box, new Vector3(cx, cy, cz), new Vector3(0.13f, width * 1.06f, 0.03f), ColourRole.Metal, Role: PartRole.WheelSpoke);

    /// <summary>The wheels' radius on a wheeled kit, as a fraction of the type's height (the wheel cylinders' diameter is 0.42).</summary>
    public const float WheelRadiusOfHeight = 0.21f;

    private static KitPart Cone(PartAxis axis, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c)
        => new(PartShape.Cone, new Vector3(cx, cy, cz), new Vector3(ex, ey, ez), c, axis);

    public static readonly ShapeKit Tank = new(new[]
    {
        Box(0f, 0.38f, 0.19f, 1.0f, 0.24f, 0.38f, ColourRole.Dark),                          // track, left
        Box(0f, -0.38f, 0.19f, 1.0f, 0.24f, 0.38f, ColourRole.Dark),                         // track, right
        Box(0f, 0f, 0.44f, 0.92f, 0.56f, 0.36f, ColourRole.Body),                            // hull
        Box(-0.07f, 0f, 0.76f, 0.44f, 0.52f, 0.30f, ColourRole.Body, role: PartRole.Turret), // turret, set back
        Cyl(PartAxis.X, 0.42f, 0f, 0.78f, 0.56f, 0f, 0.07f, ColourRole.Metal, PartRole.Gun), // barrel, forward
    }, new KitPivots(new Vector2(-0.07f, 0f), new Vector2(0.14f, 0.78f)));

    public static readonly ShapeKit Afv = new(new[]
    {
        Box(0f, 0.38f, 0.17f, 1.0f, 0.24f, 0.34f, ColourRole.Dark),
        Box(0f, -0.38f, 0.17f, 1.0f, 0.24f, 0.34f, ColourRole.Dark),
        Box(-0.05f, 0f, 0.46f, 0.84f, 0.58f, 0.40f, ColourRole.Body),                        // hull
        Box(0.39f, 0f, 0.44f, 0.20f, 0.58f, 0.30f, ColourRole.Body, 30f),                    // sloped front wedge
        Box(-0.05f, 0f, 0.74f, 0.30f, 0.36f, 0.18f, ColourRole.Body, role: PartRole.Turret), // small turret
        Cyl(PartAxis.X, 0.24f, 0f, 0.76f, 0.40f, 0f, 0.04f, ColourRole.Metal, PartRole.Gun), // thin barrel
    }, new KitPivots(new Vector2(-0.05f, 0f), new Vector2(0.10f, 0.76f)));

    public static readonly ShapeKit WheeledCar = new(new[]
    {
        Box(0f, 0f, 0.38f, 1.0f, 1.0f, 0.40f, ColourRole.Body),           // lower body
        Box(-0.05f, 0f, 0.76f, 0.52f, 0.90f, 0.34f, ColourRole.Glass),    // cabin
        Cyl(PartAxis.Y, 0.32f, 0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, 0.32f, -0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.32f, 0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.32f, -0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Spoke(0.32f, 0.45f, 0.21f, 0.12f), Spoke(0.32f, -0.45f, 0.21f, 0.12f),     // ⭐ S4 — spokes, so the wheels show they roll
        Spoke(-0.32f, 0.45f, 0.21f, 0.12f), Spoke(-0.32f, -0.45f, 0.21f, 0.12f),
    }, default);

    public static readonly ShapeKit WheeledUtility = new(new[]
    {
        Box(0f, 0f, 0.42f, 1.0f, 1.0f, 0.38f, ColourRole.Body),           // wide low body
        Box(0.06f, 0f, 0.78f, 0.36f, 0.92f, 0.34f, ColourRole.Glass),     // cab
        Box(-0.31f, 0f, 0.68f, 0.38f, 0.96f, 0.14f, ColourRole.Body),     // rear bed
        Cyl(PartAxis.Y, 0.33f, 0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, 0.33f, -0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.33f, 0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.33f, -0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Spoke(0.33f, 0.43f, 0.21f, 0.16f), Spoke(0.33f, -0.43f, 0.21f, 0.16f),
        Spoke(-0.33f, 0.43f, 0.21f, 0.16f), Spoke(-0.33f, -0.43f, 0.21f, 0.16f),
    }, default);

    /// <summary>A helicopter (UH-60-like proportions; y = main-rotor diameter): fuselage, cockpit glass, tail boom and fin,
    /// tail rotor, mast, two crossed main blades, skids.</summary>
    public static readonly ShapeKit Helicopter = new(new[]
    {
        Box(0.06f, 0f, 0.36f, 0.48f, 0.14f, 0.40f, ColourRole.Body),                         // fuselage
        Box(0.31f, 0f, 0.38f, 0.10f, 0.12f, 0.30f, ColourRole.Glass),                        // cockpit
        Box(-0.28f, 0f, 0.50f, 0.42f, 0.04f, 0.09f, ColourRole.Body),                        // tail boom
        Box(-0.47f, 0f, 0.64f, 0.06f, 0.015f, 0.34f, ColourRole.Body, -25f),                 // tail fin
        Cyl(PartAxis.Y, -0.48f, 0.02f, 0.70f, 0f, 0.006f, 0.30f, ColourRole.Dark),           // tail rotor disc
        Cyl(PartAxis.Z, 0.06f, 0f, 0.66f, 0.015f, 0f, 0.10f, ColourRole.Metal),              // mast
        Box(0.06f, 0f, 0.72f, 0.83f, 0.03f, 0.012f, ColourRole.Dark, role: PartRole.Rotor),  // main blade, fore–aft
        Box(0.06f, 0f, 0.72f, 0.025f, 1.0f, 0.012f, ColourRole.Dark, role: PartRole.Rotor),  // main blade, across
        Box(0.04f, 0.11f, 0.0125f, 0.40f, 0.015f, 0.025f, ColourRole.Dark, role: PartRole.Gear),                  // skids, on the ground
        Box(0.04f, -0.11f, 0.0125f, 0.40f, 0.015f, 0.025f, ColourRole.Dark, role: PartRole.Gear),
        Box(0.04f, 0.09f, 0.10f, 0.02f, 0.012f, 0.16f, ColourRole.Dark, role: PartRole.Gear),                     // skid struts
        Box(0.04f, -0.09f, 0.10f, 0.02f, 0.012f, 0.16f, ColourRole.Dark, role: PartRole.Gear),
    }, default);

    /// <summary>A fast jet (F-16-like proportions; y = wingspan): fuselage and nose cone, canopy, swept wings and tailplanes, a
    /// swept fin, the nozzle.</summary>
    public static readonly ShapeKit Jet = new(new[]
    {
        Cyl(PartAxis.X, -0.03f, 0f, 0.40f, 0.80f, 0f, 0.28f, ColourRole.Body),               // fuselage
        Cone(PartAxis.X, 0.45f, 0f, 0.40f, 0.10f, 0f, 0.28f, ColourRole.Body),               // nose cone
        Box(0.24f, 0f, 0.56f, 0.18f, 0.08f, 0.10f, ColourRole.Glass),                        // canopy
        Box(-0.08f, 0.24f, 0.38f, 0.26f, 0.46f, 0.025f, ColourRole.Body, yaw: 25f),          // wing, left (swept back)
        Box(-0.08f, -0.24f, 0.38f, 0.26f, 0.46f, 0.025f, ColourRole.Body, yaw: -25f),        // wing, right
        Box(-0.41f, 0.15f, 0.40f, 0.12f, 0.28f, 0.02f, ColourRole.Body, yaw: 30f),           // tailplane, left
        Box(-0.41f, -0.15f, 0.40f, 0.12f, 0.28f, 0.02f, ColourRole.Body, yaw: -30f),         // tailplane, right
        Box(-0.36f, 0f, 0.66f, 0.18f, 0.012f, 0.38f, ColourRole.Body, -35f),                 // fin (swept)
        Cyl(PartAxis.X, -0.46f, 0f, 0.40f, 0.06f, 0f, 0.22f, ColourRole.Dark),               // nozzle
        Box(0.30f, 0f, 0.13f, 0.02f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),                         // nose gear
        Box(-0.10f, 0.10f, 0.13f, 0.03f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),                     // main gear
        Box(-0.10f, -0.10f, 0.13f, 0.03f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),
    }, default);

    /// <summary>A transport (C-130-like proportions; y = wingspan): round fuselage, nose, cockpit glass, a straight high wing with
    /// four engines, the raised tail and its fin and tailplane.</summary>
    public static readonly ShapeKit CargoPlane = new(new[]
    {
        Cyl(PartAxis.X, -0.02f, 0f, 0.32f, 0.80f, 0f, 0.37f, ColourRole.Body),               // fuselage
        Cone(PartAxis.X, 0.44f, 0f, 0.33f, 0.10f, 0f, 0.36f, ColourRole.Body),               // nose
        Box(0.40f, 0f, 0.47f, 0.06f, 0.08f, 0.08f, ColourRole.Glass),                        // cockpit
        Box(0.05f, 0f, 0.58f, 0.13f, 1.0f, 0.03f, ColourRole.Body),                          // high wing, straight
        Cyl(PartAxis.X, 0.12f, 0.17f, 0.54f, 0.16f, 0f, 0.12f, ColourRole.Dark),             // engines
        Cyl(PartAxis.X, 0.12f, -0.17f, 0.54f, 0.16f, 0f, 0.12f, ColourRole.Dark),
        Cyl(PartAxis.X, 0.12f, 0.33f, 0.54f, 0.16f, 0f, 0.12f, ColourRole.Dark),
        Cyl(PartAxis.X, 0.12f, -0.33f, 0.54f, 0.16f, 0f, 0.12f, ColourRole.Dark),
        Box(-0.40f, 0f, 0.42f, 0.20f, 0.07f, 0.22f, ColourRole.Body, -12f),                  // raised tail section
        Box(-0.42f, 0f, 0.72f, 0.14f, 0.012f, 0.44f, ColourRole.Body, -20f),                 // fin
        Box(-0.45f, 0f, 0.56f, 0.12f, 0.40f, 0.02f, ColourRole.Body),                        // tailplane
        Box(0.05f, 0.08f, 0.07f, 0.20f, 0.04f, 0.14f, ColourRole.Dark, role: PartRole.Gear),                      // main-gear pods
        Box(0.05f, -0.08f, 0.07f, 0.20f, 0.04f, 0.14f, ColourRole.Dark, role: PartRole.Gear),
        Box(0.36f, 0f, 0.07f, 0.03f, 0.02f, 0.14f, ColourRole.Dark, role: PartRole.Gear),                         // nose gear
    }, default);

    public static readonly ShapeKit Unknown = new(new[] { Box(0f, 0f, 0.5f, 1f, 1f, 1f, ColourRole.Body) }, default);
    public static readonly ShapeKit None = new(System.Array.Empty<KitPart>(), default);

    /// <summary>The kit of a family (a person's is posed — <see cref="BlockFigure.Parts"/>); empty for a unit.</summary>
    public static ShapeKit For(VisualFamily family) => family switch
    {
        VisualFamily.Tank => Tank,
        VisualFamily.Afv => Afv,
        VisualFamily.WheeledCar => WheeledCar,
        VisualFamily.WheeledUtility => WheeledUtility,
        VisualFamily.Helicopter => Helicopter,
        VisualFamily.Jet => Jet,
        VisualFamily.CargoPlane => CargoPlane,
        VisualFamily.Unit => None,
        _ => Unknown,
    };

    /// <summary>A family's size (length, width or span, height in metres) when the TKB gives none.</summary>
    public static Vector3 DefaultSize(VisualFamily family) => family switch
    {
        VisualFamily.Person => new Vector3(0.35f, 0.55f, 1.8f),
        VisualFamily.Tank => new Vector3(7.9f, 3.7f, 2.4f),
        VisualFamily.Afv => new Vector3(6.5f, 3.3f, 2.6f),
        VisualFamily.WheeledCar => new Vector3(4.5f, 1.8f, 1.5f),
        VisualFamily.WheeledUtility => new Vector3(4.6f, 2.2f, 1.8f),
        VisualFamily.Helicopter => new Vector3(19.8f, 16.4f, 5.1f),
        VisualFamily.Jet => new Vector3(15f, 10f, 4.9f),
        VisualFamily.CargoPlane => new Vector3(29.8f, 40.4f, 11.6f),
        _ => new Vector3(2f, 2f, 2f),
    };
}

/// <summary>
/// ⭐ CE-1033 S1 — the person kit: a block figure (U14) posed by the shared stance rule (<see cref="LogicalStance"/>, M7), with a
/// rifle for a soldier. Parts are fractions of a 1.8 m standing figure's size (<see cref="ShapeKits.DefaultSize"/>), so a taller or
/// shorter TKB person scales. ⭐ S4: a civilian's limbs swing with the walking speed (<see cref="MotionPose"/>).
/// ⭐⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13e) — each arm is an upper arm and a forearm, built point to point (shoulder → elbow →
/// hand), and a SOLDIER's arms hold the rifle in the weapon brain's posture (<see cref="WeaponPostureKind"/>): at ease (carried low,
/// muzzle down), ready (at the chest, muzzle forward and down), aimed (shouldered, level); his arms do not swing. Every pose has the
/// same parts in the same order — legs, torso, head, left upper arm, left forearm, right upper arm, right forearm (+ rifle) — so a
/// stance change and a fall can blend.
/// </summary>
public static class BlockFigure
{
    private static readonly Vector3 S = new(0.35f, 0.55f, 1.8f);

    // Metres for a 1.8 m figure, converted to fractions of (0.35, 0.55, 1.8) below.
    private static KitPart P(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, PartRole role = PartRole.Hull,
                             bool upper = false)
        => new KitPart(PartShape.Box, new Vector3(cx, cy, cz) / S, new Vector3(ex, ey, ez) / S, c, Role: role, Upper: upper);

    /// <summary>A box from <paramref name="a"/> to <paramref name="b"/> (metres), <paramref name="w"/> wide and <paramref name="h"/>
    /// tall across — an arm segment or the rifle.</summary>
    private static KitPart Limb(Vector3 a, Vector3 b, float w, float h, ColourRole c, PartRole role = PartRole.Hull, Vector3? pivot = null)
    {
        var d = b - a;
        float length = d.Length();
        float yaw = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
        float pitch = MathF.Atan2(-d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)) * 180f / MathF.PI;   // + = nose down (KitPart's convention)
        if (MathF.Abs(d.X) < 1e-5f && MathF.Abs(d.Y) < 1e-5f) yaw = 0f;
        return new KitPart(PartShape.Box, (a + b) / 2f / S, new Vector3(length, w, h) / S, c, PartAxis.Z, pitch, yaw, role, Upper: true,
                           SwingPivot: pivot is { } p ? p / S : null);
    }

    private const float ArmWidth = 0.11f, RifleLength = 0.80f;

    /// <summary>A two-segment arm, shoulder → elbow → hand; a swinging arm (a civilian's) swings both from the shoulder.</summary>
    private static KitPart[] Arm(Vector3 shoulder, Vector3 elbow, Vector3 hand, PartRole role)
        => [Limb(shoulder, elbow, ArmWidth, ArmWidth, ColourRole.Body, role, role == PartRole.Hull ? null : shoulder),
            Limb(elbow, hand, ArmWidth * 0.9f, ArmWidth * 0.9f, ColourRole.Body, role, role == PartRole.Hull ? null : shoulder)];

    private static KitPart LegL(float cx, float cz, float ex, float ez, bool swings)
        => P(cx, 0.11f, cz, ex, 0.13f, ez, ColourRole.Dark, swings ? PartRole.LegLeft : PartRole.Hull);
    private static KitPart LegR(float cx, float cz, float ex, float ez, bool swings)
        => P(cx, -0.11f, cz, ex, 0.13f, ez, ColourRole.Dark, swings ? PartRole.LegRight : PartRole.Hull);

    // ── the bodies (legs, torso, head) per stance ──────────────────────────────────────────────────────────────────────────────────

    private static readonly KitPart[] StandingBody =
    {
        LegL(0f, 0.45f, 0.15f, 0.90f, swings: true),                                             // legs (S4: they swing when walking)
        LegR(0f, 0.45f, 0.15f, 0.90f, swings: true),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),                       // torso
        P(0f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, ColourRole.Body, upper: true),                       // head
    };

    private static readonly KitPart[] CrouchedBody =
    {
        LegL(0.10f, 0.25f, 0.42f, 0.50f, swings: false),
        LegR(0.10f, 0.25f, 0.42f, 0.50f, swings: false),
        P(-0.02f, 0f, 0.76f, 0.26f, 0.40f, 0.52f, ColourRole.Body, upper: true),
        P(0.04f, 0f, 1.14f, 0.21f, 0.20f, 0.24f, ColourRole.Body, upper: true),
    };

    private static readonly KitPart[] ProneBody =
    {
        P(-0.62f, 0.12f, 0.08f, 0.90f, 0.13f, 0.15f, ColourRole.Dark),
        P(-0.62f, -0.12f, 0.08f, 0.90f, 0.13f, 0.15f, ColourRole.Dark),
        P(0.04f, 0f, 0.13f, 0.60f, 0.42f, 0.24f, ColourRole.Body),
        P(0.48f, 0f, 0.17f, 0.24f, 0.20f, 0.21f, ColourRole.Body),
    };

    /// <summary>Where the shoulders are, per stance (metres; left is +y).</summary>
    private static readonly Vector3 StandShoulderL = new(0f, 0.26f, 1.43f), StandShoulderR = new(0f, -0.26f, 1.43f);
    private static readonly Vector3 CrouchDrop = new(0.02f, 0f, -0.45f);   // a crouched shoulder: lower, a little forward

    /// <summary>The hip height of a stance as a fraction of the figure's height — where its upper body leans and turns from.</summary>
    public static float HipFraction(StanceId stance) => stance switch { StanceId.Crouched => 0.28f, StanceId.Prone => 0.05f, _ => 0.5f };

    // ── the arms and the rifle ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A civilian's arms hanging at his sides (standing; they swing), or resting forward (crouched).</summary>
    private static KitPart[] CivilianArms(Vector3 drop, bool swing)
    {
        var l = StandShoulderL + drop; var r = StandShoulderR + drop;
        var reach = drop == Vector3.Zero ? new Vector3(0.02f, 0f, -0.60f) : new Vector3(0.30f, -0.02f, -0.24f);
        var bend = drop == Vector3.Zero ? new Vector3(0f, 0f, -0.30f) : new Vector3(0.06f, 0f, -0.20f);
        return [.. Arm(l, l + bend, l + reach with { Y = -reach.Y }, swing ? PartRole.ArmLeft : PartRole.Hull),
                .. Arm(r, r + bend, r + reach, swing ? PartRole.ArmRight : PartRole.Hull)];
    }

    /// <summary>A soldier's arms and rifle in <paramref name="posture"/>, shifted by <paramref name="drop"/> (crouched). The rifle runs
    /// from its stock <c>stock</c> along <c>dir</c>; the right (trigger) hand grips it 0.22 along, the left (support) hand 0.52 along.</summary>
    private static KitPart[] SoldierArms(WeaponPostureKind posture, Vector3 drop)
    {
        Vector3 stock, dir, elbowL, elbowR;
        switch (posture)
        {
            case WeaponPostureKind.Aimed:     // shouldered, level, under the eye
                stock = new(0.00f, -0.13f, 1.41f); dir = new(1f, 0.07f, 0f);
                elbowR = new(0.04f, -0.36f, 1.30f); elbowL = new(0.24f, 0.08f, 1.22f);
                break;
            case WeaponPostureKind.Ready:     // at the chest, muzzle forward and down
                stock = new(0.06f, -0.15f, 1.30f); dir = new(0.88f, 0.12f, -0.46f);
                elbowR = new(0.00f, -0.32f, 1.15f); elbowL = new(0.18f, 0.14f, 1.08f);
                break;
            default:                          // at ease: carried low in front, muzzle down toward the ground ahead
                stock = new(0.12f, -0.17f, 1.17f); dir = new(0.52f, 0.20f, -0.83f);
                elbowR = new(0.00f, -0.30f, 1.15f); elbowL = new(0.06f, 0.20f, 1.12f);
                break;
        }
        dir = Vector3.Normalize(dir);
        stock += drop; elbowL += drop; elbowR += drop;
        var grip = stock + dir * (RifleLength * 0.22f) + new Vector3(0f, 0f, -0.04f);
        var handguard = stock + dir * (RifleLength * 0.52f) + new Vector3(0f, 0f, -0.04f);
        return [.. Arm(StandShoulderL + drop, elbowL, handguard, PartRole.Hull),
                .. Arm(StandShoulderR + drop, elbowR, grip, PartRole.Hull),
                Limb(stock, stock + dir * RifleLength, 0.05f, 0.08f, ColourRole.Metal)];
    }

    private static readonly Vector3 ProneShoulderL = new(0.30f, 0.21f, 0.15f), ProneShoulderR = new(0.30f, -0.21f, 0.15f);
    private static KitPart[] ProneArms(bool soldier)
    {
        if (!soldier)
            return [.. Arm(ProneShoulderL, new(0.50f, 0.25f, 0.07f), new(0.78f, 0.22f, 0.07f), PartRole.Hull),
                    .. Arm(ProneShoulderR, new(0.50f, -0.25f, 0.07f), new(0.78f, -0.22f, 0.07f), PartRole.Hull)];
        var stock = new Vector3(0.40f, -0.12f, 0.15f);
        var dir = new Vector3(1f, 0.02f, 0.02f);
        return [.. Arm(ProneShoulderL, new(0.58f, 0.20f, 0.05f), stock + dir * (RifleLength * 0.55f) + new Vector3(0, 0, -0.03f), PartRole.Hull),
                .. Arm(ProneShoulderR, new(0.46f, -0.28f, 0.05f), stock + dir * (RifleLength * 0.22f) + new Vector3(0, 0, -0.03f), PartRole.Hull),
                Limb(stock, stock + Vector3.Normalize(dir) * RifleLength, 0.05f, 0.08f, ColourRole.Metal)];
    }

    // ── the poses, built once ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly KitPart[] Standing = [.. StandingBody, .. CivilianArms(Vector3.Zero, swing: true)];
    private static readonly KitPart[] Crouched = [.. CrouchedBody, .. CivilianArms(CrouchDrop, swing: false)];
    private static readonly KitPart[] Prone = [.. ProneBody, .. ProneArms(false)];
    private static readonly KitPart[] ProneArmed = [.. ProneBody, .. ProneArms(true)];
    private static readonly KitPart[][] StandingArmed =
        [[.. StandingBody, .. SoldierArms(WeaponPostureKind.AtEase, Vector3.Zero)],
         [.. StandingBody, .. SoldierArms(WeaponPostureKind.Ready, Vector3.Zero)],
         [.. StandingBody, .. SoldierArms(WeaponPostureKind.Aimed, Vector3.Zero)]];
    private static readonly KitPart[][] CrouchedArmed =
        [[.. CrouchedBody, .. SoldierArms(WeaponPostureKind.AtEase, CrouchDrop)],
         [.. CrouchedBody, .. SoldierArms(WeaponPostureKind.Ready, CrouchDrop)],
         [.. CrouchedBody, .. SoldierArms(WeaponPostureKind.Aimed, CrouchDrop)]];

    /// <summary>⭐ W4 (docs/DESIGN_Visual_Effects.md §7 VE-S) — a dead man: on his back, head forward (+X, like prone), legs apart,
    /// arms flung out; a soldier's rifle dropped beside him. Same parts in the same order as every stance, so a fall can blend.</summary>
    private static readonly KitPart[] DeadPose =
    [
        P(-0.60f, 0.20f, 0.08f, 0.90f, 0.14f, 0.14f, ColourRole.Dark) with { YawDegrees = 12f },
        P(-0.60f, -0.20f, 0.08f, 0.90f, 0.14f, 0.14f, ColourRole.Dark) with { YawDegrees = -12f },
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        P(0.46f, 0f, 0.11f, 0.22f, 0.20f, 0.21f, ColourRole.Body),
        .. Arm(new(0.28f, 0.21f, 0.08f), new(0.36f, 0.48f, 0.06f), new(0.30f, 0.76f, 0.06f), PartRole.Hull),
        .. Arm(new(0.28f, -0.21f, 0.08f), new(0.36f, -0.48f, 0.06f), new(0.30f, -0.76f, 0.06f), PartRole.Hull),
    ];

    private static readonly KitPart RifleDropped = P(0.30f, -0.95f, 0.04f, 0.80f, 0.05f, 0.08f, ColourRole.Metal) with { YawDegrees = 20f };
    private static readonly KitPart[] DeadArmed = [.. DeadPose, RifleDropped];

    /// <summary>⭐ W4 (VE-S) — the dead figure. ⛔ Not a <see cref="StanceId"/>: the stance is the animation contract shared with the
    /// Muscle; death is a health fact, and the renderer draws it because the man is destroyed (<c>CombatLife.IsDestroyed</c>).</summary>
    public static KitPart[] Dead(bool soldier) => soldier ? DeadArmed : DeadPose;

    /// <summary>⭐ W4 (VE-S) — the fall: <paramref name="from"/> (his last stance's parts) part-way to <see cref="Dead"/>, every part's
    /// centre, size, yaw and pitch interpolated (<paramref name="t"/> 0..1 over <see cref="DeathFall.Seconds"/>).</summary>
    public static KitPart[] Fall(KitPart[] from, float t, bool soldier) => Lerp(from, Dead(soldier), t);

    private static KitPart[] Lerp(KitPart[] a, KitPart[] b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (t <= 0f) return a;
        if (t >= 1f || a.Length != b.Length) return b;
        var blended = new KitPart[a.Length];
        for (int i = 0; i < a.Length; i++)
            blended[i] = b[i] with
            {
                Centre = Vector3.Lerp(a[i].Centre, b[i].Centre, t),
                Extent = Vector3.Lerp(a[i].Extent, b[i].Extent, t),
                YawDegrees = a[i].YawDegrees + (b[i].YawDegrees - a[i].YawDegrees) * t,
                PitchDegrees = a[i].PitchDegrees + (b[i].PitchDegrees - a[i].PitchDegrees) * t,
                Role = PartRole.Hull,
            };
        return blended;
    }

    /// <summary>
    /// ⭐ CE-1033 S4 — the figure part-way from <paramref name="from"/> to <paramref name="to"/> (<paramref name="t"/> 0..1, the
    /// replicated <c>StanceStatus.TransitionProgress</c>): every stance has the same parts in the same order, so each part's centre
    /// and size are interpolated. A standing figure's limbs keep their roles (they swing); a blend's do not.
    /// </summary>
    public static KitPart[] Blend(StanceId from, StanceId to, float t, bool soldier, WeaponPostureKind posture = WeaponPostureKind.AtEase)
    {
        return Lerp(Parts(from, soldier, posture), Parts(to, soldier, posture), t);   // ⭐ W4 — the one interpolation, shared with the fall
    }

    /// <summary>
    /// ⭐ WL-6b (docs/DESIGN_Weapon_Layers.md §5h) — the figure part-way from weapon posture <paramref name="from"/> to
    /// <paramref name="to"/> in <paramref name="stance"/> (<paramref name="t"/> 0..1, eased in and out; <see cref="PostureBlend"/> times
    /// it). Unlike a stance blend every part KEEPS its role, so a soldier walking while he raises his rifle keeps swinging his legs.
    /// </summary>
    public static KitPart[] Parts(StanceId stance, bool soldier, WeaponPostureKind from, WeaponPostureKind to, float t)
    {
        var a = Parts(stance, soldier, from);
        var b = Parts(stance, soldier, to);
        t = Math.Clamp(t, 0f, 1f);
        if (from == to || t >= 1f) return b;
        if (t <= 0f) return a;
        t = t * t * (3f - 2f * t);   // smoothstep: the rifle starts and stops gently
        var blended = new KitPart[b.Length];
        for (int i = 0; i < b.Length; i++)
            blended[i] = b[i] with
            {
                Centre = Vector3.Lerp(a[i].Centre, b[i].Centre, t),
                Extent = Vector3.Lerp(a[i].Extent, b[i].Extent, t),
                YawDegrees = a[i].YawDegrees + (b[i].YawDegrees - a[i].YawDegrees) * t,
                PitchDegrees = a[i].PitchDegrees + (b[i].PitchDegrees - a[i].PitchDegrees) * t,
            };
        return blended;
    }

    /// <summary>⭐ WL-6b — a stance blend whose two ends are themselves part-way between two weapon postures (a posture change during a
    /// stance change).</summary>
    public static KitPart[] Blend(StanceId from, StanceId to, float t, bool soldier, WeaponPostureKind postureFrom, WeaponPostureKind postureTo,
                                  float postureT)
        => Lerp(Parts(from, soldier, postureFrom, postureTo, postureT), Parts(to, soldier, postureFrom, postureTo, postureT), t);

    /// <summary>The posed parts for <paramref name="stance"/> (anything but crouched or prone stands — the rule's own default); a
    /// soldier's rifle and hands in the weapon brain's <paramref name="posture"/> (WL-6; prone is always on the rifle).</summary>
    public static KitPart[] Parts(StanceId stance, bool soldier, WeaponPostureKind posture = WeaponPostureKind.AtEase)
    {
        int p = Math.Clamp((int)posture, 0, 2);
        return stance switch
        {
            StanceId.Crouched => soldier ? CrouchedArmed[p] : Crouched,
            StanceId.Prone => soldier ? ProneArmed : Prone,
            _ => soldier ? StandingArmed[p] : Standing,
        };
    }
}

/// <summary>
/// ⭐ WL-6b (docs/DESIGN_Weapon_Layers.md §5h) — the picture's own timing of a weapon-posture change, on SIMULATION time (a pause
/// freezes it, every host shows the same): the rifle moves from the old posture to the new one over <see cref="Seconds"/>, and an aimed
/// upper body turns and pitches toward its aim with a <see cref="AimSeconds"/> time constant instead of jumping. The posture itself is
/// the weapon brain's (R-272), replicated; nothing here is sent — like <see cref="DeathFall"/>, a unit first seen (a load, a late host,
/// a replay jump) is simply in its posture, and a clock that goes back snaps.
/// </summary>
public sealed class PostureBlend
{
    /// <summary>How long the rifle takes to move between two postures (s, sim time).</summary>
    public const float Seconds = 0.3f;

    /// <summary>The aim angles' time constant (s, sim time): about 95 % of a turn is done in three of these.</summary>
    public const float AimSeconds = 0.12f;

    private struct State
    {
        public WeaponPostureKind From, To;
        public double ChangedAt;
        public float Twist, Pitch;
        public double AimAt;
        public bool HasAim;
    }

    private readonly Dictionary<Entity, State> _states = new();
    private readonly List<Entity> _gone = new();

    /// <summary>Where <paramref name="e"/>'s rifle is this frame: between <c>From</c> and <c>To</c>, <c>T</c> of the way (1 = there).</summary>
    public (WeaponPostureKind From, WeaponPostureKind To, float T) Posture(Entity e, WeaponPostureKind now, double simTime)
    {
        if (!_states.TryGetValue(e, out var st))
        {
            st = new State { From = now, To = now, ChangedAt = double.NegativeInfinity };
            _states[e] = st;
            return (now, now, 1f);
        }
        if (now != st.To)
        {
            // a change mid-blend starts from whichever end the rifle was nearer
            float done = Progress(st, simTime);
            st.From = done < 0.5f ? st.From : st.To;
            st.To = now;
            st.ChangedAt = simTime;
            _states[e] = st;
        }
        return (st.From, st.To, Progress(st, simTime));
    }

    private static float Progress(in State st, double simTime)
        => double.IsNegativeInfinity(st.ChangedAt) || simTime < st.ChangedAt ? 1f
         : (float)Math.Clamp((simTime - st.ChangedAt) / Seconds, 0.0, 1.0);

    /// <summary>The aim angles this frame, eased toward <paramref name="twist"/> / <paramref name="pitch"/> (0 when not aimed, so the
    /// body turns back). Must follow <see cref="Posture"/> for the same entity in the frame.</summary>
    public (float Twist, float Pitch) Aim(Entity e, float twist, float pitch, double simTime)
    {
        if (!_states.TryGetValue(e, out var st)) st = new State { From = WeaponPostureKind.AtEase, To = WeaponPostureKind.AtEase, ChangedAt = double.NegativeInfinity };
        double dt = simTime - st.AimAt;
        if (!st.HasAim || dt < 0 || dt > 1.0)
        {
            st.Twist = twist; st.Pitch = pitch;   // first seen, a clock jump, or a long gap: no easing
        }
        else if (dt > 0)
        {
            float k = 1f - MathF.Exp(-(float)dt / AimSeconds);
            st.Twist += (twist - st.Twist) * k;
            st.Pitch += (pitch - st.Pitch) * k;
        }
        st.AimAt = simTime;
        st.HasAim = true;
        _states[e] = st;
        return (st.Twist, st.Pitch);
    }

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _states.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) _states.Remove(e);
    }
}

/// <summary>
/// ⭐ W4 (docs/DESIGN_Visual_Effects.md §7 VE-S) — when each dead man fell, so the renderer can blend him from his last stance into
/// <see cref="BlockFigure.Dead"/> over <see cref="Seconds"/> of SIMULATION time (a pause freezes the fall). Only a death SEEN happening
/// falls: a body first seen already dead (a load, a late host, a replay jump) is simply lying there.
/// </summary>
public sealed class DeathFall
{
    /// <summary>How long the fall takes (s, sim time).</summary>
    public const float Seconds = 0.6f;

    /// <summary>A death further than this from the last frame he was seen alive was not seen happening (a jump) ⇒ no fall.</summary>
    public const double SeenWithin = 0.5;

    private readonly Dictionary<Entity, (double At, StanceId Stance)> _lastAlive = new();
    private readonly Dictionary<Entity, (double At, StanceId From)> _fell = new();
    private readonly List<Entity> _gone = new();

    /// <summary>He is alive this frame, in <paramref name="stance"/>.</summary>
    public void Alive(Entity e, StanceId stance, double now)
    {
        _lastAlive[e] = (now, stance);
        _fell.Remove(e);
    }

    /// <summary>He is dead this frame: how far through the fall (0..1, 1 = lying) and the stance he fell from.</summary>
    public (float T, StanceId From) Dead(Entity e, double now)
    {
        if (!_fell.TryGetValue(e, out var fall))
        {
            fall = _lastAlive.TryGetValue(e, out var alive) && now >= alive.At && now - alive.At <= SeenWithin
                ? (now, alive.Stance)
                : (double.NegativeInfinity, StanceId.Standing);
            _fell[e] = fall;
            _lastAlive.Remove(e);
        }
        if (double.IsNegativeInfinity(fall.At) || now < fall.At) return (1f, fall.From);
        return ((float)Math.Clamp((now - fall.At) / Seconds, 0.0, 1.0), fall.From);
    }

    /// <summary>Forgets everyone not drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        _gone.Clear();
        foreach (var e in _lastAlive.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _fell.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) { _lastAlive.Remove(e); _fell.Remove(e); }
    }
}
