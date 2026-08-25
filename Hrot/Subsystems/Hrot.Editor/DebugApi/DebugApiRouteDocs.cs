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
            Returns: "{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }",
            Hint:    "No params. Example: get_status({})",
            Notes: new[]
            {
                "Use this to verify the runner is alive and check current run state before driving the sim.",
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
            Returns: "{ assetId, name, kind, status, sourceFilePath, note }",
            Hint:    "Req: kind, name. Optional: path (subfolder). Example: create_asset({kind:\"BTree\",name:\"Patrol\"})",
            Params: new RouteParam[]
            {
                new("kind", "string", true, "BTree | Hsm | Blueprint"),
                new("name", "string", true, "Asset name"),
                new("path", "string", false, "Subfolder relative to the kind's asset root (default: the root)"),
            },
            Notes: new[]
            {
                "It runs the same per-kind INewAssetService the New-Asset dialog runs, writes the file and refreshes the catalog — so the result appears in list_assets by the same rebuild a dialog-created asset does.",
                "The new asset is opened as a document, so you can author it immediately with read_asset_graph and the graph tools.",
                "A host that composes no create path answers 503 explaining that EDITING an existing asset does not need it.",
            },
            ExampleArgsJson: "{\"kind\":\"BTree\",\"name\":\"PatrolTree\"}",
            ExampleGist: "create a new behaviour tree asset"),

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
            },
            ExampleArgsJson: "{}",
            ExampleGist: "pause the running simulation"),

        [("POST", "/sim/step")] = new RouteDoc(
            Tool:    "step",
            Group:   "D — Sim / preview / time",
            Summary: "Advance simulation by N discrete steps. Only meaningful in preview.",
            Returns: "ok:true envelope.",
            Hint:    "No required params. Optional: count (number, def 1). Example: step({count:5})",
            Params: new RouteParam[]
            {
                new("count", "number", false, "Number of steps to advance (default 1)",
                    DefaultJson: "1"),
            },
            Notes: new[]
            {
                "Only advances time when inPreview==true. In Edit state this is a no-op.",
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
            Summary: "Enter preview mode. Snapshots the world (revertible via stop_preview).",
            Returns: "ok:true envelope.",
            Hint:    "No required params. Optional: startPaused (bool). Example: enter_preview({startPaused:true})",
            Params: new RouteParam[]
            {
                new("startPaused", "boolean", false, "Start preview in paused state"),
            },
            Notes: new[]
            {
                "Snapshots the world; stop_preview rewinds to this snapshot.",
                "Single preview slot — mutually exclusive with checkpoint and start_recording{preview}.",
            },
            ExampleArgsJson: "{\"startPaused\":true}",
            ExampleGist: "enter preview paused for deterministic step-based control"),

        [("POST", "/preview/exit")] = new RouteDoc(
            Tool:    "stop_preview",
            Group:   "D — Sim / preview / time",
            Summary: "Exit preview mode; rewinds to the pre-preview snapshot.",
            Returns: "ok:true envelope.",
            Hint:    "No params. Example: stop_preview({})",
            Notes: new[]
            {
                "Rewinds all changes made during preview back to the snapshot taken at enter_preview.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "exit preview and revert all changes since entering preview"),

        [("POST", "/scenario/load/edit")] = new RouteDoc(
            Tool:    "load_scenario_edit",
            Group:   "E — Scenario",
            Summary: "Load a scenario for AUTHORING (Edit state), cluster-wide.",
            Returns: "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.",
            Hint:    "Req: name (string). Optional: waitForReady (bool, use true). Example: load_scenario_edit({name:\"test-move\",waitForReady:true})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Scenario name (relative path)"),
                new("waitForReady", "boolean", false, "Wait for the cluster to reach OperatingEdit before returning",
                    DefaultJson: "false"),
            },
            Notes: new[]
            {
                "Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).",
                "Edit state freezes sim time — nothing ticks until enter_preview or play.",
                "In --mode all this load is PARTIAL: CGF has no edit-load handler yet, so SimHost loads and CGF does not. Use load_scenario_live when every node must hold the world.",
            },
            ExampleArgsJson: "{\"name\":\"test-move\",\"waitForReady\":true}",
            ExampleGist: "load test-move for authoring and wait for ready"),

        [("POST", "/scenario/load/live")] = new RouteDoc(
            Tool:    "load_scenario_live",
            Group:   "E — Scenario",
            Summary: "Load a scenario for RUNNING (Live state), cluster-wide, on any host.",
            Returns: "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.",
            Hint:    "Req: name (string). Optional: waitForReady (bool, use true). Example: load_scenario_live({name:\"test-move\",waitForReady:true})",
            Params: new RouteParam[]
            {
                new("name", "string", true, "Scenario name (relative path)"),
                new("waitForReady", "boolean", false, "Wait for the cluster to reach OperatingLive before returning",
                    DefaultJson: "false"),
            },
            Notes: new[]
            {
                "Set waitForReady:true to block until the cluster reaches OperatingLive (recommended).",
                "Every host has live-load handlers, so this is the mode that loads on ALL nodes — use it when the world must be the same everywhere.",
                "A live load starts a new exercise run (a fresh ExerciseId), which is what recording and replay key off.",
            },
            ExampleArgsJson: "{\"name\":\"test-move\",\"waitForReady\":true}",
            ExampleGist: "load test-move live across the cluster and wait for ready"),

        [("POST", "/scenario/save")] = new RouteDoc(
            Tool:    "save_scenario",
            Group:   "E — Scenario",
            Summary: "Save the current authored world as a scenario.",
            Returns: "ok:true envelope.",
            Hint:    "Req: name (string). Example: save_scenario({name:\"my-scenario\"})",
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
            Hint:    "Req: eventType (string from list_commands). Optional: payload (object), wait (bool). Example: send_entity_command({eventType:\"MissionControlIntent\",payload:{}})",
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
            },
            ExampleArgsJson: "{\"eventType\":\"MissionControlIntent\",\"payload\":{\"targetId\":1000},\"wait\":false}",
            ExampleGist: "publish MissionControlIntent event"),

        [("POST", "/entities/spawn")] = new RouteDoc(
            Tool:    "spawn_entity",
            Group:   "F — Commands, discovery, spawn",
            Summary: "Spawn an entity from a TKB type.",
            Returns: "ok:true envelope. Spawn is processed on the next tick (step to realize it).",
            Hint:    "Req: tkbType (number/long from list_entity_types). Optional: transform ({position,rotation}), components (array), attributesJson (string). Example: spawn_entity({tkbType:1001})",
            Params: new RouteParam[]
            {
                new("tkbType", "number", true, "TKB type ID (long)"),
                new("transform", "object", false, "Transform: { position: {x,y,z}, rotation: {x,y,z,w} }"),
                new("components", "array", false, "Additional component overrides"),
                new("attributesJson", "string", false, "JSON string of attribute overrides (JsonAttributeCompiler patch)"),
            },
            Notes: new[]
            {
                "Spawn is queued and processed on the next tick — call step to realize it.",
                "Use list_entity_types to discover valid tkbType values.",
            },
            ExampleArgsJson: "{\"tkbType\":1001,\"transform\":{\"position\":{\"x\":100,\"y\":0,\"z\":50},\"rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}",
            ExampleGist: "spawn entity type 1001 at position (100,0,50)"),

        [("POST", "/breakpoints")] = new RouteDoc(
            Tool:    "set_breakpoint",
            Group:   "G — Breakpoints",
            Summary: "Register a run-until-condition breakpoint.",
            Returns: "{ breakpointId } (e.g. \"BP#1\").",
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
            Summary: "STAGE a write to one blueprint variable, through the same seam the editor's Details panel uses. The value lands on the next advancing tick — not on this response.",
            Returns: "{ networkId, asset, assetId, path, staged: true, pending: true, note }",
            Hint:    "Required: networkId, path, value. Example: stage_entity_variable({networkId:1000, path:\"Health\", value:42})",
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
            },
            ExampleArgsJson: "{\"networkId\":1000,\"path\":\"Health\",\"value\":42}",
            ExampleGist: "queue Health = 42; it applies on the next advancing tick"),

        [("GET", "/panels")] = new RouteDoc(
            Tool:    "list_panels",
            Group:   "T — Panels (the UI as data)",
            Summary: "What the editor's UI is showing, without pixels: which panels are instrumented at all, and which published a view-model this frame.",
            Returns: "{ captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }",
            Hint:    "No params. Example: list_panels({})",
            Notes: new[]
            {
                "registered vs captured is the load-bearing distinction: a panel nobody instrumented and a panel whose window is closed are different facts, and only the second is fixed by opening a window.",
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
            Summary: "Attach an Instance blueprint to a running entity — the quick way to try a behaviour without authoring a mission.",
            Returns: "{ networkId, blueprint, blueprintId, attached:true, note }",
            Hint:    "Required: networkId, blueprint. Example: attach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("blueprint", "string", true, "Blueprint name, asset Guid, or numeric blueprintId (see list_blueprints)"),
                new("paramsJson", "object", false, "Parameters for the blueprint, keyed by name; omit for its declared defaults"),
            },
            Notes: new[]
            {
                "Queued: the ingress system applies it on the NEXT tick, so step or play once before reading it back.",
                "After it lands, the entity's variables appear in list_entity_variables — name the asset, since the entity may now carry more than one.",
            },
            ExampleArgsJson: "{\"networkId\":1001,\"blueprint\":\"ComponentCollectionDemo\"}",
            ExampleGist: "try a blueprint on entity 1001 right now"),

        [("POST", "/entities/{networkId}/detach-blueprint")] = new RouteDoc(
            Tool:    "detach_blueprint",
            Group:   "Q — Blueprint hot-attach",
            Summary: "Detach an Instance blueprint from an entity.",
            Returns: "{ networkId, blueprint, blueprintId, detached:true, note }",
            Hint:    "Required: networkId, blueprint. Example: detach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
                new("blueprint", "string", true, "Blueprint name, asset Guid, or numeric blueprintId"),
            },
            Notes: new[]
            {
                "Queued like the attach — applied on the next tick.",
            },
            ExampleArgsJson: "{\"networkId\":1001,\"blueprint\":\"ComponentCollectionDemo\"}",
            ExampleGist: "put the entity back how you found it"),

        [("GET", "/entities/{networkId}/state")] = new RouteDoc(
            Tool:    "get_entity_state",
            Group:   "R — Entity state",
            Summary: "The well-known fields parsed out — position, rotation, velocity, speed, current behaviour — so an assertion reads state.position.x instead of digging through component JSON.",
            Returns: "{ networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier} }",
            Hint:    "Required: networkId. Example: get_entity_state({networkId:1000})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network id of the entity"),
            },
            Notes: new[]
            {
                "A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.",
                "A convenience over get_entity, reading the same components: the two cannot disagree.",
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
            Returns: "{ isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }",
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
            Summary: "Take a single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true).",
            Returns: "ok:true with inPreview:true. Returns 409 if a live run is active; 400 if already in preview/checkpointed.",
            Hint:    "No params. Must NOT be in preview. Example: checkpoint({})",
            Notes: new[]
            {
                "Single slot: mutually exclusive with enter_preview and start_recording{preview}.",
                "Restore with restore_checkpoint to rewind all changes.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "take a checkpoint before an experiment"),

        [("POST", "/checkpoint/restore")] = new RouteDoc(
            Tool:    "restore_checkpoint",
            Group:   "H — Checkpoint / diff",
            Summary: "Rewind the simulation to the checkpointed state via IPreviewController.ExitPreviewMode().",
            Returns: "ok:true with inPreview:false. Returns 400 if no checkpoint is active.",
            Hint:    "No params. Requires an active checkpoint. Example: restore_checkpoint({})",
            Notes: new[]
            {
                "Returns 400 if no checkpoint is active.",
            },
            ExampleArgsJson: "{}",
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

        [("POST", "/recording/start")] = new RouteDoc(
            Tool:    "start_recording",
            Group:   "I — Recording / replay",
            Summary: "Start recording. Enters preview and begins writing a .fdp file.",
            Returns: "{ recording:true, mode, fdpPath }",
            Hint:    "No required params. Optional: mode (\"preview\"|\"live\", def \"preview\"). Example: start_recording({mode:\"preview\"})",
            Params: new RouteParam[]
            {
                new("mode", "string", false, "Recording mode: \"preview\" (revertible) or \"live\" (not supported in editor mode). Default: \"preview\"",
                    DefaultJson: "\"preview\"",
                    EnumJson: "[\"preview\",\"live\"]"),
            },
            Notes: new[]
            {
                "mode=\"preview\" (default): revertible, uses EnterPreviewMode→PrepareRecordingAsync.",
                "mode=\"live\": not supported in editor mode.",
                "Mutually exclusive with checkpoint (both use the preview slot).",
            },
            ExampleArgsJson: "{\"mode\":\"preview\"}",
            ExampleGist: "start a revertible preview recording"),

        [("POST", "/recording/stop")] = new RouteDoc(
            Tool:    "stop_recording",
            Group:   "I — Recording / replay",
            Summary: "Stop the active recording. Finalizes BEFORE the exit rewind.",
            Returns: "{ recording:false, fdpPath }",
            Hint:    "No params. Example: stop_recording({})",
            Notes: new[]
            {
                "For preview mode: finalizes BEFORE the exit rewind (hard ordering rule).",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "stop recording and get the .fdp file path"),

        [("POST", "/replay/load")] = new RouteDoc(
            Tool:    "load_replay",
            Group:   "I — Recording / replay",
            Summary: "Load a .fdp recording into an ISOLATED ReplayBrowserContext.",
            Returns: "{ loaded:true, fdpPath, totalFrames, currentFrame }",
            Hint:    "Req: fdpPath (string, absolute path to .fdp file). Example: load_replay({fdpPath:\"/path/to/recording.fdp\"})",
            Params: new RouteParam[]
            {
                new("fdpPath", "string", true, "Absolute path to the .fdp recording file"),
            },
            Notes: new[]
            {
                "While replay is active, /replay/entities returns entities from the sandbox (not the live world).",
                "Use list_replay_entities (not list_entities) while replaying.",
            },
            ExampleArgsJson: "{\"fdpPath\":\"/path/to/recording.fdp\"}",
            ExampleGist: "load a .fdp recording for inspection"),

        [("POST", "/replay/seek")] = new RouteDoc(
            Tool:    "seek_replay",
            Group:   "I — Recording / replay",
            Summary: "Seek to a specific frame in the ISOLATED sandbox. Does NOT touch the live world.",
            Returns: "{ frame, totalFrames }",
            Hint:    "Req: frame (number, 0-based). Example: seek_replay({frame:0})",
            Params: new RouteParam[]
            {
                new("frame", "number", true, "Frame index to seek to (0-based)"),
            },
            Notes: new[]
            {
                "Isolation guarantee: does NOT touch the live world.",
            },
            ExampleArgsJson: "{\"frame\":0}",
            ExampleGist: "seek replay to frame 0 (start)"),

        [("POST", "/replay/step")] = new RouteDoc(
            Tool:    "step_replay",
            Group:   "I — Recording / replay",
            Summary: "Step one frame forward or backward in the ISOLATED sandbox. Does NOT touch the live world.",
            Returns: "{ stepped:bool, frame, totalFrames }",
            Hint:    "No required params. Optional: dir (\"forward\"|\"back\", def \"forward\"). Example: step_replay({dir:\"forward\"})",
            Params: new RouteParam[]
            {
                new("dir", "string", false, "Step direction: \"forward\" or \"back\". Default: \"forward\"",
                    DefaultJson: "\"forward\"",
                    EnumJson: "[\"forward\",\"back\"]"),
            },
            Notes: new[]
            {
                "Isolation guarantee: does NOT touch the live world.",
            },
            ExampleArgsJson: "{\"dir\":\"forward\"}",
            ExampleGist: "step one frame forward in the replay"),

        [("GET", "/replay/status")] = new RouteDoc(
            Tool:    "get_replay_status",
            Group:   "I — Recording / replay",
            Summary: "Replay sandbox status.",
            Returns: "{ replayActive, currentFrame, totalFrames }",
            Hint:    "No params. Example: get_replay_status({})",
            ExampleArgsJson: "{}",
            ExampleGist: "check if replay is active and current frame"),

        [("GET", "/replay/entities")] = new RouteDoc(
            Tool:    "list_replay_entities",
            Group:   "I — Recording / replay",
            Summary: "List entities from the ISOLATED replay sandbox at the current frame.",
            Returns: "Same schema as list_entities but from the sandbox repo, NOT the live world.",
            Hint:    "No params. Requires load_replay first. Example: list_replay_entities({})",
            Notes: new[]
            {
                "Requires an active replay (call load_replay first).",
                "Does not touch or affect the live world.",
            },
            ExampleArgsJson: "{}",
            ExampleGist: "inspect entities at current replay frame"),

        [("POST", "/replay/unload")] = new RouteDoc(
            Tool:    "unload_replay",
            Group:   "I — Recording / replay",
            Summary: "Dispose the replay sandbox and return to live world queries.",
            Returns: "ok:true envelope.",
            Hint:    "No params. Example: unload_replay({})",
            ExampleArgsJson: "{}",
            ExampleGist: "unload replay sandbox when done inspecting"),

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

        [("GET", "/attributes/schema")] = new RouteDoc(
            Tool:    "get_attributes_schema",
            Group:   "L — Mutation / fault injection",
            Summary: "Return all patchable attribute paths and their JSON Schema.",
            Returns: "{ registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, GeoPosition.*, Heading, …).",
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
            Summary: "Apply a JSON attribute patch to an entity.",
            Returns: "Updated entity dump on success.",
            Hint:    "Req: networkId (number), patchJson (object {\"Name\":\"Alpha\"} or JSON string). Example: patch_attribute({networkId:1000,patchJson:{Name:\"Alpha\"}})",
            Params: new RouteParam[]
            {
                new("networkId", "number", true, "Network entity ID (long)"),
                new("patchJson", null, true, "Patch as a JSON object {\"Name\":\"Alpha\"} or as a JSON string"),
            },
            Notes: new[]
            {
                "Authority-aware; unregistered keys are silently ignored (no error).",
                "patchJson may be a nested JSON object like {\"Name\":\"Alpha\"} or a JSON string.",
            },
            ExampleArgsJson: "{\"networkId\":1000,\"patchJson\":{\"Name\":\"Alpha\"}}",
            ExampleGist: "rename entity 1000 to Alpha"),

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
            Hint:    "Req: tkbType (number/long from list_entity_types). Example: get_entity_type({tkbType:1001})",
            Params: new RouteParam[]
            {
                new("tkbType", "number", true, "TKB type ID (long)"),
            },
            ExampleArgsJson: "{\"tkbType\":1001}",
            ExampleGist: "inspect TKB descriptor for type 1001"),

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
            Summary: "World metadata: geo origin, spatial grid extent. terrain and navmesh are null in editor mode.",
            Returns: "{ geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null }",
            Hint:    "No params. Example: get_world_info({})",
            Notes: new[]
            {
                "terrain and navmesh are null in editor mode.",
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
