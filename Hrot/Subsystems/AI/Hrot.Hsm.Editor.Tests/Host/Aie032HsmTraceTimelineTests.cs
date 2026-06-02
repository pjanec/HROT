using FluentAssertions;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Host;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

/// <summary>
/// AIE-032: HsmTraceLaneProvider registers the expected swim lanes
/// (states / events / actions / guards / timers / conflicts) with
/// specific IDs and supported levels.
/// </summary>
public sealed class Aie032HsmTraceTimelineTests
{
    private readonly HsmTraceLaneProvider _provider = new();

    // ── lane IDs ──────────────────────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_Hsm_RegistersExpectedLanes()
    {
        var ids = _provider.Lanes.Select(l => l.Id).ToArray();
        ids.Should().BeEquivalentTo(
            new[] { "hsm.states", "hsm.events", "hsm.actions", "hsm.guards", "hsm.timers", "hsm.conflicts" },
            because: "HSM trace timeline must declare exactly these six lane IDs");
    }

    [Fact]
    public void TraceTimeline_Hsm_RegistersSixLanes()
    {
        _provider.Lanes.Should().HaveCount(6,
            because: "HSM timeline has states, events, actions, guards, timers, conflicts");
    }

    // ── per-lane level assertions ─────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_Hsm_StatesLane_IsLifecycle()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.states");
        lane.SupportedLevels.Should().Be(TraceLevel.Lifecycle);
    }

    [Fact]
    public void TraceTimeline_Hsm_EventsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.events");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void TraceTimeline_Hsm_ActionsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.actions");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void TraceTimeline_Hsm_GuardsLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.guards");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void TraceTimeline_Hsm_TimersLane_IsDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.timers");
        lane.SupportedLevels.Should().Be(TraceLevel.Decisions);
    }

    [Fact]
    public void TraceTimeline_Hsm_ConflictsLane_IsErrors()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "hsm.conflicts");
        lane.SupportedLevels.Should().Be(TraceLevel.Errors);
    }

    // ── provider kind ─────────────────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_Hsm_Kind_IsHsm()
    {
        _provider.Kind.Should().Be(AssetKind.Hsm);
    }

    // ── uniqueness and completeness ───────────────────────────────────────────

    [Fact]
    public void TraceTimeline_Hsm_LaneIds_AreUnique()
    {
        _provider.Lanes.Select(l => l.Id)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TraceTimeline_Hsm_LaneDisplayNames_AreNonEmpty()
    {
        _provider.Lanes
            .Should().AllSatisfy(l => l.DisplayName.Should().NotBeNullOrWhiteSpace());
    }
}
