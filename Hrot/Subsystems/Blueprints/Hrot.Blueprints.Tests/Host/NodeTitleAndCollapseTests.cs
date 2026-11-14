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
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-17 (custom node titles) and BP-18 (node body collapse).
///
/// <para>
/// Both were the same shape of gap: the canvas already honoured the feature —
/// <c>NodeRenderer</c> draws a collapsed node and a subtitle — but <c>BlueprintNodeModel</c>
/// hardcoded <c>Subtitle =&gt; null</c> and <c>IsCollapsed =&gt; false</c>, and no command could
/// change either. <c>GraphCommand.SetNodeCollapsed</c> even existed; the sink had no case, so it
/// hit the <c>default:</c> arm that returns success and does nothing.
/// </para>
/// </summary>
public sealed class NodeTitleAndCollapseTests
{
    private sealed record Sut(GraphView View, Graph Graph, BlueprintAsset Asset);

    private static Sut MakeSut()
    {
        var asset = BlueprintAssetBuilder.Instance("TitleCollapseAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();

        var graph      = asset.Graphs[0];
        var typeSystem = new BlueprintTypeSystem(NullPinDefaultValueEditorRegistry.Instance);
        var model      = new BlueprintGraphModel(asset, graph);
        var catalog    = new BlueprintNodeCatalog(new NodeKindRegistry()) { Asset = asset };
        var validator  = new BlueprintLinkValidator(model, typeSystem);
        var history    = new CommandHistory();
        var editSvc    = new EditService { Context = new EditServiceContext(history, _ => { }) };

        var sink = new BlueprintCommandSink(
            asset, graph, model, catalog, validator, history, editSvc, markDirty: _ => { });

        var host = new StubHostServices(catalog, typeSystem, validator, sink);
        var view = new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);

        return new Sut(view, graph, asset);
    }

    private static INodeModel Place(Sut sut, Node node)
    {
        node.Id = Guid.NewGuid();
        node.EditorMetadata = new NodeMetadata { X = 10, Y = 20 };
        sut.Graph.Nodes.Add(node);
        ((BlueprintGraphModel)sut.View.Model).RebuildAndNotify();
        return sut.View.Model.FindNode(new NodeId(node.Id))!;
    }

    private static void Rename(Sut sut, NodeId id, string? title, string? previous = null)
        => sut.View.Execute(
            new GraphCommand.SetNodeProperty(id, "Title", title),
            new GraphCommand.SetNodeProperty(id, "Title", previous),
            "Rename Node");

    // ── BP-17: custom titles ─────────────────────────────────────────────────

    [Fact]
    public void ACustomTitle_ReplacesTheGeneratedOne()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Assert.Equal("Branch", node.Title);

        Rename(sut, node.Id, "Is Alive?");

