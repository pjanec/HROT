using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Tree layout: hierarchical expand/collapse list built from each entry's
/// <see cref="PickerEntry.Category"/> path or from an explicit
/// <see cref="CategoryNode"/> tree.
/// Arrow ← / → collapse/expand nodes; ↑ / ↓ navigate.
/// </summary>
internal static class TreeLayout
{
    /// <summary>Render the tree view.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx,
                            CategoryNode? explicitRoot = null)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("##picker_tree", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            if (explicitRoot is not null)
                DrawExplicitTree(state, ctx, explicitRoot, 0);
            else
                DrawImplicitTree(state, ctx);
        }
        ImGui.EndChild();
    }

    // ── implicit tree (built from Category strings) ───────────────────────────

    private static void DrawImplicitTree(PickerState state, IPickerRenderContext ctx)
    {
        // Group entries by top-level category segment.
        var byRoot = new SortedDictionary<string, List<(int idx, RankedEntry re)>>(StringComparer.OrdinalIgnoreCase);
        var uncategorized = new List<(int idx, RankedEntry re)>();

        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re = state.Filtered[i];
            if (re.Entry.Category is { Length: > 0 } cat)
            {
                string root = cat.Contains('/') ? cat[..cat.IndexOf('/')] : cat;
                if (!byRoot.TryGetValue(root, out var list))
                    byRoot[root] = list = [];
                list.Add((i, re));
            }
            else
            {
                uncategorized.Add((i, re));
            }
        }

        bool isSearching = !string.IsNullOrEmpty(state.SearchText);
        var flags = isSearching ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        foreach (var (root, items) in byRoot)
        {
            if (ImGui.TreeNodeEx(root, flags))
            {
                DrawGroupedItems(state, ctx, root, items, isSearching);
                ImGui.TreePop();
            }
        }

        foreach (var (idx, re) in uncategorized)
            DrawLeafItem(state, ctx, idx, re);
    }

    private static void DrawGroupedItems(PickerState state, IPickerRenderContext ctx,
        string parentCategoryPath,
        List<(int idx, RankedEntry re)> items,
        bool isSearching)
    {
        var bySubRoot = new SortedDictionary<string, List<(int idx, RankedEntry re)>>(StringComparer.OrdinalIgnoreCase);
        var leaves = new List<(int idx, RankedEntry re)>();

        string prefix = parentCategoryPath + "/";

        foreach (var item in items)
        {
            string? cat = item.re.Entry.Category;
            
            // If the category path extends beyond the current parent, group it by the next segment.
            if (cat != null && cat.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = cat[prefix.Length..];
                string subRoot = remainder.Contains('/') ? remainder[..remainder.IndexOf('/')] : remainder;

                if (!bySubRoot.TryGetValue(subRoot, out var list))
                {
                    list = new List<(int idx, RankedEntry re)>();
                    bySubRoot[subRoot] = list;
                }
                list.Add(item);
            }
            else
            {
                // The item belongs exactly to the current parent category.
                leaves.Add(item);
            }
        }

        var flags = isSearching ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        // 1. Draw sub-category folders recursively
        foreach (var (subRoot, subItems) in bySubRoot)
        {
            if (ImGui.TreeNodeEx(subRoot, flags))
            {
                DrawGroupedItems(state, ctx, prefix + subRoot, subItems, isSearching);
                ImGui.TreePop();
            }
        }

        // 2. Draw leaf items at this depth
        foreach (var (idx, re) in leaves)
        {
            DrawLeafItem(state, ctx, idx, re);
        }
    }

    // ── explicit tree ─────────────────────────────────────────────────────────

    private static void DrawExplicitTree(PickerState state, IPickerRenderContext ctx,
                                         CategoryNode node, int depth)
    {
        if (depth == 0)
        {
            // Root: render children directly (don't show the root node itself).
            foreach (var child in node.Children)
                DrawExplicitTree(state, ctx, child, 1);
            return;
        }

        bool open = ImGui.TreeNode(node.Name);
        if (open)
        {
            foreach (var child in node.Children)
                DrawExplicitTree(state, ctx, child, depth + 1);
            ImGui.TreePop();
        }
    }

    // ── leaf item ─────────────────────────────────────────────────────────────

    private static void DrawLeafItem(PickerState state, IPickerRenderContext ctx,
                                     int filteredIdx, RankedEntry re)
    {
        bool sel    = state.SelectedFilteredIndices.Contains(filteredIdx);
        bool focus  = state.KeyboardFocusIndex == filteredIdx;

        ImGui.PushID(filteredIdx);

        // Architecturally critical: Supply AllowDoubleClick so ImGui captures the double-click event
        // rather than treating it as two distinct single clicks that toggle state.
        if (ImGui.Selectable(re.Entry.Name, sel || focus, ImGuiSelectableFlags.AllowDoubleClick))
        {
            state.SelectedFilteredIndices.Clear();
            state.SelectedFilteredIndices.Add(filteredIdx);
            state.KeyboardFocusIndex = filteredIdx;
        }

        // Evaluate the double-click immediately after the selectable
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.Confirmed = true;
        }

        ImGui.PopID();
    }
}
