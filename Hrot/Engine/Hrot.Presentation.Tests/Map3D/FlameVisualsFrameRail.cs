using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Time;
using Hrot.Core.Tkb;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.UI.Common.Map3D;
using Xunit;
using Pixels = Hrot.Presentation.Tests.Map3D.AirCombatLooksRail.Pixels;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-4635</c> / <c>CE-4637</c> (flame weapons §7, A-1; mockup <c>docs/UX/flame-visuals.svg</c>) — the five pictures of the flame visuals on a REAL GL frame, each drawn where §6 says
/// and GONE when its state is out: a burning truck (a vehicle's three spots in ITS frame), a building record's and a napalm cell's spots, a flamethrower's jet, a burning man, scorched ground.
/// Method: the same camera, three frames — the baseline (nothing lit), lit, then out — and the pixels that differ are the picture: they must be many, centred on the point the camera
/// looks at (the spot's WORLD position, so a dropped vehicle rotation puts them elsewhere), and the out frame must equal the baseline again. The scorch must be DARKER than the
/// ground it lies on. ⛔ SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~FlameVisualsFrameRail</c>; <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> keeps the frames
/// (<c>flame-*.png</c>, never in git). 🔴 Red-proofs: see each test.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class FlameVisualsFrameRail
{
    private const long FlameTank = 0x6B21, FlameFuel = 0x6B22;

    private static AirCombatLooksRail.JetScene Scene()
    {
        var scene = new AirCombatLooksRail.JetScene();
        var w = scene.World;
        if (!w.IsComponentTypeRegistered<FireState>()) w.RegisterComponent<FireState>();
        if (!w.IsComponentTypeRegistered<WeaponPosture>()) w.RegisterComponent<WeaponPosture>();
        if (!w.IsComponentTypeRegistered<WeaponAmmo>()) w.RegisterComponent<WeaponAmmo>();
        if (!w.IsComponentTypeRegistered<EffectState>()) w.RegisterComponent<EffectState>();
        if (!w.IsComponentTypeRegistered<GroundHeatMarks>()) w.RegisterComponent<GroundHeatMarks>();
        var tank = new TkbTemplate("test flame tank", FlameTank);
        tank.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { Category = WeaponCategory.Flamethrower, AmmoGuid = FlameFuel, InitialAmmunition = 100 } } });
        scene.Tkb.Register(tank);
        var fuel = new TkbTemplate("test flame fuel", FlameFuel);
        fuel.AddDescriptor(new WarheadDto { Kind = WarheadKind.Incendiary, FlameRangeM = 40f, FlameHalfAngleDeg = 8f, FlameIntensity = 1f, BurnSeconds = 30f });
        scene.Tkb.Register(fuel);
        return scene;
    }

    /// <summary>The same spots with their burn OVER at sim 100 (they burned 50 s from 0): "out" without moving the clock — the sky and the light move with it.</summary>
    private static FireState Out(FireState f)
    {
        var o = new FireState();
        for (int i = 0; i < f.Count; i++) { var g = f.Get(i); o.TryAdd(new FireSpot(g.X, g.Y, g.Z, g.Radius, g.Since, 50f, g.Intensity), 0.0); }
        return o;
    }

    private static void At(AirCombatLooksRail.JetScene s, double t) => s.World.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });

    private static Entity Body(AirCombatLooksRail.JetScene s, long type, Vector3 at, float yaw)
    {
        var e = s.World.CreateEntity();
        s.World.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        s.World.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
        return e;
    }

    /// <summary>How many pixels differ between two frames, and where their centre is (screen px).</summary>
    private static (int Count, Vector2 Centre, Vector2 Min, Vector2 Max) Changed(Pixels a, Pixels b)
    {
        int n = 0; double sx = 0, sy = 0; var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 24) continue;
            int x = i % a.W, y = i / a.W;
            n++; sx += x; sy += y;
            min = Vector2.Min(min, new Vector2(x, y)); max = Vector2.Max(max, new Vector2(x, y));
        }
        return (n, n == 0 ? Vector2.Zero : new Vector2((float)(sx / n), (float)(sy / n)), min, max);
    }

    /// <summary>The centre of the FLAME-coloured changed pixels (bright orange / yellow — the smoke drifts downwind and the tongues rise, so the whole picture's centre is not the spot's).</summary>
    private static (int Count, Vector2 Centre) FlameColoured(Pixels a, Pixels b)
    {
        int n = 0; double sx = 0, sy = 0;
        for (int i = 0; i < a.Colours.Length; i++)
        {
            var p = a.Colours[i]; var q = b.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 24) continue;
            if (!(q.R > 190 && q.G > 70 && q.G < 225 && q.B < 110 && q.R - q.B > 110)) continue;
            n++; sx += i % a.W; sy += i / a.W;
        }
        return (n, n == 0 ? Vector2.Zero : new Vector2((float)(sx / n), (float)(sy / n)));
    }

    /// <summary>The three frames of one picture: <paramref name="lit"/> turns the state on, <paramref name="out_"/> off; asserts the picture is many pixels, centred where the camera looks
    /// (within <paramref name="centreTolerance"/> of the frame's width), and gone when out.</summary>
    private static (int Count, Vector2 Min, Vector2 Max, Pixels Lit, Pixels Base) Picture(AirCombatLooksRail.JetScene s, string name, Action lit, Action out_,
                                                                         int minPixels = 1500, float centreTolerance = 0.12f, bool flames = true)
    {
        for (int i = 0; i < 40; i++) s.Render();   // settle: the camera eases to its pose, so the baseline and the later frames see the same view
        var baseline = AirCombatLooksRail.ScreenOf(s);
        lit();
        var on = AirCombatLooksRail.ScreenOf(s);
        s.Frame.Screenshot(Path.Combine(AirCombatLooksRail.ShotsDir(), $"flame-{name}.png"));
        out_();
        var off = AirCombatLooksRail.ScreenOf(s);
        var shown = Changed(baseline, on);
        Assert.True(shown.Count >= minPixels, $"{name}: the picture is drawn — {shown.Count} px differ from the empty frame");
        var centre = new Vector2(baseline.W / 2f, baseline.H / 2f);
        // the camera looks at the spot's BASE: the picture's centre is across from it (the flames and the smoke rise above it, so only the horizontal is tight)
        Assert.True(Math.Abs(shown.Centre.X - centre.X) < Math.Max(centreTolerance, 0.12f) * baseline.W && shown.Min.Y - 0.05f * baseline.W < centre.Y && shown.Max.Y + 0.05f * baseline.W > centre.Y,
                    $"{name}: drawn where the state says (centre {shown.Centre}, box {shown.Min}..{shown.Max} vs the camera's {centre})");
        if (flames)
        {
            var fl = FlameColoured(baseline, on);
            Assert.True(fl.Count >= 100, $"{name}: flames (orange pixels) are drawn — {fl.Count}");
            Assert.True(Math.Abs(fl.Centre.X - centre.X) < centreTolerance * baseline.W, $"{name}: the FLAMES stand where the state says (their centre {fl.Centre} vs the camera's {centre})");
        }
        var gone = Changed(baseline, off);
        Assert.True(gone.Count < 40, $"{name}: gone when out — {gone.Count} px still differ");
        return (shown.Count, shown.Min, shown.Max, on, baseline);
    }

    /// <summary>⭐ U-2 — a burning truck: its three spots are in the HULL's frame, so with the hull turned 90° they stand to its side; the camera looks at the turned spots' centre.
    /// 🔴 Red-proof: the rotation dropped from <c>EffectLayer.HolderPoint</c> ⇒ the flames stand 5 m off the camera's centre ⇒ the centring assertion fails.</summary>
    [SkippableFact]
    public void Flame_ABurningTruck_ItsThreeSpotsStandWhereTheHullFrameSaysAndAreGoneWhenOut()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        At(s, 100.0);
        var at = new Vector3(s.Spot.X, s.Spot.Y, s.Ground);
        float yaw = MathF.PI / 2f;
        Body(s, TkbEntityTypes.Truck_HMMWV, at, yaw);
        var holder = s.World.CreateEntity();
        s.World.AddComponent(holder, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) });
        // the mockup's three spots on a truck, hull frame (x forward): over the cab, on the load, low at the bumper. A +90° hull turns hull (x, y) to world (−y, x): the mean of the three
        // (hull x = 1.2 m) stands 1.2 m up the world's +y from the hull's origin — and 1.7 m from where an UNTURNED frame would put it (the red-proof).
        var centre = at + new Vector3(0f, 1.2f, 0.9f);
        s.Look(centre, 10f, 0.4f, -0.2f);
        var fs = new FireState();
        fs.TryAdd(new FireSpot(1.4f, 0f, 1.4f, 2.5f, (SimInstant)0.0, 600f, 1f), 0.0);
        fs.TryAdd(new FireSpot(-0.2f, 0f, 1.2f, 3.0f, (SimInstant)0.0, 600f, 1f), 0.0);
        fs.TryAdd(new FireSpot(2.3f, 0f, 0.5f, 1.8f, (SimInstant)0.0, 600f, 0.9f), 0.0);
        var burnedOut = Out(fs);
        var shown = Picture(s, "1-truck", () => s.World.AddComponent(holder, fs), () => s.World.SetComponent(holder, burnedOut), centreTolerance: 0.06f);
        s.World.SetComponent(holder, fs); AirCombatLooksRail.ScreenOf(s);   // lit again
        Assert.Equal(6, s.Effects.Drawn);                  // three flames + three small smokes
        // the same picture in the 2-D map: the pass draws the same six items
        s.Switch.Set(false, animate: false);
        AirCombatLooksRail.ScreenOf(s);
        Assert.Equal(6, s.Effects.Drawn);
        s.Switch.Set(true, animate: false);
    }

    /// <summary>⭐ U-2 — a building record's spot and a napalm cell's spots (frame: the record's position), each picture centred on its spot and gone when out.</summary>
    [SkippableFact]
    public void Flame_ABuildingRecordAndANapalmCell_DrawTheirSpotsAtTheRecordsFrame_AndAreGoneWhenOut()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        At(s, 100.0);
        var site = new Vector3(s.Spot.X, s.Spot.Y, s.Ground);
        // a building's damage record, 20 m away from the napalm cell record
        var building = s.World.CreateEntity();
        s.World.AddComponent(building, new SimTransform { Position = site + new Vector3(-20f, 0f, 0f), Rotation = Quaternion.Identity });
        var cell = s.World.CreateEntity();
        s.World.AddComponent(cell, new SimTransform { Position = site + new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity });
        var storey = new FireState(); storey.TryAdd(new FireSpot(2f, 0f, 3f, 2.5f, (SimInstant)0.0, 600f, 1f), 0.0);
        var field = new FireState();
        field.TryAdd(new FireSpot(0f, 0f, 0f, 5f, (SimInstant)0.0, 600f, 1f), 0.0);
        field.TryAdd(new FireSpot(6f, 2f, 0f, 4f, (SimInstant)0.0, 600f, 0.8f), 0.0);
        field.TryAdd(new FireSpot(-5f, -3f, 0f, 4f, (SimInstant)0.0, 600f, 0.9f), 0.0);

        s.Look(site + new Vector3(-18f, 0f, 3f), 16f, 0.4f, -0.25f);
        Picture(s, "2a-building", () => s.World.AddComponent(building, storey), () => s.World.SetComponent(building, Out(storey)));
        At(s, 100.0);
        s.Look(site + new Vector3(20f, 0f, 1f), 26f, 0.4f, -0.3f);
        Picture(s, "2b-napalm-cell", () => s.World.AddComponent(cell, field), () => s.World.SetComponent(cell, Out(field)));
    }

    /// <summary>⭐ U-1 — a flamethrower's jet: lit while fuel was spent in the last 0.25 s; the changed pixels run along the jet (a long, thin picture), centred on its middle; out ⇒ gone.
    /// 🔴 Red-proof: <c>FlameJet</c> not drawn by the passes ⇒ no pixels.</summary>
    [SkippableFact]
    public void Flame_AJet_RunsFromTheMuzzleToItsAimPoint_AndIsGoneWhenOut()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        At(s, 100.2);
        var site = new Vector3(s.Spot.X, s.Spot.Y, s.Ground);
        var unit = Body(s, FlameTank, site + new Vector3(-12f, 0f, 0f), 0f);
        var aim = site + new Vector3(12f, 0f, 1.5f);
        s.World.AddComponent(unit, new WeaponAmmo { Count = 1, Load0 = FlameFuel, LastShotAt0 = double.NegativeInfinity, BurstStartedAt0 = double.NegativeInfinity });   // loaded, not firing
        s.World.AddComponent(unit, new WeaponPosture { Kind = WeaponPostureKind.Aimed, Target = Entity.Null, Point = aim, ActiveMount = 0 });
        s.Look(site + new Vector3(0f, 0f, 1.5f), 30f, MathF.PI / 2f, -0.05f);   // from the side
        var shown = Picture(s, "3-jet", () => s.World.SetComponent(unit, new WeaponAmmo { Count = 1, Load0 = FlameFuel, LastShotAt0 = 100.1, BurstStartedAt0 = 99.0 }),
                            () => s.World.SetComponent(unit, new WeaponAmmo { Count = 1, Load0 = FlameFuel, LastShotAt0 = 99.0, BurstStartedAt0 = 98.0 }), minPixels: 800, centreTolerance: 0.2f);
        Assert.True(shown.Max.X - shown.Min.X > 2.5f * (shown.Max.Y - shown.Min.Y), $"a long thin jet: {shown.Max - shown.Min}");
    }

    /// <summary>⭐ U-3 — a man on fire: flames on his body, centred on him; gone when his <c>BurningUntil</c> passes.</summary>
    [SkippableFact]
    public void Flame_ABurningMan_HasFlamesOnHisBody_AndTheyAreGoneWhenHeIsOut()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        At(s, 100.0);
        var at = new Vector3(s.Spot.X, s.Spot.Y, s.Ground);
        var man = Body(s, TkbEntityTypes.Infantry_Rifleman, at, 0.8f);
        s.World.AddComponent(man, new EffectState());
        s.Look(at + new Vector3(0f, 0f, 0.9f), 6f, 0.5f, -0.1f);
        Picture(s, "4-burning-man", () => s.World.SetComponent(man, new EffectState { BurningUntil = (SimInstant)130.0 }), () => s.World.SetComponent(man, new EffectState { BurningUntil = (SimInstant)90.0 }), minPixels: 500);
    }

    /// <summary>⭐ U-4 — scorched ground: a dark rectangle (length × width at its heading) draped on the ground, DARKER than the ground it lies on, gone when the mark is removed.</summary>
    [SkippableFact]
    public void Flame_ScorchedGround_IsADarkRectangleOnTheGround_AndIsGoneWhenTheMarkIsRemoved()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var s = Scene();
        At(s, 100.0);
        var rec = s.World.CreateEntity();
        var marks = new GroundHeatMarks { CellX = 1, CellY = 1, Count = 1 };
        marks.Set(0, new HeatMark(HeatMarkKind.BurnedArea, new Vector2(s.Spot.X, s.Spot.Y), 0.6f, 24f, 10f, HeatMarkEnd.NoHotEnd, (SimInstant)0.0, (SimInstant)60.0, 0f, ThermalPowerClass.NoPowerClass));
        var empty = marks; empty.Count = 0;
        s.World.AddComponent(rec, empty);
        s.Look(new Vector3(s.Spot.X, s.Spot.Y, s.Ground), 45f, 0.3f, -1.1f);   // from high up
        var shown = Picture(s, "5-scorch", () => s.World.SetComponent(rec, marks), () => s.World.SetComponent(rec, empty), minPixels: 4000, centreTolerance: 0.1f, flames: false);
        // darker than what it covers
        long before = 0, after = 0; int n = 0;
        for (int i = 0; i < shown.Base.Colours.Length; i++)
        {
            var p = shown.Base.Colours[i]; var q = shown.Lit.Colours[i];
            if (Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) <= 24) continue;
            before += p.R + p.G + p.B; after += q.R + q.G + q.B; n++;
        }
        Assert.True(after < before * 0.7, $"scorch is darker than the ground: {after / (3.0 * n):F0} vs {before / (3.0 * n):F0}");
    }
}