        Assert.Equal("Is Alive?", sut.View.Model.FindNode(node.Id)!.Title);
    }

    /// <summary>
    /// A renamed node must not lose the only indication of what it actually is, so the generated
    /// title becomes the subtitle — which <c>NodeRenderer</c> already draws.
    /// </summary>
    [Fact]
    public void TheGeneratedTitle_BecomesTheSubtitle()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Assert.Null(node.Subtitle);

        Rename(sut, node.Id, "Is Alive?");

        Assert.Equal("Branch", sut.View.Model.FindNode(node.Id)!.Subtitle);
    }

    /// <summary>
    /// Blank clears the override rather than storing an empty header — a node can always be put
    /// back to the title its configuration implies, without needing undo.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankTitle_RestoresTheGeneratedOne(string? blank)
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Rename(sut, node.Id, "Is Alive?");

        Rename(sut, node.Id, blank, previous: "Is Alive?");

        var after = sut.View.Model.FindNode(node.Id)!;
        Assert.Equal("Branch", after.Title);
        Assert.Null(after.Subtitle);
    }

    [Fact]
    public void ATitleIsTrimmed_AndPersistedOnTheAssetNode()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Rename(sut, node.Id, "  Is Alive?  ");

        Assert.Equal("Is Alive?", sut.Graph.Nodes[0].EditorMetadata.CustomTitle);
    }

    /// <summary>
    /// The override is presentational: a node whose configuration changes still re-derives its
    /// generated title underneath, which is why the two are kept separate rather than baked.
    /// </summary>
    [Fact]
    public void TheGeneratedTitle_StillTracksConfiguration_UnderACustomOne()
    {
        var sut     = MakeSut();
        var compare = new CompareNode { Operator = ComparisonOperator.Equal };
        var node    = Place(sut, compare);
        Rename(sut, node.Id, "Threshold check");

        compare.Operator = ComparisonOperator.GreaterThan;
        ((BlueprintGraphModel)sut.View.Model).RebuildAndNotify();

        var after = sut.View.Model.FindNode(node.Id)!;
        Assert.Equal("Threshold check", after.Title);
        Assert.Equal("Compare >", after.Subtitle);
    }

    [Fact]
    public void RenamingIsUndoable()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        Rename(sut, node.Id, "Is Alive?");
        Assert.Equal(1, sut.View.Undo.UndoCount);

        sut.View.UndoLast();

        Assert.Equal("Branch", sut.View.Model.FindNode(node.Id)!.Title);
    }

    // ── BP-18: collapse ──────────────────────────────────────────────────────

    /// <summary>
    /// The bug in one assertion: <c>SetNodeCollapsed</c> existed as a command, the sink had no case
    /// for it, and the <c>default:</c> arm returned success. Assert the effect, never the result.
    /// </summary>
    [Fact]
    public void SetNodeCollapsed_ActuallyCollapses()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());
        Assert.False(node.IsCollapsed);

        sut.View.Execute(
            new GraphCommand.SetNodeCollapsed(node.Id, true),
            new GraphCommand.SetNodeCollapsed(node.Id, false),
            "Collapse Node");

        Assert.True(sut.View.Model.FindNode(node.Id)!.IsCollapsed);
        Assert.True(sut.Graph.Nodes[0].EditorMetadata.Collapsed);
    }

    [Fact]
    public void CollapsingIsUndoable()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        sut.View.Execute(
            new GraphCommand.SetNodeCollapsed(node.Id, true),
            new GraphCommand.SetNodeCollapsed(node.Id, false),
            "Collapse Node");
        sut.View.UndoLast();

        Assert.False(sut.View.Model.FindNode(node.Id)!.IsCollapsed);
    }

    [Fact]
    public void CollapsingAnUnknownNode_Fails_RatherThanReportingSuccess()
    {
        var sut = MakeSut();

        var result = sut.View.Host.CommandSink.Apply(
            new GraphCommand.SetNodeCollapsed(new NodeId(Guid.NewGuid()), true));

        Assert.False(result.Success);
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Both fields are omitted from JSON at their defaults, so every existing asset round-trips
    /// byte-identically — and a node that carries them survives save/load.
    /// </summary>
    [Fact]
    public void BothFields_RoundTripThroughJson_AndAreOmittedWhenDefault()
    {
        var sut  = MakeSut();
        var node = Place(sut, new BranchNode());

        var withoutJson = Hrot.Blueprints.Core.BlueprintJsonServices.Serialize(sut.Asset);
        Assert.DoesNotContain("CustomTitle", withoutJson);
        Assert.DoesNotContain("Collapsed", withoutJson);

        Rename(sut, node.Id, "Is Alive?");
        sut.View.Execute(
            new GraphCommand.SetNodeCollapsed(node.Id, true),
            new GraphCommand.SetNodeCollapsed(node.Id, false),
            "Collapse Node");

        var json     = Hrot.Blueprints.Core.BlueprintJsonServices.Serialize(sut.Asset);
        var reloaded = Hrot.Blueprints.Core.BlueprintJsonServices.Deserialize(json)!;
        var meta     = reloaded.Graphs[0].Nodes[0].EditorMetadata;

        Assert.Equal("Is Alive?", meta.CustomTitle);
        Assert.True(meta.Collapsed);
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
