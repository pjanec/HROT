using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Grid layout: 4-column thumbnail tiles (128×144 px each).
/// Used for asset pickers with visual thumbnails.
/// </summary>
internal static class GridLayout
{
    private const float TileWidth   = 128f;
    private const float TileHeight  = 144f;
    private const int   Columns     = 4;
    private const float TilePadding = 4f;
    private const float PreviewH    = 80f;

    /// <summary>Render the grid of tiles with a detail strip at the bottom.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx)
    {
        // ⭐ CE-4142 — a request with a group sidebar is the thumbnail GALLERY; any other request keeps the classic fixed Grid below.
        if (state.Groups is { } groups) { DrawGallery(state, ctx, groups); return; }

        float listH  = ImGui.GetContentRegionAvail().Y - PreviewH - PickerLayoutMetrics.FooterReserve(state) - 8f;
        float listH2 = Math.Max(listH, TileHeight + TilePadding * 2f);

        if (ImGui.BeginChild("##picker_grid", new Vector2(0f, listH2), ImGuiChildFlags.None))
        {
            try
            {
                DrawGrid(state, ctx);
            }
            finally
            {
                ImGui.EndChild();
            }
        }
        else
        {
            ImGui.EndChild();
        }

        ImGui.Separator();
        DrawDetailStrip(state, ctx);
    }


    // ── ⭐ CE-4142 GL3 — the gallery: group sidebar left, chips + size slider on top, sized tiles right ──

    private const float SidebarW = 200f;
    private const float ThumbAspect = 92f / 160f;   // G3: 160 × 92 at the default size

