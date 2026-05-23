using FluentAssertions;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.Spatial;

/// <summary>
/// Unit tests for ContainerCycleDetector.WouldCreateCycle and WouldCreateCycleAny.
/// </summary>
public sealed class ContainerCycleDetectionTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; set; } = IdGenerator.NewNodeId();
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
        public NodeId? ParentContainerId { get; set; }
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (StubModel model, StubNode root, StubNode child, StubNode grandchild) BuildChain()
    {
        var model = new StubModel();
        var root  = new StubNode();
        var child = new StubNode { ParentContainerId = root.Id };
        var grand = new StubNode { ParentContainerId = child.Id };
        model.Add(root);
        model.Add(child);
        model.Add(grand);
        return (model, root, child, grand);
    }

    // ── WouldCreateCycle tests ────────────────────────────────────────────────

    [Fact]
    public void SameNode_IsCycle()
    {
        // Dropping a node into itself is always a cycle.
        var model = new StubModel();
        var node  = new StubNode();
        model.Add(node);

        ContainerCycleDetector.WouldCreateCycle(node.Id, node.Id, model)
            .Should().BeTrue();
    }

    [Fact]
    public void DirectDescendant_IsCycle()
    {
        // Root → child: moving root into child would create a cycle.
        var (model, root, child, _) = BuildChain();

        ContainerCycleDetector.WouldCreateCycle(root.Id, child.Id, model)
            .Should().BeTrue();
    }

    [Fact]
    public void TwoLevelsDeep_IsCycle()
    {
        // Root → child → grandchild: moving root into grandchild is a cycle.
        var (model, root, _, grand) = BuildChain();

        ContainerCycleDetector.WouldCreateCycle(root.Id, grand.Id, model)
            .Should().BeTrue();
    }

    [Fact]
    public void ChildIntoParent_IsNotCycle()
    {
        // Moving a child into its parent — that's its current parent, not a cycle.
        // (Whether the host allows it is separate; the cycle detector should say false.)
        var (model, root, child, _) = BuildChain();

        ContainerCycleDetector.WouldCreateCycle(child.Id, root.Id, model)
            .Should().BeFalse();
    }

    [Fact]
    public void UnrelatedNode_IsNotCycle()
    {
        var model   = new StubModel();
        var nodeA   = new StubNode();
        var nodeB   = new StubNode();
        var target  = new StubNode();
        model.Add(nodeA);
        model.Add(nodeB);
        model.Add(target);

        ContainerCycleDetector.WouldCreateCycle(nodeA.Id, target.Id, model)
            .Should().BeFalse();
    }

    [Fact]
    public void GrandchildIntoChild_IsNotCycle()
    {
        // Moving grandchild into child (same parent) is not a cycle.
        var (model, _, child, grand) = BuildChain();

        ContainerCycleDetector.WouldCreateCycle(grand.Id, child.Id, model)
            .Should().BeFalse();
    }

    // ── WouldCreateCycleAny tests ─────────────────────────────────────────────

    [Fact]
    public void AnyWithOneCyclic_ReturnsTrue()
    {
        var (model, root, child, _) = BuildChain();
        var unrelated = new StubNode();
        model.Add(unrelated);

        // root would create a cycle; unrelated would not
        ContainerCycleDetector.WouldCreateCycleAny(
            new[] { unrelated.Id, root.Id }, child.Id, model)
            .Should().BeTrue();
    }

    [Fact]
    public void AnyWithNoCyclic_ReturnsFalse()
    {
        var (model, root, child, _) = BuildChain();
        var a = new StubNode();
        var b = new StubNode();
        model.Add(a);
        model.Add(b);

        ContainerCycleDetector.WouldCreateCycleAny(
            new[] { a.Id, b.Id }, root.Id, model)
            .Should().BeFalse();
    }
}
