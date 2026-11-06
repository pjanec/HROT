using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Widgets;
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
    private const float PreviewH = 84f;
    private static readonly Vector2 PreviewIconSize = new(64f, 64f);

    /// <summary>Render the tree view.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx,
                            CategoryNode? explicitRoot = null,
                            bool foldSingleChildFolders = false, bool showPreview = false)
    {
        float height = ImGui.GetContentRegionAvail().Y - PickerLayoutMetrics.FooterReserve(state);
        if (showPreview)
            height -= PreviewH + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##picker_tree", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            try
            {
                // Rebuild visual rows from scratch each frame.
                state.VisualRows.Clear();

                // ⭐ CE-4098 (AQ108 D19) — the Recent section FIRST: what was picked last, before any folder.
                DrawRecentSection(state, ctx);

                if (explicitRoot is not null)
                {
                    foreach (var child in explicitRoot.Children)
                        DrawExplicitFolderNode(state, ctx, child, "", 0);
                }
                else
                {
                    DrawImplicitTree(state, ctx, foldSingleChildFolders);
                }

                // The initial-selection reveal is one-shot: this draw has opened its folders.
                state.RevealCategory = null;

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

        if (showPreview)
            DrawPreview(state, ctx);
    }

    // ── Recent section (CE-4098) ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ The rows of the Recent section — <see cref="PickerState.RecentFilteredIndices"/>, the model a rail asserts. An entry
    /// also sits in its folder below; the section is a shortcut, so both rows are visual rows (keyboard navigation walks them).
    /// </summary>
    internal static IReadOnlyList<int> RecentSectionRows(PickerState state) => state.RecentFilteredIndices();

    private static void DrawRecentSection(PickerState state, IPickerRenderContext ctx)
    {
        var rows = RecentSectionRows(state);
        if (rows.Count == 0) return;

        ImGui.TextColored(ctx.Theme.TextMuted, "↻ Recent");
        ImGui.PushID("##recent");   // the same entry is drawn again in its folder: separate ID scope
        foreach (int idx in rows)
            DrawLeafItem(state, ctx, idx, state.Filtered[idx], 0);
        ImGui.PopID();
        ImGui.Separator();
    }

    // ── preview pane (CE-1017) ────────────────────────────────────────────────

    /// <summary>The focused leaf: the focused tree row when it is a leaf, else the keyboard-focused entry.</summary>
    internal static int FocusedLeafIndex(PickerState state)
    {
        int row = state.TreeFocusRow;
        if (row >= 0 && row < state.VisualRows.Count && !state.VisualRows[row].IsFolder)
            return state.VisualRows[row].FilteredIndex;
        int k = state.KeyboardFocusIndex;
        return k >= 0 && k < state.Filtered.Count ? k : -1;
    }

    private static void DrawPreview(PickerState state, IPickerRenderContext ctx)
    {
        int idx = FocusedLeafIndex(state);
        PickerEntry? entry = idx >= 0 && idx < state.Filtered.Count ? state.Filtered[idx].Entry : null;

        if (ImGui.BeginChild("##picker_tree_preview", new Vector2(0f, PreviewH), ImGuiChildFlags.Borders))
        {
            try
            {
                if (entry is null)
                {
                    ImGui.TextColored(ctx.Theme.TextMuted, "(no selection)");
                    return;
                }

                if (entry.IconKey is { Length: > 0 } key && ctx.Icons.TryGet(key, out var icon))
                {
                    ImGui.Image(icon.TextureId, PreviewIconSize, icon.Uv0, icon.Uv1);
                    ImGui.SameLine();
                }

                ImGui.BeginGroup();
                ImGui.TextColored(ctx.Theme.TextDefault, entry.Name);
                if (entry.Category is { Length: > 0 } cat)
                    ImGui.TextColored(ctx.Theme.TextMuted, cat.Replace("/", PickerTreeBuilder.FoldSeparator));
                if (entry.Description is { Length: > 0 } desc)
                    ImGui.TextWrapped(desc);
                ImGui.EndGroup();
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

    private static void DrawImplicitTree(PickerState state, IPickerRenderContext ctx, bool foldSingleChildFolders)
    {
        // Build the pure tree model from the filtered list.
        var items = new List<(int FilteredIndex, string? Category, string Name)>(state.Filtered.Count);
        for (int i = 0; i < state.Filtered.Count; i++)
        {
            var re = state.Filtered[i];
            items.Add((i, re.Entry.Category, re.Entry.Name));
        }

        var root = PickerTreeBuilder.Build(items, foldSingleChildFolders);

        bool isSearching = !string.IsNullOrEmpty(state.SearchText);

        // REVERT BATCH-45: default-open ONLY while searching so matches are visible.
        // For non-search, ImGui remembers each folder's native open/closed state.
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
        // Stable full-path for collapse state + IDs: the builder's category path, which a folded row
        // ("A › B") keeps as its deepest real folder ("A/B") — so reveal and toggles still match.
        string fullPath = folder.FullPath;

        int visualRowIndex = state.VisualRows.Count;
        bool isFocused = state.TreeFocusRow == visualRowIndex;

        // ⭐ U-4 (DESIGN_UI_Conventions §2) — ONE row for every tree: chevron · icon · label (the shared TreeRow; the icon follows the folder's open state).
        //   The open state is the picker's own (PickerState.FolderOpen); default open only while searching so matches are visible.
        bool open = state.FolderOpen.TryGetValue(fullPath, out var remembered) ? remembered : isSearching;

        // Drive open state via one-shot request; otherwise remembered. (During search the default opens everything.)
        if (!isSearching && state.PendingToggleFolderPath == fullPath)
        {
            open = state.PendingToggleOpen;
            state.PendingToggleFolderPath = null;
        }
        else if (state.RevealCategory is { } reveal
                 && (reveal == fullPath || reveal.StartsWith(fullPath + "/", StringComparison.Ordinal)))
        {
            // Initial selection: open every folder on the pre-selected entry's path.
            open = true;
        }

        string folderKey = open ? "folder_open" : "folder";
        var icon = ctx.Icons.TryGet(folderKey, out var folderIcon) || ctx.Icons.TryGet("folder", out folderIcon)
            ? RowIcon.Image(folderIcon) : RowIcon.None;
        NodeEditor.Core.Widgets.TreeRow.Draw(fullPath, folder.Name, icon, depth, hasChildren: true, ref open, isFocused);
        state.FolderOpen[fullPath] = open;

        // Record this folder as a visual row.
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: true, FolderPath: fullPath, FilteredIndex: -1, Depth: depth));

        // Mouse single-click moves TreeFocusRow so selection follows the mouse (A17/A18).
        if (ImGui.IsItemClicked())
            state.TreeFocusRow = visualRowIndex;

        // Double-click toggles expand/collapse (A19).
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.PendingToggleFolderPath = fullPath;
            state.PendingToggleOpen = !open;
        }

        // Scroll focused folder into view (one-shot, gated on keyboard-nav flag — BATCH-52).
        if (isFocused && state.ScrollToFocus)
        {
            ImGui.SetScrollHereY(0.5f);
            state.ScrollToFocus = false;
        }

        if (open)
        {
            // Recurse sub-folders and leaves only when expanded.
            foreach (var subFolder in folder.Folders)
                DrawImplicitFolderNode(state, ctx, subFolder, fullPath, depth + 1, baseFlags, isSearching);

            foreach (var leaf in folder.Leaves)
                DrawLeafItem(state, ctx, leaf.FilteredIndex, state.Filtered[leaf.FilteredIndex], depth + 1);
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

        bool hasChildren = node.Children.Count > 0;
        bool open = state.FolderOpen.TryGetValue(fullPath, out var remembered) && remembered;

        // Drive open state via one-shot request; otherwise remembered.
        if (state.PendingToggleFolderPath == fullPath)
        {
            open = state.PendingToggleOpen;
            state.PendingToggleFolderPath = null;
        }

        string folderKey = open ? "folder_open" : "folder";
        var icon = ctx.Icons.TryGet(folderKey, out var folderIcon) || ctx.Icons.TryGet("folder", out folderIcon)
            ? RowIcon.Image(folderIcon) : RowIcon.None;
        NodeEditor.Core.Widgets.TreeRow.Draw(fullPath, node.Name, icon, depth, hasChildren, ref open, isFocused);
        if (hasChildren) state.FolderOpen[fullPath] = open;

        // Record this folder as a visual row.
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: true, FolderPath: fullPath, FilteredIndex: -1, Depth: depth));

        // Mouse single-click moves TreeFocusRow so selection follows the mouse (A17/A18).
        if (ImGui.IsItemClicked())
            state.TreeFocusRow = visualRowIndex;

        // Double-click toggles expand/collapse (A19).
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.PendingToggleFolderPath = fullPath;
            state.PendingToggleOpen = !open;
        }

        // Scroll focused folder into view (one-shot, gated on keyboard-nav flag — BATCH-52).
        if (isFocused && state.ScrollToFocus)
        {
            ImGui.SetScrollHereY(0.5f);
            state.ScrollToFocus = false;
        }

        if (open)
        {
            foreach (var child in node.Children)
                DrawExplicitFolderNode(state, ctx, child, fullPath, depth + 1);
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
        int visualRowIndex = state.VisualRows.Count;
        state.VisualRows.Add(new PickerState.TreeRow(
            IsFolder: false, FolderPath: "", FilteredIndex: filteredIdx, Depth: depth));

        // Initial selection: the pre-selected leaf becomes the focused ROW, so Enter confirms it rather
        // than expanding whatever folder happens to be row 0.
        if (state.FocusLeafOnDraw == filteredIdx)
        {
            state.TreeFocusRow = visualRowIndex;
            state.FocusLeafOnDraw = -1;
        }

        // ⭐ U-4 — the depth is the indent (TreeRow's rule: no TreePush any more), and the leaf keeps the chevron's slot.
        float leafIndent = depth * ImGui.GetStyle().IndentSpacing;
        if (leafIndent > 0f) ImGui.Indent(leafIndent);

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

        float textX = pos.X + ImGui.GetTreeNodeToLabelSpacing();   // ⭐ U-4: the chevron's slot — the same x a TreeRow's icon starts at
        float textY = pos.Y + (size.Y - ImGui.GetTextLineHeight()) * 0.5f;

        // Draw type icon if available.
        float iconSz = ImGui.GetFontSize();   // ⭐ U-4: the icon is the font's height, as in TreeRow
        float iconPadLeft = 0f;
        if (re.Entry.IconKey is { Length: > 0 } iconKey &&
            ctx.Icons.TryGet(iconKey, out var leafIcon))
        {
            float iconY = pos.Y + (size.Y - iconSz) * 0.5f;
            dl.AddImage(leafIcon.TextureId,
                new Vector2(textX, iconY),
                new Vector2(textX + iconSz, iconY + iconSz),
                leafIcon.Uv0, leafIcon.Uv1);
            textX += iconSz + ImGui.GetStyle().ItemInnerSpacing.X;
            iconPadLeft = iconSz + 4f;
        }
        else
        {
            // Left-pad when no icon so text aligns with icon-bearing leaves.
            textX += iconSz + ImGui.GetStyle().ItemInnerSpacing.X;
        }

        // Render name with match highlights.
        uint defaultTextColor = !re.Entry.IsEnabled
            ? ImGui.GetColorU32(ctx.Theme.TextDefault with { W = 0.45f })
            : sel
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

        // Handle mouse click for selection (A17/A18: also move TreeFocusRow).
        if (actualMouseClicked)
        {
            state.SelectedFilteredIndices.Clear();
            state.SelectedFilteredIndices.Add(filteredIdx);
            state.KeyboardFocusIndex = filteredIdx;
            state.TreeFocusRow = visualRowIndex;
        }

        // A disabled entry says why on hover (its Description), since it cannot be picked.
        if (!re.Entry.IsEnabled && ImGui.IsItemHovered() && re.Entry.Description is { Length: > 0 } why)
            ImGui.SetTooltip(why);

        // Double-click confirms (Confirm() refuses a disabled entry).
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            state.Confirmed = true;
        }

        // Scroll focused leaf into view (one-shot, gated on keyboard-nav flag — BATCH-52).
        if (focus && state.ScrollToFocus)
        {
            ImGui.SetScrollHereY(0.5f);
            state.ScrollToFocus = false;
        }

        ImGui.PopID();
        if (leafIndent > 0f) ImGui.Unindent(leafIndent);
    }
}
