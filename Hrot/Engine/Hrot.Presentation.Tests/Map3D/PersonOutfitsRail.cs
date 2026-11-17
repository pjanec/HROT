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
    private static readonly string White = Hex(236, 234, 226), Black = Hex(24, 24, 26), Khaki = Hex(165, 154, 114), Olive = Hex(94, 96, 64), Buck = Hex(201, 160, 106),
        BuckD = Hex(156, 118, 70), Red = Hex(140, 42, 36), HairBlack = Hex(20, 18, 18), Skin = OutfitCatalog.SkinTones[2], Grey = Hex(96, 100, 106);

    private static OutfitOption O(string kind, params string[] colours) => new() { Kind = kind, Weight = 1f, Colours = [.. colours] };
    private static OutfitSlot S(OutfitOption o) => new() { Options = [o] };

    /// <summary>(name, top, legs, over, head, hair, beard) — the garment under test and the plainest everything else.</summary>
    private static readonly (string Name, OutfitOption Top, OutfitOption Legs, OutfitOption Over, OutfitOption Head, OutfitOption Hair, OutfitOption Beard)[] Garments =
    [
        ("thobe",       O("Robe", White),       O("Hidden"),   O("None"),     O("None"),                           O("Short", HairBlack), O("None")),
        ("abaya",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("None"),                           O("Short", HairBlack), O("None")),
        ("shemagh",     O("Robe", White),       O("Hidden"),   O("None"),     O("Shemagh", OutfitCatalog.CheckShemagh), O("Short", HairBlack), O("None")),
        ("skullcap",    O("Shirt", White),      O("Trousers", Grey), O("None"), O("Skullcap", White),              O("Short", HairBlack), O("None")),
        ("hijab",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("Hijab", Black),                   O("Short", HairBlack), O("None")),
        ("niqab",       O("Abaya", Black),      O("Hidden"),   O("None"),     O("Niqab", Black),                   O("Short", HairBlack), O("None")),
        ("chestrig",    O("Shirt", Khaki),      O("Trousers", Grey), O("ChestRig", Olive), O("None"),              O("Short", HairBlack), O("None")),
        ("facewrap",    O("Shirt", Khaki),      O("Trousers", Grey), O("ChestRig", Olive), O("ShemaghFaceWrap", Khaki), O("Short", HairBlack), O("None")),
        ("buckskin",    O("Buckskin", Buck),    O("Shorts", Skin), O("None"), O("None"),                           O("Short", HairBlack), O("None")),
        ("breechcloth", O("TShirt", Skin),      O("Breechcloth", BuckD), O("None"), O("None"),                     O("Short", HairBlack), O("None")),
        ("headband",    O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("Headband", Red),                  O("Short", HairBlack), O("None")),
        ("braids",      O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("None"),                           O("Braids", HairBlack), O("None")),
        ("facepaint",   O("TShirt", Skin),      O("Shorts", Skin), O("None"), O("None"),                           O("Short", HairBlack), O("Paint")),
    ];

    private static long TypeOf(int i) => First + i;

    private static OutfitDto DtoOf(int i)
    {
        var g = Garments[i];
        return new OutfitDto { Skin = S(O("Skin", Skin)), Top = S(g.Top), Legs = S(g.Legs), Over = S(g.Over), Head = S(g.Head), Hair = S(g.Hair), Beard = S(g.Beard) };
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
        Assert.Equal(2, DetailsOf(tkb, Index("braids")).Count(d => d.Part == BlockFigure.TorsoIndex && d.Extent.Z * 0.56f > 0.3f));
        var paint = Dressed(tkb, Index("facepaint")); Assert.True(paint.FacePaint); Assert.Equal(BeardKind.None, paint.Face.Beard);
        Assert.Equal(3, DetailsOf(tkb, Index("facepaint")).Count);
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
        var e = world.CreateEntity();
        world.AddComponent(e, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });   // facing +x
        world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)TypeOf(0) });

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

        AirCombatLooksRail.Pixels Shot(int type, float yaw, string? name)
        {
            world.SetComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.05f), 3.6f, yaw, -0.04f);
            var px = AirCombatLooksRail.ScreenOf(frame, () => { frame.Step(canvas.Draw); frame.Step(canvas.Draw); });
            if (name != null) frame.Screenshot(Path.Combine(shots, name));
            return px;
        }
        for (int i = 0; i < Garments.Length; i++)
        {
            var front = Shot((int)TypeOf(i), MathF.PI, $"outfit-{Garments[i].Name}-front.png");
            var side = Shot((int)TypeOf(i), MathF.PI * 0.5f, $"outfit-{Garments[i].Name}-side.png");
            // the garment is DRAWN: the figure differs from the same figure in the plainest dress (the first garment's type, a thobe, for the thobe's own: the skullcap's)
            int plain = i == Index("skullcap") ? Index("thobe") : Index("skullcap");
            var base0 = Shot((int)TypeOf(plain), MathF.PI, null);
            Assert.True(AirCombatLooksRail.Diff(front, base0) > 300, $"{Garments[i].Name}: the front frame differs from the plain figure");
            Assert.True(bodies.DetailsDrawn > 0);
        }
    }
}
