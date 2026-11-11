using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
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
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "") => Calls.Add($"area:{tkbType}");
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
        Assert.Equal(TerrainPickCatalog.Groups, order.Skip(first).Take(6).ToArray());
        Assert.Equal(GalleryGroups.OtherName, order[^1]);

        var rows = r.ItemsProvider().ToList();
        foreach (var recipe in Shipped().Where(p => p.Kind == TerrainPickKind.Block))
            Assert.Contains(rows, e => e.Name == recipe.Name && e.Category!.StartsWith(TerrainPickCatalog.Blocks + "/") && e.Tag is EntityTypeEntry { Terrain: { } });
        Assert.Contains(rows, e => e.Category!.StartsWith(TerrainPickCatalog.Buildings));
        Assert.DoesNotContain(rows, e => e.Category!.StartsWith(TerrainPickCatalog.Clutter));   // T-2's kinds are not built: the group is listed, empty

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

    private (TerrainGhostContext Ctx, string Folder) GhostGround(bool allWater = false)
    {
        string assets = Path.Combine(_tmp, "assets"), terrain = Path.Combine(assets, "terrain", "g");
        Directory.CreateDirectory(terrain);
        foreach (var kind in new[] { "terrain-recipes", "buildings" })
        {
            Directory.CreateDirectory(Path.Combine(assets, kind));
            foreach (var f in Directory.EnumerateFiles(Path.Combine(_src, "assets", kind))) File.Copy(f, Path.Combine(assets, kind, Path.GetFileName(f)));
        }
        File.WriteAllText(Path.Combine(terrain, "terrain.json"), """{"schemaVersion":2,"name":"g","world":"w.geojson"}""");
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

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) Assert.Same(first, pl.Preview(new Vector2(100.1f, 300.1f)));   // under half a metre: the cached shape
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(1, pl.Computations);
        Assert.True(after - before <= 256, $"a steady ghost allocated {after - before} B");   // 0 B steady (the Assert.Same boxing aside)

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
}
