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
        LitShader.Reset();   // a new window: the shared shader compiles in THIS context
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        var zoom = 4f;
        // ⭐ CE-1040 — north-up: the Target (the screen's top-left) is the view's WEST and NORTH edge.
        var target = new Vector2(spot.X - 1280f / 2f / zoom, spot.Y + 800f / 2f / zoom);
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
            // ⭐ S5b — the tank's CARD (§3.6): frame in the side colour, health bar, name — drawn above its body, leader down to it.
            buffer.DrawSpatialAnchor(4242, tank.X, tank.Y, gz, 0f, 0f, 0f);
            var card = Fdp.Toolkit.Diagnostics.Gizmos.EntityCards.Card(buffer, 4242, Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Labels);
            card.Row(0).Frame(new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(235, 72, 60));
            card.Row(10).Bar(0.6f, new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(255, 255, 0), new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(40, 40, 40, 220));
            card.Row(20).Text("T-72 #4242", new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(240, 240, 240));

            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y + 10f, gz), 70f, MathF.PI * 0.75f, -0.45f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            frame.Screenshot(Path.Combine(shots, "map3d-7-gizmos.png"));
            Assert.True(gizmos.Renderer3D.LinesDrawn > 20, $"gizmo lines: {gizmos.Renderer3D.LinesDrawn}");
            Assert.Equal(0, gizmos.Renderer3D.Skipped.Total);
            Assert.Equal(1, gizmos.LabelsDrawn3D);
            Assert.Equal(1, gizmos.Cards.CardsDrawn);
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
                world.AddComponent(e, Fdp.Toolkit.Time.Lifespan.Starting(-age, look.Duration));   // W1: sim time 0 here ⇒ `age` seconds old
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
    /// ⭐ CE-1050 W2–W4 (docs/DESIGN_Visual_Effects.md §7) — the destroyed-entity story on a real GL: three vehicles killed at sim 0
    /// (an M1, a Bradley, a civilian car) beside a living T-72 — near-black wrecks, flames and smoke made by the production
    /// <c>WreckSpawnSystem</c>; three riflemen — one found dead (lying), one falling mid-way, one alive; later frames with the fire
    /// dying down (52 s) and only fading smoke left (150 s); and the 2-D map with the MIL-STD X on the wrecks' symbols.
    /// </summary>
    [SkippableFact]
    public void W_DestroyedEntities_BlackWrecksBurnAndSmoke_DeadMenLie_AndThe2DSymbolsCarryTheX()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(world);
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        void At(double t) => world.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        long net = 500;
        Entity Put(long type, float dx, float dy, float hp, StanceId stance = StanceId.Standing, float yaw = 0.35f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, query.GroundHeightAt(x, y)),
                                                     Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
            world.AddComponent(e, new NetworkIdentity { Value = ++net });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = hp, Max = 100f });
            return e;
        }
        void Kill(Entity e) => world.SetComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
        var m1 = Put(TkbEntityTypes.Tank_M1Abrams, -22f, 0f, 100f);
        var bradley = Put(TkbEntityTypes.IFV_Bradley, -10f, 1f, 100f, yaw: 1.2f);
        var car = Put(UrbanCombatTkbCatalog.TkbCivilianCar, 2f, 0f, 100f, yaw: -0.4f);
        Put(TkbEntityTypes.Tank_T72, 14f, 0f, 100f);                                       // alive, for comparison
        Put(TkbEntityTypes.Infantry_Rifleman, -6f, -9f, 0f, StanceId.Standing, 0.9f);      // found dead: lying
        var falling = Put(TkbEntityTypes.Infantry_Rifleman, -3f, -9f, 100f, StanceId.Crouched, 0.9f);
        Put(TkbEntityTypes.Infantry_Rifleman, 0f, -9f, 100f, StanceId.Standing, 0.9f);     // alive

        // the kills happen at sim 0, through the production spawner (a crossing makes flames and smoke)
        At(0.0);
        var wrecks = new Hrot.UI.Common.Effects.WreckSpawnSystem();
        void Tick()
        {
            wrecks.Execute(world, 0f);
            ((EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetCommandBuffer()).Playback(world);
        }
        Tick();
        Kill(m1); Kill(bradley); Kill(car);
        Tick();
        Assert.Equal(6, wrecks.Spawned);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();   // a new window: the shared shader compiles in THIS context
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var effects = new Hrot.UI.Common.Effects.EffectLayer(() => world, () => tkb, bodies) { GroundHeight = query.GroundHeightAt };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(effects);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        sw.Set(true, animate: false);
        var close = new CameraPose(new Vector3(spot.X - 6f, spot.Y - 2f, query.GroundHeightAt(spot.X, spot.Y)), 46f, MathF.PI * 0.72f, -0.30f);
        sw.Camera3D.Pose = close;

        // the falling man: seen alive at 7.7 s, dead from 8.0 s, drawn at 8.3 s — half-way down
        At(7.7);
        frame.Step(canvas.Draw);
        Kill(falling);
        At(8.0);
        frame.Step(canvas.Draw);
        At(8.3);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-1-burning-8s.png"));
        Assert.Equal(6, effects.Drawn);
        Assert.Equal(7, bodies.BodiesDrawn);
        Assert.Equal(0.5f, bodies.Falls.Dead(falling, 8.3).T, 2);

        // the riflemen up close: dead (lying, rifle dropped), falling, alive
        var men = new Vector2(spot.X - 3f, spot.Y - 9f);
        sw.Camera3D.Pose = new CameraPose(new Vector3(men, query.GroundHeightAt(men.X, men.Y)), 9f, MathF.PI * 0.62f, -0.45f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-2-riflemen-8s.png"));

        // a wreck up close, from the side
        var tank = world.GetComponentRO<SimTransform>(m1).Position;
        sw.Camera3D.Pose = new CameraPose(tank, 22f, MathF.PI * 0.85f, -0.22f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-3-m1-closeup-8s.png"));

        // later: the fire dying down at 52 s; at 150 s the flames are gone and the smoke is fading
        sw.Camera3D.Pose = close;
        At(52.0);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-4-dying-fire-52s.png"));
        Assert.Equal(6, effects.Drawn);
        At(150.0);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-5-fading-smoke-150s.png"));
        Assert.Equal(3, effects.Drawn);

        // the 2-D map at 8.3 s: the symbols (the production EntityPresentationGizmo) with the X on the three wrecks, the puffs from above
        At(8.3);
        var canvas2 = new MapCanvas(input: null);
        var cam2 = new MapCamera();
        const float zoom = 9f;
        var target = new Vector2(spot.X - 4f - 1280f / 2f / zoom, spot.Y - 3f + 800f / 2f / zoom);
        cam2.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas2.Camera = cam2;
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus(), camera: cam2);
        canvas2.AddLayer(effects);
        canvas2.AddLayer(gizmos);
        var symbol = new Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo();
        foreach (var e in world.Query().With<NetworkIdentity>().With<SimTransform>().Build())
            symbol.Draw(world, e, buffer);
        gizmos.Update(0.016f);
        frame.Step(canvas2.Draw);
        frame.Step(canvas2.Draw);
        frame.Screenshot(Path.Combine(shots, "wreck-6-map2d-x-8s.png"));
    }

    /// <summary>
    /// ⭐ P1 (docs/DESIGN_Visual_Effects.md §8) — rounds on a real GL: a grenade mid-throw from a rifleman, a grenade LYING by a living
    /// soldier with its fuze burning, an 81 mm bomb on its way down, and a test MISSILE type (Munition.Visual Missile + smoke trail) —
    /// the bodies at least <see cref="Hrot.UI.Common.Effects.ProjectileLayer.MinPixels"/> tall; then the 2-D map with the projector's
    /// markers and the lying grenade's fuze seconds.
    /// </summary>
    [SkippableFact]
    public void P_Projectiles_AGrenadeFliesAndLies_ABombFalls_AMissileTrailsSmoke()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        var tkb = Map3DTests.BuiltIn();
        const long TestMissile = 6999;
        var missileType = new Fdp.Interfaces.TkbTemplate("Test ATGM", TestMissile);
        missileType.AddDescriptor(new MunitionVisualDto { Shape = MunitionShape.Missile, Length = 1.2f, Diameter = 0.14f, ColorHex = "#6B6E4A", SmokeTrail = true });
        tkb.Register(missileType);
        const long TestTracer = 6998;   // ⭐ P2 — a tracer bullet type (the TKB switch, PR-F)
        var tracerType = new Fdp.Interfaces.TkbTemplate("Test 7.62 tracer", TestTracer);
        tracerType.AddDescriptor(new MunitionVisualDto { Shape = MunitionShape.Bullet, Tracer = true });
        tkb.Register(tracerType);
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 3.3 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        void Soldier(float dx, float dy, StanceId stance, float yaw)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
        }
        Entity Round(long ammo, Vector3 at, Vector3 velocity, byte warhead, float fuze = 0f, Vector3 muzzle = default)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.AddComponent(e, new SimVelocity { Linear = velocity });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.BallisticProjectile
            {
                Ammo = ammo, Warhead = warhead, FuzeRemaining = fuze, PreviousPosition = at - velocity * 0.05f, Muzzle = muzzle,
            });
            return e;
        }
        const byte Grenade = Fdp.Toolkit.Combat.Components.WarheadRound.Arc | Fdp.Toolkit.Combat.Components.WarheadRound.TimeFuze
                           | Fdp.Toolkit.Combat.Components.WarheadRound.Area;
        Soldier(-8f, 0f, StanceId.Standing, 0f);       // the thrower
        Soldier(6f, 2f, StanceId.Crouched, MathF.PI);   // the one the grenade landed by
        var throwPoint = new Vector3(spot.X - 2f, spot.Y + 0.3f, G(spot.X - 2f, spot.Y) + 3.4f);
        Round(MunitionTkbCatalog.TkbM67Grenade, throwPoint, new Vector3(9f, 0.5f, 1.5f), Grenade, 3.6f);
        Round(MunitionTkbCatalog.TkbM67Grenade, new Vector3(spot.X + 4.5f, spot.Y + 0.5f, G(spot.X + 4.5f, spot.Y + 0.5f)), Vector3.Zero,
              (byte)(Grenade | Fdp.Toolkit.Combat.Components.WarheadRound.Landed), 2.7f);
        Round(MunitionTkbCatalog.Tkb81mmMortarHe, new Vector3(spot.X + 10f, spot.Y + 12f, G(spot.X, spot.Y) + 14f), new Vector3(20f, 4f, -45f),
              Fdp.Toolkit.Combat.Components.WarheadRound.Arc | Fdp.Toolkit.Combat.Components.WarheadRound.Area);
        // a machine-gun burst of tracers from the thrower toward the north-east, three rounds in the air, one slightly off (a miss)
        var mg = new Vector3(spot.X - 8f, spot.Y, G(spot.X - 8f, spot.Y) + 1.4f);
        foreach (var (range, off) in new[] { (60f, 0f), (110f, 0f), (160f, 4f) })
        {
            var dir = Vector3.Normalize(new Vector3(0.55f, 1f, 0.02f) + new Vector3(off * 0.004f, 0, 0));
            Round(TestTracer, mg + dir * range, dir * 850f, 0, muzzle: mg);
        }
        var missileAt = new Vector3(spot.X - 6f, spot.Y + 22f, G(spot.X, spot.Y) + 5f);
        Round(TestMissile, missileAt, new Vector3(160f, -10f, 0f), 0, muzzle: missileAt + new Vector3(-50f, 3f, -1f));

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        var rounds = new Hrot.UI.Common.Effects.ProjectileLayer(() => world, () => tkb, () => sw.Camera3D) { GroundHeight = G };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(rounds);
        sw.Set(true, animate: false);

        // from the south, looking north: the soldiers in front, the bomb falling above them, the missile crossing behind
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 2f, spot.Y + 10f, G(spot.X, spot.Y) + 5f), 36f, MathF.PI * 0.5f, -0.12f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "rounds-1-overview.png"));
        Assert.Equal(7, rounds.Drawn);

        // close on the throw and the lying grenade, at the soldiers' height
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 1f, spot.Y + 0.5f, G(spot.X, spot.Y) + 1.6f), 9f, MathF.PI * 0.5f, -0.12f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "rounds-2-grenade-close.png"));

        // 2-D: the projector's streaks, markers and the lying grenade's fuze seconds
        var canvas2 = new MapCanvas(input: null);
        var cam2 = new MapCamera();
        const float zoom = 14f;
        var target = new Vector2(spot.X + 2f - 1280f / 2f / zoom, spot.Y + 8f + 800f / 2f / zoom);
        cam2.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas2.Camera = cam2;
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus(), camera: cam2);
        canvas2.AddLayer(gizmos);
        var projector = new Hrot.ScenarioEditor.Gizmos.ProjectilePresentationGizmo();
        foreach (var e in world.Query().With<Fdp.Toolkit.Combat.Components.BallisticProjectile>().With<SimTransform>().Build())
            projector.Draw(world, e, buffer);
        gizmos.Update(0.016f);
        frame.Step(canvas2.Draw);
        frame.Step(canvas2.Draw);
        frame.Screenshot(Path.Combine(shots, "rounds-3-map2d.png"));
    }

    /// <summary>
    /// ⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13, R-272) — soldiers in the weapon brain's postures on a real GL: at ease (rifle low),
    /// ready (at the chest), aimed (shouldered; one turned toward a target off to his left, the other straight ahead), crouched aimed,
    /// prone; a soldier RUNNING (leaning forward, arms on the rifle) and a civilian walking (arms swinging).
    /// </summary>
    [SkippableFact]
    public void WL6_SoldiersHoldTheirRiflesInTheWeaponBrainsPosture()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 1.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var foe = world.CreateEntity();   // what the turned soldier aims at — off to his left, not drawn (no TKB identity)
        world.AddComponent(foe, new SimTransform { Position = new Vector3(spot.X + 6f, spot.Y + 30f, G(spot.X, spot.Y)), Rotation = Quaternion.Identity });
        Entity Person(float dx, float dy, long type, StanceId stance, Fdp.Toolkit.Combat.Components.WeaponPostureKind posture, Entity aimAt = default)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // all face +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = posture, Target = aimAt });
            return e;
        }
        const long Rifleman = TkbEntityTypes.Infantry_Rifleman;
        Person(0f, 6f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.AtEase);
        Person(0f, 3f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        Person(0f, 0f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);   // aimed, at nothing placed: straight ahead
        Person(0f, -3f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, foe);
        Person(0f, -6f, Rifleman, StanceId.Crouched, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);
        Person(1.5f, -9f, Rifleman, StanceId.Prone, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);
        var runner = Person(-5f, 1.5f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        var glider = Person(-5f, -1.5f, Rifleman, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);   // ⭐ WL-6c — the glide
        var walker = Person(-5f, -4.5f, UrbanCombatTkbCatalog.TkbCivilianPedestrian, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.AtEase);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        // from the south-east, a little above them: they face east, so this is a front three-quarter view — the rifles and arms read
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1f, spot.Y - 1.5f, G(spot.X, spot.Y) + 1.0f), 12f, MathF.PI * 0.80f, -0.10f);

        // the runner at 6 m/s and the walker at 1.4 m/s, over a few frames of sim time, so the gait and the lean are live
        void Move(Entity e, float speed, double dt)
        {
            var tf = world.GetComponent<SimTransform>(e);
            tf.Position += new Vector3(speed * (float)dt, 0f, 0f);
            tf.Position = tf.Position with { Z = G(tf.Position.X, tf.Position.Y) };
            world.SetComponent(e, tf);
        }
        for (int i = 0; i < 9; i++)
        {
            now += 1.0 / 30.0;
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
            Move(runner, 6f, 1.0 / 30.0);
            Move(walker, 1.4f, 1.0 / 30.0);
            Move(glider, 1.2f, 1.0 / 30.0);
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "posture-1-side.png"));
        Assert.Equal(9, bodies.BodiesDrawn);

        // closer, side-on from the south, on the aimed pair (the one turned toward the target on his left) and the crouched one
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 0.3f, spot.Y - 3f, G(spot.X, spot.Y) + 1.1f), 6.5f, MathF.PI * 0.62f, -0.08f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "posture-2-aimed-close.png"));

        // ⭐ WL-6c — side-on, low: the athletic stance (knees bent, feet staggered) and the glide (aimed, walking, short steps)
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 2.5f, spot.Y - 1f, G(spot.X, spot.Y) + 0.8f), 7.5f, MathF.PI * 0.5f, -0.04f);
        for (int i = 0; i < 4; i++)
        {
            now += 1.0 / 30.0;
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
            Move(glider, 1.2f, 1.0 / 30.0);
            Move(runner, 6f, 1.0 / 30.0);
            Move(walker, 1.4f, 1.0 / 30.0);
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "posture-3-athletic-and-glide.png"));

        // the prone soldier, close: side-on, then from the front-left (the arms and the rifle)
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 2.0f, spot.Y - 9f, G(spot.X, spot.Y) + 0.15f), 2.6f, MathF.PI * 0.5f, -0.18f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "posture-4-prone-side.png"));
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 2.0f, spot.Y - 9f, G(spot.X, spot.Y) + 0.15f), 2.4f, MathF.PI * 0.85f, -0.30f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "posture-5-prone-front.png"));

        // ⭐ WL-6d — a crawling soldier (0.6 m/s), side-on, at two points of the cycle: left elbow forward, then right
        var crawler = Person(4f, -12f, Rifleman, StanceId.Prone, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        // from above and behind-left: a crawl reads by its limbs spreading and drawing in, which a low camera hides behind the body
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 4.4f, spot.Y - 12f, G(spot.X, spot.Y) + 0.1f), 3.2f, MathF.PI * 0.20f, -0.95f);
        void CrawlFrames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                now += 1.0 / 60.0;
                world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
                Move(crawler, 0.6f, 1.0 / 60.0);
                frame.Step(canvas.Draw);
            }
        }
        CrawlFrames(18);   // 0.18 m: about a quarter of a cycle
        frame.Screenshot(Path.Combine(shots, "posture-6-crawl-a.png"));
        CrawlFrames(35);   // half a cycle on
        frame.Screenshot(Path.Combine(shots, "posture-7-crawl-b.png"));
    }

    /// <summary>
    /// ⭐⭐ CE-1081 … CE-1084 (docs/DESIGN_Map_3D_Mode.md §6m) — the (V) acceptance, a real frame: a soldier CARRYING a casualty across his
    /// shoulders and one DRAGGING another on his back (both walking east, the casualties placed as the sim places them), a WOUNDED soldier
    /// limping, one DOWN on his back, and a doors-off Black Hawk hovering 2 m up with riders in both doors, legs out — then one of them
    /// leaves his seat and is caught mid-jump. Screenshots: <c>held-1-carried-dragged.png</c>, <c>held-2-wounded-down.png</c>,
    /// <c>held-3-doors-off.png</c>, <c>held-4-jump.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1081_V_TheCarried_TheDragged_TheWounded_TheDown_AndRidersInTheDoors()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 1.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Person(float dx, float dy, float health = 100f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // all face +x
            world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f, WoundedSpeedFactor = 0.6f });
            return e;
        }
        void Hold(Entity carrier, Entity casualty, Fdp.Toolkit.Behavior.Components.CarryMode mode)
        {
            world.AddComponent(carrier, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f, DragBehindMetres = 1f });
            var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1 };
            list.Passengers[0] = casualty;   // ⭐ CE-1085 — as the sim lists him
            list.SetMode(0, mode);
            world.AddComponent(carrier, list);
            world.AddComponent(casualty, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = carrier, Exposed = 1, Mode = mode });
        }
        var bearer = Person(0f, 3f);
        var carried = Person(0f, 3f, health: 20f);
        Hold(bearer, carried, Fdp.Toolkit.Behavior.Components.CarryMode.Carried);
        var puller = Person(0f, 0f);
        var dragged = Person(-1f, 0f, health: 20f);
        Hold(puller, dragged, Fdp.Toolkit.Behavior.Components.CarryMode.Dragged);
        var limper = Person(-1f, -3f, health: 50f);
        Person(1.5f, -6f, health: 20f);   // down

        // the sim's EmbarkedStateSystem places the casualties: carried at the carrier, dragged 1 m behind him
        void Walk(Entity e, float speed, double dt)
        {
            var tf = world.GetComponent<SimTransform>(e);
            tf.Position += new Vector3(speed * (float)dt, 0f, 0f);
            tf.Position = tf.Position with { Z = G(tf.Position.X, tf.Position.Y) };
            world.SetComponent(e, tf);
        }
        void Follow(Entity casualty, Entity carrier, float behind)
        {
            var c = world.GetComponent<SimTransform>(carrier);
            world.SetComponent(casualty, new SimTransform { Position = c.Position - new Vector3(behind, 0f, 0f), Rotation = c.Rotation });
        }

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        // front three-quarter, from the south-east: the carried man across the shoulders, the dragged one on his back behind his puller
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 0.5f, spot.Y + 1.0f, G(spot.X, spot.Y) + 1.0f), 9f, MathF.PI * 0.78f, -0.18f);
        for (int i = 0; i < 9; i++)
        {
            now += 1.0 / 30.0;
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
            Walk(bearer, 1.5f, 1.0 / 30.0); Follow(carried, bearer, 0f);
            Walk(puller, 1.0f, 1.0 / 30.0); Follow(dragged, puller, 1f);
            Walk(limper, 0.8f, 1.0 / 30.0);
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "held-1-carried-dragged.png"));
        Assert.Equal(6, bodies.BodiesDrawn);
        Assert.Equal(CarriageKind.Carried, Carriage.Of(world, carried, tkb).Kind);

        // close and side-on from the north (the carrier walks east, left to right): the casualty across his shoulders
        var bearerAt = world.GetComponent<SimTransform>(bearer).Position;
        sw.Camera3D.Pose = new CameraPose(bearerAt + new Vector3(0f, 0f, 1.2f), 4.2f, -MathF.PI * 0.42f, -0.12f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "held-1b-carried-close.png"));

        // side-on from the south, low: the limp (bent, short steps) and the man down
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 0.5f, spot.Y - 4.5f, G(spot.X, spot.Y) + 0.7f), 7f, MathF.PI * 0.5f, -0.10f);
        for (int i = 0; i < 4; i++)
        {
            now += 1.0 / 30.0;
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
            Walk(limper, 0.8f, 1.0 / 30.0);
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "held-2-wounded-down.png"));

        // ⭐ CE-1083 — a doors-off Black Hawk hovering 2 m above the ground 25 m north, three riders in each door, legs out
        float hx = spot.X + 5f, hy = spot.Y + 25f;
        var heli = world.CreateEntity();
        world.AddComponent(heli, new SimTransform { Position = new Vector3(hx, hy, G(hx, hy) + 1.75f + 2f), Rotation = Quaternion.Identity });
        world.AddComponent(heli, new TkbIdentity { TkbType = TkbEntityTypes.Heli_UH60_DoorsOff });
        var riders = new List<Entity>();
        foreach (byte seat in new byte[] { 0, 1, 2, 4, 5, 6 })
        {
            var r = Person(5f, 25f);
            world.SetComponent(r, world.GetComponent<SimTransform>(heli));   // the sim keeps a rider at the vehicle's reference point
            world.AddComponent(r, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = heli, Exposed = 1, Seat = seat });
            riders.Add(r);
        }
        sw.Camera3D.Pose = new CameraPose(new Vector3(hx + 0.5f, hy, G(hx, hy) + 3f), 16f, MathF.PI * 0.70f, -0.12f);
        for (int i = 0; i < 3; i++)
        {
            now += 1.0 / 30.0;
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "held-3-doors-off.png"));
        Assert.All(riders, r => Assert.Equal(CarriageKind.Seated, Carriage.Of(world, r, tkb).Kind));

        // ⭐ CE-1084 — the forward left rider leaves his seat: the sim puts him beside the hovering helicopter, on the ground; he is
        //   drawn falling from the door
        var jumper = riders[0];
        world.RemoveComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(jumper);
        world.SetComponent(jumper, new SimTransform { Position = new Vector3(hx + 2.4f, hy + 3.5f, G(hx + 2.4f, hy + 3.5f)), Rotation = Quaternion.Identity });
        // from the north (the left door's side), on the jumper: mid-air below the door
        sw.Camera3D.Pose = new CameraPose(new Vector3(hx + 2.4f, hy + 3.5f, G(hx, hy) + 1.8f), 9f, -MathF.PI * 0.40f, -0.05f);
        now += 1.0 / 30.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        frame.Step(canvas.Draw);
        now += 0.20;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "held-4-jump.png"));
        Assert.Equal(1, bodies.JumpsDrawn);
    }

    /// <summary>
    /// ⭐ LR-3 (docs/DESIGN_Visual_Effects.md §8.5, R-271) — what a DUMB VIEWER draws: no combat state at all (BallisticProjectile is not
    /// even registered), only replicated rounds — <c>LightweightRound</c> + the munition type + position + velocity, as the
    /// EntityMaster / WorldPos ingress leaves them. The built-in types: a 5.56 tracer burst, a 120 mm APFSDS-T, an RPG-7 and a TOW with
    /// their smoke, an 81 mm bomb falling and an M67 lying (a viewer knows no fuze); then the 2-D map's <c>LightweightRoundGizmo</c>.
    /// </summary>
    [SkippableFact]
    public void LR_AViewerDrawsReplicatedRounds_FromMasterAndPositionOnly()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.LightweightRound>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 3.3 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        void Soldier(float dx, float dy, StanceId stance, float yaw)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
        }
        int sent = 0;
        void Replicated(long type, Vector3 at, Vector3 velocity)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.AddComponent(e, new SimVelocity { Linear = velocity });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, default(Fdp.Toolkit.Replication.Components.LightweightRound));
            sent++;
        }
        Soldier(-8f, 0f, StanceId.Standing, 0f);
        Soldier(6f, 2f, StanceId.Crouched, MathF.PI);
        var mg = new Vector3(spot.X - 8f, spot.Y, G(spot.X - 8f, spot.Y) + 1.4f);
        foreach (var range in new[] { 40f, 75f, 110f })
        {
            var dir = Vector3.Normalize(new Vector3(0.55f, 1f, 0.02f));
            Replicated(MunitionTkbCatalog.Tkb556Tracer, mg + dir * range, dir * 900f);
        }
        Replicated(MunitionTkbCatalog.Tkb120mmApfsds, new Vector3(spot.X - 30f, spot.Y + 30f, G(spot.X, spot.Y) + 2.5f), new Vector3(1650f, 0f, 0f));
        Replicated(MunitionTkbCatalog.TkbRpg7Rocket, new Vector3(spot.X + 2f, spot.Y + 16f, G(spot.X, spot.Y) + 3f), new Vector3(-110f, 8f, 1f));
        Replicated(MunitionTkbCatalog.TkbTowMissile, new Vector3(spot.X - 8f, spot.Y + 24f, G(spot.X, spot.Y) + 6f), new Vector3(180f, -6f, 0f));
        Replicated(MunitionTkbCatalog.Tkb81mmMortarHe, new Vector3(spot.X + 10f, spot.Y + 12f, G(spot.X, spot.Y) + 14f), new Vector3(20f, 4f, -45f));
        Replicated(MunitionTkbCatalog.TkbM67Grenade, new Vector3(spot.X + 4.5f, spot.Y + 0.5f, G(spot.X + 4.5f, spot.Y + 0.5f)), Vector3.Zero);
        Replicated(MunitionTkbCatalog.Tkb556Ball, mg + new Vector3(0, 30, 0), new Vector3(0, 900, 0));   // never sent for real; drawn as nothing

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        var rounds = new Hrot.UI.Common.Effects.ProjectileLayer(() => world, () => tkb, () => sw.Camera3D) { GroundHeight = G };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(rounds);
        sw.Set(true, animate: false);

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 2f, spot.Y + 10f, G(spot.X, spot.Y) + 5f), 40f, MathF.PI * 0.5f, -0.12f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "lr-1-viewer-overview.png"));
        Assert.Equal(sent - 1, rounds.Drawn);                                    // all but the ball
        Assert.Equal(2, bodies.BodiesDrawn);                                        // ⛔ the two soldiers only — no round as a purple box

        var canvas2 = new MapCanvas(input: null);
        var cam2 = new MapCamera();
        const float zoom = 12f;
        var target = new Vector2(spot.X + 2f - 1280f / 2f / zoom, spot.Y + 12f + 800f / 2f / zoom);
        cam2.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        canvas2.Camera = cam2;
        var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var gizmos = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, buffer, new Fdp.Core.FdpEventBus(), camera: cam2);
        canvas2.AddLayer(gizmos);
        var projector = new Hrot.ScenarioEditor.Gizmos.LightweightRoundGizmo();
        foreach (var e in world.Query().With<Fdp.Toolkit.Replication.Components.LightweightRound>().With<SimTransform>().Build())
            projector.Draw(world, e, buffer);
        gizmos.Update(0.016f);
        frame.Step(canvas2.Draw);
        frame.Step(canvas2.Draw);
        frame.Screenshot(Path.Combine(shots, "lr-2-viewer-map2d.png"));
    }

    /// <summary>
    /// ⭐ CE-1034 H3b + H5d — a REAL height-grid terrain through the 3-D map: highway-rest's relief, the overpass (a deck 6 m up on two
    /// ramps) over the highway, a car on the deck and a truck on the highway under it. 📄 docs/DESIGN_Terrain_Height.md §3.4, §5.
    /// </summary>
    [SkippableFact]
    public void H5d_HighwayRest_In3D_DrawsTheReliefTheOverpassAndBothLevels()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.HighwayRest();
        Assert.NotNull(terrain.Height);
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        var tkb = Map3DTests.BuiltIn();
        var query = WorldQuery.Of(world)!;
        void Put(long type, float x, float y, float z, float yaw)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
        }
        float g = query.GroundHeightAt(200f, 150f);
        Put(UrbanCombatTkbCatalog.TkbCivilianCar, 200f, 147f, query.SurfaceZ(200f, 147f, g + 6f), MathF.PI / 2f);   // on the deck
        Put(TkbEntityTypes.Truck_HMMWV, 196f, 152f, g, 0f);                                                          // under it, on the highway
        Put(UrbanCombatTkbCatalog.TkbCivilianCar, 200f, 115f, query.SurfaceZ(200f, 115f, g + 3f), MathF.PI / 2f);   // half-way up the ramp

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();   // a new window: the shared shader compiles in THIS context
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var terrainLayer = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        canvas.AddLayer(terrainLayer);
        canvas.AddLayer(bodies);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        sw.Set(true, animate: false);
        // from the south-west, looking along the highway at the overpass
        sw.Camera3D.Pose = new CameraPose(new Vector3(200f, 150f, g + 3f), 75f, MathF.PI * 0.18f, -0.32f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        string png = Path.Combine(shots, "map3d-9-highway-rest.png");
        frame.Screenshot(png);

        Assert.True(terrainLayer.TriangleCount > 2000, $"terrain triangles: {terrainLayer.TriangleCount}");
        Assert.Equal(3, bodies.BodiesDrawn);
        // the relief and the overpass drew: ground green fills the lower half, the deck/ramp surfaces show over it
        var image = Raylib.LoadImage(png);
        try
        {
            int ground = 0, deck = 0;
            for (int y = 400; y < 800; y += 4)
                for (int x = 0; x < 1280; x += 4)
                {
                    var c = Raylib.GetImageColor(image, x, y);
                    if (c.G > c.R + 15 && c.G > c.B + 15) ground++;
                    if (c.R > c.G + 30 && c.R > c.B + 30) deck++;
                }
            Assert.True(ground > 5000, $"ground pixels {ground}");
            Assert.True(deck > 300, $"overpass pixels {deck}");
        }
        finally { Raylib.UnloadImage(image); }
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
        LitShader.Reset();   // a new window: the shared shader compiles in THIS context
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
        // ⭐ CE-1033 S5b — an entity CARD in 2-D (§3.6), south-west of the centre: the same renderer as in 3-D, over the symbol
        buffer.DrawSpatialAnchor(77, -100f, -30f, 0f, 0f, 0f, 0f);
        var card = Fdp.Toolkit.Diagnostics.Gizmos.EntityCards.Card(buffer, 77, Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Labels);
        card.Row(0).Frame(new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(90, 150, 255));
        card.Row(10).Bar(0.8f, new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(0, 230, 0), new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(40, 40, 40, 220));
        card.Row(20).Text("M1A2 #77", new Fdp.Toolkit.Diagnostics.Gizmos.Rgba32(240, 240, 240));
        gizmos.Update(0.016f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        Assert.Equal(1, gizmos.Cards.CardsDrawn);
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

    /// <summary>
    /// ⭐ CE-3174 (V) — the FPS STATION, in a real window: a rifleman this station holds (<see cref="Fdp.Toolkit.Behavior.Possession.PossessedBy"/>)
    /// on test-town's ground, looking at a tank across the street. The production canvas + switch + <see cref="PossessionStation"/>
    /// put the map into first person on him (the <see cref="FpsCameraDriver"/> on the 3-D camera), and the overlay draws the crosshair
    /// and the HUD — health and ammunition per mount. 📄 docs/DESIGN_Entity_Control.md §8d, §13.3. Screenshot: <c>fps-1-station.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE3174_V_TheFpsStation_DrawsTheFirstPersonView_AndTheHud()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var tkb = Map3DTests.BuiltIn();
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);

        var tank = world.CreateEntity();
        world.AddComponent(tank, new SimTransform { Position = new Vector3(spot.X, spot.Y, query.GroundHeightAt(spot.X, spot.Y)),
                                                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f) });
        world.AddComponent(tank, new TkbIdentity { TkbType = TkbEntityTypes.Tank_M1Abrams });

        var stand = FindOpenArea(query, spot - new Vector2(22f, 0f), 2f);
        float yaw = MathF.Atan2(spot.Y - stand.Y, spot.X - stand.X);   // facing the tank
        var me = world.CreateEntity();
        world.AddComponent(me, new SimTransform { Position = new Vector3(stand.X, stand.Y, query.GroundHeightAt(stand.X, stand.Y)),
                                                  Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        world.AddComponent(me, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
        world.AddComponent(me, new StanceIntent { TargetStance = StanceId.Standing });
        world.AddComponent(me, new Fdp.Toolkit.Combat.Components.Health { Current = 74f, Max = 100f });
        var ammo = new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 2 };
        ammo.Set(0, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 112, MaxAmmo = 150, MagazineSize = 30, MagazineRounds = 22 });
        ammo.Set(1, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 3, MaxAmmo = 4 });
        world.AddComponent(me, ammo);
        var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
        map.Register(1001L, me);
        map.Register(1002L, tank);
        world.SetSingletonManaged(map);
        world.AddComponent(me, new Fdp.Toolkit.Behavior.Possession.PossessedBy
        {
            StationNodeId = 0, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps, Epoch = 1,   // this node (no authority data ⇒ 0)
        });

        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        var target = new Vector2(spot.X - 1280f / 2f / 4f, spot.Y + 800f / 2f / 4f);
        two.ApplyCameraView(new MapCameraView { Target = target, Zoom = 4f, SmoothTarget = target, SmoothZoom = 4f });
        canvas.Camera = two;
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => ArticulationPose.Neutral);
        canvas.AddLayer(bodies);
        var sw = new MapViewSwitch(canvas, two, new MapCamera3D { ViewportOverride = new Vector2(1280, 800) });
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        var station = new PossessionStation(sw, () => world);
        canvas.AddLayer(station);

        station.Update(1f / 30f);
        Assert.True(station.InFps, "the station follows PossessedBy into first person");
        Assert.True(sw.Is3D);
        sw.Camera3D.Update(1f / 30f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        string png = Path.Combine(shots, "fps-1-station.png");
        frame.Screenshot(png);

        var eye = sw.Camera3D.Eye;
        Assert.InRange(Vector2.Distance(new Vector2(eye.X, eye.Y), stand), 0f, 0.05f);                 // the eye is on the rifleman
        Assert.InRange(eye.Z - query.GroundHeightAt(stand.X, stand.Y), 1.2f, 2.0f);                       // at a standing eye height
        Assert.True(bodies.BodiesDrawn >= 1, "the tank across the street is drawn");
        var lines = PossessionStation.Hud.Lines(world, me, mount: 0, vehicle: false, hullLocked: false);
        // ⭐ CE-1079 — each weapon is numbered by its KEY (1-4), not its mount index (this display-only rail had not run since)
        Assert.Equal(new[] { "HEALTH 74/100", "> 1 WEAPON  112/150  [22]", "  2 WEAPON  3/4" }, lines[..3]);
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

    /// <summary>
    /// ⭐ CE-1064 (docs/DESIGN_Map_3D_Mode.md §6j) — the CROUCHED figure, still and on the move: a soldier crouched at ease, ready and
    /// aimed, a soldier RUNNING crouched at 3 m/s (R-265's crouched run, the stance's 0.5 speed cap) with his rifle ready, and a
    /// civilian moving crouched. Side-on, at two points of the gait cycle, so the legs can be seen to move.
    /// </summary>
    [SkippableFact]
    public void CE1064_ACrouchedFigure_StillAndRunning()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 1.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Person(float dx, float dy, long type, Fdp.Toolkit.Combat.Components.WeaponPostureKind posture)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // all face +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Crouched });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = posture });
            return e;
        }
        const long Rifleman = TkbEntityTypes.Infantry_Rifleman;
        Person(0f, 3f, Rifleman, Fdp.Toolkit.Combat.Components.WeaponPostureKind.AtEase);
        Person(0f, 1.5f, Rifleman, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        Person(0f, 0f, Rifleman, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);
        var runner = Person(-4f, -1.5f, Rifleman, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        var civilian = Person(-4f, -3f, UrbanCombatTkbCatalog.TkbCivilianPedestrian, Fdp.Toolkit.Combat.Components.WeaponPostureKind.AtEase);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        void Move(Entity e, float speed, double dt)
        {
            var tf = world.GetComponent<SimTransform>(e);
            tf.Position += new Vector3(speed * (float)dt, 0f, 0f);
            tf.Position = tf.Position with { Z = G(tf.Position.X, tf.Position.Y) };
            world.SetComponent(e, tf);
        }
        void Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                now += 1.0 / 30.0;
                world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
                Move(runner, 3f, 1.0 / 30.0);
                Move(civilian, 1.5f, 1.0 / 30.0);
                frame.Step(canvas.Draw);
            }
        }

        // side-on from the south, low: the three still ones (front three-quarter), then the movers
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1f, spot.Y, G(spot.X, spot.Y) + 0.6f), 8f, MathF.PI * 0.62f, -0.06f);
        Frames(6);
        frame.Screenshot(Path.Combine(shots, "crouch-1-still-and-moving.png"));
        Assert.Equal(5, bodies.BodiesDrawn);

        // follow the runner side-on, at two points of his stride (a quarter cycle apart: 0.4 m at 3 m/s)
        CameraPose Beside(Entity e) => new(world.GetComponent<SimTransform>(e).Position + new Vector3(0.1f, 0f, 0.6f), 4.5f, MathF.PI * 0.5f, -0.05f);
        sw.Camera3D.Pose = Beside(runner);
        Frames(1);
        frame.Screenshot(Path.Combine(shots, "crouch-2-run-a.png"));
        Frames(3);
        sw.Camera3D.Pose = Beside(runner);
        Frames(1);
        frame.Screenshot(Path.Combine(shots, "crouch-3-run-b.png"));
        // ⭐ a frame with no sim time (a pause): he stays mid-stride, not back in the kneel
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "crouch-4-paused.png"));
    }

    /// <summary>
    /// ⭐ CE-1068 (docs/DESIGN_Map_3D_Mode.md §6k) — bt-range's doors in the 3-D map as door ENTITIES (the way the load creates them):
    /// one shut, one open (swung on its hinge), one destroyed (gone). Each is photographed from the side it opens to. ⛔ Before, each
    /// door entity was an unknown purple box in the middle of its doorway.
    /// </summary>
    [SkippableFact]
    public void CE1068_Doors_ShutOpenAndDestroyed()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.BtRange();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Terrain.DoorState>();
        world.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 1.0 });
        var states = new[] { Fdp.Toolkit.Terrain.TerrainDoorState.Closed, Fdp.Toolkit.Terrain.TerrainDoorState.Open };
        Assert.True(terrain.Doors.Count >= states.Length, "bt-range has two doors");
        var doors = new Entity[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            var def = terrain.Doors[i];
            var e = doors[i] = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = new Vector3(def.Center, def.SillZ), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Door });
            world.AddComponent(e, new Fdp.Toolkit.Terrain.DoorState { State = states[i] });
            world.AddComponent(e, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = def.Key });
        }

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        var query = WorldQuery.Of(world)!;
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        CameraPose Facing(int i)
        {
            var fp = terrain.DoorLeaves[i].Footprint;
            var n = Vector2.Normalize(fp[3] - fp[0]);                     // the side the leaf swings to
            var def = terrain.Doors[i];
            return new CameraPose(new Vector3(def.Center, def.SillZ + 1.0f), 5.5f, MathF.Atan2(-n.Y, -n.X) + 0.35f, -0.18f);
        }
        sw.Camera3D.Pose = Facing(0);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "door-1-closed.png"));
        sw.Camera3D.Pose = Facing(1);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "door-2-open.png"));
        Assert.Equal(2, bodies.DoorsDrawn);

        // the shut door is destroyed: from the same camera, its doorway is empty
        world.SetComponent(doors[0], new Fdp.Toolkit.Terrain.DoorState { State = Fdp.Toolkit.Terrain.TerrainDoorState.Destroyed });
        sw.Camera3D.Pose = Facing(0);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "door-3-destroyed.png"));
        Assert.Equal(1, bodies.DoorsDrawn);   // the destroyed one is not drawn

        // ⭐ CE-1069 — the open door is shut and LOCKED: it swings shut over sim time and its wood takes the locked tint
        world.SetComponent(doors[1], new Fdp.Toolkit.Terrain.DoorState { State = Fdp.Toolkit.Terrain.TerrainDoorState.Locked });
        sw.Camera3D.Pose = Facing(1);
        for (int k = 1; k <= 30; k++)
        {
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 1.0 + k / 30.0 });
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "door-4-locked.png"));
    }

    /// <summary>
    /// ⭐ CE-1070 (docs/DESIGN_Visual_Effects.md §6c) — where a muzzle flash is drawn: on an M1 and a T-72 (large), a Bradley (medium) and
    /// three riflemen (small: standing ready, standing aimed, crouched aimed). Each flash is 0.02 s old, anchored to its shooter's weapon
    /// as the spawner does it. Side-on, then close on the riflemen.
    /// </summary>
    [SkippableFact]
    public void CE1070_MuzzleFlashes_AtTheWeapons()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(world);
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Unit(long type, float dx, float dy, StanceId stance = StanceId.Standing,
                    Fdp.Toolkit.Combat.Components.WeaponPostureKind posture = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = posture });
            return e;
        }
        void Flash(long type, Entity shooter)
        {
            var e = world.CreateEntity();
            world.AddComponent(e, new SimTransform { Position = world.GetComponentRO<SimTransform>(shooter).Position, Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            var look = Hrot.Core.Tkb.EffectTkbCatalog.VisualOf(tkb, type)!;
            world.AddComponent(e, Fdp.Toolkit.Time.Lifespan.Starting(-0.02, look.Duration));
            var at = world.GetComponentRO<SimTransform>(shooter).Position;
            world.AddComponent(e, new Hrot.Map.Common.Components.EffectAnchor { Shooter = shooter, Toward = at + new Vector3(200f, 0f, 0f) });
        }
        Flash(Hrot.Core.Tkb.EffectTkbCatalog.MuzzleFlashLarge, Unit(TkbEntityTypes.Tank_M1Abrams, -14f, 10f));
        Flash(Hrot.Core.Tkb.EffectTkbCatalog.MuzzleFlashLarge, Unit(TkbEntityTypes.Tank_T72, -14f, 0f));
        Flash(Hrot.Core.Tkb.EffectTkbCatalog.MuzzleFlashMedium, Unit(TkbEntityTypes.IFV_Bradley, -14f, -10f));
        var riflemen = new[]
        {
            Unit(TkbEntityTypes.Infantry_Rifleman, 4f, 3f),
            Unit(TkbEntityTypes.Infantry_Rifleman, 4f, 0f, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed),
            Unit(TkbEntityTypes.Infantry_Rifleman, 4f, -3f, StanceId.Crouched, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed),
        };
        foreach (var r in riflemen) Flash(Hrot.Core.Tkb.EffectTkbCatalog.MuzzleFlashSmall, r);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var effects = new Hrot.UI.Common.Effects.EffectLayer(() => world, () => tkb, bodies) { GroundHeight = query.GroundHeightAt };
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(effects);
        sw.Set(true, animate: false);

        // side-on from the south: the three vehicles and the three riflemen face east (+x)
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 5f, spot.Y, G(spot.X, spot.Y) + 1.5f), 30f, MathF.PI * 0.5f, -0.12f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "muzzle-1-all.png"));
        Assert.Equal(6, effects.Drawn);

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 4.5f, spot.Y, G(spot.X, spot.Y) + 1.0f), 6f, MathF.PI * 0.5f, -0.08f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "muzzle-2-riflemen.png"));

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 10f, spot.Y, G(spot.X, spot.Y) + 2f), 13f, MathF.PI * 0.5f, -0.1f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "muzzle-3-t72.png"));
    }

    /// <summary>
    /// ⭐ CE-1097 (docs/DESIGN_Map_3D_Mode.md §6o) — the (V) acceptance for the face: riflemen standing ready, aimed and crouched, a civilian
    /// and a dead soldier, all facing east; seen face-on from the east, then from above. Every head carries its face — eyes, nose, mouth —
    /// a soldier's helmet in his uniform a step darker, the civilian's hair; the dead man's face looks up. Screenshots <c>face-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1097_V_TheFigures_HaveFaces_AndSoldiersWearHelmets()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Unit(long type, float dy, StanceId stance = StanceId.Standing,
                    Fdp.Toolkit.Combat.Components.WeaponPostureKind posture = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready, float dx = 0f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = posture });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            return e;
        }
        Unit(TkbEntityTypes.Infantry_Rifleman, 1.6f);
        Unit(TkbEntityTypes.Infantry_Rifleman, 0.6f, StanceId.Standing, Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed);
        Unit(TkbEntityTypes.Infantry_Rifleman, -0.4f, StanceId.Crouched);
        Unit(Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianPedestrian, -1.4f);   // a PERSON (TkbEntityTypes.CivilianPedestrian is drawn as a box)
        var dead = Unit(TkbEntityTypes.Infantry_Rifleman, 0f, dx: -2.4f);
        world.SetComponent(dead, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });   // first seen dead: he lies there

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        // face-on: the camera east of them, looking west at their faces
        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y + 0.1f, ground + 1.0f), 5.6f, MathF.PI, -0.10f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "face-1-front.png"));
        Assert.True(LitShader.Shared.IsValid, "the shader with the body surface compiled");   // ⭐ CE-1101 — else Raylib draws unlit, silently
        Assert.Equal(BodySurface.Glsl, LitShader.Surface);       // ⭐ CE-1101 — and it IS the body surface (else the counters lie: nothing is painted)
        Assert.Equal(5, bodies.FacesPainted);                    // ⭐ CE-1101 (§6r) — five faces painted by the shader
        Assert.InRange(bodies.FaceDetailsDrawn, 4, 5);           // and a helmet or hair box each (a civilian may be bald)

        // close on the standing riflemen
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y + 1.1f, ground + 1.55f), 1.6f, MathF.PI, -0.05f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "face-2-close.png"));

        // from above and behind them: the helmets, and the dead man's face looking up
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1.2f, spot.Y, ground + 0.4f), 5.5f, 0.35f, -0.75f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "face-3-above.png"));
    }

    /// <summary>
    /// ⭐ CE-1098 (docs/DESIGN_Map_3D_Mode.md §6p) — the (V) acceptance for blood: five riflemen facing east at health 100, 85, 50 and 15,
    /// and a dead one; more spots the more hurt he is (0 + 2 + 4 + 7 + 8). Face-on, then from above. Screenshots <c>blood-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1098_V_BloodSpots_GrowAsTheyAreHurt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        void Man(float dy, float health, float dx = 0f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
        }
        Man(1.8f, 100f);
        Man(0.6f, 85f);
        Man(-0.6f, 50f);
        Man(-1.8f, 15f);
        Man(0f, 0f, dx: -2.4f);   // dead: on his back behind them

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.0f), 5.8f, MathF.PI, -0.10f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "blood-1-front.png"));
        Assert.Equal(0 + 2 + 4 + 7 + 8, bodies.StainsDrawn);

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1.2f, spot.Y, ground + 0.4f), 5.5f, 0.35f, -0.75f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "blood-2-above.png"));
    }

    /// <summary>
    /// ⭐ CE-1105 (docs/DESIGN_Map_3D_Mode.md §6u) — the (V) acceptance for a burst you can see: an AH-64 hovering at 20 m fires its chin M134
    /// at a T-72 80 m ahead; 0.12 s later the stream of tracer dashes runs from the chin turret toward the tank. Side-on, then from behind the
    /// helicopter; then a doors-off UH-60's door gunners, the south one firing. Screenshots <c>stream-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1105_V_TheAttackHelicopterFiresABurst()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Put(long type, float dx, float dy, float up)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y) + up), Rotation = Quaternion.Identity });   // facing +x
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            return e;
        }
        var heli = Put(TkbEntityTypes.Heli_AH64, 0f, 0f, 1.33f + 20f);
        var tank = Put(TkbEntityTypes.Tank_T72, 80f, 0f, 0f);
        const double now = 3.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        Hrot.UI.Common.Effects.RecentShots.In(world).Record(heli, 0, now - 0.12, world.GetComponentRO<SimTransform>(tank).Position + new Vector3(0, 0, 1.2f));

        // a doors-off Black Hawk (404) hovering 15 m up behind it, its two door gunners at their M134s (seats 8 / 10, stations 9 / 11 — the
        //   preset's own); the south door gun fires at the ground to the south-east
        var hawk = Put(TkbEntityTypes.Heli_UH60_DoorsOff, -14f, 30f, 1.75f + 15f);
        var hawkAt = world.GetComponentRO<SimTransform>(hawk).Position;
        var doorGun = Entity.Null;
        var doorSeats = tkb.GetByType(TkbEntityTypes.Heli_UH60_DoorsOff)!.GetDescriptor<CarrierDefDto>()!.Seats;
        foreach (var (gunnerSeat, side) in new[] { (8, 1f), (10, -1f) })
        {
            var man = Put(TkbEntityTypes.Infantry_Rifleman, 0f, 0f, 0f);
            world.SetComponent(man, world.GetComponent<SimTransform>(hawk));   // the sim keeps a rider at the vehicle's reference point
            world.AddComponent(man, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(man, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = hawk, Exposed = 1, Seat = (byte)gunnerSeat });
            var gun = world.CreateEntity();
            world.AddComponent(gun, new SimTransform { Position = hawkAt + new Vector3(doorSeats[gunnerSeat + 1].X, doorSeats[gunnerSeat + 1].Y, doorSeats[gunnerSeat + 1].Z), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, side * MathF.PI / 2f) });
            world.AddComponent(gun, new TkbIdentity { TkbType = TkbEntityTypes.WeaponSystem_M134Minigun });
            world.AddComponent(gun, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1 });
            world.AddComponent(gun, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = Fdp.Toolkit.Combat.WeaponDeploy.Deployed });
            world.AddComponent(gun, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = hawk, Exposed = 1, Seat = (byte)(gunnerSeat + 1) });
            if (side < 0) doorGun = gun;
        }
        float gx = hawkAt.X + 45f, gy = hawkAt.Y - 55f;
        Hrot.UI.Common.Effects.RecentShots.In(world).Record(doorGun, 0, now - 0.10, new Vector3(gx, gy, G(gx, gy)));

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        var rounds = new Hrot.UI.Common.Effects.ProjectileLayer(() => world, () => tkb, () => sw.Camera3D) { GroundHeight = G };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(rounds);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 40f, spot.Y, ground + 16f), 75f, MathF.PI * 0.5f, -0.10f);   // from the south, above the roofs
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "stream-1-side.png"));
        Assert.Equal(2, rounds.StreamsDrawn);   // the chin gun and the door gun

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 40f, spot.Y, ground + 4f), 62f, 0.25f, -0.65f);   // behind the helicopter, looking down the stream
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "stream-2-behind.png"));

        sw.Camera3D.Pose = new CameraPose(hawkAt + new Vector3(3.1f, -1.0f, -0.4f), 5.5f, MathF.PI * 0.62f, -0.05f);   // from the south-east, close: the door gunners
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "stream-3-door-gun.png"));
        Assert.Equal(2, rounds.StreamsDrawn);
    }

    /// <summary>
    /// ⭐ CE-1102 / CE-1107 (docs/DESIGN_Map_3D_Mode.md §6n) — backend's weapon PRESETS drawn: the HMMWV (102) with its roof-ring M2 and
    /// gunner, the DShK technical (108) and the minigun technical (109), each crewed from its own catalogue seats as the sim spawns them
    /// (a seat's <c>StartWith</c>: a weapon at its station's place, a man embarked in his seat). Screenshot <c>presets-1.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1102_V_TheArmedPresets()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        int crew = 0, weapons = 0;
        void Preset(long type, float dx)
        {
            var host = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y;
            world.AddComponent(host, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing east
            world.AddComponent(host, new TkbIdentity { TkbType = type });
            var at = world.GetComponentRO<SimTransform>(host).Position;
            var seats = tkb.GetByType(type)!.GetDescriptor<CarrierDefDto>()!.Seats;
            for (int s = 0; s < seats.Count; s++)
            {
                if (seats[s].StartWith == 0) continue;
                var e = world.CreateEntity();
                world.AddComponent(e, new TkbIdentity { TkbType = seats[s].StartWith });
                world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = host, Exposed = (byte)(seats[s].Exposed != false ? 1 : 0), Seat = (byte)s });
                if (seats[s].Role == Fdp.Toolkit.Behavior.Components.SeatRole.Station)
                {
                    world.AddComponent(e, new SimTransform { Position = at + new Vector3(seats[s].X, seats[s].Y, seats[s].Z), Rotation = Quaternion.Identity });
                    world.AddComponent(e, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1 });
                    world.AddComponent(e, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = Fdp.Toolkit.Combat.WeaponDeploy.Deployed });
                    weapons++;
                }
                else
                {
                    world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });   // a rider at the host's reference
                    world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
                    if (seats[s].Exposed != false) crew++;   // an enclosed rider is not drawn
                }
            }
        }
        Preset(TkbEntityTypes.Truck_HMMWV, -7f);
        Preset(TkbEntityTypes.Technical_Dshk, 0f);
        Preset(TkbEntityTypes.Technical_Minigun, 7f);
        Assert.Equal(3, weapons);   // an M2, a DShK, an M134 — each in its station

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 1.5f), 17f, MathF.PI * 0.62f, -0.30f);   // from the south-east, above
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "presets-1.png"));
        Assert.Equal(3 + weapons + crew, bodies.BodiesDrawn);
    }

    /// <summary>
    /// ⭐ CE-1104 (docs/DESIGN_Map_3D_Mode.md §6t) — the (V) acceptance for a team carry: two medics carrying a wounded soldier on a stretcher,
    /// and a mortar team carrying a 120 mm mortar between them, all facing east. Side-on, then from above. Screenshots <c>team-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1104_V_AStretcher_AndATwoManWeapon()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsAssistingTag>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Put(long type, float dx, float dy, float health = 100f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
            return e;
        }
        // the stretcher: the front medic, the rear one 2 m behind, the casualty half way (as the sim places them)
        var front = Put(TkbEntityTypes.Infantry_Rifleman, 1.0f, 2f);
        var rear = Put(TkbEntityTypes.Infantry_Rifleman, -1.0f, 2f);
        var casualty = Put(TkbEntityTypes.Infantry_Rifleman, 0f, 2f, health: 35f);
        world.AddComponent(front, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f });
        var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1, Assistant = rear };
        list.Passengers[0] = casualty;
        list.SetMode(0, Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher);
        world.AddComponent(front, list);
        world.AddComponent(casualty, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = front, Exposed = 1, Mode = Fdp.Toolkit.Behavior.Components.CarryMode.Stretcher });
        world.AddComponent(rear, new Fdp.Toolkit.Behavior.Components.IsAssistingTag { Carrier = front });
        // the mortar team
        var gunner = Put(TkbEntityTypes.Infantry_Rifleman, 1.0f, -2f);
        var loader = Put(TkbEntityTypes.Infantry_Rifleman, -1.0f, -2f);
        var mortar = world.CreateEntity();
        world.AddComponent(mortar, new SimTransform { Position = world.GetComponentRO<SimTransform>(gunner).Position, Rotation = Quaternion.Identity });
        world.AddComponent(mortar, new TkbIdentity { TkbType = TkbEntityTypes.WeaponSystem_Mortar120 });
        world.AddComponent(mortar, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = (byte)WeaponPortability.TwoMan });
        world.AddComponent(gunner, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f });
        var crew = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1, Assistant = loader };
        crew.Passengers[1] = mortar;
        world.AddComponent(gunner, crew);
        world.AddComponent(mortar, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = gunner, Seat = 1, Exposed = 1 });   // a carried load is seen
        world.AddComponent(loader, new Fdp.Toolkit.Behavior.Components.IsAssistingTag { Carrier = gunner });

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 0.9f), 8.5f, MathF.PI * 0.5f, -0.12f);   // from the south
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "team-1-side.png"));
        Assert.Equal((Litters: 1, TeamLoads: 1), (Litters: bodies.LittersDrawn, TeamLoads: bodies.TeamLoadsDrawn));

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 0.5f), 7f, 0.4f, -0.8f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "team-2-above.png"));
    }

    /// <summary>
    /// ⭐ CE-1103 (docs/DESIGN_Map_3D_Mode.md §6s) — the (V) acceptance for the AH-64: one on its wheels, one hovering at 18 m with its rotors
    /// turning, a UH-60 beside them for scale. Side-on, then from above and ahead. Screenshots <c>ah64-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1103_V_TheAttackHelicopter()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        void Aircraft(long type, float dx, float dy, float up)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y) + 1.33f + up), Rotation = Quaternion.Identity });   // CG above its wheels
            world.AddComponent(e, new TkbIdentity { TkbType = type });
        }
        Aircraft(TkbEntityTypes.Heli_AH64, 0f, 0f, 0f);
        Aircraft(TkbEntityTypes.Heli_AH64, 4f, 20f, 18f);
        Aircraft(TkbEntityTypes.Heli_UH60, 2f, -20f, 0f);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        for (int k = 0; k < 20; k++)   // the hovering one's rotors spool up
        {
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = k / 10.0 });
            sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 2f, spot.Y, ground + 6f), 52f, MathF.PI * 0.5f, -0.10f);
            frame.Step(canvas.Draw);
        }
        frame.Screenshot(Path.Combine(shots, "ah64-1-side.png"));
        Assert.Equal(3, bodies.BodiesDrawn);

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 1f, spot.Y, ground + 1.5f), 16f, MathF.PI + 0.6f, -0.45f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "ah64-2-close.png"));
    }

    /// <summary>
    /// ⭐ CE-1099 (docs/DESIGN_Map_3D_Mode.md §6q) — the (V) acceptance for what people wear: a row of ten civilians (each dressed from his
    /// own seed), three children, and soldiers of each side — two friends (woodland), two hostiles (fleck), one neutral (plain). Face-on,
    /// then from above. Screenshots <c>dress-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1099_V_Civilians_Children_AndSoldiersBySide()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<EntityInfo>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        void Person(long type, float dx, float dy, ForceId side = ForceId.Neutral)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });   // facing +x (east)
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            world.AddComponent(e, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(e, new EntityInfo { ForceId = side });
        }
        long civilian = Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianPedestrian, child = Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianChild;
        for (int i = 0; i < 10; i++) Person(civilian, 0f, 4.5f - i * 0.9f);
        Person(child, 1.0f, 2.2f); Person(child, 1.0f, 1.3f); Person(child, 1.0f, 0.4f);
        Person(TkbEntityTypes.Infantry_Rifleman, 2.6f, 2.0f, ForceId.Friend);    // in front of the civilians (the camera is east)
        Person(TkbEntityTypes.Infantry_Rifleman, 2.6f, 1.0f, ForceId.Friend);
        Person(TkbEntityTypes.Infantry_Rifleman, 2.6f, -0.5f, ForceId.Hostile);
        Person(TkbEntityTypes.Infantry_Rifleman, 2.6f, -1.5f, ForceId.Hostile);
        Person(TkbEntityTypes.Infantry_Rifleman, 2.6f, -3.0f);
        long car = Hrot.Core.Tkb.UrbanCombatTkbCatalog.TkbCivilianCar;   // ⭐ CE-1100 — three cars behind them, each its own paint
        Person(car, -7.0f, 4.0f); Person(car, -7.0f, 0.0f); Person(car, -7.0f, -4.0f);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + 0.9f), 11.5f, MathF.PI, -0.08f);
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "dress-1-front.png"));
        Assert.True(LitShader.Shared.IsValid, "the shader with the body surface compiled");
        Assert.Equal(4 * 9, bodies.CamoPainted);                  // ⭐ CE-1101 (§6r) — two friends and two hostiles: legs, torso, arms painted
        Assert.Equal(10 + 3 + 5, bodies.FacesPainted);            // every head has its face
        Assert.InRange(bodies.DetailsDrawn, 0, 3 * 13);            // no camouflage boxes: only ties, shirts, skirts and long hair

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 2.6f, spot.Y - 0.5f, ground + 1.1f), 4.6f, MathF.PI, -0.10f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "dress-2-soldiers.png"));

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 0.5f, spot.Y, ground + 0.3f), 9f, 0.3f, -0.8f);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "dress-3-above.png"));
    }

    /// <summary>
    /// ⭐ CE-1115 (docs/DESIGN_Visual_Effects.md §9) — the (V) acceptance for decoys: a UH-60 flying east 40 m up has released a salvo of
    /// four flares (0.3 s apart, the oldest 1.2 s ago) and one chaff bundle — each a fire notification as backend's dispenser raises it,
    /// drawn by the spawn system and the effect layer. Screenshot <c>decoy-1-flares.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1115_V_FlaresAndChaff()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(world);
        world.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
        var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();   // the host's full TKB: the expendables (6034 / 6035) and their effects
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 50.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float ground = query.GroundHeightAt(spot.X, spot.Y);

        var velocity = new Vector3(45f, 0f, 0f);   // east, 45 m/s
        var heliAt = new Vector3(spot.X, spot.Y, ground + 40f);
        var heli = world.CreateEntity();
        world.AddComponent(heli, new SimTransform { Position = heliAt, Rotation = Quaternion.Identity });
        world.AddComponent(heli, new TkbIdentity { TkbType = TkbEntityTypes.Heli_UH60 });
        void Release(long ammo, double ago)   // where the aircraft was then: it flew on at its velocity since
            => world.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
            {
                Shooter = heli, WeaponIndex = 1, AmmoGuid = ammo, Quantity = 1, FiredAt = now - ago,
                Origin = heliAt - velocity * (float)ago + new Vector3(0f, 0f, -1f), Velocity = velocity,
            });
        for (int i = 0; i < 4; i++) Release(Hrot.Core.Tkb.MunitionTkbCatalog.TkbFlare, 1.2 - i * 0.3);
        Release(Hrot.Core.Tkb.MunitionTkbCatalog.TkbChaff, 1.5);
        world.Bus.SwapBuffers();
        new Hrot.UI.Common.Effects.EffectSpawnSystem().Execute(world, 0.016f);
        ((Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetCommandBuffer()).Playback(world);

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var effects = new Hrot.UI.Common.Effects.EffectLayer(() => world, () => tkb, bodies) { GroundHeight = query.GroundHeightAt };
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(effects);
        sw.Set(true, animate: false);

        sw.Camera3D.Pose = new CameraPose(heliAt + new Vector3(-22f, 0f, -6f), 38f, MathF.PI * 1.5f, -0.05f);   // from the south, level, close
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        frame.Screenshot(Path.Combine(shots, "decoy-1-flares.png"));
        var drawn = effects.Resolve();
        Assert.Equal(4, drawn.Count(r => r.Look.Kind == EffectKind.Flare));   // four flares and the chaff cloud, no muzzle flash
        Assert.Equal(1, drawn.Count(r => r.Look.Kind == EffectKind.Chaff));
        Assert.Equal(5, drawn.Count);
    }

    /// <summary>
    /// ⭐ CE-1112 (docs/DESIGN_Map_3D_Mode.md §6v) — the (V) acceptance for backend's air defence, from the CATALOGUE types: a ZU-23-2 (615)
    /// stowed on its wheels beside one deployed on its jacks with its gunner SEATED on it, a Stinger (613) lying on the ground; a Stinger
    /// gunner (202) with his launcher on his back and an Igla gunner (203) aiming his on his shoulder; the ZU-23 technical (110) with the gun
    /// on its bed floor and its gunner on the gun's seat. All facing north. Screenshots <c>ad-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1112_V_TheAirDefence()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 20.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var north = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        Entity Put(long type, float dx, float dy)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = north });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            return e;
        }
        Entity Weapon(long type, float dx, float dy, Fdp.Toolkit.Combat.WeaponDeploy deploy = Fdp.Toolkit.Combat.WeaponDeploy.Deployed)
        {
            var w = Put(type, dx, dy);
            world.AddComponent(w, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = 1, DeploySeconds = 3f, PackSeconds = 3f });
            world.AddComponent(w, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = deploy });
            return w;
        }
        Entity Soldier(long type, float dx, float dy)
        {
            var s = Put(type, dx, dy);
            world.AddComponent(s, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(s, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            return s;
        }
        Entity Carrying(Entity bearer, long launcherType)
        {
            var launcher = Weapon(launcherType, 0f, 0f, Fdp.Toolkit.Combat.WeaponDeploy.Stowed);
            world.SetComponent(launcher, world.GetComponent<SimTransform>(bearer));
            world.AddComponent(bearer, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.85f, DragSpeedFactor = 0.3f });
            var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 2 };
            list.Passengers[1] = launcher;
            world.AddComponent(bearer, list);
            world.AddComponent(launcher, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = bearer, Exposed = 1, Seat = 1 });
            return launcher;
        }

        // the guns, west to east: a ZU-23 stowed, one deployed and manned, a Stinger on the ground
        Weapon(TkbEntityTypes.WeaponSystem_Zu23, -6f, 0f, Fdp.Toolkit.Combat.WeaponDeploy.Stowed);
        var zu = Weapon(TkbEntityTypes.WeaponSystem_Zu23, 0f, 0f);
        var zuGunner = Soldier(TkbEntityTypes.Infantry_Rifleman, 0f, 0f);
        world.AddComponent(zuGunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = zu, Exposed = 1, Seat = 0 });
        Weapon(TkbEntityTypes.WeaponSystem_Stinger, 4f, 1f);

        // the MANPADS gunners: one carrying his Stinger, one aiming his Igla (it just fired)
        var bearer = Soldier(TkbEntityTypes.Infantry_StingerGunner, -3f, -6f);
        Carrying(bearer, TkbEntityTypes.WeaponSystem_Stinger);
        var aimer = Soldier(TkbEntityTypes.Infantry_IglaGunner, -0.5f, -6f);
        var igla = Carrying(aimer, TkbEntityTypes.WeaponSystem_Igla);
        world.SetComponent(aimer, new Fdp.Toolkit.Combat.Components.WeaponPosture
        {
            Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Point = new Vector3(spot.X + 40f, spot.Y + 300f, G(spot.X, spot.Y) + 300f),
        });
        Hrot.UI.Common.Effects.RecentShots.In(world).Record(igla, 0, now - 0.5);

        // the ZU-23 technical: its gun at its station seat, its gunner in his seat, as the sim spawns them (seat StartWith)
        var tech = Put(TkbEntityTypes.Technical_Zu23, 6f, -9f);
        var techTf = world.GetComponent<SimTransform>(tech);
        var seats = tkb.GetByType(TkbEntityTypes.Technical_Zu23)!.GetDescriptor<CarrierDefDto>()!.Seats;
        for (int i = 0; i < seats.Count; i++)
        {
            if (seats[i].StartWith == 0) continue;
            if (seats[i].Role == Fdp.Toolkit.Behavior.Components.SeatRole.Station)
            {
                var gun = Weapon(seats[i].StartWith, 0f, 0f);
                world.SetComponent(gun, new SimTransform { Position = techTf.Position + Vector3.Transform(new Vector3(seats[i].X, seats[i].Y, seats[i].Z), techTf.Rotation), Rotation = techTf.Rotation });
                world.AddComponent(gun, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = tech, Exposed = 1, Seat = (byte)i });
            }
            else
            {
                var man = Soldier(seats[i].StartWith, 0f, 0f);
                world.SetComponent(man, techTf);
                world.AddComponent(man, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = tech, Exposed = 1, Seat = (byte)i });
            }
        }

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        void Draw(int n = 2) { for (int i = 0; i < n; i++) { now += 1.0 / 30.0; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now }); frame.Step(canvas.Draw); } }

        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1f, spot.Y + 1f, ground + 0.8f), 12f, MathF.PI * 0.62f, -0.22f);   // the guns, from the south-east
        Draw();
        frame.Screenshot(Path.Combine(shots, "ad-1-zu23-stinger.png"));
        Assert.Equal(1, bodies.GunnersDrawn);   // the man seated on the deployed ZU-23

        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1.7f, spot.Y - 6f, ground + 1.3f), 5f, MathF.PI * 1.20f, -0.10f);   // the MANPADS gunners, from the south-west
        Draw();
        frame.Screenshot(Path.Combine(shots, "ad-2-manpads-gunners.png"));
        Assert.Equal(1, bodies.CarriedDrawn);
        Assert.Equal(1, bodies.ShoulderedDrawn);

        sw.Camera3D.Pose = new CameraPose(techTf.Position + new Vector3(0f, 0f, 1.4f), 10f, MathF.PI * 1.25f, -0.25f);   // the technical
        Draw();
        frame.Screenshot(Path.Combine(shots, "ad-3-technical.png"));
    }

    /// <summary>A point of plain ground (not inside a building footprint) closest to the town's middle.</summary>
    /// <summary>
    /// ⭐⭐ CE-1085 … CE-1093 (docs/DESIGN_Map_3D_Mode.md §6n) — the (V) acceptance, a real frame: a row of weapon systems — a tripod HMG
    /// STOWED beside one DEPLOYED with its gunner kneeling at the grips, a Kornet with its gunner, a mortar laid at 60° with its gunner, a
    /// towed howitzer with its trails spread and a man at the breech, a sentry gun, a self-propelled howitzer; a soldier carrying a Javelin
    /// on his back, one holding a launcher on his shoulder just after firing, an FPV drone hovering; a technical with its M2 in the bed and a
    /// kneeling gunner; a missile's top-attack trail; an A-10 with two of its four bombs left. Screenshots <c>ws-*.png</c>.
    /// </summary>
    [SkippableFact]
    public void CE1085_V_WeaponSystems_Gunners_Launchers_TheDrone_AndTheAttackPlane()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);

        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponAmmo>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemParams>();
        world.RegisterComponent<Fdp.Toolkit.Combat.WeaponSystemState>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        var tkb = Map3DTests.BuiltIn();
        Fdp.Core.DISEntityType Dis(byte category, byte sub = 0) => new() { Kind = 1, Domain = 1, Country = 225, Category = category, Subcategory = sub };
        void Type(long id, string name, Fdp.Core.DISEntityType dis) => tkb.Register(new Fdp.Interfaces.TkbTemplate(name, id) { DisType = dis });
        Type(990001, "M2 HMG (test)", Dis(50, 1)); Type(990002, "M134 minigun (test)", Dis(50, 2)); Type(990003, "Kornet (test)", Dis(50, 3));
        Type(990004, "Javelin (test)", Dis(50, 4)); Type(990005, "Sentry gun (test)", Dis(50, 5)); Type(990006, "81 mm mortar (test)", Dis(10));
        Type(990007, "M119 105 mm (test)", Dis(5)); Type(990008, "M109 155 mm (test)", Dis(4));
        var pickup = new Fdp.Interfaces.TkbTemplate("Technical (test)", 990009) { DisType = Dis(6) };
        pickup.AddDescriptor(new CarrierDefDto
        {
            Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Open,
            Seats =
            {
                new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Driver, Exposed = false },
                new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, X = -1.95f, Y = 0f, Z = 1.35f, Pose = SeatPose.Kneel },
                new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, X = -1.0f, Y = 0f, Z = 1.35f },
            },
        });
        tkb.Register(pickup);
        const long TestMissile = 6999;
        var missileType = new Fdp.Interfaces.TkbTemplate("Test ATGM", TestMissile);
        missileType.AddDescriptor(new MunitionVisualDto { Shape = MunitionShape.Missile, Length = 1.2f, Diameter = 0.14f, ColorHex = "#6B6E4A", SmokeTrail = true });
        tkb.Register(missileType);
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double now = 20.0;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now });
        var query = WorldQuery.Of(world)!;
        var spot = FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Put(long type, float dx, float dy, float yaw = 0f, float up = 0f)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y) + up), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
            world.AddComponent(e, new TkbIdentity { TkbType = type });
            return e;
        }
        Entity Weapon(long type, float dx, float dy, Fdp.Toolkit.Combat.WeaponDeploy deploy = Fdp.Toolkit.Combat.WeaponDeploy.Deployed)
        {
            var w = Put(type, dx, dy);
            world.AddComponent(w, new Fdp.Toolkit.Combat.WeaponSystemParams { NeedsOperator = 1, Portable = 1, DeploySeconds = 3f, PackSeconds = 3f });
            world.AddComponent(w, new Fdp.Toolkit.Combat.WeaponSystemState { Deploy = deploy });
            return w;
        }
        Entity Soldier(float dx, float dy, long type = TkbEntityTypes.Infantry_Rifleman)
        {
            var s = Put(type, dx, dy);
            world.AddComponent(s, new StanceIntent { TargetStance = StanceId.Standing });
            world.AddComponent(s, new Fdp.Toolkit.Combat.Components.WeaponPosture());
            return s;
        }
        void Mans(Entity weapon)
        {
            var man = Soldier(0f, 0f);
            world.SetComponent(man, world.GetComponent<SimTransform>(weapon));   // the sim keeps a gunner at his weapon's origin
            world.AddComponent(man, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = weapon, Exposed = 1, Seat = 0 });
        }
        // the row, west to east (x), all facing north (+y)
        float north = MathF.PI / 2f;
        Weapon(990001, -14f, 0f, Fdp.Toolkit.Combat.WeaponDeploy.Stowed);
        var hmg = Weapon(990001, -11f, 0f); Mans(hmg);
        var kornet = Weapon(990003, -7.5f, 0f); Mans(kornet);
        var mortar = Weapon(990006, -4f, 0f); Mans(mortar);
        var howitzer = Weapon(990007, 2f, 0f); Mans(howitzer);
        Weapon(990005, 7f, 0f);
        Weapon(990002, 9.5f, 0f);
        var spGun = Put(990008, 17f, 2f);
        foreach (var w in new[] { hmg, kornet, mortar, howitzer })
            world.SetComponent(w, world.GetComponent<SimTransform>(w) with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, north) });
        foreach (var w in new[] { hmg, kornet, mortar, howitzer })
            foreach (var g in world.Query().With<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>().Build())
                if (world.GetComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(g).VehicleEntity == w)
                    world.SetComponent(g, world.GetComponent<SimTransform>(w));

        // a soldier carrying a Javelin (on his back) and one who has just fired a launcher (on his shoulder), and an FPV drone above them
        var bearer = Soldier(-6f, -7f);
        var javelin = Weapon(990004, -6f, -7f, Fdp.Toolkit.Combat.WeaponDeploy.Stowed);
        world.AddComponent(bearer, new Fdp.Toolkit.Behavior.Components.PersonCarrier { CarrySpeedFactor = 0.5f, DragSpeedFactor = 0.3f });
        var list = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 2 };
        list.Passengers[1] = javelin;
        world.AddComponent(bearer, list);
        world.AddComponent(javelin, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = bearer, Exposed = 1, Seat = 1 });
        var shooter = Soldier(-3f, -7f, UrbanCombatTkbCatalog.TkbJavelinTeam);
        world.SetComponent(shooter, new Fdp.Toolkit.Combat.Components.WeaponPosture
        {
            Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Point = new Vector3(spot.X - 3f, spot.Y + 60f, G(spot.X, spot.Y) + 1.5f),
        });
        var suite = tkb.GetByType(UrbanCombatTkbCatalog.TkbJavelinTeam)!.GetDescriptor<WeaponSuiteDto>()!;
        int launcher = suite.Mounts.FindIndex(m => m.Category == WeaponCategory.Atgm);
        Hrot.UI.Common.Effects.RecentShots.In(world).Record(shooter, launcher, now - 0.5);
        Put(TkbEntityTypes.Drone_Fpv, -4.5f, -5f, up: 2.5f);

        // a technical, its M2 in the bed's station and a gunner kneeling behind it
        var tech = Put(990009, 4f, -9f);
        var techTf = world.GetComponent<SimTransform>(tech);
        var m2 = Weapon(990001, 0f, 0f);
        world.SetComponent(m2, new SimTransform { Position = techTf.Position + new Vector3(-1.0f, 0f, 1.35f), Rotation = techTf.Rotation });
        world.AddComponent(m2, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = tech, Exposed = 1, Seat = 2 });
        var techGunner = Soldier(0f, 0f);
        world.SetComponent(techGunner, techTf);
        world.AddComponent(techGunner, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = tech, Exposed = 1, Seat = 1 });

        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        var rounds = new Hrot.UI.Common.Effects.ProjectileLayer(() => world, () => tkb, () => sw.Camera3D) { GroundHeight = G };
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        canvas.AddLayer(rounds);
        sw.Set(true, animate: false);
        void Draw(int n = 2) { for (int i = 0; i < n; i++) { now += 1.0 / 30.0; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = now }); frame.Step(canvas.Draw); } }

        // the row of weapons, from the south-east, a little above
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1f, spot.Y + 0.5f, G(spot.X, spot.Y) + 0.8f), 15f, MathF.PI * 0.58f, -0.22f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-1-weapons.png"));
        Assert.Equal(4, bodies.GunnersDrawn);   // the four men at ground weapons (the technical's gunner is drawn from his SEAT, a Kneel seat pose)

        // the closer half: the stowed and deployed HMG, the Kornet and the mortar, their gunners
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 9f, spot.Y + 0.3f, G(spot.X, spot.Y) + 0.6f), 7f, MathF.PI * 0.62f, -0.18f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-2-hmg-kornet-mortar.png"));

        // the Javelin on a back, the launcher on a shoulder, the drone
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 4.5f, spot.Y - 6.5f, G(spot.X, spot.Y) + 1.4f), 6f, MathF.PI * 1.15f, -0.10f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-3-carried-shouldered-drone.png"));
        Assert.Equal(1, bodies.CarriedDrawn);
        Assert.Equal(1, bodies.ShoulderedDrawn);

        // the technical
        sw.Camera3D.Pose = new CameraPose(techTf.Position + new Vector3(0f, 0f, 1.2f), 8f, MathF.PI * 1.25f, -0.25f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-4-technical.png"));

        // the laid guns side-on, from the west and above: the mortar at 60°, the howitzer at 25°
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X - 1f, spot.Y + 1f, G(spot.X, spot.Y) + 1f), 11f, 0f, -0.30f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-4b-laid-guns.png"));

        // a missile's top attack: up from the launcher, over and down onto the SP howitzer — the trail bends with it
        var sp = world.GetComponent<SimTransform>(spGun).Position;
        var start = new Vector3(spot.X - 120f, spot.Y - 7f, G(spot.X, spot.Y) + 1.5f);   // fired from 140 m west
        var missile = world.CreateEntity();
        world.AddComponent(missile, new SimTransform { Position = start, Rotation = Quaternion.Identity });
        world.AddComponent(missile, new SimVelocity());
        world.AddComponent(missile, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Ammo = TestMissile, Muzzle = start, PreviousPosition = start });
        Vector3 Arc(float t) => Vector3.Lerp(start, sp, t) + new Vector3(0f, 0f, 45f * MathF.Sin(MathF.PI * MathF.Min(1f, t * 1.15f)));
        sw.Camera3D.Pose = new CameraPose((start + sp) / 2f + new Vector3(0f, 0f, 18f), 120f, MathF.PI * 0.5f, -0.05f);   // from the south
        for (int i = 1; i <= 70; i++)
        {
            float t = i / 80f;
            var at = Arc(t);
            world.SetComponent(missile, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            world.SetComponent(missile, new SimVelocity { Linear = (Arc(t + 0.01f) - at) * 100f });
            Draw(1);
        }
        frame.Screenshot(Path.Combine(shots, "ws-5-top-attack-trail.png"));

        // ⭐ CE-2172 / CE-1096 (air hand-off U1 / U2) — a helicopter's route drawn by the REAL route gizmo through the production gizmo
        //   layer: on the ground (z 0, draped), then 30 m, 60 m and 30 m above it at its waypoints (HeightAgl), Z-up
        world.RegisterManagedComponent<Hrot.Map.Common.Components.RoutePlan>();
        var route = world.CreateEntity();
        world.AddComponent(route, new TkbIdentity { TkbType = TkbEntityTypes.TacGraphic_Route });
        var plan = new Hrot.Map.Common.Components.RoutePlan();
        plan.Mutate(wps =>
        {
            wps.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = new Vector3(spot.X - 25f, spot.Y + 20f, 0f) });
            wps.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = new Vector3(spot.X - 5f, spot.Y + 35f, 0f), HeightAgl = 30f });
            wps.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = new Vector3(spot.X + 20f, spot.Y + 30f, 0f), HeightAgl = 60f });
            wps.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = new Vector3(spot.X + 40f, spot.Y + 10f, 0f), HeightAgl = 30f,
                                                                   Kind = Fdp.Toolkit.Navigation.RouteWaypointKind.Ground });
        });
        world.SetManagedComponent(route, plan);
        var routeBuffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var routeLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, routeBuffer, new Fdp.Core.FdpEventBus()) { CurrentCamera = () => canvas.Camera };
        routeLayer.Renderer3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(routeLayer);
        new Hrot.ScenarioEditor.Gizmos.RouteGizmo().Draw(world, route, routeBuffer);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 5f, spot.Y + 25f, G(spot.X, spot.Y) + 20f), 110f, MathF.PI * 0.40f, -0.18f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-7-route-heights.png"));

        // an A-10 flying 60 m over the row, two of its four bombs gone
        var a10 = Put(TkbEntityTypes.Jet_AttackA10, 0f, 10f, yaw: MathF.PI * 0.85f, up: 60f);
        world.AddComponent(a10, new Fdp.Toolkit.Combat.Components.WeaponAmmo { Count = 1, Ammo0 = 2, Max0 = 4 });
        sw.Camera3D.Pose = new CameraPose(world.GetComponent<SimTransform>(a10).Position, 24f, MathF.PI * 1.3f, -0.30f);
        Draw();
        frame.Screenshot(Path.Combine(shots, "ws-6-a10.png"));
    }

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
