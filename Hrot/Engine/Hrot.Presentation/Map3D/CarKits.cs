using System.Numerics;
using Fdp.Toolkit.Tkb.Domain;
using static Hrot.UI.Common.Map3D.ShapeKits;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1457 (<c>docs/DESIGN_Map_3D_Mode.md</c> §6ad-3, TC2) — seven block kits for the wheeled road vehicles a TKB type names by
/// <see cref="CarBodyStyle"/>, in the <see cref="ShapeKits"/> vocabulary (fractions of the type's size; x forward, y across, z up; wheels
/// 0.42 of the height across, so the roll animation and the ground contact are the car kit's). Each is its own silhouette — a bus is not a
/// stretched car.
/// </summary>
public static class CarKits
{
    /// <summary>A typical size (length, width, height in metres) of a style — what the TKB types are authored at.</summary>
    public static Vector3 TypicalSize(CarBodyStyle style) => style switch
    {
        CarBodyStyle.Sedan => new(4.6f, 1.8f, 1.45f),
        CarBodyStyle.Estate => new(4.7f, 1.82f, 1.5f),
        CarBodyStyle.SmallHatch => new(3.7f, 1.66f, 1.5f),
        CarBodyStyle.Suv => new(4.6f, 1.9f, 1.75f),
        CarBodyStyle.Van => new(5.3f, 2.0f, 2.3f),
        CarBodyStyle.Bus => new(12.0f, 2.55f, 3.0f),
        _ => new(8.0f, 2.5f, 3.4f),
    };

    /// <summary>The kit of a style.</summary>
    public static ShapeKit For(CarBodyStyle style) => style switch
    {
        CarBodyStyle.Sedan => Sedan,
        CarBodyStyle.Estate => Estate,
        CarBodyStyle.SmallHatch => SmallHatch,
        CarBodyStyle.Suv => Suv,
        CarBodyStyle.Van => Van,
        CarBodyStyle.Bus => Bus,
        _ => BoxTruck,
    };

    /// <summary>The style a type names (<see cref="VisualDefinitionDto.BodyStyle"/>), or null for none / an unknown name.</summary>
    public static CarBodyStyle? Parse(string? name)
        => !string.IsNullOrWhiteSpace(name) && System.Enum.TryParse<CarBodyStyle>(name, ignoreCase: true, out var s) && System.Enum.IsDefined(s) ? s : null;

    /// <summary>Four wheels on two axles at fractions <paramref name="xf"/> / <paramref name="xr"/> of the length, each with its spoke (as long as the wheel is wide across).</summary>
    private static KitPart[] Wheels(CarBodyStyle style, float xf, float xr, float track, float width)
    {
        var size = TypicalSize(style);
        float spoke = 0.42f * size.Z / size.X;   // the wheel's diameter, as a fraction of the length
        var parts = new List<KitPart>(8);
        foreach (float x in new[] { xf, xr })
            foreach (float y in new[] { track, -track })
            {
                parts.Add(Cyl(PartAxis.Y, x, y, 0.21f, 0f, width, 0.42f, ColourRole.Dark));
                parts.Add(new KitPart(PartShape.Box, new Vector3(x, y, 0.21f), new Vector3(spoke, width * 1.06f, 0.03f), ColourRole.Metal, Role: PartRole.WheelSpoke));
            }
        return parts.ToArray();
    }

    private static ShapeKit Make(CarBodyStyle style, float xf, float xr, float track, float width, params KitPart[] body)
        => new(body.Concat(Wheels(style, xf, xr, track, width)).ToArray(), default);

    /// <summary>Three boxes: a bonnet, a cabin, a boot.</summary>
    public static readonly ShapeKit Sedan = Make(CarBodyStyle.Sedan, 0.32f, -0.32f, 0.45f, 0.12f,
        Box(0f, 0f, 0.36f, 1.0f, 1.0f, 0.32f, ColourRole.Body),
        Box(0.37f, 0f, 0.56f, 0.26f, 0.98f, 0.08f, ColourRole.Body),               // bonnet
        Box(-0.37f, 0f, 0.58f, 0.26f, 0.98f, 0.12f, ColourRole.Body),              // boot
        Box(-0.06f, 0f, 0.72f, 0.46f, 0.88f, 0.30f, ColourRole.Glass),             // cabin
        Box(-0.06f, 0f, 0.89f, 0.44f, 0.86f, 0.04f, ColourRole.Body));             // roof

