# BATCH-22 Instructions

## Tasks
- **BT-S1-08** — `BTreeCommandSink` (IGraphCommandSink translating GraphCommand to BehaviorTreeAsset mutations)
- **BT-S1-14** — BTree facet structs (BTreeActionFacet, BTreeConditionFacet, BTreeWaitFacet, composite facets, decorator pill facets)

## Constraints (from AGENTS.md)
- No Unicode in comments or string literals. Use ASCII only.
- Preserve all existing comments exactly.
- Minimize textual diffs — only change what is required.
- Build must be 0 errors, 0 warnings before finishing.

---

## BT-S1-08: BTreeCommandSink

### Step 1 — Extend BTreeKinds.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeKinds.cs`

Add two new `internal static` members to the existing `BTreeKinds` class **after** the existing `IsLeaf` method:

```csharp
    public static bool IsDecorator(NodeKindKey key) =>
        key.Id == Inverter     ||
        key.Id == Repeater     ||
        key.Id == Cooldown     ||
        key.Id == ForceSuccess ||
        key.Id == ForceFailure ||
        key.Id == UntilSuccess ||
        key.Id == UntilFailure;

    public static NodeType KindIdToNodeType(string kindId) => kindId switch
    {
        Root             => NodeType.Root,
        Sequence         => NodeType.Sequence,
        Selector         => NodeType.Selector,
        ObserverSelector => NodeType.ObserverSelector,
        Parallel         => NodeType.Parallel,
        Action           => NodeType.Action,
        Condition        => NodeType.Condition,
        Wait             => NodeType.Wait,
        Subtree          => NodeType.Subtree,
        Inverter         => NodeType.Inverter,
        Repeater         => NodeType.Repeater,
        Cooldown         => NodeType.Cooldown,
        ForceSuccess     => NodeType.ForceSuccess,
        ForceFailure     => NodeType.ForceFailure,
        UntilSuccess     => NodeType.UntilSuccess,
        UntilFailure     => NodeType.UntilFailure,
        _                => NodeType.Action,
    };
```

Note: `KindIdToNodeType` is a switch expression on `string kindId`; the arms compare to the `const string` fields in the same class. This works because C# switch expressions support constant patterns.

### Step 2 — Create BTreeCommandSink.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs`

Create this file with the following content:

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fbt;
using Hrot.BTree.Editor.Model;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Translates NodeEditor GraphCommand records into mutations on BehaviorTreeAsset.
/// Implements the reversed-pin convention: From=child output, To=parent input.
/// </summary>
internal sealed class BTreeCommandSink : IGraphCommandSink
{
    private readonly BehaviorTreeAsset _asset;
    private readonly IGraphModel       _graph;

    // Maps link Guid -> (childVisualId, parentVisualId) for RemoveLinks lookup.
    private readonly Dictionary<Guid, (Guid child, Guid parent)> _links = new();

    internal BTreeCommandSink(BehaviorTreeAsset asset, IGraphModel graph)
    {
        _asset = asset;
        _graph = graph;
    }

    // ---- IGraphCommandSink --------------------------------------------------

    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.MoveNodes m:
                ApplyNodeMoves(m.Moves);
                break;

            case GraphCommand.AddNode add:
                ApplyAddNode(add);
                break;

            case GraphCommand.RemoveNodes rem:
                ApplyRemoveNodes(rem.Nodes);
                break;

            case GraphCommand.AddLink link:
                ApplyAddLink(link.AssignedId, link.From, link.To);
                break;

            case GraphCommand.RemoveLinks unlink:
                ApplyRemoveLinks(unlink.Links);
                break;

            case GraphCommand.SetNodeProperty setProp:
                ApplySetNodeProperty(setProp.Node, setProp.Key, setProp.Value);
                break;

            case GraphCommand.AddAttachment att:
                ApplyAddPill(att);
                break;

            case GraphCommand.RemoveAttachments remAtt:
                ApplyRemovePills(remAtt.AttachmentIds);
                break;

            case GraphCommand.SetAttachmentProperty setAtt:
                ApplySetPillProperty(setAtt.Id, setAtt.Key, setAtt.Value);
                break;

            case GraphCommand.ReorderAttachments reorder:
                ApplyReorderPills(reorder.HostNodeId, reorder.NewOrder);
                break;

            case GraphCommand.Batch batch:
                foreach (var sub in batch.Commands)
                    Apply(sub);
                break;

