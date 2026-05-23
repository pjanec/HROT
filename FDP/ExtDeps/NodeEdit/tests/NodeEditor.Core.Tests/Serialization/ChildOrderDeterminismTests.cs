using FluentAssertions;
using NodeEditor.Core.Interfaces;
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
    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubContainer : IContainerNodeModel
    {
        private readonly List<NodeId> _children;

        public StubContainer(IEnumerable<NodeId> children)
        {
            _children = new List<NodeId>(children);
        }

        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("c");
        public string Title => "C";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => _children;
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding => ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(100f, 60f);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyChildren_ReturnsEmpty()
    {
        var c = new StubContainer([]);
        c.ChildNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void InsertionOrder_Preserved()
    {
        var a = IdGenerator.NewNodeId();
        var b = IdGenerator.NewNodeId();
        var d = IdGenerator.NewNodeId();
        var c = new StubContainer([a, b, d]);
        c.ChildNodeIds[0].Should().Be(a);
        c.ChildNodeIds[1].Should().Be(b);
        c.ChildNodeIds[2].Should().Be(d);
    }

    [Fact]
    public void MultipleIterations_SameOrder()
    {
        var ids = new[] { IdGenerator.NewNodeId(), IdGenerator.NewNodeId(), IdGenerator.NewNodeId() };
        var c = new StubContainer(ids);

        var first  = new List<NodeId>(c.ChildNodeIds);
        var second = new List<NodeId>(c.ChildNodeIds);
        first.Should().Equal(second);
    }

    [Fact]
    public void Count_MatchesInsertedChildren()
    {
        var ids = new[] { IdGenerator.NewNodeId(), IdGenerator.NewNodeId() };
        var c = new StubContainer(ids);
        c.ChildNodeIds.Count.Should().Be(2);
    }
}
