using System.Globalization;
using Hrot.Core.Tkb;
using Color = Hrot.Look.Rgba;   // ⭐ CE-4671 LK-S2 — plain bytes, no renderer colour (the map converts when it draws)

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-4648 (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-3) — the colours of what people wear, parsed ONCE from the TKB data
/// (<see cref="OutfitCatalog"/>, <c>#RRGGBB</c>) into drawing colours. The palettes used to be arrays in <c>Outfits.cs</c>; the values and the
/// ORDER are the same, because the dressing picks by index — so every existing frame is pixel-identical.
/// </summary>
internal static class OutfitPalettes
{
    /// <summary><c>#RRGGBB</c> (or <c>RRGGBB</c>) → an opaque colour. ⛔ Throws on anything else: a catalogue typo must fail loudly, not draw black.</summary>
    public static Color Parse(string hex)
    {
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            throw new FormatException($"an outfit colour must be #RRGGBB, not '{hex}'");
        return new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)255);
    }

    public static Color[] Of(string[] hex)
    {
        var colours = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++) colours[i] = Parse(hex[i]);
        return colours;
    }

    public static readonly Color[] SkinTones = Of(OutfitCatalog.SkinTones);
    public static readonly Color[] HairColours = Of(OutfitCatalog.HairColours);
    public static readonly Color[] Tops = Of(OutfitCatalog.Tops);
    public static readonly Color[] ChildTops = Of(OutfitCatalog.ChildTops);
    public static readonly Color[] Bottoms = Of(OutfitCatalog.Bottoms);
    public static readonly Color[] Suits = Of(OutfitCatalog.Suits);
    public static readonly Color[] Ties = Of(OutfitCatalog.Ties);
    public static readonly Color[] GreyHair = Of(OutfitCatalog.GreyHair);
    public static readonly Color[] MutedTops = Of(OutfitCatalog.MutedTops);
    public static readonly Color[] Woodland = Of(OutfitCatalog.Woodland);
    public static readonly Color[] Fleck = Of(OutfitCatalog.Fleck);
    public static readonly Color[] Desert = Of(OutfitCatalog.Desert);
    public static readonly Color[] EyeColours = Of(OutfitCatalog.EyeColours);
    public static readonly Color[] LipColours = Of(OutfitCatalog.LipColours);

    public static readonly Color Shirt = Parse(OutfitCatalog.Shirt);
    public static readonly Color PoliceUniform = Parse(OutfitCatalog.PoliceUniform);
    public static readonly Color PoliceVest = Parse(OutfitCatalog.PoliceVest);
    public static readonly Color PolicePouch = Parse(OutfitCatalog.PolicePouch);
    public static readonly Color PoliceBelt = Parse(OutfitCatalog.PoliceBelt);
    public static readonly Color CapBand = Parse(OutfitCatalog.CapBand);
    public static readonly Color WoodlandCloth = Parse(OutfitCatalog.WoodlandCloth);
    public static readonly Color DesertCloth = Parse(OutfitCatalog.DesertCloth);
}