    /// <summary>A long roof running to the tail.</summary>
    public static readonly ShapeKit Estate = Make(CarBodyStyle.Estate, 0.32f, -0.32f, 0.45f, 0.12f,
        Box(0f, 0f, 0.36f, 1.0f, 1.0f, 0.32f, ColourRole.Body),
        Box(0.38f, 0f, 0.56f, 0.24f, 0.98f, 0.08f, ColourRole.Body),               // bonnet
        Box(-0.13f, 0f, 0.72f, 0.70f, 0.90f, 0.32f, ColourRole.Glass),             // a long cabin, to the tail
        Box(-0.13f, 0f, 0.90f, 0.70f, 0.88f, 0.04f, ColourRole.Body));             // the long roof

    /// <summary>Short and tall, a steep tail.</summary>
    public static readonly ShapeKit SmallHatch = Make(CarBodyStyle.SmallHatch, 0.30f, -0.30f, 0.45f, 0.12f,
        Box(0f, 0f, 0.36f, 1.0f, 1.0f, 0.34f, ColourRole.Body),
        Box(0.38f, 0f, 0.56f, 0.22f, 0.98f, 0.08f, ColourRole.Body),               // a short bonnet
        Box(-0.08f, 0f, 0.74f, 0.54f, 0.90f, 0.40f, ColourRole.Glass),             // a tall cabin
        Box(-0.08f, 0f, 0.95f, 0.50f, 0.88f, 0.04f, ColourRole.Body),              // roof
        Box(-0.38f, 0f, 0.70f, 0.14f, 0.90f, 0.40f, ColourRole.Glass, pitch: -28f)); // the steep tail

    /// <summary>High and upright, a big cabin.</summary>
    public static readonly ShapeKit Suv = Make(CarBodyStyle.Suv, 0.31f, -0.31f, 0.46f, 0.14f,
        Box(0f, 0f, 0.46f, 1.0f, 1.0f, 0.46f, ColourRole.Body),
        Box(-0.05f, 0f, 0.82f, 0.74f, 0.92f, 0.34f, ColourRole.Glass),             // a tall, boxy cabin
        Box(-0.05f, 0f, 0.995f, 0.72f, 0.90f, 0.03f, ColourRole.Body));            // roof (+ the spare wheel below)

    /// <summary>One box with a short bonnet.</summary>
    public static readonly ShapeKit Van = Make(CarBodyStyle.Van, 0.32f, -0.30f, 0.45f, 0.12f,
        Box(-0.06f, 0f, 0.56f, 0.88f, 1.0f, 0.80f, ColourRole.Body),               // the one box
        Box(0.44f, 0f, 0.34f, 0.12f, 0.98f, 0.26f, ColourRole.Body),               // a short bonnet
        Box(0.375f, 0f, 0.74f, 0.04f, 0.90f, 0.32f, ColourRole.Glass),             // the windscreen
        Box(0.22f, 0f, 0.76f, 0.26f, 1.02f, 0.22f, ColourRole.Glass));             // the cab's side windows

    /// <summary>A long box, a window band, a flat front, two long-spaced axles.</summary>
    public static readonly ShapeKit Bus = Make(CarBodyStyle.Bus, 0.33f, -0.30f, 0.46f, 0.07f,
        Box(0f, 0f, 0.53f, 1.0f, 1.0f, 0.72f, ColourRole.Body),
        Box(-0.01f, 0f, 0.69f, 0.94f, 1.02f, 0.26f, ColourRole.Glass),             // the window band
        Box(0.497f, 0f, 0.68f, 0.012f, 0.92f, 0.34f, ColourRole.Glass),            // the flat windscreen
        Box(0f, 0f, 0.905f, 0.98f, 0.9f, 0.04f, ColourRole.Dark));                 // roof gear

    /// <summary>A cab and a separate cargo box.</summary>
    public static readonly ShapeKit BoxTruck = Make(CarBodyStyle.BoxTruck, 0.36f, -0.30f, 0.45f, 0.08f,
        Box(0f, 0f, 0.27f, 1.0f, 0.86f, 0.10f, ColourRole.Dark),                   // the chassis
        Box(0.38f, 0f, 0.55f, 0.24f, 0.96f, 0.78f, ColourRole.Body),               // the cab
        Box(0.42f, 0f, 0.70f, 0.17f, 0.98f, 0.30f, ColourRole.Glass),              // its windows
        Box(-0.14f, 0f, 0.62f, 0.68f, 1.0f, 0.82f, ColourRole.Body));              // the cargo box, apart from the cab
}
