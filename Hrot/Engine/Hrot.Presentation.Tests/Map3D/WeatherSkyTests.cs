using System.Numerics;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Hrot.UI.Common.Map3D;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — the weather and the time of day as the 3-D view shows them (WeatherSky.Of): the sun where
/// the field puts it, the light never below twilight, cloud dims the sun and greys the sky, a glow on the horizon at sunrise, the fog from
/// the visibility and never an edge at the far clip. 🔒 User, 2026-10-16: "never full darkness, just twilight instead of night".
/// </summary>
public sealed class WeatherSkyTests
{
    private static WeatherSample Sample(double elevation, double azimuth, float light, float octas = 0f, float visibility = 20000f)
        => new() { SunElevationDeg = elevation, SunAzimuthDeg = azimuth, RelativeLight = light, CloudOctasAbove = octas, VisibilityM = visibility };

    [Fact]
    public void CE1146_AClearNoon_IsTheMapsUsualDay_WithTheSunWhereTheFieldPutsIt()
    {
        var a = WeatherSky.Of(Sample(60, 180, 1f));
        Assert.Equal(WeatherSky.DayAmbient, a.Ambient, 3);
        Assert.InRange(a.SunLight, WeatherSky.DaySunLight * 0.99f, WeatherSky.DaySunLight);
        Assert.True(a.ToSun.Y < -0.4f && a.ToSun.Z > 0.8f, $"the sun is high in the SOUTH: {a.ToSun}");   // azimuth 180 = south (HROT y north)
        Assert.True(a.SunVisible);
        Assert.Equal(LitShader.SkyHorizon, a.SkyHorizon);

        var east = WeatherSky.Of(Sample(10, 90, 0.6f));
        Assert.True(east.ToSun.X > 0.9f, $"azimuth 90 = east: {east.ToSun}");
    }

    [Fact]
    public void CE1146_TheDarkestNight_IsStillTwilight_NoSun_AndTheSkyIsNotBlack()
    {
        var a = WeatherSky.Of(Sample(-40, 0, 0.001f));
        Assert.True(a.Ambient >= WeatherSky.NightAmbient - 1e-3f, $"ambient {a.Ambient}");
        Assert.Equal(0f, a.SunLight);
        Assert.False(a.SunVisible);
        Assert.True(a.SkyHorizon.B > 80 && a.SkyTop.B > 50, $"a dusk-blue sky, not black: {a.SkyTop} / {a.SkyHorizon}");
    }

    [Fact]
    public void CE1146_AnOvercast_DimsTheSun_HidesItsDisc_AndGreysTheSky()
    {
        var clear = WeatherSky.Of(Sample(50, 180, 1f));
        var overcast = WeatherSky.Of(Sample(50, 180, 0.3f, octas: 8f));
        Assert.True(overcast.SunLight < clear.SunLight * 0.3f, $"sun {overcast.SunLight} vs {clear.SunLight}");
        Assert.False(overcast.SunVisible);
        int spread(Raylib_cs.Color c) => System.Math.Max(c.R, System.Math.Max(c.G, c.B)) - System.Math.Min(c.R, System.Math.Min(c.G, c.B));
        Assert.True(spread(overcast.SkyTop) < spread(clear.SkyTop), "grey, not blue");
    }

    [Fact]
    public void CE1146_InAFog_TheSkyIsTheFogsGrey_AndTheSunGoes()
    {
        var clear = WeatherSky.Of(Sample(40, 180, 0.9f, visibility: 20000f));
        var fog = WeatherSky.Of(Sample(40, 180, 0.9f, visibility: 150f));
        int gap(Raylib_cs.Color a, Raylib_cs.Color b) => System.Math.Abs(a.R - b.R) + System.Math.Abs(a.G - b.G) + System.Math.Abs(a.B - b.B);
        Assert.True(gap(fog.SkyTop, fog.SkyHorizon) < gap(clear.SkyTop, clear.SkyHorizon) / 3, "one grey from top to horizon");
        Assert.False(fog.SunVisible);
        Assert.True(clear.SunVisible);
    }

