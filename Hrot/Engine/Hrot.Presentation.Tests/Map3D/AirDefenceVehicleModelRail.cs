using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Replication.Components;
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
/// ⭐⭐ <c>CE-4618</c> (radar SAM, docs/designs/air-attack/DESIGN_Radar_SAM.md §5 U-1; 🔒 user, 2026-11-15: <i>"The UI lane can provide the visuals for the carrier and
/// launcher and the missile and the effect of launch."</i>) — the radar SAM truck's 3-D model: the family and its kit (a wheeled carrier, a cab, a four-canister launcher on
/// a turntable that turns and ELEVATES through the turret pose, a radar mast whose antenna spins while the radar radiates and stands when it is off), the motion that
/// drives the antenna and wheels (and allocates nothing), the one <see cref="RadarActivity"/> seam, and the real 3-D frame (side, three-quarter, launcher elevated 45°, the
/// mast at two spin angles vs not moving) into <c>HROT_RAIL_SHOTS</c> as <c>sam-*.png</c>.
/// ✅ Type 114 / class <c>AirDefenceVehicle</c> are backend's (CE-4617 B-2): the class maps to the family and the frame rail draws the REAL type through the real catalogue.
/// ⭐ U-2: the launcher shows the rounds left (a canister's COVER goes when its round is fired — <see cref="LauncherLoad"/>), and the launch draws its flash and smoke on the
/// frame of the shot. ⛔ The frame tests SKIP without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~AirDefenceVehicleModelRail</c>. 🔴 Red-proof: the antenna's spin removed from <c>BodyTransform</c> fails the
/// antenna rails; the tracker's spin removed fails the motion rail.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class AirDefenceVehicleModelRail
{
    private static readonly Vector3 Size = new(9.1f, 2.8f, 4.2f);   // the TKB's box (type 114)
    private static readonly KitPivots Pivots = ShapeKits.AirDefenceVehicle.Pivots;

    private static Vector3 At(KitPart p, ArticulationPose pose, MotionPose motion, Vector3? local = null)
        => Vector3.Transform(local ?? Vector3.Zero, p.BodyTransform(Size, Pivots, pose, motion));

    [Fact]
    public void TheSamTruck_HasItsOwnFamily_KitAndSize_ALauncherThatTurnsAndElevates_AMastAndWheels()
    {
        Assert.Same(ShapeKits.AirDefenceVehicle, ShapeKits.For(VisualFamily.AirDefenceVehicle));
        Assert.Equal(Size, ShapeKits.DefaultSize(VisualFamily.AirDefenceVehicle));
        Assert.True(ShapeKits.AirDefenceVehicle.IsArticulated, "turret 0: the launcher turns and elevates");
        var parts = ShapeKits.AirDefenceVehicle.Parts;
        Assert.True(parts.Length >= 40, "a modelled machine, not a box");
        Assert.Equal(2, parts.Count(p => p.Role == PartRole.Turret));          // the turntable and its pedestal
        Assert.Equal(4, parts.Count(p => p.Role == PartRole.Gun && p.Colour == ColourRole.Body && p.Round < 0));   // the four canisters (2 × 2)
        Assert.Equal(new[] { 0, 1, 2, 3 }, parts.Where(p => p.Round >= 0).Select(p => p.Round).OrderBy(r => r).ToArray());   // ⭐ one cover per canister, the fire order's index
        Assert.Equal(3, parts.Count(p => p.Role == PartRole.RadarAntenna));    // panel, backing, the IFF feed
        Assert.Equal(8, parts.Count(p => p.Role == PartRole.WheelSpoke));      // 8 × 8
        // ⛔ a Gun CYLINDER along x is the TKB's barrel (KitFromTkb re-sizes it to the muzzle) and every Turret part is re-centred on the TKB's pivot
        Assert.DoesNotContain(parts, p => p.Role == PartRole.Gun && p.Shape == PartShape.Cylinder && p.Axis == PartAxis.X);
        Assert.All(parts.Where(p => p.Role == PartRole.Turret), p => Assert.Equal(Pivots.Turret.Y, p.Centre.Y));   // on the axis
        Assert.Equal("_wheeled", VisualFamilies.IconName(VisualFamily.AirDefenceVehicle));
        Assert.NotEqual(EntityBodyLayer3D.DefaultColour(VisualFamily.Unknown), EntityBodyLayer3D.DefaultColour(VisualFamily.AirDefenceVehicle));
    }

    /// <summary>⭐ CE-4618 — backend's class <c>AirDefenceVehicle</c> (DIS <c>1.1.*.28</c>, type 114) maps to the family, and the REAL type's family, size and kit are the SAM truck's.</summary>
    [Fact]
    public void TheClass_MapsToTheFamily_AndTheRealType114IsTheSamTruck()
    {
        Assert.Equal(VisualFamily.AirDefenceVehicle, VisualFamilies.FamilyOfClass["AirDefenceVehicle"]);
        var sam = Map3DTests.BuiltIn().GetByType(TkbEntityTypes.AirDefence_RadarSam)!;
        Assert.Equal(VisualFamily.AirDefenceVehicle, VisualFamilies.Of(sam));
        var body = sam.GetDescriptor<Fdp.Toolkit.Tkb.Domain.BodyGeometryDto>()!;
        Assert.Equal((Size.X, Size.Y, Size.Z), (body.Length, body.Width, body.Height));   // the kit's metres ARE the TKB's metres
        // ⭐ the launcher is the TKB's own: the kit placed FROM the TKB keeps the hinge and the mouth where the TKB says (muzzle = trunnion + offset)
        var (parts, pivots) = KitFromTkb.For(ShapeKits.AirDefenceVehicle, sam, Size);
        Assert.Equal(Pivots.Turret.X, pivots.Turret.X, 3);
        Assert.Equal(Pivots.GunTrunnionXZ.X, pivots.GunTrunnionXZ.X, 3);
        Assert.Equal(Pivots.GunTrunnionXZ.Y, pivots.GunTrunnionXZ.Y, 3);
        var mount = sam.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>()!.Mounts[0];
        var turret = sam.GetDescriptor<Fdp.Toolkit.Tkb.Domain.TurretSuiteDto>()!.Turrets[0];
        float mouthX = turret.PivotX + turret.TrunnionX + mount.MuzzleOffsetX;
        var topLeftCover = parts.First(p => p.Round == 3);
        Assert.InRange(mouthX - (topLeftCover.Centre.X * Size.X), -0.1f, 0.1f);   // the cover sits at the muzzle the round leaves by
    }

    /// <summary>⭐ CE-4618 — the 2-D map: the TKB's symbol is the 2525 ground air-defence missile unit, and the class chain gives the ground-vehicle outline.</summary>
    [Fact]
    public void The2dMap_DrawsType114_AsAGroundVehicle_WithTheTkbsAirDefenceSymbol()
    {
        var sam = Map3DTests.BuiltIn().GetByType(TkbEntityTypes.AirDefence_RadarSam)!;
        Assert.Equal("SHGPUCDM-------", sam.GetDescriptor<Fdp.Toolkit.Tkb.Domain.VisualDefinitionDto>()!.SymbolCode);
        Assert.Equal(GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary.GroundVehicle, Hrot.Presentation.Map.EntityClassShapeLibrary.ProfileNameOf(sam.DisType));
    }

    [Fact]
    public void TheNeutralKit_LiesInsideItsBox_StandingOnTheGround_CabForward_TheAntennaTopMost()
    {
        float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var part in ShapeKits.AirDefenceVehicle.Parts)
        {
            var m = part.BodyTransform(Size);
            for (int i = 0; i < 8; i++)
            {
                var local = part.Shape == PartShape.Box
                    ? new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f)
                    : new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, (i >> 2) & 1);
                var w = Vector3.Transform(local, m);
                minX = MathF.Min(minX, w.X); maxX = MathF.Max(maxX, w.X); maxY = MathF.Max(maxY, MathF.Abs(w.Y));
                minZ = MathF.Min(minZ, w.Z); maxZ = MathF.Max(maxZ, w.Z);
            }
        }
        Assert.InRange(maxX, 4.5f, 4.65f);     // the cab's bumper: the front
        Assert.InRange(minX, -4.65f, -4.45f);   // the rear bumper
        Assert.InRange(maxY, 1.3f, 1.45f);     // the tyres' outer faces
        Assert.InRange(minZ, -0.05f, 0.05f);   // on its wheels
        Assert.InRange(maxZ, 4.1f, 4.3f);     // the search antenna's top
        Assert.True(ShapeKits.AirDefenceVehicle.Parts.Where(p => p.Role == PartRole.RadarAntenna).All(p => At(p, default, default).Z > 3.5f), "the antenna sits above the cab and the canisters");
    }

    [Fact]
    public void TheLauncher_ElevatesAboutItsRearHinge_To85Degrees_AndTurnsWithTheTurntable_TheHullNeverMoves()
    {
        var parts = ShapeKits.AirDefenceVehicle.Parts;
        var cap = parts.First(p => p.Round == 3);   // a canister's front cover (the top-left one)
        var rear = parts.First(p => p.Role == PartRole.Gun && p.Colour == ColourRole.Dark && p.Centre.X < -0.15f);   // the rear frame, at the hinge
        var hull = parts.First(p => p.Role == PartRole.Hull);
        Vector3 Tip(float az, float el) => At(cap, new ArticulationPose(az, el, Published: true), default);
        var level = Tip(0f, 0f);
        float deg = MathF.PI / 180f;
        var at45 = Tip(0f, 45f * deg);
        var at85 = Tip(0f, 85f * deg);
        Assert.InRange(level.Z, 2.7f, 3.5f);
        Assert.True(at45.Z > level.Z + 1.8f && at45.X < level.X - 0.6f, $"45° lifts the muzzle end and draws it back: {level} -> {at45}");
        Assert.True(at85.Z > 5.0f, $"85° is nearly upright (the canisters are 3.5 m long, hinged at 2.6 m): {at85}");
        // the rear end stays at the hinge as it elevates
        float rearMoves = Vector3.Distance(At(rear, new ArticulationPose(0f, 0f, true), default), At(rear, new ArticulationPose(0f, 85f * deg, true), default));
        Assert.True(rearMoves < 1.0f && rearMoves < Vector3.Distance(level, at85) / 3f, $"hinged near the REAR (0.7 m behind the hinge): the rear frame moves {rearMoves:0.00} m, the mouth {Vector3.Distance(level, at85):0.0} m");
        // a quarter turn swings the muzzle end from the front to the left of the turntable
        var left = Tip(MathF.PI / 2f, 0f);
        Assert.True(left.Y > 2.5f && MathF.Abs(left.X - (-1.5f)) < 0.6f, $"traversed 90° left about the turntable axis: {left}");
        // the hull and the mast are not articulated
        Assert.Equal(hull.BodyTransform(Size), hull.BodyTransform(Size, Pivots, new ArticulationPose(1.2f, 1.0f, true), default));
        var mast = parts.First(p => p.Shape == PartShape.Cylinder && p.Axis == PartAxis.Z);
        Assert.Equal(mast.BodyTransform(Size), mast.BodyTransform(Size, Pivots, new ArticulationPose(1.2f, 1.0f, true), new MotionPose(0f, 0f, 0f, Antenna: 2.0f)));
    }

    [Fact]
    public void TheAntenna_SpinsAboutTheMast_WhereItSpins_AndNothingElseMoves()
    {
        var parts = ShapeKits.AirDefenceVehicle.Parts;
        var panel = parts.First(p => p.Role == PartRole.RadarAntenna && p.Colour == ColourRole.Body);
        var feed = parts.First(p => p.Role == PartRole.RadarAntenna && p.Colour == ColourRole.Metal);
        var off = MotionPose.Still;
        var on90 = new MotionPose(0f, 0f, 0f, Antenna: MathF.PI / 2f);
        // the panel is on the mast axis: it turns on the spot, its 2.2 m face from across the truck to along it
        var c0 = At(panel, default, off); var c1 = At(panel, default, on90);
        Assert.True(Vector3.Distance(c0, c1) < 0.01f, "centred on the mast");
        var across0 = Vector3.TransformNormal(Vector3.UnitY, panel.BodyTransform(Size, Pivots, default, off));
        var across1 = Vector3.TransformNormal(Vector3.UnitY, panel.BodyTransform(Size, Pivots, default, on90));
        Assert.True(MathF.Abs(across0.Y) > 0.99f * across0.Length() && MathF.Abs(across1.X) > 0.99f * across1.Length(), "the panel's long side swung a quarter turn");
        // the IFF feed is off the axis: it ORBITS the mast, 0.9 m out, the same height
        var f0 = At(feed, default, off); var f1 = At(feed, default, on90);
        Assert.InRange(MathF.Abs(f0.Y), 0.75f, 0.85f);
        Assert.True(Vector3.Distance(f0, f1) > 1.0f && MathF.Abs(f0.Z - f1.Z) < 0.001f, $"orbits: {f0} -> {f1}");
        Assert.InRange(new Vector2(f1.X - 2.5f, f1.Y).Length(), 0.7f, 0.9f);
        // every other part is exactly where it was
        foreach (var p in parts.Where(p => p.Role != PartRole.RadarAntenna))
            Assert.Equal(p.BodyTransform(Size, Pivots, default, off), p.BodyTransform(Size, Pivots, default, on90));
    }

    [Fact]
    public void TheMotion_TheAntennaStandsWhileTheRadarIsOff_SpoolsUpAndTurnsWhileOn_AndWindsDownWhenItStops_TheWheelsRoll()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        var q = Quaternion.Identity;
        double sim = 0;
        MotionPose Step(bool on, Vector3 pos) { sim += 0.05; return t.Advance(e, pos, q, VisualFamily.AirDefenceVehicle, Size, sim, on); }
        for (int i = 0; i < 100; i++) Assert.Equal(0f, Step(false, Vector3.Zero).Antenna);   // off: it stands, never a stray angle
        float first = Step(true, Vector3.Zero).Antenna;
        Assert.InRange(first, 0f, 0.2f);                                                       // ⭐ spooling: the first frame barely moves
        for (int i = 0; i < 80; i++) Step(true, Vector3.Zero);                                  // 4 s: at full speed (spool 3 s)
        float a = Step(true, Vector3.Zero).Antenna; float b = Step(true, Vector3.Zero).Antenna;
        float perSecond = Wrapped(b - a) / 0.05f;
        Assert.InRange(perSecond, MotionTracker.AntennaRevsPerSecond * 2f * MathF.PI * 0.95f, MotionTracker.AntennaRevsPerSecond * 2f * MathF.PI * 1.05f);   // ~1.7 s a turn
        float last = 0f;
        for (int i = 0; i < 100; i++) last = Step(false, Vector3.Zero).Antenna;                // radar off: spools down and STOPS
        Assert.Equal(last, Step(false, Vector3.Zero).Antenna);
        // the wheels roll by the distance driven over the SAM truck's own wheel radius (0.6 m)
        float roll0 = Step(false, Vector3.Zero).WheelRoll;
        float roll1 = Step(false, new Vector3(0.6f, 0f, 0f)).WheelRoll;
        Assert.InRange(Wrapped(roll1 - roll0), 0.95f, 1.05f);
    }

    [Fact]
    public void TheMotion_AllocatesNothing_InTheSteadyState()
    {
        var t = new MotionTracker();
        var e = new Entity(1, 1);
        double sim = 0;
        for (int i = 0; i < 200; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.1f, 0, 0), Quaternion.Identity, VisualFamily.AirDefenceVehicle, Size, sim, true, sim); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { sim += 0.016; t.Advance(e, new Vector3(i * 0.1f, 0, 0), Quaternion.Identity, VisualFamily.AirDefenceVehicle, Size, sim, i % 400 < 300, sim); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0L, bytes);   // ⭐ HOT PATH — 0 B
        // and the one place that reads "is the radar on" — backend's reader of the replicated RadarEmission, a static call
        using var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Perception.Radar.RadarEmission>();
        var ent = world.CreateEntity();
        world.AddComponent(ent, Radiating());
        Assert.True(RadarActivity.IsOn(world, ent));   // warm
        before = GC.GetAllocatedBytesForCurrentThread();
        bool on = true;
        for (int i = 0; i < 2000; i++) on &= RadarActivity.IsOn(world, ent);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(on);
    }

    private static Fdp.Toolkit.Perception.Radar.RadarEmission Radiating()
        => new() { Mode = Fdp.Toolkit.Perception.Radar.RadarMode.Search, EmitterClass = Fdp.Toolkit.Perception.Radar.RadarEmitterClass.AirDefence, RangeM = 8000f };

    [Fact]
    public void TheAntenna_ReadsTheReplicatedRadarEmission_OffWithoutIt_OnWhileItRadiates()
    {
        using var world = new EntityRepository();
        var e = world.CreateEntity();
        Assert.False(RadarActivity.IsOn(world, e));   // a world that never registered RadarEmission: off
        world.RegisterComponent<Fdp.Toolkit.Perception.Radar.RadarEmission>();
        Assert.False(RadarActivity.IsOn(world, e));   // no component: off
        world.AddComponent(e, new Fdp.Toolkit.Perception.Radar.RadarEmission { Mode = Fdp.Toolkit.Perception.Radar.RadarMode.Off });
        Assert.False(RadarActivity.IsOn(world, e));   // silent
        world.SetComponent(e, Radiating());
        Assert.True(RadarActivity.IsOn(world, e));    // radiating (CE-4617 B-1 writes it on the owner; replication brings it to every node)
    }

    private static float Wrapped(float d)
    {
        d %= MathF.PI * 2f;
        if (d < 0) d += MathF.PI * 2f;
        return d > MathF.PI * 1.5f ? d - MathF.PI * 2f : d;
    }

    // ── the real 3-D frame ────────────────────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public void TheSamTruck_IsDrawn_ByTheRealFrame_Side_ThreeQuarter_LauncherElevated_TheMastSpinningVsStill()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        const long SamType = TkbEntityTypes.AirDefence_RadarSam;   // ✅ the REAL type 114, through the real catalogue (no pinned look)
        var terrain = Map3DTests.TestTown();
        var world = Map3DTests.WorldWith(terrain);
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<TkbIdentity>();
        var tkb = Map3DTests.BuiltIn();
        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
        double clock = 0.0;
        void Clock(double dt) { clock += dt; world.SetSingletonUnmanaged(new GlobalTime { TotalTime = clock }); }
        Clock(0.0);
        var query = WorldQuery.Of(world)!;
        var spot = OpenGround(terrain, query);
        float G(float x, float y) => query.GroundHeightAt(x, y);
        var scale = new List<Entity>();
        Entity Put(long type, float dx, float dy, bool forScale = false)
        {
            var e = world.CreateEntity();
            float x = spot.X + dx, y = spot.Y + dy;
            world.AddComponent(e, new SimTransform { Position = new Vector3(x, y, G(x, y)), Rotation = Quaternion.Identity });
            world.AddComponent(e, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(type) });
            if (forScale) scale.Add(e);
            return e;
        }
        var sam = Put(SamType, 0f, 0f);
        Put(TkbEntityTypes.Tank_T72, 0f, 12f, forScale: true);
        Put(TkbEntityTypes.Infantry_Rifleman, 6f, -5f, forScale: true);

        world.RegisterComponent<Fdp.Toolkit.Perception.Radar.RadarEmission>();
        world.AddComponent(sam, new Fdp.Toolkit.Perception.Radar.RadarEmission { Mode = Fdp.Toolkit.Perception.Radar.RadarMode.Off });
        bool radarOn = false;
        void Radar(bool on) { radarOn = on; world.SetComponent(sam, on ? Radiating() : new Fdp.Toolkit.Perception.Radar.RadarEmission { Mode = Fdp.Toolkit.Perception.Radar.RadarMode.Off }); }
        var pose = ArticulationPose.Neutral;
        try
        {
            using var frame = UiFrameHarness.Begin(1280, 800);
            LitShader.Reset();
            var canvas = new MapCanvas(input: null);
            var two = new MapCamera();
            canvas.Camera = two;
            var bodies = new EntityBodyLayer3D(() => world, () => tkb, (w, e) => e == sam ? pose : ArticulationPose.Neutral);
            var sw = new MapViewSwitch(canvas, two);
            sw.Camera3D.GroundHeight = (x, y) => G(x, y);
            canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(world)));
            canvas.AddLayer(bodies);
            sw.Set(true, animate: false);

            float ground = G(spot.X, spot.Y);
            void Look(float lookUp, float dist, float yaw, float pitch)
                => sw.Camera3D.Pose = new CameraPose(new Vector3(spot.X, spot.Y, ground + lookUp), dist, yaw, pitch);
            void Render() { frame.Step(canvas.Draw); frame.Step(canvas.Draw); }
            string Shot(string name) { Render(); var path = Path.Combine(shots, name); frame.Screenshot(path); return path; }
            const float Side = MathF.PI * 0.5f;

            // the real type 114 draws as the SAM truck (and the T-72 and the man beside it for scale)
            Look(2.0f, 22f, Side, -0.08f);
            Shot("sam-1-side.png");
            Assert.Equal(3, bodies.BodiesDrawn);
            Look(2.0f, 22f, Side + 0.75f, -0.30f);
            Shot("sam-2-front34.png");
            Look(2.0f, 22f, Side - 0.75f, -0.30f);
            Shot("sam-2b-rear34.png");

            // the launcher elevated 45° and turned a little, from the rear three-quarter and the side
            float deg = MathF.PI / 180f;
            pose = new ArticulationPose(0.0f, 45f * deg, Published: true);
            Look(2.5f, 24f, Side - 0.75f, -0.22f);
            Shot("sam-3-elevated45.png");
            Look(2.5f, 24f, Side, -0.05f);
            Shot("sam-3b-elevated45-side.png");
            pose = new ArticulationPose(0.6f, 85f * deg, Published: true);
            Look(3.0f, 26f, Side - 0.75f, -0.22f);
            Shot("sam-3c-elevated85.png");
            pose = ArticulationPose.Neutral;

            // the mast: radar OFF is a still picture; radar ON spins — only the antenna's pixels change
            Look(3.0f, 14f, Side + 0.6f, -0.18f);
            foreach (var e in scale) world.SetComponent(e, new SimTransform { Position = new Vector3(spot.X + 400f, spot.Y + 400f, 0f), Rotation = Quaternion.Identity });   // out of the picture
            var empty = ScreenOf(frame, canvas, () => world.SetComponent(sam, new SimTransform { Position = new Vector3(spot.X + 5000f, spot.Y, ground), Rotation = Quaternion.Identity }), Render);
            world.SetComponent(sam, new SimTransform { Position = new Vector3(spot.X, spot.Y, ground), Rotation = Quaternion.Identity });
            Radar(false);
            Clock(0.1); var off1 = ScreenOf(frame, canvas, null, Render, Path.Combine(shots, "sam-4-mast-off.png"));
            Clock(2.0); var off2 = ScreenOf(frame, canvas, null, Render);
            Assert.Equal(0, Diff(off1, off2).Count);   // still: the same picture whatever the clock says
            var vehicle = Diff(empty, off1);
            Assert.True(vehicle.Count > 4000, $"the truck is on screen: {vehicle.Count} px");
            Radar(true);
            for (int i = 0; i < 14; i++) { Clock(0.5); Render(); }   // spool up (3 s) and run
            var spun = new List<Pixels>();
            for (int i = 0; i < 4; i++) { Clock(0.37); spun.Add(ScreenOf(frame, canvas, null, Render, i < 2 ? Path.Combine(shots, $"sam-5-mast-on-{i + 1}.png") : null)); }
            var changed = spun.Select(s => Diff(off1, s)).Where(df => df.Count > 0).ToList();
            Assert.True(changed.Count >= 3, "radar On: the antenna is turning — frames differ from the still one");
            foreach (var df in changed)
            {
                // ⭐ differs ONLY where the antenna is: the top part of the truck's silhouette (mast 3.55 m … antenna 4.35 m of a 4.4 m truck)
                Assert.True(df.MaxY <= vehicle.MinY + (vehicle.MaxY - vehicle.MinY) * 0.33f, $"the changes ({df.MinY}..{df.MaxY}) lie in the top third of the truck ({vehicle.MinY}..{vehicle.MaxY})");
                Assert.True(df.Count < vehicle.Count / 8, $"{df.Count} changed px of {vehicle.Count}");
            }
            Assert.True(Diff(spun[0], spun[1]).Count > 0 || Diff(spun[1], spun[2]).Count > 0, "and it keeps turning between frames");
            // the gallery picture, to the scratchpad only
            foreach (var e in scale) world.DestroyEntity(e);
            Radar(false);
            pose = new ArticulationPose(0.0f, 32f * deg, Published: true);   // the launcher raised a little: the picture says "missile launcher" at thumbnail size
            Look(2.1f, 13.5f, Side + 0.75f, -0.2f);
            Shot("sam-6-thumb.png");
            Assert.Equal(1, bodies.BodiesDrawn);
        }
        finally { }
    }

    // ── ⭐ U-2 — the rounds left (the launcher's covers) and the launch ──────────────────────────────────────────────────────

    [Fact]
    public void TheCovers_FollowTheRoundsLeft_InTheFireOrder_TopLeftTopRightBottomLeftBottomRight()
    {
        var parts = ShapeKits.AirDefenceVehicle.Parts;
        int Covers(int rounds) => parts.Count(p => p.Round >= 0 && p.Round < rounds);
        Assert.Equal(new[] { 4, 3, 2, 1, 0 }, new[] { Covers(4), Covers(3), Covers(2), Covers(1), Covers(0) });
        Assert.Equal(4, Covers(LauncherLoad.Full));   // no ammunition component yet: every cover on
        // the order: the round fired FIRST (4 → 3) uncovers Round 3 = top-left; then top-right, bottom-left, bottom-right
        var hingeZ = Pivots.GunTrunnionXZ.Y;
        KitPart Cover(int round) => parts.Single(p => p.Round == round);
        var (tl, tr, bl, br) = (Cover(3), Cover(2), Cover(1), Cover(0));
        Assert.True(tl.Centre.Y > 0 && tl.Centre.Z > hingeZ, "1st: top-left");
        Assert.True(tr.Centre.Y < 0 && tr.Centre.Z > hingeZ, "2nd: top-right");
        Assert.True(bl.Centre.Y > 0 && bl.Centre.Z < hingeZ, "3rd: bottom-left");
        Assert.True(br.Centre.Y < 0 && br.Centre.Z < hingeZ, "4th: bottom-right");
        // each cover has its dark open mouth right behind it (so a removed cover shows a dark hole, not the canister's light end)
        foreach (var c in new[] { tl, tr, bl, br })
            Assert.Contains(parts, p => p.Role == PartRole.Gun && p.Colour == ColourRole.Dark && p.Round < 0 && MathF.Abs(p.Centre.Y - c.Centre.Y) < 1e-4f
                                        && MathF.Abs(p.Centre.Z - c.Centre.Z) < 1e-4f && p.Centre.X < c.Centre.X && p.Centre.X > c.Centre.X - 0.01f);
    }

    [Fact]
    public void TheLauncherLoad_ReadsTheReplicatedWeaponAmmo_FullWhereNothingSays_AndAllocatesNothing()
    {
        using var world = new EntityRepository();
        var e = world.CreateEntity();
        Assert.Equal(LauncherLoad.Full, LauncherLoad.RoundsLeft(world, e, 0));   // a world that never registered it
        world.RegisterComponent<WeaponAmmo>();
        Assert.Equal(LauncherLoad.Full, LauncherLoad.RoundsLeft(world, e, 0));   // no component
        world.AddComponent(e, new WeaponAmmo { Count = 1, Ammo0 = 3, Max0 = 4 });
        Assert.Equal(3, LauncherLoad.RoundsLeft(world, e, 0));
        Assert.Equal(LauncherLoad.Full, LauncherLoad.RoundsLeft(world, e, 1));   // a mount the unit does not have
        world.SetComponent(e, new WeaponAmmo { Count = 1, Ammo0 = 0, Max0 = 4 });
        Assert.Equal(0, LauncherLoad.RoundsLeft(world, e, 0));
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 2000; i++) sum += LauncherLoad.RoundsLeft(world, e, 0);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // ⭐ HOT PATH — 0 B
        Assert.Equal(0, sum);
    }

    /// <summary>A scene of ONE real radar SAM (type 114) on open ground, the host's full TKB (the effect sets), the body / projectile / effect layers — for the frame rails of U-2.</summary>
    private sealed class SamScene : IDisposable
    {
        public readonly EntityRepository World; public readonly Fdp.Interfaces.ITkbDatabase Tkb;
        public readonly Entity Sam; public readonly Vector2 Spot; public readonly float Ground;
        public readonly UiFrameSession Frame; public readonly EntityBodyLayer3D Bodies; public readonly Hrot.UI.Common.Effects.EffectLayer Effects;
        public readonly Hrot.UI.Common.Effects.ProjectileLayer Projectiles; public readonly MapCanvas Canvas; public readonly MapViewSwitch Switch;
        public double Now = 50.0;

        public SamScene(int ammo)
        {
            var terrain = Map3DTests.TestTown();
            World = Map3DTests.WorldWith(terrain);
            HrotSharedComponentRegistry.RegisterAll(World);
            Hrot.Map.Common.PresentationComponentRegistry.RegisterEffects(World);
            World.RegisterComponent<SimTransform>(); World.RegisterComponent<TkbIdentity>();
            World.RegisterEvent<Fdp.Toolkit.Combat.Events.WeaponFireNotification>();
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            Tkb = tkb;
            World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
            World.SetSingletonUnmanaged(new GlobalTime { TotalTime = Now });
            var query = WorldQuery.Of(World)!;
            Spot = OpenGround(terrain, query);
            Ground = query.GroundHeightAt(Spot.X, Spot.Y);
            Sam = World.CreateEntity();
            World.AddComponent(Sam, new SimTransform { Position = new Vector3(Spot.X, Spot.Y, Ground), Rotation = Quaternion.Identity });
            World.AddComponent(Sam, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)TkbEntityTypes.AirDefence_RadarSam });
            World.AddComponent(Sam, new WeaponAmmo { Count = 1, Ammo0 = ammo, Max0 = 4 });
            Turret = Fdp.Toolkit.Combat.TurretParts.Ensure(World, Sam, 0);
            Frame = UiFrameHarness.Begin(1280, 800);
            LitShader.Reset();
            Canvas = new MapCanvas(input: null);
            var two = new MapCamera();
            Canvas.Camera = two;
            var poses = new TurretPoseSource();
            Bodies = new EntityBodyLayer3D(() => World, () => tkb, (w, e) => poses.PoseOf(w, e));
            Effects = new Hrot.UI.Common.Effects.EffectLayer(() => World, () => tkb, Bodies) { GroundHeight = query.GroundHeightAt };
            Projectiles = new Hrot.UI.Common.Effects.ProjectileLayer(() => World, () => tkb) { GroundHeight = query.GroundHeightAt };
            Switch = new MapViewSwitch(Canvas, two);
            Switch.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
            Canvas.AddLayer(new TerrainLayer3D(() => WorldQuery.RenderGeometryOf(World)));
            Canvas.AddLayer(Bodies);
            Canvas.AddLayer(Projectiles);
            Canvas.AddLayer(Effects);
            Switch.Set(true, animate: false);
        }

        public readonly Entity Turret;
        public void Pose(float azimuth, float elevation)
            => World.GetComponentRW<TurretPose>(Turret) = new TurretPose { Azimuth = (Fdp.Core.Angle)azimuth, Elevation = (Fdp.Core.Angle)elevation };
        public void Rounds(int n) => World.SetComponent(Sam, new WeaponAmmo { Count = 1, Ammo0 = n, Max0 = 4 });
        public void Look(float lookUp, float dist, float yaw, float pitch)
            => Switch.Camera3D.Pose = new CameraPose(new Vector3(Spot.X, Spot.Y, Ground + lookUp), dist, yaw, pitch);
        public void Render() { Frame.Step(Canvas.Draw); Frame.Step(Canvas.Draw); }
        public string Shot(string dir, string name) { Render(); var path = Path.Combine(dir, name); Frame.Screenshot(path); return path; }
        public void Dispose() => Frame.Dispose();
    }

    private static string ShotsDir()
    {
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-map3d");
        Directory.CreateDirectory(shots);
        return shots;
    }

    [SkippableFact]
    public void TheLauncher_ShowsTheRoundsLeft_FourTwoNone_ByTheReplicatedWeaponAmmo_OnTheRealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new SamScene(ammo: 4);
        const float Side = MathF.PI * 0.5f;
        scene.Pose(0f, 0f);
        int Covers() { scene.Look(2.4f, 13f, Side + 1.05f, -0.2f); scene.Render(); return scene.Bodies.PartsDrawn; }
        int p4 = Covers(); scene.Shot(shots, "sam-load-4.png");
        scene.Rounds(2);
        int p2 = Covers(); scene.Shot(shots, "sam-load-2.png");
        scene.Rounds(0);
        int p0 = Covers(); scene.Shot(shots, "sam-load-0.png");
        Assert.Equal(2, p4 - p2);   // ⭐ two rounds gone ⇒ two covers off the frame
        Assert.Equal(2, p2 - p0);
        scene.Rounds(4);
        Assert.Equal(p4, Covers());   // a reload puts them back
        // the pictures differ where the canisters' mouths are (a removed cover is a dark hole): the three frames are three different pictures
        scene.Look(2.4f, 13f, Side + 1.05f, -0.2f);
        scene.Rounds(4); var full = ScreenOf(scene.Frame, scene.Canvas, null, scene.Render);
        scene.Rounds(2); var two = ScreenOf(scene.Frame, scene.Canvas, null, scene.Render);
        scene.Rounds(0); var none = ScreenOf(scene.Frame, scene.Canvas, null, scene.Render);
        Assert.True(Diff(full, two).Count > 30 && Diff(two, none).Count > 30, $"4→2: {Diff(full, two).Count} px, 2→0: {Diff(two, none).Count} px");
    }

    [SkippableFact]
    public void TheLaunch_ShowsItsFlashSmokeAndMissile_OnTheFrameOfTheShot_AtTheCanistersMouth()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = ShotsDir();
        using var scene = new SamScene(ammo: 4);
        var world = scene.World;
        float deg = MathF.PI / 180f;
        scene.Pose(0.35f, 40f * deg);
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>();
        world.RegisterComponent<Fdp.Toolkit.Time.Lifespan>();
        var tkb = scene.Tkb;
        var query = WorldQuery.Of(world)!;
        scene.Look(4.0f, 22f, MathF.PI * 0.5f + 0.7f, -0.12f);
        scene.Render();
        var before = ScreenOf(scene.Frame, scene.Canvas, null, scene.Render);
        Assert.DoesNotContain(scene.Effects.Resolve(), r => r.Look.Kind is EffectKind.MuzzleFlash or EffectKind.Smoke);
        Assert.Empty(scene.Projectiles.Resolve());

        // the shot, as the sim raises it: mount 0 fires the SAM missile from the muzzle (the canister's mouth) — round 1 of 4: ammunition 4 → 3
        Assert.True(Fdp.Toolkit.Combat.Muzzle.TryOf(world, scene.Sam, 0, out var mouth, out var forward));
        const long Missile = Hrot.Core.Tkb.MunitionTkbCatalog.TkbRadarSamMissile;
        world.Bus.Publish(new Fdp.Toolkit.Combat.Events.WeaponFireNotification
        {
            Shooter = scene.Sam, WeaponIndex = 0, AmmoGuid = Missile, Quantity = 1, FiredAt = (SimInstant)scene.Now,
            Origin = mouth, Velocity = forward * 120f,
        });
        world.Bus.SwapBuffers();
        new Hrot.UI.Common.Effects.EffectSpawnSystem().Execute(world, 0.016f);
        ((Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetCommandBuffer()).Playback(world);
        scene.Rounds(3);
        // the round itself, a fraction of a second out of the tube: 12 m along the boresight, flying at 120 m/s
        var round = world.CreateEntity();
        world.AddComponent(round, new SimTransform { Position = mouth + forward * 12f, Rotation = Quaternion.Identity });
        world.AddComponent(round, new SimVelocity { Linear = forward * 120f });
        world.AddComponent(round, new Fdp.Toolkit.Combat.Components.BallisticProjectile { Ammo = Missile, PreviousPosition = mouth + forward * 11f, Muzzle = mouth });

        scene.Look(4.0f, 22f, MathF.PI * 0.5f + 0.7f, -0.12f);
        string shot = scene.Shot(shots, "sam-launch-1.png");
        var after = ScreenOf(scene.Frame, scene.Canvas, null, scene.Render);
        var effects = scene.Effects.Resolve();
        var flash = Assert.Single(effects, r => r.Look.Kind == EffectKind.MuzzleFlash);
        var smoke = Assert.Single(effects, r => r.Look.Kind == EffectKind.Smoke);
        Assert.Equal(Hrot.Core.Tkb.EffectTkbCatalog.VisualOf(tkb, Hrot.Core.Tkb.EffectTkbCatalog.MissileLaunchFlash)!.ColorHex, flash.Look.ColorHex);   // the SAM's own flash, not a calibre default
        Assert.True(Vector3.Distance(flash.At, mouth) < 0.5f, $"the flash is at the canister's mouth: {flash.At} vs {mouth}");
        Assert.True(Vector3.Distance(smoke.At, mouth) < 0.5f, $"and so is the smoke it leaves: {smoke.At} vs {mouth}");
        // ⭐ the mouth is where a COVER sits: the muzzle is on the top-left canister's front (the TKB's offset = the kit's cover)
        var kitCover = ShapeKits.AirDefenceVehicle.Parts.First(p => p.Round == 3);
        var coverAt = Vector3.Transform(Vector3.Zero, kitCover.BodyTransform(Size, Pivots, new ArticulationPose(0.35f, 40f * deg, true), default)) + new Vector3(scene.Spot.X, scene.Spot.Y, scene.Ground);
        Assert.True(Vector3.Distance(coverAt, mouth) < 0.35f, $"the drawn canister mouth ({coverAt}) is the TKB's muzzle ({mouth})");
        var missile = Assert.Single(scene.Projectiles.Resolve());
        Assert.Equal(MunitionShape.Missile, missile.Look.Shape);
        Assert.True(missile.Look.SmokeTrail);
        Assert.True(Diff(before, after).Count > 1500, $"the frame of the shot differs from the frame before it: {Diff(before, after).Count} px");
        Assert.True(File.Exists(shot));
        // and the launcher shows 3 rounds: the fired one's cover is off in this very frame (ammunition 4 → 3 as the shot is raised)
        scene.Render();
        Assert.Equal(3, LauncherLoad.RoundsLeft(world, scene.Sam, 0));
    }

    // ── pixels ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Pixels(Color[] Colours, int W, int H);

    private sealed record DiffStat(int Count, int MinY, int MaxY);

    private static Pixels ScreenOf(UiFrameSession frame, MapCanvas canvas, Action? before, Action render, string? save = null)
    {
        before?.Invoke();
        render();
        if (save != null) frame.Screenshot(save);
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

    private static DiffStat Diff(Pixels a, Pixels b)
    {
        int n = 0, minY = int.MaxValue, maxY = -1;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 6) continue;
            n++;
            int y = i / a.W;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        return new DiffStat(n, minY, maxY);
    }

    private static Vector2 OpenGround(Fdp.Toolkit.Terrain.TerrainWorld terrain, IWorldQuery query)
    {
        var mid = (terrain.BoundsMin + terrain.BoundsMax) / 2f;
        for (float r = 0f; r < 400f; r += 6f)
            for (int k = 0; k < 16; k++)
            {
                float a = k * MathF.PI / 8f;
                var p = mid + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                bool open = true;
                for (float dx = -30f; dx <= 15f && open; dx += 3f)
                    for (float dy = -12f; dy <= 14f && open; dy += 3f)
                        open = MathF.Abs(query.SurfaceZ(p.X + dx, p.Y + dy, 0) - query.GroundHeightAt(p.X + dx, p.Y + dy)) < 0.2f;
                if (open) return p;
            }
        return mid;
    }
}
