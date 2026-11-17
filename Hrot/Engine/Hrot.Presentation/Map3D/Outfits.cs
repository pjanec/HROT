using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q O-2) — what a civilian wears on his legs. ⭐ CE-4648 (OF-4) — <c>LongSkirt</c> (to the
/// ankles) and <c>Hidden</c> (under a robe) are new; the member NAMES are what an <c>OutfitDto</c> option's Kind says.</summary>
public enum Legwear : byte { Trousers, Shorts, Skirt, LongSkirt, Hidden, Breechcloth, Tunban }

/// <summary>⭐ CE-1099 (§6q O-2) — a civilian's hair: short, long (down his back) or none.</summary>
public enum HairStyle : byte { Short, Long, None, Braids }

/// <summary>⭐ CE-1099 (§6q O-4) — a soldier's camouflage, by his side: Friend woodland, Neutral none. ⭐ CE-1235 (O-4a, R-1020) — Hostile
/// desert (was fleck, which stays a pattern a look may name).</summary>
public enum CamoPattern : byte { None, Woodland, Fleck, Desert }

/// <summary>
/// ⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q) — what one man wears: the colour of each part of his figure (<see cref="Outfits.ColourOfPart"/>)
/// and the details drawn on them (<see cref="Outfits.Details"/>). Computed from his seed, so the same on every node and every frame.
/// ⭐ CE-4648 — <paramref name="Dressed"/>: from a type's own wardrobe (<c>OutfitDto</c>), whose garment kinds (<paramref name="TopKind"/>,
/// <paramref name="HeadKind"/>, <paramref name="OverKind"/>) and cloth colours (<paramref name="HeadCloth"/>, <paramref name="OverColour"/>) the details draw.
/// </summary>
public readonly record struct Outfit(
    Color Top, Color Sleeve, Color Bottom, Color Shin, Color Skin, Color Hair, HairStyle HairStyle, Legwear Legwear,
    bool Suit, Color Tie, CamoPattern Camo, bool Child, bool Soldier, FaceLook Face = default, bool Police = false, bool Riot = false,
    bool Woman = false, bool Dressed = false, OutfitTop TopKind = OutfitTop.Shirt, OutfitHead HeadKind = OutfitHead.None,
    OutfitOver OverKind = OutfitOver.None, Color HeadCloth = default, Color OverColour = default, bool FacePaint = false, OutfitExtra ExtraKind = OutfitExtra.None, Color ExtraColour = default);

/// <summary>⭐ CE-1101 (§6r S-3) — a moustache or a beard, or none.</summary>
public enum BeardKind : byte { None, Moustache, Beard, Paint }

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
    /// <summary>⭐ CE-1162 — an elderly civilian's outfit: grey hair (a man often balding), muted top and trousers or a long skirt.</summary>
    private static Outfit Elderly(Draw r, long seed, long type, Color skin, Color? own, PersonSex sex)
    {
        bool woman = Sexed(r.Chance(0.5f), sex);   // ⭐ CE-4648 OF-2 — the type's sex wins; the draw is still consumed, so every later roll is the one it always was
        var hair = OutfitPalettes.GreyHair[r.Pick(OutfitPalettes.GreyHair.Length)];
        var style = woman ? (r.Chance(0.6f) ? HairStyle.Short : HairStyle.Long) : (r.Chance(0.4f) ? HairStyle.None : HairStyle.Short);
        var top = own is { } c ? Scale(c, 0.8f) : OutfitPalettes.MutedTops[r.Pick(OutfitPalettes.MutedTops.Length)];
        var legs = woman && r.Chance(0.5f) ? Legwear.Skirt : Legwear.Trousers;
        var bottom = OutfitPalettes.Bottoms[r.Pick(OutfitPalettes.Bottoms.Length)];
        return new Outfit(top, top, bottom, legs == Legwear.Trousers ? bottom : skin, skin, hair, style, legs, false, default, CamoPattern.None,
                          false, false, FaceOf(seed, type, woman, false), Woman: woman);
    }

    /// <summary>⭐ CE-4648 OF-2 — who he is: a type that says <c>Female</c> / <c>Male</c> (<c>PersonDto.Sex</c>) always draws a woman / a man; <c>Unspecified</c> keeps the seed's chance.</summary>
    private static bool Sexed(bool chance, PersonSex sex) => sex switch { PersonSex.Female => true, PersonSex.Male => false, _ => chance };

    /// <summary>⭐ CE-1162 — a security-force officer's uniform (navy).</summary>
    public static readonly Color PoliceUniform = OutfitPalettes.PoliceUniform;

    /// <summary>⭐ CE-1172 (U11) — the police vest (darker than the uniform), its pouches, the black belt; the cap's band. ⭐ CE-4648 — from <c>OutfitCatalog</c>.</summary>
    public static readonly Color PoliceVest = OutfitPalettes.PoliceVest, PolicePouch = OutfitPalettes.PolicePouch, PoliceBelt = OutfitPalettes.PoliceBelt,
                                 CapBand = OutfitPalettes.CapBand;

    /// <summary>⭐ CE-1235 (docs/DESIGN_Map_3D_Mode.md §6q O-4a, R-1020) — a soldier's cloth when neither he nor his type names one: his
    /// SIDE's — a friend (blue) green-olive, a hostile (red) desert khaki, a neutral olive — so the two sides read apart in a dim room,
    /// not only by their pattern. ⭐ CE-4648 — from <c>OutfitCatalog</c>.</summary>
    public static readonly Color WoodlandCloth = OutfitPalettes.WoodlandCloth, DesertCloth = OutfitPalettes.DesertCloth;

    /// <summary>⭐ CE-1101 (§6r S-2) — the three tones the shader paints a pattern in (over the uniform): woodland brown, black and light
    /// green; fleck sand, olive and red-brown; ⭐ CE-1235 desert tan, brown and pale sand.</summary>
    public static (Color, Color, Color) CamoTones(CamoPattern pattern) => pattern switch
    {
        CamoPattern.Fleck => (OutfitPalettes.Fleck[0], OutfitPalettes.Fleck[1], OutfitPalettes.Fleck[2]),
        CamoPattern.Desert => (OutfitPalettes.Desert[0], OutfitPalettes.Desert[1], OutfitPalettes.Desert[2]),
        _ => (OutfitPalettes.Woodland[0], OutfitPalettes.Woodland[1], OutfitPalettes.Woodland[2]),
    };

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
        return new FaceLook(eyes, mouth, brow, beard, OutfitPalettes.EyeColours[r.Pick(OutfitPalettes.EyeColours.Length)], OutfitPalettes.LipColours[r.Pick(OutfitPalettes.LipColours.Length)]);
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
    /// <param name="sex">⭐ CE-4648 (OF-2) — the type's <c>PersonDto.Sex</c>: <c>Female</c> ⇒ always a woman, <c>Male</c> ⇒ always a man, <c>Unspecified</c> ⇒
    /// the seed picks (the old behaviour). The <c>r.Chance(0.5)</c> draw is consumed either way, so every later roll is unchanged.</param>
    /// <param name="wardrobe">⭐ CE-4648 (OF-1, OF-11) — the type's own wardrobe (its <c>OutfitDto</c>, flattened once): when it has one he is dressed from it
    /// whatever his class is — no side camouflage unless his top is a <c>Uniform</c> that says <c>BySide</c>. Null ⇒ the everyday wardrobes below.</param>
    public static Outfit For(long seed, long type, bool soldier, bool child, Color? own, ForceId side, PersonKit kit = PersonKit.Civilian,
                             bool elderly = false, PersonSex sex = PersonSex.Unspecified, Wardrobe? wardrobe = null)
    {
        if (wardrobe != null) return Dress(wardrobe, seed, type, child, own, side, sex);
        var r = new Draw(seed, type);
        var skin = OutfitPalettes.SkinTones[r.Pick(OutfitPalettes.SkinTones.Length)];
        var hair = OutfitPalettes.HairColours[r.Pick(OutfitPalettes.HairColours.Length)];
        if (elderly && kit == PersonKit.Civilian && !soldier && !child) return Elderly(r, seed, type, skin, own, sex);
        if (kit == PersonKit.Security)
        {
            var uniform = own ?? PoliceUniform;
            var trousers = Scale(uniform, 0.8f);
            // ⭐ CE-1172 (People Display U11) — the police look: a cap (the shield man a riot helmet), a vest and a belt
            return new Outfit(uniform, uniform, trousers, trousers, skin, hair, HairStyle.Short, Legwear.Trousers, false, default, CamoPattern.None,
                              child, true, FaceOf(seed, type, woman: false, child), Police: true, TopKind: OutfitTop.Uniform,
                              Riot: type == Hrot.Core.Tkb.CivilianTkbCatalog.TkbSecurityShield);
        }
        if (soldier)
        {
            // ⭐ CE-1235 (O-4a) — the side picks the CLOTH too (his own colour, or his type's, still wins)
            var uniform = own ?? (side == ForceId.Hostile ? DesertCloth : WoodlandCloth);
            var trousers = Scale(uniform, 0.85f);
            var camo = side switch { ForceId.Friend => CamoPattern.Woodland, ForceId.Hostile => CamoPattern.Desert, _ => CamoPattern.None };
            return new Outfit(uniform, uniform, trousers, trousers, skin, hair, HairStyle.Short, Legwear.Trousers, false, default, camo, child, true,
                              FaceOf(seed, type, woman: false, child), TopKind: OutfitTop.Uniform);
        }

        bool woman = Sexed(r.Chance(0.5f), sex);   // ⭐ CE-4648 OF-2 — the type's sex wins, the draw is consumed all the same
        var style = woman ? (r.Chance(0.75f) ? HairStyle.Long : HairStyle.Short)
                          : (r.Chance(0.15f) && !child ? HairStyle.None : r.Chance(0.1f) ? HairStyle.Long : HairStyle.Short);
        bool suit = !child && r.Chance(woman ? 0.08f : 0.15f);
        if (suit)
        {
            var cloth = OutfitPalettes.Suits[r.Pick(OutfitPalettes.Suits.Length)];
            return new Outfit(cloth, cloth, cloth, cloth, skin, hair, style, Legwear.Trousers, true, OutfitPalettes.Ties[r.Pick(OutfitPalettes.Ties.Length)],
                              CamoPattern.None, false, false, FaceOf(seed, type, woman, false), Woman: woman, TopKind: OutfitTop.Suit);
        }

        // O-1 — the top: his type's colour, a little lighter or darker; else an everyday colour (a child's brighter)
        var top = own is { } c ? Scale(c, 0.75f + 0.5f * r.Next()) : child ? OutfitPalettes.ChildTops[r.Pick(OutfitPalettes.ChildTops.Length)] : OutfitPalettes.Tops[r.Pick(OutfitPalettes.Tops.Length)];
        bool shortSleeves = r.Chance(child ? 0.6f : 0.45f);
        float roll = r.Next();
        var legs = woman && roll < (child ? 0.35f : 0.40f) ? Legwear.Skirt
                 : roll > (child ? 0.55f : 0.80f) ? Legwear.Shorts
                 : Legwear.Trousers;
        var bottom = legs == Legwear.Skirt && r.Chance(0.5f) ? OutfitPalettes.Tops[r.Pick(OutfitPalettes.Tops.Length)] : OutfitPalettes.Bottoms[r.Pick(OutfitPalettes.Bottoms.Length)];
        var shin = legs == Legwear.Trousers ? bottom : skin;
        return new Outfit(top, shortSleeves ? skin : top, bottom, shin, skin, hair, style, legs, false, default, CamoPattern.None, child, false,
                          FaceOf(seed, type, woman, child), Woman: woman);
    }

    // ── ⭐ CE-4648 (OF-1, OF-6, OF-7, OF-11) — a type that has its own wardrobe ──────────────────────────────────────────────────────

    /// <summary>One slot's option, by its draw — ⭐ OF-6: ONE draw picks the option, even when the slot has just one.</summary>
    private static int Option(Wardrobe.Slot slot, ref Draw r) => slot.PickOption(r.Next());

    /// <summary>One slot's colour — ⭐ OF-6: ONE draw, even when the option has one colour (none ⇒ the default colour, alpha 0).</summary>
    private static Color Colour(Wardrobe.Slot slot, int option, ref Draw r)
    {
        var colours = slot.Colours[option];
        float u = r.Next();
        return colours.Length == 0 ? default : colours[Math.Min(colours.Length - 1, (int)(u * colours.Length))];
    }

    /// <summary>
    /// ⭐ CE-4648 — the man seeded <paramref name="seed"/> of a type with its own <paramref name="w"/>ardrobe. ⭐ OF-6: the draws go in a FIXED
    /// order — skin, sex, top, legs, over, head, hair, beard — each slot exactly one draw for its option and one for its colour, so adding
    /// an option to one type reshuffles nobody else. ⭐ OF-5: a scarf or veil hides the hair (it is drawn in the cloth's colour), a niqab or a
    /// face wrap hides the beard, a robe or an abaya hides the legs. ⭐ OF-11: the wardrobe wins over his class — no camouflage unless his top
    /// is a <c>Uniform</c> that says <c>BySide</c>. ⛔ No LINQ, no allocation: indexes arrays flattened once per type.
    /// </summary>
    private static Outfit Dress(Wardrobe w, long seed, long type, bool child, Color? own, ForceId side, PersonSex sex)
    {
        var r = new Draw(seed, type);
        var skin = Colour(w.Skin, Option(w.Skin, ref r), ref r);
        bool woman = Sexed(r.Chance(0.5f), sex);   // the sex draw is ALWAYS consumed (OF-6)
        var set = w.SetFor(woman);

        int ti = Option(set.Top, ref r);
        var topKind = (OutfitTop)set.Top.Kind[ti];
        var cloth = Colour(set.Top, ti, ref r);
        int li = Option(set.Legs, ref r);
        var legKind = (Legwear)set.Legs.Kind[li];
        var legCloth = Colour(set.Legs, li, ref r);
        int oi = Option(set.Over, ref r);
        var overKind = (OutfitOver)set.Over.Kind[oi];
        var overColour = Colour(set.Over, oi, ref r);
        int hi = Option(set.Head, ref r);
        var headKind = (OutfitHead)set.Head.Kind[hi];
        var headCloth = Colour(set.Head, hi, ref r);
        int ci = Option(set.Hair, ref r);
        var hairStyle = (HairStyle)set.Hair.Kind[ci];
        var hair = Colour(set.Hair, ci, ref r);
        int bi = Option(set.Beard, ref r);
        var beardKind = (BeardKind)set.Beard.Kind[bi];
        // ⭐ R-1064 — the LAST slot: drawn after everything else, so no existing person's draws move
        int xi = Option(set.Extra, ref r);
        var extraKind = (OutfitExtra)set.Extra.Kind[xi];
        var extraCloth = Colour(set.Extra, xi, ref r);

        // the top: an entity's / type's own colour wins; a Uniform with no colour of its own wears his side's cloth
        var camo = CamoPattern.None;
        Color top = own ?? cloth;
        if (topKind == OutfitTop.Uniform)
        {
            if (own == null && cloth.A == 0) top = side == ForceId.Hostile ? DesertCloth : WoodlandCloth;
            if (set.Top.Camo[ti] == OutfitCamo.BySide) camo = side switch { ForceId.Friend => CamoPattern.Woodland, ForceId.Hostile => CamoPattern.Desert, _ => CamoPattern.None };
        }
        else if (top.A == 0) top = OutfitPalettes.Tops[0];

        // the legs
        bool longGarment = topKind is OutfitTop.Robe or OutfitTop.Abaya or OutfitTop.Chadari;
        if (longGarment) legKind = Legwear.Hidden;                    // OF-5 — a robe hides the legs
        else if (legKind == Legwear.Hidden) legKind = Legwear.Trousers;
        Color bottom;
        if (longGarment || topKind == OutfitTop.Suit) bottom = top;
        else if (topKind == OutfitTop.Uniform && legCloth.A == 0) bottom = Scale(top, 0.85f);
        else bottom = legCloth.A == 0 ? Scale(top, 0.8f) : legCloth;
        var shin = legKind is Legwear.Shorts or Legwear.Skirt ? skin : bottom;

        // the head: a scarf, a hijab, a niqab or a face wrap hides the hair (OF-5) — ⭐ CE-4648 S2: the garment itself is drawn (Details), the hair is not
        bool covered = headKind is OutfitHead.Shemagh or OutfitHead.Hijab or OutfitHead.Niqab or OutfitHead.ShemaghFaceWrap;
        if (covered) { hairStyle = HairStyle.None; if (headCloth.A != 0) hair = headCloth; }
        if (headKind is OutfitHead.Niqab or OutfitHead.ShemaghFaceWrap) beardKind = BeardKind.None;   // the face below the eyes is covered
        if (topKind == OutfitTop.Chadari) { hairStyle = HairStyle.None; beardKind = BeardKind.None; headKind = OutfitHead.None; }   // ⭐ the chadari covers the whole head: the face is fully covered
        bool paint = beardKind == BeardKind.Paint && !covered;   // ⭐ R-1063 — face paint: drawn as boxes (Details), never given to the shader as a beard
        if (beardKind == BeardKind.Paint) beardKind = BeardKind.None;

        bool suit = topKind == OutfitTop.Suit;
        var sleeve = topKind == OutfitTop.TShirt ? skin : top;
        var face = FaceOf(seed, type, woman, child) with { Beard = child ? BeardKind.None : beardKind };
        return new Outfit(top, sleeve, bottom, shin, skin, hair, hairStyle, legKind, suit, suit ? OutfitPalettes.Ties[(int)((ulong)(seed ^ type) % (ulong)OutfitPalettes.Ties.Length)] : default,
                          camo, child, topKind == OutfitTop.Uniform, face, Police: false, Riot: headKind == OutfitHead.RiotHelmet, Woman: woman, Dressed: true,
                          TopKind: topKind, HeadKind: headKind, OverKind: overKind, HeadCloth: headCloth, OverColour: overColour, FacePaint: paint, ExtraKind: extraKind, ExtraColour: extraCloth);
    }

    /// <summary>
    /// ⭐ CE-1099 — the colour of figure part <paramref name="index"/> (the shared part order) in outfit <paramref name="o"/>: legs, torso,
    /// head, arms; null for anything else (the rifle keeps its own). Under a skirt the thighs are bare.
    /// </summary>
    public static Color? ColourOfPart(in Outfit o, int index) => index switch
    {
        0 or 2 => o.Legwear is Legwear.Skirt or Legwear.Breechcloth ? o.Skin : o.Bottom,   // thighs
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
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.51f, 0f, 0.30f), new(0.03f, 0.26f, 0.36f), OutfitPalettes.Shirt, Turned: true));
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.53f, 0f, 0.10f), new(0.03f, 0.08f, 0.62f), o.Tie, Turned: true));
        }
        if (o.Legwear == Legwear.Skirt)   // from the waist to the knee, a little wider than the hips
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0f, 0f, -0.86f), new(1.25f, 1.08f, 0.76f), o.Bottom, Turned: true));
        if (o.Police)   // ⭐ CE-1172 (U11) — the vest over his chest and back, two pouches on its front, the belt round his waist
        {
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0f, 0f, 0.14f), new(1.08f, 1.06f, 0.62f), PoliceVest, Turned: true));
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.56f, 0.22f, -0.02f), new(0.10f, 0.20f, 0.18f), PolicePouch, Turned: true));
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0.56f, -0.22f, -0.02f), new(0.10f, 0.20f, 0.18f), PolicePouch, Turned: true));
            into.Add(new PartDetail(BlockFigure.TorsoIndex, new(0f, 0f, -0.42f), new(1.10f, 1.08f, 0.10f), PoliceBelt, Turned: true));
        }
        if (o.HairStyle is HairStyle.Long or HairStyle.Braids && !o.Police)   // ⭐ CE-4650 NA-2 — Braids is drawn as the long-hair box (stand-in)   // down the back of the head to the shoulders
            into.Add(new PartDetail(BlockFigure.HeadIndex, new(-0.44f, 0f, -0.10f), new(0.22f, 1.08f, 1.0f), o.Hair, Turned: true));
        // ⭐ CE-1101 (§6r) — a soldier's camouflage is painted by the shader (BodySurface), not laid as boxes

        // ── ⭐ CE-4648 S2 (OF-4, OF-5; mockups docs/designs/person-looks/person-outfits-*.svg) — the new garment kinds, REAL boxes ─────
        //   every box is the mockup's, in WORLD metres of the 1.8 m standing figure (x forward, y left, z up) converted into its part's unit frame (W).
        Garments(o, seed, into);
    }

    private static readonly Vector3 TorsoC = new(0f, 0f, 1.18f), TorsoS = new(0.24f, 0.40f, 0.56f), HeadC = new(0f, 0f, 1.60f), HeadS = new(0.21f, 0.20f, 0.24f);
    private static readonly Color Red = new(194, 59, 50, 255), Black = new(30, 30, 34, 255), PaintWhite = new(244, 243, 238, 255),
                                  FeatherWhite = new(238, 234, 222, 255), PaintBlack = new(24, 24, 26, 255), BandRed = new(140, 42, 36, 255);

    /// <summary>A box in world metres on the torso or the head (centre x, y, z; size dx, dy, dz) → the part's unit frame.</summary>
    private static void W(List<PartDetail> into, int part, float x, float y, float z, float dx, float dy, float dz, Color c)
    {
        var (cc, s) = part == BlockFigure.HeadIndex ? (HeadC, HeadS) : (TorsoC, TorsoS);
        into.Add(new PartDetail(part, new((x - cc.X) / s.X, (y - cc.Y) / s.Y, (z - cc.Z) / s.Z), new(dx / s.X, dy / s.Y, dz / s.Z), c, Turned: true));
    }

    private static Color Lighten(Color c, float m) => new((byte)(c.R + (255 - c.R) * m), (byte)(c.G + (255 - c.G) * m), (byte)(c.B + (255 - c.B) * m), (byte)255);
    /// <summary>A seam / placket tone: a shade darker for a light cloth, a little lighter for a dark one.</summary>
    private static Color Seam(Color c) => c.R + c.G + c.B > 450 ? Scale(c, 0.86f) : Lighten(c, 0.10f);

    private static readonly Color CheckCloth = OutfitPalettes.Parse(OutfitCatalog.CheckShemagh);   // parsed ONCE (OF-7: nothing allocates per frame)
    private static bool IsCheck(Color c) => c.R == CheckCloth.R && c.G == CheckCloth.G && c.B == CheckCloth.B;

    private static void Garments(in Outfit o, long seed, List<PartDetail> into)
    {
        const int T = BlockFigure.TorsoIndex, H = BlockFigure.HeadIndex;
        // ── the body garments ──
        if (o.Legwear == Legwear.LongSkirt)   // (a stand-in kept: not in this slice)
            into.Add(new PartDetail(T, new(0f, 0f, -1.22f), new(1.25f, 1.08f, 1.48f), o.Bottom, Turned: true));
        else if (o.Legwear == Legwear.Hidden && o.TopKind == OutfitTop.Abaya)
        {
            W(into, T, 0f, 0f, 0.50f, 0.37f, 0.54f, 0.95f, o.Bottom);               // the cloak to the ground
            W(into, T, 0f, 0f, 1.38f, 0.30f, 0.52f, 0.12f, o.Bottom);               // the cape over the shoulders
            W(into, T, 0.19f, 0f, 1.00f, 0.012f, 0.03f, 0.90f, Seam(o.Bottom));     // the front opening
        }
        else if (o.Legwear == Legwear.Hidden)   // a robe (thobe / dishdasha)
        {
            W(into, T, 0f, 0f, 0.62f, 0.30f, 0.44f, 0.60f, o.Bottom);               // the skirt to the ankle…
            W(into, T, 0f, 0f, 0.30f, 0.34f, 0.50f, 0.48f, o.Bottom);               // …the hem a little wider
            W(into, T, 0f, 0f, 1.455f, 0.22f, 0.30f, 0.05f, o.Top);                 // the band collar
            W(into, T, 0.13f, 0f, 1.10f, 0.012f, 0.03f, 0.62f, Seam(o.Top));        // the placket
        }
        else if (o.Legwear == Legwear.Breechcloth)
        {
            W(into, T, 0f, 0f, 0.93f, 0.27f, 0.43f, 0.05f, Scale(o.Bottom, 0.7f));  // the belt
            W(into, T, 0.135f, 0f, 0.74f, 0.02f, 0.16f, 0.38f, Lighten(o.Bottom, 0.15f));    // the flaps, front and back
            W(into, T, -0.135f, 0f, 0.74f, 0.02f, 0.16f, 0.38f, Lighten(o.Bottom, 0.15f));
            for (int k = 0; k < 7; k++) W(into, T, 0.142f, -0.15f + 0.05f * k, 0.93f, 0.012f, 0.03f, 0.03f, Bead(k));   // the beaded belt
        }
        if (o.TopKind == OutfitTop.Buckskin)
        {
            W(into, T, 0f, 0f, 1.20f, 0.27f, 0.43f, 0.50f, o.Top);                  // the tunic, a little wider than the torso
            var fringe = Scale(o.Top, 0.78f);
            for (int k = 0; k < 5; k++)                                             // the hem cut into a fringe, front and back
            {
                W(into, T, 0.135f, -0.16f + 0.08f * k, 0.88f, 0.02f, 0.03f, 0.12f, fringe);
                W(into, T, -0.135f, -0.16f + 0.08f * k, 0.88f, 0.02f, 0.03f, 0.12f, fringe);
            }
        }
        if (o.OverKind == OutfitOver.ChestRig)
        {
            var pouch = Scale(o.OverColour, 0.74f);
            W(into, T, 0f, 0f, 1.22f, 0.27f, 0.43f, 0.30f, o.OverColour);           // the panel over the chest
            W(into, T, 0f, 0.10f, 1.40f, 0.25f, 0.05f, 0.12f, o.OverColour);        // the two shoulder straps
            W(into, T, 0f, -0.10f, 1.40f, 0.25f, 0.05f, 0.12f, o.OverColour);
            for (int k = -1; k <= 1; k++) W(into, T, 0.145f, 0.13f * k, 1.20f, 0.06f, 0.08f, 0.13f, pouch);   // three magazine pouches
        }
        // ── the head garments ──
        switch (o.HeadKind)
        {
            case OutfitHead.Cap:   // (kept) a cap: the crown and its peak
                into.Add(new PartDetail(H, new(-0.02f, 0f, 0.45f), new(1.10f, 1.10f, 0.20f), o.HeadCloth, Turned: true));
                into.Add(new PartDetail(H, new(0.56f, 0f, 0.31f), new(0.26f, 0.92f, 0.04f), o.HeadCloth, Turned: true));
                break;
            case OutfitHead.Skullcap:
                W(into, H, 0f, 0f, 1.705f, 0.215f, 0.205f, 0.05f, o.HeadCloth);
                W(into, H, 0f, 0f, 1.67f, 0.225f, 0.215f, 0.03f, o.HeadCloth);
                break;
            case OutfitHead.Shemagh: Shemagh(o, into, T, H); break;
            case OutfitHead.ShemaghFaceWrap: FaceWrap(o, into, T, H); break;
            case OutfitHead.Hijab: Hijab(o, into, T, H, veil: false); break;
            case OutfitHead.Niqab: Hijab(o, into, T, H, veil: true); break;
            case OutfitHead.Headband: Headband(o, seed, into, H); break;
        }
        if (o.HairStyle == HairStyle.Braids)
        {
            var hc = o.Hair;
            W(into, H, 0f, 0f, 1.745f, 0.23f, 0.22f, 0.05f, hc);                    // the crown
            W(into, H, -0.13f, 0f, 1.52f, 0.05f, 0.24f, 0.40f, hc);                 // the fall down the back
            for (int s = 1; s >= -1; s -= 2)                                        // a braid each side: out of the hair beside the face (over the ear), over the shoulder, down the chest, tied in red
            {
                W(into, H, 0.03f, 0.115f * s, 1.585f, 0.10f, 0.04f, 0.22f, hc);
                W(into, T, 0.06f, 0.12f * s, 1.42f, 0.09f, 0.05f, 0.10f, hc);
                W(into, T, 0.145f, 0.12f * s, 1.28f, 0.05f, 0.05f, 0.30f, hc);
                W(into, T, 0.145f, 0.12f * s, 1.10f, 0.055f, 0.055f, 0.03f, BandRed);
            }
        }
        if (o.FacePaint)   // ⭐ R-1063 — stripes by the seed: a band over the eyes, a cheek stripe each side (the two cheek colours swap by the seed)
        {
            bool swap = ((seed ^ (seed >> 7)) & 1) != 0;
            W(into, H, 0.108f, 0f, 1.63f, 0.012f, 0.20f, 0.04f, PaintBlack);
            W(into, H, 0.108f, 0.07f, 1.56f, 0.012f, 0.05f, 0.10f, swap ? PaintWhite : Red);
            W(into, H, 0.108f, -0.07f, 1.56f, 0.012f, 0.05f, 0.10f, swap ? Red : PaintWhite);
        }
        Ornaments(o, into);
        Afghan(o, into);
    }

    // ── ⭐ CE-4648 (R-1064) — the Native American ornaments ──────────────────────────────────────────────────────────────────────────

    private static readonly Color BeadBlue = new(47, 95, 176, 255), BeadRed = new(194, 59, 50, 255), BeadWhite = new(241, 238, 228, 255), BeadYellow = new(227, 184, 49, 255),
                                  PipeBone = new(239, 233, 216, 255), PipeLeather = new(122, 82, 48, 255);
    /// <summary>Bead <paramref name="i"/> of a row: blue, red, white, yellow in turn.</summary>
    private static Color Bead(int i) => (i & 3) switch { 0 => BeadBlue, 1 => BeadRed, 2 => BeadWhite, _ => BeadYellow };

    private static void BeadRow(List<PartDetail> into, float x, float z, int n, float y0, float step, int offset)
    {
        for (int i = 0; i < n; i++) W(into, BlockFigure.TorsoIndex, x, y0 + i * step, z, 0.012f, 0.03f, 0.03f, Bead(i + offset));
    }

    private static void Ornaments(in Outfit o, List<PartDetail> into)
    {
        const int T = BlockFigure.TorsoIndex;
        if (o.OverKind is OutfitOver.BeadedYoke or OutfitOver.YokeHairPipe)   // a band of small coloured squares across the chest and over the shoulders
        {
            BeadRow(into, 0.134f, 1.405f, 9, -0.16f, 0.04f, 1); BeadRow(into, 0.134f, 1.365f, 9, -0.16f, 0.04f, 0); BeadRow(into, -0.134f, 1.405f, 9, -0.16f, 0.04f, 1);
            W(into, T, 0f, 0.15f, 1.468f, 0.20f, 0.03f, 0.012f, BeadBlue); W(into, T, 0f, -0.15f, 1.468f, 0.20f, 0.03f, 0.012f, BeadRed);
        }
        if (o.OverKind is OutfitOver.HairPipe or OutfitOver.YokeHairPipe)     // a bone hair-pipe breastplate: six vertical tubes between two leather bars
        {
            for (int i = 0; i < 6; i++) W(into, T, 0.142f, -0.15f + 0.06f * i, 1.25f, 0.025f, 0.032f, 0.30f, PipeBone);
            W(into, T, 0.142f, 0f, 1.405f, 0.03f, 0.34f, 0.025f, PipeLeather); W(into, T, 0.142f, 0f, 1.10f, 0.03f, 0.34f, 0.025f, PipeLeather);
        }
        if (o.ExtraKind is OutfitExtra.Armbands or OutfitExtra.ArmbandsNecklace)   // a red band with a blue stripe round each upper arm (on the arm parts, so they swing with them)
        {
            into.Add(new PartDetail(6, new(-0.17f, 0f, 0f), new(0.14f, 1.20f, 1.20f), BeadRed));
            into.Add(new PartDetail(8, new(-0.17f, 0f, 0f), new(0.14f, 1.20f, 1.20f), BeadRed));
            into.Add(new PartDetail(6, new(-0.17f, 0f, 0f), new(0.05f, 1.26f, 1.26f), BeadBlue));
            into.Add(new PartDetail(8, new(-0.17f, 0f, 0f), new(0.05f, 1.26f, 1.26f), BeadBlue));
        }
        if (o.ExtraKind is OutfitExtra.Necklace or OutfitExtra.ArmbandsNecklace)   // a white bead collar, a strand on the chest, a yellow pendant
        {
            W(into, T, 0f, 0f, 1.46f, 0.19f, 0.23f, 0.035f, BeadWhite);
            for (int s = -2; s <= 2; s++) W(into, T, 0.138f, 0.06f * s, 1.40f - 0.02f * Math.Abs(s), 0.02f, 0.025f, 0.07f, Bead(1 + Math.Abs(s)));
            W(into, T, 0.145f, 0f, 1.30f, 0.03f, 0.04f, 0.05f, BeadYellow);
        }
    }

    // ── ⭐ CE-4648 (R-1066) — Afghan dress ───────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Color VestDark = new(38, 38, 41, 255), GrilleDark = new(51, 65, 90, 255);

    private static void Afghan(in Outfit o, List<PartDetail> into)
    {
        const int T = BlockFigure.TorsoIndex, H = BlockFigure.HeadIndex;
        if (o.TopKind == OutfitTop.Perahan)   // a knee-length tunic: the sides LEFT OPEN (slits) between a front and a back panel
        {
            var top = o.Top;
            W(into, T, 0f, 0f, 1.20f, 0.27f, 0.43f, 0.50f, top);
            W(into, T, 0f, 0f, 1.455f, 0.22f, 0.30f, 0.045f, Scale(top, 0.8f));
            W(into, T, 0.145f, 0f, 0.72f, 0.03f, 0.42f, 0.46f, top); W(into, T, -0.145f, 0f, 0.72f, 0.03f, 0.42f, 0.46f, top);
            W(into, T, 0.15f, 0f, 0.93f, 0.03f, 0.43f, 0.025f, Scale(top, 0.85f)); W(into, T, -0.15f, 0f, 0.93f, 0.03f, 0.43f, 0.025f, Scale(top, 0.85f));
        }
        if (o.Legwear == Legwear.Tunban)      // very loose trousers: a baggy sleeve round each leg part (they swing with it), a narrow cuff at the ankle
        {
            var c = o.Bottom;
            for (int part = 0; part < 4; part++)
            {
                bool shin = (part & 1) == 1;
                into.Add(new PartDetail(part, new(0f, 0f, 0f), new(shin ? 0.92f : 1.0f, 1.9f, 1.7f), c));            // x runs ALONG the limb: baggier across than the leg
                if (shin) into.Add(new PartDetail(part, new(0.46f, 0f, 0f), new(0.12f, 1.1f, 1.05f), Scale(c, 0.6f)));   // the narrow gathered cuff at the ankle
            }
        }
        if (o.TopKind == OutfitTop.Chadari)   // the full-body pleated cover: the head wholly covered but a dark mesh grille over the eyes; crown to ankle
        {
            var c = o.Top;
            W(into, H, 0f, 0f, 1.715f, 0.27f, 0.265f, 0.13f, c); W(into, H, 0f, 0f, 1.54f, 0.27f, 0.265f, 0.17f, c);
            W(into, H, 0f, 0.135f, 1.625f, 0.21f, 0.04f, 0.06f, c); W(into, H, 0f, -0.135f, 1.625f, 0.21f, 0.04f, 0.06f, c); W(into, H, -0.135f, 0f, 1.625f, 0.05f, 0.265f, 0.06f, c);
            W(into, H, 0.137f, 0f, 1.625f, 0.012f, 0.21f, 0.06f, GrilleDark);
            W(into, T, 0f, 0f, 1.38f, 0.33f, 0.50f, 0.14f, c); W(into, T, 0f, 0f, 0.62f, 0.34f, 0.52f, 0.62f, c); W(into, T, 0f, 0f, 0.17f, 0.38f, 0.58f, 0.32f, c);
            var pleat = Scale(c, 0.78f);
            for (int i = 0; i < 7; i++) W(into, T, 0.19f, -0.18f + 0.06f * i, 0.60f, 0.012f, 0.012f, 1.05f, pleat);
        }
        if (o.HeadKind == OutfitHead.Pakol)   // the flat round wool cap: a thick rolled rim and a flat top, a little wider than the head
        {
            var c = o.HeadCloth;
            W(into, H, 0f, 0f, 1.69f, 0.235f, 0.225f, 0.085f, c); W(into, H, 0f, 0f, 1.745f, 0.25f, 0.24f, 0.04f, Scale(c, 0.9f)); W(into, H, 0f, 0f, 1.665f, 0.24f, 0.23f, 0.02f, Scale(c, 0.7f));
        }
        if (o.HeadKind == OutfitHead.Turban)  // a large wrapped lungee: layer on layer, wider and taller than the head, a loose tail down the back
        {
            var c = o.HeadCloth; var tail = c.R + c.G + c.B < 200 ? new Color(154, 154, 162, 255) : new Color(43, 43, 48, 255);
            W(into, H, 0f, 0f, 1.71f, 0.27f, 0.27f, 0.09f, c); W(into, H, 0f, 0f, 1.79f, 0.26f, 0.28f, 0.08f, Scale(c, 0.93f)); W(into, H, 0f, 0f, 1.865f, 0.235f, 0.25f, 0.07f, c);
            W(into, H, 0.136f, 0f, 1.75f, 0.012f, 0.26f, 0.03f, tail);
            W(into, T, -0.165f, 0.05f, 1.45f, 0.05f, 0.11f, 0.50f, c); W(into, T, -0.168f, 0.05f, 1.22f, 0.052f, 0.112f, 0.03f, tail);
        }
        if (o.ExtraKind is OutfitExtra.Waistcoat or OutfitExtra.WaistcoatPatu)   // a dark vest: two front panels (open between), a back panel, two straps
        {
            var v = o.ExtraKind == OutfitExtra.Waistcoat ? o.ExtraColour : VestDark;
            W(into, T, 0.145f, 0.115f, 1.22f, 0.03f, 0.17f, 0.42f, v); W(into, T, 0.145f, -0.115f, 1.22f, 0.03f, 0.17f, 0.42f, v); W(into, T, -0.145f, 0f, 1.22f, 0.03f, 0.42f, 0.42f, v);
            W(into, T, 0f, 0.15f, 1.45f, 0.25f, 0.05f, 0.05f, v); W(into, T, 0f, -0.15f, 1.45f, 0.25f, 0.05f, 0.05f, v);
        }
        if (o.ExtraKind is OutfitExtra.Patu or OutfitExtra.WaistcoatPatu)       // a wool shawl over the shoulders, down the back and over one shoulder to the front
        {
            var c = o.ExtraColour; var hem = Scale(c, 0.6f);
            W(into, T, 0f, 0f, 1.40f, 0.31f, 0.47f, 0.10f, c); W(into, T, -0.15f, 0f, 1.12f, 0.04f, 0.42f, 0.62f, c); W(into, T, 0.15f, 0.13f, 1.18f, 0.04f, 0.17f, 0.52f, c);
            W(into, T, -0.153f, 0f, 0.84f, 0.042f, 0.43f, 0.04f, hem); W(into, T, 0.153f, 0.13f, 0.95f, 0.042f, 0.17f, 0.04f, hem);
        }
    }

    /// <summary>The shemagh: a LARGE cloth wider than the head and over the crown, framing the face; flaps past the ears and in front to mid-chest, a deep
    /// back drape; the agal — a black double ring — ON the crown. The check variant is white with red bands; any other cloth is plain.</summary>
    private static void Shemagh(in Outfit o, List<PartDetail> into, int T, int H)
    {
        bool check = IsCheck(o.HeadCloth);
        var cloth = check ? PaintWhite : o.HeadCloth;
        var band = Red;
        W(into, H, 0f, 0f, 1.74f, 0.25f, 0.26f, 0.12f, cloth);
        if (check) { W(into, H, 0f, 0f, 1.745f, 0.252f, 0.045f, 0.122f, band); W(into, H, 0f, 0.07f, 1.745f, 0.252f, 0.02f, 0.122f, band); W(into, H, 0f, -0.07f, 1.745f, 0.252f, 0.02f, 0.122f, band); }
        for (int s = 1; s >= -1; s -= 2)
        {
            W(into, H, 0f, 0.13f * s, 1.58f, 0.21f, 0.045f, 0.28f, cloth);                       // the temple flap past the ear
            W(into, T, 0.14f, 0.125f * s, 1.27f, 0.04f, 0.10f, 0.34f, cloth);                    // the front panel beside the neck, to mid-chest
            if (check)
            {
                W(into, H, 0f, 0.13f * s, 1.62f, 0.212f, 0.047f, 0.035f, band); W(into, H, 0f, 0.13f * s, 1.51f, 0.212f, 0.047f, 0.035f, band);
                W(into, T, 0.142f, 0.125f * s, 1.33f, 0.042f, 0.102f, 0.035f, band); W(into, T, 0.142f, 0.125f * s, 1.20f, 0.042f, 0.102f, 0.035f, band);
            }
        }
        W(into, T, -0.135f, 0f, 1.48f, 0.06f, 0.32f, 0.62f, cloth);                              // the back drape, crown to between the shoulder blades
        if (check) { W(into, T, -0.138f, 0f, 1.58f, 0.062f, 0.322f, 0.04f, band); W(into, T, -0.138f, 0f, 1.44f, 0.062f, 0.322f, 0.04f, band); W(into, T, -0.138f, 0f, 1.30f, 0.062f, 0.322f, 0.04f, band); }
        W(into, H, 0f, 0f, 1.815f, 0.23f, 0.23f, 0.02f, Black);                                  // the agal
        W(into, H, 0f, 0f, 1.845f, 0.23f, 0.23f, 0.02f, Black);
    }

    /// <summary>The face wrap: the shemagh WOUND round the head and over the face — a narrow eye slit of skin stays; the loose tail at one shoulder; no agal.</summary>
    private static void FaceWrap(in Outfit o, List<PartDetail> into, int T, int H)
    {
        bool check = IsCheck(o.HeadCloth);
        var w = check ? PaintWhite : o.HeadCloth;
        var fold = check ? Red : Scale(o.HeadCloth, 0.82f);
        W(into, H, 0f, 0f, 1.718f, 0.25f, 0.26f, 0.125f, w);                                   // above the slit: the crown and the brow
        W(into, H, 0f, 0f, 1.53f, 0.25f, 0.26f, 0.14f, w);                                     // below it: nose, mouth, chin
        W(into, H, 0f, 0.13f, 1.627f, 0.21f, 0.04f, 0.055f, w); W(into, H, 0f, -0.13f, 1.627f, 0.21f, 0.04f, 0.055f, w); W(into, H, -0.13f, 0f, 1.627f, 0.05f, 0.26f, 0.055f, w);   // round the sides and the back at the slit
        W(into, H, 0f, 0f, 1.76f, 0.255f, 0.265f, 0.03f, fold); W(into, H, 0f, 0f, 1.49f, 0.255f, 0.265f, 0.03f, fold);   // the turn folds
        W(into, T, 0.02f, 0.15f, 1.33f, 0.07f, 0.07f, 0.30f, w); W(into, T, 0.02f, 0.15f, 1.17f, 0.075f, 0.075f, 0.03f, fold);   // the loose tail at one shoulder
    }

    /// <summary>The hijab (and the niqab: the same plus a veil over the face below the eyes): a headscarf, the cloth continuing over the shoulders and down the chest and back.</summary>
    private static void Hijab(in Outfit o, List<PartDetail> into, int T, int H, bool veil)
    {
        var c = o.HeadCloth;
        W(into, H, 0f, 0f, 1.715f, 0.25f, 0.24f, 0.10f, c); W(into, H, 0.125f, 0f, 1.685f, 0.03f, 0.22f, 0.05f, c);   // the crown, the brow
        W(into, H, 0f, 0.125f, 1.58f, 0.22f, 0.04f, 0.30f, c); W(into, H, 0f, -0.125f, 1.58f, 0.22f, 0.04f, 0.30f, c);   // the sides past the ears
        W(into, H, -0.13f, 0f, 1.55f, 0.05f, 0.26f, 0.34f, c);                                                          // the nape
        W(into, T, 0.02f, 0f, 1.455f, 0.22f, 0.27f, 0.05f, c);                                                          // under the chin
        W(into, T, 0f, 0f, 1.38f, 0.30f, 0.46f, 0.12f, c);                                                              // over the shoulders
        W(into, T, 0.135f, 0f, 1.30f, 0.04f, 0.30f, 0.30f, c); W(into, T, -0.135f, 0f, 1.28f, 0.04f, 0.34f, 0.34f, c);  // down the chest and the back to mid-chest
        if (veil) W(into, H, 0.118f, 0f, 1.54f, 0.04f, 0.22f, 0.12f, c);                                                // the niqab's veil below the eyes
    }

    /// <summary>⭐ CE-4648 — how many feathers (1–3) the man seeded <paramref name="seed"/> wears in his headband.</summary>
    public static int FeatherCount(long seed) => 1 + (int)(((ulong)seed * 0x9E3779B97F4A7C15UL) >> 61) % 3;

    /// <summary>The headband with 1–3 feathers (by the seed) rising from its BACK, stepped backwards and FANNED outwards past the sides of the head — each 0.055 m wide,
    /// white with a clear black tip.</summary>
    private static void Headband(in Outfit o, long seed, List<PartDetail> into, int H)
    {
        W(into, H, 0f, 0f, 1.665f, 0.225f, 0.215f, 0.03f, o.HeadCloth);
        int n = FeatherCount(seed);
        for (int k = 0; k < n; k++)
        {
            int s = n == 1 ? 0 : n == 2 ? (k == 0 ? -1 : 1) : k - 1;
            // three steps up and back: two white ones, then the black tip; the outer feathers lean away from the middle
            for (int i = 0; i < 3; i++)
            {
                float h = i == 2 ? 0.075f : 0.10f;
                float z = 1.705f + (i == 0 ? 0.05f : i == 1 ? 0.15f : 0.2375f);
                W(into, H, -0.105f - 0.02f * i, 0.07f * s * (1f + 0.75f * i), z, 0.02f, 0.055f, h, i == 2 ? PaintBlack : FeatherWhite);
            }
        }
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
