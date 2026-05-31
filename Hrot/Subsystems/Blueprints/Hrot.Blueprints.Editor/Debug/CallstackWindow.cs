using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug;

public sealed class CallstackWindow : BlueprintEditorWindowBase
{
    private readonly IBlueprintDebugSession _session;
    private readonly EditorSelectionStore _selectionStore;

    public override string Title => "Callstack";

    public CallstackWindow(IBlueprintDebugSession session, EditorSelectionStore selectionStore)
    {
        _session        = session        ?? throw new ArgumentNullException(nameof(session));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
    }

    public override void DrawUI()
    {
        // ImGui list of node history per active entity -- requires ImGui runtime.
        var history = _session.GetRecentNodeHistory();
        _ = history;
    }
}
