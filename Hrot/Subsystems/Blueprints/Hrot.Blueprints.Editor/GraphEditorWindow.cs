using ImGuiNET;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.Reload;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Blueprints.Editor;

public sealed class GraphEditorWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;
    private readonly QuickReloadService _quickReloadService;
    private readonly FullRebuildService _fullRebuildService;

    public override string Title => "Graph Editor";

    public BlueprintAsset? CurrentAsset { get; private set; }
    public SelectionState Selection { get; } = new();
    public CommandHistory Commands { get; } = new();

    private IDataBreakpointManager? _bpManager;

    public void SetBreakpointManager(IDataBreakpointManager? manager) => _bpManager = manager;

    public GraphEditorWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState,
        QuickReloadService quickReloadService,
        FullRebuildService fullRebuildService)
    {
        _selectionStore     = selectionStore     ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker       = dirtyTracker       ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState        = editorState        ?? throw new ArgumentNullException(nameof(editorState));
        _quickReloadService = quickReloadService ?? throw new ArgumentNullException(nameof(quickReloadService));
        _fullRebuildService = fullRebuildService ?? throw new ArgumentNullException(nameof(fullRebuildService));

        _selectionStore.OnSelectionChanged += OnSelectionChanged;
    }

    public void OpenAsset(BlueprintAsset asset)
    {
        CurrentAsset = asset;
        Selection.ClearAll();
        Commands.Clear();
    }

    public override void DrawUI()
    {
        if (CurrentAsset == null)
        {
            ImGui.TextDisabled("No blueprint selected.");
            return;
        }

        // -- Toolbar --
        bool isDirty = _dirtyTracker.IsDirty(CurrentAsset.AssetId);

        if (ImGui.Button("Save"))
        {
            // Save is handled externally; mark clean optimistically.
            _dirtyTracker.MarkClean(CurrentAsset.AssetId);
        }

        ImGui.SameLine();

        if (!isDirty) ImGui.BeginDisabled();
        if (ImGui.Button("Quick Reload"))
        {
            var asset = CurrentAsset;
            _ = _quickReloadService.TriggerAsync(asset);
        }
        if (!isDirty) ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Full Rebuild"))
        {
            _ = _fullRebuildService.TriggerAsync();
        }

        ImGui.Separator();

        // -- Canvas placeholder --
        ImGui.BeginChild("##canvas", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.TextDisabled($"Graph: {CurrentAsset.Name}");
        ImGui.EndChild();
    }

    public override void OnDeactivated()
    {
        Selection.ClearAll();
    }

    private void OnSelectionChanged()
    {
        var selected = _selectionStore.SelectedAsset;
        if (selected != null)
            OpenAsset(selected);
    }
}

