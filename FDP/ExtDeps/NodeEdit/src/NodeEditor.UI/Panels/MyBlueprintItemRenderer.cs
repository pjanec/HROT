using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Renders a single My Blueprint item row.
/// Pixel layout: [selection bg] [indent] [accent dot 8px] [icon 16x16] [name] [badge chip] [tooltip ⓘ on hover]
/// </summary>
internal static class MyBlueprintItemRenderer
{
    private const float AccentDotRadius = 4f;
    private const float IconSize        = 16f;
    private const float RowHeight       = 20f;
    private const float BadgeMaxWidth   = 80f;

    /// <summary>
    /// Render one item row.
    /// Returns true if the user clicked it (triggers selection event).
    /// Returns via <paramref name="doubleClicked"/> if user double-clicked.
    /// </summary>
    public static bool Render(
        MyBlueprintItem item,
        bool isSelected,
        IIconProvider icons,
        IEditorTheme theme,
        IReadOnlyList<int>? matchPositions,
        out bool doubleClicked,
        System.Action? postSelectable)
    {
        doubleClicked = false;

        var cursorStart = ImGui.GetCursorScreenPos();

        bool clicked = ImGui.Selectable("##selectable_" + item.ItemId, isSelected,
                          ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
                          new Vector2(0f, RowHeight));

        if (clicked && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            doubleClicked = true;

        postSelectable?.Invoke();

        // Draw the row contents on top of the selectable.
        float x = cursorStart.X + ImGui.GetTreeNodeToLabelSpacing();
        float y = cursorStart.Y + (RowHeight - AccentDotRadius * 2f) * 0.5f;

        var dl = ImGui.GetWindowDrawList();

        // Accent dot (type color).
        if (item.AccentColor.HasValue)
        {
            var ac = item.AccentColor.Value;
            dl.AddCircleFilled(new Vector2(x + AccentDotRadius, y + AccentDotRadius),
                               AccentDotRadius,
                               ImGui.GetColorU32(ac));
        }
        x += AccentDotRadius * 2f + 4f;

        // Icon (16x16) — pass atlas UV rect so sprite-sheet icons are correctly cropped.
        if (item.IconKey is not null && icons.TryGet(item.IconKey, out var iconHandle))
        {
            ImGui.SetCursorScreenPos(new Vector2(x, cursorStart.Y + (RowHeight - IconSize) * 0.5f));
            ImGui.Image(iconHandle.TextureId, new Vector2(IconSize, IconSize),
                        iconHandle.Uv0, iconHandle.Uv1);
        }
        x += IconSize + 4f;

        // Name (with optional highlight of matched chars).
        ImGui.SetCursorScreenPos(new Vector2(x, cursorStart.Y + (RowHeight - ImGui.GetTextLineHeight()) * 0.5f));
        DrawName(item.DisplayName, matchPositions, theme);

        // Badge chip.
        if (item.BadgeText is { Length: > 0 } badge)
        {
            DrawBadge(badge, theme, dl);
        }

        // Hover tooltip.
        if (item.Tooltip is { Length: > 0 } tip && ImGui.IsItemHovered())
            ImGui.SetTooltip(tip);

        return clicked && !doubleClicked;
    }

    // ── name with match highlights ────────────────────────────────────────────

    private static void DrawName(string name, IReadOnlyList<int>? matchPositions, IEditorTheme theme)
    {
        if (matchPositions is null or { Count: 0 })
        {
            ImGui.TextUnformatted(name);
            return;
        }

        var matchSet = new HashSet<int>(matchPositions);
        var normal   = ImGui.GetColorU32(theme.TextDefault);
        var highlight = ImGui.GetColorU32(theme.SelectionAccent);

        for (int i = 0; i < name.Length; i++)
        {
            string ch = name[i].ToString();
            if (matchSet.Contains(i))
                ImGui.TextColored(theme.SelectionAccent, ch);
            else
                ImGui.TextColored(theme.TextDefault, ch);

            if (i < name.Length - 1)
                ImGui.SameLine(0f, 0f);
        }
    }

    // ── badge chip ────────────────────────────────────────────────────────────

    private static void DrawBadge(string text, IEditorTheme theme, ImDrawListPtr dl)
    {
        ImGui.SameLine();
        var pos    = ImGui.GetCursorScreenPos();
        float pad  = 4f;
        float w    = Math.Min(ImGui.CalcTextSize(text).X + pad * 2f, BadgeMaxWidth);
        float h    = ImGui.GetTextLineHeight() + pad;

        uint bg = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.5f, 0.8f));
        dl.AddRectFilled(pos, new Vector2(pos.X + w, pos.Y + h), bg, 3f);

        ImGui.SetCursorScreenPos(new Vector2(pos.X + pad, pos.Y + pad * 0.5f));
        ImGui.TextColored(theme.TextDefault, text);
    }
}