    [Fact]
    public void CE1146_ASunrise_GlowsOnTheHorizon()
    {
        var sunrise = WeatherSky.Of(Sample(1, 90, 0.3f));
        var morning = WeatherSky.Of(Sample(30, 120, 0.9f));
        Assert.True(sunrise.SkyHorizon.R > sunrise.SkyHorizon.B, $"warm at sunrise: {sunrise.SkyHorizon}");
        Assert.True(morning.SkyHorizon.B > morning.SkyHorizon.R, $"blue by day: {morning.SkyHorizon}");
    }

    [Fact]
    public void CE1146_TheFog_FollowsTheVisibility_AndNeverEndsInAnEdgeAtTheFarClip()
    {
        var cam = new MapCamera3D { Distance = 5f };
        float fog = cam.FogDensityFor(WeatherSky.Of(Sample(30, 180, 1f, visibility: 200f)));
        Assert.Equal(SceneAtmosphere.FogFor(200f), fog, 6);
        Assert.True(System.MathF.Exp(-(200f * fog) * (200f * fog)) is > 0.04f and < 0.06f, "5 % contrast at the visibility");

        float clear = cam.FogDensityFor(WeatherSky.Of(Sample(30, 180, 1f, visibility: 50000f)));
        Assert.Equal(SceneAtmosphere.FogFor(cam.FarClip * 0.9f), clear, 6);   // clamped: faded fully before the far clip
        Assert.Equal(1.1f / (5f * 6f + 600f), cam.FogDensityFor(SceneAtmosphere.Default), 6);   // the free camera's haze, unchanged
    }
    /// <summary>
    /// ⭐ CE-1146 PR-1 (Map 3-D §6aa) — the precipitation overlay: none without precipitation; more drops for a heavier rate (a cap for a
    /// cloudburst); rain falls as streaks slanted WITH the wind across the view, snow as flakes; the same field at the same time (no state),
    /// moving with time; every drop on the screen. ✅ Red-proof: the slant's sign flipped ⇒ the streaks lean into the wind.
    /// </summary>
    [Fact]
    public void CE1146_PR1_Rain_AsStreaksWithTheWind_Snow_AsFlakes_AsManyAsTheRate()
    {
        Assert.Equal(0, Precipitation.CountOf(PrecipitationKind.None, 10f));
        Assert.True(Precipitation.CountOf(PrecipitationKind.Rain, 10f) > Precipitation.CountOf(PrecipitationKind.Rain, 1f));
        Assert.Equal(Precipitation.MaxDrops, Precipitation.CountOf(PrecipitationKind.Rain, 500f));

        var screen = new Vector2(1280f, 800f);
        var a = new List<(Vector2 A, Vector2 B, float Size)>();
        var b = new List<(Vector2 A, Vector2 B, float Size)>();
        Precipitation.Drops(PrecipitationKind.Rain, 4f, windAcross: 8f, time: 3f, screen, a);
        Assert.Equal(Precipitation.CountOf(PrecipitationKind.Rain, 4f), a.Count);
        Assert.All(a, d => Assert.True(d.Size == 0f && d.B.Y > d.A.Y && d.B.X > d.A.X, "a streak falling to the right with the wind"));
        Assert.All(a, d => Assert.True(d.A.X >= 0f && d.A.X <= screen.X && d.A.Y >= -20f && d.A.Y <= screen.Y + 20f, $"on screen: {d.A}"));
        Precipitation.Drops(PrecipitationKind.Rain, 4f, 8f, 3f, screen, b);
        Assert.Equal(a, b);                                                           // the same field at the same time
        Precipitation.Drops(PrecipitationKind.Rain, 4f, 8f, 3.05f, screen, b);
        Assert.NotEqual(a[0].A, b[0].A);                                             // and it falls
        Precipitation.Drops(PrecipitationKind.Rain, 4f, -8f, 3f, screen, b);
        Assert.All(b, d => Assert.True(d.B.X < d.A.X, "the other wind, the other slant"));
        Precipitation.Drops(PrecipitationKind.Snow, 2f, 0f, 3f, screen, b);
        Assert.All(b, d => Assert.True(d.Size > 0f, "flakes"));
    }
}
