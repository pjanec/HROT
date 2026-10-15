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
    bool Suit, Color Tie, CamoPattern Camo, bool Child, bool Soldier);

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
    private static readonly Color[] Woodland = [new(92, 70, 46, 255), new(44, 56, 34, 255), new(30, 30, 26, 255)];
    private static readonly Color[] Fleck = [new(176, 160, 118, 255), new(88, 64, 44, 255), new(70, 86, 50, 255)];

    /// <summary>How many camouflage patches each pattern lays on a man.</summary>
    public const int WoodlandPatches = 12, FleckPatches = 16;

    /// <summary>
    /// ⭐ CE-1099 — the outfit of the man seeded <paramref name="seed"/> (his network id) of TKB type <paramref name="type"/>.
    /// <paramref name="own"/> is his colour when his entity or his type has one (null: none); for a <paramref name="soldier"/> it is his
    /// uniform (the family default when null). <paramref name="side"/> picks a soldier's camouflage.
    /// </summary>
    public static Outfit For(long seed, long type, bool soldier, bool child, Color? own, ForceId side)
    {
        var r = new Draw(seed, type);
        var skin = SkinTones[r.Pick(SkinTones.Length)];
        var hair = HairColours[r.Pick(HairColours.Length)];
        if (soldier)
        {
            var uniform = own ?? new Color(96, 110, 72, 255);
            var trousers = Scale(uniform, 0.85f);
            var camo = side switch { ForceId.Friend => CamoPattern.Woodland, ForceId.Hostile => CamoPattern.Fleck, _ => CamoPattern.None };
            return new Outfit(uniform, uniform, trousers, trousers, skin, hair, HairStyle.Short, Legwear.Trousers, false, default, camo, child, true);
        }

        bool woman = r.Chance(0.5f);
        var style = woman ? (r.Chance(0.75f) ? HairStyle.Long : HairStyle.Short)
                          : (r.Chance(0.15f) && !child ? HairStyle.None : r.Chance(0.1f) ? HairStyle.Long : HairStyle.Short);
        bool suit = !child && r.Chance(woman ? 0.08f : 0.15f);
        if (suit)
        {
            var cloth = Suits[r.Pick(Suits.Length)];
            return new Outfit(cloth, cloth, cloth, cloth, skin, hair, style, Legwear.Trousers, true, Ties[r.Pick(Ties.Length)],
                              CamoPattern.None, false, false);
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
        return new Outfit(top, shortSleeves ? skin : top, bottom, shin, skin, hair, style, legs, false, default, CamoPattern.None, child, false);
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
    /// long hair, or a soldier's camouflage patches. Repeatable from <paramref name="seed"/>.
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
        if (o.Camo == CamoPattern.None) return;
        bool woodland = o.Camo == CamoPattern.Woodland;
        var palette = woodland ? Woodland : Fleck;
        int n = woodland ? WoodlandPatches : FleckPatches;
        for (int k = 0; k < n; k++)
        {
            var r = new Draw(seed, 0x5EED + k);
            int part = CamoParts[r.Pick(CamoParts.Length)];
            bool torso = part == BlockFigure.TorsoIndex;
            int side = r.Pick(torso ? 6 : 4);
            float w = woodland ? 0.34f + r.Next() * 0.26f : 0.12f + r.Next() * 0.12f;
            float h = w * (0.6f + r.Next() * 0.8f);
            var (c, e) = BlockFigure.SpotOnSide(torso, side, r.Next(), r.Next(), w, MathF.Min(h, 0.9f));
            into.Add(new PartDetail(part, c, e, palette[r.Pick(palette.Length)]));
        }
    }

    /// <summary>The parts a camouflage patch may be on: the torso twice as often, then every leg and arm part (never the head or rifle).</summary>
    private static readonly int[] CamoParts = [BlockFigure.TorsoIndex, BlockFigure.TorsoIndex, 0, 1, 2, 3, 6, 7, 8, 9];

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
