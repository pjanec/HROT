using Fdp.Presentation.Icons;
using Hrot.Blueprints.Core;   // BlueprintJsonServices
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Catalog;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Debug;
using Hrot.Editor.AiShared.Adapters;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Numerics;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Behavioral tests for <see cref="BlueprintDocumentFactory"/> (AIE-046).
/// Uses a temp directory with a real .bp.json file to exercise the factory end-to-end.
/// All tests are headless (no ImGui, no Raylib).
/// </summary>
public sealed class BlueprintDocumentFactoryTests : IDisposable
{
    // Fake GPU handle (non-zero) accepted by IconAtlas — no real GL/DX allocation.
    private readonly IconAtlas _atlas = new(new IntPtr(1), 256f, 256f, 16f);
    private readonly string    _tempDir;

    public BlueprintDocumentFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BpFactoryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        _atlas.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private BlueprintFileAsset MakeFileAsset(BlueprintAsset? asset = null)
    {
        asset ??= BlueprintAssetBuilder.Instance("FactoryTest")
            .WithGraph("EventGraph", GraphKind.Event, g => g.Entry())
            .Build();

        var json     = BlueprintJsonServices.Serialize(asset);
        var filePath = Path.Combine(_tempDir, $"{asset.Name}.bp.json");
        File.WriteAllText(filePath, json);

        return new BlueprintFileAsset(asset.AssetId, asset.Name, filePath);
    }

    // AiEditorAdapterBundle wraps the fake atlas — no GPU calls at construction.
    private AiEditorAdapterBundle MakeBundle() => new(_atlas);


    // ── factory builds context ────────────────────────────────────────────────

    [Fact]
    public void BlueprintDocumentFactory_Build_ProducesHostServices_AndGraphView()
    {
        var fileAsset = MakeFileAsset();
        var bundle    = MakeBundle();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle);

