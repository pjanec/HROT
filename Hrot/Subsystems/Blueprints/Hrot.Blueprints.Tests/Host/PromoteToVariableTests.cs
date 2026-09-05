using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-60 🔴 — "Promote to Variable" silently did nothing.
///
/// <para>
/// <c>GraphCommand.PromoteToVariable</c> was implemented only by <c>NodeEditor.Demo</c>'s
/// <c>FakeCommandSink</c>. <c>BlueprintCommandSink</c> has no case for it, so the command hit the
/// <c>default:</c> arm — which returns <c>new GraphCommandResult(true, null)</c>. The modal opened,
/// the name was typed, nothing happened, and the editor reported success.
/// </para>
///
/// <para>
/// ⚠ <b>Every assertion here is on the effect, never on <c>Success</c>.</b> A test asserting the
/// result would have passed against the bug — that <i>is</i> the bug.
/// </para>
/// </summary>
public sealed class PromoteToVariableTests
{
    private sealed record Sut(
        EditorCommandsImpl Commands, GraphView View, Graph Graph, BlueprintAsset Asset);

    private static Sut MakeSut()
    {
        // A Branch node gives a typed data input ("Condition"); Compare gives a typed data output.
        var asset = BlueprintAssetBuilder.Instance("PromoteAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();

        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var registry   = new NodeKindRegistry();
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(registry) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterPromoteToVariableCommand(commands, view, asset, registry);

        return new Sut(commands, view, graph, asset);
    }

    /// <summary>Adds a node to the asset graph and returns its projected model.</summary>
    private static INodeModel Place(Sut sut, Node node)
    {
        node.Id = Guid.NewGuid();
        node.EditorMetadata = new NodeMetadata { X = 300, Y = 100 };
        sut.Graph.Nodes.Add(node);
        ((BlueprintGraphModel)sut.View.Model).RebuildAndNotify();
        return sut.View.Model.FindNode(new NodeId(node.Id))!;
    }

    private static IPinModel DataPin(INodeModel node, PinDirection direction)
        => node.Pins.First(p => p.Kind == PinKind.Data && p.Direction == direction);

    private static EditorCommandContext Ctx(
        PinId? pin, string? name = "Promoted", bool isLocal = false, string? category = null)
        => new(ScreenPos: null, CanvasPos: null,
               Args: new Dictionary<string, object?>
               {
                   ["pinId"]        = pin,
                   ["name"]         = name,
                   ["isLocal"]      = isLocal,
                   ["categoryPath"] = category,
               });

    private static void Promote(Sut sut, EditorCommandContext ctx)
        => sut.Commands.Invoke(NodeEditor.Core.CommandCatalog.PromoteToVariable, ctx);

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void TheCommand_IsRegistered()
    {
        Assert.NotNull(MakeSut().Commands.Get(NodeEditor.Core.CommandCatalog.PromoteToVariable));
    }

    // ── Input pin → Get node feeding it ──────────────────────────────────────

    [Fact]
    public void PromotingAnInputPin_DeclaresTheVariable()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, "ShouldFire"));

        var decl = Assert.Single(sut.Asset.Variables);
        Assert.Equal("ShouldFire", decl.Name);
        Assert.Equal("System.Boolean", decl.Type.TypeId);
    }

    [Fact]
    public void PromotingAnInputPin_PlacesAGetNode_LinkedIntoThatPin()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        var pin  = DataPin(node, PinDirection.Input);

        Promote(sut, Ctx(pin.Id));

        var getNode = Assert.Single(sut.Graph.Nodes.OfType<GetVariableNode>());
        Assert.Equal($"var:{sut.Asset.Variables[0].Id:D}", getNode.VariableId);

        var link = Assert.Single(sut.Graph.Links);
        Assert.Equal(getNode.Id, link.FromNodeId);
        Assert.Equal(node.Id.Value, link.ToNodeId);
        Assert.Equal(pin.Id.Value, link.ToPinId);
    }

    /// <summary>The Get node reads left-to-right into the pin it feeds, so it belongs to the left.</summary>
    [Fact]
    public void TheGetNode_IsPlacedLeftOfItsConsumer()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id));

        var getNode = sut.Graph.Nodes.OfType<GetVariableNode>().Single();
        Assert.True(getNode.EditorMetadata.X < node.Position.X);
    }

    // ── Output pin → Set node fed by it ──────────────────────────────────────

    [Fact]
    public void PromotingAnOutputPin_PlacesASetNode_FedByThatPin()
    {
        var sut  = MakeSut();
        var node = Place(sut, new CompareNode { Operator = ComparisonOperator.Equal });
        var pin  = DataPin(node, PinDirection.Output);

        Promote(sut, Ctx(pin.Id, "Result"));

        var setNode = Assert.Single(sut.Graph.Nodes.OfType<SetVariableNode>());

        var link = Assert.Single(sut.Graph.Links);
        Assert.Equal(node.Id.Value, link.FromNodeId);
        Assert.Equal(pin.Id.Value,  link.FromPinId);
        Assert.Equal(setNode.Id,    link.ToNodeId);

        // …and into the Set node's *Value* pin, not its exec-in.
        var valuePin = setNode.Pins.Single(p => p.Id == link.ToPinId);
        Assert.False(valuePin.IsExec);
        Assert.Equal("Value", valuePin.Name);

        Assert.True(setNode.EditorMetadata.X > node.Position.X);
    }

    // ── Undo (BP-11: one gesture, one entry) ─────────────────────────────────

    /// <summary>
    /// Declaring, placing and linking is one gesture, so it must be one undo entry — and undoing it
    /// must leave neither a dangling node nor an orphan declaration.
    /// </summary>
    [Fact]
    public void Promotion_IsASingleUndoEntry_ThatReversesEverything()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        int nodesBefore = sut.Graph.Nodes.Count;

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id));
        Assert.Single(sut.Asset.Variables);
        Assert.Equal(nodesBefore + 1, sut.Graph.Nodes.Count);
        Assert.Single(sut.Graph.Links);

        Assert.Equal(1, sut.View.Undo.UndoCount);
        sut.View.UndoLast();

        Assert.Empty(sut.Asset.Variables);
        Assert.Equal(nodesBefore, sut.Graph.Nodes.Count);
        Assert.Empty(sut.Graph.Links);
    }

    [Fact]
    public void Redo_RestoresTheWholeGesture()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, "ShouldFire"));
        sut.View.UndoLast();
        sut.View.RedoLast();

        Assert.Equal("ShouldFire", Assert.Single(sut.Asset.Variables).Name);
        Assert.Single(sut.Graph.Nodes.OfType<GetVariableNode>());
        Assert.Single(sut.Graph.Links);
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    /// <summary>An exec pin carries no value; promoting one must place nothing.</summary>
    [Fact]
    public void PromotingAnExecPin_DoesNothing()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        var exec = node.Pins.First(p => p.Kind == PinKind.Exec);

        Promote(sut, Ctx(exec.Id));

        Assert.Empty(sut.Asset.Variables);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankName_PromotesNothing(string? name)
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, name));

        Assert.Empty(sut.Asset.Variables);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    [Fact]
    public void NoPin_PromotesNothing()
    {
        var sut = MakeSut();

        Promote(sut, Ctx(pin: null));

        Assert.Empty(sut.Asset.Variables);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    /// <summary>
    /// A name that is already taken is uniquified rather than rejected. The designer asked to
    /// promote, not to overwrite, and <c>CreateVariable</c> would refuse a duplicate outright —
    /// which, from the modal, would look exactly like the bug this item fixes.
    /// </summary>
    [Fact]
    public void ANameCollision_IsUniquified_NotRejected()
    {
        var sut  = MakeSut();
        BlueprintDocumentFactory.CreateVariable(sut.Asset, "Promoted", "System.Single");
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, "Promoted"));

        Assert.Equal(new[] { "Promoted", "Promoted1" },
            sut.Asset.Variables.Select(v => v.Name).ToArray());
    }

    [Fact]
    public void TheCategory_LandsOnTheDeclaration()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, "Promoted", category: " Combat "));

        Assert.Equal("Combat", Assert.Single(sut.Asset.Variables).Category);
    }

    /// <summary>
    /// BP-57: there is no per-graph variable scope in the data model. "Promote to Local Variable"
    /// still promotes — to a Blueprint variable — rather than doing nothing.
    /// </summary>
    [Fact]
    public void PromotingLocal_StillProduces_ABlueprintVariable()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Promote(sut, Ctx(DataPin(node, PinDirection.Input).Id, "Scratch", isLocal: true));

        Assert.Equal("Scratch", Assert.Single(sut.Asset.Variables).Name);
        Assert.Single(sut.Graph.Nodes.OfType<GetVariableNode>());
    }

    // ── Test host services ───────────────────────────────────────────────────

    private sealed class StubHostServices : IEditorHostServices
    {
        public StubHostServices(INodeCatalog catalog, ITypeSystem typeSystem,
            ILinkValidator validator, IGraphCommandSink sink)
        {
            NodeCatalog = catalog; TypeSystem = typeSystem; LinkValidator = validator; CommandSink = sink;
        }

        public INodeCatalog      NodeCatalog   { get; }
        public ITypeSystem       TypeSystem    { get; }
        public ILinkValidator    LinkValidator { get; }
        public IGraphCommandSink CommandSink   { get; }
        public IPickerRegistry   Pickers       => null!;
        public IClipboard        Clipboard     => null!;
        public IIconProvider     Icons         => null!;
        public IDiagnosticsSink? Diagnostics   => null;
        public IDebugSession?    Debug         => null;
        public IInputSource      Input         => null!;
        public IEditorTheme      Theme         => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }
}
