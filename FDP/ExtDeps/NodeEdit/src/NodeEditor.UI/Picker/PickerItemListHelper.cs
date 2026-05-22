using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Shared helper for rendering a flat, virtualized item list inside a picker child window.
/// Used by Standard and Compact layouts. Handles selection, keyboard focus highlight,
/// right-click context menu (Favorite / Copy ID), and Favorites/Recent section headers.
/// </summary>
internal static class PickerItemListHelper
{
    private const float RowHeight = 22f;

    /// <summary>
    /// Draw the full item list including Favorites and Recent pinned sections.
    /// Must be called inside a child window.
    /// </summary>
    public static void DrawItems(PickerState state, IPickerRenderContext ctx, bool singleColumn)
    {
        if (state.Filtered.Count == 0)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "(no results)");
            return;
        }

        bool showFavSection = false;
        bool showRecSection = false;

        foreach (var re in state.Filtered)
        {
            if (re.IsFavorite) { showFavSection = true; break; }
        }
        foreach (var re in state.Filtered)
        {
            if (re.IsRecent) { showRecSection = true; break; }
        }

        if (showFavSection)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "\u2605 Favorites");
            foreach (var (re, i) in IndexedFiltered(state))
            {
                if (re.IsFavorite) DrawRow(state, ctx, i, re);
            }
            ImGui.Separator();
        }

        if (showRecSection)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "\u21BB Recent");
            foreach (var (re, i) in IndexedFiltered(state))
            {
                if (re.IsRecent && !re.IsFavorite) DrawRow(state, ctx, i, re);
            }
            ImGui.Separator();
        }

        // Main results.
        bool useClipper = state.Filtered.Count > 2000;
        if (useClipper)
        {
            // Approximate virtualization: only draw visible rows.
            float scrollY   = ImGui.GetScrollY();
            float windowH   = ImGui.GetWindowHeight();
            int firstRow    = Math.Max(0, (int)(scrollY / RowHeight) - 1);
            int lastRow     = Math.Min(state.Filtered.Count - 1, (int)((scrollY + windowH) / RowHeight) + 1);

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + firstRow * RowHeight);
            for (int i = firstRow; i <= lastRow; i++)
            {
                DrawRow(state, ctx, i, state.Filtered[i]);
            }
            float remaining = (state.Filtered.Count - lastRow - 1) * RowHeight;
            if (remaining > 0f)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remaining);
        }
        else
        {
            for (int i = 0; i < state.Filtered.Count; i++)
                DrawRow(state, ctx, i, state.Filtered[i]);
        }
    }

    // ── private ──────────────────────────────────────────────────────────────

    private static void DrawRow(PickerState state, IPickerRenderContext ctx, int filteredIdx, RankedEntry re)
    {
        bool sel   = state.SelectedFilteredIndices.Contains(filteredIdx);
        bool focus = state.KeyboardFocusIndex == filteredIdx;

        ImGui.PushID(filteredIdx);

        // Selection label — highlight matched characters when match positions available.
        if (sel || focus)
            ImGui.PushStyleColor(ImGuiCol.Header, ImGui.GetColorU32(ctx.Theme.SelectionAccent));

        bool clicked = ImGui.Selectable(re.Entry.Name, sel || focus,
                          ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
                          new Vector2(0f, RowHeight - 4f));

        if (sel || focus) ImGui.PopStyleColor();

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.Confirmed = true;
        }

        if (clicked)
        {
            bool ctrl  = ImGui.GetIO().KeyCtrl;
            bool shift = ImGui.GetIO().KeyShift;

            if (ctrl)
            {
                if (!state.SelectedFilteredIndices.Remove(filteredIdx))
                    state.SelectedFilteredIndices.Add(filteredIdx);
            }
            else if (shift && state.SelectedFilteredIndices.Count > 0)
            {
                int anchor = state.KeyboardFocusIndex;
                int lo = Math.Min(anchor, filteredIdx);
                int hi = Math.Max(anchor, filteredIdx);
                for (int k = lo; k <= hi; k++)
                    state.SelectedFilteredIndices.Add(k);
            }
            else
            {
                state.SelectedFilteredIndices.Clear();
                state.SelectedFilteredIndices.Add(filteredIdx);
            }

            state.KeyboardFocusIndex = filteredIdx;
        }

        // Right-click context menu.
        if (ImGui.BeginPopupContextItem("##row_ctx"))
        {
            bool isFav = re.IsFavorite;
            if (ImGui.MenuItem(isFav ? "Unfavorite" : "Favorite"))
                state.Favorites.Toggle(state.ContextKey, re.Entry.Id);

            if (ImGui.MenuItem("Copy ID"))
                ImGui.SetClipboardText(re.Entry.Id);

            ImGui.EndPopup();
        }

        // Scroll-to when keyboard-focused.
        if (focus && !sel)
            ImGui.SetScrollHereY(0.5f);

        ImGui.PopID();
    }

    private static IEnumerable<(RankedEntry re, int index)> IndexedFiltered(PickerState state)
    {
        for (int i = 0; i < state.Filtered.Count; i++)
            yield return (state.Filtered[i], i);
    }
}
