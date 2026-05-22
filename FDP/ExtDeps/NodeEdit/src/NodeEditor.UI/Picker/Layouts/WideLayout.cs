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
            DrawCategorySidebar(state, ctx);
        }
        ImGui.EndChild();

        ImGui.SameLine(0f, 4f);

        // Item list.
        if (ImGui.BeginChild("##picker_wide_list", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            DrawWideItems(state, ctx);
        }
        ImGui.EndChild();
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
            if (ImGui.Selectable(re.Entry.Name, selected || focused,
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

            // Second line: description in muted color.
            ImGui.SameLine(0f, 4f);
            ImGui.BeginGroup();
            ImGui.TextColored(ctx.Theme.TextDefault, re.Entry.Name);
            if (re.Entry.Description is { Length: > 0 } desc)
                ImGui.TextColored(ctx.Theme.TextMuted, desc);
            ImGui.EndGroup();

            ImGui.PopID();
        }
    }
}
