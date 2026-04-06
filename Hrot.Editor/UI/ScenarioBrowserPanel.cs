using ImGuiNET;

namespace Hrot.Editor.UI;

/// <summary>
/// Editor panel providing New / Save / Save As / Load operations with modal dialogs.
/// Delegates all actions to <see cref="IEditorLogic"/>; no direct bus or repo access.
/// </summary>
public sealed class ScenarioBrowserPanel
{
    // ── Modal dialog state ────────────────────────────────────────────────────

    private bool   _showLoadDialog;
    private bool   _showSaveAsDialog;
    private int    _selectedLoadIdx = -1;
    private string _saveAsName = "";

    // ── Testable handlers ─────────────────────────────────────────────────────

    public void HandleNewClick(IEditorLogic logic)
    {
        logic.NewScenario();
        _selectedLoadIdx = -1;
    }

    public void HandleSaveClick(IEditorLogic logic)
    {
        if (!string.IsNullOrEmpty(logic.LoadedScenarioName))
            logic.SaveCurrentScenario();
        else
            _showSaveAsDialog = true;
    }

    public void HandleSaveAsClick()  => _showSaveAsDialog = true;

    public void HandleLoadClick()    => _showLoadDialog   = true;

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {
        // ── Current scenario indicator ────────────────────────────────────────
        var loaded = logic.LoadedScenarioName;
        ImGui.Text(string.IsNullOrEmpty(loaded) ? "(no scenario loaded)" : $"Scenario: {loaded}");
        ImGui.Separator();

        // ── Action buttons ────────────────────────────────────────────────────
        if (ImGui.Button("New"))     HandleNewClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Save"))    HandleSaveClick(logic);
        ImGui.SameLine();
        if (ImGui.Button("Save As")) HandleSaveAsClick();
        ImGui.SameLine();
        if (ImGui.Button("Load"))    HandleLoadClick();

        // ── Load modal dialog ─────────────────────────────────────────────────
        if (_showLoadDialog)
        {
            ImGui.OpenPopup("Load Scenario##browser");
            _showLoadDialog = false;
        }

        bool loadOpen = true;
        if (ImGui.BeginPopupModal("Load Scenario##browser", ref loadOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Select a scenario to load:");
            var available = logic.AvailableScenarios;

            if (available.Count == 0)
            {
                ImGui.TextDisabled("(no scenarios found)");
            }
            else
            {
                for (int i = 0; i < available.Count; i++)
                {
                    if (ImGui.Selectable(available[i], _selectedLoadIdx == i))
                        _selectedLoadIdx = i;
                }
            }

            ImGui.Separator();
            bool canLoad = _selectedLoadIdx >= 0 && _selectedLoadIdx < available.Count;
            if (!canLoad) ImGui.BeginDisabled();
            if (ImGui.Button("Load") && canLoad)
            {
                logic.LoadScenarioByName(available[_selectedLoadIdx]);
                ImGui.CloseCurrentPopup();
            }
            if (!canLoad) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        // ── Save As modal dialog ──────────────────────────────────────────────
        if (_showSaveAsDialog)
        {
            ImGui.OpenPopup("Save Scenario As##browser");
            _saveAsName = logic.LoadedScenarioName ?? "";
            _showSaveAsDialog = false;
        }

        bool saveAsOpen = true;
        if (ImGui.BeginPopupModal("Save Scenario As##browser", ref saveAsOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Enter a name for the scenario:");
            ImGui.InputText("##saveasname", ref _saveAsName, 128);

            ImGui.Separator();
            bool canSave = !string.IsNullOrWhiteSpace(_saveAsName);
            if (!canSave) ImGui.BeginDisabled();
            if (ImGui.Button("Save") && canSave)
            {
                logic.SaveScenarioAs(_saveAsName.Trim());
                ImGui.CloseCurrentPopup();
            }
            if (!canSave) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
