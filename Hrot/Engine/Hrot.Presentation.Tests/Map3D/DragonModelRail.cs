using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.World;
using Hrot.Core.Tkb;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4634</c> D-4 (docs/designs/fun/DESIGN_Dragon.md §8, DR-11; the acceptance spec is <c>docs/UX/dragon-look.svg</c>, <c>scripts/gen-dragon-mockup.py</c>) — the dragon's 3-D look:
/// TKB 411 resolves to its own family and kit (not the AH-64 it is classed under); the kit stands on its feet in the TKB's 20 × 26 × 6 m box, its MOUTH where the breath leaves
/// (turret 0's pivot + trunnion + the flamethrower's muzzle); the NECK yaws with the breath's aim and the HEAD also nods; in flight the wings BEAT while it climbs or flies slowly and
/// GLIDE at speed with the legs tucked, landed they FOLD; dead, it lies wings out and legs folded (never a burnt wreck); its riders sit astride (the horse's pose); 0 B per frame.
/// ⛔ The frame test SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~DragonModelRail</c>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class DragonModelRail
{
    private static readonly Vector3 Size = DragonKits.Size;

    internal static TkbDatabase WithDragon()
    {
        var db = Map3DTests.BuiltIn();
        FunTkbCatalog.RegisterAll(db);   // as HrotEnvironment registers the fun pack (CE-4650)
        return db;
    }

    private static Vector3 At(KitPart p, KitPivots pivots, ArticulationPose pose, MotionPose motion, Vector3 local = default)
        => Vector3.Transform(local, p.BodyTransform(Size, pivots, pose, motion));

    /// <summary>The front-centre of the head (the mouth) — the Gun box reaching furthest forward.</summary>
    private static KitPart Head(ShapeKit kit) => kit.Parts.Where(p => p.Role == PartRole.Gun).OrderByDescending(p => p.Centre.X + p.Extent.X / 2f).First();

    [Fact]
    public void D_FAMILY_Type411_IsTheDragon_ItsOwnKit_TheTkbsBox_RedNotOlive_AnOpenCarrier_NotAnAnimal()
    {
        Assert.Equal(VisualFamily.Dragon, VisualFamilies.FamilyOfClass["Dragon"]);
        var t = WithDragon().GetByType(TkbEntityTypes.Dragon)!;
        Assert.Equal(VisualFamily.Dragon, VisualFamilies.Of(t));   // the more specific class wins over AttackHelicopter (1.2.*.20)
        var look = EntityBodyLayer3D.Resolve(t);
        Assert.Equal(VisualFamily.Dragon, look.Family);
        Assert.Same(DragonKits.Flying, EntityBodyLayer3D.KitOf(look));
        Assert.Equal(new Vector3(20f, 26f, 6f), look.Size);
        Assert.Equal(Size, ShapeKits.DefaultSize(VisualFamily.Dragon));
        Assert.True(look.Colour.R > look.Colour.G + 60 && look.Colour.R > look.Colour.B + 60, $"the TKB's dark red, got {look.Colour}");
        var red = EntityBodyLayer3D.DefaultColour(VisualFamily.Dragon);
        Assert.True(red.R > red.G + 60, $"the default is red too, got {red}");
        Assert.True(EntityBodyLayer3D.OpenCarrier(VisualFamily.Dragon), "its rider sits on it in the open: drawn while he is the operator");
        Assert.False(VisualFamilies.IsAnimal(VisualFamily.Dragon), "a combat aircraft, not a grazing animal (no animal mode, no lying down)");
        Assert.True(DragonKits.Flying.IsArticulated && DragonKits.Landed.IsArticulated);
    }

    [Fact]
    public void D_KIT_StandsOnItsFeet_InTheTkbsBox_WingsSpreadOrFolded_AndTheMouthIsTheBreathsMuzzle()
    {
        static (Vector3 Min, Vector3 Max) Bounds(ShapeKit kit)
        {
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var p in kit.Parts)
            {
                var m = p.BodyTransform(Size);
                for (int i = 0; i < 8; i++)
                {
                    var w = Vector3.Transform(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f), m);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                }
            }
            return (min, max);
        }
        var (fMin, fMax) = Bounds(DragonKits.Flying);
        var (lMin, lMax) = Bounds(DragonKits.Landed);
        Assert.InRange(fMin.Z, -0.02f, 0.02f);                 // on its feet: the box bottom (the TKB's contacts, 3 m below the CG)
        Assert.InRange(lMin.Z, -0.02f, 0.02f);
        Assert.InRange(fMax.Y - fMin.Y, 25f, 26.5f);           // the TKB's 26 m span
        Assert.InRange(lMax.Y - lMin.Y, 3f, 5f);               // folded: as wide as its body and planted feet
        Assert.InRange(fMax.Z, 5f, 6.5f);                      // 6 m tall (the horns)
        Assert.InRange(fMax.X, 10.4f, 11f);                    // the snout is the front
        Assert.InRange(fMin.X, -14f, -12.5f);                  // the tail beyond the 20 m box (a tail is long; the box is the body the sim collides)

        // ⭐ the TKB places the neck and the head exactly where the kit draws them: KitFromTkb's pivots = the kit's own, and it moves no part
        var t = WithDragon().GetByType(TkbEntityTypes.Dragon)!;
        var (parts, pivots) = KitFromTkb.For(DragonKits.Flying, t, Size);
        Assert.Equal(DragonKits.Flying.Pivots.Turret.X, pivots.Turret.X, 4);
        Assert.Equal(DragonKits.Flying.Pivots.Turret.Y, pivots.Turret.Y, 4);
        Assert.Equal(DragonKits.Flying.Pivots.GunTrunnionXZ.X, pivots.GunTrunnionXZ.X, 4);
        Assert.Equal(DragonKits.Flying.Pivots.GunTrunnionXZ.Y, pivots.GunTrunnionXZ.Y, 4);
        Assert.Equal(DragonKits.Flying.Parts, parts);   // no Turret part to re-centre (the neck is PartRole.Neck), no barrel cylinder to resize
        // the mouth = turret 0's pivot + trunnion + mount 0's muzzle, from the CG — in kit metres (feet at z 0)
        var turret = t.GetDescriptor<TurretSuiteDto>()!.Turrets[0];
        var mount = t.GetDescriptor<WeaponSuiteDto>()!.Mounts.First(m => m.TurretIndex == 0 && m.MuzzleOffsetX > 0f);
        var mouth = new Vector3(turret.PivotX + turret.TrunnionX + mount.MuzzleOffsetX, 0f, turret.PivotZ + turret.TrunnionZ + 3f);
        Assert.Equal(DragonKits.Mouth, mouth);
        var head = Head(DragonKits.Flying);
        var front = At(head, pivots, ArticulationPose.Neutral, MotionPose.Still, new Vector3(0.5f, 0f, 0f));
        Assert.InRange(Vector3.Distance(front, mouth), 0f, 0.35f);
        Assert.Contains(DragonKits.Flying.Parts, p => p.Role == PartRole.Neck);
        Assert.DoesNotContain(DragonKits.Flying.Parts, p => p.Role == PartRole.Turret);
        Assert.Equal(4, DragonKits.Landed.Parts.Count(p => p.Role == PartRole.QuadFrontLeft) * 2);   // two parts a leg
        Assert.DoesNotContain(DragonKits.Landed.Parts, p => p.Role is PartRole.WingLeft or PartRole.WingRight);
        Assert.Equal(2 * 2, DragonKits.Flying.Parts.Count(p => p.Role is PartRole.WingLeft or PartRole.WingRight));
    }

    [Fact]
    public void D_POSE_TheNeckYawsWithTheBreathsAim_TheHeadAlsoNods_AndNothingElseTurns()
    {
        var kit = DragonKits.Flying;
        var pv = kit.Pivots;
        var neck = kit.Parts.First(p => p.Role == PartRole.Neck);
        var head = Head(kit);
        var body = kit.Parts.First(p => p.Role == PartRole.Hull);
        var turned = new ArticulationPose(0.6f, 0f);
        var nodded = new ArticulationPose(0f, -0.4f);
        var baseAt = new Vector3(DragonKits.NeckBase.X, 0f, 0f);
        // azimuth: the neck and the head swing left about the neck's base (a vertical axis), keeping their distance from it
        var n0 = At(neck, pv, ArticulationPose.Neutral, MotionPose.Still); var n1 = At(neck, pv, turned, MotionPose.Still);
        Assert.True(n1.Y > 0.5f, $"turned left: {n1}");
        Assert.Equal(Vector2.Distance(new(n0.X, n0.Y), new(baseAt.X, baseAt.Y)), Vector2.Distance(new(n1.X, n1.Y), new(baseAt.X, baseAt.Y)), 3);
        Assert.Equal(n0.Z, n1.Z, 3);
        var m0 = At(head, pv, ArticulationPose.Neutral, MotionPose.Still, new Vector3(0.5f, 0, 0));
        var m1 = At(head, pv, turned, MotionPose.Still, new Vector3(0.5f, 0, 0));
        Assert.Equal(MathF.Atan2(m1.Y, m1.X - baseAt.X) - MathF.Atan2(m0.Y, m0.X - baseAt.X), 0.6f, 3);
        // elevation: the head nods (the mouth goes DOWN for a negative elevation: the breath aimed at the ground), the neck does not
        var m2 = At(head, pv, nodded, MotionPose.Still, new Vector3(0.5f, 0, 0));
        Assert.True(m2.Z < m0.Z - 0.4f, $"the mouth lowers: {m0.Z} → {m2.Z}");
        Assert.Equal(neck.BodyTransform(Size, pv, ArticulationPose.Neutral, MotionPose.Still), neck.BodyTransform(Size, pv, nodded, MotionPose.Still));
        // the body never turns with the aim
        Assert.Equal(body.BodyTransform(Size, pv, ArticulationPose.Neutral, MotionPose.Still), body.BodyTransform(Size, pv, new ArticulationPose(1.1f, -0.5f), MotionPose.Still));
    }

    [Fact]
    public void D_WINGS_BeatClimbingOrSlow_GlideFast_FoldLanded_TuckInFlight_FreezeWhenPaused_AndAllocateNothing()
    {
        // the curves
        Assert.Equal(1f, DragonPoses.BeatTarget(5f, 0f));
        Assert.Equal(1f, DragonPoses.BeatTarget(30f, 3f));
        Assert.Equal(0f, DragonPoses.BeatTarget(30f, 0f));
        Assert.Equal(DragonPoses.GlideAngle, DragonPoses.Wing(1.37, 0f));
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 100; i++) { float w = DragonPoses.Wing(i * 0.01, 1f); lo = MathF.Min(lo, w); hi = MathF.Max(hi, w); }
        Assert.InRange(hi - lo, 1.1f, 1.2f);   // ±33°, one beat a second

        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        var pos = new Vector3(0f, 0f, 30f);
        MotionPose Fly(Vector3 v, bool airborne) { sim += 0.05; pos += v * 0.05f; return t.Advance(e, pos, Quaternion.Identity, VisualFamily.Dragon, Size, sim, airborne); }
        // slow, climbing: beats (after the blend) and the legs tuck
        var seen = new List<float>();
        for (int i = 0; i < 60; i++) seen.Add(Fly(new Vector3(4f, 0f, 3f), true).Wing);
        float a = seen.Skip(40).Min(), b = seen.Skip(40).Max();
        Assert.True(b - a > 0.9f, $"beating: {a}..{b}");
        Assert.InRange(Fly(new Vector3(4f, 0f, 3f), true).Tuck, 0.95f, 1f);
        // fast and level: glides (the beat eases out over ~1 s, then the wing is still at the glide angle)
        for (int i = 0; i < 80; i++) Fly(new Vector3(40f, 0f, 0f), true);
        float g1 = Fly(new Vector3(40f, 0f, 0f), true).Wing, g2 = Fly(new Vector3(40f, 0f, 0f), true).Wing;
        Assert.InRange(MathF.Abs(g1 - g2), 0f, 0.01f);
        Assert.InRange(g1, DragonPoses.GlideAngle - 0.02f, DragonPoses.GlideAngle + 0.02f);
        // landed: no wing motion (the folded kit), the legs come down and WALK when it moves
        MotionPose landed = default;
        for (int i = 0; i < 60; i++) landed = Fly(new Vector3(2f, 0f, 0f), false);
        Assert.Equal(0f, landed.Wing);
        Assert.InRange(landed.Tuck, 0f, 0.05f);
        Assert.Equal(QuadGait.Walk, landed.Gait);
        // a paused clock freezes the stroke
        for (int i = 0; i < 30; i++) Fly(new Vector3(4f, 0f, 3f), true);
        var p1 = t.Advance(e, pos, Quaternion.Identity, VisualFamily.Dragon, Size, sim, true);
        var p2 = t.Advance(e, pos, Quaternion.Identity, VisualFamily.Dragon, Size, sim, true);
        Assert.Equal(p1.Wing, p2.Wing);
        // ⭐ HOT PATH — 0 B in the steady state
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) Fly(new Vector3(i % 400 < 200 ? 4f : 40f, 0f, i % 100 < 50 ? 2f : 0f), i % 600 < 500);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        // a wing part rotates about its root by the pose's angle; the folded kit does not move
        var wing = DragonKits.Flying.Parts.First(p => p.Role == PartRole.WingLeft);
        Assert.True(At(wing, default, ArticulationPose.Neutral, MotionPose.Still with { Wing = 0.5f }).Z > At(wing, default, ArticulationPose.Neutral, MotionPose.Still).Z + 2f, "the tip goes up");
        // ⭐ both wings beat TOGETHER (a mirror pair): at the same pose both tips rise alike and stay on their own side
        var right = DragonKits.Flying.Parts.First(p => p.Role == PartRole.WingRight);
        foreach (float w in new[] { -0.5f, 0.3f, 0.6f })
        {
            var l = At(wing, default, ArticulationPose.Neutral, MotionPose.Still with { Wing = w });
            var r = At(right, default, ArticulationPose.Neutral, MotionPose.Still with { Wing = w });
            Assert.Equal(l.Z, r.Z, 2);
            Assert.Equal(l.Y, -r.Y, 2);
        }
    }

    /// <summary>⭐ the breath leaves the DRAWN mouth under every aim: the flame jet starts at <see cref="Fdp.Toolkit.Combat.Muzzle"/> (the sim's posed muzzle, <c>EffectLayer.MuzzleOf</c>);
    /// the head's front, posed by the same azimuth and elevation through the layer's kit-to-body shift, is that point. 🔴 Red-proof: the neck's or head's pivot off the TKB's ⇒ apart.</summary>
    [Fact]
    public void D_BREATH_TheJetLeavesTheDrawnMouth_UnderEveryAimTheTkbAllows()
    {
        var t = WithDragon().GetByType(TkbEntityTypes.Dragon)!;
        var turret = t.GetDescriptor<TurretSuiteDto>()!.Turrets[0];
        var mount = t.GetDescriptor<WeaponSuiteDto>()!.Mounts.First(m => m.TurretIndex == 0 && m.MuzzleOffsetX > 0f);
        var offset = new Vector3(mount.MuzzleOffsetX, mount.MuzzleOffsetY, mount.MuzzleOffsetZ);
        var geometry = t.GetDescriptor<BodyGeometryDto>()!;
        var kitToBody = Matrix4x4.CreateTranslation(geometry.BodyCentreX, geometry.BodyCentreY, geometry.BodyCentreZ - Size.Z / 2f);   // the layer's (EntityBodyLayer3D, CE-1041)
        var (_, pivots) = KitFromTkb.For(DragonKits.Flying, t, Size);
        var head = Head(DragonKits.Flying);
        foreach (float az in new[] { -1.2f, -0.5f, 0f, 0.7f, 1.2f })
            foreach (float el in new[] { -1.0f, -0.4f, 0f, 0.25f })
            {
                var drawn = Vector3.Transform(Vector3.Transform(new Vector3(0.5f, 0f, 0f), head.BodyTransform(Size, pivots, new ArticulationPose(az, el), MotionPose.Still)), kitToBody);
                var sim = Fdp.Toolkit.Combat.Muzzle.Posed(turret, offset, az, el);
                Assert.True(Vector3.Distance(drawn, sim) < 0.35f, $"az {az} el {el}: drawn mouth {drawn} vs the jet's origin {sim}");
            }
    }

    /// <summary>⭐ the bird's bob (user, `2026-11-19`: <i>"birds oscillate vertically as they move their wings … the passengers and the view from the dragon moves up and down in sync"</i>):
    /// beating, the drawn body rises and sinks ±<see cref="DragonPoses.HeaveM"/> OPPOSITE the wings (they sweep down, it lifts); gliding it is steady; landed it is still; the sim altitude
    /// never moves (only <see cref="MotionTracker.SwayOf"/>). 🔴 Red-proof: the heave left out of <c>SwayOf</c> ⇒ the bob is 0.</summary>
    [Fact]
    public void D_HEAVE_TheBodyBobsWithTheBeat_OppositeTheWings_SteadyGliding_StillLanded_AndAllocatesNothing()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        var pos = new Vector3(0f, 0f, 30f);
        (MotionPose Pose, float Bob) Fly(Vector3 v, bool airborne, double dt = 0.02)
        {
            sim += dt; pos += v * (float)dt;
            var pose = t.Advance(e, pos, Quaternion.Identity, VisualFamily.Dragon, Size, sim, airborne);
            return (pose, t.SwayOf(e).Translation.Z);
        }
        for (int i = 0; i < 150; i++) Fly(new Vector3(4f, 0f, 2f), true);   // 3 s climbing slowly: fully beating
        float lo = float.MaxValue, hi = float.MinValue; int opposite = 0, samples = 0;
        for (int i = 0; i < 50; i++)   // one beat
        {
            var (pose, bob) = Fly(new Vector3(4f, 0f, 2f), true);
            lo = MathF.Min(lo, bob); hi = MathF.Max(hi, bob);
            float stroke = pose.Wing - DragonPoses.GlideAngle * 0.5f;   // the wing about its mid-stroke
            if (MathF.Abs(stroke) > 0.2f) { samples++; if (MathF.Sign(stroke) != MathF.Sign(bob)) opposite++; }
        }
        Assert.InRange(hi, DragonPoses.HeaveM * 0.9f, DragonPoses.HeaveM * 1.01f);
        Assert.InRange(lo, -DragonPoses.HeaveM * 1.01f, -DragonPoses.HeaveM * 0.9f);
        Assert.Equal(samples, opposite);   // wings up ⇒ body down, wings down ⇒ body up
        Assert.Equal(DragonPoses.Heave(sim, 1f), t.SwayOf(e).Translation.Z, 4);   // the same curve the look and the sound read
        // a paused clock holds it
        var held = t.SwayOf(e);
        t.Advance(e, pos, Quaternion.Identity, VisualFamily.Dragon, Size, sim, true);
        Assert.Equal(held, t.SwayOf(e));
        // gliding: the bob fades with the beat
        for (int i = 0; i < 200; i++) Fly(new Vector3(40f, 0f, 0f), true);
        float glideMax = 0f;
        for (int i = 0; i < 50; i++) glideMax = MathF.Max(glideMax, MathF.Abs(Fly(new Vector3(40f, 0f, 0f), true).Bob));
        Assert.InRange(glideMax, 0f, 0.03f);
        // landed: none at all
        for (int i = 0; i < 10; i++) Fly(new Vector3(4f, 0f, 2f), true);
        Fly(Vector3.Zero, false);
        Assert.True(t.SwayOf(e).IsIdentity, "standing on its legs it does not bob");
        // a never-seen entity, and any other family: identity
        Assert.True(t.SwayOf(new Entity(9, 1)).IsIdentity);
        // ⭐ HOT PATH — 0 B
        for (int i = 0; i < 100; i++) Fly(new Vector3(4f, 0f, 2f), true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0f;
        for (int i = 0; i < 2000; i++) sum += Fly(new Vector3(4f, 0f, 2f), true).Bob;
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── the real 3-D frame ────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void D_FRAME_TheDragon_Flying_Landed_WithItsRiders_Breathing_AndDead_ByTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<IsEmbarkedTag>(); world.RegisterComponent<WeaponPosture>();
        world.RegisterComponent<FlightState>(); world.RegisterComponent<Health>();
        var tkb = WithDragon();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = MineClearerModelRail.OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        float ground = G(spot.X, spot.Y);
        float rest = BodyGeometry.RestingHeight(tkb.GetByType(TkbEntityTypes.Dragon)!.GetDescriptor<BodyGeometryDto>()!);
        var dragon = world.CreateEntity();
        void Place(Vector3 at, Quaternion? rot = null) => world.SetComponent(dragon, new SimTransform { Position = at, Rotation = rot ?? Quaternion.Identity });
        world.AddComponent(dragon, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground + rest), Rotation = Quaternion.Identity });
        world.AddComponent(dragon, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Dragon });
        world.AddComponent(dragon, new FlightState { Airborne = 0 });
        world.AddComponent(dragon, new Health { Current = 100f, Max = 100f });
        Entity Rider(byte seat)
        {
            var r = world.CreateEntity();
            world.AddComponent(r, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
            world.AddComponent(r, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Infantry_Rifleman });
            world.AddComponent(r, new IsEmbarkedTag { VehicleEntity = dragon, Exposed = 1, Seat = seat });
            world.AddComponent(r, new WeaponPosture { Kind = WeaponPostureKind.AtEase });
            return r;
        }
        var rider = Rider(0); var pillion = Rider(1);
        var pose = ArticulationPose.Neutral;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var two = new MapCamera();
        canvas.Camera = two;
        var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => e == dragon ? pose : ArticulationPose.Neutral);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => G(x, y);
        canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
        canvas.AddLayer(bodies);
        sw.Set(true, animate: false);
        void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
        Pixels Screen(string? save = null) { Render(); if (save != null) frame.Screenshot(Path.Combine(shots, save)); return ScreenOf(); }
        void Look(Vector3 at, float dist, float yaw, float pitch) => sw.Camera3D.Pose = new CameraPose(at, dist, yaw, pitch);
        const float Side = MathF.PI * 0.5f;

        // B2 — landed, wings folded, the rider and the pillion astride
        var landedAt = new Vector3(spot.X, spot.Y, ground + 3f);
        Look(landedAt, 30f, Side + 0.6f, -0.25f);
        for (int i = 0; i < 4; i++) { Clock(0.1); Render(); }
        var landed = Screen("dragon-b2-landed-riders.png");
        Assert.Equal(3, bodies.BodiesDrawn);   // the dragon and its two riders (no gear legs: its feet are the kit's)
        // A3 — the same landed dragon with no riders: the riders are on screen above
        world.DestroyEntity(rider); world.DestroyEntity(pillion);
        var bare = Screen("dragon-a3-landed.png");
        Assert.True(Diff(landed, bare) > 400, $"the riders are drawn on its back: {Diff(landed, bare)} px");
        Assert.Equal(1, bodies.BodiesDrawn);
        // B1 — breathing: the neck turned to its aim, the head down
        pose = new ArticulationPose(0.6f, -0.35f);
        var breathing = Screen("dragon-b1-breath-aim.png");
        Assert.True(Diff(bare, breathing) > 1500, $"the neck and head turned: {Diff(bare, breathing)} px");
        pose = ArticulationPose.Neutral;
        // A1 — flying, climbing slowly: the wings spread and beat (frames differ while the sim runs)
        world.SetComponent(dragon, new FlightState { Airborne = 1 });
        var air = new Vector3(spot.X, spot.Y, ground + 25f);
        Look(air, 45f, Side + 0.5f, -0.35f);
        for (int i = 0; i < 30; i++) { Clock(0.05); air += new Vector3(0.2f, 0f, 0.15f); Place(air); Render(); }
        Look(air, 45f, Side + 0.5f, -0.35f);
        var beatA = Screen("dragon-a1-flying-beat.png");
        Clock(0.25); air += new Vector3(1f, 0f, 0.75f); Place(air); Look(air, 45f, Side + 0.5f, -0.35f);
        var beatB = Screen();
        Assert.True(Diff(beatA, beatB) > 2000, $"the wings beat: {Diff(beatA, beatB)} px");
        // ⭐ the rider bobs with it, and so does his view: his camera eye on the drawn body (the sway) vs the sim's steady seat — apart by exactly the dragon's drawn bob
        var flyer = Rider(0);
        float maxBob = 0f;
        for (int i = 0; i < 25; i++)
        {
            Clock(0.04); air += new Vector3(0.16f, 0f, 0.12f); Place(air); Look(air, 45f, Side + 0.5f, -0.35f); Render();
            Assert.True(SeatSwitch.TryEye(world, flyer, tkb, bodies, out var drawnEye));
            Assert.True(SeatSwitch.TryEye(world, flyer, tkb, out var simEye));
            float bob = bodies.SwayOf(dragon).Translation.Z;
            Assert.Equal(bob, drawnEye.Z - simEye.Z, 3);
            maxBob = MathF.Max(maxBob, MathF.Abs(bob));
        }
        Assert.InRange(maxBob, DragonPoses.HeaveM * 0.5f, DragonPoses.HeaveM * 1.01f);
        world.DestroyEntity(flyer);
        // A2 — gliding fast and level: the wings hold still (only the ground under it changes as it moves; a paused clock: nothing)
        for (int i = 0; i < 40; i++) { Clock(0.05); air += new Vector3(2f, 0f, 0f); Place(air); Render(); }
        Look(air, 45f, Side + 0.5f, -0.35f);
        var glideA = Screen("dragon-a2-gliding.png");
        var glideB = Screen();
        Assert.Equal(0, Diff(glideA, glideB));
        // B3 — dead on the ground: lies wings out, legs folded (not a wreck)
        world.SetComponent(dragon, new FlightState { Airborne = 0 });
        Place(landedAt);
        world.SetComponent(dragon, new Health { Current = 0f, Max = 100f });
        Look(landedAt, 40f, Side + 0.6f, -0.45f);
        Screen("dragon-b3-dead.png");
        Assert.Equal(1, bodies.BodiesDrawn);
    }

    // ── pixels ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Pixels(Color[] Colours, int W, int H);

    private static Pixels ScreenOf()
    {
        var image = Raylib.LoadImageFromScreen();
        try
        {
            var all = new Color[image.Width * image.Height];
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) all[y * image.Width + x] = Raylib.GetImageColor(image, x, y);
            return new Pixels(all, image.Width, image.Height);
        }
        finally { Raylib.UnloadImage(image); }
    }

    private static int Diff(Pixels a, Pixels b)
    {
        int n = 0;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > 6) n++;
        }
        return n;
    }
}
