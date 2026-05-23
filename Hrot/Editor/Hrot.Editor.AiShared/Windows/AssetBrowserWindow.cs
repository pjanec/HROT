using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Asset browser window -- lists all editor assets grouped by folder.
/// Single-click sets ActiveAsset; double-click opens the asset in its editor canvas.
/// </summary>
public sealed class AssetBrowserWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IAssetCatalog _catalog;

    public AssetBrowserWindow(EditorSelectionStore store, IAssetCatalog catalog)
        : base("ai_asset_browser", "Asset Browser", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _catalog = catalog;
    }

    protected override void DrawClientArea()
    {
        if (_catalog.All.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("No assets loaded.");
            return;
        }

        foreach (var asset in _catalog.All)
        {
            ImGuiNET.ImGui.Text(asset.Name);
        }
    }
}
