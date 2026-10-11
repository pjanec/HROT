using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;

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
public readonly record struct MotionPose(float Rotor, float WheelRoll, float Swing)
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
public readonly record struct KitPart(
    PartShape Shape, Vector3 Centre, Vector3 Extent, ColourRole Colour, PartAxis Axis = PartAxis.Z, float PitchDegrees = 0f,
    float YawDegrees = 0f, PartRole Role = PartRole.Hull)
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
                var pivot = centre + new Vector3(0f, 0f, Extent.Z * size.Z / 2f);
                m *= Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationY(-sign * motion.Swing) * Matrix4x4.CreateTranslation(pivot);
                break;
            }
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
/// ⭐ CE-1033 S1 — the person kit: a six-box block figure (U14) posed by the shared stance rule (<see cref="LogicalStance"/>,
/// M7), with a rifle for a soldier. Parts are fractions of a 1.8 m standing figure's size (<see cref="ShapeKits.DefaultSize"/>),
/// so a taller or shorter TKB person scales. ⭐ S4: the standing figure's limbs swing with the walking speed (<see cref="MotionPose"/>).
/// </summary>
public static class BlockFigure
{
    // Metres for a 1.8 m figure, converted to fractions of (0.35, 0.55, 1.8) below.
    private static KitPart P(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, PartRole role = PartRole.Hull)
    {
        var s = new Vector3(0.35f, 0.55f, 1.8f);
        return new KitPart(PartShape.Box, new Vector3(cx, cy, cz) / s, new Vector3(ex, ey, ez) / s, c, Role: role);
    }

    private static readonly KitPart[] Standing =
    {
        P(0f, 0.11f, 0.45f, 0.15f, 0.13f, 0.90f, ColourRole.Dark, PartRole.LegLeft),     // legs (S4: they swing when walking)
        P(0f, -0.11f, 0.45f, 0.15f, 0.13f, 0.90f, ColourRole.Dark, PartRole.LegRight),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body),                          // torso
        P(0f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, ColourRole.Body),                          // head
        P(0f, 0.27f, 1.13f, 0.11f, 0.11f, 0.60f, ColourRole.Body, PartRole.ArmLeft),     // arms
        P(0f, -0.27f, 1.13f, 0.11f, 0.11f, 0.60f, ColourRole.Body, PartRole.ArmRight),
    };

    private static readonly KitPart[] Crouched =
    {
        P(0.10f, 0.11f, 0.25f, 0.42f, 0.13f, 0.50f, ColourRole.Dark),
        P(0.10f, -0.11f, 0.25f, 0.42f, 0.13f, 0.50f, ColourRole.Dark),
        P(-0.02f, 0f, 0.76f, 0.26f, 0.40f, 0.52f, ColourRole.Body),
        P(0.04f, 0f, 1.14f, 0.21f, 0.20f, 0.24f, ColourRole.Body),
        P(0.14f, 0.24f, 0.74f, 0.34f, 0.11f, 0.11f, ColourRole.Body),
        P(0.14f, -0.24f, 0.74f, 0.34f, 0.11f, 0.11f, ColourRole.Body),
    };

    private static readonly KitPart[] Prone =
    {
        P(-0.62f, 0.12f, 0.08f, 0.90f, 0.13f, 0.15f, ColourRole.Dark),
        P(-0.62f, -0.12f, 0.08f, 0.90f, 0.13f, 0.15f, ColourRole.Dark),
        P(0.04f, 0f, 0.13f, 0.60f, 0.42f, 0.24f, ColourRole.Body),
        P(0.48f, 0f, 0.17f, 0.24f, 0.20f, 0.21f, ColourRole.Body),
        P(0.56f, 0.22f, 0.08f, 0.46f, 0.10f, 0.10f, ColourRole.Body),
        P(0.56f, -0.22f, 0.08f, 0.46f, 0.10f, 0.10f, ColourRole.Body),
    };

    private static readonly KitPart RifleStanding = P(0.30f, -0.14f, 1.20f, 0.80f, 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart RifleCrouched = P(0.42f, -0.10f, 0.80f, 0.80f, 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart RifleProne = P(0.92f, -0.12f, 0.14f, 0.80f, 0.05f, 0.08f, ColourRole.Metal);

    private static readonly KitPart[] StandingArmed = [.. Standing, RifleStanding];
    private static readonly KitPart[] CrouchedArmed = [.. Crouched, RifleCrouched];
    private static readonly KitPart[] ProneArmed = [.. Prone, RifleProne];

    /// <summary>
    /// ⭐ CE-1033 S4 — the figure part-way from <paramref name="from"/> to <paramref name="to"/> (<paramref name="t"/> 0..1, the
    /// replicated <c>StanceStatus.TransitionProgress</c>): every stance has the same parts in the same order, so each part's centre
    /// and size are interpolated. A standing figure's limbs keep their roles (they swing); a blend's do not.
    /// </summary>
    public static KitPart[] Blend(StanceId from, StanceId to, float t, bool soldier)
    {
        var a = Parts(from, soldier);
        var b = Parts(to, soldier);
        t = Math.Clamp(t, 0f, 1f);
        if (t <= 0f) return a;
        if (t >= 1f || a.Length != b.Length) return b;
        var blended = new KitPart[a.Length];
        for (int i = 0; i < a.Length; i++)
            blended[i] = b[i] with
            {
                Centre = Vector3.Lerp(a[i].Centre, b[i].Centre, t),
                Extent = Vector3.Lerp(a[i].Extent, b[i].Extent, t),
                Role = PartRole.Hull,
            };
        return blended;
    }

    /// <summary>The posed parts for <paramref name="stance"/> (anything but crouched or prone stands — the rule's own default).</summary>
    public static KitPart[] Parts(StanceId stance, bool soldier) => stance switch
    {
        StanceId.Crouched => soldier ? CrouchedArmed : Crouched,
        StanceId.Prone => soldier ? ProneArmed : Prone,
        _ => soldier ? StandingArmed : Standing,
    };
}
