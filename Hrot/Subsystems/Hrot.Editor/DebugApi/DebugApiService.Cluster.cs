using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.DebugApi;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ <c>CE-4002</c> — the three routes the cluster-membership use cases need to be tested live:
    /// <c>GET /cluster/roster</c>, <c>POST /cluster/transition</c>, <c>POST /cluster/cancel</c>.
    /// 📄 <c>docs/DESIGN_Cluster_Membership_Use_Cases.md</c> §3.1.
    ///
    /// <para>⛔⛔ <b>NOTHING HERE IS NEW BEHAVIOUR.</b> The roster is the master's own <c>NodeRoster</c>; a transition and
    /// a cancel are the SAME intents the operator's panel publishes, put onto a bus that reaches the
    /// <c>ClusterMaster</c>. ⇒ these routes are a second SURFACE on existing mechanisms, never a second mechanism.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        private readonly Func<IReadOnlyList<ClusterRosterEntry>?>? _editorClusterRoster;
        private readonly Func<string?>? _editorClusterLoadNotice;   // CE-4087

        private const string NoClusterRoster =
            "No subsystem on this host runs the ClusterMaster, so none can report its roster. Ask the node that hosts "
          + "the master (the orchestrator process, or the editor's in-process core). Check the 'cluster.roster' cell "
          + "in GET /capabilities.";

        private const string NoClusterBus =
            "No subsystem on this host has an orchestration bus that reaches the ClusterMaster, so it cannot publish "
          + "this request. Check the 'scenario.load' / 'cluster.cancel' cells in GET /capabilities.";

        /// <summary><c>GET /cluster/roster</c> — every node the master holds, as the MASTER sees it.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) GetClusterRoster()
        {
            var roster = _editorClusterRoster?.Invoke() ?? _dispatcher?.ClusterRosterAnyNode;
            if (roster == null) return (null, NoClusterRoster, DebugApiHints.Panel);

            var nodes = new JsonArray();
            var conflicts = new JsonArray();
            foreach (var r in roster)
            {
                // ⭐ CE-3354 ② — a row carrying NameConflict is a REFUSED heartbeat (its name is held by another live node), not a member
                if (r.NameConflict != null)
                {
                    conflicts.Add(new JsonObject { ["nodeId"] = r.NodeId, ["subsystem"] = r.Subsystem, ["name"] = r.Name, ["reason"] = r.NameConflict });
                    continue;
                }
                nodes.Add(new JsonObject
                {
                    ["nodeId"]                = r.NodeId,
                    ["name"]                  = r.Name,   // ⭐ CE-3354 ②
                    ["subsystem"]             = r.Subsystem,
                    ["reportedState"]         = r.ReportedState,
                    ["roles"]                 = r.Roles,
                    ["secondsSinceHeartbeat"] = r.SecondsSinceHeartbeat,
                });
            }
            // ⭐⭐ CE-4087 — what the master's LOAD GATE says: "waiting for: MuscleGround (SimHost)" while a load waits for a node
            //   holding a required role, the refusal reason after it was refused, null when no load is held. A load that is
            //   refused here is a FAILED load the operator can read — never a silent hang.
            var loadNotice = _editorClusterLoadNotice?.Invoke() ?? _dispatcher?.ClusterLoadNoticeAnyNode;
            return (new JsonObject
            {
                ["count"] = nodes.Count,
                ["nodes"] = nodes,
                ["nameConflicts"] = conflicts,   // ⭐ CE-3354 ② — heartbeats refused because another live node holds the name
                ["load"]  = new JsonObject { ["notice"] = loadNotice },
                ["note"]  = "reportedState is each node's OWN claim from its heartbeat, not the cluster's state. "
                          + "A node leaves this list after HeartbeatTimeoutSeconds of silence (default 5 s). "
                          + "load.notice is the master's load gate: a scenario load waits for a Brain and a MuscleGround node.",
            }, null, null);
        }

        /// <summary>⭐ CE-4101 — the door's move a transition to <paramref name="target"/> is, or <see langword="null"/> when it is none (the master's raw verbs by name).</summary>
        public static DoorMove? TransitionMove(ClusterState target, string? scenario) => target switch
        {
            ClusterState.Idle                                             => DoorMove.Leaving(LeaveReason.HttpStop),
            ClusterState.OperatingEdit when !string.IsNullOrWhiteSpace(scenario) => DoorMove.OpeningScenario(scenario!),
            ClusterState.OperatingLive when !string.IsNullOrWhiteSpace(scenario) => DoorMove.LoadingLive(scenario!),
            ClusterState.OperatingReplay                                  => DoorMove.OpeningRecording,
            _                                                             => null,
        };

        /// <summary>
        /// <c>POST /cluster/transition {target, scenario?, exerciseId?, timeMode?, targetWallTicks?}</c> — publish ONE
        /// <see cref="TransitionStateIntent"/>, exactly as the operator panel does. ⚠ ASYNCHRONOUS: the answer confirms
        /// the intent was PUBLISHED; read <c>GET /status</c> or <c>GET /cluster/roster</c> for the outcome.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) RequestClusterTransition(JsonNode? body, HttpUnsavedGate? gate = null)
        {
            var targetText = body?["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(targetText)
             || !Enum.TryParse<ClusterState>(targetText, ignoreCase: true, out var target)
             || !Enum.IsDefined(typeof(ClusterState), target))
                return (null,
                        "Body must name a target state: {\"target\": \"OperatingReplay\", \"exerciseId\": \"…\"}. Legal: "
                      + string.Join(", ", Enum.GetNames(typeof(ClusterState))) + ".",
                        DebugApiHints.Scenario);

            Guid exerciseId = Guid.Empty;
            var exText = body?["exerciseId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(exText) && !Guid.TryParse(exText, out exerciseId))
                return (null, $"exerciseId '{exText}' is not a GUID.", DebugApiHints.Scenario);

            // ⭐ CE-3299 D17 — the live session's recording mode: 0 / absent = FULL, N > 0 = a keyframe every N s of sim time only
            double checkpointingSeconds = 0;
            var cpText = body?["checkpointingSeconds"]?.ToString();
            if (!string.IsNullOrWhiteSpace(cpText)
                && !double.TryParse(cpText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out checkpointingSeconds))
                return (null, $"checkpointingSeconds '{cpText}' is not a number.", DebugApiHints.Scenario);

            long wallTicks = 0;
            var wtText = body?["targetWallTicks"]?.ToString();
            if (!string.IsNullOrWhiteSpace(wtText) && !long.TryParse(wtText, out wallTicks))
                return (null, $"targetWallTicks '{wtText}' is not an integer.", DebugApiHints.Scenario);

            var master = MasterOrNull;
            if (master == null) return (null, NoClusterBus, DebugApiHints.Panel);

            var intent = new TransitionStateIntent
            {
                TransactionId   = Guid.NewGuid(),
                TargetState     = target,
                TargetWallTicks = wallTicks,
                ScenarioId      = body?["scenario"]?.ToString(),
                ExerciseId      = exerciseId,
                TimeMode        = body?["timeMode"]?.ToString(),
                CheckpointingSeconds = checkpointingSeconds,
            };
            // ⭐ CE-4101 — the targets that OPEN a scenario / recording or LEAVE to Idle are the editor's door's decision (HTTP's refusing asker: a run is stopped, unsaved edits are refused with a 409 unless
            //   discarded); every other target (a Loading / Unloading state, Preview, Review) stays the master's raw verb by name, as this route always was.
            if (TransitionMove(target, intent.ScenarioId) is { } move)
            {
                var asker = HttpAsker(gate);
                HttpDoor.Admit(move, asker, gate?.Answer ?? UnsavedAnswer.Unchanged, () => master.Transition(intent));
                if (asker.Refusal is { } refusal) return (null, refusal, ConflictHint);
            }
            else master.Transition(intent);
            return (new JsonObject
            {
                ["published"]     = true,
                ["transactionId"] = intent.TransactionId.ToString(),
                ["target"]        = target.ToString(),
                ["note"]          = "Published, not completed. Pass transactionId to POST /cluster/cancel to cancel it.",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /cluster/cancel {targetId}</c> — the operator's cancel (<c>R-4004</c>), published as the panel does.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) RequestClusterCancel(JsonNode? body)
        {
            var idText = body?["targetId"]?.ToString();
            if (string.IsNullOrWhiteSpace(idText) || !Guid.TryParse(idText, out var targetId))
                return (null,
                        "Body must name the request to cancel: {\"targetId\": \"<transactionId from POST /cluster/transition>\"}.",
                        DebugApiHints.Scenario);

            var master = MasterOrNull;
            if (master == null) return (null, NoClusterBus, DebugApiHints.Panel);

            master.Cancel(new CancelOperationIntent { TargetRequestId = targetId });
            return (new JsonObject
            {
                ["published"] = true,
                ["targetId"]  = targetId.ToString(),
            }, null, null);
        }
    }
}
