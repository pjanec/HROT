using ImGuiNET;
using Hrot.Blueprints.Editor.Inspector;

namespace Hrot.Blueprints.Editor;

public sealed class InspectorWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly DrawerRegistry _drawerRegistry;

    public override string Title => "Inspector";

    public InspectorWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        DrawerRegistry drawerRegistry)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _drawerRegistry = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    public override void DrawUI()
    {
        var asset = _selectionStore.SelectedAsset;

        if (!ImGui.BeginTabBar("##inspector_tabs"))
            return;

        // -- Node tab --
        if (ImGui.BeginTabItem("Node"))
        {
            if (asset == null)
                ImGui.TextDisabled("No node selected.");
            else
                ImGui.TextUnformatted("Node inspector -- select a node in the graph editor.");
            ImGui.EndTabItem();
        }

        // -- Graph tab --
        if (ImGui.BeginTabItem("Graph"))
        {
            if (asset == null)
                ImGui.TextDisabled("No blueprint selected.");
            else
            {
                ImGui.TextUnformatted($"Graphs: {asset.Graphs.Count}");
                foreach (var g in asset.Graphs)
                    ImGui.BulletText(g.Name);
            }
            ImGui.EndTabItem();
        }

        // -- Asset tab --
        if (ImGui.BeginTabItem("Asset"))
        {
            if (asset == null)
            {
                ImGui.TextDisabled("No blueprint selected.");
            }
            else
            {
                ImGui.TextUnformatted($"Name:     {asset.Name}");
                ImGui.TextUnformatted($"ID:       {asset.AssetId:D}");
                ImGui.TextUnformatted($"Dispatch: {asset.Dispatch}");
                bool dirty = _dirtyTracker.IsDirty(asset.AssetId);
                ImGui.TextUnformatted($"Dirty:    {dirty}");
            }
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}

