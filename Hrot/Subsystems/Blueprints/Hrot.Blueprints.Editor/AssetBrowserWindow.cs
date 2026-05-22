using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor;

public sealed class AssetBrowserWindow : BlueprintEditorWindowBase
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;

    private List<AssetCatalogEntry> _catalogEntries = new();

    public override string Title => "Asset Browser";

    public AssetBrowserWindow(
        IAssetCatalog catalog,
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState)
    {
        _catalog        = catalog        ?? throw new ArgumentNullException(nameof(catalog));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
    }

    public void RefreshCatalog()
        => _catalogEntries = _catalog.EnumerateAll().ToList();

    public IReadOnlyList<AssetCatalogEntry> CatalogEntries => _catalogEntries;

    public override void DrawUI()
    {
        // ImGui rendering -- requires editor runtime. Stub for Slice 1.
    }

    public override void OnActivated()   => RefreshCatalog();
    public override void OnDeactivated() { }
}
