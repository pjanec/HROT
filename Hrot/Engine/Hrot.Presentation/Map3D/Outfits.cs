using System.Numerics;
using Fdp.Core;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q O-2) — what a civilian wears on his legs.</summary>
public enum Legwear : byte { Trousers, Shorts, Skirt }

/// <summary>⭐ CE-1099 (§6q O-2) — a civilian's hair: short, long (down his back) or none.</summary>
public enum HairStyle : byte { Short, Long, None }

/// <summary>⭐ CE-1099 (§6q O-4) — a soldier's camouflage, by his side: Friend woodland, Hostile fleck, Neutral none.</summary>
public enum CamoPattern : byte { None, Woodland, Fleck }

/// <summary>
/// ⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q) — what one man wears: the colour of each part of his figure (<see cref="Outfits.ColourOfPart"/>)
/// and the details drawn on them (<see cref="Outfits.Details"/>). Computed from his seed, so the same on every node and every frame.
/// </summary>
public readonly record struct Outfit(
    Color Top, Color Sleeve, Color Bottom, Color Shin, Color Skin, Color Hair, HairStyle HairStyle, Legwear Legwear,
    bool Suit, Color Tie, CamoPattern Camo, bool Child, bool Soldier, FaceLook Face = default);

/// <summary>⭐ CE-1101 (§6r S-3) — a moustache or a beard, or none.</summary>
public enum BeardKind : byte { None, Moustache, Beard }

/// <summary>⭐ CE-1101 (docs/DESIGN_Map_3D_Mode.md §6r S-3) — how one man's face is painted by the shader (<see cref="BodySurface"/>): where
/// his eyes sit (their distance from the middle, in the head box's unit frame), how wide his mouth is, how heavy his brows are, the colour
/// of his eyes and lips, and a moustache or beard.</summary>
public readonly record struct FaceLook(float EyeSpacing, float MouthHalfWidth, float BrowHalfHeight, BeardKind Beard, Color EyeColour, Color Lips);

/// <summary>⭐ CE-1099 (§6q) — a box drawn on figure part <paramref name="Part"/> (the shared part order), in that part's unit frame;
/// <paramref name="Turned"/> details (a tie, a skirt, long hair) are first turned onto the side the man faces (<see cref="BlockFigure.FaceFrame"/>,
/// by his head's <see cref="FaceOn"/>), so they lie right when he lies on his back.</summary>
public readonly record struct PartDetail(int Part, Vector3 Centre, Vector3 Extent, Color Colour, bool Turned = false);

/// <summary>
/// ⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q) — dressing the block figure. A civilian's clothes vary per man (O-1, O-2): his type's colour
/// when it has one, else everyday colours; sleeves, legwear, a suit, hair and skin drawn from his seed. A child is dressed brighter (O-3).
/// A soldier keeps his uniform, his trousers the same cloth a shade darker, and his SIDE adds a camouflage pattern (O-4). Every choice comes
/// from the seed (O-5).
/// </summary>
public static class Outfits
{
    // ── palettes ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    private static readonly Color[] SkinTones =
        [new(236, 200, 170, 255), new(214, 172, 136, 255), new(198, 150, 110, 255), new(160, 112, 76, 255), new(112, 76, 52, 255)];
    private static readonly Color[] HairColours =
        [new(30, 24, 20, 255), new(70, 48, 30, 255), new(140, 96, 56, 255), new(200, 170, 110, 255), new(150, 150, 150, 255), new(130, 60, 30, 255)];
    private static readonly Color[] Tops =
        [new(180, 40, 40, 255), new(40, 90, 160, 255), new(230, 230, 225, 255), new(60, 120, 70, 255), new(220, 180, 60, 255), new(120, 60, 130, 255),
         new(200, 110, 50, 255), new(90, 90, 96, 255), new(150, 190, 220, 255), new(210, 150, 160, 255), new(120, 90, 60, 255), new(30, 30, 34, 255)];
    private static readonly Color[] ChildTops =
        [new(230, 60, 60, 255), new(60, 140, 230, 255), new(250, 210, 40, 255), new(70, 190, 90, 255), new(240, 120, 190, 255), new(250, 150, 40, 255)];
    private static readonly Color[] Bottoms =
        [new(52, 72, 110, 255), new(150, 132, 96, 255), new(110, 110, 112, 255), new(36, 36, 38, 255), new(92, 66, 44, 255), new(40, 46, 70, 255)];
    private static readonly Color[] Suits = [new(52, 54, 58, 255), new(34, 40, 62, 255), new(28, 28, 30, 255), new(96, 98, 102, 255)];
    private static readonly Color[] Ties = [new(170, 30, 36, 255), new(40, 70, 150, 255), new(110, 30, 50, 255), new(40, 100, 60, 255)];
    private static readonly Color Shirt = new(238, 238, 236, 255);
    private static readonly Color[] GreyHair = [new(200, 200, 198, 255), new(170, 170, 168, 255), new(232, 230, 224, 255), new(140, 138, 134, 255)];
    private static readonly Color[] MutedTops =
        [new(120, 100, 80, 255), new(90, 96, 110, 255), new(150, 140, 120, 255), new(100, 110, 90, 255), new(130, 90, 90, 255), new(70, 72, 80, 255)];

