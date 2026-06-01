using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using NodeEditor.UI.Canvas;
using Xunit;

namespace NodeEditor.UI.Tests.Canvas;

/// <summary>
/// BPF-028: CommitNodeDrop must route through view.Execute so moves land on the undo stack.
/// BPF-029: CommitNodeDrop must emit a single ChangeParentMultiple for all selected nodes.
/// BPF-030: CommitNodeDrop must skip nodes whose ancestor is also being dragged.
/// BPF-048: Coverage verification — all three invariants are tested together.
/// </summary>
public sealed class ContainerDragTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private sealed class FakeNodeModel : INodeModel
    {
        public FakeNodeModel(NodeId id, Vector2 position, NodeId? parent = null)
        {
            Id = id;
            Position = position;
            ParentContainerId = parent;
        }

        public NodeId      Id               { get; }
        public NodeKindKey Kind             => new("node");
        public string      Title            => "N";
        public string?     Subtitle         => null;
        public NodeCategory Category        => NodeCategory.Function;
        public Vector2     Position         { get; set; }
        public Vector2?    SizeOverride     => null;
        public NodeState   State            => NodeState.Normal;
        public string?     StatusTooltip    => null;
        public bool        IsCollapsed      => false;
        public bool        ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => Array.Empty<IPinModel>();
        public NodeId? ParentContainerId { get; }
    }

    private sealed class FakeContainerModel : IContainerNodeModel
    {
        private readonly List<NodeId>           _childIds    = new();
        private readonly List<RegionDescriptor> _regions     = new();

        public FakeContainerModel(NodeId id, Vector2 position, NodeId? parent = null)
        {
            Id = id;
            Position = position;
            ParentContainerId = parent;
        }

        public NodeId      Id               { get; }
        public NodeKindKey Kind             => new("container");
        public string      Title            => "C";
        public string?     Subtitle         => null;
        public NodeCategory Category        => NodeCategory.Function;
        public Vector2     Position         { get; set; }
        public Vector2?    SizeOverride     => null;
        public NodeState   State            => NodeState.Normal;
        public string?     StatusTooltip    => null;
        public bool        IsCollapsed      => false;
        public bool        ShowAdvancedPins => false;
        public IReadOnlyList<IPinModel> Pins => Array.Empty<IPinModel>();
        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds => _childIds;
        public IReadOnlyList<RegionDescriptor> Regions => _regions;
        public ContainerPadding Padding => ContainerPadding.Default;
        public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
        public NodeId? ParentContainerId { get; }
        public int GetRegionIndexForChild(NodeId childId) => -1;

        public void AddChild(NodeId childId)
        {
            if (!_childIds.Contains(childId)) _childIds.Add(childId);
        }
    }

    private sealed class FakeGraphModel : IGraphModel
    {
        private readonly Dictionary<NodeId, INodeModel> _nodes = new();

        public GraphId             Id          => GraphId.Empty;
        public string              DisplayName => "test";
        public GraphKindDescriptor Kind        => new("test", "Test", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => _nodes.Values;
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();

        public event Action<GraphChangeNotification>? Changed { add { } remove { } }

        public INodeModel?    FindNode(NodeId id)    => _nodes.TryGetValue(id, out var n) ? n : null;
        public IPinModel?     FindPin(PinId id)      => null;
        public ILinkModel?    FindLink(LinkId id)    => null;
        public ICommentModel? FindComment(CommentId id) => null;

        public FakeNodeModel AddNode(NodeId id, Vector2 pos, NodeId? parent = null)
        {
            var n = new FakeNodeModel(id, pos, parent);
            _nodes[id] = n;
            return n;
        }

        public FakeContainerModel AddContainer(NodeId id, Vector2 pos, NodeId? parent = null)
        {
            var c = new FakeContainerModel(id, pos, parent);
            _nodes[id] = c;
            return c;
        }
    }

    private sealed class SpySink : IGraphCommandSink
    {
        public List<GraphCommand> Log { get; } = new();
        public GraphCommandResult Apply(GraphCommand cmd)
        {
            Log.Add(cmd);
            return new GraphCommandResult(true, null);
        }
    }

    private sealed class StubTheme : IEditorTheme
    {
        public Vector4 BackgroundColor              => Vector4.Zero;
        public Vector4 GridMinorColor               => Vector4.Zero;
        public Vector4 GridMajorColor               => Vector4.Zero;
        public Vector4 SelectionAccent              => Vector4.Zero;
        public Vector4 PrimarySelectionAccent       => Vector4.Zero;
        public Vector4 ErrorColor                   => Vector4.Zero;
        public Vector4 WarningColor                 => Vector4.Zero;
        public Vector4 TextDefault                  => Vector4.Zero;
        public Vector4 TextMuted                    => Vector4.Zero;
        public float   NodeCornerRadius             => 4f;
        public float   NodeBorderThickness          => 1f;
        public float   NodeHeaderHeight             => 30f;
        public float   PinGlyphSize                 => 10f;
        public float   WireThicknessExec            => 2f;
        public float   WireThicknessData            => 1f;
        public Vector4 GetCategoryHeaderColor(NodeCategory c) => Vector4.Zero;
        public nint  GetFontForSize(float size) => (nint)0;
    }

    private sealed class StubInput : IInputSource
    {
        public Vector2     MousePosition   => Vector2.Zero;
        public Vector2     MouseDelta      => Vector2.Zero;
        public float       WheelDelta      => 0f;
        public KeyModifiers Modifiers      => KeyModifiers.None;
        public ReadOnlySpan<char> TextThisFrame => ReadOnlySpan<char>.Empty;
        public bool IsMouseDown(MouseButton btn)              => false;
        public bool IsMousePressed(MouseButton btn)           => false;
        public bool IsMouseReleased(MouseButton btn)          => false;
        public bool IsMouseDoubleClicked(MouseButton btn)     => false;
        public bool IsKeyDown(EditorKey k)                    => false;
        public bool IsKeyPressed(EditorKey k, bool r = false) => false;
        public bool IsKeyReleased(EditorKey k)                => false;
    }

    private sealed class StubHost : IEditorHostServices
    {
        private readonly SpySink _sink;
        public StubHost(SpySink sink) { _sink = sink; }
        public INodeCatalog   NodeCatalog  => throw new NotImplementedException();
        public ITypeSystem    TypeSystem   => throw new NotImplementedException();
        public ILinkValidator LinkValidator => throw new NotImplementedException();
        public IGraphCommandSink CommandSink => _sink;
        public IPickerRegistry Pickers      => throw new NotImplementedException();
        public IClipboard      Clipboard    => throw new NotImplementedException();
        public IIconProvider   Icons        => throw new NotImplementedException();
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession?   Debug       => null;
        public IInputSource Input           => new StubInput();
        public IEditorTheme Theme           => new StubTheme();
    }

    private static GraphView MakeView(FakeGraphModel model, SpySink sink) =>
        new(model, sink,
            new StubValidator(), new StubTypeSystem(), new StubCatalog(),
            new StubHost(sink));

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId f, PinId t)
            => new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey k, out TypeDisplayInfo i) { i = default!; return false; }
        public Vector4 GetPinColor(TypeKey k) => default;
        public PinShape GetPinShape(TypeKey k, ContainerKind c) => default;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey k) => null;
        public bool AreCompatible(TypeKey f, TypeKey t) => false;
        public bool IsImplicitCast(TypeKey f, TypeKey t) => false;
    }

    private sealed class StubCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
    }

    // ── BPF-028: Undo stack ───────────────────────────────────────────────────

    [Fact]
    public void CommitNodeDrop_SingleRootNode_PushesOneUndoEntry()
    {
        var model = new FakeGraphModel();
        var nodeId = IdGenerator.NewNodeId();
        model.AddNode(nodeId, new Vector2(10f, 20f));

        var sink = new SpySink();
        var view = MakeView(model, sink);
        view.Selection.Add(SelectionEntry.OfNode(nodeId));
        view.Interaction.DragOverridePositions[nodeId] = new Vector2(50f, 60f);
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        view.Undo.UndoCount.Should().Be(1,
            because: "BPF-028: CommitNodeDrop must route through view.Execute to record undo");
    }

    [Fact]
    public void CommitNodeDrop_TwoRootNodes_StillPushesOneUndoEntry()
    {
        var model = new FakeGraphModel();
        var id1   = IdGenerator.NewNodeId();
        var id2   = IdGenerator.NewNodeId();
        model.AddNode(id1, new Vector2(10f, 10f));
        model.AddNode(id2, new Vector2(20f, 20f));

        var sink = new SpySink();
        var view = MakeView(model, sink);
        view.Selection.Add(SelectionEntry.OfNode(id1));
        view.Selection.Add(SelectionEntry.OfNode(id2));
        view.Interaction.DragOverridePositions[id1] = new Vector2(100f, 100f);
        view.Interaction.DragOverridePositions[id2] = new Vector2(200f, 200f);
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        view.Undo.UndoCount.Should().Be(1,
            because: "BPF-028: multiple nodes dragged together should produce one undo entry");
    }

    // ── BPF-029: Single ChangeParentMultiple ──────────────────────────────────

    [Fact]
    public void CommitNodeDrop_TwoNodes_EmitsSingleChangeParentMultiple()
    {
        var model = new FakeGraphModel();
        var id1   = IdGenerator.NewNodeId();
        var id2   = IdGenerator.NewNodeId();
        model.AddNode(id1, new Vector2(10f, 10f));
        model.AddNode(id2, new Vector2(20f, 20f));

        var sink = new SpySink();
        var view = MakeView(model, sink);
        view.Selection.Add(SelectionEntry.OfNode(id1));
        view.Selection.Add(SelectionEntry.OfNode(id2));
        view.Interaction.DragOverridePositions[id1] = new Vector2(100f, 100f);
        view.Interaction.DragOverridePositions[id2] = new Vector2(200f, 200f);
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        sink.Log.Should().HaveCount(1,
            because: "BPF-029: one command is applied (via Execute, which forwards to the sink)");
        sink.Log[0].Should().BeOfType<GraphCommand.ChangeParentMultiple>(
            because: "BPF-029: all node moves are batched into one ChangeParentMultiple");
        var cmd = (GraphCommand.ChangeParentMultiple)sink.Log[0];
        cmd.Moves.Should().HaveCount(2,
            because: "both selected nodes appear in the single ChangeParentMultiple");
    }

    [Fact]
    public void CommitNodeDrop_MovesContainAllSelectedNodeIds()
    {
        var model = new FakeGraphModel();
        var id1   = IdGenerator.NewNodeId();
        var id2   = IdGenerator.NewNodeId();
        var id3   = IdGenerator.NewNodeId();
        model.AddNode(id1, Vector2.Zero);
        model.AddNode(id2, Vector2.Zero);
        model.AddNode(id3, Vector2.Zero);

        var sink = new SpySink();
        var view = MakeView(model, sink);
        float pos = 10f;
        foreach (var id in new[] { id1, id2, id3 })
        {
            view.Selection.Add(SelectionEntry.OfNode(id));
            view.Interaction.DragOverridePositions[id] = new Vector2(pos, pos);
            pos += 10f;
        }
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        var cmd = (GraphCommand.ChangeParentMultiple)sink.Log[0];
        cmd.Moves.Select(m => m.NodeId).Should().BeEquivalentTo(new[] { id1, id2, id3 },
            because: "all three selected nodes must appear in the ChangeParentMultiple");
    }

    // ── BPF-030: Ancestor suppression ────────────────────────────────────────

    [Fact]
    public void HasSelectedAncestor_ContainerParentInSet_ReturnsTrue()
    {
        var model   = new FakeGraphModel();
        var contId  = IdGenerator.NewNodeId();
        var childId = IdGenerator.NewNodeId();
        model.AddContainer(contId, Vector2.Zero);
        model.AddNode(childId, Vector2.Zero, parent: contId);

        var selected = new HashSet<NodeId> { contId, childId };

        CanvasInput.HasSelectedAncestor(childId, selected, model).Should().BeTrue(
            because: "the child's parent container is in the selection set");
    }

    [Fact]
    public void HasSelectedAncestor_ContainerNotInSet_ReturnsFalse()
    {
        var model   = new FakeGraphModel();
        var contId  = IdGenerator.NewNodeId();
        var childId = IdGenerator.NewNodeId();
        model.AddContainer(contId, Vector2.Zero);
        model.AddNode(childId, Vector2.Zero, parent: contId);

        // Only child is selected, not the container
        var selected = new HashSet<NodeId> { childId };

        CanvasInput.HasSelectedAncestor(childId, selected, model).Should().BeFalse(
            because: "the container is not in the selection set");
    }

    [Fact]
    public void HasSelectedAncestor_RootNode_ReturnsFalse()
    {
        var model  = new FakeGraphModel();
        var nodeId = IdGenerator.NewNodeId();
        model.AddNode(nodeId, Vector2.Zero); // root-level, no parent

        var selected = new HashSet<NodeId> { nodeId };

        CanvasInput.HasSelectedAncestor(nodeId, selected, model).Should().BeFalse(
            because: "root nodes have no ancestor");
    }

    [Fact]
    public void CommitNodeDrop_ContainerAndChildBothSelected_ChildIsSuppressed()
    {
        // BPF-030: when a container and one of its children are both selected,
        // only the container should appear in the ChangeParentMultiple moves.
        var model    = new FakeGraphModel();
        var contId   = IdGenerator.NewNodeId();
        var childId  = IdGenerator.NewNodeId();
        var cont     = model.AddContainer(contId, new Vector2(0f, 0f));
        model.AddNode(childId, new Vector2(10f, 10f), parent: contId);
        cont.AddChild(childId);

        var sink = new SpySink();
        var view = MakeView(model, sink);
        view.Selection.Add(SelectionEntry.OfNode(contId));
        view.Selection.Add(SelectionEntry.OfNode(childId));
        view.Interaction.DragOverridePositions[contId]  = new Vector2(100f, 100f);
        view.Interaction.DragOverridePositions[childId] = new Vector2(110f, 110f);
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        sink.Log.Should().HaveCount(1);
        var cmd = (GraphCommand.ChangeParentMultiple)sink.Log[0];
        cmd.Moves.Should().HaveCount(1,
            because: "BPF-030: the child is suppressed because its ancestor (the container) is also selected");
        cmd.Moves[0].NodeId.Should().Be(contId,
            because: "only the container (ancestor) should be included in the move command");
    }

    // ── BPF-048: Combined invariants ─────────────────────────────────────────

    [Fact]
    public void CommitNodeDrop_ThreeNodes_SatisfiesAllThreeInvariants()
    {
        // Three independent root nodes dragged together.
        var model = new FakeGraphModel();
        var id1   = IdGenerator.NewNodeId();
        var id2   = IdGenerator.NewNodeId();
        var id3   = IdGenerator.NewNodeId();
        model.AddNode(id1, Vector2.Zero);
        model.AddNode(id2, Vector2.Zero);
        model.AddNode(id3, Vector2.Zero);

        var sink = new SpySink();
        var view = MakeView(model, sink);
        foreach (var id in new[] { id1, id2, id3 })
        {
            view.Selection.Add(SelectionEntry.OfNode(id));
            view.Interaction.DragOverridePositions[id] = new Vector2(50f, 50f);
        }
        view.Interaction.DropTargetContainerId = null;

        CanvasInput.CommitNodeDrop(view, new StubInput());

        // BPF-028: undo stack has an entry
        view.Undo.UndoCount.Should().Be(1, because: "BPF-028");
        // BPF-029: single ChangeParentMultiple with all three nodes
        sink.Log.Should().HaveCount(1, because: "BPF-029: single command applied");
        sink.Log[0].Should().BeOfType<GraphCommand.ChangeParentMultiple>(because: "BPF-029");
        var cmd = (GraphCommand.ChangeParentMultiple)sink.Log[0];
        cmd.Moves.Should().HaveCount(3, because: "BPF-029: all nodes in one command");
        // BPF-030: no ancestor suppression needed (no parent-child relationships)
        cmd.Moves.Select(m => m.NodeId).Should().BeEquivalentTo(new[] { id1, id2, id3 },
            because: "BPF-030: all independent nodes are included");
    }
}
