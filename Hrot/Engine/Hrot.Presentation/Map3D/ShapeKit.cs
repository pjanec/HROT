using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>The primitive a kit part is drawn with (one shared unit mesh each).</summary>
public enum PartShape : byte { Box, Cylinder }

/// <summary>Which body axis a cylinder runs along (x = forward, y = left, z = up).</summary>
public enum PartAxis : byte { X, Y, Z }

/// <summary>Which colour a part takes: the entity's colour, or a fixed material shade.</summary>
public enum ColourRole : byte { Body, Dark, Glass, Metal }

/// <summary>
/// One part of a shape kit. <paramref name="Centre"/> and <paramref name="Extent"/> are FRACTIONS of the type's length (x,
/// forward), width (y, left) and height (z, up), measured from the footprint centre on the ground. A cylinder's length is its
/// extent along <paramref name="Axis"/>; its diameter is the height fraction (or the length fraction for an upright one).
/// <paramref name="PitchDegrees"/> tilts the part nose-down about the body's y axis (a sloped glacis).
/// </summary>
public readonly record struct KitPart(
    PartShape Shape, Vector3 Centre, Vector3 Extent, ColourRole Colour, PartAxis Axis = PartAxis.Z, float PitchDegrees = 0f)
{
    /// <summary>The part's transform in BODY space (metres, before the entity's own rotation and position).</summary>
    public Matrix4x4 BodyTransform(Vector3 size)
    {
        var centre = Centre * size;
        Matrix4x4 local;
        if (Shape == PartShape.Box)
        {
            local = Matrix4x4.CreateScale(Extent * size);
        }
        else
        {
            // The unit cylinder runs along +Z from 0 to 1 with radius 0.5: centre it, size it, lay it along its axis.
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
        return local * Matrix4x4.CreateTranslation(centre);
    }
}

/// <summary>
/// ⭐ CE-1033 S1 — what each kind of entity looks like in 3-D (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7): a short list of parts
/// as fractions of the type's size, chosen by <see cref="VisualFamily"/>. Static in S1 (wheel roll, rotor spin, limb swing: S4).
/// </summary>
public static class ShapeKits
{
    private static KitPart Box(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, float pitch = 0f)
        => new(PartShape.Box, new Vector3(cx, cy, cz), new Vector3(ex, ey, ez), c, PartAxis.Z, pitch);

    private static KitPart Cyl(PartAxis axis, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c)
        => new(PartShape.Cylinder, new Vector3(cx, cy, cz), new Vector3(ex, ey, ez), c, axis);

    public static readonly KitPart[] Tank =
    {
        Box(0f, 0.38f, 0.19f, 1.0f, 0.24f, 0.38f, ColourRole.Dark),       // track, left
        Box(0f, -0.38f, 0.19f, 1.0f, 0.24f, 0.38f, ColourRole.Dark),      // track, right
        Box(0f, 0f, 0.44f, 0.92f, 0.56f, 0.36f, ColourRole.Body),         // hull
        Box(-0.07f, 0f, 0.76f, 0.44f, 0.52f, 0.30f, ColourRole.Body),     // turret, set back
        Cyl(PartAxis.X, 0.42f, 0f, 0.78f, 0.56f, 0f, 0.07f, ColourRole.Metal),   // barrel, forward
    };

    public static readonly KitPart[] Afv =
    {
        Box(0f, 0.38f, 0.17f, 1.0f, 0.24f, 0.34f, ColourRole.Dark),
        Box(0f, -0.38f, 0.17f, 1.0f, 0.24f, 0.34f, ColourRole.Dark),
        Box(-0.05f, 0f, 0.46f, 0.84f, 0.58f, 0.40f, ColourRole.Body),     // hull
        Box(0.39f, 0f, 0.44f, 0.20f, 0.58f, 0.30f, ColourRole.Body, 30f), // sloped front wedge
        Box(-0.05f, 0f, 0.74f, 0.30f, 0.36f, 0.18f, ColourRole.Body),     // small turret
        Cyl(PartAxis.X, 0.24f, 0f, 0.76f, 0.40f, 0f, 0.04f, ColourRole.Metal),   // thin barrel
    };

    public static readonly KitPart[] WheeledCar =
    {
        Box(0f, 0f, 0.38f, 1.0f, 1.0f, 0.40f, ColourRole.Body),           // lower body
        Box(-0.05f, 0f, 0.76f, 0.52f, 0.90f, 0.34f, ColourRole.Glass),    // cabin
        Cyl(PartAxis.Y, 0.32f, 0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, 0.32f, -0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.32f, 0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.32f, -0.45f, 0.21f, 0f, 0.12f, 0.42f, ColourRole.Dark),
    };

    public static readonly KitPart[] WheeledUtility =
    {
        Box(0f, 0f, 0.42f, 1.0f, 1.0f, 0.38f, ColourRole.Body),           // wide low body
        Box(0.06f, 0f, 0.78f, 0.36f, 0.92f, 0.34f, ColourRole.Glass),     // cab
        Box(-0.31f, 0f, 0.68f, 0.38f, 0.96f, 0.14f, ColourRole.Body),     // rear bed
        Cyl(PartAxis.Y, 0.33f, 0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, 0.33f, -0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.33f, 0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
        Cyl(PartAxis.Y, -0.33f, -0.43f, 0.21f, 0f, 0.16f, 0.42f, ColourRole.Dark),
    };

    public static readonly KitPart[] Unknown = { Box(0f, 0f, 0.5f, 1f, 1f, 1f, ColourRole.Body) };

    /// <summary>The kit of a family (a person's is posed — <see cref="BlockFigure.Parts"/>); empty for a unit.</summary>
    public static KitPart[] For(VisualFamily family) => family switch
    {
        VisualFamily.Tank => Tank,
        VisualFamily.Afv => Afv,
        VisualFamily.WheeledCar => WheeledCar,
        VisualFamily.WheeledUtility => WheeledUtility,
        VisualFamily.Unit => System.Array.Empty<KitPart>(),
        _ => Unknown,
    };

    /// <summary>A family's size (length, width, height in metres) when the TKB gives none.</summary>
    public static Vector3 DefaultSize(VisualFamily family) => family switch
    {
        VisualFamily.Person => new Vector3(0.35f, 0.55f, 1.8f),
        VisualFamily.Tank => new Vector3(7.9f, 3.7f, 2.4f),
        VisualFamily.Afv => new Vector3(6.5f, 3.3f, 2.6f),
        VisualFamily.WheeledCar => new Vector3(4.5f, 1.8f, 1.5f),
        VisualFamily.WheeledUtility => new Vector3(4.6f, 2.2f, 1.8f),
        _ => new Vector3(2f, 2f, 2f),
    };
}

/// <summary>
/// ⭐ CE-1033 S1 — the person kit: a six-box block figure (U14) posed by the shared stance rule (<see cref="LogicalStance"/>,
/// M7), with a rifle for a soldier. Parts are fractions of a 1.8 m standing figure's size (<see cref="ShapeKits.DefaultSize"/>),
/// so a taller or shorter TKB person scales. Static poses in S1; blending and limb swing are S4.
/// </summary>
public static class BlockFigure
{
    // Metres for a 1.8 m figure, converted to fractions of (0.35, 0.55, 1.8) below.
    private static KitPart P(float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c)
    {
        var s = new Vector3(0.35f, 0.55f, 1.8f);
        return new KitPart(PartShape.Box, new Vector3(cx, cy, cz) / s, new Vector3(ex, ey, ez) / s, c);
    }

    private static readonly KitPart[] Standing =
    {
        P(0f, 0.11f, 0.45f, 0.15f, 0.13f, 0.90f, ColourRole.Dark),      // legs
        P(0f, -0.11f, 0.45f, 0.15f, 0.13f, 0.90f, ColourRole.Dark),
        P(0f, 0f, 1.18f, 0.24f, 0.40f, 0.56f, ColourRole.Body),         // torso
        P(0f, 0f, 1.60f, 0.21f, 0.20f, 0.24f, ColourRole.Body),         // head
        P(0f, 0.27f, 1.13f, 0.11f, 0.11f, 0.60f, ColourRole.Body),      // arms
        P(0f, -0.27f, 1.13f, 0.11f, 0.11f, 0.60f, ColourRole.Body),
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

    /// <summary>The posed parts for <paramref name="stance"/> (anything but crouched or prone stands — the rule's own default).</summary>
    public static KitPart[] Parts(StanceId stance, bool soldier) => stance switch
    {
        StanceId.Crouched => soldier ? CrouchedArmed : Crouched,
        StanceId.Prone => soldier ? ProneArmed : Prone,
        _ => soldier ? StandingArmed : Standing,
    };
}
