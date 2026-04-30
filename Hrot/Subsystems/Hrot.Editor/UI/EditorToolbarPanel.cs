using ImGuiNET;
using System.Numerics;

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

    public void HandleReloadAIClick(IEditorLogic logic) => logic.RebuildAndReloadAI();

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
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiNET.ImGuiCol.Button, new Vector4(0.8f, 0.4f, 0.0f, 1.0f));
        if (ImGui.Button("Reload BTrees")) HandleReloadAIClick(logic);
        ImGui.PopStyleColor();
    }
}
