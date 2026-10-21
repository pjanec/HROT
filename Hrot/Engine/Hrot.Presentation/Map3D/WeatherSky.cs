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
    public static SceneAtmosphere Of(in WeatherSample s)
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
        return new SceneAtmosphere(top, horizon, toSun, ambient, sunLight, visibility, disc);
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
        camera.Atmosphere = at is { } point && _world() is { } world ? WeatherSky.Of(WeatherField.At(world, point)) : null;
    }

    public void Draw(RenderContext ctx) { }
    public void DrawOverlay(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
