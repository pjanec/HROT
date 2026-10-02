using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>AiGraphCanvasWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠ Reuses the minimal <c>GraphView</c>-construction stubs from
/// <see cref="AiGraphCanvasWindowTests"/> (own copies — <c>file</c>-scoped stubs there cannot be
/// shared across test classes without a new shared-test-support file, which would be a larger change
/// than this sweep's scope).</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class AiGraphCanvasWindowDumpsItsStateTests : IDisposable
{
    private static AiDocumentManager MakeDocManager() => new(perspectiveSwitchCallback: _ => { });

    private sealed class RecordingRenderSeam : ICanvasRenderSeam
    {
        public void Render(GraphView view) { }
    }

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name = "Test", bool isDirty = false)
        { Kind = kind; Name = name; AssetId = Guid.NewGuid(); IsDirty = isDirty; }
        public Guid   AssetId { get; }
        public string Name    { get; }
        public AssetKind Kind { get; }
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty { get; }
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private sealed class StubGraphModel : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "Main";
        public GraphKindDescriptor Kind => new("stub", "Stub", false, false);
        public IReadOnlyCollection<INodeModel>    Nodes    => Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel>    Links    => Array.Empty<ILinkModel>();
        public IReadOnlyCollection<ICommentModel> Comments => Array.Empty<ICommentModel>();
        public INodeModel?  FindNode(NodeId id) => null;
        public IPinModel?   FindPin(PinId id)   => null;
        public ILinkModel?  FindLink(LinkId id) => null;
#pragma warning disable CS0067
        public event Action<GraphChangeNotification>? Changed;
#pragma warning restore CS0067
    }

    private sealed class StubCommandSink : IGraphCommandSink
    {
        public GraphCommandResult Apply(GraphCommand command) => new(true, null);
    }

    private sealed class StubValidator : ILinkValidator
    {
        public LinkValidationResult Validate(PinId from, PinId to) =>
            new(LinkValidity.Valid, null, false, null);
    }

    private sealed class StubTypeSystem : ITypeSystem
    {
        public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
        { info = new TypeDisplayInfo("?", null, null); return false; }
        public Vector4 GetPinColor(TypeKey key) => Vector4.One;
        public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Circle;
        public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;
        public bool AreCompatible(TypeKey from, TypeKey to) => false;
        public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
    }

    private sealed class StubNodeCatalog : INodeCatalog
    {
        public IReadOnlyList<NodeCatalogEntry> All => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCategoryDescriptor> Categories => Array.Empty<NodeCategoryDescriptor>();
        public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q) => Array.Empty<NodeCatalogEntry>();
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) =>
            Array.Empty<NodeCatalogEntry>();
    }

    private sealed class StubHostServices : IEditorHostServices
    {
        private readonly StubCommandSink _cmd = new();
        private readonly StubValidator   _val = new();
        private readonly StubTypeSystem  _ts  = new();
        private readonly StubNodeCatalog _cat = new();

        public INodeCatalog     NodeCatalog  => _cat;
        public ITypeSystem      TypeSystem   => _ts;
        public ILinkValidator   LinkValidator => _val;
        public IGraphCommandSink CommandSink => _cmd;
        public IPickerRegistry  Pickers     => null!;
        public IClipboard       Clipboard   => null!;
        public IIconProvider    Icons       => null!;
        public IDiagnosticsSink? Diagnostics => null;
        public IDebugSession?   Debug       => null;
        public IInputSource     Input       => null!;
        public IEditorTheme     Theme       => null!;
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers =>
            Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    private static GraphView MakeGraphView()
    {
        var model = new StubGraphModel();
        var host  = new StubHostServices();
        return new GraphView(model, host.CommandSink, host.LinkValidator,
            host.TypeSystem, host.NodeCatalog, host);
    }

    private static AiCanvasContext MakeContext(string kind = "BTree") =>
        new(MakeGraphView(), kind);

    public AiGraphCanvasWindowDumpsItsStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("ai_canvas_btree", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(), new RecordingRenderSeam());

        Assert.Contains("ai_canvas_btree", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("ai_canvas_btree", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("ai_canvas_btree"));
        Assert.NotNull(win);
    }

    [Fact]
    public void WithNoActiveDocument_TheDumpSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(), new RecordingRenderSeam());

        win.SimulateBuildAndPublish();

        var dump = PanelSnapshot.TryGet("ai_canvas_btree")!.Dump();
        Assert.False(dump["hasActiveDocument"]!.GetValue<bool>());
        Assert.Null(dump["activeDocumentName"]);
        Assert.Equal(0, dump["openDocumentCount"]!.GetValue<int>());
    }

    [Fact]
    public void AfterABuild_TheDumpCarriesTheActiveDocumentAndBreadcrumb()
    {
        PanelSnapshot.CaptureEnabled = true;
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var asset = new FakeAsset(AssetKind.BTree, "Tree1", isDirty: true);
        var doc   = dm.Open(asset);
        doc.ViewState = MakeContext("BTree");

        var vm = win.SimulateBuildAndPublish();

        Assert.Equal("ai_canvas_btree", vm.PanelId);
        Assert.Equal(AiGraphCanvasWindow.Kind, vm.PanelKind);

        var dump = PanelSnapshot.TryGet("ai_canvas_btree")!.Dump();
        Assert.True(dump["hasActiveDocument"]!.GetValue<bool>());
        Assert.Equal("Tree1", dump["activeDocumentName"]!.GetValue<string>());
        Assert.True(dump["activeDocumentDirty"]!.GetValue<bool>());
        Assert.Equal(1, dump["openDocumentCount"]!.GetValue<int>());
        Assert.Equal("Tree1  >  Main (Stub)", dump["breadcrumb"]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());   // CaptureEnabled stays false

        var asset = new FakeAsset(AssetKind.BTree, "Tree1");
        var doc   = dm.Open(asset);
        doc.ViewState = MakeContext("BTree");

        var vm = win.SimulateBuildAndPublish();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("ai_canvas_btree", PanelSnapshot.RegisteredPanels);
        Assert.Equal("Tree1", vm.ActiveDocumentName);
    }
}
