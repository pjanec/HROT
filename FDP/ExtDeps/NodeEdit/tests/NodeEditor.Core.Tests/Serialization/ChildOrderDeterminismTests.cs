using FluentAssertions;
using NodeEditor.Demo.FakeBlueprint;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Serialization;

/// <summary>
/// Verifies that IContainerNodeModel.ChildNodeIds iterates in insertion order
/// (stable, deterministic), so that fluent emitters produce byte-identical output
/// across runs (spec NEC ss 15).
/// </summary>
public sealed class ChildOrderDeterminismTests
{
    // ── Tests ─────────────────────────────────────────────────────────────────
    // Uses NodeEditor.Demo.FakeBlueprint.FakeContainerModel (production demo type).

    [Fact]
    public void EmptyChildren_ReturnsEmpty()
    {
        var c = new FakeContainerModel(IdGenerator.NewNodeId(), "Container", Vector2.Zero);
        c.ChildNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void InsertionOrder_Preserved()
    {
        var a = IdGenerator.NewNodeId();
        var b = IdGenerator.NewNodeId();
        var d = IdGenerator.NewNodeId();
        var c = new FakeContainerModel(IdGenerator.NewNodeId(), "Container", Vector2.Zero);
        c.AddChild(a);
        c.AddChild(b);
        c.AddChild(d);
        c.ChildNodeIds[0].Should().Be(a);
        c.ChildNodeIds[1].Should().Be(b);
        c.ChildNodeIds[2].Should().Be(d);
    }

    [Fact]
    public void MultipleIterations_SameOrder()
    {
        var ids = new[] { IdGenerator.NewNodeId(), IdGenerator.NewNodeId(), IdGenerator.NewNodeId() };
        var c = new FakeContainerModel(IdGenerator.NewNodeId(), "Container", Vector2.Zero);
        foreach (var id in ids) c.AddChild(id);

        var first  = new List<NodeId>(c.ChildNodeIds);
        var second = new List<NodeId>(c.ChildNodeIds);
        first.Should().Equal(second);
    }

    [Fact]
    public void Count_MatchesInsertedChildren()
    {
        var a = IdGenerator.NewNodeId();
        var b = IdGenerator.NewNodeId();
        var c = new FakeContainerModel(IdGenerator.NewNodeId(), "Container", Vector2.Zero);
        c.AddChild(a);
        c.AddChild(b);
        c.ChildNodeIds.Count.Should().Be(2);
    }
}

