using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Radar;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4647</c> (AC-15 step 7; docs/designs/air-attack/DESIGN_Air_Combat.md §4d; mockup <c>air-combat-hud.svg</c>) — the FIGHTER HUD of a possessed F-16 / MiG: the gun pipper at the led point (<see cref="TargetLead.Point"/>), the missile cue
/// (SEARCH / IN ZONE / LOCK), the radar warning (the existing <see cref="RadarWarning"/> / <see cref="MissileWarning"/>), the g. The MODEL rails run anywhere; the FRAME rail measures the mockup's anchors on the real frame (R-4146) and SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~FighterHudRail</c> (PNGs to <c>HROT_RAIL_SHOTS</c> as <c>aircombat-hud-*.png</c>). 🔴 Red-proof: the pipper's lead removed (<c>PipperAt = target</c>) fails the lead rail.
/// 🔧 Stand-ins: <see cref="FighterStateStandIn"/> (g) and the zone as a range band (<see cref="FighterHud"/>); the jet is the MiG stand-in of <see cref="AirCombatLooksRail"/>.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class FighterHudRail
{
    private const float Now = 100f;

    private sealed class Scene : IDisposable
    {
        public readonly EntityRepository World; public readonly Fdp.Toolkit.Tkb.TkbDatabase Tkb; public readonly Entity Me;
        public Scene()
        {
            World = new EntityRepository();
            World.RegisterComponent<SimTransform>(); World.RegisterComponent<TkbIdentity>(); World.RegisterComponent<SimVelocity>(); World.RegisterComponent<EntityInfo>();
            World.RegisterComponent<WeaponAmmo>(); World.RegisterComponent<MissileWarning>(); World.RegisterComponent<RadarWarning>(); World.RegisterComponent<Health>();
            Tkb = HrotEnvironment.CreateTkb(); AirCombatLooksRail.RegisterMigStandIn(Tkb);
            World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Tkb);
            Me = Jet(AirCombatLooksRail.MigStandIn, Vector3.Zero, Vector3.Zero, ForceId.Friend);
        }

        public Entity Jet(long type, Vector3 at, Vector3 velocity, ForceId side)
        {
            var e = World.CreateEntity();
            World.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            World.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            World.AddComponent(e, new SimVelocity { Linear = velocity });
            World.AddComponent(e, new EntityInfo { ForceId = side });
            World.AddComponent(e, new Health { Current = 100f, Max = 100f });
            return e;
        }

        public FighterHudModel Build(FighterHud hud, int mount, Vector3? look = null) => hud.Build(World, Me, look ?? Vector3.UnitX, mount, Tkb, Now);
        public void Dispose() { FighterStateStandIn.Source = null; World.Dispose(); }
    }

    private const int Ir = 0, Radar = 1, Gun = 2;   // the stand-in's mounts

    [Fact]
    public void TheGunPipper_IsTheLedPoint_OnlyInsideFiveHundredMetres_NeverOnAFriend_AFallenJet_OrWithAMissileSelected()
    {
        using var s = new Scene(); var hud = new FighterHud();
        var foe = s.Jet(TkbEntityTypes.Jet_F16, new Vector3(400f, 0f, 0f), new Vector3(0f, 120f, 0f), ForceId.Hostile);   // crossing to the left
        var m = s.Build(hud, Gun);
        Assert.True(m.Active && m.GunSelected && m.HasTarget && m.HasPipper);
        var led = TargetLead.Point(Vector3.Zero, Vector3.Zero, 1050f, new Vector3(400f, 0f, 0f), new Vector3(0f, 120f, 0f));
        Assert.Equal(led, m.PipperAt);                                         // ⭐ the ONE predictor
        Assert.True(m.PipperAt.Y > 20f, $"led ahead of a crosser: {m.PipperAt}");   // 🔴 the lead removed ⇒ PipperAt.Y == 0
        Assert.Equal(MissileCue.None, m.Missile);                              // a gun selected: no missile cue
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(600f, 0f, 0f), Rotation = Quaternion.Identity });
        Assert.False(s.Build(hud, Gun).HasPipper);                             // beyond the gun's reach
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(400f, 0f, 0f), Rotation = Quaternion.Identity });
        s.World.SetComponent(foe, new EntityInfo { ForceId = ForceId.Friend });
        Assert.False(s.Build(hud, Gun).HasTarget);                             // a friend is nobody's target
        s.World.SetComponent(foe, new EntityInfo { ForceId = ForceId.Hostile });
        s.World.SetComponent(foe, new Health { Current = 0f, Max = 100f });
        Assert.False(s.Build(hud, Gun).HasTarget);                             // a fallen jet either
    }

    [Fact]
    public void TheMissileCue_IsSearchWithNothingInTheCone_InZoneInsideTheBandOffTheSeeker_LockOutsideTheBand_LockInZoneInBoth()
    {
        using var s = new Scene(); var hud = new FighterHud();
        Assert.Equal(MissileCue.Search, s.Build(hud, Ir).Missile);             // nobody there
        var foe = s.Jet(TkbEntityTypes.Jet_F16, new Vector3(3000f, 0f, 0f), Vector3.Zero, ForceId.Hostile);
        Assert.Equal(MissileCue.LockInZone, s.Build(hud, Ir).Missile);         // dead ahead, 3 km
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(8000f, 0f, 0f), Rotation = Quaternion.Identity });
        Assert.Equal(MissileCue.Lock, s.Build(hud, Radar).Missile);            // 8 km: the seeker has him, the shot is too far
        float a20 = 20f * MathF.PI / 180f;
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(MathF.Cos(a20), MathF.Sin(a20), 0f) * 3000f, Rotation = Quaternion.Identity });
        Assert.Equal(MissileCue.InZone, s.Build(hud, Ir).Missile);             // 20° off the nose: in range, the seeker does not have him
        float a45 = 45f * MathF.PI / 180f;
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(MathF.Cos(a45), MathF.Sin(a45), 0f) * 3000f, Rotation = Quaternion.Identity });
        var off = s.Build(hud, Ir);
        Assert.False(off.HasTarget); Assert.Equal(MissileCue.Search, off.Missile);   // outside the search cone
        s.World.SetComponent(foe, new SimTransform { Position = new Vector3(300f, 0f, 0f), Rotation = Quaternion.Identity });
        Assert.Equal(MissileCue.Lock, s.Build(hud, Ir).Missile);               // too close for a missile (500 m): locked, not in the zone
    }

    [Fact]
    public void TheRadarWarning_IsATickAtTheEmittersBearingRelativeToTheNose_TheTextOnALaunch_NothingOnceTheMissileIsGone()
    {
        using var s = new Scene(); var hud = new FighterHud();
        Assert.Equal(WarningCue.None, s.Build(hud, Ir).Warning);
        s.World.AddComponent(s.Me, new RadarWarning { Level = RadarWarningLevel.Tracked, BearingDeg = 180f });   // a radar to the south, the nose is east (heading 090)
        var m = s.Build(hud, Ir);
        Assert.Equal(WarningCue.Tracked, m.Warning); Assert.Equal(90f, m.WarningBearingDeg);                      // 180 − 090: to the right
        s.World.AddComponent(s.Me, new MissileWarning { At = (SimInstant)(Now - 0.8), BearingDeg = 270f, Seeker = GuidanceKind.InfraRed, TimeToImpactS = 5f });
        m = s.Build(hud, Ir);
        Assert.Equal(WarningCue.Launch, m.Warning); Assert.Equal(180f, m.WarningBearingDeg); Assert.Equal("LAUNCH IR  4.2 s", m.WarningText);   // 5 s − 0.8 s elapsed
        s.World.SetComponent(s.Me, new MissileWarning { At = (SimInstant)(Now - 0.8), BearingDeg = 270f, Seeker = GuidanceKind.Radar, TimeToImpactS = 5f });
        Assert.Equal("LAUNCH RADAR  4.2 s", s.Build(hud, Ir).WarningText);
        s.World.SetComponent(s.Me, new MissileWarning { At = (SimInstant)(Now - 9.0), BearingDeg = 270f, Seeker = GuidanceKind.Radar, TimeToImpactS = 5f });
        Assert.Equal(WarningCue.Tracked, s.Build(hud, Ir).Warning);            // the missile's time is up: back to the tracked tick
    }

    [Fact]
    public void TheG_IsReadFromTheFighterState_WhiteBelowSix_AmberFromSix_RedWithinOneOfTheLimit_AndNeverADecimalComma()
    {
        using var s = new Scene(); var hud = new FighterHud();
        float g = 3.1f; FighterStateStandIn.Source = (w, e) => (1f, g);
        var m = s.Build(hud, Ir); Assert.Equal(GColour.Normal, m.G); Assert.Equal("G  3.1", m.GText);
        g = 6.8f; m = s.Build(hud, Ir); Assert.Equal(GColour.High, m.G);
        g = 8.7f; m = s.Build(hud, Ir); Assert.Equal(GColour.Limit, m.G);
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            using var s2 = new Scene(); var hud2 = new FighterHud();
            s2.Jet(TkbEntityTypes.Jet_F16, new Vector3(3100f, 0f, 0f), new Vector3(-210f, 0f, 0f), ForceId.Hostile);
            var cz = s2.Build(hud2, Ir);
            Assert.Equal("G  8.7", cz.GText); Assert.Equal("3.1 km  210 m/s", cz.RangeText);   // 🔒 language neutral (R-4221)
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void TheHud_IsOnlyForAFighter_AnF16WithNoMissileMountDrawsNothing_AndASteadyFrameAllocatesNothing()
    {
        using var s = new Scene(); var hud = new FighterHud();
        var f16 = s.Jet(TkbEntityTypes.Jet_F16, new Vector3(0f, 500f, 0f), Vector3.Zero, ForceId.Friend);
        Assert.False(hud.Build(s.World, f16, Vector3.UnitX, 0, s.Tkb, Now).Active);
        Assert.False(FighterHud.Applies(s.World, f16, s.Tkb)); Assert.True(FighterHud.Applies(s.World, s.Me, s.Tkb));
        s.Jet(TkbEntityTypes.Jet_F16, new Vector3(3000f, 0f, 0f), new Vector3(-200f, 0f, 0f), ForceId.Hostile);
        s.World.AddComponent(s.Me, new MissileWarning { At = (SimInstant)(Now - 0.8), BearingDeg = 270f, Seeker = GuidanceKind.InfraRed, TimeToImpactS = 5f });
        FighterStateStandIn.Source = (w, e) => (1f, 7f);
        for (int i = 0; i < 20; i++) s.Build(hud, Ir);
        long before = GC.GetAllocatedBytesForCurrentThread(); int n = 0;
        for (int i = 0; i < 1000; i++) n += s.Build(hud, Ir).HasTarget ? 1 : 0;
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
        Assert.Equal(1000, n);
    }

    // ── the real frame: the mockup's anchors, measured (R-4146) ─────────────────────────────────────────────────────────────────

    private static bool Near(Color c, Color want, int tol) => Math.Abs(c.R - want.R) <= tol && Math.Abs(c.G - want.G) <= tol && Math.Abs(c.B - want.B) <= tol;

    private static int Around(Image im, Vector2 at, int radius, Color want, int tol)
    {
        int n = 0;
        for (int y = (int)at.Y - radius; y <= (int)at.Y + radius; y++)
            for (int x = (int)at.X - radius; x <= (int)at.X + radius; x++)
                if (x >= 0 && y >= 0 && x < im.Width && y < im.Height && Near(Raylib.GetImageColor(im, x, y), want, tol)) n++;
        return n;
    }

    [SkippableFact]
    public void TheRealFrame_ShowsThePipperTheDiamondTheZoneTagTheWarningRingAndTheG_AtTheMockupsAnchors()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = AirCombatLooksRail.ShotsDir();
        using var scene = new AirCombatLooksRail.JetScene();
        var me = scene.Jet(AirCombatLooksRail.MigStandIn, 0f, 300f); var eye = scene.Where(me);
        scene.World.RegisterComponent<EntityInfo>(); scene.World.RegisterComponent<MissileWarning>(); scene.World.RegisterComponent<Health>();
        scene.World.AddComponent(me, new EntityInfo { ForceId = ForceId.Friend }); scene.World.AddComponent(me, new Health { Current = 100f, Max = 100f });
        var foe = scene.Jet(TkbEntityTypes.Jet_F16, 700f, 300f, 25f);
        scene.World.AddComponent(foe, new EntityInfo { ForceId = ForceId.Hostile }); scene.World.AddComponent(foe, new Health { Current = 100f, Max = 100f });
        scene.World.AddComponent(foe, new SimVelocity { Linear = new Vector3(-100f, 60f, 0f) });
        scene.World.AddComponent(me, new SimVelocity { Linear = new Vector3(200f, 0f, 0f) });
        scene.World.AddComponent(me, new MissileWarning { At = (SimInstant)scene.Now, BearingDeg = 200f, Seeker = GuidanceKind.InfraRed, TimeToImpactS = 4.2f });
        FighterStateStandIn.Source = (w, e) => (1f, 6.2f);
        var hud = new FighterHud();
        scene.Look(eye, 1f, MathF.PI * 0.5f - 0.03f, 0.0f);   // the eye on the jet, looking along +x… (Yaw from east: set below)
        scene.Switch.Camera3D.Pose = new Fdp.Toolkit.Vis3D.CameraPose(eye + new Vector3(16f, 0f, 1.5f), 0.01f, 0f, 0f);
        scene.Render();
        var cam = scene.Switch.Camera3D;
        FighterHudModel Draw(int mount, string name)
        {
            var model = hud.Build(scene.World, me, cam.Forward, mount, scene.Tkb, scene.Now);
            scene.Frame.Step(() => { scene.Canvas.Draw(); FighterHud.Draw(model, cam, HudText.Font()); });
            scene.Frame.Screenshot(System.IO.Path.Combine(shots, name));
            return model;
        }
        var missile = Draw(Ir, "aircombat-hud-missile.png");
        var im = Raylib.LoadImage(System.IO.Path.Combine(shots, "aircombat-hud-missile.png"));
        Assert.Equal(MissileCue.LockInZone, missile.Missile);
        var t = cam.WorldToScreen(missile.TargetAt);
        Assert.True(Around(im, t + new Vector2(0f, -34f), 6, FighterHud.Green, 40) > 4, "the lock diamond's top corner, green, on the target");
        var z = FighterHud.ZoneCentre(cam.Viewport, cam.ViewCentre);
        Assert.True(Around(im, z, 60, FighterHud.Green, 40) > 80, "the IN ZONE tag, green, bottom-centre");
        var ring = FighterHud.RwrCentre(cam.Viewport);
        Assert.True(Around(im, ring + new Vector2(FighterHud.RwrRadius + 40f, 0f), 40, FighterHud.Red, 40) > 30, "LAUNCH IR 4.2 s, red, beside the ring");
        var g = FighterHud.GRect(cam.Viewport);
        Assert.True(Around(im, new Vector2(g.X + g.Width / 2f, g.Y + 14f), 30, FighterHud.Amber, 40) > 25, "G 6.2 in amber, bottom-right");
        Raylib.UnloadImage(im);
        Assert.False(missile.HasPipper);
        // the gun: nearer, the pipper at the led point
        scene.World.SetComponent(foe, new SimTransform { Position = eye + new Vector3(420f, 18f, 0f), Rotation = Quaternion.Identity });
        var gun = Draw(Gun, "aircombat-hud-gun.png");
        Assert.True(gun.HasPipper);
        var gim = Raylib.LoadImage(System.IO.Path.Combine(shots, "aircombat-hud-gun.png"));
        var p = cam.WorldToScreen(gun.PipperAt);
        Assert.True(Around(gim, p + new Vector2(19f, 0f), 4, FighterHud.Amber, 40) > 6, "the pipper's ring (19 px), amber, at the led point");
        Raylib.UnloadImage(gim);
        Assert.True(Vector2.Distance(p, cam.WorldToScreen(gun.TargetAt)) > 6f, "the pipper is NOT on the target: it is led");
    }
}
