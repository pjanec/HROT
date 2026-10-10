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
        var poses = new Dictionary<Entity, ArticulationPose>();
        foreach (var c in Cast)
        {
            var e = world.CreateEntity();
            // ⭐ §3.11 — the M1 traverses 60° left with the gun raised, the Bradley 90° right: the articulated parts, posed.
            if (c.Type == TkbEntityTypes.Tank_M1Abrams) poses[e] = new ArticulationPose(MathF.PI / 3f, 0.25f);
            if (c.Type == TkbEntityTypes.IFV_Bradley) poses[e] = new ArticulationPose(-MathF.PI / 2f, 0.1f);
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
        var bodies = new EntityBodyLayer3D(() => world, () => tkb,
            (w, e) => poses.TryGetValue(e, out var p) ? p : ArticulationPose.Neutral);
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

        // ④ the built-in aircraft (CE-1041): two flying (gear up), the C-130 parked on the ground resting on its gear
        var air = new (long Type, float Dx, float Dy, float Alt)[]
        {
            (TkbEntityTypes.Heli_UH60, 14f, 30f, 22f),
            (TkbEntityTypes.Jet_F16, -30f, 45f, 38f),
            (TkbEntityTypes.Cargo_C130, 0f, 0f, 0f),         // 0 ⇒ parked on open ground (found below): its CG at the resting pose
        };
        var apron = FindOpenArea(query, spot + new Vector2(40f, 60f), 24f);
        foreach (var a in air)
        {
            var e = world.CreateEntity();
            float x = a.Alt > 0f ? spot.X + a.Dx : apron.X, y = a.Alt > 0f ? spot.Y + a.Dy : apron.Y, yaw = 0.9f;
            Vector3 position;
            Quaternion rotation;
            if (a.Alt > 0f)
            {
                position = new Vector3(x, y, query.GroundHeightAt(x, y) + a.Alt);
                rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.15f);
            }
            else
            {
                var geometry = tkb.GetByType(a.Type).GetDescriptor<Fdp.Toolkit.Tkb.Domain.BodyGeometryDto>()!;
                var rest = Fdp.Toolkit.Tkb.Domain.BodyGeometry.RestingPose(geometry, new Vector2(x, y), yaw, query.GroundHeightAt);
                position = new Vector3(x, y, rest.Z);
                rotation = Fdp.Toolkit.Tkb.Domain.BodyGeometry.Rotation(yaw, rest);
            }
            world.AddComponent(e, new SimTransform { Position = position, Rotation = rotation });
            world.AddComponent(e, new TkbIdentity { TkbType = a.Type });
        }
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 6f, spot.Y + 50f, query.GroundHeightAt(spot.X, spot.Y) + 30f), 120f,
                                          MathF.PI * 0.5f, -0.22f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "map3d-4-aircraft.png"));
        Assert.Equal(Cast.Length - 1 + air.Length, bodies.BodiesDrawn);

        // ⑤ the posed turrets, close
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 18f, spot.Y, query.GroundHeightAt(spot.X - 18f, spot.Y)), 24f,
                                          MathF.PI * 0.62f, -0.5f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "map3d-5-turrets.png"));

        // ⑥ the parked C-130 on its gear (the gear drawn at the TKB's contact points)
        {
            float cx = apron.X, cy = apron.Y;
            sw.Camera3D.Pose = new CameraPose(new Vector3(cx, cy, query.GroundHeightAt(cx, cy) + 3f), 46f, 0.9f - MathF.PI / 2f, -0.14f);   // from its left side
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            frame.Screenshot(Path.Combine(shots, "map3d-6-parked-c130.png"));
        }

        // ⑦ CE-1033 S3 — gizmos in 3-D, through the production gizmo layer: a measurement line and a pick ring DRAPED on the
        //   ground (z = 0, M18), a fire trace from the M1's muzzle height, a fragment radius with height, and a ground label.
        {
            var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus())
            {
                CurrentCamera = () => canvas.Camera,
            };
            gizmos.Renderer3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
            canvas.AddLayer(gizmos);
            var yellow = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 220, 40);
            var red = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 60, 40);
            var tank = new Vector2(spot.X - 12f, spot.Y);
            float gz = query.GroundHeightAt(tank.X, tank.Y);
            buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeLine(
                new Vector3(spot.X - 25f, spot.Y - 6f, 0f), new Vector3(spot.X + 15f, spot.Y - 6f, 0f), yellow, 2f,
                target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.Map2D));
            buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeSphere(
                new Vector3(tank, 0f), 5f, yellow, 1.5f, target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.Map2D));
            buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeLine(
                new Vector3(tank, gz + 2.4f), new Vector3(spot.X + 30f, spot.Y + 40f, gz + 1f), red, 1.5f,
                target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.Map2D));
            buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeSphere(
                new Vector3(spot.X + 30f, spot.Y + 40f, gz + 1f), 6f, red, 1.5f, target: Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.Map2D));
            var label = Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeText(spot.X - 25f, spot.Y - 6f,
                new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("40 m"), yellow, fontSizePx: 16f);
            label.TargetView = Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget.Map2D;
            buffer.AppendRaw(label);

            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y + 10f, gz), 70f, MathF.PI * 0.75f, -0.45f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            frame.Screenshot(Path.Combine(shots, "map3d-7-gizmos.png"));
            Assert.True(gizmos.Renderer3D.LinesDrawn > 20, $"gizmo lines: {gizmos.Renderer3D.LinesDrawn}");
            Assert.Equal(0, gizmos.Renderer3D.Skipped.Total);
            Assert.Equal(1, gizmos.LabelsDrawn3D);
        }

        // ⑧ CE-1042 E3 — realism effects from their TKB type: a large explosion mid-bloom, a crater decal draped on the ground,
        //   a medium explosion, and a 125 mm muzzle flash at the T-72's hull front.
        {
            Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(world);
            world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
            var effectsLayer = new Hrot.UI.Common.Effects.EffectLayer(() => world, () => tkb, bodies) { GroundHeight = query.GroundHeightAt };
            canvas.AddLayer(effectsLayer);
            void Fx(long type, Vector3 at, float age, Entity shooter = default)
            {
                var e = world.CreateEntity();
                world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
                world.AddComponent(e, new TkbIdentity { TkbType = type });
                var look = Hrot.Core.Tkb.EffectTkbCatalog.VisualOf(tkb, type)!;
                world.AddComponent(e, new Hrot.Map.Common.Components.EffectLifetime { Age = age, Duration = look.Duration });
                if (shooter != default) world.AddComponent(e, new Hrot.Map.Common.Components.EffectAnchor { Shooter = shooter });
            }
            var blast = new Vector2(spot.X + 25f, spot.Y + 25f);
            float bz = query.GroundHeightAt(blast.X, blast.Y);
            Fx(Hrot.Core.Tkb.EffectTkbCatalog.ExplosionLarge, new Vector3(blast, bz + 1f), 0.5f);
            Fx(Hrot.Core.Tkb.EffectTkbCatalog.DecalLarge, new Vector3(blast.X - 9f, blast.Y - 4f, query.GroundHeightAt(blast.X - 9f, blast.Y - 4f)), 5f);
            Fx(Hrot.Core.Tkb.EffectTkbCatalog.ExplosionMedium, new Vector3(blast.X - 15f, blast.Y + 8f, bz + 0.5f), 0.3f);
            Entity t72 = default;
            foreach (var e in world.Query().With<TkbIdentity>().Build())
                if (world.GetComponentRO<TkbIdentity>(e).TkbType == TkbEntityTypes.Tank_T72) t72 = e;
            Fx(Hrot.Core.Tkb.EffectTkbCatalog.MuzzleFlashLarge, Vector3.Zero, 0.02f, t72);

            sw.Camera3D.Pose = new CameraPose(new Vector3(blast.X - 15f, blast.Y - 5f, bz), 70f, MathF.PI * 0.8f, -0.35f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            frame.Screenshot(Path.Combine(shots, "map3d-8-effects.png"));
            Assert.Equal(4, effectsLayer.Drawn);
        }

        AssertLooksLikeAWorld(Path.Combine(shots, "map3d-2-tilted.png"));
        AssertLooksLikeAWorld(Path.Combine(shots, "map3d-3-close.png"));
    }

    /// <summary>
    /// ⭐ CE-1040 — the 2-D map draws NORTH UP (docs/DESIGN_Map_North_Up.md): a red filled triangle north of the centre lands in
    /// the top half of the picture, a blue box south of it in the bottom half, and both SURVIVE the mirror (back-face culling is
    /// off for the 2-D pass, N4); the gizmo text draws (N3).
    /// </summary>
    [SkippableFact]
    public void CE1040_The2DMap_DrawsNorthUp_AndFilledShapesAndTextSurviveTheMirror()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        using var frame = UiFrameHarness.Begin(1280, 800);
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        const float zoom = 4f;
        var target = new Vector2(-1280f / 2f / zoom, 800f / 2f / zoom);   // centre (0, 0), north-up: the top edge is north
        two.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas.Camera = two;
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus(), camera: two);
        canvas.AddLayer(gizmos);
        var red = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(230, 30, 30);
        var blue = new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(30, 60, 230);
        // a CCW triangle (as world data usually is) north of the centre
        buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeFilledTriangle(new Vector2(-20, 40), new Vector2(20, 40), new Vector2(0, 70), red));
        buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeBox2D(new Vector2(0f, -50f), new Vector2(20f, 10f), blue,
            sizeMode: Fdp.Toolkit.Diagnostics.Gizmos.SizeMode.WorldMeters, fillColor: blue));
        buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeText(-40f, 20f, new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("NORTH"),
            new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 255, 255), fontSizePx: 30f));
        // a big "T" east of the centre: upright, its crossbar is its TOP (N3 — world text is un-flipped at its point)
        buffer.AppendRaw(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive.MakeText(60f, 30f, new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("T"),
            new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 255, 255), fontSizePx: 60f));
        gizmos.Update(0.016f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        string png = Path.Combine(shots, "map2d-north-up.png");
        frame.Screenshot(png);

        var image = Raylib.LoadImage(png);
        try
        {
            int redTop = 0, redBottom = 0, blueTop = 0, blueBottom = 0, white = 0;
            for (int y = 0; y < 800; y += 2)
                for (int x = 0; x < 1280; x += 2)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    bool isRed = c.R > 180 && c.G < 90 && c.B < 90, isBlue = c.B > 180 && c.R < 90 && c.G < 120;
                    if (isRed) { if (y < 400) redTop++; else redBottom++; }
                    if (isBlue) { if (y < 400) blueTop++; else blueBottom++; }
                    if (c.R > 230 && c.G > 230 && c.B > 230) white++;
                }
            Assert.True(redTop > 200 && redBottom == 0, $"the triangle north of the centre must draw in the TOP half (top {redTop}, bottom {redBottom})");
            Assert.True(blueBottom > 200 && blueTop == 0, $"the box south of the centre must draw in the BOTTOM half (top {blueTop}, bottom {blueBottom})");
            Assert.True(white > 20, $"the gizmo text must draw (white pixels: {white})");
            // the "T": the widest white row is in the top third of the glyph ⇒ upright, not mirrored
            int top = int.MaxValue, bottom = -1, bestRow = -1, bestCount = 0;
            for (int y = 150; y < 450; y++)
            {
                int count = 0;
                for (int x = 860; x < 1040; x++)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    if (c.R > 230 && c.G > 230 && c.B > 230) count++;
                }
                if (count == 0) continue;
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                if (count > bestCount) { bestCount = count; bestRow = y; }
            }
            Assert.True(bottom > top, "the T must draw east of the centre");
            Assert.True(bestRow < top + (bottom - top) / 3, $"the T's crossbar (row {bestRow}) must be its TOP ({top}..{bottom}) — text upright");
        }
        finally { Raylib.UnloadImage(image); }
    }

    /// <summary>The point nearest <paramref name="from"/> with a clear square of ground ±<paramref name="half"/> metres around it.</summary>
    private static Vector2 FindOpenArea(IWorldQuery query, Vector2 from, float half)
    {
        for (float r = 0f; r < 500f; r += 8f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = from + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -half; dx <= half && open; dx += 4f)
                    for (float dy = -half; dy <= half && open; dy += 4f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return from;
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
