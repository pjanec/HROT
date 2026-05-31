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

    // private

    private static void DrawRow(PickerState state, IPickerRenderContext ctx, int filteredIdx, RankedEntry re)
    {
        bool isChecked = state.SelectedFilteredIndices.Contains(filteredIdx);
        bool isFocused = state.KeyboardFocusIndex == filteredIdx;
        bool isHighlighted = state.HighlightedIndices.Contains(filteredIdx) || (state.SelectionMode == PickerSelectionMode.Single && isChecked);

        ImGui.PushID(filteredIdx);

        var pos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(availWidth, RowHeight - 4f);

        // Invisible selectable captures hit-tests without fighting visual layout
        bool clicked = ImGui.Selectable("##sel", false,
            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
            size);

        var dl = ImGui.GetWindowDrawList();

        // Highlight background (row span emphasis)
        if (isHighlighted)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.35f }), 2f);

        // Keyboard focus indicator
        if (isFocused)
            dl.AddRect(pos, pos + size, ImGui.GetColorU32(ctx.Theme.TextDefault with { W = 0.5f }), 2f);

        float textX = pos.X + 4f;

        // Render multi-select checkbox [ ] / [x]
        bool checkboxClicked = false;
        if (state.SelectionMode != PickerSelectionMode.Single)
        {
            var cbSize = new Vector2(14f, 14f);
            var cbPos = new Vector2(textX, pos.Y + (size.Y - cbSize.Y) * 0.5f);

            dl.AddRect(cbPos, cbPos + cbSize, ImGui.GetColorU32(ctx.Theme.TextMuted), 2f);
            if (isChecked)
                dl.AddRectFilled(cbPos + new Vector2(3f, 3f), cbPos + new Vector2(11f, 11f), ImGui.GetColorU32(ctx.Theme.TextDefault), 1f);

            // Strict geometric hit-test against the checkbox
            var mouse = ImGui.GetMousePos();
            if (clicked && mouse.X >= cbPos.X && mouse.X <= cbPos.X + cbSize.X && mouse.Y >= cbPos.Y && mouse.Y <= cbPos.Y + cbSize.Y)
            {
                checkboxClicked = true;
            }

            textX += 20f;
        }

        // Render display name with match highlights
        float textY = pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f;
        uint defaultTextColor = isHighlighted ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)) : ImGui.GetColorU32(ctx.Theme.TextDefault);
        uint highlightColor   = isHighlighted ? ImGui.GetColorU32(new Vector4(1f, 1f, 0.4f, 1f)) : ImGui.GetColorU32(ctx.Theme.SelectionAccent);

        if (re.MatchPositions is { Count: > 0 } matchSet)
        {
            var set = new HashSet<int>(matchSet);
            for (int i = 0; i < re.Entry.Name.Length; i++)
            {
                var ch = re.Entry.Name[i].ToString();
                var color = set.Contains(i) ? highlightColor : defaultTextColor;
                dl.AddText(new Vector2(textX, textY), color, ch);
                textX += ImGui.CalcTextSize(ch).X;
            }
        }
        else
        {
            dl.AddText(new Vector2(textX, textY), defaultTextColor, re.Entry.Name);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (state.SelectionMode == PickerSelectionMode.Single)
                state.Confirmed = true;
            else
            {
                // Double-click in multi-select forces the item checked
                if (!state.SelectedFilteredIndices.Contains(filteredIdx))
                    state.SelectedFilteredIndices.Add(filteredIdx);
            }
        }

        // Apply mouse-click rules
        if (clicked)
        {
            bool ctrl  = ImGui.GetIO().KeyCtrl;
            bool shift = ImGui.GetIO().KeyShift;

            if (state.SelectionMode != PickerSelectionMode.Single)
            {
                if (checkboxClicked)
                {
                    // Clicked exactly on the checkbox box -> toggle ONLY the checked state
                    if (!state.SelectedFilteredIndices.Remove(filteredIdx))
                        state.SelectedFilteredIndices.Add(filteredIdx);

                    state.KeyboardFocusIndex = filteredIdx;
                }
                else
                {
                    // Clicked the row label -> update highlight/focus span
                    if (ctrl)
                    {
                        if (!state.HighlightedIndices.Remove(filteredIdx))
                            state.HighlightedIndices.Add(filteredIdx);
                    }
                    else if (shift && state.HighlightedIndices.Count > 0)
                    {
                        int anchor = state.KeyboardFocusIndex;
                        int lo = Math.Min(anchor, filteredIdx);
                        int hi = Math.Max(anchor, filteredIdx);
                        state.HighlightedIndices.Clear();
                        for (int k = lo; k <= hi; k++)
                            state.HighlightedIndices.Add(k);
                    }
                    else
                    {
                        state.HighlightedIndices.Clear();
                        state.HighlightedIndices.Add(filteredIdx);
                    }
                    state.KeyboardFocusIndex = filteredIdx;
                }
            }
            else
            {
                // Single-select mode enforces unified highlight and selection
                state.SelectedFilteredIndices.Clear();
                state.SelectedFilteredIndices.Add(filteredIdx);
                state.KeyboardFocusIndex = filteredIdx;
            }
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
        if (isFocused)
            ImGui.SetScrollHereY(0.5f);

        ImGui.PopID();
    }

    private static IEnumerable<(RankedEntry re, int index)> IndexedFiltered(PickerState state)
    {
        for (int i = 0; i < state.Filtered.Count; i++)
            yield return (state.Filtered[i], i);
    }
}
