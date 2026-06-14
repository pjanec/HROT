using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.BTree.Editor.Tests;

public sealed class BTreeCommandSinkTests
{
    // ---- Helpers ------------------------------------------------------------

    private static BehaviorTreeBlob EmptyBlob() =>
        new BehaviorTreeBlob
        {
            TreeName        = "test",
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };

    private static BehaviorTreeAsset MakeAsset() =>
        new BehaviorTreeAsset(
            Guid.NewGuid(), "TestTree", "/TestTree.cs", true,
            "BB", "Ctx", EmptyBlob());

    // ---- Stubs --------------------------------------------------------------

    private sealed class StubPin : IPinModel
    {
        public PinId Id { get; }
        public NodeId OwnerNodeId { get; }
        public string Label => string.Empty;
        public PinDirection Direction { get; }
        public PinKind Kind => PinKind.Exec;
        public TypeKey? Type => null;
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

    private sealed class StubLink : ILinkModel
    {
        public LinkId Id { get; }
        public PinId FromPin { get; }
        public PinId ToPin { get; }
        public LinkStyle Style => LinkStyle.Solid;
        public IReadOnlyList<Vector2> Waypoints => Array.Empty<Vector2>();

        public StubLink(LinkId id, PinId from, PinId to)
        {
            Id = id; FromPin = from; ToPin = to;
        }
    }

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel>   _nodes = new();
        private readonly Dictionary<PinId,  StubPin>      _pins  = new();
        private readonly Dictionary<LinkId, StubLink>     _links = new();

        public GraphId Id => GraphId.NewId();
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
        public IReadOnlyCollection<ILinkModel>    Links    => _links.Values;
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        // Register exec pins for a node (output for child role, input for parent role).
        public void RegisterPins(NodeId nodeId, out PinId outputPin, out PinId inputPin)
        {
            outputPin = new PinId(Guid.NewGuid());
            inputPin  = new PinId(Guid.NewGuid());
            _pins[outputPin] = new StubPin(outputPin, nodeId, PinDirection.Output);
            _pins[inputPin]  = new StubPin(inputPin,  nodeId, PinDirection.Input);
        }

        // Register a link for FindLink resolution.
        public void RegisterLink(LinkId linkId, PinId fromPin, PinId toPin)
        {
            _links[linkId] = new StubLink(linkId, fromPin, toPin);
        }

        public INodeModel?  FindNode(NodeId id) => _nodes.TryGetValue(id, out var n) ? n : null;
        public IPinModel?   FindPin(PinId id)   => _pins.TryGetValue(id, out var p) ? p : null;
        public ILinkModel?  FindLink(LinkId id) => _links.TryGetValue(id, out var l) ? l : null;
    }

    private static (BehaviorTreeAsset asset, StubGraph graph, BTreeCommandSink sink) Build()
    {
        var asset = MakeAsset();
        var graph = new StubGraph();
        var sink  = new BTreeCommandSink(asset, graph);
        return (asset, graph, sink);
    }

    // ---- Tests --------------------------------------------------------------

