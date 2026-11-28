using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Effects;
using Hrot.UI.Common.Map3D;
using Xunit;
using Xunit.Abstractions;
using Pixels = Hrot.Presentation.Tests.Map3D.AirCombatLooksRail.Pixels;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-159 – D-161; the acceptance spec is <c>designs/devices/mockups/laser-beam-visual.svg</c>) — the
/// laser BEAM and its SPOT on a REAL GL frame, per band and per sensor view, measured as the light the laser ADDS (the same frame with the laser on minus off):
/// band 0 to the eye and under night vision · band 2 not to the eye, faint under night vision · band 3 not to the eye, bright under night vision · band 1
/// nowhere · nothing under thermal · a hazy evening's beam brighter than a clear one's · a Preview spot drawn · an emitter that is off draws nothing.
/// ⛔ SKIPS without a display: <c>xvfb-run -a dotnet test … --filter FullyQualifiedName~LaserBeamFrameRail</c>; <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> keeps the frames
/// (<c>laser-*.png</c>, never in git).
/// 🔴 Red-proof (run): <see cref="LaserVisibility.Table"/> giving the designator 1 under night vision (not 0.25) ⇒
/// <see cref="D159_NightVision_SeesBands0And3Bright_Band2Faint_Band1Not"/> and <c>LaserLayer3DTests.D159_TheDesignator_IsFaintUnderNightVision</c> are red.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class LaserBeamFrameRail
{
    private readonly ITestOutputHelper _out;
    public LaserBeamFrameRail(ITestOutputHelper output) => _out = output;

    private sealed class Scene : IDisposable
    {
        public readonly AirCombatLooksRail.JetScene S = new();
        public readonly SensorView View;
        public readonly LaserLayer3D Lasers;
        public readonly Entity Emitter;
        private Entity _env;

        public Scene()
        {
            var w = S.World;
            foreach (var reg in new Action[] {
                () => { if (!w.IsComponentTypeRegistered<LaserPointing>()) w.RegisterComponent<LaserPointing>(); },
                () => { if (!w.IsComponentTypeRegistered<LaserSpot>()) w.RegisterComponent<LaserSpot>(); },
                () => { if (!w.IsComponentTypeRegistered<WeatherConditions>()) w.RegisterComponent<WeatherConditions>(); },
                () => { if (!w.IsComponentTypeRegistered<ScenarioClock>()) w.RegisterComponent<ScenarioClock>(); } }) reg();
            View = new SensorView(S.Switch, bus: null) { Noise = false, AutoGain = false };
            S.Canvas.PostProcess = View.Post;
            S.Canvas.AddLayer(View);
            Lasers = new LaserLayer3D(() => S.World, () => S.Switch.Camera3D);
            S.Canvas.AddLayer(Lasers);
            Weather(1_500f, 0.9f);
            // the emitter 30 m west of the spot, 1.6 m up, pointing east; its spot 30 m east of the spot (60 m away)
            var at = new Vector3(S.Spot.X - 30f, S.Spot.Y, S.Ground + 1.6f);
            Emitter = w.CreateEntity();
            w.AddComponent(Emitter, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            w.AddComponent(Emitter, new LaserPointing { On = 1, Band = LaserBands.Visible, Power = 1f, Seq = 1 });
            var spot = new LaserSpot { Count = 1, PointingSeq = 1 };
            spot.Points[0] = new LaserSpotPoint { Point = at + new Vector3(60f, 0f, 0f), Range = 60f, Strength = LaserSpots.StrengthAt(1f, 60f), Part = -1 };
            w.AddComponent(Emitter, spot);
            S.Look(new Vector3(S.Spot.X, S.Spot.Y, S.Ground + 1.6f), 70f, MathF.PI * 0.5f + 0.35f, -0.22f);
        }

        public void Weather(float visibilityM, float humidity)
        {
            var c = WeatherConditions.Standard; c.VisibilityM = visibilityM; c.RelativeHumidity = humidity;
            if (_env.IsNull)
            {
                _env = S.World.CreateEntity();
                S.World.AddComponent(_env, new ScenarioClock { StartUtcTicks = new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc).Ticks });
                S.World.AddComponent(_env, c);
            }
            else S.World.SetComponent(_env, c);
        }

        public void Set(byte band, bool on = true, bool preview = false)
        {
            var p = S.World.GetComponentRO<LaserPointing>(Emitter); p.Band = band; p.On = on ? (byte)1 : (byte)0;
            S.World.SetComponent(Emitter, p);
            var s = S.World.GetComponentRO<LaserSpot>(Emitter); s.Preview = preview ? (byte)1 : (byte)0;
            S.World.SetComponent(Emitter, s);
        }

        private static readonly WeatherSample Moon = new() { SunElevationDeg = -40, MoonElevationDeg = 55, MoonAzimuthDeg = 180, MoonIllumination = 1f, VisibilityM = float.PositiveInfinity, GroundTemperatureC = 12f, TemperatureC = 12f };

        public void Mode(SceneSensor m) => View.SetMode(m);

        public Pixels Screen()
        {
            return AirCombatLooksRail.ScreenOf(S.Frame, () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    S.Switch.Camera3D.Atmosphere = View.Mode == SceneSensor.None ? null : View.Apply(Moon, true, SceneAtmosphere.Default, 50.0, 14.4);
                    S.Render();
                }
            });
        }

        public void Shot(string name) => S.Frame.Screenshot(Path.Combine(AirCombatLooksRail.ShotsDir(), name));

        public void Dispose() => S.Dispose();
    }

    private static float Lum(Raylib_cs.Color c) => (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

    /// <summary>The light the laser ADDS: the sum of the luminance gained, pixel by pixel, between the frame with it off and with it on.</summary>
    private static float Added(Pixels off, Pixels on)
    {
        double sum = 0;
        for (int i = 0; i < on.Colours.Length; i++) sum += Math.Max(0f, Lum(on.Colours[i]) - Lum(off.Colours[i]) - 2f / 255f);
        return (float)sum;
    }

    /// <summary>The light <paramref name="band"/> adds under <paramref name="mode"/> (and its picture, when <paramref name="shot"/> is named).</summary>
    private float AddedBy(Scene sc, byte band, SceneSensor mode, string? shot = null, bool preview = false)
    {
        sc.Mode(mode);
        sc.Set(band, on: false);
        sc.Screen();   // settle: a view just switched (or a scene just built) still moves on its first frames — measured: thermal 11.9 of "added" light with nothing drawn
        var off = sc.Screen();
        sc.Set(band, on: true, preview: preview);
        var on = sc.Screen();
        if (shot != null) sc.Shot(shot);
        float a = Added(off, on);
        _out.WriteLine($"band {band} under {mode}{(preview ? " (preview)" : "")}: adds {a:0.0}");
        return a;
    }

    /// <summary>The naked eye sees band 0 — the beam and the spot — and none of the others.</summary>
    [SkippableFact]
    public void D159_TheEye_SeesOnlyBand0()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var sc = new Scene();
        float visible = AddedBy(sc, LaserBands.Visible, SceneSensor.None, "laser-eye-band0.png");
        Assert.True(visible > 30f, $"the visible beam adds light to the eye: {visible}");
        Assert.Equal(0f, AddedBy(sc, LaserBands.EyeSafeRangefinder, SceneSensor.None));
        Assert.Equal(0f, AddedBy(sc, LaserBands.Designator, SceneSensor.None, "laser-eye-band2.png"));
        Assert.Equal(0f, AddedBy(sc, LaserBands.NearInfrared, SceneSensor.None));
        Assert.Equal(0, sc.Lasers.EmittersDrawn);   // the last one (band 3) is not seen by the eye: nothing drawn for it
    }

    /// <summary>Night vision sees band 0 and band 3 bright, band 2 faint (well under a third of band 3's light), band 1 not at all.</summary>
    [SkippableFact]
    public void D159_NightVision_SeesBands0And3Bright_Band2Faint_Band1Not()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var sc = new Scene();
        float b0 = AddedBy(sc, LaserBands.Visible, SceneSensor.NightVision, "laser-nv-band0.png");
        float b2 = AddedBy(sc, LaserBands.Designator, SceneSensor.NightVision, "laser-nv-band2.png");
        float b3 = AddedBy(sc, LaserBands.NearInfrared, SceneSensor.NightVision, "laser-nv-band3.png");
        float b1 = AddedBy(sc, LaserBands.EyeSafeRangefinder, SceneSensor.NightVision);
        Assert.True(b0 > 30f, $"band 0 under night vision: {b0}");
        Assert.True(b3 > 30f, $"band 3 under night vision: {b3}");
        Assert.True(b2 > 1f, $"band 2 under night vision is seen: {b2}");
        Assert.True(b2 < b3 * 0.5f, $"band 2 ({b2}) is faint next to band 3 ({b3})");
        Assert.Equal(0f, b1);
    }

    /// <summary>Thermal sees no laser.</summary>
    [SkippableFact]
    public void D159_Thermal_SeesNoLaser()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var sc = new Scene();
        sc.View.SensorResolution = false;
        Assert.Equal(0f, AddedBy(sc, LaserBands.Visible, SceneSensor.Thermal, "laser-thermal-band0.png"));
        Assert.Equal(0f, AddedBy(sc, LaserBands.NearInfrared, SceneSensor.Thermal));
    }

    /// <summary>D-160 — a hazy evening's beam adds more light than a clear evening's (the spot is the same in both); a Preview spot is drawn; an emitter that is
    /// off draws nothing.</summary>
    [SkippableFact]
    public void D160_AHazyBeam_IsBrighter_APreviewSpotIsDrawn_AnOffLaserIsNot()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var sc = new Scene();
        sc.Weather(20_000f, 0.4f);
        float clear = AddedBy(sc, LaserBands.Visible, SceneSensor.None, "laser-eye-clear.png");
        sc.Weather(1_200f, 0.95f);
        float hazy = AddedBy(sc, LaserBands.Visible, SceneSensor.None, "laser-eye-hazy.png");
        Assert.True(hazy > clear * 1.5f, $"hazy {hazy} vs clear {clear}");
        Assert.True(AddedBy(sc, LaserBands.Visible, SceneSensor.None, preview: true) > 30f, "a Preview spot (paused) is drawn");
        sc.Mode(SceneSensor.None);
        sc.Set(LaserBands.Visible, on: false);
        var a = sc.Screen(); var b = sc.Screen();
        Assert.Equal(0f, Added(a, b));
        Assert.Equal(0, sc.Lasers.EmittersDrawn);
    }
}
