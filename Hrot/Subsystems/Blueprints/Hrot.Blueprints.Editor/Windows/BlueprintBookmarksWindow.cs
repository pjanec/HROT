using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.UI.Bookmarks;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="ManagedWindow"/> that hosts the NodeEdit <see cref="BookmarksPanel"/> for the
/// Blueprint perspective, listing the active document's bookmarks.
///
/// <para>
/// BP-03: the panel now supports rename (double-click or context menu) and delete, and clicking a
/// row jumps the canvas to it — the same move Ctrl+1..9 performs, but reachable without knowing
/// which slot is which. Setting a bookmark is still Ctrl+Shift+1..9, wired by
/// <c>BlueprintDocumentFactory</c>.
/// </para>
/// </summary>
public sealed class BlueprintBookmarksWindow : ManagedWindow
{
    private readonly AiDocumentManager _docManager;

    /// <param name="docManager">The shared document manager; used to resolve the active document.</param>
    /// <param name="idOverride">Optional stable ImGui id; defaults to <c>"ai_bookmarks_blueprint"</c>.</param>
    public BlueprintBookmarksWindow(AiDocumentManager docManager, string? idOverride = null)
        : base(idOverride ?? "ai_bookmarks_blueprint", "Bookmarks", "Blueprint", WindowScope.PerspectiveBound)
    {
        _docManager = docManager ?? throw new System.ArgumentNullException(nameof(docManager));
    }

    protected override void DrawClientArea()
    {
        var doc = _docManager.Active;
        var isBlueprint = doc != null &&
            string.Equals(doc.Kind.ToString(), AssetKind.Blueprint.ToString(), System.StringComparison.OrdinalIgnoreCase);

        var ctx   = isBlueprint ? doc!.ViewState as AiCanvasContext : null;
        var store = ctx?.Bookmarks;
        if (store == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint open.");
            return;
        }

        // BP-03: restore the saved viewport when a row is activated. The Blueprint editor renders a
        // single graph per document, so a bookmark's TargetGraph is always this view's own graph —
        // no cross-graph navigation to perform (same reasoning as BookmarkCommands' navigateToGraph
        // no-op in BlueprintDocumentFactory).
        var view = ctx?.View;
        Action<NodeEditor.Core.Bookmarks.Bookmark>? onJump = view is null
            ? null
            : b =>
            {
                view.Viewport.PanGraph = b.ViewportPan;
                view.Viewport.SetZoom(b.ViewportZoom);
            };

        new BookmarksPanel(store, onJump).Draw();
    }
}
