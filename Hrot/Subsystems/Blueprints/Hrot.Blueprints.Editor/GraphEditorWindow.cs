using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.GraphEditor;

namespace Hrot.Blueprints.Editor;

public sealed class GraphEditorWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;

    public override string Title => "Graph Editor";

    public BlueprintAsset? CurrentAsset { get; private set; }
    public SelectionState Selection { get; } = new();
    public CommandHistory Commands { get; } = new();

    public GraphEditorWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
    }

    public void OpenAsset(BlueprintAsset asset)
    {
        CurrentAsset = asset;
        Selection.ClearAll();
        Commands.Clear();
    }

    public override void DrawUI()
    {
        // ImGui canvas rendering -- requires editor runtime. Stub for Slice 1.
    }

    public override void OnDeactivated()
    {
        Selection.ClearAll();
    }
}
