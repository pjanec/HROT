using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared.Selection;
using AiSelectionStore = Hrot.Editor.AiShared.Selection.EditorSelectionStore;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Details window for the Blueprint perspective.
/// Shows the node-drawer UI for the currently-selected Blueprint node.
///
/// <para>
/// The window reads <see cref="EditorSelectionStore.ActiveSubSelection"/> each draw frame.
/// When the selection is a <see cref="BlueprintNodeSelection"/>, it resolves the matching
/// <see cref="IBlueprintNodeDrawer"/> from the <see cref="BlueprintNodeDrawerRegistry"/> and
/// creates (or reuses) an <see cref="INodeEditSession"/> for that node.
/// </para>
///
/// <para>
/// Keep all ImGui calls inside <see cref="DrawClientArea"/> so the interaction/projection
/// logic (selection → session) can be exercised in headless unit tests.
/// </para>
/// </summary>
public sealed class BlueprintDetailsWindow : ManagedWindow
{
    private readonly AiSelectionStore _selectionStore;
    private readonly BlueprintNodeDrawerRegistry _drawerRegistry;

    // Cached session — rebuilt when selection changes.
    private INodeEditSession? _session;
    private Guid _sessionNodeId;
    private Guid _sessionGraphId;

    // Active asset supplied by Retarget; needed to create sessions.
    private BlueprintAsset? _asset;

    // Projection helpers (extracted so headless tests can call ResolveDrawerForSelection directly).

    /// <summary>
    /// The kind of drawer that was resolved for the current selection.
    /// Null when nothing is selected, the selection is not a blueprint node, or no drawer
    /// handles the node type.  Used by tests to assert the resolved drawer kind.
    /// </summary>
    public Type? ResolvedDrawerKind { get; private set; }

    // ── ctor ─────────────────────────────────────────────────────────────────

    /// <param name="selectionStore">Per-perspective selection store.</param>
    /// <param name="drawerRegistry">Blueprint node-drawer registry.</param>
    /// <param name="idOverride">Stable ImGui id; defaults to <c>"ai_details_blueprint"</c>.</param>
    /// <param name="owningPerspective">Perspective name; defaults to <c>"Blueprint"</c>.</param>
    public BlueprintDetailsWindow(
        AiSelectionStore selectionStore,
        BlueprintNodeDrawerRegistry drawerRegistry,
        string? idOverride        = null,
        string? owningPerspective = null)
        : base(idOverride        ?? "ai_details_blueprint",
               "Details",
               owningPerspective ?? "Blueprint",
               WindowScope.PerspectiveBound)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _drawerRegistry = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retarget to a different active Blueprint asset (e.g. when the document changes).
    /// Clears the cached session so the next frame rebuilds it against the new asset.
    /// </summary>
    public void Retarget(BlueprintAsset? asset)
    {
        if (_asset == asset) return;
        _asset = asset;
        ClearSession();
    }

    /// <summary>
    /// Resolves (or returns cached) drawer + session for the currently-selected node.
    /// Returns the resolved <see cref="INodeEditSession"/> or null when nothing is selected.
    /// This is the core projection logic; separated so tests can call it directly without ImGui.
    /// </summary>
    public INodeEditSession? ResolveSession()
    {
        if (_asset == null) { ClearSession(); return null; }

        var sub = _selectionStore.ActiveSubSelection as BlueprintNodeSelection;
        if (sub == null) { ClearSession(); return null; }

        // Same node already has a session — reuse it.
        if (_session != null && _sessionNodeId == sub.NodeId && _sessionGraphId == sub.GraphId)
            return _session;

        // New selection — find the node in the asset graph.
        ClearSession();

        var graph = _asset.Graphs.FirstOrDefault(g => g.Id == sub.GraphId);
        if (graph == null) return null;

        var node = graph.Nodes.FirstOrDefault(n => n.Id == sub.NodeId);
        if (node == null) return null;

        var drawer = _drawerRegistry.GetDrawerFor(node);
        if (drawer == null) { ResolvedDrawerKind = null; return null; }

        ResolvedDrawerKind = drawer.GetType();
        _session       = drawer.CreateSession(node, _asset);
        _sessionNodeId  = sub.NodeId;
        _sessionGraphId = sub.GraphId;
        return _session;
    }

    // ── ManagedWindow ─────────────────────────────────────────────────────────

    protected override void DrawClientArea()
    {
        var session = ResolveSession();

        if (session == null)
        {
            // ImGui.TextDisabled — acceptable call inside DrawClientArea.
            ImGuiNET.ImGui.TextDisabled("No node selected.");
            return;
        }

        // Delegate all rendering to the session (which may call ImGui freely).
        session.Draw();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void ClearSession()
    {
        _session?.Dispose();
        _session        = null;
        _sessionNodeId  = Guid.Empty;
        _sessionGraphId = Guid.Empty;
        ResolvedDrawerKind = null;
    }
}
