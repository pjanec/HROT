using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Editor panel providing New / Save / Load file operations.
/// Delegates all actions to <see cref="IEditorLogic"/>; no direct bus or repo access.
/// </summary>
public sealed class ScenarioBrowserPanel
{
    private string _saveLoadPath = "scenario.json";

    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleNewClick(IEditorLogic logic) => logic.NewScenario();

    public void HandleSaveClick(IEditorLogic logic) => logic.SaveScenario(_saveLoadPath);

    public void HandleLoadClick(IEditorLogic logic) => logic.LoadScenario(_saveLoadPath);

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        ImGui.InputText("Path", ref _saveLoadPath, 512);
        ImGui.Separator();
        if (ImGui.Button("New"))  HandleNewClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Save")) HandleSaveClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Load")) HandleLoadClick(logic);
    }
}
