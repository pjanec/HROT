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
    public void SummaryOf_ReturnsNonNull()
    {
        // ⭐ O7c-②: the summary is built from the CURSOR, not from a boxed component. The renderer
        //   is no longer an [ImGuiRenderer] keyed on a type — it is a section of the tier renderer.
        Assert.NotNull(BTreeVisualizerRenderer.SummaryOf(new Fbt.BehaviorTreeState()));
    }

    // ⛔ O7c-②: `RenderValue_Object_ReturnsFalse` is REMOVED. It pinned the non-entity-aware
    //    IImGuiRenderer arm, which existed only because the renderer was reached through
    //    [ImGuiRenderer(typeof(BrainBTreeState))]. ⇒ the interface is gone with the component, so the
    //    claim has no subject — an EXPIRED test, not a dropped one (§31).

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
