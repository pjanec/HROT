using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Refactor;
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
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;
    private readonly ILiveSessionProvider _liveProvider;

    private IEditableAsset? _pendingRenameAsset;
    private readonly byte[] _browserRenameBuf = new byte[512];
    private bool _openBrowserRenameModal;

    public AssetBrowserWindow(
        EditorSelectionStore store,
        IAssetCatalog catalog,
        IRefactorService refactorService,
        FindResultsWindow findResults,
        ILiveSessionProvider liveProvider)
        : base("ai_asset_browser", "Asset Browser", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _catalog = catalog;
        _refactorService = refactorService;
        _findResults = findResults;
        _liveProvider = liveProvider;
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
            var liveCount = _liveProvider.GetActiveEntityCount(asset.AssetId);
            var label = liveCount > 0
                ? $"{asset.Name}  [{liveCount} live]"
                : asset.Name;
            ImGuiNET.ImGui.Selectable(label);
            var popupId = $"##bctx_{asset.AssetId}";
            if (ImGuiNET.ImGui.BeginPopupContextItem(popupId))
            {
                if (ImGuiNET.ImGui.MenuItem("Find References"))
                {
                    var refs = _refactorService.FindReferences(asset.Name);
                    _findResults.ShowReferences(asset.Name, refs);
                }
                if (ImGuiNET.ImGui.MenuItem("Rename..."))
                {
                    _pendingRenameAsset = asset;
                    _openBrowserRenameModal = true;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                }
                if (ImGuiNET.ImGui.MenuItem("Delete (preview)..."))
                {
                    var deletePreview = _refactorService.PreviewDelete(
                        asset.AssetId, new DeleteOptions());
                    _findResults.ShowReferences(
                        $"Delete preview: {asset.Name}",
                        deletePreview.DanglingReferences);
                }
                ImGuiNET.ImGui.EndPopup();
            }
        }

        if (_openBrowserRenameModal)
        {
            ImGuiNET.ImGui.OpenPopup("Rename##browser");
            _openBrowserRenameModal = false;
        }

        if (_pendingRenameAsset != null)
        {
            var renameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename##browser", ref renameOpen,
                ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiNET.ImGui.Text($"Rename: {_pendingRenameAsset.Name}");
                ImGuiNET.ImGui.Text("New name:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.InputText("##rname_browser", _browserRenameBuf,
                    (uint)_browserRenameBuf.Length);
                if (ImGuiNET.ImGui.Button("OK"))
                {
                    var newKey = System.Text.Encoding.UTF8.GetString(_browserRenameBuf)
                        .TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(newKey))
                    {
                        var preview = _refactorService.PreviewRename(
                            _pendingRenameAsset.Name, newKey, new RefactorOptions());
                        _findResults.ShowRenamePreview(preview);
                    }
                    _pendingRenameAsset = null;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                {
                    _pendingRenameAsset = null;
                    Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.EndPopup();
            }
            if (!renameOpen)
            {
                _pendingRenameAsset = null;
                Array.Clear(_browserRenameBuf, 0, _browserRenameBuf.Length);
            }
        }
    }
}
