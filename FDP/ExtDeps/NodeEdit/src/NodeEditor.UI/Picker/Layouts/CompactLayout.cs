using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>
/// Compact single-column layout: no detail pane, smaller default window (~320×360).
/// Used for enum value pickers and channel-action pickers.
/// </summary>
internal static class CompactLayout
{
    /// <summary>Render the compact list (no detail pane).</summary>
    public static void Draw(PickerState state, IPickerRenderContext ctx)
    {
        float height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("##picker_compact", new Vector2(0f, height), ImGuiChildFlags.None))
        {
            PickerItemListHelper.DrawItems(state, ctx, singleColumn: true);
        }
        ImGui.EndChild();
    }
}