    /// <summary>⭐ CE-1162 — an elderly civilian's outfit: grey hair (a man often balding), muted top and trousers or a long skirt.</summary>
    private static Outfit Elderly(Draw r, long seed, long type, Color skin, Color? own)
    {
        bool woman = r.Chance(0.5f);
        var hair = GreyHair[r.Pick(GreyHair.Length)];
        var style = woman ? (r.Chance(0.6f) ? HairStyle.Short : HairStyle.Long) : (r.Chance(0.4f) ? HairStyle.None : HairStyle.Short);
        var top = own is { } c ? Scale(c, 0.8f) : MutedTops[r.Pick(MutedTops.Length)];
        var legs = woman && r.Chance(0.5f) ? Legwear.Skirt : Legwear.Trousers;
        var bottom = Bottoms[r.Pick(Bottoms.Length)];
        return new Outfit(top, top, bottom, legs == Legwear.Trousers ? bottom : skin, skin, hair, style, legs, false, default, CamoPattern.None,
                          false, false, FaceOf(seed, type, woman, false));
    }

    /// <summary>⭐ CE-1162 — a security-force officer's uniform (navy).</summary>
    public static readonly Color PoliceUniform = new(34, 46, 82, 255);
    // ⚠ measured on the first frame: dark patches on olive vanished under the shading ⇒ each pattern mixes a LIGHT and a DARK tone
    private static readonly Color[] Woodland = [new(122, 92, 56, 255), new(26, 28, 22, 255), new(128, 150, 84, 255)];
    private static readonly Color[] Fleck = [new(214, 194, 140, 255), new(166, 156, 98, 255), new(126, 72, 42, 255)];   // sand-heavy: a hostile reads paler
    private static readonly Color[] EyeColours = [new(60, 40, 28, 255), new(96, 64, 36, 255), new(70, 110, 160, 255), new(80, 120, 80, 255), new(110, 116, 120, 255)];
    private static readonly Color[] LipColours = [new(132, 52, 48, 255), new(150, 70, 66, 255), new(118, 60, 56, 255), new(170, 60, 70, 255)];

    /// <summary>⭐ CE-1101 (§6r S-2) — the three tones the shader paints a pattern in (over the uniform): woodland brown, black and light
    /// green; fleck sand, olive and red-brown.</summary>
    public static (Color, Color, Color) CamoTones(CamoPattern pattern) => pattern == CamoPattern.Fleck
        ? (Fleck[0], Fleck[1], Fleck[2]) : (Woodland[0], Woodland[1], Woodland[2]);

    /// <summary>⭐ CE-1101 — where a man's pattern starts in the noise (metres of offset), so no two men wear the same cloth cut.</summary>
    public static float CamoSeed(long seed) => (new Draw(seed, 0xC4A0).Next()) * 97f;

    /// <summary>⭐ CE-1101 (§6r S-3) — the face of the man seeded <paramref name="seed"/>: the eyes 0.17–0.25 from the middle, a mouth
    /// 0.11–0.19 wide each side, brows of three weights, five eye colours, four lip colours, and for a grown man sometimes a moustache or a
    /// beard. Drawn from its own stream, so the clothes do not move when the face does.</summary>
    public static FaceLook FaceOf(long seed, long type, bool woman, bool child)
    {
        var r = new Draw(seed, type ^ 0xFACE);
        float eyes = 0.17f + r.Next() * 0.08f, mouth = 0.11f + r.Next() * 0.08f, brow = 0.018f + r.Pick(3) * 0.008f;
        float roll = r.Next();
        var beard = woman || child ? BeardKind.None : roll < 0.15f ? BeardKind.Moustache : roll < 0.30f ? BeardKind.Beard : BeardKind.None;
        return new FaceLook(eyes, mouth, brow, beard, EyeColours[r.Pick(EyeColours.Length)], LipColours[r.Pick(LipColours.Length)]);
    }

