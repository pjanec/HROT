using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Fdp.Toolkit.Vis2D.Abstractions;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-5034 GW-3 (📄 docs/DESIGN_Glass_Windows.md §5, §6a; mockup docs/UX/glass-windows.svg — intact · cracked · shattered) — the REAL terrain layer and the glass layer
/// on a view that only RECEIVED a <see cref="WindowDamage"/> entity (a display node): an intact pane tints its window, a hit draws a crack star at its (u, v), a broken window
/// is an open hole with shards in the frame and a burst ONCE, a steady frame allocates nothing. Frames under <c>HROT_RAIL_SHOTS</c> (never git).
/// 🔴 Red-proof (compiles): <c>GlassLook.Panes</c> not skipping a broken window ⇒ the shattered frame still shows its pane.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheGlassWindowsDrawTests
{
    private readonly ITestOutputHelper _out;
    public TheGlassWindowsDrawTests(ITestOutputHelper output) => _out = output;

    /// <summary>A brick house 10 × 8 m at (20, 20): the south wall (y 20) has two windows, x 22..23.2 and 26..27.2, sill 0.9, head 2.1.</summary>
    private const string House = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H",
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick", "openings": [ { "kind": "window", "at": 2, "width": 1.2 }, { "kind": "window", "at": 6, "width": 1.2 } ] },
                  { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                  { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                  { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static string Shots() => Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");

    private static Camera3D Cam(Vector3 eye, Vector3 target) => new() { Position = HrotToRaylib.Position(eye), Target = HrotToRaylib.Position(target), Up = Vector3.UnitY, FovY = 40f, Projection = CameraProjection.Perspective };

    // outside, south of the wall, looking at the window pair from a little above
    private static readonly Camera3D Front = Cam(new Vector3(24.6f, 13.5f, 2.0f), new Vector3(24.6f, 20f, 1.5f));

    private static void Pass(UiFrameSession frame, TerrainLayer3D terrain, GlassLayer3D glass, Camera3D cam) => frame.Step(() =>
    {
        Raylib.ClearBackground(new Color(120, 150, 190, 255));
        Rlgl.SetClipPlanes(0.05, 2000.0);
        Raylib.BeginMode3D(cam); terrain.Draw3D(new RenderContext()); glass.Draw3D(new RenderContext()); Raylib.EndMode3D();
        Rlgl.SetClipPlanes(0.01, 1000.0);
    });

    private static void Settle(UiFrameSession frame, TerrainLayer3D terrain, GlassLayer3D glass, Camera3D cam)
    {
        IMeshLayer3D mesh = terrain;
        mesh.RequestBuild();
        var sw = Stopwatch.StartNew();
        while (!mesh.IsBuilt && sw.Elapsed < TimeSpan.FromSeconds(120)) { mesh.PumpBuild(); Pass(frame, terrain, glass, cam); Thread.Sleep(5); }
        Assert.True(mesh.IsBuilt);
        do { Pass(frame, terrain, glass, cam); Thread.Sleep(5); } while (terrain.Chunks is { Pending: > 0 } && sw.Elapsed < TimeSpan.FromSeconds(120));
        for (int i = 0; i < 3; i++) Pass(frame, terrain, glass, cam);
    }

    private static Image Shot(UiFrameSession frame, string file)
    {
        Directory.CreateDirectory(Shots());
        frame.Screenshot(Path.Combine(Shots(), file));
        return Raylib.LoadImageFromScreen();
    }

    private static int Diff(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    /// <summary>How many pixels differ by more than 40 inside the screen box of world rectangle x0..x1 (on the wall), z0..z1.</summary>
    private static int Changed(Image a, Image b, Camera3D cam, float x0, float x1, float z0, float z1)
    {
        var p0 = Raylib.GetWorldToScreen(HrotToRaylib.Position(new Vector3(x0, 20f, z1)), cam);
        var p1 = Raylib.GetWorldToScreen(HrotToRaylib.Position(new Vector3(x1, 20f, z0)), cam);
        int n = 0;
        for (int y = (int)MathF.Max(0, p0.Y); y < (int)MathF.Min(a.Height, p1.Y); y++)
            for (int x = (int)MathF.Max(0, p0.X); x < (int)MathF.Min(a.Width, p1.X); x++)
                if (Diff(Raylib.GetImageColor(a, x, y), Raylib.GetImageColor(b, x, y)) > 40) n++;
        return n;
    }

    private static int SouthWall(TerrainWorld w)
    {
        for (int i = 0; i < w.Panels.Count; i++) if (w.Panels[i].Openings.Count == 2) return i;
        return -1;
    }

    [SkippableFact]
    public void IntactCrackedShattered_AsTheMockup_FromTheReplicatedDamage()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var w = TerrainWorldParser.Parse(House, "house");
        var repo = new EntityRepository();
        repo.RegisterComponent<WindowDamage>();
        repo.RegisterComponent<BuildingDamage>();
        repo.RegisterManagedComponent<TerrainObjectKey>();
        repo.SetSingletonManaged(w);
        int south = SouthWall(w);
        Assert.True(south >= 0);

        using var frame = UiFrameHarness.Begin(960, 540);
        LitShader.Reset();
        using var terrain = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(repo));
        var glass = new GlassLayer3D(() => repo);

        // ① intact: two panes, no cracks
        Settle(frame, terrain, glass, Front);
        Assert.Equal(2, glass.PanesDrawn);
        Assert.Equal(0, glass.CrackLines);
        var intact = Shot(frame, "glass-1-intact.png");

        // ② the left window takes two hits (a received entity: what the NED ingress makes on a display node)
        var d = new WindowDamage { Building = 0 };
        WindowDamages.Hit(ref d, south, 0, 0.3f, 0.65f);
        WindowDamages.Hit(ref d, south, 0, 0.7f, 0.35f);
        var e = repo.CreateEntity();
        repo.AddComponent(e, d);
        for (int i = 0; i < 3; i++) Pass(frame, terrain, glass, Front);
        Assert.Equal(2, glass.PanesDrawn);
        Assert.True(glass.CrackLines > 20, $"two stars, both faces: {glass.CrackLines} segments");
        Assert.Equal(0, glass.Bursts);
        var cracked = Shot(frame, "glass-2-cracked.png");

        // steady: the glass layer allocates nothing on a frame where nothing changed
        long before = 0, after = 0;
        frame.Step(() =>
        {
            Raylib.BeginMode3D(Front);
            before = GC.GetAllocatedBytesForCurrentThread(); glass.Draw3D(new RenderContext()); after = GC.GetAllocatedBytesForCurrentThread();
            Raylib.EndMode3D();
        });
        _out.WriteLine($"steady glass frame: {after - before} B");

        // ③ the left window breaks: an open hole, shards in its frame, a burst once
        var d2 = repo.GetComponentRO<WindowDamage>(e);
        WindowDamages.Break(ref d2, south, 0);
        repo.SetComponent(e, d2);
        for (int i = 0; i < 3; i++) Pass(frame, terrain, glass, Front);
        Assert.Equal(1, glass.PanesDrawn);
        Assert.InRange(glass.FrameShards, 3, 5);
        Assert.Equal(1, glass.Bursts);
        var shattered = Shot(frame, "glass-3-shattered.png");
        try
        {
            int crackPixels = Changed(intact, cracked, Front, 22f, 23.2f, 0.9f, 2.1f);
            int holePixels = Changed(intact, shattered, Front, 22f, 23.2f, 0.9f, 2.1f);
            int rightPixels = Changed(intact, shattered, Front, 26.1f, 27.1f, 1.0f, 2.0f);
            _out.WriteLine($"cracked window changed {crackPixels} px; shattered {holePixels} px; the right (intact) window {rightPixels} px");
            Assert.True(crackPixels > 30, "the cracks show");
            Assert.True(holePixels > 30, "the broken pane is gone");
            Assert.True(rightPixels < 30, "the other window is untouched");
        }
        finally { Raylib.UnloadImage(intact); Raylib.UnloadImage(cracked); Raylib.UnloadImage(shattered); }
        Assert.True(after - before <= 0, $"a steady glass frame allocated {after - before} B");
    }
}
