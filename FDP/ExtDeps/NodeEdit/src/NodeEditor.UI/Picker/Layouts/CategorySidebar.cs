using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// ⭐ CE-4142 GL3 — THE category sidebar of the picker, shared by the Wide layout (alphabetical roots, "All") and the Grid layout when
/// its request sets <see cref="PickerRequest.GroupSidebar"/> (the request's own group ORDER, a count per group, empty groups faded,
/// and the virtual Recent &amp; favourites group first). One drawing, one filter rule: <see cref="Matches"/> is what both layouts apply to
/// a row, so a sidebar click can never filter the Wide list one way and the thumbnails another.
/// 📄 docs/DESIGN_Add_Entity_Picker.md §2g (the sidebar moved out of <c>WideLayout</c>, not copied).
/// </summary>
internal static class CategorySidebar
{
    /// <summary>The top-level segment of a <c>"Group/Chip"</c> category; empty for none.</summary>
    public static string RootOf(string? category)
    {
        if (string.IsNullOrEmpty(category)) return "";
        int slash = category.IndexOf('/');
        return slash < 0 ? category : category[..slash];
    }

    /// <summary>The second segment of a <c>"Group/Chip"</c> category; null when there is none.</summary>
    public static string? ChipOf(string? category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        int slash = category.IndexOf('/');
        return slash < 0 || slash == category.Length - 1 ? null : category[(slash + 1)..];
    }

    /// <summary>
    /// True when <paramref name="row"/> shows under the sidebar selection <paramref name="selected"/> (empty = All). With
    /// <paramref name="groups"/> the virtual Recent group is the rows that are recent or starred. ⭐ G7: while the user is SEARCHING
    /// (<paramref name="searching"/>) the group filter is off — a search ranges over every group.
    /// </summary>
    public static bool Matches(RankedEntry row, string selected, PickerGroupSidebar? groups, bool searching)
    {
        if (string.IsNullOrEmpty(selected) || (groups is not null && searching)) return true;
        if (groups?.RecentGroup is { } recent && selected == recent) return row.IsRecent || row.IsFavorite;
        return RootOf(row.Entry.Category).Equals(selected, StringComparison.Ordinal);
    }

    /// <summary>The rows of the group sidebar: (name, count) in the request's order, the Recent group's count being its recent/starred rows.</summary>
    public static List<(string Name, int Count)> GroupCounts(PickerState state, PickerGroupSidebar groups)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int recent = 0;
        foreach (var re in state.Filtered)
        {
            string root = RootOf(re.Entry.Category);
            counts[root] = counts.GetValueOrDefault(root) + 1;
            if (re.IsRecent || re.IsFavorite) recent++;
        }

        var rows = new List<(string, int)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in groups.Order)
        {
            if (!seen.Add(name)) continue;
            rows.Add((name, groups.RecentGroup == name ? recent : counts.GetValueOrDefault(name)));
        }
        // A group the entries use but the order does not list still shows (after the listed ones): never a hidden bucket.
        foreach (var (name, n) in counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            if (name.Length > 0 && seen.Add(name)) rows.Add((name, n));
        return rows;
    }

    /// <summary>The distinct chips of the rows in group <paramref name="group"/>, ordinal order; chips are the second category segment.</summary>
    public static List<string> ChipsOf(PickerState state, string group, PickerGroupSidebar? groups)
    {
        var chips = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var re in state.Filtered)
            if (RootOf(re.Entry.Category).Equals(group, StringComparison.Ordinal) && ChipOf(re.Entry.Category) is { } chip)
                chips.Add(chip);
        return chips.ToList();
    }

    /// <summary>Draws the sidebar into the current child window. <paramref name="groups"/> null ⇒ the Wide layout's classic sidebar.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx, PickerGroupSidebar? groups)
    {
        if (groups is null) { DrawClassic(state, ctx); return; }

        bool allSel = string.IsNullOrEmpty(state.SelectedCategory);
        if (SelectableRow("All", state.Filtered.Count, allSel, faded: false, ctx, "##grp_all"))
        {
            state.SelectedCategory = "";
            state.SelectedChip = "";
        }

        foreach (var (name, count) in GroupCounts(state, groups))
        {
            bool sel = state.SelectedCategory == name;
            if (SelectableRow(name, count, sel, faded: count == 0, ctx, "##grp_" + name, IconOf(groups, name, out bool dim), dim))
            {
                state.SelectedCategory = name;
                state.SelectedChip = "";
            }
        }
    }

    private static NodeEditor.Core.Widgets.IconGlyph? IconOf(PickerGroupSidebar groups, string name, out bool dim)
    {
        dim = groups.DimmedIcons?.Contains(name) == true;
        return groups.Icons is { } map && map.TryGetValue(name, out var g) ? g : null;
    }

    private static bool SelectableRow(string name, int count, bool selected, bool faded, IPickerRenderContext ctx, string id,
                                      NodeEditor.Core.Widgets.IconGlyph? icon = null, bool dim = false)
    {
        var accent = ctx.Theme.SelectionAccent;
        var muted = ctx.Theme.TextMuted;
        ImGui.PushID(id);
        if (selected) ImGui.PushStyleColor(ImGuiCol.Text, accent);
        else if (faded) ImGui.PushStyleColor(ImGuiCol.Text, muted with { W = 0.45f });
        // ⭐ a group with an icon: the glyph in its own colour at the row's left, the label after it
        string label = PickerText.Drawable(name);
        bool clicked = icon is null ? ImGui.Selectable(label, selected) : ImGui.Selectable("##row", selected);
        if (icon is { } ic)
        {
            var rmin = ImGui.GetItemRectMin();
            float lh = ImGui.GetTextLineHeight();
            float y = rmin.Y + (ImGui.GetItemRectMax().Y - rmin.Y - lh) * 0.5f;
            var dl = ImGui.GetWindowDrawList();
            dl.AddText(new Vector2(rmin.X + 4f, y), ImGui.GetColorU32(ic.Color with { W = dim ? 0.5f : 1f }), ic.Glyph);
            dl.AddText(new Vector2(rmin.X + 4f + lh * 1.5f, y), ImGui.GetColorU32(selected ? accent : faded ? muted with { W = 0.45f } : dim ? muted : ctx.Theme.TextDefault), label);
        }
        if (selected || faded) ImGui.PopStyleColor();

        // The count sits at the row's right edge, muted.
        string n = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var w = ImGui.CalcTextSize(n).X;
        ImGui.GetWindowDrawList().AddText(new Vector2(max.X - w - 6f, min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(muted with { W = faded ? 0.35f : 0.8f }), n);
        ImGui.PopID();
        return clicked;
    }

    // ── the Wide layout's sidebar, moved here unchanged ───────────────────────

    private static void DrawClassic(PickerState state, IPickerRenderContext ctx)
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
}
