using ImGuiNET;
using NodeEditor.Core.Bookmarks;
using System.Numerics;

namespace NodeEditor.UI.Bookmarks;

/// <summary>
/// Side-panel listing all bookmarks.
///
/// <para>
/// BP-03: rename and delete. The V1 panel was a read-only text list, so a bookmark set with
/// Ctrl+Shift+N could never be relabelled or removed — the only way to reclaim a slot was to
/// overwrite it, and an unslotted bookmark was permanent. <see cref="BookmarkStore.Remove"/>
/// already existed and <see cref="Bookmark"/> is a record, so both were one call away.
/// </para>
///
/// <para>
/// Ordering and labelling live in <see cref="BookmarkPanelLogic"/> so they are testable without an
/// ImGui context; this class is the ImGui surface only.
/// </para>
/// </summary>
public sealed class BookmarksPanel
{
    private readonly BookmarkStore _store;
    private readonly Action<Bookmark>? _onJump;

    // Inline-rename view state: which bookmark is being edited, and its in-progress text.
    private string? _renamingId;
    private string  _renameBuffer = "";
    private bool    _renameJustStarted;

    /// <param name="store">The document's bookmark collection.</param>
    /// <param name="onJump">
    /// Optional: invoked when a bookmark row is activated, so the host can move the viewport. When
    /// null the rows stay inert, matching V1 for callers that have no view to drive.
    /// </param>
    public BookmarksPanel(BookmarkStore store, Action<Bookmark>? onJump = null)
    {
        _store  = store ?? throw new ArgumentNullException(nameof(store));
        _onJump = onJump;
    }

    /// <summary>Draw the panel contents. Call inside an <c>ImGui.Begin</c>/<c>End</c> pair.</summary>
    public void Draw()
    {
        if (!_store.All.Any())
        {
            ImGui.TextDisabled("No bookmarks yet. Press Ctrl+Shift+1..9 to set one.");
            _renamingId = null;
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 6));

        // Materialised: deleting a row mutates the store mid-iteration.
        foreach (var b in BookmarkPanelLogic.InDisplayOrder(_store.All).ToList())
        {
            ImGui.PushID(b.BookmarkId);

            if (_renamingId == b.BookmarkId)
                DrawRenameRow(b);
            else
                DrawRow(b);

            ImGui.PopID();
        }

        ImGui.PopStyleVar();
    }

    /// <summary>Width reserved at the right edge of a row for the delete button.</summary>
    private const float DeleteButtonWidth = 22f;

    private void DrawRow(Bookmark b)
    {
        ImGui.Text(BookmarkPanelLogic.SlotLabel(b));
        ImGui.SameLine();

        // The Selectable is explicitly sized to stop short of the delete button. A zero-width
        // Selectable spans the full remaining row, so it swallowed the click before the button ever
        // saw it — the button was drawn and correctly positioned, and simply unreachable. Sizing the
        // two so their rects do not overlap is more predictable here than AllowItemOverlap, which
        // relies on draw order and on the caller remembering SetItemAllowOverlap.
        var rowWidth        = ImGui.GetContentRegionAvail().X;
        var selectableWidth = MathF.Max(1f, rowWidth - DeleteButtonWidth - ImGui.GetStyle().ItemSpacing.X);

        if (ImGui.Selectable(b.Label, false, ImGuiSelectableFlags.AllowDoubleClick,
                new Vector2(selectableWidth, 0f)))
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                BeginRename(b);
            else
                _onJump?.Invoke(b);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                $"Pan: ({b.ViewportPan.X:F0}, {b.ViewportPan.Y:F0})  Zoom: {b.ViewportZoom:F2}×\n" +
                "Double-click to rename · right-click for more");

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Jump To", _onJump is not null))
                _onJump?.Invoke(b);
            if (ImGui.MenuItem("Rename"))
                BeginRename(b);
            ImGui.Separator();
            if (ImGui.MenuItem("Delete"))
                _store.Remove(b.BookmarkId);
            ImGui.EndPopup();
        }

        // Delete button, in the strip the Selectable above deliberately left free. No cursor
        // arithmetic needed: SameLine lands exactly where the Selectable ended.
        ImGui.SameLine();
        if (ImGui.SmallButton("x"))
            _store.Remove(b.BookmarkId);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Delete this bookmark");
    }

    private void DrawRenameRow(Bookmark b)
    {
        ImGui.Text(BookmarkPanelLogic.SlotLabel(b));
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1f);
        if (_renameJustStarted)
        {
            ImGui.SetKeyboardFocusHere();
            _renameJustStarted = false;
        }

        bool committed = ImGui.InputText("##rename", ref _renameBuffer, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        // Enter commits; Escape abandons; clicking away also commits (matching how the rest of the
        // editor's inline fields behave). A blank label is refused by the store, so abandoning a
        // rename mid-word leaves the original label intact rather than an empty row.
        if (committed)
        {
            _store.Rename(b.BookmarkId, _renameBuffer);
            _renamingId = null;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _renamingId = null;
        }
        else if (ImGui.IsItemDeactivated())
        {
            _store.Rename(b.BookmarkId, _renameBuffer);
            _renamingId = null;
        }
    }

    private void BeginRename(Bookmark b)
    {
        _renamingId        = b.BookmarkId;
        _renameBuffer      = b.Label;
        _renameJustStarted = true;
    }
}

/// <summary>
/// Non-ImGui logic for <see cref="BookmarksPanel"/>, split out so the ordering and labelling rules
/// are testable headlessly (the panel itself needs an ImGui context).
/// </summary>
public static class BookmarkPanelLogic
{
    /// <summary>
    /// Slot-bound bookmarks first, in slot order (1-9), then unslotted ones. Unslotted bookmarks
    /// order by label so the list is stable rather than dictionary-ordered.
    /// </summary>
    public static IEnumerable<Bookmark> InDisplayOrder(IEnumerable<Bookmark> bookmarks)
        => bookmarks
            .OrderBy(b => b.SlotNumber is >= 1 and <= 9 ? b.SlotNumber : 10)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>[n]</c> / <c>[  ]</c> prefix shown before a bookmark's label.</summary>
    public static string SlotLabel(Bookmark b)
        => b.SlotNumber is >= 1 and <= 9 ? $"[{b.SlotNumber}]" : "[  ]";
}
