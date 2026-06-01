using NodeEditor.Core.Action;
using NodeEditor.Core.Bookmarks;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.UI.Bookmarks;

/// <summary>
/// Registers Ctrl+1..9 (jump) and Ctrl+Shift+1..9 (set) bookmark commands.
/// </summary>
public static class BookmarkCommands
{
    /// <summary>Register all 18 bookmark commands (set + jump × 9 slots).</summary>
    public static void RegisterAll(
        EditorCommandsImpl cmds,
        GraphView          view,
        BookmarkStore      store,
        IEditorIndicators  indicators,
        System.Action<GraphId> navigateToGraph)
    {
        for (int slot = 1; slot <= 9; slot++)
        {
            int s = slot; // closure capture

            cmds.Register(
                new EditorCommandDescriptor(
                    $"editor.bookmark.jump.{s}",
                    $"Jump to Bookmark {s}",
                    "Navigation",
                    $"Jump to bookmark in slot {s}.",
                    null,
                    new KeyBinding((EditorKey)((int)EditorKey.D0 + s), KeyModifiers.Ctrl),
                    IsEnabled: () => store.GetSlot(s) is not null),
                _ => JumpToBookmark(store, s, view, navigateToGraph));

            cmds.Register(
                new EditorCommandDescriptor(
                    $"editor.bookmark.set.{s}",
                    $"Set Bookmark {s}",
                    "Navigation",
                    $"Set bookmark in slot {s} to the current viewport.",
                    null,
                    new KeyBinding((EditorKey)((int)EditorKey.D0 + s), KeyModifiers.Ctrl | KeyModifiers.Shift),
                    IsEnabled: () => true),
                _ => SetBookmark(store, s, view, indicators));
        }
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private static void JumpToBookmark(
        BookmarkStore store, int slot, GraphView view, System.Action<GraphId> navigate)
    {
        var b = store.GetSlot(slot);
        if (b is null) return;

        if (b.TargetGraph != view.Model.Id)
            navigate(b.TargetGraph);

        // Animate over 180 ms via a short tween stored in InteractionState
        view.Interaction.BeginViewportTween(b.ViewportPan, b.ViewportZoom, durationMs: 180);
    }

    private static void SetBookmark(BookmarkStore store, int slot, GraphView view, IEditorIndicators indicators)
    {
        bool isOverwrite = store.GetSlot(slot) is not null;
        var pan  = view.Viewport.PanGraph;
        var zoom = view.Viewport.Zoom;
        var label = $"{view.Model.DisplayName} @ ({pan.X:F0}, {pan.Y:F0})";

        store.SetSlot(slot, new Core.Bookmarks.Bookmark(
            Guid.NewGuid().ToString("N"),
            view.Model.Id,
            label,
            pan,
            zoom,
            slot,
            DateTime.UtcNow));

        indicators.Notify(new EditorNotification(
            Id: Guid.NewGuid().ToString(),
            Severity: isOverwrite ? NotificationSeverity.Warning : NotificationSeverity.Success,
            Title: isOverwrite ? $"Bookmark {slot} Overwritten" : $"Bookmark {slot} Set",
            Body: label,
            AutoDismiss: TimeSpan.FromSeconds(3),
            Actions: null));
    }
}
