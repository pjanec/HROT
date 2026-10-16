using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Weather;
using Hrot.SimHost;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests.Weather;

/// <summary>
/// ⭐ CE-1159 — the demo <c>scenarios/ua-weather-regions</c> (🔒 user, 2026-10-16: <i>"record a task to build such a demo"</i>), loaded
/// through the PRODUCTION scenario serializer (<see cref="HrotScenarioSerializerFactory"/>, strict about unknown components) into the
/// SimHost's component set: a clear evening west of the regions, the fog bank's centre at its own visibility, and between the fog and
/// the overcast regions a blend of the two (each region weighs 1 at its centroid and 0 at its edge, W-2).
/// 🔴 Red-proof: drop either region's outline from the file ⇒ the matching assertion fails.
/// </summary>
public sealed class WeatherRegionsDemoTests
{
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "scenarios", "ua-weather-regions"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("scenarios/ua-weather-regions not found above the test binary");
    }

    [Fact]
    public void CE1159_TheWeatherRegionsDemo_IsClearWest_FogInTheBank_AndBlendedBetween()
    {
        using var w = new EntityRepository();
        SimHostComponentRegistry.RegisterAll(w);
        HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry())
            .Deserialize(w, File.ReadAllText(Path.Combine(RepoRoot(), "scenarios", "ua-weather-regions", "scenario.json")));

        int regions = 0, outlined = 0;
        foreach (var e in w.Query().With<WeatherConditions>().With<SimTransform>().Build())
        {
            regions++;
            if (WeatherField.TryOutline(w, e, out _)) outlined++;
        }
        Assert.True(regions == 2 && outlined == 2, $"two Weather Regions with outlines: {regions} with weather + position, {outlined} outlined");
        Assert.True(ScenarioTime.TryClock(w, out var clock));
        Assert.Equal(new DateTime(2025, 6, 21, 19, 5, 0, DateTimeKind.Utc), clock.StartUtc);   // dusk at the terrain's origin

        var clear = WeatherField.At(w, new Vector3(60f, 300f, 2f));
        var fog = WeatherField.At(w, new Vector3(250f, 300f, 2f));
        var between = WeatherField.At(w, new Vector3(325f, 300f, 2f));
        Assert.True(clear.VisibilityM > 10000f, $"clear west of the regions: {clear.VisibilityM} m");
        Assert.True(fog.VisibilityM < 1000f, $"inside the fog bank: {fog.VisibilityM} m");
        Assert.InRange(between.VisibilityM, fog.VisibilityM, clear.VisibilityM);
        Assert.True(between.CloudOctas > clear.CloudOctas, $"the overcast darkens the blend: {between.CloudOctas} vs {clear.CloudOctas} octas");
    }
}
