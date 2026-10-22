using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — the weather and the time of day as the 3-D view shows them: one PURE mapping from
/// <see cref="WeatherSample"/> (backend's <see cref="WeatherField.At(EntityRepository, Vector3)"/>, weather design §5 S3) to the
/// <see cref="SceneAtmosphere"/> the lit shader and the sky draw with — the sun's direction and disc, the sky gradient with a glow on the
/// horizon at sunrise and sunset, the light (ambient + sun, dimmed by cloud), and the fog from the visibility.
/// 🔒 User, 2026-10-16: <i>"time of day (never full darkness, just twilight instead of night so we still see the scene), weapon flashes and
/// flares and explosions and similar light emitting stuff should stay bright no matter the ambient light level"</i> ⇒ the light never
/// falls below <see cref="TwilightFloor"/>, and nothing here touches the effects (they draw unlit, so they keep their full colour).
/// </summary>
public static class WeatherSky
{
    /// <summary>The share of the day's light the darkest night still shows (the user's "twilight instead of night").</summary>
    public const float TwilightFloor = 0.35f;
    /// <summary>The lit shader's ambient at the floor and at full day (the day value is the look the map always had).</summary>
    public const float NightAmbient = 0.30f, DayAmbient = 0.42f;
    /// <summary>The sun's share of the light at full day, clear sky.</summary>
    public const float DaySunLight = 0.58f;

    private static readonly Color DayTop = LitShader.SkyTop, DayHorizon = LitShader.SkyHorizon;
    private static readonly Color DuskTop = new(38, 50, 86, 255), DuskHorizon = new(78, 90, 122, 255);
    private static readonly Color Overcast = new(168, 172, 178, 255);
    private static readonly Color Glow = new(236, 150, 96, 255);
    private static readonly Color NoonSun = new(255, 250, 228, 255), LowSun = new(255, 176, 110, 255);

    /// <summary>The atmosphere for one weather sample.</summary>
    public static SceneAtmosphere Of(in WeatherSample s, ReadOnlySpan<Vector2> drift = default)
    {
        float light = Math.Clamp(float.IsFinite(s.RelativeLight) ? s.RelativeLight : 1f, 0f, 1f);
        float shown = TwilightFloor + (1f - TwilightFloor) * light;
        float cloud = Math.Clamp(s.CloudOctasAbove / 8f, 0f, 1f);
        float el = (float)(s.SunElevationDeg * Math.PI / 180.0), az = (float)(s.SunAzimuthDeg * Math.PI / 180.0);
        var toSun = Vector3.Normalize(new Vector3(MathF.Sin(az) * MathF.Cos(el), MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el)));

        float elDeg = (float)s.SunElevationDeg;
        float glow = Math.Clamp(1f - MathF.Abs(elDeg - 1f) / 8f, 0f, 1f) * (1f - 0.8f * cloud);   // sunrise / sunset, thinned by cloud
        var grey = Scale(Overcast, shown);
        var horizon = Mix(Mix(Mix(DuskHorizon, DayHorizon, light), grey, 0.75f * cloud), Glow, 0.55f * glow);
        var top = Mix(Mix(DuskTop, DayTop, light), Scale(Overcast, 0.85f * shown), 0.75f * cloud);
        float visibility = float.IsFinite(s.VisibilityM) && s.VisibilityM > 0f ? s.VisibilityM : 0f;
        float murk = visibility > 0f ? Math.Clamp((2000f - visibility) / 1800f, 0f, 1f) : 0f;   // fog and mist: the sky goes the fog's colour
        horizon = Mix(horizon, grey, 0.6f * murk);
        top = Mix(top, horizon, 0.85f * murk);

