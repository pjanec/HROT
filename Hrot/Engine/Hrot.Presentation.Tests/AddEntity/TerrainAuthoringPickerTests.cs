using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Constants;
using Hrot.Core.Mission;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ Terrain authoring U-1 + U-2 (docs/DESIGN_Terrain_Authoring.md §15; the mockup <c>terrain-authoring-2-blocks.svg</c>): Add Entity's six terrain groups list every shipped recipe, a pick arms the block tool with its spec, a block
/// is BORN with its <c>TerrainBlock</c> (the seed its ghost showed, the heading the drag and R gave), and the ghost is the pure expander's answer on the cursor — cached, so a steady cursor costs nothing.
/// ✅ Red-proofs (each a compiling mutation, applied and reverted in one command): the <c>Terrain</c> branch of <c>AddEntityAction.Place</c> removed ⇒ the arm rail red; <c>TerrainBirth.BornWith</c> not adding the block ⇒ the born rail red;
/// <c>TerrainPlacement.Preview</c> without its cache ⇒ the 0-B rail red.
/// </summary>
public sealed class TerrainAuthoringPickerTests : IDisposable
{
    private readonly string _src = ContentLayout.TryFindSourceTree()!;
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tap-" + Guid.NewGuid().ToString("N")[..10]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    private IReadOnlyList<TerrainPick> Shipped() => TerrainPickCatalog.Build(new[] { ContentLayout.TerrainRecipeAssets(_src) }, new[] { ContentLayout.BuildingAssets(_src) });

    // ── U-1: the groups ────────────────────────────────────────────────────

    [Fact]
    public void U1_TheCatalogue_ListsEveryShippedRecipeAsABlock_AndEveryBuildingTemplateAsAHouse()
    {
        var picks = Shipped();
        var recipes = Directory.EnumerateFiles(ContentLayout.TerrainRecipeAssets(_src), "*.recipe.json").Select(f => Path.GetFileName(f)[..^".recipe.json".Length]).OrderBy(x => x).ToArray();
        var templates = Directory.EnumerateFiles(ContentLayout.BuildingAssets(_src), "*.building.json").Select(f => Path.GetFileName(f)[..^".building.json".Length]).OrderBy(x => x).ToArray();
        Assert.NotEmpty(recipes);
        Assert.Equal(recipes, picks.Where(p => p.Kind == TerrainPickKind.Block).Select(p => p.Id).OrderBy(x => x).ToArray());
        Assert.Equal(templates, picks.Where(p => p.Kind == TerrainPickKind.House).Select(p => p.Id).OrderBy(x => x).ToArray());
        Assert.All(picks.Where(p => p.Kind == TerrainPickKind.Block), p => Assert.Equal(TerrainPickCatalog.Blocks, p.Group));
        Assert.All(picks.Where(p => p.Kind == TerrainPickKind.House), p => Assert.Equal(TerrainPickCatalog.Buildings, p.Group));
        Assert.All(picks, p => Assert.False(string.IsNullOrWhiteSpace(p.Line)));
    }

