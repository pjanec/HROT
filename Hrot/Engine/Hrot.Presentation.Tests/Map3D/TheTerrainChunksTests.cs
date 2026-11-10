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

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah) — the 3-D terrain BY CHUNK and level of detail: the eye and frustum read from rlgl are the pass's camera (L-8), the wanted
/// level by distance (L-2), a chunked terrain is "built" at its coarsest level and draws level 0 near the eye once that arrives (L-7), and a steady frame allocates nothing
/// (the hot-path rule). Display rails run under <c>xvfb-run -a</c> — a skip is not a pass.
/// </summary>
[Collection(Hrot.Presentation.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheTerrainChunksTests
{
    private static TerrainWorld Load(string name)
    {
        var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
        var world = Directory.GetFiles(folder, "*.world.geojson").Single();
        return TerrainWorldParser.Parse(File.ReadAllText(world), name, TerrainAssets.ForFolder(folder));
    }

    [Fact]
    public void TheWantedLevel_FollowsTheDistanceInChunks()
    {
        var l = new TerrainChunkLayout(Vector2.Zero, 256f, 1f, 22, 22, 4, 0f, 50f);
        var eye = new Vector3(128f, 128f, 10f);   // over chunk (0, 0)
        Assert.Equal(0, TerrainChunks3D.LevelFor(l, 0, 0, eye));
        Assert.Equal(0, TerrainChunks3D.LevelFor(l, 1, 0, eye));    // 128 m away = 0.5 chunk
        Assert.Equal(1, TerrainChunks3D.LevelFor(l, 2, 0, eye));    // 384 m = 1.5 chunks
        Assert.Equal(2, TerrainChunks3D.LevelFor(l, 4, 0, eye));    // 896 m = 3.5 chunks
        Assert.Equal(3, TerrainChunks3D.LevelFor(l, 8, 0, eye));    // 1 920 m = 7.5 chunks
        var two = l with { Levels = 2 };
        Assert.Equal(1, TerrainChunks3D.LevelFor(two, 8, 0, eye));  // capped by the layout's levels
        Assert.Equal(2, TerrainChunks3D.LevelFor(l, 0, 0, new Vector3(128f, 128f, 50f + 800f)));   // high above: the height counts too
    }

    /// <summary>L-8 — 📐 the matrix convention, proven: inside a 3-D pass, the eye read from rlgl's modelview is the camera's position, and the frustum keeps what is in
    /// front and drops what is behind. 🔴 Red-proof (compiles): the eye read from M41..M43 ⇒ red.</summary>
    [SkippableFact]
    public void TheEyeAndTheFrustum_ReadFromRlgl_AreTheCamera()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(640, 480);
        var cam = new Camera3D { Position = new Vector3(10f, 20f, 30f), Target = Vector3.Zero, Up = Vector3.UnitY, FovY = 45f, Projection = CameraProjection.Perspective };
        Vector3 eye = default; bool hasEye = false, front = false, behind = true, side = true;
        frame.Step(() =>
        {
            Raylib.BeginMode3D(cam);
            hasEye = View.TryEye(Rlgl.GetMatrixModelview(), out eye);
            var planes = new Vector4[6];
            Assert.True(View.TryPlanes(Rlgl.GetMatrixModelview(), Rlgl.GetMatrixProjection(), planes));
            front = View.BoxInside(planes, new Vector3(-1f), new Vector3(1f));                          // the target
            behind = View.BoxInside(planes, new Vector3(19f, 39f, 59f), new Vector3(21f, 41f, 61f));    // behind the eye
            side = View.BoxInside(planes, new Vector3(-500f, -1f, 499f), new Vector3(-498f, 1f, 501f)); // far off to the side
            Raylib.EndMode3D();
        });
        Assert.True(hasEye);
        Assert.InRange(Vector3.Distance(eye, cam.Position), 0f, 1e-2f);
        Assert.True(front, "the target is in the frustum");
        Assert.False(behind, "a box behind the eye is culled");
        Assert.False(side, "a box far to the side is culled");
    }

    /// <summary>L-7 / L-2 / hot path — a chunked terrain (demo-city) becomes "built" at its coarsest level, then draws level 0 under the eye; a steady
    /// frame then allocates 0 B. 🔴 Red-proof (compiles): <c>Draw</c> always picks the coarsest level ⇒ the "level 0 near" cell reds.</summary>
    [SkippableFact]
    public void AChunkedTerrain_IsBuiltCoarseFirst_DrawsLevelZeroNearTheEye_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(800, 600);
        LitShader.Reset();
        var terrain = Load("demo-city");
        var world = Map3DTests.WorldWith(terrain);
        using var layer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        IMeshLayer3D mesh = layer;
        var chunks = () => layer.Chunks!;
        var centre = (terrain.BoundsMin + terrain.BoundsMax) * 0.5f;
        float gz = terrain.GroundHeightAt(centre.X, centre.Y);
        var eyeHrot = new Vector3(centre.X, centre.Y - 60f, gz + 40f);
        var cam = new Camera3D { Position = HrotToRaylib.Position(eyeHrot), Target = HrotToRaylib.Position(new Vector3(centre, gz)), Up = Vector3.UnitY, FovY = 60f, Projection = CameraProjection.Perspective };
        var ctx = new RenderContext();
        void Pass() => frame.Step(() => { Raylib.BeginMode3D(cam); layer.Draw3D(ctx); Raylib.EndMode3D(); });

        mesh.RequestBuild();
        Assert.NotNull(layer.Chunks);
        var sw = Stopwatch.StartNew();
        while (!mesh.IsBuilt && sw.Elapsed < TimeSpan.FromSeconds(120)) { mesh.PumpBuild(); Thread.Sleep(5); }
        Assert.True(mesh.IsBuilt, "the coarse picture is built");

        // the chunk under the eye: level 0 arrives and is drawn
        var l = chunks().Layout;
        int cx = Math.Clamp((int)MathF.Floor((centre.X - l.Origin.X) / l.ChunkSize), 0, l.Nx - 1);
        int cy = Math.Clamp((int)MathF.Floor((centre.Y - l.Origin.Y) / l.ChunkSize), 0, l.Ny - 1);
        Assert.Equal(0, TerrainChunks3D.LevelFor(l, cx, cy, eyeHrot));
        sw.Restart();
        while (chunks().LevelDrawn(cx, cy) != 0 && sw.Elapsed < TimeSpan.FromSeconds(120)) { Pass(); Thread.Sleep(5); }
        Assert.Equal(0, chunks().LevelDrawn(cx, cy));
        Assert.True(layer.TriangleCount > 0);

        // converge (everything the eye wants is built), then a steady frame allocates nothing
        sw.Restart();
        while (chunks().Pending > 0 && sw.Elapsed < TimeSpan.FromSeconds(120)) { Pass(); Thread.Sleep(5); }
        Assert.Equal(0, chunks().Pending);
        for (int i = 0; i < 5; i++) Pass();
        long allocated = 0;
        frame.Step(() =>
        {
            Raylib.BeginMode3D(cam);
            long before = GC.GetAllocatedBytesForCurrentThread();
            layer.Draw3D(ctx);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Raylib.EndMode3D();
        });
        Assert.Equal(0, allocated);
    }
}
