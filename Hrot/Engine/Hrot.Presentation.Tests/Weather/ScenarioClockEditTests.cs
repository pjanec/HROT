using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Events;
using Fdp.Toolkit.Weather;
using Hrot.UI.Common.Weather;
using Xunit;

namespace Hrot.Presentation.Tests.Weather;

/// <summary>
/// ⭐ CE-1161 (docs/designs/weather/DESIGN_Weather_And_Time.md §7 E-2) — the scenario start time is written DIRECTLY by the World
/// Environment's owner (the networkless editor always is) and asked of the owner otherwise. 📐 The JSON patch alone does nothing in the
/// networkless editor: no system there applies it.
/// </summary>
public sealed class ScenarioClockEditTests : IDisposable
{
    private readonly EntityRepository _w = new();

    public ScenarioClockEditTests()
    {
        _w.RegisterComponent<ScenarioClock>();
        _w.RegisterComponent<NetworkAuthority>();
        _w.RegisterComponent<NetworkIdentity>();
    }

    public void Dispose() => _w.Dispose();

    private Entity Environment()
    {
        var env = _w.CreateEntity();
        _w.AddComponent(env, ScenarioClock.Default);
        _w.AddComponent(env, new NetworkIdentity(5));
        return env;
    }

    [Fact]
    public void CE1161_TheOwner_WritesTheStartTimeAndOffset_Directly_AsTyped()
    {
        var env = Environment();
        Assert.True(ScenarioClockEdit.TryParse("2025-10-01 05:30", out var utc));
        Assert.True(ScenarioClockEdit.SetStartUtc(_w, utc));
        Assert.True(ScenarioClockEdit.SetOffsetMinutes(_w, 120));

        var clock = _w.GetComponentRO<ScenarioClock>(env);
        Assert.Equal(new DateTime(2025, 10, 1, 5, 30, 0, DateTimeKind.Utc), clock.StartUtc);
        Assert.Equal(120, clock.DisplayUtcOffsetMinutes);
        Assert.True(ScenarioClockEdit.TryRead(_w, out var s) && s.Direct);
        Assert.Equal(new DateTime(2025, 10, 1, 7, 30, 0), s.StartLocal);
    }

    [Fact]
    public void CE1161_ANonOwner_AsksTheOwner_AndLeavesItsCopyAlone()
    {
        var env = Environment();
        _w.AddComponent(env, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));

        Assert.True(ScenarioClockEdit.SetStartUtc(_w, new DateTime(2025, 10, 1, 5, 30, 0, DateTimeKind.Utc)));

        Assert.Equal(ScenarioClock.Default.StartUtcTicks, _w.GetComponentRO<ScenarioClock>(env).StartUtcTicks);
        _w.Bus.SwapBuffers();
        var ask = Assert.Single(_w.Bus.ReadManaged<UpdateEntityAttributeCommand>());
        Assert.Equal(5, ask.NetworkId);
        Assert.Contains("\"ScenarioStartUtcTicks\":" + new DateTime(2025, 10, 1, 5, 30, 0, DateTimeKind.Utc).Ticks, ask.AttributePatchJson);
    }

    [Fact]
    public void CE1161_OnlyTheExactFormat_Parses_AndNoEnvironmentMeansNothingToEdit()
    {
        Assert.False(ScenarioClockEdit.TryParse("1.10.2025 5:30", out _));
        Assert.False(ScenarioClockEdit.TryParse("", out _));
        Assert.False(ScenarioClockEdit.TryRead(_w, out _));
        Assert.False(ScenarioClockEdit.SetStartUtc(_w, DateTime.UtcNow));
    }
}
