using System.Globalization;

namespace Hrot.Look;

/// <summary>
/// ⭐ CE-4671 LK-4 (DESIGN_Vehicle_Appearance_State.md §7, §7c) — a colour as plain bytes: what the look library hands every renderer (⛔ never a renderer's own colour
/// type). Laid out and named like the map's colour (R, G, B, A), so the outfit rules moved here compile as they were; the map converts when it draws.
/// </summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    /// <summary>From four 0…255 ints (clamped) — the outfit tables' form.</summary>
    public Rgba(int r, int g, int b, int a) : this(Clamp(r), Clamp(g), Clamp(b), Clamp(a)) { }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    /// <summary>"#RRGGBB" (an "#RRGGBBAA" is read as its colour, opaque — as the map always read the TKB's colours), else null. Culture-neutral (R-4221);
    /// a span, 0 B (CE-4555: every body's colour is read each frame). Moved from the map's <c>EntityBodyLayer3D.ParseHex</c>, which forwards.</summary>
    public static Rgba? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.AsSpan().Trim().TrimStart('#');
        if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;
        return new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
    }
}
