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
        float listH  = ImGui.GetContentRegionAvail().Y - PreviewH - ImGui.GetFrameHeightWithSpacing() - 8f;
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
            if (re.Entry.IconTextureId is IntPtr texId)
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
