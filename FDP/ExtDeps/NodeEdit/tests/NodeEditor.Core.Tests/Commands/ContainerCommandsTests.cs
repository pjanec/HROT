using FluentAssertions;
using NodeEditor.Core.Commands;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Commands;

/// <summary>Tests for container-related GraphCommand records (TASK-NEC-07).</summary>
public sealed class ContainerCommandsTests
{
    [Fact]
    public void ChangeParent_StoresFields()
    {
        var nodeId = IdGenerator.NewNodeId();
        var parentId = IdGenerator.NewNodeId();
        var pos = new Vector2(10f, 20f);

        var cmd = new GraphCommand.ChangeParent(nodeId, parentId, 1, pos);

        cmd.NodeId.Should().Be(nodeId);
        cmd.NewParentContainerId.Should().Be(parentId);
        cmd.NewRegionIndex.Should().Be(1);
        cmd.NewLocalPosition.Should().Be(pos);
    }

    [Fact]
    public void ChangeParent_ToRoot_NullParentAndRegion()
    {
        var nodeId = IdGenerator.NewNodeId();
        var cmd = new GraphCommand.ChangeParent(nodeId, null, null, Vector2.Zero);

        cmd.NewParentContainerId.Should().BeNull();
        cmd.NewRegionIndex.Should().BeNull();
    }

    [Fact]
    public void ChangeParentMultiple_StoresMoves()
    {
        var moves = new List<ChangeParentMove>
        {
            new(IdGenerator.NewNodeId(), null, null, Vector2.Zero),
            new(IdGenerator.NewNodeId(), IdGenerator.NewNodeId(), 0, new Vector2(5f, 5f)),
        };

        var cmd = new GraphCommand.ChangeParentMultiple(moves);

        cmd.Moves.Should().HaveCount(2);
        cmd.Moves[0].NewParentContainerId.Should().BeNull();
        cmd.Moves[1].NewRegionIndex.Should().Be(0);
    }

    [Fact]
    public void SetContainerCollapsed_StoresFields()
    {
        var id = IdGenerator.NewNodeId();

        var collapse = new GraphCommand.SetContainerCollapsed(id, true);
        collapse.ContainerId.Should().Be(id);
        collapse.IsCollapsed.Should().BeTrue();

        var expand = new GraphCommand.SetContainerCollapsed(id, false);
        expand.IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public void AddRegion_StoresFields()
    {
        var id = IdGenerator.NewNodeId();
        var cmd = new GraphCommand.AddRegion(id, 1, "Locomotion", 2);

        cmd.ContainerId.Should().Be(id);
        cmd.InsertAtIndex.Should().Be(1);
        cmd.RegionName.Should().Be("Locomotion");
        cmd.Priority.Should().Be(2);
    }

    [Fact]
    public void RemoveRegion_StoresFields()
    {
        var id = IdGenerator.NewNodeId();
        var cmd = new GraphCommand.RemoveRegion(id, 0, ChildRedistributionPolicy.MoveToFirstRegion);

        cmd.ContainerId.Should().Be(id);
        cmd.RegionIndex.Should().Be(0);
        cmd.Policy.Should().Be(ChildRedistributionPolicy.MoveToFirstRegion);
    }

    [Fact]
    public void ReorderRegions_StoresFields()
    {
        var id = IdGenerator.NewNodeId();
        var order = new List<int> { 2, 0, 1 };
        var cmd = new GraphCommand.ReorderRegions(id, order);

        cmd.ContainerId.Should().Be(id);
        cmd.NewOrder.Should().Equal(2, 0, 1);
    }

    [Fact]
    public void SetRegionProperty_StoresFields()
    {
        var id = IdGenerator.NewNodeId();
        var cmd = new GraphCommand.SetRegionProperty(id, 1, "Label", "Combat");

        cmd.ContainerId.Should().Be(id);
        cmd.RegionIndex.Should().Be(1);
        cmd.Key.Should().Be("Label");
        cmd.Value.Should().Be("Combat");
    }

    [Fact]
    public void ChangeParentMove_StoresFields()
    {
        var nodeId   = IdGenerator.NewNodeId();
        var parentId = IdGenerator.NewNodeId();
        var move = new ChangeParentMove(nodeId, parentId, 2, new Vector2(3f, 4f));

        move.NodeId.Should().Be(nodeId);
        move.NewParentContainerId.Should().Be(parentId);
        move.NewRegionIndex.Should().Be(2);
        move.NewLocalPosition.Should().Be(new Vector2(3f, 4f));
    }

    [Fact]
    public void ChildRedistributionPolicy_HasExpectedValues()
    {
        var values = System.Enum.GetValues<ChildRedistributionPolicy>();
        values.Should().Contain(ChildRedistributionPolicy.DeleteChildren);
        values.Should().Contain(ChildRedistributionPolicy.MoveToFirstRegion);
        values.Should().Contain(ChildRedistributionPolicy.MoveToParent);
    }
}
