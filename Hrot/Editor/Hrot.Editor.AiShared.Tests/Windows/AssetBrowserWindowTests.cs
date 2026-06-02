using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.References;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Tests.Windows;

file sealed class StubRefactorService : IRefactorService
{
    public IReadOnlyList<AssetReferenceInfo> FindReferences(string targetKey) => Array.Empty<AssetReferenceInfo>();
    public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid hostAssetId) => Array.Empty<AssetReferenceInfo>();
    public RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) =>
        new RefactorPreview(fromKey, toKey, Array.Empty<RefactorFileEdit>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyRename(RefactorPreview preview) =>
        new RefactorResult(true, Array.Empty<string>(), null);
    public DeletePreview PreviewDelete(Guid assetId, DeleteOptions options) =>
        new DeletePreview(assetId, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
    public RefactorResult ApplyDelete(DeletePreview preview) =>
        new RefactorResult(true, Array.Empty<string>(), null);
    public Task<RefactorPreview> PreviewRenameAsync(string fromKey, string toKey, RefactorOptions options, CancellationToken ct = default) =>
        Task.FromResult(PreviewRename(fromKey, toKey, options));
    public Task<RefactorResult> ApplyRenameAsync(RefactorPreview preview, CancellationToken ct = default) =>
        Task.FromResult(ApplyRename(preview));
}

file sealed class StubLiveSessionProvider : ILiveSessionProvider
{
    public int GetActiveEntityCount(Guid assetId) => 0;
}

file sealed class FakeAsset : IEditableAsset
{
    public FakeAsset(AssetKind kind = AssetKind.BTree, string name = "TestAsset")
    {
        AssetId = Guid.NewGuid();
        Kind    = kind;
        Name    = name;
    }
    public Guid AssetId { get; }
    public string Name { get; }
    public AssetKind Kind { get; }
    public string SourceFilePath => "/fake.cs";
    public bool IsDirty => false;
    public bool IsEditorOwned => true;
#pragma warning disable 67
    public event Action? Changed;
#pragma warning restore 67
}

public class AssetBrowserWindowTests
{
    private static AssetBrowserWindow CreateWindow() =>
        new AssetBrowserWindow(
            new EditorSelectionStore(),
            new AssetCatalog(),
            new StubRefactorService(),
            new FindResultsWindow(),
            new StubLiveSessionProvider());

    [Fact]
    public void Constructor_SetsId()
    {
        var window = CreateWindow();
        Assert.Equal("ai_asset_browser", window.Id);
    }

    [Fact]
    public void Constructor_SetsTitle()
    {
        var window = CreateWindow();
        Assert.Equal("Asset Browser", window.Title);
    }

    [Fact]
    public void Constructor_SetsOwningPerspective()
    {
        var window = CreateWindow();
        Assert.Equal("Authoring", window.OwningPerspective);
    }

    [Fact]
    public void Constructor_SetsScopeGlobal()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.Global, window.Scope);
    }

    // ── AIE-013: Global scope ─────────────────────────────────────────────────

    [Fact]
    public void AssetBrowser_IsGlobalScope()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.Global, window.Scope);
    }

    // ── AIE-013: Open-docs section ────────────────────────────────────────────

    private static AiDocumentManager MakeMgr(out List<string> switchLog)
    {
        var log = new List<string>();
        switchLog = log;
        return new AiDocumentManager(perspectiveSwitchCallback: k => log.Add(k));
    }

    private static IEditableAsset MakeBTreeAsset(string name = "MyTree") =>
        new FakeAsset(AssetKind.BTree, name);

    private static IEditableAsset MakeHsmAsset(string name = "MyHsm") =>
        new FakeAsset(AssetKind.Hsm, name);

    [Fact]
    public void AssetBrowser_OpenSection_ListsOpenDocs_WithActiveMarker_AndDirty()
    {
        var mgr = MakeMgr(out _);
        var assetA = MakeBTreeAsset("Tree1");
        var assetB = MakeHsmAsset("Fsm1");

        var docA = mgr.Open(assetA);
        var docB = mgr.Open(assetB); // B is now active
        docA.MarkDirty();

        var vm = AssetBrowserWindow.BuildOpenDocsViewModel(mgr);

        Assert.Equal(2, vm.Rows.Count);

        var rowA = vm.Rows.First(r => r.Document == docA);
        Assert.Equal("Tree1",   rowA.DisplayName);
        Assert.Equal("BTree",   rowA.KindTag);
        Assert.True(rowA.IsDirty);
        Assert.False(rowA.IsActive); // B is active, not A

        var rowB = vm.Rows.First(r => r.Document == docB);
        Assert.Equal("Fsm1",    rowB.DisplayName);
        Assert.Equal("Hsm",     rowB.KindTag);
        Assert.False(rowB.IsDirty);
        Assert.True(rowB.IsActive);
    }

    [Fact]
    public void AssetBrowser_OpenSection_EmptyWhenNoDocuments()
    {
        var mgr = MakeMgr(out _);
        var vm  = AssetBrowserWindow.BuildOpenDocsViewModel(mgr);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void AssetBrowser_OpenSection_NullManager_ReturnsEmptyViewModel()
    {
        var vm = AssetBrowserWindow.BuildOpenDocsViewModel(null);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void AssetBrowser_ClickOpenRow_CallsActivate()
    {
        var mgr  = MakeMgr(out var log);
        var docA = mgr.Open(MakeBTreeAsset("A"));
        var docB = mgr.Open(MakeHsmAsset("B"));

        // log: ["BTree", "Hsm"]
        log.Clear();

        // Click on row A (currently inactive)
        AssetBrowserWindow.HandleActivateRow(mgr, docA);

        // Manager must have activated docA → perspective switch to "BTree"
        Assert.Same(docA, mgr.Active);
        Assert.Equal(new[] { "BTree" }, log);
    }

    [Fact]
    public void AssetBrowser_CloseButton_CallsClose()
    {
        var mgr  = MakeMgr(out _);
        var docA = mgr.Open(MakeBTreeAsset("A"));
        var docB = mgr.Open(MakeHsmAsset("B"));

        AssetBrowserWindow.HandleCloseRow(mgr, docA);

        Assert.DoesNotContain(docA, mgr.OpenDocuments);
        Assert.Single(mgr.OpenDocuments);
        Assert.Same(docB, mgr.Active);
    }

    [Fact]
    public void AssetBrowser_DoubleClickCatalog_CallsOpen()
    {
        var mgr    = MakeMgr(out _);
        var asset  = MakeBTreeAsset("NewAsset");

        // Catalog open through the static helper
        AssetBrowserWindow.HandleCatalogOpen(mgr, asset);

        Assert.Single(mgr.OpenDocuments);
        Assert.Equal("NewAsset", mgr.Active!.Asset.Name);
    }

    [Fact]
    public void AssetBrowser_DoubleClickCatalog_NoDocManager_NoThrow()
    {
        var asset = MakeBTreeAsset("X");
        var ex    = Record.Exception(() => AssetBrowserWindow.HandleCatalogOpen(null, asset));
        Assert.Null(ex);
    }
}
