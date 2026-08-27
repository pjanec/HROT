using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.Core.Mission;
using Hrot.UI.Common.Models;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// <b>Group P — mission editing (MX4b).</b> Missions are the <i>proper</i> way a behaviour attaches
    /// to an entity — as a task. Read the plan, add a task, clear the tasks, run/restart it, all over the
    /// SAME seam the editor's Mission panel commits through: <c>IMissionEditorService</c>.
    ///
    /// <para><b>One path, edit-time == runtime.</b> Every write here is
    /// <c>GetMissionSnapshot → modify → CommitMissionAsync(id, plan, baseVersion)</c> — a full-mission
    /// replace with optimistic concurrency, exactly as <see cref="Hrot.UI.Common.Panels.MissionPanel"/>
    /// does it (compare <c>HandleAddTask</c>/<c>HandleCommit</c>). ⛔ There is NO parallel write API and
    /// NO behaviour-name mapper: a task carries the behaviour name pass-through, and its params ride as the
    /// raw JSON string the engine parses (<c>MissionControlBehaviorParamsHelper</c> reads it with plain
    /// <c>System.Text.Json</c>) — the SAME string the panel's params editor stores in
    /// <see cref="MissionTask.BehaviorParams"/>.</para>
    ///
    /// <para><b>The commit is asynchronous and resolves across frames.</b>
    /// <c>CommitMissionAsync</c>/<c>SendControlCommandAsync</c> publish a <c>MissionControlIntent</c> and
    /// hand back a <see cref="Task{T}"/> that completes only when the editor loop's <c>PollAcks()</c> reads
    /// the correlated <c>MissionControlAckEvent</c>. ⇒ ⚠ the <c>Begin*</c> methods here run on the main
    /// thread (they read the snapshot and publish the intent), but the returned task MUST be awaited OFF the
    /// main thread — awaiting it on the main thread would deadlock the very loop that resolves it. The host
    /// awaits with a bounded timeout (see <c>AwaitMissionCommitAsync</c>).</para>
    ///
    /// <para><b>OCC.</b> <c>GetMissionSnapshot</c> returns the plan and its version; the edit passes that
    /// version straight back to <c>CommitMissionAsync</c>. A stale version yields a failed
    /// <see cref="MissionCommitResult"/> (<c>ERR_VERSION_CONFLICT</c>), which the host surfaces as a 409 —
    /// ⛔ never a silent overwrite. ⚠ The offline editor adapter (<c>EditorMissionService</c>) does not yet
    /// persist a snapshot version — <c>GetMissionSnapshot</c> reports <c>0</c>, and a <c>baseVersion</c> of
    /// <c>0</c> bypasses the engine's OCC check by design — so a conflict cannot arise there today; the
    /// route still passes the version it read so the guard engages the moment the adapter tracks one.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ── read ────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>GET /missions/{networkId}</c> — the entity's mission plan (tasks + specs) and its OCC
        /// version. An entity with no active mission is a valid answer, not an error: it returns an empty
        /// task list, so an agent can add the first task without a special case. Must run on the main thread.
        /// </summary>
        public (JsonNode? result, string? error, string? hintCategory) GetMission(long networkId)
        {
            if (_missionEditor is null)
                return (null,
                    "No mission service is wired into this host, so missions cannot be read or edited.",
                    DebugApiHints.MissionTask);

            if (!_entityMap.TryGetEntity(networkId, out _))
                return (null, $"Entity {networkId} not found. List entities with GET /entities.", DebugApiHints.Entity);

            var (plan, version) = _missionEditor.GetMissionSnapshot(networkId);
            return (SerializeMission(networkId, plan, version), null, null);
        }

        // ── writes (snapshot → modify → commit) ─────────────────────────────────

        /// <summary>
        /// <c>POST /missions/{networkId}/task {behavior, params?, triggers?}</c> — append one mission task
        /// naming <paramref name="behavior"/> with <paramref name="paramsNode"/> as its param JSON, then
        /// commit the whole plan. Mirrors <c>MissionPanel.HandleAddTask</c>: a fresh <see cref="Guid"/>
        /// task id, an empty executing engine, and a default <c>BehaviorFinished</c> trigger when none is
        /// given. Runs on the main thread; the returned task must be awaited off it.
        /// </summary>
        /// <returns>
        /// <c>commit</c> — the in-flight commit task, or <see langword="null"/> when <c>error</c> is set;
        /// <c>meta</c> — the payload the host merges the ack version into on success.
        /// </returns>
        public (Task<MissionCommitResult>? commit, JsonNode? meta, string? error, string? hintCategory) BeginAddMissionTask(
            long networkId, string? behavior, JsonNode? paramsNode, JsonNode? triggersNode)
        {
            if (string.IsNullOrWhiteSpace(behavior))
                return (null, null,
                    "behavior is required — the name of the behaviour this task runs.", DebugApiHints.MissionTask);

            if (!TrySnapshotForEdit(networkId, out var plan, out var version, out var error, out var hint))
                return (null, null, error, hint);

            var task = new MissionTask
            {
                TaskId          = Guid.NewGuid(),
                ExecutingEngine = string.Empty,
                BehaviorId      = behavior!,
                // The engine parses BehaviorParams with plain System.Text.Json, and the panel stores the
                // raw JSON string here — so pass the caller's params through verbatim, no re-encoding.
                BehaviorParams  = paramsNode?.ToJsonString() ?? string.Empty,
                Triggers        = BuildTriggers(triggersNode),
                State           = eTaskState.TASK_PLANNED,
            };
            plan.Tasks.Add(task);

            var meta = new JsonObject
            {
                ["networkId"] = networkId,
                ["taskId"]    = task.TaskId.ToString("D"),
                ["behavior"]  = behavior,
                ["taskCount"] = plan.Tasks.Count,
            };
            return (_missionEditor!.CommitMissionAsync(networkId, plan, version), meta, null, null);
        }

        /// <summary>
        /// <c>DELETE /missions/{networkId}/tasks</c> — clear every task (so a fresh sequence can be added),
        /// by committing an empty plan through the same OCC path. Runs on the main thread; await the task off it.
        /// </summary>
        public (Task<MissionCommitResult>? commit, JsonNode? meta, string? error, string? hintCategory) BeginClearMissionTasks(
            long networkId)
        {
            if (!TrySnapshotForEdit(networkId, out var plan, out var version, out var error, out var hint))
                return (null, null, error, hint);

            plan.Tasks.Clear();
            plan.ActiveTaskId = Guid.Empty;

            var meta = new JsonObject
            {
                ["networkId"] = networkId,
                ["taskCount"] = 0,
            };
            return (_missionEditor!.CommitMissionAsync(networkId, plan, version), meta, null, null);
        }

        /// <summary>
        /// <c>POST /missions/{networkId}/run {restart?}</c> — run (or restart) the mission by jumping to
        /// its first task. Both map to <c>CMD_JUMP_TO_TASK</c> with an empty target id, which the execution
        /// system resolves to task index 0 and resets the phase clock — so "run" and "restart" are the same
        /// jump-to-start operation the mechanism offers. Runs on the main thread; await the task off it.
        /// </summary>
        public (Task<MissionCommitResult>? commit, JsonNode? meta, string? error, string? hintCategory) BeginRunMission(
            long networkId, bool restart)
        {
            if (_missionEditor is null)
                return (null, null,
                    "No mission service is wired into this host, so missions cannot be run.", DebugApiHints.MissionTask);

            if (!_entityMap.TryGetEntity(networkId, out _))
                return (null, null, $"Entity {networkId} not found. List entities with GET /entities.", DebugApiHints.Entity);

            var meta = new JsonObject
            {
                ["networkId"] = networkId,
                ["restart"]   = restart,
            };
            // Guid.Empty → the execution system jumps to task index 0 (start), resetting the phase clock.
            return (_missionEditor.SendControlCommandAsync(networkId, eMissionCommandType.CMD_JUMP_TO_TASK, Guid.Empty),
                    meta, null, null);
        }

        // ── shared plumbing ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the entity, checks the service is wired, and reads a MUTABLE plan draft (never null —
        /// an entity with no mission yields an empty plan) plus the OCC version to commit against.
        /// </summary>
        private bool TrySnapshotForEdit(
            long networkId, out MissionPlan plan, out long version, out string? error, out string? hintCategory)
        {
            plan    = new MissionPlan();
            version = 0;
            error   = null;
            hintCategory = null;

            if (_missionEditor is null)
            {
                error = "No mission service is wired into this host, so missions cannot be edited.";
                hintCategory = DebugApiHints.MissionTask;
                return false;
            }

            if (!_entityMap.TryGetEntity(networkId, out _))
            {
                error = $"Entity {networkId} not found. List entities with GET /entities.";
                hintCategory = DebugApiHints.Entity;
                return false;
            }

            var (snapshot, snapshotVersion) = _missionEditor.GetMissionSnapshot(networkId);
            version = snapshotVersion;
            plan    = snapshot ?? new MissionPlan();
            plan.Tasks ??= new List<MissionTask>();
            return true;
        }

        /// <summary>
        /// Builds the task's trigger list from an optional <c>triggers</c> array of <c>{ type, params? }</c>.
        /// Defaults to a single <c>BehaviorFinished</c> trigger (as <c>MissionPanel.HandleAddTask</c> does)
        /// so a new task is well-formed and can transition; an explicit empty array leaves it untriggered.
        /// </summary>
        private static List<MissionTrigger> BuildTriggers(JsonNode? triggersNode)
        {
            if (triggersNode is not JsonArray arr)
                return new List<MissionTrigger> { new MissionTrigger { Type = "BehaviorFinished" } };

            var triggers = new List<MissionTrigger>(arr.Count);
            foreach (var item in arr)
            {
                if (item is null) continue;
                var type = item["type"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(type)) continue;
                triggers.Add(new MissionTrigger
                {
                    Type   = type!,
                    Params = item["params"]?.ToJsonString() ?? item["params"]?.GetValue<string>() ?? string.Empty,
                });
            }
            return triggers;
        }

        /// <summary>The mission plan as the API reports it: tasks with their behaviour, params, triggers, and OCC version.</summary>
        private static JsonNode SerializeMission(long networkId, MissionPlan? plan, long version)
        {
            var tasks = new JsonArray();
            if (plan?.Tasks is not null)
            {
                foreach (var t in plan.Tasks)
                {
                    var triggers = new JsonArray();
                    if (t.Triggers is not null)
                        foreach (var trig in t.Triggers)
                            triggers.Add(new JsonObject
                            {
                                ["type"]   = trig.Type,
                                ["params"] = trig.Params,
                            });

                    tasks.Add(new JsonObject
                    {
                        ["taskId"]          = t.TaskId.ToString("D"),
                        ["behaviorId"]      = t.BehaviorId,
                        ["behaviorParams"]  = t.BehaviorParams,
                        ["executingEngine"] = t.ExecutingEngine,
                        ["state"]           = t.State.ToString(),
                        ["triggers"]        = triggers,
                    });
                }
            }

            return new JsonObject
            {
                ["networkId"] = networkId,
                ["plan"]      = new JsonObject
                {
                    ["activeTaskId"] = plan?.ActiveTaskId.ToString("D"),
                    ["tasks"]        = tasks,
                },
                ["version"] = version,
            };
        }
    }
}
