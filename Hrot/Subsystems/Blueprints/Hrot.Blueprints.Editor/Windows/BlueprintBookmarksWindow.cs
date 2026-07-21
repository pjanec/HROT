using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.UI.Bookmarks;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="ManagedWindow"/> that hosts the NodeEdit <see cref="BookmarksPanel"/> for the
/// Blueprint perspective. Read-only list of the active document's bookmarks (V1: no
/// rename/delete UI — see <see cref="BookmarksPanel"/>); set/jump is via the Ctrl+1..9 /
/// Ctrl+Shift+1..9 commands wired by <c>BlueprintDocumentFactory</c>.
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

        var store = isBlueprint ? (doc!.ViewState as AiCanvasContext)?.Bookmarks : null;
        if (store == null)
        {
            ImGuiNET.ImGui.TextDisabled("No blueprint open.");
            return;
        }

        new BookmarksPanel(store).Draw();
    }
}
