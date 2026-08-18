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
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// BP-12b — My Blueprint items could not be renamed, duplicated or deleted.
///
/// <para>
/// <c>MyBlueprintContextMenu</c> has always invoked <c>editor.rename-item</c>,
/// <c>editor.duplicate-item</c> and <c>editor.delete-item</c>; nothing ever registered them. The
/// visible consequence: a variable could be <b>created but never renamed or removed</b>.
/// </para>
/// </summary>
public sealed class MyBlueprintItemCommandTests
{
    private sealed record Sut(EditorCommandsImpl Commands, GraphView View, BlueprintAsset Asset);

    private static Sut MakeSut(Action<BlueprintAsset>? configure = null)
    {
        var asset = BlueprintAssetBuilder.Instance("ItemCmdAsset")
            .WithGraph("EventGraph", GraphKind.Event, _ => { })
            .Build();
        configure?.Invoke(asset);

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

        var commands = new EditorCommandsImpl();
        BlueprintDocumentFactory.RegisterMyBlueprintItemCommands(commands, asset, view);

        return new Sut(commands, view, asset);
    }

    private static EditorCommandContext Ctx(string itemId, string? newName = null)
        => new(ScreenPos: null, CanvasPos: null,
               Args: newName is null
                   ? new Dictionary<string, object?> { ["itemId"] = itemId }
                   : new Dictionary<string, object?> { ["itemId"] = itemId, ["newName"] = newName });

    private static string VarId(VariableDecl decl) => $"var:{decl.Id:D}";
    private static string EvtId(CustomEventDecl decl) => $"evt:{decl.Id:D}";

    // ── Registration (the bug itself) ─────────────────────────────────────────

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void EachItemCommand_IsRegistered(string commandId)
    {
        Assert.NotNull(MakeSut().Commands.Get(commandId));
    }

    // ── Variables ─────────────────────────────────────────────────────────────