    private sealed class RecordingSpawn : ISpawnController
    {
        public readonly List<string> Calls = new();
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) => Calls.Add($"point {tkbType} {initialPropertiesJson}");
        public void StartAreaAuthoringMode(string styleOverrideJson = "") => Calls.Add("area");
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "") => Calls.Add($"area-{tkbType}-{styleOverrideJson}");
        public void StartRouteAuthoringMode() => Calls.Add("route");
        public void StartZoneAuthoringMode(string styleOverrideJson = "") => Calls.Add("zone");
    }

    private (AddEntityAction Action, RecordingSpawn Spawn, Func<PickerRequest?> Request, Func<Action<PickerResult>?> OnChosen) Rig()
    {
        var tkb = new TkbDatabase();
        NedTkbCatalog.RegisterAll(tkb);
        var spawn = new RecordingSpawn();
        PickerRequest? req = null; Action<PickerResult>? chosen = null;
        var action = new AddEntityAction(() => tkb, () => (r, cb) => { req = r; chosen = cb; }, () => spawn, gallery: () => EntityTypeCatalogTests.ShippedGallery(), terrain: Shipped);
        return (action, spawn, () => req, () => chosen);
    }

    [Fact]
    public void U1_AddEntity_OffersTheSixTerrainGroups_BlocksListsEveryRecipe_AndAPickArmsTheBlockTool()
    {
        var (action, spawn, request, onChosen) = Rig();
        Assert.True(action.Open(GlobalActionIds.AddEntityFriendly));
        var r = request()!;

        // the six groups, after the file's own, before Other (B-5)
        var order = r.GroupSidebar!.Order.ToList();
        int first = order.IndexOf(TerrainPickCatalog.Blocks);
        Assert.True(first > 0);
        Assert.Equal(TerrainPickCatalog.Groups, order.Skip(first).Take(TerrainPickCatalog.Groups.Count).ToArray());   // ⭐ CE-4592 — seven since Earthworks
        Assert.Equal(GalleryGroups.OtherName, order[^1]);

        var rows = r.ItemsProvider().ToList();
        foreach (var recipe in Shipped().Where(p => p.Kind == TerrainPickKind.Block))
            Assert.Contains(rows, e => e.Name == recipe.Name && e.Category!.StartsWith(TerrainPickCatalog.Blocks + "/") && e.Tag is EntityTypeEntry { Terrain: { } });
        Assert.Contains(rows, e => e.Category!.StartsWith(TerrainPickCatalog.Buildings));
        Assert.Contains(rows, e => e.Category!.StartsWith(TerrainPickCatalog.Clutter));   // backend's T-2 kinds are built: every group lists

        Assert.Empty(spawn.Calls);   // 🔒 D4: nothing is armed before the pick
        var hamlet = rows.Single(e => e.Name == "Hamlet");
        onChosen()!(new PickerResult(new[] { hamlet }));
        Assert.Equal($"point {TkbEntityTypes.TerrainBlock} {{\"$terrain\":\"block:hamlet\"}}", Assert.Single(spawn.Calls));

        var house = rows.First(e => e.Tag is EntityTypeEntry { Terrain.Kind: TerrainPickKind.House });
        onChosen()!(new PickerResult(new[] { house }));
        Assert.StartsWith($"point {TkbEntityTypes.TerrainFeature} {{\"$terrain\":\"house:", spawn.Calls[1]);
    }

    [Fact]
    public void U1_ATerrainPickHasNoSide_ItsPaneNeverSaysPlacedAs_AndTheTerrainRoleOnlyShowsOnTheHostThatPassesIt()
    {
        var tkb = new TkbDatabase(); NedTkbCatalog.RegisterAll(tkb);
        var entry = EntityTypeCatalog.ToPickerEntries(EntityTypeCatalog.Build(tkb, gallery: EntityTypeCatalogTests.ShippedGallery(), terrain: Shipped()), sideBearing: true, gallery: true)
                                     .First(e => e.Name == "Hamlet");
        var rows = AddEntityPane.Rows(tkb, entry, eForceIdentifier.FORCE_FRIENDLY);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, r => r.Label == "Placed as" && r.Value == "Friendly");
        Assert.Contains(rows, r => r.Label == "Houses");

        // a host that passes no terrain source (CGF, SimHost, IG) lists no terrain entry
        var none = EntityTypeCatalog.Build(tkb, gallery: EntityTypeCatalogTests.ShippedGallery());
        Assert.DoesNotContain(none, e => e.Terrain is not null);
    }

    // ── U-2: born with, and the ghost ──────────────────────────────────────

    [Fact]
    public void U2_ABlockIsBornWithItsTerrainBlock_TheSeedTheGhostShowed_AndHeadingNorthIsZero()
    {
        var comps = new List<object>();
        var north = Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth;
        TerrainBirth.BornWith(TkbEntityTypes.TerrainBlock, "block:hamlet", north, comps, seed: 4471);
        TerrainBirth.BornWith(TkbEntityTypes.TerrainBlock, "block:hamlet", north, comps, seed: 9);   // never twice
        var b = Assert.Single(comps.OfType<TerrainBlock>());
        Assert.Equal(("hamlet", 4471u, 0f, TerrainBlock.FitAll), (b.Recipe.ToString(), b.Seed, b.HeadingDeg, b.Fit));
        Assert.Equal("Hamlet", Assert.Single(comps.OfType<EntityInfo>()).Name.ToString());

        // R adds a quarter turn
        var turned = new List<object>();
        TerrainBirth.BornWith(TkbEntityTypes.TerrainBlock, "block:copse", north, turned, 1, extraHeadingDeg: 90f);
        Assert.Equal(90f, turned.OfType<TerrainBlock>().Single().HeadingDeg);

        // a house is a feature; a spec of the other kind, or on another type, adds nothing
        var h = new List<object>();
        TerrainBirth.BornWith(TkbEntityTypes.TerrainFeature, "house:house-2f", north, h);
        Assert.Equal("house-2f", h.OfType<TerrainFeature>().Single().Template.ToString());
        var wrong = new List<object>();
        TerrainBirth.BornWith(TkbEntityTypes.TerrainBlock, "house:house-2f", north, wrong);
        TerrainBirth.BornWith(TkbEntityTypes.Infantry_Rifleman, "block:hamlet", north, wrong);
        Assert.Empty(wrong);
    }

    [Fact]
    public void U2_TheSpecRidesThePlacementJson_AndComesOutAgain_NeverReachingTheAttributeCompiler()
    {
        var pick = Shipped().First(p => p.Id == "hamlet");
        var (json, spec) = TerrainBirth.Split(TerrainBirth.PlacementJson(pick));
        Assert.Null(json);
        Assert.Equal("block:hamlet", spec);
        Assert.Equal((TerrainPickKind.Block, "hamlet"), TerrainBirth.Parse(spec));
        Assert.Equal(("{\"x\":1}", (string?)null), TerrainBirth.Split("{\"x\":1}"));
    }

    private (TerrainGhostContext Ctx, string Folder) GhostGround(bool allWater = false, bool roads = false)
    {
        string assets = Path.Combine(_tmp, "assets"), terrain = Path.Combine(assets, "terrain", "g");
        Directory.CreateDirectory(terrain);
        foreach (var kind in new[] { "terrain-recipes", "buildings" })
        {
            Directory.CreateDirectory(Path.Combine(assets, kind));
            foreach (var f in Directory.EnumerateFiles(Path.Combine(_src, "assets", kind))) File.Copy(f, Path.Combine(assets, kind, Path.GetFileName(f)), overwrite: true);
        }
        // ⭐ T-5 — roads: test-town's own net (a cross at (200, 200), the east-west road along y = 200) when asked
        File.WriteAllText(Path.Combine(terrain, "terrain.json"), roads ? """{"schemaVersion":2,"name":"g","world":"w.geojson","roadNetworks":["roads.json"]}""" : """{"schemaVersion":2,"name":"g","world":"w.geojson"}""");
        if (roads) File.Copy(Path.Combine(_src, "assets", "terrain", "test-town", "roads.json"), Path.Combine(terrain, "roads.json"));
        // a 600 m ground with a lake in the middle (x 280…320): a block placed on it must keep its houses out of the water
        File.WriteAllText(Path.Combine(terrain, "w.geojson"), """{"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,600,600],"groundZ":0},"features":[{"type":"Feature","properties":{"kind":"surface","surface":"water"},"geometry":{"type":"Polygon","coordinates":[[[LO,0],[HI,0],[HI,600],[LO,600],[LO,0]]]""".Replace("LO", allWater ? "0" : "280").Replace("HI", allWater ? "600" : "320") + """}}]}""");
        var def = Path.Combine(terrain, "terrain.json");
        var world = TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "g")!;
        return (new TerrainGhostContext(world, terrain), terrain);
    }

    [Fact]
    public void U2_TheGhostIsTheExpandersAnswer_FitsItselfToTheSite_AndAHouseKeepsOutOfTheWater()
    {
        var (ctx, _) = GhostGround();
        var pl = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7);
        var onLand = pl.Preview(new Vector2(100f, 300f))!;
        Assert.NotEmpty(onLand.Houses);
        Assert.True(onLand.Fits);
        Assert.StartsWith("clear of water", onLand.Line2);
        Assert.NotEmpty(onLand.Outline);

        // dropped on the lake the same recipe finds no room: the verdict warns (the mockup's amber line) and no house stands in the water
        var onLake = pl.Preview(new Vector2(300f, 300f))!;
        Assert.All(onLake.Houses, h => Assert.All(h, p => Assert.False(p.X is > 280f and < 320f)));

        // the same inputs are the same ghost (the layout is a function of the seed)
        var again = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7).Preview(new Vector2(100f, 300f))!;
        Assert.Equal(onLand.Houses.Select(h => h[0]).ToArray(), again.Houses.Select(h => h[0]).ToArray());

        // a single house previews its own footprint
        var house = new TerrainPlacement(TerrainPickKind.House, "house-2f", () => ctx).Preview(new Vector2(50f, 50f))!;
        Assert.Single(house.Houses);
    }

    /// <summary>
    /// ⭐ T-5 (CE-3431, backend's <c>AlignRoad</c>; mockup 2's hint <i>aligned to the track (R to rotate)</i>) — the ghost previews the alignment LIVE: dropped within reach of a road, a block placed at 30° turns to the road (its houses are the
    /// expander's answer at the ALIGNED heading, not at 30°) and the first hint line says so; out of reach, or with the Align bit off, the heading stays as placed and the line is the plain one. A steady cursor next to the road still costs 0 B.
    /// 🔴 Red-proofs (compile): <c>TerrainGhost.Compute</c> passing no road net to the expander ⇒ the houses stay at 30° ⇒ red; the hint never saying <i>aligned</i> ⇒ red.
    /// </summary>
    [Fact]
    public void U2_TheGhostAlignsToTheRoadLive_TheHintSaysSo_AndASteadyCursorAtTheRoadAllocatesNothing()
    {
        var (ctx, folder) = GhostGround(roads: true);
        Assert.NotNull(ctx.Roads);
        var at = new Vector2(100f, 195f);
        var recipe = TerrainRecipe.Find("hamlet", ctx.RecipeRoots);
        Assert.True(ctx.Roads!.TryAlign(at, recipe.Radius, 30f, out float aligned));
        Assert.NotEqual(30f, aligned);   // the case discriminates: the road really turns this block

        var ghost = TerrainGhost.Compute(TerrainPickKind.Block, "hamlet", ctx, at, 30f, 7);
        Assert.Equal("aligned to the track (R to rotate)", ghost.Line1);
        var want = FeatureExpander.Expand(recipe, 7, at, aligned, TerrainBlock.FitAll, ctx.Base, "ghost", ctx.Assets, ctx.Roads);
        var plain = FeatureExpander.Expand(recipe, 7, at, 30f, TerrainBlock.FitAll, ctx.Base, "ghost", ctx.Assets);
        Assert.NotEqual(plain.Points.ToArray(), want.Points.ToArray());   // aligned ≠ as placed
        Assert.Equal(want.ShapesOrEmpty.Take(want.Houses).Select(h => h[0]).ToArray(), ghost.Houses.Take(want.Houses).Select(h => h[0]).ToArray());

        // the Align bit off, or out of the road's reach: the heading is as placed and the line is the plain one
        var off = TerrainGhost.Compute(TerrainPickKind.Block, "hamlet", ctx, at, 30f, 7, (byte)(TerrainBlock.FitAll & ~TerrainBlock.AlignRoad));
        Assert.Equal("heading 30° · R to rotate", off.Line1);
        Assert.Equal(plain.ShapesOrEmpty.Take(plain.Houses).Select(h => h[0]).ToArray(), off.Houses.Take(plain.Houses).Select(h => h[0]).ToArray());
        var far = TerrainGhost.Compute(TerrainPickKind.Block, "hamlet", ctx, new Vector2(500f, 560f), 30f, 7);
        Assert.Equal("heading 30° · R to rotate", far.Line1);
        // a terrain without a road net never aligns
        var (noRoads, _) = GhostGround();
        Assert.Null(noRoads.Roads);
        Assert.Equal("heading 30° · R to rotate", TerrainGhost.Compute(TerrainPickKind.Block, "hamlet", noRoads, at, 30f, 7).Line1);

        // the placement carries it, and a steady cursor beside the road is the cached shape — 0 B per frame
        var pl = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7);
        var first = pl.Preview(at, 30f)!;
        Assert.Equal("aligned to the track (R to rotate)", first.Line1);
        for (int i = 0; i < 300; i++) pl.Preview(new Vector2(100.1f, 195.1f), 30f);   // warm: the JIT allocates on its first calls (tiering)
        long before = GC.GetAllocatedBytesForCurrentThread();
        int same = 0;
        for (int i = 0; i < 200; i++) if (ReferenceEquals(first, pl.Preview(new Vector2(100.1f, 195.1f), 30f))) same++;
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(200, same);
        Assert.Equal(0, after - before);   // 0 B per steady frame
        _ = folder;
    }

    [Fact]
    public void U2_AGhostWithNoRoomWarns_TheMockupsAmberLine_AndDrawsNoHouse()
    {
        var (ctx, _) = GhostGround(allWater: true);
        var shape = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7).Preview(new Vector2(300f, 300f))!;
        Assert.Empty(shape.Houses);
        Assert.False(shape.Fits);
        Assert.StartsWith("only 0 of", shape.Line2);
    }

    [Fact]
    public void U2_RRotatesAQuarterTurn_TheClickRenewsTheSeed_AndASteadyCursorExpandsNothing()
    {
        var (ctx, _) = GhostGround();
        var pl = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7);
        var first = pl.Preview(new Vector2(100f, 300f));
        Assert.Equal(1, pl.Computations);

        for (int i = 0; i < 300; i++) pl.Preview(new Vector2(100.1f, 300.1f));   // warm: the JIT allocates on its first calls (tiering)
        int same = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) if (ReferenceEquals(first, pl.Preview(new Vector2(100.1f, 300.1f)))) same++;   // under half a metre: the cached shape
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(200, same);
        Assert.Equal(1, pl.Computations);
        Assert.Equal(0, after - before);   // 0 B per steady frame

        pl.Rotate();
        Assert.Equal(90f, pl.RotationDeg);
        Assert.NotSame(first, pl.Preview(new Vector2(100f, 300f)));
        for (int i = 0; i < 3; i++) pl.Rotate();
        Assert.Equal(0f, pl.RotationDeg);

        uint seed = pl.Seed;
        pl.Renew();
        Assert.InRange(pl.Seed, 1000u, 9999u);
        _ = seed;
    }

    [Fact]
    public void U2_TheArmedToolDrawsTheGhost_RTurnsIt_AndTheClickCreatesOneBlockEntity()
    {
        var (ctx, _) = GhostGround();
        var pl = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 7);
        var made = new List<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>();
        var gizmo = new Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo(cmd =>
        {
            cmd.InitialComponents ??= new List<object>();
            TerrainBirth.BornWith(cmd.TkbType, "block:hamlet", cmd.InitialTransform?.Rotation ?? Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth, cmd.InitialComponents, pl.Seed, pl.RotationDeg);
            made.Add(cmd);
        }, TkbEntityTypes.TerrainBlock, autoPopOnPlace: false, terrain: pl);
        gizmo.OnDragUpdate(new Vector3(100f, 300f, 0f));
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        gizmo.UpdateAndDraw(new EntityRepository(), 0f, buffer);
        var prims = buffer.GetFrame().ToArray();
        Assert.Contains(prims, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Line);   // the dashed outline and the footprints
        Assert.Contains(prims, p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.FilledTriangle);   // the tan houses

        gizmo.OnKeyEvent((Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey)82, true);   // R
        Assert.Equal(90f, pl.RotationDeg);

        uint ghostSeed = pl.Seed;
        gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, true, new Vector3(100f, 300f, 0f));
        gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, new Vector3(100f, 300f, 0f));
        var cmd = Assert.Single(made);   // ONE entity per click (B-2)
        Assert.Equal((long)TkbEntityTypes.TerrainBlock, (long)cmd.TkbType);
        var block = cmd.InitialComponents!.OfType<TerrainBlock>().Single();
        Assert.Equal((ghostSeed, 90f), (block.Seed, block.HeadingDeg));   // the layout the ghost showed, turned as R said
        Assert.NotEqual(ghostSeed, pl.Seed);                               // the next ghost is another layout
    }

    // ── U-7: the single-feature tools (backend's T-2) ───────────────────────────

    [Fact]
    public void U7_TheFeaturePicks_FillTheFourGroups_ClutterIsEveryClutterType_AndTheScatterIsGreyedWithItsReason()
    {
        var picks = TerrainPickCatalog.FeaturePicks();
        Assert.Contains(picks, p => p.Group == TerrainPickCatalog.Vegetation && p.FeatureKind == TerrainFeature.Wood && p.Shape == TerrainShape.Area);
        Assert.Contains(picks, p => p.Group == TerrainPickCatalog.Walls && p.FeatureKind == TerrainFeature.Hedge && p.Shape == TerrainShape.Line);
        Assert.Contains(picks, p => p.Group == TerrainPickCatalog.Water && p.FeatureKind == TerrainFeature.Bridge && p.Shape == TerrainShape.Line);
        Assert.Equal(ClutterTypes.All.Count, picks.Count(p => p.FeatureKind == TerrainFeature.Clutter));
        Assert.All(picks.Where(p => p.FeatureKind == TerrainFeature.Clutter), p => Assert.Equal(TerrainShape.Point, p.Shape));
        var scatter = picks.Single(p => p.FeatureKind == TerrainFeature.Scatter);
        Assert.Contains("T-3", scatter.Disabled);
        Assert.All(picks.Where(p => p.FeatureKind != TerrainFeature.Scatter && p.FeatureKind != TerrainFeature.Ground), p => Assert.Null(p.Disabled));   // ⭐ CE-4592 — the earthworks have their own rail
        // every group of the six now lists something
        var all = Shipped();
        foreach (var g in TerrainPickCatalog.Groups) Assert.Contains(all, p => p.Group == g);
    }

    [Fact]
    public void U7_ALinePickArmsTheDrawingTool_APointPickTheClickTool_AndAGreyedPickArmsNothing()
    {
        var spawn = new RecordingSpawn();
        var picks = TerrainPickCatalog.FeaturePicks();
        Assert.True(PlacementToolRegistry.ArmTerrain(spawn, picks.Single(p => p.FeatureKind == TerrainFeature.Wall)));
        Assert.True(PlacementToolRegistry.ArmTerrain(spawn, picks.Single(p => p.FeatureKind == TerrainFeature.Tree)));
        Assert.False(PlacementToolRegistry.ArmTerrain(spawn, picks.Single(p => p.FeatureKind == TerrainFeature.Scatter)));
        Assert.Equal($"area-{TkbEntityTypes.TerrainFeature}-{{\"$terrain\":\"feature:2::Line\"}}", spawn.Calls[0]);
        Assert.Equal($"point {TkbEntityTypes.TerrainFeature} {{\"$terrain\":\"feature:6::Point\"}}", spawn.Calls[1]);
        Assert.Equal(2, spawn.Calls.Count);
    }

    [Fact]
    public void U7_ADrawnWall_IsBornATerrainFeatureWithItsPolyline_NoAreaStyle_AndNeedsTwoPoints()
    {
        var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());
        var arm = new Hrot.ScenarioEditor.Tools.AreaAuthoringArm(manager);
        var made = new List<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>();
        string spec = "feature:2:brick:Line";
        arm.Arm(new Hrot.ScenarioEditor.Tools.AreaAuthoringRequest((TkbTypeId)TkbEntityTypes.TerrainFeature, "", made.Add, MinPoints: 2, Closed: false,
            Born: comps => { comps.RemoveAll(c => c is Hrot.Map.Common.Components.MapOverlayStyle); TerrainBirth.BornWith(TkbEntityTypes.TerrainFeature, spec, Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth, comps, 5); }));
        var gizmo = arm.ActiveGizmo!;
        foreach (var p in new[] { new Vector2(100f, 100f), new Vector2(140f, 100f) })
        {
            gizmo.OnDragUpdate(new Vector3(p.X, p.Y, 0f));
            gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, new Vector3(p.X, p.Y, 0f));
        }
        gizmo.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
        var cmd = Assert.Single(made);   // two points finish a LINE (an area needs three)
        var f = cmd.InitialComponents!.OfType<TerrainFeature>().Single();
        Assert.Equal((TerrainFeature.Wall, "brick", 5u), (f.Kind, f.Template.ToString(), f.Seed));
        Assert.Equal(2, cmd.InitialComponents!.OfType<Hrot.Map.Common.Components.EditablePolyline>().Single().Points.Count);
        Assert.DoesNotContain(cmd.InitialComponents!, c => c is Hrot.Map.Common.Components.MapOverlayStyle);
        Assert.Equal("Wall", cmd.InitialComponents!.OfType<EntityInfo>().Single().Name.ToString());
    }

    // ── follow-up 2: the drawing panel's choices are what the commit reads; the clutter glyphs; the scatter lights with its recipe ───────────────

    /// <summary>
    /// ⭐ Gap C (mockup 3 ① ③) — a wall drawn after the panel switched it to a Wall of 3 m is BORN a Wall of 3 m (not the hedge the pick said), named Wall; a scatter's density row and seed are what the entity carries.
    /// 🔴 Mutation: <c>TerrainDrawOptions.Born</c> not writing the panel's kind (<c>f.Kind = Kind</c> removed) ⇒ the kind assertion red.
    /// </summary>
    [Fact]
    public void U7_TheDrawingPanelsChoices_AreWhatTheCommitReads_KindHeightDensitySeed()
    {
        var o = TerrainDrawOptions.From("feature:4::Line")!;   // the Hedge pick
        Assert.True(o.IsLine); Assert.Equal("Hedge", o.KindName); Assert.Equal(1.5f, o.HeightShown);
        o.SetKind(TerrainFeature.Wall); o.Height = 3f;
        var manager = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.GlobalGizmoManager(new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());
        var arm = new Hrot.ScenarioEditor.Tools.AreaAuthoringArm(manager);
        var made = new List<Fdp.Toolkit.NetworkSpawning.Events.SpawnEntityCommand>();
        arm.Arm(new Hrot.ScenarioEditor.Tools.AreaAuthoringRequest((TkbTypeId)TkbEntityTypes.TerrainFeature, "", made.Add, MinPoints: 2, Closed: false, Born: o.Born));
        var gizmo = arm.ActiveGizmo!;
        foreach (var p in new[] { new Vector2(100f, 100f), new Vector2(140f, 100f) }) { gizmo.OnDragUpdate(new Vector3(p.X, p.Y, 0f)); gizmo.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, new Vector3(p.X, p.Y, 0f)); }
        gizmo.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
        var cmd = Assert.Single(made);
        var f = cmd.InitialComponents!.OfType<TerrainFeature>().Single();
        Assert.Equal((TerrainFeature.Wall, 3f), (f.Kind, f.Pad));
        Assert.Equal("Wall", cmd.InitialComponents!.OfType<EntityInfo>().Single().Name.ToString());
        Assert.Equal(40f, TerrainDrawOptions.Length(new[] { new Vector2(100f, 100f), new Vector2(140f, 100f) }));

        // a bridge's width, a scatter's recipe · density · seed
        var b = TerrainDrawOptions.From("feature:10::Line")!; Assert.True(b.IsBridge); Assert.Equal(4f, b.HeightShown);
        var sc = TerrainDrawOptions.From("feature:11:street-clutter:Area")!;
        sc.ScatterRecipes = new[] { ("street-clutter", "Street clutter", 3f) }; sc.Density = 2;   // High
        Assert.Equal(6f, sc.ItemsPer100m2);
        var square = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) };
        Assert.Equal(600f, sc.ItemsOf(square), 3);   // 10 000 m² = 100 units of 100 m², × 6 each
        var born = new List<object>(); sc.Born(born);
        var sf = born.OfType<TerrainFeature>().Single();
        Assert.Equal((TerrainFeature.Scatter, "street-clutter", sc.Seed, 6f), (sf.Kind, sf.Template.ToString(), sf.Seed, sf.Pad));
    }

    /// <summary>
    /// ⭐ Gap C (mockup 3, the Clutter tiles) — every clutter type has its OWN glyph key from the shared icon table (a car, a barrel, a crate, a block, a barrier, sandbags, a booth), none the generic one; walls, woods, bridges and water have theirs; blocks and houses (pictures) have none.
    /// 🔴 Mutation: <c>TerrainPick.GlyphKey</c> returning null ⇒ red.
    /// </summary>
    [Fact]
    public void U7_EveryClutterTypeHasItsOwnGlyph_FromTheSharedIconTable()
    {
        var picks = TerrainPickCatalog.FeaturePicks();
        var clutter = picks.Where(p => p.FeatureKind == TerrainFeature.Clutter).ToList();
        Assert.Equal(clutter.Count, clutter.Select(p => p.GlyphKey).Distinct().Count());
        Assert.All(clutter, p => { Assert.NotNull(p.GlyphKey); Assert.NotEqual("terrain/clutter", p.GlyphKey); Assert.True(NodeEditor.Core.Widgets.Icons.TryGet(p.GlyphKey!, out _), p.GlyphKey); });
        Assert.Equal("clutter/car", clutter.Single(p => p.Template == "car").GlyphKey);
        Assert.Equal("terrain/walls", picks.Single(p => p.FeatureKind == TerrainFeature.Hedge).GlyphKey);
        Assert.Equal("terrain/bridge", picks.Single(p => p.FeatureKind == TerrainFeature.Bridge).GlyphKey);
        Assert.All(Shipped().Where(p => p.Kind != TerrainPickKind.Feature), p => Assert.Null(p.GlyphKey));
        // the colours differ: a car is grey, a barrel amber, a crate orange
        Assert.NotEqual(NodeEditor.Core.Widgets.Icons.Get("clutter/car").Color, NodeEditor.Core.Widgets.Icons.Get("clutter/barrel").Color);
        Assert.NotEqual(NodeEditor.Core.Widgets.Icons.Get("clutter/barrel").Color, NodeEditor.Core.Widgets.Icons.Get("clutter/crate").Color);
    }

    /// <summary>⭐ Gap C — the greyed scatter lights the moment ONE shipped recipe carries a <c>scatter</c> section (backend's T-3), starts on that recipe, and the panel's <i>What</i> lists it; until then it stays greyed.</summary>
    [Fact]
    public void U7_TheScatterPickLights_WhenARecipeCarriesAScatterSection()
    {
        Assert.Contains("T-3", Shipped().Single(p => p.FeatureKind == TerrainFeature.Scatter).Disabled);
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "street-clutter.recipe.json"), """{ "id": "street-clutter", "radius": 40, "scatter": { "density": 3, "items": [ { "kind": "clutter", "type": "car", "weight": 1 }, { "kind": "tree", "weight": 2 } ] } }""");
        var picks = TerrainPickCatalog.Build(new[] { _tmp, ContentLayout.TerrainRecipeAssets(_src) }, new[] { ContentLayout.BuildingAssets(_src) });
        var scatter = picks.Single(p => p.FeatureKind == TerrainFeature.Scatter);
        Assert.Null(scatter.Disabled);
        Assert.Equal("feature:11:street-clutter:Area", scatter.Spec);
        Assert.Equal(("street-clutter", 3f), (TerrainPickCatalog.ScatterRecipes(new[] { _tmp }).Single().Id, TerrainPickCatalog.ScatterRecipes(new[] { _tmp }).Single().Density));
        Assert.Empty(TerrainPickCatalog.ScatterRecipes(new[] { ContentLayout.TerrainRecipeAssets(_src) }));
    }

    /// <summary>
    /// ⭐ <c>CE-4592</c> (P-5; mockup <c>docs/UX/ground-shaping-and-ditches.svg</c> panel A) — the 7th group, EARTHWORKS: Level / Raise / Lower are area tools that place a
    /// <c>TerrainFeature</c> of kind Ground (mode in its template); Crater, the ditches and the foxhole are listed greyed with the item that brings them. Born: a raise 2 m, a
    /// lower 1 m, a level at the clicked ground; a later shape has a higher draw order (the newest wins). 🔴 Red-proof: born with <c>NewSeed()</c> ⇒ the order is random.
    /// </summary>
    [Fact]
    public void CE4592_Earthworks_LevelRaiseLower_AreAreaTools_TheRestGreyed_AndAShapeIsBornWithItsValueAndANewerOrder()
    {
        Assert.Equal(TerrainPickCatalog.Earthworks, TerrainPickCatalog.Groups[^1]);
        var earth = TerrainPickCatalog.FeaturePicks().Where(p => p.Group == TerrainPickCatalog.Earthworks).ToList();
        Assert.Equal(new[] { "Level", "Raise", "Lower", "Crater", "Ditch", "Anti-tank ditch", "Foxhole" }, earth.Select(p => p.Name).ToArray());
        Assert.All(earth.Take(3), p => { Assert.Null(p.Disabled); Assert.Equal(TerrainShape.Area, p.Shape); Assert.Equal(TerrainFeature.Ground, p.FeatureKind); Assert.Equal("terrain/source", p.GlyphKey); });
        Assert.Contains("V-14", earth.Single(p => p.Name == "Crater").Disabled);
        // ⭐ CE-4593 (P-6) — the ditches are line tools, the foxhole a point tool
        Assert.Equal(TerrainShape.Line, earth.Single(p => p.Name == "Ditch").Shape);
        Assert.Equal(TerrainShape.Line, earth.Single(p => p.Name == "Anti-tank ditch").Shape);
        Assert.Equal(TerrainShape.Point, earth.Single(p => p.Name == "Foxhole").Shape);
        Assert.All(earth.Where(p => p.Name != "Crater"), p => Assert.Null(p.Disabled));

        var raise = new List<object> { new Fdp.Core.SimTransform { Position = new System.Numerics.Vector3(1f, 2f, 7.5f) } };
        TerrainBirth.BornWith(Hrot.Map.Common.TkbEntityTypes.TerrainFeature, earth.Single(p => p.Name == "Raise").Spec, System.Numerics.Quaternion.Identity, raise);
        var r = raise.OfType<TerrainFeature>().Single();
        Assert.Equal(TerrainFeature.Ground, r.Kind); Assert.Equal("raise", r.Template.ToString()); Assert.Equal(TerrainBirth.DefaultRaise, r.Pad);
        var level = new List<object> { new Fdp.Core.SimTransform { Position = new System.Numerics.Vector3(1f, 2f, 7.5f) } };
        System.Threading.Thread.Sleep(1100);
        TerrainBirth.BornWith(Hrot.Map.Common.TkbEntityTypes.TerrainFeature, earth.Single(p => p.Name == "Level").Spec, System.Numerics.Quaternion.Identity, level);
        var l = level.OfType<TerrainFeature>().Single();
        Assert.Equal(7.5f, l.Pad);
        Assert.True(l.Seed > r.Seed, "the shape drawn later has the higher draw order");
        Assert.Equal("Level ground", level.OfType<EntityInfo>().Single().Name.ToString());
    }
}