    private static void DrawGallery(PickerState state, IPickerRenderContext ctx, PickerGroupSidebar groups)
    {
        float height = ImGui.GetContentRegionAvail().Y - PickerLayoutMetrics.FooterReserve(state);

        if (ImGui.BeginChild("##gallery_groups", new Vector2(SidebarW, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
        {
            try { CategorySidebar.Draw(state, ctx, groups); }
            finally { ImGui.EndChild(); }
        }
        else ImGui.EndChild();

        ImGui.SameLine(0f, 4f);

        if (ImGui.BeginChild("##gallery_right", new Vector2(0f, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
        {
            try
            {
                DrawChipsAndSlider(state, ctx, groups);
                if (ImGui.BeginChild("##gallery_tiles", new Vector2(0f, 0f), ImGuiChildFlags.None))
                {
                    try { DrawTiles(state, ctx, groups); }
                    finally { ImGui.EndChild(); }
                }
                else ImGui.EndChild();
            }
            finally { ImGui.EndChild(); }
        }
        else ImGui.EndChild();
    }

    private static bool Searching(PickerState state) => state.SearchText.Length > 0;

    private static void DrawChipsAndSlider(PickerState state, IPickerRenderContext ctx, PickerGroupSidebar groups)
    {
        const float sliderW = 110f;
        string sel = state.SelectedCategory;
        if (sel.Length > 0 && sel != groups.RecentGroup && !Searching(state))
        {
            var chips = CategorySidebar.ChipsOf(state, sel, groups);
            if (chips.Count >= 2)
            {
                if (ChipButton("All", state.SelectedChip.Length == 0, ctx)) state.SelectedChip = "";
                foreach (var chip in chips)
                {
                    ImGui.SameLine();
                    if (ChipButton(chip, state.SelectedChip == chip, ctx)) state.SelectedChip = chip;
                }
                ImGui.SameLine();
            }
        }

        // The tile-size slider sits at the right end of the row.
        float x = ImGui.GetWindowWidth() - sliderW - ImGui.GetStyle().WindowPadding.X;
        if (ImGui.GetCursorPosX() < x) ImGui.SetCursorPosX(x);
        ImGui.SetNextItemWidth(sliderW);
        float w = state.TileWidth;
        if (ImGui.SliderFloat("##tile_size", ref w, PickerState.MinTileWidth, PickerState.MaxTileWidth, "") && Math.Abs(w - state.TileWidth) >= 0.5f)
        {
            state.TileWidth = w;
            state.TileWidthChanged = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Tile size");
        ImGui.Separator();
    }

    private static bool ChipButton(string label, bool active, IPickerRenderContext ctx)
    {
        if (active) ImGui.PushStyleColor(ImGuiCol.Button, ctx.Theme.SelectionAccent with { W = 0.55f });
        bool clicked = ImGui.SmallButton(PickerText.Drawable(label) + "##chip_" + label);
        if (active) ImGui.PopStyleColor();
        return clicked;
    }

    private static void DrawTiles(PickerState state, IPickerRenderContext ctx, PickerGroupSidebar groups)
    {
        bool searching = Searching(state);
        float tileW = state.TileWidth;
        float thumbH = MathF.Round(tileW * ThumbAspect);
        float lineH = ImGui.GetTextLineHeight();
        float tileH = thumbH + lineH * (searching ? 3f : 2f) + 10f;
        float availW = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)MathF.Floor((availW + TilePadding) / (tileW + TilePadding)));
        state.GridColumns = cols;
        state.VisibleFiltered.Clear();

        string chip = state.SelectedChip;
        bool chipFilter = chip.Length > 0 && !searching && state.SelectedCategory.Length > 0 && state.SelectedCategory != groups.RecentGroup;

        int col = 0;
        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re = state.Filtered[i];
            if (!CategorySidebar.Matches(re, state.SelectedCategory, groups, searching)) continue;
            if (chipFilter && CategorySidebar.ChipOf(re.Entry.Category) != chip) continue;

            state.VisibleFiltered.Add(i);
            DrawTile(state, ctx, re, i, tileW, thumbH, tileH, searching);

            if (++col < cols) ImGui.SameLine(0f, TilePadding);
            else { col = 0; ImGui.Dummy(new Vector2(0f, 2f)); }
        }

        if (state.VisibleFiltered.Count == 0)
            ImGui.TextColored(ctx.Theme.TextMuted, "Nothing here.");
    }

    private static void DrawTile(PickerState state, IPickerRenderContext ctx, RankedEntry re, int i,
                                 float tileW, float thumbH, float tileH, bool showGroup)
    {
        var entry = re.Entry;
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushID(i);

        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(tileW, tileH);
        bool clicked = ImGui.InvisibleButton("##tile", size);
        bool hovered = ImGui.IsItemHovered();
        bool selected = state.SelectedFilteredIndices.Contains(i);
        bool focused = state.KeyboardFocusIndex == i;
        float alpha = entry.IsEnabled ? 1f : 0.45f;

        if (selected || focused)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.3f }), 4f);
        else if (hovered)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.12f }), 4f);

        // ── the picture: the thumbnail, else today's DIS glyph on a dashed tile ──
        var tMin = pos + new Vector2(3f, 3f);
        var tMax = pos + new Vector2(tileW - 3f, thumbH);
        var tSize = tMax - tMin;
        if (entry.ImageKey is { Length: > 0 } key && state.Images.TryGet(key, out var img))
        {
            float scale = Math.Min(tSize.X / img.Width, tSize.Y / img.Height);
            var d = new Vector2(img.Width * scale, img.Height * scale);
            var p0 = tMin + (tSize - d) * 0.5f;
            dl.AddImage(img.Texture, p0, p0 + d, Vector2.Zero, Vector2.One, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
        }
        else
        {
            dl.AddRectFilled(tMin, tMax, ImGui.GetColorU32(new Vector4(0.2f, 0.24f, 0.3f, 0.5f * alpha)), 3f);
            DashedRect(dl, tMin, tMax, ImGui.GetColorU32(ctx.Theme.TextMuted with { W = 0.5f * alpha }));
            if (entry.IconKey is { Length: > 0 } iconKey && ctx.Icons.TryGet(iconKey, out var icon))
            {
                float g = Math.Min(48f, Math.Min(tSize.X, tSize.Y) * 0.6f);
                var c = tMin + tSize * 0.5f;
                dl.AddImage(icon.TextureId, c - new Vector2(g, g) * 0.5f, c + new Vector2(g, g) * 0.5f, icon.Uv0, icon.Uv1,
                            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha)));
            }
        }

        // ── name, the one line, and (while searching) the group ──
        float lineH = ImGui.GetTextLineHeight();
        float y = pos.Y + thumbH + 3f;
        var textMin = new Vector2(pos.X + 4f, y);
        dl.PushClipRect(textMin, new Vector2(pos.X + tileW - 4f, pos.Y + tileH), true);
        var def = ctx.Theme.TextDefault with { W = alpha };
        var muted = ctx.Theme.TextMuted;
        dl.AddText(textMin, ImGui.GetColorU32(def), PickerText.Drawable(entry.Name ?? "(null)"));
        if (entry.Description is { Length: > 0 } line)
            dl.AddText(new Vector2(textMin.X, y + lineH), ImGui.GetColorU32(muted), PickerText.Drawable(line));
        if (showGroup && CategorySidebar.RootOf(entry.Category) is { Length: > 0 } grp)
            dl.AddText(new Vector2(textMin.X, y + lineH * 2f), ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.8f }), PickerText.Drawable(grp));
        dl.PopClipRect();

        if (re.IsFavorite)
            dl.AddText(new Vector2(pos.X + tileW - 16f, pos.Y + 4f), ImGui.GetColorU32(new Vector4(1f, 0.82f, 0.2f, 1f)), "*");

        if (clicked)
        {
            state.SelectedFilteredIndices.Clear();
            state.SelectedFilteredIndices.Add(i);
            state.KeyboardFocusIndex = i;
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            state.Confirmed = true;
        // G10: F stars the tile under the mouse (not while typing in the search box).
        if (hovered && !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F, false))
            state.Favorites.Toggle(state.ContextKey, entry.Id);
        if (ImGui.BeginPopupContextItem("##tile_ctx"))
        {
            if (ImGui.MenuItem(re.IsFavorite ? "Unfavorite" : "Favorite"))
                state.Favorites.Toggle(state.ContextKey, entry.Id);
            ImGui.EndPopup();
        }
        if (focused && state.ScrollToFocus)
        {
            ImGui.SetScrollHereY(0.5f);
            state.ScrollToFocus = false;
        }

        ImGui.PopID();
    }

    private static void DashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint col)
    {
        const float dash = 6f, gap = 4f;
        for (float x = min.X; x < max.X; x += dash + gap)
        {
            float x2 = Math.Min(x + dash, max.X);
            dl.AddLine(new Vector2(x, min.Y), new Vector2(x2, min.Y), col);
            dl.AddLine(new Vector2(x, max.Y), new Vector2(x2, max.Y), col);
        }
        for (float y = min.Y; y < max.Y; y += dash + gap)
        {
            float y2 = Math.Min(y + dash, max.Y);
            dl.AddLine(new Vector2(min.X, y), new Vector2(min.X, y2), col);
            dl.AddLine(new Vector2(max.X, y), new Vector2(max.X, y2), col);
        }
    }

    // ── grid ─────────────────────────────────────────────────────────────────

    private static void DrawGrid(PickerState state, IPickerRenderContext ctx)
    {
        int col = 0;
        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re      = state.Filtered[i];
            bool sel    = state.SelectedFilteredIndices.Contains(i);
            bool focus  = state.KeyboardFocusIndex == i;

            ImGui.PushID(i);

            // Tile background.
            var pos  = ImGui.GetCursorScreenPos();
            var size = new Vector2(TileWidth, TileHeight);

            if (sel || focus)
            {
                var dl = ImGui.GetWindowDrawList();
                uint bgColor = ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.3f });
                dl.AddRectFilled(pos, pos + size, bgColor, 4f);
            }

            // Thumbnail (use colored rect if no texture).
            var thumbPos  = pos;
            var thumbSize = new Vector2(TileWidth, TileHeight - 24f);
            // ⭐ CE-4168 (AQ108 D26 P5) — the entry's lazily loaded picture, fitted into the tile keeping its aspect.
            if (re.Entry.ImageKey is { Length: > 0 } imageKey && state.Images.TryGet(imageKey, out var img))
            {
                float scale = Math.Min(thumbSize.X / img.Width, thumbSize.Y / img.Height);
                var drawSize = new Vector2(img.Width * scale, img.Height * scale);
                ImGui.SetCursorScreenPos(thumbPos + (thumbSize - drawSize) * 0.5f);
                ImGui.Image(img.Texture, drawSize);
            }
            else if (re.Entry.IconTextureId is IntPtr texId)
            {
                ImGui.SetCursorScreenPos(thumbPos);
                ImGui.Image(texId, thumbSize);
            }
            else
            {
                var dl = ImGui.GetWindowDrawList();
                uint placeholderColor = ImGui.GetColorU32(new Vector4(0.25f, 0.3f, 0.4f, 1f));
                dl.AddRectFilled(thumbPos, thumbPos + thumbSize, placeholderColor, 2f);
            }

            // Name label below thumbnail.
            ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + thumbSize.Y + 2f));
            ImGui.SetNextItemWidth(TileWidth);
            ImGui.TextColored(ctx.Theme.TextDefault, re.Entry.Name);

            // Invisible selectable over the full tile for interaction.
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton($"##tile_{i}", size))
            {
                state.SelectedFilteredIndices.Clear();
                state.SelectedFilteredIndices.Add(i);
                state.KeyboardFocusIndex = i;
            }

            // Architecturally critical: explicitly capture the double-click 
            // to confirm selection and close the picker.
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                state.Confirmed = true;
            }

            ImGui.PopID();

            col++;
            if (col < Columns)
                ImGui.SameLine(0f, TilePadding);
            else
            {
                col = 0;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + TilePadding);
            }
        }
    }

    // ── detail strip ─────────────────────────────────────────────────────────

    private static void DrawDetailStrip(PickerState state, IPickerRenderContext ctx)
    {
        int focused = state.KeyboardFocusIndex;
        PickerEntry? entry = (focused >= 0 && focused < state.Filtered.Count)
            ? state.Filtered[focused].Entry
            : null;

        if (ImGui.BeginChild("##picker_grid_detail", new Vector2(0f, PreviewH), ImGuiChildFlags.None))
        {
            try
            {
                if (entry is not null)
                {
                    ImGui.TextColored(ctx.Theme.TextDefault, entry.Name);
                    if (entry.Category is { Length: > 0 })
                        ImGui.TextColored(ctx.Theme.TextMuted, entry.Category);
                    if (entry.Description is { Length: > 0 })
                        ImGui.TextWrapped(entry.Description);
                }
                else
                {
                    ImGui.TextColored(ctx.Theme.TextMuted, "(no selection)");
                }
            }
            finally
            {
                ImGui.EndChild();
            }
        }
        else
        {
            ImGui.EndChild();
        }
    }
}
