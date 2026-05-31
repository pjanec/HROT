using FluentAssertions;
using Hrot.Hsm.Editor.Model;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

/// <summary>
/// FIX3-003: verifies that StateNode.ChildNodeIds preserves child insertion order
/// (NEC-10 canonical-order invariant).  StateNode is the second production
/// IContainerNodeModel implementation; its ChildNodeIds is a LINQ projection over
/// List&lt;StateNode&gt; and is a materially different code path from FakeContainerModel.
/// </summary>
public sealed class StateNodeChildOrderDeterminismTests
{
    [Fact]
    public void StateNode_ChildNodeIds_PreservesInsertionOrder()
    {
        // Arrange: three children added in a defined, non-sorted order.
        var parent = new StateNode("Parent");
        var c1     = new StateNode("C1");
        var c2     = new StateNode("C2");
        var c3     = new StateNode("C3");
        parent.Children.Add(c1);
        parent.Children.Add(c2);
        parent.Children.Add(c3);

        // Act
        var ids = parent.ChildNodeIds;

        // Assert: order must match insertion order, not lexicographic or guid order.
        ids.Should().HaveCount(3);
        ids[0].Should().Be(new NodeId(c1.StableId));
        ids[1].Should().Be(new NodeId(c2.StableId));
        ids[2].Should().Be(new NodeId(c3.StableId));
    }

    [Fact]
    public void StateNode_ChildNodeIds_IsStableAcrossMultipleReads()
    {
        // Arrange
        var parent = new StateNode("Parent");
        var c1     = new StateNode("A");
        var c2     = new StateNode("B");
        var c3     = new StateNode("C");
        parent.Children.Add(c3); // non-alphabetical insertion order
        parent.Children.Add(c1);
        parent.Children.Add(c2);

        // Act
        var first  = parent.ChildNodeIds;
        var second = parent.ChildNodeIds;

        // Assert: two reads produce the same sequence.
        first.Should().Equal(second);
        // And the order must match insertion order (c3 was first).
        first[0].Should().Be(new NodeId(c3.StableId));
        first[1].Should().Be(new NodeId(c1.StableId));
        first[2].Should().Be(new NodeId(c2.StableId));
    }
}
