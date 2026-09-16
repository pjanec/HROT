using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Inspector;
using EngineWindowRegistrar = Fdp.Toolkit.Runner.IWindowRegistrar;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Registers all Blueprint editor windows with the supplied <see cref="IBlueprintWindowRegistry"/>.
/// Also implements the engine <see cref="Fdp.Toolkit.Runner.IWindowRegistrar"/> interface so the
/// subsystem orchestrator can register blueprint windows into the application <see cref="WindowManager"/>.
/// </summary>
public sealed class BlueprintWindowRegistrar : EngineWindowRegistrar
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;
    private readonly IBlueprintDebugSession _session;
    private readonly IBlueprintEditorCoordinator _coordinator;
    private readonly DrawerRegistry _drawerRegistry;

    public BlueprintWindowRegistrar(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState,
        IBlueprintDebugSession session,
        IBlueprintEditorCoordinator coordinator,
        DrawerRegistry drawerRegistry)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
        _session        = session        ?? throw new ArgumentNullException(nameof(session));
        _coordinator    = coordinator    ?? throw new ArgumentNullException(nameof(coordinator));
        _drawerRegistry = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    /// <summary>
    /// Registers factories for all Blueprint editor windows into <paramref name="registry"/>.
    /// </summary>
    public void RegisterWindows(IBlueprintWindowRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        // ⛔⛔ L5 — the "Inspector" entry is RETIRED (Q38's retire list, R-112's table row).
        //    📐 Measured: Hrot.Blueprints.Editor.InspectorWindow was a 70-line STUB — its Node tab
        //       drew the literal string "Node inspector -- select a node in the graph editor." and
        //       its Graph/Asset tabs listed names. ⭐ The real node inspector is
        //       BlueprintDetailsWindow's node arm (live since Batch 87), and the asset facts are the
        //       Details panel's business.
        //    ⚠ This is the SECOND class named InspectorWindow; the 697-line AiShared one STAYS
        //      (§6 L3: "do not delegate this one").

        registry.Register("Debug Panel",
            () => new DebugPanelWindow(_session));

        registry.Register("Watch Panel",
            () => new WatchPanelWindow(_session));

        registry.Register("Callstack",
            () => new CallstackWindow(_session, _selectionStore));

        registry.Register("Hot Reload Log",
            () => new HotReloadLogWindow(_coordinator));

    }

    // ---- Fdp.Toolkit.Runner.IWindowRegistrar --------------------------------

    /// <summary>
    /// Engine interface entry point. Creates a <see cref="BlueprintManagedWindowAdapter"/> for
    /// each blueprint window and registers it with the application <see cref="WindowManager"/>.
    /// </summary>
    void EngineWindowRegistrar.RegisterWindows(WindowManager wm)
    {
        if (wm is null) throw new ArgumentNullException(nameof(wm));
        RegisterWindows(new WindowManagerRegistry(wm));
    }

    // Bridges IBlueprintWindowRegistry to the engine WindowManager.
    private sealed class WindowManagerRegistry : IBlueprintWindowRegistry
    {
        private readonly WindowManager _wm;
        public WindowManagerRegistry(WindowManager wm) => _wm = wm;

        public void Register(string name, Func<IBlueprintEditorWindow> factory)
            => _wm.RegisterWindow(new BlueprintManagedWindowAdapter(name, factory));
    }
}
