using FluentAssertions;
using Hrot.BTree.Editor.Host;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Host;

public class BTreeTraceLaneProviderTests
{
    private readonly BTreeTraceLaneProvider _sut = new();

    [Fact]
    public void Kind_IsBTree()
    {
        _sut.Kind.Should().Be(AssetKind.BTree);
    }

    [Fact]
    public void Lanes_HasFourLanes()
    {
        _sut.Lanes.Should().HaveCount(4);
    }

    [Fact]
    public void Lanes_HaveCorrectIds()
    {
        _sut.Lanes.Select(l => l.Id).Should()
            .BeEquivalentTo(["bt.nodes", "bt.stack", "bt.async", "bt.errors"]);
    }

    [Fact]
    public void NodesLane_SupportedLevels_IncludesLifecycleAndDecisions()
    {
        var lane = _sut.Lanes.Single(l => l.Id == "bt.nodes");
        lane.SupportedLevels.Should().HaveFlag(TraceLevel.Lifecycle);
        lane.SupportedLevels.Should().HaveFlag(TraceLevel.Decisions);
    }

    [Fact]
    public void StackLane_SupportedLevels_IsLifecycle()
    {
        var lane = _sut.Lanes.Single(l => l.Id == "bt.stack");
        lane.SupportedLevels.Should().Be(TraceLevel.Lifecycle);
    }

    [Fact]
    public void AsyncLane_SupportedLevels_IsAsync()
    {
        var lane = _sut.Lanes.Single(l => l.Id == "bt.async");
        lane.SupportedLevels.Should().Be(TraceLevel.Async);
    }

    [Fact]
    public void ErrorsLane_SupportedLevels_IsErrors()
    {
        var lane = _sut.Lanes.Single(l => l.Id == "bt.errors");
        lane.SupportedLevels.Should().Be(TraceLevel.Errors);
    }

    [Fact]
    public void Lanes_HaveUniqueIds()
    {
        _sut.Lanes.Select(l => l.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Lanes_HaveNonEmptyDisplayNames()
    {
        _sut.Lanes.Should().AllSatisfy(l =>
            l.DisplayName.Should().NotBeNullOrWhiteSpace());
    }
}
