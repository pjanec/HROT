using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Editor.UiFrameRail;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-3423 (handoff D-1 "demo-city unchanged (frames)"; DESIGN_Map_3D_Mode §6ah) — the SAME cameras drawn BEFORE (the whole-mesh path: the geometry wrapped so it is not
/// chunked — L-11) and AFTER (by chunk and level): near the camera the picture is the same; far may simplify. Frames to <c>HROT_RAIL_SHOTS</c> for the side-by-side (⛔ never
/// git). scale-1 is drawn AFTER only, when generated — its "before" is the 61 M-triangle soup this replaces.
/// </summary>
[Collection(Hrot.Presentation.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheTerrainChunksLookTests
{
    private readonly ITestOutputHelper _out;
    public TheTerrainChunksLookTests(ITestOutputHelper output) => _out = output;

    /// <summary>The geometry, NOT chunked: TerrainLayer3D takes the whole-mesh path for it (L-11).</summary>
    private sealed class Whole : ITerrainRenderGeometry
    {
        private readonly ITerrainRenderGeometry _inner;
        public Whole(ITerrainRenderGeometry inner) => _inner = inner;
        public object Identity => _inner.Identity;
        public void Build(out Vector3[] v, out int[] i, out TerrainSurfaceKind[] k) => _inner.Build(out v, out i, out k);
        public void BuildTagged(out Vector3[] v, out int[] i, out TerrainSurfaceKind[] k, out string?[] m) => _inner.BuildTagged(out v, out i, out k, out m);
    }

    private static string? FolderOf(string name)
    {
        var root = ContentLayout.TryFindSourceTree();
        var f = root == null ? null : Path.Combine(ContentLayout.TerrainAssets(root), name);
        return f != null && Directory.Exists(f) && Directory.GetFiles(f, "*.world.geojson").Length == 1 ? f : null;
    }

    private static TerrainWorld Load(string folder, string name)
        => TerrainWorldParser.Parse(File.ReadAllText(Directory.GetFiles(folder, "*.world.geojson").Single()), name, TerrainAssets.ForFolder(folder));

    private static string Shots() => Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");

    private static (string Name, Camera3D Cam)[] Cameras(TerrainWorld t)
    {
        var c = (t.BoundsMin + t.BoundsMax) * 0.5f;
        float gz = t.GroundHeightAt(c.X, c.Y);
        float span = MathF.Max(t.BoundsMax.X - t.BoundsMin.X, t.BoundsMax.Y - t.BoundsMin.Y);
        Camera3D At(Vector3 eye, Vector3 target) => new() { Position = HrotToRaylib.Position(eye), Target = HrotToRaylib.Position(target), Up = Vector3.UnitY, FovY = 55f, Projection = CameraProjection.Perspective };
        return new[]
        {
            ("near", At(new Vector3(c.X - 60f, c.Y - 90f, gz + 35f), new Vector3(c, gz))),
            ("mid", At(new Vector3(c.X - span * 0.25f, c.Y - span * 0.35f, gz + span * 0.18f), new Vector3(c, gz))),
            ("far", At(new Vector3(c.X - span * 0.6f, c.Y - span * 0.8f, gz + span * 0.45f), new Vector3(c, gz))),
        };
    }

    /// <summary>Draws the terrain through <paramref name="layer"/> at <paramref name="cam"/> once everything it wants is built; returns the screenshot path.</summary>
    private static string Shoot(UiFrameSession frame, TerrainLayer3D layer, Camera3D cam, string file)
    {
        IMeshLayer3D mesh = layer;
        var ctx = new RenderContext();
        // the map camera's clip planes (MapCamera3D.NearClip / FarClip): near = 0.1 % of the look-at distance, far = max(2 km, 60 ×)
        float d = Vector3.Distance(cam.Position, cam.Target);
        void Pass() => frame.Step(() =>
        {
            Raylib.ClearBackground(new Color(120, 150, 190, 255));
            Rlgl.SetClipPlanes(MathF.Max(0.05f, d * 0.001f), MathF.Max(2000f, d * 60f));
            Raylib.BeginMode3D(cam); layer.Draw3D(ctx); Raylib.EndMode3D();
            Rlgl.SetClipPlanes(0.01, 1000.0);
        });
        mesh.RequestBuild();
        var sw = Stopwatch.StartNew();
        while (!mesh.IsBuilt && sw.Elapsed < TimeSpan.FromSeconds(600)) { mesh.PumpBuild(); Pass(); Thread.Sleep(5); }
        Assert.True(mesh.IsBuilt, $"{file}: built");
        sw.Restart();
        do { Pass(); Thread.Sleep(5); } while (layer.Chunks is { Pending: > 0 } && sw.Elapsed < TimeSpan.FromSeconds(600));
        for (int i = 0; i < 3; i++) Pass();
        Directory.CreateDirectory(Shots());
        var path = Path.Combine(Shots(), file);
        frame.Screenshot(path);
        return path;
    }

    /// <summary>The share of sampled pixels within <paramref name="tolerance"/> (sum of channel differences) between two frames.</summary>
    private static double Same(string a, string b, int tolerance)
    {
        var ia = Raylib.LoadImage(a); var ib = Raylib.LoadImage(b);
        try
        {
            int same = 0, all = 0;
            for (int y = 0; y < ia.Height; y += 2)
                for (int x = 0; x < ia.Width; x += 2)
                {
                    var p = Raylib.GetImageColor(ia, x, y); var q = Raylib.GetImageColor(ib, x, y);
                    all++;
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= tolerance) same++;
                }
            return (double)same / all;
        }
        finally { Raylib.UnloadImage(ia); Raylib.UnloadImage(ib); }
    }

    /// <summary>demo-city at three cameras, before and after: near and mid the SAME picture (≥ 98 % / 95 % of pixels), far may simplify (reported).
    /// 🔴 Red-proof (compiles): level 0 drawn at the coarsest level's distance rule inverted ⇒ near differs.</summary>
    [SkippableFact]
    public void DemoCity_BeforeAndAfter_NearTheCameraTheSamePicture()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var folder = FolderOf("demo-city")!;
        using var frame = UiFrameHarness.Begin(960, 600);
        LitShader.Reset();
        var t = Load(folder, "demo-city");
        var world = Map3DTests.WorldWith(t);
        foreach (var (name, cam) in Cameras(t))
        {
            string before, after;
            using (var whole = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world) is { } g ? new Whole(g) : null))
                before = Shoot(frame, whole, cam, $"terrain-lod-demo-city-{name}-before.png");
            using (var chunked = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)))
                after = Shoot(frame, chunked, cam, $"terrain-lod-demo-city-{name}-after.png");
            double same = Same(before, after, 30);
            _out.WriteLine($"demo-city {name}: {same:P1} of pixels the same");
            if (name == "near") Assert.True(same >= 0.98, $"near: {same:P1} the same");
            if (name == "mid") Assert.True(same >= 0.95, $"mid: {same:P1} the same");
        }
    }

    /// <summary>scale-1 (when generated) drawn by chunk at the three cameras — the frames for the status file, and the triangles drawn each.</summary>
    [SkippableFact]
    public void ScaleOne_DrawnByChunk_AtThreeCameras()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Skip.IfNot(Environment.GetEnvironmentVariable("HROT_MEASURE") == "1", "opt-in: HROT_MEASURE=1");
        var folder = FolderOf("scale-1");
        Skip.If(folder == null, "assets/terrain/scale-1 is not generated");
        using var frame = UiFrameHarness.Begin(960, 600);
        LitShader.Reset();
        var t = Load(folder!, "scale-1");
        var world = Map3DTests.WorldWith(t);
        using var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        foreach (var (name, cam) in Cameras(t))
        {
            var sw = Stopwatch.StartNew();
            Shoot(frame, layer, cam, $"terrain-lod-scale-1-{name}-after.png");
            var c = layer.Chunks!;
            _out.WriteLine($"scale-1 {name}: {c.TrianglesDrawn:N0} triangles in {c.MeshesDrawn} meshes drawn; resident {c.ResidentBytes / (1 << 20):N0} MB; settled in {sw.Elapsed.TotalSeconds:F1} s; peak {Process.GetCurrentProcess().PeakWorkingSet64 / (1 << 20):N0} MB");
        }
    }
}
