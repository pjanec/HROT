using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Standard two-pane layout: filtered list on the left (~60%), detail pane on the right (~40%).
/// Supports Favorites and Recent header sections.
/// </summary>
internal static class StandardLayout
{
    private const float DetailFraction = 0.40f;

    /// <summary>Render the list + detail split for the Standard layout.</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx)
    {
        float avail = ImGui.GetContentRegionAvail().X;
        float listW = avail * (1f - DetailFraction) - 4f;
        float detailW = avail * DetailFraction;

        ImGui.BeginGroup();
        DrawList(state, ctx, listW);
        ImGui.EndGroup();

        ImGui.SameLine(0f, 4f);

        ImGui.BeginGroup();
        DrawDetail(state, ctx, detailW);
        ImGui.EndGroup();
    }

    // ── list ─────────────────────────────────────────────────────────────────

    private static void DrawList(PickerState state, IPickerRenderContext ctx, float width)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing(); // leave room for OK/Cancel
        if (ImGui.BeginChild("##picker_list", new Vector2(width, height), ImGuiChildFlags.None))
        {
            PickerItemListHelper.DrawItems(state, ctx, singleColumn: true);
        }
        ImGui.EndChild();
    }

    // ── detail ────────────────────────────────────────────────────────────────

    private static void DrawDetail(PickerState state, IPickerRenderContext ctx, float width)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("##picker_detail", new Vector2(width, height), ImGuiChildFlags.None,
                ImGuiWindowFlags.NoScrollbar))
        {
            int focused = state.KeyboardFocusIndex;
            PickerEntry? entry = (focused >= 0 && focused < state.Filtered.Count)
                ? state.Filtered[focused].Entry
                : null;

            if (entry is not null)
            {
                ImGui.PushTextWrapPos(width - 8f);
                ImGui.TextColored(ctx.Theme.TextDefault, entry.Name);
                if (entry.Category is { Length: > 0 })
                    ImGui.TextColored(ctx.Theme.TextMuted, entry.Category);
                if (entry.Description is { Length: > 0 })
                    ImGui.TextWrapped(entry.Description);
                ImGui.PopTextWrapPos();
            }
            else
            {
                ImGui.TextColored(ctx.Theme.TextMuted, "(no selection)");
            }
        }
        ImGui.EndChild();
    }
}
