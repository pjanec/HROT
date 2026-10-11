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
