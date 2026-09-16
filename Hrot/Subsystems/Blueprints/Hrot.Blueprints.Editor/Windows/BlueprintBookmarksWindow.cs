using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.UI.Bookmarks;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 (group 3) — THE CALLER-REGISTERS RULE, applied.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Adoption's <c>2026-08-22</c> extension.
/// <c>BookmarksPanel</c> is a generic NodeEdit panel with no identity of its own — it renders whatever
/// <see cref="NodeEditor.Core.Bookmarks.BookmarkStore"/> it is handed. ⇒ this window, the CALLER,
/// registers the structure it resolved — the store's bookmarks, projected by hand (a NodeEdit type,
/// so no reflection over it — the queue's gotcha).
/// </summary>
public sealed record BlueprintBookmarksWindowPanelViewModel(
    string PanelId,
    string PanelKind,
    bool   HasBlueprintOpen,
    IReadOnlyList<string> Labels) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump()
    {
        var labels = new JsonArray();
        foreach (var l in Labels) labels.Add(l);

        return new JsonObject
        {
            ["panelId"]          = PanelId,
            ["panelKind"]        = PanelKind,
            ["hasBlueprintOpen"] = HasBlueprintOpen,
            ["bookmarkCount"]    = Labels.Count,
            ["labels"]           = labels,
        };
    }
}

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
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. ⛔ Local literal — no other host has a bookmarks window
    /// today (measured: one implementation, repo-wide).</summary>
    internal const string Kind = "bookmarks";

    private readonly AiDocumentManager _docManager;

    /// <param name="docManager">The shared document manager; used to resolve the active document.</param>
    /// <param name="idOverride">Optional stable ImGui id; defaults to <c>"ai_bookmarks_blueprint"</c>.</param>
    public BlueprintBookmarksWindow(AiDocumentManager docManager, string? idOverride = null)
        : base(idOverride ?? "ai_bookmarks_blueprint", "Bookmarks", "Blueprint", WindowScope.PerspectiveBound)
    {
        _docManager = docManager ?? throw new System.ArgumentNullException(nameof(docManager));

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
    }

    /// <summary>⭐⭐⭐ U-obs-5: BUILD · CAPTURE. ⛔⛔ No ImGui.</summary>
    private BlueprintBookmarksWindowPanelViewModel BuildAndPublish(NodeEditor.Core.Bookmarks.BookmarkStore? store)
    {
        var labels = new List<string>();
        if (store != null)
            foreach (var b in store.All) labels.Add(b.Label);

        var vm = new BlueprintBookmarksWindowPanelViewModel(Id, Kind, store != null, labels);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal BlueprintBookmarksWindowPanelViewModel SimulateDrawClientArea()
    {
        var doc = _docManager.Active;
        var isBlueprint = doc != null &&
            string.Equals(doc.Kind.ToString(), AssetKind.Blueprint.ToString(), System.StringComparison.OrdinalIgnoreCase);
        var ctx = isBlueprint ? doc!.ViewState as AiCanvasContext : null;
        return BuildAndPublish(ctx?.Bookmarks);
    }

    protected override void DrawClientArea()
    {
        var doc = _docManager.Active;
        var isBlueprint = doc != null &&
            string.Equals(doc.Kind.ToString(), AssetKind.Blueprint.ToString(), System.StringComparison.OrdinalIgnoreCase);

        var ctx   = isBlueprint ? doc!.ViewState as AiCanvasContext : null;
        var store = ctx?.Bookmarks;
        BuildAndPublish(store);

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
