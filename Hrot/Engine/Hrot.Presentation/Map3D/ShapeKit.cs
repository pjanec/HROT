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

/// <summary>Which colour a part takes: the entity's colour, or a fixed material shade. ⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o F-1) —
/// <see cref="Skin"/>: a figure's head. ⭐ CE-1104 — <see cref="Canvas"/>: a stretcher's.</summary>
public enum ColourRole : byte { Body, Dark, Glass, Metal, Skin, Canvas }

/// <summary>
/// ⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o F-4) — which side of a block figure's HEAD box is his face (the face is drawn on it):
/// <see cref="Front"/> the box's +x side, the top of the head +z (upright, prone, seated); <see cref="Up"/> the +z side, the top of the
/// head +x (on his back); <see cref="Down"/> the −z side, the top of the head +x (draped over a shoulder). <see cref="None"/>: not a head.
/// </summary>
public enum FaceOn : byte { None, Front, Up, Down }

/// <summary>⭐ CE-1097 (§6o F-3) — the shade a head detail is drawn in. ⭐ CE-1101 (§6r) — the eyes, nose and mouth are painted by the
/// shader now (<see cref="BodySurface"/>); only the helmet and the hair are boxes.</summary>
public enum FaceShade : byte { Helmet, Hair }

/// <summary>⭐ CE-1097 (§6o) — one detail of a face, a box in the HEAD box's own unit frame (−0.5..0.5 on each axis; x = out of the
/// face, y = his left, z = the top of his head), before <see cref="BlockFigure.FaceFrame"/> turns it onto the side the face is on.</summary>
public readonly record struct FacePart(Vector3 Centre, Vector3 Extent, FaceShade Shade);

/// <summary>⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p) — one blood spot: a flat box in figure part <paramref name="Part"/>'s unit frame
/// (the shared part order), on one of its sides; <paramref name="Fresh"/> brighter red, else darker.</summary>
public readonly record struct Stain(int Part, Vector3 Centre, Vector3 Extent, bool Fresh, bool Char = false);

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
    /// <summary>⭐ CE-1093 (docs/DESIGN_Map_3D_Mode.md §6n N-9) — a tail-rotor blade: spins about its centre's SIDEWAYS axis, faster than the
    /// main rotor (<see cref="ShapeKits.TailRotorRatio"/>).</summary>
    TailRotor,
}

/// <summary>
/// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §6, "things come alive") — the moving parts' pose for one frame: the main-rotor
/// angle, the wheels' roll angle (radians) and the limb swing (radians, + = the left leg forward). Default: all still.
/// </summary>
/// <para>⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13e) — and the block figure's UPPER BODY (<see cref="KitPart.Upper"/> parts): <paramref name="Lean"/>
/// tilts it forward about the hip (radians; a run, and an aim pitched down), <paramref name="Twist"/> turns it about the vertical
/// (radians, counter-clockwise; an aim to the side), about a hip at <paramref name="HipZ"/> of the figure's height.</para>
/// <para>⭐ WL-6c — the gait itself: <paramref name="Stride"/> (0..1, how much the figure is walking or running) and its
/// <paramref name="GaitPhase"/> (radians; <c>Swing = amplitude · sin(phase)</c>); <paramref name="KneeFlexL"/> / <paramref name="KneeFlexR"/>
/// bend each shin back about its knee (radians) — the foot lifting as the leg swings through.</para>
/// <para>⭐ WL-6d — <paramref name="Moving"/> (0..1, from the speed alone — a crawl is too slow to read as a walk) and the
/// <paramref name="CrawlPhase"/> (radians, one cycle per <c>MotionTracker.CrawlMetres</c>).</para>
public readonly record struct MotionPose(float Rotor, float WheelRoll, float Swing, float Lean = 0f, float Twist = 0f, float HipZ = 0.5f,
                                         float Stride = 0f, float GaitPhase = 0f, float KneeFlexL = 0f, float KneeFlexR = 0f,
                                         float Moving = 0f, float CrawlPhase = 0f)
{
    public static readonly MotionPose Still = default;
}

