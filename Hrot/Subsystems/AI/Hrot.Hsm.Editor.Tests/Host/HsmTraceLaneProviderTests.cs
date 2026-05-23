using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Host;
using FluentAssertions;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

public sealed class HsmTraceLaneProviderTests
{
    private readonly HsmTraceLaneProvider _provider = new();

    [Fact]
    public void Kind_IsHsm()
    {
        _provider.Kind.Should().Be(AssetKind.Hsm);
    }

    [Fact]
    public void Lanes_HasSixLanes()
    {
        _provider.Lanes.Should().HaveCount(6);
    }

    [Fact]
    public void Lanes_HaveUniqueIds()
    {
        _provider.Lanes.Select(l => l.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Lanes_HaveNonEmptyDisplayNames()
    {
        foreach (var lane in _provider.Lanes)
            lane.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StatesLane_IsLifecycle()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.states");
        lane.SupportedLevels.Should().Be(TraceLevel.Lifecycle);
    }

    [Fact]
    public void EventsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.events");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void ActionsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.actions");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void GuardsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.guards");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void TimersLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.timers");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void ConflictsLane_IsErrors()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.conflicts");
        lane.SupportedLevels.Should().Be(TraceLevel.Errors);
    }
}
