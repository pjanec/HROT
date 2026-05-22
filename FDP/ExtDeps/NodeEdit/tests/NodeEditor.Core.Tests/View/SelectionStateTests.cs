using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

public class SelectionStateTests
{
    [Fact]
    public void ReplaceWith_Single_HasOneItem()
    {
        var sel = new SelectionState();
        sel.Add(SelectionEntry.OfNode(NodeId.NewId()));
        sel.Add(SelectionEntry.OfLink(LinkId.NewId()));

        var target = SelectionEntry.OfNode(NodeId.NewId());
        sel.ReplaceWith(target);

        sel.Count.Should().Be(1);
        sel.Contains(target).Should().BeTrue();
    }

    [Fact]
    public void Add_AddsWithoutRemovingOthers()
    {
        var sel = new SelectionState();
        var first = SelectionEntry.OfNode(NodeId.NewId());
        var second = SelectionEntry.OfLink(LinkId.NewId());

        sel.Add(first);
        sel.Add(second);

        sel.Count.Should().Be(2);
        sel.Contains(first).Should().BeTrue();
        sel.Contains(second).Should().BeTrue();
    }

    [Fact]
    public void Toggle_AddsThenRemoves()
    {
        var sel = new SelectionState();
        var entry = SelectionEntry.OfNode(NodeId.NewId());

        sel.Toggle(entry);
        sel.Contains(entry).Should().BeTrue();

        sel.Toggle(entry);
        sel.Contains(entry).Should().BeFalse();
    }

    [Fact]
    public void Nodes_Filters()
    {
        var sel = new SelectionState();
        var nodeId = NodeId.NewId();
        sel.Add(SelectionEntry.OfNode(nodeId));
        sel.Add(SelectionEntry.OfLink(LinkId.NewId()));
        sel.Add(SelectionEntry.OfComment(CommentId.NewId()));

        var nodes = sel.Nodes.ToList();
        nodes.Should().ContainSingle();
        nodes[0].Should().Be(nodeId);
    }
}
