using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Hrot.UI.Common.Effects;
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
/// ⭐⭐ <c>CE-4647</c> (AC-15, <c>docs/designs/air-attack/DESIGN_Air_Combat.md</c> §4d; mockups <c>air-combat-{body,stores,hud,arena}.svg</c>; handoff <c>HANDOFF_Coordinator_To_Ui_Air_Combat_Visuals.md</c>) — the air combat LOOKS:
/// the MiG's twin-fin body (step 2), the missiles on the rails with rounds left (step 3) and — in this file as they land — the AAM look / burn trail / launch flash (4), the afterburner, the vortex and the dead jet's smoke (5).
/// <para>⭐ Re-bound (the air lane's contract is merged): the MiG is the REAL type 409 (class <c>TwinEngineFighter</c>), the missiles the real munitions 6060…; the jets carry the air lane's REAL weapon suites (S3: gun · IR pair · radar pair · dispenser last) and the rails find each mount by what it holds, never by index; they write the real <c>FighterPilotState</c>.</para>
/// ⛔ The frame tests SKIP without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~AirCombatLooksRail</c>. 🔴 Red-proofs: see each test.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class AirCombatLooksRail
{
    /// <summary>The MiG-29's type id (the contract: <see cref="TkbEntityTypes.Jet_MiG29"/>).</summary>
    internal const long Mig = TkbEntityTypes.Jet_MiG29;
    private static readonly Vector3 MigSize = new(17.3f, 11.4f, 4.7f);
    private static readonly ulong Aim9x = (ulong)MunitionTkbCatalog.TkbAim9x, Aim120 = (ulong)MunitionTkbCatalog.TkbAim120;

    // ── the scene ───────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A scene of jets in the air over open ground (the host's full TKB, the body / projectile / effect layers) — the frame rails of AC-15.</summary>
    internal sealed class JetScene : IDisposable
    {
        public readonly EntityRepository World; public readonly TkbDatabase Tkb;
        public readonly UiFrameSession Frame; public readonly EntityBodyLayer3D Bodies; public readonly Hrot.UI.Common.Effects.EffectLayer Effects;
        public readonly Hrot.UI.Common.Effects.ProjectileLayer Projectiles; public readonly MapCanvas Canvas; public readonly MapViewSwitch Switch;
        public readonly Vector2 Spot; public readonly float Ground; public double Now = 50.0;

        public JetScene()
        {
            var terrain = Map3DTests.TestTown();
            World = Map3DTests.WorldWith(terrain);
            HrotSharedComponentRegistry.RegisterAll(World);
            PresentationComponentRegistry.RegisterEffects(World);
            World.RegisterComponent<SimTransform>(); World.RegisterComponent<TkbIdentity>(); World.RegisterComponent<SimVelocity>(); World.RegisterComponent<FighterPilotState>();
            Tkb = HrotEnvironment.CreateTkb();
            World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Tkb);
            World.SetSingletonUnmanaged(new GlobalTime { TotalTime = Now });
            var query = WorldQuery.Of(World)!;
            Spot = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
            Ground = query.GroundHeightAt(Spot.X, Spot.Y);
            Frame = UiFrameHarness.Begin(1280, 800);
            LitShader.Reset();
            Canvas = new MapCanvas(input: null);
            var two = new MapCamera();
            Canvas.Camera = two;
            var poses = new TurretPoseSource();
            Bodies = new EntityBodyLayer3D(() => World, () => Tkb, (w, e) => poses.PoseOf(w, e));
            Effects = new Hrot.UI.Common.Effects.EffectLayer(() => World, () => Tkb, Bodies) { GroundHeight = query.GroundHeightAt };
            Projectiles = new Hrot.UI.Common.Effects.ProjectileLayer(() => World, () => Tkb) { GroundHeight = query.GroundHeightAt };
            Switch = new MapViewSwitch(Canvas, two);
            Switch.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
            Canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(World)));
            Canvas.AddLayer(Bodies);
            Canvas.AddLayer(Projectiles);
            Canvas.AddLayer(Effects);
            Switch.Set(true, animate: false);
        }

        /// <summary>A jet of <paramref name="type"/> at (<paramref name="dx"/>, 0, <paramref name="height"/>) from the spot, nose along +x.</summary>
        public Entity Jet(long type, float dx, float height, float dy = 0f)
        {
            var e = World.CreateEntity();
            World.AddComponent(e, new SimTransform { Position = new Vector3(Spot.X + dx, Spot.Y + dy, Ground + height), Rotation = Quaternion.Identity });
            World.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            return e;
        }

        public void Mirror(Entity e, int ir, int radar)
        {
            if (!World.IsComponentTypeRegistered<WeaponAmmo>()) World.RegisterComponent<WeaponAmmo>();
            var a = MirrorOf(Tkb, World.GetComponentRO<TkbIdentity>(e).TkbType, ir, radar);
            if (World.HasComponent<WeaponAmmo>(e)) World.SetComponent(e, a); else World.AddComponent(e, a);
        }

        public void Look(Vector3 at, float dist, float yaw, float pitch) => Switch.Camera3D.Pose = new CameraPose(at, dist, yaw, pitch);
        public Vector3 Where(Entity e) => World.GetComponentRO<SimTransform>(e).Position;
        public void Render() { Frame.Step(Canvas.Draw); Frame.Step(Canvas.Draw); }
        public string Shot(string dir, string name) { Render(); var path = Path.Combine(dir, name); Frame.Screenshot(path); return path; }
        public void Dispose() => Frame.Dispose();
    }

    /// <summary>The index of the mount of <paramref name="type"/> that <paramref name="pick"/> chooses — rails never assume the suite's order.</summary>
    internal static int MountIndex(TkbDatabase db, long type, Func<WeaponMountDto, bool> pick)
    {
        var mounts = db.GetByType(type).GetDescriptor<WeaponSuiteDto>()!.Mounts;
        for (int i = 0; i < mounts.Count; i++) if (pick(mounts[i])) return i;
        throw new InvalidOperationException($"type {type} has no such mount");
    }

    internal static bool IsGun(WeaponMountDto m) => m.Category == WeaponCategory.Autocannon;
    internal static bool IsIrMount(WeaponMountDto m) => JetStores.IsIr(m.AmmoGuid);
    internal static bool IsRadarMount(WeaponMountDto m) => JetStores.IsRadar(m.AmmoGuid);

    /// <summary>The replicated ammunition mirror of a jet whose IR pair holds <paramref name="ir"/> rounds and radar pair <paramref name="radar"/>, in the REAL mount order (gun and dispenser full).</summary>
    internal static WeaponAmmo MirrorOf(TkbDatabase db, long type, int ir, int radar)
    {
        var mounts = db.GetByType(type).GetDescriptor<WeaponSuiteDto>()!.Mounts;
        var a = new WeaponAmmo { Count = (byte)Math.Min(mounts.Count, WeaponAmmo.MaxMounts) };
        for (int i = 0; i < a.Count; i++)
        {
            int rounds = IsIrMount(mounts[i]) ? ir : IsRadarMount(mounts[i]) ? radar : mounts[i].InitialAmmunition, max = IsIrMount(mounts[i]) || IsRadarMount(mounts[i]) ? 2 : mounts[i].InitialAmmunition;
            switch (i) { case 0: a.Ammo0 = rounds; a.Max0 = max; break; case 1: a.Ammo1 = rounds; a.Max1 = max; break; case 2: a.Ammo2 = rounds; a.Max2 = max; break; default: a.Ammo3 = rounds; a.Max3 = max; break; }
        }
        return a;
    }

    private static DISEntityType Dis(string text) { Assert.True(DISEntityType.TryParse(text, out var t)); return t; }

    internal static string ShotsDir()
    {
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        return shots;
    }

    // ── step 2: the body ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMig_HasItsOwnFamily_AndAKitThatIsNotTheF16_TwoFinsCantedOutward_TwoNozzles_IntakesUnderTheWings()
    {
        Assert.Same(ShapeKits.TwinEngineJet, ShapeKits.For(VisualFamily.TwinEngineJet));
        Assert.Equal(new Vector3(17.3f, 11.4f, 4.7f), ShapeKits.DefaultSize(VisualFamily.TwinEngineJet));
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Jet), EntityBodyLayer3D.DefaultColour(VisualFamily.TwinEngineJet));
        var fins = ShapeKits.TwinEngineJet.Parts.Where(p => p.Shape == PartShape.Box && p.Extent.Z > 0.3f && p.Extent.Y < 0.02f).ToArray();
        Assert.Equal(2, fins.Length);                                           // TWO fins (the F-16 has one)
        Assert.Single(ShapeKits.Jet.Parts, p => p.Shape == PartShape.Box && p.Extent.Z > 0.3f && p.Extent.Y < 0.02f);
        Assert.True(fins[0].Centre.Y > 0 && fins[1].Centre.Y < 0 && fins[0].Centre.Y == -fins[1].Centre.Y, "one on each side");
        // canted OUTWARD: the top of the left fin (y > 0) is further left than its foot, the right one's further right (the roll's sign, through the real transform)
        foreach (var fin in fins)
        {
            var m = fin.BodyTransform(ShapeKits.DefaultSize(VisualFamily.TwinEngineJet));
            float foot = Vector3.Transform(new Vector3(0f, 0f, -0.5f), m).Y, top = Vector3.Transform(new Vector3(0f, 0f, 0.5f), m).Y;
            Assert.True(MathF.Sign(fin.Centre.Y) * (top - foot) > 0.15f, $"fin at y {fin.Centre.Y}: foot {foot}, top {top} — leaning out");
        }
        Assert.Equal(2, ShapeKits.TwinEngineJet.Parts.Count(p => p.Shape == PartShape.Cylinder && p.Colour == ColourRole.Dark));   // TWO nozzles (the F-16's one is the only dark cylinder)
        Assert.Equal(2, ShapeKits.TwinEngineJet.Parts.Count(p => p.Shape == PartShape.Box && p.Colour == ColourRole.Dark && p.Role == PartRole.Hull));   // two intakes
        var intakes = ShapeKits.TwinEngineJet.Parts.Where(p => p.Shape == PartShape.Box && p.Colour == ColourRole.Dark && p.Role == PartRole.Hull).ToArray();
        var wing = ShapeKits.TwinEngineJet.Parts.First(p => p.Shape == PartShape.Box && p.YawDegrees > 20f && p.Centre.Y > 0.2f);
        Assert.All(intakes, i => Assert.True(i.Centre.Z < wing.Centre.Z, "under the wings"));
    }

    /// <summary>The contract's class name maps to the family, and the REAL type 409 resolves to it (and is not the F-16's <c>Jet</c>).</summary>
    [Fact]
    public void TheClass_TwinEngineFighter_MapsToTheFamily_AndTheRealType409IsTheMig()
    {
        Assert.Equal(VisualFamily.TwinEngineJet, VisualFamilies.FamilyOfClass["TwinEngineFighter"]);
        Assert.NotNull(EntityClassTable.Default.Find("TwinEngineFighter"));
        var tkb = HrotEnvironment.CreateTkb();
        Assert.Equal(VisualFamily.TwinEngineJet, VisualFamilies.Of(tkb.GetByType(Mig)));
        Assert.Equal(VisualFamily.Jet, VisualFamilies.Of(tkb.GetByType(TkbEntityTypes.Jet_F16)));
        var look = EntityBodyLayer3D.Resolve(tkb.GetByType(Mig));
        Assert.Equal(VisualFamily.TwinEngineJet, look.Family);
        Assert.InRange(look.Size.X, 17.2f, 17.4f);   // the TKB's own box (17.32 m) wins; the kit's fractions scale to it
    }

    [SkippableFact]
    public void TheMigIsDrawnNextToTheF16_OnTheRealFrame_ADifferentPicture_TwoFins_AndItsPartsAreCounted()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new JetScene();
        var f16 = scene.Jet(TkbEntityTypes.Jet_F16, 0f, 120f, 0f);
        var at = scene.Where(f16);   // the MiG joins 22 m to the left; for the single-jet views the F-16 is moved out of the line of sight
        // only the F-16
        scene.Look(at, 34f, 0.45f, -0.12f); scene.Render();
        int p1 = scene.Bodies.PartsDrawn;
        var alone = ScreenOf(scene);
        scene.Shot(shots, "aircombat-f16.png");
        // the MiG beside it (a bit further back, the same height)
        var mig = scene.Jet(Mig, 0f, 120f, 22f);
        scene.Look(at + new Vector3(0f, 11f, 0f), 46f, 0.45f, -0.12f); scene.Render();
        int p2 = scene.Bodies.PartsDrawn;
        scene.Shot(shots, "aircombat-f16-and-mig.png");
        int nonGear = ShapeKits.TwinEngineJet.Parts.Count(p => p.Role != PartRole.Gear), all = ShapeKits.TwinEngineJet.Parts.Length;
        const int Stores = 4 + 4 * 3;   // four pylons, four missiles of three parts — the stand-in's suite carries both pairs, no mirror yet ⇒ full
        Assert.InRange(p2 - p1, nonGear + Stores, all + Stores);          // ⭐ the MiG's own kit, not the F-16's (15 vs 19 parts)
        Assert.True(Diff(alone, ScreenOf(scene)) > 3000, "a second jet changes the picture");
        Assert.True(mig.Index != f16.Index);
        // ⭐ R-4146 — the mockup's three views of the MiG, drawn on the real frame (side · top · rear), for the side-by-side
        var m = scene.Where(mig);
        scene.World.SetComponent(f16, new SimTransform { Position = at + new Vector3(0f, 120f, 0f), Rotation = Quaternion.Identity });
        scene.Look(m, 30f, MathF.PI * 0.5f, 0.0f); scene.Shot(shots, "aircombat-mig-side.png");
        scene.Look(m, 30f, MathF.PI * 0.5f, -1.45f); scene.Shot(shots, "aircombat-mig-top.png");
        scene.Look(m, 30f, 0f, 0.0f); scene.Shot(shots, "aircombat-mig-rear.png");
        scene.World.SetComponent(f16, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        scene.World.SetComponent(mig, new SimTransform { Position = at + new Vector3(0f, 120f, 0f), Rotation = Quaternion.Identity });
        scene.Look(at, 30f, MathF.PI * 0.5f, 0.0f); scene.Shot(shots, "aircombat-f16-side.png");
    }

    // ── step 3: the missiles on the rails ───────────────────────────────────────────────────────────────────────────────────────

    private static EntityRepository StoresWorld(out Entity mig, out Entity f16, out TkbDatabase tkb)
    {
        var world = new EntityRepository();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<TkbIdentity>(); world.RegisterComponent<WeaponAmmo>();
        tkb = HrotEnvironment.CreateTkb();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        mig = world.CreateEntity(); world.AddComponent(mig, new SimTransform { Rotation = Quaternion.Identity }); world.AddComponent(mig, new TkbIdentity { TkbType = (TkbTypeId)Mig });
        f16 = world.CreateEntity(); world.AddComponent(f16, new SimTransform { Rotation = Quaternion.Identity }); world.AddComponent(f16, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Jet_F16 });
        return world;
    }

    /// <summary>⭐ AC-15 step 3 — the missiles left are the replicated <c>WeaponAmmo</c> of the mounts that hold a missile: 4 → 2 after two shots (one IR, one radar), the full load where nothing is mirrored yet, nothing for a type with no missile mount (the A-10); BOTH fighters carry their four; 0 B.</summary>
    [Fact]
    public void TheMissilesLeft_FollowTheRoundsOfTheirMounts_FourThenTwoThenNone_FullWithoutAMirror_NothingWithoutAMissileMount_AndAllocateNothing()
    {
        using var world = StoresWorld(out var mig, out var f16, out var tkb);
        foreach (var (jet, type) in new[] { (mig, Mig), (f16, TkbEntityTypes.Jet_F16) })   // ⭐ BOTH jets carry the real suite now
        {
            Assert.True(JetStores.Left(world, jet, tkb, out int ir, out int radar), $"type {type}"); Assert.Equal((2, 2), (ir, radar));   // no mirror yet: the suite's full load
            world.AddComponent(jet, MirrorOf(tkb, type, 2, 2));
            JetStores.Left(world, jet, tkb, out ir, out radar); Assert.Equal(4, ir + radar);
            world.SetComponent(jet, MirrorOf(tkb, type, 1, 1));   // two shots: one IR, one radar
            JetStores.Left(world, jet, tkb, out ir, out radar); Assert.Equal((1, 1), (ir, radar));
            world.SetComponent(jet, MirrorOf(tkb, type, 0, 0));
            Assert.True(JetStores.Left(world, jet, tkb, out ir, out radar)); Assert.Equal((0, 0), (ir, radar));       // winchester: the rails stay, empty
            world.SetComponent(jet, MirrorOf(tkb, type, 2, 2));
        }
        // a type with no missile mount (the A-10) carries none
        var a10 = world.CreateEntity(); world.AddComponent(a10, new SimTransform { Rotation = Quaternion.Identity }); world.AddComponent(a10, new TkbIdentity { TkbType = (TkbTypeId)TkbEntityTypes.Jet_AttackA10 });
        Assert.False(JetStores.Left(world, a10, tkb, out _, out _));
        world.SetComponent(mig, MirrorOf(tkb, Mig, 0, 0));
        long before = GC.GetAllocatedBytesForCurrentThread(); int sum = 0;
        for (int i = 0; i < 2000; i++) { JetStores.Left(world, mig, tkb, out int a, out int b); sum += a + b; }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
        Assert.Equal(0, sum);
    }

    [Fact]
    public void TheRails_AreTheMockupsNumbers_IrOutboardRadarInboard_TheLeftRailFirst_PylonsAlways()
    {
        foreach (var family in new[] { VisualFamily.Jet, VisualFamily.TwinEngineJet })
        {
            var size = ShapeKits.DefaultSize(family); var body = ShapeKits.For(family).Parts;
            var pylons = JetStores.Pylons(family);
            Assert.Equal(4, pylons.Length);
            Assert.Equal(body.Length + 4, JetStores.Load(family, size, body, 0, 0).Length);                 // empty rails: pylons only
            Assert.Equal(body.Length + 4 + 6, JetStores.Load(family, size, body, 2, 0).Length);            // 2 missiles × 3 parts
            Assert.Equal(body.Length + 4 + 12, JetStores.Load(family, size, body, 2, 2).Length);
            var ir = JetStores.Missile(family, 0, size)[0]; var rd = JetStores.Missile(family, 2, size)[0];
            Assert.True(MathF.Abs(ir.Centre.Y) > MathF.Abs(rd.Centre.Y), $"{family}: IR OUTBOARD of radar");
            Assert.True(ir.Centre.Y > 0 && JetStores.Missile(family, 1, size)[0].Centre.Y < 0, "slot 0 left, slot 1 right");
            Assert.InRange(ir.Extent.X * size.X, 2.9f, 3.1f); Assert.InRange(rd.Extent.X * size.X, 3.6f, 3.8f);   // R-73 3.0 m · R-77 3.7 m
        }
    }

    [SkippableFact]
    public void TheRealFrame_ShowsTheMissilesOnTheRails_FourTwoNone_ASixPartDifferenceEachWay_ForBothJets()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        foreach (var (type, tag) in new[] { (Mig, "mig"), (TkbEntityTypes.Jet_F16, "f16") })
        {
            using var scene = new JetScene();
            var jet = scene.Jet(type, 0f, 90f); var at = scene.Where(jet);
            int Parts(int ir, int rd, string? shot = null)
            {
                scene.Mirror(jet, ir, rd);
                scene.Look(at + new Vector3(0f, 0f, -0.5f), 21f, MathF.PI * 0.5f + 0.8f, 0.22f); scene.Render();   // from BELOW the wing: that is where the rails are
                if (shot != null) scene.Shot(shots, shot);
                return scene.Bodies.PartsDrawn;
            }
            int p4 = Parts(2, 2, $"aircombat-{tag}-stores-4.png"), p2 = Parts(1, 1, $"aircombat-{tag}-stores-2.png"), p0 = Parts(0, 0, $"aircombat-{tag}-stores-0.png");
            Assert.Equal(6, p4 - p2);   // ⭐ two missiles gone (3 parts each) — one per shot
            Assert.Equal(6, p2 - p0);
            Assert.Equal(p4, Parts(2, 2));   // reloaded
            var full = ScreenOf(scene); Parts(1, 1); var two = ScreenOf(scene);
            Assert.True(Diff(full, two) > 150, $"{tag} 4 → 2: {Diff(full, two)} px");
        }
    }

    // ── step 4: the missile in flight, the launch ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBurn_IsTheMotorsSeconds_TheTrailIsFullWhileItBurns_FadesAfterIt_AndIsNoneWhileTheMissileCoasts()
    {
        var tkb = HrotEnvironment.CreateTkb();
        Assert.Equal(5f, ProjectileLayer.BurnOf(tkb, MunitionTkbCatalog.TkbAim9x)); Assert.Equal(5f, ProjectileLayer.BurnOf(tkb, MunitionTkbCatalog.TkbR73));        // IR 5 s: the round's own MissileDto.BurnSeconds
        Assert.Equal(8f, ProjectileLayer.BurnOf(tkb, MunitionTkbCatalog.TkbAim120)); Assert.Equal(8f, ProjectileLayer.BurnOf(tkb, MunitionTkbCatalog.TkbR77));      // radar 8 s
        Assert.Equal(0f, ProjectileLayer.BurnOf(tkb, MunitionTkbCatalog.TkbM61Hei)); Assert.Equal(0f, ProjectileLayer.BurnOf(null, MunitionTkbCatalog.TkbAim9x));   // a gun round, no TKB: no limit
        Assert.Equal(1f, ProjectileLayer.TrailFactor(5f, 0.0));
        Assert.Equal(1f, ProjectileLayer.TrailFactor(5f, 5.0));
        Assert.InRange(ProjectileLayer.TrailFactor(5f, 5.4), 0.45f, 0.55f);
        Assert.Equal(0f, ProjectileLayer.TrailFactor(5f, 6.0));
        Assert.Equal(1f, ProjectileLayer.TrailFactor(0f, 99.0));              // no motor data: always (as before)
        Assert.Equal(1f, ProjectileLayer.TrailFactor(5f, double.NaN));         // a round with no path yet
    }

    [Fact]
    public void TheAirMissiles_GetTheBoosterFlash_FromTheCatalog_AndAJetsShotIsALaunch()
    {
        var db = HrotEnvironment.CreateTkb();
        var set = EffectTkbCatalog.EffectsFor(db, MunitionTkbCatalog.TkbAim9x, 0f);
        Assert.Equal(EffectTkbCatalog.MissileLaunchFlash, set.MuzzleFlash);
        using var world = StoresWorld(out var mig, out var f16, out _);
        var mount = db.GetByType(Mig).GetDescriptor<WeaponSuiteDto>()!.Mounts[MountIndex(db, Mig, IsIrMount)];
        Assert.Equal(FiringBlast.Launch, WeaponKits.BlastOf(world, db, mig, mount));          // the puff left at the rail
        foreach (var (jet, type) in new[] { (mig, Mig), (f16, TkbEntityTypes.Jet_F16) })   // both fighters, by what each mount holds
        {
            var suite = db.GetByType(type).GetDescriptor<WeaponSuiteDto>()!;
            Assert.Equal(FiringBlast.Launch, WeaponKits.BlastOf(world, db, jet, suite.Mounts[MountIndex(db, type, IsIrMount)]));
            Assert.Equal(FiringBlast.Launch, WeaponKits.BlastOf(world, db, jet, suite.Mounts[MountIndex(db, type, IsRadarMount)]));
            Assert.Equal(FiringBlast.None, WeaponKits.BlastOf(world, db, jet, suite.Mounts[MountIndex(db, type, IsGun)]));   // the cannon: none, as before
        }
    }

    [SkippableFact]
    public void TheTrail_IsDrawnWhileTheMotorBurns_AndNotWhileTheMissileCoasts_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new JetScene();
        scene.World.RegisterComponent<BallisticProjectile>();
        var at = new Vector3(scene.Spot.X, scene.Spot.Y, scene.Ground + 120f);
        var round = scene.World.CreateEntity();
        scene.World.AddComponent(round, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        scene.World.AddComponent(round, new SimVelocity { Linear = new Vector3(300f, 0f, 0f) });
        scene.World.AddComponent(round, new BallisticProjectile { Ammo = MunitionTkbCatalog.TkbAim9x, PreviousPosition = at - new Vector3(3f, 0f, 0f), Muzzle = at - new Vector3(45f, 0f, 0f) });
        void Clock(double t) { scene.Now = t; scene.World.SetSingletonUnmanaged(new GlobalTime { TotalTime = t }); }
        scene.Look(at - new Vector3(20f, 0f, 0f), 60f, MathF.PI * 0.5f + 0.2f, -0.1f);
        Clock(50.0); scene.Render();                                           // the first frame it is seen: its launch
        var path = scene.Projectiles.PathOf(round)!;
        Assert.Equal(50.0, path.FirstSeen);
        Clock(53.0); var burning = ScreenOf(scene); scene.Shot(shots, "aircombat-aam-burning.png");
        Assert.Equal(1f, ProjectileLayer.TrailFactor(ProjectileLayer.BurnOf(scene.Tkb, MunitionTkbCatalog.TkbAim9x), 53.0 - path.FirstSeen));
        Clock(56.5); var coasting = ScreenOf(scene); scene.Shot(shots, "aircombat-aam-coasting.png");
        Assert.Equal(0f, ProjectileLayer.TrailFactor(ProjectileLayer.BurnOf(scene.Tkb, MunitionTkbCatalog.TkbAim9x), 56.5 - path.FirstSeen));
        Assert.True(Diff(burning, coasting) > 400, $"the smoke is there during the burn and gone while it coasts: {Diff(burning, coasting)} px");
        Assert.Single(scene.Projectiles.Resolve());                            // the missile itself is drawn in both
    }

    [SkippableFact]
    public void TheLaunch_ShowsItsFlashAndSmoke_OnTheFrameOfTheShot_AtTheRail()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new JetScene();
        scene.World.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
        var mig = scene.Jet(Mig, 0f, 90f); var at = scene.Where(mig);
        scene.Mirror(mig, 2, 2);
        scene.Look(at, 26f, MathF.PI * 0.5f + 0.9f, -0.35f); scene.Render();
        var before = ScreenOf(scene);
        Assert.DoesNotContain(scene.Effects.Resolve(), r => r.Look.Kind is EffectKind.MuzzleFlash or EffectKind.Smoke);
        var rail = at + new Vector3(-1.4f, 4.1f, -0.9f);                      // under the left wing, the outboard IR rail (the shot's origin)
        scene.World.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
        {
            Shooter = mig, WeaponIndex = MountIndex(scene.Tkb, Mig, IsIrMount), AmmoGuid = MunitionTkbCatalog.TkbAim9x, Quantity = 1, FiredAt = (SimInstant)scene.Now, Origin = rail, Velocity = new Vector3(250f, 0f, 0f),
        });
        scene.World.Bus.SwapBuffers();
        new Hrot.UI.Common.Effects.EffectSpawnSystem().Execute(scene.World, 0.016f);
        ((Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)scene.World).GetCommandBuffer()).Playback(scene.World);
        scene.Mirror(mig, 1, 2);                                              // the round left the rail in this very frame
        scene.Shot(shots, "aircombat-launch.png");
        var after = ScreenOf(scene);
        var effects = scene.Effects.Resolve();
        var flash = Assert.Single(effects, r => r.Look.Kind == EffectKind.MuzzleFlash);
        var smoke = Assert.Single(effects, r => r.Look.Kind == EffectKind.Smoke);
        Assert.Equal(EffectTkbCatalog.VisualOf(scene.Tkb, EffectTkbCatalog.MissileLaunchFlash)!.ColorHex, flash.Look.ColorHex);   // the booster's flash, not a calibre default
        Assert.True(Vector3.Distance(smoke.At, rail) < 0.5f, $"the smoke is left at the rail: {smoke.At} vs {rail}");
        Assert.True(Diff(before, after) > 1200, $"the frame of the shot differs from the frame before it: {Diff(before, after)} px");
        Assert.Equal(1, JetStoresLeftIr(scene, mig));
    }

    private static int JetStoresLeftIr(JetScene scene, Entity jet) { JetStores.Left(scene.World, jet, scene.Tkb, out int ir, out _); return ir; }

    // ── step 5: the afterburner, the wingtip vortex, the dead jet ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheFlamesAndTheVortices_AreBuiltOnTheNozzlesAndTheWingtips_OnePerNozzle_OnePerTip_TheStateDecidesWhichKitIsDrawn()
    {
        foreach (var (family, nozzles) in new[] { (VisualFamily.Jet, 1), (VisualFamily.TwinEngineJet, 2) })
        {
            var size = ShapeKits.DefaultSize(family); var body = ShapeKits.For(family).Parts;
            var flames = JetStores.Flames(family, size);
            Assert.Equal(nozzles, flames.Length);
            Assert.All(flames, f => { Assert.Equal(PartShape.Cone, f.Shape); Assert.Equal(180f, f.PitchDegrees); Assert.True(f.Centre.X < -0.5f, "behind the tail"); });
            var nozzle = body.Where(p => p.Shape == PartShape.Cylinder && p.Colour == ColourRole.Dark).ToArray();
            Assert.Equal(nozzles, nozzle.Length);
            Assert.All(flames, f => Assert.Contains(nozzle, n => MathF.Abs(n.Centre.Y - f.Centre.Y) < 1e-4f));   // each flame sits behind a nozzle
            var v = JetStores.Vortices(family, size);
            Assert.Equal(2, v.Length);
            Assert.Equal(v[0].Centre.Y, -v[1].Centre.Y);
            Assert.InRange(v[0].Extent.X * size.X, 39f, 41f);                  // 40 m back
            Assert.Equal(body.Length + flames.Length, JetStores.Load(family, size, body, -1, 0, burner: true).Length);
            Assert.Equal(body.Length + 2, JetStores.Load(family, size, body, -1, 0, vortex: true).Length);
            Assert.Equal(body.Length + flames.Length + 2, JetStores.Load(family, size, body, -1, 0, true, true).Length);
        }
        Assert.Equal(0.9f, JetStores.BurnerFrom); Assert.Equal(6f, JetStores.VortexFromG);
    }

    [SkippableFact]
    public void TheAfterburner_LightsAtFullThrottle_TheVortexAboveSixG_BothFromTheFighterState_NeitherOnAWreck_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new JetScene();
        var mig = scene.Jet(Mig, 0f, 120f); var f16 = scene.Jet(TkbEntityTypes.Jet_F16, 0f, 120f, 40f);
        void Set(float throttle, float g)
        {
            foreach (var j in new[] { mig, f16 })
            {
                var s = new FighterPilotState { Throttle = throttle, GLoad = g };
                if (scene.World.HasComponent<FighterPilotState>(j)) scene.World.SetComponent(j, s); else scene.World.AddComponent(j, s);
            }
        }
        Set(0.5f, 1f);
        
        var at = scene.Where(mig);
        int Parts(string? shot = null) { scene.Look(at + new Vector3(-6f, 20f, 0f), 62f, MathF.PI * 0.5f + 0.6f, 0.05f); scene.Render(); if (shot != null) scene.Shot(shots, shot); return scene.Bodies.PartsDrawn; }
        int cruise = Parts("aircombat-cruise.png"); var cruiseShot = ScreenOf(scene);
        Set(0.89f, 1f); Assert.Equal(cruise, Parts());                                        // just under the threshold: nothing
        Set(1.0f, 1f); int burner = Parts("aircombat-afterburner.png"); var burnerShot = ScreenOf(scene);
        Assert.Equal(2 + 1, burner - cruise);                                                      // ⭐ the MiG's TWO flames + the F-16's ONE
        Set(0.5f, 5.9f); Assert.Equal(cruise, Parts());                                       // just under 6 g: nothing
        Set(0.5f, 7.2f); int vortex = Parts("aircombat-vortex.png"); var vortexShot = ScreenOf(scene);
        Assert.Equal(2 * 2, vortex - cruise);                                                      // two wingtip trails on each of the two jets
        Assert.True(Diff(cruiseShot, burnerShot) > 200 && Diff(cruiseShot, vortexShot) > 400, $"burner {Diff(cruiseShot, burnerShot)} px, vortex {Diff(cruiseShot, vortexShot)} px");
        Set(1.0f, 7.2f); int both = Parts("aircombat-burner-and-vortex.png");
        Assert.Equal(cruise + 3 + 4, both);
        scene.World.RemoveComponent<FighterPilotState>(mig); scene.World.RemoveComponent<FighterPilotState>(f16); Assert.Equal(cruise, Parts());                      // no state source: the plain jets
    }

    /// <summary>⭐ The dead jet FALLS in flames: the W3 wreck fire and smoke (<c>WreckSpawnSystem</c>, the crossing health ≤ 0) are anchored to its BODY, so they follow it down; the jet itself is the black wreck and carries no afterburner.</summary>
    [SkippableFact]
    public void ADeadJet_FallsWithFireAndSmokeOnIt_TheyFollowItDown_AndItsAfterburnerIsOut_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new JetScene();
        scene.World.RegisterComponent<Health>(); scene.World.RegisterComponent<Fdp.Toolkit.Time.Lifespan>();
        var jet = scene.Jet(TkbEntityTypes.Jet_F16, 0f, 400f);
        scene.World.AddComponent(jet, new Health { Current = 400f, Max = 400f });
        scene.World.AddComponent(jet, new FighterPilotState { Throttle = 1f, GLoad = 2f });                                   // its engine was at full throttle when it was hit
        var wrecks = new Hrot.UI.Common.Effects.WreckSpawnSystem();
        void Tick() { var cmd = ((Fdp.ModuleHost.Abstractions.ISimulationView)scene.World).GetCommandBuffer(); wrecks.Execute(scene.World, 0.016f); ((Fdp.Core.EntityCommandBuffer)cmd).Playback(scene.World); }
        Tick();                                                                                    // alive: the crossing is not yet
        var at = scene.Where(jet);
        scene.Look(at, 70f, MathF.PI * 0.5f + 0.6f, 0.0f); scene.Render(); int living = scene.Bodies.PartsDrawn;
        scene.World.SetComponent(jet, new Health { Current = 0f, Max = 400f });
        Tick();
        var fx = scene.Effects.Resolve();
        Assert.Contains(fx, r => r.Look.Kind == EffectKind.Fire); Assert.Contains(fx, r => r.Look.Kind == EffectKind.Smoke);
        var fire = fx.First(r => r.Look.Kind == EffectKind.Fire).At;
        Assert.True(Vector3.Distance(fire, at) < 6f, $"the fire is ON the jet: {fire} vs {at}");
        scene.Render(); Assert.Equal(living - 1 - 16, scene.Bodies.PartsDrawn);                      // the wreck: the afterburner's flame is out (1) and its missiles and rails are gone (4 pylons + 4 × 3)
        // it falls 150 m: the fire went with it
        scene.World.SetComponent(jet, new SimTransform { Position = at - new Vector3(60f, 0f, 150f), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f) });
        var down = scene.Effects.Resolve().First(r => r.Look.Kind == EffectKind.Fire).At;
        Assert.True(down.Z < fire.Z - 140f && Vector3.Distance(down, scene.Where(jet)) < 6f, $"the fire follows the falling jet: {down}");
        scene.Look(scene.Where(jet), 70f, MathF.PI * 0.5f + 0.6f, 0.0f); scene.Shot(shots, "aircombat-dead-jet-falling.png");
    }

    // ── pixels ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    internal sealed record Pixels(Color[] Colours, int W, int H);

    internal static Pixels ScreenOf(JetScene scene) => ScreenOf(scene.Frame, scene.Render);

    private static Pixels ScreenOf(UiFrameSession frame, Action render)
    {
        render();
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

    internal static int Diff(Pixels a, Pixels b)
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