    /// <summary>
    /// ⭐ CE-1099 — the outfit of the man seeded <paramref name="seed"/> (his network id) of TKB type <paramref name="type"/>.
    /// <paramref name="own"/> is his colour when his entity or his type has one (null: none); for a <paramref name="soldier"/> it is his
    /// uniform (the family default when null). <paramref name="side"/> picks a soldier's camouflage.
    /// </summary>
    /// <param name="kit">⭐ CE-1162 (civilian U-1) — a <see cref="PersonKit.Security"/> officer wears the police uniform (navy, no
    /// camouflage: his side does not change his uniform), armed or not.</param>
    /// <param name="elderly">⭐ CE-1162 (U1-b) — an elderly civilian (the type's <c>PersonDto.Age</c>): grey or white hair, often thin on top,
    /// muted everyday clothes (no shorts, no bright tops), sometimes a cardigan.</param>
    public static Outfit For(long seed, long type, bool soldier, bool child, Color? own, ForceId side, PersonKit kit = PersonKit.Civilian,
                             bool elderly = false)
    {
        var r = new Draw(seed, type);
        var skin = SkinTones[r.Pick(SkinTones.Length)];
        var hair = HairColours[r.Pick(HairColours.Length)];
        if (elderly && kit == PersonKit.Civilian && !soldier && !child) return Elderly(r, seed, type, skin, own);
        if (kit == PersonKit.Security)
        {
            var uniform = own ?? PoliceUniform;
            var trousers = Scale(uniform, 0.8f);
            return new Outfit(uniform, uniform, trousers, trousers, skin, hair, HairStyle.Short, Legwear.Trousers, false, default, CamoPattern.None,
                              child, true, FaceOf(seed, type, woman: false, child));
        }
        if (soldier)
        {
            var uniform = own ?? new Color(96, 110, 72, 255);
            var trousers = Scale(uniform, 0.85f);
            var camo = side switch { ForceId.Friend => CamoPattern.Woodland, ForceId.Hostile => CamoPattern.Fleck, _ => CamoPattern.None };
            return new Outfit(uniform, uniform, trousers, trousers, skin, hair, HairStyle.Short, Legwear.Trousers, false, default, camo, child, true,
                              FaceOf(seed, type, woman: false, child));
        }

        bool woman = r.Chance(0.5f);
        var style = woman ? (r.Chance(0.75f) ? HairStyle.Long : HairStyle.Short)
                          : (r.Chance(0.15f) && !child ? HairStyle.None : r.Chance(0.1f) ? HairStyle.Long : HairStyle.Short);
        bool suit = !child && r.Chance(woman ? 0.08f : 0.15f);
        if (suit)
        {
            var cloth = Suits[r.Pick(Suits.Length)];
            return new Outfit(cloth, cloth, cloth, cloth, skin, hair, style, Legwear.Trousers, true, Ties[r.Pick(Ties.Length)],
                              CamoPattern.None, false, false, FaceOf(seed, type, woman, false));
        }

        // O-1 — the top: his type's colour, a little lighter or darker; else an everyday colour (a child's brighter)
        var top = own is { } c ? Scale(c, 0.75f + 0.5f * r.Next()) : child ? ChildTops[r.Pick(ChildTops.Length)] : Tops[r.Pick(Tops.Length)];
        bool shortSleeves = r.Chance(child ? 0.6f : 0.45f);
        float roll = r.Next();
        var legs = woman && roll < (child ? 0.35f : 0.40f) ? Legwear.Skirt
                 : roll > (child ? 0.55f : 0.80f) ? Legwear.Shorts
                 : Legwear.Trousers;
        var bottom = legs == Legwear.Skirt && r.Chance(0.5f) ? Tops[r.Pick(Tops.Length)] : Bottoms[r.Pick(Bottoms.Length)];
        var shin = legs == Legwear.Trousers ? bottom : skin;
        return new Outfit(top, shortSleeves ? skin : top, bottom, shin, skin, hair, style, legs, false, default, CamoPattern.None, child, false,
                          FaceOf(seed, type, woman, child));
    }

