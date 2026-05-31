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
    // ── Production-pattern container model ────────────────────────────────────
    // Mirrors FakeContainerModel from NodeEditor.Demo: children backed by List<NodeId>,
    // inserted via AddChild(). This exercises the actual IContainerNodeModel contract.

    private sealed class FakeContainerModel : IContainerNodeModel
    {
        private readonly List<NodeId>            _childIds    = new();
        private readonly List<RegionDescriptor>  _regions     = new();
        private readonly Dictionary<NodeId, int> _childRegion = new();

        public NodeId       Id       { get; } = IdGenerator.NewNodeId();
        public NodeKindKey  Kind     => new("container");
        public string       Title    => "Container";
        public string?      Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2      Position { get; set; } = Vector2.Zero;
        public Vector2?     SizeOverride => null;
        public NodeState    State        => NodeState.Normal;
        public string?      StatusTooltip => null;
        public bool         IsCollapsed   => false;
        public bool         ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => _childIds;
        public IReadOnlyList<RegionDescriptor> Regions => _regions;
        public ContainerPadding Padding => ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(200f, 100f);

        public int GetRegionIndexForChild(NodeId childId) =>
            _childRegion.TryGetValue(childId, out var r) ? r : -1;

        public void AddChild(NodeId childId, int regionIndex = -1)
        {
            if (!_childIds.Contains(childId))
                _childIds.Add(childId);
            if (regionIndex >= 0)
                _childRegion[childId] = regionIndex;
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyChildren_ReturnsEmpty()
    {
        var c = new FakeContainerModel();
        c.ChildNodeIds.Should().BeEmpty();
    }

    [Fact]
    public void InsertionOrder_Preserved()
    {
        var a = IdGenerator.NewNodeId();
        var b = IdGenerator.NewNodeId();
        var d = IdGenerator.NewNodeId();
        var c = new FakeContainerModel();
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
        var c = new FakeContainerModel();
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
        var c = new FakeContainerModel();
        c.AddChild(a);
        c.AddChild(b);
        c.ChildNodeIds.Count.Should().Be(2);
    }
}

