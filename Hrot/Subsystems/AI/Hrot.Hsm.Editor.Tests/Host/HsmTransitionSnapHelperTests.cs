using System.Numerics;
using FluentAssertions;
using Fhsm.Compiler;
using Hrot.Hsm.Editor.Host;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Host;

public class HsmTransitionSnapHelperTests
{
    // Build a minimal HsmAsset using the same builder+projector pattern as other tests.
    private static HsmAsset MakeDummyAsset()
    {
        var builder  = new HsmBuilder("Dummy");
        var graph    = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob     = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return HsmAssetProjector.Project(blob, metadata, null, Guid.NewGuid(), "Dummy", "", false, "");
    }

    [Fact]
    public void FindNearestSnapTarget_EmptyAsset_ReturnsNull()
    {
        var asset = MakeDummyAsset();
        // Dummy asset has no non-root states, so result is always null.
        var result = HsmTransitionSnapHelper.FindNearestSnapTarget(
            Vector2.Zero, asset);
        result.Should().BeNull();
    }

    [Fact]
    public void IsValidTransitionTarget_HistoryState_ReturnsFalse()
    {
        var asset = MakeDummyAsset();
        var state = new StateNode("H") { IsHistory = true };
        HsmTransitionSnapHelper.IsValidTransitionTarget(state, asset).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransitionTarget_DeepHistoryState_ReturnsFalse()
    {
        var asset = MakeDummyAsset();
        var state = new StateNode("DH") { IsDeepHistory = true };
        HsmTransitionSnapHelper.IsValidTransitionTarget(state, asset).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransitionTarget_FinalState_AllowedByDefault()
    {
        var asset = MakeDummyAsset();
        var state = new StateNode("F") { IsFinal = true };
        HsmTransitionSnapHelper.IsValidTransitionTarget(state, asset).Should().BeTrue();
    }

    [Fact]
    public void IsValidTransitionTarget_FinalState_ForbiddenWhenFlagFalse()
    {
        var asset = MakeDummyAsset();
        var state = new StateNode("F") { IsFinal = true };
        HsmTransitionSnapHelper.IsValidTransitionTarget(state, asset, allowFinalTarget: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidTransitionTarget_RootState_ReturnsFalse()
    {
        var asset = MakeDummyAsset();
        HsmTransitionSnapHelper.IsValidTransitionTarget(asset.RootState, asset).Should().BeFalse();
    }

    [Fact]
    public void IsValidTransitionTarget_PlainState_ReturnsTrue()
    {
        var asset = MakeDummyAsset();
        var state = new StateNode("S");
        HsmTransitionSnapHelper.IsValidTransitionTarget(state, asset).Should().BeTrue();
    }
}
