using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Inspector;
using Hrot.Blueprints.Editor.Reload;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Registers all Blueprint editor windows with the supplied <see cref="IBlueprintWindowRegistry"/>.
/// Call <see cref="RegisterWindows"/> from the editor bootstrap / DI composition root.
/// </summary>
public sealed class BlueprintWindowRegistrar
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;
    private readonly IBlueprintDebugSession _session;
    private readonly IBlueprintEditorCoordinator _coordinator;
    private readonly QuickReloadService _quickReloadService;
    private readonly FullRebuildService _fullRebuildService;
    private readonly DrawerRegistry _drawerRegistry;

    public BlueprintWindowRegistrar(
        IAssetCatalog catalog,
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState,
        IBlueprintDebugSession session,
        IBlueprintEditorCoordinator coordinator,
        QuickReloadService quickReloadService,
        FullRebuildService fullRebuildService,
        DrawerRegistry drawerRegistry)
    {
        _catalog             = catalog             ?? throw new ArgumentNullException(nameof(catalog));
        _selectionStore      = selectionStore      ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker        = dirtyTracker        ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState         = editorState         ?? throw new ArgumentNullException(nameof(editorState));
        _session             = session             ?? throw new ArgumentNullException(nameof(session));
        _coordinator         = coordinator         ?? throw new ArgumentNullException(nameof(coordinator));
        _quickReloadService  = quickReloadService  ?? throw new ArgumentNullException(nameof(quickReloadService));
        _fullRebuildService  = fullRebuildService  ?? throw new ArgumentNullException(nameof(fullRebuildService));
        _drawerRegistry      = drawerRegistry      ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    /// <summary>
    /// Registers factories for all Blueprint editor windows into <paramref name="registry"/>.
    /// </summary>
    public void RegisterWindows(IBlueprintWindowRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        registry.Register("Asset Browser",
            () => new AssetBrowserWindow(_catalog, _selectionStore, _dirtyTracker, _editorState));

        registry.Register("Graph Editor",
            () => new GraphEditorWindow(_selectionStore, _dirtyTracker, _editorState,
                                        _quickReloadService, _fullRebuildService));

        registry.Register("Inspector",
            () => new InspectorWindow(_selectionStore, _dirtyTracker, _drawerRegistry));

        registry.Register("Debug Panel",
            () => new DebugPanelWindow(_session));

        registry.Register("Watch Panel",
            () => new WatchPanelWindow(_session));

        registry.Register("Callstack",
            () => new CallstackWindow(_session, _selectionStore));

        registry.Register("Hot Reload Log",
            () => new HotReloadLogWindow(_coordinator));
    }
}
