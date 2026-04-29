using Hrot.Presentation.Renderers;
using Xunit;

namespace Hrot.Presentation.Tests;

public class BTreeVisualizerRendererTests
{
    // SC2 (logic): Active node at RunningNodeIndex gets green color code
    [Fact]
    public void GetNodeColorCode_ReturnsGreen_ForRunningNode()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 2, runningNodeIndex: 2, hasChildren: false);
        Assert.Equal(1, colorCode); // 1 = green
    }

    // Non-running node gets default color
    [Fact]
    public void GetNodeColorCode_ReturnsDefault_WhenTreeIdle()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 2, runningNodeIndex: 0, hasChildren: false);
        Assert.Equal(0, colorCode); // 0 = default
    }

    // Inactive leaf while tree is running gets gray
    [Fact]
    public void GetNodeColorCode_ReturnsGray_ForInactiveLeafWhenTreeRunning()
    {
        int colorCode = BTreeVisualizerRenderer.GetNodeColorCode(
            nodeIndex: 3, runningNodeIndex: 2, hasChildren: false);
        Assert.Equal(2, colorCode); // 2 = gray
    }

    // GetSummary returns structured string
    [Fact]
    public void GetSummary_ReturnsNonNull()
    {
        var renderer = new BTreeVisualizerRenderer();
        var state = new Fdp.Toolkit.Behavior.Components.BrainBTreeState();
        Assert.NotNull(renderer.GetSummary(state));
    }

    // Non-entity-aware RenderValue always returns false
    [Fact]
    public void RenderValue_Object_ReturnsFalse()
    {
        var renderer = new BTreeVisualizerRenderer();
        Assert.False(renderer.RenderValue(new Fdp.Toolkit.Behavior.Components.BrainBTreeState()));
    }
}
