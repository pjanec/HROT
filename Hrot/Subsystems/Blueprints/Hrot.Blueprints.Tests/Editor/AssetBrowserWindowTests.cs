using Hrot.Blueprints.Editor;

namespace Hrot.Blueprints.Tests.Editor;

public sealed class AssetBrowserWindowTests
{
    private static AssetBrowserWindow MakeWindow(IAssetCatalog catalog)
    {
        return new AssetBrowserWindow(
            catalog,
            new EditorSelectionStore(),
            new DirtyTracker(),
            new EditorState());
    }

    // SC1
    [Fact]
    public void AssetBrowserWindow_EmptyCatalog_CatalogEntriesIsEmpty()
    {
        var window = MakeWindow(new StubCatalog());
        window.RefreshCatalog();
        Assert.Equal(0, window.CatalogEntries.Count);
    }

    // SC2
    [Fact]
    public void AssetBrowserWindow_OnActivated_RefreshesCatalog()
    {
        var entry  = new AssetCatalogEntry(Guid.NewGuid(), "test.bp");
        var window = MakeWindow(new StubCatalog(entry));
        window.OnActivated();
        Assert.Equal(1, window.CatalogEntries.Count);
        Assert.Same(entry, window.CatalogEntries[0]);
    }

    private sealed class StubCatalog : IAssetCatalog
    {
        private readonly AssetCatalogEntry[] _entries;
        public StubCatalog(params AssetCatalogEntry[] entries) => _entries = entries;
        public IEnumerable<AssetCatalogEntry> EnumerateAll() => _entries;
    }
}
