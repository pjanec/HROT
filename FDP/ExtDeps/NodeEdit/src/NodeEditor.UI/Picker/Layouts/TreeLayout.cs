using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Tree layout: hierarchical expand/collapse list built from each entry's
/// <see cref="PickerEntry.Category"/> path or from an explicit
/// <see cref="CategoryNode"/> tree.
/// Arrow ← / → collapse/expand nodes; ↑ / ↓ navigate visual-order rows.
/// </summary>
internal static class TreeLayout
{
    private const float RowHeight = 22f;
    private static readonly Vector2 IconSize = new(16f, 16f);

    /// <summary>Render the tree view.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx,
                            CategoryNode? explicitRoot = null)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("##picker_tree", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            try
            {
                // Rebuild visual rows from scratch each frame.
                state.VisualRows.Clear();

                if (explicitRoot is not null)
                {
                    foreach (var child in explicitRoot.Children)
                        DrawExplicitFolderNode(state, ctx, child, "", 0);
                }
                else
                {
                    DrawImplicitTree(state, ctx);
                }

                // Clamp tree focus after rebuild (row count may have changed).
                if (state.VisualRows.Count > 0)
                    state.TreeFocusRow = Math.Clamp(state.TreeFocusRow, 0, state.VisualRows.Count - 1);
                else
                    state.TreeFocusRow = 0;
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

    // ── implicit tree (built from Category strings) ───────────────────────────

    private static void DrawImplicitTree(PickerState state, IPickerRenderContext ctx)
    {
        // Build the pure tree model from the filtered list.
        var items = new List<(int FilteredIndex, string? Category, string Name)>(state.Filtered.Count);
        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re = state.Filtered[i];
            items.Add((i, re.Entry.Category, re.Entry.Name));
        }

        var root = PickerTreeBuilder.Build(items);

        bool isSearching = !string.IsNullOrEmpty(state.SearchText);

        // REVERT BATCH-45: default-open ONLY while searching so matches are visible.
        // For non-search, drive each folder's open state from ExpandedFolders.
        var flags = isSearching ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        // Render folders.
        foreach (var folder in root.Folders)
            DrawImplicitFolderNode(state, ctx, folder, "", 0, flags, isSearching);

        // Render uncategorized leaves.
        foreach (var leaf in root.Leaves)
            DrawLeafItem(state, ctx, leaf.FilteredIndex, state.Filtered[leaf.FilteredIndex], 0);
    }

    private static void DrawImplicitFolderNode(PickerState state, IPickerRenderContext ctx,
        PickerTreeBuilder.Node folder, string parentPath, int depth,
        ImGuiTreeNodeFlags baseFlags, bool isSearching)
    {
        // Compute stable full-path for collapse state + IDs.
        string fullPath = string.IsNullOrEmpty(parentPath)
            ? folder.Name
            : parentPath + "/" + folder.Name;

        int visualRowIndex = state.VisualRows.Count;
        bool isFocused = state.TreeFocusRow == visualRowIndex;

        // Determine tree flags.
        var treeFlags = baseFlags | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (isFocused)
            treeFlags |= ImGuiTreeNodeFlags.Selected;

        // Draw folder icon if available. Pick closed/open glyph from the node's
        // persisted open state (1-frame lag is acceptable); the icon must be
        // drawn BEFORE TreeNodeEx.
        uint nodeId = ImGui.GetID(fullPath);
        int defaultOpenInt = (baseFlags & ImGuiTreeNodeFlags.DefaultOpen) != 0 ? 1 : 0;
        bool wasOpen = ImGui.GetStateStorage().GetInt(nodeId, defaultOpenInt) != 0;

        string folderKey = wasOpen ? "folder_open" : "folder";
        bool hasIcon = ctx.Icons.TryGet(folderKey, out var folderIcon)
                       || ctx.Icons.TryGet("folder", out folderIcon);
        if (hasIcon)
        {
            ImGui.Image(folderIcon.TextureId, IconSize, folderIcon.Uv0, folderIcon.Uv1);
            ImGui.SameLine();
        }

        // Drive open state from ExpandedFolders (non-search) or force open (search).
        if (!isSearching)
            ImGui.SetNextItemOpen(state.ExpandedFolders.Contains(fullPath), ImGuiCond.Always);

        bool open = ImGui.TreeNodeEx(folder.Name, treeFlags);

        // Record this folder as a visual row.
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: true, FolderPath: fullPath, FilteredIndex: -1, Depth: depth));

        // Sync mouse-driven arrow toggle with ExpandedFolders (mirrors SaveAsBrowserDialog pattern).
        if (open)
            state.ExpandedFolders.Add(fullPath);
        else if (folder.Folders.Count > 0 || folder.Leaves.Count > 0)
            state.ExpandedFolders.Remove(fullPath);

        // Scroll focused folder into view.
        if (isFocused)
            ImGui.SetScrollHereY(0.5f);

        if (open)
        {
            // Recurse sub-folders and leaves only when expanded.
            foreach (var subFolder in folder.Folders)
                DrawImplicitFolderNode(state, ctx, subFolder, fullPath, depth + 1, baseFlags, isSearching);

            foreach (var leaf in folder.Leaves)
                DrawLeafItem(state, ctx, leaf.FilteredIndex, state.Filtered[leaf.FilteredIndex], depth + 1);

            ImGui.TreePop();
        }
    }

    // ── explicit tree ─────────────────────────────────────────────────────────

    private static void DrawExplicitFolderNode(PickerState state, IPickerRenderContext ctx,
                                               CategoryNode node, string parentPath, int depth)
    {
        string fullPath = string.IsNullOrEmpty(parentPath)
            ? node.Name
            : parentPath + "/" + node.Name;

        int visualRowIndex = state.VisualRows.Count;
        bool isFocused = state.TreeFocusRow == visualRowIndex;

        var treeFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth;
        if (isFocused)
            treeFlags |= ImGuiTreeNodeFlags.Selected;
        if (node.Children.Count == 0)
            treeFlags |= ImGuiTreeNodeFlags.Leaf;

        // Drive open state from ExpandedFolders (explicit tree has no search auto-expand).
        ImGui.SetNextItemOpen(state.ExpandedFolders.Contains(fullPath), ImGuiCond.Always);

        bool open = ImGui.TreeNodeEx(node.Name, treeFlags);

        // Record this folder as a visual row.
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: true, FolderPath: fullPath, FilteredIndex: -1, Depth: depth));

        // Sync mouse arrow toggle with ExpandedFolders.
        if (open)
            state.ExpandedFolders.Add(fullPath);
        else if (node.Children.Count > 0)
            state.ExpandedFolders.Remove(fullPath);

        // Scroll focused folder into view.
        if (isFocused)
            ImGui.SetScrollHereY(0.5f);

        if (open)
        {
            foreach (var child in node.Children)
                DrawExplicitFolderNode(state, ctx, child, fullPath, depth + 1);

            ImGui.TreePop();
        }
    }

    // ── leaf item ─────────────────────────────────────────────────────────────

    private static void DrawLeafItem(PickerState state, IPickerRenderContext ctx,
                                     int filteredIdx, RankedEntry re, int depth = 0)
    {
        bool sel    = state.SelectedFilteredIndices.Contains(filteredIdx);
        bool focus  = state.KeyboardFocusIndex == filteredIdx;
        bool isSearching = !string.IsNullOrEmpty(state.SearchText);

        // Record this leaf as a visual row (before rendering, so the focus index
        // computed in HandleKeyboardNavigation maps to the correct row).
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: false, FolderPath: "", FilteredIndex: filteredIdx, Depth: depth));

        ImGui.PushID(filteredIdx);

        var pos = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(availWidth, RowHeight - 4f);

        // Invisible selectable captures hit-tests without fighting visual layout
        // (mirrors PickerItemListHelper.DrawRow technique).
        bool clicked = ImGui.Selectable("##sel", false,
            ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
            size);

        bool actualMouseClicked = clicked && ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        var dl = ImGui.GetWindowDrawList();

        // Highlight background (selected or focused).
        if (sel || focus)
        {
            uint bgColor = sel
                ? ImGui.GetColorU32(ctx.Theme.SelectionAccent with { W = 0.35f })
                : ImGui.GetColorU32(ctx.Theme.TextDefault with { W = 0.2f });
            dl.AddRectFilled(pos, pos + size, bgColor, 2f);
        }

        // Keyboard focus indicator.
        if (focus)
            dl.AddRect(pos, pos + size, ImGui.GetColorU32(ctx.Theme.TextDefault with { W = 0.5f }), 2f);

        float textX = pos.X + 4f;
        float textY = pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f;

        // Draw type icon if available.
        float iconPadLeft = 0f;
        if (re.Entry.IconKey is { Length: > 0 } iconKey &&
            ctx.Icons.TryGet(iconKey, out var leafIcon))
        {
            float iconY = pos.Y + (size.Y - IconSize.Y) * 0.5f;
            dl.AddImage(leafIcon.TextureId,
                new Vector2(textX, iconY),
                new Vector2(textX + IconSize.X, iconY + IconSize.Y),
                leafIcon.Uv0, leafIcon.Uv1);
            textX += IconSize.X + 4f;
            iconPadLeft = IconSize.X + 4f;
        }
        else
        {
            // Left-pad when no icon so text aligns with icon-bearing leaves.
            textX += IconSize.X + 4f;
        }

        // Render name with match highlights.
        uint defaultTextColor = sel
            ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f))
            : ImGui.GetColorU32(ctx.Theme.TextDefault);
        uint highlightColor = sel
            ? ImGui.GetColorU32(new Vector4(1f, 1f, 0.4f, 1f))
            : ImGui.GetColorU32(ctx.Theme.SelectionAccent);

        var runs = PickerTextHighlighter.SplitRuns(re.Entry.Name, re.MatchPositions);
        foreach (var run in runs)
        {
            uint color = run.IsMatch ? highlightColor : defaultTextColor;
            dl.AddText(new Vector2(textX, textY), color, run.Text);
            textX += ImGui.CalcTextSize(run.Text).X;
        }

        // Handle mouse click for selection.
        if (actualMouseClicked)
        {
            state.SelectedFilteredIndices.Clear();
            state.SelectedFilteredIndices.Add(filteredIdx);
            state.KeyboardFocusIndex = filteredIdx;
        }

        // Double-click confirms.
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.Confirmed = true;
        }

        // Scroll focused leaf into view.
        if (focus)
            ImGui.SetScrollHereY(0.5f);

        ImGui.PopID();
    }
}
