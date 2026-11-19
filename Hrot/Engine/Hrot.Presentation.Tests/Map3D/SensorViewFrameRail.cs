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
        long off = Alloc(20);
        n.View.SetMode(SceneSensor.Thermal);
        n.S.Bodies.UnitHeat = n.View.UnitHeat;
        long th = Alloc(20);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) n.View.UnitHeat(n.S.World, e);
        long unit = GC.GetAllocatedBytesForCurrentThread() - a0;
        _out.WriteLine($"20 frames: off {off} B, thermal {th} B; 20 × UnitHeat {unit} B");
        Assert.Equal(0, unit);   // 🔴 seen red while building: SimClock.Of's clock object, 24 B per unit per frame
        Assert.True(th <= off, $"a steady thermal frame allocates nothing more: {th} B vs {off} B over 20 frames");
    }
}
