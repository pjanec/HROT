using System.Numerics;
using Hrot.Look.Devices;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ SD-S5d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10a D-163 / D-164; mockup <c>designs/devices/mockups/device-looks-visual.svg</c>) — the block
/// shape of each <see cref="DeviceKind"/> (the <see cref="WeaponKits"/> pattern): ONE kit per kind serves every form — at a vehicle station or stacked on a device
/// (<see cref="For"/>, drawn as the device's own body at its <c>DevicePoses</c> pose) and raised in a man's hands (<see cref="Held"/>, the same parts placed at his
/// hands). Parts are fractions of <see cref="DeviceLooks.SizeOf"/> (x forward along the view, y left, z up from the bottom). Built once: 0 B.
/// </summary>
public static class DeviceKits
{
    private static KitPart P(DeviceKind k, PartShape shape, PartAxis axis, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c)
    {
        var s = DeviceLooks.SizeOf(k);
        return new KitPart(shape, new Vector3(cx / s.X, cy / s.Y, cz / s.Z), new Vector3(ex / s.X, ey / s.Y, ez / s.Z), c, axis);
    }

    private static KitPart B(DeviceKind k, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c) => P(k, PartShape.Box, PartAxis.Z, cx, cy, cz, ex, ey, ez, c);
    private static KitPart X(DeviceKind k, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c) => P(k, PartShape.Cylinder, PartAxis.X, cx, cy, cz, ex, ey, ez, c);

    private static ShapeKit Build(DeviceKind k) => new(k switch
    {
        DeviceKind.Binoculars =>
        [
            X(k, 0f, 0.045f, 0.035f, 0.14f, 0.065f, 0.065f, ColourRole.Body), X(k, 0f, -0.045f, 0.035f, 0.14f, 0.065f, 0.065f, ColourRole.Body),   // the barrels
            X(k, 0.069f, 0.045f, 0.035f, 0.004f, 0.05f, 0.05f, ColourRole.Glass), X(k, 0.069f, -0.045f, 0.035f, 0.004f, 0.05f, 0.05f, ColourRole.Glass),   // the objectives
            B(k, -0.02f, 0f, 0.04f, 0.05f, 0.03f, 0.025f, ColourRole.Dark),                                                                   // the bridge
        ],
        DeviceKind.Rangefinder =>
        [
            B(k, 0f, 0f, 0.04f, 0.20f, 0.18f, 0.08f, ColourRole.Body),                                                                      // the body
            X(k, 0.099f, 0.045f, 0.04f, 0.004f, 0.05f, 0.05f, ColourRole.Glass), X(k, 0.099f, -0.045f, 0.04f, 0.004f, 0.05f, 0.05f, ColourRole.Glass),   // the receivers
            B(k, 0.045f, 0f, 0.09f, 0.09f, 0.06f, 0.02f, ColourRole.Body),                                                                  // the emitter housing on top
            B(k, 0.092f, 0f, 0.09f, 0.008f, 0.03f, 0.014f, ColourRole.Red),                                                                 // the emitter aperture
            B(k, -0.104f, 0.045f, 0.045f, 0.012f, 0.04f, 0.035f, ColourRole.Dark), B(k, -0.104f, -0.045f, 0.045f, 0.012f, 0.04f, 0.035f, ColourRole.Dark),   // the eyepieces
        ],
        DeviceKind.Designator =>
        [
            B(k, 0f, 0f, 0.085f, 0.38f, 0.16f, 0.17f, ColourRole.Body),
            X(k, 0.193f, 0f, 0.085f, 0.006f, 0.11f, 0.11f, ColourRole.Glass),                                                               // the big aperture
            B(k, -0.195f, 0f, 0.10f, 0.012f, 0.05f, 0.05f, ColourRole.Dark),                                                                 // the eyepiece
            B(k, 0f, 0f, 0.175f, 0.10f, 0.03f, 0.01f, ColourRole.Dark),                                                                     // the handle
        ],
        DeviceKind.DroneController =>
        [
            B(k, 0f, 0f, 0.09f, 0.04f, 0.30f, 0.18f, ColourRole.Body),
            B(k, 0.021f, 0f, 0.10f, 0.004f, 0.15f, 0.11f, ColourRole.Glass),                                                                // the screen
            B(k, 0.025f, 0.12f, 0.05f, 0.02f, 0.025f, 0.025f, ColourRole.Dark), B(k, 0.025f, -0.12f, 0.05f, 0.02f, 0.025f, 0.025f, ColourRole.Dark),   // the sticks
            B(k, 0f, -0.12f, 0.19f, 0.008f, 0.008f, 0.02f, ColourRole.Dark),                                                                // the antenna
        ],
        DeviceKind.Console =>
        [
            B(k, 0f, 0f, 0.125f, 0.12f, 0.35f, 0.25f, ColourRole.Body),
            B(k, 0.061f, 0.04f, 0.15f, 0.004f, 0.2f, 0.13f, ColourRole.Glass),                                                              // the screen
            B(k, 0.062f, -0.13f, 0.15f, 0.006f, 0.05f, 0.09f, ColourRole.Metal),                                                            // the keys
        ],
        _ =>
        [
            B(k, 0f, 0f, 0.075f, 0.30f, 0.20f, 0.15f, ColourRole.Body),
            B(k, 0.152f, 0.05f, 0.09f, 0.01f, 0.02f, 0.02f, ColourRole.Metal), B(k, 0.152f, 0f, 0.09f, 0.01f, 0.02f, 0.02f, ColourRole.Metal),
            B(k, 0.152f, -0.05f, 0.09f, 0.01f, 0.02f, 0.02f, ColourRole.Metal),                                                             // the switches
        ],
    }, default);

