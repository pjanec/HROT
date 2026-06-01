using FluentAssertions;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NodeEditor.Core.Tests.View;

/// <summary>Tests for GraphView.NodeCanvasPosition, NodeLocalPosition, and GetParentContainer.</summary>
public sealed class ContainerTransformTests
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
        public NodeId? ParentContainerId { get; set; }
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();
    }

    private sealed class StubContainer : IContainerNodeModel
    {
        public NodeId Id { get; set; } = IdGenerator.NewNodeId();
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
        public NodeId? ParentContainerId { get; set; }
        public IReadOnlyList<IPinModel> Pins => System.Array.Empty<IPinModel>();

        public bool IsContainer => true;
        public IReadOnlyList<NodeId> ChildNodeIds { get; set; } = System.Array.Empty<NodeId>();
        public IReadOnlyList<RegionDescriptor> Regions => System.Array.Empty<RegionDescriptor>();
        public int GetRegionIndexForChild(NodeId childId) => -1;
        public ContainerPadding Padding { get; set; } = ContainerPadding.Default;
        public RegionLayoutOrientation RegionOrientation => RegionLayoutOrientation.VerticalStack;
        public Vector2 MinimumInteriorSize => new(200f, 100f);
    }

    private sealed class GraphModelWithNodes : IGraphModel
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

    private sealed class StubTheme : IEditorTheme
    {
        public Vector4 BackgroundColor        => Vector4.Zero;
        public Vector4 GridMinorColor         => Vector4.Zero;
        public Vector4 GridMajorColor         => Vector4.Zero;
        public Vector4 SelectionAccent        => Vector4.One;
        public Vector4 PrimarySelectionAccent => Vector4.One;
        public Vector4 ErrorColor             => Vector4.One;
        public Vector4 WarningColor           => Vector4.One;
        public Vector4 TextDefault            => Vector4.One;
        public Vector4 TextMuted              => new(0.6f, 0.6f, 0.6f, 1f);
        public float NodeCornerRadius    => 4f;
        public float NodeBorderThickness => 1.5f;
        public float NodeHeaderHeight    => 28f;
        public float PinGlyphSize        => 10f;
        public float WireThicknessExec   => 3f;
        public float WireThicknessData   => 2f;
        public Vector4 GetCategoryHeaderColor(NodeCategory _) => Vector4.Zero;
        public nint GetFontForSize(float _) => System.IntPtr.Zero;
    }

    private sealed class StubSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId from, PinId to)
            => new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
        { info = default!; return false; }
        public Vector4 GetPinColor(TypeKey key) => default;
        public PinShape GetPinShape(TypeKey key, ContainerKind container) => default;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
        public bool AreCompatible(TypeKey from, TypeKey to) => false;
        public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
    }

    private sealed class StubCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => System.Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => System.Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => System.Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => System.Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHost : IEditorHostServices
    {
        private readonly IEditorTheme _theme;
        public StubHost(IEditorTheme theme) { _theme = theme; }
        public INodeCatalog NodeCatalog => new StubCatalog();
        public ITypeSystem TypeSystem => new StubTypeSystem();
        public ILinkValidator LinkValidator => new StubValidator();
        public IGraphCommandSink CommandSink => new StubSink();
        public IPickerRegistry Pickers => throw new System.NotImplementedException();
        public IClipboard Clipboard => throw new System.NotImplementedException();
        public IIconProvider Icons => throw new System.NotImplementedException();
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => throw new System.NotImplementedException();
        public IEditorTheme Theme => _theme;
    }

    private static GraphView MakeView(GraphModelWithNodes model, IEditorTheme? theme = null)
    {
        var t    = theme ?? new StubTheme();
        var sink = new StubSink();
        return new GraphView(model, sink, new StubValidator(), new StubTypeSystem(), new StubCatalog(), new StubHost(t));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RootNode_NodeCanvasPosition_EqualsNodePosition()
    {
        var model = new GraphModelWithNodes();
        var node  = new StubNode { Position = new Vector2(100f, 200f) };
        model.Add(node);

        var view = MakeView(model);
        view.NodeCanvasPosition(node.Id).Should().Be(new Vector2(100f, 200f));
    }

    [Fact]
    public void RootNode_GetParentContainer_ReturnsNull()
    {
        var model = new GraphModelWithNodes();
        var node  = new StubNode();
        model.Add(node);

        var view = MakeView(model);
        view.GetParentContainer(node.Id).Should().BeNull();
    }

    [Fact]
    public void ChildNode_GetParentContainer_ReturnsContainerId()
    {
        var model     = new GraphModelWithNodes();
        var container = new StubContainer { Position = new Vector2(500f, 400f) };
        var child     = new StubNode { ParentContainerId = container.Id };
        model.Add(container);
        model.Add(child);

        var view = MakeView(model);
        view.GetParentContainer(child.Id).Should().Be(container.Id);
    }

    [Fact]
    public void ChildNode_NodeCanvasPosition_IncludesParentOffset()
    {
        // Container at canvas (500, 400). Default padding: Top=8, Left=12.
        // Theme NodeHeaderHeight = 28.
        // Child local position = (20, 30).
        // Expected canvas: (500 + 12 + 20, 400 + 28 + 8 + 30) = (532, 466).
        var model     = new GraphModelWithNodes();
        var container = new StubContainer
        {
            Position = new Vector2(500f, 400f),
            Padding  = ContainerPadding.Default,
        };
        var child = new StubNode
        {
            Position          = new Vector2(20f, 30f),
            ParentContainerId = container.Id,
        };
        model.Add(container);
        model.Add(child);

        var view     = MakeView(model);
        var expected = new Vector2(532f, 466f);
        view.NodeCanvasPosition(child.Id).Should().Be(expected);
    }

    [Fact]
    public void UnknownNode_NodeCanvasPosition_ReturnsZero()
    {
        var model = new GraphModelWithNodes();
        var view  = MakeView(model);
        view.NodeCanvasPosition(IdGenerator.NewNodeId()).Should().Be(Vector2.Zero);
    }

    [Fact]
    public void NodeLocalPosition_ReturnsStoredPosition()
    {
        var model = new GraphModelWithNodes();
        var node  = new StubNode { Position = new Vector2(77f, 88f) };
        model.Add(node);

        var view = MakeView(model);
        view.NodeLocalPosition(node.Id).Should().Be(new Vector2(77f, 88f));
    }
}