            default:
                return new GraphCommandResult(false, $"Unsupported: {command.GetType().Name}");
        }

        return new GraphCommandResult(true, null);
    }

    // ---- Mutation helpers ---------------------------------------------------

    private void ApplyNodeMoves(IReadOnlyList<NodeMove> moves)
    {
        foreach (var m in moves)
        {
            var node = _asset.FindNode(m.Node.Value);
            if (node != null)
                node.Position = m.NewPosition;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddNode(GraphCommand.AddNode add)
    {
        var nodeType = BTreeKinds.KindIdToNodeType(add.Kind.Id);
        var node = new BTreeEditorNode
        {
            VisualId        = add.AssignedId.Value,
            KernelType      = nodeType,
            KernelBlobIndex = -1,
            Position        = add.Position,
            DisplayLabel    = add.Kind.Id,
        };
        _asset.AddNode(node);
        _asset.MarkDirty();
    }

    private void ApplyRemoveNodes(IReadOnlyList<NodeId> nodeIds)
    {
        foreach (var id in nodeIds)
            _asset.RemoveNode(id.Value);
        _asset.MarkDirty();
    }

    private void ApplyAddLink(LinkId linkId, PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);
        if (fromPin == null || toPin == null)
            return;

        // Reversed convention: From = child output, To = parent input.
        var childId  = fromPin.OwnerNodeId.Value;
        var parentId = toPin.OwnerNodeId.Value;

        var parent = _asset.FindNode(parentId);
        if (parent == null)
            return;

        if (!parent.ChildVisualIds.Contains(childId))
            parent.ChildVisualIds.Add(childId);

        _links[linkId.Value] = (childId, parentId);
        _asset.MarkDirty();
    }

    private void ApplyRemoveLinks(IReadOnlyList<LinkId> linkIds)
    {
        foreach (var id in linkIds)
        {
            if (_links.TryGetValue(id.Value, out var pair))
            {
                var parent = _asset.FindNode(pair.parent);
                parent?.ChildVisualIds.Remove(pair.child);
                _links.Remove(id.Value);
            }
        }
        _asset.MarkDirty();
    }

    private void ApplySetNodeProperty(NodeId nodeId, string key, object? value)
    {
        var node = _asset.FindNode(nodeId.Value);
        if (node == null)
            return;

        switch (key)
        {
            case "comment":
                node.Comment = value as string;
                break;
            case "isBreakpoint":
                node.IsBreakpoint = value is bool b && b;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyAddPill(GraphCommand.AddAttachment att)
    {
        if (att.HostProperties == null)
            return;
        if (!att.HostProperties.TryGetValue("decoratorType", out var dtObj))
            return;
        if (dtObj is not NodeType dt)
            return;

        var pill = new BTreeEditorPill
        {
            VisualId         = att.NewId.Value,
            HostNodeVisualId = att.HostNodeId.Value,
            DecoratorType    = dt,
            StackIndex       = att.StackIndex,
        };

        if (att.HostProperties.TryGetValue("intParam", out var ip) && ip is int intVal)
            pill.IntParam = intVal;
        if (att.HostProperties.TryGetValue("floatParam", out var fp) && fp is float floatVal)
            pill.FloatParam = floatVal;
        if (att.HostProperties.TryGetValue("comment", out var cp) && cp is string comment)
            pill.Comment = comment;

        _asset.AddPill(pill);
        _asset.MarkDirty();
    }

    private void ApplyRemovePills(IReadOnlyList<AttachmentId> ids)
    {
        foreach (var id in ids)
            _asset.RemovePill(id.Value);
        _asset.MarkDirty();
    }

    private void ApplySetPillProperty(AttachmentId id, string key, object? value)
    {
        var pill = _asset.FindPill(id.Value);
        if (pill == null)
            return;

        switch (key)
        {
            case "intParam":
                pill.IntParam = value is int i ? i : null;
                break;
            case "floatParam":
                pill.FloatParam = value is float f ? f : null;
                break;
            case "comment":
                pill.Comment = value as string;
                break;
        }
        _asset.MarkDirty();
    }

    private void ApplyReorderPills(NodeId hostNodeId, IReadOnlyList<AttachmentId> newOrder)
    {
        for (int i = 0; i < newOrder.Count; i++)
        {
            var pill = _asset.FindPill(newOrder[i].Value);
            if (pill != null && pill.HostNodeVisualId == hostNodeId.Value)
                pill.StackIndex = i;
        }
        _asset.MarkDirty();
    }
}
```

### Step 3 — Create BTreeCommandSinkTests.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeCommandSinkTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using Fbt;
using Hrot.BTree.Editor.Host;
using Hrot.BTree.Editor.Model;
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

    private sealed class StubGraph : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel>  _nodes = new();
        private readonly Dictionary<PinId,  StubPin>     _pins  = new();

        public GraphId Id => GraphId.NewId();
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => new("test", "test", false, false);
        public IReadOnlyCollection<INodeModel>   Nodes    => _nodes.Values;
        public IReadOnlyCollection<ILinkModel>   Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067

        // Register a node's two exec pins (output for child role, input for parent role).
        public void RegisterPins(NodeId nodeId, out PinId outputPin, out PinId inputPin)
        {
            outputPin = new PinId(Guid.NewGuid());
            inputPin  = new PinId(Guid.NewGuid());
            _pins[outputPin] = new StubPin(outputPin, nodeId, PinDirection.Output);
            _pins[inputPin]  = new StubPin(inputPin,  nodeId, PinDirection.Input);
        }

        public INodeModel?  FindNode(NodeId id) => _nodes.TryGetValue(id, out var n) ? n : null;
        public IPinModel?   FindPin(PinId id)   => _pins.TryGetValue(id, out var p) ? p : null;
        public ILinkModel?  FindLink(LinkId id) => null;
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
}
```

---

## BT-S1-14: BTree Facet Structs

### Step 4 — Create Inspector/BehaviorHashPickerAttribute.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BehaviorHashPickerAttribute.cs`

```csharp
using System;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a behavior-method picker
/// dropdown populated from the editor's BehaviorRegistry.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BehaviorHashPickerAttribute : Attribute { }
```

### Step 5 — Create Inspector/BlackboardFieldPickerAttribute.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BlackboardFieldPickerAttribute.cs`

```csharp
using System;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a blackboard field
/// picker dropdown constrained to fields compatible with the action's expression-target type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BlackboardFieldPickerAttribute : Attribute { }
```

### Step 6 — Create Inspector/BTreeFacets.cs

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Inspector/BTreeFacets.cs`

```csharp
using StructEdit.Core.Attributes;

namespace Hrot.BTree.Editor.Inspector;

// ---- Leaf node facets -------------------------------------------------------

/// <summary>Inspector facet for Action leaf nodes.</summary>
public struct BTreeActionFacet
{
    [EditDisplayName("Method")]
    [BehaviorHashPicker]
    public string MethodFqn;

    [EditDisplayName("Expression target (blackboard field)")]
    [BlackboardFieldPicker]
    public string? ExpressionTargetField;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public string LastResult;

    [EditReadOnly]
    public int TickCount;
}

/// <summary>Inspector facet for Condition leaf nodes.</summary>
public struct BTreeConditionFacet
{
    [EditDisplayName("Method")]
    [BehaviorHashPicker]
    public string MethodFqn;

    [EditDisplayName("Expression target (blackboard field)")]
    [BlackboardFieldPicker]
    public string? ExpressionTargetField;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public string LastResult;

    [EditReadOnly]
    public int TickCount;
}

/// <summary>Inspector facet for Wait leaf nodes.</summary>
public struct BTreeWaitFacet
{
    [EditDisplayName("Duration (seconds)")]
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

// ---- Composite node facets --------------------------------------------------

/// <summary>Inspector facet for Sequence composite nodes.</summary>
public struct BTreeSequenceFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for Selector composite nodes.</summary>
public struct BTreeSelectorFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for ObserverSelector composite nodes.</summary>
public struct BTreeObserverSelectorFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for Parallel composite nodes.</summary>
public struct BTreeParallelFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for the Root node.</summary>
public struct BTreeRootFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Subtree leaf nodes.</summary>
public struct BTreeSubtreeFacet
{
    [EditDisplayName("Referenced asset")]
    public string SubtreeName;

    [EditReadOnly]
    public string SubtreeAssetId;

    [EditReadOnly]
    public bool IsResolved;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

// ---- Decorator pill facets --------------------------------------------------

/// <summary>Inspector facet for Repeater decorator pills.</summary>
public struct BTreeRepeaterFacet
{
    [EditDisplayName("Count")]
    [EditRange(1, 9999)]
    public int Count;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Cooldown decorator pills.</summary>
public struct BTreeCooldownFacet
{
    [EditDisplayName("Duration (seconds)")]
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Inverter decorator pills.</summary>
public struct BTreeInverterFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for ForceSuccess decorator pills.</summary>
public struct BTreeForceSuccessFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for ForceFailure decorator pills.</summary>
public struct BTreeForceFailureFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for UntilSuccess decorator pills.</summary>
public struct BTreeUntilSuccessFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for UntilFailure decorator pills.</summary>
public struct BTreeUntilFailureFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
```

---

## Build and Test Verification

After creating/modifying all the files above:

1. Run `dotnet build "Hrot\Subsystems\AI\Hrot.BTree.Editor\Hrot.BTree.Editor.csproj"` — must be 0 errors, 0 warnings.
2. Run `dotnet test "Hrot\Subsystems\AI\Hrot.BTree.Editor.Tests\Hrot.BTree.Editor.Tests.csproj"` — all 58 tests must pass (44 existing + 14 new BTreeCommandSinkTests).

## Checklist
- [ ] `BTreeKinds.cs` — added `IsDecorator()` and `KindIdToNodeType()` after `IsLeaf()`
- [ ] `BTreeCommandSink.cs` — created with all command handlers
- [ ] `Inspector/BehaviorHashPickerAttribute.cs` — created
- [ ] `Inspector/BlackboardFieldPickerAttribute.cs` — created
- [ ] `Inspector/BTreeFacets.cs` — created with all 14 facet structs
- [ ] `BTreeCommandSinkTests.cs` — created with 14 tests
- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: all pass
