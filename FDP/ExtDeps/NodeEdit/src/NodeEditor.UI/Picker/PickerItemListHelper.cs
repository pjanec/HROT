using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// Shared helper for rendering a flat, virtualized item list inside a picker child window.
/// Handles selection, keyboard focus highlight, and dynamic section headers.
/// </summary>
internal static class PickerItemListHelper
{
    private const float RowHeight = 22f;

    public static void DrawItems(PickerState state, IPickerRenderContext ctx, bool singleColumn)
    {
        if (state.Filtered.Count == 0)
        {
            ImGui.TextColored(ctx.Theme.TextMuted, "(no results)");
            return;
        }

        // Category grouping is only meaningful on an empty/whitespace query (browse mode).
        // When a search query is active, score ranking dominates and grouping is suppressed.
        bool groupByCategory = string.IsNullOrWhiteSpace(state.SearchText);

        // Use the clipper only when grouping is inactive (grouping requires sequential header
        // injection that the offset-based clipper can't handle). BTree has ~15 entries so
        // the fallback to a full pass is negligible. For large flat lists without grouping
        // the clipper still kicks in at 2000+ entries.
        bool useClipper = !groupByCategory && state.Filtered.Count > 2000;
        int firstRow = 0;
        int lastRow = state.Filtered.Count - 1;
        float cursorY = ImGui.GetCursorPosY();

        if (useClipper)
        {
            float scrollY = ImGui.GetScrollY();
            float windowH = ImGui.GetWindowHeight();
            firstRow = Math.Max(0, (int)(scrollY / RowHeight) - 1);
            lastRow = Math.Min(state.Filtered.Count - 1, (int)((scrollY + windowH) / RowHeight) + 1);
            ImGui.SetCursorPosY(cursorY + firstRow * RowHeight);
        }

        // When grouping, compute the display order: fav/recent entries stay first (in their
        // existing order), then normal entries are stable-sorted by Category so headers can
        // be emitted sequentially. The original FilteredIndex must be preserved for selection/
        // focus logic \u2014 we carry it alongside.
        var displayOrder = groupByCategory
            ? ComputeGroupedDisplayOrder(state.Filtered)
            : null; // null = use direct index

        bool favHeaderDrawn = firstRow > 0;
        bool recHeaderDrawn = firstRow > 0;
        string? lastNormCategory = firstRow > 0 ? "" : null; // null = not yet entered normal section

        // Architecturally critical: Iterate exactly once to prevent ID collisions.
        // Refilter() already sorts the collection strictly by Favorite > Recent > Score.
        int count = displayOrder?.Count ?? (lastRow - firstRow + 1);
        for (int di = 0; di < count; di++)
        {
            int i = displayOrder != null ? displayOrder[di].FilteredIndex : firstRow + di;
            var re = displayOrder != null ? displayOrder[di].Entry : state.Filtered[i];

            if (re.IsFavorite && !favHeaderDrawn)
            {
                ImGui.TextColored(ctx.Theme.TextMuted, "\u2605 Favorites");
                favHeaderDrawn = true;
            }
            else if (re.IsRecent && !re.IsFavorite && !recHeaderDrawn)
            {
                if (di > 0) ImGui.Separator();
                ImGui.TextColored(ctx.Theme.TextMuted, "\u21BB Recent");
                recHeaderDrawn = true;
            }
            else if (!re.IsFavorite && !re.IsRecent)
            {
                if (lastNormCategory is null)
                {
                    // First normal entry: emit separator.
                    if (di > 0) ImGui.Separator();
                    lastNormCategory = "";
                }

                // Category header whenever category changes (only in browse/grouping mode).
                if (groupByCategory)
                {
                    string cat = re.Entry.Category ?? "";
                    if (cat != lastNormCategory)
                    {
                        if (lastNormCategory!.Length > 0) ImGui.Spacing();
                        if (cat.Length > 0)
                            ImGui.TextColored(ctx.Theme.TextMuted, cat);
                        lastNormCategory = cat;
                    }
                }
            }

            DrawRow(state, ctx, i, re);
        }

        if (useClipper)
        {
            float remaining = (state.Filtered.Count - lastRow - 1) * RowHeight;
            if (remaining > 0f)
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remaining);
        }
    }

    /// <summary>
    /// Computes the display order for grouped rendering: fav/recent entries keep
    /// their original filtered indices, then normal entries are stable-sorted by
    /// <see cref="PickerEntry.Category"/> (preserving score order within each category).
    /// Returns a list of (FilteredIndex, RankedEntry) pairs in draw order.
    /// </summary>
    /// <remarks>
    /// This is a pure, side-effect-free helper \u2014 factored out so it can be unit-tested
    /// independently of ImGui rendering.
    /// </remarks>
    internal static List<(int FilteredIndex, RankedEntry Entry)> ComputeGroupedDisplayOrder(
        List<RankedEntry> filtered)
    {
        var result = new List<(int, RankedEntry)>(filtered.Count);

        // Pass 1: fav and recent entries keep their relative order.
        for (int i = 0; i < filtered.Count; i++)
        {
            var re = filtered[i];
            if (re.IsFavorite || re.IsRecent)
                result.Add((i, re));
        }

        // Pass 2: normal entries, stable-sorted by Category then original index
        // (which already reflects score desc / name asc from Refilter).
        var normals = new List<(int OrigIdx, RankedEntry Re)>();
        for (int i = 0; i < filtered.Count; i++)
        {
            var re = filtered[i];
            if (!re.IsFavorite && !re.IsRecent)
                normals.Add((i, re));
        }

        // Stable sort by category string (null/empty last so named categories group first).
        normals.Sort((a, b) =>
        {
            string ca = a.Re.Entry.Category ?? "";
            string cb = b.Re.Entry.Category ?? "";
            int cmp = string.Compare(ca, cb, StringComparison.OrdinalIgnoreCase);
            return cmp != 0 ? cmp : a.OrigIdx.CompareTo(b.OrigIdx); // preserve score order within category
        });

        foreach (var (oi, re) in normals)
            result.Add((oi, re));

        return result;
    }

    private static void DrawRow(PickerState state, IPickerRenderContext ctx, int filteredIdx, RankedEntry re)
    {
        bool isChecked = state.SelectedFilteredIndices.Contains(filteredIdx);
        bool isFocused = state.KeyboardFocusIndex == filteredIdx;
        bool chunkMatched = state.HighlightedIndices.Contains(filteredIdx) || (state.SelectionMode == PickerSelectionMode.Single && isChecked);

        ImGui.PushID(filteredIdx);

        var pos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(availWidth, RowHeight - 4f);

        // Invisible selectable captures hit-tests without fighting visual layout
        bool clicked = ImGui.Selectable("##sel", false,
            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
            size);

        // Architecturally critical: ImGui.Selectable natively triggers on Space/Enter presses. 
        // We must strictly filter this out so keyboard events don't falsely execute mouse-click 
        // logic and fracture the continuous range selection.
        bool actualMouseClicked = clicked && ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        var dl = ImGui.GetWindowDrawList();

        // Highlight background (Row span emphasis)
        if (chunkMatched)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.35f }), 2f);
        
        // Keyboard focus indicator
        if (isFocused)
            dl.AddRect(pos, pos + size, ImGui.GetColorU32(ctx.Theme.TextDefault with { W = 0.5f }), 2f);

        float textX = pos.X + 4f;

        // Draw inline row icon (16 px) when IconKey resolves via the provider.
        // Mirrors how TreeLayout draws leaf icons (same provider, same AddImage call).
        const float IconSize = 16f;
        if (re.Entry.IconKey is { Length: > 0 } iconKey &&
            ctx.Icons.TryGet(iconKey, out var rowIcon))
        {
            float iconY = pos.Y + (size.Y - IconSize) * 0.5f;
            dl.AddImage(rowIcon.TextureId,
                new Vector2(textX, iconY),
                new Vector2(textX + IconSize, iconY + IconSize),
                rowIcon.Uv0, rowIcon.Uv1);
            textX += IconSize + 4f;
        }

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
            if (actualMouseClicked && mouse.X >= cbPos.X && mouse.X <= cbPos.X + cbSize.X && mouse.Y >= cbPos.Y && mouse.Y <= cbPos.Y + cbSize.Y)
            {
                checkboxClicked = true;
            }

            textX += 20f;
        }

        // Render display name with match highlights
        float textY = pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f;
        uint defaultTextColor = chunkMatched ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)) : ImGui.GetColorU32(ctx.Theme.TextDefault);
        uint highlightColor   = chunkMatched ? ImGui.GetColorU32(new Vector4(1f, 1f, 0.4f, 1f)) : ImGui.GetColorU32(ctx.Theme.SelectionAccent);

        var runs = PickerTextHighlighter.SplitRuns(re.Entry.Name, re.MatchPositions);
        foreach (var run in runs)
        {
            uint color = run.IsMatch ? highlightColor : defaultTextColor;
            dl.AddText(new Vector2(textX, textY), color, run.Text);
            textX += ImGuiNET.ImGui.CalcTextSize(run.Text).X;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (state.SelectionMode == PickerSelectionMode.Single)
                state.Confirmed = true;
            else
            {
                if (!state.SelectedFilteredIndices.Contains(filteredIdx))
                    state.SelectedFilteredIndices.Add(filteredIdx);
            }
        }

        // Apply strict mouse-click rules
        if (actualMouseClicked)
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
                        state.SelectionAnchorIndex = filteredIdx;
                    }
                    else if (shift && state.HighlightedIndices.Count > 0)
                    {
                        int anchor = state.SelectionAnchorIndex;
                        int lo = Math.Min(anchor, filteredIdx);
                        int hi = Math.Max(anchor, filteredIdx);
                        state.HighlightedIndices.Clear();
                        for (int k = lo; k <= hi; k++)
                            state.HighlightedIndices.Add(k);
                    }
                    else
                    {
                        state.SelectionAnchorIndex = filteredIdx;
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

        if (ImGui.BeginPopupContextItem("##row_ctx"))
        {
            bool isFav = re.IsFavorite;
            if (ImGui.MenuItem(isFav ? "Unfavorite" : "Favorite"))
                state.Favorites.Toggle(state.ContextKey, re.Entry.Id);

            if (ImGui.MenuItem("Copy ID"))
                ImGui.SetClipboardText(re.Entry.Id);

            ImGui.EndPopup();
        }

        if (isFocused && state.ScrollToFocus)
        {
            ImGui.SetScrollHereY(0.5f);
            state.ScrollToFocus = false;
        }

        ImGui.PopID();
    }
}