    [Fact]
    public void AddNode_sequence_creates_node_with_correct_type()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        var result = sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        result.Success.Should().BeTrue();
        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Sequence);
        node.DisplayLabel.Should().Be("Sequence", "a created node must get a friendly title, not the raw kind id");
    }

    [Fact]
    public void AddNode_wait_initializes_payload_and_friendly_label()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey(BTreeKinds.Wait), Vector2.Zero, null));

        var node = asset.FindNode(nodeId.Value);
        node.Should().NotBeNull();
        node!.KernelType.Should().Be(NodeType.Wait);
        node.DisplayLabel.Should().Be("Wait");
        node.Wait.Should().NotBeNull("a created Wait must have an editable Duration payload, not null");
    }

    [Fact]
    public void AddNode_action_stores_position()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var pos = new Vector2(42f, 99f);

        sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey(BTreeKinds.Action), pos, null));

        asset.FindNode(nodeId.Value)!.Position.Should().Be(pos);
    }

    [Fact]
    public void RemoveNode_removes_from_asset()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Action), Vector2.Zero, null));
        asset.FindNode(nodeId.Value).Should().NotBeNull();

        sink.Apply(new GraphCommand.RemoveNodes(new[] { nodeId }));

        asset.FindNode(nodeId.Value).Should().BeNull();
    }

    [Fact]
    public void AddLink_parent_receives_child()
    {
        var (asset, graph, sink) = Build();
        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        // Reversed convention: child output pin -> parent input pin.
        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);

        var linkId = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddLink(linkId, childOut, parentIn));

        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().Contain(childId.Value);
    }

    [Fact]
    public void RemoveLink_removes_child_from_parent()
    {
        var (asset, graph, sink) = Build();
        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);

        var linkId = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddLink(linkId, childOut, parentIn));
        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().Contain(childId.Value);

        sink.Apply(new GraphCommand.RemoveLinks(new[] { linkId }));

        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().NotContain(childId.Value);
    }

    [Fact]
    public void AddLink_NormalAttach_adds_parentless_node_to_parent()
    {
        var (asset, graph, sink) = Build();
        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);

        var linkId = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddLink(linkId, childOut, parentIn));

        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().Contain(childId.Value);
    }

    [Fact]
    public void AddLink_MovesChildToNewParent()
    {
        var (asset, graph, sink) = Build();
        var p1 = NodeId.NewId();
        var p2 = NodeId.NewId();
        var c  = NodeId.NewId();

        // Set up: two parent-capable nodes, one action child.
        sink.Apply(new GraphCommand.AddNode(p1, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(p2, new NodeKindKey(BTreeKinds.Selector), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(c,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(p1, out _, out var p1In);
        graph.RegisterPins(p2, out _, out var p2In);
        graph.RegisterPins(c,  out var cOut, out _);

        // Wire c -> p1 (c becomes child of p1).
        var link1 = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddLink(link1, cOut, p1In));
        asset.FindNode(p1.Value)!.ChildVisualIds.Should().Contain(c.Value);

        // Re-wire c -> p2 (c should move from p1 to p2).
        var link2 = new LinkId(Guid.NewGuid());
        sink.Apply(new GraphCommand.AddLink(link2, cOut, p2In));

        // p1 must no longer contain c.
        asset.FindNode(p1.Value)!.ChildVisualIds.Should().NotContain(c.Value);
        // p2 must contain c.
        asset.FindNode(p2.Value)!.ChildVisualIds.Should().Contain(c.Value);
    }

    [Fact]
    public void AddLink_NoDuplicateParents()
    {
        var (asset, graph, sink) = Build();
        var p1 = NodeId.NewId();
        var p2 = NodeId.NewId();
        var c  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(p1, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(p2, new NodeKindKey(BTreeKinds.Selector), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(c,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(p1, out _, out var p1In);
        graph.RegisterPins(p2, out _, out var p2In);
        graph.RegisterPins(c,  out var cOut, out _);

        // Wire c -> p1.
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), cOut, p1In));
        // Re-wire c -> p2.
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), cOut, p2In));

        // Exactly one node has c in its ChildVisualIds.
        var count = 0;
        foreach (var node in asset.Nodes)
        {
            if (node.ChildVisualIds.Contains(c.Value))
                count++;
        }
        count.Should().Be(1);
    }

    [Fact]
    public void AddLink_WouldCreateCycle_IsRejected()
    {
        var (asset, graph, sink) = Build();
        var p = NodeId.NewId(); // root / ancestor
        var a = NodeId.NewId(); // middle
        var b = NodeId.NewId(); // leaf

        // Tree: p -> a -> b
        sink.Apply(new GraphCommand.AddNode(p, new NodeKindKey(BTreeKinds.Selector),  Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(a, new NodeKindKey(BTreeKinds.Sequence),  Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(b, new NodeKindKey(BTreeKinds.Action),    Vector2.Zero, null));

        graph.RegisterPins(p, out var pOut, out var pIn);
        graph.RegisterPins(a, out var aOut, out var aIn);
        graph.RegisterPins(b, out var bOut, out var bIn);

        // Wire a -> p  (a child of p).
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), aOut, pIn));
        asset.FindNode(p.Value)!.ChildVisualIds.Should().Contain(a.Value);
        // Wire b -> a  (b child of a).
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), bOut, aIn));
        asset.FindNode(a.Value)!.ChildVisualIds.Should().Contain(b.Value);

        // Attempt: make p a child of b (would create cycle p->a->b->p).
        // Reversed convention: child output = pOut, parent input = bIn.
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), pOut, bIn));

        // Model unchanged — b must NOT have p as a child.
        asset.FindNode(b.Value)!.ChildVisualIds.Should().NotContain(p.Value);
        // Original structure intact.
        asset.FindNode(p.Value)!.ChildVisualIds.Should().Contain(a.Value);
        asset.FindNode(a.Value)!.ChildVisualIds.Should().Contain(b.Value);
    }

    [Fact]
    public void AddLink_SelfParent_IsRejected()
    {
        var (asset, graph, sink) = Build();
        var n = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(n, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        graph.RegisterPins(n, out var nOut, out var nIn);

        // Attempt to wire n -> n (self-parent).
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), nOut, nIn));

        // Node must not list itself as a child.
        asset.FindNode(n.Value)!.ChildVisualIds.Should().NotContain(n.Value);
    }

    [Fact]
    public void MoveNodes_updates_position()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.MoveNodes(new[] { new NodeMove(nodeId, new Vector2(100f, 200f)) }));

        asset.FindNode(nodeId.Value)!.Position.Should().Be(new Vector2(100f, 200f));
    }

    [Fact]
    public void SetNodeProperty_comment_updates_node()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.SetNodeProperty(nodeId, "comment", "hello world"));

        asset.FindNode(nodeId.Value)!.Comment.Should().Be("hello world");
    }

    [Fact]
    public void SetNodeProperty_isBreakpoint_sets_flag()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Action), Vector2.Zero, null));
        sink.Apply(new GraphCommand.SetNodeProperty(nodeId, "isBreakpoint", true));

        asset.FindNode(nodeId.Value)!.IsBreakpoint.Should().BeTrue();
    }

    [Fact]
    public void AddAttachment_creates_repeater_pill()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { ["decoratorType"] = NodeType.Repeater, ["intParam"] = 3 };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "R", "x3", null, 0, props));

        var pill = asset.FindPill(attId.Value);
        pill.Should().NotBeNull();
        pill!.DecoratorType.Should().Be(NodeType.Repeater);
        pill.IntParam.Should().Be(3);
        pill.HostNodeVisualId.Should().Be(nodeId.Value);
    }

    [Fact]
    public void RemoveAttachment_removes_pill()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { ["decoratorType"] = NodeType.Inverter };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "I", null, null, 0, props));
        asset.FindPill(attId.Value).Should().NotBeNull();

        sink.Apply(new GraphCommand.RemoveAttachments(new[] { attId }));

        asset.FindPill(attId.Value).Should().BeNull();
    }

    [Fact]
    public void SetAttachmentProperty_intParam_updates_pill()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { ["decoratorType"] = NodeType.Repeater, ["intParam"] = 1 };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "R", null, null, 0, props));

        sink.Apply(new GraphCommand.SetAttachmentProperty(attId, "intParam", 5));

        asset.FindPill(attId.Value)!.IntParam.Should().Be(5);
    }

    [Fact]
    public void ReorderAttachments_updates_stack_indices()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var att0   = AttachmentId.NewId();
        var att1   = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddAttachment(att0, nodeId, AttachmentCategory.Decorator, "I", null, null, 0,
            new Dictionary<string, object?> { ["decoratorType"] = NodeType.Inverter }));
        sink.Apply(new GraphCommand.AddAttachment(att1, nodeId, AttachmentCategory.Decorator, "R", null, null, 1,
            new Dictionary<string, object?> { ["decoratorType"] = NodeType.Repeater }));

        sink.Apply(new GraphCommand.ReorderAttachments(nodeId, new[] { att1, att0 }));

        asset.FindPill(att1.Value)!.StackIndex.Should().Be(0);
        asset.FindPill(att0.Value)!.StackIndex.Should().Be(1);
    }

    [Fact]
    public void Batch_applies_all_sub_commands()
    {
        var (asset, _, sink) = Build();
        var nodeId1 = NodeId.NewId();
        var nodeId2 = NodeId.NewId();

        var result = sink.Apply(new GraphCommand.Batch("test", new GraphCommand[]
        {
            new GraphCommand.AddNode(nodeId1, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null),
            new GraphCommand.AddNode(nodeId2, new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null),
        }));

        result.Success.Should().BeTrue();
        asset.FindNode(nodeId1.Value).Should().NotBeNull();
        asset.FindNode(nodeId2.Value).Should().NotBeNull();
    }

    [Fact]
    public void Apply_unsupported_command_returns_failure()
    {
        var (_, _, sink) = Build();

        var result = sink.Apply(new GraphCommand.SetNodeCollapsed(NodeId.NewId(), true));

        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
    }

    // ---- ChangeParentMultiple (BCP-BATCH-01-FIX BUG 1) ----------------------

    /// <summary>
    /// ChangeParentMultiple (the command the canvas issues for every node drop, BPF-029)
    /// must persist NewLocalPosition to the asset so the node does not jump back.
    /// </summary>
    [Fact]
    public void ChangeParentMultiple_persists_new_position()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();

        // Add a node at origin.
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        asset.FindNode(nodeId.Value)!.Position.Should().Be(Vector2.Zero);

        // Drop the node to a new position via ChangeParentMultiple.
        var newPos = new Vector2(123f, 456f);
        var result = sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[] { new ChangeParentMove(nodeId, null, null, newPos) }));

        result.Success.Should().BeTrue();
        // Asset node position must be updated.
        asset.FindNode(nodeId.Value)!.Position.Should().Be(newPos);
    }

    /// <summary>
    /// ChangeParentMultiple with multiple nodes must update all of them.
    /// </summary>
    [Fact]
    public void ChangeParentMultiple_multiple_nodes_all_positions_updated()
    {
        var (asset, _, sink) = Build();
        var id1 = NodeId.NewId();
        var id2 = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(id1, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(id2, new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        var pos1 = new Vector2(10f, 20f);
        var pos2 = new Vector2(30f, 40f);
        var result = sink.Apply(new GraphCommand.ChangeParentMultiple(
            new[]
            {
                new ChangeParentMove(id1, null, null, pos1),
                new ChangeParentMove(id2, null, null, pos2),
            }));

        result.Success.Should().BeTrue();
        asset.FindNode(id1.Value)!.Position.Should().Be(pos1);
        asset.FindNode(id2.Value)!.Position.Should().Be(pos2);
    }

    // ---- BATCH-16: break-link works for projected (JSON-loaded) links ---------

    /// <summary>
    /// A projected link (one that exists in the asset but was NOT created via
    /// ApplyAddLink) must be deletable. This is the core fix: ApplyRemoveLinks
    /// resolves via _graph.FindLink instead of the session-only _links dict.
    /// </summary>
    [Fact]
    public void RemoveLinks_ProjectedLink_DeletesIt()
    {
        var asset = MakeAsset();
        var root = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Root,
            DisplayLabel = "Root",
        };
        var child = new BTreeEditorNode
        {
            VisualId     = Guid.NewGuid(),
            KernelType   = NodeType.Action,
            DisplayLabel = "Leaf",
        };
        asset.AddNode(root);
        asset.AddNode(child);

        // Project the link directly: Root has Child as a child
        // (no ApplyAddLink — simulates JSON-loaded topology).
        root.ChildVisualIds.Add(child.VisualId);

        // Construct BTreeGraphModel so the link is projected.
        var graph = new BTreeGraphModel(asset);
        var sink  = new BTreeCommandSink(asset, graph);

        // Get the projected link's LinkId from graph.Links.
        var link = graph.Links
            .First(l => graph.FindPin(l.FromPin)?.OwnerNodeId.Value == child.VisualId
                     && graph.FindPin(l.ToPin)?.OwnerNodeId.Value == root.VisualId);
        var linkId = link.Id;

        // Act: remove the projected link.
        var result = sink.Apply(new GraphCommand.RemoveLinks(new[] { linkId }));

        // Assert: child no longer in root's ChildVisualIds.
        result.Success.Should().BeTrue();
        root.ChildVisualIds.Should().NotContain(child.VisualId);
    }

    /// <summary>
    /// Regression: a session-added link (via ApplyAddLink) is still removable.
    /// Exercises the graph-model path (not the _links fallback).
    /// </summary>
    [Fact]
    public void RemoveLinks_SessionAddedLink_DeletesIt()
    {
        var (asset, graph, sink) = Build();
        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);

        var linkId = new LinkId(Guid.NewGuid());

        // Register the link in the stub so FindLink resolves it
        // (exercises graph path, not fallback).
        graph.RegisterLink(linkId, childOut, parentIn);

        // Add link via sink (populates _links AND attaches child to parent).
        sink.Apply(new GraphCommand.AddLink(linkId, childOut, parentIn));
        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().Contain(childId.Value);

        // Act: remove via graph path.
        sink.Apply(new GraphCommand.RemoveLinks(new[] { linkId }));

        // Assert: child detached.
        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().NotContain(childId.Value);
    }

    // ---- Wire direction: drag may start on either endpoint ------------------

    /// <summary>
    /// The user can start a wire drag from a parent's bottom (Input) pin and drop on
    /// a child's top (Output) pin — so the AddLink command's From is the Input pin.
    /// ApplyAddLink must resolve child/parent by pin DIRECTION, not by From/To order,
    /// so the parent still receives the child (not the inverse).
    /// </summary>
    [Fact]
    public void AddLink_ReversedDragDirection_AssignsParentCorrectly()
    {
        var (asset, graph, sink) = Build();
        var parentId = NodeId.NewId();
        var childId  = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(parentId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(childId,  new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(parentId, out _, out var parentIn);
        graph.RegisterPins(childId,  out var childOut, out _);

        // Drag STARTED at the parent's Input pin -> From = parentIn, To = childOut.
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), parentIn, childOut));

        // Parent (Input-pin owner) must receive the child (Output-pin owner) — not inverted.
        asset.FindNode(parentId.Value)!.ChildVisualIds.Should().Contain(childId.Value);
        asset.FindNode(childId.Value)!.ChildVisualIds.Should().NotContain(parentId.Value);
    }

    /// <summary>
    /// A same-direction drag (two Output or two Input pins) is invalid and must be rejected.
    /// </summary>
    [Fact]
    public void AddLink_SameDirectionPins_IsRejected()
    {
        var (asset, graph, sink) = Build();
        var aId = NodeId.NewId();
        var bId = NodeId.NewId();

        sink.Apply(new GraphCommand.AddNode(aId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        sink.Apply(new GraphCommand.AddNode(bId, new NodeKindKey(BTreeKinds.Action),   Vector2.Zero, null));

        graph.RegisterPins(aId, out var aOut, out _);
        graph.RegisterPins(bId, out var bOut, out _);

        // Output -> Output: nonsensical, must not attach either way.
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), aOut, bOut));

        asset.FindNode(aId.Value)!.ChildVisualIds.Should().NotContain(bId.Value);
        asset.FindNode(bId.Value)!.ChildVisualIds.Should().NotContain(aId.Value);
    }

    // ---- Drag-to-create auto-wire: adopt canvas-supplied pin IDs ------------

    /// <summary>
    /// A leaf created via drag-to-create carries pre-generated PinIds ([output] only).
    /// The node must adopt that ID for its OutputPinId so the auto-wire link resolves.
    /// </summary>
    [Fact]
    public void AddNode_LeafAdoptsSuppliedOutputPinId()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var suppliedOut = new PinId(Guid.NewGuid());

        sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey(BTreeKinds.Action), Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = new List<PinId> { suppliedOut } }));

        asset.FindNode(nodeId.Value)!.OutputPinId.Should().Be(suppliedOut.Value);
    }

    /// <summary>
    /// A composite created via drag-to-create carries PinIds in catalog order
    /// ([input, output]). Both must be adopted onto the matching pin.
    /// </summary>
    [Fact]
    public void AddNode_CompositeAdoptsSuppliedInputThenOutputPinIds()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var suppliedIn  = new PinId(Guid.NewGuid());
        var suppliedOut = new PinId(Guid.NewGuid());

        sink.Apply(new GraphCommand.AddNode(
            nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = new List<PinId> { suppliedIn, suppliedOut } }));

        var node = asset.FindNode(nodeId.Value)!;
        node.InputPinId.Should().Be(suppliedIn.Value);
        node.OutputPinId.Should().Be(suppliedOut.Value);
    }

    /// <summary>
    /// End-to-end auto-wire over the real BTreeGraphModel: a leaf dropped from a
    /// parent's Input pin is created with the canvas-supplied output PinId, then the
    /// auto-wire AddLink (which references that exact PinId) must resolve and attach
    /// the leaf as the parent's child. Before the adopt-pin-IDs fix, FindPin failed
    /// and the link was silently dropped.
    /// </summary>
    [Fact]
    public void AddNode_then_AutoWire_AttachesChild_OverRealGraphModel()
    {
        var asset = MakeAsset();
        var parent = new BTreeEditorNode { VisualId = Guid.NewGuid(), KernelType = NodeType.Sequence };
        asset.AddNode(parent);

        var graph = new BTreeGraphModel(asset);
        var sink  = new BTreeCommandSink(asset, graph);

        var childId  = NodeId.NewId();
        var childOut = new PinId(Guid.NewGuid());

        // Drop-create the leaf with a pre-generated output pin id (as the canvas does).
        sink.Apply(new GraphCommand.AddNode(
            childId, new NodeKindKey(BTreeKinds.Action), Vector2.Zero,
            new Dictionary<string, object?> { ["PinIds"] = new List<PinId> { childOut } }));

        // The supplied pin id now resolves to the child over the real graph model.
        graph.FindPin(childOut)!.OwnerNodeId.Value.Should().Be(childId.Value);

        // Auto-wire: child.Output -> parent.Input (parent's pin id is derived).
        var parentIn = new PinId(parent.InputPinId);
        sink.Apply(new GraphCommand.AddLink(new LinkId(Guid.NewGuid()), childOut, parentIn));

        parent.ChildVisualIds.Should().Contain(childId.Value);
    }

    /// <summary>
    /// Removing a random/non-existent LinkId must not throw and must leave the model unchanged.
    /// </summary>
    [Fact]
    public void RemoveLinks_UnknownLink_NoThrow()
    {
        var (asset, graph, sink) = Build();
        var nodeId = NodeId.NewId();
        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));

        var randomLinkId = new LinkId(Guid.NewGuid());

        // Act: removing a non-existent link should not throw.
        var result = sink.Apply(new GraphCommand.RemoveLinks(new[] { randomLinkId }));

        // Assert: success, model unchanged.
        result.Success.Should().BeTrue();
        asset.FindNode(nodeId.Value).Should().NotBeNull();
    }

    // ---- DEC-03: picker path (paletteKind) -------------------------------------

    /// <summary>
    /// AddAttachment with paletteKind="bt.decorator.repeater" (no decoratorType)
    /// must create a Repeater pill with default IntParam=1.
    /// </summary>
    [Fact]
    public void AddAttachment_PaletteKind_Repeater_CreatesPillWithDefaultIntParam()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = BTreeKinds.Repeater };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "R", null, null, 0, props));

        var pill = asset.FindPill(attId.Value);
        pill.Should().NotBeNull();
        pill!.DecoratorType.Should().Be(NodeType.Repeater);
        pill.HostNodeVisualId.Should().Be(nodeId.Value);
        pill.StackIndex.Should().Be(0);
        pill.IntParam.Should().NotBeNull("default IntParam must be set for Repeater");
        pill.IntParam.Should().Be(1);
    }

    /// <summary>
    /// AddAttachment with paletteKind="bt.decorator.cooldown" (no decoratorType)
    /// must create a Cooldown pill with default FloatParam=1f.
    /// </summary>
    [Fact]
    public void AddAttachment_PaletteKind_Cooldown_CreatesPillWithDefaultFloatParam()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = BTreeKinds.Cooldown };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "C", null, null, 1, props));

        var pill = asset.FindPill(attId.Value);
        pill.Should().NotBeNull();
        pill!.DecoratorType.Should().Be(NodeType.Cooldown);
        pill.HostNodeVisualId.Should().Be(nodeId.Value);
        pill.StackIndex.Should().Be(1);
        pill.FloatParam.Should().NotBeNull("default FloatParam must be set for Cooldown");
        pill.FloatParam.Should().Be(1f);
    }

    /// <summary>
    /// AddAttachment with a paletteKind that is NOT a decorator (e.g. Sequence)
    /// must be a safe no-op — no pill added.
    /// </summary>
    [Fact]
    public void AddAttachment_PaletteKind_NonDecorator_IsNoOp()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = BTreeKinds.Sequence };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "?", null, null, 0, props));

        asset.FindPill(attId.Value).Should().BeNull("non-decorator paletteKind must not add a pill");
    }

    /// <summary>
    /// Regression guard: the existing decoratorType-based path must still add a pill.
    /// </summary>
    [Fact]
    public void AddAttachment_DecoratorType_ExistingPath_StillWorks()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { ["decoratorType"] = NodeType.Inverter };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "I", null, null, 0, props));

        var pill = asset.FindPill(attId.Value);
        pill.Should().NotBeNull("decoratorType path must still add a pill");
        pill!.DecoratorType.Should().Be(NodeType.Inverter);
    }

    /// <summary>
    /// Round-trip: a pill added via the picker path must survive model→DTO→model.
    /// </summary>
    [Fact]
    public void AddAttachment_PaletteKind_Repeater_RoundTripsViaMapper()
    {
        var (asset, _, sink) = Build();
        var nodeId = NodeId.NewId();
        var attId  = AttachmentId.NewId();

        sink.Apply(new GraphCommand.AddNode(nodeId, new NodeKindKey(BTreeKinds.Sequence), Vector2.Zero, null));
        var props = new Dictionary<string, object?> { [AttachmentHostPropertyKeys.Kind] = BTreeKinds.Repeater };
        sink.Apply(new GraphCommand.AddAttachment(attId, nodeId, AttachmentCategory.Decorator, "R", null, null, 0, props));

        // Verify the pill exists before round-trip.
        asset.FindPill(attId.Value).Should().NotBeNull();

        // Round-trip via mapper.
        var dto      = BehaviorTreeAssetMapper.ToDto(asset);
        var restored = BehaviorTreeAssetMapper.FromDto(dto);

        var restoredPill = restored.FindPill(attId.Value);
        restoredPill.Should().NotBeNull("pill must survive model→DTO→model round-trip");
        restoredPill!.DecoratorType.Should().Be(NodeType.Repeater);
        restoredPill.IntParam.Should().Be(1);
        restoredPill.HostNodeVisualId.Should().Be(nodeId.Value);
    }
}
