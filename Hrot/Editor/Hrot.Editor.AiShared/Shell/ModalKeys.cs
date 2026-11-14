using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ CE-1013 / CE-1014 — the ONE "a modal closes on Esc" test: true when Esc is pressed while this modal (or one of its child
/// windows) has focus. Call it inside the modal's <c>BeginPopupModal</c> block and treat it exactly like the Cancel button.
/// ⚠ A text field consumes Esc to drop its edit first — the next Esc then closes the modal, the usual ImGui behaviour.
/// </summary>
public static class ModalKeys
{
    public static bool EscapePressed()
        => ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape, false);
}
