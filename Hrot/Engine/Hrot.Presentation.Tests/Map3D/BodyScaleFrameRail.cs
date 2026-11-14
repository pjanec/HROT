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
        var terrain = new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world));
        canvas.AddLayer(terrain);
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        // the terrain's meshes are built on a worker and arrive frames later (CE-1394): a rail that compares two frames waits for them first
        Map3DFrameRail.StepUntil(frame, canvas, () => terrain.TriangleCount > 1000);
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
        for (int i = 0; i < 30; i++) frame.Step(canvas.Draw);   // warm: the first frames grow the layer's maps and lists once
        Assert.True(bodies.BatchInstances > 100, $"the squad is in the batch ({bodies.BatchInstances} instances)");
        int allocating = 0; long worst = 0;
        for (int i = 0; i < 20; i++) { frame.Step(canvas.Draw); worst = Math.Max(worst, probe.LastBytes); if (probe.LastBytes > 0) allocating++; }
        Assert.True(worst == 0, $"{allocating} of 20 steady frames of the body layer allocated (worst {worst} B)");
    }

    /// <summary>
    /// ⭐ CE-4557 (§6ak D-1) — the bands' edges, pure: a man 1.8 m tall at a distance where he is 13 / 11 / 1.6 / 1.4 px on screen is Full / Far / Far / Hidden;
    /// a vehicle is banded by its LARGEST extent (a 6 m car seen from afar keeps its length). The scale: w = the point's x, 600 px per unit at w = 1.
    /// </summary>
    [Fact]
    public void CE4557_TheBandEdges_ByTheBodysLargestExtentOnScreen()
    {
        var scale = new ViewFrustum.ScreenScale(new Vector4(1f, 0f, 0f, 0f), 600f);
        var man = new Vector3(0.5f, 0.5f, 1.8f);
        Vector3 At(float px) => new(1.8f * 600f / px, 0f, 0f);   // where he covers px pixels
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Full, EntityBodyLayer3D.BandFor(scale, At(13f), man));
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Far, EntityBodyLayer3D.BandFor(scale, At(11f), man));
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Far, EntityBodyLayer3D.BandFor(scale, At(1.6f), man));
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Hidden, EntityBodyLayer3D.BandFor(scale, At(1.4f), man));
        var car = new Vector3(6f, 2f, 1.5f);   // at the man's 11 px distance a 6 m car is ~37 px: Full
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Full, EntityBodyLayer3D.BandFor(scale, At(11f), car));
        Assert.Equal(EntityBodyLayer3D.ScreenBand.Full, EntityBodyLayer3D.BandFor(default, At(0.1f), man));   // no pass: everything Full, as before
    }

    private static (EntityRepository World, Fdp.Interfaces.ITkbDatabase Tkb, Vector2 Spot, Func<float, float, float> Ground, Entity Man) OneMan()
    {
        var (world, tkb, spot, G) = Squad(riflemen: 1);
        Entity man = default;
        var keep = new List<Entity>();
        foreach (var e in world.Query().With<SimTransform>().Build()) keep.Add(e);
        foreach (var e in keep)
            if (man.IsNull && world.GetComponentRO<Fdp.Toolkit.Combat.Components.Health>(e).Current > 0f
                && world.GetComponentRO<TkbIdentity>(e).TkbType == (TkbTypeId)TkbEntityTypes.Infantry_Rifleman) man = e;
            else world.DestroyEntity(e);
        return (world, tkb, spot, G, man);
    }

    /// <summary>
    /// ⭐ CE-4557 (§6ak D-1, D-2, D-7) — on this GL, through the production camera: one rifleman swept from near to far — his band follows his height on screen as
    /// the CAMERA measures it (<c>MapCamera3D.ZoomAt</c> × 1.8 m: Full from 12 px, one box below, nothing below 1.5 px); a Far man is ONE instance (no pose, no
    /// face); a SELECTED man is drawn in full however far. And at the Full / Far edge the figure does not pop: just above and just below 12 px he covers about
    /// the same pixels in about the same colour (screenshots <c>band-edge-*.png</c>).
    /// </summary>
    [SkippableFact]
    public void CE4557_TheBands_FollowTheScreenHeight_ASelectedManStaysFull_AndTheEdgeDoesNotPop()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (world, tkb, spot, G, man) = OneMan();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var (frame, canvas, sw) = Open(world, bodies, G);
        using var _ = frame;
        string shots = Shots();
        var at = world.GetComponentRO<SimTransform>(man).Position;
        var mid = at + new Vector3(0f, 0f, 0.9f);
        float PixelsAt(float distance)
        {
            sw.Camera3D.Pose = new CameraPose(mid, distance, MathF.PI * 0.25f, -0.12f);
            frame.Step(canvas.Draw);
            frame.Step(canvas.Draw);
            return sw.Camera3D.ZoomAt(mid) * 1.8f;
        }
        foreach (float d in new[] { 20f, 60f, 150f, 300f, 900f, 2500f })
        {
            float px = PixelsAt(d);
            if (MathF.Abs(px - EntityBodyLayer3D.FullFromPx) < 0.5f || MathF.Abs(px - EntityBodyLayer3D.HiddenBelowPx) < 0.1f) continue;   // too close to an edge to call
            var expected = px >= EntityBodyLayer3D.FullFromPx ? "full" : px >= EntityBodyLayer3D.HiddenBelowPx ? "far" : "hidden";
            var got = bodies.BodiesFull == 1 ? "full" : bodies.BodiesFar == 1 ? "far" : bodies.BodiesHidden == 1 ? "hidden" : "?";
            Assert.True(expected == got, $"at {d} m he is {px:0.0} px: expected {expected}, drawn {got}");
            if (got == "far") Assert.Equal(1, bodies.BatchInstances);   // one box, nothing posed
            if (got == "hidden") Assert.Equal(0, bodies.BatchInstances);
        }

        // the Full / Far edge: the distances for 13.5 px and 10.5 px, looking DOWN onto open ground; each frame rendered with the man and with him moved
        // out of view — the pixels that differ are exactly his silhouette (no building, no sky in the comparison)
        float pxTimesDistance = PixelsAt(100f) * 100f;   // px · distance is constant along the view
        var (fullPx, fullColour) = Silhouette(pxTimesDistance / 13.5f, "full", expectFull: true);
        var (farPx, farColour) = Silhouette(pxTimesDistance / 10.5f, "far", expectFull: false);
        // from 13.5 to 10.5 px his area scales by (10.5 / 13.5)^2 — the box must cover about what the figure covered at that size, not a blob or nothing
        float expectedRatio = (10.5f / 13.5f) * (10.5f / 13.5f);
        Assert.True(fullPx > 10 && farPx > 5, $"the figure is on screen ({fullPx} / {farPx} px)");
        float ratio = farPx / (float)fullPx / expectedRatio;
        Assert.True(ratio is > 0.6f and < 1.6f, $"the far box covers about what the figure covered: {farPx} px vs {fullPx} px (x{ratio:0.00} of the expected)");
        Assert.True(Vector3.Distance(fullColour, farColour) < 30f, $"the far box keeps his colour: full {fullColour}, far {farColour}");

        (int Pixels, Vector3 Colour) Silhouette(float distance, string name, bool expectFull)
        {
            sw.Camera3D.Pose = new CameraPose(mid, distance, MathF.PI * 0.25f, -0.55f);
            frame.Step(canvas.Draw); frame.Step(canvas.Draw);
            Assert.Equal(expectFull ? 1 : 0, bodies.BodiesFull);
            Assert.Equal(expectFull ? 0 : 1, bodies.BodiesFar);
            string with = Path.Combine(shots, $"band-edge-{name}.png");
            frame.Screenshot(with);
            var home = world.GetComponentRO<SimTransform>(man);
            world.SetComponent(man, home with { Position = home.Position + new Vector3(0f, 0f, -500f) });   // out of view, under the ground
            frame.Step(canvas.Draw); frame.Step(canvas.Draw);
            string without = Path.Combine(shots, $"band-edge-{name}-empty.png");
            frame.Screenshot(without);
            world.SetComponent(man, home);
            frame.Step(canvas.Draw);
            return Diff(with, without);
        }

        // a SELECTED man is drawn in full at any distance (D-7)
        world.AddComponent(man, new Hrot.Map.Common.Components.SelectionState { IsSelected = true, IsPrimarySelection = true });
        PixelsAt(900f);
        Assert.Equal(1, bodies.BodiesFull);
        Assert.True(bodies.BatchInstances > 10, $"his parts are drawn ({bodies.BatchInstances} instances)");
        Assert.Equal(1, bodies.SelectionBoxesDrawn);
    }

    /// <summary>The pixels of <paramref name="with"/> that differ from <paramref name="without"/> (by more than 12 levels in a channel) — what was drawn in one and
    /// not the other — and their mean colour in <paramref name="with"/>.</summary>
    private static (int Pixels, Vector3 Colour) Diff(string with, string without)
    {
        var a = Raylib.LoadImage(with);
        var b = Raylib.LoadImage(without);
        try
        {
            int n = 0; var sum = Vector3.Zero;
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                {
                    var p = Raylib.GetImageColor(a, x, y);
                    var q = Raylib.GetImageColor(b, x, y);
                    if (Math.Abs(p.R - q.R) > 12 || Math.Abs(p.G - q.G) > 12 || Math.Abs(p.B - q.B) > 12) { n++; sum += new Vector3(p.R, p.G, p.B); }
                }
            return (n, n > 0 ? sum / n : Vector3.Zero);
        }
        finally { Raylib.UnloadImage(a); Raylib.UnloadImage(b); }
    }

    /// <summary>
    /// ⭐ CE-4558 (§6ak D-6, G-4) — the pick through the boxes the frame DREW: after a drawn frame, a pick at each body's pixel (and between bodies) through
    /// <see cref="BodyPickIndex"/> hits what the old scan of every entity hits, at the same distance (a picker over a layer that never drew scans); it tests a
    /// handful of boxes, not every body; and while the mouse and the camera are still, frame after frame, NO pick runs (the camera's pick cache).
    /// 🔴 Red-proof: the walk stopping at its first cell ⇒ a body behind the first cell is missed ⇒ the scan and the index disagree ⇒ red.
    /// </summary>
    [SkippableFact]
    public void CE4558_ThePick_ThroughTheDrawnBoxes_HitsWhatTheScanHits_AndNothingRunsWhileStill()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (world, tkb, spot, G) = Squad();
        var bodies = new EntityBodyLayer3D(() => world, () => tkb);
        var (frame, canvas, sw) = Open(world, bodies, G);
        using var session = frame;
        var cam = sw.Camera3D;
        Func<Vector3, float> mpp = p => 1f / cam.ZoomAt(p);
        var indexed = new EntityBoxPicker(() => world, () => tkb, bodies, mpp);
        var scanned = new EntityBoxPicker(() => world, () => tkb, new EntityBodyLayer3D(() => world, () => tkb), mpp);   // never draws: scans
        cam.Pickers.Add(indexed);
        cam.Pose = new CameraPose(new Vector3(spot.X + 1f, spot.Y + 1f, G(spot.X, spot.Y) + 1.0f), 14f, MathF.PI * 0.15f, -0.25f);
        for (int i = 0; i < 3; i++) frame.Step(canvas.Draw);
        Assert.True(bodies.PickIndex.HasFrame && bodies.PickIndex.Count >= 15, $"the frame recorded its boxes ({bodies.PickIndex.Count})");

        // every body's centre pixel, and points between them
        var targets = new List<Vector3>();
        foreach (var e in world.Query().With<SimTransform>().Build())
            if (bodies.TryGetBox(world, e, tkb, out var c, out _, out _)) { targets.Add(c); targets.Add(c + new Vector3(0.7f, 0.3f, 0.4f)); }
        int hits = 0, maxTested = 0;
        foreach (var t in targets)
        {
            var px = cam.WorldToScreen(t);
            var eye = cam.Eye;
            var dir = Vector3.Normalize(t - eye);
            bool a = indexed.TryPick(eye, dir, 500f, out var ha);
            maxTested = Math.Max(maxTested, bodies.PickIndex.LastTested);
            bool b = scanned.TryPick(eye, dir, 500f, out var hb);
            Assert.True(a == b, $"at {t}: index {(a ? "hit" : "miss")}, scan {(b ? "hit" : "miss")}");
            if (!a) continue;
            hits++;
            Assert.True(Vector3.Distance(ha.Point, hb.Point) < 1e-3f && MathF.Abs(ha.Distance - hb.Distance) < 1e-3f,
                        $"at {t}: index hit {ha.Point} at {ha.Distance:0.000}, scan hit {hb.Point} at {hb.Distance:0.000}");
        }
        Assert.True(hits >= targets.Count / 2, $"most rays hit a body ({hits} of {targets.Count})");
        Assert.True(maxTested < 15, $"a pick tests the boxes near its ray, not every body ({maxTested} tested)");

        // still: the hover asks for the same pixel every frame — the pick runs once, then not again until something moves
        var pixel = cam.WorldToScreen(targets[0]);
        cam.Pick(pixel, PickFilter.All);
        long picks = BodyDrawStats.Picks;
        for (int i = 0; i < 10; i++) { frame.Step(canvas.Draw); cam.Pick(pixel, PickFilter.All); }
        Assert.Equal(picks, BodyDrawStats.Picks);
        cam.Pose = cam.Pose with { Yaw = cam.Pose.Yaw + 0.05f };   // the camera moves: the next pick runs
        frame.Step(canvas.Draw);
        cam.Pick(pixel, PickFilter.All);
        long moved = BodyDrawStats.Picks;
        Assert.True(moved > picks, "the camera moved: the picks ran again");
        frame.Step(canvas.Draw);
        cam.Pick(pixel, PickFilter.All);
        Assert.Equal(moved, BodyDrawStats.Picks);   // and still again: nothing
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
