using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
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
    private string _saveAsName = "";    private bool   _showMigrationHistoryDialog;
    private IReadOnlyList<SidecarFileInfo>? _migrationSidecars;
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

    public void HandleMigrationHistoryClick(IEditorLogic logic)
    {
        _migrationSidecars           = logic.GetMigrationSidecarsForCurrentScenario();
        _showMigrationHistoryDialog  = true;
    }

    // ── ImGui rendering ───────────────────────────────────────────────────────

    public void DrawContent(IEditorLogic logic)
    {        // ── Degraded-mode banner ────────────────────────────────────────────────────
        if (logic.IsScenarioDegraded)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.5f, 0f, 1f));
            ImGui.TextWrapped("[!] Degraded mode: scenario loaded from a snapshot backup. " +
                              "Saving will lose newer-version data.");
            ImGui.PopStyleColor();
            ImGui.Separator();
        }
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
        ImGui.SameLine();
        if (ImGui.Button("Migration History"))
            HandleMigrationHistoryClick(logic);

        // ── Load modal dialog ─────────────────────────────────────────────────
        if (_showLoadDialog)
        {
            ImGui.OpenPopup("Load Scenario##browser");
            _showLoadDialog = false;
        }

        bool loadOpen = true;
        if (ImGui.BeginPopupModal("Load Scenario##browser", ref loadOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
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
                    if (ImGui.Selectable(available[i], _selectedLoadIdx == i, ImGuiSelectableFlags.NoAutoClosePopups))
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
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
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

        // ── Migration history dialog ─────────────────────────────────────────────────────────
        if (_showMigrationHistoryDialog)
        {
            ImGui.OpenPopup("Migration History##browser");
            _showMigrationHistoryDialog = false;
        }

        bool historyOpen = true;
        if (ImGui.BeginPopupModal("Migration History##browser", ref historyOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
            ImGui.Text("Sidecar files for the current scenario:");
            ImGui.Separator();

            var sidecars = _migrationSidecars;
            if (sidecars == null || sidecars.Count == 0)
            {
                ImGui.TextDisabled("(no sidecars present)");
            }
            else
            {
                if (ImGui.BeginTable("##sidecars", 4,
                        ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
                {
                    ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Kind",    ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthFixed, 60f);
                    ImGui.TableSetupColumn("Hash",    ImGuiTableColumnFlags.WidthFixed, 130f);
                    ImGui.TableHeadersRow();

                    foreach (var s in sidecars)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(s.FileName);
                        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(s.Kind.ToString());
                        ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(s.Version.ToString());
                        ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(s.ContentHash);
                    }
                    ImGui.EndTable();
                }
            }

            ImGui.Spacing();
            if (ImGui.Button("Close", new System.Numerics.Vector2(100f, 0f)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
