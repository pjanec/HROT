using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Spatial;

/// <summary>Unit tests for ContainerBoundsComputer.ComputeOuterSize.</summary>
public sealed class ContainerBoundsTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; set; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("stub");
        public string Title => "Stub";
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; } = Vector2.Zero;
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
    }

    private sealed class StubContainer : IContainerNodeModel
    {
        private readonly List<NodeId> _childIds;

        public StubContainer(
            ContainerPadding? padding = null,
            Vector2? minInterior = null,
            IEnumerable<NodeId>? children = null)
        {
            Padding = padding ?? ContainerPadding.Default;
            MinimumInteriorSize = minInterior ?? new Vector2(100f, 60f);
            _childIds = children == null ? new List<NodeId>() : new List<NodeId>(children);
        }

        public NodeId Id { get; } = IdGenerator.NewNodeId();
        public NodeKindKey Kind => new("container");
        public string Title => "Container";
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
        public IReadOnlyList<NodeId> ChildNodeIds => _childIds;
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding { get; }
        public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
        public Vector2 MinimumInteriorSize { get; }
    }

    private sealed class StubModel : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel> _nodes = new();

        public GraphId Id => GraphId.Empty;
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "Test", false, false);
        public IReadOnlyCollection<INodeModel> Nodes => _nodes.Values;
        public IReadOnlyCollection<ILinkModel> Links => System.Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => System.Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => _nodes.TryGetValue(id, out var v) ? v : null;
        public IPinModel? FindPin(PinId id) => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event System.Action<GraphChangeNotification>? Changed { add { } remove { } }

        public void Add(INodeModel node) => _nodes[node.Id] = node;
    }

    private const float HeaderHt = 28f;
    private const float Outline  = ContainerBoundsComputer.OutlineWidth;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyContainer_UsesMinimumInteriorSize()
    {
        // No children, so interior size = MinimumInteriorSize.
        var pad = new ContainerPadding(Top: 8f, Right: 12f, Bottom: 12f, Left: 12f);
        var container = new StubContainer(pad, new Vector2(100f, 60f));
        var model = new StubModel();

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, _ => null, HeaderHt);

        float expectedW = 100f + pad.Left + pad.Right  + 2f * Outline;
        float expectedH = HeaderHt + 60f  + pad.Top    + pad.Bottom + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void SingleChild_AtOrigin_SizeAdded()
    {
        var pad = new ContainerPadding(Top: 4f, Right: 4f, Bottom: 4f, Left: 4f);
        var child = new StubNode { Position = Vector2.Zero };
        var childSize = new Vector2(80f, 40f);

        var container = new StubContainer(pad, new Vector2(0f, 0f), new[] { child.Id });
        var model = new StubModel();
        model.Add(child);

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, id => id == child.Id ? childSize : null, HeaderHt);

        // Interior extents: max(0, 0+80)=80, max(0, 0+40)=40
        float expectedW = 80f + pad.Left + pad.Right  + 2f * Outline;
        float expectedH = HeaderHt + 40f + pad.Top    + pad.Bottom + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void SingleChild_AtOffset_ExtentIncludesPosition()
    {
        var pad = new ContainerPadding(Top: 0f, Right: 0f, Bottom: 0f, Left: 0f);
        // Child placed at local position (50, 30) with size (100, 50)
        // Extent: x=150, y=80
        var child = new StubNode { Position = new Vector2(50f, 30f) };
        var childSize = new Vector2(100f, 50f);

        var container = new StubContainer(pad, new Vector2(0f, 0f), new[] { child.Id });
        var model = new StubModel();
        model.Add(child);

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, id => id == child.Id ? childSize : null, HeaderHt);

        float expectedW = 150f + 2f * Outline;
        float expectedH = HeaderHt + 80f + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void MultipleChildren_MaxExtentUsed()
    {
        var pad = new ContainerPadding(Top: 0f, Right: 0f, Bottom: 0f, Left: 0f);
        var childA = new StubNode { Position = new Vector2(0f, 0f) };
        var childB = new StubNode { Position = new Vector2(200f, 10f) };
        var sizeA = new Vector2(100f, 80f);  // extents: (100, 80)
        var sizeB = new Vector2(50f,  40f);  // extents: (250, 50)

        var container = new StubContainer(pad, new Vector2(0f, 0f), new[] { childA.Id, childB.Id });
        var model = new StubModel();
        model.Add(childA);
        model.Add(childB);

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model,
            id => id == childA.Id ? sizeA : id == childB.Id ? sizeB : null,
            HeaderHt);

        // maxX = 250, maxY = 80
        float expectedW = 250f + 2f * Outline;
        float expectedH = HeaderHt + 80f + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void MinimumInteriorSize_WinsOverSmallChildren()
    {
        var pad = new ContainerPadding(Top: 0f, Right: 0f, Bottom: 0f, Left: 0f);
        // Child extent is 10x10, but min interior is 200x100 — min wins.
        var child = new StubNode { Position = new Vector2(0f, 0f) };
        var childSize = new Vector2(10f, 10f);

        var container = new StubContainer(pad, new Vector2(200f, 100f), new[] { child.Id });
        var model = new StubModel();
        model.Add(child);

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, id => id == child.Id ? childSize : null, HeaderHt);

        float expectedW = 200f + 2f * Outline;
        float expectedH = HeaderHt + 100f + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void ChildWithUnknownSize_Skipped()
    {
        // A child whose size delegate returns null should not affect extents.
        var pad = new ContainerPadding(Top: 0f, Right: 0f, Bottom: 0f, Left: 0f);
        var child = new StubNode { Position = new Vector2(500f, 400f) };

        var container = new StubContainer(pad, new Vector2(10f, 10f), new[] { child.Id });
        var model = new StubModel();
        model.Add(child);

        // Size delegate always returns null -> child skipped -> min interior wins
        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, _ => null, HeaderHt);

        float expectedW = 10f + 2f * Outline;
        float expectedH = HeaderHt + 10f + 2f * Outline;
        result.X.Should().BeApproximately(expectedW, 0.001f);
        result.Y.Should().BeApproximately(expectedH, 0.001f);
    }

    [Fact]
    public void OutlineWidthConstant_IsPositive()
    {
        ContainerBoundsComputer.OutlineWidth.Should().BeGreaterThan(0f);
    }

    [Fact]
    public void ZeroPadding_OnlyChildExtentsAndHeaderAndOutline()
    {
        var pad = new ContainerPadding(Top: 0f, Right: 0f, Bottom: 0f, Left: 0f);
        var child = new StubNode { Position = new Vector2(10f, 5f) };
        var childSize = new Vector2(30f, 20f); // extent: (40, 25)

        var container = new StubContainer(pad, new Vector2(0f, 0f), new[] { child.Id });
        var model = new StubModel();
        model.Add(child);

        var result = ContainerBoundsComputer.ComputeOuterSize(
            container, model, id => id == child.Id ? childSize : null, HeaderHt);

        result.X.Should().BeApproximately(40f + 2f * Outline, 0.001f);
        result.Y.Should().BeApproximately(HeaderHt + 25f + 2f * Outline, 0.001f);
    }
}
