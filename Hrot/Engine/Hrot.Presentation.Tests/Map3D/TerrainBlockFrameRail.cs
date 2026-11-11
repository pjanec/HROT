using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.UI.Common.Map3D;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.AddEntity;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.Map2D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ Terrain authoring U-3 + U-4 (mockup <c>terrain-authoring-2-blocks.svg</c>, the map) in a REAL frame (Xvfb; a skip is not a pass): test-town with a hamlet and a copse (compiled by <see cref="OverlayCompiler"/>, loaded through the production
/// <c>TerrainResidency</c>) and the two block entities. ① at OVERVIEW zoom every template house is ONE tan footprint — no floor plan (the slab blue is gone from the houses) — ② close in the floor plan returns, ③ the unselected block's dashed grey
/// outline and the selected block's solid blue outline are on screen. Frames go to <c>HROT_RAIL_SHOTS</c> (⛔ never committed).
/// 🔴 Red-proofs (compile): <c>TerrainLayer2D.InteriorMinZoom</c> made 0 ⇒ the overview shows floor plans ⇒ red; the gizmo's selected colour = the idle one ⇒ red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainBlockFrameRail
{
    private const string HamletKey = "b1000000-0000-4000-8000-000000000001", CopseKey = "b1000000-0000-4000-8000-000000000002";
    private static readonly Vector2 HamletAt = new(345f, 180f), CopseAt = new(275f, 200f);

    private static string Shots()
    {
        string d = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } s ? s : Path.Combine(Path.GetTempPath(), "hrot-terrain-blocks");
        Directory.CreateDirectory(d);
        return d;
    }

    private static JsonObject Block(string recipe, uint seed, Vector2 at) => new()
    {
        ["TerrainBlock"] = new JsonObject { ["Recipe"] = recipe, ["Seed"] = seed, ["HeadingDeg"] = 0, ["Fit"] = 15 },
        ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(at.X, at.Y, 0) },
    };

    private static (EntityRepository World, Entity Hamlet, Entity Copse) Town()
    {
        string root = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);
        var catalog = new TerrainCatalog(new[] { root });
        string scenarioDir = Path.Combine(Path.GetTempPath(), "tbfr-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(scenarioDir);
        var dom = new JsonObject { ["header"] = new JsonObject { ["terrainName"] = "test-town" }, ["entities"] = new JsonObject { [HamletKey] = Block("hamlet", 21, HamletAt), [CopseKey] = Block("copse", 4, CopseAt) } };
        var r = OverlayCompiler.Compile(dom, scenarioDir, catalog.ResolveDefinition, bakeNavtiles: false);
        Assert.True(r.Wrote);
        var world = new EntityRepository();
        var residency = new TerrainResidency(catalog, new CarKinem.Road.RoadNetworkHolder());
        residency.Commit(world, residency.Prepare("test-town", r.Folder));
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<TerrainBlock>(); world.RegisterComponent<EntityGuid>(); world.RegisterComponent<SelectionState>();
        Entity Make(string key, string recipe, uint seed, Vector2 at, int net, bool selected)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(at.X, at.Y, 0), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TerrainBlock { Recipe = new FixedString32(recipe), Seed = seed, Fit = TerrainBlock.FitAll });
            world.AddComponent(e, new EntityGuid(Guid.Parse(key)));
            world.AddComponent(e, new NetworkIdentity(net));
            world.AddComponent(e, new SelectionState { IsSelected = selected });
            return e;
        }
        var hamlet = Make(HamletKey, "hamlet", 21, HamletAt, 91, selected: false);
        var copse = Make(CopseKey, "copse", 4, CopseAt, 92, selected: true);
        // what the live bake would have published: the parts' extents, by the guid before the '/' of their label
        var merged = world.GetSingletonManaged<TerrainWorld>()!;
        var parts = merged.Buildings.Where(b => b.Label.Contains('/') && Guid.TryParse(b.Label.Split('/')[0], out _))
            .Select(b => new OverlayPart(b.Label, 1, new Vector2(b.Footprint.Min(f => f.X), b.Footprint.Min(f => f.Y)), new Vector2(b.Footprint.Max(f => f.X), b.Footprint.Max(f => f.Y)))).ToList();
        Assert.True(parts.Count >= 3, $"the overlay's houses are in the merged world ({parts.Count})");
        // the copse has no house: its extent is its woods' — the block's own spot, 30 m
        parts.Add(new OverlayPart(CopseKey + "/w0", 2, CopseAt - new Vector2(30f), CopseAt + new Vector2(30f)));
        world.RegisterManagedComponent<LiveTerrainBakeInfo>();
        world.SetSingletonManaged(LiveTerrainBakeInfo.From(1, parts, null));
        return (world, hamlet, copse);
    }

    private static Image Render(EntityRepository world, Entity[] blocks, Vector2 centre, float zoom, string png, out TerrainLayer2D layer)
    {
        using var frame = UiFrameHarness.Begin(1280, 800);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var cam = new MapCamera();
        canvas.Camera = cam;
        var target = centre + new Vector2(-1280f / 2f / zoom, 800f / 2f / zoom);
        cam.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        buffer.SetActiveLayers(new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Map = { Grid = false } }.ToMask());
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, buffer, new FdpEventBus(), () => world);
        var gizmo = new TerrainBlockGizmo();
        frame.StepN(4, () => { buffer.Clear(); foreach (var b in blocks) gizmo.Draw(world, b, buffer); canvas.Draw(); });
        layer = layers.Terrain2D!;
        frame.Screenshot(png);
        return Raylib.LoadImageFromScreen();
    }

    private static int Count(Image image, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (what(Raylib.GetImageColor(image, x, y))) n++;
        return n;
    }

    private static bool Near(Color c, int r, int g, int b, int d = 10) => Math.Abs(c.R - r) <= d && Math.Abs(c.G - g) <= d && Math.Abs(c.B - b) <= d;

    [SkippableFact]
    public void TheMap_ShowsTemplateHousesAsTanFootprintsFarOut_TheFloorPlanClose_AndTheBlocksOutlines()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Shots();
        var (world, hamlet, copse) = Town();
        var centre = HamletAt + new Vector2(-35f, 0f);

        Assert.False(new Hrot.UI.Common.Map2D.EmptyScenarioHintLayer(() => world).Shows(), "placed blocks are content: the Empty-scenario hint is not drawn over them");
        using var far = new ImageBox(Render(world, new[] { hamlet, copse }, centre, 3f, Path.Combine(shots, "u3-u4-blocks-overview.png"), out var farLayer));
        Assert.True(farLayer.HouseTopsDrawn > 0, "the layer drew template-house footprint tops at overview zoom");
        int tan = Count(far.Image, c => Near(c, 228, 196, 144, 6));
        Assert.True(tan > 3000, $"tan footprints on screen at overview zoom ({tan} px)");
        int idle = Count(far.Image, c => Near(c, 185, 193, 201, 14));   // the dashed grey outline (alpha 215 over the ground grey)
        int sel = Count(far.Image, c => Near(c, 86, 166, 250, 12));
        Assert.True(idle > 150, $"the hamlet's dashed outline is on screen ({idle} px)");
        Assert.True(sel > 150, $"the selected copse's solid blue outline is on screen ({sel} px)");

        using var near = new ImageBox(Render(world, new[] { hamlet, copse }, HamletAt, 9f, Path.Combine(shots, "u4-house-close.png"), out var nearLayer));
        Assert.Equal(0, nearLayer.HouseTopsDrawn);
        int tanNear = Count(near.Image, c => Near(c, 228, 196, 144, 6));
        Assert.True(tanNear < tan / 4, $"close in the floor plan replaces the footprint tops ({tanNear} tan px vs {tan} far out)");
    }

    /// <summary>
    /// ⭐ Terrain authoring T-5 (mockup <c>terrain-authoring-2-blocks.svg</c>, the ghost at the track, <i>aligned to the track (R to rotate)</i>) in a REAL frame (Xvfb; a skip is not a pass): a hamlet ghost placed at 30° beside test-town's east-west road
    /// is drawn by <see cref="TerrainGhostDraw"/> over the terrain layers — the dashed blue outline, tan houses, the white first hint line (it SAYS aligned) and the green verdict line — and the road's turn is measured: the ghost's houses stand at the
    /// ALIGNED heading (the expander's answer with the road net), not the placed 30°. Shot: <c>t5-ghost-at-the-track.png</c> under <c>HROT_RAIL_SHOTS</c> (never committed).
    /// 🔴 Red-proof (compile): <c>TerrainGhost.Compute</c> passing no road net to the expander ⇒ the houses stay at 30° ⇒ the heading measure reds.
    /// </summary>
    [SkippableFact]
    public void TheGhostAtTheTrack_IsDrawnAlignedToTheRoad_WithTheHintThatSaysSo()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Shots();
        var (world, _, _) = Town();
        string folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), "test-town"), def = Path.Combine(folder, "terrain.json");
        var ctx = new TerrainGhostContext(TerrainFolderWorld.ReadWorld(def, TerrainFolderWorld.ReadDefinition(def), "test-town")!, folder);
        Assert.NotNull(ctx.Roads);
        var at = new Vector2(110f, 188f);
        var shape = new TerrainPlacement(TerrainPickKind.Block, "hamlet", () => ctx, seed: 21).Preview(at, 30f)!;
        Assert.Equal("aligned to the track (R to rotate)", shape.Line1);
        Assert.NotEmpty(shape.Houses);
        // the road turned the block: its first house's long edge runs along the road (y = 200 east-west), not along the placed 30°
        Assert.True(ctx.Roads!.TryAlign(at, TerrainRecipe.Find("hamlet", ctx.RecipeRoots).Radius, 30f, out float aligned));
        var edge = shape.Houses[0][1] - shape.Houses[0][0];
        float deg = MathF.Atan2(edge.Y, edge.X) * 180f / MathF.PI, rel = ((deg - aligned) % 90f + 90f) % 90f;
        Assert.True(rel < 1f || rel > 89f, $"the house edge ({deg}°) is square to the aligned heading ({aligned}°), not to the placed 30°");

        float zoom = 4f;
        using var frame = UiFrameHarness.Begin(1280, 800);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var cam = new MapCamera();
        canvas.Camera = cam;
        var target = at + new Vector2(-1280f / 2f / zoom, 800f / 2f / zoom);
        cam.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        buffer.SetActiveLayers(new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Map = { Grid = false } }.ToMask());
        Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, buffer, new FdpEventBus(), () => world);
        frame.StepN(4, () => { buffer.Clear(); TerrainGhostDraw.Draw(buffer, shape, new Vector3(at.X, at.Y, 0f), 12f / zoom); canvas.Draw(); });
        frame.Screenshot(Path.Combine(shots, "t5-ghost-at-the-track.png"));
        using var img = new ImageBox(Raylib.LoadImageFromScreen());
        int outline = Count(img.Image, c => Near(c, 120, 190, 255, 40)), text = Count(img.Image, c => c.R > 245 && c.G > 245 && c.B > 245), verdict = Count(img.Image, c => Near(c, 110, 210, 130, 12));
        Assert.True(outline > 200, $"the dashed ghost outline is on screen ({outline} px)");
        Assert.True(text > 60, $"the white hint line is on screen ({text} px)");
        Assert.True(verdict > 40, $"the green verdict line is on screen ({verdict} px)");
    }

    private sealed class ImageBox : IDisposable { public readonly Image Image; public ImageBox(Image i) => Image = i; public void Dispose() => Raylib.UnloadImage(Image); }
}
