using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS.Services;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior;
using BehaviorConstants = FDP.Toolkit.Behavior.BehaviorConstants;
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

    private MissionPlan? _draftPlan;
    private long _draftBaseVersion;
    private int _draftEntityId;

    private bool _commitInFlight;
    private Task<MissionCommitResult>? _pendingCommit;

    private readonly string[] _behaviorIds;

    private const int DoctrineIdMoveToLocation = 1;
    private const int DoctrineIdFollowRoute    = 2;
    private const int DoctrineIdJoinFormation  = 3;
    private const int DoctrineIdIdle           = 4;
    private const int BehaviorCatalogCapacity  = 4;

    private const string BehaviorNameMoveToLocation = "MoveToLocation";
    private const string BehaviorNameFollowRoute    = "FollowRoute";
    private const string BehaviorNameJoinFormation  = "JoinFormation";
    private const string BehaviorNameIdle           = "Idle";

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new mission panel with a locally-built doctrine registry used
    /// to populate the BehaviorId dropdown.
    /// </summary>
    public MissionPanel()
    {
        var registry = new DoctrineRegistry();
        _behaviorIds = BuildBehaviorList(registry);
    }

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

    /// <summary>Current locally-edited mission plan draft (null when unset).</summary>
    public MissionPlan? DraftPlan => _draftPlan;

    /// <summary>True while a commit is pending.</summary>
    public bool CommitInFlight => _commitInFlight;

    /// <summary>True when the commit button should be enabled.</summary>
    public bool CommitButtonEnabled => CanCommit;

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

    // ── Draft editing handlers (public for testability) ─────────────────────

    /// <summary>Creates a fresh draft plan and assigns a base version.</summary>
    public void SetDraftPlan(MissionPlan plan, long baseVersion)
    {
        _draftPlan = ClonePlan(plan);
        _draftBaseVersion = baseVersion;
        _draftEntityId = _selectedEntityId;
    }

    /// <summary>Appends a new default task to the draft plan.</summary>
    public void HandleAddTask()
    {
        if (!EnsureDraftForEdit()) return;

        var tasks = GetDraftTasks();
        tasks.Add(new MissionTask
        {
            TaskId         = Guid.NewGuid(),
            ExecutingEngine = string.Empty,
            BehaviorId     = string.Empty,
            BehaviorParams = string.Empty,
            Triggers       = new List<MissionTrigger>(),
            State          = eTaskState.TASK_PLANNED
        });
    }

    /// <summary>Deletes the task at <paramref name="index"/> from the draft plan.</summary>
    public void HandleDeleteTask(int index)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (index < 0 || index >= tasks.Count) return;

        tasks.RemoveAt(index);
    }

    /// <summary>Moves a task from <paramref name="fromIndex"/> to <paramref name="toIndex"/>.</summary>
    public void HandleMoveTask(int fromIndex, int toIndex)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (fromIndex < 0 || fromIndex >= tasks.Count) return;
        if (toIndex < 0 || toIndex >= tasks.Count) return;
        if (fromIndex == toIndex) return;

        var task = tasks[fromIndex];
        tasks.RemoveAt(fromIndex);
        tasks.Insert(toIndex, task);
    }

    /// <summary>Updates the draft task BehaviorId at the specified index.</summary>
    public void HandleEditBehaviorId(int index, string behaviorId)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (index < 0 || index >= tasks.Count) return;

        var task = tasks[index];
        task.BehaviorId = behaviorId ?? string.Empty;
        tasks[index] = task;
    }

    /// <summary>Updates the draft task BehaviorParams JSON at the specified index.</summary>
    public void HandleEditBehaviorParams(int index, string behaviorParams)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (index < 0 || index >= tasks.Count) return;

        var task = tasks[index];
        task.BehaviorParams = behaviorParams ?? string.Empty;
        tasks[index] = task;
    }

    /// <summary>Starts an async mission commit for the current draft plan.</summary>
    public void HandleCommit(IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (!CanCommit) return;

        var plan = _draftPlan!.Value;
        FdpLog<MissionPanel>.Info("[IOS] Commit triggered: entityId={0} taskCount={1} baseVersion={2}",
            _selectedEntityId, plan.Tasks?.Count ?? 0, _draftBaseVersion);
        _pendingCommit = logic.MissionEditorService
            .CommitMissionAsync(_selectedEntityId, plan, _draftBaseVersion);
        _commitInFlight = true;
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
        IosPanelColors.Push();
        ImGui.Begin("Selection & Mission");
        IosPanelColors.Pop();

        PollCommitCompletion();

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

        SyncDraftFromSnapshot(logic);

        var info    = entity.HasDescriptor<EntityInfo>()    ? entity.GetDescriptor<EntityInfo>()    : default;
        var mission = entity.HasDescriptor<EntityMission>() ? entity.GetDescriptor<EntityMission>() : default;

        var planToShow = _draftPlan ?? mission.Plan;

        ImGui.Text($"Selected: {info.Name}");
        ImGui.Text($"ID: {_selectedEntityId}");

        if (planToShow.Tasks != null)
        {
            ImGui.Text("Mission:");
            for (int i = 0; i < planToShow.Tasks.Count; i++)
            {
                var task    = planToShow.Tasks[i];
                bool active = task.TaskId == planToShow.ActiveTaskId;

                ImGui.Text($"{GetTaskIcon(task, active)} {i + 1}.");
                ImGui.SameLine();

                var behaviorLabel = string.IsNullOrEmpty(task.BehaviorId)
                    ? "<none>"
                    : task.BehaviorId;

                if (ImGui.BeginCombo($"Behavior##{i}", behaviorLabel))
                {
                    for (int b = 0; b < _behaviorIds.Length; b++)
                    {
                        bool selected = task.BehaviorId == _behaviorIds[b];
                        if (ImGui.Selectable(_behaviorIds[b], selected))
                            HandleEditBehaviorId(i, _behaviorIds[b]);
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                string paramsBuffer = task.BehaviorParams ?? string.Empty;
                var paramsSize = new System.Numerics.Vector2(
                    ImGui.GetContentRegionAvail().X,
                    ImGui.GetTextLineHeightWithSpacing() * PanelConstants.MissionBehaviorParamsEditorLines);

                if (ImGui.InputTextMultiline(
                        $"Params##{i}",
                        ref paramsBuffer,
                        PanelConstants.MissionBehaviorParamsMaxLength,
                        paramsSize))
                {
                    HandleEditBehaviorParams(i, paramsBuffer);
                }

                if (ImGui.SmallButton($"↑##{i}"))
                    HandleMoveTask(i, i - 1);
                ImGui.SameLine();
                if (ImGui.SmallButton($"↓##{i}"))
                    HandleMoveTask(i, i + 1);
                ImGui.SameLine();
                if (ImGui.SmallButton($"✕##{i}"))
                    HandleDeleteTask(i);

                ImGui.Separator();
            }

            if (ImGui.Button("+ Add Task"))
                HandleAddTask();

            ImGui.SameLine();
            // Capture the enabled state once before the button so that HandleCommit()
            // changing CommitButtonEnabled mid-frame cannot cause a mismatched
            // BeginDisabled / EndDisabled pair (Task-7 fix).
            bool commitEnabled = CommitButtonEnabled;
            if (!commitEnabled) ImGui.BeginDisabled();
            if (ImGui.Button("Commit")) HandleCommit(logic);
            if (!commitEnabled) ImGui.EndDisabled();

            if (ImGui.Button("JUMP"))  HandleJump(logic);
            ImGui.SameLine();
            if (ImGui.Button("ABORT")) HandleAbort(logic);
        }

        ImGui.End();
    }

    // ── Draft helpers ───────────────────────────────────────────────────────

    private bool CanCommit
        => _selectedEntityId != 0 && _draftPlan.HasValue && !_commitInFlight;

    private bool EnsureDraftForEdit()
    {
        if (_selectedEntityId == 0) return false;

        if (!_draftPlan.HasValue)
            _draftPlan = CreateEmptyPlan();

        _draftEntityId = _selectedEntityId;
        return true;
    }

    private List<MissionTask> GetDraftTasks()
    {
        var plan = _draftPlan!.Value;
        if (plan.Tasks == null)
        {
            plan.Tasks = new List<MissionTask>();
            _draftPlan = plan;
        }
        return plan.Tasks;
    }

    private bool TryGetDraftTasks(out List<MissionTask> tasks)
    {
        tasks = null!;
        if (!_draftPlan.HasValue) return false;

        var plan = _draftPlan.Value;
        if (plan.Tasks == null) return false;

        tasks = plan.Tasks;
        return true;
    }

    private void SyncDraftFromSnapshot(IIosLogic logic)
    {
        if (_selectedEntityId == 0)
        {
            ClearDraft();
            return;
        }

        if (_draftPlan.HasValue && _draftEntityId == _selectedEntityId)
            return;

        var (plan, version) = logic.MissionEditorService.GetMissionSnapshot(_selectedEntityId);
        _draftBaseVersion = version;
        _draftEntityId = _selectedEntityId;

        _draftPlan = plan.HasValue
            ? ClonePlan(plan.Value)
            : CreateEmptyPlan();
    }

    private void ClearDraft()
    {
        _draftPlan = null;
        _draftBaseVersion = 0;
        _draftEntityId = 0;
    }

    private void PollCommitCompletion()
    {
        if (!_commitInFlight || _pendingCommit == null) return;
        if (!_pendingCommit.IsCompleted) return;

        MissionCommitResult result;
        if (_pendingCommit.IsFaulted)
        {
            result = new MissionCommitResult
            {
                Success = false,
                ErrorMessage = _pendingCommit.Exception?.GetBaseException().Message
            };
        }
        else
        {
            result = _pendingCommit.Result;
        }

        _commitInFlight = false;
        _pendingCommit = null;

        if (result.Success)
        {
            FdpLog<MissionPanel>.Info("[IOS] Commit succeeded: entityId={0} newVersion={1}",
                _selectedEntityId, result.NewVersion);
            _draftBaseVersion = result.NewVersion;
        }
        else
        {
            FdpLog<MissionPanel>.Warn("[IOS] Commit failed: entityId={0} errorCode={1} error={2}",
                _selectedEntityId, result.ErrorCode, result.ErrorMessage);
            HandleConflictResult(result);
        }
    }

    private static MissionPlan CreateEmptyPlan()
        => new MissionPlan { Tasks = new List<MissionTask>() };

    private static MissionPlan ClonePlan(MissionPlan plan)
    {
        var clone = new MissionPlan
        {
            ActiveTaskId = plan.ActiveTaskId,
            Tasks = new List<MissionTask>(plan.Tasks?.Count ?? 0)
        };

        if (plan.Tasks == null) return clone;

        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            var copied = task;
            if (task.Triggers != null)
                copied.Triggers = new List<MissionTrigger>(task.Triggers);
            else
                copied.Triggers = new List<MissionTrigger>();

            clone.Tasks.Add(copied);
        }

        return clone;
    }

    private static string[] BuildBehaviorList(DoctrineRegistry registry)
    {
        var names = new List<string>(BehaviorCatalogCapacity)
        {
            BehaviorNameMoveToLocation,
            BehaviorNameFollowRoute,
            BehaviorNameJoinFormation,
            BehaviorNameIdle
        };

        registry.Register(DoctrineIdMoveToLocation, BehaviorNameMoveToLocation,
            new DoctrineDefinition { Name = BehaviorNameMoveToLocation, BrainTier = BehaviorConstants.BrainTierBTree });
        registry.Register(DoctrineIdFollowRoute, BehaviorNameFollowRoute,
            new DoctrineDefinition { Name = BehaviorNameFollowRoute, BrainTier = BehaviorConstants.BrainTierBTree });
        registry.Register(DoctrineIdJoinFormation, BehaviorNameJoinFormation,
            new DoctrineDefinition { Name = BehaviorNameJoinFormation, BrainTier = BehaviorConstants.BrainTierBTree });
        registry.Register(DoctrineIdIdle, BehaviorNameIdle,
            new DoctrineDefinition { Name = BehaviorNameIdle, BrainTier = BehaviorConstants.BrainTierBTree });

        return names.ToArray();
    }
}
