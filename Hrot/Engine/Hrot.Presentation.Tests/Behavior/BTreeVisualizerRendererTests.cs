using Fbt;
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

    // IsAncestralPath returns false when tree is idle
    [Fact]
    public void IsAncestralPath_ReturnsFalse_WhenTreeIdle()
    {
        var blob = new BehaviorTreeBlob
        {
            Nodes = new[] { new NodeDefinition { SubtreeOffset = 3, ChildCount = 1 } }
        };
        var state = new BehaviorTreeState();
        // RunningNodeIndex = 0 means idle
        Assert.False(BTreeVisualizerRenderer.IsAncestralPath(blob, ref state, nodeIndex: 0));
    }

    // IsAncestralPath returns true when the running node is inside the node's subtree
    [Fact]
    public unsafe void IsAncestralPath_ReturnsTrue_WhenRunningNodeIsInSubtree()
    {
        // Tree layout (DFS preorder):
        // [0] Sequence, SubtreeOffset=3 (covers [0,3))
        // [1] Repeater, SubtreeOffset=2 (covers [1,3))
        // [2] Wait,     SubtreeOffset=1 (leaf)
        var blob = new BehaviorTreeBlob
        {
            Nodes = new[]
            {
                new NodeDefinition { Type = NodeType.Sequence,  ChildCount = 1, SubtreeOffset = 3 },
                new NodeDefinition { Type = NodeType.Repeater,  ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Wait,      ChildCount = 0, SubtreeOffset = 1 },
            }
        };
        var state = new BehaviorTreeState { RunningNodeIndex = 2 }; // Wait is running

        Assert.True(BTreeVisualizerRenderer.IsAncestralPath(blob, ref state, nodeIndex: 0));  // Sequence
        Assert.True(BTreeVisualizerRenderer.IsAncestralPath(blob, ref state, nodeIndex: 1));  // Repeater
        Assert.False(BTreeVisualizerRenderer.IsAncestralPath(blob, ref state, nodeIndex: 2)); // running, not ancestral
    }
}