/// <summary>
/// ⭐ CE-1033 — the pose of a kit's articulated parts: turret azimuth RELATIVE TO THE HULL (radians, counter-clockwise seen from
/// above, 0 = forward) and gun elevation RELATIVE TO THE TURRET (radians, positive up). Default: everything forward and level.
/// ⭐ CE-1114 — <see cref="Published"/>: the pose is a turret part's replicated <c>TurretPose</c> (every angle is true, a level gun
/// included), not a stand-in.
/// </summary>
public readonly record struct ArticulationPose(float TurretAzimuth, float GunElevation, bool Published = false)
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
/// where a limb swings from when it is not its own top — a forearm swings from the shoulder with its upper arm. ⭐ WL-6c —
/// <paramref name="KneePivot"/> marks a SHIN: it first bends back about its knee by <see cref="MotionPose.KneeFlexL"/> / <c>R</c>, then swings
/// with its thigh from the hip.</para>
/// <para>⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o) — <paramref name="Face"/> marks a figure's HEAD and which side of it is the face.</para>
public readonly record struct KitPart(
    PartShape Shape, Vector3 Centre, Vector3 Extent, ColourRole Colour, PartAxis Axis = PartAxis.Z, float PitchDegrees = 0f,
    float YawDegrees = 0f, PartRole Role = PartRole.Hull, bool Upper = false, Vector3? SwingPivot = null, Vector3? KneePivot = null,
    FaceOn Face = FaceOn.None)
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
            case PartRole.TailRotor when motion.Rotor != 0f:
                m *= Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(motion.Rotor * ShapeKits.TailRotorRatio) * Matrix4x4.CreateTranslation(centre);
                break;
            case PartRole.WheelSpoke when motion.WheelRoll != 0f:
                // rolling forward turns the wheel's top forward: about +y, the top (+z) toward +x
                m *= Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(motion.WheelRoll) * Matrix4x4.CreateTranslation(centre);
                break;
            case PartRole.LegLeft or PartRole.LegRight or PartRole.ArmLeft or PartRole.ArmRight
                when motion.Swing != 0f || (KneePivot != null && (motion.KneeFlexL != 0f || motion.KneeFlexR != 0f)):
            {
                // ⭐ WL-6c — a shin bends back about its knee first (+ angle = the foot toward −x), then swings with the thigh
                if (KneePivot is { } kp)
                {
                    float flex = Role == PartRole.LegLeft ? motion.KneeFlexL : motion.KneeFlexR;
                    if (flex != 0f)
                    {
                        var knee = kp * size;
                        m *= Matrix4x4.CreateTranslation(-knee) * Matrix4x4.CreateRotationY(flex) * Matrix4x4.CreateTranslation(knee);
                    }
                }
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

    /// <summary>⭐ CE-1093 — how many turns the tail rotor makes per main-rotor turn (UH-60: ~4.7).</summary>
    public const float TailRotorRatio = 4.7f;

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
        Box(-0.48f, 0.025f, 0.70f, 0.17f, 0.004f, 0.012f, ColourRole.Dark, role: PartRole.TailRotor),   // ⭐ CE-1093 — tail-rotor blades
        Box(-0.48f, 0.025f, 0.70f, 0.006f, 0.004f, 0.56f, ColourRole.Dark, role: PartRole.TailRotor),
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

    /// <summary>⭐ CE-1086 (§6n N-7) — a quadcopter (an FPV drone; x, y = its rotor span): a body, an X frame, four motor pods and four
    /// rotors (each spins about its own motor, <see cref="PartRole.Rotor"/>), the camera at the front, the warhead slung under it.</summary>
    public static readonly ShapeKit Drone = new(new[]
    {
        Box(0f, 0f, 0.45f, 0.36f, 0.30f, 0.34f, ColourRole.Body),                            // body
        Box(0f, 0f, 0.50f, 1.15f, 0.07f, 0.10f, ColourRole.Dark, yaw: 45f),                   // frame arm, front-left to rear-right
        Box(0f, 0f, 0.50f, 1.15f, 0.07f, 0.10f, ColourRole.Dark, yaw: -45f),                  // frame arm, front-right to rear-left
        Cyl(PartAxis.Z, 0.36f, 0.36f, 0.58f, 0.13f, 0f, 0.22f, ColourRole.Metal),            // motor pods
        Cyl(PartAxis.Z, 0.36f, -0.36f, 0.58f, 0.13f, 0f, 0.22f, ColourRole.Metal),
        Cyl(PartAxis.Z, -0.36f, 0.36f, 0.58f, 0.13f, 0f, 0.22f, ColourRole.Metal),
        Cyl(PartAxis.Z, -0.36f, -0.36f, 0.58f, 0.13f, 0f, 0.22f, ColourRole.Metal),
        Box(0.36f, 0.36f, 0.72f, 0.46f, 0.05f, 0.02f, ColourRole.Dark, role: PartRole.Rotor),   // rotors (two blades each)
        Box(0.36f, -0.36f, 0.72f, 0.46f, 0.05f, 0.02f, ColourRole.Dark, role: PartRole.Rotor),
        Box(-0.36f, 0.36f, 0.72f, 0.46f, 0.05f, 0.02f, ColourRole.Dark, role: PartRole.Rotor),
        Box(-0.36f, -0.36f, 0.72f, 0.46f, 0.05f, 0.02f, ColourRole.Dark, role: PartRole.Rotor),
        Box(0.21f, 0f, 0.50f, 0.07f, 0.10f, 0.14f, ColourRole.Glass),                        // the camera, at the front
        Cyl(PartAxis.X, 0.02f, 0f, 0.16f, 0.62f, 0f, 0.30f, ColourRole.Dark),                // the warhead, slung under
    }, default);

    /// <summary>
    /// ⭐ CE-1092 (docs/DESIGN_Map_3D_Mode.md §6n N-8) — an attack plane (A-10-like; y = span): a round fuselage with the GAU-8's barrel out
    /// of the nose, a low straight wing, two engines high on the rear fuselage, the tailplane and its TWIN fins. Its bombs are
    /// <see cref="AttackJetBombs"/>, drawn one per round left on its bomb rack.
    /// </summary>
    public static readonly ShapeKit AttackJet = new(new[]
    {
        Cyl(PartAxis.X, 0.02f, 0f, 0.40f, 0.78f, 0f, 0.30f, ColourRole.Body),               // fuselage
        Cone(PartAxis.X, 0.45f, 0f, 0.40f, 0.08f, 0f, 0.28f, ColourRole.Body),              // nose
        Cyl(PartAxis.X, 0.50f, 0f, 0.34f, 0.07f, 0f, 0.05f, ColourRole.Metal),              // the GAU-8's barrel
        Box(0.30f, 0f, 0.57f, 0.10f, 0.06f, 0.12f, ColourRole.Glass),                       // canopy
        Box(0.02f, 0f, 0.30f, 0.15f, 1.00f, 0.03f, ColourRole.Body),                        // the straight wing, low
        Cyl(PartAxis.X, -0.20f, 0.10f, 0.64f, 0.18f, 0f, 0.24f, ColourRole.Dark),           // engines, high on the rear fuselage
        Cyl(PartAxis.X, -0.20f, -0.10f, 0.64f, 0.18f, 0f, 0.24f, ColourRole.Dark),
        Box(-0.42f, 0f, 0.58f, 0.10f, 0.32f, 0.025f, ColourRole.Body),                      // tailplane
        Box(-0.43f, 0.16f, 0.74f, 0.10f, 0.012f, 0.32f, ColourRole.Body),                   // twin fins
        Box(-0.43f, -0.16f, 0.74f, 0.10f, 0.012f, 0.32f, ColourRole.Body),
        Box(0.12f, 0f, 0.13f, 0.02f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),
        Box(0.02f, 0.15f, 0.13f, 0.03f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),
        Box(0.02f, -0.15f, 0.13f, 0.03f, 0.02f, 0.26f, ColourRole.Dark, role: PartRole.Gear),
    }, default);

    // ── ⭐ CE-1103 (docs/DESIGN_Map_3D_Mode.md §6s) — the AH-64: authored in its own CENTRE-OF-GRAVITY metres (x forward, y left, z up from
    //    the CG), then put into the kit box by the geometry's own offsets (Body.Geometry: 17.73 × 14.63 × 3.87, BodyCentreX −1.5, the box
    //    bottom 1.335 m below the CG) — so a part lands where the TKB's own numbers (the turret pivot, the wheels) put things ──────────
    private const float AhL = 17.73f, AhW = 14.63f, AhH = 3.87f, AhRefX = -1.5f, AhBottom = -1.335f;

    private static KitPart AhBox(float x, float y, float z, float lx, float wy, float hz, ColourRole c, float pitch = 0f, PartRole role = PartRole.Hull)
        => Box((x - AhRefX) / AhL, y / AhW, (z - AhBottom) / AhH, lx / AhL, wy / AhW, hz / AhH, c, pitch, role: role);

    private static KitPart AhCylX(float x, float y, float z, float length, float diameter, ColourRole c, PartRole role = PartRole.Hull)
        => Cyl(PartAxis.X, (x - AhRefX) / AhL, y / AhW, (z - AhBottom) / AhH, length / AhL, 0f, diameter / AhH, c, role);

    /// <summary>
    /// ⭐ CE-1103 (§6s H-1, H-2) — an attack helicopter (AH-64-like): a slim fuselage, the gunner's canopy low in front and the pilot's
    /// raised behind it, a sensor ball on the nose, engine nacelles beside the mast, stub wings each with a rocket pod (inboard) and a
    /// missile rail (outboard), a long tail boom with a swept fin, a stabiliser and the tail rotor on its left, a four-blade main rotor, and
    /// the CHIN GUN — an articulated turret and barrel, placed by the TKB turret (<see cref="KitFromTkb"/>) and turned by its pose.
    /// </summary>
    public static readonly ShapeKit AttackHelicopter = new(new[]
    {
        AhBox(0.9f, 0f, -0.15f, 6.6f, 1.0f, 1.55f, ColourRole.Body),                   // fuselage
        AhBox(5.0f, 0f, -0.35f, 1.6f, 0.85f, 1.1f, ColourRole.Body),                   // nose
        AhBox(6.05f, 0f, -0.45f, 0.55f, 0.6f, 0.6f, ColourRole.Dark),                  // the nose sensor ball
        AhBox(4.2f, 0f, 0.55f, 1.4f, 0.8f, 0.55f, ColourRole.Glass),                   // the gunner's canopy, low in front
        AhBox(2.85f, 0f, 0.85f, 1.5f, 0.82f, 0.62f, ColourRole.Glass),                 // the pilot's, raised behind
        AhCylX(-0.4f, 0.8f, 0.6f, 2.4f, 0.62f, ColourRole.Body),                       // engine nacelles
        AhCylX(-0.4f, -0.8f, 0.6f, 2.4f, 0.62f, ColourRole.Body),
        AhBox(0f, 0f, 0.85f, 1.6f, 0.8f, 0.5f, ColourRole.Body),                       // the rotor pylon
        Cyl(PartAxis.Z, (0f - AhRefX) / AhL, 0f, (1.45f - AhBottom) / AhH, 0.3f / AhL, 0f, 0.75f / AhH, ColourRole.Metal),   // mast
        AhBox(0f, 0f, 1.95f, 14.6f, 0.55f, 0.08f, ColourRole.Dark, role: PartRole.Rotor),   // main rotor, four blades
        AhBox(0f, 0f, 1.95f, 0.55f, 14.6f, 0.08f, ColourRole.Dark, role: PartRole.Rotor),
        AhBox(-5.4f, 0f, 0.15f, 6.2f, 0.5f, 0.65f, ColourRole.Body),                   // tail boom
        AhBox(-8.4f, 0f, 1.05f, 1.1f, 0.12f, 1.9f, ColourRole.Body, pitch: -20f),      // the swept fin
        AhBox(-8.6f, 0f, 0.0f, 0.8f, 3.4f, 0.07f, ColourRole.Body),                    // horizontal stabiliser
        AhBox(-8.7f, 0.25f, 1.6f, 2.8f, 0.05f, 0.2f, ColourRole.Dark, role: PartRole.TailRotor),   // tail rotor, on the left
        AhBox(-8.7f, 0.25f, 1.6f, 0.2f, 0.05f, 2.8f, ColourRole.Dark, role: PartRole.TailRotor),
        AhBox(0.7f, 1.55f, 0.35f, 1.1f, 2.0f, 0.12f, ColourRole.Body),                 // stub wings
        AhBox(0.7f, -1.55f, 0.35f, 1.1f, 2.0f, 0.12f, ColourRole.Body),
        AhCylX(0.6f, 1.3f, 0.0f, 1.7f, 0.42f, ColourRole.Dark),                        // rocket pods, inboard
        AhCylX(0.6f, -1.3f, 0.0f, 1.7f, 0.42f, ColourRole.Dark),
        AhBox(0.7f, 2.3f, 0.0f, 1.7f, 0.45f, 0.42f, ColourRole.Metal),                 // missile rails, outboard
        AhBox(0.7f, -2.3f, 0.0f, 1.7f, 0.45f, 0.42f, ColourRole.Metal),
        AhBox(4.3f, 0f, -0.95f, 0.5f, 0.5f, 0.35f, ColourRole.Dark, role: PartRole.Turret),   // the chin turret (KitFromTkb places it)
        AhCylX(5.0f, 0f, -1.0f, 1.0f, 0.1f, ColourRole.Metal, PartRole.Gun),           // its barrel (KitFromTkb sizes it to the muzzle)
        AhBox(1.8f, 1.0f, -0.95f, 0.15f, 0.12f, 0.75f, ColourRole.Dark, role: PartRole.Gear),   // gear, when the TKB gives none
        AhBox(1.8f, -1.0f, -0.95f, 0.15f, 0.12f, 0.75f, ColourRole.Dark, role: PartRole.Gear),
        AhBox(-8.6f, 0f, -0.8f, 0.12f, 0.1f, 0.9f, ColourRole.Dark, role: PartRole.Gear),
    }, new KitPivots(new Vector2((4.3f - AhRefX) / AhL, 0f), new Vector2((4.5f - AhRefX) / AhL, (-1.0f - AhBottom) / AhH)));

    /// <summary>⭐ CE-1092 — the attack plane's bombs on their wing pylons (Mk-82: 2.2 m, 0.27 m), outboard first; a pylon each.</summary>
    public static readonly KitPart[] AttackJetBombs =
    [
        Cyl(PartAxis.X, 0.03f, 0.31f, 0.20f, 0.135f, 0f, 0.06f, ColourRole.Dark),
        Cyl(PartAxis.X, 0.03f, -0.31f, 0.20f, 0.135f, 0f, 0.06f, ColourRole.Dark),
        Cyl(PartAxis.X, 0.03f, 0.19f, 0.20f, 0.135f, 0f, 0.06f, ColourRole.Dark),
        Cyl(PartAxis.X, 0.03f, -0.19f, 0.20f, 0.135f, 0f, 0.06f, ColourRole.Dark),
    ];

    /// <summary>⭐ CE-1092 — the pylons the bombs hang from (drawn whether loaded or not).</summary>
    public static readonly KitPart[] AttackJetPylons =
    [
        Box(0.03f, 0.31f, 0.25f, 0.06f, 0.008f, 0.06f, ColourRole.Dark),
        Box(0.03f, -0.31f, 0.25f, 0.06f, 0.008f, 0.06f, ColourRole.Dark),
        Box(0.03f, 0.19f, 0.25f, 0.06f, 0.008f, 0.06f, ColourRole.Dark),
        Box(0.03f, -0.19f, 0.25f, 0.06f, 0.008f, 0.06f, ColourRole.Dark),
    ];

    /// <summary>⭐ CE-1089 (§6n N-5) — a self-propelled howitzer (M109-like; x includes the barrel): tracks, hull, a big boxy turret set
    /// back, a long barrel with a muzzle brake and its bore evacuator, the travel lock folded on the glacis.</summary>
    public static readonly ShapeKit SpArtillery = new(new[]
    {
        Box(-0.12f, 0.38f, 0.16f, 0.70f, 0.24f, 0.32f, ColourRole.Dark),                          // track, left (the hull is behind the barrel)
        Box(-0.12f, -0.38f, 0.16f, 0.70f, 0.24f, 0.32f, ColourRole.Dark),                         // track, right
        Box(-0.12f, 0f, 0.40f, 0.68f, 0.56f, 0.32f, ColourRole.Body),                            // hull
        Box(-0.20f, 0f, 0.72f, 0.42f, 0.62f, 0.34f, ColourRole.Body, role: PartRole.Turret),     // the big turret
        Cyl(PartAxis.X, 0.20f, 0f, 0.72f, 0.62f, 0f, 0.06f, ColourRole.Metal, PartRole.Gun),     // barrel
        Cyl(PartAxis.X, 0.17f, 0f, 0.72f, 0.07f, 0f, 0.09f, ColourRole.Metal, PartRole.Gun),     // bore evacuator
        Cyl(PartAxis.X, 0.50f, 0f, 0.72f, 0.04f, 0f, 0.10f, ColourRole.Dark, PartRole.Gun),      // muzzle brake
        Box(0.21f, 0f, 0.46f, 0.03f, 0.10f, 0.20f, ColourRole.Dark, -30f),                       // travel lock, folded
    }, new KitPivots(new Vector2(-0.20f, 0f), new Vector2(-0.06f, 0.72f)));

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
        VisualFamily.Drone => Drone,
        VisualFamily.SpArtillery => SpArtillery,
        VisualFamily.AttackJet => AttackJet,
        VisualFamily.AttackHelicopter => AttackHelicopter,   // ⭐ CE-1103
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
        VisualFamily.Drone => new Vector3(0.6f, 0.6f, 0.3f),
        VisualFamily.SpArtillery => new Vector3(9.7f, 3.6f, 3.3f),
        VisualFamily.AttackJet => new Vector3(16.3f, 17.5f, 4.5f),
        VisualFamily.AttackHelicopter => new Vector3(AhL, AhW, AhH),   // ⭐ CE-1103 — the AH-64's box (the TKB's own wins)
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
/// same parts in the same order — left thigh, left shin, right thigh, right shin (WL-6c), torso, head, left upper arm, left forearm,
/// right upper arm, right forearm (+ rifle) — so a
/// stance change and a fall can blend.
/// </summary>
public static partial class BlockFigure
{
    private static readonly Vector3 S = new(0.35f, 0.55f, 1.8f);

    /// <summary>⭐ CE-1073 — the head's place in every figure's parts (the shared order above: four leg parts, the torso, the head).
    /// The first-person camera reads the eyes from it; a rail holds the order for every stance, posture and fall.</summary>
    public const int HeadIndex = 5;

    // Metres for a 1.8 m figure, converted to fractions of (0.35, 0.55, 1.8) below.
    private static KitPart P(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, PartRole role = PartRole.Hull,
                             bool upper = false)
        => new KitPart(PartShape.Box, new Vector3(cx, cy, cz) / S, new Vector3(ex, ey, ez) / S, c, Role: role, Upper: upper);

    /// <summary>⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o) — a figure's HEAD: a skin-coloured box whose <paramref name="face"/> side carries
    /// the face (<see cref="FaceParts"/>), drawn on it by the renderer.</summary>
    private static KitPart Head(float cx, float cy, float cz, float ex, float ey, float ez, FaceOn face, bool upper = false)
        => P(cx, cy, cz, ex, ey, ez, ColourRole.Skin, upper: upper) with { Face = face };

    // ── ⭐ CE-1097 (§6o F-2, F-3) — the face, in the head box's unit frame (x out of the face, y his left, z the top of his head) ──────

    /// <summary>A soldier's helmet: over the top of his head, a little wider than it and its brim over his brow.</summary>
    private static readonly FacePart[] SoldierFace =
        [new(new(-0.02f, 0f, 0.37f), new(1.16f, 1.16f, 0.42f), FaceShade.Helmet)];

    /// <summary>A civilian's hair, close over the top and back of his head.</summary>
    private static readonly FacePart[] CivilianFace =
        [new(new(-0.06f, 0f, 0.41f), new(1.06f, 1.06f, 0.24f), FaceShade.Hair)];

    /// <summary>⭐ CE-1097 — the boxes drawn on a figure's head: a soldier's helmet or a civilian's hair (⭐ CE-1101 — the features are the
    /// shader's).</summary>
    public static IReadOnlyList<FacePart> FaceParts(bool soldier) => soldier ? SoldierFace : CivilianFace;

    /// <summary>⭐ CE-1097 (§6o F-4) — turns the face's frame (x out of the face, z the top of the head) onto the side of the head box
    /// the face is on: <see cref="FaceOn.Up"/> x → +z, z → +x; <see cref="FaceOn.Down"/> x → −z, z → +x (both proper rotations).</summary>
    public static Matrix4x4 FaceFrame(FaceOn on) => on switch
    {
        FaceOn.Up => new Matrix4x4(0f, 0f, 1f, 0f, 0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
        FaceOn.Down => new Matrix4x4(0f, 0f, -1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f),
        _ => Matrix4x4.Identity,
    };

    // ── ⭐ CE-1098 (§6p) — blood: repeatably random spots on the body, more as health drops ──────────────────────────────────────

    /// <summary>⭐ CE-1098 — the most blood spots a figure shows (at no health left: a dead man shows them all).</summary>
    public const int MaxStains = 8;

    /// <summary>⭐ CE-1098 (§6p B-2) — how many spots a man with <paramref name="health"/> of <paramref name="max"/> shows: none unhurt, one
    /// after any hit, <see cref="MaxStains"/> at none left.</summary>
    public static int StainCount(float health, float max)
    {
        if (!(max > 0f) || health >= max) return 0;
        float lost = 1f - Math.Clamp(health, 0f, max) / max;
        return Math.Clamp((int)MathF.Ceiling(lost * MaxStains), 1, MaxStains);
    }

    /// <summary>The parts a spot may be on (the shared order: thighs 0 / 2, shins 1 / 3, torso 4, upper arms 6 / 8, forearms 7 / 9) — the
    /// torso three times as often; never the head (the face is there).</summary>
    private static readonly int[] StainParts = [TorsoIndex, TorsoIndex, TorsoIndex, 0, 2, 1, 3, 6, 8, 7, 9];

    /// <summary>
    /// ⭐ CE-1098 (§6p B-1, B-3) — spot <paramref name="k"/> of the man seeded <paramref name="seed"/> (his network id — the same on every node):
    /// which part it is on, and a flat box in that part's unit frame lying on one of its sides — for the torso its front, back, sides or
    /// shoulders; for a limb a side along its length. The same seed and k always give the same spot, so a man's spots stay put and the
    /// earlier ones stay as more appear.
    /// </summary>
    public static Stain StainOf(long seed, int k)
    {
        ulong s = Mix((ulong)seed * 0x9E3779B1UL + (ulong)k * 0x85EBCA77UL + 0x2545F491UL);
        float U() { s = Mix(s); return (s >> 40) / (float)(1 << 24); }   // [0, 1)
        int part = StainParts[(int)(U() * StainParts.Length)];
        bool torso = part == TorsoIndex;
        int side = (int)(U() * (torso ? 6 : 4));
        float w = 0.36f + U() * 0.28f, h = w * (0.7f + U() * 0.6f);
        var (c, e) = SpotOnSide(torso, side, U(), U(), w, h);
        return new Stain(part, c, e, Fresh: U() < 0.5f);
    }

    /// <summary>⭐ CE-1098 / CE-1099 — a flat box lying on one SIDE of a figure part, in the part's unit frame: for the torso
    /// (<paramref name="torso"/>) side 0–1 its chest, 2 its back, 3 / 4 its sides, 5 its shoulders; for a limb (which runs along its own x)
    /// sides 0–3 the four faces along it. <paramref name="w"/> × <paramref name="h"/> is its size (fractions of the side), and
    /// <paramref name="u"/>, <paramref name="v"/> (0..1) place it so the WHOLE box stays on the side. Shared by the blood and the camouflage.</summary>
    internal static (Vector3 Centre, Vector3 Extent) SpotOnSide(bool torso, int side, float u, float v, float w, float h)
    {
        float a = (u - 0.5f) * 2f * (0.5f - w / 2f) * 0.9f, b = (v - 0.5f) * 2f * (0.5f - h / 2f) * 0.9f;
        const float Proud = 0.51f, Thin = 0.03f;
        return torso
            ? side switch
            {
                0 or 1 => (new Vector3(Proud, a, b), new Vector3(Thin, w, h)),   // chest (twice as often: the side a man is seen from)
                2 => (new Vector3(-Proud, a, b), new Vector3(Thin, w, h)),    // back
                3 => (new Vector3(a, Proud, b), new Vector3(w, Thin, h)),     // left side
                4 => (new Vector3(a, -Proud, b), new Vector3(w, Thin, h)),    // right side
                _ => (new Vector3(a, b, Proud), new Vector3(w, h, Thin)),     // shoulders (seen from above; the chest of a man on his back)
            }
            : side switch   // a limb runs along its own x: a spot on one of the four sides along it
            {
                0 => (new Vector3(a, Proud, b), new Vector3(w, Thin, h)),
                1 => (new Vector3(a, -Proud, b), new Vector3(w, Thin, h)),
                2 => (new Vector3(a, b, Proud), new Vector3(w, h, Thin)),
                _ => (new Vector3(a, b, -Proud), new Vector3(w, h, Thin)),
            };
    }

    /// <summary>⭐ CE-1122 (docs/DESIGN_Map_3D_Mode.md §6y HM-2) — a mark's size across (m): a bullet's blood, a fragment's, a burn's char.</summary>
    public const float BulletMarkM = 0.09f, FragmentMarkM = 0.06f, BurnMarkM = 0.16f;

    /// <summary>
    /// ⭐ CE-1122 (§6y HM-1, HM-2; hit location HL-8) — a replicated hit mark as a stain on its part: <paramref name="local"/> is the point in
    /// the hit box's unit frame (−0.5 … 0.5, on the face it entered — the axis nearest ±0.5); the stain lies flat on that face, just proud of
    /// it, and is <see cref="BulletMarkM"/> / <see cref="FragmentMarkM"/> / <see cref="BurnMarkM"/> across in METRES on a part
    /// <paramref name="partMetres"/> big (so a spot on a forearm is round, not stretched along it).
    /// </summary>
    public static Stain MarkStain(int part, Vector3 local, Fdp.Toolkit.Physics.BodyParts.HitMarkKind kind, bool fresh, Vector3 partMetres)
    {
        int axis = MathF.Abs(local.X) >= MathF.Abs(local.Y) && MathF.Abs(local.X) >= MathF.Abs(local.Z) ? 0 : MathF.Abs(local.Y) >= MathF.Abs(local.Z) ? 1 : 2;
        float across = kind switch
        {
            Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Burn => BurnMarkM,
            Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Fragment => FragmentMarkM,
            _ => BulletMarkM,
        };
        float U(float metres) => metres > 1e-4f ? Math.Clamp(across / metres, 0.05f, 0.9f) : 0.5f;
        var extent = new Vector3(U(partMetres.X), U(partMetres.Y), U(partMetres.Z));
        var centre = local;
        const float Proud = 0.51f, Thin = 0.03f;
        switch (axis)
        {
            case 0: centre.X = MathF.CopySign(Proud, local.X); extent.X = Thin; break;
            case 1: centre.Y = MathF.CopySign(Proud, local.Y); extent.Y = Thin; break;
            default: centre.Z = MathF.CopySign(Proud, local.Z); extent.Z = Thin; break;
        }
        return new Stain(part, centre, extent, fresh, kind == Fdp.Toolkit.Physics.BodyParts.HitMarkKind.Burn);
    }

    /// <summary>⭐ CE-1098 — where spot <paramref name="st"/> is drawn: a unit box sized and placed in its part's frame, carried by that part's
    /// own transform <paramref name="part"/> (so it moves with the limb).</summary>
    public static Matrix4x4 StainDetail(in Stain st, Matrix4x4 part)
        => Matrix4x4.CreateScale(st.Extent) * Matrix4x4.CreateTranslation(st.Centre) * part;

    private static ulong Mix(ulong z)   // SplitMix64's finaliser
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>⭐ CE-1097 — where face detail <paramref name="d"/> is drawn: a unit box sized and placed in the head box's frame, turned onto
    /// the face's side, then carried by the head part's own transform <paramref name="head"/> (its lean, twist, blend and fall).</summary>
    public static Matrix4x4 FaceDetail(in FacePart d, FaceOn on, Matrix4x4 head)
        => Matrix4x4.CreateScale(d.Extent) * Matrix4x4.CreateTranslation(d.Centre) * FaceFrame(on) * head;

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

    // ── ⭐ WL-6c — legs: thigh + shin, the knee solved from hip and ankle ─────────────────────────────────────────────────────────

    private const float Thigh = 0.44f, Shin = 0.44f;

    /// <summary>Two-bone IK: the knee between <paramref name="hip"/> and <paramref name="ankle"/> for a thigh and shin of the given
    /// lengths, bending toward <paramref name="bend"/> (a straight leg when the ankle is out of reach).</summary>
    public static Vector3 Knee(Vector3 hip, Vector3 ankle, float thigh, float shin, Vector3 bend)
    {
        var d = ankle - hip;
        float len = d.Length();
        var dir = d / len;
        if (len >= thigh + shin - 1e-4f) return hip + dir * thigh;
        float a = (thigh * thigh - shin * shin + len * len) / (2f * len);
        float h = MathF.Sqrt(MathF.Max(0f, thigh * thigh - a * a));
        var perp = bend - dir * Vector3.Dot(bend, dir);
        if (perp.LengthSquared() < 1e-6f) perp = Vector3.UnitZ - dir * Vector3.Dot(Vector3.UnitZ, dir);
        return hip + dir * a + Vector3.Normalize(perp) * h;
    }

    /// <summary>A leg from <paramref name="hip"/> to <paramref name="ankle"/>, the knee bending toward <paramref name="bend"/> (forward
    /// for a standing man). A swinging leg swings from the hip, its shin bending at the knee as it goes.</summary>
    private static KitPart[] Leg(Vector3 hip, Vector3 ankle, bool left, bool swings, Vector3? bend = null)
    {
        var knee = Knee(hip, ankle, Thigh, Shin, bend ?? Vector3.UnitX);
        var role = swings ? (left ? PartRole.LegLeft : PartRole.LegRight) : PartRole.Hull;
        Vector3? pivot = swings ? hip : null;
        return [Limb(hip, knee, 0.13f, 0.15f, ColourRole.Dark, role, pivot) with { Upper = false },
                Limb(knee, ankle, 0.12f, 0.14f, ColourRole.Dark, role, pivot) with { Upper = false, KneePivot = knee / S }];
    }

    private static KitPart[] Legs(Vector3 hipL, Vector3 ankleL, Vector3 hipR, Vector3 ankleR, bool swings, Vector3? bend = null)
        => [.. Leg(hipL, ankleL, true, swings, bend), .. Leg(hipR, ankleR, false, swings, bend)];

    // ── the bodies (legs, torso, head) per stance ──────────────────────────────────────────────────────────────────────────────────

    private static readonly KitPart[] StandingBody =
    [
        .. Legs(new(0f, 0.11f, 0.90f), new(0f, 0.11f, 0.02f), new(0f, -0.11f, 0.90f), new(0f, -0.11f, 0.02f), swings: true),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),                       // torso
        Head(0f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),                      // head
    ];

    /// <summary>⭐ WL-6c — how much lower the athletic fighting stance carries the hips (m): knees bent, weight forward.</summary>
    public const float AthleticDrop = 0.10f;
    private static readonly Vector3 Drop = new(0f, 0f, -AthleticDrop);

    /// <summary>⭐ WL-6c — the ATHLETIC FIGHTING STANCE of an aimed soldier standing still: hips lower, knees bent, the support (left)
    /// foot forward and the firing-side foot back; the upper body is the standing one lowered (it leans forward by
    /// <see cref="MotionPose.Lean"/>, <c>EntityBodyLayer3D</c>).</summary>
    private static readonly KitPart[] AthleticBody =
    [
        .. Legs(new(-0.02f, 0.12f, 0.80f), new(0.24f, 0.15f, 0.02f), new(-0.02f, -0.12f, 0.80f), new(-0.20f, -0.16f, 0.02f), swings: true),
        P(0f, 0f, 1.18f - AthleticDrop, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),
        Head(0.02f, 0f, 1.60f - AthleticDrop, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),
    ];

    /// <summary>⭐ WL-6c — the COMBAT GLIDE's legs (an aimed soldier on the move): hips as low as the athletic stance, both feet under
    /// them, knees bent — the gait then swings them in short steps (<c>EntityBodyLayer3D</c> shortens the swing).</summary>
    private static readonly KitPart[] GlideLegs =
        Legs(new(0f, 0.12f, 0.80f), new(0.03f, 0.14f, 0.02f), new(0f, -0.12f, 0.80f), new(0.03f, -0.14f, 0.02f), swings: true);

    /// <summary>
    /// ⭐ CE-1064 (docs/DESIGN_Map_3D_Mode.md §6j) — crouched and still: the KNEEL. The right (firing-side) knee and shin lie on the
    /// ground, the left foot is planted forward with its shin upright, and the hips sit back over the right heel. The upper body is
    /// unchanged, so the head stays at the stance's 1.1 m eye. ⛔ This replaces a squat with both feet under the hips: its thighs were
    /// level, its knees stuck out in front and its upright torso sat behind its feet, so it read as a man sitting on an invisible chair.
    /// Each leg keeps one y from hip to ankle, so no segment gets a sideways yaw that a stance blend would sweep through.
    /// </summary>
    private static readonly KitPart[] CrouchedBody =
    [
        .. Leg(new(-0.05f, 0.12f, 0.50f), new(0.40f, 0.12f, 0.02f), true, swings: false, bend: Vector3.UnitZ),   // support knee up
        .. KneelingLeg(new(-0.05f, -0.12f, 0.50f), new(-0.40f, -0.12f, 0.07f)),                                  // firing-side knee down
        P(-0.02f, 0f, 0.76f, 0.26f, 0.40f, 0.52f, ColourRole.Body, upper: true),
        Head(0.04f, 0f, 1.14f, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),
    ];

    /// <summary>A leg kneeling: the knee forward and down on the ground, the shin lying back from it to the foot. The shin is built
    /// from the foot TO the knee, so it points forward (yaw 0) like every other leg segment and a blend turns it in the leg's own plane.</summary>
    private static KitPart[] KneelingLeg(Vector3 hip, Vector3 ankle)
    {
        var knee = Knee(hip, ankle, Thigh, Shin, Vector3.UnitX);
        return [Limb(hip, knee, 0.13f, 0.15f, ColourRole.Dark) with { Upper = false },
                Limb(ankle, knee, 0.12f, 0.14f, ColourRole.Dark) with { Upper = false }];
    }

    /// <summary>⭐ CE-1064 — how much lower a crouched RUN carries the hips than standing (m), and how far its upper body leans
    /// forward from them (radians, about 26°; <c>EntityBodyLayer3D</c> adds it on top of the gait's own lean).</summary>
    public const float CrouchRunDrop = 0.30f, CrouchRunLean = 0.45f;

    /// <summary>⭐ CE-1064 — how much shorter a crouched run's steps are than an upright one's (a long stride from bent knees swung the
    /// back shin out flat behind him — a lunge, not a run).</summary>
    public const float CrouchRunStepCut = 0.35f;

    private static readonly Vector3 RunDrop = new(0.02f, 0f, -CrouchRunDrop);

    /// <summary>⭐ CE-1064 — the CROUCHED RUN (R-265: a threatened or cautious soldier moves in a crouch): hips 0.6 m, both feet under
    /// them, knees bent forward; the legs SWING (thigh and shin from the hip, the shin bending at the knee) and the upper body is the
    /// standing one lowered by <see cref="CrouchRunDrop"/>, leaning forward by <see cref="CrouchRunLean"/>.</summary>
    private static readonly KitPart[] CrouchRunBody =
    [
        .. Legs(new(0f, 0.12f, 0.90f - CrouchRunDrop), new(0.06f, 0.12f, 0.02f), new(0f, -0.12f, 0.90f - CrouchRunDrop), new(0.06f, -0.12f, 0.02f), swings: true),
        P(0.02f, 0f, 1.18f - CrouchRunDrop, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),
        Head(0.02f, 0f, 1.60f - CrouchRunDrop, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),
    ];

    private static readonly KitPart[] ProneBody =
    [
        .. Legs(new(-0.20f, 0.12f, 0.09f), new(-1.06f, 0.14f, 0.07f), new(-0.20f, -0.12f, 0.09f), new(-1.06f, -0.14f, 0.07f), swings: false),
        P(0.04f, 0f, 0.13f, 0.60f, 0.42f, 0.24f, ColourRole.Body),
        Head(0.48f, 0f, 0.17f, 0.24f, 0.20f, 0.21f, FaceOn.Front),
    ];

    /// <summary>Where the shoulders are, per stance (metres; left is +y).</summary>
    private static readonly Vector3 StandShoulderL = new(0f, 0.26f, 1.43f), StandShoulderR = new(0f, -0.26f, 1.43f);
    private static readonly Vector3 CrouchDrop = new(0.02f, 0f, -0.45f);   // a crouched shoulder: lower, a little forward

    /// <summary>The hip height of a stance as a fraction of the figure's height — where its upper body leans and turns from.</summary>
    public static float HipFraction(StanceId stance) => stance switch { StanceId.Crouched => 0.28f, StanceId.Prone => 0.05f, _ => 0.5f };

    /// <summary>⭐ CE-1064 — the crouched run's hip height as a fraction of the figure's height.</summary>
    public const float CrouchRunHipFraction = (0.90f - CrouchRunDrop) / 1.8f;

    // ── the arms and the rifle ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A civilian's arms hanging at his sides and swinging (standing, a crouched run), or resting forward (crouched, still).</summary>
    private static KitPart[] CivilianArms(Vector3 drop, bool swing)
    {
        var l = StandShoulderL + drop; var r = StandShoulderR + drop;
        bool hanging = swing || drop == Vector3.Zero;
        var reach = hanging ? new Vector3(0.02f, 0f, -0.60f) : new Vector3(0.30f, -0.02f, -0.24f);
        var bend = hanging ? new Vector3(0f, 0f, -0.30f) : new Vector3(0.06f, 0f, -0.20f);
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

    /// <summary>
    /// ⭐ CE-1164 (civilian U-3, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U3-a) — COWERING: crouched low, both forearms up over the
    /// head (elbows forward, hands on top of it) — what a civilian under fire with nowhere to go does (the CivilianHsm's Cower state,
    /// <c>EffectState.Cowering</c>).
    /// </summary>
    public static readonly KitPart[] Cower = [.. CrouchedBody, .. CowerArms()];

    private static KitPart[] CowerArms()
    {
        var l = StandShoulderL + CrouchDrop; var r = StandShoulderR + CrouchDrop;
        return [.. Arm(l, l + new Vector3(0.22f, 0.07f, 0.10f), l + new Vector3(0.10f, -0.13f, 0.34f), PartRole.Hull),
                .. Arm(r, r + new Vector3(0.22f, -0.07f, 0.10f), r + new Vector3(0.10f, 0.13f, 0.34f), PartRole.Hull)];
    }
    private static readonly KitPart[] Prone = [.. ProneBody, .. ProneArms(false)];
    private static readonly KitPart[] ProneArmed = [.. ProneBody, .. ProneArms(true)];
    private static readonly KitPart[][] StandingArmed =
        [[.. StandingBody, .. SoldierArms(WeaponPostureKind.AtEase, Vector3.Zero)],
         [.. StandingBody, .. SoldierArms(WeaponPostureKind.Ready, Vector3.Zero)],
         [.. AthleticBody, .. SoldierArms(WeaponPostureKind.Aimed, Drop)]];   // ⭐ WL-6c — aimed: the athletic fighting stance

    /// <summary>The index of the first part after the four leg parts (torso).</summary>
    public const int TorsoIndex = 4;

    /// <summary>
    /// ⭐ WL-6c — the COMBAT GLIDE: a standing figure's legs part-way (<paramref name="weight"/> 0..1 — aimed × moving) from the pose's
    /// own legs to <see cref="GlideLegs"/> (feet under the lowered hips, knees bent). Only the four leg parts change; their roles stay,
    /// so the gait swings them.
    /// </summary>
    public static KitPart[] Glide(KitPart[] parts, float weight)
    {
        weight = Math.Clamp(weight, 0f, 1f);
        if (weight <= 0f || parts.Length < TorsoIndex) return parts;
        var glided = (KitPart[])parts.Clone();
        for (int i = 0; i < TorsoIndex; i++)
            glided[i] = GlideLegs[i] with
            {
                Centre = Vector3.Lerp(parts[i].Centre, GlideLegs[i].Centre, weight),
                Extent = Vector3.Lerp(parts[i].Extent, GlideLegs[i].Extent, weight),
                YawDegrees = parts[i].YawDegrees + (GlideLegs[i].YawDegrees - parts[i].YawDegrees) * weight,
                PitchDegrees = parts[i].PitchDegrees + (GlideLegs[i].PitchDegrees - parts[i].PitchDegrees) * weight,
                SwingPivot = parts[i].SwingPivot is { } a && GlideLegs[i].SwingPivot is { } b ? Vector3.Lerp(a, b, weight) : GlideLegs[i].SwingPivot,
                KneePivot = parts[i].KneePivot is { } ka && GlideLegs[i].KneePivot is { } kb ? Vector3.Lerp(ka, kb, weight) : GlideLegs[i].KneePivot,
            };
        return glided;
    }
    private static readonly KitPart[][] CrouchedArmed =
        [[.. CrouchedBody, .. SoldierArms(WeaponPostureKind.AtEase, CrouchDrop)],
         [.. CrouchedBody, .. SoldierArms(WeaponPostureKind.Ready, CrouchDrop)],
         [.. CrouchedBody, .. SoldierArms(WeaponPostureKind.Aimed, CrouchDrop)]];

    // ⭐ CE-1064 — the crouched run, per posture (a soldier's arms stay on the rifle; a civilian's swing)
    private static readonly KitPart[] CrouchRun = [.. CrouchRunBody, .. CivilianArms(RunDrop, swing: true)];
    private static readonly KitPart[][] CrouchRunArmed =
        [[.. CrouchRunBody, .. SoldierArms(WeaponPostureKind.AtEase, RunDrop)],
         [.. CrouchRunBody, .. SoldierArms(WeaponPostureKind.Ready, RunDrop)],
         [.. CrouchRunBody, .. SoldierArms(WeaponPostureKind.Aimed, RunDrop)]];

    /// <summary>
    /// ⭐ CE-1064 (docs/DESIGN_Map_3D_Mode.md §6j) — a crouched figure ON THE MOVE: part-way (<paramref name="weight"/> 0..1, how much he
    /// is walking or running — <see cref="MotionPose.Stride"/>) from <paramref name="still"/> (the kneel, in its posture) to the
    /// crouched run in the same posture blend. Every part takes the run's role, so the legs (and a civilian's arms) swing with the gait;
    /// the shared parts-in-order rule makes the blend part by part, like <see cref="Glide"/>.
    /// </summary>
    public static KitPart[] CrouchedMoving(KitPart[] still, bool soldier, WeaponPostureKind from, WeaponPostureKind to, float postureT, float weight)
    {
        weight = Math.Clamp(weight, 0f, 1f);
        if (weight <= 0f) return still;
        var run = PostureMix(soldier ? CrouchRunArmed : [CrouchRun, CrouchRun, CrouchRun], from, to, postureT);
        if (run.Length != still.Length) return run;
        var moving = new KitPart[run.Length];
        for (int i = 0; i < run.Length; i++)
            moving[i] = run[i] with
            {
                Centre = Vector3.Lerp(still[i].Centre, run[i].Centre, weight),
                Extent = Vector3.Lerp(still[i].Extent, run[i].Extent, weight),
                YawDegrees = still[i].YawDegrees + (run[i].YawDegrees - still[i].YawDegrees) * weight,
                PitchDegrees = still[i].PitchDegrees + (run[i].PitchDegrees - still[i].PitchDegrees) * weight,
                SwingPivot = still[i].SwingPivot is { } a && run[i].SwingPivot is { } b ? Vector3.Lerp(a, b, weight) : run[i].SwingPivot,
                KneePivot = still[i].KneePivot is { } ka && run[i].KneePivot is { } kb ? Vector3.Lerp(ka, kb, weight) : run[i].KneePivot,
            };
        return moving;
    }

    /// <summary>⭐ W4 (docs/DESIGN_Visual_Effects.md §7 VE-S) — a dead man: on his back, head forward (+X, like prone), legs apart,
    /// arms flung out; a soldier's rifle dropped beside him. Same parts in the same order as every stance, so a fall can blend.</summary>
    private static readonly KitPart[] DeadPose =
    [
        .. Legs(new(-0.17f, 0.12f, 0.08f), new(-1.02f, 0.32f, 0.07f), new(-0.17f, -0.12f, 0.08f), new(-1.02f, -0.32f, 0.07f), swings: false),
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        Head(0.46f, 0f, 0.11f, 0.22f, 0.20f, 0.21f, FaceOn.Up),
        .. Arm(new(0.28f, 0.21f, 0.08f), new(0.36f, 0.48f, 0.06f), new(0.30f, 0.76f, 0.06f), PartRole.Hull),
        .. Arm(new(0.28f, -0.21f, 0.08f), new(0.36f, -0.48f, 0.06f), new(0.30f, -0.76f, 0.06f), PartRole.Hull),
    ];

    private static readonly KitPart RifleDropped = P(0.30f, -0.95f, 0.04f, 0.80f, 0.05f, 0.08f, ColourRole.Metal) with { YawDegrees = 20f };
    private static readonly KitPart[] DeadArmed = [.. DeadPose, RifleDropped];

    /// <summary>⭐ W4 (VE-S) — the dead figure. ⛔ Not a <see cref="StanceId"/>: the stance is the animation contract shared with the
    /// Muscle; death is a health fact, and the renderer draws it because the man is destroyed (<c>CombatLife.IsDestroyed</c>).</summary>
    public static KitPart[] Dead(bool soldier) => soldier ? DeadArmed : DeadPose;

    // ── ⭐ CE-1081 … CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m) — the held, the seated and the downed; every one in the shared part order ──

    private static readonly KitPart RifleOnBack = Limb(new(-0.12f, -0.05f, 0.27f), new(0.62f, 0.05f, 0.27f), 0.05f, 0.08f, ColourRole.Metal);

    /// <summary>
    /// ⭐ CE-1081 (C-1) — CARRIED across a man's shoulders (a fireman's carry), limp: his middle is the figure's origin, his torso lies
    /// along +X toward his head, which hangs a little lower; his legs hang down the other side, knees folded; his arms hang straight down;
    /// a soldier's rifle stays slung on his back. <c>Carriage.OnShoulders</c> places this frame on the carrier.
    /// </summary>
    public static readonly KitPart[] DrapedBody =
    [
        .. Legs(new(-0.10f, 0.11f, 0.02f), new(-0.32f, 0.13f, -0.80f), new(-0.10f, -0.11f, 0.02f), new(-0.32f, -0.13f, -0.80f), swings: false,
                bend: new Vector3(-1f, 0f, 0f)),
        P(0.22f, 0f, 0.06f, 0.60f, 0.40f, 0.24f, ColourRole.Body),                 // torso, lying across
        Head(0.62f, 0f, -0.12f, 0.22f, 0.20f, 0.24f, FaceOn.Down),                 // head, hanging
        .. Arm(new(0.48f, 0.21f, 0.02f), new(0.56f, 0.22f, -0.28f), new(0.58f, 0.18f, -0.58f), PartRole.Hull),
        .. Arm(new(0.48f, -0.21f, 0.02f), new(0.56f, -0.22f, -0.28f), new(0.58f, -0.18f, -0.58f), PartRole.Hull),
    ];

    /// <summary>⭐ CE-1081 — the carried figure (with his rifle on his back when a soldier).</summary>
    public static KitPart[] Draped(bool soldier) => soldier ? [.. DrapedBody, RifleOnBack] : DrapedBody;

    /// <summary>
    /// ⭐ CE-1081 (C-1) — DRAGGED on his back, head toward the man pulling him (+X, like the dead and the prone), legs trailing together,
    /// both arms up over his head to the carrier's hand; a soldier's rifle on his chest.
    /// </summary>
    public static readonly KitPart[] TowedBody =
    [
        .. Legs(new(-0.17f, 0.11f, 0.08f), new(-1.02f, 0.15f, 0.07f), new(-0.17f, -0.11f, 0.08f), new(-1.02f, -0.15f, 0.07f), swings: false),
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        Head(0.46f, 0f, 0.13f, 0.22f, 0.20f, 0.21f, FaceOn.Up),
        .. Arm(new(0.28f, 0.21f, 0.12f), new(0.52f, 0.20f, 0.40f), new(0.74f, 0.10f, 0.75f), PartRole.Hull),
        .. Arm(new(0.28f, -0.21f, 0.12f), new(0.52f, -0.20f, 0.40f), new(0.74f, -0.10f, 0.75f), PartRole.Hull),
    ];

    /// <summary>
    /// ⭐ CE-1104 (docs/DESIGN_Map_3D_Mode.md §6t T-b1, T-b2) — ON A STRETCHER: on his back, head forward (+X, toward the front man), legs
    /// together, arms along his sides (a soldier's rifle on his chest), centred on the litter — and the litter itself, appended after the
    /// shared part order: its canvas and two poles 1.7 m long whose ends are the carriers' handles. The frame is the litter's
    /// (<see cref="Carriage.TeamFrame"/>): its origin is where the poles are held.
    /// </summary>
    public static KitPart[] OnLitter(bool soldier) => soldier ? LitterArmed : LitterPlain;

    /// <summary>⭐ CE-1104 — half a stretcher pole: the handle is this far ahead of (front) or behind (rear) the litter's middle (m).</summary>
    public const float LitterHalfLength = 0.85f;

    private static readonly Vector3 OnLitterShift = new(0.24f, 0f, 0.03f);   // his middle on the litter's, lying on the canvas

    private static KitPart[] Shifted(KitPart[] parts, Vector3 by)
    {
        var moved = new KitPart[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            moved[i] = parts[i] with
            {
                Centre = parts[i].Centre + by / S,
                SwingPivot = parts[i].SwingPivot is { } sp ? sp + by / S : null,
                KneePivot = parts[i].KneePivot is { } kp ? kp + by / S : null,
            };
        return moved;
    }

    private static readonly KitPart[] LitterBody = Shifted(
    [
        .. Legs(new(-0.17f, 0.10f, 0.08f), new(-1.02f, 0.11f, 0.07f), new(-0.17f, -0.10f, 0.08f), new(-1.02f, -0.11f, 0.07f), swings: false),
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        Head(0.46f, 0f, 0.12f, 0.22f, 0.20f, 0.21f, FaceOn.Up),
        .. Arm(new(0.28f, 0.22f, 0.10f), new(0.02f, 0.25f, 0.08f), new(-0.24f, 0.24f, 0.08f), PartRole.Hull),
        .. Arm(new(0.28f, -0.22f, 0.10f), new(0.02f, -0.25f, 0.08f), new(-0.24f, -0.24f, 0.08f), PartRole.Hull),
    ], OnLitterShift);

    private static readonly KitPart[] Litter =
    [
        P(0f, 0f, -0.01f, 1.55f, 0.56f, 0.03f, ColourRole.Canvas),                     // the canvas
        P(0f, 0.30f, 0f, 2f * LitterHalfLength, 0.04f, 0.04f, ColourRole.Metal),       // the poles, their ends the handles
        P(0f, -0.30f, 0f, 2f * LitterHalfLength, 0.04f, 0.04f, ColourRole.Metal),
    ];

    private static readonly KitPart RifleOnChest = Limb(new(0.30f, -0.10f, 0.27f), new(-0.40f, 0.10f, 0.27f), 0.05f, 0.08f, ColourRole.Metal);
    private static readonly KitPart[] LitterPlain = [.. LitterBody, .. Litter];
    private static readonly KitPart[] LitterArmed = [.. LitterBody, Shifted([RifleOnChest], OnLitterShift)[0], .. Litter];

    /// <summary>
    /// ⭐ CE-1104 (§6t T-b3, T-b4) — a standing figure's arms on a TEAM load's handles: the front man's hands just behind his hips (the load
    /// is behind him), the rear man's just ahead of him, both at <see cref="Carriage.HandleHeight"/> and the poles' width apart; a
    /// soldier's rifle slung across his back. Only the arm parts (and the rifle) change; the legs keep their gait.
    /// </summary>
    public static KitPart[] HandleArms(KitPart[] standing, bool front, bool soldier)
    {
        if (standing.Length < TorsoIndex + 2 + 4) return standing;
        var parts = (KitPart[])standing.Clone();
        float x = front ? -0.15f : 0.15f, z = Carriage.HandleHeight;
        var handL = new Vector3(x, 0.30f, z); var handR = new Vector3(x, -0.30f, z);
        KitPart[] arms =
        [
            .. Arm(StandShoulderL, new(x * 0.5f, 0.31f, 1.12f), handL, PartRole.Hull),
            .. Arm(StandShoulderR, new(x * 0.5f, -0.31f, 1.12f), handR, PartRole.Hull),
        ];
        for (int i = 0; i < 4; i++) parts[TorsoIndex + 2 + i] = arms[i];
        if (soldier && parts.Length > TorsoIndex + 6)
            parts[TorsoIndex + 6] = Limb(new(-0.16f, 0.22f, 1.55f), new(-0.14f, -0.24f, 0.85f), 0.05f, 0.08f, ColourRole.Metal);   // slung
        return parts;
    }

    /// <summary>⭐ CE-1081 — the dragged figure.</summary>
    public static KitPart[] Towed(bool soldier)
        => soldier ? [.. TowedBody, Limb(new(-0.25f, -0.05f, 0.26f), new(0.45f, 0.05f, 0.27f), 0.05f, 0.08f, ColourRole.Metal)] : TowedBody;

    /// <summary>
    /// ⭐ CE-1081 (C-1) — the CARRIER's arms on a standing figure: carrying, both hands up on the load on his shoulders; dragging, the left
    /// arm hangs and the right reaches back and down to the casualty's hands. A soldier's rifle is slung across his back (he cannot fire —
    /// <c>Embarkation</c> takes his CanShoot). Only the arm parts (and the rifle) change; the legs keep their gait roles.
    /// </summary>
    public static KitPart[] CarrierArms(KitPart[] standing, bool drag, bool soldier)
    {
        if (standing.Length < TorsoIndex + 2 + 4) return standing;
        var parts = (KitPart[])standing.Clone();
        var arms = drag
            ? (KitPart[])[.. Arm(StandShoulderL, StandShoulderL + new Vector3(0.02f, 0.02f, -0.30f), StandShoulderL + new Vector3(0.04f, -0.02f, -0.60f), PartRole.Hull),
                          .. Arm(StandShoulderR, new(-0.20f, -0.28f, 1.15f), new(-0.32f, -0.12f, 0.85f), PartRole.Hull)]
            : [.. Arm(StandShoulderL, new(0.20f, 0.32f, 1.25f), new(0.16f, 0.20f, 1.58f), PartRole.Hull),
               .. Arm(StandShoulderR, new(0.20f, -0.32f, 1.25f), new(0.18f, -0.20f, 1.60f), PartRole.Hull)];
        for (int i = 0; i < 4; i++) parts[TorsoIndex + 2 + i] = arms[i];
        if (soldier && parts.Length > TorsoIndex + 6)
            parts[TorsoIndex + 6] = Limb(new(-0.16f, 0.22f, 1.55f), new(-0.14f, -0.24f, 0.85f), 0.05f, 0.08f, ColourRole.Metal);   // slung
        return parts;
    }

    /// <summary>⭐ CE-1085 … CE-1090 — a soldier's weapon part in the shared order (after the four leg parts, torso, head and four arm parts).</summary>
    public const int WeaponIndex = TorsoIndex + 6;

    /// <summary>
    /// ⭐ CE-1088 (docs/DESIGN_Map_3D_Mode.md §6n N-2) — a man AT HIS WEAPON: kneeling (<paramref name="kneel"/>, the kneel's legs) or
    /// standing, both hands forward on its grips or handles (0.5 m in front of him, at the grips' height), a soldier's rifle slung across his
    /// back. The figure's frame is placed at the weapon's gunner spot (<see cref="WeaponKits.GunnerSpot"/>) or his seat.
    /// </summary>
    public static KitPart[] AtWeapon(bool kneel, bool soldier)
    {
        var body = kneel ? CrouchedBody : StandingBody;
        var drop = kneel ? CrouchDrop : Vector3.Zero;
        float gripZ = kneel ? 0.62f : 0.98f;
        var l = StandShoulderL + drop;
        var r = StandShoulderR + drop;
        KitPart[] parts =
        [
            .. body,
            .. Arm(l, l + new Vector3(0.24f, 0.02f, -0.20f), new Vector3(0.50f, 0.10f, gripZ), PartRole.Hull),
            .. Arm(r, r + new Vector3(0.24f, -0.02f, -0.20f), new Vector3(0.50f, -0.10f, gripZ), PartRole.Hull),
        ];
        if (!soldier) return parts;
        var back = drop + new Vector3(0f, 0f, 0f);
        return [.. parts, Limb(new Vector3(-0.16f, 0.22f, 1.55f) + back, new Vector3(-0.14f, -0.24f, 0.85f) + back, 0.05f, 0.08f, ColourRole.Metal)];
    }

    /// <summary>The length and diameter of a shoulder-fired tube (m) — a Javelin's, near enough for an NLAW or an RPG.</summary>
    public const float TubeLength = 1.10f, TubeDiameter = 0.13f;

    /// <summary>
    /// ⭐ CE-1090 (§6n N-4) — the soldier's rifle replaced by a launcher TUBE on his shoulder, along the rifle's line (his aim): the tube
    /// sits a little higher and further back than the rifle, so a third of it sticks out behind the shoulder (where the backblast leaves).
    /// Only the weapon part changes — the hands stay where they hold the rifle. A figure with no weapon part is returned as it is.
    /// </summary>
    public static KitPart[] Shouldered(KitPart[] parts)
    {
        if (parts.Length <= WeaponIndex) return parts;
        var rifle = parts[WeaponIndex];
        float p = rifle.PitchDegrees * MathF.PI / 180f, y = rifle.YawDegrees * MathF.PI / 180f;
        var dir = new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));   // the rifle's +X, in the body
        var centre = rifle.Centre * S - dir * 0.18f + new Vector3(0f, 0f, 0.06f);
        var shouldered = (KitPart[])parts.Clone();
        shouldered[WeaponIndex] = rifle with
        {
            Shape = PartShape.Cylinder, Axis = PartAxis.X, Centre = centre / S,
            Extent = new Vector3(TubeLength, 0f, TubeDiameter) / S, Colour = ColourRole.Body,
        };
        return shouldered;
    }

    /// <summary>⭐ CE-1081 — how far a carrier's upper body leans under the load (radians): carrying ~11°, dragging ~17°.</summary>
    public const float CarryLean = 0.20f, DragLean = 0.30f;

    /// <summary>
    /// ⭐ CE-1083 (C-3) — SEATED, his hips the figure's origin: on a bench (<paramref name="legsOut"/> false) thighs forward and shins down
    /// to the floor; on a sill (true) legs hanging out over its edge. Upper body upright, hands on his thighs — a soldier holds his rifle
    /// upright between his knees.
    /// </summary>
    public static KitPart[] Seated(bool legsOut, bool soldier)
    {
        var legs = legsOut
            ? Legs(new(0f, 0.11f, 0f), new(0.30f, 0.13f, -0.80f), new(0f, -0.11f, 0f), new(0.30f, -0.13f, -0.80f), swings: false)
            : Legs(new(0f, 0.11f, 0f), new(0.46f, 0.11f, -0.44f), new(0f, -0.11f, 0f), new(0.46f, -0.11f, -0.44f), swings: false,
                   bend: new Vector3(1f, 0f, 1f));
        var shoulderL = new Vector3(0f, 0.26f, 0.53f);
        var shoulderR = new Vector3(0f, -0.26f, 0.53f);
        KitPart[] body =
        [
            .. legs,
            P(-0.02f, 0f, 0.28f, 0.24f, 0.40f, 0.56f, ColourRole.Body, upper: true),
            Head(0f, 0f, 0.70f, 0.21f, 0.20f, 0.24f, FaceOn.Front, upper: true),
        ];
        if (!soldier)
            return [.. body, .. Arm(shoulderL, new(0.12f, 0.30f, 0.28f), new(0.30f, 0.16f, 0.05f), PartRole.Hull),
                             .. Arm(shoulderR, new(0.12f, -0.30f, 0.28f), new(0.30f, -0.16f, 0.05f), PartRole.Hull)];
        return [.. body, .. Arm(shoulderL, new(0.16f, 0.26f, 0.30f), new(0.32f, 0.04f, 0.18f), PartRole.Hull),
                         .. Arm(shoulderR, new(0.16f, -0.26f, 0.24f), new(0.30f, -0.04f, 0.02f), PartRole.Hull),
                         Limb(new(0.28f, 0f, -0.32f), new(0.34f, 0f, 0.48f), 0.05f, 0.08f, ColourRole.Metal)];
    }

    /// <summary>
    /// ⭐ CE-1082 (C-2) — DOWN BUT ALIVE (incapacitated): on his back like the dead, but one knee up and a hand on his chest — alive,
    /// hurt; a soldier's rifle dropped beside him.
    /// </summary>
    public static readonly KitPart[] DownedBody =
    [
        .. Leg(new(-0.17f, 0.12f, 0.08f), new(-1.02f, 0.22f, 0.07f), true, swings: false),
        .. Leg(new(-0.17f, -0.12f, 0.08f), new(-0.62f, -0.14f, 0.07f), false, swings: false, bend: Vector3.UnitZ),   // knee up
        P(0.02f, 0f, 0.12f, 0.60f, 0.42f, 0.22f, ColourRole.Body),
        Head(0.46f, 0f, 0.12f, 0.22f, 0.20f, 0.21f, FaceOn.Up),
        .. Arm(new(0.28f, 0.21f, 0.10f), new(0.14f, 0.30f, 0.20f), new(0.08f, 0.02f, 0.27f), PartRole.Hull),        // hand on his chest
        .. Arm(new(0.28f, -0.21f, 0.08f), new(0.36f, -0.48f, 0.06f), new(0.30f, -0.76f, 0.06f), PartRole.Hull),
    ];

    /// <summary>⭐ CE-1082 — the downed figure.</summary>
    public static KitPart[] Downed(bool soldier) => soldier ? [.. DownedBody, RifleDropped] : DownedBody;

    /// <summary>⭐ CE-1082 — the collapse: <paramref name="from"/> part-way to <see cref="Downed"/> (<paramref name="t"/> 0..1, as the fall).</summary>
    public static KitPart[] Collapse(KitPart[] from, float t, bool soldier) => Lerp(from, Downed(soldier), t);

    /// <summary>⭐ CE-1082 — a WOUNDED man's limp (radians / shares): his upper body bent forward ~15°, steps this much shorter.</summary>
    public const float WoundedLean = 0.26f, WoundedStepCut = 0.30f;

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
                Face = t < 0.5f ? a[i].Face : b[i].Face,   // ⭐ CE-1097 (§6o F-4) — the face turns over halfway through a fall
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
        => PostureLerp(Parts(stance, soldier, from), Parts(stance, soldier, to), from, to, t);

    /// <summary>The same posture blend over a per-posture set of poses (index = <see cref="WeaponPostureKind"/>, 0..2).</summary>
    private static KitPart[] PostureMix(KitPart[][] byPosture, WeaponPostureKind from, WeaponPostureKind to, float t)
        => PostureLerp(byPosture[Math.Clamp((int)from, 0, 2)], byPosture[Math.Clamp((int)to, 0, 2)], from, to, t);

    private static KitPart[] PostureLerp(KitPart[] a, KitPart[] b, WeaponPostureKind from, WeaponPostureKind to, float t)
    {
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
                SwingPivot = a[i].SwingPivot is { } sa && b[i].SwingPivot is { } sb ? Vector3.Lerp(sa, sb, t) : b[i].SwingPivot,
                KneePivot = a[i].KneePivot is { } ka && b[i].KneePivot is { } kb ? Vector3.Lerp(ka, kb, t) : b[i].KneePivot,
            };
        return blended;
    }

    /// <summary>⭐ WL-6b — a stance blend whose two ends are themselves part-way between two weapon postures (a posture change during a
    /// stance change).</summary>
    public static KitPart[] Blend(StanceId from, StanceId to, float t, bool soldier, WeaponPostureKind postureFrom, WeaponPostureKind postureTo,
                                  float postureT)
        => Lerp(Parts(from, soldier, postureFrom, postureTo, postureT), Parts(to, soldier, postureFrom, postureTo, postureT), t);

    // ── ⭐ WL-6d — the CRAWL: an elbow-and-knee combat crawl, procedural, one cycle per MotionTracker.CrawlMetres ────────────────

    private static readonly Vector3 CrawlHipL = new(-0.20f, 0.12f, 0.09f), CrawlHipR = new(-0.20f, -0.12f, 0.09f);

    /// <summary>
    /// ⭐ WL-6d — the prone figure CRAWLING, part-way (<paramref name="weight"/> 0..1, how much he is moving) from <paramref name="still"/>
    /// (his prone pose) into the crawl at <paramref name="phase"/>: the elbows take turns reaching forward and pulling (lifted a little as
    /// they come forward), the OPPOSITE knee draws up and out to the side, the body stays flat with a small shimmy; a soldier carries his
    /// rifle in his right hand, never aimed. Same parts in the same order as <paramref name="still"/>.
    /// </summary>
    public static KitPart[] Crawl(KitPart[] still, bool soldier, float phase, float weight)
    {
        weight = Math.Clamp(weight, 0f, 1f);
        if (weight <= 0f) return still;
        var pose = CrawlPose(soldier, phase);
        if (pose.Length != still.Length) return still;
        return Mix(still, pose, weight);
    }

    /// <summary>The crawl itself at <paramref name="phase"/> (sin φ > 0: the LEFT elbow reaching, the RIGHT knee drawn up).</summary>
    public static KitPart[] CrawlPose(bool soldier, float phase)
    {
        float reachL = MathF.Sin(phase), reachR = -reachL;                 // + = that elbow forward
        float liftL = MathF.Max(0f, MathF.Cos(phase)), liftR = MathF.Max(0f, -MathF.Cos(phase));   // lifted while coming forward
        float pullR = MathF.Max(0f, reachL), pullL = MathF.Max(0f, reachR);                        // the opposite knee draws up
        float shimmy = 4f * reachL;                                          // degrees: the body twists a little toward the reaching arm

        Vector3 Ankle(float pull, float side) => Vector3.Lerp(new(-1.06f, 0.14f * side, 0.07f), new(-0.66f, 0.42f * side, 0.07f), pull);
        // elbows out wide (a crawl reads from above by its limbs), forearms forward and a little in
        Vector3 Elbow(float reach, float lift, float side) => new(0.44f + 0.17f * reach, 0.31f * side, 0.05f + 0.05f * lift);
        var elbowL = Elbow(reachL, liftL, 1f); var elbowR = Elbow(reachR, liftR, -1f);
        var handL = elbowL + new Vector3(0.25f, -0.06f, 0.03f + 0.03f * liftL);
        var handR = elbowR + new Vector3(0.25f, 0.06f, 0.03f + 0.03f * liftR);
        KitPart[] parts =
        [
            .. Leg(CrawlHipL, Ankle(pullL, 1f), true, swings: false, bend: new Vector3(0.3f, 1f, 0.1f)),
            .. Leg(CrawlHipR, Ankle(pullR, -1f), false, swings: false, bend: new Vector3(0.3f, -1f, 0.1f)),
            P(0.04f, 0f, 0.14f, 0.60f, 0.42f, 0.24f, ColourRole.Body) with { YawDegrees = shimmy },
            Head(0.50f, 0f, 0.20f, 0.24f, 0.20f, 0.21f, FaceOn.Front) with { YawDegrees = shimmy * 1.5f },
            .. Arm(ProneShoulderL + new Vector3(0f, 0f, 0.01f), elbowL, handL, PartRole.Hull),
            .. Arm(ProneShoulderR + new Vector3(0f, 0f, 0.01f), elbowR, handR, PartRole.Hull),
        ];
        if (!soldier) return parts;
        // the rifle in the right hand, held a quarter along, muzzle forward and a little in — dragged along with that arm
        var dir = Vector3.Normalize(new Vector3(1f, 0.10f, -0.02f));
        var stock = handR + new Vector3(0f, 0f, 0.03f) - dir * (RifleLength * 0.25f);
        return [.. parts, Limb(stock, stock + dir * RifleLength, 0.05f, 0.08f, ColourRole.Metal)];
    }

    /// <summary>Part-way from <paramref name="a"/> to <paramref name="b"/>, every part keeping <paramref name="b"/>'s role.</summary>
    private static KitPart[] Mix(KitPart[] a, KitPart[] b, float t)
    {
        var mixed = new KitPart[b.Length];
        for (int i = 0; i < b.Length; i++)
            mixed[i] = b[i] with
            {
                Centre = Vector3.Lerp(a[i].Centre, b[i].Centre, t),
                Extent = Vector3.Lerp(a[i].Extent, b[i].Extent, t),
                YawDegrees = a[i].YawDegrees + (b[i].YawDegrees - a[i].YawDegrees) * t,
                PitchDegrees = a[i].PitchDegrees + (b[i].PitchDegrees - a[i].PitchDegrees) * t,
            };
        return mixed;
    }

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
