using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4648</c> S2 (docs/designs/person-looks/DESIGN_Person_Outfits.md §3 OF-4 / OF-5; mockups <c>docs/designs/person-looks/person-outfits-*.svg</c>) — the
/// thirteen new garments drawn for REAL on the block figure, each by a type of its own (one option per slot), front and side. ⭐ The geometry tests need no display;
/// ⛔ the frame test SKIPS without one: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~PersonOutfitsRail</c> (frames <c>outfit-&lt;garment&gt;-front|side.png</c> go to <c>HROT_RAIL_SHOTS</c>, never committed).
/// 🔴 Red-proofs: see each test.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PersonOutfitsRail
{
    private const long First = 9100;
    private static string Hex(int r, int g, int b) => OutfitCatalog.C(r, g, b);
    private static readonly string White = Hex(244, 243, 238), Black = Hex(24, 24, 26), Khaki = Hex(165, 154, 114), Olive = Hex(94, 96, 64), Buck = Hex(201, 160, 106),
        BuckD = Hex(156, 118, 70), Red = Hex(140, 42, 36), HairBlack = Hex(20, 18, 18), Skin = OutfitCatalog.SkinTones[2], Grey = Hex(96, 100, 106), BuckT = Hex(201, 160, 106),
        PaleBlue = Hex(169, 184, 200), PakolB = Hex(185, 165, 127), VestB = Hex(30, 30, 32), PatuB = Hex(107, 91, 69), ChadariB = Hex(111, 160, 218), OliveP = Hex(110, 112, 78);

    private static OutfitOption O(string kind, params string[] colours) => new() { Kind = kind, Weight = 1f, Colours = [.. colours] };
    private static OutfitSlot S(OutfitOption o) => new() { Options = [o] };

    /// <summary>(name, top, legs, over, head, hair, beard) — the garment under test and the plainest everything else.</summary>
    private static readonly (string Name, OutfitOption Top, OutfitOption Legs, OutfitOption Over, OutfitOption Head, OutfitOption Hair, OutfitOption Beard, OutfitOption Extra)[] Garments =
    [
        ("thobe",       O("Robe", White),       O("Hidden"),   O("None"),     O("None"),                           O("Short", HairBlack), O("None"), O("None")),
        ("abaya",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("None"),                           O("Short", HairBlack), O("None"), O("None")),
        ("shemagh",     O("Robe", White),       O("Hidden"),   O("None"),     O("Shemagh", OutfitCatalog.CheckShemagh), O("Short", HairBlack), O("None"), O("None")),
        ("skullcap",    O("Shirt", White),      O("Trousers", Grey), O("None"), O("Skullcap", White),              O("Short", HairBlack), O("None"), O("None")),
        ("hijab",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("Hijab", Black),                   O("Short", HairBlack), O("None"), O("None")),
        ("niqab",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("Niqab", Black),                   O("Short", HairBlack), O("None"), O("None")),
        ("chestrig",    O("Shirt", Khaki),      O("Trousers", Grey), O("ChestRig", Olive), O("None"),              O("Short", HairBlack), O("None"), O("None")),
        ("facewrap",    O("Shirt", Khaki),      O("Trousers", Grey), O("ChestRig", Olive), O("ShemaghFaceWrap", Khaki), O("Short", HairBlack), O("None"), O("None")),
        // ⭐ user 2026-11-18 — the plain-clothes fighter of the user's photo: an olive jacket, the baggy shalwar, no rig, the black face wrap
        ("me-plainclothes", O("Jacket", Olive), O("Tunban", Khaki), O("None"), O("ShemaghFaceWrap", Black), O("Short", HairBlack), O("None"), O("None")),
        ("buckskin",    O("Buckskin", Buck),    O("Shorts", Skin), O("None"), O("None"),                           O("Short", HairBlack), O("None"), O("None")),
        ("breechcloth", O("TShirt", Skin),      O("Breechcloth", BuckD), O("None"), O("None"),                     O("Short", HairBlack), O("None"), O("None")),
        ("headband",    O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("Headband", Red),                  O("Short", HairBlack), O("None"), O("None")),
        ("braids",      O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("None"),                           O("Braids", HairBlack), O("None"), O("None")),
        ("facepaint",   O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("None"),                           O("Short", HairBlack), O("Paint"), O("None")),
        // ── the Native American warrior and the Afghan dress (R-1064 … R-1066): one garment per row, as the mockups draw them ──
        ("warrior",        O("Buckskin", BuckT),  O("Breechcloth", BuckD), O("YokeHairPipe"), O("Headband", Red),   O("Braids", HairBlack), O("Paint"), O("ArmbandsNecklace")),
        ("na-yoke",        O("Buckskin", BuckT),  O("Breechcloth", BuckD), O("BeadedYoke"),   O("None"),           O("Braids", HairBlack), O("None"), O("None")),
        ("na-breastplate", O("Buckskin", BuckT),  O("Breechcloth", BuckD), O("HairPipe"),     O("None"),           O("Braids", HairBlack), O("None"), O("None")),
        ("na-armbands",    O("Buckskin", BuckT),  O("Breechcloth", BuckD), O("None"),         O("None"),           O("Braids", HairBlack), O("None"), O("Armbands")),
        ("na-necklace",    O("Buckskin", BuckT),  O("Breechcloth", BuckD), O("None"),         O("None"),           O("Braids", HairBlack), O("None"), O("Necklace")),
        ("af-man",         O("Perahan", PaleBlue), O("Tunban"), O("None"),     O("Pakol", PakolB),  O("Short", HairBlack), O("None"), O("Waistcoat", VestB)),
        ("af-perahan",     O("Perahan", White),    O("Tunban"), O("None"),     O("None"),           O("Short", HairBlack), O("None"), O("None")),
        ("af-waistcoat",   O("Perahan", White),    O("Tunban"), O("None"),     O("None"),           O("Short", HairBlack), O("None"), O("Waistcoat", VestB)),
        ("af-pakol",       O("Perahan", White),    O("Tunban"), O("None"),     O("Pakol", PakolB),  O("Short", HairBlack), O("None"), O("None")),
        ("af-lungee",      O("Perahan", White),    O("Tunban"), O("None"),     O("Turban", White),  O("Short", HairBlack), O("None"), O("None")),
        ("af-patu",        O("Perahan", White),    O("Tunban"), O("None"),     O("None"),           O("Short", HairBlack), O("None"), O("Patu", PatuB)),
        ("af-chadari",     O("Chadari", ChadariB), O("Tunban"), O("None"),     O("None"),           O("Long", HairBlack),  O("None"), O("None")),
        ("af-elder",       O("Perahan", White),    O("Tunban"), O("None"),     O("Turban", White),  O("Short", Grey),      O("Beard"), O("Patu", PatuB)),
        ("af-fighter",     O("Perahan", OliveP),   O("Tunban"), O("ChestRig", Olive), O("Turban", Black), O("Short", HairBlack), O("Beard"), O("Patu", PatuB)),
    ];

    private static long TypeOf(int i) => First + i;
    /// <summary>The reference: an EXISTING white garment — the everyday white shirt of the catalogue (<c>OutfitCatalog.Shirt</c>) over grey trousers.</summary>
    private static long ReferenceType => First + Garments.Length;

    private static OutfitDto DtoOf(int i)
    {
        var g = Garments[i];
        return new OutfitDto { Skin = S(O("Skin", Skin)), Top = S(g.Top), Legs = S(g.Legs), Over = S(g.Over), Head = S(g.Head), Hair = S(g.Hair), Beard = S(g.Beard), Extra = S(g.Extra) };
    }

    /// <summary>A copy of the adult-male civilian (1101) with <paramref name="outfit"/> as its wardrobe.</summary>
    private static void RegisterAll(TkbDatabase tkb)
    {
        var add = typeof(TkbTemplate).GetMethod(nameof(TkbTemplate.AddDescriptor))!;
        var src = tkb.GetByType(1101)!;
        for (int i = 0; i < Garments.Length; i++)
        {
            var t = new TkbTemplate("Outfit " + Garments[i].Name, TypeOf(i)) { DisType = src.DisType };
            foreach (var (type, part, data) in src.GetAllDescriptors())
                if (type != typeof(OutfitDto)) add.MakeGenericMethod(type).Invoke(t, [data, part]);
            t.AddDescriptor(DtoOf(i));
            tkb.Register(t);
        }
        var r = new TkbTemplate("Outfit reference white shirt", ReferenceType) { DisType = src.DisType };
        foreach (var (type, part, data) in src.GetAllDescriptors())
            if (type != typeof(OutfitDto)) add.MakeGenericMethod(type).Invoke(r, [data, part]);
        r.AddDescriptor(new OutfitDto { Skin = S(O("Skin", Skin)), Top = S(O("Shirt", OutfitCatalog.Shirt)), Legs = S(O("Trousers", Grey)), Over = S(O("None")), Head = S(O("None")),
                                        Hair = S(O("Short", HairBlack)), Beard = S(O("None")) });
        tkb.Register(r);
    }

    private static EntityBodyLayer3D.TypeLook LookOf(TkbDatabase tkb, int i) => EntityBodyLayer3D.Resolve(tkb.GetByType(TypeOf(i))!);

    private static Outfit Dressed(TkbDatabase tkb, int i, long seed = 7)
    {
        var look = LookOf(tkb, i);
        return Outfits.For(seed, TypeOf(i), look.Soldier, look.Child, null, ForceId.Neutral, look.Kit, look.Elderly, look.Sex, look.Wardrobe);
    }

    private static List<PartDetail> DetailsOf(TkbDatabase tkb, int i, long seed = 7)
    {
        var d = new List<PartDetail>();
        var look = LookOf(tkb, i);
        Outfits.Details(Outfits.For(seed, TypeOf(i), look.Soldier, look.Child, null, ForceId.Neutral, look.Kit, look.Elderly, look.Sex, look.Wardrobe), seed, d);
        return d;
    }

    private static int Index(string name) => Array.FindIndex(Garments, g => g.Name == name);

    private static float WorldZ(PartDetail d)   // a detail's world height (m): the part's centre + its unit-frame offset × the part's height
        => d.Part == BlockFigure.HeadIndex ? 1.60f + d.Centre.Z * 0.24f : 1.18f + d.Centre.Z * 0.56f;

    [Fact]
    public void EveryGarmentIsDrawn_ByItsOwnBoxes_AndTheHidingRulesHold()
    {
        var tkb = HrotEnvironment.CreateTkb();
        RegisterAll(tkb);
        for (int i = 0; i < Garments.Length; i++) Assert.NotEmpty(DetailsOf(tkb, i));

        // robe: a skirt to the ankle, a collar, a placket; legs hidden in the cloth (OF-5)
        var thobe = DetailsOf(tkb, Index("thobe")); var o = Dressed(tkb, Index("thobe"));
        Assert.Equal(Legwear.Hidden, o.Legwear);
        Assert.Contains(thobe, d => d.Part == BlockFigure.TorsoIndex && WorldZ(d) - d.Extent.Z * 0.28f < 0.12f);   // reaches the ankle
        // abaya: wider than the robe
        var abaya = DetailsOf(tkb, Index("abaya"));
        Assert.True(abaya.Max(d => d.Extent.Y * 0.40f) > thobe.Max(d => d.Extent.Y * 0.40f), "wider than the thobe");
        // shemagh: wider than the head (crown), two temple flaps, two front panels over the chest, a deep back drape, two black agal rings on the crown
        var sh = DetailsOf(tkb, Index("shemagh"));
        Assert.Contains(sh, d => d.Part == BlockFigure.HeadIndex && d.Extent.Y * 0.20f >= 0.255f && WorldZ(d) > 1.72f);                  // the crown, over the head's width
        Assert.Equal(2, sh.Count(d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f > 0.135f && d.Extent.Z * 0.56f > 0.3f));        // the front panels beside the neck
        Assert.Contains(sh, d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f < -0.12f && d.Extent.Z * 0.56f > 0.55f);          // the back drape
        Assert.Equal(2, sh.Count(d => d.Colour.R < 40 && d.Colour.G < 40 && WorldZ(d) > 1.8f));                                         // the agal
        // skullcap: low dome, no flaps
        Assert.DoesNotContain(DetailsOf(tkb, Index("skullcap")), d => d.Part == BlockFigure.TorsoIndex);
        // hijab: drape to mid-chest and down the back; the hair hidden (OF-5); niqab: and the veil over the lower face
        var hijab = DetailsOf(tkb, Index("hijab")); Assert.Equal(HairStyle.None, Dressed(tkb, Index("hijab")).HairStyle);
        Assert.Contains(hijab, d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f > 0.12f && d.Extent.Z * 0.56f >= 0.29f);
        Assert.Contains(hijab, d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f < -0.12f);
        Assert.Equal(hijab.Count + 1, DetailsOf(tkb, Index("niqab")).Count);
        // chest rig: a panel, two straps, three pouches
        Assert.Equal(6, DetailsOf(tkb, Index("chestrig")).Count);
        // face wrap: covered above AND below the eye slit, the slit itself bare, a tail on the torso, no agal
        var wrap = DetailsOf(tkb, Index("facewrap")).Where(d => d.Part == BlockFigure.HeadIndex).ToList();
        static bool Front(PartDetail d) => (d.Centre.X + d.Extent.X / 2f) * 0.21f >= 0.12f;   // reaches the face's plane
        Assert.Contains(wrap, d => Front(d) && WorldZ(d) - d.Extent.Z * 0.12f > 1.655f);                                                // over the brow
        Assert.Contains(wrap, d => Front(d) && WorldZ(d) + d.Extent.Z * 0.12f < 1.605f);                                                // over the nose and mouth
        Assert.DoesNotContain(wrap, d => Front(d) && WorldZ(d) - d.Extent.Z * 0.12f < 1.62f && WorldZ(d) + d.Extent.Z * 0.12f > 1.64f);   // nothing across the eye slit at the face
        Assert.DoesNotContain(wrap, d => d.Colour.R < 40);                                                                              // no agal
        // buckskin: a fringe (many small boxes); breechcloth: thighs bare, shins in leggings; headband: 1–3 feathers; braids: two chest braids; face paint: stripes, no shader beard
        Assert.True(DetailsOf(tkb, Index("buckskin")).Count >= 11);
        var bc = Dressed(tkb, Index("breechcloth")); Assert.Equal(Legwear.Breechcloth, bc.Legwear); Assert.Equal(bc.Skin, Outfits.ColourOfPart(bc, 0)); Assert.Equal(bc.Bottom, Outfits.ColourOfPart(bc, 1));
        var feathers = new HashSet<int>(); for (int s = 1; s <= 60; s++) feathers.Add(DetailsOf(tkb, Index("headband"), s).Count(d => d.Colour.R > 200 && d.Colour.G > 200) );
        Assert.Equal([2, 4, 6], feathers.Order());                                                                                       // seeded 1…3 feathers (two white segments each)
        Assert.Equal(2, DetailsOf(tkb, Index("braids")).Count(d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f > 0.135f && d.Extent.Z * 0.56f > 0.29f));   // a braid each side, down the chest
        // the warrior's ornaments: armbands round the MIDDLE of both upper arms (parts 6, 8; x runs along the arm, a third from the shoulder), the yoke's squares, six hair-pipe tubes, a necklace, a beaded belt
        var warrior = DetailsOf(tkb, Index("warrior"));
        Assert.Equal(4, warrior.Count(d => d.Part is 6 or 8 && d.Centre.X < -0.1f && d.Centre.X > -0.3f && d.Extent.X < 0.2f && d.Extent.Y > 1.1f));
        Assert.True(warrior.Count(d => d.Part == BlockFigure.TorsoIndex && d.Extent.Y * 0.40f < 0.04f && d.Extent.Z * 0.56f < 0.04f) >= 27 + 7, "the yoke's squares and the belt's beads");
        Assert.Equal(6, warrior.Count(d => d.Part == BlockFigure.TorsoIndex && d.Colour.R > 230 && d.Extent.Z * 0.56f > 0.28f));         // the hair-pipe tubes
        Assert.Equal(2, warrior.Count(d => d.Part == BlockFigure.HeadIndex && d.Centre.Y * 0.20f is > 0.10f or < -0.10f && d.Extent.Z * 0.24f > 0.2f));   // a braid each side starts at the head, over the ear
        // Afghan: the baggy tunban (each leg part gets a sleeve WIDER than the limb and a narrow cuff on the shin), the chadari, the lungee, the pakol
        var af = DetailsOf(tkb, Index("af-perahan"));
        Assert.Equal(4, af.Count(d => d.Part < 4 && d.Extent.Y >= 1.5f && d.Extent.Z >= 1.5f));
        Assert.Equal(2, af.Count(d => d.Part is 1 or 3 && d.Extent.X < 0.2f && d.Extent.Y < 1.3f));
        Assert.Contains(af, d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f > 0.135f && d.Extent.Z * 0.56f > 0.4f);       // the front panel to the knee; the sides stay open
        Assert.True(DetailsOf(tkb, Index("af-chadari")).Count >= 14, "the pleated cover, crown to ankle, the grille");
        Assert.Equal(HairStyle.None, Dressed(tkb, Index("af-chadari")).HairStyle);
        var lungee = DetailsOf(tkb, Index("af-lungee"));
        Assert.Contains(lungee, d => d.Part == BlockFigure.HeadIndex && d.Extent.Y * 0.20f >= 0.265f);   // wider than the head…
        Assert.Contains(lungee, d => d.Part == BlockFigure.HeadIndex && WorldZ(d) > 1.85f);               // …and taller
        Assert.Contains(lungee, d => d.Part == BlockFigure.TorsoIndex && d.Centre.X * 0.24f < -0.15f && d.Extent.Z * 0.56f > 0.45f);   // the loose tail
        Assert.True(DetailsOf(tkb, Index("af-patu")).Count(d => d.Part == BlockFigure.TorsoIndex) >= 10, "the shawl");
        var paint = Dressed(tkb, Index("facepaint")); Assert.True(paint.FacePaint); Assert.Equal(BeardKind.None, paint.Face.Beard);
        Assert.Equal(3, DetailsOf(tkb, Index("facepaint")).Count);
    }

    private const string BeardPin1105 = "00011121010210120201202202201120202201200020112112212222220212200221002201221201";
    private static readonly long[] Fighters = [2003, 2010, 2011, 2012];

    /// <summary>⭐ CE-4648 polish — the odds the user asked for, pinned over seeds 1–500 of the REAL catalogue: a Middle-East fighter wears head cover (shemagh, face wrap or skullcap)
    /// in ≥ 75 % of seeds and a chest rig in ≥ 60 %; a boy often wears a small thobe. (Also writes the thumbnails' "typical" seed table when HROT_GEN_THUMB_SEEDS=1.)</summary>
    [Fact]
    public void TheFightersAreMostlyCoveredAndRigged_AndBoysOftenWearAThobe_OverSeeds1To500()
    {
        var tkb = HrotEnvironment.CreateTkb();
        Outfit Of(long type, long seed) { var look = EntityBodyLayer3D.Resolve(tkb.GetByType(type)!); return Outfits.For(seed, type, look.Soldier, look.Child, null, ForceId.Hostile, look.Kit, look.Elderly, look.Sex, look.Wardrobe); }
        foreach (long type in Fighters)
        {
            int covered = 0, rigged = 0, baggy = 0, blackWrap = 0;
            for (int s = 1; s <= 500; s++)
            {
                var o = Of(type, s);
                if (o.HeadKind is OutfitHead.Shemagh or OutfitHead.ShemaghFaceWrap or OutfitHead.Skullcap) covered++;
                if (o.OverKind == OutfitOver.ChestRig) rigged++;
                if (o.Legwear == Legwear.Tunban) baggy++;
                if (o.HeadKind == OutfitHead.ShemaghFaceWrap && o.HeadCloth.R < 60 && o.HeadCloth.G < 60 && o.HeadCloth.B < 60) blackWrap++;
            }
            Assert.True(covered >= 375, $"type {type}: head cover in {covered}/500");
            // ⭐ user 2026-11-18 (the plain-clothes fighter photo): a rig in ~60 % (not a large majority), the baggy shalwar under a shirt / jacket, the black face wrap
            Assert.InRange(rigged, 250, 350);
            Assert.True(baggy >= 60, $"type {type}: the baggy shalwar in {baggy}/500");
            Assert.True(blackWrap >= 15, $"type {type}: a black face wrap in {blackWrap}/500");
        }
        int thobes = 0;
        for (int s = 1; s <= 500; s++) if (Of(1107, s).TopKind == OutfitTop.Robe) thobes++;
        Assert.InRange(thobes, 150, 350);   // the boy's pool has a robe in 40 %

        if (Environment.GetEnvironmentVariable("HROT_GEN_THUMB_SEEDS") == "1")
        {
            // the seeds (entity ids) whose outfit is the TYPICAL one: the thumbnails script respawns until the entity's id is in its type's list
            bool Typical(long type, Outfit o) => type switch
            {
                1105 or 1109 => o.TopKind == OutfitTop.Robe && o.HeadKind is OutfitHead.Shemagh or OutfitHead.Skullcap && o.Face.Beard != BeardKind.None,
                1107 => o.TopKind == OutfitTop.Robe,
                1111 => o.HeadKind is OutfitHead.Pakol or OutfitHead.Turban && o.ExtraKind is OutfitExtra.Waistcoat or OutfitExtra.Patu,
                1112 or 1116 => o.TopKind == OutfitTop.Chadari,
                1113 => o.HeadKind == OutfitHead.Pakol,
                1114 => o.HeadKind == OutfitHead.Hijab,
                1115 => o.HeadKind == OutfitHead.Turban && o.ExtraKind == OutfitExtra.Patu,
                2019 or 2020 or 2021 => o.HeadKind is OutfitHead.Turban or OutfitHead.Pakol && o.OverKind == OutfitOver.ChestRig,
                _ => o.HeadKind is OutfitHead.Shemagh or OutfitHead.ShemaghFaceWrap && o.OverKind == OutfitOver.ChestRig,
            };
            var json = new System.Text.StringBuilder("{\n");
            long[] types = [1105, 1107, 1109, 1111, 1112, 1113, 1114, 1115, 1116, 2003, 2010, 2011, 2012, 2019, 2020, 2021];
            for (int k = 0; k < types.Length; k++)
            {
                var seeds = new List<long>();
                for (long s = 1; s <= 6000; s++) if (Typical(types[k], Of(types[k], s))) seeds.Add(s);
                json.Append($"  \"{types[k]}\": [{string.Join(", ", seeds)}]{(k + 1 < types.Length ? "," : "")}\n");
            }
            json.Append("}\n");
            var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "scripts"))) dir = dir.Parent;
            File.WriteAllText(Path.Combine(dir!.FullName, "scripts", "thumbnail-typical-seeds.json"), json.ToString());
        }
    }

    /// <summary>⭐ R-1066 — the odds of the Afghan wardrobes over seeds 1–500 of the REAL catalogue: fighters wear head cover in ≥ 85 % and a chest rig in ≥ 60 % (and often a waistcoat or a patu);
    /// the women are mostly in the chadari; the elderly men mostly in a lungee with a patu; the boys always in a perahan.</summary>
    [Fact]
    public void TheAfghanOdds_OverSeeds1To500()
    {
        var tkb = HrotEnvironment.CreateTkb();
        Outfit Of(long type, long seed) { var look = EntityBodyLayer3D.Resolve(tkb.GetByType(type)!); return Outfits.For(seed, type, look.Soldier, look.Child, null, ForceId.Hostile, look.Kit, look.Elderly, look.Sex, look.Wardrobe); }
        foreach (long type in new long[] { 2019, 2020, 2021 })
        {
            int covered = 0, rigged = 0, layered = 0;
            for (int s = 1; s <= 500; s++)
            {
                var o = Of(type, s);
                if (o.HeadKind is OutfitHead.Turban or OutfitHead.Pakol or OutfitHead.Shemagh or OutfitHead.Skullcap or OutfitHead.ShemaghFaceWrap) covered++;
                if (o.OverKind == OutfitOver.ChestRig) rigged++;
                if (o.ExtraKind is OutfitExtra.Waistcoat or OutfitExtra.Patu or OutfitExtra.WaistcoatPatu) layered++;
                Assert.Equal(OutfitTop.Perahan, o.TopKind); Assert.Equal(Legwear.Tunban, o.Legwear); Assert.Equal(CamoPattern.None, o.Camo);
            }
            Assert.True(covered >= 425, $"type {type}: head cover in {covered}/500");
            Assert.True(rigged >= 300, $"type {type}: a chest rig in {rigged}/500");
            Assert.True(layered >= 200, $"type {type}: a waistcoat or a patu in {layered}/500");
        }
        int Count(long type, Func<Outfit, bool> f) { int n = 0; for (int s = 1; s <= 500; s++) if (f(Of(type, s))) n++; return n; }
        Assert.True(Count(1112, o => o.TopKind == OutfitTop.Chadari) >= 350, "the women are mostly in the chadari");
        Assert.True(Count(1116, o => o.TopKind == OutfitTop.Chadari) >= 300, "so are the elderly women");
        Assert.True(Count(1115, o => o.HeadKind == OutfitHead.Turban && o.ExtraKind == OutfitExtra.Patu) >= 220, "the elders: a lungee and a patu");
        Assert.Equal(500, Count(1113, o => o.TopKind == OutfitTop.Perahan && o.Legwear == Legwear.Tunban));
        Assert.True(Count(1111, o => o.HeadKind is OutfitHead.Pakol or OutfitHead.Turban or OutfitHead.Skullcap) >= 350, "a man covers his head");
        Assert.True(Count(1114, o => o.HeadKind == OutfitHead.Hijab) >= 380, "a girl's coloured headscarf");
    }

    /// <summary>⭐ R-1064 — the Extra slot is drawn LAST: giving a wardrobe an Extra slot changes nothing but the extra (every other field of every person is identical), so no existing person moves.
    /// 🔴 Red-proof: draw the extra BEFORE the beard in <c>Outfits.Dress</c> ⇒ the beard (and the face) of the people of a wardrobe with an Extra changes.</summary>
    [Fact]
    public void TheExtraSlot_IsDrawnLast_SoNothingElseMoves()
    {
        // ⭐ the pin: every slot is exactly two draws, so only a FIXED expectation tells where the Extra draws sit — the beard kinds (the slot just before the Extra) of 1105's first 80 people
        {
            var look = EntityBodyLayer3D.Resolve(HrotEnvironment.CreateTkb().GetByType(1105)!);
            var sb = new System.Text.StringBuilder();
            for (int s = 1; s <= 80; s++) sb.Append((int)Outfits.For(s, 1105, look.Soldier, look.Child, null, ForceId.Neutral, look.Kit, look.Elderly, look.Sex, look.Wardrobe).Face.Beard);
            if (Environment.GetEnvironmentVariable("HROT_GEN_PIN") == "1") File.WriteAllText("/tmp/claude-0/pin.txt", sb.ToString());
            Assert.Equal(BeardPin1105, sb.ToString());
        }
        var plain = OutfitCatalog.MiddleEastMan;
        var withExtra = plain with { Extra = S(O("Waistcoat", Black)) };
        var a = Wardrobe.Of(plain)!; var b = Wardrobe.Of(withExtra)!;
        for (int seed = 1; seed <= 500; seed++)
        {
            var x = Outfits.For(seed, 1105, false, false, null, ForceId.Neutral, PersonKit.Civilian, false, PersonSex.Male, a);
            var y = Outfits.For(seed, 1105, false, false, null, ForceId.Neutral, PersonKit.Civilian, false, PersonSex.Male, b);
            Assert.Equal(OutfitExtra.None, x.ExtraKind);
            Assert.Equal(OutfitExtra.Waistcoat, y.ExtraKind);
            Assert.Equal(x with { ExtraKind = y.ExtraKind, ExtraColour = y.ExtraColour }, y);
        }
    }

    /// <summary>The OF-7 promise for the new garments: dressing and detailing 500 people in every garment allocates nothing once warm.</summary>
    [Fact]
    public void TheNewGarments_AllocateNothing_ForFiveHundredPeople()
    {
        var tkb = HrotEnvironment.CreateTkb();
        RegisterAll(tkb);
        var looks = new EntityBodyLayer3D.TypeLook[Garments.Length];
        for (int i = 0; i < looks.Length; i++) looks[i] = LookOf(tkb, i);
        var into = new List<PartDetail>(64);
        long Run()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int p = 0; p < 500; p++)
            {
                int i = p % looks.Length; ref readonly var look = ref looks[i];
                Outfits.Details(Outfits.For(1000 + p, TypeOf(i), look.Soldier, look.Child, null, ForceId.Neutral, look.Kit, look.Elderly, look.Sex, look.Wardrobe), 1000 + p, into);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Run(); Run();
        Assert.Equal(0L, Run());
    }

    [SkippableFact]
    public void TheFrames_ShowEachGarment_FrontAndSide_AgainstThePlainFigure()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = AirCombatLooksRail.ShotsDir();
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>();
        var tkb = HrotEnvironment.CreateTkb();
        RegisterAll(tkb);
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        float ground = query.GroundHeightAt(spot.X, spot.Y);
        // ⭐ the scene's sun travels toward (+x, +y, down) (LitShader): a figure facing +x has its front in SHADE. The player-facing view is LIT: the figure turns to face the sun
        //   (toward −x, −y) and the camera looks at his front from three-quarters.
        float turn = -MathF.PI * 0.75f;
        var faceSun = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, turn);
        Entity person = default;
        void Spawn(long type, bool threeFeathers)
        {
            if (person != default) world.DestroyEntity(person);
            while (true)
            {
                var e = world.CreateEntity();
                if (threeFeathers && Outfits.FeatherCount(e.Index) != 3) continue;   // a bare filler entity: nothing is drawn for it
                world.AddComponent(e, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = faceSun });
                world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
                person = e; return;
            }
        }

        using var frame = UiFrameHarness.Begin(420, 560);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera(); canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        AirCombatLooksRail.Pixels Shot(float offset, string? name)
        {
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.05f), 3.6f, MathF.PI + turn + offset, -0.04f);
            var px = AirCombatLooksRail.ScreenOf(frame, () => { frame.Step(canvas.Draw); frame.Step(canvas.Draw); });
            if (name != null) frame.Screenshot(Path.Combine(shots, name));
            return px;
        }
        const float ThreeQuarter = 0.45f;
        // the reference first: the catalogue's white shirt under the same light
        Spawn(ReferenceType, false);
        var refPx = Shot(ThreeQuarter, "outfit-reference-white-shirt-front.png");
        double refTorso = Mean(refPx, TorsoBlock);
        for (int i = 0; i < Garments.Length; i++)
        {
            Spawn(TypeOf(i), Garments[i].Name == "headband");
            var front = Shot(ThreeQuarter, $"outfit-{Garments[i].Name}-front.png");
            Shot(Side, $"outfit-{Garments[i].Name}-side.png");
            // ⭐ a WHITE garment reads white: under the same light its pixels are at least as bright as the existing white shirt's
            if (Garments[i].Name == "thobe") Assert.True(Mean(front, TorsoBlock) >= refTorso - 0.5, $"{Garments[i].Name}: torso {Mean(front, TorsoBlock):0.0} vs the white shirt's {refTorso:0.0}");
            if (Garments[i].Name is "skullcap" or "shemagh") Assert.True(Brightest(front, HeadBand) >= Brightest(refPx, TorsoBlock) - 4.0, $"{Garments[i].Name}: the cap's cloth reads white");
            Assert.True(bodies.DetailsDrawn > 0);
        }
    }

    private const float Side = 1.45f;   // the camera swung to the figure's lit side
    /// <summary>
    /// ⭐⭐ <c>CE-1976</c> (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md BW-3 / BW-10; mockup <c>docs/designs/weapons/bows-visuals.svg</c>, R-4146) — the bows in the REAL frame: the archer standing (carried, then aiming:
    /// the string drawn to the cheek), the crossbowman, the warrior on his horse with his bow, an arrow in flight, arrows stuck at their angle. Frames <c>bows-*.png</c> go to <c>HROT_RAIL_SHOTS</c> (never committed); it SKIPS
    /// without a display (<c>xvfb-run -a</c>). 🔴 Red-proof: without <c>HeldLook.Bow</c> the aimed frame equals the carried frame (the rifle line) — the pixel-difference assert fails.
    /// </summary>
    [SkippableFact]
    public void TheBows_AreDrawnByTheRealFrame_TheArcherAimsAndCarries_TheWarriorRidesWithHisBow_ArrowsFlyAndStick()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(); world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Animals.AnimalState>(); world.RegisterComponent<SimVelocity>(); world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        var tkb = HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Spawn(long type, float dx, float dy, float yaw = 0f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            return e;
        }
        var target = Spawn(TkbEntityTypes.Infantry_Rifleman, 30f, 0f);
        var archer = Spawn(UrbanCombatTkbCatalog.TkbArcher, 0f, 0f);
        var crossbow = Spawn(UrbanCombatTkbCatalog.TkbCrossbowman, -3f, 3f);
        var horse = Spawn(703, -6f, 7f);
        var warrior = Spawn(FunTkbCatalog.TkbNativeWarrior, -6f, 7f);
        world.AddComponent(warrior, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = horse, Exposed = 1, Seat = 0 });
        foreach (var m in new[] { archer, crossbow, warrior }) world.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.AtEase });
        // an arrow in flight (nose along its velocity, pitched over its arc), and arrows stuck at their angle
        void Arrow(long ammo, Vector3 at, Vector3 velocity, bool stuck)
        {
            var e = world.CreateEntity();
            var flight = Vector3.Normalize(velocity);
            var axis = Vector3.Cross(Vector3.UnitX, flight);
            var rot = axis.LengthSquared() > 1e-6f ? Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Vector3.Dot(Vector3.UnitX, flight))) : Quaternion.Identity;
            world.AddComponent(e, new SimTransform { Position = at, Rotation = rot });
            world.AddComponent(e, new SimVelocity { Linear = stuck ? Vector3.Zero : velocity });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.BallisticProjectile
            {
                Ammo = ammo, PreviousPosition = at - velocity * 0.016f,
                Warhead = stuck ? (byte)(Fdp.Toolkit.Combat.Components.WarheadRound.Landed | Fdp.Toolkit.Combat.Components.WarheadRound.Sticks) : (byte)0,
            });
        }
        float gz = G(spot.X + 4f, spot.Y + 5f);
        Arrow(MunitionTkbCatalog.TkbArrow, new Vector3(spot.X + 4f, spot.Y + 5f, gz + 1.6f), new Vector3(40f, 0f, 3f), false);
        Arrow(MunitionTkbCatalog.TkbCrossbowBolt, new Vector3(spot.X + 4f, spot.Y + 5.6f, gz + 1.5f), new Vector3(60f, 0f, -2f), false);
        for (int i = 0; i < 4; i++)
        {
            float x = spot.X + 8f + i * 0.5f, y = spot.Y + 1.0f;
            Arrow(i == 3 ? MunitionTkbCatalog.TkbCrossbowBolt : MunitionTkbCatalog.TkbArrow, new Vector3(x, y, G(x, y) + 0.1f), new Vector3(0.9f, 0.1f * i, -0.35f - 0.1f * i), true);
        }

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera(); canvas.Camera = two;
        var pose = ArticulationPose.Neutral;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => pose);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        var rounds = new Hrot.UI.Common.Effects.ProjectileLayer(() => world, () => tkb, () => sw.Camera3D) { GroundHeight = G };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(rounds);
        sw.Set(true, animate: false);
        AirCombatLooksRail.Pixels Shot(Vector3 look, float dist, float yaw, float pitch, string name)
        {
            sw.Camera3D.Pose = new CameraPose(look, dist, yaw, pitch);
            Clock(0.05);
            var px = AirCombatLooksRail.ScreenOf(frame, () => { frame.Step(canvas.Draw); frame.Step(canvas.Draw); });
            frame.Screenshot(Path.Combine(shots, name));
            return px;
        }
        const float Side = MathF.PI * 0.5f;
        for (int i = 0; i < 6; i++) { Clock(0.1); frame.Step(canvas.Draw); }
        Vector3 Eye(float dx, float dy, float up) => new(spot.X + dx, spot.Y + dy, G(spot.X + dx, spot.Y + dy) + up);
        // carried first (the bow hangs in the left hand), then aiming (the string drawn to the cheek): the frames must differ
        var carried = Shot(Eye(0f, 0f, 1.0f), 4.2f, Side, -0.04f, "bows-1-archer-carried-side.png");
        Shot(Eye(0f, 0f, 1.0f), 4.2f, 0.4f, -0.04f, "bows-1b-archer-carried-back.png");   // the quiver, from behind
        Shot(Eye(0f, 0f, 1.0f), 4.2f, Side + 0.9f, -0.1f, "bows-1c-archer-carried-34.png");
        world.SetComponent(archer, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = target });
        world.SetComponent(crossbow, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = target });
        world.SetComponent(warrior, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = target });
        for (int i = 0; i < 8; i++) { Clock(0.1); frame.Step(canvas.Draw); }
        var aimed = Shot(Eye(0f, 0f, 1.0f), 4.2f, Side, -0.04f, "bows-2-archer-aiming-side.png");
        Shot(Eye(0f, 0f, 1.2f), 3.2f, Side + 0.9f, -0.1f, "bows-2b-archer-aiming-34.png");
        Shot(Eye(-3f, 3f, 1.0f), 4.2f, Side, -0.04f, "bows-3-crossbowman-aiming-side.png");
        Shot(Eye(-6f, 7f, 1.3f), 6f, Side, -0.04f, "bows-4-warrior-on-horse-aiming-side.png");
        Shot(Eye(-6f, 7f, 1.4f), 5f, Side + 0.7f, -0.1f, "bows-4b-warrior-on-horse-34.png");
        Shot(Eye(4f, 5.3f, 1.5f), 3.2f, Side, -0.03f, "bows-5-arrow-and-bolt-in-flight.png");
        Shot(Eye(9f, 1f, 0.3f), 3.0f, Side, -0.08f, "bows-6-stuck-arrows.png");
        Shot(Eye(9f, 1f, 0.3f), 3.0f, Side + 0.9f, -0.2f, "bows-6b-stuck-arrows-34.png");
        Assert.True(AirCombatLooksRail.Diff(carried, aimed) > 600, $"the bow drawn to the cheek differs from the carried one on screen: {AirCombatLooksRail.Diff(carried, aimed)} px");
        Assert.True(bodies.BodiesDrawn >= 5, "the target, the archer, the crossbowman, the horse and the warrior are drawn");
        Assert.Equal(6, rounds.Drawn);   // the arrow and the bolt in flight, four stuck
    }

    private static readonly (int X0, int Y0, int X1, int Y1) TorsoBlock = (160, 225, 192, 250), HeadBand = (160, 140, 260, 175);
    private static double Lum(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    private static double Mean(AirCombatLooksRail.Pixels p, (int X0, int Y0, int X1, int Y1) r)
    {
        double s = 0; int n = 0;
        for (int y = r.Y0; y < r.Y1; y++) for (int x = r.X0; x < r.X1; x++) { s += Lum(p.Colours[y * p.W + x]); n++; }
        return s / n;
    }
    private static double Brightest(AirCombatLooksRail.Pixels p, (int X0, int Y0, int X1, int Y1) r)
    {
        double m = 0;
        for (int y = r.Y0; y < r.Y1; y++) for (int x = r.X0; x < r.X1; x++) m = Math.Max(m, Lum(p.Colours[y * p.W + x]));
        return m;
    }
}