    /// <summary>
    /// ⭐ CE-1099 — the colour of figure part <paramref name="index"/> (the shared part order) in outfit <paramref name="o"/>: legs, torso,
    /// head, arms; null for anything else (the rifle keeps its own). Under a skirt the thighs are bare.
    /// </summary>
    public static Color? ColourOfPart(in Outfit o, int index) => index switch
    {
        0 or 2 => o.Legwear == Legwear.Skirt ? o.Skin : o.Bottom,   // thighs
        1 or 3 => o.Shin,                                             // shins
        BlockFigure.TorsoIndex => o.Top,
        BlockFigure.HeadIndex => o.Skin,
        6 or 8 => o.Top,                                              // upper arms
        7 or 9 => o.Sleeve,                                           // forearms
        _ => null,
    };

    /// <summary>
    /// ⭐ CE-1099 — the details of outfit <paramref name="o"/> into <paramref name="into"/> (cleared first): a suit's shirt and tie, a skirt,
    /// long hair. ⭐ CE-1101 — no camouflage patches any more: the shader paints the pattern (<see cref="BodySurface"/>).
    /// </summary>
    public static void Details(in Outfit o, long seed, List<PartDetail> into)
    {
        into.Clear();
        if (o.Suit)
        {
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.51f, 0f, 0.30f), new(0.03f, 0.26f, 0.36f), Shirt, Turned: true));
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.53f, 0f, 0.10f), new(0.03f, 0.08f, 0.62f), o.Tie, Turned: true));
        }
        if (o.Legwear == Legwear.Skirt)   // from the waist to the knee, a little wider than the hips
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0f, 0f, -0.86f), new(1.25f, 1.08f, 0.76f), o.Bottom, Turned: true));
        if (o.HairStyle == HairStyle.Long)   // down the back of the head to the shoulders
            into.Add(new PartDetail(BlockFigure.HeadIndex, new(-0.44f, 0f, -0.10f), new(0.22f, 1.08f, 1.0f), o.Hair, Turned: true));
        // ⭐ CE-1101 (§6r) — a soldier's camouflage is painted by the shader (BodySurface), not laid as boxes
    }

    // ── ⭐ CE-1100 (§6q O-6) — a personal car's paint ────────────────────────────────────────────────────────────────────────────

    /// <summary>Car paints as the streets show them: mostly white, black, silver and grey, then blue and red, and a few others.</summary>
    private static readonly (Color Paint, float Share)[] CarPaints =
    [
        (new(232, 232, 228, 255), 0.22f), (new(24, 24, 26, 255), 0.18f), (new(176, 178, 182, 255), 0.16f), (new(100, 102, 106, 255), 0.14f),
        (new(36, 64, 130, 255), 0.09f), (new(160, 28, 30, 255), 0.08f), (new(40, 86, 54, 255), 0.04f), (new(196, 176, 136, 255), 0.04f),
        (new(222, 186, 40, 255), 0.02f), (new(110, 72, 44, 255), 0.03f),
    ];

    /// <summary>
    /// ⭐ CE-1100 (docs/DESIGN_Map_3D_Mode.md §6q O-6) — the paint of the car seeded <paramref name="seed"/> (its network id) of TKB type
    /// <paramref name="type"/>: its own colour (authored, or its type's) a little lighter or darker per car; with none, a street colour.
    /// </summary>
    public static Color CarColour(long seed, long type, Color? own)
    {
        var r = new Draw(seed, type ^ 0xCA2);
        if (own is { } c) return Scale(c, 0.82f + 0.36f * r.Next());
        float roll = r.Next(), sum = 0f;
        foreach (var (paint, share) in CarPaints)
        {
            sum += share;
            if (roll < sum) return paint;
        }
        return CarPaints[0].Paint;
    }

    private static Color Scale(Color c, float f)
        => new((byte)Math.Clamp(c.R * f, 0f, 255f), (byte)Math.Clamp(c.G * f, 0f, 255f), (byte)Math.Clamp(c.B * f, 0f, 255f), (byte)255);

    /// <summary>A small repeatable draw (SplitMix64) from a man's seed and a salt (his type, or a patch number).</summary>
    private struct Draw(long seed, long salt)
    {
        private ulong _s = (ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)salt * 0xC2B2AE3D27D4EB4FUL ^ 0x165667B19E3779F9UL;

        public float Next()
        {
            _s += 0x9E3779B97F4A7C15UL;
            ulong z = _s;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 40) / (float)(1 << 24);
        }

        public int Pick(int n) => Math.Min(n - 1, (int)(Next() * n));
        public bool Chance(float p) => Next() < p;
    }
}
