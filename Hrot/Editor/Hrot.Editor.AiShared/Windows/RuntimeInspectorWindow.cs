using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shell for the shared runtime inspector. Renders the entity-lifecycle status,
/// mode controls, scrub bar, and delegates the asset-specific pane to
/// the registered IRuntimeInspectorPane for the active asset kind.
/// Subsystems provide IRuntimeInspectorPane implementations; this window
/// selects the matching pane at draw time.
/// </summary>
public sealed class RuntimeInspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IDebugSessionRegistry _registry;
    private readonly List<IRuntimeInspectorPane> _panes = new();

    /// <param name="store">Editor selection store for this perspective.</param>
    /// <param name="registry">Debug session registry.</param>
    /// <param name="idOverride">
    ///   Optional stable ImGui id override (e.g. <c>"ai_runtime_inspector_btree"</c>)
    ///   for per-perspective instances with independent dock layouts.
    /// </param>
    /// <param name="owningPerspective">
    ///   Perspective that owns this instance. Defaults to <c>"Authoring"</c>.
    /// </param>
    public RuntimeInspectorWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry,
        string? idOverride = null,
        string? owningPerspective = null)
        : base(idOverride ?? "ai_runtime_inspector", "Runtime Inspector",
               owningPerspective ?? "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
    }

    /// <summary>Register a subsystem-provided pane. Called at editor startup.</summary>
    public void RegisterPane(IRuntimeInspectorPane pane) => _panes.Add(pane);

    /// <summary>Number of registered panes. Exposed for test verification.</summary>
    internal int RegisteredPaneCount => _panes.Count;

    protected override void DrawClientArea()
    {
        // Shell: show empty state until subsystem panes are registered.
        ImGuiNET.ImGui.TextDisabled("No active session.");
    }
}
