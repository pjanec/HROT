using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using System.Text.Json.Nodes;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ O-4 (CE-4415, docs/DESIGN_Terrain_Authoring.md §18c) — VISUAL FIDELITY of a compiled terrain overlay: test-town with one hamlet block and one copse (compiled on the fly by
/// <see cref="OverlayCompiler"/>, loaded through <c>TerrainResidency.Prepare(name, overlay)</c>) rendered in the production 2-D terrain layer and the 3-D terrain layer, next to test-town's own
/// houses, for comparison with <c>docs/UX/terrain-authoring-2-blocks.svg</c>. Opt-in like the other frame rails: skips without a display; PNGs go to <c>HROT_RAIL_SHOTS</c> (else the temp folder) —
/// <c>terrain-overlay-2d.png</c>, <c>terrain-overlay-2d-bare.png</c>, <c>terrain-overlay-3d.png</c>, <c>terrain-overlay-3d-bare.png</c>.
/// <c>HROT_RAIL_SHOTS=… xvfb-run -a dotnet test … --filter FullyQualifiedName~TerrainOverlayFrameRail</c>
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TerrainOverlayFrameRail
{
    private const string HamletKey = "b1000000-0000-4000-8000-000000000001", CopseKey = "b1000000-0000-4000-8000-000000000002";
    private static readonly Vector2 HamletAt = new(345f, 180f), CopseAt = new(275f, 200f);

    private static string Shots()
    {
        string d = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } s ? s : Path.Combine(Path.GetTempPath(), "hrot-terrain-overlay");
        Directory.CreateDirectory(d);
        return d;
    }

    private static JsonObject Block(string recipe, uint seed, Vector2 at) => new()
    {
        ["TerrainBlock"] = new JsonObject { ["Recipe"] = recipe, ["Seed"] = seed, ["HeadingDeg"] = 0, ["Fit"] = 15 },
        ["SimTransform"] = new JsonObject { ["Position"] = new JsonArray(at.X, at.Y, 0) },
    };

    /// <summary>test-town resident through the REAL load (<c>Prepare(name, overlayFolder)</c>), with the overlay compiled from the two blocks; <c>overlay == false</c> = the bare terrain.</summary>
    private static (EntityRepository World, string? Folder) Town(bool overlay)
    {
        string root = ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!);
        var catalog = new TerrainCatalog(new[] { root });
        string? folder = null;
        if (overlay)
        {
            string scenarioDir = Path.Combine(Path.GetTempPath(), "toverlay-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(scenarioDir);
            var dom = new JsonObject
            {
                ["header"] = new JsonObject { ["terrainName"] = "test-town" },
                ["entities"] = new JsonObject { [HamletKey] = Block("hamlet", 21, HamletAt), [CopseKey] = Block("copse", 4, CopseAt) },
            };
            var r = OverlayCompiler.Compile(dom, scenarioDir, catalog.ResolveDefinition, bakeNavtiles: false);
            Assert.True(r.Wrote); Assert.True(r.Houses >= 3, $"the hamlet placed {r.Houses} houses"); Assert.True(r.Woods >= 2, $"{r.Woods} woods");
            folder = r.Folder;
        }
        var world = new EntityRepository();
        var residency = new TerrainResidency(catalog, new CarKinem.Road.RoadNetworkHolder());
        residency.Commit(world, residency.Prepare("test-town", folder));
        return (world, folder);
    }

    private static (int Dx, int Dy) At(Vector2 centre, float zoom, Vector2 p) => ((int)(640f + (p.X - centre.X) * zoom), (int)(400f - (p.Y - centre.Y) * zoom));

    private static int Delta(Image a, Image b, int x, int y, int r = 3)
    {
        int best = 0;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = Raylib.GetImageColor(a, x + dx, y + dy); var q = Raylib.GetImageColor(b, x + dx, y + dy);
                best = Math.Max(best, Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B));
            }
        return best;
    }

    private static void Render2D(EntityRepository world, Vector2 centre, float zoom, string png)
    {
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var cam = new MapCamera();
        canvas.Camera = cam;
        var target = centre + new Vector2(-1280f / 2f / zoom, 800f / 2f / zoom);   // north-up: the top-left corner's world point
        cam.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        buffer.SetActiveLayers(new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Map = { Grid = false } }.ToMask());
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, buffer, new FdpEventBus(), () => world);
        frame.StepN(4, canvas.Draw);
        Assert.True(layers.Terrain2D!.FillTriangles > 10, $"the 2-D terrain layer drew ({layers.Terrain2D.FillTriangles} fill triangles)");
        frame.Screenshot(png);
    }

    private static CameraPose Pose(EntityRepository world, Vector2? at = null, float distance = 120f)
    {
        var q = WorldQuery.Of(world)!;
        var c = at ?? HamletAt;
        return new CameraPose(new Vector3(c.X, c.Y, q.GroundHeightAt(c.X, c.Y) + 3f), distance, MathF.PI * 0.30f, -0.50f);   // from the south-west, looking north-east, down 29°
    }

    [SkippableFact]
    public void TheHamletAndTheCopse_AreDrawnAmongTestTownsOwnHouses_In2DAnd3D()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Shots();
        var (withOverlay, _) = Town(overlay: true);
        var (bare, _) = Town(overlay: false);
        var merged = withOverlay.GetSingletonManaged<TerrainWorld>()!;
        var bareWorld = bare.GetSingletonManaged<TerrainWorld>()!;
        var houses = merged.Buildings.Where(b => b.Label.StartsWith(HamletKey + "/") && !b.Label.Contains("/w")).ToList();
        Assert.True(houses.Count >= 3);
        Assert.Equal(bareWorld.Buildings.Count + houses.Count, merged.Buildings.Count);

        // (a) 2-D: centred on the block, 4 px per metre (a 10 × 8 m house ≈ 40 × 32 px)
        const float zoom = 4f;
        var centre = HamletAt + new Vector2(-20f, 0f);
        string p2 = Path.Combine(shots, "terrain-overlay-2d.png"), p2b = Path.Combine(shots, "terrain-overlay-2d-bare.png");
        Render2D(withOverlay, centre, zoom, p2);
        Render2D(bare, centre, zoom, p2b);
        var a = Raylib.LoadImage(p2); var b = Raylib.LoadImage(p2b);
        try
        {
            int changed = 0, inView = 0;
            foreach (var h in houses)
            {
                var c = new Vector2(h.Footprint.Average(f => f.X), h.Footprint.Average(f => f.Y));
                var (x, y) = At(centre, zoom, c);
                if (x < 4 || y < 4 || x > 1276 || y > 796) continue;
                inView++;
                if (Delta(a, b, x, y) > 60) changed++;
            }
            Assert.True(inView >= 3, $"the hamlet's houses are in the 2-D view ({inView})");
            Assert.True(changed >= inView - 1, $"each overlay house's pixels differ from the bare ground ({changed} of {inView})");
            // the map is not empty: the base's own houses are drawn in both
            var tower = At(centre, zoom, new Vector2(315f, 255f));
            Assert.True(Delta(a, b, 10, 10, 0) < 5 && (Raylib.GetImageColor(a, tower.Dx, tower.Dy).R != 80 || Raylib.GetImageColor(a, tower.Dx, tower.Dy).G != 80), "the base's Tower is drawn");
        }
        finally { Raylib.UnloadImage(a); Raylib.UnloadImage(b); }

        // the comparison: demo-city's OWN template houses at the same zoom — the overlay houses should look like these (no assertion beyond a drawn, non-empty frame)
        using (var city = Hrot.Presentation.Tests.Map2D.TerrainLayer2DTests.DemoCity())
        {
            var cw = city.GetSingletonManaged<TerrainWorld>()!;
            var own = cw.Buildings.First(bd => !bd.Solid);
            Render2D(city, new Vector2(own.Footprint.Average(f => f.X), own.Footprint.Average(f => f.Y)), zoom, Path.Combine(shots, "terrain-base-template-2d.png"));
        }

        // (b) 3-D: obliquely over the block, the base's L-Block / Tower in the foreground / background
        string p3 = Path.Combine(shots, "terrain-overlay-3d.png"), p3b = Path.Combine(shots, "terrain-overlay-3d-bare.png");
        var points = houses.Select(h => new Vector3(h.Footprint.Average(f => f.X), h.Footprint.Average(f => f.Y), h.StoreyZ.Count > 1 ? h.StoreyZ[1] * 0.5f : 1.5f)).ToList();
        var screens = new List<Vector2>();
        Render3DWithProjection(withOverlay, p3, points, screens);
        Render3DWithProjection(bare, p3b, new List<Vector3>(), new List<Vector2>());
        // the woods up close, next to the base's own North Wood (for the eye: no assertion)
        Render3DWithProjection(withOverlay, Path.Combine(shots, "terrain-overlay-3d-copse.png"), new List<Vector3>(), new List<Vector2>(), CopseAt, 70f);
        Render3DWithProjection(withOverlay, Path.Combine(shots, "terrain-overlay-3d-basewood.png"), new List<Vector3>(), new List<Vector2>(), new Vector2(100f, 340f), 90f);
        var c3 = Raylib.LoadImage(p3); var d3 = Raylib.LoadImage(p3b);
        try
        {
            int changed = 0, inView = 0;
            foreach (var s in screens)
            {
                if (s.X < 4 || s.Y < 4 || s.X > 1276 || s.Y > 796) continue;
                inView++;
                if (Delta(c3, d3, (int)s.X, (int)s.Y) > 60) changed++;
            }
            Assert.True(inView >= 2, $"the hamlet's houses are in the 3-D view ({inView})");
            Assert.True(changed >= inView - 1, $"each overlay house's pixels differ from the bare ground in 3-D ({changed} of {inView})");
        }
        finally { Raylib.UnloadImage(c3); Raylib.UnloadImage(d3); }
    }

    private static void Render3DWithProjection(EntityRepository world, string png, List<Vector3> points, List<Vector2> into, Vector2? at = null, float distance = 120f)
    {
        var query = WorldQuery.Of(world)!;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        canvas.AddLayer(layer);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        sw.Set(true);
        frame.Step(canvas.Draw);
        Map3DFrameRail.StepUntil(frame, canvas, () => layer.TriangleCount > 1000);
        for (int i = 0; i < 40 && sw.IsSwitching; i++) { sw.Camera3D.Update(1f / 30f); frame.Step(canvas.Draw); }
        sw.Camera3D.Pose = Pose(world, at, distance);
        frame.Step(canvas.Draw); frame.Step(canvas.Draw);
        frame.Screenshot(png);
        foreach (var p in points) into.Add(sw.Camera3D.WorldToScreen(p));
    }
}
