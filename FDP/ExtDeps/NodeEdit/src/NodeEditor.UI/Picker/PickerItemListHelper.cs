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

        bool useClipper = state.Filtered.Count > 2000;
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

        bool favHeaderDrawn = firstRow > 0;
        bool recHeaderDrawn = firstRow > 0;
        bool normHeaderDrawn = firstRow > 0;

        // Architecturally critical: Iterate exactly once to prevent ID collisions.
        // Refilter() already sorts the collection strictly by Favorite > Recent > Score.
        for (int i = firstRow; i <= lastRow; i++)
        {
            var re = state.Filtered[i];
            
            if (re.IsFavorite && !favHeaderDrawn)
            {
                ImGui.TextColored(ctx.Theme.TextMuted, "\u2605 Favorites");
                favHeaderDrawn = true;
            }
            else if (re.IsRecent && !re.IsFavorite && !recHeaderDrawn)
            {
                if (i > 0) ImGui.Separator();
                ImGui.TextColored(ctx.Theme.TextMuted, "\u21BB Recent");
                recHeaderDrawn = true;
            }
            else if (!re.IsFavorite && !re.IsRecent && !normHeaderDrawn)
            {
                if (i > 0) ImGui.Separator();
                normHeaderDrawn = true;
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

        if (isFocused)
            ImGui.SetScrollHereY(0.5f);

        ImGui.PopID();
    }
}
