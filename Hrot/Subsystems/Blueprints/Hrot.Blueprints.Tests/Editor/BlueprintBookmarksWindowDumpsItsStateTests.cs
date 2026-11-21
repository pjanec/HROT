using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — <c>BlueprintBookmarksWindow</c> converted to the <c>PanelSnapshot</c>
/// contract via the CALLER-REGISTERS rule.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Adoption's <c>2026-08-22</c> extension.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class BlueprintBookmarksWindowDumpsItsStateTests : IDisposable
{
    private sealed class FakeAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name { get; init; } = "Test";
        public AssetKind Kind { get; init; } = AssetKind.Blueprint;
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty => false;
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
        public LinkValidationResult Validate(PinId from, PinId to) => new(LinkValidity.Valid, null, false, null);
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
        public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q) => Array.Empty<NodeCatalogEntry>();
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
        public IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers => Array.Empty<ICustomCanvasRenderer>();
        public ICustomElementContextMenuProvider? CustomElementContextMenu => null;
    }

    private static GraphView MakeGraphView()
    {
        var host = new StubHostServices();
        return new GraphView(new StubGraphModel(), host.CommandSink, host.LinkValidator, host.TypeSystem, host.NodeCatalog, host);
    }

    private static AiDocumentManager MakeDocManagerWithBlueprint(BookmarkStore? store)
    {
        var manager = new AiDocumentManager(_ => { });
        var doc = manager.Open(new FakeAsset());
        doc.ViewState = new AiCanvasContext(MakeGraphView(), "Blueprint") { Bookmarks = store };
        return manager;
    }

    public BlueprintBookmarksWindowDumpsItsStateTests()
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
        const string id = "bookmarks_rail1";
        Assert.DoesNotContain(id, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new BlueprintBookmarksWindow(new AiDocumentManager(_ => { }), idOverride: id);

        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(id, PanelSnapshot.CapturedPanels);
        Assert.NotNull(window);
    }

    [Fact]
    public void WithNoBlueprintOpen_TheDumpSaysSo()
    {
        const string id = "bookmarks_rail2";
        PanelSnapshot.CaptureEnabled = true;
        var window = new BlueprintBookmarksWindow(new AiDocumentManager(_ => { }), idOverride: id);

        window.SimulateDrawClientArea();

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.False(dump["hasBlueprintOpen"]!.GetValue<bool>());
        Assert.Equal(0, dump["bookmarkCount"]!.GetValue<int>());
    }

    [Fact]
    public void AfterOpeningABlueprintWithBookmarks_TheDumpCarriesTheLabels()
    {
        const string id = "bookmarks_rail3";
        PanelSnapshot.CaptureEnabled = true;
        var store = new BookmarkStore();
        store.SetSlot(1, new Bookmark("bm1", GraphId.NewId(), "Entry Point", Vector2.Zero, 1f, 1, DateTime.UtcNow));
        var manager = MakeDocManagerWithBlueprint(store);
        var window  = new BlueprintBookmarksWindow(manager, idOverride: id);

        var vm = window.SimulateDrawClientArea();

        Assert.Equal(id, vm.PanelId);
        Assert.Equal(BlueprintBookmarksWindow.Kind, vm.PanelKind);
        Assert.True(vm.HasBlueprintOpen);

        var dump = PanelSnapshot.TryGet(id)!.Dump();
        Assert.Equal(1, dump["bookmarkCount"]!.GetValue<int>());
        Assert.Equal("Entry Point", dump["labels"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing_ButStaysRegistered()
    {
        const string id = "bookmarks_rail4";
        var window = new BlueprintBookmarksWindow(new AiDocumentManager(_ => { }), idOverride: id);   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(id, PanelSnapshot.RegisteredPanels);
        Assert.False(vm.HasBlueprintOpen);
    }
}
