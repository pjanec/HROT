using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-4671 LK-4 / LK-S2 — the ONE place the map converts the look library's plain colour (<see cref="Hrot.Look.Rgba"/>) to and from its own drawing
/// colour: the library never sees a renderer's type, and every draw site converts here.</summary>
public static class LookColours
{
    public static Color ToColor(this Hrot.Look.Rgba c) => new(c.R, c.G, c.B, c.A);
    public static Color? ToColor(this Hrot.Look.Rgba? c) => c is { } v ? new Color(v.R, v.G, v.B, v.A) : null;
    public static Hrot.Look.Rgba ToRgba(this Color c) => new(c.R, c.G, c.B, c.A);
    public static Hrot.Look.Rgba? ToRgba(this Color? c) => c is { } v ? new Hrot.Look.Rgba(v.R, v.G, v.B, v.A) : null;
}
