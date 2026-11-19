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
            if (!S.World.IsComponentTypeRegistered<FireState>()) S.World.RegisterComponent<FireState>();
            S.Look(new Vector3(S.Spot.X, S.Spot.Y, S.Ground), 140f, 0.6f, -0.45f);
        }

        /// <summary>Lights the scene for <paramref name="sample"/> as <see cref="WeatherSkyLayer"/> does, then renders (settled).</summary>
        public void Light(in WeatherSample sample, int frames = 6)
        {
            for (int i = 0; i < frames; i++)
            {
                S.Switch.Camera3D.Atmosphere = View.Mode == SceneSensor.None ? null : View.Apply(sample, true, SceneAtmosphere.Default);
                if (View.Mode == SceneSensor.None) View.Apply(sample, true, SceneAtmosphere.Default);
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
    /// (noise σ ∝ 1/√lux), and with the noise off both are ~0. 🔴 Red-proof: <c>SensorView.Apply</c>'s σ held at its floor (0.02) ⇒ the ratio collapses to ~1.</summary>
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
        Assert.True(stars > 3f * moon, $"starlight grain {stars:0.0000} > 3 × full-moon grain {moon:0.0000}");
        n.View.Noise = false;
        n.Light(Starlight); var q1 = n.Screen(); n.Light(Starlight, 1); var q2 = n.Screen();
        Assert.True(FrameToFrame(q1, q2) < 0.01f, "with the noise off a static scene is static");
    }

    /// <summary>⭐ NV-2 — a fire is SEEN, never LIGHTING: under night vision its core saturates (≥ 0.9) and its halo brightens the pixels just outside it,
    /// while a region far from it is unchanged (the noise and the automatic gain off, so only the fire differs). And the scene around peaks below the bloom
    /// (a lit surface never blooms). 🔴 Red-proof: <c>SceneSensors.Emissive</c> returning the colour unchanged ⇒ the orange core stays below the threshold
    /// and the halo ring does not brighten.</summary>
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
    /// off it does not. 🔴 Red-proof: the final pass' <c>abc</c> held at 1 ⇒ the far region keeps its brightness with the fire in view.</summary>
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
        n.Fire(new Vector2(0f, 0f), 12f);
        n.Fire(new Vector2(18f, 6f), 12f);
        n.Light(FullMoon);
        var after = n.Screen();
        n.Shot("nv-gain.png");
        Assert.True(n.S.Effects.Drawn > 0, "the fires are drawn");
        float farBefore = MeanIn(before, 20, before.H * 3 / 4, before.W / 4, before.H - 20), farAfter = MeanIn(after, 20, after.H * 3 / 4, after.W / 4, after.H - 20);
        _out.WriteLine($"far region with automatic gain: {farBefore:0.000} → {farAfter:0.000}");
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
}
