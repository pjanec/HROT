using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1033 S1 — the 3-D map RENDERS, in a real window (Xvfb in the cloud, so Mesa's software GL): test-town's terrain and one
/// entity of every built-in kit, through the production canvas, switch, shader and layers. Screenshots go to
/// <c>HROT_RAIL_SHOTS</c> (else the temp folder). Skips — never fails — where there is no display.
/// 📄 docs/DESIGN_Map_3D_Mode.md §6 S1, §7 (the shader on the cloud's GL; the overhead swap pose).
/// <c>xvfb-run -a -s "-screen 0 1280x800x24" dotnet test … --filter "FullyQualifiedName~Map3DFrameRail"</c>
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class Map3DFrameRail
{
    /// <summary>The window must not share the process with tests running in parallel — measured: one full-suite run in two
    /// crashed the test host before this collection existed.</summary>
    public const string Serial = "Map3DFrameRail (serial, owns the window)";

    private static readonly (long Type, StanceId Stance, float Dx, float Dy)[] Cast =
    {
        (TkbEntityTypes.Tank_M1Abrams, StanceId.Standing, -24f, 0f),
        (TkbEntityTypes.IFV_Bradley, StanceId.Standing, -12f, 0f),
        (UrbanCombatTkbCatalog.TkbMilitaryApc, StanceId.Standing, 18f, 0f),
        (TkbEntityTypes.Truck_HMMWV, StanceId.Standing, 0f, 0f),
        (UrbanCombatTkbCatalog.TkbCivilianCar, StanceId.Standing, 9f, 0f),
        (TkbEntityTypes.Infantry_Rifleman, StanceId.Standing, -6f, -9f),
        (TkbEntityTypes.Infantry_Rifleman, StanceId.Crouched, -3f, -9f),
        (TkbEntityTypes.Infantry_Rifleman, StanceId.Prone, 0f, -9f),
        (UrbanCombatTkbCatalog.TkbCivilianPedestrian, StanceId.Standing, 3f, -9f),
        (TkbEntityTypes.Unit_TankPlatoon, StanceId.Standing, 6f, -9f),   // a unit: no body
    };

    [SkippableFact]
    public void S1_TestTown_In3D_DrawsTheTerrainAndEveryKit_OnThisGL()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        var tkb = Map3DTests.BuiltIn();

        // The cast stands on a street near the middle of the town, on the ground there (R-248).
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        foreach (var c in Cast)
        {
            var e = world.CreateEntity();
            float x = spot.X + c.Dx, y = spot.Y + c.Dy;
            world.AddComponent(e, new SimTransform
            {
                Position = new Vector3(x, y, query.GroundHeightAt(x, y)),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f),
            });
            world.AddComponent(e, new TkbIdentity { TkbType = c.Type });
            world.AddComponent(e, new StanceIntent { TargetStance = c.Stance });
        }

        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        using var frame = UiFrameHarness.Begin(1280, 800);
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        var zoom = 4f;
        var target = spot - new Vector2(1280, 800) / 2f / zoom;
        two.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas.Camera = two;
        var terrainLayer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        canvas.AddLayer(terrainLayer);
        canvas.AddLayer(bodies);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);

        // ① the swap pose: overhead, same centre and scale as the 2-D view
        sw.Set(true);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "map3d-1-overhead.png"));
        Assert.True(LitShader.Shared.IsValid, "the lit shader did not compile on this GL");
        Assert.True(terrainLayer.TriangleCount > 1000, $"terrain triangles: {terrainLayer.TriangleCount}");
        Assert.Equal(Cast.Length - 1, bodies.BodiesDrawn);

        // ② the animated tilt, run to its end
        for (int i = 0; i < 40 && sw.IsSwitching; i++)
        {
            sw.Camera3D.Update(1f / 30f);
            frame.Step(canvas.Draw);
        }
        Assert.False(sw.IsSwitching);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "map3d-2-tilted.png"));

        // ③ close up on the cast, from the south-east
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 2f, spot.Y - 4f, query.GroundHeightAt(spot.X, spot.Y)), 48f,
                                          MathF.PI * 0.72f, -0.42f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "map3d-3-close.png"));
        Assert.Equal(Cast.Length - 1, bodies.BodiesDrawn);
        Assert.True(bodies.PartsDrawn > 40, $"parts drawn: {bodies.PartsDrawn}");

        AssertLooksLikeAWorld(Path.Combine(shots, "map3d-2-tilted.png"));
        AssertLooksLikeAWorld(Path.Combine(shots, "map3d-3-close.png"));
    }

    /// <summary>A point of plain ground (not inside a building footprint) closest to the town's middle.</summary>
    private static Vector2 FindOpenGround(Fdp.Toolkit.Terrain.TerrainWorld terrain, IWorldQuery query)
    {
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        for (float r = 0f; r < 400f; r += 6f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = mid + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -30f; dx <= 15f && open; dx += 3f)
                    for (float dy = -12f; dy <= 4f && open; dy += 3f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return mid;
    }

    /// <summary>The picture is the lit scene, not a blank: sky at the top, and below it pixels of all three terrain kinds —
    /// ground (green), roof (red-brown) and wall (grey) — each lit to more than one shade.</summary>
    private static void AssertLooksLikeAWorld(string png)
    {
        var image = Raylib.LoadImage(png);
        try
        {
            Assert.True(image.Width > 0, $"no screenshot at {png}");
            var top = Raylib.GetImageColor(image, 4, 4);   // the tilted overview has sky across its top edge
            if (png.Contains("tilted")) Assert.True(top.B > top.R, $"the top of {Path.GetFileName(png)} is not sky: {top.R},{top.G},{top.B}");
            int ground = 0, roof = 0, wall = 0, dark = 0;
            var shades = new HashSet<int>();
            for (int y = image.Height / 4; y < image.Height; y += 5)
                for (int x = 0; x < image.Width; x += 5)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    shades.Add((c.R >> 3) << 10 | (c.G >> 3) << 5 | (c.B >> 3));
                    if (c.G > c.R + 10 && c.G > c.B + 10) ground++;
                    else if (c.R > c.G + 25 && c.R > c.B + 25) roof++;
                    else if (Math.Abs(c.R - c.G) < 14 && Math.Abs(c.G - c.B) < 14 && c.R > 70 && c.R < 235) wall++;
                    if (c.R < 64 && c.G < 64 && c.B < 64) dark++;
                }
            string name = Path.GetFileName(png);
            Assert.True(ground > 500, $"{name}: ground pixels {ground}");
            Assert.True(shades.Count > 8, $"{name}: only {shades.Count} shades — not lit");
            if (name.Contains("tilted"))
            {
                Assert.True(roof > 50, $"{name}: roof pixels {roof}");
                Assert.True(wall > 50, $"{name}: wall pixels {wall}");
            }
            else
            {
                Assert.True(dark > 60, $"{name}: dark (track, wheel) pixels {dark} — the vehicles are not drawn");
            }
        }
        finally { Raylib.UnloadImage(image); }
    }
}

/// <summary>Runs <see cref="Map3DFrameRail"/> alone, after the parallel tests (the Blueprints frame rails do the same).</summary>
[CollectionDefinition(Map3DFrameRail.Serial, DisableParallelization = true)]
public sealed class Map3DFrameRailCollection { }
