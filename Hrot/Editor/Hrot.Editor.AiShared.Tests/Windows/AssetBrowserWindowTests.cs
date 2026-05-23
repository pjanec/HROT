using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
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

public class AssetBrowserWindowTests
{
    private static AssetBrowserWindow CreateWindow() =>
        new AssetBrowserWindow(
            new EditorSelectionStore(),
            new AssetCatalog(),
            new StubRefactorService(),
            new FindResultsWindow());

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
    public void Constructor_SetsScopePerspectiveBound()
    {
        var window = CreateWindow();
        Assert.Equal(WindowScope.PerspectiveBound, window.Scope);
    }
}
