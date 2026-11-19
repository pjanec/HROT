using System.Diagnostics;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Map3D;
using Xunit;
using Xunit.Abstractions;
using Pixels = Hrot.Presentation.Tests.Map3D.AirCombatLooksRail.Pixels;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-1984</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md §7 NV-1…NV-3, ALLOC, COST; mockup <c>docs/UX/sensor-view.svg</c> row A) — the NIGHT-VISION
/// sensor view on a REAL GL frame: the canvas draws its 3-D layers into the post-process' target and the processed picture to the screen. Asserted RELATIVE
/// (never exact colours): the grain grows as the light falls, a self-luminous fire saturates and blooms while lighting nothing around it, a bright source pulls
/// the automatic gain down, and a steady sensor frame allocates nothing more than the normal one. ⛔ SKIPS without a display:
/// <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~SensorViewFrameRail</c>; <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> keeps the frames (<c>nv-*.png</c>, never in git).
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class SensorViewFrameRail
{
    private readonly ITestOutputHelper _out;
    public SensorViewFrameRail(ITestOutputHelper output) => _out = output;

    private sealed class NvScene : IDisposable
    {
        public readonly AirCombatLooksRail.JetScene S = new();
        public readonly SensorView View;

        public NvScene()
        {
            View = new SensorView(S.Switch, bus: null);
            S.Canvas.PostProcess = View.Post;
            S.Canvas.AddLayer(View);
            S.Bodies.UnitHeat = View.UnitHeat;   // ⭐ CE-1985 — as AttachMapLayers wires it
            if (!S.World.IsComponentTypeRegistered<FireState>()) S.World.RegisterComponent<FireState>();
            S.Look(new Vector3(S.Spot.X, S.Spot.Y, S.Ground), 140f, 0.6f, -0.45f);
        }

        /// <summary>Lights the scene for <paramref name="sample"/> as <see cref="WeatherSkyLayer"/> does, then renders (settled).</summary>
        public void Light(in WeatherSample sample, int frames = 6)
        {
            for (int i = 0; i < frames; i++)
            {
                S.Switch.Camera3D.Atmosphere = View.Mode == SceneSensor.None ? null : View.Apply(sample, true, SceneAtmosphere.Default, 50.0, 14.4);
                if (View.Mode == SceneSensor.None) View.Apply(sample, true, SceneAtmosphere.Default, 50.0, 14.4);
                S.Render();
            }
        }

        public Pixels Screen() => AirCombatLooksRail.ScreenOf(S);
        public void Shot(string name) => S.Frame.Screenshot(Path.Combine(AirCombatLooksRail.ShotsDir(), name));

        /// <summary>A fire of <paramref name="radius"/> m at <paramref name="offset"/> from the spot, burning for a minute from now.</summary>
        public Entity Fire(Vector2 offset, float radius)
        {
            var e = S.World.CreateEntity();
            S.World.AddComponent(e, new SimTransform { Position = new Vector3(S.Spot.X + offset.X, S.Spot.Y + offset.Y, S.Ground), Rotation = Quaternion.Identity });
            var f = new FireState();
            f.TryAdd(new FireSpot(0f, 0f, 0.5f, radius, (SimInstant)(S.Now - 1.0), 60f, 1f), S.Now - 1.0);
            S.World.AddComponent(e, f);
            return e;
        }

        public void Dispose() => S.Dispose();
    }

    private static WeatherSample FullMoon => new() { SunElevationDeg = -40, MoonElevationDeg = 55, MoonAzimuthDeg = 180, MoonIllumination = 1f, VisibilityM = float.PositiveInfinity };
    private static WeatherSample Starlight => new() { SunElevationDeg = -40, MoonElevationDeg = -20, MoonIllumination = 0.1f, VisibilityM = float.PositiveInfinity };

    private static float Lum(Raylib_cs.Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

    /// <summary>The mean |difference| of luminance between two frames of the same static scene inside the centre box — the frame-to-frame GRAIN.</summary>
    private static float FrameToFrame(Pixels a, Pixels b)
    {
        double sum = 0; int n = 0;
        for (int y = a.H / 4; y < a.H * 3 / 4; y += 2)
            for (int x = a.W / 4; x < a.W * 3 / 4; x += 2)
            { sum += Math.Abs(Lum(a.Colours[y * a.W + x]) - Lum(b.Colours[y * a.W + x])); n++; }
        return (float)(sum / n);
    }

    private static float MeanIn(Pixels p, int x0, int y0, int x1, int y1)
    {
        double sum = 0; int n = 0;
        for (int y = Math.Max(0, y0); y < Math.Min(p.H, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(p.W, x1); x++) { sum += Lum(p.Colours[y * p.W + x]); n++; }
        return n == 0 ? 0f : (float)(sum / n);
    }

    /// <summary>⭐ NV-1 — the grain grows as the light falls: the frame-to-frame difference of a static scene under starlight is many times the full moon's
    /// (noise σ ∝ 1/√lux), and with the noise off both are ~0.</summary>
    [SkippableFact]
    public void NV1_TheGrainGrowsAsTheLightFalls()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = new NvScene();
        n.View.SetMode(SceneSensor.NightVision);
        n.Light(FullMoon); var m1 = n.Screen(); n.Light(FullMoon, 1); var m2 = n.Screen();
        n.Shot("nv-full-moon.png");
        n.Light(Starlight); var s1 = n.Screen(); n.Light(Starlight, 1); var s2 = n.Screen();
        n.Shot("nv-starlight.png");
        float moon = FrameToFrame(m1, m2), stars = FrameToFrame(s1, s2);
        _out.WriteLine($"grain: full moon {moon:0.0000}, starlight {stars:0.0000}; lux {n.View.Lux}");
        Assert.True(n.View.Post.Frames > 0, "the post-process ran");
        // 🔴 seen red (`2026-11-19`): the halving chain's 1×1 target left rlgl's framebuffer at 1×1 — the next pass culled every body as Hidden
        Assert.Equal(Raylib_cs.Raylib.GetScreenHeight(), Raylib_cs.Rlgl.GetFramebufferHeight());
        Assert.True(stars > 3f * moon, $"starlight grain {stars:0.0000} > 3 × full-moon grain {moon:0.0000}");
        n.View.Noise = false;
        n.Light(Starlight); var q1 = n.Screen(); n.Light(Starlight, 1); var q2 = n.Screen();
        Assert.True(FrameToFrame(q1, q2) < 0.01f, "with the noise off a static scene is static");
    }

    /// <summary>⭐ NV-2 — a fire is SEEN, never LIGHTING: under night vision its core saturates (≥ 0.9) and its halo brightens the pixels just outside it,
    /// while a region far from it is unchanged (the noise and the automatic gain off, so only the fire differs). And the scene around peaks below the bloom
    /// (a lit surface never blooms). 🔴 Seen red while building (`2026-11-19`): with the hot puffs drawn translucent the core read 0.83 and the ring did
    /// not brighten; with the halo's sampler unbound the ring moved +0.02 only.</summary>
    [SkippableFact]
    public void NV2_AFire_SaturatesAndBlooms_AndLightsNothingAroundIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = new NvScene();
        n.View.SetMode(SceneSensor.NightVision);
        n.View.Noise = false; n.View.AutoGain = false;
        n.View.Palette = 1;   // white (P45): its full scale reads ~0.96 luminance (the green phosphor's top is ~0.81)
        n.S.Look(new Vector3(n.S.Spot.X, n.S.Spot.Y, n.S.Ground), 25f, 0.6f, -0.35f);
        n.Light(FullMoon);
        var before = n.Screen();
        var centre = new Vector2(before.W / 2f, before.H / 2f);
        n.Fire(Vector2.Zero, 2.5f);
        n.Light(FullMoon);
        var after = n.Screen();
        n.Shot("nv-fire.png");
        Assert.True(n.S.Effects.Drawn > 0, "the fire is drawn");

        float core = 0f;
        for (int y = (int)centre.Y - 40; y < centre.Y + 10; y++)
            for (int x = (int)centre.X - 25; x < centre.X + 25; x++) core = MathF.Max(core, Lum(after.Colours[y * after.W + x]));
        float sceneMax = 0f;
        for (int i = 0; i < before.Colours.Length; i++) sceneMax = MathF.Max(sceneMax, Lum(before.Colours[i]));
        // the halo: a ring 30–60 px out from the fire's base, brighter than it was
        float ringBefore = MeanIn(before, (int)centre.X + 30, (int)centre.Y - 60, (int)centre.X + 60, (int)centre.Y + 0);
        float ringAfter = MeanIn(after, (int)centre.X + 30, (int)centre.Y - 60, (int)centre.X + 60, (int)centre.Y + 0);
        // far away: the top-left quarter, unchanged
        float farBefore = MeanIn(before, 20, 20, before.W / 4, before.H / 4), farAfter = MeanIn(after, 20, 20, after.W / 4, after.H / 4);
        _out.WriteLine($"core {core:0.000}, scene max before {sceneMax:0.000}, ring {ringBefore:0.000} → {ringAfter:0.000}, far {farBefore:0.000} → {farAfter:0.000}");
        Assert.True(core >= 0.9f, $"the fire saturates the tube: core {core:0.000}");
        Assert.True(sceneMax < core, $"the lit scene stays below the fire (scene max {sceneMax:0.000})");
        Assert.True(ringAfter > ringBefore + 0.03f, $"the halo brightens the ring round it: {ringBefore:0.000} → {ringAfter:0.000}");
        Assert.True(MathF.Abs(farAfter - farBefore) < 0.01f, $"it lights nothing far from it: {farBefore:0.000} → {farAfter:0.000}");
    }

    /// <summary>⭐ NV-3 — the automatic gain: a big fire in view pulls the rest of the picture down (the tube's average), and with the automatic gain
    /// off it does not. 🔴 Seen red while building (`2026-11-19`): with the mean read through mipmaps, then through a chain of ODD sizes (5×3 → 2×1 sampled
    /// past the fires), the far region stayed exactly 0.194 → 0.194.</summary>
    [SkippableFact]
    public void NV3_ABrightSourceInView_PullsTheAutomaticGainDown()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = new NvScene();
        n.View.SetMode(SceneSensor.NightVision);
        n.View.Noise = false;
        n.S.Look(new Vector3(n.S.Spot.X, n.S.Spot.Y, n.S.Ground), 70f, 0.6f, -0.5f);
        n.Light(FullMoon);
        var before = n.Screen();
        float meanBefore = n.View.Post.ProbeMean();
        n.Fire(new Vector2(0f, 0f), 12f);
        n.Fire(new Vector2(18f, 6f), 12f);
        n.Light(FullMoon);
        var after = n.Screen();
        n.Shot("nv-gain.png");
        Assert.True(n.S.Effects.Drawn > 0, "the fires are drawn");
        float farBefore = MeanIn(before, 20, before.H * 3 / 4, before.W / 4, before.H - 20), farAfter = MeanIn(after, 20, after.H * 3 / 4, after.W / 4, after.H - 20);
        _out.WriteLine($"far region with automatic gain: {farBefore:0.000} → {farAfter:0.000}; the picture's mean {meanBefore:0.000} → {n.View.Post.ProbeMean():0.000}");
        Assert.True(farAfter < farBefore * 0.9f, $"the gain dims the rest: {farBefore:0.000} → {farAfter:0.000}");
    }

    /// <summary>⭐ ALLOC + COST — a steady night-vision frame allocates nothing the normal frame does not (R-2084), and its cost is reported (the design's
    /// budget: +2 ms at 1080p on real hardware; the CI VM's software GL is slower and only reported).</summary>
    [SkippableFact]
    public void NV_ASteadySensorFrame_AllocatesNothingMore_AndItsCostIsReported()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = new NvScene();
        var sample = FullMoon;
        long Alloc(int frames)
        {
            n.Light(sample, 4);
            long a = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                var atm = n.View.Apply(sample, true, SceneAtmosphere.Default);
                n.S.Switch.Camera3D.Atmosphere = n.View.Mode == SceneSensor.None ? null : atm;
                n.S.Render();
            }
            return GC.GetAllocatedBytesForCurrentThread() - a;
        }
        double Ms(int frames)
        {
            n.Light(sample, 4);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) n.S.Render();
            return sw.Elapsed.TotalMilliseconds / (frames * 2);   // Render draws two frames
        }
        long off = Alloc(20); double offMs = Ms(20);
        n.View.SetMode(SceneSensor.NightVision);
        long nv = Alloc(20); double nvMs = Ms(20);
        _out.WriteLine($"20 frames: off {off} B, night vision {nv} B; per frame off {offMs:0.0} ms, night vision {nvMs:0.0} ms (+{nvMs - offMs:0.0})");
        Assert.True(nv <= off, $"a steady night-vision frame allocates nothing more: {nv} B vs {off} B over 20 frames");
    }

    // ── ⭐ CE-1985 — thermal (DESIGN_Sensor_View §5, §7 TH) ─────────────────────────────────────────────────────────────────────────────────

    private static WeatherSample ClearNight => new()
    {
        Utc = new DateTime(2026, 6, 21, 1, 0, 0, DateTimeKind.Utc), SunElevationDeg = -20, GroundTemperatureC = 8f, TemperatureC = 8f,
        DiurnalPhase = -0.8f, CloudOctas = 0f, CloudOctasAbove = 0f, VisibilityM = float.PositiveInfinity, Wind = new Vector2(2f, 0f),
    };

    /// <summary>A truck at the spot, nose along +x, with its type's thermal data (a wheeled engine, hot end at the front) and its engine <paramref name="state"/>.</summary>
    private static Entity Truck(NvScene n, Fdp.Toolkit.Perception.Signatures.HeatSourceState state)
    {
        var w = n.S.World;
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.ThermalState>()) w.RegisterComponent<Fdp.Toolkit.Perception.Signatures.ThermalState>();
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.HeatSource>()) w.RegisterComponent<Fdp.Toolkit.Perception.Signatures.HeatSource>();
        var e = n.S.Jet(Hrot.Map.Common.TkbEntityTypes.Truck_HMMWV, 0f, 0f);
        w.AddComponent(e, new Fdp.Toolkit.Perception.Signatures.ThermalState
        {
            BaseSignature = 0.2f, IdleHeat = 0.35f, RunningHeat = 0.75f, ReferenceSpeed = 10f, RunningHeatPerSecond = 0.01f, CooldownPerSecond = 0.002f,
            HasPowerPlant = true, HotEnd = (byte)Fdp.Toolkit.Terrain.HeatMarkEnd.Front,
        });
        double now = n.S.Now;
        w.AddComponent(e, Fdp.Toolkit.Perception.Signatures.HeatSource.Of(state, (SimInstant)(now - 1800.0), (SimInstant)(now - 1800.0)));   // half an hour so
        return e;
    }

    private static NvScene Thermal()
    {
        var n = new NvScene();
        n.View.SetMode(SceneSensor.Thermal);
        n.View.Noise = false;
        n.View.SensorResolution = false;
        n.S.Look(new Vector3(n.S.Spot.X, n.S.Spot.Y, n.S.Ground), 18f, 1.2f, -0.45f);
        return n;
    }

    /// <summary>⭐ TH-4 — a truck whose engine has run for half an hour reads HOT at its front (its hot end) at night, hotter than the ground and than the
    /// same truck parked with its engine off; parked, its hull is no warmer than the ground (painted metal cools under a clear sky). The sky reads cold.
    /// 🔴 Red-proof candidate: <c>EntityBodyLayer3D.Submit</c> passing the part's own colour in the thermal pass ⇒ the truck reads as a garbage °C, the
    /// running / off frames do not differ.</summary>
    [SkippableFact]
    public void TH4_ARunningEngine_ReadsHotAtItsHotEnd_AParkedOneDoesNot_AndTheClearNightSkyIsCold()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = Thermal();
        var e = Truck(n, Fdp.Toolkit.Perception.Signatures.HeatSourceState.Off);
        n.Light(ClearNight);
        var off = n.Screen();
        n.Shot("th-truck-off.png");
        n.S.World.SetComponent(e, Fdp.Toolkit.Perception.Signatures.HeatSource.Of(Fdp.Toolkit.Perception.Signatures.HeatSourceState.Running,
            (SimInstant)(n.S.Now - 1800.0), (SimInstant)(n.S.Now - 1800.0)));
        n.Light(ClearNight);
        var run = n.Screen();
        n.Shot("th-truck-running.png");
        float hottestOff = 0f, hottestRun = 0f;
        for (int y = off.H / 4; y < off.H * 3 / 4; y++)
            for (int x = off.W / 4; x < off.W * 3 / 4; x++)
            { hottestOff = MathF.Max(hottestOff, Lum(off.Colours[y * off.W + x])); hottestRun = MathF.Max(hottestRun, Lum(run.Colours[y * run.W + x])); }
        float ground = MeanIn(off, 20, off.H - 120, 220, off.H - 20);
        float sky = MeanIn(off, off.W / 2 - 100, 60, off.W / 2 + 100, 90);
        _out.WriteLine($"hottest: engine off {hottestOff:0.000}, running {hottestRun:0.000}; ground {ground:0.000}; sky {sky:0.000}; air {n.View.AirC:0.0} C, sky {n.View.SkyC:0.0} C, hull {n.View.HullC:0.0} C, scale {n.View.LevelC:0.0}+{n.View.SpanC:0.0}");
        Assert.True(n.View.Post.Frames > 0, "the post-process ran");
        Assert.True(hottestRun > hottestOff + 0.15f, $"the running engine's hot end reads hot: {hottestOff:0.000} → {hottestRun:0.000}");
        Assert.True(hottestRun > ground + 0.2f, $"hotter than the ground: {hottestRun:0.000} vs {ground:0.000}");
        Assert.True(n.View.SkyC < n.View.AirC - 15f, "a clear night sky is cold");
    }

    /// <summary>⭐ TH-4 — a fire saturates in thermal (white-hot ≥ 0.9) and its halo blooms; the ground far away keeps its value.</summary>
    [SkippableFact]
    public void TH4_AFire_SaturatesInThermal()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = Thermal();
        n.View.AutoGain = true;
        n.Light(ClearNight);
        var before = n.Screen();
        n.Fire(Vector2.Zero, 2.5f);
        n.Light(ClearNight);
        var after = n.Screen();
        n.Shot("th-fire.png");
        float core = 0f;
        for (int y = after.H / 3; y < after.H * 2 / 3; y++)
            for (int x = after.W / 3; x < after.W * 2 / 3; x++) core = MathF.Max(core, Lum(after.Colours[y * after.W + x]));
        float farBefore = MeanIn(before, 20, 20, before.W / 4, before.H / 4), farAfter = MeanIn(after, 20, 20, after.W / 4, after.H / 4);
        _out.WriteLine($"thermal fire core {core:0.000}; far {farBefore:0.000} → {farAfter:0.000}");
        Assert.True(core >= 0.9f, $"the fire saturates: {core:0.000}");
        Assert.True(MathF.Abs(farAfter - farBefore) < 0.02f, $"the ground far from it keeps its value: {farBefore:0.000} → {farAfter:0.000}");
    }

    /// <summary>A vehicle's stand at the spot (nose along +x, hot end at the front): a 6 × 3 m mark whose engine ran <paramref name="engineOn"/> s and that it left
    /// <paramref name="leftAgo"/> s ago — the record <c>GroundHeatMarkRules.LeaveStand</c> writes, on its own cell entity.</summary>
    private static Entity Stand(NvScene n, double leftAgo, float engineOn = 1800f)
    {
        var w = n.S.World;
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Terrain.GroundHeatMarks>()) w.RegisterComponent<Fdp.Toolkit.Terrain.GroundHeatMarks>();
        var mark = new Fdp.Toolkit.Terrain.HeatMark(Fdp.Toolkit.Terrain.HeatMarkKind.VehicleStand, n.S.Spot, 0f, 6f, 3f, Fdp.Toolkit.Terrain.HeatMarkEnd.Front,
            (SimInstant)(n.S.Now - leftAgo - engineOn), (SimInstant)(n.S.Now - leftAgo), engineOn, Fdp.Toolkit.Terrain.ThermalPowerClass.Tank);
        var record = new Fdp.Toolkit.Terrain.GroundHeatMarks();
        Assert.True(Fdp.Toolkit.Terrain.GroundHeatMarkRules.Add(ref record, in mark, out _));
        var e = w.CreateEntity();
        w.AddComponent(e, record);
        return e;
    }

    /// <summary>⭐ TH-5 — a fresh vehicle stand reads warmer than the field around it in thermal, its hot end (the front third) warmest; once its power class'
    /// cooling time has passed it is gone. ⛔ The normal view never draws it.
    /// 🔴 Red-proof candidate: <c>EffectLayer.DrawHeatMarks3D</c> drawing with the normal blend ⇒ the mark replaces the ground with a near −40 °C code and
    /// reads COLD.</summary>
    [SkippableFact]
    public void TH5_AFreshVehicleStand_ReadsWarmerThanTheFieldAroundIt_HotEndWarmest_AndIsGoneOnceCold()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = Thermal();
        n.View.AutoGain = false;   // a fixed scale: the mark must not move the gain between the frames compared
        n.Light(ClearNight);
        var bare = n.Screen();
        Stand(n, leftAgo: 30.0);
        n.Light(ClearNight);
        var warm = n.Screen();
        n.Shot("th-stand.png");
        int marksDrawn = n.S.Effects.HeatMarksDrawn;
        // the mark's front third and its rear lie either side of the screen's centre column, along x (the camera looks along +x)
        var front = n.S.Switch.Camera3D.WorldToScreen(new Vector3(n.S.Spot.X + 2f, n.S.Spot.Y, n.S.Ground));
        var rear = n.S.Switch.Camera3D.WorldToScreen(new Vector3(n.S.Spot.X - 2f, n.S.Spot.Y, n.S.Ground));
        float Box(Pixels p, Vector2 at) => MeanIn(p, (int)at.X - 4, (int)at.Y - 4, (int)at.X + 4, (int)at.Y + 4);
        float fBare = Box(bare, front), fWarm = Box(warm, front), rBare = Box(bare, rear), rWarm = Box(warm, rear);
        float far = MeanIn(warm, 20, warm.H - 60, 120, warm.H - 20), farBare = MeanIn(bare, 20, bare.H - 60, 120, bare.H - 20);
        n.S.Now += Fdp.Toolkit.Terrain.GroundHeatMarkRules.ColdAfterSeconds(Fdp.Toolkit.Terrain.ThermalPowerClass.Tank) + 60.0;   // only time moves
        n.S.World.SetSingletonUnmanaged(new GlobalTime { TotalTime = n.S.Now });
        n.Light(ClearNight);
        var cold = n.Screen();
        float fCold = Box(cold, front);
        _out.WriteLine($"stand: marks drawn {marksDrawn}; front {fBare:0.000} → {fWarm:0.000} → cold {fCold:0.000}; rear {rBare:0.000} → {rWarm:0.000}; far {farBare:0.000} → {far:0.000}");
        Assert.Equal(1, marksDrawn);
        Assert.True(fWarm > fBare + 0.08f, $"the stand's hot end reads warmer than the bare field there: {fBare:0.000} → {fWarm:0.000}");
        Assert.True(rWarm > rBare + 0.03f, $"its cool part reads warmer too: {rBare:0.000} → {rWarm:0.000}");
        Assert.True(fWarm > rWarm + 0.02f, $"the hot end is the warmest: front {fWarm:0.000} vs rear {rWarm:0.000}");
        Assert.True(MathF.Abs(far - farBare) < 0.02f, $"the field away from it keeps its value: {farBare:0.000} → {far:0.000}");
        Assert.True(MathF.Abs(fCold - fBare) < 0.03f, $"once cold the mark is gone: {fBare:0.000} → {fCold:0.000}");
    }

    /// <summary>⭐ D-10 — the SPOT METER reads the °C the scene was drawn at under the pointer: on a fresh stand's hot end it reads the bare ground's °C plus
    /// exactly the stand's rise (<c>MarkWarmth</c> × <see cref="Hrot.UI.Common.Effects.EffectLayer.StandMarkK"/>, ± one code step) — the probe and the additive
    /// encoding checked against each other. 🔴 Red-proof: <c>SensorPostProcess</c> reading texture row <c>y</c> instead of <c>h − 1 − y</c> ⇒ the probe lands on the
    /// mirrored point (the sky) and the reading is no rise at all.</summary>
    [SkippableFact]
    public void TH_TheSpotMeter_ReadsTheGroundPlusTheStandsRise()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = Thermal();
        var at = n.S.Switch.Camera3D.WorldToScreen(new Vector3(n.S.Spot.X + 2f, n.S.Spot.Y, n.S.Ground));
        n.View.Pointer = () => at;
        n.View.Post.ProbePixel = at;   // as Update sets it in a host frame
        n.Light(ClearNight);
        float bare = n.View.SpotC;
        var e = Stand(n, leftAgo: 30.0);
        n.Light(ClearNight);
        float warm = n.View.SpotC;
        var mark = n.S.World.GetComponentRO<Fdp.Toolkit.Terrain.GroundHeatMarks>(e).Get(0);
        float rise = ThermalDriversOps.MarkWarmth(in mark, n.S.Now, Fdp.Toolkit.Weather.WeatherField.GlobalOf(n.S.World, out _))
                     * Hrot.UI.Common.Effects.EffectLayer.StandMarkK;
        _out.WriteLine($"spot meter at {at}: bare {bare:0.00} C, on the stand's hot end {warm:0.00} C; expected rise {rise:0.00} K; air {n.View.AirC:0.0} C");
        // and near the TOP of the picture: the clear night sky, read as its own cold °C (a probe that mirrored the rows would read the ground there)
        var sky = new Vector2(at.X, 30f);
        n.View.Pointer = () => sky;
        n.View.Post.ProbePixel = sky;
        n.Light(ClearNight);
        float skyC = n.View.SpotC;
        _out.WriteLine($"spot meter in the sky {skyC:0.0} C (the view's sky {n.View.SkyC:0.0} C)");
        Assert.False(float.IsNaN(bare), "the probe read the scene");
        Assert.InRange(bare, n.View.AirC - 25f, n.View.AirC + 15f);
        Assert.InRange(warm - bare, rise - 0.8f, rise + 0.8f);
        Assert.True(skyC < n.View.AirC - 10f, $"the sky reads cold: {skyC:0.0} C vs air {n.View.AirC:0.0} C");
    }

    /// <summary>⭐ D-10 — the selected observer's OUTLINES: with a unit carrying a thermal sensor (a sensor part, as a TKB unit carries it) primary-selected, a truck
    /// whose engine runs is bracketed and the same truck long parked is not — the AI sensor's own signature rule (<c>ThermalPerception.SignatureOf</c>).
    /// 🔴 Red-proof: <c>ThermalPerception.Reaches</c> comparing <c>&gt; min</c> against the BASE signature only ⇒ the running truck is not bracketed.</summary>
    [SkippableFact]
    public void TH_TheSelectedObserversOutlines_BracketWhatItsThermalSensorCanSee()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = Thermal();
        var w = n.S.World;
        n.View.World = () => w;
        n.View.Bodies = n.S.Bodies;
        var truck = Truck(n, HeatSourceState.Running);
        if (!w.IsComponentTypeRegistered<Hrot.Map.Common.Components.SelectionState>()) w.RegisterComponent<Hrot.Map.Common.Components.SelectionState>();
        try { w.RegisterManagedComponent<Fdp.Toolkit.Perception.Components.SensorCapability>(); } catch (InvalidOperationException) { }   // already registered
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorTag>()) w.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorTag>();
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
        var observer = w.CreateEntity();
        w.AddComponent(observer, new SimTransform { Position = new Vector3(n.S.Spot.X - 40f, n.S.Spot.Y, n.S.Ground), Rotation = Quaternion.Identity });
        w.AddComponent(observer, new Hrot.Map.Common.Components.SelectionState { IsSelected = true, IsPrimarySelection = true });
        var sensor = w.CreateEntity();
        w.AddComponent(sensor, new Fdp.Toolkit.Perception.Components.SensorTag());
        w.AddComponent(sensor, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = observer });
        var entry = new Fdp.Toolkit.Tkb.Domain.SensorEntryDto
        {
            Kind = Fdp.Toolkit.Perception.Components.SensorModality.Thermal,
            Thermal = new Fdp.Toolkit.Tkb.Domain.ThermalSensorDto { Range = 800f, MinSignature = 0.5f },
        };
        w.SetManagedComponent(sensor, new Fdp.Toolkit.Perception.Components.SensorCapability { Default = entry, Current = entry });
        // the hover readout over the truck (mockup row C): a 3-D pick by the drawn box, as AttachMapLayers registers it
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        w.AddComponent(truck, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4242 });
        n.S.Switch.Camera3D.Pickers.Add(new EntityBoxPicker(() => w, () => n.S.Tkb, n.S.Bodies));
        var over = n.S.Switch.Camera3D.WorldToScreen(n.S.Where(truck) + new Vector3(0f, 0f, 1f));
        n.View.Pointer = () => over;
        n.Light(ClearNight);
        int running = n.View.OutlinesDrawn;
        bool sawObserver = n.View.Observer == observer;
        var hovered = n.View.HoveredUnit;
        string r2 = n.View.ReadoutDrivers, r3 = n.View.ReadoutSignature;
        var box = n.View.ReadoutBox;
        n.Shot("th-outlines.png");
        var shot = n.Screen();
        float boxLum = MeanIn(shot, (int)box.X + 2, (int)box.Y + 2, (int)(box.X + box.Width) - 2, (int)(box.Y + box.Height) - 2);
        _out.WriteLine($"readout at {over}: box {box.X},{box.Y} {box.Width}x{box.Height}, its mean {boxLum:0.000}: {n.View.ReadoutName} | {r2} | {r3}");
        w.SetComponent(truck, HeatSource.Of(HeatSourceState.Off, (SimInstant)(n.S.Now - 7200.0), (SimInstant)(n.S.Now - 7200.0)));
        n.Light(ClearNight);
        int parked = n.View.OutlinesDrawn;
        _out.WriteLine($"outlines: running {running}, parked two hours {parked}; observer found {sawObserver}");
        Assert.True(sawObserver, "the primary-selected unit's thermal sensor part was found");
        Assert.Equal(truck, hovered);
        Assert.DoesNotContain("#", n.View.ReadoutName);   // no scenario name ⇒ its TKB type's
        Assert.Contains("engine running for 30 min", r2);
        Assert.Contains("SEEN (outlined)", r3);
        Assert.True(box.Width > 100 && boxLum < 0.3f, $"the readout's dark box is on the frame: {box.Width} px wide, mean {boxLum:0.000}");
        Assert.Equal(1, running);
        Assert.Equal(0, parked);
    }

    private static WeatherSample Noon => new()
    {
        Utc = new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc), SunElevationDeg = 60, SunAzimuthDeg = 180, IrradianceWm2 = 850f, GroundTemperatureC = 24f, TemperatureC = 22f,
        DiurnalPhase = 0.9f, CloudOctas = 0f, CloudOctasAbove = 0f, VisibilityM = float.PositiveInfinity, Wind = new Vector2(2f, 0f),
    };

    /// <summary>⭐ TH-1 (the roads half) — demo-city's SUMO roads read as ASPHALT in thermal, not as the concrete every unadapted lit draw falls back to: the spot
    /// meter on the middle of an asphalt triangle reads the asphalt class' apparent °C (the roads are flat: no sun-facing term). 🔴 Red-proof:
    /// <c>TrafficLayer3D</c> not setting its class ⇒ the road reads concrete's °C.</summary>
    [SkippableFact]
    public void TH1_TheSumoRoads_ReadAsAsphalt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var world = Hrot.Presentation.Tests.Map2D.TerrainLayer2DTests.DemoCity();
        var net = Hrot.Traffic.TrafficNetCache.Of(world)!;
        var query = Fdp.Toolkit.World.WorldQuery.Of(world)!;
        using var frame = UiFrameHarness.Begin(1280, 800);
        LitShader.Reset();
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null);
        var two = new Fdp.Toolkit.Vis2D.Components.MapCamera();
        canvas.Camera = two;
        var terrain3D = new TerrainLayer3D(() => Fdp.Toolkit.World.WorldQuery.RenderGeometryOf(world));
        canvas.AddLayer(terrain3D);
        var traffic = new TrafficLayer3D(() => world);
        canvas.AddLayer(traffic);
        var sw = new MapViewSwitch(canvas, two);
        sw.Camera3D.GroundHeight = (x, y) => query.GroundHeightAt(x, y);
        sw.Set(true, animate: false);
        var view = new SensorView(sw, bus: null);
        canvas.PostProcess = view.Post;
        canvas.AddLayer(view);
        view.SetMode(SceneSensor.Thermal);
        view.Noise = false; view.SensorResolution = false;
        // the longest straight asphalt triangle: look straight down at it from 60 m
        Vector3 best = default; float bestArea = 0f;
        foreach (var m in net.Roads.Meshes)
        {
            if (m.Kind != Sim.Scene.SceneKind.Asphalt || m.Sidewalk) continue;
            for (int t = 0; t + 2 < m.Indices.Length; t += 3)
            {
                var a = m.Vertices[m.Indices[t]]; var b = m.Vertices[m.Indices[t + 1]]; var c = m.Vertices[m.Indices[t + 2]];
                float area = Vector3.Cross(b - a, c - a).Length();
                if (area > bestArea) { bestArea = area; best = (a + b + c) / 3f; }
            }
        }
        Assert.True(bestArea > 4f, $"anti-vacuity: an asphalt triangle to look at ({bestArea:0.0} m2)");
        sw.Camera3D.Pose = new CameraPose(best, 60f, 0.3f, -1.5f);
        void Frames(int k) { for (int i = 0; i < k; i++) { sw.Camera3D.Atmosphere = view.Apply(Noon, true, SceneAtmosphere.Default, 50.0, 14.4); frame.Step(canvas.Draw); } }
        Frames(2);
        Map3DFrameRail.StepUntil(frame, canvas, () => terrain3D.TriangleCount > 0 && traffic.TriangleCount > 1000);
        var at = sw.Camera3D.WorldToScreen(best);
        view.Post.ProbePixel = at;
        Frames(4);
        float road = view.Post.HasProbed ? LitShader.DecodeC(view.Post.Probed) : float.NaN;
        float asphalt = view.ApparentOf(ThermalClassId.Asphalt), concrete = view.ApparentOf(ThermalClassId.Concrete);
        frame.Screenshot(Path.Combine(AirCombatLooksRail.ShotsDir(), "th-roads.png"));
        _out.WriteLine($"road at {at}: {road:0.00} C; asphalt {asphalt:0.00} C, concrete {concrete:0.00} C, air {view.AirC:0.0} C");
        Assert.True(MathF.Abs(asphalt - concrete) > 1.5f, $"anti-vacuity: the two classes differ at noon ({asphalt:0.0} vs {concrete:0.0})");
        // ±2 K: the road's own slope / normal against a 60° sun moves it off the flat class value (📐 measured −1.45 K); the classes are ~7 K apart
        Assert.InRange(road, asphalt - 2f, asphalt + 2f);
        Assert.True(MathF.Abs(road - asphalt) < MathF.Abs(road - concrete), $"nearer asphalt than concrete: {road:0.0} vs {asphalt:0.0} / {concrete:0.0}");
    }

    /// <summary>⭐ ALLOC — a steady thermal frame allocates nothing the normal frame does not (the class table once, the forcing once a day).</summary>
    [SkippableFact]
    public void TH_ASteadyThermalFrame_AllocatesNothingMore()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var n = new NvScene();
        var e = Truck(n, Fdp.Toolkit.Perception.Signatures.HeatSourceState.Running);
        var sample = ClearNight;
        long Alloc(int frames)
        {
            n.Light(sample, 4);
            long a = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                var atm = n.View.Apply(sample, true, SceneAtmosphere.Default, 50.0, 14.4);
                n.S.Switch.Camera3D.Atmosphere = n.View.Mode == SceneSensor.None ? null : atm;
                n.S.Render();
            }
            return GC.GetAllocatedBytesForCurrentThread() - a;
        }
        // ⭐ CE-1985 part 2 — the per-frame paths of the thermal pass's aids run too: a warm heat mark drawn, the spot meter's one-texel read-back,
        //   the outline scan (no observer selected: the scan finds none)
        Stand(n, leftAgo: 30.0);
        var at = n.S.Switch.Camera3D.WorldToScreen(new Vector3(n.S.Spot.X + 2f, n.S.Spot.Y, n.S.Ground));
        n.View.Pointer = () => at;
        n.View.World = () => n.S.World;
        n.View.Bodies = n.S.Bodies;
        double Ms(int frames)
        {
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < frames; i++)
            {
                n.View.Update(0.016f);
                var atm = n.View.Apply(sample, true, SceneAtmosphere.Default, 50.0, 14.4);
                n.S.Switch.Camera3D.Atmosphere = n.View.Mode == SceneSensor.None ? null : atm;
                n.S.Render();
            }
            return Stopwatch.GetElapsedTime(t0).TotalMilliseconds / (frames * 2);   // Render draws two frames
        }
        long off = Alloc(20); double offMs = Ms(20);
        n.View.SetMode(SceneSensor.Thermal);
        n.S.Bodies.UnitHeat = n.View.UnitHeat;
        n.View.Post.ProbePixel = at;
        long th = Alloc(20); double thMs = Ms(20);
        Assert.True(n.S.Effects.HeatMarksDrawn == 1 && n.View.Post.HasProbed, "anti-vacuity: the mark was drawn and the spot meter read");
        _out.WriteLine($"per frame: off {offMs:0.0} ms, thermal {thMs:0.0} ms (+{thMs - offMs:0.0}) — the cloud VM's software GL");
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) n.View.UnitHeat(n.S.World, e);
        long unit = GC.GetAllocatedBytesForCurrentThread() - a0;
        _out.WriteLine($"20 frames: off {off} B, thermal {th} B; 20 × UnitHeat {unit} B");
        Assert.Equal(0, unit);   // 🔴 seen red while building: SimClock.Of's clock object, 24 B per unit per frame
        Assert.True(th <= off, $"a steady thermal frame allocates nothing more: {th} B vs {off} B over 20 frames");
    }
}
