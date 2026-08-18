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
/// BP-12a — My Blueprint → right-click a variable → "Get"/"Set".
///
/// <para>
/// <c>MyBlueprintContextMenu.DrawVariableMenu</c> has always invoked
/// <c>editor.create-variable-get</c> / <c>-set</c>, but nothing registered them. The panel's
/// <c>Invoke</c> discarded the failure result (that was BP-12e), so the most-used motion in
/// Unreal-style authoring silently did nothing. The drag-to-canvas path already worked —
/// <c>CanvasRenderer.PlaceVariableNode</c> handles the drop — so this closes the gap for the menu,
/// which is the only route when the panel is docked away from the canvas.
/// </para>
/// </summary>
public sealed class VariableGetSetCommandTests
{
    private static (BlueprintAsset asset, Graph graph) MakeAsset()
    {
        var asset = BlueprintAssetBuilder.Instance("VarCmdAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        asset.Variables.Add(new VariableDecl { Name = "Health", Type = new BlueprintTypeRef { TypeId = "System.Int32" } });
        return (asset, asset.Graphs[0]);
    }

    private static (EditorCommandsImpl commands, GraphView view, Graph graph, BlueprintAsset asset) MakeSut()
    {
        var (asset, graph) = MakeAsset();

        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry());
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterVariableGetSetCommands(commands, view, asset);

        return (commands, view, graph, asset);
    }

    private static EditorCommandContext Ctx(string? itemId)
        => new(ScreenPos: null, CanvasPos: null,
               Args: itemId is null ? null : new Dictionary<string, object?> { ["itemId"] = itemId });

    // ── Registration ─────────────────────────────────────────────────────────

    /// <summary>
    /// The bug in one assertion: the commands the context menu invokes must exist. Before this they
    /// did not, and BP-12e's fix meant the failure was at least logged rather than swallowed.
    /// </summary>
    [Theory]
    [InlineData("editor.create-variable-get")]
    [InlineData("editor.create-variable-set")]
    public void Command_IsRegistered(string commandId)
    {
        var (commands, _, _, _) = MakeSut();
        Assert.NotNull(commands.Get(commandId));
    }

    // ── Behaviour ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateVariableGet_PlacesAGetVariableNode_BoundToTheVariable()
    {
        var (commands, _, graph, _) = MakeSut();
        int before = graph.Nodes.Count;

        var result = commands.Invoke("editor.create-variable-get", Ctx("Health"));

        Assert.True(result.Success);
        Assert.Equal(before + 1, graph.Nodes.Count);
        var node = Assert.IsType<GetVariableNode>(graph.Nodes[^1]);
        Assert.Equal("Health", node.VariableId);
    }

    [Fact]
    public void CreateVariableSet_PlacesASetVariableNode_BoundToTheVariable()
    {
        var (commands, _, graph, _) = MakeSut();

        commands.Invoke("editor.create-variable-set", Ctx("Health"));

        var node = Assert.IsType<SetVariableNode>(graph.Nodes[^1]);
        Assert.Equal("Health", node.VariableId);
    }

    /// <summary>
    /// Placement goes through <c>view.Execute</c>, so it lands on the same stack as every other
    /// structural edit (BP-11) — Ctrl+Z removes the node the menu just added.
    /// </summary>
    [Fact]
    public void PlacingAVariableNode_IsUndoable()
    {
        var (commands, view, graph, _) = MakeSut();
        int before = graph.Nodes.Count;

        commands.Invoke("editor.create-variable-get", Ctx("Health"));
        Assert.Equal(before + 1, graph.Nodes.Count);

        Assert.Equal(1, view.Undo.UndoCount);
        Assert.True(view.Undo.Undo());
        Assert.Equal(before, graph.Nodes.Count);
    }

    /// <summary>
    /// Invoked with no selection (an empty or absent itemId), the command must do nothing rather
    /// than place a node bound to nothing — which would compile to a dangling variable reference.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithoutAVariable_PlacesNothing(string? itemId)
    {
        var (commands, _, graph, _) = MakeSut();
        int before = graph.Nodes.Count;

        var result = commands.Invoke("editor.create-variable-get", Ctx(itemId));

        Assert.True(result.Success);   // the command ran; it simply had nothing to do
        Assert.Equal(before, graph.Nodes.Count);
    }

    /// <summary>
    /// A stale id (variable since renamed) still places a node carrying that id, so the reference is
    /// visible and fixable on the canvas rather than silently dropped.
    /// </summary>
    [Fact]
    public void UnknownVariableId_StillPlacesANodeCarryingIt()
    {
        var (commands, _, graph, _) = MakeSut();

        commands.Invoke("editor.create-variable-get", Ctx("Renamed"));

        var node = Assert.IsType<GetVariableNode>(graph.Nodes[^1]);
        Assert.Equal("Renamed", node.VariableId);
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
