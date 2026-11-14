using ImGuiNET;

namespace NodeEditor.UI.Picker.Layouts;

/// <summary>Shared vertical metrics of the picker layouts.</summary>
internal static class PickerLayoutMetrics
{
    /// <summary>
    /// The height a layout leaves under itself for the footer row (OK/Cancel). ⭐ CE-4168 — inside the side-pane split the SPLIT
    /// already left that room (its child ends above the footer), so the layout reserves nothing more.
    /// </summary>
    public static float FooterReserve(PickerState state)
        => state.InSidePane ? 0f : ImGui.GetFrameHeightWithSpacing();
}
