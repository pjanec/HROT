using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

/// <summary>
/// AIE-032: BTreeTraceLaneProvider registers the expected four swim lanes
/// (nodes / stack / async / errors) with specific IDs and levels.
/// </summary>
public sealed class Aie032BTreeTraceTimelineTests
{
    private readonly BTreeTraceLaneProvider _provider = new();

    // ── lane IDs ──────────────────────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_BTree_RegistersExpectedLanes()
    {
        // Exactly the four documented lanes must be present.
        var ids = _provider.Lanes.Select(l => l.Id).ToArray();
        ids.Should().BeEquivalentTo(
            new[] { "bt.nodes", "bt.stack", "bt.async", "bt.errors" },
            because: "BTree trace timeline must declare exactly these four lane IDs");
    }

    [Fact]
    public void TraceTimeline_BTree_RegistersFourLanes()
    {
        _provider.Lanes.Should().HaveCount(4,
            because: "the BTree timeline exposes nodes, stack, async, and errors lanes");
    }

    // ── per-lane level assertions ─────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_BTree_NodesLane_HasLifecycleAndDecisions()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "bt.nodes");
        lane.SupportedLevels.Should().HaveFlag(TraceLevel.Lifecycle);
        lane.SupportedLevels.Should().HaveFlag(TraceLevel.Decisions);
    }

    [Fact]
    public void TraceTimeline_BTree_StackLane_IsLifecycleOnly()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "bt.stack");
        lane.SupportedLevels.Should().Be(TraceLevel.Lifecycle);
    }

    [Fact]
    public void TraceTimeline_BTree_AsyncLane_IsAsyncOnly()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "bt.async");
        lane.SupportedLevels.Should().Be(TraceLevel.Async);
    }

    [Fact]
    public void TraceTimeline_BTree_ErrorsLane_IsErrorsOnly()
    {
        var lane = _provider.Lanes.Single(l => l.Id == "bt.errors");
        lane.SupportedLevels.Should().Be(TraceLevel.Errors);
    }

    // ── provider kind ─────────────────────────────────────────────────────────

    [Fact]
    public void TraceTimeline_BTree_Kind_IsBTree()
    {
        _provider.Kind.Should().Be(AssetKind.BTree);
    }

    // ── uniqueness and completeness ───────────────────────────────────────────

    [Fact]
    public void TraceTimeline_BTree_LaneIds_AreUnique()
    {
        _provider.Lanes.Select(l => l.Id)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TraceTimeline_BTree_LaneDisplayNames_AreNonEmpty()
    {
        _provider.Lanes
            .Should().AllSatisfy(l => l.DisplayName.Should().NotBeNullOrWhiteSpace());
    }
}
