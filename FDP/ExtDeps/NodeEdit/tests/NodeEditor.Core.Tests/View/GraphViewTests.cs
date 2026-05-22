using System.Collections.Generic;
using System.Numerics;
using FluentAssertions;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

public class GraphViewTests
{
    // ── Stubs ────────────────────────────────────────────────────────────────

    private sealed class SpySink : IGraphCommandSink
    {
        public List<GraphCommand> Log { get; } = new();

        public GraphCommandResult Apply(GraphCommand command)
        {
            Log.Add(command);
            return new GraphCommandResult(true, null);
        }
    }

    private sealed class StubModel : IGraphModel
    {
        public GraphId Id => GraphId.Empty;
        public string DisplayName => "stub";
        public GraphKindDescriptor Kind => new("stub", "Stub", false, false);
        public IReadOnlyCollection<INodeModel> Nodes => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel> Links => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel? FindNode(NodeId id) => null;
        public IPinModel? FindPin(PinId id) => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event Action<GraphChangeNotification>? Changed { add { } remove { } }
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
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHost : IEditorHostServices
    {
        private readonly IGraphCommandSink _sink;
        public StubHost(IGraphCommandSink sink) { _sink = sink; }

        public INodeCatalog NodeCatalog => new StubCatalog();
        public ITypeSystem TypeSystem => new StubTypeSystem();
        public ILinkValidator LinkValidator => new StubValidator();
        public IGraphCommandSink CommandSink => _sink;
        public IPickerRegistry Pickers => throw new NotImplementedException();
        public IClipboard Clipboard => throw new NotImplementedException();
        public IIconProvider Icons => throw new NotImplementedException();
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession? Debug => null;
        public IInputSource Input => throw new NotImplementedException();
        public IEditorTheme Theme => throw new NotImplementedException();
    }

    private static GraphView MakeView(SpySink sink) =>
        new(new StubModel(), sink, new StubValidator(), new StubTypeSystem(), new StubCatalog(), new StubHost(sink));

    private static GraphCommand MakeNoOp() =>
        new GraphCommand.MoveNodes(Array.Empty<NodeMove>());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Construct_DoesNotThrow()
    {
        var sink = new SpySink();
        var act = () => MakeView(sink);
        act.Should().NotThrow();
    }

    [Fact]
    public void Execute_CallsSinkApply()
    {
        var sink = new SpySink();
        var view = MakeView(sink);

        var forward = MakeNoOp();
        var inverse = MakeNoOp();

        view.Execute(forward, inverse, "test");

        sink.Log.Should().ContainSingle();
        sink.Log[0].Should().Be(forward);
    }

    [Fact]
    public void UndoLast_CallsSinkWithInverse()
    {
        var sink = new SpySink();
        var view = MakeView(sink);

        var forward = new GraphCommand.RemoveNodes(new[] { NodeId.NewId() });
        var inverse = MakeNoOp();

        view.Execute(forward, inverse, "test");
        sink.Log.Count.Should().Be(1);

        view.UndoLast();
        sink.Log.Count.Should().Be(2);
        sink.Log[1].Should().Be(inverse);
    }
}
