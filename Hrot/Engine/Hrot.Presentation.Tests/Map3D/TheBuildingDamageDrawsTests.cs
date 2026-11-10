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
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-4392 (handoff D-1 / D-2 / D-4; docs/DESIGN_Map_3D_Mode.md §6aj) — the REAL 3-D terrain layer, fed the way every host feeds it (<c>WorldQuery.RenderGeometryOf(view)</c>,
/// the render source of <c>MapInteractionPack</c>), on a view that only RECEIVED a <see cref="BuildingDamage"/> entity (no deciding system, its terrain never changed — a display
/// node, R-4196): a breach opens its wall in the frame, seen from outside and from inside; a collapse replaces the house by its heap; an unchanged table rebuilds nothing and
/// a steady frame allocates nothing. Frames under <c>HROT_RAIL_SHOTS</c> (never git). 🔴 Red-proof (compiles): <c>TerrainChunks3D.SetDamage</c> returning at once ⇒ the frames do not change.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheBuildingDamageDrawsTests
{
    private readonly ITestOutputHelper _out;
    public TheBuildingDamageDrawsTests(ITestOutputHelper output) => _out = output;

    /// <summary>A brick house 10 × 8 m, one 3 m storey, at (20, 20): the south wall runs x 20..30 at y 20.</summary>
    private const string House = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H",
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                  { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                  { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                  { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static string Shots() => Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");

    private static Camera3D Cam(Vector3 eye, Vector3 target) => new() { Position = HrotToRaylib.Position(eye), Target = HrotToRaylib.Position(target), Up = Vector3.UnitY, FovY = 50f, Projection = CameraProjection.Perspective };

    private static void Pass(UiFrameSession frame, TerrainLayer3D layer, Camera3D cam) => frame.Step(() =>
    {
        Raylib.ClearBackground(new Color(120, 150, 190, 255));
        Rlgl.SetClipPlanes(0.05, 2000.0);
        Raylib.BeginMode3D(cam); layer.Draw3D(new RenderContext()); Raylib.EndMode3D();
        Rlgl.SetClipPlanes(0.01, 1000.0);
    });

    private static void Settle(UiFrameSession frame, TerrainLayer3D layer, Camera3D cam)
    {
        IMeshLayer3D mesh = layer;
        mesh.RequestBuild();
        var sw = Stopwatch.StartNew();
        while (!mesh.IsBuilt && sw.Elapsed < TimeSpan.FromSeconds(120)) { mesh.PumpBuild(); Pass(frame, layer, cam); Thread.Sleep(5); }
        Assert.True(mesh.IsBuilt);
        do { Pass(frame, layer, cam); Thread.Sleep(5); } while (layer.Chunks is { Pending: > 0 } && sw.Elapsed < TimeSpan.FromSeconds(120));
        for (int i = 0; i < 3; i++) Pass(frame, layer, cam);
    }

    private static Color Centre(UiFrameSession frame, string file)
    {
        Directory.CreateDirectory(Shots());
        frame.Screenshot(Path.Combine(Shots(), file));
        var img = Raylib.LoadImageFromScreen();
        try { return Raylib.GetImageColor(img, img.Width / 2, img.Height / 2); }
        finally { Raylib.UnloadImage(img); }
    }

    private static int Diff(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static (EntityRepository Repo, TerrainWorld World) World()
    {
        var w = TerrainWorldParser.Parse(House, "house");
        var repo = new EntityRepository();
        repo.RegisterComponent<BuildingDamage>();
        repo.RegisterManagedComponent<TerrainObjectKey>();
        repo.SetSingletonManaged(w);
        return (repo, w);
    }

    // a received damage entity (what the NED ingress makes on a display node): the building's key, its breaches / collapse
    private static void Receive(EntityRepository repo, TerrainWorld w, bool collapsed, params WallBreach[] breaches)
    {
        var d = new BuildingDamage { Building = 0, Collapsed = collapsed ? (byte)1 : (byte)0 };
        foreach (var b in breaches) BuildingDamages.Add(ref d, in b, null);
        var e = repo.CreateEntity();
        repo.AddComponent(e, d);
        repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(w, 0) });
    }

    private static int SouthWall(TerrainWorld w)
    {
        for (int i = 0; i < w.Panels.Count; i++)
            if (MathF.Abs(w.Panels[i].A.Y - 20f) < 1e-3f && MathF.Abs(w.Panels[i].B.Y - 20f) < 1e-3f && w.Panels[i].B.X > w.Panels[i].A.X) return i;
        return -1;
    }

    [SkippableFact]
    public void ABreach_OpensTheWallInTheFrame_FromOutsideAndInside_OnADisplayNode()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(640, 400);
        LitShader.Reset();
        var (repo, w) = World();
        using var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(repo));   // the hosts' render source
        var outside = Cam(new Vector3(25f, 12f, 2.6f), new Vector3(25f, 24f, 0f));   // looking down through the breach at the floor inside (straight through it meets the far wall, also brick)
        var inside = Cam(new Vector3(25f, 26.5f, 1.5f), new Vector3(25f, 20f, 1.5f));

        Settle(frame, layer, outside);
        var wallOut = Centre(frame, "damage-1-wall-outside.png");
        Settle(frame, layer, inside);
        var wallIn = Centre(frame, "damage-2-wall-inside.png");

        // ⭐ no damage change ⇒ nothing rebuilt, and a steady frame allocates nothing
        int rebuilds = layer.Chunks!.DamageRebuilds;
        long a0 = 0, a1 = 0;
        for (int i = 0; i < 10; i++)
            frame.Step(() =>
            {
                Raylib.BeginMode3D(inside);
                a0 += GC.GetAllocatedBytesForCurrentThread(); layer.Draw3D(new RenderContext()); a1 += GC.GetAllocatedBytesForCurrentThread();
                Raylib.EndMode3D();
            });
        Assert.Equal(rebuilds, layer.Chunks.DamageRebuilds);
        Assert.Equal(0, layer.Chunks.Pending);
        Assert.Equal(0L, a1 - a0);

        Receive(repo, w, false, new WallBreach(SouthWall(w), 3.5f, 6.5f, 0.3f, 2.6f, BreachKind.Hole));
        Settle(frame, layer, outside);
        var holeOut = Centre(frame, "damage-3-breach-outside.png");
        Assert.True(layer.Chunks.DamageRebuilds > rebuilds, "the breach rebuilt the house's chunk");
        Settle(frame, layer, inside);
        var holeIn = Centre(frame, "damage-4-breach-inside.png");
        _out.WriteLine($"outside wall {wallOut.R},{wallOut.G},{wallOut.B} → {holeOut.R},{holeOut.G},{holeOut.B}; inside {wallIn.R},{wallIn.G},{wallIn.B} → {holeIn.R},{holeIn.G},{holeIn.B}");
        Assert.True(Diff(wallOut, holeOut) > 60, "from outside the wall opened");
        Assert.True(Diff(wallIn, holeIn) > 60, "from inside the wall opened");
        Assert.Same(w, repo.GetSingletonManaged<TerrainWorld>());   // R-4196: the display node's terrain is not changed
    }

    [SkippableFact]
    public void ACollapse_ReplacesTheHouseByItsHeap()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(640, 400);
        LitShader.Reset();
        var (repo, w) = World();
        using var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(repo));
        var cam = Cam(new Vector3(34f, 8f, 9f), new Vector3(25f, 24f, 1.5f));
        Settle(frame, layer, cam);
        Centre(frame, "damage-5-house.png");
        var before = Raylib.LoadImageFromScreen();
        Receive(repo, w, true);
        Settle(frame, layer, cam);
        var heap = Centre(frame, "damage-6-heap.png");
        var after = Raylib.LoadImageFromScreen();
        try
        {
            int changed = 0, total = 0;
            for (int y = 0; y < before.Height; y += 2)
                for (int x = 0; x < before.Width; x += 2) { total++; if (Diff(Raylib.GetImageColor(before, x, y), Raylib.GetImageColor(after, x, y)) > 40) changed++; }
            _out.WriteLine($"collapse: {changed * 100.0 / total:F1} % of the frame changed; the centre is {heap.R},{heap.G},{heap.B}");
            Assert.True(changed * 100.0 / total > 3.0, "the house is gone and the heap drawn");
            Assert.True(heap.R > heap.G && heap.G > heap.B, $"the centre is the heap's warm grey ({heap.R},{heap.G},{heap.B}), not the grass");
        }
        finally { Raylib.UnloadImage(before); Raylib.UnloadImage(after); }
        Assert.Same(w, repo.GetSingletonManaged<TerrainWorld>());
    }
}
