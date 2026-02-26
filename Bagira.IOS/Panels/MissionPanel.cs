using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Services;
using ImGuiNET;

namespace Bagira.IOS.Panels;

/// <summary>
/// IOS UI panel that displays the currently selected entity's identity and
/// mission plan, and provides buttons to send jump / abort control commands
/// via <see cref="Bagira.IOS.Services.IMissionEditorService"/>.
///
/// <para><b>Testing:</b> all UI-triggered logic lives in public
/// <c>Handle*</c> methods that accept an <see cref="IIosLogic"/>; tests can
/// call these directly with a Moq mock to assert side-effects without an ImGui
/// render frame.</para>
/// </summary>
public sealed class MissionPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private int _selectedEntityId = 0;

    // ── Public state accessor ─────────────────────────────────────────────────

    /// <summary>
    /// The entity currently shown by the panel.
    /// Set to 0 (no selection) or a valid DER entity ID.
    /// </summary>
    public int SelectedEntityId
    {
        get => _selectedEntityId;
        set => _selectedEntityId = value;
    }

    // ── Task icon helper (public for testability) ─────────────────────────────

    /// <summary>
    /// Returns a single Unicode glyph that visually represents the task state.
    ///
    /// <list type="table">
    ///   <listheader><term>State</term><description>Glyph</description></listheader>
    ///   <item><term>Active (isActive = true)</term><description>▶</description></item>
    ///   <item><term>TASK_DONE</term><description>✓</description></item>
    ///   <item><term>TASK_FAILED</term><description>✗</description></item>
    ///   <item><term>TASK_SKIPPED</term><description>⏭</description></item>
    ///   <item><term>TASK_PLANNED (default)</term><description>⏹</description></item>
    /// </list>
    /// </summary>
    public static string GetTaskIcon(MissionTask task, bool isActive)
    {
        if (isActive) return "▶";

        return task.State switch
        {
            eTaskState.TASK_DONE    => "✓",
            eTaskState.TASK_FAILED  => "✗",
            eTaskState.TASK_SKIPPED => "⏭",
            _                       => "⏹"   // TASK_PLANNED and anything else
        };
    }

    // ── Button handlers (public for testability) ──────────────────────────────

    /// <summary>
    /// Handles the "JUMP" button press: sends a
    /// <see cref="eMissionCommandType.CMD_JUMP_TO_TASK"/> control command for
    /// the currently selected entity.
    /// </summary>
    public void HandleJump(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (_selectedEntityId == 0) return;

        logic.MissionEditorService.SendControlCommand(
            _selectedEntityId,
            eMissionCommandType.CMD_JUMP_TO_TASK,
            Guid.Empty);
    }

    /// <summary>
    /// Handles the "ABORT" button press: sends a
    /// <see cref="eMissionCommandType.CMD_ABORT_ALL"/> control command for the
    /// currently selected entity.
    /// </summary>
    public void HandleAbort(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (_selectedEntityId == 0) return;

        logic.MissionEditorService.SendControlCommand(
            _selectedEntityId,
            eMissionCommandType.CMD_ABORT_ALL,
            Guid.Empty);
    }

    // ── Conflict alert (IOS.10.3) ────────────────────────────────────────────

    private string? _conflictMessage;

    /// <summary>
    /// True when a version-conflict commit result is pending user acknowledgement.
    /// </summary>
    public bool HasConflictAlert => _conflictMessage is not null;

    /// <summary>
    /// The conflict error message to show in the ImGui modal.
    /// <c>null</c> when no alert is active.
    /// </summary>
    public string? ConflictMessage => _conflictMessage;

    /// <summary>
    /// Inspects a <see cref="MissionCommitResult"/> and, when it represents an
    /// optimistic-lock version conflict
    /// (<see cref="MissionCommitResult.ErrorCode"/> ==
    /// <see cref="PanelConstants.VersionConflictErrorCode"/>), stores the error
    /// message so <see cref="Draw"/> can surface an ImGui modal to the operator.
    /// </summary>
    public void HandleConflictResult(MissionCommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success && result.ErrorCode == PanelConstants.VersionConflictErrorCode)
            _conflictMessage = result.ErrorMessage ?? PanelConstants.VersionConflictErrorMessage;
    }

    /// <summary>
    /// Clears the active conflict alert.  Call when the operator dismisses the
    /// conflict modal.
    /// </summary>
    public void DismissConflict() => _conflictMessage = null;

    // ── Draw stub (Phase P9) ──────────────────────────────────────────────────

    /// <summary>
    /// Renders the panel via ImGui.
    /// Called once per frame from the application shell (Phase P9).
    /// </summary>
    public void Draw(IIosLogic logic)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ImGui.Begin("Selection & Mission");

        if (_selectedEntityId == 0)
        {
            ImGui.Text("No selection");
            ImGui.End(); return;
        }

        var entity = logic.Repo.GetEntity(_selectedEntityId);
        if (entity == null)
        {
            ImGui.Text("Entity not found");
            ImGui.End(); return;
        }

        var info    = entity.HasDescriptor<EntityInfo>()    ? entity.GetDescriptor<EntityInfo>()    : default;
        var mission = entity.HasDescriptor<EntityMission>() ? entity.GetDescriptor<EntityMission>() : default;

        ImGui.Text($"Selected: {info.Name}");
        ImGui.Text($"ID: {_selectedEntityId}");

        if (mission.Plan.Tasks != null)
        {
            ImGui.Text("Mission:");
            for (int i = 0; i < mission.Plan.Tasks.Count; i++)
            {
                var task    = mission.Plan.Tasks[i];
                bool active = task.TaskId == mission.Plan.ActiveTaskId;
                ImGui.Text($"{GetTaskIcon(task, active)} {i + 1}. {task.BehaviorId}");
            }

            if (ImGui.Button("JUMP"))  HandleJump(logic);
            ImGui.SameLine();
            if (ImGui.Button("ABORT")) HandleAbort(logic);
        }

        ImGui.End();
    }
}