        float ambient = NightAmbient + (DayAmbient - NightAmbient) * light;
        float sunLight = elDeg > 0f
            ? DaySunLight * Math.Clamp(MathF.Sin(el) / 0.25f, 0f, 1f) * (1f - 0.75f * MathF.Pow(cloud, 3.4f))   // Kasten–Czeplak, as the field
            : 0f;
        var disc = elDeg > -1f && cloud < 0.85f && murk < 0.9f ? Mix(LowSun, NoonSun, Math.Clamp(elDeg / 20f, 0f, 1f)) : default;
        var atmosphere = new SceneAtmosphere(top, horizon, toSun, ambient, sunLight, visibility, disc);
        return atmosphere with { Clouds = CloudsOf(s, ambient, sunLight, drift) };
    }

    /// <summary>⭐ CE-1146 CL-2 (§6aa.CL) — each kind's look, texture tile (m) and shade (1 = white in full light).</summary>
    public static (CloudLook Look, float TileM, float Shade) LookOf(CloudKind kind) => kind switch
    {
        CloudKind.Stratus => (CloudLook.Sheet, 6000f, 0.88f),
        CloudKind.Cirrus => (CloudLook.Streaks, 8000f, 1f),
        CloudKind.Stratocumulus => (CloudLook.Puffy, 2000f, 0.94f),
        CloudKind.Cumulonimbus => (CloudLook.Puffy, 3000f, 0.62f),
        _ => (CloudLook.Puffy, 3000f, 1f),   // cumulus
    };

    /// <summary>
    /// ⭐ CE-1146 CL-1…CL-5 (§6aa.CL; 🔒 user, 2026-10-22: <i>"yes exactly that"</i>) — the sample's cloud deck as the layers the 3-D view
    /// draws: covering octas / 8 of the sky, each kind its look, white lit by the scene's ambient + sun (a night deck a dark grey), carried by
    /// <paramref name="drift"/> (m, per layer; the wind at its base × the sim time — <see cref="WeatherSkyLayer"/>). Null when clear.
    /// </summary>
    public static SceneCloudLayer[]? CloudsOf(in WeatherSample s, float ambient, float sunLight, ReadOnlySpan<Vector2> drift = default)
    {
        var deck = s.Clouds;
        int count = 0;
        for (int i = 0; i < deck.Count; i++) if (deck.Layer(i).Octas > 0) count++;
        if (count == 0) return null;
        float light = Math.Clamp((ambient + 0.85f * sunLight) / (DayAmbient + 0.85f * DaySunLight), 0.22f, 1f);
        var layers = new SceneCloudLayer[count];
        int n = 0;
        for (int i = 0; i < deck.Count; i++)
        {
            var l = deck.Layer(i);
            if (l.Octas == 0) continue;
            var (look, tile, shade) = LookOf(l.Kind);
            float k = light * shade;
            var tint = new Color((byte)(246 * k), (byte)(247 * k), (byte)(250 * k), (byte)255);
            layers[n++] = new SceneCloudLayer(l.BaseM, l.ThicknessM, Math.Clamp(l.Octas / 8f, 0f, 1f), look, tile, i < drift.Length ? drift[i] : Vector2.Zero, tint);
        }
        return layers;
    }

    internal static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)255);
    }

    private static Color Scale(Color c, float k)
        => new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), (byte)255);
}

/// <summary>
/// ⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — hands the 3-D camera the weather's atmosphere: in first person read at the EYE; ⭐ in the
/// free camera (since 2026-10-19, 🔒 user: <i>"just showing the same weather as in FPS as the first slice"</i> — the 2026-10-16 "first person
/// only" is superseded) read at the point it LOOKS AT, its fog starting there (<see cref="MapCamera3D.FogStart"/>) so what is looked at stays
/// clear. Every frame — <see cref="WeatherField"/> is allocation-free and pure. Another driver (none today) and the 2-D map get none.
/// </summary>
public sealed class WeatherSkyLayer : IMapLayer
{
    private readonly MapViewSwitch _view;
    private readonly Func<EntityRepository?> _world;

    public WeatherSkyLayer(MapViewSwitch view, Func<EntityRepository?> world)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public string Name => "WeatherSky";
    public int LayerBitIndex => -1;

    public void Update(float dt)
    {
        var camera = _view.Camera3D;
        // ⭐ CE-3289 — a driver that looks AT a point (the film director) reads the weather there, like the free camera
        Vector3? at = !_view.Is3D ? null : camera.Driver is FpsCameraDriver ? camera.Eye
                    : camera.Driver is null or { ViewsFromEye: false } ? camera.LookAt : null;
        _hasSample = false;
        if (at is { } point && _world() is { } world)
        {
            _sample = WeatherField.At(world, point);
            _hasSample = true;
            camera.Atmosphere = WeatherSky.Of(_sample, DriftOf(world, point, _sample));
        }
        else camera.Atmosphere = null;
    }

    private WeatherSample _sample;
    private bool _hasSample;
    private readonly Vector2[] _drift = new Vector2[WeatherConditions.MaxCloudLayers];

    /// <summary>⭐ CE-1146 CL-5 (§6aa.CL) — how far the wind AT EACH LAYER'S BASE has carried it by the SIM time (paused ⇒ still).</summary>
    private Vector2[] DriftOf(EntityRepository world, Vector3 point, in WeatherSample sample)
    {
        var deck = sample.Clouds;
        double t = Fdp.Toolkit.Time.SimClock.Of(world).TotalTime;
        for (int i = 0; i < _drift.Length; i++)
            _drift[i] = i < deck.Count ? WeatherField.WindAt(world, new Vector3(point.X, point.Y, deck.Layer(i).BaseM)) * (float)t : Vector2.Zero;
        return _drift;
    }
    private readonly List<(Vector2 A, Vector2 B, float Size)> _drops = new(Precipitation.MaxDrops);

    /// <summary>Drops drawn by the last frame (rails).</summary>
    public int DropsDrawn { get; private set; }

    public void Draw(RenderContext ctx) { }

