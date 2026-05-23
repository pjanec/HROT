using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Interfaces;

/// <summary>Tests for IContainerNodeModel, ContainerPadding, RegionDescriptor, and INodeModelExtensions.</summary>
public sealed class ContainerNodeModelTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("stub");
        public string Title => "Stub";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
    }

    private sealed class StubContainer : IContainerNodeModel
    {
        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("container");
        public string Title => "Container";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding => ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
    }

    private sealed class StubInactiveContainer : IContainerNodeModel
    {
        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("container-inactive");
        public string Title => "Inactive";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position => Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => false; // inactive
        public IReadOnlyList<NodeId> ChildNodeIds => System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding => ContainerPadding.Default;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegularNode_IsContainerNode_ReturnsFalse()
    {
        INodeModel node = new StubNode();
        node.IsContainerNode().Should().BeFalse();
    }

    [Fact]
    public void RegularNode_AsContainer_ReturnsNull()
    {
        INodeModel node = new StubNode();
        node.AsContainer().Should().BeNull();
    }

    [Fact]
    public void ContainerNode_IsContainerNode_ReturnsTrue()
    {
        INodeModel node = new StubContainer();
        node.IsContainerNode().Should().BeTrue();
    }

    [Fact]
    public void ContainerNode_AsContainer_ReturnsSelf()
    {
        var container = new StubContainer();
        INodeModel node = container;
        node.AsContainer().Should().BeSameAs(container);
    }

    [Fact]
    public void InactiveContainerNode_IsContainerNode_ReturnsFalse()
    {
        INodeModel node = new StubInactiveContainer();
        node.IsContainerNode().Should().BeFalse();
    }

    [Fact]
    public void InactiveContainerNode_AsContainer_ReturnsNull()
    {
        INodeModel node = new StubInactiveContainer();
        node.AsContainer().Should().BeNull();
    }

    [Fact]
    public void RegularNode_ParentContainerId_DefaultIsNull()
    {
        INodeModel node = new StubNode();
        node.ParentContainerId.Should().BeNull();
    }

    [Fact]
    public void ContainerPadding_Default_HasExpectedValues()
    {
        var pad = ContainerPadding.Default;
        pad.Top.Should().Be(8f);
        pad.Left.Should().Be(12f);
        pad.Right.Should().Be(12f);
        pad.Bottom.Should().Be(12f);
    }

    [Fact]
    public void RegionDescriptor_StoresFields()
    {
        var rd = new RegionDescriptor(1, "Combat", 2, null);
        rd.Index.Should().Be(1);
        rd.Name.Should().Be("Combat");
        rd.Priority.Should().Be(2);
        rd.CustomColor.Should().BeNull();
    }
}
