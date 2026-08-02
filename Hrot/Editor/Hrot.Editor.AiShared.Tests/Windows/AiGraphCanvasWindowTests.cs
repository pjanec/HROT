using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// Tests for <see cref="AiGraphCanvasWindow"/> (AIE-020).
/// All tests are headless — no ImGui context needed.
/// </summary>
public sealed class AiGraphCanvasWindowTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AiDocumentManager MakeDocManager() =>
        new(perspectiveSwitchCallback: _ => { });

    private sealed class RecordingRenderSeam : ICanvasRenderSeam
    {
        public GraphView? LastRenderedView;
        public int RenderCallCount;

        public void Render(GraphView view)
        {
            LastRenderedView = view;
            RenderCallCount++;
        }
    }

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name = "Test")
        {
            Kind    = kind;
            Name    = name;
            AssetId = Guid.NewGuid();
        }
        public Guid   AssetId       { get; }
        public string Name          { get; }
        public AssetKind Kind       { get; }
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty        => false;
        public bool IsEditorOwned  => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    // ── Minimal stubs for GraphView construction ───────────────────────────────

    private sealed class StubGraphModel : IGraphModel
    {
        public GraphId Id => GraphId.NewId();
        public string DisplayName => "Stub";
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
        new AiCanvasContext(MakeGraphView(), kind);

    // ── AIE-020 Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsId()
    {
        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(), new RecordingRenderSeam());
        Assert.Equal("ai_canvas_btree", win.Id);
    }

    [Fact]
    public void Constructor_SetsOwningPerspective()
    {
        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(), new RecordingRenderSeam());
        Assert.Equal("BTree", win.OwningPerspective);
    }

    [Fact]
    public void Constructor_SetsScopePerspectiveBound()
    {
        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(), new RecordingRenderSeam());
        Assert.Equal(WindowScope.PerspectiveBound, win.Scope);
    }

    [Fact]
    public void AiGraphCanvasWindow_NoActiveDoc_ShowsEmptyState()
    {
        var seam = new RecordingRenderSeam();
        var dm   = MakeDocManager();
        var win  = new AiGraphCanvasWindow("BTree", dm, seam);

        // No documents open — both must be null, no render calls.
        Assert.Null(win.ActiveDocument);
        Assert.Null(win.ActiveContext);
        Assert.Equal(0, seam.RenderCallCount);
    }

    [Fact]
    public void AiGraphCanvasWindow_RendersActiveDocumentView()
    {
        // Arrange: open a BTree doc, populate its ViewState.
        var seam = new RecordingRenderSeam();
        var dm   = MakeDocManager();
        var win  = new AiGraphCanvasWindow("BTree", dm, seam);

        var asset = new FakeAsset(AssetKind.BTree, "Tree1");
        var doc   = dm.Open(asset);

        var ctx = MakeContext("BTree");
        doc.ViewState = ctx;

        // Assert: ActiveDocument and ActiveContext are resolved.
        Assert.Same(doc,      win.ActiveDocument);
        Assert.Same(ctx,      win.ActiveContext);
        Assert.Same(ctx.View, win.ActiveContext!.View);

        // Simulate the render seam invocation (mirrors DrawClientArea logic).
        seam.Render(win.ActiveContext!.View);
        Assert.Equal(1,       seam.RenderCallCount);
        Assert.Same(ctx.View, seam.LastRenderedView);
    }

    [Fact]
    public void AiGraphCanvasWindow_OnFocus_ActivatesDocument()
    {
        // SimulateFocus → AiDocumentManager.Activate is invoked.
        string? lastPerspective = null;
        var dm  = new AiDocumentManager(p => lastPerspective = p);
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var doc = dm.Open(new FakeAsset(AssetKind.BTree));
        doc.ViewState = MakeContext();

        win.SimulateFocus(doc);

        Assert.Same(doc, dm.Active);
        Assert.Equal("BTree", lastPerspective);
    }

    [Fact]
    public void AiGraphCanvasWindow_OnFocus_IsIdempotentForSameDocument()
    {
        // SimulateFocus is a no-op on the second call for the same doc.
        int activations = 0;
        var dm  = new AiDocumentManager(_ => { });
        dm.ActiveChanged += () => activations++;
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        var doc = dm.Open(new FakeAsset(AssetKind.BTree));
        doc.ViewState = MakeContext();

        activations = 0; // reset after Open()'s activation

        win.SimulateFocus(doc);
        Assert.Equal(1, activations); // first focus activates

        win.SimulateFocus(doc);
        Assert.Equal(1, activations); // second call is no-op
    }

    [Fact]
    public void AiGraphCanvasWindow_WrongKind_HasNullActiveDocument()
    {
        // A BTree canvas must not show an HSM doc.
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("BTree", dm, new RecordingRenderSeam());

        dm.Open(new FakeAsset(AssetKind.Hsm));

        Assert.Null(win.ActiveDocument);
    }

    [Fact]
    public void AiGraphCanvasWindow_CustomIdOverride()
    {
        var win = new AiGraphCanvasWindow("BTree", MakeDocManager(),
            new RecordingRenderSeam(), idOverride: "my_btree_canvas");
        Assert.Equal("my_btree_canvas", win.Id);
    }

    [Fact]
    public void AiCanvasContext_StoresViewAndKind()
    {
        var view = MakeGraphView();
        var ctx  = new AiCanvasContext(view, "BTree");
        Assert.Same(view, ctx.View);
        Assert.Equal("BTree", ctx.Kind);
    }

    // ── MULTI-TAB: window title is the stable container name, not the active asset ──

    [Fact]
    public void UpdateTitle_IsStableContainerName_NotTheActiveAssetName()
    {
        var dm  = MakeDocManager();
        var win = new AiGraphCanvasWindow("Blueprint", dm, new RecordingRenderSeam());

        var asset = new FakeAsset(AssetKind.Blueprint, "PatrolBehavior");
        var doc   = dm.Open(asset);
        doc.ViewState = MakeContext("Blueprint");

        // Run the non-ImGui per-frame path that refreshes the title.
        win.SimulateDrawClientArea();

        // The window hosts a tab bar (one tab per open blueprint), so the title stays the container
        // name and must NOT reflect the active document — otherwise the window's close [x] reads as
        // "close this blueprint" when it actually closes the whole canvas and every tab in it.
        Assert.Equal("Blueprint Canvas", win.Title);
        Assert.DoesNotContain("PatrolBehavior", win.Title);

        // Title stays pure ASCII — the engine ImGui font renders no em-dash "—" (U+2014 → "?").
        foreach (var ch in win.Title)
            Assert.True(ch <= 0x7F, $"Title contains non-ASCII char U+{(int)ch:X4}: '{win.Title}'");
    }

    [Fact]
    public void DocumentOpened_Event_FiresOnNewDocument()
    {
        // Verify the new DocumentOpened event on AiDocumentManager.
        AiDocument? openedDoc = null;
        var dm = new AiDocumentManager(_ => { });
        dm.DocumentOpened += d => openedDoc = d;

        var asset = new FakeAsset(AssetKind.BTree);
        var doc   = dm.Open(asset);

        Assert.NotNull(openedDoc);
        Assert.Same(doc, openedDoc);
    }

    [Fact]
    public void DocumentOpened_Event_DoesNotFireOnReOpen()
    {
        // Re-opening an already-open document must NOT fire DocumentOpened again.
        int fireCount = 0;
        var dm = new AiDocumentManager(_ => { });
        dm.DocumentOpened += _ => fireCount++;

        var asset = new FakeAsset(AssetKind.BTree);
        dm.Open(asset); // first open — fires
        dm.Open(asset); // re-open (already open) — must not fire again

        Assert.Equal(1, fireCount);
    }
}