    private static readonly ShapeKit[] Kits = BuildAll();

    private static ShapeKit[] BuildAll()
    {
        var all = new ShapeKit[(int)DeviceKind.Console + 1];
        for (int i = 0; i < all.Length; i++) all[i] = Build((DeviceKind)i);
        return all;
    }

    /// <summary>The kit of <paramref name="kind"/> — the device drawn as its own body (at a station, stacked, later on a tripod or lying).</summary>
    public static ShapeKit For(DeviceKind kind) => (int)kind < Kits.Length ? Kits[(int)kind] : Kits[0];

    /// <summary>The role a held device's BODY parts are drawn in: a man's parts shade HIS colour, so the device keeps its own — olive / tan ⇒ the fixed canvas
    /// colour, dark ⇒ dark (<see cref="DeviceLooks.ColourOf"/> in the nearest fixed role).</summary>
    public static ColourRole HeldBodyRole(DeviceKind kind) => kind is DeviceKind.Rangefinder or DeviceKind.Designator or DeviceKind.Box ? ColourRole.Canvas : ColourRole.Dark;

    private static readonly KitPart[][] HeldKits = BuildHeld();

    private static KitPart[][] BuildHeld()
    {
        var all = new KitPart[Kits.Length][];
        for (int i = 0; i < all.Length; i++)
        {
            var kit = Kits[i].Parts;
            var held = new KitPart[kit.Length];
            var role = HeldBodyRole((DeviceKind)i);
            for (int j = 0; j < kit.Length; j++) held[j] = kit[j].Colour == ColourRole.Body ? kit[j] with { Colour = role } : kit[j];
            all[i] = held;
        }
        return all;
    }

    /// <summary>
    /// D-164 ① — <paramref name="kind"/>'s parts placed in a FIGURE's space (fractions of the figure's size <paramref name="figure"/>): its back face at
    /// <paramref name="hands"/> (metres in the figure's frame, x forward), looking forward, centred on the hands' height. Allocates its array (a held item's arms
    /// allocate already, <see cref="BlockFigure.HeldItemArms"/>); the device's own parts are cached.
    /// </summary>
    public static KitPart[] Held(DeviceKind kind, Vector3 hands, Vector3 figure)
    {
        var size = DeviceLooks.SizeOf(kind);
        var parts = HeldKits[(int)kind < HeldKits.Length ? (int)kind : 0];
        var origin = hands + new Vector3(size.X / 2f - 0.02f, 0f, -size.Z / 2f);   // the device's bottom-centre: its back face just past the hands
        var placed = new KitPart[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            var centre = origin + p.Centre * size;
            placed[i] = p with { Centre = centre / figure, Extent = p.Extent * size / figure, Upper = true };
        }
        return placed;
    }
}
