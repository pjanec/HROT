using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
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
/// ⭐ CE-4555 … CE-4558 (docs/DESIGN_Map_3D_Mode.md §6ak) — bodies and doors at scale, on this machine's GL: the batch draws every part of a frame in one
/// instanced call per mesh and the picture is the one the per-part <c>DrawMesh</c> drew; the screen-size bands; the pick through the drawn boxes.
/// Screenshots go to <c>HROT_RAIL_SHOTS</c> (else the temp folder) — never to git. Skips where there is no display.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class BodyScaleFrameRail
{
    private static string Shots()
    {
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        return shots;
    }

    /// <summary>A squad on open ground in test-town: riflemen (camouflage, faces, helmets), one aimed, one crouched, one hurt (stains), civilians (faces, hair,
    /// outfit details), a dead man, and a car — every kind of instance the body layer adds.</summary>
    internal static (EntityRepository World, Fdp.Interfaces.ITkbDatabase Tkb, Vector2 Spot, Func<float, float, float> Ground) Squad(int riflemen = 12)
    {
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        world.RegisterComponent<StanceIntent>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        world.RegisterComponent<Hrot.Map.Common.Components.SelectionState>();
        world.RegisterComponent<EntityInfo>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0.0 });
        var query = WorldQuery.Of(world)!;
        var spot = Map3DFrameRail.FindOpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        Entity Unit(long type, float dx, float dy, StanceId stance = StanceId.Standing, float health = 100f,
                    Fdp.Toolkit.Combat.Components.WeaponPostureKind posture = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) });
            world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            world.AddComponent(e, new StanceIntent { TargetStance = stance });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = posture });
            world.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
            world.AddComponent(e, new EntityInfo { ForceId = ForceId.Friend });   // a side: a soldier wears its camouflage
            return e;
        }
        for (int i = 0; i < riflemen; i++)
            Unit(TkbEntityTypes.Infantry_Rifleman, (i % 4) * 1.4f, (i / 4) * 1.4f - 2f, i == 5 ? StanceId.Crouched : StanceId.Standing, i == 3 ? 40f : 100f,
                 i == 1 ? Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed : Fdp.Toolkit.Combat.Components.WeaponPostureKind.Ready);
        Unit(CivilianTkbCatalog.TkbCivilianAdultMale, -1.5f, -2f);
        Unit(CivilianTkbCatalog.TkbCivilianAdultMale, -1.5f, 0f);
        Unit(TkbEntityTypes.Infantry_Rifleman, -2.5f, 2f, health: 0f);   // dead: lies there
        Unit(UrbanCombatTkbCatalog.TkbCivilianCar, 2f, 6f);
        return (world, tkb, spot, G);
    }

    /// <summary>Wraps the body layer and measures the bytes its <c>Draw3D</c> allocates (this thread).</summary>
    private sealed class AllocProbe(EntityBodyLayer3D inner) : ISceneLayer
    {
        public long LastBytes;
        public string Name => inner.Name;
        public int LayerBitIndex => inner.LayerBitIndex;
        public bool Has3D => true;
        public void Update(float dt) => inner.Update(dt);
        public void Draw(RenderContext ctx) => inner.Draw(ctx);
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
        public void Draw3D(RenderContext ctx)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            inner.Draw3D(ctx);
            LastBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private static (UiFrameSession Frame, MapCanvas Canvas, MapViewSwitch Switch) Open(EntityRepository world, ISceneLayer bodies, Func<float, float, float> ground)
    {
        var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => ground(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        return (frame, canvas, sw);
    }

    /// <summary>
    /// ⭐ CE-4555 (G-1) / CE-4556 (G-2) — the batch: every part, face box, outfit detail and stain of the frame is ONE instance, drawn in one instanced call per mesh
    /// with instances (≤ 3: cube, cylinder, cone) — and the picture is the one the per-part <c>DrawMesh</c> path draws (the same frame through
    /// <see cref="EntityBodyLayer3D.ForcePerDraw"/>, the layer's draw before the batch, faces and camouflage set as uniforms): the look is unchanged (§6ak.5 ③).
    /// 🔴 Red-proof (run 2026-11-14): the instanced program reading a tone from the wrong vector (<c>#define tone1 instData2.rgb</c>) ⇒ 21 885 pixels differ ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE4556_TheBatch_OneCallPerMesh_AndTheSamePictureAsDrawMeshPerPart()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (world, tkb, spot, G) = Squad();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var (frame, canvas, sw) = Open(world, bodies, G);
        using var _ = frame;
        string shots = Shots();
        float ground = G(spot.X, spot.Y);
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 1f, spot.Y + 1f, ground + 1.0f), 9f, MathF.PI * 0.15f, -0.18f);
        for (int i = 0; i < 4; i++) frame.Step(canvas.Draw);

        Assert.True(LitShader.Shared.InstancedIsValid, "the instanced program compiled on this GL");
        int instances = bodies.PartsDrawn + bodies.FaceDetailsDrawn + bodies.DetailsDrawn + bodies.StainsDrawn;
        Assert.True(bodies.BodiesDrawn >= 15, $"the squad is drawn ({bodies.BodiesDrawn} bodies)");
        Assert.Equal(instances, bodies.BatchInstances);
        int buckets = 0;
        for (int m = 0; m < 3; m++) if (bodies.Batch.CountOf(m) > 0) buckets++;
        Assert.Equal(buckets, bodies.BatchDrawCalls);
        Assert.InRange(bodies.BatchDrawCalls, 1, 3);
        Assert.True(bodies.FacesPainted >= 14 && bodies.CamoPainted > 30, $"faces {bodies.FacesPainted}, camouflaged parts {bodies.CamoPainted}");
        Assert.True(bodies.StainsDrawn > 0, "the hurt rifleman bleeds");
        string a = Path.Combine(shots, "batch-instanced.png");
        frame.Screenshot(a);

        bodies.ForcePerDraw = true;
        frame.Step(canvas.Draw);
        frame.Step(canvas.Draw);
        Assert.Equal(bodies.BatchInstances, bodies.BatchDrawCalls);   // one DrawMesh each, as before the batch
        string b = Path.Combine(shots, "batch-per-draw.png");
        frame.Screenshot(b);
        bodies.ForcePerDraw = false;

        var (differ, lit) = Compare(a, b);
        Assert.True(lit > 20_000, $"the squad covers the frame ({lit} non-sky pixels)");
        Assert.True(differ <= lit / 200, $"the instanced picture is the per-part picture: {differ} of {lit} pixels differ by more than 6 levels");
    }

    /// <summary>
    /// ⭐ CE-4555 (G-1, §6ak.5 ⑤) — the layer and its batch allocate NOTHING in a steady frame (the batch's arrays and buffers are grown, never shrunk).
    /// </summary>
    [SkippableFact]
    public void CE4555_ASteadyFrame_AllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (world, tkb, spot, G) = Squad();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var probe = new AllocProbe(bodies);
        var (frame, canvas, sw) = Open(world, probe, G);
        using var _ = frame;
        sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X + 1f, spot.Y + 1f, G(spot.X, spot.Y) + 1.0f), 9f, MathF.PI * 0.15f, -0.18f);
        for (int i = 0; i < 6; i++) frame.Step(canvas.Draw);
        Assert.True(bodies.BatchInstances > 100, $"the squad is in the batch ({bodies.BatchInstances} instances)");
        long worst = 0;
        for (int i = 0; i < 5; i++) { frame.Step(canvas.Draw); worst = Math.Max(worst, probe.LastBytes); }
        Assert.True(worst == 0, $"a steady frame of the body layer allocated {worst} B");
    }

    /// <summary>Pixels of <paramref name="a"/> that differ from <paramref name="b"/> by more than 6 levels in a channel, of the pixels that are not sky.</summary>
    internal static (int Differ, int Lit) Compare(string a, string b)
    {
        var ia = Raylib.LoadImage(a);
        var ib = Raylib.LoadImage(b);
        try
        {
            int differ = 0, lit = 0;
            for (int y = 0; y < ia.Height; y += 1)
                for (int x = 0; x < ia.Width; x += 1)
                {
                    var p = Raylib.GetImageColor(ia, x, y);
                    var q = Raylib.GetImageColor(ib, x, y);
                    bool sky = p.B > p.R + 20 && p.B > p.G && q.B > q.R + 20 && q.B > q.G;
                    if (sky) continue;
                    lit++;
                    if (Math.Abs(p.R - q.R) > 6 || Math.Abs(p.G - q.G) > 6 || Math.Abs(p.B - q.B) > 6) differ++;
                }
            return (differ, lit);
        }
        finally { Raylib.UnloadImage(ia); Raylib.UnloadImage(ib); }
    }
}
