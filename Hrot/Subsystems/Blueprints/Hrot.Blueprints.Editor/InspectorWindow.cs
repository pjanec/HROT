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
        // Three-tab layout: Node, Graph, Asset -- requires ImGui runtime.
    }
}
