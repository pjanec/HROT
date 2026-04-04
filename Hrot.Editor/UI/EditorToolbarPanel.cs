using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Editor toolbar panel for tool mode selection.
/// Delegates all tool activation to <see cref="IEditorLogic"/>.
/// </summary>
public sealed class EditorToolbarPanel
{
    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleSpawnClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Spawn);
    public void HandleSelectClick(IEditorLogic logic) => logic.ActivateTool(EditorTool.Select);
    public void HandleEditClick(IEditorLogic logic)   => logic.ActivateTool(EditorTool.Edit);
    public void HandleRouteClick(IEditorLogic logic)  => logic.ActivateTool(EditorTool.Route);

    public void HandleToggleModeClick(IEditorLogic logic)
    {
        if (logic.CurrentMode == SimHostMode.Internal)
            _ = logic.SwitchToExternalAsync();   // fire-and-forget; kernel drains during game loop
        else
            _ = logic.SwitchToInternalAsync();
    }

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        ImGui.Text("Tools");
        ImGui.Separator();
        if (ImGui.Button("Select"))       HandleSelectClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Place Entity")) HandleSpawnClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Edit Shape"))   HandleEditClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Edit Route"))   HandleRouteClick(logic);
        ImGui.SameLine();
        string modeLabel = logic.CurrentMode == SimHostMode.Internal ? "Go External" : "Go Internal";
        if (ImGui.Button(modeLabel)) HandleToggleModeClick(logic);
    }
}