        Assert.NotNull(ctx);
        Assert.NotNull(ctx.View);
        Assert.Equal("Blueprint", ctx.Kind, ignoreCase: true);
    }

    [Fact]
    public void BlueprintDocumentFactory_Build_GraphView_ExposesProjectedNodes()
    {
        // Asset with one EventEntry node.
        var asset = BlueprintAssetBuilder.Instance("WithNode")
            .WithGraph("EventGraph", GraphKind.Event, g => g.Entry())
            .Build();
        var fileAsset = MakeFileAsset(asset);
        var bundle    = MakeBundle();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle);

        // The model projects nodes from the asset graph.
        Assert.NotEmpty(ctx.View.Model.Nodes);
        Assert.Equal(asset.Graphs[0].Nodes.Count, ctx.View.Model.Nodes.Count);
    }

    [Fact]
    public void BlueprintDocumentFactory_Build_InjectedEditService_ContextUpdated()
    {
        var fileAsset   = MakeFileAsset();
        var bundle      = MakeBundle();
        var editService = new EditService();

        BlueprintDocumentFactory.Build(fileAsset, bundle, editService: editService);

        // After Build, the EditService has a per-document context.
        Assert.NotNull(editService.Context);
    }

    [Fact]
    public void BlueprintDocumentFactory_Build_WrongAssetType_Throws()
    {
        // Supply a non-BlueprintFileAsset IEditableAsset.
        var bundle      = MakeBundle();
        var wrongAsset  = new FakeNonBlueprintAsset();

        var ex = Assert.Throws<ArgumentException>(() =>
            BlueprintDocumentFactory.Build(wrongAsset, bundle));

        Assert.Contains("BlueprintFileAsset", ex.Message);
    }

    [Fact]
    public void BlueprintDocumentFactory_Build_CustomRenderers_IncludeWhenFiringPulse()
    {
        var fileAsset = MakeFileAsset();
        var bundle    = MakeBundle();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle);

        // At least one custom renderer should be registered (WhenFiringPulseRenderer).
        Assert.NotEmpty(ctx.View.Host.CustomCanvasRenderers);
    }

    [Fact]
    public void BlueprintDocumentFactory_Build_DirtyCallback_MarksDirty()
    {
        var asset     = BlueprintAssetBuilder.Instance("DirtyTest")
            .WithGraph("EventGraph", GraphKind.Event, g => g.Entry()).Build();
        var fileAsset = MakeFileAsset(asset);
        var bundle    = MakeBundle();
        var history   = new Hrot.Blueprints.Editor.GraphEditor.CommandHistory();
        var editService = new EditService();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle, editService: editService);

        // Simulate a property edit via the command sink.
        var node = ctx.View.Model.Nodes.First();
        var result = ctx.View.Commands.Apply(new NodeEditor.Core.Commands.GraphCommand.SetNodeProperty(
            node.Id, "Comment", "test comment"));

        Assert.True(result.Success);
        // Asset was marked dirty by the mark-dirty callback.
        Assert.True(fileAsset.IsDirty);
    }

    // ── document-manager integration ─────────────────────────────────────────

    [Fact]
    public void BlueprintDocumentFactory_OpeningBlueprintAsset_PopulatesViewState()
    {
        var asset     = BlueprintAssetBuilder.Instance("OpenTest")
            .WithGraph("EventGraph", GraphKind.Event, _ => { }).Build();
        var fileAsset = MakeFileAsset(asset);
        var bundle    = MakeBundle();

        // Simulate the DocumentOpened handler (like EditorSubsystem does it).
        AiCanvasContext? captured = null;
        var doc = new Hrot.Editor.AiShared.Documents.AiDocument(fileAsset, Hrot.Editor.AiShared.AssetKind.Blueprint);
        if (doc.ViewState == null)
        {
            doc.ViewState = BlueprintDocumentFactory.Build(doc.Asset, bundle);
        }
        captured = doc.ViewState as AiCanvasContext;

        Assert.NotNull(captured);
        Assert.Equal("Blueprint", captured!.Kind, ignoreCase: true);
    }

    // ── debug-session wiring (BATCH-E) ──────────────────────────────────────

    [Fact]
    public void Build_WithDebugSession_SetsHostDebug()
    {
        var asset = BlueprintAssetBuilder.Instance("DbgTest")
            .WithGraph("EventGraph", GraphKind.Event, g => g.Entry())
            .Build();
        var fileAsset = MakeFileAsset(asset);
        var bundle = MakeBundle();
        var debugSession = new CapturingDebugSession();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle, debugSession: debugSession);

        Assert.NotNull(ctx.View.Host.Debug);
        Assert.IsType<BlueprintDebugToNodeEditAdapter>(ctx.View.Host.Debug);
    }

    [Fact]
    public void ToggleBreakpoint_Command_Registered_And_Invokable()
    {
        var asset = BlueprintAssetBuilder.Instance("CmdTest")
            .WithGraph("EventGraph", GraphKind.Event, g => g.Entry())
            .Build();
        var fileAsset = MakeFileAsset(asset);
        var bundle = MakeBundle();
        var debugSession = new CapturingDebugSession();

        var ctx = BlueprintDocumentFactory.Build(fileAsset, bundle, debugSession: debugSession);

        // Command descriptor is registered.
        Assert.NotNull(ctx.Commands);
        var descriptor = ctx.Commands!.Get(CommandCatalog.ToggleBreakpoint);
        Assert.NotNull(descriptor);

        // Select the entry node so the command is enabled.
        var entryNode = ctx.View.Model.Nodes.FirstOrDefault();
        Assert.NotNull(entryNode);
        ctx.View.Selection.ReplaceWith(SelectionEntry.OfNode(entryNode.Id));
        Assert.True(descriptor.IsEnabled(),
            "ToggleBreakpoint should be enabled when a node is selected.");

        // Invoke the command.
        var result = ctx.Commands.Invoke(CommandCatalog.ToggleBreakpoint);
        Assert.True(result.Success, $"Command invocation failed: {result.Message}");

        // Verify the breakpoint was set on the selected node via the adapter.
        var bps = debugSession.GetBreakpoints();
        Assert.Contains(bps, bp => bp.NodeId == entryNode.Id.Value.ToString("D"));
    }

    // ── stub ──────────────────────────────────────────────────────────────────

    private sealed class FakeNonBlueprintAsset : Hrot.Editor.AiShared.IEditableAsset
    {
        public Guid     AssetId        => Guid.NewGuid();
        public string   Name           => "Fake";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.BTree; // wrong kind
        public bool     IsDirty        => false;
        public bool     IsEditorOwned  => false;
        public string   SourceFilePath => "";
        public event System.Action? Changed;
    }
}
