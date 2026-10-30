using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.RunMode;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <c>R-3052</c>…<c>R-3055</c> — <b>the routes that drive the cluster: preview, checkpoint, replay, seek — each one master intent,
    /// on every host.</b> 📄 <c>docs/DESIGN_Http_Control_Through_The_Cluster_Master.md</c> §3.
    /// </summary>
    /// <remarks>
    /// <para>⭐ The editor is a one-node cluster whose master reads its own bus, so these routes have no editor arm: ⛔ no
    /// <c>IPreviewController</c> shortcut, no preview snapshot called a checkpoint, no replay world of the debug service's own.</para>
    /// <para>⭐ Entering a replay is ONE request from any state: the planner plans the way through the intermediate states (R-3055).</para>
    /// <para>⚠ Every method PUBLISHES and returns; the host's route waits for the cluster state when asked (<c>waitForReady</c>).</para>
    /// </remarks>
    public sealed partial class DebugApiService
    {
        private const string NoMaster =
            "No subsystem on this host has an orchestration bus that reaches the ClusterMaster, so it cannot publish this "
          + "request. Check the 'scenario.load' cell in GET /capabilities.";

        /// <summary>
        /// ⭐⭐⭐ <c>CE-3344</c> (R-3069) — <b>why the cluster cannot be operated from this host right now</b> (<c>ClusterView.NotOperableReason</c>: waiting for a mandatory
        /// subsystem or role, Degraded, no master, this node not in sync), or <see langword="null"/> when it can — and when this host keeps no cluster read model (then no gate
        /// refuses; the master's own refusal is the authority). ONE fact, the same text on every host: what the 409 of every cluster route says.
        /// </summary>
        public string? ClusterNotOperableReason => ClusterNotOperableReasonKnown()?.Reason;

        /// <summary>⭐ CE-1451 — THE one read of the host's run choice: the provider's own (independent of any drive — the IG has none), else the time facade's.</summary>
        private Hrot.UI.Common.RunMode.RunModeChoice? RunChoiceOrNull
        {
            // ⛔ the fallback keeps its old refusal (NOT_SUPPORTED_HERE for a host with no time facade): /status reads it through TryRead, the routes answer 501
            get => _dispatcher?.RunChoiceAnyNode ?? _time.RunChoice;
        }

        /// <summary>⭐ CE-1451 — <c>GET /status.load</c>: THIS node's phase / load step / failure (<c>ClusterView.LocalPhase/LocalStep/LocalReason</c>, the heartbeat's) and the cluster's load progress; null with no view.</summary>
        /// <summary>⭐ CE-3354 ② — <c>GET /status.node</c>: the answering node's <c>{name, nodeId}</c>, from its cluster view (the same view <c>load</c> reads).</summary>
        private JsonNode? NodeOrNull()
            => (_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode) is { NodeId: { } id } view
                ? new JsonObject { ["name"] = view.NodeName ?? "", ["nodeId"] = id }
                : null;

        /// <summary>⭐ CE-3354 ⑦ — the load <c>/status.load</c> follows: the master's broadcast current load (the cluster view), else the last this API sent.</summary>
        private (Guid RequestId, string? Scenario)? CurrentLoad
            => (_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode) is { LastLoadRequestId: var id } view && id != Guid.Empty
                ? (id, view.LastLoadScenario)
                : _lastLoad is { } mine ? (mine.RequestId, mine.Scenario) : null;

        private JsonNode? LoadStatusOrNull()
        {
            if ((_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode) is not { } view) return null;
            return new JsonObject
            {
                ["localPhase"] = view.LocalPhase.ToString(),
                ["localStep"] = view.LocalStep,
                ["localReason"] = view.LocalReason,
                ["clusterProgress"] = view.LoadProgress,
                ["playReady"] = view.IsLocalPlayReady,
                // ⭐⭐ CE-3354 ④ — the LAST load this API sent and its phase over the WHOLE cluster: Loaded only when every node's load chain resolved
                //   (the master's Success for that request), Failed with the master's reason; null fields when this API sent no load yet
                //   ⭐ CE-3354 ⑦ — WHICH load is the MASTER's fact (the id it broadcasts with the cluster state — whoever sent it: this API, the command line, the operator panel, another node);
                //   this API's own last load is only the fallback for a master that broadcasts none.
                ["requestId"] = (CurrentLoad is { } cur ? cur.RequestId : (Guid?)null)?.ToString(),
                ["scenario"]  = CurrentLoad?.Scenario,
                ["phase"]     = CurrentLoad is { } c1 ? LoadPhaseOf(c1.RequestId)?.Phase : null,
                ["reason"]    = CurrentLoad is { } c2 ? LoadPhaseOf(c2.RequestId)?.Reason : null,
            };
        }

        /// <summary>⭐ CE-1451 — <c>GET /status.runGate</c>: the run gate the host's controls read (<c>RunModeChoice.Gate</c>); null where the host has no run choice.</summary>
        private JsonNode? RunGateOrNull()
            => RunChoiceOrNull?.Gate is { } g
                ? new JsonObject { ["open"] = g.Open, ["reason"] = g.Reason, ["loading"] = g.Loading, ["progress"] = g.Progress, ["inError"] = g.InError }
                : null;

        /// <summary>⭐ CE-1451 — <c>GET /status.loadingScreen</c>: whether the node's loading screen is up and what it says; null where none is composed.</summary>
        private JsonNode? LoadingScreenOrNull()
            => Hrot.UI.Common.Panels.LoadingScreen.Of(RunChoiceOrNull) is { } s ? new JsonObject { ["visible"] = s.Visible, ["text"] = s.Text } : null;

        /// <summary>⭐ CE-3344 — the same, with "this host keeps no cluster read model" (<see langword="null"/>) told apart from "operable" (<c>Reason == null</c>).</summary>
        private (string? Reason, bool Known)? ClusterNotOperableReasonKnown()
            => (_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode) is { } view ? (view.NotOperableReason, true) : null;

        /// <summary>
        /// ⭐⭐ <c>CE-4090</c> (AQ108 D14, D22, §5a) — <b>"this run was not recorded", from the master's broadcast</b>: <see langword="true"/> when the
        /// run the cluster shows started with <i>Record live runs</i> off; <see langword="null"/> when no node here tracks the cluster state
        /// (then no gate refuses — the master's own refusal is the authority). The same fact on every host.
        /// </summary>
        private bool? RunNotRecordedOrNull => _editorRunNotRecorded?.Invoke() ?? _dispatcher?.RunNotRecordedAnyNode;

        /// <summary>⭐ CE-4090 §5a — the refusal every recording-dependent route answers (HTTP 409, <see cref="RunRecordingGates.NotRecordedReason"/>); <see langword="null"/> when the run is recorded.</summary>
        private (JsonNode? Result, string? Error, string? HintCategory)? RefuseWhenNotRecorded(string what)
            => RunNotRecordedOrNull == true
                ? (null, $"{RunRecordingGates.NotRecordedReason}: {what} needs the run's recording (start the run with 'Record live runs' on — POST /run-settings {{\"recordLiveRuns\": true}}).", DebugApiHints.Scenario)
                : null;

        /// <summary>
        /// ⭐⭐ <c>CE-4097</c> part two (AQ108 D22) — <c>POST /sim/stop</c>: the SAME <see cref="Hrot.UI.Common.Facades.ITimeTransportFacade.Stop"/> the toolbar's ■ calls (so the
        /// run choice's R2 — Live back to the scenario's start, the armed mode kept — applies to a request exactly as to a click). 409 where there is nothing to stop (editing, Idle):
        /// leaving editing is unloading the scenario, a load route's job.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory, int Status) Stop()
        {
            var time = _time;
            var from = time.Mode;
            if (!time.IsStopEnabled)
                return (null, $"Nothing to stop while the cluster is in {from}: ■ leaves a run (a dry run, a live run, a DAR, an AAR); leaving editing is unloading the scenario — POST /scenario/load/edit or /scenario/load/live.", DebugApiHints.Scenario, 409);
            time.Stop();
            return (new JsonObject { ["published"] = true, ["stoppedFrom"] = from.ToString(), ["armedMode"] = ArmedModeText(), ["state"] = GetSimState() }, null, null, 200);
        }

        /// <summary>⭐ CE-4097 part two — the host's armed run mode as a word (<c>preview</c> | <c>live</c> | <c>aar</c>); <see langword="null"/> when this host has no run choice.</summary>
        private string? ArmedModeText() => RunChoiceOrNull?.Armed.ToString().ToLowerInvariant();

        /// <summary>
        /// ⭐⭐ <c>CE-4097</c> part two (AQ108 D22) — <c>POST /run-mode {armed: preview|live|aar, confirm?}</c>: a thin route over the indicator's <see cref="Hrot.UI.Common.RunMode.RunModeChoice.Choose"/>
        /// (for agents that drive the UI). Where the indicator would ASK first (a run would end) the route answers 409 with the confirmation's text, and does the move only when
        /// <c>confirm: true</c> accepts it — ⛔ an agent never confirms by accident. ⭐ The armed mode is the ONLY thing it sets; ▶ ⏸ ■ and the HTTP time routes never follow it.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory, int Status) SetRunMode(JsonNode? body)
        {
            var choice = RunChoiceOrNull;
            if (choice is null)
                return (null, "No run-mode choice on this host's time facade (the Replay Browser and a host with no time facade have none).", DebugApiHints.Panel, 503);
            var text = body?["armed"]?.GetValue<string>();
            if (!Enum.TryParse<Hrot.UI.Common.RunMode.RunArmed>(text, ignoreCase: true, out var target))
                return (null, "armed (preview | live | aar) is required: {\"armed\": \"live\"}.", DebugApiHints.Scenario, 400);
            bool confirm = body?["confirm"] is JsonValue c && c.TryGetValue(out bool yes) && yes;

            var move = choice.Moves().FirstOrDefault(m => m.Target == target);
            if (move is null) return (null, "A DAR has its own exit (POST /review/exit); the armed mode cannot be changed inside it.", DebugApiHints.Scenario, 409);
            if (!move.Enabled) return (null, $"{target} cannot be armed now: {move.DisabledReason}.", DebugApiHints.Scenario, 409);

            choice.Choose(target);
            if (choice.Pending is { } pending)
            {
                if (!confirm)
                {
                    var ask = pending.Text;
                    pending.Resolve(pending.Buttons[^1]);   // Cancel — the question is not left standing for the next click
                    return (null, $"{ask} — send confirm:true to accept.", DebugApiHints.Scenario, 409);
                }
                pending.Resolve(pending.Buttons[0]);
            }
            return (new JsonObject { ["armedMode"] = ArmedModeText(), ["mode"] = _time.Mode.ToString() }, null, null, 200);
        }

        /// <summary>⭐ CE-4090 + CE-3341 — <c>GET /run-settings</c>: <c>{recordLiveRuns, armed}</c>, read off the master's broadcast (the same fact on every host;
        /// <c>armed</c> is <c>preview</c> | <c>live</c> | <c>aar</c>, ⭐ the CLUSTER's armed mode — not this host's <c>/status.armedMode</c>); a host
        /// that tracks no cluster read model answers 503-class "No subsystem…".</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) GetRunSettings()
        {
            var off = _editorRecordLiveRunsOff?.Invoke() ?? _dispatcher?.RecordLiveRunsOffAnyNode;
            if (off is null)
                return (null, "No subsystem on this host keeps the cluster's read model (a ClusterUiCache / ClusterSlave) that carries the run settings.", DebugApiHints.Panel);
            var armed = _editorArmedMode?.Invoke() ?? _dispatcher?.ArmedModeAnyNode ?? Fdp.Toolkit.Orchestration.ClusterArmedMode.Live;   // the same View fact as `off`; Live = the master's default
            var ask = _editorAskToKeepRecordings?.Invoke() ?? _dispatcher?.AskToKeepRecordingsAnyNode ?? false;   // ⭐ AQ108 D15 (CE-4100 ①) — the same View fact; OFF = the master's default
            var mapNodes = new JsonArray();
            foreach (var n in MapInLoadNodesNow()) mapNodes.Add(n);   // ⭐ CE-3354 ③ — the same View fact
            return (new JsonObject { ["recordLiveRuns"] = !off.Value, ["armed"] = armed.ToString().ToLowerInvariant(), ["askToKeepRecordings"] = ask, ["mapInLoadNodes"] = mapNodes }, null, null);
        }

        /// <summary>⭐ CE-3354 ③ — the master's map-in-load set as this host's cluster view last heard it (empty where there is no view).</summary>
        internal IReadOnlyList<string> MapInLoadNodesNow()
            => (_editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode)?.MapInLoadNodes ?? Array.Empty<string>();

        /// <summary>
        /// ⭐ <c>CE-4090</c> (AQ108 D14) — <c>POST /run-settings {recordLiveRuns: bool}</c>: the cluster-wide <i>Record live runs</i> setting (default ON),
        /// held by the MASTER and applied from the NEXT live load; the run on screen is not touched. Any host asks.
        /// ⭐ <b>CE-3341</b> (AQ108 D12a): the body may also carry <c>armed: preview|live|aar</c> — the cluster's armed mode, the SAME op the operator UI sends; at least one of
        /// the two fields is required, an absent one changes nothing. ⛔ No load route sends an armed mode (D22).
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SetRunSettings(JsonNode? body)
        {
            const string usage = "recordLiveRuns (true | false), armed (preview | live | aar), askToKeepRecordings (true | false), mapInLoad (true | false: this node) and / or mapInLoadNodes ([names]) is required: {\"recordLiveRuns\": false}, {\"armed\": \"live\"}, {\"askToKeepRecordings\": true}, {\"mapInLoad\": true} or {\"mapInLoadNodes\": [\"film-cam\"]}.";
            bool? record = null;
            if (body?["recordLiveRuns"] is { } rv)
            {
                if (rv is not JsonValue v || !v.TryGetValue(out bool b)) return (null, usage, DebugApiHints.Scenario);
                record = b;
            }
            Fdp.Toolkit.Orchestration.ClusterArmedMode? armed = null;
            if (body?["armed"] is { } av)
            {
                if (av is not JsonValue s || !s.TryGetValue(out string? text)
                    || !Enum.TryParse<Fdp.Toolkit.Orchestration.ClusterArmedMode>(text, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed))
                    return (null, usage, DebugApiHints.Scenario);
                armed = parsed;
            }
            bool? ask = null;
            if (body?["askToKeepRecordings"] is { } kv)
            {
                if (kv is not JsonValue k || !k.TryGetValue(out bool kb)) return (null, usage, DebugApiHints.Scenario);
                ask = kb;
            }
            // ⭐ CE-3354 ③ — the map-in-load set: `mapInLoadNodes: [names]` REPLACES it; `mapInLoad: true|false` adds / removes THIS node's name (the one /status.node reports).
            string[]? mapNodes = null;
            if (body?["mapInLoadNodes"] is { } mv)
            {
                if (mv is not JsonArray arr) return (null, usage, DebugApiHints.Scenario);
                var names = new List<string>();
                foreach (var item in arr)
                {
                    if (item is not JsonValue iv || !iv.TryGetValue(out string? name) || string.IsNullOrWhiteSpace(name) || name.Contains(','))
                        return (null, "mapInLoadNodes holds node names: non-empty strings without commas.", DebugApiHints.Scenario);
                    names.Add(name.Trim());
                }
                mapNodes = names.ToArray();
            }
            if (body?["mapInLoad"] is { } ml)
            {
                if (ml is not JsonValue mlv || !mlv.TryGetValue(out bool on)) return (null, usage, DebugApiHints.Scenario);
                if (mapNodes != null) return (null, "Send mapInLoad (this node) or mapInLoadNodes (the whole set), not both.", DebugApiHints.Scenario);
                var view = _editorClusterView?.Invoke() ?? _dispatcher?.ClusterViewAnyNode;
                if (view?.NodeName is not { Length: > 0 } mine)
                    return (null, "This host has no node name to add (it keeps no cluster view). Name the nodes with mapInLoadNodes.", DebugApiHints.Panel);
                var set = MapInLoadNodesNow().Where(n => !string.Equals(n, mine, StringComparison.OrdinalIgnoreCase)).ToList();
                if (on) set.Add(mine);
                mapNodes = set.ToArray();
            }
            if (record is null && armed is null && ask is null && mapNodes is null) return (null, usage, DebugApiHints.Scenario);
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            var intent = new SetRunSettingsIntent { RequestId = Guid.NewGuid(), RecordLiveRuns = record, Armed = armed, AskToKeepRecordings = ask, MapInLoadNodes = mapNodes };
            master.SetRunSettings(intent);
            var result = new JsonObject
            {
                ["published"] = true, ["requestId"] = intent.RequestId.ToString(),
                ["note"] = "recordLiveRuns applies from the next live load; the run on screen keeps its mode (GET /status → recording). armed is the cluster's armed mode (GET /run-settings).",
            };
            if (record is { } r) result["recordLiveRuns"] = r;
            if (armed is { } a) result["armed"] = a.ToString().ToLowerInvariant();
            if (ask is { } k2) result["askToKeepRecordings"] = k2;
            if (mapNodes != null) { var arr2 = new JsonArray(); foreach (var n in mapNodes) arr2.Add(n); result["mapInLoadNodes"] = arr2; }
            return (result, null, null);
        }

        /// <summary>
        /// ⭐ <c>CE-4090</c> (AQ108 D15) — <c>DELETE /exercises/{id}</c>: discard an UNARCHIVED recording (its ledger entry and staging copies —
        /// <c>ExerciseRetention.Delete</c>, CE-3218). 404 for an id the master's inventory does not list (a kept exercise stays); the master
        /// refuses the run still recording.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory, int Status) DeleteExercise(string? idText)
        {
            if (!Guid.TryParse(idText, out var id))
                return (null, $"exerciseId '{idText}' is not an exercise id (GET /exercises lists them).", DebugApiHints.Scenario, 400);
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel, 503);
            var items = _editorExercises?.Invoke() ?? _dispatcher?.ExercisesAnyNode;
            if (items is null) return (null, "No subsystem on this host keeps the cluster's exercise inventory.", DebugApiHints.Panel, 503);
            if (!items.Any(x => x.ExerciseId == id))
                return (null, $"No unarchived exercise {id} (GET /exercises lists them); a kept exercise is not deleted here.", DebugApiHints.Scenario, 404);
            var intent = new ExecuteStorageOpIntent { RequestId = Guid.NewGuid(), Operation = StorageOpType.Delete, ExerciseId = id };
            master.Storage(intent);
            return (new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString(), ["exerciseId"] = id.ToString() }, null, null, 200);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4091</c> (AQ108 D7, D22) — <c>POST /scenario/load/edit {recorded}</c>: the scenario NAME that opens exercise <paramref name="idText"/>'s
        /// scenario copy as a new, unsaved scenario (<see cref="RecordedScenarioNames"/>). Only the name is made here — ⛔ nothing is written: the
        /// master's prefetch puts the copy under that name before it stages it to the nodes. 404 for an id the exercise inventory (when this host
        /// keeps one) does not list; a recording with no copy fails the load at the master, like any scenario that is not there.
        /// </summary>
        public (string? Name, string? Error, string? HintCategory, int Status) RecordedScenarioName(string? idText)
        {
            if (!Guid.TryParse(idText, out var id) || id == Guid.Empty)
                return (null, $"recorded '{idText}' is not an exercise id (GET /exercises lists them).", DebugApiHints.Scenario, 400);
            var items = _editorExercises?.Invoke() ?? _dispatcher?.ExercisesAnyNode;
            if (items is not null && !items.Any(x => x.ExerciseId == id))
                return (null, $"No recording {id} (GET /exercises lists them).", DebugApiHints.Scenario, 404);
            return (RecordedScenarioNames.ForExercise(id), null, null, 200);
        }

        // ── ⭐⭐ CE-4093 (AQ108 D20, D22) — the unsaved-changes CHECK ROUND ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>POST /scenario/check-unsaved</c>, first half — asks the master for a check round (every node compares its whole world with its baseline) and returns
        /// the request id; the host's route waits for the answer (<see cref="UnsavedCheckOutcomeOrNull"/>). ⛔ Never answers from a cache: only a round the master ran.
        /// </summary>
        public (Guid RequestId, string? Error, string? HintCategory) StartUnsavedCheck()
        {
            try { return UnsavedAsker.Start() is { } id ? (id, null, null) : (Guid.Empty, NoMaster, DebugApiHints.Panel); }
            catch (InvalidOperationException ex) { return (Guid.Empty, ex.Message, DebugApiHints.Scenario); }
        }

        /// <summary>⭐⭐ CE-1440 (R-3066 audit #13) — the ONE asker (<see cref="Hrot.UI.Common.RunMode.UnsavedCheckAsker"/>) over this service's master and read model; the toolbar's move uses the same type.</summary>
        public Hrot.UI.Common.RunMode.UnsavedCheckAsker UnsavedAsker
            => new(() => MasterOrNull, new Hrot.UI.Common.RunMode.UnsavedCheckReads(() => ObservesUnsavedChecks, UnsavedCheckOutcomeOrNull));

        /// <summary>⭐ CE-4093 — how check round <paramref name="requestId"/> ended (the master's answer, or its refusal); null while it has not.</summary>
        public UnsavedCheckOutcome? UnsavedCheckOutcomeOrNull(Guid requestId)
            => _editorUnsavedCheck?.Invoke(requestId) ?? _dispatcher?.UnsavedCheckAnyNode(requestId);

        /// <summary>⭐ CE-4093 — this host can see check-round answers (it keeps a cluster read model fed by the master); a host that cannot must not gate on them.</summary>
        public bool ObservesUnsavedChecks => _editorUnsavedCheck != null || _dispatcher?.ObservesUnsavedChecksAnyNode == true;

        /// <summary>⭐ CE-4093 (D22) — <c>GET /status.scenarioDirty</c>: the LAST check round's verdict; null when none ran (or the cluster left editing since). ⛔ Never runs a round.</summary>
        public bool? ScenarioDirtyOrNull => _editorScenarioDirty?.Invoke() ?? _dispatcher?.ScenarioDirtyAnyNode;

        /// <summary>⭐ CE-4093 — the scenario this host has open (the editor driver's name), or null when this host has no editor driver / none is open.</summary>
        public string? LoadedScenarioNameOrNull()
        {
            try { return _editor.LoadedScenarioName; }
            catch (Hrot.Presentation.DebugApi.NotSupportedHereException) { return null; }
        }

        /// <summary>⭐ CE-4093 — the cluster is open for editing: the only state in which unsaved changes can be lost (and the only one the master checks).</summary>
        public bool IsEditingScenario => CurrentClusterStateOrNull() == ClusterState.OperatingEdit;

        /// <summary><c>POST /preview/enter {startPaused?}</c> — the cluster's dry run (<c>OperatingPreview</c>). ⛔ Never recorded (R-3053).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) EnterPreview(bool startPaused = false)
        {
            if (CurrentClusterStateOrNull() == ClusterState.OperatingPreview)
                return (new JsonObject { ["published"] = false, ["target"] = nameof(ClusterState.OperatingPreview), ["note"] = "Already in preview." }, null, null);
            // ⭐⭐ CE-1440 (R-3066 audit #11) — the ONE construction of the dry run's intent (the toolbar's ▶ from Edit builds it too).
            //   ⚠ Behaviour change: a RUNNING preview now says TimeMode "Continuous" (it was null ⇒ the master left the clock paused:
            //   GlobalContextProcessManager resumes a preview only on "Continuous"), so POST /preview/enter {startPaused:false} runs, as ▶ does.
            return PublishTransition(Hrot.UI.Common.Facades.DryRunIntents.Enter(startPaused));
        }

        /// <summary><c>POST /preview/exit</c> — back to <c>OperatingEdit</c>: the dry run is rewound.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) ExitPreview()
        {
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingPreview && s != ClusterState.LoadingPreview)
                return (new JsonObject { ["published"] = false, ["target"] = nameof(ClusterState.OperatingEdit), ["note"] = "Not in preview." }, null, null);
            return PublishTransition(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.OperatingEdit });
        }

        /// <summary><c>POST /checkpoint {label?}</c> — a named keyframe of the LIVE recording (R-3044).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) TakeCheckpoint(string? label)
        {
            if (RefuseWhenNotRecorded("a checkpoint") is { } refused) return refused;   // ⭐ CE-4090 §5a
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            // ⭐ named here when the caller gives no name: a restore finds a checkpoint only by its label (ClusterMaster.StartRestore)
            if (string.IsNullOrWhiteSpace(label)) label = "cp-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var intent = new TakeCheckpointIntent { RequestId = Guid.NewGuid(), Label = label };
            master.TakeCheckpoint(intent);
            return (new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString(), ["label"] = label }, null, null);
        }

        /// <summary><c>POST /checkpoint/restore {label}</c> — the live run continues from a checkpoint (the master refuses outside a live run).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) RestoreCheckpoint(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return (null, "label is required: the label checkpoint returned, e.g. {\"label\": \"before-assault\"}.", DebugApiHints.Scenario);
            if (RefuseWhenNotRecorded("a checkpoint restore") is { } refused) return refused;   // ⭐ CE-4090 §5a
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            var intent = new RestoreCheckpointIntent { RequestId = Guid.NewGuid(), Label = label };
            master.RestoreCheckpoint(intent);
            return (new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString(), ["label"] = label }, null, null);
        }

        /// <summary><c>GET /exercises</c> — what the master can replay (its exercise inventory), newest first.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) ListExercises()
        {
            var items = _editorExercises?.Invoke() ?? _dispatcher?.ExercisesAnyNode;
            if (items is null)
                return (null, "No subsystem on this host keeps the cluster's exercise inventory (a ClusterUiCache). Ask the node "
                            + "that hosts the master (the orchestrator, or the editor).", DebugApiHints.Panel);
            var arr = new JsonArray();
            foreach (var x in items.OrderByDescending(x => x.StartTimeUtc))
                arr.Add(new JsonObject
                {
                    ["exerciseId"] = x.ExerciseId.ToString(),
                    ["scenario"]   = x.ScenarioId,
                    ["startUtc"]   = x.StartTimeUtc.ToString("O"),
                    ["durationSeconds"] = Math.Round(x.Duration.TotalSeconds, 3),
                    // ⭐ CE-4097 (AQ108 D5) — a branched run's parent (null when it began at a load) and the branch point (the parent's
                    //   wall ticks; atSeconds = (branchWallTicks − the replay's zero) / 1e7)
                    ["parentExerciseId"] = x.ParentExerciseId == Guid.Empty ? null : x.ParentExerciseId.ToString(),
                    ["branchWallTicks"]  = x.BranchWallTicks,
                });
            return (new JsonObject { ["count"] = arr.Count, ["exercises"] = arr }, null, null);
        }

        /// <summary>
        /// ⭐ <c>CE-4058</c> ③ (Director Timeline TE-13) — <c>POST /exercises/keep {exerciseId?}</c>: "Keep for films" — the exercise is
        /// ARCHIVED: every node's recording, its meta and timeline sidecar pulled into <c>{shared}/exercises/&lt;id&gt;/</c> with the scenario
        /// as it was loaded and <c>Orchestrator.json</c>. ⭐ The cluster's existing Export op (CE-278 §5), not a second copier. When no
        /// exercise is named, the newest in the inventory.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) KeepExercise(JsonNode? body)
        {
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            Guid id;
            if (body?["exerciseId"] is JsonValue v && v.TryGetValue(out string? text))
            {
                if (!Guid.TryParse(text, out id))
                    return (null, $"exerciseId '{text}' is not an exercise id (list_exercises names them).", DebugApiHints.Scenario);
            }
            else
            {
                // ⭐ CE-4090 §5a — "keep the last run" when the last run was not recorded: refused, not the newest OLDER exercise
                if (RefuseWhenNotRecorded("keeping the last run") is { } refused) return refused;
                var items = (_editorExercises?.Invoke() ?? _dispatcher?.ExercisesAnyNode)?.OrderByDescending(x => x.StartTimeUtc).ToList();
                if (items is not { Count: > 0 }) return (null, "No exercise to keep: the inventory is empty (list_exercises).", DebugApiHints.Scenario);
                id = items[0].ExerciseId;
            }
            var intent = new ExecuteStorageOpIntent { RequestId = Guid.NewGuid(), Operation = StorageOpType.Export, ExerciseId = id };
            master.Storage(intent);
            return (new JsonObject
            {
                ["published"]  = true,
                ["requestId"]  = intent.RequestId.ToString(),
                ["exerciseId"] = id.ToString(),
                ["archive"]    = System.IO.Path.Combine(OrchestrationConstants.GetSharedRoot(), OrchestrationConstants.ExercisesDirectoryName, id.ToString()),
            }, null, null);
        }

        /// <summary>
        /// ⭐ <c>POST /replay {exerciseId?, atSeconds?}</c> — enter a replay of the exercise (when none is named the MASTER picks: the live
        /// run in progress, else the newest finished one), at
        /// <c>atSeconds</c> after its start (else the start), from ANY state: ONE request — the planner unloads first when it must.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) EnterReplay(JsonNode? body, HttpUnsavedGate? gate = null)
        {
            // ⭐ RB-2 — {path | paths}: a recording file or node group that need not be in any inventory. Only the standalone Replay
            //   Browser opens files (its own seam); a cluster host replays what its master recorded, so it refuses rather than guess.
            if (body?["path"] is not null || body?["paths"] is not null)
                return OpenRecordingFiles(body);

            Guid exerciseId = Guid.Empty;
            var exText = body?["exerciseId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(exText) && !Guid.TryParse(exText, out exerciseId))
                return (null, $"exerciseId '{exText}' is not a GUID. GET /exercises lists them.", DebugApiHints.Scenario);
            // ⭐ CE-4090 §5a — "replay the last run" (no exercise named) when the last run was not recorded: refused
            if (exerciseId == Guid.Empty && RefuseWhenNotRecorded("a replay of the last run") is { } refused) return refused;
            // ⭐ R-3055 (the R-3049 rule) — none named ⇒ Guid.Empty: the MASTER picks the run in progress, else the newest finished one
            //   (ClusterMaster.ResolveReplayExercise) — the same request means the same thing on every host.
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);
            if (!TryReadSimSeconds(body, out double? atSim, out error)) return (null, error, DebugApiHints.Scenario);

            // ⭐ CE-4045 H1b — the intent through ReplaySessionIntents, the ONE construction (the director's replay check builds it there too)
            //   C9: wall ticks AFTER the replay's zero (the earliest first frame across the nodes); 0 = the start · CE-3329 R11: or a RECORDED
            //   sim time, resolved by the master once the nodes' maps are in
            // ⭐ CE-4101 — opening a recording leaves editing like any open: the editor's door decides (HTTP's refusing asker: a run is stopped, unsaved edits are refused with a 409 unless discarded)
            (JsonNode? Result, string? Error, string? HintCategory) published = default;
            var asker = HttpAsker(gate);
            HttpDoor.Admit(DoorMove.OpeningRecording, asker, gate?.Answer ?? UnsavedAnswer.Unchanged,
                () => published = PublishTransition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReplay(exerciseId, at, atSim)));
            if (asker.Refusal is { } refusal) return (null, refusal, ConflictHint);
            if (published.Result is JsonObject o)
            {
                o["exerciseId"] = exerciseId == Guid.Empty ? null : exerciseId.ToString();
                o["atSeconds"]  = at;
                if (exerciseId == Guid.Empty) o["note"] = "No exercise named: the master replays the run in progress, else the newest finished one.";
            }
            return published;
        }

        /// <summary>⭐ <c>CE-4059</c> P3 (TE-15, docs/DESIGN_Director_Timeline_Editor.md §11r) — this host opens recordings itself (the Replay Browser's
        /// control is the active provider's): the director routes' <c>recording=</c> opens through <see cref="EnterReplay"/>, else it is refused.</summary>
        public bool OpensRecordingFiles => _dispatcher?.ReplayBrowser != null;

        private (JsonNode? Result, string? Error, string? HintCategory) OpenRecordingFiles(JsonNode body)
        {
            var paths = new System.Collections.Generic.List<string>();
            if (body["path"]?.ToString() is { Length: > 0 } one) paths.Add(one);
            if (body["paths"] is JsonArray many)
                foreach (var p in many) if (p?.ToString() is { Length: > 0 } x) paths.Add(x);
            if (paths.Count == 0) return (null, "path / paths name no file: {\"paths\": [\"…/node_1.fdp\", \"…/node_2.fdp\"]}.", DebugApiHints.Scenario);
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);

            var browser = _dispatcher?.ReplayBrowser;
            if (browser is null)
                return (null, "Only the Replay Browser opens recording files by path. A cluster replays the exercises its master recorded: "
                            + "POST /replay {exerciseId} (GET /exercises lists them).", DebugApiHints.Scenario);
            var refused = browser.Open(Hrot.Presentation.DebugApi.RecordingName.Files(paths), at);   // ⭐ CE-1391 — the ONE opener
            if (refused != null) return (null, refused, DebugApiHints.Scenario);
            var arr = new JsonArray();
            foreach (var p in paths) arr.Add(p);
            // ⭐ "published": the open is synchronous here, so waitForReady finds the state already reached
            return (new JsonObject
            {
                ["published"] = true,
                ["target"]    = nameof(ClusterState.OperatingReplay),
                ["paths"]     = arr,
                ["atSeconds"] = at,
            }, null, null);
        }

        /// <summary><c>POST /replay/seek {atSeconds}</c> — the operator timeline's seek round (TE-19), seconds after the replay's start.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SeekReplayTo(JsonNode? body)
        {
            if (body?["atSeconds"] is null && body?["atSimSeconds"] is null)
                return (null, "atSeconds (wall, after the replay's zero) or atSimSeconds (the recorded sim time) is required: {\"atSeconds\": 30}.", DebugApiHints.Scenario);
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);
            if (!TryReadSimSeconds(body, out double? atSim, out error)) return (null, error, DebugApiHints.Scenario);
            if (CurrentClusterStateOrNull() is { } s && !ClusterStates.IsReplaying(s))   // ⭐ CE-3329 — a review seeks the same way
                return (null, $"The cluster is in {s}, not a replay: POST /replay (or POST /review) first.", DebugApiHints.Scenario);
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            // ⭐ CE-3329 R11 — atSimSeconds wins; the master (or the Replay Browser) resolves it through the replay's time map
            var intent = Hrot.UI.Common.Facades.ReplaySessionIntents.Seek(at, atSim);   // ⭐ CE-4045 H1b — the ONE construction
            try { master.SeekReplay(intent); }
            catch (InvalidOperationException ex) { return (null, ex.Message, DebugApiHints.Scenario); }
            var result = new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString() };
            if (atSim is { } simTarget) result["atSimSeconds"] = simTarget; else result["atSeconds"] = at;
            return (result, null, null);
        }

        private Fdp.Toolkit.Orchestration.ReplayAxis? ReplayAxisOrNull => _editorReplayAxis?.Invoke() ?? _dispatcher?.ReplayAxisAnyNode;

        /// <summary>
        /// ⭐ CE-3329 <c>POST /replay/axis {axis: "Sim" | "Wall"}</c> (R-3058) — switch the replay's time axis for the whole session, from
        /// any host: the master holds it and every node hears it. No seek — the frame on screen stays; what comes next follows the axis.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SetReplayAxis(JsonNode? body)
        {
            var text = body?["axis"]?.ToString();
            if (string.IsNullOrWhiteSpace(text) || !Enum.TryParse<Fdp.Toolkit.Orchestration.ReplayAxis>(text, ignoreCase: true, out var axis)
                || !Enum.IsDefined(axis))
                return (null, "axis is required: \"Sim\" (the run's own clock, pauses collapsed) or \"Wall\".", DebugApiHints.Scenario);
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            var intent = new SetReplayAxisIntent { RequestId = Guid.NewGuid(), Axis = axis };
            try { master.SetReplayAxis(intent); }
            catch (InvalidOperationException ex) { return (null, ex.Message, DebugApiHints.Scenario); }
            return (new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString(), ["axis"] = axis.ToString() }, null, null);
        }

        /// <summary>
        /// ⭐ CE-3329 §12n <c>POST /replay/loop {fromSeconds, toSeconds} | {fromSimSeconds, toSimSeconds} | {}</c> — loop the replay between
        /// two moments (wall seconds after the zero, or the recording's sim seconds); <c>{}</c> clears. A SESSION setting: the master holds
        /// it, every host draws it, the master's replay clock wraps it. A non-number is refused here, nothing is published.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SetReplayLoop(JsonNode? body)
        {
            if (!TryReadLoopBound(body, "fromSeconds", out var from, out var error)
                || !TryReadLoopBound(body, "toSeconds", out var to, out error)
                || !TryReadLoopBound(body, "fromSimSeconds", out var fromSim, out error)
                || !TryReadLoopBound(body, "toSimSeconds", out var toSim, out error))
                return (null, error, DebugApiHints.Scenario);
            bool anyWall = from.HasValue || to.HasValue, anySim = fromSim.HasValue || toSim.HasValue;
            if (anyWall && anySim)
                return (null, "Name the loop in wall seconds (fromSeconds + toSeconds) OR in sim seconds (fromSimSeconds + toSimSeconds), not both.", DebugApiHints.Scenario);
            if (anyWall && !(from.HasValue && to.HasValue) || anySim && !(fromSim.HasValue && toSim.HasValue))
                return (null, "A loop needs both bounds: fromSeconds + toSeconds, or fromSimSeconds + toSimSeconds ({} clears it).", DebugApiHints.Scenario);
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            var intent = anySim ? Hrot.UI.Common.Facades.ReplaySessionIntents.SetLoopSim(fromSim!.Value, toSim!.Value)
                       : anyWall ? Hrot.UI.Common.Facades.ReplaySessionIntents.SetLoopWall(from!.Value, to!.Value)
                       : Hrot.UI.Common.Facades.ReplaySessionIntents.ClearLoop();
            try { master.SetReplayLoop(intent); }
            catch (InvalidOperationException ex) { return (null, ex.Message, DebugApiHints.Scenario); }
            return (new JsonObject { ["published"] = true, ["requestId"] = intent.RequestId.ToString(), ["cleared"] = !anySim && !anyWall }, null, null);
        }

        /// <summary>⭐ CE-3329 §12n — a loop bound: absent ⇒ null; else a finite non-negative number (like <see cref="TryReadSimSeconds"/>).</summary>
        private static bool TryReadLoopBound(JsonNode? body, string key, out double? seconds, out string? error)
        {
            seconds = null; error = null;
            var text = body?[key]?.ToString();
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v >= 0 && double.IsFinite(v)) { seconds = v; return true; }
            error = $"{key} '{text}' is not a non-negative number of seconds.";
            return true;
        }

        /// <summary><c>POST /replay/stop</c> — leave the replay (to <c>Idle</c>).</summary>
        /// <remarks>
        /// ⭐⭐ <c>CE-4101</c> (AQ108 §2.11a) — the ONE Idle intent (<see cref="Fdp.Toolkit.Orchestration.ScenarioLoadSequence.UnloadIntent"/>), sent once the editor's OUT door lets it through: a stop while EDITING with unsaved
        /// changes is refused (<see cref="ConflictHint"/> ⇒ 409, the same text as a load's) unless the body discarded them; a run is simply stopped.
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) StopReplay(HttpUnsavedGate? gate = null)
        {
            var intent = Fdp.Toolkit.Orchestration.ScenarioLoadSequence.UnloadIntent();
            (JsonNode? Result, string? Error, string? HintCategory) published = default;
            var asker = HttpAsker(gate);
            HttpDoor.Admit(DoorMove.Leaving(LeaveReason.HttpStop), asker, gate?.Answer ?? UnsavedAnswer.Unchanged, () => published = PublishTransition(intent));
            return asker.Refusal is { } refusal ? (null, refusal, ConflictHint) : published;
        }

        /// <summary><c>GET /replay/status</c> — the cluster's state, the loaded replay's length, and this node's clock.</summary>
        public JsonNode GetReplayStatus()
        {
            var state = CurrentClusterStateOrNull();
            return new JsonObject
            {
                ["clusterState"]    = state?.ToString(),
                ["replaying"]       = state is { } st && ClusterStates.IsReplaying(st),
                // ⭐ CE-3329 — a review of the live run (durationSeconds is then the live point, after the zero)
                ["review"]          = state is { } rs && ClusterStates.IsReview(rs),
                ["durationSeconds"] = (JsonNode?)(_editorReplayDuration?.Invoke() ?? _dispatcher?.ReplayDurationAnyNode),
                ["recording"]       = ReplayRecordingPathOrNull,
                // ⭐ CE-3329 P0 (R-3057 R11, row 6) — the RECORDED sim time of the frame on screen; `sim` below is the replay clock (wall)
                ["simSeconds"]      = ReplayPositionOrNull?.SimSeconds is { } simSeconds ? Math.Round(simSeconds, 6) : null,
                // ⭐ CE-3329 R-3058 — the session's axis: Sim (a play jumps the paused stretches) or Wall
                ["axis"]            = ReplayAxisOrNull?.ToString(),
                // ⭐ CE-3329 §12n — the session's loop range on the ACTIVE axis (the session carries wall seconds; the facade converts); null = none
                ["loop"]            = TryRead(() => _time.Loop is { } lp ? new JsonObject { ["fromSeconds"] = Math.Round(lp.From, 6), ["toSeconds"] = Math.Round(lp.To, 6) } : null),
                ["sim"]             = TryRead(() => GetSimState()),
                // ⭐ RB-2 — the Replay Browser adds its frame, frames, span, nodes, view and rate; null on every other host
                ["browser"]         = TryRead(() => _dispatcher?.ReplayBrowser?.Status()),
            };
        }

        private (JsonNode? Result, string? Error, string? HintCategory) PublishTransition(TransitionStateIntent intent)
        {
            var master = MasterOrNull;
            if (master is null) return (null, NoMaster, DebugApiHints.Panel);
            // ⭐ RB-1 — a master that acts in place (the Replay Browser's federation) says why it refused; a bus-backed one only publishes
            try { master.Transition(intent); }
            catch (InvalidOperationException ex) { return (null, ex.Message, DebugApiHints.Scenario); }
            return (new JsonObject
            {
                ["published"]     = true,
                ["transactionId"] = intent.TransactionId.ToString(),
                ["target"]        = intent.TargetState.ToString(),
            }, null, null);
        }

        /// <summary>⭐ CE-3329 P3 — the cluster shows a recording (a review or a replay): world-changing commands are refused (D-E).</summary>
        public bool ShowsRecording => CurrentClusterStateOrNull() is { } s && ClusterStates.IsReplaying(s);

        /// <summary>⭐ CE-4090 (AQ108 D22) — the cluster runs a LIVE exercise: the scenario is not saved from it (<c>POST /scenario/save</c> → 409).</summary>
        public bool ShowsLiveRun => CurrentClusterStateOrNull() == ClusterState.OperatingLive;

        /// <summary>
        /// ⭐ CE-3329 <c>POST /review {atSeconds? | atSimSeconds?}</c> (R11) — review the LIVE run in place: the cluster pauses, every node
        /// keeps its live point, and the run's own recording is shown from the moment named (else the live point). ONE request, any host.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) EnterReview(JsonNode? body)
        {
            // ⭐⭐ CE-4090 (AQ108 D9) — a review always has an EXPLICIT START: the moment of the live run to look back at. ⛔ "No position = the
            //   live point" is retired (it opened a review of nothing — the UI picks a window or a timeline moment). A 4xx, before anything else.
            if (body?["atSeconds"] is null && body?["atSimSeconds"] is null)
                return (null, "a review needs its start: {\"atSeconds\": <seconds after the run's start>} or {\"atSimSeconds\": <recorded sim seconds>} "
                            + "(the moment to look back at — the live point is no longer implied).", DebugApiHints.Scenario);
            if (RefuseWhenNotRecorded("a review (DAR)") is { } refused) return refused;   // ⭐ CE-4090 §5a
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);
            if (!TryReadSimSeconds(body, out double? atSim, out error)) return (null, error, DebugApiHints.Scenario);
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingLive && s != ClusterState.OperatingReview)
                return (null, $"A review starts from a live run; the cluster is in {s}.", DebugApiHints.Scenario);
            // ⭐ CE-4045 H1b — the ONE construction; the start is explicit (AQ108 D9, above)
            return PublishTransition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReview(at, atSim));
        }

        /// <summary>⭐ CE-3329 <c>POST /review/exit</c> — back to the live run, exactly where it paused (live, paused).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) ExitReview()
        {
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingReview)
                return (new JsonObject { ["published"] = false, ["target"] = nameof(ClusterState.OperatingLive), ["note"] = "Not in a review." }, null, null);
            return PublishTransition(Hrot.UI.Common.Facades.ReplaySessionIntents.ExitReview());   // ⭐ the ONE construction (was an inline copy)
        }

        private ClusterState? CurrentClusterStateOrNull()
        {
            try { return CurrentClusterState(); }
            catch (Exception) { return null; }
        }

        /// <summary>⭐ CE-3329 R11 — <c>atSimSeconds</c>: absent ⇒ null; else a non-negative number of the recording's own sim seconds.</summary>
        private static bool TryReadSimSeconds(JsonNode? body, out double? seconds, out string? error)
        {
            seconds = null; error = null;
            var text = body?["atSimSeconds"]?.ToString();
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v >= 0 && double.IsFinite(v)) { seconds = v; return true; }
            error = $"atSimSeconds '{text}' is not a non-negative number of seconds.";
            return false;
        }

        private static bool TryReadSeconds(JsonNode? body, out double seconds, out string? error)
        {
            seconds = 0; error = null;
            var text = body?["atSeconds"]?.ToString();
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds)
                && seconds >= 0 && double.IsFinite(seconds)) return true;
            error = $"atSeconds '{text}' is not a non-negative number of seconds.";
            return true;
        }
    }
}
