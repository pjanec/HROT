using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeLinkValidatorTests
{
    // ── Minimal graph stubs ──────────────────────────────────────────────────

    private sealed class StubPin : IPinModel
    {
        public PinId Id { get; }
        public NodeId OwnerNodeId { get; }
        public string Label => string.Empty;
        public PinDirection Direction { get; }
        public PinKind Kind => PinKind.Exec;
        public TypeKey? Type => BTreeTypeSystem.ExecKey;
        public PinShape Shape => PinShape.Circle;
        public bool IsAdvanced => false;
        public bool IsOptional => false;
        public string? Tooltip => null;
        public IPinDefaultValue? Default => null;

        public StubPin(PinId id, NodeId owner, PinDirection dir)
        {
            Id = id; OwnerNodeId = owner; Direction = dir;
        }
    }

    private sealed class StubNode : INodeModel
    {
        public NodeId Id { get; }
        public NodeKindKey Kind { get; }
        public string Title => Kind.Id;
        public string? Subtitle => null;
        public NodeCategory Category => NodeCategory.Function;
        public Vector2 Position { get; set; }
        public Vector2? SizeOverride => null;
        public NodeState State => NodeState.Normal;
        public string? StatusTooltip => null;
        public bool IsCollapsed => false;
        public bool ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins { get; }

        // In the reversed convention each node gets:
        //   - one Output pin (used when this node is a child of another)
        //   - one Input pin  (used to receive its own children)
        public PinId OutputPin { get; }
        public PinId InputPin  { get; }

        public StubNode(NodeId id, string kindId)
        {
            Id         = id;
            Kind       = new NodeKindKey(kindId);
            OutputPin  = new PinId(Guid.NewGuid());
            InputPin   = new PinId(Guid.NewGuid());
            Pins = new[] { new StubPin(OutputPin, id, PinDirection.Output), new StubPin(InputPin, id, PinDirection.Input) };
        }
    }

    private sealed class StubLink : ILinkModel
    {
        public LinkId Id { get; } = new(Guid.NewGuid());
        public PinId  FromPin { get; }
        public PinId  ToPin   { get; }
        public LinkStyle Style => LinkStyle.Solid;
        public IReadOnlyList<System.Numerics.Vector2> Waypoints => Array.Empty<System.Numerics.Vector2>();
        public StubLink(PinId from, PinId to) { FromPin = from; ToPin = to; }
    }

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<NodeId, StubNode> _nodes = new();
        private readonly Dictionary<PinId,  StubPin>  _pins  = new();
        private readonly List<StubLink>               _links = new();

        public GraphId    Id          => GraphId.NewId();
        public string     DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel> Nodes  => _nodes.Values;
        public IReadOnlyCollection<ILinkModel> Links  => _links;
        public IReadOnlyCollection<ICommentModel> Comments => System.Array.Empty<ICommentModel>();
#pragma warning disable CS0067
        public event System.Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        public StubNode AddNode(string kindId)
        {
            var n = new StubNode(new NodeId(Guid.NewGuid()), kindId);
            _nodes[n.Id] = n;
            foreach (var p in n.Pins) _pins[(p as StubPin)!.Id] = (StubPin)p;
            return n;
        }

        public void AddLink(PinId from, PinId to) => _links.Add(new StubLink(from, to));

        public INodeModel? FindNode(NodeId id) => _nodes.TryGetValue(id, out var n) ? n : null;
        public IPinModel?  FindPin(PinId id)   => _pins.TryGetValue(id, out var p) ? p : null;
        public ILinkModel? FindLink(LinkId id) => _links.Find(l => l.Id == id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (StubGraph graph, BTreeLinkValidator validator) Build()
    {
        var g = new StubGraph();
        var v = new BTreeLinkValidator(g);
        return (g, v);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Connecting_composite_as_parent_is_valid()
    {
        var (g, v) = Build();
        var child  = g.AddNode(BTreeKinds.Action);
        var parent = g.AddNode(BTreeKinds.Sequence);

        // reversed: child.OutputPin -> parent.InputPin
        var result = v.Validate(child.OutputPin, parent.InputPin);

        result.Verdict.Should().Be(LinkValidity.Valid);
    }

    [Fact]
    public void Connecting_leaf_as_parent_is_invalid()
    {
        var (g, v) = Build();
        var child  = g.AddNode(BTreeKinds.Condition);
        var parent = g.AddNode(BTreeKinds.Action); // leaf cannot be a parent

        var result = v.Validate(child.OutputPin, parent.InputPin);

        result.Verdict.Should().Be(LinkValidity.Invalid);
        result.Reason.Should().Contain("Leaf nodes");
    }

    [Fact]
    public void Subtree_node_as_parent_is_invalid()
    {
        var (g, v) = Build();
        var child  = g.AddNode(BTreeKinds.Action);
        var parent = g.AddNode(BTreeKinds.Subtree); // subtree is a leaf

        var result = v.Validate(child.OutputPin, parent.InputPin);

        result.Verdict.Should().Be(LinkValidity.Invalid);
    }

    [Fact]
    public void Cycle_detection_fires_for_direct_cycle()
    {
        var (g, v) = Build();
        var a = g.AddNode(BTreeKinds.Sequence);
        var b = g.AddNode(BTreeKinds.Sequence);

        // a is child of b: a.OutputPin -> b.InputPin
        g.AddLink(a.OutputPin, b.InputPin);

        // Now try to make b a child of a, which would create a cycle:
        // b.OutputPin -> a.InputPin
        var result = v.Validate(b.OutputPin, a.InputPin);

        result.Verdict.Should().Be(LinkValidity.Invalid);
        result.Reason.Should().Contain("cycle");
    }

    [Fact]
    public void Self_loop_is_rejected()
    {
        var (g, v) = Build();
        var n = g.AddNode(BTreeKinds.Sequence);

        var result = v.Validate(n.OutputPin, n.InputPin);

        result.Verdict.Should().Be(LinkValidity.Invalid);
    }

    [Fact]
    public void Root_as_parent_of_child_is_valid()
    {
        var (g, v) = Build();
        var child = g.AddNode(BTreeKinds.Sequence);
        var root  = g.AddNode(BTreeKinds.Root);

        var result = v.Validate(child.OutputPin, root.InputPin);

        result.Verdict.Should().Be(LinkValidity.Valid);
    }

    [Fact]
    public void ObserverSelector_as_parent_is_valid()
    {
        var (g, v) = Build();
        var child  = g.AddNode(BTreeKinds.Action);
        var parent = g.AddNode(BTreeKinds.ObserverSelector);

        var result = v.Validate(child.OutputPin, parent.InputPin);

        result.Verdict.Should().Be(LinkValidity.Valid);
    }
}
