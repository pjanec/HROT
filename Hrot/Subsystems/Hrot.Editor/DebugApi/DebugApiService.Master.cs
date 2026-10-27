using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Orchestration;

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

        /// <summary><c>POST /preview/enter {startPaused?}</c> — the cluster's dry run (<c>OperatingPreview</c>). ⛔ Never recorded (R-3053).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) EnterPreview(bool startPaused = false)
        {
            if (CurrentClusterStateOrNull() == ClusterState.OperatingPreview)
                return (new JsonObject { ["published"] = false, ["target"] = nameof(ClusterState.OperatingPreview), ["note"] = "Already in preview." }, null, null);
            return PublishTransition(new TransitionStateIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetState   = ClusterState.OperatingPreview,
                // ⭐ the master pauses a start-paused preview through the same path as Stop (ClusterMaster: passesSimulationStart)
                TimeMode      = startPaused ? "Deterministic" : null,
            });
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
        public (JsonNode? Result, string? Error, string? HintCategory) EnterReplay(JsonNode? body)
        {
            // ⭐ RB-2 — {path | paths}: a recording file or node group that need not be in any inventory. Only the standalone Replay
            //   Browser opens files (its own seam); a cluster host replays what its master recorded, so it refuses rather than guess.
            if (body?["path"] is not null || body?["paths"] is not null)
                return OpenRecordingFiles(body);

            Guid exerciseId = Guid.Empty;
            var exText = body?["exerciseId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(exText) && !Guid.TryParse(exText, out exerciseId))
                return (null, $"exerciseId '{exText}' is not a GUID. GET /exercises lists them.", DebugApiHints.Scenario);
            // ⭐ R-3055 (the R-3049 rule) — none named ⇒ Guid.Empty: the MASTER picks the run in progress, else the newest finished one
            //   (ClusterMaster.ResolveReplayExercise) — the same request means the same thing on every host.
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);
            if (!TryReadSimSeconds(body, out double? atSim, out error)) return (null, error, DebugApiHints.Scenario);

            // ⭐ CE-4045 H1b — the intent through ReplaySessionIntents, the ONE construction (the director's replay check builds it there too)
            //   C9: wall ticks AFTER the replay's zero (the earliest first frame across the nodes); 0 = the start · CE-3329 R11: or a RECORDED
            //   sim time, resolved by the master once the nodes' maps are in
            var published = PublishTransition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReplay(exerciseId, at, atSim));
            if (published.Result is JsonObject o)
            {
                o["exerciseId"] = exerciseId == Guid.Empty ? null : exerciseId.ToString();
                o["atSeconds"]  = at;
                if (exerciseId == Guid.Empty) o["note"] = "No exercise named: the master replays the run in progress, else the newest finished one.";
            }
            return published;
        }

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
            var refused = browser.Open(paths, at);
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
        public (JsonNode? Result, string? Error, string? HintCategory) StopReplay()
            => PublishTransition(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.Idle });

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

        /// <summary>
        /// ⭐ CE-3329 <c>POST /review {atSeconds? | atSimSeconds?}</c> (R11) — review the LIVE run in place: the cluster pauses, every node
        /// keeps its live point, and the run's own recording is shown from the moment named (else the live point). ONE request, any host.
        /// </summary>
        public (JsonNode? Result, string? Error, string? HintCategory) EnterReview(JsonNode? body)
        {
            if (!TryReadSeconds(body, out double at, out var error)) return (null, error, DebugApiHints.Scenario);
            if (!TryReadSimSeconds(body, out double? atSim, out error)) return (null, error, DebugApiHints.Scenario);
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingLive && s != ClusterState.OperatingReview)
                return (null, $"A review starts from a live run; the cluster is in {s}.", DebugApiHints.Scenario);
            bool named = body?["atSeconds"] is not null || atSim is not null;
            // ⭐ CE-4045 H1b — the ONE construction; no moment named ⇒ the live point (the master's default)
            var published = PublishTransition(Hrot.UI.Common.Facades.ReplaySessionIntents.EnterReview(named ? at : null, atSim));
            if (published.Result is JsonObject o && !named) o["note"] = "No moment named: the review opens at the live point.";
            return published;
        }

        /// <summary>⭐ CE-3329 <c>POST /review/exit</c> — back to the live run, exactly where it paused (live, paused).</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) ExitReview()
        {
            if (CurrentClusterStateOrNull() is { } s && s != ClusterState.OperatingReview)
                return (new JsonObject { ["published"] = false, ["target"] = nameof(ClusterState.OperatingLive), ["note"] = "Not in a review." }, null, null);
            return PublishTransition(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.OperatingLive });
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
