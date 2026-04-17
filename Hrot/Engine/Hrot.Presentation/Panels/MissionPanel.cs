using Hrot.Core.Mission;
using Fdp.Core.Logging;
using ImGuiNET;
using System.Text.Json;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.UI.Common.Panels;

/// <summary>
/// Shared UI panel that displays the currently selected entity's mission plan
/// and provides buttons to send control commands (jump / abort) and to edit
/// mission task sequences.
///
/// <para>Depends on <see cref="IMissionEditorService"/> for mission reads and
/// commits, and <see cref="IMapPickService"/> for async map-pick operations.</para>
///
/// <para><b>Testing:</b> all UI-triggered logic lives in public
/// <c>Handle*</c> methods that accept the service interfaces directly; tests can
/// call these without an ImGui render frame.</para>
/// </summary>
public sealed class MissionPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private int _selectedEntityId = 0;

    private MissionPlan? _draftPlan;
    private long         _draftBaseVersion;
    private int          _draftEntityId;

    private bool                          _commitInFlight;
    private Task<MissionCommitResult>?    _pendingCommit;

    // ── Map-pick pending state ─────────────────────────────────────────────────

    /// <summary>
    /// Pending async location pick. Non-null while the operator is
    /// picking a location on the map for a <c>MoveToLocation</c> task.
    /// </summary>
    private Task<GeoPoint>? _pendingLocationPick;

    /// <summary>
    /// Pending async entity pick. Non-null while the operator is
    /// picking a route entity for a <c>FollowRoute</c> task.
    /// </summary>
    private Task<int>? _pendingEntityPick;

    /// <summary>Task index that the current pending pick is targeting.</summary>
    private int _pendingPickTaskIndex = -1;

    // ── Trigger types ─────────────────────────────────────────────────────────

    private static readonly string[] _triggerTypes =
    {
        "DoctrineFinished", "TimerElapsed", "ReachedDestination", "HealthCritical", "UnderAttack"
    };

    private const string BehaviorNameMoveToLocation = "MoveToLocation";
    private const string BehaviorNameFollowRoute    = "FollowRoute";
    private const string BehaviorNameFireAtTarget   = "FireAtTarget";

    /// <summary>Returns the default params string for the given trigger type.</summary>
    public static string GetDefaultTriggerParams(string triggerType) => triggerType switch
    {
        "TimerElapsed"   => "10.0",
        "HealthCritical" => "0.25",
        _                => ""
    };

    // ── Construction ─────────────────────────────────────────────────────────

    private readonly long _localNodeId;

    /// <summary>Creates a new mission panel.</summary>
    public MissionPanel(long localNodeId = 0) => _localNodeId = localNodeId;

    // ── Public state accessors ────────────────────────────────────────────────

    /// <summary>
    /// The entity currently shown by the panel.
    /// Set to 0 (no selection) or a valid entity ID.
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

    /// <summary>The current optimistic-lock base version used for mission commits.</summary>
    internal long TestHook_DraftBaseVersion => _draftBaseVersion;

    // ── Task icon helper (public for testability) ─────────────────────────────

    /// <summary>
    /// Returns a single Unicode glyph that visually represents the task state.
    /// </summary>
    public static string GetTaskIcon(MissionTask task, bool isActive)
    {
        if (isActive) return "▶";

        return task.State switch
        {
            eTaskState.TASK_DONE    => "✓",
            eTaskState.TASK_FAILED  => "✗",
            eTaskState.TASK_SKIPPED => "⏭",
            _                       => "⏹"
        };
    }

    // ── Button handlers (public for testability) ──────────────────────────────

    /// <summary>
    /// Handles the "JUMP" button press: sends a
    /// <see cref="eMissionCommandType.CMD_JUMP_TO_TASK"/> control command.
    /// </summary>
    public void HandleJump(IMissionEditorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_selectedEntityId == 0) return;

        _pendingCommit  = service.SendControlCommandAsync(
            _selectedEntityId,
            eMissionCommandType.CMD_JUMP_TO_TASK,
            Guid.Empty);
        _commitInFlight = true;
    }

    /// <summary>
    /// Handles the "ABORT" button press: sends a
    /// <see cref="eMissionCommandType.CMD_ABORT_ALL"/> control command.
    /// </summary>
    public void HandleAbort(IMissionEditorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_selectedEntityId == 0) return;

        _pendingCommit  = service.SendControlCommandAsync(
            _selectedEntityId,
            eMissionCommandType.CMD_ABORT_ALL,
            Guid.Empty);
        _commitInFlight = true;
    }

    // ── Draft editing handlers (public for testability) ─────────────────────

    /// <summary>Creates a fresh draft plan and assigns a base version.</summary>
    public void SetDraftPlan(MissionPlan plan, long baseVersion)
    {
        _draftPlan        = ClonePlan(plan);
        _draftBaseVersion = baseVersion;
        _draftEntityId    = _selectedEntityId;
    }

    /// <summary>Appends a new default task to the draft plan.</summary>
    public void HandleAddTask()
    {
        if (!EnsureDraftForEdit()) return;

        var tasks = GetDraftTasks();
        tasks.Add(new MissionTask
        {
            TaskId          = Guid.NewGuid(),
            ExecutingEngine = string.Empty,
            BehaviorId      = string.Empty,
            BehaviorParams  = string.Empty,
            Triggers        = new List<MissionTrigger> { new MissionTrigger { Type = "DoctrineFinished" } },
            State           = eTaskState.TASK_PLANNED
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
        if (toIndex   < 0 || toIndex   >= tasks.Count) return;
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

        var task       = tasks[index];
        task.BehaviorId = behaviorId ?? string.Empty;
        tasks[index]   = task;
    }

    /// <summary>Updates the draft task BehaviorParams JSON at the specified index.</summary>
    public void HandleEditBehaviorParams(int index, string behaviorParams)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (index < 0 || index >= tasks.Count) return;

        var task           = tasks[index];
        task.BehaviorParams = behaviorParams ?? string.Empty;
        tasks[index]       = task;
    }

    /// <summary>Updates the trigger type at the given task/trigger index and resets params to defaults.</summary>
    public void HandleEditTriggerType(int taskIndex, int triggerIndex, string newType)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (taskIndex < 0 || taskIndex >= tasks.Count) return;
        var task = tasks[taskIndex];
        if (task.Triggers != null && triggerIndex >= 0 && triggerIndex < task.Triggers.Count)
        {
            var trigger   = task.Triggers[triggerIndex];
            trigger.Type   = newType;
            trigger.Params = GetDefaultTriggerParams(newType);
            task.Triggers[triggerIndex] = trigger;
            tasks[taskIndex]           = task;
        }
    }

    /// <summary>Updates the trigger params at the given task/trigger index.</summary>
    public void HandleEditTriggerParams(int taskIndex, int triggerIndex, string newParams)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (taskIndex < 0 || taskIndex >= tasks.Count) return;
        var task = tasks[taskIndex];
        if (task.Triggers != null && triggerIndex >= 0 && triggerIndex < task.Triggers.Count)
        {
            var trigger   = task.Triggers[triggerIndex];
            trigger.Params = newParams ?? string.Empty;
            task.Triggers[triggerIndex] = trigger;
            tasks[taskIndex]           = task;
        }
    }

    /// <summary>Adds a trigger of the specified type to the task at the given index.</summary>
    public void HandleAddTrigger(int taskIndex, string type)
    {
        if (!TryGetDraftTasks(out var tasks)) return;
        if (taskIndex < 0 || taskIndex >= tasks.Count) return;
        var task = tasks[taskIndex];
        task.Triggers ??= new List<MissionTrigger>();
        task.Triggers.Add(new MissionTrigger
        {
            Type   = type,
            Params = GetDefaultTriggerParams(type)
        });
        tasks[taskIndex] = task;
    }

    /// <summary>Starts an async mission commit for the current draft plan.</summary>
    public void HandleCommit(IMissionEditorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!CanCommit) return;

        var plan = _draftPlan!;
        FdpLog<MissionPanel>.Info("[Node-{0}] Commit triggered: entityId={1} taskCount={2} baseVersion={3}",
            _localNodeId, _selectedEntityId, plan.Tasks?.Count ?? 0, _draftBaseVersion);
        _pendingCommit  = service.CommitMissionAsync(_selectedEntityId, plan, _draftBaseVersion);
        _commitInFlight = true;
    }

    /// <summary>
    /// Handles "Force Commit": commits the current draft overriding any OCC version check
    /// by passing <c>baseVersion == 0</c>.
    /// </summary>
    public void HandleForceCommit(IMissionEditorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (!CanCommit) return;
        var plan = _draftPlan!;
        FdpLog<MissionPanel>.Info("[Node-{0}] Force Commit: entity={1} tasks={2}",
            _localNodeId, _selectedEntityId, plan.Tasks?.Count ?? 0);
        _pendingCommit  = service.CommitMissionAsync(_selectedEntityId, plan, 0);
        _commitInFlight = true;
        DismissConflict();
    }

    // ── Conflict alert ─────────────────────────────────────────────────────────

    private string? _conflictMessage;

    /// <summary>True when a version-conflict commit result is pending user acknowledgement.</summary>
    public bool HasConflictAlert => _conflictMessage is not null;

    /// <summary>The conflict error message to show in the ImGui modal. <c>null</c> when no alert is active.</summary>
    public string? ConflictMessage => _conflictMessage;

    /// <summary>
    /// Inspects a <see cref="MissionCommitResult"/> and, when it represents a failure,
    /// stores the error message so <see cref="Draw"/> can surface an ImGui modal.
    /// </summary>
    public void HandleConflictResult(MissionCommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success)
            _conflictMessage = result.ErrorMessage ?? PanelConstants.VersionConflictErrorMessage;
    }

    /// <summary>Clears the active conflict alert.</summary>
    public void DismissConflict() => _conflictMessage = null;

    // ── Map-pick handlers (public for testability) ────────────────────────────

    /// <summary>
    /// Initiates an async location pick for the <c>MoveToLocation</c> task at
    /// <paramref name="index"/>.
    /// </summary>
    public void HandlePickLocation(int index, IMapPickService pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (!TryGetDraftTasks(out _)) return;
        if (index < 0) return;

        _pendingPickTaskIndex = index;
        _pendingLocationPick  = pick.PickLocationAsync();
    }

    /// <summary>
    /// Initiates an async entity pick for the <c>FollowRoute</c> task at
    /// <paramref name="index"/>.
    /// </summary>
    public void HandlePickEntity(int index, IMapPickService pick, string[]? filterPresets)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (!TryGetDraftTasks(out _)) return;
        if (index < 0) return;

        _pendingPickTaskIndex = index;
        _pendingEntityPick    = pick.PickEntityAsync(filterPresets);
    }

    /// <summary>True when an async location pick is in flight for any task.</summary>
    public bool IsLocationPickPending => _pendingLocationPick is { IsCompleted: false };

    /// <summary>True when an async entity pick is in flight for any task.</summary>
    public bool IsEntityPickPending => _pendingEntityPick is { IsCompleted: false };

    // ── JSON helpers for MoveToLocation / FollowRoute params ─────────────────

    /// <summary>
    /// Builds the canonical <c>MoveToLocation</c> behavior-params JSON.
    /// </summary>
    internal static string BuildMoveToLocationParams(double lat, double lon)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"targetLat\":{lat:F6},\"targetLon\":{lon:F6}" +
            $",\"speed\":{PanelConstants.MoveToLocationDefaultSpeed}" +
            $",\"arrivalRadius\":{PanelConstants.MoveToLocationDefaultArrivalRadius}}}");

    /// <summary>
    /// Builds the canonical <c>FollowRoute</c> behavior-params JSON.
    /// </summary>
    internal static string BuildFollowRouteParams(int routeEntityId)
        => $"{{\"routeEntityId\":{routeEntityId}}}";

    /// <summary>
    /// Builds the canonical <c>FireAtTarget</c> behavior-params JSON.
    /// </summary>
    internal static string BuildFireAtTargetParams(long targetNetworkId, int maxRounds = 0, float cooldownSeconds = 1.0f)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"targetNetworkId\":{targetNetworkId},\"maxRounds\":{maxRounds},\"cooldownSeconds\":{cooldownSeconds:F2}}}");

    /// <summary>
    /// Tries to parse the target network ID, max rounds, and cooldown seconds from a
    /// <c>FireAtTarget</c> params JSON string.
    /// </summary>
    internal static bool TryParseFireAtTargetParams(
        string json, out long targetNetworkId, out int maxRounds, out float cooldownSeconds)
    {
        targetNetworkId = 0; maxRounds = 0; cooldownSeconds = 1.0f;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;
            if (root.TryGetProperty("targetNetworkId", out var idEl))
                targetNetworkId = idEl.GetInt64();
            if (root.TryGetProperty("maxRounds", out var mrEl))
                maxRounds = mrEl.GetInt32();
            if (root.TryGetProperty("cooldownSeconds", out var csEl))
                cooldownSeconds = csEl.GetSingle();
            return targetNetworkId != 0;
        }
        catch { /* malformed */ }
        return false;
    }

    /// <summary>
    /// Tries to parse lat/lon from a <c>MoveToLocation</c> params JSON string.
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

    // ── Draw ──────────────────────────────────────────────────────────────────

    /// <summary>Renders the panel via ImGui. Called once per frame from the application shell.</summary>
    public void Draw(IMissionEditorService service, IMapPickService pick)
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        ImGui.Begin("Selection & Mission");
        DrawContent(service, pick);
        ImGui.End();
    }

    /// <summary>
    /// Renders only the panel body content (no <c>ImGui.Begin</c>/<c>End</c>).
    /// Calls <see cref="IMissionEditorService.GetAvailableBehaviors"/> each frame
    /// before the ImGui guard so that tests can verify the call without a render context.
    /// </summary>
    public void DrawContent(IMissionEditorService service, IMapPickService pick)
    {
        // Refresh behavior list before any ImGui calls so tests can verify without a render ctx.
        var behaviors = service.GetAvailableBehaviors(_selectedEntityId);

        PollCommitCompletion();
        PollPickCompletion();

        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        if (_selectedEntityId == 0)
        {
            ImGui.Text("No selection");
            return;
        }

        SyncDraftFromSnapshot(service);

        var planToShow = _draftPlan;

        ImGui.Text($"ID: {_selectedEntityId}");

        if (planToShow != null && planToShow.Tasks != null)
        {
            ImGui.Text("Mission:");
            for (int i = 0; i < planToShow.Tasks.Count; i++)
            {
                var task   = planToShow.Tasks[i];
                bool active = task.TaskId == planToShow.ActiveTaskId;

                ImGui.Text($"{GetTaskIcon(task, active)} {i + 1}.");
                ImGui.SameLine();

                var behaviorLabel = string.IsNullOrEmpty(task.BehaviorId)
                    ? "<none>"
                    : task.BehaviorId;

                if (ImGui.BeginCombo($"Behavior##{i}", behaviorLabel))
                {
                    for (int b = 0; b < behaviors.Count; b++)
                    {
                        bool selected = task.BehaviorId == behaviors[b];
                        if (ImGui.Selectable(behaviors[b], selected))
                            HandleEditBehaviorId(i, behaviors[b]);
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }

                string paramsBuffer = task.BehaviorParams ?? string.Empty;

                if (task.BehaviorId == BehaviorNameMoveToLocation)
                {
                    DrawMoveToLocationParams(i, paramsBuffer, pick);
                }
                else if (task.BehaviorId == BehaviorNameFollowRoute)
                {
                    DrawFollowRouteParams(i, paramsBuffer, pick);
                }
                else if (task.BehaviorId == BehaviorNameFireAtTarget)
                {
                    DrawFireAtTargetParams(i, paramsBuffer, pick);
                }
                else
                {
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

                if (task.Triggers != null && task.Triggers.Count > 0)
                {
                    var trigger      = task.Triggers[0];
                    string trigType  = trigger.Type   ?? "DoctrineFinished";
                    string trigParam = trigger.Params ?? string.Empty;

                    ImGui.Text("Trigger:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150f);
                    if (ImGui.BeginCombo($"##TrigType{i}", trigType))
                    {
                        for (int t = 0; t < _triggerTypes.Length; t++)
                        {
                            bool isSel = trigType == _triggerTypes[t];
                            if (ImGui.Selectable(_triggerTypes[t], isSel))
                                HandleEditTriggerType(i, 0, _triggerTypes[t]);
                            if (isSel) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(120f);
                    if (ImGui.InputText($"##TrigParams{i}", ref trigParam, 1024))
                        HandleEditTriggerParams(i, 0, trigParam);
                    ImGui.SameLine();
                    if (ImGui.Button($"Default##TrigDef{i}"))
                        HandleEditTriggerParams(i, 0, GetDefaultTriggerParams(trigType));
                }
                else
                {
                    if (ImGui.Button($"+ Add Trigger##{i}"))
                        HandleAddTrigger(i, "DoctrineFinished");
                }

                if (ImGui.SmallButton($"Up##{i}"))   HandleMoveTask(i, i - 1);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Down##{i}")) HandleMoveTask(i, i + 1);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Delete##{i}")) HandleDeleteTask(i);

                ImGui.Separator();
            }

            if (ImGui.Button("+ Add Task")) HandleAddTask();
            ImGui.SameLine();

            if (HasConflictAlert)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f),
                    "⚠ Conflict: Mission plan was modified by another operator!");
                if (ImGui.Button("Discard Draft (Reload)"))
                {
                    ClearDraft();
                    DismissConflict();
                }
                ImGui.SameLine();
                if (ImGui.Button("Force Commit (Overwrite)"))
                    HandleForceCommit(service);
            }
            else
            {
                bool commitEnabled = CommitButtonEnabled;
                if (!commitEnabled) ImGui.BeginDisabled();
                if (ImGui.Button("Commit")) HandleCommit(service);
                if (!commitEnabled) ImGui.EndDisabled();

                if (_draftPlan != null)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Discard Draft")) ClearDraft();
                }
                if (ImGui.Button("JUMP"))  HandleJump(service);
                ImGui.SameLine();
                if (ImGui.Button("ABORT")) HandleAbort(service);
            }
        }
    }

    // ── Draft helpers ───────────────────────────────────────────────────────

    private bool CanCommit
        => _selectedEntityId != 0 && _draftPlan != null && !_commitInFlight;

    private bool EnsureDraftForEdit()
    {
        if (_selectedEntityId == 0) return false;

        if (_draftPlan == null)
            _draftPlan = CreateEmptyPlan();

        _draftEntityId = _selectedEntityId;
        return true;
    }

    private List<MissionTask> GetDraftTasks()
    {
        var plan = _draftPlan!;
        if (plan.Tasks == null)
            plan.Tasks = new List<MissionTask>();
        return plan.Tasks;
    }

    private bool TryGetDraftTasks(out List<MissionTask> tasks)
    {
        tasks = null!;
        if (_draftPlan == null) return false;

        var plan = _draftPlan;
        if (plan.Tasks == null) return false;

        tasks = plan.Tasks;
        return true;
    }

    private void SyncDraftFromSnapshot(IMissionEditorService service)
    {
        if (_selectedEntityId == 0) { ClearDraft(); return; }
        if (_draftPlan != null && _draftEntityId == _selectedEntityId) return;

        var (plan, version) = service.GetMissionSnapshot(_selectedEntityId);
        _draftBaseVersion  = version;
        _draftEntityId     = _selectedEntityId;

        _draftPlan = plan != null
            ? ClonePlan(plan)
            : CreateEmptyPlan();
    }

    private void ClearDraft()
    {
        _draftPlan        = null;
        _draftBaseVersion = 0;
        _draftEntityId    = 0;
    }

    private void PollCommitCompletion()
    {
        if (!_commitInFlight || _pendingCommit == null) return;
        if (!_pendingCommit.IsCompleted) return;

        MissionCommitResult result;
        if (_pendingCommit.IsFaulted)
        {
            result = new MissionCommitResult(false, 0,
                _pendingCommit.Exception?.GetBaseException().Message);
        }
        else
        {
            result = _pendingCommit.Result;
        }

        _commitInFlight = false;
        _pendingCommit  = null;

        if (result.Success)
        {
            FdpLog<MissionPanel>.Info("[Node-{0}] Commit succeeded: entityId={1} newVersion={2}",
                _localNodeId, _selectedEntityId, result.NewVersion);
            _draftBaseVersion = result.NewVersion;
        }
        else
        {
            FdpLog<MissionPanel>.Warn("[Node-{0}] Commit failed: entityId={1} error={2}",
                _localNodeId, _selectedEntityId, result.ErrorMessage!);
            HandleConflictResult(result);
        }
    }

    /// <summary>Internal test hook: manually drives the commit-completion polling cycle.</summary>
    internal void TestHook_PollCommitCompletion() => PollCommitCompletion();

    /// <summary>Internal test hook: clears the draft plan and dismisses the conflict alert.</summary>
    public void TestHook_ClearDraftAndDismissConflict()
    {
        ClearDraft();
        DismissConflict();
    }

    private static MissionPlan CreateEmptyPlan()
        => new MissionPlan { Tasks = new List<MissionTask>() };

    private static MissionPlan ClonePlan(MissionPlan plan)
    {
        var clone = new MissionPlan
        {
            ActiveTaskId = plan.ActiveTaskId,
            Tasks        = new List<MissionTask>(plan.Tasks?.Count ?? 0)
        };

        if (plan.Tasks == null) return clone;

        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            var newTriggers = new List<MissionTrigger>();
            if (task.Triggers != null)
            {
                for (int j = 0; j < task.Triggers.Count; j++)
                {
                    var tr = task.Triggers[j];
                    newTriggers.Add(new MissionTrigger { Type = tr.Type, Params = tr.Params });
                }
            }
            clone.Tasks.Add(new MissionTask
            {
                TaskId          = task.TaskId,
                ExecutingEngine = task.ExecutingEngine,
                BehaviorId      = task.BehaviorId,
                BehaviorParams  = task.BehaviorParams,
                State           = task.State,
                Triggers        = newTriggers,
            });
        }

        return clone;
    }

    // ── Behavior-specific parameter editors ───────────────────────────────────

    private void DrawFireAtTargetParams(int taskIndex, string currentParams, IMapPickService pick)
    {
        bool pickingThis = IsEntityPickPending && _pendingPickTaskIndex == taskIndex;

        if (TryParseFireAtTargetParams(currentParams, out long targetId, out _, out _))
            ImGui.Text($"Target NetID: {targetId}");
        else
            ImGui.TextDisabled("No target set");

        if (pickingThis)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0f, 1f), "[Picking Entity...]");
        }
        else
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Pick Target##{taskIndex}"))
                HandlePickEntity(taskIndex, pick, filterPresets: null);
        }

        var paramsSize = new System.Numerics.Vector2(
            ImGui.GetContentRegionAvail().X,
            ImGui.GetTextLineHeightWithSpacing() * PanelConstants.MissionBehaviorParamsEditorLines);

        string buffer = currentParams;
        if (ImGui.InputTextMultiline($"##Params{taskIndex}", ref buffer,
                PanelConstants.MissionBehaviorParamsMaxLength, paramsSize))
        {
            HandleEditBehaviorParams(taskIndex, buffer);
        }
    }

    private void DrawMoveToLocationParams(int taskIndex, string currentParams, IMapPickService pick)
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
                HandlePickLocation(taskIndex, pick);
        }
    }

    private void DrawFollowRouteParams(int taskIndex, string currentParams, IMapPickService pick)
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
                HandlePickEntity(taskIndex, pick, new[] { PanelConstants.FilterPresetRoadGraphs });
        }
    }

    // ── Pick completion polling ────────────────────────────────────────────────

    private void PollPickCompletion()
    {
        if (_pendingLocationPick?.IsCompleted == true)
        {
            int idx  = _pendingPickTaskIndex;
            var task = _pendingLocationPick;

            _pendingLocationPick  = null;
            _pendingPickTaskIndex = -1;

            if (!task.IsFaulted && !task.IsCanceled && idx >= 0)
            {
                var pos   = task.Result;
                string json = BuildMoveToLocationParams(pos.Latitude, pos.Longitude);
                HandleEditBehaviorParams(idx, json);
                FdpLog<MissionPanel>.Info(
                    "[Node-{0}] LocationPick resolved: task={1} lat={2:F4} lon={3:F4}",
                    _localNodeId, idx, pos.Latitude, pos.Longitude);
            }
        }

        if (_pendingEntityPick?.IsCompleted == true)
        {
            int idx  = _pendingPickTaskIndex;
            var task = _pendingEntityPick;

            _pendingEntityPick    = null;
            _pendingPickTaskIndex = -1;

            if (!task.IsFaulted && !task.IsCanceled && idx >= 0)
            {
                int entityId = task.Result;

                // Build params JSON appropriate for the behavior being edited.
                string json;
                string behaviorId = (_draftPlan?.Tasks != null && idx < _draftPlan.Tasks.Count)
                    ? (_draftPlan.Tasks[idx].BehaviorId ?? string.Empty)
                    : string.Empty;

                if (behaviorId == BehaviorNameFireAtTarget)
                {
                    // Preserve existing MaxRounds / CooldownSeconds; update only TargetNetworkId.
                    string existing = (_draftPlan?.Tasks != null && idx < _draftPlan.Tasks.Count)
                        ? (_draftPlan.Tasks[idx].BehaviorParams ?? string.Empty)
                        : string.Empty;
                    TryParseFireAtTargetParams(existing, out _, out int maxRounds, out float cooldownSeconds);
                    json = BuildFireAtTargetParams((long)entityId, maxRounds, cooldownSeconds);
                }
                else
                {
                    json = BuildFollowRouteParams(entityId);
                }

                HandleEditBehaviorParams(idx, json);
                FdpLog<MissionPanel>.Info(
                    "[Node-{0}] EntityPick resolved: task={1} entityId={2} behavior={3}",
                    _localNodeId, idx, entityId, behaviorId);
            }
        }
    }
}
