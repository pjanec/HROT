using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
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
/// ⭐⭐ <c>CE-4604</c> (the dynamic terrain programme; user, <c>2026-11-14</c>: <i>"Check the ui result visually (terrain flattening, wall breaches, craters, ditches, foxholes…)"</i>) — ONE scene with
/// every change the programme makes to the ground and the walls, through the REAL 3-D terrain layer and the hosts' render source (<c>WorldQuery.RenderGeometryOf</c>): a raised mound, a
/// levelled pad on a slope, a ditch and an anti-tank ditch, a foxhole, two craters (baked as every node bakes them — <c>TerrainObstacles.With</c> + the crater table) and a breached house wall
/// (a received <see cref="BuildingDamage"/>). The world is built the way the save and every node build it: a base grid MERGED with an overlay of compiled feature parts.
/// Frames under <c>HROT_RAIL_SHOTS</c> (never git) — the eye check is the point; the asserts pin that each change reaches the frame: the pixel under each feature differs from the
/// same view of the untouched base.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheDynamicTerrainLooksTests
{
    private readonly ITestOutputHelper _out;
    public TheDynamicTerrainLooksTests(ITestOutputHelper output) => _out = output;

    private const string House = """
        { "type": "Feature", "properties": { "kind": "building", "label": "H",
            "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
              "storeys": [ { "height": 3, "walls": [
                { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
          "geometry": { "type": "Point", "coordinates": [20, 20] } }
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

    /// <summary>A gentle slope (0.04 east, 0.02 north) with a low swell — 161 × 161 posts every metre, from (−80, −80).</summary>
    private static TerrainWorld Base()
    {
        int n = 161;
        var z = new float[n * n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
            {
                float x = -80f + c, y = -80f + r;
                z[r * n + c] = 0.04f * x + 0.02f * y + 0.6f * MathF.Sin(x * 0.07f) * MathF.Cos(y * 0.05f);
            }
        return new TerrainWorld { Name = "dyn", BoundsMin = new Vector2(-80f), BoundsMax = new Vector2(80f), Height = new TerrainHeightGrid(new Vector2(-80f), 1f, n, n, z) };
    }

    private static IReadOnlyList<JsonObject> Ground(TerrainWorld b, string key, string mode, float value, uint order, Vector2 at, float heading, params Vector2[] pts)
        => FeatureExpander.OfKind(TerrainFeature.Ground, key, mode, heading, value, order, at, pts, b, new TerrainAssets(), Array.Empty<string?>());

    private static Vector2[] Square(float cx, float cy, float h) => new[] { new Vector2(cx - h, cy - h), new Vector2(cx + h, cy - h), new Vector2(cx + h, cy + h), new Vector2(cx - h, cy + h) };

    /// <summary>The scene's world: the base merged with an overlay of the house, a garden wall and every ground shape — the way the save and every node build it.</summary>
    private static TerrainWorld Merged(TerrainWorld b, bool withChanges)
    {
        var features = new List<JsonObject> { (JsonObject)JsonNode.Parse(House)! };
        features.AddRange(FeatureExpander.OfKind(TerrainFeature.Wall, "garden", "", 0f, 0f, 0, Vector2.Zero, new[] { new Vector2(32f, 20f), new Vector2(40f, 20f), new Vector2(40f, 30f) }, b, new TerrainAssets(), Array.Empty<string?>()));
        if (withChanges)
        {
            features.AddRange(Ground(b, "mound", "raise", 3f, 1, Vector2.Zero, 0f, Square(-40f, 30f, 10f)));
            features.AddRange(Ground(b, "pad", "level", 0f, 2, Vector2.Zero, 0f, Square(-40f, -25f, 9f)));
            features.AddRange(Ground(b, "ditch", "ditch", 0f, 3, Vector2.Zero, 0f, new Vector2(-10f, -55f), new Vector2(30f, -55f)));
            features.AddRange(Ground(b, "atditch", "at-ditch", 0f, 4, Vector2.Zero, 0f, new Vector2(-10f, -68f), new Vector2(30f, -68f)));
            features.AddRange(Ground(b, "fox", "foxhole", 0f, 5, new Vector2(55f, -10f), 0f));
        }
        var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(b, features).ToJsonString(), "dyn/overlay", new TerrainAssets());
        return TerrainWorldMerge.Merge(b, overlay);
    }

    private static int SouthWall(TerrainWorld w)
    {
        for (int i = 0; i < w.Panels.Count; i++)
            if (MathF.Abs(w.Panels[i].A.Y - 20f) < 1e-3f && MathF.Abs(w.Panels[i].B.Y - 20f) < 1e-3f && w.Panels[i].B.X > w.Panels[i].A.X && w.Panels[i].A.X < 30f) return i;
        return -1;
    }

    /// <summary>The repo a display node holds: the baked world (craters cached into its grid, as <c>TerrainResidency</c> does) and the received breach.</summary>
    private static EntityRepository Repo(TerrainWorld merged, bool withChanges)
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<BuildingDamage>(); repo.RegisterComponent<GroundDamage>();
        repo.RegisterManagedComponent<TerrainObjectKey>();
        var world = merged;
        if (withChanges)
        {
            var d = new GroundDamage { CellX = 0, CellY = 0 };
            GroundDamages.Add(ref d, new Crater(new Vector2(5f, -20f), 3.6f, 1.6f, 0.36f, (SimInstant)10.0, CraterKind.Large), out _);
            GroundDamages.Add(ref d, new Crater(new Vector2(14f, -24f), 1.2f, 0.5f, 0.12f, (SimInstant)11.0, CraterKind.Small), out _);
            var c = repo.CreateEntity(); repo.AddComponent(c, d);
            world = TerrainObstacles.With(merged, Array.Empty<TerrainPrism>(), null, CraterTable.Of(repo));
            var bd = new BuildingDamage { Building = 0 };
            BuildingDamages.Add(ref bd, new WallBreach(SouthWall(world), 3.5f, 6.5f, 0.3f, 2.6f, BreachKind.Hole), null);
            var e = repo.CreateEntity(); repo.AddComponent(e, bd);
            repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(world, 0) });
        }
        repo.SetSingletonManaged(world);
        return repo;
    }

    [SkippableFact]
    public void EveryDynamicTerrainChange_ReachesTheFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(800, 520);
        LitShader.Reset();
        var b = Base();
        var views = new (string Name, Vector3 Eye, Vector3 At)[]
        {
            ("overview", new Vector3(10f, -110f, 70f), new Vector3(0f, -10f, 0f)),
            ("mound", new Vector3(-40f, 5f, 14f), new Vector3(-40f, 30f, 2f)),
            ("level-pad", new Vector3(-40f, -50f, 10f), new Vector3(-40f, -25f, -1.5f)),
            ("ditches", new Vector3(10f, -40f, 9f), new Vector3(10f, -55f, -1f)),
            ("foxhole", new Vector3(55f, -16f, 5f), new Vector3(55f, -10f, 0.5f)),
            ("craters", new Vector3(8f, -8f, 9f), new Vector3(6f, -20f, -1f)),
            ("breach", new Vector3(25f, 12f, 2.6f), new Vector3(25f, 24f, 0f)),
        };
        foreach (bool changed in new[] { false, true })
        {
            var repo = Repo(Merged(b, changed), changed);
            using var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(repo));
            for (int i = 0; i < views.Length; i++)
            {
                Settle(frame, layer, Cam(views[i].Eye, views[i].At));
                Directory.CreateDirectory(Shots());
                frame.Screenshot(Path.Combine(Shots(), File(i, views[i].Name, changed)));
            }
            repo.Dispose();
        }
        for (int i = 1; i < views.Length; i++)
        {
            var (changedPx, darkBefore, darkAfter) = Compare(Path.Combine(Shots(), File(i, views[i].Name, false)), Path.Combine(Shots(), File(i, views[i].Name, true)));
            _out.WriteLine($"{views[i].Name}: {changedPx:P1} of the frame changed; soot-dark pixels {darkBefore} → {darkAfter}");
            Assert.True(changedPx > 0.01f, $"{views[i].Name}: the change does not reach the frame ({changedPx:P2})");
            if (views[i].Name == "craters") Assert.True(darkAfter > darkBefore + 400, $"the crater's soot is drawn ({darkBefore} → {darkAfter} dark pixels)");
        }
    }

    private static string File(int i, string name, bool after) => $"dyn-{i}-{name}-{(after ? "after" : "before")}.png";

    /// <summary>The fraction of pixels that changed by more than 40 (summed RGB), and the soot-dark pixels (each channel under 45) in each frame.</summary>
    private static (float Changed, int DarkBefore, int DarkAfter) Compare(string beforePng, string afterPng)
    {
        var a = Raylib.LoadImage(beforePng); var c = Raylib.LoadImage(afterPng);
        try
        {
            int changed = 0, da = 0, dc = 0, n = a.Width * a.Height;
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                {
                    var p = Raylib.GetImageColor(a, x, y); var q = Raylib.GetImageColor(c, x, y);
                    if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 40) changed++;
                    if (p.R < 45 && p.G < 45 && p.B < 45) da++;
                    if (q.R < 45 && q.G < 45 && q.B < 45) dc++;
                }
            return (changed / (float)n, da, dc);
        }
        finally { Raylib.UnloadImage(a); Raylib.UnloadImage(c); }
    }
}
