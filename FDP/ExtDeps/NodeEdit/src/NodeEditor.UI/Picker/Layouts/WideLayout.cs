using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Wide two-column layout: category tree sidebar (240 px) + item list with inline description.
/// Used for the node-search picker on dropped wire.
/// </summary>
internal static class WideLayout
{
    private const float SidebarWidth = 240f;

    /// <summary>Render the wide layout with category sidebar and item list.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();

        // Category sidebar.
        if (ImGui.BeginChild("##picker_cats", new Vector2(SidebarWidth, height),
                ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar))
        {
            try
            {
                DrawCategorySidebar(state, ctx);
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

        ImGui.SameLine(0f, 4f);

        // Item list.
        if (ImGui.BeginChild("##picker_wide_list", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            try
            {
                DrawWideItems(state, ctx);
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

    // ── sidebar ───────────────────────────────────────────────────────────────

    private static void DrawCategorySidebar(PickerState state, IPickerRenderContext ctx)
    {
        // Collect unique top-level categories from filtered entries.
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var re in state.Filtered)
        {
            if (re.Entry.Category is { Length: > 0 } cat)
            {
                int slash = cat.IndexOf('/');
                roots.Add(slash < 0 ? cat : cat[..slash]);
            }
        }

        // "All" option.
        bool allSel = string.IsNullOrEmpty(state.SelectedCategory);
        if (allSel) ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.SelectionAccent);
        if (ImGui.Selectable("All", allSel))
            state.SelectedCategory = "";
        if (allSel) ImGui.PopStyleColor();

        foreach (var root in roots.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
        {
            bool sel = state.SelectedCategory == root;
            if (sel) ImGui.PushStyleColor(ImGuiCol.Text, ctx.Theme.SelectionAccent);
            if (ImGui.Selectable(root, sel))
                state.SelectedCategory = root;
            if (sel) ImGui.PopStyleColor();
        }
    }

    // ── item list ─────────────────────────────────────────────────────────────

    private static void DrawWideItems(PickerState state, IPickerRenderContext ctx)
    {
        string catFilter = state.SelectedCategory;

        int visibleIdx = 0;
        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re = state.Filtered[i];

            // Filter by selected category (top-level segment).
            if (!string.IsNullOrEmpty(catFilter))
            {
                string? cat = re.Entry.Category;
                if (cat is null) continue;
                string topLevel = cat.Contains('/') ? cat[..cat.IndexOf('/')] : cat;
                if (!topLevel.Equals(catFilter, StringComparison.Ordinal)) continue;
            }

            bool selected = state.SelectedFilteredIndices.Contains(i);
            bool focused  = state.KeyboardFocusIndex == i;

            ImGui.PushID(visibleIdx++);

            // Capture SCREEN position before the selectable — used for draw-list overlay.
            var startScreenPos = ImGui.GetCursorScreenPos();

            // 1. Invisible selectable for hit-testing; spans full width at 36px height.
            //    Advances the layout cursor naturally — no SetCursorPos needed.
            if (ImGui.Selectable($"##wide_row_{i}", selected || focused,
                    ImGuiSelectableFlags.AllowOverlap | ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0f, 36f)))
            {
                state.SelectedFilteredIndices.Clear();
                state.SelectedFilteredIndices.Add(i);
                state.KeyboardFocusIndex = i;
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                state.Confirmed = true;
            }

            // 2. Draw 2-line text overlay via DrawList (screen coords) — avoids SetCursorPos entirely.
            //    CRITICAL: AddText bypasses native 'vsnprintf' so strings containing '%' don't crash.
            var dl = ImGui.GetWindowDrawList();
            float textX = startScreenPos.X + 4f;
            float textY = startScreenPos.Y + 2f;

            // Inline row icon (24px, vertically centered in the 36px row) when the
            // entry's IconKey resolves via the provider. Mirrors PickerItemListHelper.
            const float IconSize = 24f;
            if (re.Entry.IconKey is { Length: > 0 } iconKey &&
                ctx.Icons.TryGet(iconKey, out var rowIcon))
            {
                float iconY = startScreenPos.Y + (36f - IconSize) * 0.5f;
                dl.AddImage(rowIcon.TextureId,
                    new Vector2(textX, iconY),
                    new Vector2(textX + IconSize, iconY + IconSize),
                    rowIcon.Uv0, rowIcon.Uv1);
                textX += IconSize + 6f;
            }

            uint defaultCol = ImGui.GetColorU32(ctx.Theme.TextDefault);
            uint mutedCol   = ImGui.GetColorU32(ctx.Theme.TextMuted);

            dl.AddText(new Vector2(textX, textY), defaultCol, re.Entry.Name ?? "(null)");
            if (re.Entry.Description is { Length: > 0 } desc)
            {
                float lineH = ImGui.GetTextLineHeight();
                dl.AddText(new Vector2(textX, textY + lineH), mutedCol, desc);
            }

            ImGui.PopID();
        }
    }
}
