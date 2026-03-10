using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IOS.Services;
using FDP.Kernel.Logging;
using FDP.Toolkit.Behavior;
using BehaviorConstants = FDP.Toolkit.Behavior.BehaviorConstants;
using ImGuiNET;
using System.Text.Json;

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

    // ── Map-pick pending state ─────────────────────────────────────────────────

    /// <summary>
    /// Pending async location pick. Non-null while the operator is
    /// picking a location on the map for a <c>MoveToLocation</c> task.
    /// </summary>
    private Task<GeoPosition>? _pendingLocationPick;

    /// <summary>
    /// Pending async entity pick. Non-null while the operator is
    /// picking a route entity for a <c>FollowRoute</c> task.
    /// </summary>
    private Task<int>? _pendingEntityPick;

    /// <summary>Task index that the current pending pick is targeting.</summary>
    private int _pendingPickTaskIndex = -1;

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

    // ── Map-pick handlers (public for testability) ────────────────────────────

    /// <summary>
    /// Initiates an async location pick for the <c>MoveToLocation</c> task at
    /// <paramref name="index"/>.  The panel polls <see cref="PollPickCompletion"/>
    /// each frame and writes the JSON params when the pick resolves.
    /// </summary>
    public void HandlePickLocation(int index, IIosLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (!TryGetDraftTasks(out _)) return;
        if (index < 0) return;

        _pendingPickTaskIndex = index;
        _pendingLocationPick  = logic.MapPickService.PickLocationAsync();
    }

    /// <summary>
    /// Initiates an async entity pick for the <c>FollowRoute</c> (or similar)
    /// task at <paramref name="index"/>.
    /// </summary>
    public void HandlePickEntity(int index, IIosLogic logic, string[] filterPresets)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (!TryGetDraftTasks(out _)) return;
        if (index < 0) return;

        _pendingPickTaskIndex = index;
        _pendingEntityPick    = logic.MapPickService.PickEntityAsync(filterPresets);
    }

    /// <summary>True when an async location pick is in flight for any task.</summary>
    public bool IsLocationPickPending => _pendingLocationPick is { IsCompleted: false };

    /// <summary>True when an async entity pick is in flight for any task.</summary>
    public bool IsEntityPickPending => _pendingEntityPick is { IsCompleted: false };

    // ── JSON helpers for MoveToLocation / FollowRoute params ─────────────────

    /// <summary>
    /// Builds the canonical <c>MoveToLocation</c> behavior-params JSON:
    /// <c>{"targetLat":…,"targetLon":…}</c>.
    /// </summary>
    internal static string BuildMoveToLocationParams(double lat, double lon)
        => $"{{\"targetLat\":{lat:F6},\"targetLon\":{lon:F6}}}";

    /// <summary>
    /// Builds the canonical <c>FollowRoute</c> behavior-params JSON:
    /// <c>{"routeEntityId":…}</c>.
    /// </summary>
    internal static string BuildFollowRouteParams(int routeEntityId)
        => $"{{\"routeEntityId\":{routeEntityId}}}";

    /// <summary>
    /// Tries to parse lat/lon from a <c>MoveToLocation</c> params JSON string.
    /// Returns <c>false</c> when the JSON is empty or malformed.
    /// </summary>
    internal static bool TryParseMoveToLocationParams(string json, out double lat, out double lon)
    {
        lat = lon = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;
            if (root.TryGetProperty("targetLat", out var latEl)
             && root.TryGetProperty("targetLon", out var lonEl))
            {
                lat = latEl.GetDouble();
                lon = lonEl.GetDouble();
                return true;
            }
        }
        catch { /* malformed */ }
        return false;
    }

    /// <summary>
    /// Tries to parse the route entity ID from a <c>FollowRoute</c> params JSON string.
    /// Returns <c>false</c> when the JSON is empty or malformed.
    /// </summary>
    internal static bool TryParseFollowRouteParams(string json, out int routeEntityId)
    {
        routeEntityId = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;
            if (root.TryGetProperty("routeEntityId", out var idEl))
            {
                routeEntityId = idEl.GetInt32();
                return true;
            }
        }
        catch { /* malformed */ }
        return false;
    }



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
        PollPickCompletion();

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

                // ─── Behavior-specific parameter editor ──────────────────────
                // MoveToLocation and FollowRoute get a map-pick button instead of
                // a raw JSON text field to prevent operators from having to type
                // raw coordinates or numeric entity IDs by hand.
                if (task.BehaviorId == BehaviorNameMoveToLocation)
                {
                    DrawMoveToLocationParams(i, paramsBuffer, logic);
                }
                else if (task.BehaviorId == BehaviorNameFollowRoute)
                {
                    DrawFollowRouteParams(i, paramsBuffer, logic);
                }
                else
                {
                    // Generic fallback: raw JSON text editor for other behaviors.
                    var paramsSize = new System.Numerics.Vector2(
                        ImGui.GetContentRegionAvail().X,
                        ImGui.GetTextLineHeightWithSpacing() * PanelConstants.MissionBehaviorParamsEditorLines);

                    ImGui.Text("Params:");
                    if (ImGui.InputTextMultiline(
                            $"##Params{i}",
                            ref paramsBuffer,
                            PanelConstants.MissionBehaviorParamsMaxLength,
                            paramsSize))
                    {
                        HandleEditBehaviorParams(i, paramsBuffer);
                    }
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

    // ── Behavior-specific parameter editors ───────────────────────────────────

    /// <summary>
    /// Draws the parameter UI for a <c>MoveToLocation</c> task: shows the
    /// current location summary and a "Pick Location" button that triggers an
    /// async location pick via the map canvas.
    /// </summary>
    private void DrawMoveToLocationParams(int taskIndex, string currentParams, IIosLogic logic)
    {
        bool pickingThis = IsLocationPickPending && _pendingPickTaskIndex == taskIndex;

        if (TryParseMoveToLocationParams(currentParams, out double lat, out double lon))
            ImGui.Text($"Target: {lat:F4}°N, {lon:F4}°E");
        else
            ImGui.TextDisabled("No target set");

        if (pickingThis)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0f, 1f), "[Picking…]");
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Pick Location##{taskIndex}"))
                HandlePickLocation(taskIndex, logic);
        }
    }

    /// <summary>
    /// Draws the parameter UI for a <c>FollowRoute</c> task: shows the
    /// current route entity ID and a "Pick Route" button that triggers an
    /// async entity pick filtered to the <c>road_graphs</c> layer.
    /// </summary>
    private void DrawFollowRouteParams(int taskIndex, string currentParams, IIosLogic logic)
    {
        bool pickingThis = IsEntityPickPending && _pendingPickTaskIndex == taskIndex;

        if (TryParseFollowRouteParams(currentParams, out int routeId))
            ImGui.Text($"Route entity: {routeId}");
        else
            ImGui.TextDisabled("No route set");

        if (pickingThis)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0f, 1f), "[Picking…]");
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Pick Route##{taskIndex}"))
                HandlePickEntity(taskIndex, logic, new[] { PanelConstants.FilterPresetRoadGraphs });
        }
    }

    // ── Pick completion polling ────────────────────────────────────────────────

    /// <summary>
    /// Checks pending pick tasks. When a task completes this frame the result
    /// is written into the draft mission plan as a JSON params string and the
    /// pending task state is cleared.
    /// </summary>
    private void PollPickCompletion()
    {
        if (_pendingLocationPick?.IsCompleted == true)
        {
            int idx = _pendingPickTaskIndex;
            var task = _pendingLocationPick;

            _pendingLocationPick  = null;
            _pendingPickTaskIndex = -1;

            if (!task.IsFaulted && !task.IsCanceled && idx >= 0)
            {
                var pos  = task.Result;
                string json = BuildMoveToLocationParams(pos.Latitude, pos.Longitude);
                HandleEditBehaviorParams(idx, json);
                FdpLog<MissionPanel>.Info(
                    "[IOS] LocationPick resolved: task={0} lat={1:F4} lon={2:F4}",
                    idx, pos.Latitude, pos.Longitude);
            }
        }

        if (_pendingEntityPick?.IsCompleted == true)
        {
            int idx = _pendingPickTaskIndex;
            var task = _pendingEntityPick;

            _pendingEntityPick    = null;
            _pendingPickTaskIndex = -1;

            if (!task.IsFaulted && !task.IsCanceled && idx >= 0)
            {
                int entityId = task.Result;
                string json = BuildFollowRouteParams(entityId);
                HandleEditBehaviorParams(idx, json);
                FdpLog<MissionPanel>.Info(
                    "[IOS] EntityPick resolved: task={0} entityId={1}", idx, entityId);
            }
        }
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