    [Fact]
    public void AVariable_CanBeRenamed()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), "Hitpoints"));

        Assert.Equal("Hitpoints", decl.Name);
    }

    /// <summary>The gap the tracker names: created, then never removable.</summary>
    [Fact]
    public void AVariable_CanBeDeleted()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.delete-item", Ctx(VarId(decl)));

        Assert.Empty(sut.Asset.Variables);
    }

    [Fact]
    public void AVariable_CanBeDuplicated_UnderAFreeName_KeepingItsType()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(
            sut.Asset, "Health", "System.Int32", capacity: 4, initialLength: 2)!;
        decl.Category = "Combat";

        sut.Commands.Invoke("editor.duplicate-item", Ctx(VarId(decl)));

        Assert.Equal(2, sut.Asset.Variables.Count);
        var copy = sut.Asset.Variables[1];
        Assert.Equal("Health1", copy.Name);
        Assert.Equal("System.Int32", copy.Type.TypeId);
        Assert.Equal(4, copy.Type.Capacity);
        Assert.Equal(2, copy.Type.InitialLength);
        Assert.Equal("Combat", copy.Category);
        Assert.NotEqual(decl.Id, copy.Id);
    }

    /// <summary>
    /// Nodes bind to a variable by GUID, so a rename must not disturb them — that is the whole
    /// reason ids are stored rather than names.
    /// </summary>
    [Fact]
    public void RenamingAVariable_LeavesNodesBound()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        var node = new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{decl.Id:D}" };
        sut.Asset.Graphs[0].Nodes.Add(node);

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), "Hitpoints"));

        Assert.Equal($"var:{decl.Id:D}", node.VariableId);
    }

    // ── Custom events ─────────────────────────────────────────────────────────

    [Fact]
    public void ACustomEvent_CanBeRenamedDuplicatedAndDeleted()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(
            sut.Asset, "OnHit", new[] { ("Damage", "System.Single") })!;

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));
        Assert.Equal("OnStruck", decl.Name);

        sut.Commands.Invoke("editor.duplicate-item", Ctx(EvtId(decl)));
        Assert.Equal(2, sut.Asset.CustomEvents.Count);
        Assert.Equal("OnStruck1", sut.Asset.CustomEvents[1].Name);
        Assert.Equal("Damage", sut.Asset.CustomEvents[1].Parameters.Single().Name);

        sut.Commands.Invoke("editor.delete-item", Ctx(EvtId(decl)));
        Assert.Single(sut.Asset.CustomEvents);
    }

    /// <summary>
    /// The compiler emits <c>Event_{Name}</c> from the <b>graph</b>, so renaming the declaration
    /// alone would break the pairing and produce a BP1407 on the next compile.
    /// </summary>
    [Fact]
    public void RenamingACustomEvent_RenamesItsHandlerGraph()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;
        sut.Asset.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "OnHit", Kind = GraphKind.Event });

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));

        Assert.Contains(sut.Asset.Graphs, g => g.Kind == GraphKind.Event && g.Name == "OnStruck");
    }

    /// <summary>
    /// The editor writes GUIDs, which survive a rename untouched — but Stage5 accepts a bare name
    /// and hand-authored assets use one. Leaving those behind would turn a rename into a silent
    /// BP1403.
    /// </summary>
    [Fact]
    public void RenamingACustomEvent_RewritesNameKeyedCalls_AndLeavesGuidCallsAlone()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        var byName = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = "OnHit" };
        var byGuid = new CallCustomEventNode { Id = Guid.NewGuid(), EventId = decl.Id.ToString("D") };
        sut.Asset.Graphs[0].Nodes.Add(byName);
        sut.Asset.Graphs[0].Nodes.Add(byGuid);

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), "OnStruck"));

        Assert.Equal("OnStruck", byName.EventId);
        Assert.Equal(decl.Id.ToString("D"), byGuid.EventId);
    }

    /// <summary>A custom event's name is emitted verbatim as <c>Event_{Name}</c>.</summary>
    [Theory]
    [InlineData("On Struck")]
    [InlineData("1st")]
    [InlineData("class")]
    public void ACustomEvent_CannotBeRenamedToANonIdentifier(string newName)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(EvtId(decl), newName));

        Assert.Equal("OnHit", decl.Name);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankRename_ChangesNothing(string newName)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(decl), newName));

        Assert.Equal("Health", decl.Name);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    [Fact]
    public void RenamingToATakenName_ChangesNothing()
    {
        var sut = MakeSut();
        var a   = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        BlueprintDocumentFactory.CreateVariable(sut.Asset, "Armour", "System.Int32");

        sut.Commands.Invoke("editor.rename-item", Ctx(VarId(a), "armour"));

        Assert.Equal("Health", a.Name);
    }

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void AnUnknownItemId_ChangesNothing(string commandId)
    {
        var sut = MakeSut();
        BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32");

        sut.Commands.Invoke(commandId, Ctx($"var:{Guid.NewGuid():D}", "Whatever"));

        Assert.Single(sut.Asset.Variables);
        Assert.Equal("Health", sut.Asset.Variables[0].Name);
        Assert.Equal(0, sut.View.Undo.UndoCount);
    }

    /// <summary>
    /// The prefix is what distinguishes a variable id from an event id; a variable id must never
    /// resolve against the custom-event list.
    /// </summary>
    [Fact]
    public void ItemIdPrefixes_AreNotInterchangeable()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateCustomEvent(sut.Asset, "OnHit")!;

        sut.Commands.Invoke("editor.delete-item", Ctx($"var:{decl.Id:D}"));

        Assert.Single(sut.Asset.CustomEvents);
    }

    /// <summary>
    /// Deleting a declaration leaves the nodes that referenced it alone. They render as dangling
    /// and the compiler names them, which is recoverable; silently deleting a designer's wired-up
    /// nodes is not.
    /// </summary>
    [Fact]
    public void DeletingAVariable_LeavesItsNodesInPlace()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;
        sut.Asset.Graphs[0].Nodes.Add(
            new GetVariableNode { Id = Guid.NewGuid(), VariableId = $"var:{decl.Id:D}" });

        sut.Commands.Invoke("editor.delete-item", Ctx(VarId(decl)));

        Assert.Single(sut.Asset.Graphs[0].Nodes);
    }

    // ── Undo ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("editor.rename-item")]
    [InlineData("editor.delete-item")]
    [InlineData("editor.duplicate-item")]
    public void EveryItemEdit_IsUndoable(string commandId)
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke(commandId, Ctx(VarId(decl), "Hitpoints"));
        Assert.Equal(1, sut.View.Undo.UndoCount);

        sut.View.UndoLast();

        var restored = Assert.Single(sut.Asset.Variables);
        Assert.Equal("Health", restored.Name);
    }

    [Fact]
    public void RedoingAnItemEdit_ReappliesItExactlyOnce()
    {
        var sut  = MakeSut();
        var decl = BlueprintDocumentFactory.CreateVariable(sut.Asset, "Health", "System.Int32")!;

        sut.Commands.Invoke("editor.duplicate-item", Ctx(VarId(decl)));
        sut.View.UndoLast();
        sut.View.RedoLast();

        // Not three: redo restores the recorded end state rather than repeating the duplication.
        Assert.Equal(2, sut.Asset.Variables.Count);
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
