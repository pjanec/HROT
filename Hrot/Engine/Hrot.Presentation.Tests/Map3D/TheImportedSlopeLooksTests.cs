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
            mini.Buildings.Add(MiniSet.Rect(h.Id, h.X0, h.Y0, h.X1, h.Y1, storeys: h.Storeys, baseZ: Raw(h.X0, h.Y0)));   // a house stands at the LOWEST ground under it (TH-C); its pad only cuts (G-2)
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

        // two cross-sections (north–south, across the road): x = −27 (the uphill house), x = 15 (the downhill house) — raw vs imported, for the eye
        Directory.CreateDirectory(Shots());
        using (var csv = new StreamWriter(Path.Combine(Shots(), "slope-profiles.csv")))
        {
            csv.WriteLine("y,raw_x-27,imported_x-27,raw_x15,imported_x15");
            for (double y = -45; y <= 45; y += 0.25)
                csv.WriteLine(FormattableString.Invariant($"{y},{Raw(-27, y)},{L(-27, y)},{Raw(15, y)},{L(15, y)}"));
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

    /// <summary>
    /// ⭐ <c>CE-4597</c> (P-3, G-5 — a house PLACED in a scenario stands on a pad; user, <c>2026-11-15</c>: <i>"show it"</i>) — three repository templates (house-2f, barn,
    /// courtyard-house) placed on a 12 % slope through the scenario's own path (<see cref="OverlayExpansion.House"/> → the overlay document → <see cref="TerrainWorldMerge.Merge"/>,
    /// what the save and every node do). The "before" is the same houses with no <c>pad</c>: each stands at its lowest ground with the slope running into its uphill walls.
    /// The asserts: the ground under each footprint is flat at the house's base after, and sloped before.
    /// </summary>
    [SkippableFact]
    public void ScenarioHousePads_OnASlope_ReachTheFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        int n = 161;
        var z = new float[n * n];
        for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) z[r * n + c] = Raw(-80 + c, -80 + r);
        var b = new TerrainWorld { Name = "slope-p3", BoundsMin = new Vector2(-80f), BoundsMax = new Vector2(80f), Height = new TerrainHeightGrid(new Vector2(-80f), 1f, n, n, z) };
        var assets = TerrainAssets.ForFolder(Path.Combine(_nas, "none"), ContentLayout.BuildingAssets(ContentLayout.TryFindSourceTree()!));
        var placed = new (string Key, string Template, Vector2 At, float Heading)[]
        {
            ("h-2f", "house-2f", new Vector2(-30f, 10f), 0f),
            ("h-barn", "barn", new Vector2(5f, -5f), 20f),
            ("h-court", "courtyard-house", new Vector2(35f, 20f), -10f),
        };
        TerrainWorld Merge(bool pads)
        {
            var features = new System.Collections.Generic.List<System.Text.Json.Nodes.JsonObject>();
            foreach (var p in placed)
            {
                var f = OverlayExpansion.House(p.Key, p.Template, p.Heading, 0f, p.At, b, assets);
                if (!pads) ((System.Text.Json.Nodes.JsonObject)f["properties"]!).Remove("pad");
                features.Add(f);
            }
            var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(b, features).ToJsonString(), "slope-p3/overlay", assets);
            return TerrainWorldMerge.Merge(b, overlay);
        }
        var before = Merge(false); var after = Merge(true);
        Assert.Equal(placed.Length, after.Buildings.Count);
        for (int i = 0; i < after.Buildings.Count; i++)
        {
            var h = after.Buildings[i];
            float lo = float.MaxValue, hi = float.MinValue, rawLo = float.MaxValue, rawHi = float.MinValue;
            var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
            foreach (var v in h.Footprint) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            for (float x = min.X + 0.5f; x <= max.X - 0.5f; x += 0.5f)
                for (float y = min.Y + 0.5f; y <= max.Y - 0.5f; y += 0.5f)
                {
                    if (!Inside(h.Footprint, new Vector2(x, y))) continue;
                    float a = after.Height!.Sample(x, y), r0 = before.Height!.Sample(x, y);
                    lo = MathF.Min(lo, a); hi = MathF.Max(hi, a); rawLo = MathF.Min(rawLo, r0); rawHi = MathF.Max(rawHi, r0);
                }
            _out.WriteLine($"{h.Label}: base {h.BaseZ:F2}; ground under it {lo:F2}…{hi:F2} with the pad, {rawLo:F2}…{rawHi:F2} without");
            Assert.InRange(hi - lo, 0f, 0.1f);    // a rotated footprint on the 1 m grid: a few cm of ripple along a slanted edge
            Assert.InRange(lo - h.BaseZ, -0.1f, 0.1f);
            Assert.True(rawHi - rawLo > 1f, "the slope under the house without its pad");
        }

        // a cross-section through the barn and the 2-storey house, for the eye
        Directory.CreateDirectory(Shots());
        using (var csv = new StreamWriter(Path.Combine(Shots(), "scenario-profiles.csv")))
        {
            csv.WriteLine("y,raw_x-30,pad_x-30,raw_x5,pad_x5");
            for (float y = -40; y <= 45; y += 0.25f)
                csv.WriteLine(FormattableString.Invariant($"{y},{before.Height!.Sample(-30f, y)},{after.Height!.Sample(-30f, y)},{before.Height!.Sample(5f, y)},{after.Height!.Sample(5f, y)}"));
        }

        Vector3 At(TerrainWorld w, float x, float y, float above) => new(x, y, w.Height!.Sample(x, y) + above);
        Vector2 Centre(int i) { var c = Vector2.Zero; foreach (var v in after.Buildings[i].Footprint) c += v; return c / after.Buildings[i].Footprint.Length; }
        var views = new System.Collections.Generic.List<(string Name, Vector3 Eye, Vector3 At)> { ("overview", At(b, -5f, -75f, 45f), At(b, 0f, 10f, 0f)) };
        for (int i = 0; i < after.Buildings.Count; i++)
        {
            var c = Centre(i);
            string name = after.Buildings[i].Label!.Split('/')[0];
            views.Add((name + "-from-uphill", At(b, c.X - 14f, c.Y + 22f, 9f), At(b, c.X, c.Y, 0f)));      // onto the uphill wall and the cut bank behind it
            views.Add((name + "-uphill-corner", At(b, c.X + 18f, c.Y + 10f, 3f), At(b, c.X, c.Y + 4f, 0.5f)));
        }
        using var frame = UiFrameHarness.Begin(900, 560);
        LitShader.Reset();
        foreach (bool padded in new[] { false, true })
        {
            using var repo = new EntityRepository();
            repo.SetSingletonManaged(padded ? after : before);
            using var terrain = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(repo));
            for (int i = 0; i < views.Count; i++)
            {
                Settle(frame, terrain, null, Cam(views[i].Eye, views[i].At));
                frame.Screenshot(Path.Combine(Shots(), $"scenario-{i}-{views[i].Name}-{(padded ? "pad" : "nopad")}.png"));
            }
        }
        for (int i = 1; i < views.Count; i++)
        {
            float changed = Changed(Path.Combine(Shots(), $"scenario-{i}-{views[i].Name}-nopad.png"), Path.Combine(Shots(), $"scenario-{i}-{views[i].Name}-pad.png"));
            _out.WriteLine($"{views[i].Name}: {changed:P1} of the frame changed");
            Assert.True(changed > 0.01f, $"{views[i].Name}: the pad does not reach the frame ({changed:P2})");
        }
    }

    private static bool Inside(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
        return inside;
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
