using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.Map3D;
using Hrot.Terrain.Import;
using Hrot.Terrain.Import.Tests;
using Hrot.Traffic;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ <c>CE-4403</c> (P-1 house pads and road corridors at import; user, <c>2026-11-15</c>: <i>"I havent see flattened ground under houses and roads in slope, can you show?"</i>) —
/// a 12 % slope with a two-lane road across it and three houses, through the REAL importer (<see cref="TerrainImporter"/>) and the REAL node load (<see cref="TerrainResidency"/> +
/// the SUMO loader), drawn by the 3-D terrain and road layers. The same views of the RAW slope (the input DEM, no pads, no corridor) are the "before".
/// Frames under <c>HROT_RAIL_SHOTS</c> (never git) — the eye check is the point; the asserts pin the ground itself: level across the road, flat under each house, the raw slope differs.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheImportedSlopeLooksTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _nas = Path.Combine(Path.GetTempPath(), "slope-import-" + Guid.NewGuid().ToString("N")[..8]);
    public TheImportedSlopeLooksTests(ITestOutputHelper output) => _out = output;
    public void Dispose() { try { Directory.Delete(_nas, true); } catch (IOException) { } }

    /// <summary>A two-lane edge along y = 0 (lanes at y ±1.6, 3.2 m each) from x −90 to 90, in input coordinates (the importer rails' net).</summary>
    private const string TwoLaneNet = "<net version=\"1.20\"><location netOffset=\"0,0\"/><edge id=\"e\" from=\"a\" to=\"b\">" +
        "<lane id=\"e_0\" index=\"0\" speed=\"13.9\" length=\"180\" width=\"3.2\" shape=\"-90,-1.6 90,-1.6\"/><lane id=\"e_1\" index=\"1\" speed=\"13.9\" length=\"180\" width=\"3.2\" shape=\"-90,1.6 90,1.6\"/></edge><junction id=\"a\" type=\"dead_end\" x=\"-90\" y=\"0\"/><junction id=\"b\" type=\"dead_end\" x=\"90\" y=\"0\"/></net>\n";

    private static float Raw(double x, double y) => (float)(10 + 0.12 * y + 0.04 * x);

    private static readonly (string Id, double X0, double Y0, double X1, double Y1, int Storeys)[] Houses =
    {
        ("uphill", -34, 12, -20, 22, 2),
        ("downhill", 8, -24, 22, -13, 1),
        ("far", 34, 18, 46, 30, 1),
    };

    private static string Shots() => Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");

    private static Camera3D Cam(Vector3 eye, Vector3 target) => new() { Position = HrotToRaylib.Position(eye), Target = HrotToRaylib.Position(target), Up = Vector3.UnitY, FovY = 50f, Projection = CameraProjection.Perspective };

    private static void Pass(UiFrameSession frame, TerrainLayer3D terrain, TrafficLayer3D? roads, Camera3D cam) => frame.Step(() =>
    {
        Raylib.ClearBackground(new Color(120, 150, 190, 255));
        Rlgl.SetClipPlanes(0.05, 2000.0);
        Raylib.BeginMode3D(cam); terrain.Draw3D(new RenderContext()); roads?.Draw3D(new RenderContext()); Raylib.EndMode3D();
        Rlgl.SetClipPlanes(0.01, 1000.0);
    });

    private static void Settle(UiFrameSession frame, TerrainLayer3D terrain, TrafficLayer3D? roads, Camera3D cam)
    {
        IMeshLayer3D mesh = terrain;
        mesh.RequestBuild();
        var sw = Stopwatch.StartNew();
        bool Built() => mesh.IsBuilt && (roads is null || ((IMeshLayer3D)roads).IsBuilt);
        while (!Built() && sw.Elapsed < TimeSpan.FromSeconds(120)) { mesh.PumpBuild(); Pass(frame, terrain, roads, cam); Thread.Sleep(5); }
        Assert.True(Built());
        do { Pass(frame, terrain, roads, cam); Thread.Sleep(5); } while (terrain.Chunks is { Pending: > 0 } && sw.Elapsed < TimeSpan.FromSeconds(120));
        for (int i = 0; i < 3; i++) Pass(frame, terrain, roads, cam);
    }

    /// <summary>The same world on another ground: a copy whose <c>Height</c> is <paramref name="height"/> (a test-only reflection copy — the "before" frames only).</summary>
    private static TerrainWorld WithHeight(TerrainWorld w, TerrainHeightGrid height)
    {
        var copy = (TerrainWorld)RuntimeHelpers.GetUninitializedObject(typeof(TerrainWorld));
        foreach (var f in typeof(TerrainWorld).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            f.SetValue(copy, f.Name is "_beyond" or "_rect" or "_panelGrid" or "_footGrid" ? null : f.GetValue(w));   // the ground-derived caches rebuild
        typeof(TerrainWorld).GetField("<Height>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(copy, height);
        return copy;
    }

    [SkippableFact]
    public void ImportedHousePadsAndARoadCorridor_OnASlope_ReachTheFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var mini = new MiniSet { Ground = Raw, Net = true, NetXml = TwoLaneNet };
        foreach (var h in Houses)
        {
            double cx = (h.X0 + h.X1) / 2, cy = (h.Y0 + h.Y1) / 2;
            mini.Buildings.Add(MiniSet.Rect(h.Id, h.X0, h.Y0, h.X1, h.Y1, storeys: h.Storeys, baseZ: Raw(cx, cy)));
        }
        string terrains = ContentLayout.TerrainAssets(_nas), scenarios = ContentLayout.Scenarios(_nas);
        Directory.CreateDirectory(terrains); Directory.CreateDirectory(scenarios);
        var importer = new TerrainImporter(terrains, scenarios);
        string set = mini.Write(Path.Combine(_nas, "set"));
        importer.ImportAsync(set, "slope", false, null, default).GetAwaiter().GetResult();
        var rep = importer.LastReport!;
        _out.WriteLine($"import: {rep.Pads} pads, {rep.RoadCorridors} corridor(s), {rep.RoadPostsLevelled} road posts levelled");
        Assert.Equal(Houses.Length, rep.Pads);
        Assert.Equal(1, rep.RoadCorridors);

        using var repo = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { terrains }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(repo, residency.Prepare("slope"));
        var after = repo.GetSingletonManaged<TerrainWorld>();
        Assert.NotNull(TrafficNetCache.Of(repo));
        var g = after.Height!;
        Vector2 W(double x, double y) => new((float)(x - mini.MinX), (float)(y - mini.MinY));
        float L(double x, double y) { var p = W(x, y); return g.Sample(p.X, p.Y); }
        Vector3 At(double x, double y, float above) { var p = W(x, y); return new Vector3(p, L(x, y) + above); }

        // the ground itself: level across the road, flat under each house, the raw slope elsewhere
        _out.WriteLine($"road at x=-40: z(-4)={L(-40, -4):F2} z(0)={L(-40, 0):F2} z(+4)={L(-40, 4):F2}  (raw {Raw(-40, -4):F2} … {Raw(-40, 4):F2})");
        Assert.InRange(L(-40, 4) - L(-40, -4), -0.03, 0.03);
        foreach (var h in Houses)
        {
            float a = L(h.X0 + 0.5, h.Y0 + 0.5), b = L(h.X1 - 0.5, h.Y1 - 0.5);
            _out.WriteLine($"house {h.Id}: corners {a:F2} / {b:F2} (raw {Raw(h.X0, h.Y0):F2} / {Raw(h.X1, h.Y1):F2})");
            Assert.InRange(b - a, -0.03, 0.03);
        }

        var raw = g.Patched(0, 0, g.Cols - 1, g.Rows - 1, (c, r, _) => Raw(mini.MinX + c, mini.MinY + r));
        using var beforeRepo = new EntityRepository();
        beforeRepo.SetSingletonManaged(WithHeight(after, raw));

        var views = new (string Name, Vector3 Eye, Vector3 At)[]
        {
            ("overview", At(-10, -95, 55f), At(0, 0, 0f)),
            ("road-across-the-slope", At(-75, -22, 7f), At(-45, 0, 0f)),
            ("road-end-on", At(-95, 0, 4f), At(-55, 0, 0f)),
            ("house-uphill", At(-27, -14, 9f), At(-27, 17, 0f)),
            ("house-downhill", At(-8, -40, 9f), At(15, -18, 0f)),
            ("house-far", At(56, 8, 8f), At(40, 24, 0f)),
        };
        using var frame = UiFrameHarness.Begin(900, 560);
        LitShader.Reset();
        Directory.CreateDirectory(Shots());
        foreach (bool built in new[] { false, true })
        {
            var source = built ? repo : beforeRepo;
            using var terrain = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(source));
            using var roads = built ? new TrafficLayer3D(() => repo) : null;
            for (int i = 0; i < views.Length; i++)
            {
                Settle(frame, terrain, roads, Cam(views[i].Eye, views[i].At));
                frame.Screenshot(Path.Combine(Shots(), File(i, views[i].Name, built)));
            }
        }
        for (int i = 1; i < views.Length; i++)
        {
            float changed = Changed(Path.Combine(Shots(), File(i, views[i].Name, false)), Path.Combine(Shots(), File(i, views[i].Name, true)));
            _out.WriteLine($"{views[i].Name}: {changed:P1} of the frame changed");
            Assert.True(changed > 0.01f, $"{views[i].Name}: the pad / corridor does not reach the frame ({changed:P2})");
        }
    }

    private static string File(int i, string name, bool after) => $"slope-{i}-{name}-{(after ? "imported" : "raw")}.png";

    private static float Changed(string beforePng, string afterPng)
    {
        var a = Raylib.LoadImage(beforePng); var c = Raylib.LoadImage(afterPng);
        try
        {
            int changed = 0;
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                {
                    var p = Raylib.GetImageColor(a, x, y); var q = Raylib.GetImageColor(c, x, y);
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 40) changed++;
                }
            return changed / (float)(a.Width * a.Height);
        }
        finally { Raylib.UnloadImage(a); Raylib.UnloadImage(c); }
    }
}
