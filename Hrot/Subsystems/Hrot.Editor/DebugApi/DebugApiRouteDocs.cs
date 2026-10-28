using System;
using System.Collections.Generic;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE AGENT-FACING DOC OF EVERY ENDPOINT, keyed by the route that serves it.</b>
    /// 📄 <c>MCP_Integration.md</c> § *"Follow-up — GENERATE `tool-catalog.mjs` from the routes"* *(`HN-030`)*.
    ///
    /// <para>⭐⭐ <b>This replaced a hand-maintained mirror in another language.</b> The content below was MOVED
    /// from <c>tools/ai-debug-mcp/tool-catalog.mjs</c>, not rewritten — it was already correct; what was wrong
    /// was that nothing tied it to the routes. ⇒ <c>GET /capabilities</c> now emits it and the JS catalog is
    /// GENERATED from that dump.</para>
    ///
    /// <para>⛔⛔ <b>Why a keyed table and not an argument on each <c>_routes.Add</c> call.</b> ⚠ Proximity was
    /// the goal *(and it is the real argument for doing this at all)*, but <c>BuildRoutes</c> is already 535
    /// lines and these docs are ~1000 more; inlining them would make one method ~1500 lines and bury the route
    /// ORDER, which matters *(see the <c>/perspectives</c> vs <c>/perspective</c> note there)*. ⭐⭐ What makes
    /// drift impossible is not proximity but ENFORCEMENT: <c>EveryRouteIsDocumentedTests</c> fails when a route
    /// has no entry here, so the two cannot move apart. ⚠ Stated as a trade, not as the obviously-right answer
    /// — an inline-per-route variant is a legitimate follow-up if the extra length is preferred.</para>
    ///
    /// <para>⭐ <b>Ordered and grouped to mirror the route table</b>, so the two read side by side.</para>
    ///
    /// <para>⚠⚠ <b>One tool is NOT here and cannot be:</b> <c>start_simulation</c> spawns the runner process
    /// from inside the MCP server and has no HTTP endpoint at all. ⇒ the generator MERGES a small JS-side
    /// supplement, and that supplement is the one thing generation cannot police — which is why the
    /// reconciliation rail still exists.</para>
    /// </summary>
    internal static class DebugApiRouteDocs
    {
        /// <summary>⭐ <c>(method, path)</c> → the endpoint's agent-facing contract.</summary>
        public static readonly IReadOnlyDictionary<(string Method, string Path), RouteDoc> ByRoute =
            new Dictionary<(string, string), RouteDoc>()
    {
        [("POST", "/shutdown")] = new RouteDoc(
            Tool:    "stop_simulation",
            Group:   "A — Lifecycle & status",
            Summary: "Shut down the runner gracefully via POST /shutdown, then hard-kill if needed.",
            Returns: "The /shutdown envelope, or { note: \"runner already gone\" }",
            Hint:    "No params. Example: stop_simulation({})",
            Notes: new[]
            {
                "MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.",
                "Always call when done to avoid orphan runner processes.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "graceful runner shutdown"),

        [("GET", "/status")] = new RouteDoc(
            Tool:    "get_status",
            Group:   "A — Lifecycle & status",
            Summary: "Runner liveness + sim state summary.",
            Returns: "{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording, perspective, routedTo, scenarioDirty }",
            Hint:    "No params. Example: get_status({})",
            Notes: new[]
            {
                "Use this to verify the runner is alive and check current run state before driving the sim.",
                "`recording` is whether the run the cluster shows is RECORDED: true in a live run, a review (DAR) or a recording (AAR) that has its recording; false in Idle / Edit / Preview and in a live run loaded with 'Record live runs' off (set_run_settings). When it is false in a live run, checkpoint, restore_checkpoint, enter_review, keep_exercise and enter_replay (of the last run) answer 409 \"this run was not recorded\".",
                "`scenarioDirty` is the LAST check round's verdict (check_unsaved): true = the scenario open for editing has unsaved changes, false = saved; null when no round ran, or the cluster left editing since. Reading it NEVER runs a round — call check_unsaved for a fresh answer.",
                "`perspective` is the active perspective; `routedTo` is the perspective the reads here were answered from. They are equal EXCEPT while the cluster rests in Idle and every host shows the Start perspective: then `perspective` is \"Start\" and `routedTo` is the perspective the host will return to when a scenario or recording is opened (the world reads — entities, sim state, AI — keep answering from it).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "check runner liveness and sim state"),

        [("GET", "/capabilities")] = new RouteDoc(
            Tool:    "get_capabilities",
            Group:   "A — Lifecycle & status",
            Summary: "What THIS host can actually do — every endpoint, and the measured per-perspective matrix.",
            Returns: "{ mode, host{hasMaster,currentPerspective,routablePerspectives}, endpoints[], matrix{perspective:{capability:bool}}, unclassifiedRoutes[] }",
            Hint:    "No params. Example: get_capabilities({})",
            Notes: new[]
            {
                "ASK THIS FIRST when a call answers 501 NOT_SUPPORTED_HERE — the matrix says which capabilities the active perspective offers, so you can switch perspective or pick another endpoint instead of guessing.",
                "mode tells you how the process was started: \"editor\" (one context, everything local) or a cluster mode such as \"all\" (orchestrator + simhost + ig + excon + cgf).",
                "The matrix is MEASURED from wired dependencies, not declared — a false cell is a bug, not a stale table.",
                "host.hasMaster:false means a step cannot be confirmed cluster-wide on this host.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "find out what this host supports before driving it"),

        // ══ Group V — the AI-ASSET drive surface (cgf==editor slice 2) ═══════════════════════
        // 📄 DESIGN_Cgf_Editor_Sharing_Slice2_Open_Asset.md §3a — THREE addresses, none of them a raw
        //    path in a URL segment.

        [("GET", "/assets")] = new RouteDoc(
            Tool:    "list_assets",
            Group:   "V — AI assets & graph tabs",
            Summary: "Every AI asset (BTree/HSM/Blueprint) this host has indexed, with both of its addresses.",
            Returns: "{ count, assets[{assetId,name,kind,sourceFilePath,isDirty}], note? }",
            Hint:    "No params. Example: list_assets({})",
            Notes: new[]
            {
                "CALL THIS FIRST before opening anything — it is how you turn a human path into the assetId the open-by-id route wants.",
                "sourceFilePath is the RELATIVE path including subfolders, normalised to forward slashes; paste it verbatim into open_asset_by_path.",
                "name is NOT an address: two subfolders may hold the same file name. Address by assetId (stable) or sourceFilePath (human).",
                "count:0 with a note means the catalog indexed nothing — on a deployed node the source asset tree is absent (asset roots must come from config).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "discover which AI assets this host can open"),

        [("POST", "/assets/{assetId}/open")] = new RouteDoc(
            Tool:    "open_asset",
            Group:   "V — AI assets & graph tabs",
            Summary: "Open an AI asset by its stable GUID; the graph canvas and outline then render it.",
            Returns: "{ assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }",
            Hint:    "Req: assetId (GUID, from list_assets). Example: open_asset({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of the asset to open — from list_assets"),
            },
            Notes: new[]
            {
                "The panels publish the opened asset on the NEXT frame — step a tick before get_panels, or you read the previous content.",
                "Opening an already-open asset re-activates its tab rather than duplicating it.",
                "Opening also switches the perspective to the asset kind (the document manager drives it), so the canvas is actually drawing.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "open a specific asset by id and make it the active graph"),

        [("POST", "/assets/open")] = new RouteDoc(
            Tool:    "open_asset_by_path",
            Group:   "V — AI assets & graph tabs",
            Summary: "Open an AI asset by its relative source file path — the human address.",
            Returns: "{ assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }",
            Hint:    "Req: path (string, a sourceFilePath from list_assets). Example: open_asset_by_path({path:\"Assets/Blueprints/sub/x.bp.json\"})",
            Params: new RouteParam[]
            {
                new("path", "string", true, "Relative sourceFilePath, or any suffix of it at a folder boundary"),
            },
            Notes: new[]
            {
                "The path travels in the BODY on purpose — a relative path has slashes and dots, which a URL segment would need encoding for.",
                "Matching is a path SUFFIX at a folder boundary: 'sub/x.bp.json' matches, 'x' does not, and 'my_x.bp.json' never matches a query for 'x.bp.json'.",
                "An AMBIGUOUS path is a 400 that lists the candidates — it is never resolved by picking the first, which would silently open the wrong asset.",
            },
            ExampleArgsJson: "{\"path\":\"Assets/Blueprints/hill_attack.bp.json\"}",
            ExampleGist: "open an asset by the path a human would read off disk"),

        [("POST", "/assets/{assetId}/save")] = new RouteDoc(
            Tool:    "save_ai_asset",
            Group:   "V — AI assets & graph tabs",
            Summary: "Persist edited AI assets to their source files.",
            Returns: "{ assetId, name, sourceFilePath, status, stillDirty, note }",
            Hint:    "Req: assetId (GUID of an OPEN document, from list_documents). Example: save_ai_asset({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document — save acts on open documents, not files"),
            },
            Notes: new[]
            {
                "IT SAVES EVERY DIRTY OPEN DOCUMENT, not only this one — it runs the shared Save-All command, which is what the editor's own Save All button runs.",
                "A document with no source path is SKIPPED with a warning in `status` rather than throwing; check `stillDirty` to see whether this one was written.",
                "Saving is NOT a precondition for reload: reload compiles from the in-memory asset, so an unsaved edit still hot-applies.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "write an edited graph back to disk"),

        [("POST", "/assets/{assetId}/reload")] = new RouteDoc(
            Tool:    "reload_ai_asset",
            Group:   "V — AI assets & graph tabs",
            Summary: "Recompile an edited AI asset and commit it into the running behaviour registry.",
            Returns: "{ assetId, name, kind, status, note }",
            Hint:    "Req: assetId (GUID of an OPEN document, from list_documents). Example: reload_ai_asset({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document to recompile"),
            },
            Notes: new[]
            {
                "Compiles from the IN-MEMORY asset, not from the file — so it reflects unsaved edits, and save is a separate intent.",
                "The asset is ACTIVATED first: the reload pipeline acts on the active document, so reloading a background tab without activating it would recompile the wrong graph.",
                "A SOFT reload patches lookup tables and live instances KEEP their state; a HARD (topology) reload bumps the generation and instances RESET — that reset is intended, not a bug.",
                "A Hard reload on a live cluster is a confirmed cluster-wide reset, and the confirmation belongs to the interactive node — this call never prompts.",
                "`status` carries the compiler's own message, including the failure text when it did not compile. A failed compile is a 200 with a failure status, not an HTTP error: it is a legitimate outcome of editing.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "hot-apply an edited graph to the running brain"),

        [("POST", "/ai/reload")] = new RouteDoc(
            Tool:    "reload_ai_behaviours",
            Group:   "V — AI assets & graph tabs",
            Summary: "⭐ CE-1321 — rebuild the C# behaviours (Hrot.AI.Behaviors) and HOT-SWAP them into the running editor: build, copy into the watched directory, wait for the swap; open graph documents are re-decompiled. The same path as the toolbar's Reload AI.",
            Returns: "{ build: ok|failed|not-found|not-wired|busy, exitCode, errors[], reloaded, note, added[], removed[], behaviours, documentsReconciled, registrarFailures[], seconds }",
            Hint:    "No parameters. Edit the C# behaviour source first, then reload_ai_behaviours({}). Takes a build (tens of seconds). Example: reload_ai_behaviours({})",
            Params: Array.Empty<RouteParam>(),
            Notes: new[]
            {
                "Running units KEEP their state: the registry merges in place and a running behaviour's slot memory is untouched; HSM instances are re-bound by slot. A changed params/state LAYOUT is yours to restart (assign it again).",
                "A failed build is a 200 with build:failed and the compiler's error lines — the running assembly is unchanged.",
                "Only Hrot.AI.Behaviors hot-reloads. C# anywhere else (engine, toolkits) needs a rebuild and a restart of the editor.",
                "Cheaper first: a value (stage_entity_variable with owner) or a JSON asset (reload_ai_asset) needs no build at all — the skill's §J ladder.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "rebuild and hot-swap the C# behaviours after editing their source"),

        // ── Group W — AI-asset AUTHORING (AQ56 / DESIGN_Mcp_Authoring.md) ────────────────────
        //
        // ⭐⭐⭐ The read-then-edit-by-guid loop. ⛔ The ids in these payloads are the IN-MEMORY ones the
        //    command sink edits by — NEVER the deterministic ids the save path writes to the file.

        [("GET", "/assets/{assetId}/graph")] = new RouteDoc(
            Tool:    "read_asset_graph",
            Group:   "W — AI-asset authoring",
            Summary: "Read an open AI asset's graph as JSON: nodes, pins, links and comments, keyed by the in-memory guids the edit tools take.",
            Returns: "{ assetId, name, kind, graphId, displayName, graphKind, nodeCount, linkCount, nodes[{nodeId,kind,title,position,pins[{pinId,label,direction,kind,type,default}]}], links[{linkId,fromPin,toPin,fromNode,toNode}], comments[], note }",
            Hint:    "Req: assetId (GUID of an OPEN document — open_asset first). Example: read_asset_graph({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document (open_asset / list_documents)"),
            },
            Notes: new[]
            {
                "THIS IS THE FIRST CALL of any authoring session: you never predict an id, you read the ones the edit tools accept.",
                "The ids are the IN-MEMORY guids. The saved .json binds links by deterministic name-derived pin ids instead — an id copied out of the file addresses nothing here.",
                "Re-read after each edit rather than caching: adding a node can reproject another node's pins.",
                "Only the graph-document kinds (BTree, HSM, Blueprint) have a graph; a Scenario or Blackboard asset is a 404 explaining that.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "read the whole graph before editing it"),

        [("GET", "/assets/{assetId}/graph/catalog")] = new RouteDoc(
            Tool:    "list_node_kinds",
            Group:   "W — AI-asset authoring",
            Summary: "The node kinds this graph can add, with their pin signatures. Call this instead of guessing a kind id for add_graph_node.",
            Returns: "{ count, total, kinds[{kind,displayName,category,description,isDeprecated,inputs[],outputs[]}], note }",
            Hint:    "Req: assetId. Optional: filter (substring over kind id and display name). Example: list_node_kinds({assetId:\"...\",filter:\"branch\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("filter", "string", false, "Case-insensitive substring matched against the kind id AND the display name"),
            },
            Notes: new[]
            {
                "The catalog is PER GRAPH — a BTree graph and a Blueprint graph offer different kinds, so read the one you are editing.",
                "`kind` is what add_graph_node takes verbatim. An unknown kind is refused with this endpoint named, not silently ignored.",
                "`inputs`/`outputs` are the declared pin SIGNATURES; the actual pin guids only exist once the node is added.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "discover what node kinds this graph accepts"),

        [("POST", "/assets/{assetId}/graph/nodes")] = new RouteDoc(
            Tool:    "add_graph_node",
            Group:   "W — AI-asset authoring",
            Summary: "Add a node to an open graph through the same command sink human editing uses. Returns the new node's guid and its pins.",
            Returns: "{ nodeId, kind, title, pins[{pinId,label,direction,kind,type}], note }",
            Hint:    "Req: assetId, kind (from list_node_kinds). Optional: x, y. Example: add_graph_node({assetId:\"...\",kind:\"bt.selector\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("kind", "string", true, "Node kind id — take one verbatim from list_node_kinds"),
                new("x", "number", false, "Canvas X position (default 0)", DefaultJson: "0"),
                new("y", "number", false, "Canvas Y position (default 0)", DefaultJson: "0"),
            },
            Notes: new[]
            {
                "The edit goes through the editor's undo stack, so it is undoable exactly like a node dropped on the canvas.",
                "The response carries the new node's PINS because linking needs them — you do not have to re-read the whole graph to wire it up.",
                "An unknown kind is a 400 naming list_node_kinds: the host sink can report success and build nothing, so this route re-reads the model and refuses rather than returning a guid that addresses nothing.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"kind\":\"bt.selector\",\"x\":120,\"y\":40}",
            ExampleGist: "add a node and get back its guid"),

        [("POST", "/assets/{assetId}/graph/links")] = new RouteDoc(
            Tool:    "add_graph_link",
            Group:   "W — AI-asset authoring",
            Summary: "Connect two pins in an open graph. The host's own link validator runs first, so an illegal wire is refused for the same reason a dragged one would be.",
            Returns: "{ linkId, fromPin, toPin, requiresCast, note }",
            Hint:    "Req: assetId, fromPin, toPin (pin GUIDs from read_asset_graph or add_graph_node). Example: add_graph_link({assetId:\"...\",fromPin:\"...\",toPin:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("fromPin", "string", true, "Source (output-side) pin GUID"),
                new("toPin", "string", true, "Target (input-side) pin GUID"),
            },
            Notes: new[]
            {
                "The validator is the SAME one the canvas consults while dragging a wire, so MCP can never author a graph the editor would reject.",
                "A refusal is a 400 carrying the host's own reason text — it is a legitimate answer, not a server error.",
                "When the validator classes the pair ValidWithCast the canvas would auto-insert a cast node; this route connects them directly and says so in `note`.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"fromPin\":\"11111111-1111-1111-1111-111111111111\",\"toPin\":\"22222222-2222-2222-2222-222222222222\"}",
            ExampleGist: "wire two pins together"),

        [("POST", "/assets/{assetId}/graph/params")] = new RouteDoc(
            Tool:    "set_graph_param",
            Group:   "W — AI-asset authoring",
            Summary: "Set the literal default value on an input data pin of an open graph.",
            Returns: "{ pinId, label, previousValue, value, note }",
            Hint:    "Req: assetId, pinId, value. Example: set_graph_param({assetId:\"...\",pinId:\"...\",value:3.5})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("pinId", "string", true, "GUID of an INPUT DATA pin (from read_asset_graph)"),
                new("value", "string", true, "The new default. Sent as JSON and converted to the CLR type the pin's current default already holds; an explicit null clears it"),
            },
            Notes: new[]
            {
                "This is a PIN default, not a free-form node property: the pin default is the one edit whose inverse can be built from the model, so it is the one that stays undoable.",
                "An exec pin or an output pin is refused — an exec pin has no value and an output's value is computed.",
                "`value` in the response is RE-READ from the model after the edit, so it shows what the host actually stored rather than what you sent.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"pinId\":\"11111111-1111-1111-1111-111111111111\",\"value\":3.5}",
            ExampleGist: "set a literal on an input pin"),

        [("POST", "/assets/{assetId}/graph/remove")] = new RouteDoc(
            Tool:    "remove_graph_elements",
            Group:   "W — AI-asset authoring",
            Summary: "Remove nodes and/or links from an open graph by invoking the editor's own Delete command.",
            Returns: "{ removedNodes, removedLinks, nodeCount, linkCount, note }",
            Hint:    "Req: assetId and at least one of nodes[] / links[]. Example: remove_graph_elements({assetId:\"...\",nodes:[\"...\"]})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("nodes", "array", false, "Node GUIDs to remove"),
                new("links", "array", false, "Link GUIDs to remove"),
            },
            Notes: new[]
            {
                "It invokes the editor's shared Delete command rather than building its own removal, so incident links, reroute waypoints and attachments are handled and the undo restores nodes before the links that reference them.",
                "`removedLinks` counts the links deleted IMPLICITLY with their nodes, so it is usually larger than the list you named.",
                "An id that is not in the graph refuses the WHOLE call — a partial delete would be worse than a refusal.",
                "The canvas selection is left cleared afterwards, exactly as after a human delete.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"nodes\":[\"11111111-1111-1111-1111-111111111111\"]}",
            ExampleGist: "delete a node and its wires"),

        [("POST", "/assets")] = new RouteDoc(
            Tool:    "create_asset",
            Group:   "W — AI-asset authoring",
            Summary: "Create a new AI asset (BTree / HSM / Blueprint) through the host's own New-Asset path, then open it as a document.",
            Returns: "{ assetId, name, kind, recipe, status, sourceFilePath, note }",
            Hint:    "Req: kind, name. Optional: path (subfolder), recipe (from list_asset_recipes). Example: create_asset({kind:\"BTree\",name:\"Patrol\"})",
            Params: new RouteParam[]
            {
                new("kind", "string", true, "BTree | Hsm | Blueprint"),
                new("name", "string", true, "Asset name"),
                new("path", "string", false, "Subfolder relative to the kind's asset root (default: the root)"),
                new("recipe", "string", false, "Recipe NAME from list_asset_recipes (default: the kind's blank template)"),
            },
            Notes: new[]
            {
                "It runs the same per-kind INewAssetService the New-Asset dialog runs, writes the file and refreshes the catalog — so the result appears in list_assets by the same rebuild a dialog-created asset does.",
                "The new asset is opened as a document, so you can author it immediately with read_asset_graph and the graph tools.",
                "A host that composes no create path answers 503 explaining that EDITING an existing asset does not need it.",
                "Call list_asset_recipes first to see what this host can create from. A recipe name it does not offer is REFUSED with the available names — it never silently falls back to a blank asset.",
            },
            ExampleArgsJson: "{\"kind\":\"BTree\",\"name\":\"PatrolTree\"}",
            ExampleGist: "create a new behaviour tree asset"),

        [("POST", "/cluster/diagnostics/dump")] = new RouteDoc(
            Tool:    "trigger_cluster_diagnostic_dump",
            Group:   "Y — node diagnostics",
            Summary: "Collect diagnostics on the named cluster nodes and pull them to the NAS — the same operation the ExCon's Execute Diagnostic Dump button drives.",
            Returns: "{ transactionId, nodes[], queued:true, note }",
            Hint:    "Req: nodes[] (from get_cluster_diagnostic_status). Example: trigger_cluster_diagnostic_dump({nodes:[1,2]})",
            Params: new RouteParam[]
            {
                new("nodes", "array", true, "Node ids to dump (at least one)"),
                new("dumpEvents", "boolean", false, "Include event history (default true)"),
                new("dumpEntities", "boolean", false, "Include entity state (default true)"),
                new("dumpArchitecture", "boolean", false, "Include the architecture snapshot (default true)"),
                new("dumpLogs", "boolean", false, "Include NLog files (default true)"),
                new("eventProviders", "array", false, "Restrict event dumping to these provider names"),
                new("useMarkdown", "boolean", false, "Wrap the output in a markdown report (default false)"),
                new("maxAgeHours", "number", false, "Only include log entries younger than this (default 24)"),
                new("severityThreshold", "number", false, "Minimum log severity to include (default 0)"),
            },
            Notes: new[]
            {
                "ASYNCHRONOUS and cluster-wide: the response confirms the request was PUBLISHED, not that files exist. Every selected node gathers, then the orchestrator pulls to the NAS over SMB.",
                "Poll get_cluster_diagnostic_status until manifestPaths is non-empty — that is the completion signal.",
                "An empty nodes[] is refused rather than read as 'every node': the editor's own panel disables its button on the same condition, and dumping the whole cluster is a different operation from dumping one node.",
                "This adds no collection mechanism — it publishes the same CQRS intent the operator's button publishes, onto whichever node's orchestration bus is reachable.",
                "The request is logged with its transaction id and target nodes: a headless origin never pre-flights a confirmation, so that log is the safety net (ruling 53).",
            },
            ExampleArgsJson: "{\"nodes\":[1]}",
            ExampleGist: "collect diagnostics from node 1 to the NAS"),

        // ⭐ CE-4002 — the membership routes (docs/DESIGN_Cluster_Membership_Use_Cases.md §3.1).
        [("GET", "/cluster/roster")] = new RouteDoc(
            Tool:    "get_cluster_roster",
            Group:   "A — Lifecycle & status",
            Summary: "Every node the cluster master holds, as the MASTER sees it: id, subsystem, its own reported state, roles, heartbeat age.",
            Returns: "{ count, nodes[{ nodeId, subsystem, reportedState, roles, secondsSinceHeartbeat }], load{ notice }, note }",
            Hint:    "No args. Example: get_cluster_roster({})",
            Params: System.Array.Empty<RouteParam>(),
            Notes: new[]
            {
                "reportedState is each node's OWN claim from its heartbeat, not the cluster's state — read get_status for that.",
                "load.notice is the master's LOAD GATE: a scenario load needs a Brain and a MuscleGround node. While one waits it reads \"waiting for: MuscleGround (SimHost)\"; after the master refused it, the reason; null when no load is held.",
                "A node leaves the list after the heartbeat timeout (default 5 s) of silence.",
                "Only the node hosting the ClusterMaster can answer (the orchestrator process, or the editor's in-process core); elsewhere it answers 503 — check the 'cluster.roster' cell in get_capabilities.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "list the nodes in the cluster"),

        [("POST", "/cluster/transition")] = new RouteDoc(
            Tool:    "request_cluster_transition",
            Group:   "A — Lifecycle & status",
            Summary: "Ask the cluster master to move the whole cluster to a target state — the same TransitionStateIntent the operator panel publishes.",
            Returns: "{ published:true, transactionId, target, note }",
            Hint:    "Req: target (a ClusterState name). Example: request_cluster_transition({target:\"OperatingReplay\", exerciseId:\"…\"})",
            Params: new RouteParam[]
            {
                new("target", "string", true, "The ClusterState to move to (e.g. Idle, OperatingReplay); the refusal lists the legal names"),
                new("scenario", "string", false, "Scenario id for a load"),
                new("exerciseId", "string", false, "Exercise GUID for a replay"),
                new("timeMode", "string", false, "Time mode for the load"),
                new("targetWallTicks", "number", false, "Wall-clock ticks the transition should take effect at"),
            },
            Notes: new[]
            {
                "ASYNCHRONOUS: the answer confirms the intent was PUBLISHED, not that the cluster reached the state — poll get_status or get_cluster_roster.",
                "Pass the returned transactionId to cancel_cluster_operation to cancel it; a cancel returns the cluster to Idle (R-4004).",
                "Any node with an orchestration bus can publish it; a host without one answers 503.",
            },
            ExampleArgsJson: "{\"target\":\"Idle\"}",
            ExampleGist: "return the cluster to Idle",
            ManualVerify: true),

        [("POST", "/cluster/cancel")] = new RouteDoc(
            Tool:    "cancel_cluster_operation",
            Group:   "A — Lifecycle & status",
            Summary: "The operator's cancel (R-4004): cancel a cluster request in flight by its transactionId; the cluster returns to Idle.",
            Returns: "{ published:true, targetId }",
            Hint:    "Req: targetId (the transactionId from request_cluster_transition). Example: cancel_cluster_operation({targetId:\"…\"})",
            Params: new RouteParam[]
            {
                new("targetId", "string", true, "The transactionId of the request to cancel (a GUID)"),
            },
            Notes: new[]
            {
                "ASYNCHRONOUS: confirms the cancel was published; read get_status for the cluster returning to Idle.",
                "Any node with an orchestration bus can publish it; a host without one answers 503.",
            },
            ExampleArgsJson: "{\"targetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "cancel a cluster load in flight",
            ManualVerify: true),

        [("GET", "/cluster/diagnostics/status")] = new RouteDoc(
            Tool:    "get_cluster_diagnostic_status",
            Group:   "Y — node diagnostics",
            Summary: "Whether a cluster transaction is in flight, and the file manifest of the last successful diagnostic dump.",
            Returns: "{ inFlight, manifestPaths[], manifestCount, note }",
            Hint:    "No args. Example: get_cluster_diagnostic_status({})",
            Params: System.Array.Empty<RouteParam>(),
            Notes: new[]
            {
                "Reads the same read model the ExCon's Cluster Diagnostics panel renders, so it answers what a human at the console would see.",
                "manifestPaths are relative to the NAS base directory and describe the LAST SUCCESSFUL dump. EMPTY means none has completed yet — not that one failed.",
                "inFlight covers any cluster transaction, not only a dump.",
                "Only a node that builds and pumps a ClusterUiCache can answer (in --mode all that is ExCon); a host without one can still TRIGGER a dump but cannot observe it, and says so.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "check whether the diagnostic dump finished"),

        [("GET", "/diagnostics/render")] = new RouteDoc(
            Tool:    "get_render_diagnostics",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-1145 — the map view's frame rate (fps, frame-time mean / p95 / max over ~2 s) and its camera (mode 2D / 3D / FPS, eye, forward, yaw / pitch, distance, height above the ground, viewport).",
            Returns: "{ fps, frameMsMean, frameMsP95, frameMsMax, frames, windowSeconds, mode, camera{ eye[3], forward[3], yawDeg, pitchDeg, distance, heightAboveGround, viewport[2] } }",
            Hint:    "Zeros until the window has drawn a frame (a headless host never draws). Example: get_render_diagnostics({})",
            ExampleArgsJson: "{}",
            ExampleGist: "read the map view's frame rate and camera"),
        [("POST", "/film/shot")] = new RouteDoc(
            Tool:    "film_shot",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-3289 — hold the 3-D map's camera on a shot (a pose, an eye and a target, or a unit to follow) until released; switches the map to 3-D. The operator's and the scenario's camera are left alone.",
            Returns: "{ follow, lookAt[3], distance, yawDeg, pitchDeg, fovDeg } — the shot held",
            Hint:    "One of: {follow, distance?, yawDeg?, pitchDeg?, lift?} · {eye:[x,y,z], target:[x,y,z]} · {lookAt:[x,y,z], distance?, yawDeg?, pitchDeg?} · ⭐ CE-4038 {spec:{a take's shot — follow · over+toward · lookAt · eye+target, by NAME, with orbit, to, fov}, t:sim seconds into it} (evaluated by the director's shot math). Yaw from east counter-clockwise (90 = looking north), pitch negative looks down; any shot may add fovDeg (a telephoto lens). Example: film_shot({follow:1000, distance:25, yawDeg:200, pitchDeg:-15})",
            Params: new RouteParam[]
            {
                new("follow", "number", false, "Network id of the unit to follow (its position, every frame)"),
                new("eye", "array", false, "The camera position [x,y,z] (with target)"),
                new("target", "array", false, "The point looked at [x,y,z] (with eye)"),
                new("lookAt", "array", false, "The point looked at [x,y,z] (a pose, with distance / yawDeg / pitchDeg)"),
                new("distance", "number", false, "Metres from the point looked at (follow: 30, lookAt: 100)"),
                new("yawDeg", "number", false, "View heading, degrees from east counter-clockwise (default 0)"),
                new("pitchDeg", "number", false, "View elevation, degrees, negative looks down (follow: -20, lookAt: -30)"),
                new("lift", "number", false, "Follow: metres above the unit's position looked at (default 1)"),
                new("fovDeg", "number", false, "Vertical field of view, degrees (default: the camera's 45; narrow = telephoto)"),
                new("spec", "object", false, "⭐ CE-4038 — a take's shot in its own keys (docs/DESIGN_Film_Director.md §4), units by name"),
                new("t", "number", false, "With spec: sim seconds into the shot (its orbit and its 'to')"),
            },
            Notes: new[] { "docs/DESIGN_Map_3D_Mode.md §6ab. The camera is a driver, like first person: never saved to the scenario." },
            ExampleArgsJson: "{\"follow\":1000,\"distance\":25,\"yawDeg\":200,\"pitchDeg\":-15}",
            ExampleGist: "follow unit 1000 from behind and above"),
        [("DELETE", "/film/shot")] = new RouteDoc(
            Tool:    "film_release",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-3289 — give the 3-D map's camera back to the operator (it keeps the last pose).",
            Returns: "{ released: true }",
            Hint:    "Example: film_release({})",
            ExampleArgsJson: "{}",
            ExampleGist: "release the film camera"),
        [("POST", "/film/capture")] = new RouteDoc(
            Tool:    "film_capture",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-3289 — write the next drawn frame's 3-D SCENE (sky, terrain, bodies, effects, rounds — no panels, gizmos, cards or HUD) as a PNG, at any size.",
            Returns: "{ path, width, height, simTime }",
            Hint:    "Req: path (.png). Optional: width (1280), height (720), supersample (1, up to 4 — renders larger and scales down). Needs a window (Xvfb) and the map on screen; 504 after 30 s otherwise. Pause the sim and step it between captures for a smooth film. Example: film_capture({path:\"/tmp/f/0001.png\"})",
            Params: new RouteParam[]
            {
                new("path", "string", true, "Where to write the PNG"),
                new("width", "number", false, "Frame width in px (default 1280)"),
                new("height", "number", false, "Frame height in px (default 720)"),
                new("supersample", "number", false, "Render this many times larger and scale down, 1-4 (default 1)"),
            },
            Notes: new[] { "docs/DESIGN_Map_3D_Mode.md §6ab; scripts/film.py drives it." },
            ExampleArgsJson: "{\"path\":\"/tmp/film/0001.png\"}",
            ExampleGist: "capture the scene to a PNG"),
        // ⭐ CE-4038 (docs/DESIGN_Film_Director.md §4) — the film director, the map's layers and the sound
        [("GET", "/director/takes")] = new RouteDoc(
            Tool:    "director_list_takes",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — the film takes: every scenario with a takes/ folder and its takes (scenarios/<s>/takes/<t>.take.json).",
            Returns: "{ root, count, scenarios[{ scenario, takes[] }] }",
            Hint:    "Optional: scenario (one scenario's takes). Example: director_list_takes({})",
            Params: new RouteParam[] { new("scenario", "string", false, "Only this scenario's takes") },
            Notes: new[] { "docs/DESIGN_Film_Director.md §4, D-2: a take lives beside its scenario; 'Save As' copies the takes." },
            ExampleArgsJson: "{}",
            ExampleGist: "list the film takes"),
        [("GET", "/director/reels")] = new RouteDoc(
            Tool:    "director_list_reels",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — the reels (films/<name>.reel.json): playlists of takes, each ONE movie on film.",
            Returns: "{ root, reels[{ name, title, items, problems[] }] }",
            Hint:    "Example: director_list_reels({})",
            ExampleArgsJson: "{}",
            ExampleGist: "list the reels"),
        [("GET", "/director/take/{scenario}/{take}")] = new RouteDoc(
            Tool:    "director_get_take",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — one take as JSON (its shots, anchors, baked anchor times) and what is wrong with it.",
            Returns: "{ scenario, take, path, json{…}, problems[] }",
            Hint:    "Req: scenario, take (main is the default take). Example: director_get_take({scenario:\"ua-tank-ammo\", take:\"main\"})",
            Params: new RouteParam[]
            {
                new("scenario", "string", true, "The scenario's name"),
                new("take", "string", true, "The take's name (main)"),
            },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\",\"take\":\"main\"}",
            ExampleGist: "read a take"),
        [("PUT", "/director/take/{scenario}/{take}")] = new RouteDoc(
            Tool:    "director_put_take",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — write a take (authoring by an agent): the body is the take; refused, with every problem, when a key or a value is wrong.",
            Returns: "{ saved, shots, anchors }",
            Hint:    "Req: scenario, take, and the take's keys: title?, caption?, tickHz? (60 | 20 | 15), endAt?, anchors{name:{on, who?, by?, target?, is?, n?, point?, within?, below?}}, shots[{at, seconds, follow | over+toward | lookAt | eye+target, distance?, yaw?, pitch?, orbit?, lift?, back?, height?, ahead?, fov?, to?, ease?, speed?, caption?, layers?, sound?}]. 'at' is sim seconds or '<anchor>[+-offset]'. Example: director_put_take({scenario:\"ua-tank-ammo\", take:\"short\", shots:[{at:1, seconds:4, follow:\"Tank\", distance:20}]})",
            Params: new RouteParam[]
            {
                new("scenario", "string", true, "The scenario's name"),
                new("take", "string", true, "The take's name"),
                new("shots", "array", true, "The shots, in order"),
                new("anchors", "object", false, "Named beats the scout bakes (on: shot · blast · hit · near · task · side · health · embark · gesture)"),
                new("title", "string", false, "The take's title (its card)"),
                new("caption", "string", false, "The caption shown under the picture"),
                new("tickHz", "number", false, "The fixed step rate: 60 (default), 20 or 15 when 60 cannot keep up in real time"),
                new("endAt", null, false, "When the take ends: sim seconds or an anchor expression (default: the last shot's end)"),
            },
            Notes: new[] { "docs/DESIGN_Film_Director.md §4. The shot keys are film.py's plan keys plus to, ease, speed, caption, layers, sound." },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\",\"take\":\"short\",\"shots\":[{\"at\":1,\"seconds\":4,\"follow\":\"Tank\",\"distance\":20}]}",
            ExampleGist: "write a take"),
        [("POST", "/director/play")] = new RouteDoc(
            Tool:    "director_play",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — play a reel or one take: each scenario opened from its file, the take's shots held on the 3-D camera, ONE fixed step per rendered frame (R-3038). Without capture it plays LIVE on screen and saves nothing; with capture it renders frame by frame and writes ONE movie of the reel.",
            Returns: "the director's status (see director_status)",
            Hint:    "One of: {reel} · {scenario, take?} (take: a name, default main, or the take itself as an object — played, never saved). Optional: speed (live), loop (live), capture{dir, fps?, size?\"1280x720\", supersample?, captions?, movie?, encode?}. Poll director_status. Example: director_play({reel:\"highlights\"})",
            Params: new RouteParam[]
            {
                new("reel", "string", false, "A reel's name (films/<name>.reel.json)"),
                new("scenario", "string", false, "A scenario, to play one take"),
                new("take", null, false, "The take's name (default main), or a take object"),
                new("speed", "number", false, "Live: sim seconds per wall second (default 1)"),
                new("loop", "boolean", false, "Live: play the reel again and again"),
                new("capture", "object", false, "To film: {dir (one take: beside it, takes/<take>.render/), fps (30), size (1280x720), supersample (1), captions (true: burned in), movie (dir/<reel>.mp4), encode (true)}"),
                new("recording", "string", false, "⭐ CE-4059 (TE-15) — the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened FIRST through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)"),
            },
            Notes: new[] { "docs/DESIGN_Film_Director.md §0, §3a, §3b, §4a. Plays in the editor's PREVIEW (R-4061); every item loads again (R-3034). Capture needs a window (Xvfb) and ffmpeg.",
                           "⭐ CE-4059 (docs/DESIGN_Director_Timeline_Editor.md §11r): in the Replay Browser a capture is paced by the take, not real time — the recording steps once per captured frame; {recording, take, capture:{}} renders a take of that recording to an MP4 beside it (scenario: the recording's own when omitted)." },
            ExampleArgsJson: "{\"reel\":\"highlights\"}",
            ExampleGist: "play a reel live"),
        [("POST", "/director/scout")] = new RouteDoc(
            Tool:    "director_scout",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — scout a take or a reel: run each take once, stepped as the film will be, and BAKE its anchor times (with the step rate, the scenario's hash) into the take. The report names every beat that did not happen — a scenario health check.",
            Returns: "the director's status; per item: found{anchor: time}, missing[], mismatches[] (a beat that moved since the last scout = a determinism finding)",
            Hint:    "{reel} or {scenario, take?}. Example: director_scout({scenario:\"ua-tank-ammo\"})",
            Params: new RouteParam[]
            {
                new("reel", "string", false, "A reel's name"),
                new("scenario", "string", false, "A scenario"),
                new("take", "string", false, "The take (default main)"),
                new("bake", "boolean", false, "Write the found times into the take (default true; false = a measurement only, e.g. the two-run identity report)"),
            },
            Notes: new[] { "docs/DESIGN_Film_Director.md §0 (R-3038): the filmed run IS the scouted run once CE-3293 S5 lands; until then re-scout after S5." },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\"}",
            ExampleGist: "bake a take's anchor times"),
        [("POST", "/director/stop")] = new RouteDoc(
            Tool: "director_stop", Group: "Y — node diagnostics",
            Summary: "⭐ CE-4038 — stop the director (the preview is left, the camera released, the user's layers and sound back).",
            Returns: "the director's status", Hint: "Example: director_stop({})", ExampleArgsJson: "{}", ExampleGist: "stop the director"),
        [("POST", "/director/next")] = new RouteDoc(
            Tool: "director_next", Group: "Y — node diagnostics",
            Summary: "⭐ CE-4038 — end this reel item (reported as stopped) and go on to the next.",
            Returns: "the director's status", Hint: "Example: director_next({})", ExampleArgsJson: "{}", ExampleGist: "skip to the next take"),
        [("POST", "/director/pause")] = new RouteDoc(
            Tool: "director_pause", Group: "Y — node diagnostics",
            Summary: "⭐ CE-4038 — pause the live show (⚠ a paused frame changes the run — the report says so).",
            Returns: "the director's status", Hint: "Example: director_pause({})", ExampleArgsJson: "{}", ExampleGist: "pause the show"),
        [("POST", "/director/resume")] = new RouteDoc(
            Tool: "director_resume", Group: "Y — node diagnostics",
            Summary: "⭐ CE-4038 — resume the live show.",
            Returns: "the director's status", Hint: "Example: director_resume({})", ExampleArgsJson: "{}", ExampleGist: "resume the show"),
        [("GET", "/director/checks/{scenario}")] = new RouteDoc(
            Tool:    "director_get_checks",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4042 — a scenario's machine-checkable conditions (scenarios/<s>/checks.json) as JSON, what is wrong with them, and its golden's size.",
            Returns: "{ scenario, path, json{…}, problems[], golden?{ path, steps, snapshots, shots } }",
            Hint:    "Req: scenario. Example: director_get_checks({scenario:\"ua-tank-ammo\"})",
            Params: new RouteParam[] { new("scenario", "string", true, "The scenario's name") },
            Notes: new[] { "docs/DESIGN_Scenario_Regression.md §3 (the grammar), §4 (the golden)." },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\"}",
            ExampleGist: "read a scenario's checks"),
        [("PUT", "/director/checks/{scenario}")] = new RouteDoc(
            Tool:    "director_put_checks",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4042 — write a scenario's checks (authoring by an agent): refused, with every problem, when a key, a cue or a time is wrong. Written to the working copy AND the source tree.",
            Returns: "{ saved[], expect, anchors, do, golden }",
            Hint:    "Req: scenario, expect[{name, on, …, after?, before?} | {name, never:{on,…}} | {name, on,…, atEnd:true}]. Cues: shot{by?, at?, hit?, n?, where?} · blast{by?, n?, where?} · hit{target} · field{who, path, op, value, quantifier?} · near · task · side · health · embark · gesture. Example: director_put_checks({scenario:\"ua-tank-ammo\", runFor:60, golden:{every:10}, expect:[{name:\"the M1 fires at the T-72\", on:\"shot\", by:\"Tank\", at:\"Enemy Tank\", before:30}]})",
            Params: new RouteParam[]
            {
                new("scenario", "string", true, "The scenario's name"),
                new("expect", "array", true, "The expectations"),
                new("runFor", "number", false, "Sim seconds the run lasts (default 60)"),
                new("golden", null, false, "{every: seconds} — compare with a golden; false — conditions only (a demo)"),
                new("anchors", "object", false, "Named cues other times refer to"),
                new("do", "array", false, "Changes made during the run: [{name?, at, set:{who, component, patch}}]"),
                new("sampleEvery", "number", false, "Sim seconds between reads of the world (default 0.25)"),
            },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\",\"runFor\":60,\"expect\":[{\"name\":\"fires\",\"on\":\"shot\",\"by\":\"Tank\"}]}",
            ExampleGist: "write a scenario's checks"),
        [("POST", "/director/verify")] = new RouteDoc(
            Tool:    "director_verify",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4042 — verify scenarios: each loaded from its file, stepped once per frame for runFor, judged by its checks.json and compared with its golden.json (the first difference named). Read the verdicts in director_status's reports[].verify.",
            Returns: "the director's status (phase, reports[{ status: pass|fail, verify{ passed, verdicts[{name, ok, t, kind}], golden: none|same|different|missing|captured|not-captured, goldenDiff[], written[], problems[], replay: null|same|different|failed, replaySnapshots, replayDiff[], replayNotCarried[] } }])",
            Hint:    "{scenario} or {scenarios:[…]} or {all:true}; ⭐ replay:true runs it LIVE and compares it, snapshot by snapshot, with its OWN recording played back (CE-4045 H1a — no golden compared; components the recording's manifest does not carry are listed in replayNotCarried, never failed); capture:true re-records the golden (refused for a failing run, and refused on a RELOAD). ⭐ fresh:true runs it in a FRESH child process per scenario — the regression verdict and the only safe re-record; it answers {job} at once, read the report with director_verify_job. Without fresh it plays in THIS editor (on screen; each run after the first is a reload, CE-3294). Example: director_verify({scenario:\"ua-tank-ammo\", fresh:true})",
            Params: new RouteParam[]
            {
                new("scenario", "string", false, "One scenario"),
                new("scenarios", "array", false, "Several scenarios (one process: each after the first is a reload)"),
                new("all", "boolean", false, "Every scenario with a checks.json"),
                new("capture", "boolean", false, "Re-record each golden (default false)"),
                new("fresh", "boolean", false, "Run in a fresh child process per scenario (the regression verdict); answers a job id"),
                new("replay", "boolean", false, "Run LIVE and compare with the run's own recording played back (CE-4045 H1a, R-4085); not with capture"),
            },
            Notes: new[] { "docs/DESIGN_Scenario_Regression.md — R-4085 (replay: the live run against its own recording, §10-1), R-4065 (conditions for all, a golden for a feature scenario, a fresh process each), R-4066 (the engine runs it; HTTP and the command line drive it)." },
            ExampleArgsJson: "{\"scenario\":\"ua-tank-ammo\"}",
            ExampleGist: "verify a scenario against its checks and golden"),
        [("GET", "/director/verify/jobs")] = new RouteDoc(
            Tool:    "director_verify_jobs",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4042 — the fresh-process verify jobs this editor started (POST /director/verify {fresh:true}): each one's state, exit code and output folder.",
            Returns: "{ jobs[{ job, state: running|done, exitCode, passed, scenarios[], capture, replay, out, junit, log, seconds }] }",
            Hint:    "Example: director_verify_jobs({})",
            ExampleArgsJson: "{}",
            ExampleGist: "list the fresh verify jobs"),
        [("GET", "/director/verify/jobs/{job}")] = new RouteDoc(
            Tool:    "director_verify_job",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4042 — one fresh-process verify job: running or done, and when done its full report (every scenario's verdicts, golden, first difference).",
            Returns: "{ job, state, exitCode, passed, scenarios[], out, junit, log, seconds, report?{ passed, failed[], scenarios[{ scenario, status, verdicts[], golden, goldenDiff[], written[] }] } }",
            Hint:    "Req: job (from director_verify {fresh:true}). Poll until state is done. Example: director_verify_job({job:\"v1-101500\"})",
            Params: new RouteParam[] { new("job", "string", true, "The job id") },
            ExampleArgsJson: "{\"job\":\"v1-101500\"}",
            ExampleGist: "read a fresh verify job's report"),
        [("GET", "/director/moments")] = new RouteDoc(
            Tool:    "director_moments",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4059 (CR-8) — the moments the drafter can film in the index the Event Timeline shows: every first shot of a shooter at a target, every hit, every blast, scored (blast 3 + damage/100, hit 2, shot 1), a moment within 2 s after another merged into it, in time order.",
            Returns: "{ scenario, complete, count, moments[{ lane, eventIndex, frame, sim, actor, actorName, subject, subjectName, score }] }",
            Hint:    "Pick one and POST /director/draft {lane, eventIndex}. 409 while no index is shown (run the scenario first). complete:false = a recording's index is still being read (ask again). Example: director_moments({})",
            Params: new RouteParam[] { new("recording", "string", false, "⭐ CE-4059 (TE-15) — the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened FIRST through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)") },
            ExampleArgsJson: "{}",
            ExampleGist: "list the moments the drafter can film"),
        [("POST", "/director/draft")] = new RouteDoc(
            Tool:    "director_draft",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4059 (CR-8) — draft a shot sequence on one event of the shown index (establish, set-up, the moment, consequence, and a slow replay from the reverse angle) and OPEN it in the take session as a new, unsaved take — exactly what right-clicking the event and choosing \"Draft a shot sequence here\" does.",
            Returns: "{ scenario, take, opened, saved: false, keys, segments, json }",
            Hint:    "Then play it (director_play {scenario, take}: the open take is played, unsaved), adjust it, and save it from the Director window. 409 when another open take has unsaved edits. Example: director_draft({lane:\"combat.shot\", eventIndex:0})",
            Params: new RouteParam[]
            {
                new("lane", "string", true, "The event's lane: combat.shot, combat.hit or combat.blast"),
                new("eventIndex", "integer", true, "The event's position in its lane (GET /director/moments)"),
                new("replay", "boolean", false, "Add the slow replay from the reverse angle (default true)"),
                new("recording", "string", false, "⭐ CE-4059 (TE-15) — the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened FIRST through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)"),
            },
            ExampleArgsJson: "{\"lane\":\"combat.shot\",\"eventIndex\":0}",
            ExampleGist: "draft a take on the first shot"),
        [("POST", "/director/stills")] = new RouteDoc(
            Tool:    "director_stills",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4059 P3 (docs/DESIGN_Director_Timeline_Editor.md §11r) — the STILLS of a take: a PNG of the program's camera at each film second asked (one seek and one frame each — the runner plays from that second and holds it) and a CONTACT SHEET (one PNG, a labelled grid). The Replay Browser with a window only: refused on a host that cannot seek (the editor) and on a headless one (no renderer) — plan:true answers the plan anywhere.",
            Returns: "{ state (done · failed · running), scenario, take, dir, planned[{ number, filmTime, sourceTime, segment, name }], stills[{ number, name, filmTime, sourceTime, segment, shownSim, path, width, height }], sheet, sheetGrid{ columns, rows, cellWidth, cellHeight, width, height }, problems[] } — with plan:true: { state: planned, dir, planned[], sheetGrid, problems[] }",
            Hint:    "One of: times:[film seconds] · everyKey:true (each camera key once on screen) · neither (the keys that name a still — a documentation take). Default dir: beside the take (takes/<take>.stills/). Then read the sheet (an image). Example: director_stills({recording:\"/…/node_1.fdp\", take:\"main\", everyKey:true})",
            Params: new RouteParam[]
            {
                new("recording", "string", false, "⭐ CE-4059 (TE-15) — the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened FIRST through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)"),
                new("scenario", "string", false, "The take's scenario (default: the one the open recording names)"),
                new("take", "string", false, "The take (default main); the OPEN take, unsaved edits included, is what is filmed"),
                new("times", "array", false, "Film seconds to still (through the take's segments: a film second of a slow or replayed stretch maps to its source second)", ItemsJson: "{\"type\":\"number\"}"),
                new("everyKey", "boolean", false, "A still at every camera key, once its camera is on screen (after its fly or dip-in)"),
                new("width", "integer", false, "Each still's width in px (default 1280)"),
                new("height", "integer", false, "Each still's height in px (default 720)"),
                new("dir", "string", false, "Where the PNGs and contact-sheet.png go (default beside the take: takes/<take>.stills/)"),
                new("captions", "boolean", false, "Burn the subtitle on screen into each still (default true)"),
                new("plan", "boolean", false, "Only answer the plan (the film → source seconds and the names); nothing is captured — any host"),
                new("wait", "boolean", false, "Answer when the job is done (default true; false: answer at once, then GET /director/stills)"),
            },
            Notes: new[] { "docs/DESIGN_Director_Timeline_Editor.md §11r; docs/DESIGN_Film_Production.md §4a (CE-4060: the documentation take's still keys render through this route).",
                           "Needs a window: on a server run the Replay Browser under Xvfb (DISPLAY=:99)." },
            ExampleArgsJson: "{\"take\":\"main\",\"everyKey\":true,\"plan\":true}",
            ExampleGist: "plan a take's stills"),
        [("POST", "/director/promote")] = new RouteDoc(
            Tool:    "director_promote",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-1304 P4 (docs/DESIGN_Director_Timeline_Editor.md §11s) — PROMOTE a take adjusted on a recording (saved beside it) into its scenario's own takes (<scenarios root>/<scenario>/takes/): a COPY — its keys already hang on events (CR-6), which every later run, live or recorded, finds again in its own index (CR-5). The same as the Director window's \"Promote to scenario\". The Replay Browser only: a host whose takes ARE the scenario's (the editor) answers 400.",
            Returns: "{ promoted: true, from, to, overwrote, bake (kept · dropped · none), fixedKeys[] }",
            Hint:    "The SAVED take is copied (409 while it has unsaved edits). The bake is kept when it was made for the scenario file as it is now, dropped otherwise. fixedKeys = keys at a fixed second (no event before them): they do not follow a timing change. 409 when the scenario already has a take of that name — pass overwrite:true to replace it. Example: director_promote({take:\"main\"})",
            Params: new RouteParam[]
            {
                new("take", "string", true, "The take beside the open recording"),
                new("scenario", "string", false, "Its scenario (default: the one the open recording names)"),
                new("overwrite", "boolean", false, "Replace the scenario's take of the same name (default false: 409)"),
                new("recording", "string", false, "⭐ CE-4059 (TE-15) — the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened FIRST through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)"),
            },
            Notes: new[] { "docs/DESIGN_Director_Timeline_Editor.md §11s (D5 promote = a copy, D6 the bake rule, D7 a recording shows its scenario's own takes); docs/DESIGN_Film_Production.md workflow ⑧." },
            ExampleArgsJson: "{\"take\":\"main\"}",
            ExampleGist: "promote the recording's main take"),
        [("GET", "/director/stills")] = new RouteDoc(
            Tool:    "director_stills_status",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4059 P3 (§11r) — the last stills job: running / done / failed, the stills written so far and the contact sheet.",
            Returns: "the same object POST /director/stills answers ({ state, dir, planned[], stills[], sheet, sheetGrid, problems[] }; state idle before any job)",
            Hint:    "Example: director_stills_status({})",
            ExampleArgsJson: "{}",
            ExampleGist: "read the stills job"),
        [("POST", "/director/renders")] = new RouteDoc(
            Tool:    "director_render_enqueue",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-1306 R4 (docs/DESIGN_Director_Timeline_Editor.md §11u) — QUEUE a render (or several: renders:[…]): a movie of a take or a reel (the /director/play capture, to MP4), or a take's stills + contact sheet (kind:\"stills\", the /director/stills job). Renders run ONE AFTER ANOTHER in the background of this host through its one director; each job's recording (recording=) is opened at ITS turn. On the Replay Browser a render SEEKS the recording; on the editor it runs the scenario live (paced by the take). Refused on a headless host (no renderer).",
            Returns: "{ running, queued, refusal, jobs[{ id, state (queued · opening · running · done · failed · cancelled), kind, what, recording, scenario, take, reel, size, fps, dir, output, outputs[], frames, item, items, waiting, problems[], enqueuedAt, startedAt, finishedAt, wallSeconds }], queuedIds[], refused[{ index, error }] }",
            Hint:    "Answers at once; poll GET /director/renders (director_renders) until each job is done / failed. A failed job (an unknown take, a recording that does not open) does not stop the queue. While a render runs, /director/play and /director/stills answer 409. Default folder: a folder of the job's own beside the take (takes/<take>.render/<id>/, takes/<take>.stills/<id>/), a reel's under the scenarios root (.renders/<reel>/<id>/). Example: director_render_enqueue({renders:[{recording:\"/…/a/node_1.fdp\", take:\"main\", capture:{size:\"1280x720\", fps:30}}, {recording:\"/…/b/node_1.fdp\", kind:\"stills\", take:\"_doc\"}]})",
            Params: new RouteParam[]
            {
                new("kind", "string", false, "movie (default: one MP4 of the take or the reel) or stills (a take's stills and contact sheet)", EnumJson: "[\"movie\",\"stills\"]"),
                new("recording", "string", false, "⭐ the Replay Browser only: a recording (a node_N.fdp path, its folder, or an exercise id) opened when THIS job starts, through the same open POST /replay makes; 400 on a host that opens no recordings (the editor)"),
                new("scenario", "string", false, "The take's scenario (default: the one the open recording names, or the editor's loaded scenario)"),
                new("take", "string", false, "The take (default main)"),
                new("reel", "string", false, "A saved reel (films/<name>.reel.json) instead of one take — a movie only"),
                new("capture", "object", false, "A movie's capture: { dir?, fps? (1…60, default 30), size? (\"WxH\", default 1280x720), supersample?, captions? (default true), movie? (the MP4's path), encode? (false: frames only) }"),
                new("times", "array", false, "Stills: the film seconds to still (else everyKey, else the take's still keys — a documentation take)", ItemsJson: "{\"type\":\"number\"}"),
                new("everyKey", "boolean", false, "Stills: a still at every camera key once its camera is on screen"),
                new("width", "integer", false, "Stills: each still's width (default 1280)"),
                new("height", "integer", false, "Stills: each still's height (default 720)"),
                new("dir", "string", false, "Stills: where the PNGs and contact-sheet.png go (default takes/<take>.stills/<id>/)"),
                new("renders", "array", false, "Several renders at once, each an object with the fields above (queued in order; each refusal named in refused[])", ItemsJson: "{\"type\":\"object\"}"),
            },
            Notes: new[] { "docs/DESIGN_Director_Timeline_Editor.md §11u (R4: the reel composer and the render queue); §11r (stills, MP4 in the RB); docs/DESIGN_Film_Production.md §4a (CE-4060: a night run queues one stills job per recorded scenario).",
                           "Needs a window: on a server run the Replay Browser (or the editor) under Xvfb (DISPLAY=:99)." },
            ExampleArgsJson: "{\"take\":\"main\",\"capture\":{\"size\":\"640x360\",\"fps\":15}}",
            ExampleGist: "queue a render of the main take"),
        [("GET", "/director/renders")] = new RouteDoc(
            Tool:    "director_renders",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-1306 R4 (§11u) — the RENDER QUEUE: the running job, the queued ones in order and the ended ones (done with their output path, failed with why, cancelled).",
            Returns: "{ running, queued, refusal, jobs[{ id, state, kind, what, recording, scenario, take, reel, size, fps, dir, output, outputs[], frames, item, items, waiting, problems[], enqueuedAt, startedAt, finishedAt, wallSeconds }] }",
            Hint:    "output = the MP4 (or the frames' folder when encode:false; the contact sheet for stills), outputs = the stills. waiting = why a queued job has not started (the director plays something else). Example: director_renders({})",
            ExampleArgsJson: "{}",
            ExampleGist: "read the render queue"),
        [("DELETE", "/director/renders/{id}")] = new RouteDoc(
            Tool:    "director_render_cancel",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-1306 R4 (§11u) — CANCEL a render: a queued one never runs; the running one stops the director (or the stills job) and the queue goes on with the next.",
            Returns: "the queue ({ running, queued, refusal, jobs[] }) — the job cancelled; 404 no such render; 409 it had already ended",
            Hint:    "Req: id (r1, r2, … from director_render_enqueue / director_renders). Example: director_render_cancel({id:\"r2\"})",
            Params: new RouteParam[]
            {
                new("id", "string", true, "The render's id (r1, r2, …)"),
            },
            ExampleArgsJson: "{\"id\":\"r1\"}",
            ExampleGist: "cancel render r1"),
        [("GET", "/director/status")] = new RouteDoc(
            Tool:    "director_status",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 — what the director does: phase (Idle · Loading · Entering · Playing · Encoding · Done · Stopped), the reel item, the shot, sim time, the realtime factor, and a report per item.",
            Returns: "{ phase, mode, reel, item, items, paused, speed, shot, simTime, realtimeFactor, tickHz, frames, movie, error, reports[{ scenario, take, status, problems[], missing[], mismatches[], skippedReplays[], unresolved[], found{}, tickHz, usedBake, realtimeFactor, steps, lateFrames, waitFrames, maxStepsInAFrame, maxRetiredInAFrame, frames, simEnd, wallSeconds, cuts[{ shot, t, rounds, units{ name: [x, y, z, health] } }] }], anchorsSeen?, mismatches? }",
            Hint:    "maxStepsInAFrame is 1 by construction (the stepping rule); maxRetiredInAFrame is what the master took in one frame (1 when the rule holds at the kernel); waitFrames counts frames with two steps still outstanding. Example: director_status({})",
            ExampleArgsJson: "{}",
            ExampleGist: "read the director's status"),
        [("GET", "/map/layers")] = new RouteDoc(
            Tool:    "get_map_layers",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 B10 (R-4060) — the tactical map's layers, by the names and hover text the Layers panel shows, per view (2-D and 3-D sets), and whether a film take's override is in force.",
            Returns: "{ shown: \"2d\"|\"3d\", override, 2d[{ name, display, on, doc }], 3d[…] }",
            Hint:    "Optional: view (2d | 3d). Example: get_map_layers({view:\"3d\"})",
            Params: new RouteParam[] { new("view", "string", false, "2d or 3d (default both)") },
            ExampleArgsJson: "{\"view\":\"3d\"}",
            ExampleGist: "list the map's layers"),
        [("POST", "/map/layers")] = new RouteDoc(
            Tool:    "set_map_layers",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 B10 (R-4060) — turn map layers on and off EXACTLY as the user's click does (saved to the user's file like one).",
            Returns: "the view's layers (as get_map_layers)",
            Hint:    "Optional: view (2d | 3d, default the one shown), on[], off[] — names as get_map_layers lists them (Group.Row). Example: set_map_layers({view:\"3d\", on:[\"Ai.VisionCones\"], off:[\"Units.Labels\"]})",
            Params: new RouteParam[]
            {
                new("view", "string", false, "2d or 3d (default: the view shown)"),
                new("on", "array", false, "Layers to turn on"),
                new("off", "array", false, "Layers to turn off"),
            },
            Notes: new[] { "A film take's layers are NOT this: they are a transient override, never saved (docs/DESIGN_Film_Director.md §4b)." },
            ExampleArgsJson: "{\"view\":\"3d\",\"on\":[\"Ai.VisionCones\"]}",
            ExampleGist: "show the vision cones"),
        [("GET", "/audio")] = new RouteDoc(
            Tool:    "get_audio",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 B11 (R-4059) — the Sound switch (the scene heard from the camera in 2-D and 3-D; FPS always sounds), a film take's own switch, the master volume and mute.",
            Returns: "{ sound, takeSound, master, muted, note }",
            Hint:    "Example: get_audio({})",
            ExampleArgsJson: "{}",
            ExampleGist: "read the sound switch"),
        [("POST", "/audio")] = new RouteDoc(
            Tool:    "set_audio",
            Group:   "Y — node diagnostics",
            Summary: "⭐ CE-4038 B11 (R-4059) — set the Sound switch, the master volume and mute, as the menu does (saved to the user's file).",
            Returns: "as get_audio",
            Hint:    "Optional: sound (bool), master (0…1), muted (bool). Example: set_audio({sound:true})",
            Params: new RouteParam[]
            {
                new("sound", "boolean", false, "Settings ▸ Sound: the scene heard from the camera outside FPS"),
                new("master", "number", false, "Master volume 0…1"),
                new("muted", "boolean", false, "Mute"),
            },
            ExampleArgsJson: "{\"sound\":true}",
            ExampleGist: "turn the sound on"),
        [("GET", "/diagnostics/architecture")] = new RouteDoc(
            Tool:    "get_architecture_diagnostics",
            Group:   "Y — node diagnostics",
            Summary: "This NODE's modules, ECS systems and DDS translators, one entry per subsystem, read from each subsystem's own ModuleHostKernel.",
            Returns: "{ subsystems[{ subsystem, perspective, modules[], systems[{ phase, module, systemName, executions, avgMs, recentAvgMs, maxMs, errors }], translators[], moduleCount, systemCount, translatorCount }], note }",
            Hint:    "Optional: subsystem (filter). Example: get_architecture_diagnostics({subsystem:\"SimHost\"})",
            Params: new RouteParam[]
            {
                new("subsystem", "string", false, "Restrict to one subsystem or perspective name (e.g. SimHost, IG, Scenario)"),
            },
            Notes: new[]
            {
                "Per SUBSYSTEM, not per node: a --mode all node runs SimHost, IG, CGF and the orchestrator side by side and each holds its own kernel, so one snapshot per node would have to drop the rest.",
                "Every node hosts its own MCP endpoint. This answers for THIS node only — ask each node's own endpoint for its own architecture.",
                "A subsystem with no ECS kernel (ExCon, an orchestrator-only node) correctly reports nothing; check the 'diagnostics.architecture' cell in get_capabilities rather than reading the absence as a wiring bug.",
                "modules carry lifecycleState and circuitState, so a module stuck open or failing shows up here without reading logs.",
                "It allocates the whole snapshot on every call — fine for an operator query, wrong in a loop.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see what modules and translators this node is running"),

        [("GET", "/assets/recipes")] = new RouteDoc(
            Tool:    "list_asset_recipes",
            Group:   "W — AI-asset authoring",
            Summary: "List the recipes and blank templates create_asset can build from, per asset kind.",
            Returns: "{ kinds[], recipes[{ id, kind, name, description, category, isBlankTemplate, sourceFilePath }], note }",
            Hint:    "Optional: kind (filter). Example: list_asset_recipes({kind:\"Blueprint\"})",
            Params: new RouteParam[]
            {
                new("kind", "string", false, "Restrict to one AssetKind (BTree | Hsm | Blueprint)"),
            },
            Notes: new[]
            {
                "`name` is exactly what create_asset takes as its `recipe` argument.",
                "`isBlankTemplate` separates a synthetic empty starting point (Empty, Starter) from a CONTENT recipe cloned from a real asset — the two are not interchangeable and the name alone does not tell you which it is.",
                "The list is read live from each kind's INewAssetService, so recipes added to disk appear without restarting the host.",
                "`description` is null for recipes that carry no RecipeMetadata — the synthetic Empty/Starter entries do not.",
                "A host that composes no per-kind new-asset registry answers 503; the same registry backs create_asset, so if this is 503 then create is too.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see what kinds of asset this host can create"),

        [("DELETE", "/entities/{networkId}")] = new RouteDoc(
            Tool:    "delete_entity",
            Group:   "W — AI-asset authoring",
            Summary: "Remove an entity from the world through the ELM lifecycle. Scenario authoring is world manipulation, and this is its delete.",
            Returns: "{ networkId, queued:true, note }",
            Hint:    "Req: networkId (from list_entities). Example: delete_entity({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity to destroy"),
            },
            Notes: new[]
            {
                "There is no such thing as editing a scenario FILE: the file is a reduced snapshot of the world at save time, so authoring a scenario means spawning, configuring and deleting entities, then calling save_scenario.",
                "Queued like spawn_entity — teardown runs on a later tick. Call step, then list_entities, before asserting the entity is gone.",
                "An unknown networkId is a 404 rather than a queued no-op.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "delete an entity from the world"),

        // ── Group X — the UNION backbone, discovery and the editor command bus ──────────────
        //
        // ⭐⭐⭐ 📄 DESIGN_Mcp_Authoring.md §10 · §10.7 · §11. These complete the authoring surface: the
        //    whole GraphCommand union, the schema/doc discovery that makes it usable, and the editor
        //    command bus as a THIRD invoke surface through the same discover→invoke shape.

        [("GET", "/assets/{assetId}/graph/command")] = new RouteDoc(
            Tool:    "list_graph_command_types",
            Group:   "X — Graph command union & discovery",
            Summary: "Every GraphCommand variant apply_graph_command accepts, with the fields each one takes.",
            Returns: "{ count, variants[{type,fields[]}], unsupported[{type,reason}], note }",
            Hint:    "Req: assetId (GUID of an OPEN document). Example: list_graph_command_types({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
            },
            Notes: new[]
            {
                "Call this before apply_graph_command instead of guessing a payload shape — the variant names match the nested record names in NodeEditor.Core.Commands.GraphCommand exactly.",
                "A field suffixed '?' is optional. Ids are GUID strings from read_asset_graph.",
                "'Batch' takes {commands:[...]} and applies them as ONE undo entry, with the inverses reversed so nodes are restored before the links that reference them.",
                "The 'unsupported' list is normally empty; an entry there is a deliberate decision with its reason, not an oversight.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "discover every graph-edit command and its fields"),

        [("POST", "/assets/{assetId}/graph/command")] = new RouteDoc(
            Tool:    "apply_graph_command",
            Group:   "X — Graph command union & discovery",
            Summary: "Apply ONE GraphCommand to an open graph — the whole ~35-variant union, including BTree decorators (attachments) and HSM parallel regions the typed verbs cannot express.",
            Returns: "{ type, applied, undoable, message, newIds{}, nodeCount, linkCount, nodeDelta, linkDelta, note }",
            Hint:    "Req: assetId, type (from list_graph_command_types) + that variant's fields. Example: apply_graph_command({assetId:\"...\",type:\"AddAttachment\",host:\"<nodeGuid>\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("type", "string", true, "The GraphCommand variant name, e.g. AddNode / AddAttachment / AddRegion / Batch"),
                new("commands", "array", false, "For type:\"Batch\" — the nested commands, applied as one atomic undo entry"),
            },
            Notes: new[]
            {
                "THE PARITY GUARANTEE: the command goes through GraphView.Execute — the same undo stack and the same host sink a canvas gesture uses. There is no MCP-only mutation path, on any of the three hosts, with zero per-host code.",
                "The typed verbs (add_graph_node, add_graph_link, set_graph_param, remove_graph_elements) are sugar over this same union — they are not a parallel model.",
                "`newIds` carries any id the command MINTED (nodeId / linkId / attachmentId / commentId), so you can address what you just created.",
                "`undoable:false` means no inverse could be derived from the read-only model (the refactor ops, SetNodeProperty, RemoveRegion). The edit still applied; the undo stack simply has no entry. A wrong inverse would corrupt the graph silently, so none is recorded.",
                "A refusal is the HOST's own answer (an invalid wire, an unknown kind) and comes back 400 with its reason — it is a legitimate outcome of editing, not a server error.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"type\":\"AddNode\",\"kind\":\"bt.selector\",\"position\":{\"x\":80,\"y\":40}}",
            ExampleGist: "add a node through the union route and get its new guid"),

        [("GET", "/assets/{assetId}/graph/catalog/{kind}")] = new RouteDoc(
            Tool:    "get_node_kind_schema",
            Group:   "X — Graph command union & discovery",
            Summary: "One node kind's full schema and documentation: pins, flags, palette behaviour, and the reflected DTO params when the kind resolves to an action.",
            Returns: "{ kind, displayName, category, doc, isPure, isLatent, isDeprecated, paletteAction, isAttachmentKind?, attachmentCategory?, keywords[], inputs[], outputs[], paramsSource, params[], note }",
            Hint:    "Req: assetId, kind (verbatim from list_node_kinds). Example: get_node_kind_schema({assetId:\"...\",kind:\"bt.selector\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("kind", "string", true, "Node kind id, verbatim from list_node_kinds"),
            },
            Notes: new[]
            {
                "MEASURED from the host's own INodeCatalog and action-schema exporter — never a hand-authored kind table, which would rot the moment a node kind is added and nothing would fail.",
                "`paramsSource` says where params came from: exporter:exact, exporter:suffix (a probable match, not a certain one), none:not-an-action, none:dto-fields-not-reflected, or none:no-exporter-wired. An empty list WITHOUT that field would read as 'this kind has no params', which is a different and often false claim.",
                "The catalog cannot say whether a kind is a CONTAINER — container-ness belongs to an instantiated node. Read container/region structure per node from read_asset_graph.",
                "`paletteAction` is the kind-level structure fact the catalog does have: CreateNode makes a node, AttachToSelected makes an ATTACHMENT on the selected node.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"kind\":\"bt.selector\"}",
            ExampleGist: "read one node kind's pins, params and docs"),

        [("GET", "/assets/{assetId}/graph/nodes/{nodeId}/properties")] = new RouteDoc(
            Tool:    "get_node_properties",
            Group:   "X — Graph command union & discovery",
            Summary: "One node's editable properties with their CURRENT values — what the Details panel shows.",
            Returns: "{ assetId, nodeId, kind, title, doc, count, properties[{pinId,name,type,value,hasValue,doc?,rangeMin?,rangeMax?,unit?,picker?}], note }",
            Hint:    "Req: assetId, nodeId (GUID from read_asset_graph). Example: get_node_properties({assetId:\"...\",nodeId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document"),
                new("nodeId", "string", true, "Node GUID from read_asset_graph or add_graph_node"),
            },
            Notes: new[]
            {
                "Values come from the MODEL and schema from the CATALOG, joined here — so a value is never reported without the type and constraints needed to change it correctly.",
                "Only INPUT DATA pins appear: an exec pin has no value and an output's is computed, so listing them would invite a set that must be refused.",
                "Set one with set_graph_param, or apply_graph_command type:\"SetPinDefault\".",
                "Range / unit / picker metadata is the same the Details editor itself reads — it is carried on the pin's default descriptor, not re-derived here.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\",\"nodeId\":\"11111111-1111-1111-1111-111111111111\"}",
            ExampleGist: "read a node's current property values and their schema"),

        [("GET", "/editor/commands")] = new RouteDoc(
            Tool:    "list_editor_commands",
            Group:   "X — Graph command union & discovery",
            Summary: "The EDITOR command bus — every toolbar/menu/hotkey command with its live enabled and checked state.",
            Returns: "{ count, total, commands[{id,displayName,category,doc,defaultKey?,isEnabled,isChecked?}], note }",
            Hint:    "Optional: category. Example: list_editor_commands({})",
            Params: new RouteParam[]
            {
                new("category", "string", false, "Filter to one category, e.g. \"Edit\""),
            },
            Notes: new[]
            {
                "This is NOT list_commands — that one enumerates publishable FDP EVENT types for send_entity_command. These are the editor's own commands, invoked with invoke_editor_command.",
                "isEnabled/isChecked are evaluated NOW over live editor state (is there a selection? is the undo stack empty?), so they are a snapshot.",
                "The command set is per OPEN DOCUMENT — it is built by the per-kind document factory, so opening a different asset kind changes it. Open an AI asset first.",
                "The descriptors are self-documenting: DisplayName, Category, Description and DefaultKey are carried inline, so no attribute harvest is needed here.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "list the editor commands and which are currently enabled"),

        [("GET", "/editor/commands/{commandId}")] = new RouteDoc(
            Tool:    "get_editor_command",
            Group:   "X — Graph command union & discovery",
            Summary: "Describe one editor command.",
            Returns: "{ id, displayName, category, doc, defaultKey?, isEnabled, isChecked? }",
            Hint:    "Req: commandId (from list_editor_commands). Example: get_editor_command({commandId:\"editor.delete-selection\"})",
            Params: new RouteParam[]
            {
                new("commandId", "string", true, "Command id, e.g. \"editor.delete-selection\""),
            },
            Notes: new[]
            {
                "Ids look like 'editor.delete-selection'. The available set depends on which document kind is open.",
                "A 404 here means the id is not registered for the currently open document — not that it never exists.",
            },
            ExampleArgsJson: "{\"commandId\":\"editor.delete-selection\"}",
            ExampleGist: "describe one editor command before invoking it"),

        [("POST", "/editor/commands/{commandId}/invoke")] = new RouteDoc(
            Tool:    "invoke_editor_command",
            Group:   "X — Graph command union & discovery",
            Summary: "Run an editor command through the same seam the toolbar, menu and hotkey use.",
            Returns: "{ commandId, displayName, invoked, success, message, note }",
            Hint:    "Req: commandId. Optional: args (object), canvasPos {x,y}. Example: invoke_editor_command({commandId:\"editor.select-all\"})",
            Params: new RouteParam[]
            {
                new("commandId", "string", true, "Command id from list_editor_commands"),
                new("args", "object", false, "Parameters, delivered as EditorCommandContext.Args"),
                new("canvasPos", "object", false, "Canvas position {x,y} for context-menu-style commands"),
            },
            Notes: new[]
            {
                "A DISABLED command is refused with 409 BEFORE it is invoked. The editor greys it out for the same reason — usually an empty selection or an empty undo stack — and running it anyway would be the one path that accepts what the editor refuses.",
                "Read list_editor_commands for the live enabled state, and set up the precondition first (e.g. select something).",
                "A headless origin never pre-flights a confirmation (ruling 53): the command runs directly and the origin-side LOG is the safety net. The host logs every invocation.",
                "Effects that redraw appear on the NEXT frame — step a tick before reading get_panels.",
            },
            ExampleArgsJson: "{\"commandId\":\"editor.select-all\"}",
            ExampleGist: "run an editor command headlessly"),

        [("GET", "/documents")] = new RouteDoc(
            Tool:    "list_documents",
            Group:   "V — AI assets & graph tabs",
            Summary: "The open graph tabs and which one is active.",
            Returns: "{ activeAssetId, count, documents[{assetId,name,kind,sourceFilePath,isDirty,isActive}] }",
            Hint:    "No params. Example: list_documents({})",
            Notes: new[]
            {
                "Only the ACTIVE document's canvas draws, so this is how you confirm which graph get_panels is about to show you.",
                "This is the editor's own tab model, exposed — not a second list.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see which graphs are open and which one is on screen"),

        [("POST", "/documents/{assetId}/activate")] = new RouteDoc(
            Tool:    "activate_document",
            Group:   "V — AI assets & graph tabs",
            Summary: "Switch the active graph tab to an already-open document.",
            Returns: "{ activeAssetId, note }",
            Hint:    "Req: assetId (GUID, from list_documents). Example: activate_document({assetId:\"...\"})",
            Params: new RouteParam[]
            {
                new("assetId", "string", true, "GUID of an OPEN document — from list_documents"),
            },
            Notes: new[]
            {
                "Activate only switches between tabs that are ALREADY open; a closed asset is a 404, not an implicit open. Use open_asset for that.",
                "Details and the toolbar re-publish for the newly active kind on the NEXT frame.",
            },
            ExampleArgsJson: "{\"assetId\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "bring an already-open graph to the front"),

        [("POST", "/panels/{panelId}/focus")] = new RouteDoc(
            Tool:    "focus_panel",
            Group:   "V — AI assets & graph tabs",
            Summary: "Open and focus a window by its panel id.",
            Returns: "{ panelId, perspective, isOpen, isPinned, note }",
            Hint:    "Req: panelId (string, from get_panels). Example: focus_panel({panelId:\"ai_watch_blueprint\"})",
            Params: new RouteParam[]
            {
                new("panelId", "string", true, "Registered window id — the PANEL id from get_panels, not the panel KIND"),
            },
            Notes: new[]
            {
                "An unknown id is a 404 here, deliberately — the underlying UI call is a silent no-op, which over HTTP would hand you a 200 and then the wrong panel.",
                "A perspective-bound window belonging to another perspective is PINNED rather than switched to; the response says which happened.",
                "Focus takes effect on the NEXT frame.",
            },
            ExampleArgsJson: "{\"panelId\":\"ai_watch_blueprint\"}",
            ExampleGist: "bring a specific panel on screen before reading it"),

        [("GET", "/perspectives")] = new RouteDoc(
            Tool:    "list_perspectives",
            Group:   "A — Lifecycle & status",
            Summary: "Every perspective a registered window claims, plus the active one.",
            Returns: "{ current, perspectives[] }",
            Hint:    "No params. Example: list_perspectives({})",
            Notes: new[]
            {
                "A perspective exists because a window CLAIMS it — this list is derived, not configured.",
                "current is reported alongside the list because it is the only honest answer to \"did my switch take?\".",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see which perspectives this host can route to"),

        [("POST", "/perspective")] = new RouteDoc(
            Tool:    "switch_perspective",
            Group:   "A — Lifecycle & status",
            Summary: "Switch the active perspective, then report what actually happened.",
            Returns: "{ current, note }",
            Hint:    "Req: name (string, from list_perspectives). Example: switch_perspective({name:\"SimHost\"})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Perspective to activate — must be one list_perspectives returns"),
            },
            Notes: new[]
            {
                "ALWAYS read `current` back — an unknown name is a no-op, so trusting the 200 would leave you reading the WRONG perspective's panels.",
                "A 400 names the claimed set; a 503 means perspective access is not wired on this host.",
                "A 409 means the Start perspective is showing (the cluster rests in Idle, nothing loaded): a switch is refused whatever the name. LOAD FIRST (load_scenario_live / load_scenario_edit), then switch — leaving Idle restores the host's remembered perspective on its own. Read get_status.routedTo to see which perspective the reads are answered from meanwhile.",
                "The new perspective publishes its panels on the NEXT frame — step a tick before get_panels, or you read the previous one.",
                "In a cluster host (mode \"all\") this is how you choose which node subsequent commands act on.",
            },
            ExampleArgsJson: "{\"name\":\"SimHost\"}",
            ExampleGist: "act in the SimHost node's context"),

        [("GET", "/entities")] = new RouteDoc(
            Tool:    "list_entities",
            Group:   "B — Queries",
            Summary: "List all entities with networkId, name, and component names.",
            Returns: "[{networkId, name, components:[names]}]",
            Hint:    "No required params. Optional: component (string), near (\"x,y,r\"). Example: list_entities({component:\"SimTransform\"})",
            Params: new RouteParam[]
            {
                new("component", "string", false, "Filter: only entities that have this component type"),
                new("near", "string", false, "Spatial filter: \"x,y,r\" (comma-separated floats)"),
            },
            Notes: new[]
            {
                "Optional filters compose: component (only entities having it), near (\"x,y,r\" within radius r of (x,y)).",
            },
            ExampleArgsJson: "{\"component\":\"SimTransform\"}",
            ExampleGist: "list only entities with SimTransform component"),

        [("GET", "/entities/{networkId}")] = new RouteDoc(
            Tool:    "get_entity",
            Group:   "B — Queries",
            Summary: "Full component dump for one entity.",
            Returns: "Full component dump for the entity. Non-finite floats render as \"NaN\"/\"Infinity\"/\"-Infinity\".",
            Hint:    "Req: networkId (number/long). Example: get_entity({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "Non-finite floats appear as string sentinels \"NaN\"/\"Infinity\"/\"-Infinity\" — valid JSON, not a bug.",
                "SHAPE: { EntityId, NetworkId, Components:{ ... } } — Components is PascalCase, and so is every "
              + "component and field name inside it (SimTransform.Position, NavigationIntent.Mode). Indexing a "
              + "lowercase 'components' silently yields nothing and reads like an empty entity.",
                "TO DIAGNOSE 'the sim ignores my order', COMPARE THE INTENT COMPONENT WITH ITS STATUS "
              + "COMPONENT. The pair distinguishes three different bugs that look identical from the UI: intent "
              + "empty => nothing issued the order; intent set + status ABSENT => the consumer never ran; "
              + "intent set + status PRESENT + zero velocity => the consumer ran and produced no motion. "
              + "Worked example: NavigationIntent{Mode,TargetSpeed} against NavigationStatus.",
                "AUTHORITY FIRST: NetworkAuthority carry HasAuthority, PrimaryOwnerId and "
              + "LocalNodeId. On a cluster a write to an entity this node does not own is legitimately dropped, "
              + "so check HasAuthority before filing 'the write did nothing'.",
                "MEASURE MOTION AS A POSITION DELTA OVER A simTime DELTA, never over wall-clock. Sample "
              + "get_status.simTime alongside each dump: BIT-IDENTICAL positions across a real simTime advance "
              + "is the hard evidence; the same reading across a stalled clock proves nothing.",
                "ON --mode all THIS READS ONE NODE -- THE ACTIVE PERSPECTIVE -- AND THE NODES DISAGREE. "
              + "Brain (CGF/Scenario) and muscle (SimHost) hold separate copies of the same entity, and a "
              + "defect can live entirely in the gap between them. Measured 2026-08-28: entity 1001 held "
              + "Class:Tank, AccelGain:1.8 on CGF and PersonalCar, AccelGain:0 on SimHost, because the "
              + "scenario's authored VehicleParams reached the brain intact and was dropped on the wire hop. "
              + "The brain computed a valid path (which rendered) while the muscle could not accelerate -- on "
              + "screen, indistinguishable from a broken navigator. SO: read the entity on BOTH nodes before "
              + "concluding anything about 'the cluster'.",
                "AND ?perspective= DOES NOT WORK: it is not implemented on any route and is IGNORED (you get "
              + "a hint saying so since CE-112). Switch with POST /perspective {name:...} and then read; "
              + "confirm with get_status.perspective. Passing ?perspective=ExCon -- a subsystem with NO WORLD "
              + "AT ALL -- used to return a full component dump, which is how the ignored key was caught.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "get full component dump for entity 1000"),

        [("GET", "/components")] = new RouteDoc(
            Tool:    "list_component_types",
            Group:   "B — Queries",
            Summary: "Enumerate registered ECS component types with field schemas.",
            Returns: "All registered component types + field schemas (for use with edit_component).",
            Hint:    "No params. Example: list_component_types({})",
            Notes: new[]
            {
                "Use this to discover component type names before calling edit_component.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "list all ECS component types and their schemas"),

        [("GET", "/scenarios")] = new RouteDoc(
            Tool:    "list_scenarios",
            Group:   "B — Queries",
            Summary: "List available scenarios by relative path.",
            Returns: "Available scenario names (relative paths) for use with load_scenario_edit / load_scenario_live.",
            Hint:    "No params. Example: list_scenarios({})",
            ExampleArgsJson: "{}",
            ExampleGist: "discover loadable scenario names"),

        [("GET", "/events")] = new RouteDoc(
            Tool:    "get_event_history",
            Group:   "C — Event history",
            Summary: "Query the diagnostic event history.",
            Returns: "Recent diagnostic events from the specified bus.",
            Hint:    "No required params. Optional: bus (\"world\"|\"orchestration\"), type (string), since (frame), max (number). Example: get_event_history({bus:\"world\",max:50})",
            Params: new RouteParam[]
            {
                new("bus", "string", false, "Event bus to query",
                    DefaultJson: "\"world\"",
                    EnumJson: "[\"world\",\"orchestration\"]"),
                new("type", "string", false, "Filter by event type name"),
                new("since", "number", false, "Return events since this frame number"),
                new("max", "number", false, "Maximum events to return (default 200)",
                    DefaultJson: "200"),
            },
            Notes: new[]
            {
                "bus: \"world\" (default) or \"orchestration\".",
                "Read-only; safe to call any time.",
            },
            ExampleArgsJson: "{\"bus\":\"world\",\"type\":\"CenterOnEntityCommand\",\"max\":10}",
            ExampleGist: "query world bus for recent CenterOnEntityCommand events"),

        [("GET", "/sim/state")] = new RouteDoc(
            Tool:    "get_sim_state",
            Group:   "D — Sim / preview / time",
            Summary: "Current sim state: isPaused, inPreview, totalTime, timeScale.",
            Returns: "{ isPaused, inPreview, totalTime, timeScale }",
            Hint:    "No params. Example: get_sim_state({})",
            Notes: new[]
            {
                "Check this before driving — most mistakes are run-state mistakes.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "check current paused/preview/time state"),

        [("POST", "/sim/play")] = new RouteDoc(
            Tool:    "play",
            Group:   "D — Sim / preview / time",
            Summary: "Enter preview and/or resume if paused. Time advances after this.",
            Returns: "ok:true envelope.",
            Hint:    "No params. Example: play({})",
            Notes: new[]
            {
                "Time advances after play (until pause or a breakpoint fires).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "start or resume simulation"),

        [("POST", "/sim/pause")] = new RouteDoc(
            Tool:    "pause",
            Group:   "D — Sim / preview / time",
            Summary: "Pause the simulation. Time freezes; commands queue until step/play.",
            Returns: "ok:true envelope.",
            Hint:    "No params. Example: pause({})",
            Notes: new[]
            {
                "Commands and spawns while paused are queued and take effect on the next step/play.",
                "THE ACK MEANS APPLIED as of CE-104: the route waits until isPaused is actually true before "
              + "returning, so the next read is safe. Measured before the fix on --mode all: ok:true while "
              + "/status still read isPaused:false, with the clock running a further ~0.2s — because a "
              + "cluster-wide pause is a FUTURE BARRIER so every node stops at the same simTime.",
                "A 504 from this route means the pause barrier is stuck — a node never reached it. Check "
              + "get_logs({level:\"Warning\"}).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "pause the running simulation"),

        [("POST", "/sim/step")] = new RouteDoc(
            Tool:    "step",
            Group:   "D — Sim / preview / time",
            Summary: "Advance simulation by N discrete steps. Only meaningful in preview.",
            Returns: "ok:true envelope.",
            Hint:    "No required params. Optional: count (number, def 1), dt (seconds per step, def 1/60). Example: step({count:5})",
            Params: new RouteParam[]
            {
                new("count", "number", false, "Number of steps to advance (default 1)",
                    DefaultJson: "1"),
                new("dt", "number", false, "Seconds per step, in (0, 0.1] (default 1/60). The film director's coarser "
                    + "rates: 0.05 (20 Hz) or 0.0667 (15 Hz), CE-4038 / DESIGN_Film_Director.md D-1"),
            },
            Notes: new[]
            {
                "Only advances time when inPreview==true. In Edit state this is a no-op.",
                "count IS honoured cluster-wide as of CE-105, and it is gated per step: the route issues ONE "
              + "step, waits for it to land, and repeats — so N steps take N frames and the call returns when "
              + "the last one is acknowledged. Measured: count:60 advances simTime by exactly 1.0000s. Before "
              + "the fix the loop ran inside a single frame and the cluster dropped all but the first, "
              + "answering ok:true with 0.0167s of progress.",
                "STILL VERIFY BY READING simTime, NOT ok. The gate now makes the ack trustworthy, but a "
              + "simTime delta is the cheap proof and it costs one call.",
                "A STEP IS REFUSED OUTRIGHT UNLESS THE CLUSTER IS IN A STEPPING MODE, and the refusal is not "
              + "visible in the envelope — it is a warning in the log (see get_logs, RefusedStepCount). A "
              + "cluster that is free-running drops every step. If simTime does not move, check "
              + "get_status.isPaused first: pause, then step.",
                "This is the loop's weak link, so prove it once at the start of a session: pause, step, read "
              + "simTime. A silently-refused step makes every later observation meaningless (it invalidated a "
              + "root-cause in this repo once — a 'nothing moved' reading taken through a step that never ran).",
            },
            ExampleArgsJson: "{\"count\":5}",
            ExampleGist: "advance 5 simulation ticks"),

        [("POST", "/sim/timescale")] = new RouteDoc(
            Tool:    "set_time_scale",
            Group:   "D — Sim / preview / time",
            Summary: "Set simulation time scale.",
            Returns: "ok:true envelope.",
            Hint:    "Req: scale (number, 1.0=real-time). Example: set_time_scale({scale:2.0})",
            Params: new RouteParam[]
            {
                new("scale", "number", true, "Time scale factor (1.0 = real-time)"),
            },
            Notes: new[]
            {
                "1.0 = real-time, >1.0 = faster, <1.0 = slower.",
            },
            ExampleArgsJson: "{\"scale\":2}",
            ExampleGist: "run simulation at 2x real-time"),

        [("POST", "/preview/enter")] = new RouteDoc(
            Tool:    "enter_preview",
            Group:   "D — Sim / preview / time",
            Summary: "Enter the cluster's dry run (OperatingPreview) from Edit, through the cluster master on every host (R-3052). Never recorded (R-3053).",
            Returns: "{ published, transactionId, target } (+ awaited, reached with waitForReady).",
            Hint:    "No required params. Optional: startPaused (bool), waitForReady (bool). Example: enter_preview({startPaused:true, waitForReady:true})",
            Params: new RouteParam[]
            {
                new("startPaused", "boolean", false, "Start the dry run paused"),
                new("waitForReady", "boolean", false, "Wait until the cluster reports OperatingPreview"),
            },
            Notes: new[]
            {
                "stop_preview rewinds every node to the moment the dry run began.",
                "A dry run is never recorded: only a live run records (R-3053).",
            },
            ExampleArgsJson: "{\"startPaused\":true,\"waitForReady\":true}",
            ExampleGist: "enter preview paused for deterministic step-based control"),

        [("POST", "/preview/exit")] = new RouteDoc(
            Tool:    "stop_preview",
            Group:   "D — Sim / preview / time",
            Summary: "Leave the dry run back to Edit through the cluster master; every node rewinds to the moment it began.",
            Returns: "{ published, transactionId, target } (+ awaited, reached with waitForReady).",
            Hint:    "No required params. Optional: waitForReady (bool). Example: stop_preview({waitForReady:true})",
            Params: new RouteParam[] { new("waitForReady", "boolean", false, "Wait until the cluster reports OperatingEdit") },
            Notes: new[]
            {
                "Rewinds all changes made during the dry run.",
            },
            ExampleArgsJson: "{\"waitForReady\":true}",
            ExampleGist: "exit preview and revert all changes since entering preview"),

        [("POST", "/scenario/load/edit")] = new RouteDoc(
            Tool:    "load_scenario_edit",
            Group:   "E — Scenario",
            Summary: "Load a scenario for AUTHORING (Edit state), cluster-wide.",
            Returns: "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor, stoppedRun (CE-1025: \"a live run\" / \"a dry run\" / null).",
            Hint:    "Req: name (string). Optional: waitForReady (bool, use true), discardUnsaved (bool). Answers 409 \"the scenario has unsaved changes\" when ANOTHER scenario is loaded over unsaved edits, unless discardUnsaved:true. Example: load_scenario_edit({name:\"test-move\",waitForReady:true})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Scenario name (relative path)"),
                new("waitForReady", "boolean", false, "Wait for the cluster to reach OperatingEdit before returning",
                    DefaultJson: "false"),
                new("discardUnsaved", "boolean", false, "Load even though the open scenario has unsaved changes (they are lost). Without it a load of ANOTHER scenario over unsaved edits answers 409.",
                    DefaultJson: "false"),
            },
            Notes: new[]
            {
                "⭐ CE-4093 (AQ108 D22) — a load of ANOTHER scenario while one is open for editing first asks the cluster to compare every node's whole world with its saved baseline (the check_unsaved round) and answers 409 \"the scenario has unsaved changes\" when it differs; save_scenario first, or send discardUnsaved:true. Reloading the scenario already open is not gated.",
                "⭐ CE-1025 — a running live run or dry run is stopped first, with no prompt (HTTP is non-interactive): the dry run's restore point is FORGOTTEN, not rewound to; the answer's stoppedRun names the run (\"a live run\" / \"a dry run\") or is null.",
                "Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).",
                "Edit state freezes sim time — nothing ticks until enter_preview or play.",
                "CE-102 (2026-08-28) gave CGF the shared edit-load handler, so an edit load is NO LONGER partial on that node. This note previously said CGF had none -- that is now stale. Still prefer load_scenario_live for a real run, and verify either load by reading state.",
                "AND THAT PARTIAL LOAD STILL ANSWERS ok:true — this is the single most misleading response in "
              + "the API. Measured on --mode all from the Scenario perspective: ok:true with scenario:NULL, "
              + "entityCount:0, an empty list_entities, and a gizmo frame of 603 primitives that were ALL grid "
              + "lines. Every field says 'empty world' and the envelope says 'success'.",
                "SO VERIFY A LOAD, NEVER TRUST IT: after any load read get_status.entityCount AND "
              + "list_entities AND (if the map matters) the non-Line shape count from get_gizmo_frame. Three "
              + "independent reads, because the envelope is not one of them. On the same host "
              + "load_scenario_live gave 8 entities and Box2D 8 / Arrow 12 / Text 8.",
            },
            ExampleArgsJson: "{\"name\":\"test-move\",\"waitForReady\":true}",
            ExampleGist: "load test-move for authoring and wait for ready"),

        [("POST", "/scenario/load/live")] = new RouteDoc(
            Tool:    "load_scenario_live",
            Group:   "E — Scenario",
            Summary: "Load a scenario for RUNNING (Live state), cluster-wide, on any host.",
            Returns: "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor, stoppedRun (CE-1025: \"a live run\" / \"a dry run\" / null).",
            Hint:    "Req: name (string). Optional: waitForReady (bool, use true), discardUnsaved (bool). Answers 409 \"the scenario has unsaved changes\" when the scenario open for editing differs from its saved state, unless discardUnsaved:true. Example: load_scenario_live({name:\"test-move\",waitForReady:true})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Scenario name (relative path)"),
                new("waitForReady", "boolean", false, "Wait for the cluster to reach OperatingLive before returning",
                    DefaultJson: "false"),
                new("discardUnsaved", "boolean", false, "Go live even though the scenario open for editing has unsaved changes (they are lost). Without it such a load answers 409.",
                    DefaultJson: "false"),
            },
            Notes: new[]
            {
                "⭐ CE-4093 (AQ108 D22) — while a scenario is open for EDITING the cluster first compares every node's whole world with its saved baseline (the check_unsaved round); when it differs this answers 409 \"the scenario has unsaved changes\" — save_scenario first, or send discardUnsaved:true. From Idle, a preview, a live run or a replay nothing is asked.",
                "⭐ CE-1025 — a running live run or dry run is stopped first, with no prompt (HTTP is non-interactive): the dry run's restore point is FORGOTTEN, not rewound to; the answer's stoppedRun names the run (\"a live run\" / \"a dry run\") or is null.",
                "Set waitForReady:true to block until the cluster reaches OperatingLive (recommended).",
                "Every host has live-load handlers, so this is the mode that loads on ALL nodes — use it when the world must be the same everywhere.",
                "A live load starts a new exercise run (a fresh ExerciseId), which is what recording and replay key off.",
            },
            ExampleArgsJson: "{\"name\":\"test-move\",\"waitForReady\":true}",
            ExampleGist: "load test-move live across the cluster and wait for ready"),

        [("POST", "/scenario/check-unsaved")] = new RouteDoc(
            Tool:    "check_unsaved",
            Group:   "E — Scenario",
            Summary: "Ask the cluster whether the scenario open for editing has unsaved changes (every node compares its whole world with its baseline).",
            Returns: "ok:true envelope with changed (bool) and changedNodes (node ids whose world differs from its baseline, or that have none).",
            Hint:    "No params. Answers 409 unless the cluster is open for editing (OperatingEdit). Example: check_unsaved({})",
            Notes: new[]
            {
                "⭐ CE-4093 (AQ108 D20) — a CHECK ROUND on demand, never periodic: each node captures its whole world as a binary keyframe, hashes it and compares it with the baseline taken when the load committed or its slice was last saved; anything that changed counts (not only the scenario's parts). It can only err towards \"changed\" (an identical unit deleted and re-added), never report unchanged for a changed world. ~60 ms per node on a large scenario.",
                "A save round that FAILED after the last load (the slice pull or the merge) makes the answer changed too — the files on disk are stale whatever the worlds say.",
                "Only an explicit \"unchanged\" from a node means saved: a node with no baseline counts as changed (and is named in changedNodes). A host with no ECS world does not take part.",
                "get_status.scenarioDirty reports the LAST round's verdict without running one; load_scenario_live (and load_scenario_edit of another scenario) run this round themselves and answer 409 when it says changed.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "ask whether the open scenario has unsaved changes"),

        [("POST", "/scenario/save")] = new RouteDoc(
            Tool:    "save_scenario",
            Group:   "E — Scenario",
            Summary: "Save the current authored world as a scenario.",
            Returns: "ok:true envelope.",
            Hint:    "Req: name (string). Answers 409 in a live run and in a review / replay (AAR): that world is not the scenario — take a checkpoint, then make a scenario from it. Example: save_scenario({name:\"my-scenario\"})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Scenario file name to save as"),
            },
            ExampleArgsJson: "{\"name\":\"my-scenario\"}",
            ExampleGist: "save current world as my-scenario"),

        [("GET", "/commands")] = new RouteDoc(
            Tool:    "list_commands",
            Group:   "F — Commands, discovery, spawn",
            Summary: "Enumerate publishable FDP event types with field schemas.",
            Returns: "Publishable FDP event types + field schemas; each tagged managed:true/false.",
            Hint:    "No params. Example: list_commands({})",
            Notes: new[]
            {
                "Call this to discover what send_entity_command accepts.",
                "managed:true events have server-side handling; managed:false are raw FDP events.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "discover available FDP event types before sending a command"),

        [("POST", "/entities/command")] = new RouteDoc(
            Tool:    "send_entity_command",
            Group:   "F — Commands, discovery, spawn",
            Summary: "Publish an FDP event by type name.",
            Returns: "ok:true envelope. awaited:false if sim not running (not an error).",
            Hint:    "Req: eventType (string from list_commands). Optional: payload (object — the event's field names, any case; an entity field takes the unit's network id), wait (bool). Example: send_entity_command({eventType:\"CenterOnEntityCommand\",payload:{networkId:1000}})",
            Params: new RouteParam[]
            {
                new("eventType", "string", true, "FDP event type name (e.g. MissionControlIntent)"),
                new("payload", "object", false, "Event fields as JSON object"),
                new("wait", "boolean", false, "Attempt to wait for correlated ack"),
            },
            Notes: new[]
            {
                "Set wait:true to attempt correlated-ack wait — only effective while time advances, else awaited:false.",
                "awaited:false is NOT an error — it means time was not advancing.",
                "CE-3168: a payload key that names no field of the event is a 400 (it used to be dropped silently, and the event published with defaults).",
                "To ORDER a unit use order_entity — it addresses units by network id and reaches the unit's brain node from any node.",
            },
            ExampleArgsJson: "{\"eventType\":\"CenterOnEntityCommand\",\"payload\":{\"networkId\":1000},\"wait\":false}",
            ExampleGist: "publish CenterOnEntityCommand event"),

        [("POST", "/entities/spawn")] = new RouteDoc(
            Tool:    "spawn_entity",
            Group:   "F — Commands, discovery, spawn",
            Summary: "Spawn an entity from a TKB type.",
            Returns: "{ spawned, tkbType, reliable, requestId, ownerNodeId, awaited, reason }. Created through the node's creation pack (request → spawn + ownership grants) on the next ticks — step to realize it.",
            Hint:    "Req: tkbType (number/long from list_entity_types). Optional: transform ({position,rotation}), components (array), attributesJson (string). Example: spawn_entity({tkbType:1001})",
            Params: new RouteParam[]
            {
                new("tkbType", "number", true, "TKB type ID (long)"),
                new("transform", "object", false, "Transform: { position: {x,y,z}, rotation: {x,y,z,w} }"),
                new("components", "array", false, "Additional component overrides"),
                new("attributesJson", "string", false, "JSON string of attribute overrides (JsonAttributeCompiler patch)"),
                new("ownerNodeId", "number", false, "0 (default) or this node's id ⇒ this node creates and owns it (and grants role groups); another node's id ⇒ routed to that node, which creates it"),
                new("reliable", "boolean", false, "CE-292: true ⇒ engage the cross-node construction barrier (InitType=AllPeers); the creator is this node unless ownerNodeId names another"),
                new("reliableTimeoutSeconds", "number", false, "CE-292: creator's reliable-init abort timeout in seconds (0 ⇒ gateway default)"),
            },
            Notes: new[]
            {
                "Spawn is queued and processed on the next tick — call step to realize it.",
                "CE-515 ③: goes through the node's EntityCreationPack (RequestEntityCreation), like every other author — the creator owns the entity and grants each role's group (Brain/MuscleGround/Perception) to a node serving that role. It no longer publishes a raw SpawnEntityCommand that nobody owned.",
                "Use list_entity_types to discover valid tkbType values.",
                "CE-292 — reliable:true engages the cross-node construction barrier: the creator holds the entity Constructing until the capability-filtered peers (advertising fdp.reliable-init) report Active, or reliableTimeoutSeconds aborts it via an EntityMaster dispose. On the wire this shows as an EntityMaster carrying the WaitForAcks flag + EntityLifecycleStatusDescriptor samples (sniff with ddsmonitor).",
            },
            ExampleArgsJson: "{\"tkbType\":1101,\"transform\":{\"position\":{\"x\":100,\"y\":0,\"z\":50},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}",
            ExampleGist: "spawn entity type 1101 at position (100,0,50)"),

        [("POST", "/breakpoints")] = new RouteDoc(
            Tool:    "set_breakpoint",
            Group:   "G — Breakpoints",
            Summary: "Register a run-until-condition breakpoint.",
            Returns: "{ breakpointId, conditionSummary } (e.g. \"BP#1\", \"Withdrawing entered (PlatoonCommander)\").",
            Hint:    "Req: condition (SearchPredicateDto with $type). Optional: filterNetworkId, occurrenceThreshold, name. Example: set_breakpoint({condition:{\"$type\":\"Lifecycle\",...}})",
            Params: new RouteParam[]
            {
                new("condition", "object", true, "SearchPredicateDto with $type discriminator (e.g. {\"$type\":\"Lifecycle\",\"IdentifierType\":\"NameSubstring\",\"TargetValue\":\"Alpha\",\"NamePropertyPath\":\"Name\"})"),
                new("filterNetworkId", "number", false, "Optional: only trigger for this entity (network ID)"),
                new("occurrenceThreshold", "number", false, "Number of hits before pausing (default 1)",
                    DefaultJson: "1"),
                new("name", "string", false, "Human-readable label for the breakpoint"),
            },
            Notes: new[]
            {
                "condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).",
                "Poll get_breakpoint_status after play to detect when the breakpoint fires.",
                "⭐ CE-1323 — BEHAVIOUR BREAKPOINTS: {\"$type\":\"BehaviourStep\",\"Behaviour\":\"<running behaviour name>\",\"Kind\":K,...} with K = StateEnter|StateExit {State} · InsideEnter {State, Region?: -1 = anything under it, n = region n of a parallel state} · TransitionFires {From?, To, Event?} (no From = a global transition) · EventHandled {Event} (dequeued, consumed or deferred, even when no transition uses it) · NodeRuns {Node: the BTree node's VisualId}. Names are the machine's own (GET /entities/{id}/brain names the running behaviour); a name not in it is a 400 that lists what is.",
                "Per unit: filterNetworkId. Any other condition: {\"$type\":\"Compound\",\"Operator\":\"And\",\"Conditions\":[<BehaviourStep>, <PropertyMatch …>]} — the rest is checked on the unit at the step. A step scoped to a unit arms its traces; an unscoped step sees only armed units (a Brain tab open, Debug this unit, observe-all).",
                "Only records written since the last look count, so continue_from_breakpoint never stops twice on one arrival. get_breakpoint_status names what fired (lastHit.what).",
            },
            ExampleArgsJson: "{\"condition\":{\"$type\":\"PropertyMatch\",\"ComponentType\":\"SimTransform\",\"PropertyPath\":\"Position.X\",\"Operator\":\"GreaterThan\",\"Predicate\":{\"$type\":\"Numeric\",\"MinValue\":100,\"MaxValue\":1000000000}},\"name\":\"moved-east\"}",
            ExampleGist: "pause when entity SimTransform.Position.X > 100"),

        [("GET", "/breakpoint-types")] = new RouteDoc(
            Tool:    "list_breakpoint_types",
            Group:   "S — Discovery with schema",
            Summary: "List every condition type a breakpoint can use, each with the JSON schema of its parameters. Call this BEFORE set_breakpoint instead of guessing a $type.",
            Returns: "[{ $type, clrType, paramSchema }]  — paramSchema is { type:\"object\", properties:{...} }",
            Hint:    "No params. Example: list_breakpoint_types({})",
            Notes: new[]
            {
                "The condition union is CLOSED: these are exactly the $type values set_breakpoint accepts.",
                "A nested predicate appears as { $ref: \"SearchPredicateDto\" } — fill it with another arm from this same list.",
                "Enum-valued params carry their allowed values in \"enum\"; a param marked picker:\"propertyPath\" wants a dotted field path such as \"Position.X\".",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "discover the valid condition $type values and their parameter shapes"),

        [("GET", "/behaviors")] = new RouteDoc(
            Tool:    "list_behaviors",
            Group:   "P — Discovery with schema",
            Summary: "List the behaviours available, each with the JSON schema of its parameter DTO. Key by tkbType (what this KIND of entity can do) or entityId (what THIS entity can do); omit both for every registered behaviour.",
            Returns: "[{ id, name, brainTier, paramSchema }]",
            Hint:    "Optional: tkbType or entityId. Example: list_behaviors({entityId:1000})",
            Params: new RouteParam[]
            {
                new("tkbType", "number", false, "TKB template id — returns the behaviours valid for that entity type (see list_tkb_types)"),
                new("entityId", "number", false, "Network id — returns exactly what the editor mission-task combo offers for that entity"),
            },
            Notes: new[]
            {
                "paramSchema is derived from the behaviour definition the runtime itself parses params with, so what you author matches what the engine reads.",
                "An unknown entityId is a 404 whose hint points at GET /entities — it is not answered with an empty list.",
                "A behaviour with no parameters returns an empty properties object, never null.",
            },
            ExampleArgsJson: "{\"entityId\":1000}",
            ExampleGist: "discover what entity 1000 can be told to do, and how to shape the params"),

        // ── Group P — mission editing (MX4b) ──────────────────────────────────
        [("GET", "/missions/{networkId}")] = new RouteDoc(
            Tool:    "get_mission",
            Group:   "P — Mission editing",
            Summary: "Read an entity's mission plan — its ordered tasks (behaviour, params, triggers, state) and the OCC version you pass back when editing. An entity with no mission returns an empty task list, not an error.",
            Returns: "{ networkId, plan: { activeTaskId, tasks: [{ taskId, behaviorId, behaviorParams, executingEngine, state, triggers: [{ type, params }] }] }, version }",
            Hint:    "Example: get_mission({networkId:1000}). Add a task with add_mission_task; discover behaviours with list_behaviors.",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the entity whose mission to read"),
            },
            Notes: new[]
            {
                "version is the optimistic-lock token — pass it straight back to add_mission_task / clear_mission_tasks so a concurrent edit is caught as a 409 rather than silently overwritten.",
                "The offline editor does not yet persist a snapshot version, so it reports 0 today; the edit path still round-trips it.",
                "An unknown networkId is a 404 whose hint points at GET /entities.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read entity 1000's current mission before editing it"),

        [("POST", "/missions/{networkId}/task")] = new RouteDoc(
            Tool:    "add_mission_task",
            Group:   "P — Mission editing",
            Summary: "Append one mission task to an entity — the PROPER way a behaviour attaches (as a task). Names the behaviour pass-through and carries its params as JSON matching the behaviour's paramSchema. Commits the whole plan through the editor's own mission path with optimistic concurrency.",
            Returns: "{ networkId, taskId, behavior, taskCount, committed:true, version }",
            Hint:    "Discover the behaviour and its param shape with list_behaviors({entityId}); a bad version is a 409, re-read get_mission and retry.",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the entity to add the task to"),
                new("behavior", "string", true, "The behaviour this task runs — one of the ids from list_behaviors for this entity"),
                new("params", "object", false, "The behaviour's parameters, as JSON matching its paramSchema from list_behaviors. Stored verbatim as the task's behaviorParams."),
                new("triggers", "array", false, "Optional transition triggers [{ type, params? }]. Defaults to a single BehaviorFinished trigger so the task can advance.",
                    ItemsJson: "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\"},\"params\":{}}}"),
            },
            Notes: new[]
            {
                "params is passed through verbatim — the engine reads it with plain JSON, the same string the editor's Mission panel stores. Shape it to the behaviour's paramSchema (list_behaviors), not to a separate mapper.",
                "CE-228: the example above uses MoveToLocation's REAL keys. It previously showed {Latitude, Longitude}, which bind to nothing — the params region would have stayed all-zero and the entity would have driven to the origin, with no error anywhere (R-132's failure shape, invited by this very doc).",
                "The commit is asynchronous: it resolves when the engine acknowledges. If the sim is not being pumped at all the call returns a 504 pointing at play/step.",
                "A stale version yields a 409 (ERR_VERSION_CONFLICT), never a silent overwrite.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"behavior\":\"MoveToLocation\",\"params\":{\"X\":500,\"Y\":520,\"Speed\":8,\"ArrivalRadius\":5}}",
            ExampleGist: "give entity 1000 a MoveToLocation task"),

        [("DELETE", "/missions/{networkId}/tasks")] = new RouteDoc(
            Tool:    "clear_mission_tasks",
            Group:   "P — Mission editing",
            Summary: "Clear every task from an entity's mission (so a fresh sequence can be added), by committing an empty plan through the same optimistic-concurrency path.",
            Returns: "{ networkId, taskCount:0, committed:true, version }",
            Hint:    "Then add_mission_task to build the new sequence; read the current state first with get_mission.",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the entity whose tasks to clear"),
            },
            Notes: new[]
            {
                "Commits an empty plan — the same asynchronous, version-checked path as add_mission_task, so the same 409/504 rules apply.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "wipe entity 1000's mission so a new sequence can be authored"),

        [("POST", "/missions/{networkId}/run")] = new RouteDoc(
            Tool:    "run_mission",
            Group:   "P — Mission editing",
            Summary: "Run (or restart) an entity's mission by jumping to its first task and resetting the phase clock. run and restart are the same jump-to-start the mechanism offers.",
            Returns: "{ networkId, restart, committed:true, version }",
            Hint:    "Advance the sim (play_simulation or step_simulation) so the entity actually executes the mission after this.",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the entity whose mission to run"),
                new("restart", "boolean", false, "Reserved: run and restart both jump to task 0 and reset the phase clock.", DefaultJson: "false"),
            },
            Notes: new[]
            {
                "Sends CMD_JUMP_TO_TASK to task index 0 — the mission still only advances while the sim is running (play_simulation / step_simulation).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "start entity 1000 executing its mission from the first task"),

        [("GET", "/entities/{networkId}/variables")] = new RouteDoc(
            Tool:    "list_entity_variables",
            Group:   "O — Variables (the watch, over HTTP)",
            Summary: "List an entity's blueprint variables — the same (entity, asset, path) addressing a Details/watch row uses, with each variable's live value and whether a staged write is still pending on it.",
            Returns: "{ networkId, asset, assetId, dispatch, variables: [{ path, type, value, writable, pending, pendingValue? }] }",
            Hint:    "Required: networkId. Optional: asset. Example: list_entity_variables({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity (see list_entities)"),
                new("asset", "string", false, "Blueprint NAME or asset Guid. Omit when the entity carries exactly one blueprint; the error names the choices when it carries several."),
            },
            Notes: new[]
            {
                "pending: true means a staged write for that variable has not been applied yet, so value is still the OLD number — the machine half of the editor's yellow.",
                "writable: false means the variable has no live address (its blueprint's dispatch kind has no staged-write layout), so it can be read but not staged.",
                "A Library-dispatch blueprint legitimately has no working-state variables and returns an empty list, not an error.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read every blueprint variable on entity 1000"),

        [("GET", "/entities/{networkId}/variable")] = new RouteDoc(
            Tool:    "get_entity_variable",
            Group:   "O — Variables (the watch, over HTTP)",
            Summary: "Read one blueprint variable by name, with its live value and its pending (staged-but-not-yet-applied) value if a write is queued.",
            Returns: "{ networkId, asset, assetId, path, type, value, writable, pending, pendingValue? }",
            Hint:    "Required: networkId, path. Example: get_entity_variable({networkId:1000, path:\"Health\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("path", "string", true, "The variable's name, as list_entity_variables reports it"),
                new("asset", "string", false, "Blueprint NAME or asset Guid; omit when the entity carries exactly one"),
                new("owner", "string", false, "⭐ CE-1320 — task · sop · paused · blueprint: write ANY behaviour slot instead of a blueprint variable by name", EnumJson: "[\"task\",\"sop\",\"paused\",\"blueprint\"]"),
                new("behaviour", "string", false, "With owner: the behaviour / blueprint name (needed when two slots match)"),
                new("role", "string", false, "With owner: params · variables (default variables)", EnumJson: "[\"params\",\"variables\"]"),
                new("part", "string", false, "With owner: a node's working-state or hosted-child slot, by its label (as get_entity_slots lists it)"),
                new("paramsJson", "string", false, "With owner, instead of path/value: a whole parameter set applied through the behaviour's own parser (the start record takes it too)"),
            },
            Notes: new[]
            {
                "An unknown variable name is a 400 pointing back at list_entity_variables — never an empty success.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"path\":\"Health\"}",
            ExampleGist: "read entity 1000's Health variable and whether an edit is still queued"),

        [("POST", "/entities/{networkId}/variable")] = new RouteDoc(
            Tool:    "stage_entity_variable",
            Group:   "O — Variables (the watch, over HTTP)",
            Summary: "STAGE a write to a running behaviour's value, through the same seam the editor's Details panel uses — a blueprint variable by name, or (with owner) ANY behaviour slot's params or working state: the task's, the SOP's, a paused task's, an attached blueprint's, a node's or a hosted child's. The value lands on the next advancing tick, IN PLACE — the behaviour is never restarted.",
            Returns: "{ networkId, asset, assetId, path, staged: true, pending: true, note } — with owner: { networkId, owner, behaviour, part, role, path, component, offset, size, staged, pending, willDrain, startRecordUpdated, restarted:false, note }",
            Hint:    "Required: networkId, path, value. Example: stage_entity_variable({networkId:1000, path:\"Health\", value:42}). Any slot: stage_entity_variable({networkId:1000, owner:\"task\", role:\"params\", path:\"CoverRadius\", value:40}) — discover owners, behaviours and parts with get_entity_slots.",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("path", "string", true, "The variable's name"),
                new("value", "any", true, "The new value, in the same JSON shape the read reports (a number for a numeric variable, [x,y,z] for a vector)"),
                new("asset", "string", false, "Blueprint NAME or asset Guid; omit when the entity carries exactly one"),
            },
            Notes: new[]
            {
                "Running is not a reason to refuse — it is a reason to stage. There is no \"pause first\" step.",
                "Until the world advances, get_entity_variable still reports the OLD value with pending: true. Step or play to make it land.",
                "A value whose width does not match the field is refused rather than written: the blackboard is shared between subsystems, so an overrun would corrupt a neighbour.",
                "⭐ With owner, path is dotted (St fields, In fields — e.g. Shots.Count) and [n] indexes an inline array (Shots.Items[1]). A task-params write also rewrites the start record's JSON, so a save and the \"same order again?\" checks stay true.",
                "⭐ The what-if loop: checkpoint → stage_entity_variable(owner…) → step → read → restore_checkpoint (skill §J).",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"path\":\"Health\",\"value\":42}",
            ExampleGist: "queue Health = 42; it applies on the next advancing tick"),

        [("GET", "/panels")] = new RouteDoc(
            Tool:    "list_panels",
            Group:   "T — Panels (the UI as data)",
            Summary: "What the editor's UI is showing, without pixels: every registered window plus every instrumented panel, and which of them published a view-model this frame.",
            Returns: "{ captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }",
            Hint:    "No params. Example: list_panels({})",
            Notes: new[]
            {
                "registered vs captured is the load-bearing distinction: a surface that publishes no model and one whose window is closed are different facts, and only the second is fixed by opening a window.",
                "CE-076: registered is COMPLETE for windows — WindowManager.RegisterWindow declares every window it registers, so a window can no longer be invisible here by forgetting to opt in. A window that publishes no view-model still appears in registered and is absent from captured.",
                "A LAZILY registered window (one created on first activation of its perspective) is absent until that perspective has been visited — switch_perspective first if you are enumerating exhaustively.",
                "kinds groups the live panels by their logical name — the key a cross-host comparison uses, since panel ids are unique per instance by design.",
                "captured entries are latest-wins and are NOT cleared per frame: a panel that stopped drawing still reports its last model.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see which panels are live and what kinds they are"),

        [("GET", "/panels/{panelId}")] = new RouteDoc(
            Tool:    "get_panel",
            Group:   "T — Panels (the UI as data)",
            Summary: "One panel's dumped view-model — the same object its draw renders from, so a field here is a field the designer sees.",
            Returns: "{ panelId, panelKind, model }",
            Hint:    "Required: panelId. Example: get_panel({panelId:\"editor_bp_manager\"})",
            Params: new RouteParam[]
            {
                new("panelId", "string", true, "Panel address from list_panels (e.g. \"editor_bp_manager\")"),
            },
            Notes: new[]
            {
                "The model is structured JSON, never a formatted blob — assert a field, do not parse prose.",
                "A miss says WHICH kind of miss it is: not instrumented, or instrumented but not drawing.",
            },
            ExampleArgsJson: "{\"panelId\":\"editor_bp_manager\"}",
            ExampleGist: "read the breakpoint panel's model and assert what it lists"),

        [("GET", "/panels/_gizmo")] = new RouteDoc(
            Tool:    "get_gizmo_frame",
            Group:   "T — Panels (the UI as data)",
            Summary: "What the map is drawing this frame, as data: the debug primitives, projected per shape.",
            Returns: "{ count, dropped, emitted, truncated, primitives:[{shape, space, layer, color, ...shape-specific}] }",
            Hint:    "Optional: max. Example: get_gizmo_frame({max:50})",
            Params: new RouteParam[]
            {
                new("max", "number", false, "Cap the number of primitives returned (default 500)"),
            },
            Notes: new[]
            {
                "truncated tells you the frame was clipped by max — without it a cap would read as the end of the frame.",
                "A shape with no field projection yet is reported by name with a note, never as aliased bytes.",
                "MOST OF A FRAME IS THE GRID. Measured on --mode all with an EMPTY world: 603 primitives, all "
              + "of them Line. So a non-zero count is NOT evidence that anything is on the map.",
                "TO ANSWER 'are the entities visible', COUNT PRIMITIVES BY shape AND IGNORE Line. A loaded "
              + "hill-attack frame read 739 primitives: Line 670 plus Box2D 8, Arrow 12, Text 8, SemanticShape "
              + "16, SpatialAnchor 16 — the non-Line shapes are the world. Compare that histogram against the "
              + "same read on the other host to localise a 'nothing renders here' report.",
                "The default cap is 500 and the grid alone can exceed it, so pass max (e.g. 2000) before "
              + "concluding a shape is absent — otherwise truncated:true means your answer is about the grid.",
            },
            ExampleArgsJson: "{\"max\":50}",
            ExampleGist: "inspect what the map is drawing without taking a screenshot"),

        [("GET", "/blueprints")] = new RouteDoc(
            Tool:    "list_blueprints",
            Group:   "Q — Blueprint hot-attach",
            Summary: "Every blueprint this editor compiled, with whether it can be attached to an entity.",
            Returns: "{ count, blueprints:[{ blueprintId, name, assetId, kind, stateSize, attachable }] }",
            Hint:    "No params. Example: list_blueprints({})",
            Notes: new[]
            {
                "Only Instance-dispatch blueprints occupy a slot on an entity; attachable says so up front rather than through a refusal.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "find a blueprint to try on a running entity"),

        [("POST", "/entities/{networkId}/attach-blueprint")] = new RouteDoc(
            Tool:    "attach_blueprint",
            Group:   "Q — Blueprint hot-attach",
            Summary: "Attach an Instance blueprint to an entity — the quick way to try a behaviour without authoring a mission. Run-state-aware: lands immediately while paused/Edit, next tick while running.",
            Returns: "{ networkId, blueprint, blueprintId, attached:true, path:\"direct\"|\"event\", applied:\"immediate\"|\"next-tick\", status?, tier?, note }",
            Hint:    "Required: networkId, blueprint. Example: attach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("blueprint", "string", true, "Blueprint name, asset Guid, or numeric blueprintId (see list_blueprints)"),
                new("paramsJson", "object", false, "Parameters for the blueprint, keyed by name (its paramSchema); omit for its declared defaults"),
            },
            Notes: new[]
            {
                "Run-state-aware (mirrors the editor's own panel): while time is FROZEN (Edit or paused) it attaches THIS frame (path:direct); while the sim is advancing it queues the ingress event (path:event) and you must step/play once before reading it back.",
                "Params now PERSIST through save_scenario — an attach with non-default params survives save→reload (they ride the assignment as resolved bytes, layout-versioned by the blueprint's StructureHash).",
                "A malformed paramsJson on the direct path is a 400 that changes nothing (parse-before-commit), not a half-applied slot.",
                "After it lands, the entity's variables appear in list_entity_variables — name the asset, since the entity may now carry more than one. See what is attached with list_entity_blueprints.",
            },
            ExampleArgsJson: "{\"networkId\":1001,\"blueprint\":\"ComponentCollectionDemo\"}",
            ExampleGist: "try a blueprint on entity 1001 right now"),

        [("POST", "/entities/{networkId}/detach-blueprint")] = new RouteDoc(
            Tool:    "detach_blueprint",
            Group:   "Q — Blueprint hot-attach",
            Summary: "Detach an Instance blueprint from an entity. Run-state-aware, like attach_blueprint.",
            Returns: "{ networkId, blueprint, blueprintId, detached, path:\"direct\"|\"event\", applied:\"immediate\"|\"next-tick\", note }",
            Hint:    "Required: networkId, blueprint. Example: detach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("blueprint", "string", true, "Blueprint name, asset Guid, or numeric blueprintId"),
            },
            Notes: new[]
            {
                "Run-state-aware: removes the slot THIS frame while time is frozen (path:direct); queues the event while the sim advances (path:event, next tick).",
                "On the direct path, detached:false means no slot for that blueprint was on the entity — nothing to remove.",
            },
            ExampleArgsJson: "{\"networkId\":1001,\"blueprint\":\"ComponentCollectionDemo\"}",
            ExampleGist: "put the entity back how you found it"),

        [("GET", "/entities/{networkId}/blueprints")] = new RouteDoc(
            Tool:    "list_entity_blueprints",
            Group:   "Q — Blueprint hot-attach",
            Summary: "The Instance blueprints currently attached to an entity — see what you have assigned before editing.",
            Returns: "{ networkId, count, blueprints:[{ blueprintId, name, assetId, payloadSize }] }",
            Hint:    "Required: networkId. Example: list_entity_blueprints({networkId:1001})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
            },
            Notes: new[]
            {
                "Reads the same slot table save_scenario snapshots, so it shows exactly what would persist.",
                "list_blueprints is the catalog (everything compiled); this is what is attached to ONE entity.",
            },
            ExampleArgsJson: "{\"networkId\":1001}",
            ExampleGist: "see which blueprints are on entity 1001"),

        [("GET", "/entities/{networkId}/state")] = new RouteDoc(
            Tool:    "get_entity_state",
            Group:   "R — Entity state",
            Summary: "The well-known fields parsed out — position, rotation, velocity, speed, current behaviour — so an assertion reads state.position.x instead of digging through component JSON.",
            Returns: "{ networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier}, roeEffective:{fire,reactions,forceLevel,returnFireWindowSeconds}, look:{bodyYawDeg,headYawDeg,eyeYawDeg,source,hasTarget,target,fovDegrees} }",
            Hint:    "Required: networkId. Example: get_entity_state({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
            },
            Notes: new[]
            {
                "A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.",
                "A convenience over get_entity, reading the same components: the two cannot disagree.",
                "roeEffective (CE-2289) is the ROE the unit OBEYS: its own where an order set it, else its nearest commander's, else its type default — a squad's HoldFire shows on every man.",
                "look (CE-3252, LD-D) is where a PERSON looks: eyeYawDeg = body + head, the cone vision uses; source order / aim / glance / free. Omitted for a body with no look (a vehicle's eyes are its body).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "where is entity 1000, how fast, doing what"),

        [("POST", "/breakpoints/continue")] = new RouteDoc(
            Tool:    "continue_from_breakpoint",
            Group:   "G — Breakpoints",
            Summary: "Resume the debugger after a breakpoint hit. Also what applies any live variable writes staged while it was stopped.",
            Returns: "{ wasPaused, action, isPaused, note }",
            Hint:    "Optional: step. Example: continue_from_breakpoint({})",
            Params: new RouteParam[]
            {
                new("step", "boolean", false, "Advance one step instead of running on"),
            },
            Notes: new[]
            {
                "⚠ Deleting a breakpoint does NOT resume: the debugger stays stopped, and while it is stopped every staged variable write is queued and never applied. Call this after a hit, not remove_breakpoint.",
                "Harmless when nothing is stopped — it answers wasPaused:false.",
                "The host also serves POST /breakpoints/step, which is exactly this call with step:true. Deliberately ONE tool, not two — use continue_from_breakpoint({step:true}).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "let the world run again after a breakpoint fired"),

        [("GET", "/breakpoints")] = new RouteDoc(
            Tool:    "list_breakpoints",
            Group:   "G — Breakpoints",
            Summary: "List all registered breakpoints.",
            Returns: "[{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]",
            Hint:    "No params. Example: list_breakpoints({})",
            ExampleArgsJson: "{}",
            ExampleGist: "list all active breakpoints and their hit counts"),

        [("DELETE", "/breakpoints/{id}")] = new RouteDoc(
            Tool:    "remove_breakpoint",
            Group:   "G — Breakpoints",
            Summary: "Remove a breakpoint by its ID string.",
            Returns: "ok:true envelope.",
            Hint:    "Req: id (string, e.g. \"BP#1\" from set_breakpoint). Example: remove_breakpoint({id:\"BP#1\"})",
            Params: new RouteParam[]
            {
                new("id", "string", true, "Breakpoint ID string (e.g. \"BP#1\" from set_breakpoint or list_breakpoints)"),
            },
            ExampleArgsJson: "{\"id\":\"BP#1\"}",
            ExampleGist: "remove breakpoint BP#1"),

        [("GET", "/breakpoints/hits")] = new RouteDoc(
            Tool:    "get_breakpoint_status",
            Group:   "G — Breakpoints",
            Summary: "Current pause state and last breakpoint hit.",
            Returns: "{ isPaused, pausedTick, lastHit: { breakpointId, networkId, name, conditionSummary, what, journalWrittenByTheTick?:[{seq, at, kind, text}] } | null }  — what (CE-1323) is what a behaviour step saw: \"entered Withdrawing\", \"Engaging -> Withdrawing fired on OrderReceived\", \"event Contact handled (consumed)\"; null for a data breakpoint",
            Hint:    "No params. Example: get_breakpoint_status({})",
            Notes: new[]
            {
                "Poll this after play to detect when a breakpoint fires.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "poll for breakpoint hit after calling play"),

        [("POST", "/checkpoint")] = new RouteDoc(
            Tool:    "checkpoint",
            Group:   "H — Checkpoint / diff",
            Summary: "Take a named checkpoint of the LIVE run — a keyframe of every node's live recording, through the cluster master (R-3044).",
            Returns: "{ published, requestId, label }",
            Hint:    "Optional: label (string). Requires a live RECORDED run (409 \"this run was not recorded\" otherwise). Example: checkpoint({label:\"before-assault\"})",
            Params: new RouteParam[] { new("label", "string", false, "The checkpoint's name (restore_checkpoint finds it by it); one is made when omitted, and returned") },
            Notes: new[]
            {
                "A checkpoint is part of the live recording (it survives into the replay), not a preview snapshot.",
                "restore_checkpoint continues the live run from it.",
            },
            ExampleArgsJson: "{\"label\":\"before-assault\"}",
            ExampleGist: "take a checkpoint before an experiment"),

        [("POST", "/checkpoint/restore")] = new RouteDoc(
            Tool:    "restore_checkpoint",
            Group:   "H — Checkpoint / diff",
            Summary: "Continue the live run from a named checkpoint, through the cluster master.",
            Returns: "{ published, requestId, label }",
            Hint:    "Req: label (string, as checkpoint returned it). Requires a live RECORDED run (409 \"this run was not recorded\" otherwise). Example: restore_checkpoint({label:\"before-assault\"})",
            Params: new RouteParam[] { new("label", "string", true, "The checkpoint to restore") },
            ExampleArgsJson: "{\"label\":\"before-assault\"}",
            ExampleGist: "revert all changes since the last checkpoint"),

        [("POST", "/diff/capture")] = new RouteDoc(
            Tool:    "capture_diff_baseline",
            Group:   "H — Checkpoint / diff",
            Summary: "Serialize current entity states server-side and return a baselineId.",
            Returns: "{ baselineId } (e.g. \"BL#1\")",
            Hint:    "No required params. Optional: entities (array of networkIds). Example: capture_diff_baseline({entities:[1000]})",
            Params: new RouteParam[]
            {
                new("entities", "array", false, "Optional list of networkIds to capture (default: all entities)",
                    ItemsJson: "{\"type\":\"number\"}"),
            },
            Notes: new[]
            {
                "Use before mutating the world, then call diff_state with the baselineId to see what changed.",
                "Optional entities array (networkId list) scopes which entities to capture (default: all).",
            },
            ExampleArgsJson: "{\"entities\":[1000]}",
            ExampleGist: "capture baseline for entity 1000 before mutation"),

        [("POST", "/diff/compare")] = new RouteDoc(
            Tool:    "diff_state",
            Group:   "H — Checkpoint / diff",
            Summary: "Compare a previously captured baseline against current entity state.",
            Returns: "A DiffNode tree showing only what changed (token-efficient). Includes entity births/deaths.",
            Hint:    "Req: baselineId (string from capture_diff_baseline). Optional: entities (array). Example: diff_state({baselineId:\"BL#1\"})",
            Params: new RouteParam[]
            {
                new("baselineId", "string", true, "Baseline ID from capture_diff_baseline (e.g. \"BL#1\")"),
                new("entities", "array", false, "Optional list of networkIds to diff (default: all entities in baseline)",
                    ItemsJson: "{\"type\":\"number\"}"),
            },
            Notes: new[]
            {
                "baselineId comes from capture_diff_baseline.",
                "Returns only changed components — token-efficient for AI consumption.",
            },
            ExampleArgsJson: "{\"baselineId\":\"BL#1\",\"entities\":[1000]}",
            ExampleGist: "diff entity 1000 against baseline BL#1"),

        [("GET", "/exercises")] = new RouteDoc(
            Tool:    "list_exercises",
            Group:   "I — Replay",
            Summary: "The exercises the cluster master can replay (its inventory) — every live run is one, recorded on every node (R-3053).",
            Returns: "{ count, exercises:[{exerciseId, scenario, startUtc, durationSeconds}] } newest first.",
            Hint:    "No params. Example: list_exercises({})",
            Notes: new[] { "Every host answers the same: each node keeps the master's inventory as it arrives over the cluster bus." },
            ExampleArgsJson: "{}",
            ExampleGist: "what can be replayed"),

        [("POST", "/exercises/keep")] = new RouteDoc(
            Tool:    "keep_exercise",
            Group:   "I — Replay",
            Summary: "⭐ CE-4058 — \"Keep for films\": archive an exercise — every node's recording, its meta and timeline sidecar, the scenario as it was loaded and Orchestrator.json, into {shared}/exercises/<id>/ (the cluster's Export op).",
            Returns: "{ published, requestId, exerciseId, archive } — archive is the folder the files land in.",
            Hint:    "Optional: exerciseId (string; the newest when omitted — 409 \"this run was not recorded\" when the last run was not recorded). Example: keep_exercise({})",
            Params: new RouteParam[]
            {
                new("exerciseId", "string", false, "The exercise to keep (list_exercises); the newest when omitted"),
            },
            Notes: new[]
            {
                "A kept exercise survives the temp root being cleared (a relaunch): the Replay Browser lists the archive beside the nodes' own copies.",
                "The takes of a kept recording live in its folder (Director Timeline TE-13).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "keep a recording for films"),

        [("DELETE", "/exercises/{id}")] = new RouteDoc(
            Tool:    "delete_exercise",
            Group:   "I — Replay",
            Summary: "⭐ CE-4090 (AQ108 D15) — discard an UNARCHIVED recording: its ledger entry and its staging copies (the retention code's delete, CE-3218). A kept (archived) exercise and the run still recording are not deleted here.",
            Returns: "{ published, requestId, exerciseId }; 404 for an id GET /exercises does not list; 400 for a malformed id.",
            Hint:    "Req: id (path, an exerciseId from list_exercises). Example: delete_exercise({id:\"…\"})",
            Params: new RouteParam[] { new("id", "string", true, "The exercise to discard (list_exercises)") },
            Notes: new[]
            {
                "The master refuses (the request fails, nothing is deleted) while the exercise is the run still recording: stop the run first.",
                "keep_exercise archives a recording for films; this is its opposite for a recording that should not pile up.",
            },
            ExampleArgsJson: "{\"id\":\"00000000-0000-0000-0000-000000000000\"}",
            ExampleGist: "discard a recording"),

        [("POST", "/run-settings")] = new RouteDoc(
            Tool:    "set_run_settings",
            Group:   "I — Replay",
            Summary: "⭐ CE-4090 (AQ108 D14) — the cluster-wide run settings, held by the master: recordLiveRuns (default true). With it OFF a live run writes no recording and no inventory entry; checkpoint, restore_checkpoint, enter_review, keep_exercise and enter_replay (of the last run) then answer 409 \"this run was not recorded\". Live does not imply recording.",
            Returns: "{ published, requestId, recordLiveRuns, note }",
            Hint:    "Req: recordLiveRuns (bool). Applies from the NEXT live load; the run on screen keeps its mode (get_status → recording). Example: set_run_settings({recordLiveRuns:false})",
            Params: new RouteParam[] { new("recordLiveRuns", "boolean", true, "Whether a live run starts a recording (the cluster's default is true)") },
            Notes: new[]
            {
                "The scenario verifier always records, whatever this says.",
                "A replay → live branch follows the setting like any live run.",
            },
            ExampleArgsJson: "{\"recordLiveRuns\":false}",
            ExampleGist: "run live without recording"),

        [("POST", "/replay")] = new RouteDoc(
            Tool:    "enter_replay",
            Group:   "I — Replay",
            Summary: "Replay an exercise on the whole cluster, from ANY state, in ONE request — the cluster master plans the way (unload, load, seek) (R-3055).",
            Returns: "{ published, transactionId, target, exerciseId, atSeconds } (+ awaited, reached with waitForReady).",
            Hint:    "Optional: exerciseId (string; the newest when omitted), atSeconds (number, from the exercise's start), waitForReady (bool); on the Replay Browser also path or paths (recording files). Example: enter_replay({atSeconds:30, waitForReady:true})",
            Params: new RouteParam[]
            {
                new("exerciseId", "string", false, "The exercise (list_exercises); when omitted the master picks: the live run in progress, else the newest finished one"),
                new("atSeconds", "number", false, "Where to enter, seconds after the exercise's start (default 0)"),
                new("atSimSeconds", "number", false, "⭐ CE-3329 — or where to enter in the RECORDED sim time (the run's own clock; a pause lands at its end); needs a v7 recording — lands ON OR BEFORE: the master maps it to a wall moment (interpolated between frames) and every node lands on its last frame at or before that moment, so the landed simSeconds can be up to one frame early (measured: 10 → 9.895); compare at the landed simSeconds the status reports"),
                new("waitForReady", "boolean", false, "Wait until the cluster reports OperatingReplay"),
                new("path", "string", false, "Replay Browser only (RB-2): one recording file (.fdp) to open, need not be in list_exercises"),
                new("paths", "array", false, "Replay Browser only (RB-2): a node group — one .fdp per node of one exercise"),
            },
            Notes: new[]
            {
                "With no exerciseId the cluster master picks: the live run in progress, else the newest finished one — the same on every host.",
                "Every node replays its OWN recording; the ordinary readers (list_entities, get_entity, get_entity_brain, get_entity_journal, get_selection) then read the replayed world.",
                "play / pause / step drive the replay clock; seek_replay jumps.",
                "The Replay Browser (-m replaybrowser, standalone) answers the same: its exercises are the recordings under the staging root; path / paths open any file or node group. A cluster host refuses a path.",
            },
            ExampleArgsJson: "{\"atSeconds\":30,\"waitForReady\":true}",
            ExampleGist: "replay the last exercise from 30 s in"),

        [("POST", "/replay/seek")] = new RouteDoc(
            Tool:    "seek_replay",
            Group:   "I — Replay",
            Summary: "Seek the replay to a moment — the operator timeline's seek round: every node restores its own recording there and the clocks snap. The moment is atSeconds, or a lookup over THIS node's recording: a frame, a step of frames, the frame after an event (cause), or the next / previous change of a unit (RB-2).",
            Returns: "{ published, requestId, atSeconds } (+ frame, resolvedFrom for a lookup; next: found, or {found:false, note})",
            Hint:    "One of: atSeconds (number) · atSimSeconds (number, the recorded sim time) · frame (integer) · frames (integer, ±) · cause ({frame, entity}) · next ({entity, component?}) with direction (1 | -1). Example: seek_replay({next:{entity:1042, component:\"Health\"}, direction:1})",
            Params: new RouteParam[]
            {
                new("atSeconds", "number", false, "Seconds after the exercise's start"),
                new("atSimSeconds", "number", false, "⭐ CE-3329 — or the RECORDED sim time to land on (a paused instant lands at its end, so what changed during the pause shows); refused for a v6 recording — lands ON OR BEFORE: the master maps it to a wall moment (interpolated between frames) and every node lands on its last frame at or before that moment, so the landed simSeconds can be up to one frame early (measured: 10 → 9.895); compare at the landed simSeconds the status reports"),
                new("frame", "integer", false, "A frame of this node's recording"),
                new("frames", "integer", false, "Frames forward (+) or back (-) from the one shown"),
                new("cause", "object", false, "{frame, entity}: the frame after an event, where its effect shows (the Event browser's causality jump); select the entity with select_entity"),
                new("next", "object", false, "{entity, component?}: the next frame where the unit changed (in that component, or any) · {lane}: the next event in a timeline lane (get_timeline_lanes)"),
                new("direction", "integer", false, "With next: 1 forward (default), -1 backward"),
            },
            Notes: new[]
            {
                "The cluster pauses for the seek; read get_replay_status, then the ordinary readers.",
                "A lookup reads this node's own recording; next scans it off the main thread in a reader of its own (the replayed world is not touched) — a long file takes a moment.",
            },
            ExampleArgsJson: "{\"atSeconds\":45}",
            ExampleGist: "jump the replay to 45 s"),

        [("POST", "/replay/stop")] = new RouteDoc(
            Tool:    "stop_replay",
            Group:   "I — Replay",
            Summary: "Leave the replay: the cluster goes to Idle.",
            Returns: "{ published, transactionId, target } (+ awaited, reached with waitForReady).",
            Hint:    "Optional: waitForReady (bool). Example: stop_replay({waitForReady:true})",
            Params: new RouteParam[] { new("waitForReady", "boolean", false, "Wait until the cluster reports Idle") },
            ExampleArgsJson: "{\"waitForReady\":true}",
            ExampleGist: "end the replay"),

        [("POST", "/review")] = new RouteDoc(
            Tool:    "enter_review",
            Group:   "I — Replay",
            Summary: "⭐ CE-3329 — During Action Review: pause the LIVE run and review its own recording in place — every node keeps its exact live point (a full checkpoint in RAM, a labelled keyframe review-<n> in its recording), the past is shown on the wire, and POST /review/exit returns to exactly where it paused. Only from OperatingLive.",
            Returns: "{ published, transactionId, target } (+ awaited, reached with waitForReady).",
            Hint:    "Req: atSeconds (wall, after the replay's zero) OR atSimSeconds (the recorded sim time) — a review always has an explicit start (400 without one; ⭐ AQ108 D9: the live point is no longer implied); optional waitForReady (bool). 409 \"this run was not recorded\" for a run loaded with 'Record live runs' off. Example: enter_review({atSimSeconds:30, waitForReady:true})",
            Params: new RouteParam[]
            {
                new("atSeconds", "number", false, "Where the review opens, seconds after the recording's zero — this or atSimSeconds is REQUIRED (AQ108 D9)"),
                new("atSimSeconds", "number", false, "Or the recorded sim time to open at (this or atSeconds is REQUIRED) — lands ON OR BEFORE: the master maps it to a wall moment (interpolated between frames) and every node lands on its last frame at or before that moment, so the landed simSeconds can be up to one frame early (measured: 10 → 9.895); compare at the landed simSeconds the status reports"),
                new("waitForReady", "boolean", false, "Wait until the cluster reports OperatingReview"),
            },
            Notes: new[] { "In a review: seek with POST /replay/seek (or /review/seek), play / pause / step with /sim/*, the axis with /replay/axis; it pauses itself at the live point.",
                           "World-changing commands answer 409 until you return (spawn, orders, components, save, checkpoints …)." },
            ExampleArgsJson: "{\"atSimSeconds\":30,\"waitForReady\":true}",
            ExampleGist: "look back at the live run"),

        [("POST", "/review/exit")] = new RouteDoc(
            Tool:    "exit_review",
            Group:   "I — Replay",
            Summary: "⭐ CE-3329 — leave the review: every node puts its live point back (the checkpoint restore's semantics), the clock returns to it, the recording resumes — live, paused.",
            Returns: "{ published, transactionId, target } (+ awaited, reached with waitForReady).",
            Hint:    "Optional: waitForReady (bool). Example: exit_review({waitForReady:true})",
            Params: new RouteParam[] { new("waitForReady", "boolean", false, "Wait until the cluster reports OperatingLive") },
            ExampleArgsJson: "{\"waitForReady\":true}",
            ExampleGist: "back to the live run"),

        [("POST", "/review/seek")] = new RouteDoc(
            Tool:    "seek_review",
            Group:   "I — Replay",
            Summary: "⭐ CE-3329 — the same seek as POST /replay/seek, named for a review (atSeconds, atSimSeconds, frame, frames, cause, next).",
            Returns: "as seek_replay",
            Hint:    "One of: atSeconds · atSimSeconds · frame · frames · cause · next. Example: seek_review({atSimSeconds:12})",
            Params: new RouteParam[]
            {
                new("atSeconds", "number", false, "Seconds after the recording's zero"),
                new("atSimSeconds", "number", false, "The recorded sim time — lands ON OR BEFORE: the master maps it to a wall moment (interpolated between frames) and every node lands on its last frame at or before that moment, so the landed simSeconds can be up to one frame early (measured: 10 → 9.895); compare at the landed simSeconds the status reports"),
            },
            ExampleArgsJson: "{\"atSimSeconds\":12}",
            ExampleGist: "move within the review"),

        [("POST", "/replay/axis")] = new RouteDoc(
            Tool:    "set_replay_axis",
            Group:   "I — Replay",
            Summary: "⭐ CE-3329 — switch the replay's time axis for the whole session (R-3058): Sim (the run's own clock — a play jumps over every paused stretch and its changes land at once) or Wall (pauses play out). No seek: the frame on screen stays.",
            Returns: "{ published, requestId, axis }",
            Hint:    "Req: axis (\"Sim\" | \"Wall\"). Example: set_replay_axis({axis:\"Wall\"})",
            Params: new RouteParam[] { new("axis", "string", true, "\"Sim\" (default) or \"Wall\"") },
            Notes: new[] { "The master holds it and every node hears it (the cluster-state broadcast); the Replay Browser keeps its own (the viewer's).",
                           "get_replay_status reports it as axis; atSimSeconds seeks work on either axis." },
            ExampleArgsJson: "{\"axis\":\"Wall\"}",
            ExampleGist: "skip or play the pauses"),

        [("POST", "/replay/loop")] = new RouteDoc(
            Tool:    "set_replay_loop",
            Group:   "I — Replay",
            Summary: "⭐ CE-3329 §12n — loop the replay (or review) between two moments: when the playhead reaches the end the master's replay clock jumps back to the start, on either axis. A SESSION setting held by the master and drawn by every host; {} clears it.",
            Returns: "{ published, requestId, cleared }",
            Hint:    "Req: fromSeconds+toSeconds (wall seconds after the zero) OR fromSimSeconds+toSimSeconds (the recording's sim seconds); none = clear. Example: set_replay_loop({fromSeconds:2,toSeconds:5})",
            Params: new RouteParam[]
            {
                new("fromSeconds", "number", false, "Loop start, wall seconds after the replay's zero"),
                new("toSeconds", "number", false, "Loop end, wall seconds after the replay's zero"),
                new("fromSimSeconds", "number", false, "Loop start in the recording's own sim seconds (the master resolves it)"),
                new("toSimSeconds", "number", false, "Loop end in the recording's own sim seconds"),
            },
            Notes: new[] { "Valid only 0 <= from < to <= the replay's length; otherwise the master refuses it (the previous loop stays). Cleared when the cluster leaves the replay / review.",
                           "Enforced only while the clock runs; a seek outside the loop is honoured. get_replay_status reports it as loop {fromSeconds,toSeconds} on the active axis (null = none). The Replay Browser keeps its own loop (the viewer's)." },
            ExampleArgsJson: "{\"fromSeconds\":2,\"toSeconds\":5}",
            ExampleGist: "loop a stretch of the replay"),

        [("GET", "/replay/status")] = new RouteDoc(
            Tool:    "get_replay_status",
            Group:   "I — Replay",
            Summary: "The cluster's state, the loaded replay's length, this node's replay recording, and its clock.",
            Returns: "{ clusterState, replaying, durationSeconds, recording, simSeconds, axis, loop, sim, browser } — axis: the session's time axis (Sim | Wall, CE-3329) — loop: {fromSeconds,toSeconds} on the active axis, or null (CE-3329 §12n) — simSeconds: the RECORDED sim time of the frame on screen (the live run's own clock; sim.totalTime is the replay clock, wall seconds after the zero); browser (Replay Browser only): {exerciseId, frame, frames, atSeconds, spanSeconds, primaryNode, view, playing, rate, nodes[], history, selectionHistory}",
            Hint:    "No params. Example: get_replay_status({})",
            ExampleArgsJson: "{}",
            ExampleGist: "is the cluster replaying, and where"),

        [("POST", "/replay/view")] = new RouteDoc(
            Tool:    "set_replay_view",
            Group:   "I — Replay",
            Summary: "Replay Browser only (RB-3): show one node's replayed world or the merged world of all nodes — the Federation panel's switch.",
            Returns: "the Replay Browser's status (get_replay_status's browser field)",
            Hint:    "Req: view (\"merged\" or a loaded node id). Example: set_replay_view({view:\"merged\"})",
            Params: new RouteParam[] { new("view", "string", true, "\"merged\", or the node id whose world to show") },
            Notes: new[] { "The ordinary readers then read that world. Every other host answers NOT_SUPPORTED_HERE(replay.browser)." },
            ExampleArgsJson: "{\"view\":\"merged\"}",
            ExampleGist: "look at every node's units together"),

        [("POST", "/replay/history")] = new RouteDoc(
            Tool:    "step_replay_history",
            Group:   "I — Replay",
            Summary: "Replay Browser only (RB-3): back / forward through the playback waypoints — the Timeline's back and forward buttons (every seek leaves one).",
            Returns: "the Replay Browser's status",
            Hint:    "Req: direction (\"back\" | \"forward\"). Example: step_replay_history({direction:\"back\"})",
            Params: new RouteParam[] { new("direction", "string", true, "back | forward") },
            ExampleArgsJson: "{\"direction\":\"back\"}",
            ExampleGist: "go back to where the replay was before the last jump"),

        [("POST", "/selection/history")] = new RouteDoc(
            Tool:    "step_selection_history",
            Group:   "I — Replay",
            Summary: "Replay Browser only (RB-3): back / forward through the units selected.",
            Returns: "the Replay Browser's status",
            Hint:    "Req: direction (\"back\" | \"forward\"). Example: step_selection_history({direction:\"back\"})",
            Params: new RouteParam[] { new("direction", "string", true, "back | forward") },
            ExampleArgsJson: "{\"direction\":\"back\"}",
            ExampleGist: "select the unit selected before"),

        [("POST", "/replay/export")] = new RouteDoc(
            Tool:    "export_replay_json",
            Group:   "I — Replay",
            Summary: "Replay Browser only (RB-4): export the shown node's recording to JSON, every option the Timeline panel has, as a job off the main thread.",
            Returns: "{ job, state:'running', input, path } — poll get_replay_export",
            Hint:    "All optional: window (full|frames|time), from, to, format (absolute|changelog|incremental), entities (network ids), entityIndex, includeEntities, includeEvents, minified, epsilon, output (file path; default a temp file). Example: export_replay_json({window:\"frames\", from:4000, to:4200, entities:[1042]})",
            Params: new RouteParam[]
            {
                new("window", "string", false, "full (default) | frames | time"),
                new("from", "number", false, "First frame (frames) or second (time)"),
                new("to", "number", false, "Last frame (frames) or second (time)"),
                new("format", "string", false, "absolute (default) | changelog | incremental"),
                new("entities", "array", false, "Only these units (network ids, resolved in the shown world)"),
                new("entityIndex", "integer", false, "Only the entity with this index"),
                new("includeEntities", "boolean", false, "Default true"),
                new("includeEvents", "boolean", false, "Default true"),
                new("minified", "boolean", false, "Default false"),
                new("epsilon", "number", false, "Float tolerance for changes (default 0.001)"),
                new("output", "string", false, "Where to write (default: a temp file, named in the answer)"),
            },
            Notes: new[] { "The file is what the panel's 'Save to JSON' writes for the same options." },
            ExampleArgsJson: "{\"window\":\"frames\",\"from\":0,\"to\":300,\"format\":\"changelog\"}",
            ExampleGist: "dump the first 300 frames' changes to a JSON file"),

        [("GET", "/replay/export/{job}")] = new RouteDoc(
            Tool:    "get_replay_export",
            Group:   "I — Replay",
            Summary: "Replay Browser only (RB-4): an export job's state, file and size.",
            Returns: "{ job, state: running | done | failed, input, path, bytes, error }",
            Hint:    "Req: job (string, from export_replay_json). Example: get_replay_export({job:\"a1b2c3d4e5f6\"})",
            Params: new RouteParam[] { new("job", "string", true, "The job id") },
            ExampleArgsJson: "{\"job\":\"a1b2c3d4e5f6\"}",
            ExampleGist: "is the export finished, and where is the file"),

        [("GET", "/timeline/lanes")] = new RouteDoc(
            Tool:    "get_timeline_lanes",
            Group:   "I — Replay",
            Summary: "The Event Timeline's lanes on this host (RB-6): each lane with its event count in the shown recording's index, and the index's progress (it builds in the background on first open).",
            Returns: "{ recording, progress, complete, error, frames, lanes:[{id, name, group, priority, glyph, indexed, count, error}] }",
            Hint:    "No params. Example: get_timeline_lanes({})",
            Notes: new[] { "Only a host that keeps a timeline index answers (the Replay Browser, the editor and every ECS cluster node — live); others: NOT_SUPPORTED_HERE(timeline.read)." },
            ExampleArgsJson: "{}",
            ExampleGist: "what the timeline shows, and how far its index is"),

        [("GET", "/timeline/events")] = new RouteDoc(
            Tool:    "get_timeline_events",
            Group:   "I — Replay",
            Summary: "One timeline lane's events in a frame range, optionally only one unit's (RB-6) — what the index already holds, no scan of the file.",
            Returns: "{ lane, count, truncated, events:[{frame, subject, other, x, y, value}] }",
            Hint:    "Req: lane (query). Optional from, to (frames), entity (network id), limit (default 500). Example: get_timeline_events({lane:\"shots\", entity:1042})",
            Params: new RouteParam[]
            {
                new("lane", "string", true, "The lane id (get_timeline_lanes)"),
                new("from", "integer", false, "First frame"),
                new("to", "integer", false, "Last frame"),
                new("entity", "number", false, "Only events of this unit (subject or other)"),
                new("limit", "integer", false, "How many events to return (default 500, at most 5000)"),
            },
            Notes: new[] { "seek_replay({next:{lane}}) jumps to the next event of a lane." },
            ExampleArgsJson: "{\"lane\":\"shots\",\"limit\":20}",
            ExampleGist: "list a lane's events"),

        [("POST", "/timeline/lanes")] = new RouteDoc(
            Tool:    "add_timeline_lane",
            Group:   "I — Replay",
            Summary: "Add a user's lane to the timeline (RB-6): a descriptor and the condition that makes an event — the lane catalogue's Register; the index scans it in the background.",
            Returns: "{ registered, note }",
            Hint:    "Req: Lane ({Id, DisplayName, Group?, Priority?, Glyph?}) and When (a condition, as search_replay takes). Optional Payload. Example: add_timeline_lane({Lane:{Id:\"dmg\", DisplayName:\"Damage\"}, When:{\"$type\":\"PropertyMatch\", ...}})",
            Params: new RouteParam[]
            {
                new("Lane", "object", true, "{Id, DisplayName, Group?, Priority?, Color?, Glyph?}"),
                new("When", "object", true, "The condition (SearchPredicateDto JSON)"),
                new("Payload", "object", false, "Which fields of a match to keep (ResultProjectionDto)"),
            },
            ExampleArgsJson: "{\"Lane\":{\"Id\":\"dmg\",\"DisplayName\":\"Damage\"},\"When\":{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Health\",\"PropertyPath\":\"Current\",\"Operator\":\"Changed\",\"Predicate\":{\"$type\":\"Numeric\"}}}",
            ExampleGist: "add a lane that marks every health change"),

        [("GET", "/entities/{networkId}/journal/history")] = new RouteDoc(
            Tool:    "get_journal_history",
            Group:   "I — Replay",
            Summary: "EVERY brain-journal entry of one unit across this node's replay recording (or a frame range), each once, with the frame it first appears at (CE-1265) — the full why-history of a replayed exercise.",
            Returns: "{ networkId, from, to, found, gap, count, entries:[{seq, at, kind, text, frame}] }",
            Hint:    "Req: networkId (number); optional from, to (frames, query). In a replay. Example: get_journal_history({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("from", "number", false, "First frame (query, default 0)"),
                new("to", "number", false, "Last frame (query, default the last)"),
            },
            Notes: new[]
            {
                "Ask the node that runs the unit's brain (its journal is in that node's recording).",
                "It reads the recording file frame by frame, read-only: the replayed world is not touched; a long file takes a moment.",
                "gap:true means entries were lost between two recorded frames (should not happen: every change dirties the unit's record).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "list every decision unit 1000 made in the exercise, and why"),

        [("POST", "/replay/search")] = new RouteDoc(
            Tool:    "search_replay",
            Group:   "I — Replay",
            Summary: "Find the frames of this node's replay recording where a condition holds (CE-1266) — the same condition shape breakpoints take (component field match, event, lifecycle, spatial, compound).",
            Returns: "{ count, truncated, hits:[{frame, networkId, context}] }",
            Hint:    "Req: condition (object, a SearchPredicateDto) or lifecycle (object: {IdentifierType: NetworkId|EcsHandle|NameSubstring, TargetValue, NameComponentType?}). Optional networkId (number), limit (number, default 200). Example: search_replay({condition:{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Fdp.Toolkit.Behavior.Components.BehaviorState\",\"PropertyPath\":\"ActiveBehaviorHash\",\"Operator\":\"Changed\",\"Predicate\":{\"$type\":\"Numeric\"}}, networkId:1000})",
            Params: new RouteParam[]
            {
                new("condition", "object", false, "The condition (SearchPredicateDto JSON, as add_breakpoint takes)"),
                new("lifecycle", "object", false, "RB-5 — the Search panel's lifecycle mode: each matching unit's first and last frame ({mode:'lifecycle', hits:[{startFrame, endFrame, networkId, context}]})"),
                new("networkId", "number", false, "Only this unit"),
                new("limit", "number", false, "How many hits to return (default 200)"),
            },
            Notes: new[]
            {
                "Changed compares each frame with the frame before it (a unit's first frame counts as changed) — e.g. every frame a unit's ActiveBehaviorHash switched.",
                "ComponentType takes the component's name or its full name; an unknown name is an error, never an empty result.",
                "Then seek_replay to a hit to read the moment with the ordinary readers.",
            },
            ExampleArgsJson: "{\"condition\":{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Fdp.Toolkit.Behavior.Components.BehaviorState\",\"PropertyPath\":\"ActiveBehaviorHash\",\"Operator\":\"Changed\",\"Predicate\":{\"$type\":\"Numeric\"}},\"networkId\":1000,\"limit\":10}",
            ExampleGist: "find when something happened in the recording"),

        [("GET", "/logs")] = new RouteDoc(
            Tool:    "get_logs",
            Group:   "J — Logs",
            Summary: "Query the in-process log sinks. Returns [{timestamp, level, logger, message}] sorted newest-first.",
            Returns: "[{timestamp, level, logger, message}] sorted newest-first.",
            Hint:    "No required params. Optional: level (Trace|Debug|Info|Warning|Error|Critical), logger (string), since (ISO-8601), max (number). Example: get_logs({level:\"Warning\"})",
            Params: new RouteParam[]
            {
                new("level", "string", false, "Minimum severity level (inclusive). Omit to return all levels.",
                    EnumJson: "[\"Trace\",\"Debug\",\"Info\",\"Warning\",\"Error\",\"Critical\"]"),
                new("logger", "string", false, "Filter by logger name substring (case-insensitive). Omit to return all loggers."),
                new("since", "string", false, "ISO-8601 timestamp. Only entries with timestamp >= since are returned."),
                new("max", "number", false, "Maximum number of entries to return (default 200).",
                    DefaultJson: "200"),
            },
            Notes: new[]
            {
                "level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.",
                "logger = case-insensitive substring match on logger name.",
                "since = ISO-8601 timestamp; entries with timestamp >= since are included.",
                "Read off-thread — no main-thread marshal required.",
                "ON A CLUSTER ALWAYS PASS level:\"Info\" (or higher). Measured on --mode all: an UNFILTERED "
              + "read returned 176 of 200 entries as [TC3] time-sync chatter (TimeSyncRequest/SyncResponse/RTT "
              + "gentle-steer) logged at Trace by four nodes — no application line survives in the window. The "
              + "same read with level:\"Info\" returned 25 lines and zero [TC3].",
                "AN UNKNOWN FILTER IS STILL SERVED LENIENTLY, BUT IT NOW TELLS YOU (CE-107). level:\"INFO_\" "
              + "or a misspelled param (limit instead of max) returns the whole ring as before — the endpoint "
              + "does not refuse a diagnostic call — but the SUCCESS envelope carries hint.why naming the "
              + "ignored key or the unapplied level, and it also says so when no level filter was given at "
              + "all. READ hint ON SUCCESS: it is the difference between an answer and an answer to a "
              + "different question.",
                "The param is max, NOT limit. Nothing rejects limit; it is simply not read.",
            },
            ExampleArgsJson: "{\"level\":\"Warning\",\"max\":50}",
            ExampleGist: "get last 50 Warning-or-higher log entries"),

        [("POST", "/trace/observe")] = new RouteDoc(
            Tool:    "observe_trace",
            Group:   "K — AI behavior traces",
            Summary: "Arm or disarm AI behavior trace buffer allocation for an entity.",
            Returns: "{ armed, networkId }",
            Hint:    "Req: networkId (number), on (bool). Example: observe_trace({networkId:1000,on:true})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("on", "boolean", true, "true to arm tracing, false to disarm"),
            },
            Notes: new[]
            {
                "Must arm before get_entity_trace will return populated trace data.",
                "Without arming, get_entity_trace returns empty trace.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"on\":true}",
            ExampleGist: "arm AI behavior tracing for entity 1000"),

        [("GET", "/entities/{networkId}/trace")] = new RouteDoc(
            Tool:    "get_entity_trace",
            Group:   "K — AI behavior traces",
            Summary: "Extract AI behavior trace for an entity.",
            Returns: "BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.",
            Hint:    "Req: networkId (number). Must call observe_trace({networkId,on:true}) first. Example: get_entity_trace({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "Arm the entity with observe_trace first to populate trace data.",
                "Returns tier field indicating the AI tier type (BTree/HSM/blueprint).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read AI behavior trace for entity 1000 after arming"),

        [("GET", "/entities/{networkId}/brain")] = new RouteDoc(
            Tool:    "get_entity_brain",
            Group:   "K — AI behavior traces",
            Summary: "What a unit is doing and why — the Brain tab as JSON (CE-1248): task, who ordered it, paused task, SOP, ROE, each channel's right-now, HSM regions, squad play, utility choices (every brain unit), contacts, and the brain journal's recent changes.",
            Returns: "{ networkId, hasBrain, onBrainNode, doing, origin, urgency, sinceSeconds, paused, sop, roe, rightNow:[{channel, actionId, reason, hold, progress, needed}], regions:[{region, state, stateIndex}], squad, choices:[{decisionId, winner, previousWinner, margin, switches}], contacts, heardOnly, journal:{on, seq, changes:[{seq, at, kind, text}]} }",
            Hint:    "Req: networkId (number). Read on the brain's perspective (Scenario on a cluster). Example: get_entity_brain({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "onBrainNode:false means this node does not run the unit's brain — only task / SOP / ROE are replicated here.",
                "choices fill only while the unit is observed (observe_trace, set_debug_settings trace, or its Brain tab open); the journal is always on.",
                "journal.changes are newest first; poll get_entity_journal with ?since=<seq> for only the new ones.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "see what unit 1000 is doing, who ordered it, and what changed lately"),

        [("GET", "/entities/{networkId}/journal")] = new RouteDoc(
            Tool:    "get_entity_journal",
            Group:   "K — AI behavior traces",
            Summary: "A unit's brain journal after a sequence number (CE-1249): every task start / pause / resume / end / clear / refusal (with its reason), SOP change, utility switch, move start and HSM region change, with its cause — the last 24, always on.",
            Returns: "{ networkId, on, seq, gap, entries:[{seq, at, kind, text}] } — oldest first; gap:true means more than 24 were written since `since` and the oldest are gone.",
            Hint:    "Req: networkId (number); optional since (number, query, default 0). Example: get_entity_journal({networkId:1000, since:12})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("since", "number", false, "Only entries with seq greater than this (query); pass the last seq you saw"),
            },
            Notes: new[]
            {
                "seq counts every entry ever written to the unit and never resets — use it to poll without repeats.",
                "A refusal repeated every tick is ONE entry whose time is refreshed.",
                "on:false — not a brain unit on this node, or the host was started with the journal switched off.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"since\":0}",
            ExampleGist: "list what changed in unit 1000's mind and why"),

        [("GET", "/view")] = new RouteDoc(
            Tool:    "get_view",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "What the operator is looking at (CE-1262): the view mode (2D / 3D / FPS), the camera (eye, forward, yaw, pitch, distance, vertical field of view, viewport, the 2-D rectangle), the possessed unit and the unit its crosshair rests on, the selection, and the units in view nearest first with their screen point.",
            Returns: "{ mode, camera:{eye, forward, yawDeg, pitchDeg, distance, fovyDeg, viewport, rect2D?}, possessed, aimTarget, zoomFovDeg?, inView:[{networkId, name, distance, screen}], selection:[ids], primary, following?, followDistance? } — camera:null (with a note) where no map canvas draws the world.",
            Hint:    "Optional limit (number, query, default 50). Example: get_view({limit:20})",
            Params: new RouteParam[]
            {
                new("limit", "number", false, "How many in-view units to list, nearest first (query; default 50, max 500)"),
            },
            Notes: new[]
            {
                "The operator points by SELECTING (click; in first person the select key on the unit under the crosshair) — read primary / selection.",
                "aimTarget is what the first-person crosshair rests on right now; it moves with the aim.",
                "inView in 2D is the map's own culling; in 3D / FPS a frustum test (not occlusion) — check a line with explain_line_of_sight using possessed as observer.",
                "For what the possessed unit itself perceives, read get_entity_sensors / get_entity_memory on possessed.",
            },
            ExampleArgsJson: "{\"limit\":20}",
            ExampleGist: "see what the operator sees and which unit they mean"),

        [("POST", "/view/follow")] = new RouteDoc(
            Tool:    "follow_unit",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "The map's 3-D camera follows a unit (CE-1293): far away it orbits and looks at him, near it is over his shoulder, at distance 0 it is at his eyes looking where he looks. entity:null hands the camera back to the operator.",
            Returns: "{ following, distance } — or { following:null } after entity:null. 409 where no map canvas draws the world, or while the station possesses a unit.",
            Hint:    "Example: follow_unit({entity:4242, distance:20}); stop: follow_unit({entity:null})",
            Params: new RouteParam[]
            {
                new("entity", "number", true, "The unit's networkId (body); null stops following"),
                new("distance", "number", false, "Metres from him on the continuum (body; default 14; 0 = his eyes; max 400)"),
            },
            Notes: new[]
            {
                "The operator takes the camera back with W/A/S/D, Esc or a middle-button drag; get_view reports following / followDistance while it lasts.",
                "Distances are a person's: a vehicle's orbit / shoulder band is 4x, a flyer's 8x.",
            },
            ExampleArgsJson: "{\"entity\":4242,\"distance\":20}",
            ExampleGist: "watch a unit the agent is debugging"),

        [("GET", "/selection")] = new RouteDoc(
            Tool:    "get_selection",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "The units the operator has selected (CE-1263): the primary and all.",
            Returns: "{ primary, selection:[networkId] }",
            Hint:    "No params. Example: get_selection({})",
            Notes: new[]
            {
                "To point the operator at a unit, select_entity (the panels follow the selection) and focus_entity (centres the camera).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "which unit is the operator asking about"),

        [("POST", "/selection")] = new RouteDoc(
            Tool:    "select_entity",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "Select a unit for the operator (RB-7, R-4083) — the panels that follow the selection (the inspector, the Diff, seek-to-change) then show it. Through the UI's own request path: SelectEntityCommand, applied by the host's SelectionRequestSystem on the next frame.",
            Returns: "{ requested, note } — read get_selection after a frame",
            Hint:    "Req: entity (number, a network id). Example: select_entity({entity:1042})",
            Params: new RouteParam[] { new("entity", "number", true, "The unit's network id") },
            Notes: new[]
            {
                "Your own reads need no selection — every per-unit route takes the id. This is for pointing the OPERATOR at a unit.",
                "On the Replay Browser the selection joins its selection history (step_selection_history).",
            },
            ExampleArgsJson: "{\"entity\":1042}",
            ExampleGist: "show the operator unit 1042 in the panels"),

        [("GET", "/entities/{networkId}/debug-settings")] = new RouteDoc(
            Tool:    "get_debug_settings",
            Group:   "K — AI behavior traces",
            Summary: "A unit's debug settings (CE-1329) — the ONE store the Brain tab's Show on map ticks and the map's right-click Pin gizmos / AI trace items use: the gizmo families pinned on it (they draw for it, selected or not; selection draws none), its trace (the user's switch; traceOn also counts a Brain tab showing it and an observe / breakpoint arming), and whether the 3-D camera follows it. Remembered per scenario.",
            Returns: "{ networkId, pins:[family labels], trace, traceOn, follow }",
            Hint:    "Req: networkId (number). Editor only. Example: get_debug_settings({networkId:1000})",
            Params: new RouteParam[] { new("networkId", "number", true, "Network entity ID (long)") },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "what is switched on for unit 1000"),

        [("POST", "/entities/{networkId}/debug-settings")] = new RouteDoc(
            Tool:    "set_debug_settings",
            Group:   "K — AI behavior traces",
            Summary: "Sets a unit's debug settings (CE-1329) — exactly what the Brain tab's ticks and the right-click menu set, and the operator sees it: pins (REPLACES the pinned gizmo families), trace (records the unit's BTree / HSM rings and utility trace), follow (the 3-D camera follows it), reset (forgets what is remembered for it first). Only what is given changes. Remembered per scenario (restored on its next load). ⛔ Replaces debug_unit.",
            Returns: "{ networkId, pins:[family labels], trace, traceOn, follow }",
            Hint:    "Req: networkId (number); optional pins (string[] of: Perception, Contacts, EQS, Utility, Squad, Actions, Command picture, My units' contacts, Path), trace (bool), follow (bool), reset (bool). Editor only. Example: set_debug_settings({networkId:1000, pins:[\"Perception\",\"Contacts\"], trace:true})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("pins", "array", false, "The gizmo families to pin (replaces the set); [] unpins all"),
                new("trace", "boolean", false, "Record the unit's behaviour trace (remembered)"),
                new("follow", "boolean", false, "The 3-D camera follows the unit (remembered); false lets go"),
                new("reset", "boolean", false, "Forget everything remembered for this unit first"),
            },
            Notes: new[]
            {
                "Then read get_entity_brain (choices fill), get_entity_trace, and arm break_on_journal.",
                "A unit whose Brain tab is open is traced anyway (traceOn), without touching its trace switch.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"pins\":[\"Perception\"],\"trace\":true}",
            ExampleGist: "watch unit 1000 closely: its vision cone on the map and its trace"),

        [("POST", "/entities/{networkId}/journal/break")] = new RouteDoc(
            Tool:    "break_on_journal",
            Group:   "G — Breakpoints",
            Summary: "Stop the simulation when THIS unit changes its mind (CE-1258): a data breakpoint on its brain journal's sequence number, filtered to the unit (a neighbour's change does not stop it); optionally only for some entry kinds.",
            Returns: "{ breakpointId, networkId } — then poll get_breakpoint_status: its lastHit.journalWrittenByTheTick lists the entries the hit tick wrote.",
            Hint:    "Req: networkId (number); optional kinds (string[]: TaskStarted, TaskPaused, TaskResumed, TaskEnded, TaskCleared, TaskRefused, SopStarted, SopEnded, DecisionSwitched, MoveStarted, PlayStarted, PlayPhase, PlayEnded, OrderIssued, StateChanged). Example: break_on_journal({networkId:1000, kinds:[\"TaskStarted\",\"TaskPaused\"]})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("kinds", "array", false, "Only stop for these journal entry kinds (default: any entry)", ItemsJson: "{\"type\":\"string\"}"),
            },
            Notes: new[]
            {
                "A stop shows the world at the START of the tick that changed it (the cause still in place); continue_from_breakpoint({step:true}) runs that tick.",
                "It keeps firing on every later change until removed with remove_breakpoint.",
                "Editor only, like every breakpoint.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "pause the next time unit 1000's brain changes"),

        [("GET", "/utility/decisions")] = new RouteDoc(
            Tool:    "list_utility_decisions",
            Group:   "K — AI behavior traces",
            Summary: "Every registered utility decision (CE-1325): name, asset id, kind, option count — the C# [UtilityDecision] classes.",
            Returns: "[{ decisionId, name, assetId, kind, options }]",
            Hint:    "No params. Example: list_utility_decisions({})",
            ExampleArgsJson: "{}",
            ExampleGist: "list the utility decisions"),

        [("GET", "/utility/decisions/{key}")] = new RouteDoc(
            Tool:    "get_utility_decision",
            Group:   "K — AI behavior traces",
            Summary: "What a utility decision IS (CE-1325): per option its aggregation and considerations (input, context, weight, curve kind + slope / exponent / shift, modifier); with networkId that unit's values (winner, margin, per-consideration raw -> curved -> weighted).",
            Returns: "{ decisionId, name, assetId, kind, ranking, hysteresis, options:[{optionId, name, aggregation, considerations:[{input, context, weight, curve, slope, exponent, xShift, modifier, raw?, curved?, weighted?}], score?, winner?, zeroedBy?}], live?:{unit, traced, evaluations, switches, winner, runnerUp, margin, ranked} }",
            Hint:    "Req: key (asset id, name or id). Optional networkId (query). Example: get_utility_decision({key:\"Combat posture\", networkId:1007})",
            Params: new RouteParam[]
            {
                new("key", "string", true, "The decision's asset id, its name, or its numeric id"),
                new("networkId", "number", false, "A unit: adds its values (query parameter)"),
            },
            Notes: new[]
            {
                "The settings come from the RUNNING definition — the same for a C#-built behaviour as for an authored one.",
                "Live per-consideration values need the unit traced (observe_trace); without, live.traced is still true when the decision log exists and the rows have no raw values.",
                "An option scored 0 names the consideration that zeroed it (zeroedBy).",
            },
            ExampleArgsJson: "{\"key\":\"Combat posture\"}",
            ExampleGist: "read a utility decision's settings"),

        [("GET", "/behaviours/{name}/decisions")] = new RouteDoc(
            Tool:    "get_behaviour_decisions",
            Group:   "K — AI behavior traces",
            Summary: "Which node / state / transition of a behaviour binds which utility decision, and how (CE-1325): ChooseOption chooses, RankCandidates ranks, an IsOption guard reads one option, a custom action holds one in its params.",
            Returns: "{ behaviour, bindings:[{kind: node|state|transition, element, label, role: Chooses|Ranks|Reads|Holds, via (the params field), decision, assetId, decisionId, option?}] }",
            Hint:    "Req: name (the registered behaviour). Example: get_behaviour_decisions({name:\"CombatPosture\"})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "The registered behaviour name"),
            },
            Notes: new[]
            {
                "Read from the running blob: a node's method key carries its params offset, and the behaviour's default params hold the decision reference there.",
            },
            ExampleArgsJson: "{\"name\":\"CombatPosture\"}",
            ExampleGist: "which nodes of a behaviour run which utility decision"),

        [("GET", "/entities/{networkId}/utility")] = new RouteDoc(
            Tool:    "get_entity_utility",
            Group:   "K — AI behavior traces",
            Summary: "What a unit's utility decisions chose, and why (CE-3069) — the decisions on EVERY brain unit, always (CE-1319); the per-consideration breakdown only while observed.",
            Returns: "{ observed, decisions:[{decision, kind, winner, previousWinner, switchCount, evalCount, margin, hysteresis, ranked:[{option|networkId,name, score}]}], lastPass:{winnerOptionId, winnerScore, margin, options:[{optionIndex, considerations:[{input, raw, curved, weight}]}]} } — scores are AFTER hysteresis.",
            Hint:    "Req: networkId (number). The decisions need no arming; for lastPass (the considerations) arm observe_trace({networkId,on:true}) on the Brain perspective (Scenario on a cluster) and let the sim tick. Example: get_entity_utility({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "⭐ CE-1319 (BV-61): the decision log is on every brain unit, always — so it replays too; observe_trace arms only the BTree/HSM trace and the utility per-consideration trace (lastPass).",
                "One entry per decision the unit ran (up to 4); a ranking's winner is a candidate entity {networkId, name}. observed:false = no brain log on this node (not a brain unit here).",
                "switchCount counts winner changes since the unit's brain started — poll it to see a posture switch.",
                "lastPass is the most recent option decision's per-consideration breakdown; rankings trace only their winner.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read which posture a unit chose and the scores behind it"),

        [("GET", "/entities/{networkId}/weapons")] = new RouteDoc(
            Tool:    "get_entity_weapons",
            Group:   "K — AI behavior traces",
            Summary: "A unit's weapon mounts (owner = mount 0, then mount children) — ammo, cooldown, TKB numbers; with ?target= the WeaponSelection inputs per mount and the chosen mount (CE-3089).",
            Returns: "{ count, mountChildrenEnabled, mounts:[{index, on:owner|child, ammo, maxAmmo, cooldown, turret?, tkb:{range, penetration, damage, muzzleVelocity}|null, inputs?:{hasAmmo, rangeFit, effectiveness, readiness, roundsLeft}}], turrets:[{index, part, azimuthDeg?, elevationDeg?, task?:{mode, mount, epoch, target, point, roundsGranted}, muscle?:{state, failure, epoch, roundsFired}}], choice? }",
            Hint:    "Req: networkId (number); optional target (number, query). Read on the Brain perspective. Example: get_entity_weapons({networkId:1000, target:1002})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("target", "number", false, "Network id of a target to score the mounts against (query)"),
            },
            Notes: new[]
            {
                "choice is the mount the AimAndFire executor fires on Mount=Auto (the posture's fire step): 0 = the owner's primary.",
                "rangeFit is distance / mount range (≈ 1 inside the range in the decision's curve); 10 = unknown.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"target\":1002}",
            ExampleGist: "see which weapon a unit would fire at a target, and why"),

        [("GET", "/entities/{networkId}/reports")] = new RouteDoc(
            Tool:    "get_entity_reports",
            Group:   "K — AI behavior traces",
            Summary: "Reports up the chain of command (CE-2295, CC-12): on a commander its picture — the elements its subordinates reported, each subordinate's last progress / colours / age / overdue, open requests; on any unit the extra requirements its superior set and its outbox (queued, posture, muted).",
            Returns: "{ networkId, now, onBrainNode?, picture?:{elements:[{from, size, count, activity, equipment, at, radius, observedAt, ageSeconds, receivedAt, seq, synthetic}], subordinates:[{networkId, name?, progress, strength, ammo, phase, lastSeq, lastReportAt, ageSeconds, overdue}], requests:[{from, kind, at, receivedAt, seq}]}, requirements?:{pir, ffir, sitrepEverySeconds, pirArea, setBy}, outbox?:{queued:[report + dueAt], nextSeq, posture, muted, mutedUntil} }",
            Hint:    "Req: networkId of a commander (a platoon, a squad). Read on its BRAIN node (Scenario on a cluster). Example: get_entity_reports({networkId:5001})",
            Params: new RouteParam[] { new("networkId", "number", true, "Network entity ID (long)") },
            Notes: new[]
            {
                "The INTERPRETED view; the raw components (CommandPicture, ReportingRequirements, ReportOutbox) are in get_entity, each buffer a plain array (CC-16).",
                "A report ages from observedAt (when the sender saw it), not from its arrival; overdue = nothing heard for 60 s.",
            },
            ExampleArgsJson: "{\"networkId\":5001}",
            ExampleGist: "what has the platoon heard from its squads"),

        [("POST", "/entities/{networkId}/reports")] = new RouteDoc(
            Tool:    "fake_entity_report",
            Group:   "K — AI behavior traces",
            Summary: "A report AS IF this unit's staff sent it to its superior (CE-2295, CC-13) — it enters before the wire, ages from observedAgoSeconds, and the superior's whole decision path runs as for a real one. Marked synthetic: always taken, replaced by the unit's next real report (CC-14).",
            Returns: "{ networkId, sent:true, synthetic:true, report }",
            Hint:    "Req: networkId of the SENDING unit (a squad), kind. Example: fake_entity_report({networkId:5002, kind:'Spot', size:'Team', count:3, at:[200,320,0]})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "The unit the report comes FROM (its superior receives it)"),
                new("kind", "string", true, "Contact · Spot · Sitrep · Lace · Progress · Request"),
                new("subtype", "string", false, "Progress: Set · PhaseLine · Foothold · RoomClear · … · FireLifted · TaskFailed; Request: Casevac · FireSupport · Resupply · Reinforce · Permission"),
                new("count", "number", false, "Spot / Contact: how many (0 = the element is lost); PhaseLine: the line's index"),
                new("size", "string", false, "Individual · Team · SquadSize · PlatoonSize"),
                new("at", "array", false, "[x, y, z] where (default: the unit's own position)"),
                new("strength", "string", false, "Sitrep: Green · Amber · Red · Black"),
                new("observedAgoSeconds", "number", false, "how old the observation already is"),
            },
            Notes: new[] { "Run on any node that holds the unit; BR-3 carries the report to the superior's brain node." },
            ExampleArgsJson: "{\"networkId\":5002,\"kind\":\"Spot\",\"size\":\"Team\",\"count\":3,\"at\":[200,320,0]}",
            ExampleGist: "tell the platoon squad 5002 sees three enemies"),

        [("POST", "/entities/{networkId}/reports/mute")] = new RouteDoc(
            Tool:    "mute_entity_reports",
            Group:   "K — AI behavior traces",
            Summary: "Lost comms (CE-2295, CC-15): the unit's staff sends nothing for `seconds` — CONTACT included; its outbox keeps filling. 0 restores the radio.",
            Returns: "{ networkId, mutedSeconds }",
            Hint:    "Req: networkId, seconds. Run on the unit's BRAIN node (the outbox lives there). Example: mute_entity_reports({networkId:5002, seconds:60})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "The unit whose radio fails"),
                new("seconds", "number", true, "How long (0 = restore)"),
            },
            ExampleArgsJson: "{\"networkId\":5002,\"seconds\":60}",
            ExampleGist: "squad 5002 goes silent for a minute"),

        [("GET", "/squad/reports")] = new RouteDoc(
            Tool:    "get_squad_reports",
            Group:   "K — AI behavior traces",
            Summary: "The reports this node's inbox saw, newest first (CE-2295, PC-11) — folded, stale (a late older one dropped) or not addressed here; a ring of 256 per node.",
            Returns: "{ count, returned, reports:[{serial, seenAt, from, to, originNode, verdict, kind, subtype, at, observedAt, ageSeconds, seq, synthetic, …}] }",
            Hint:    "Optional: last (default 50), unit (from or to). Read on a brain node. Example: get_squad_reports({last:20, unit:5001})",
            Params: new RouteParam[]
            {
                new("last", "number", false, "How many (default 50)"),
                new("unit", "number", false, "Only reports from or to this unit"),
            },
            Notes: new[] { "⛔ Not GET /events: that ring holds 500 events of every type and a fight's shots evict a report within seconds." },
            ExampleArgsJson: "{\"last\":20}",
            ExampleGist: "the last reports up the chain"),

        [("GET", "/entities/{networkId}/squad")] = new RouteDoc(
            Tool:    "get_entity_squad",
            Group:   "K — AI behavior traces",
            Summary: "A squad as its commander sees it — members with their assigned fire targets, and the merged contact pool (CE-3087).",
            Returns: "commander: { role:'commander', memberCount, members:[{slot, networkId, name, assignment:{networkId, name, score, focusFireCount}|null}], lastMergeTick, contactCount, contacts:[{networkId, name | heard:true, threat, position, sources, lastSeenTick}], maneuver:{active, kind, kindName, phase, phaseName, phaseSince, members:[{slot, networkId, name, order:{kind, point, cover, issuedAt, target}|null}], history:[{t, phase, phaseName, event}], gates?:{covering, set}, care?:{casualty, medic, stage, coverPoint}}|null } · member: { role:'member', commander:{networkId, name}, assignment } · otherwise { role:'none' }",
            Hint:    "Req: networkId (number) of a commander or a member. Read on the Brain perspective (Scenario on a cluster). Example: get_entity_squad({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "The assignment is the leader's fire distribution (CE-3088): recomputed after each merge of the pool (≈ 10 Hz), at most 2 members per target.",
                "An assignment is a consideration in the member's own threat ranking, not an order — a hurt member may still break off (its posture).",
                "Heard contacts (no identity) appear in the pool with heard:true and are never assigned.",
                "maneuver (CE-2157) is the squad drill the commander runs: its phase, the last 16 phase changes with the event that moved each (Start, BoundComplete, DefiladeReached, Timeout, End, ...), and the order each member last got. null when no drill ever ran.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read which target each squad member is assigned, what the squad knows, and which drill phase it is in"),

        [("GET", "/entities/{networkId}/sensors")] = new RouteDoc(
            Tool:    "get_entity_sensors",
            Group:   "K — AI behavior traces",
            Summary: "Every sensor of a unit — kind, result family and last answer in its own shape (CE-3072).",
            Returns: "{ count, sensors:[{partId, kind, family:Ranked|Area, templateId, epoch, suspended, origin:tkb|behaviour|other, answer:{ready, count, lastUpdateTick, top:[{entityId, score, position}]} | {ready, count, lastUpdateTick, areas:[{featureId, kind, threat, distanceAlongRoute, center, nearHandle, farHandle}]}, settings? }] }",
            Hint:    "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — the answers land there. Example: get_entity_sensors({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "A Ranked sensor lists its top 5 results; an Area sensor (the danger-area sensor) lists its areas, the next one ahead first.",
                "answer.ready is false until the first answer — an Area answer with count 0 and ready true means a clear route.",
                "settings appear for an Area sensor: which route it watches (OwnMove | Handle | ToPoint) and its corridor.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read which danger areas a unit sees ahead on its route, and how threatened each is"),

        [("GET", "/entities/{networkId}/memory")] = new RouteDoc(
            Tool:    "get_entity_memory",
            Group:   "K — AI behavior traces",
            Summary: "A unit's raw target memory, every slot — heard (anonymous) contacts included (CE-3144).",
            Returns: "{ networkId, hasMemory, count, changeEpoch, anonymousSerial, slots:[{id, anonymous, position:[x,y,z], freshness, radius, lastSeenTick, modalities, sourceClass, alive?, networkId?}] }",
            Hint:    "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — memory is written there. Example: get_entity_memory({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "GET /entities/{id} shows memory through the scenario translator, which drops heard slots and entities it cannot map; this route shows them all.",
                "Slots are sorted freshest first; a heard slot has a negative synthetic id and a radius.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read what a unit remembers — seen and heard contacts, how fresh, and where"),

        [("POST", "/entities/{networkId}/sector")] = new RouteDoc(
            Tool:    "set_entity_sector",
            Group:   "K — AI behavior traces",
            Summary: "Set a man's or a commander's sector — the arc he may choose to shoot into, where he watches, his NoFire areas (CE-2298, DA-26).",
            Returns: "{ networkId, arc:{left,right,minRange,maxRange}|null, watch|null, allowArea|null, noFire, setBy } — or { networkId, forwarded:true } when his brain is elsewhere",
            Hint:    "Req: networkId. Body (all optional): left, right (compass, 0 = north, clockwise), minRange, maxRange, watch (compass), allowArea, noFire:[areaIds]. Empty body withdraws. Example: set_entity_sector({networkId:5301, watch:0})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("left", "number", false, "Arc's left bearing (compass degrees)"),
                new("right", "number", false, "Arc's right bearing (compass degrees)"),
                new("minRange", "number", false, "Arc's min range (m)"),
                new("maxRange", "number", false, "Arc's max range (m); 0 = none"),
                new("watch", "number", false, "Where he looks (compass degrees)"),
                new("allowArea", "number", false, "An area entity's network id he may shoot into"),
                new("noFire", "array", false, "Up to four area entities' network ids no round may end in"),
            },
            Notes: new[]
            {
                "Origin Operator through the one writer (EngagementAreaSystem.Apply): it replaces the whole sector; a higher setter refuses it.",
                "The arc's origin is where he stands now. On a node that does not run his brain the sector is forwarded to the one that does (kind 4) — the answer says forwarded.",
                "His eyes follow it: a holding or fighting man looks at his sector when no threat or sound holds them (Look Direction §3.3a).",
            },
            ExampleArgsJson: "{\"networkId\":5301,\"watch\":0}",
            ExampleGist: "point a sentry north, or give a man his arc of fire"),

        [("GET", "/entities/{networkId}/slots")] = new RouteDoc(
            Tool:    "get_entity_slots",
            Group:   "K — AI behavior traces",
            Summary: "Every behaviour occurrence slot a unit carries — the task's, the SOP's, a paused task's, hosted children's — named and decoded (CE-2288).",
            Returns: "{ networkId, count, slots:[{key, kind, payloadSize, structureHash, owner (Task|Sop|PausedTask|OwnerUnknown), behaviour, role (Params|State|Hsm|Working|HostedChild|RoleUnknown), label, layout, value}] }",
            Hint:    "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — the slots live where the brain runs. Example: get_entity_slots({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "GET /entities/{id} projects only the running task's params; this route lists every slot, including a hosted drill's own block and the SOP's.",
                "A slot stores no owner: each is named by recomputing the keys of the task, the SOP and a paused task; an unmatched slot is listed with owner OwnerUnknown.",
                "value is the slot decoded through its behaviour's layout (params, a hosted child's params, a stateful node's working state); null when no layout is known.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "see a commander's whole brain — the play, the SOP, a paused task and the drill it hosts"),

        // ⭐⭐ CE-3167 — orders and the situation report (docs/DESIGN_Entity_Control.md §4.1, R-270): the AI player's API.
        [("POST", "/entities/{networkId}/order")] = new RouteDoc(
            Tool:    "order_entity",
            Group:   "Z — Entity control (orders)",
            Summary: "Order a unit, as an operator: move to a point, attack an enemy, hold (stop), run any behaviour, or release it to its own SOP. The unit works out HOW (route, cover, weapon). The order outranks its SOP and mission and clears its mission plan; works from any node (forwarded to the unit's brain owner).",
            Returns: "{ networkId, accepted:true, detail, behavior?, params?, note } — 404 unknown unit, 409 killed / other side (side lock), 400 malformed.",
            Hint:    "Req: networkId, kind. Move: {kind:'move', lat,lon} or {x,y} (+ speed, arrivalRadius, roadUse). Attack: {kind:'attack', targetId}. Any behaviour: {kind:'behavior', behavior, params} — discover with list_behaviors({entityId}). Then poll get_entity_order. Example: order_entity({networkId:1000, kind:'move', x:120, y:-40})",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the unit"),
                new("kind", "string", true, "move · attack · hold · stop (= hold) · behavior · release", EnumJson: "[\"move\",\"attack\",\"hold\",\"stop\",\"behavior\",\"release\"]"),
                new("lat", "number", false, "move: target latitude (degrees) — the portable form"),
                new("lon", "number", false, "move: target longitude (degrees)"),
                new("x", "number", false, "move: target easting in THIS node's local metres (converted to lat/lon when the node can)"),
                new("y", "number", false, "move: target northing in this node's local metres"),
                new("speed", "number", false, "move: speed in m/s (default: the unit's)"),
                new("arrivalRadius", "number", false, "move: arrival radius in metres"),
                new("roadUse", "string", false, "move: Never · Neutral · Prefer · StronglyPrefer · InTraffic (a vehicle handed to SUMO, AQ93 R-4023)"),
                new("targetId", "integer", false, "attack: the enemy's network id (get_sitrep lists the contacts your side knows)"),
                new("behavior", "string", false, "behavior: the behaviour name (list_behaviors)"),
                new("params", "object", false, "behavior: its parameters, matching its paramSchema"),
            },
            Notes: new[]
            {
                "An order is origin Operator: the unit's SOP and its mission cannot replace it; a reaction (under fire) may pause it unless its ROE says StayOnTask.",
                "kind 'attack' runs CombatPosture with the target designated and assault: the unit closes in and fights THAT enemy (a nearer threat does not take over) and the order SUCCEEDS when it is down. ROE still applies — on HoldFire it closes in without firing.",
                "The order is resolved on the unit's brain node a frame or two later; get_entity_order reads the result there.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"kind\":\"move\",\"x\":120,\"y\":-40,\"speed\":3}",
            ExampleGist: "send unit 1000 to a point"),

        [("GET", "/entities/{networkId}/order")] = new RouteDoc(
            Tool:    "get_entity_order",
            Group:   "Z — Entity control (orders)",
            Summary: "A unit's operator order: running · paused (a reaction interrupted it) · succeeded · failed · faulted · none — and what it runs now.",
            Returns: "{ networkId, onBrainNode, state, behavior?, params?, since?, interruptedBy?, finishedAt?, fault?, current:{behavior, origin}, task?:{behavior, origin, since, params} }",
            Hint:    "Req: networkId. Read on the unit's brain node (the Scenario perspective on a cluster). Example: get_entity_order({networkId:1000})",
            Params: new RouteParam[] { new("networkId", "integer", true, "Network id of the unit") },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "has unit 1000 arrived?"),

        [("DELETE", "/entities/{networkId}/order")] = new RouteDoc(
            Tool:    "release_entity",
            Group:   "Z — Entity control (orders)",
            Summary: "Release a unit: the operator's order ends and the unit's own SOP decides again.",
            Returns: "{ networkId, accepted:true, detail:'released' }",
            Hint:    "Req: networkId. Example: release_entity({networkId:1000})",
            Params: new RouteParam[] { new("networkId", "integer", true, "Network id of the unit") },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "hand unit 1000 back to its AI"),

        // ⭐ CE-3172 — possession (docs/DESIGN_Entity_Control.md §8b): one station drives one unit, first person.
        [("POST", "/entities/{networkId}/possess")] = new RouteDoc(
            Tool:    "possess_entity",
            Group:   "Z — Entity control (orders)",
            Summary: "Possess a unit as this node's station (first person): its current task pauses and control_entity frames drive its body until release_possession, or until it dies; the paused task then resumes. Another station's hold is taken over.",
            Returns: "{ networkId, accepted:true, detail:'possessing' } — 404 unknown unit, 409 killed / other side.",
            Hint:    "Req: networkId. Then send control_entity frames; read who holds it with get_entity_order (current.origin = Possession). Example: possess_entity({networkId:1000})",
            Params: new RouteParam[] { new("networkId", "integer", true, "Network id of the unit") },
            Notes: new[]
            {
                "While possessed, ordinary orders to the unit are refused (409 Possessed) — release it first.",
                "Possession is a behaviour (OperatorControl) at origin Possession, above Operator: no reaction interrupts it (R-270).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "take unit 1000 in first person"),

        [("DELETE", "/entities/{networkId}/possess")] = new RouteDoc(
            Tool:    "release_possession",
            Group:   "Z — Entity control (orders)",
            Summary: "Let a possessed unit go: it stops, and the task it was doing before resumes.",
            Returns: "{ networkId, accepted:true, detail:'released' } — 409 when another station (or none) holds it.",
            Hint:    "Req: networkId. Example: release_possession({networkId:1000})",
            Params: new RouteParam[] { new("networkId", "integer", true, "Network id of the unit") },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "hand unit 1000 back to its task"),

        [("POST", "/entities/{networkId}/control")] = new RouteDoc(
            Tool:    "control_entity",
            Group:   "Z — Entity control (orders)",
            Summary: "One control frame for the unit this station possesses — a person: forward/back + strafe in its body frame, facing an absolute yaw; a vehicle: throttle (forward; negative brakes, then reverses) + steer. The newest seq wins; keep sending while the controls are held.",
            Returns: "{ networkId, accepted:true, detail:'control' } — 409 when this station does not hold the unit.",
            Hint:    "Req: networkId, seq (rising). Example: control_entity({networkId:1000, seq:1, forward:1, yaw:0})",
            Params: new RouteParam[]
            {
                new("networkId", "integer", true, "Network id of the unit"),
                new("seq", "integer", true, "Frame number, rising — an older frame is ignored"),
                new("forward", "number", false, "-1..1: a person's forward (+) / back (-, at 60 %); a vehicle's throttle (- brakes, then reverses)"),
                new("lateral", "number", false, "-1..1: a person's strafe, + = left (at 60 %)"),
                new("steer", "number", false, "-1..1: a vehicle's steer, + = left"),
                new("yaw", "number", false, "a person's facing — ABSOLUTE world yaw in radians (0 = +X, counter-clockwise)"),
                new("gait", "integer", false, "a person's pace: 0 = run, 1 = walk (half speed)"),
                new("trigger", "boolean", false, "the trigger is held (operator fire — S4d)"),
                new("aim", "array", false, "[x,y,z] where the station aims, world metres (S4d)"),
                new("use", "integer", false, "the USE press counter (0-255, wraps): a new value asks the unit's brain to act on useTarget (CE-1151)"),
                new("useVerb", "integer", false, "which verb of the target's list the use press means (0 = the primary)"),
                new("useTarget", "integer", false, "network id of what the use press acts on (a door, a man who is down, a vehicle, a weapon)"),
            },
            Notes: new[] { "Frames only drive a unit possessed by THIS node's station (possess_entity first).",
                           "A unit held in Riding mode (its human drives a vehicle) takes no frames." },
            ExampleArgsJson: "{\"networkId\":1000,\"seq\":1,\"forward\":1,\"yaw\":0}",
            ExampleGist: "walk unit 1000 forward, facing east"),

        [("GET", "/orders")] = new RouteDoc(
            Tool:    "list_orders",
            Group:   "Z — Entity control (orders)",
            Summary: "The order vocabulary (Move, Attack, Hold …): each order's fields — kind (MapPoint · MapEntity · Value), required, entity-pick presets — and its params schema. entityId narrows it to the orders that unit fulfils.",
            Returns: "{ networkId?, orders:[ { name, gesture, pick, fields:[{name, kind, required, presets?}], paramSchema } ] }",
            Hint:    "Opt: entityId. A required field must be in the order's params or the order is refused. Example: list_orders({entityId:1000})",
            Params: new RouteParam[] { new("entityId", "integer", false, "Only the orders this unit (network id) fulfils") },
            ExampleArgsJson: "{\"entityId\":1000}",
            ExampleGist: "what can unit 1000 be ordered to do, and what does each order need"),

        [("POST", "/orders")] = new RouteDoc(
            Tool:    "order_entities",
            Group:   "Z — Entity control (orders)",
            Summary: "Several orders in one call (a squad, a turn's worth of moves). Each is answered on its own; a refusal does not stop the rest.",
            Returns: "{ results:[ { networkId, accepted, detail, behavior?, params? } ] }",
            Hint:    "Req: orders — an array of order_entity bodies, each with its networkId.",
            Params: new RouteParam[]
            {
                new("orders", "array", true, "[{ networkId, kind, … }] — the order_entity fields per unit",
                    ItemsJson: "{\"type\":\"object\",\"properties\":{\"networkId\":{\"type\":\"integer\"},\"kind\":{\"type\":\"string\"}}}"),
            },
            ExampleArgsJson: "{\"orders\":[{\"networkId\":1000,\"kind\":\"hold\"},{\"networkId\":1001,\"kind\":\"move\",\"x\":50,\"y\":10}]}",
            ExampleGist: "hold one unit, move another"),

        [("POST", "/orders/side-lock")] = new RouteDoc(
            Tool:    "lock_side",
            Group:   "Z — Entity control (orders)",
            Summary: "Lock this API session to one side: orders to any other side's units are refused, and the sitrep defaults to it. side:null unlocks.",
            Returns: "{ side }",
            Hint:    "Req: side (Friend · Hostile · Neutral …, or null). Example: lock_side({side:'Friend'})",
            Params: new RouteParam[] { new("side", "string", false, "The side to play, or null to unlock") },
            ExampleArgsJson: "{\"side\":\"Friend\"}",
            ExampleGist: "play the Friend side"),

        [("GET", "/sitrep")] = new RouteDoc(
            Tool:    "get_sitrep",
            Group:   "Z — Entity control (orders)",
            Summary: "The situation as ONE side knows it: its units (position, health, ammo, what each is doing) and the contacts its units remember (fog of war; a heard contact has no id). truth=true lists every other unit instead.",
            Returns: "{ side, truth, units:[{networkId, name?, position, health, killed, ammo?, doing}], contacts:[{networkId?, heard?, position, freshness, radius?, by, alive?}] }",
            Hint:    "Opt: side (defaults to the locked side), truth. Read on the brain node (memory lives there). Example: get_sitrep({side:'Friend'})",
            Params: new RouteParam[]
            {
                new("side", "string", false, "Friend · Hostile · Neutral … (defaults to the side lock)"),
                new("truth", "boolean", false, "true = every other unit, not only the known ones (debugging)"),
            },
            ExampleArgsJson: "{\"side\":\"Friend\"}",
            ExampleGist: "what does the Friend side see?"),

        [("GET", "/attributes/schema")] = new RouteDoc(
            Tool:    "get_attributes_schema",
            Group:   "L — Mutation / fault injection",
            Summary: "Return all patchable attribute paths and their JSON Schema.",
            Returns: "{ registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, Position.X/Y/Z (local ENU metres), Heading, …).",
            Hint:    "No params. Example: get_attributes_schema({})",
            Notes: new[]
            {
                "Use patch_attribute to apply a patch using these paths.",
                "Paths not in registeredPaths are silently ignored by patch_attribute.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "discover patchable attribute paths before calling patch_attribute"),

        [("POST", "/entities/{networkId}/attribute")] = new RouteDoc(
            Tool:    "patch_attribute",
            Group:   "L — Mutation / fault injection",
            Summary: "Apply a JSON attribute patch to an entity — locally where this node owns it, by request to the owner where it does not.",
            Returns: "Updated entity dump on success, plus write:{route, appliedComponents, requestedComponents}; route is direct, requested or noMatch.",
            Hint:    "Req: networkId (number), patchJson (object {\"Name\":\"Alpha\"} or JSON string). Example: patch_attribute({networkId:1000,patchJson:{Name:\"Alpha\"}})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("patchJson", null, true, "Patch as a JSON object {\"Name\":\"Alpha\"} or as a JSON string"),
            },
            Notes: new[]
            {
                "Unregistered keys are silently ignored (no error) — write.route is then noMatch.",
                "CE-3003: a key on a component this node does NOT own is sent to its owner as an UpdateEntityAttributeRequest "
              + "(the JSON arm the owner already applies) — write.route is requested, and the change is in the OWNER's world, "
              + "not yet in this dump: read it back from the owner's perspective after a tick. Components this node owns in "
              + "the same patch are applied here.",
                "CE-3003 / CE-191: a key on a component this node does not own and no network descriptor carries (a node-local "
              + "component, or a networkless host) has no owner to ask — the route REFUSES with 400 instead of answering ok.",
                "patchJson may be a nested JSON object like {\"Name\":\"Alpha\"} or a JSON string.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"patchJson\":{\"Name\":\"Alpha\"}}",
            ExampleGist: "rename entity 1000 to Alpha"),

        [("POST", "/entities/create-request")] = new RouteDoc(
            Tool:    "create_entity_request",
            Group:   "B — Queries",
            Summary: "Create an entity THROUGH the request path (routing + auto-takeover grant).",
            Returns: "{ networkId, ... } for the created entity, or a 400 on a malformed body.",
            Hint:    "Req: tkbType (long). Unlike spawn_entity this goes through the create-request pipeline. Example: create_entity_request({tkbType:123, ownerNodeId:1})",
            Params: new RouteParam[]
            {
                new("tkbType", "integer", true, "TKB entity type id (long)"),
                new("ownerNodeId", "integer", false, "This node's id ⇒ create + own locally; 0 ⇒ forward to the broadcast arbiter", DefaultJson: "0"),
                new("transform", "object", false, "Optional initial transform"),
                new("attributesJson", "string", false, "Optional initial attributes as a JSON string"),
            },
            Notes: new[]
            {
                "CE-271 seam ⑤ / CE-515 ③: the create-request pipeline (routing + ownership grants). POST /entities/spawn "
              + "now uses the same pipeline; the only difference is ownerNodeId:0 — here 'forward to the arbiter', "
              + "there 'this node'.",
            },
            ExampleArgsJson: "{\"tkbType\":123,\"ownerNodeId\":1}",
            ExampleGist: "create an entity through the create-request pipeline, owned by node 1"),

        [("GET", "/entities/{networkId}/ownership")] = new RouteDoc(
            Tool:    "get_entity_ownership",
            Group:   "B — Queries",
            Summary: "Per-descriptor network ownership of one entity (NED nodes only).",
            Returns: "{ networkId, primaryOwnerId, localNodeId, descriptors:[{ descriptorTypeId, isMaster, ownedByThisNode, ownerNodeId, components:[..] }] }.",
            Hint:    "Req: networkId (number/long). 503 on a host with no NED transport (editor/AllInOne). Example: get_entity_ownership({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
            },
            Notes: new[]
            {
                "Ownership is DESCRIPTOR-level (a NED network concept), not component-level: each descriptorTypeId "
              + "binds a set of components, and one component may belong to several descriptors. isMaster marks the "
              + "EntityMaster descriptor whose owner is the entity's primary/save owner.",
                "ownedByThisNode is the authority fact this node acts on; ownerNodeId is the explicit per-descriptor "
              + "override when one exists (null ⇒ follows the primary owner).",
                "VERIFICATION SURFACE for transfer_entity_ownership: read ownership, POST the transfer, read again.",
                "PERSPECTIVE-SCOPED on --mode all: reports the ACTIVE perspective node's view. Read on both the "
              + "giving and receiving node to confirm a transfer landed.",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "read which node owns each of an entity's descriptors"),

        [("POST", "/entities/{networkId}/ownership/transfer")] = new RouteDoc(
            Tool:    "transfer_entity_ownership",
            Group:   "B — Queries",
            Summary: "Hand an entity (or a subset of its descriptors) to another node (NED nodes only).",
            Returns: "{ requested:true, networkId, newOwnerNodeId, scope, note } — fire-and-forget; verify with GET .../ownership.",
            Hint:    "Req: networkId (path), newOwnerNodeId (int). scope defaults AllOwnedByThisNode. 503 on a host with no NED transport. Example: transfer_entity_ownership({networkId:1000, newOwnerNodeId:2, scope:\"MasterOnly\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("newOwnerNodeId", "integer", true, "The node to hand the descriptors to"),
                new("scope", "string", false, "Which descriptors move", DefaultJson: "\"AllOwnedByThisNode\"",
                    EnumJson: "[\"MasterOnly\",\"AllOwnedByThisNode\",\"SpecificDescriptors\"]"),
                new("descriptors", "array", false, "NED descriptor type ids (required only for SpecificDescriptors)",
                    ItemsJson: "{\"type\":\"number\"}"),
            },
            Notes: new[]
            {
                "DESCRIPTOR-level and NED-initiated (ruling 2026-09-15): 'descriptors' are network descriptor type "
              + "ids (get them from get_entity_ownership), never ECS component types.",
                "Only descriptors THIS node owns move — so on a native cluster AllOwnedByThisNode hands off the "
              + "brain's descriptors but leaves the muscle-owned world-position descriptor where it is.",
                "Save ownership (primaryOwnerId) moves IFF EntityMaster is in the set (MasterOnly and "
              + "AllOwnedByThisNode include it; a SpecificDescriptors set that omits master leaves it here).",
                "The old owner stops publishing without disposing (entity never blinks out); the new owner "
              + "confirms by re-publishing EntityMaster. Verify with get_entity_ownership on the new owner.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"newOwnerNodeId\":2,\"scope\":\"AllOwnedByThisNode\"}",
            ExampleGist: "hand entity 1000 to node 2"),

        [("POST", "/entities/{networkId}/component")] = new RouteDoc(
            Tool:    "edit_component",
            Group:   "L — Mutation / fault injection",
            Summary: "StructEdit escape hatch for arbitrary component fields.",
            Returns: "Updated entity component state. Invalid values → 400, component unchanged.",
            Hint:    "Req: networkId (number), componentType (string from list_component_types), patch (object). Example: edit_component({networkId:1000,componentType:\"SimTransform\",patch:{...}})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("componentType", "string", true, "ECS component type name (e.g. \"EntityInfo\", \"SimTransform\")"),
                new("patch", "object", true, "JSON object with field names and new values to apply to the component"),
            },
            Notes: new[]
            {
                "Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, and writes the result back to ECS.",
                "Invalid values → 400, component unchanged.",
                "CE-3003: a component another node owns (a network descriptor carries it and this node holds no authority) "
              + "is REFUSED with 400 — writing it here would change only this node's replica, which the owner overwrites. "
              + "Use patch_attribute (it asks the owner) or select the owner's perspective (get_entity_ownership names it).",
                "For fields registered in the attribute schema, prefer patch_attribute.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"componentType\":\"SimTransform\",\"patch\":{\"Position\":{\"X\":999,\"Y\":0,\"Z\":0}}}",
            ExampleGist: "set SimTransform Position.X to 999 for entity 1000"),

        [("GET", "/tkb/types")] = new RouteDoc(
            Tool:    "list_entity_types",
            Group:   "M (TKB) — Entity-type catalog",
            Summary: "List entity types (TKB templates) with id, name, category, disType.",
            Returns: "[{tkbType, name, categoryPath, disType}]",
            Hint:    "No required params. Optional: category (string). Example: list_entity_types({})",
            Params: new RouteParam[]
            {
                new("category", "string", false, "Filter by category path"),
            },
            ExampleArgsJson: "{\"category\":\"Vehicle\"}",
            ExampleGist: "list all TKB types in the Vehicle category"),

        [("GET", "/tkb/types/{tkbType}")] = new RouteDoc(
            Tool:    "get_entity_type",
            Group:   "M (TKB) — Entity-type catalog",
            Summary: "Full TKB descriptor: mandatory components, child blueprints, DIS type, and descriptor DTOs.",
            Returns: "Full TKB descriptor including mandatory components, child blueprints, descriptors. No spawn.",
            Hint:    "Req: tkbType (number/long from list_entity_types). Example: get_entity_type({tkbType:1101})",
            Params: new RouteParam[]
            {
                new("tkbType", "number", true, "TKB type ID (long)"),
            },
            ExampleArgsJson: "{\"tkbType\":1101}",
            ExampleGist: "inspect TKB descriptor for type 1101"),

        [("GET", "/tkb/resolve")] = new RouteDoc(
            Tool:    "resolve_entity_type_parameters",
            Group:   "M (TKB) — Entity-type catalog",
            Summary: "Every combat/perception parameter of a TKB type with its value and where it came from (buildings Stage 0) — or, with ammo/weapon, the generated reference library's launcher × ammo pair (tuning T-1).",
            Returns: "type: { tkbType, name, disType, engineFallbacks, parameters:[{name, value, provenance:Explicit|Generated|ReferenceByDis|ReferenceByName|EngineFallback|NotApplicable, source}] } · ammo: { ammo, weapon, found, weaponMatched, muzzleSpeed, penetrationMm, penetrationFormula, damage, damageFormula, driving } · library=1: { ammo:[…], weapons:[…] }",
            Hint:    "Req: type (number — tkbType from list_entity_types), OR ammo (+ optional weapon), OR library=1. Example: resolve_entity_type_parameters({type:100}) · resolve_entity_type_parameters({ammo:'12.7x99 AP', weapon:'M2HB'})",
            Params: new RouteParam[]
            {
                new("type", "number", false, "TKB type ID (long)"),
                new("ammo", "string", false, "reference-library ammo name (e.g. '5.56x45 ball') — answers the generated pair instead of a type"),
                new("weapon", "string", false, "reference-library weapon name (e.g. 'M4_Carbine'); omitted or not firing this ammo ⇒ the ammo's generic profile"),
                new("library", "string", false, "any value with no type/ammo: list the whole reference library"),
            },
            Notes: new[]
            {
                "The values are the ones the simulation uses: the translators and the fire chain call the same resolver rules.",
                "Generated = a builder derived it by formula (source names the formula and its inputs); EngineFallback = the type said nothing and an engine default applies.",
                "engineFallbacks counts the parameters on engine defaults — the place to look when a type behaves 'generically'.",
                "ReferenceByName = the generated reference library (tuning T-1): a mount's loaded ammo named by its TKB type's name, when the TKB states no penetration; the formula and its inputs are in source.",
            },
            ExampleArgsJson: "{\"type\":100}",
            ExampleGist: "see which of the M1's numbers are stated, derived or engine defaults"),

        [("GET", "/terrain/levels")] = new RouteDoc(
            Tool:    "get_terrain_levels",
            Group:   "N — World / coordinates",
            Summary: "The terrain levels at a point — ground = level 0, storeys/slabs/roofs above, basements below (buildings Stage 1).",
            Returns: "{ terrain, x, y, levels:[{level, z, kind:ground|above|below}] } — or { terrain:null } when no terrain is resident",
            Hint:    "Req: x, y (local metres). Example: get_terrain_levels({x:102, y:104})",
            Params: new RouteParam[]
            {
                new("x", "number", true, "Local X (m, east)"),
                new("y", "number", true, "Local Y (m, north)"),
            },
            Notes: new[]
            {
                "The same levels Add Entity's SpawnHeight resolves (TerrainWorld.SurfacesAt): surfaces within 0.3 m of the ground merge into level 0.",
            },
            ExampleArgsJson: "{\"x\":102,\"y\":104}",
            ExampleGist: "list the floors a unit could be placed on inside a building"),

        [("GET", "/terrain/query")] = new RouteDoc(
            Tool:    "query_terrain",
            Group:   "N — World / coordinates",
            Summary: "Dry-run terrain trace between two points — every crossed wall/fence/floor with its material and, per purpose, its transmittance (sight) or ballistic resistance and the round's chance through it (fire).",
            Returns: "sight: { terrain, purpose, transmittance, seesThrough, threshold, length, crossed:[{along, kind:panel|prism|slab|ramp, label, material, transmittance, building, storey}], note } · fire: { terrain, purpose, penetration, length, stopped, stopAlong, arrivingDamage, arrivingPenetration, crossed:[{along, kind, label, material, pathMetres, resistanceMmRha, roundPenetrationMm, chance, passes, building, storey}], note }",
            Hint:    "Req: from, to ('x,y,z' local metres). Optional: purpose (sight|fire), penetration (mm RHA, fire), damage (fire). Example: query_terrain({from:'102,90,1.6', to:'102,104,1.6', purpose:'fire', penetration:'5'})",
            Params: new RouteParam[]
            {
                new("from", "string", true, "Start 'x,y,z' (local metres)"),
                new("to", "string", true, "End 'x,y,z' (local metres)"),
                new("purpose", "string", false, "sight (default) or fire (sound/fragment/blast arrive with their solvers)"),
                new("penetration", "number", false, "fire: the round's penetration, mm RHA (0 = unknown round — the engine fallback meets the terrain)"),
                new("damage", "number", false, "fire: the round's damage (0 = the flat default)"),
            },
            Notes: new[]
            {
                "Transmittance multiplies along the line (chain-link 0.85, hedge 0.3, solid walls 0); seesThrough = transmittance >= 0.5.",
                "Perception uses the same rule (TerrainWorld.SegmentBlocked = transmittance < 0.5), so this answer is what a sensor sees.",
                "fire (R-217): resistance = material mm RHA/m × the path inside the piece; chance = ArmorModel.PenetrationChance(round, resistance); damage multiplies by each chance; a crossing the round cannot pass stops it — exactly what bullets do.",
            },
            ExampleArgsJson: "{\"from\":\"102,90,1.6\",\"to\":\"102,104,1.6\"}",
            ExampleGist: "check whether a window lets a soldier outside see into a room"),

        [("GET", "/perception/hearing")] = new RouteDoc(
            Tool:    "explain_hearing",
            Group:   "K — AI behavior traces",
            Summary: "What one unit could hear in the last seconds and why not, as this node's hearing sensor decides it — each sound's verdict (heard / beyond its carry / beyond his ear / walls or corners / his own side), the straight line's loss, the way round through doorways, windows, stairs and street corners, and where it seems to come from (AQ106 L).",
            Returns: "{ listener, seconds, count, heard, sounds:[{source, kind, class, at, ageSeconds, carry, distance, effectiveCarry, verdict, via, heardFrom, straightDb, crossings, pathMetres, pathDb, openings, corners, route:[[x,y,z]]}], note }",
            Hint:    "Req: listener (network id); seconds (default 10). Example: explain_hearing({listener:1001, seconds:5})",
            Params: new RouteParam[]
            {
                new("listener", "number", true, "the hearing unit's network id"),
                new("seconds", "number", false, "how far back the short sounds (shots, bursts, doors) are listed; default 10"),
            },
            Notes: new[]
            {
                "A dry run of the SAME sensor (its ears and its one verdict, AcousticSensorGenerator.Judge) — it cannot disagree with what was heard; it moves no cursor.",
                "Answers where hearing runs (the Perception node: SimHost, the editor); 404 when the listener is not on this node or has no acoustic sensor here.",
                "The moving and shouting bodies are those near him NOW; the short sounds are the ring's of the last `seconds`.",
            },
            ExampleArgsJson: "{\"listener\":1001,\"seconds\":5}",
            ExampleGist: "find out why the defender did not hear the attacker on the stairs"),

        [("GET", "/perception/los")] = new RouteDoc(
            Tool:    "explain_line_of_sight",
            Group:   "K — AI behavior traces",
            Summary: "Why one unit does or does not see another, as this node's perception decides it — eye and aim heights, stances, every terrain crossing with its transmittance, and any entity in the way (tuning T-4).",
            Returns: "{ observer, target, visible, verdict, eye, eyeHeight, observerStance, targetStance, threshold, points:[{height, aim, clear, verdict, terrainTransmittance, crossed:[{along, kind, label, material, transmittance, building, storey}], blockingEntity}], note }",
            Hint:    "Req: observer, target (network ids). Example: explain_line_of_sight({observer:1001, target:1002})",
            Params: new RouteParam[]
            {
                new("observer", "number", true, "the seeing unit's network id"),
                new("target", "number", true, "the seen unit's network id"),
            },
            Notes: new[]
            {
                "A dry run of the SAME strategy perception composes (TerrainWorldLosStrategy.ForLiveWorld) — it cannot disagree with it.",
                "Answers where perception runs with the entities resident (SimHost, Editor); 404 when either unit is not on this node.",
                "Stage 4: the target is SEEN when ANY body point (per its logical stance) has a clear line; every point is listed with its own verdict.",
            },
            ExampleArgsJson: "{\"observer\":1001,\"target\":1002}",
            ExampleGist: "find out which wall hides the enemy from the rifleman"),

        [("GET", "/combat/detonations")] = new RouteDoc(
            Tool:    "get_combat_detonations",
            Group:   "K — AI behavior traces",
            Summary: "The last warhead bursts on this node (grenades, mortars, HE) — the warhead and where its numbers came from, every entity in reach with its stance, fragment exposure and blast barrier/shadow, the damage each effect did, and the doors breached (buildings Stage 6).",
            Returns: "{ count, returned, detonations:[{seq, tick, shooter, struck, burst, ammo, warhead, warheadSource, fragmentRadius, blastInjuryRadius, effects:[{entity, entityIndex, stance, bodyPoints, distance, fragmentExposure, fragmentFalloff, fragmentArmourChance, fragmentDamage, blastFalloff, blastBarrier, blastShadow, blastDamage, totalDamage, shieldedBy, at}], doorsBreached:[key], rays:[{to, transmission, stopAt}]}] } — newest first",
            Hint:    "Optional: last (default 10, ring of 64). Example: get_combat_detonations({last:3})",
            Params: new RouteParam[]
            {
                new("last", "number", false, "how many bursts (newest first; default 10, ring of 64)"),
            },
            Notes: new[]
            {
                "Written by AreaEffectSystem as it decides each burst — never recomputed. Only bursts of a munition WITH a warhead are recorded (a rifle round has none).",
                "fragmentExposure = the mean over the target's body points (its stance) of the fragments through the terrain and past any body or vehicle; blastBarrier = what slabs/walls/shut doors let through; blastShadow = the diffraction factor behind an obstacle taller than the target.",
                "The entity the round struck is listed too (it is at the burst); its direct hit is a separate damage event.",
                "rays = every fragment line the map's blast layer draws (CE-3117): the body point, the fragment transmission 0..1, and the first obstacle on the way (a terrain piece where the fragments enter it, or the point nearest a blocking collider), null for a clear line.",
            },
            ExampleArgsJson: "{\"last\":3}",
            ExampleGist: "find out why the prone soldier behind the wall survived the grenade"),

        [("GET", "/combat/shots")] = new RouteDoc(
            Tool:    "get_combat_shots",
            Group:   "K — AI behavior traces",
            Summary: "The last rounds fired on this node — the inputs each was fired with (penetration and its provenance, AQ85 sigma/deflection) and why it ended (hit, stopped by a wall, expired), with every terrain crossing (tuning T-4).",
            Returns: "{ count, returned, shots:[{seq, tick, endTick, shooter, target, weaponIndex, muzzle, aim, ordinal, sigmaRad, deflectionRad, penetration, penetrationSource, damage, outcome:InFlight|Hit|StoppedByTerrain|Expired, end, hit, arrivingDamage, arrivingPenetration, raisedAt, firedAt, tripSeconds, caughtUpSeconds, crossings:[{kind, label, material, resistanceMmRha, roundPenetrationMm, chance, passed, at}]}] } — newest first; raisedAt/tripSeconds (CE-2180): the brain's synced sim time and the trip to this node (null when not stamped); caughtUpSeconds: how far a bomb was flown forward to start from its release",
            Hint:    "Optional: last (default 20), shooter, target (network ids). Example: get_combat_shots({last:10, shooter:1001})",
            Params: new RouteParam[]
            {
                new("last", "number", false, "how many records (newest first; default 20, ring of 256)"),
                new("shooter", "number", false, "only rounds fired by this network id"),
                new("target", "number", false, "only rounds fired at this network id"),
            },
            Notes: new[]
            {
                "Written by the systems that decide each round (FireProcessing, Ballistics, HitResolution) — never recomputed, so it cannot disagree with what happened.",
                "Served where the fire chain runs (SimHost, Editor); elsewhere it says no round was fired on this node.",
                "A round that hit a unit lists only the crossings before the unit; arrivingDamage is what the damage step received.",
            },
            ExampleArgsJson: "{\"last\":10}",
            ExampleGist: "find out why a rifleman's rounds never hurt the target behind the wall"),

        [("GET", "/doors")] = new RouteDoc(
            Tool:    "list_doors",
            Group:   "N — World / coordinates",
            Summary: "The doors the resident terrain defines, by terrain-object key, with their live state (what sight and fire see) and the door entity that stands for each (buildings Stage 5b).",
            Returns: "{ terrain, count, doors:[{key, state:Open|Closed|Locked|Destroyed, initial, source, x, y, sillZ, building, storey, runtimeId, entityState}] }",
            Hint:    "No params. Example: list_doors({})",
            Params: new RouteParam[] { },
            Notes: new[]
            {
                "key = '<terrain>/<building>/<doorId>', a string — never a network id. runtimeId is the door ENTITY's network id (created by the scenario load), null before it exists.",
                "state = the terrain's live state; entityState = the door entity's replicated DoorState. They differ only for the frame before the mirror runs.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "see which doors of the range are locked"),

        [("POST", "/entities/{networkId}/focus")] = new RouteDoc(
            Tool:    "focus_entity",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "Pan and zoom the map canvas to an entity. MANUAL-VERIFY: camera move requires windowed session.",
            Returns: "{ focused: true } on success.",
            Hint:    "Req: networkId (number). Example: focus_entity({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID to center the view on"),
            },
            Notes: new[]
            {
                "Publishes CenterOnEntityCommand (headless-verifiable via event history).",
                "The actual camera move only occurs in a windowed session (MANUAL-VERIFY).",
            },
            ExampleArgsJson: "{\"networkId\":1000}",
            ExampleGist: "center editor camera on entity 1000",
            ManualVerify: true),

        [("POST", "/annotations")] = new RouteDoc(
            Tool:    "add_annotation",
            Group:   "O — Manual-assist (focus / annotations)",
            Summary: "Draw a debug primitive (sphere, anchor, or line) in the gizmo buffer. MANUAL-VERIFY: gizmo render requires windowed session.",
            Returns: "{ added: true, primitiveIndex, bufferCount } on success.",
            Hint:    "Req: type (\"sphere\"|\"anchor\"|\"line\"). For sphere: x,y,z,radius. For line: from:{x,y,z},to:{x,y,z}. Example: add_annotation({type:\"sphere\",x:0,y:0,z:0,radius:5})",
            Params: new RouteParam[]
            {
                new("type", "string", true, "Annotation type",
                    EnumJson: "[\"sphere\",\"anchor\",\"line\"]"),
                new("networkId", "number", false, "Entity network ID (anchor only)"),
                new("x", "number", false, "World X coordinate"),
                new("y", "number", false, "World Y coordinate"),
                new("z", "number", false, "World Z coordinate"),
                new("radius", "number", false, "Sphere radius in metres"),
                new("heading", "number", false, "Heading in degrees (anchor)"),
                new("color", "string", false, "Hex color string e.g. \"#FF0000\""),
                new("from", "object", false, "Line start point {x,y,z}",
                    PropertiesJson: "{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}}"),
                new("to", "object", false, "Line end point {x,y,z}",
                    PropertiesJson: "{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}}"),
            },
            Notes: new[]
            {
                "\"sphere\" — x, y, z, radius (float), optional color (hex \"#RRGGBB\").",
                "\"anchor\" — networkId, x, y, z, optional heading (float).",
                "\"line\" — from:{x,y,z}, to:{x,y,z}, optional color.",
                "The buffer write is headless-verifiable; the actual gizmo render requires a windowed session (MANUAL-VERIFY).",
            },
            ExampleArgsJson: "{\"type\":\"sphere\",\"x\":100,\"y\":0,\"z\":50,\"radius\":10,\"color\":\"#FF4400\"}",
            ExampleGist: "draw a red sphere at (100,0,50) with radius 10",
            ManualVerify: true),

        [("GET", "/world/info")] = new RouteDoc(
            Tool:    "get_world_info",
            Group:   "N — World / coordinates",
            Summary: "World metadata: geo origin, the perception and collider grids (live, placed over the terrain), the resident terrain and the navmesh.",
            Returns: "{ geo:{origin:{lat,lon,alt}}, spatialGrid:{cellSize,originX,originY,width,height,extent}, colliderGrid:{...}, terrain:{name,bounds,groundZ,prisms,walkables,surfaces}|null, navmesh:{provider,baked,version}|null }",
            Hint:    "No params. Example: get_world_info({})",
            Notes: new[]
            {
                "Reads the ACTIVE PERSPECTIVE's world (on --mode all set the perspective first, e.g. SimHost).",
                "terrain is null when no terrain is resident; navmesh is null on a node that composes no navigation solver.",
                "navmesh.baked is false until a terrain's navmesh is published (the provider is then the straight-line fallback).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "get world geo origin and spatial grid extent"),

        [("POST", "/world/geo-to-local")] = new RouteDoc(
            Tool:    "geo_to_local",
            Group:   "N — World / coordinates",
            Summary: "Convert geographic coordinates to local ENU {x,y,z}.",
            Returns: "{ x, y, z, rotation? } — optional rotation if headingDeg was provided.",
            Hint:    "Req: lat, lon, alt (all numbers). Optional: headingDeg (number). Example: geo_to_local({lat:50.0,lon:14.0,alt:200})",
            Params: new RouteParam[]
            {
                new("lat", "number", true, "Latitude (degrees)"),
                new("lon", "number", true, "Longitude (degrees)"),
                new("alt", "number", true, "Altitude (meters)"),
                new("headingDeg", "number", false, "Optional heading (degrees CW from North) → rotation quaternion"),
            },
            Notes: new[]
            {
                "Optional headingDeg → adds rotation quaternion to response.",
            },
            ExampleArgsJson: "{\"lat\":50.0755,\"lon\":14.4378,\"alt\":200}",
            ExampleGist: "convert Prague geo coords to local ECS metres"),

        [("POST", "/world/local-to-geo")] = new RouteDoc(
            Tool:    "local_to_geo",
            Group:   "N — World / coordinates",
            Summary: "Convert local ENU {x,y,z} to geographic coordinates.",
            Returns: "{ lat, lon, alt, headingDeg? } — Heading: North=0°, East=90°.",
            Hint:    "Req: x, y, z (all numbers). Optional: rotation ({x,y,z,w}). Example: local_to_geo({x:100,y:0,z:50})",
            Params: new RouteParam[]
            {
                new("x", "number", true, "Local X (meters East)"),
                new("y", "number", true, "Local Y (meters Up)"),
                new("z", "number", true, "Local Z (meters North)"),
                new("rotation", "object", false, "Optional quaternion {x,y,z,w} → headingDeg in response",
                    PropertiesJson: "{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"},\"w\":{\"type\":\"number\"}}"),
            },
            Notes: new[]
            {
                "Optional rotation quaternion {x,y,z,w} → adds headingDeg to response.",
                "Heading convention: North=0°, East=90°.",
            },
            ExampleArgsJson: "{\"x\":100,\"y\":0,\"z\":50}",
            ExampleGist: "convert local ECS position (100,0,50) to geographic coords"),

        // ⭐⭐ DOCUMENTED, DELIBERATELY NOT A TOOL. The endpoint is real and reachable; it is simply the same
        //    operation as continue_from_breakpoint({step:true}), and shipping two tools for one action is how
        //    an agent-facing surface becomes ambiguous. ⛔ Without NotATool the "every route is documented"
        //    rail could only be satisfied by inventing a tool nobody wants.
        [("POST", "/breakpoints/step")] = new RouteDoc(
            Tool:    "(none — use continue_from_breakpoint with step:true)",
            Group:   "G — Breakpoints",
            Summary: "Resume the debugger by exactly one step. Identical to POST /breakpoints/continue {step:true}.",
            Returns: "{ wasPaused, action, isPaused, note }",
            Hint:    "Use continue_from_breakpoint({step:true}) instead — this route exists but is not exposed as a separate tool.",
            NotATool: true),
    };
    }
}