    /// <summary>⭐ <c>CE-1146</c> PR-1 (docs/DESIGN_Map_3D_Mode.md §6aa) — the PRECIPITATION overlay, over the 3-D scene: the weather the
    /// camera reads (at the eye in first person, at the look-at point else) as screen-space rain streaks or snowflakes.</summary>
    public void DrawOverlay(RenderContext ctx)
    {
        DropsDrawn = 0;
        if (!_view.Is3D || !_hasSample || _sample.Precipitation == PrecipitationKind.None) return;
        var camera = _view.Camera3D;
        var size = camera.Viewport;
        var right = new Vector2(MathF.Sin(camera.Yaw), -MathF.Cos(camera.Yaw));   // the view's right, on the ground (Yaw from east, CCW)
        float across = Vector2.Dot(_sample.Wind, right);
        Precipitation.Drops(_sample.Precipitation, _sample.PrecipitationMmPerHour, across, (float)Raylib.GetTime(), size, _drops);
        var colour = Precipitation.Colour(_sample.Precipitation);
        foreach (var (a, b, r) in _drops)
        {
            if (r > 0f) Raylib.DrawCircleV(a, r, colour);
            else Raylib.DrawLineEx(a, b, 1.2f, colour);
        }
        DropsDrawn = _drops.Count;
    }

    /// <summary>The overlay draws in 3-D (the scene has no body of its own).</summary>
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}

/// <summary>
/// ⭐ <c>CE-1146</c> PR-1 (docs/DESIGN_Map_3D_Mode.md §6aa; 🔒 user, 2026-10-19: <i>"depending from what place the 3d map camera is looking, the
/// weather effects should be shown (fog density/distance, precipitation overlay...)"</i>) — rain, sleet and hail as short streaks falling fast,
/// slanted by the wind across the view; snow as small flakes drifting down; drizzle as faint fine streaks. As many as the rate asks
/// (<see cref="CountOf"/>), each at a fixed place of its own in a screen-sized loop, so the field is the same every frame at a time and costs
/// no state. Pure — the rail runs it.
/// </summary>
public static class Precipitation
{
    /// <summary>The most drops on screen (a cloudburst).</summary>
    public const int MaxDrops = 700;

    /// <summary>How many drops for <paramref name="kind"/> at <paramref name="mmPerHour"/>: a base for any precipitation, then by the rate.</summary>
    public static int CountOf(PrecipitationKind kind, float mmPerHour)
    {
        if (kind == PrecipitationKind.None) return 0;
        float rate = float.IsFinite(mmPerHour) ? Math.Max(0f, mmPerHour) : 0f;
        int n = kind switch
        {
            PrecipitationKind.Drizzle => 80 + (int)(40f * rate),
            PrecipitationKind.Snow => 120 + (int)(60f * rate),
            _ => 100 + (int)(45f * rate),
        };
        return Math.Clamp(n, 0, MaxDrops);
    }

    /// <summary>The drops at <paramref name="time"/> (s) on a <paramref name="screen"/>: a streak from A to B (Size 0), or a flake at A of
    /// radius Size. <paramref name="windAcross"/> (m/s, + to the right) slants the streaks and drifts the flakes.</summary>
    public static void Drops(PrecipitationKind kind, float mmPerHour, float windAcross, float time, Vector2 screen,
                             List<(Vector2 A, Vector2 B, float Size)> into)
    {
        into.Clear();
        int n = CountOf(kind, mmPerHour);
        if (n == 0 || screen.X < 1f || screen.Y < 1f) return;
        bool snow = kind == PrecipitationKind.Snow;
        float speed = snow ? 70f : kind == PrecipitationKind.Drizzle ? 700f : 1100f;   // px per s
        float length = snow ? 0f : kind == PrecipitationKind.Drizzle ? 9f : 18f;
        float slant = Math.Clamp(windAcross / 12f, -0.8f, 0.8f);                       // run per unit of fall
        for (int i = 0; i < n; i++)
        {
            float u = Hash(i * 2 + 1), v = Hash(i * 2 + 2), w = Hash(i * 7 + 3);
            float fall = (v * screen.Y + speed * (0.75f + 0.5f * w) * time) % (screen.Y + 40f) - 20f;
            float x = (u * screen.X + slant * fall + (snow ? 6f * MathF.Sin(time * 1.3f + 10f * u) : 0f)) % screen.X;
            if (x < 0f) x += screen.X;
            var a = new Vector2(x, fall);
            into.Add(snow ? (a, a, 1.2f + 1.6f * w) : (a, a + new Vector2(slant, 1f) * length * (0.7f + 0.6f * w), 0f));
        }
    }

    /// <summary>A drop's colour: rain blue-grey and see-through, drizzle fainter, snow white, sleet and hail paler.</summary>
    public static Color Colour(PrecipitationKind kind) => kind switch
    {
        PrecipitationKind.Snow => new Color(245, 248, 252, 200),
        PrecipitationKind.Drizzle => new Color(205, 214, 228, 70),
        PrecipitationKind.Sleet or PrecipitationKind.Hail => new Color(225, 232, 240, 140),
        _ => new Color(190, 202, 220, 110),
    };

    private static float Hash(int i)
    {
        uint h = (uint)i * 2654435761u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
