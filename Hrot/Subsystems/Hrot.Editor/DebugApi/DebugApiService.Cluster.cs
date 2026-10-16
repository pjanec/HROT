using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.DebugApi;

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
        private readonly Action<CancelOperationIntent>? _editorRequestCancel;

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
            foreach (var r in roster)
                nodes.Add(new JsonObject
                {
                    ["nodeId"]                = r.NodeId,
                    ["subsystem"]             = r.Subsystem,
                    ["reportedState"]         = r.ReportedState,
                    ["roles"]                 = r.Roles,
                    ["secondsSinceHeartbeat"] = r.SecondsSinceHeartbeat,
                });
            return (new JsonObject
            {
                ["count"] = roster.Count,
                ["nodes"] = nodes,
                ["note"]  = "reportedState is each node's OWN claim from its heartbeat, not the cluster's state. "
                          + "A node leaves this list after HeartbeatTimeoutSeconds of silence (default 5 s).",
            }, null, null);
        }

        /// <summary>
        /// <c>POST /cluster/transition {target, scenario?, exerciseId?, timeMode?, targetWallTicks?}</c> — publish ONE
        /// <see cref="TransitionStateIntent"/>, exactly as the operator panel does. ⚠ ASYNCHRONOUS: the answer confirms
        /// the intent was PUBLISHED; read <c>GET /status</c> or <c>GET /cluster/roster</c> for the outcome.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) RequestClusterTransition(JsonNode? body)
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

            long wallTicks = 0;
            var wtText = body?["targetWallTicks"]?.ToString();
            if (!string.IsNullOrWhiteSpace(wtText) && !long.TryParse(wtText, out wallTicks))
                return (null, $"targetWallTicks '{wtText}' is not an integer.", DebugApiHints.Scenario);

            var publish = _editorRequestTransition ?? _dispatcher?.RequestTransitionAnyNode;
            if (publish == null) return (null, NoClusterBus, DebugApiHints.Panel);

            var intent = new TransitionStateIntent
            {
                TransactionId   = Guid.NewGuid(),
                TargetState     = target,
                TargetWallTicks = wallTicks,
                ScenarioId      = body?["scenario"]?.ToString(),
                ExerciseId      = exerciseId,
                TimeMode        = body?["timeMode"]?.ToString(),
            };
            publish(intent);
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

            var publish = _editorRequestCancel ?? _dispatcher?.RequestCancelAnyNode;
            if (publish == null) return (null, NoClusterBus, DebugApiHints.Panel);

            publish(new CancelOperationIntent { TargetRequestId = targetId });
            return (new JsonObject
            {
                ["published"] = true,
                ["targetId"]  = targetId.ToString(),
            }, null, null);
        }
    }
}
