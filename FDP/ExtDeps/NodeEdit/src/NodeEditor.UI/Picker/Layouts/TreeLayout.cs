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
                if (explicitRoot is not null)
                    DrawExplicitTree(state, ctx, explicitRoot, 0);
                else
                    DrawImplicitTree(state, ctx);
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
        // Default-open always so every leaf is rendered and ↑/↓ keyboard nav reaches it.
        // Mouse arrow-click collapse still works via the arrow triangle — this only
        // changes the INITIAL state.
        var flags = ImGuiTreeNodeFlags.DefaultOpen;

        // Render folders.
        foreach (var folder in root.Folders)
            DrawFolderNode(state, ctx, folder, flags);

        // Render uncategorized leaves.
        foreach (var leaf in root.Leaves)
            DrawLeafItem(state, ctx, leaf.FilteredIndex, state.Filtered[leaf.FilteredIndex]);
    }

    private static void DrawFolderNode(PickerState state, IPickerRenderContext ctx,
        PickerTreeBuilder.Node folder, ImGuiTreeNodeFlags flags)
    {
        // Draw folder icon if available. Pick closed/open glyph from the node's
        // persisted open-state (1-frame lag is acceptable for a folder glyph); the
        // icon must be drawn BEFORE TreeNodeEx, so we read last frame's state from
        // ImGui's per-id storage. Falls back to "folder" if "folder_open" is unknown.
        uint nodeId = ImGui.GetID(folder.Name);
        int defaultOpenInt = (flags & ImGuiTreeNodeFlags.DefaultOpen) != 0 ? 1 : 0;
        bool wasOpen = ImGui.GetStateStorage().GetInt(nodeId, defaultOpenInt) != 0;

        string folderKey = wasOpen ? "folder_open" : "folder";
        bool hasIcon = ctx.Icons.TryGet(folderKey, out var folderIcon)
                       || ctx.Icons.TryGet("folder", out folderIcon);
        if (hasIcon)
        {
            ImGui.Image(folderIcon.TextureId, IconSize, folderIcon.Uv0, folderIcon.Uv1);
            ImGui.SameLine();
        }

        bool open = ImGui.TreeNodeEx(folder.Name, flags);

        if (open)
        {
            foreach (var subFolder in folder.Folders)
                DrawFolderNode(state, ctx, subFolder, flags);

            foreach (var leaf in folder.Leaves)
                DrawLeafItem(state, ctx, leaf.FilteredIndex, state.Filtered[leaf.FilteredIndex]);

            ImGui.TreePop();
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

        bool open = ImGui.TreeNodeEx(node.Name, ImGuiTreeNodeFlags.DefaultOpen); // default-open always
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
        bool isSearching = !string.IsNullOrEmpty(state.SearchText);

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
