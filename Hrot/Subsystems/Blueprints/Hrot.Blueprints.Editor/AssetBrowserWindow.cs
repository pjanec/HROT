using System.IO;
using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using ImGuiNET;

namespace Hrot.Blueprints.Editor;

public sealed class AssetBrowserWindow : BlueprintEditorWindowBase
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;

    private List<AssetCatalogEntry> _catalogEntries = new();
    private string _filterText = string.Empty;

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
        if (ImGui.Button("Refresh")) RefreshCatalog();
        ImGui.SameLine();
        ImGui.InputText("Filter", ref _filterText, 128);

        if (ImGui.BeginTable("AssetsTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name",     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dispatch", ImGuiTableColumnFlags.WidthFixed, 100f);
            ImGui.TableSetupColumn("Hostings", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("Status",   ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableHeadersRow();

            foreach (var entry in _catalogEntries)
            {
                if (!string.IsNullOrEmpty(_filterText) &&
                    !entry.Path.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                bool isDirty  = _dirtyTracker.IsDirty(entry.AssetId);
                string prefix = isDirty ? "* " : "";
                bool isSelected = _selectionStore.SelectedAsset?.AssetId == entry.AssetId;

                // Double-click opens the asset in the graph editor.
                if (ImGui.Selectable(
                        $"{prefix}{Path.GetFileNameWithoutExtension(entry.Path)}##{entry.AssetId}",
                        isSelected,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                {
                    if (ImGui.IsMouseDoubleClicked(0))
                    {
                        var asset = _editorState.GetInMemoryAsset(entry.AssetId);
                        if (asset != null) _selectionStore.SelectAsset(asset);
                    }
                }

                ImGui.TableNextColumn();
                ImGui.TextDisabled("---");
                ImGui.TableNextColumn();
                ImGui.TextDisabled("---");
                ImGui.TableNextColumn();
                if (isDirty) ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Modified");
            }

            ImGui.EndTable();
        }
    }

    public override void OnActivated()   => RefreshCatalog();
    public override void OnDeactivated() { }
}
