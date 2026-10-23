/**
 * tool-catalog.mjs — GENERATED. Do not edit.
 *
 * ⛔ Edits here are lost on the next `npm run gen:catalog`. The source of truth for every
 *    endpoint-backed tool is its RouteDoc in Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs;
 *    the one tool with no endpoint (start_simulation) lives in catalog-supplement.mjs.
 *
 * ⭐ Regenerate:  npm run gen:catalog       Check for staleness:  npm run gen:catalog:check
 * ⭐ SKILL.md is then generated from this file, as before (npm run gen:skill).
 */

export const TOOLS_CATALOG = [

  // ── Group A — Lifecycle & status ────────────────────────────────────────────

  {
    "name": "start_simulation",
    "group": "A — Lifecycle & status",
    "summary": "Launch the Hrot ClusterRunner with the AI Debug API enabled, in editor or cluster mode. Polls /status until ready.",
    "http": null,
    "params": [
      {
        "name": "runnerDll",
        "type": "string",
        "required": false,
        "description": "Absolute path to Hrot.ClusterRunner.dll (overrides --runner-dll CLI arg)"
      },
      {
        "name": "port",
        "type": "number",
        "required": false,
        "description": "Debug API port (overrides --port CLI arg). Default: 8099",
        "default": 8099
      },
      {
        "name": "mode",
        "type": "string",
        "required": false,
        "description": "Runner mode: \"editor\" (one node, everything local) or a cluster mode such as \"all\" (orchestrator + simhost + ig + excon + cgf). Default: editor",
        "default": "editor"
      },
      {
        "name": "headless",
        "type": "boolean",
        "required": false,
        "description": "Pass --headless to the runner. Editor mode only — see notes. Default: false",
        "default": false
      }
    ],
    "returns": "{ url, pid, mode }",
    "notes": [
      "MCP-side lifecycle tool — no HTTP endpoint.",
      "runnerDll is required unless the server was started with --runner-dll.",
      "A cluster mode (\"all\") serves the same API but commands act in the currently selected perspective — call get_capabilities and switch_perspective.",
      "headless is REFUSED for a cluster mode: a panel publishes only when it draws, and the headless runner loop never draws, so every panel dump would come back empty. Launch it windowed (under Xvfb on Linux)."
    ],
    "example": {
      "args": {
        "runnerDll": "/path/to/Hrot.ClusterRunner.dll",
        "port": 8099,
        "mode": "all"
      },
      "gist": "launch the whole cluster in one process on the default port"
    },
    "hint": "Required (if no --runner-dll on server): runnerDll (string). Optional: mode (\"editor\"|\"all\"), port, headless. Example: start_simulation({runnerDll:\"/path/to/dll\", mode:\"all\"})",
    "manualVerify": false
  },

  {
    "name": "get_capabilities",
    "group": "A — Lifecycle & status",
    "summary": "What THIS host can actually do — every endpoint, and the measured per-perspective matrix.",
    "http": {
      "method": "GET",
      "path": "/capabilities"
    },
    "params": [],
    "returns": "{ mode, host{hasMaster,currentPerspective,routablePerspectives}, endpoints[], matrix{perspective:{capability:bool}}, unclassifiedRoutes[] }",
    "notes": [
      "ASK THIS FIRST when a call answers 501 NOT_SUPPORTED_HERE — the matrix says which capabilities the active perspective offers, so you can switch perspective or pick another endpoint instead of guessing.",
      "mode tells you how the process was started: \"editor\" (one context, everything local) or a cluster mode such as \"all\" (orchestrator + simhost + ig + excon + cgf).",
      "The matrix is MEASURED from wired dependencies, not declared — a false cell is a bug, not a stale table.",
      "host.hasMaster:false means a step cannot be confirmed cluster-wide on this host."
    ],
    "example": {
      "args": {},
      "gist": "find out what this host supports before driving it"
    },
    "hint": "No params. Example: get_capabilities({})",
    "manualVerify": false
  },

  {
    "name": "cancel_cluster_operation",
    "group": "A — Lifecycle & status",
    "summary": "The operator's cancel (R-4004): cancel a cluster request in flight by its transactionId; the cluster returns to Idle.",
    "http": {
      "method": "POST",
      "path": "/cluster/cancel"
    },
    "params": [
      {
        "name": "targetId",
        "type": "string",
        "required": true,
        "description": "The transactionId of the request to cancel (a GUID)"
      }
    ],
    "returns": "{ published:true, targetId }",
    "notes": [
      "ASYNCHRONOUS: confirms the cancel was published; read get_status for the cluster returning to Idle.",
      "Any node with an orchestration bus can publish it; a host without one answers 503."
    ],
    "example": {
      "args": {
        "targetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "cancel a cluster load in flight"
    },
    "hint": "Req: targetId (the transactionId from request_cluster_transition). Example: cancel_cluster_operation({targetId:\"…\"})",
    "manualVerify": true
  },

  {
    "name": "get_cluster_roster",
    "group": "A — Lifecycle & status",
    "summary": "Every node the cluster master holds, as the MASTER sees it: id, subsystem, its own reported state, roles, heartbeat age.",
    "http": {
      "method": "GET",
      "path": "/cluster/roster"
    },
    "params": [],
    "returns": "{ count, nodes[{ nodeId, subsystem, reportedState, roles, secondsSinceHeartbeat }], note }",
    "notes": [
      "reportedState is each node's OWN claim from its heartbeat, not the cluster's state — read get_status for that.",
      "A node leaves the list after the heartbeat timeout (default 5 s) of silence.",
      "Only the node hosting the ClusterMaster can answer (the orchestrator process, or the editor's in-process core); elsewhere it answers 503 — check the 'cluster.roster' cell in get_capabilities."
    ],
    "example": {
      "args": {},
      "gist": "list the nodes in the cluster"
    },
    "hint": "No args. Example: get_cluster_roster({})",
    "manualVerify": false
  },

  {
    "name": "request_cluster_transition",
    "group": "A — Lifecycle & status",
    "summary": "Ask the cluster master to move the whole cluster to a target state — the same TransitionStateIntent the operator panel publishes.",
    "http": {
      "method": "POST",
      "path": "/cluster/transition"
    },
    "params": [
      {
        "name": "target",
        "type": "string",
        "required": true,
        "description": "The ClusterState to move to (e.g. Idle, OperatingReplay); the refusal lists the legal names"
      },
      {
        "name": "scenario",
        "type": "string",
        "required": false,
        "description": "Scenario id for a load"
      },
      {
        "name": "exerciseId",
        "type": "string",
        "required": false,
        "description": "Exercise GUID for a replay"
      },
      {
        "name": "timeMode",
        "type": "string",
        "required": false,
        "description": "Time mode for the load"
      },
      {
        "name": "targetWallTicks",
        "type": "number",
        "required": false,
        "description": "Wall-clock ticks the transition should take effect at"
      }
    ],
    "returns": "{ published:true, transactionId, target, note }",
    "notes": [
      "ASYNCHRONOUS: the answer confirms the intent was PUBLISHED, not that the cluster reached the state — poll get_status or get_cluster_roster.",
      "Pass the returned transactionId to cancel_cluster_operation to cancel it; a cancel returns the cluster to Idle (R-4004).",
      "Any node with an orchestration bus can publish it; a host without one answers 503."
    ],
    "example": {
      "args": {
        "target": "Idle"
      },
      "gist": "return the cluster to Idle"
    },
    "hint": "Req: target (a ClusterState name). Example: request_cluster_transition({target:\"OperatingReplay\", exerciseId:\"…\"})",
    "manualVerify": true
  },

  {
    "name": "switch_perspective",
    "group": "A — Lifecycle & status",
    "summary": "Switch the active perspective, then report what actually happened.",
    "http": {
      "method": "POST",
      "path": "/perspective"
    },
    "params": [
      {
        "name": "name",
        "type": "string",
        "required": true,
        "description": "Perspective to activate — must be one list_perspectives returns"
      }
    ],
    "returns": "{ current, note }",
    "notes": [
      "ALWAYS read `current` back — an unknown name is a no-op, so trusting the 200 would leave you reading the WRONG perspective's panels.",
      "A 400 names the claimed set; a 503 means perspective access is not wired on this host.",
      "The new perspective publishes its panels on the NEXT frame — step a tick before get_panels, or you read the previous one.",
      "In a cluster host (mode \"all\") this is how you choose which node subsequent commands act on."
    ],
    "example": {
      "args": {
        "name": "SimHost"
      },
      "gist": "act in the SimHost node's context"
    },
    "hint": "Req: name (string, from list_perspectives). Example: switch_perspective({name:\"SimHost\"})",
    "manualVerify": false
  },

  {
    "name": "list_perspectives",
    "group": "A — Lifecycle & status",
    "summary": "Every perspective a registered window claims, plus the active one.",
    "http": {
      "method": "GET",
      "path": "/perspectives"
    },
    "params": [],
    "returns": "{ current, perspectives[] }",
    "notes": [
      "A perspective exists because a window CLAIMS it — this list is derived, not configured.",
      "current is reported alongside the list because it is the only honest answer to \"did my switch take?\"."
    ],
    "example": {
      "args": {},
      "gist": "see which perspectives this host can route to"
    },
    "hint": "No params. Example: list_perspectives({})",
    "manualVerify": false
  },

  {
    "name": "stop_simulation",
    "group": "A — Lifecycle & status",
    "summary": "Shut down the runner gracefully via POST /shutdown, then hard-kill if needed.",
    "http": {
      "method": "POST",
      "path": "/shutdown"
    },
    "params": [],
    "returns": "The /shutdown envelope, or { note: \"runner already gone\" }",
    "notes": [
      "MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.",
      "Always call when done to avoid orphan runner processes."
    ],
    "example": {
      "args": {},
      "gist": "graceful runner shutdown"
    },
    "hint": "No params. Example: stop_simulation({})",
    "manualVerify": false
  },

  {
    "name": "get_status",
    "group": "A — Lifecycle & status",
    "summary": "Runner liveness + sim state summary.",
    "http": {
      "method": "GET",
      "path": "/status"
    },
    "params": [],
    "returns": "{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }",
    "notes": [
      "Use this to verify the runner is alive and check current run state before driving the sim."
    ],
    "example": {
      "args": {},
      "gist": "check runner liveness and sim state"
    },
    "hint": "No params. Example: get_status({})",
    "manualVerify": false
  },

  // ── Group B — Queries ───────────────────────────────────────────────────────

  {
    "name": "list_component_types",
    "group": "B — Queries",
    "summary": "Enumerate registered ECS component types with field schemas.",
    "http": {
      "method": "GET",
      "path": "/components"
    },
    "params": [],
    "returns": "All registered component types + field schemas (for use with edit_component).",
    "notes": [
      "Use this to discover component type names before calling edit_component."
    ],
    "example": {
      "args": {},
      "gist": "list all ECS component types and their schemas"
    },
    "hint": "No params. Example: list_component_types({})",
    "manualVerify": false
  },

  {
    "name": "list_entities",
    "group": "B — Queries",
    "summary": "List all entities with networkId, name, and component names.",
    "http": {
      "method": "GET",
      "path": "/entities"
    },
    "params": [
      {
        "name": "component",
        "type": "string",
        "required": false,
        "description": "Filter: only entities that have this component type"
      },
      {
        "name": "near",
        "type": "string",
        "required": false,
        "description": "Spatial filter: \"x,y,r\" (comma-separated floats)"
      }
    ],
    "returns": "[{networkId, name, components:[names]}]",
    "notes": [
      "Optional filters compose: component (only entities having it), near (\"x,y,r\" within radius r of (x,y))."
    ],
    "example": {
      "args": {
        "component": "SimTransform"
      },
      "gist": "list only entities with SimTransform component"
    },
    "hint": "No required params. Optional: component (string), near (\"x,y,r\"). Example: list_entities({component:\"SimTransform\"})",
    "manualVerify": false
  },

  {
    "name": "get_entity",
    "group": "B — Queries",
    "summary": "Full component dump for one entity.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "Full component dump for the entity. Non-finite floats render as \"NaN\"/\"Infinity\"/\"-Infinity\".",
    "notes": [
      "Non-finite floats appear as string sentinels \"NaN\"/\"Infinity\"/\"-Infinity\" — valid JSON, not a bug.",
      "SHAPE: { EntityId, NetworkId, Components:{ ... } } — Components is PascalCase, and so is every component and field name inside it (SimTransform.Position, NavigationIntent.Mode). Indexing a lowercase 'components' silently yields nothing and reads like an empty entity.",
      "TO DIAGNOSE 'the sim ignores my order', COMPARE THE INTENT COMPONENT WITH ITS STATUS COMPONENT. The pair distinguishes three different bugs that look identical from the UI: intent empty => nothing issued the order; intent set + status ABSENT => the consumer never ran; intent set + status PRESENT + zero velocity => the consumer ran and produced no motion. Worked example: NavigationIntent{Mode,TargetSpeed} against NavigationStatus.",
      "AUTHORITY FIRST: NetworkAuthority carry HasAuthority, PrimaryOwnerId and LocalNodeId. On a cluster a write to an entity this node does not own is legitimately dropped, so check HasAuthority before filing 'the write did nothing'.",
      "MEASURE MOTION AS A POSITION DELTA OVER A simTime DELTA, never over wall-clock. Sample get_status.simTime alongside each dump: BIT-IDENTICAL positions across a real simTime advance is the hard evidence; the same reading across a stalled clock proves nothing.",
      "ON --mode all THIS READS ONE NODE -- THE ACTIVE PERSPECTIVE -- AND THE NODES DISAGREE. Brain (CGF/Scenario) and muscle (SimHost) hold separate copies of the same entity, and a defect can live entirely in the gap between them. Measured 2026-08-28: entity 1001 held Class:Tank, AccelGain:1.8 on CGF and PersonalCar, AccelGain:0 on SimHost, because the scenario's authored VehicleParams reached the brain intact and was dropped on the wire hop. The brain computed a valid path (which rendered) while the muscle could not accelerate -- on screen, indistinguishable from a broken navigator. SO: read the entity on BOTH nodes before concluding anything about 'the cluster'.",
      "AND ?perspective= DOES NOT WORK: it is not implemented on any route and is IGNORED (you get a hint saying so since CE-112). Switch with POST /perspective {name:...} and then read; confirm with get_status.perspective. Passing ?perspective=ExCon -- a subsystem with NO WORLD AT ALL -- used to return a full component dump, which is how the ignored key was caught."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "get full component dump for entity 1000"
    },
    "hint": "Req: networkId (number/long). Example: get_entity({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_ownership",
    "group": "B — Queries",
    "summary": "Per-descriptor network ownership of one entity (NED nodes only).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/ownership"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ networkId, primaryOwnerId, localNodeId, descriptors:[{ descriptorTypeId, isMaster, ownedByThisNode, ownerNodeId, components:[..] }] }.",
    "notes": [
      "Ownership is DESCRIPTOR-level (a NED network concept), not component-level: each descriptorTypeId binds a set of components, and one component may belong to several descriptors. isMaster marks the EntityMaster descriptor whose owner is the entity's primary/save owner.",
      "ownedByThisNode is the authority fact this node acts on; ownerNodeId is the explicit per-descriptor override when one exists (null ⇒ follows the primary owner).",
      "VERIFICATION SURFACE for transfer_entity_ownership: read ownership, POST the transfer, read again.",
      "PERSPECTIVE-SCOPED on --mode all: reports the ACTIVE perspective node's view. Read on both the giving and receiving node to confirm a transfer landed."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read which node owns each of an entity's descriptors"
    },
    "hint": "Req: networkId (number/long). 503 on a host with no NED transport (editor/AllInOne). Example: get_entity_ownership({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "transfer_entity_ownership",
    "group": "B — Queries",
    "summary": "Hand an entity (or a subset of its descriptors) to another node (NED nodes only).",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/ownership/transfer"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "newOwnerNodeId",
        "type": "integer",
        "required": true,
        "description": "The node to hand the descriptors to"
      },
      {
        "name": "scope",
        "type": "string",
        "required": false,
        "description": "Which descriptors move",
        "enum": [
          "MasterOnly",
          "AllOwnedByThisNode",
          "SpecificDescriptors"
        ],
        "default": "AllOwnedByThisNode"
      },
      {
        "name": "descriptors",
        "type": "array",
        "required": false,
        "description": "NED descriptor type ids (required only for SpecificDescriptors)",
        "items": {
          "type": "number"
        }
      }
    ],
    "returns": "{ requested:true, networkId, newOwnerNodeId, scope, note } — fire-and-forget; verify with GET .../ownership.",
    "notes": [
      "DESCRIPTOR-level and NED-initiated (ruling 2026-09-15): 'descriptors' are network descriptor type ids (get them from get_entity_ownership), never ECS component types.",
      "Only descriptors THIS node owns move — so on a native cluster AllOwnedByThisNode hands off the brain's descriptors but leaves the muscle-owned world-position descriptor where it is.",
      "Save ownership (primaryOwnerId) moves IFF EntityMaster is in the set (MasterOnly and AllOwnedByThisNode include it; a SpecificDescriptors set that omits master leaves it here).",
      "The old owner stops publishing without disposing (entity never blinks out); the new owner confirms by re-publishing EntityMaster. Verify with get_entity_ownership on the new owner."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "newOwnerNodeId": 2,
        "scope": "AllOwnedByThisNode"
      },
      "gist": "hand entity 1000 to node 2"
    },
    "hint": "Req: networkId (path), newOwnerNodeId (int). scope defaults AllOwnedByThisNode. 503 on a host with no NED transport. Example: transfer_entity_ownership({networkId:1000, newOwnerNodeId:2, scope:\"MasterOnly\"})",
    "manualVerify": false
  },

  {
    "name": "create_entity_request",
    "group": "B — Queries",
    "summary": "Create an entity THROUGH the request path (routing + auto-takeover grant).",
    "http": {
      "method": "POST",
      "path": "/entities/create-request"
    },
    "params": [
      {
        "name": "tkbType",
        "type": "integer",
        "required": true,
        "description": "TKB entity type id (long)"
      },
      {
        "name": "ownerNodeId",
        "type": "integer",
        "required": false,
        "description": "This node's id ⇒ create + own locally; 0 ⇒ forward to the broadcast arbiter",
        "default": 0
      },
      {
        "name": "transform",
        "type": "object",
        "required": false,
        "description": "Optional initial transform"
      },
      {
        "name": "attributesJson",
        "type": "string",
        "required": false,
        "description": "Optional initial attributes as a JSON string"
      }
    ],
    "returns": "{ networkId, ... } for the created entity, or a 400 on a malformed body.",
    "notes": [
      "CE-271 seam ⑤ / CE-515 ③: the create-request pipeline (routing + ownership grants). POST /entities/spawn now uses the same pipeline; the only difference is ownerNodeId:0 — here 'forward to the arbiter', there 'this node'."
    ],
    "example": {
      "args": {
        "tkbType": 123,
        "ownerNodeId": 1
      },
      "gist": "create an entity through the create-request pipeline, owned by node 1"
    },
    "hint": "Req: tkbType (long). Unlike spawn_entity this goes through the create-request pipeline. Example: create_entity_request({tkbType:123, ownerNodeId:1})",
    "manualVerify": false
  },

  {
    "name": "list_scenarios",
    "group": "B — Queries",
    "summary": "List available scenarios by relative path.",
    "http": {
      "method": "GET",
      "path": "/scenarios"
    },
    "params": [],
    "returns": "Available scenario names (relative paths) for use with load_scenario_edit / load_scenario_live.",
    "example": {
      "args": {},
      "gist": "discover loadable scenario names"
    },
    "hint": "No params. Example: list_scenarios({})",
    "manualVerify": false
  },

  // ── Group E — Scenario ──────────────────────────────────────────────────────

  {
    "name": "load_scenario_edit",
    "group": "E — Scenario",
    "summary": "Load a scenario for AUTHORING (Edit state), cluster-wide.",
    "http": {
      "method": "POST",
      "path": "/scenario/load/edit"
    },
    "params": [
      {
        "name": "name",
        "type": "string",
        "required": true,
        "description": "Scenario name (relative path)"
      },
      {
        "name": "waitForReady",
        "type": "boolean",
        "required": false,
        "description": "Wait for the cluster to reach OperatingEdit before returning",
        "default": false
      }
    ],
    "returns": "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.",
    "notes": [
      "Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).",
      "Edit state freezes sim time — nothing ticks until enter_preview or play.",
      "CE-102 (2026-08-28) gave CGF the shared edit-load handler, so an edit load is NO LONGER partial on that node. This note previously said CGF had none -- that is now stale. Still prefer load_scenario_live for a real run, and verify either load by reading state.",
      "AND THAT PARTIAL LOAD STILL ANSWERS ok:true — this is the single most misleading response in the API. Measured on --mode all from the Scenario perspective: ok:true with scenario:NULL, entityCount:0, an empty list_entities, and a gizmo frame of 603 primitives that were ALL grid lines. Every field says 'empty world' and the envelope says 'success'.",
      "SO VERIFY A LOAD, NEVER TRUST IT: after any load read get_status.entityCount AND list_entities AND (if the map matters) the non-Line shape count from get_gizmo_frame. Three independent reads, because the envelope is not one of them. On the same host load_scenario_live gave 8 entities and Box2D 8 / Arrow 12 / Text 8."
    ],
    "example": {
      "args": {
        "name": "test-move",
        "waitForReady": true
      },
      "gist": "load test-move for authoring and wait for ready"
    },
    "hint": "Req: name (string). Optional: waitForReady (bool, use true). Example: load_scenario_edit({name:\"test-move\",waitForReady:true})",
    "manualVerify": false
  },

  {
    "name": "load_scenario_live",
    "group": "E — Scenario",
    "summary": "Load a scenario for RUNNING (Live state), cluster-wide, on any host.",
    "http": {
      "method": "POST",
      "path": "/scenario/load/live"
    },
    "params": [
      {
        "name": "name",
        "type": "string",
        "required": true,
        "description": "Scenario name (relative path)"
      },
      {
        "name": "waitForReady",
        "type": "boolean",
        "required": false,
        "description": "Wait for the cluster to reach OperatingLive before returning",
        "default": false
      }
    ],
    "returns": "ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.",
    "notes": [
      "Set waitForReady:true to block until the cluster reaches OperatingLive (recommended).",
      "Every host has live-load handlers, so this is the mode that loads on ALL nodes — use it when the world must be the same everywhere.",
      "A live load starts a new exercise run (a fresh ExerciseId), which is what recording and replay key off."
    ],
    "example": {
      "args": {
        "name": "test-move",
        "waitForReady": true
      },
      "gist": "load test-move live across the cluster and wait for ready"
    },
    "hint": "Req: name (string). Optional: waitForReady (bool, use true). Example: load_scenario_live({name:\"test-move\",waitForReady:true})",
    "manualVerify": false
  },

  {
    "name": "save_scenario",
    "group": "E — Scenario",
    "summary": "Save the current authored world as a scenario.",
    "http": {
      "method": "POST",
      "path": "/scenario/save"
    },
    "params": [
      {
        "name": "name",
        "type": "string",
        "required": true,
        "description": "Scenario file name to save as"
      }
    ],
    "returns": "ok:true envelope.",
    "example": {
      "args": {
        "name": "my-scenario"
      },
      "gist": "save current world as my-scenario"
    },
    "hint": "Req: name (string). Example: save_scenario({name:\"my-scenario\"})",
    "manualVerify": false
  },

  // ── Group G — Breakpoints ───────────────────────────────────────────────────

  {
    "name": "list_breakpoints",
    "group": "G — Breakpoints",
    "summary": "List all registered breakpoints.",
    "http": {
      "method": "GET",
      "path": "/breakpoints"
    },
    "params": [],
    "returns": "[{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]",
    "example": {
      "args": {},
      "gist": "list all active breakpoints and their hit counts"
    },
    "hint": "No params. Example: list_breakpoints({})",
    "manualVerify": false
  },

  {
    "name": "set_breakpoint",
    "group": "G — Breakpoints",
    "summary": "Register a run-until-condition breakpoint.",
    "http": {
      "method": "POST",
      "path": "/breakpoints"
    },
    "params": [
      {
        "name": "condition",
        "type": "object",
        "required": true,
        "description": "SearchPredicateDto with $type discriminator (e.g. {\"$type\":\"Lifecycle\",\"IdentifierType\":\"NameSubstring\",\"TargetValue\":\"Alpha\",\"NamePropertyPath\":\"Name\"})"
      },
      {
        "name": "filterNetworkId",
        "type": "number",
        "required": false,
        "description": "Optional: only trigger for this entity (network ID)"
      },
      {
        "name": "occurrenceThreshold",
        "type": "number",
        "required": false,
        "description": "Number of hits before pausing (default 1)",
        "default": 1
      },
      {
        "name": "name",
        "type": "string",
        "required": false,
        "description": "Human-readable label for the breakpoint"
      }
    ],
    "returns": "{ breakpointId } (e.g. \"BP#1\").",
    "notes": [
      "condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).",
      "Poll get_breakpoint_status after play to detect when the breakpoint fires."
    ],
    "example": {
      "args": {
        "condition": {
          "$type": "PropertyMatch",
          "ComponentType": "SimTransform",
          "PropertyPath": "Position.X",
          "Operator": "GreaterThan",
          "Predicate": {
            "$type": "Numeric",
            "MinValue": 100,
            "MaxValue": 1000000000
          }
        },
        "name": "moved-east"
      },
      "gist": "pause when entity SimTransform.Position.X > 100"
    },
    "hint": "Req: condition (SearchPredicateDto with $type). Optional: filterNetworkId, occurrenceThreshold, name. Example: set_breakpoint({condition:{\"$type\":\"Lifecycle\",...}})",
    "manualVerify": false
  },

  {
    "name": "remove_breakpoint",
    "group": "G — Breakpoints",
    "summary": "Remove a breakpoint by its ID string.",
    "http": {
      "method": "DELETE",
      "path": "/breakpoints/{id}"
    },
    "params": [
      {
        "name": "id",
        "type": "string",
        "required": true,
        "description": "Breakpoint ID string (e.g. \"BP#1\" from set_breakpoint or list_breakpoints)"
      }
    ],
    "returns": "ok:true envelope.",
    "example": {
      "args": {
        "id": "BP#1"
      },
      "gist": "remove breakpoint BP#1"
    },
    "hint": "Req: id (string, e.g. \"BP#1\" from set_breakpoint). Example: remove_breakpoint({id:\"BP#1\"})",
    "manualVerify": false
  },

  {
    "name": "continue_from_breakpoint",
    "group": "G — Breakpoints",
    "summary": "Resume the debugger after a breakpoint hit. Also what applies any live variable writes staged while it was stopped.",
    "http": {
      "method": "POST",
      "path": "/breakpoints/continue"
    },
    "params": [
      {
        "name": "step",
        "type": "boolean",
        "required": false,
        "description": "Advance one step instead of running on"
      }
    ],
    "returns": "{ wasPaused, action, isPaused, note }",
    "notes": [
      "⚠ Deleting a breakpoint does NOT resume: the debugger stays stopped, and while it is stopped every staged variable write is queued and never applied. Call this after a hit, not remove_breakpoint.",
      "Harmless when nothing is stopped — it answers wasPaused:false.",
      "The host also serves POST /breakpoints/step, which is exactly this call with step:true. Deliberately ONE tool, not two — use continue_from_breakpoint({step:true})."
    ],
    "example": {
      "args": {},
      "gist": "let the world run again after a breakpoint fired"
    },
    "hint": "Optional: step. Example: continue_from_breakpoint({})",
    "manualVerify": false
  },

  {
    "name": "get_breakpoint_status",
    "group": "G — Breakpoints",
    "summary": "Current pause state and last breakpoint hit.",
    "http": {
      "method": "GET",
      "path": "/breakpoints/hits"
    },
    "params": [],
    "returns": "{ isPaused, pausedTick, lastHit: { breakpointId, networkId, journalWrittenByTheTick?:[{seq, at, kind, text}] } | null }",
    "notes": [
      "Poll this after play to detect when a breakpoint fires."
    ],
    "example": {
      "args": {},
      "gist": "poll for breakpoint hit after calling play"
    },
    "hint": "No params. Example: get_breakpoint_status({})",
    "manualVerify": false
  },

  {
    "name": "break_on_journal",
    "group": "G — Breakpoints",
    "summary": "Stop the simulation when THIS unit changes its mind (CE-1258): a data breakpoint on its brain journal's sequence number, filtered to the unit (a neighbour's change does not stop it); optionally only for some entry kinds.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/journal/break"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "kinds",
        "type": "array",
        "required": false,
        "description": "Only stop for these journal entry kinds (default: any entry)",
        "items": {
          "type": "string"
        }
      }
    ],
    "returns": "{ breakpointId, networkId } — then poll get_breakpoint_status: its lastHit.journalWrittenByTheTick lists the entries the hit tick wrote.",
    "notes": [
      "A stop shows the world at the START of the tick that changed it (the cause still in place); continue_from_breakpoint({step:true}) runs that tick.",
      "It keeps firing on every later change until removed with remove_breakpoint.",
      "Editor only, like every breakpoint."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "pause the next time unit 1000's brain changes"
    },
    "hint": "Req: networkId (number); optional kinds (string[]: TaskStarted, TaskPaused, TaskResumed, TaskEnded, TaskCleared, TaskRefused, SopStarted, SopEnded, DecisionSwitched, MoveStarted, PlayStarted, PlayPhase, PlayEnded, OrderIssued, StateChanged). Example: break_on_journal({networkId:1000, kinds:[\"TaskStarted\",\"TaskPaused\"]})",
    "manualVerify": false
  },

  // ── Group H — Checkpoint / diff ─────────────────────────────────────────────

  {
    "name": "checkpoint",
    "group": "H — Checkpoint / diff",
    "summary": "Take a single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true).",
    "http": {
      "method": "POST",
      "path": "/checkpoint"
    },
    "params": [],
    "returns": "ok:true with inPreview:true. Returns 409 if a live run is active; 400 if already in preview/checkpointed.",
    "notes": [
      "Single slot: mutually exclusive with enter_preview and start_recording{preview}.",
      "Restore with restore_checkpoint to rewind all changes."
    ],
    "example": {
      "args": {},
      "gist": "take a checkpoint before an experiment"
    },
    "hint": "No params. Must NOT be in preview. Example: checkpoint({})",
    "manualVerify": false
  },

  {
    "name": "restore_checkpoint",
    "group": "H — Checkpoint / diff",
    "summary": "Rewind the simulation to the checkpointed state via IPreviewController.ExitPreviewMode().",
    "http": {
      "method": "POST",
      "path": "/checkpoint/restore"
    },
    "params": [],
    "returns": "ok:true with inPreview:false. Returns 400 if no checkpoint is active.",
    "notes": [
      "Returns 400 if no checkpoint is active."
    ],
    "example": {
      "args": {},
      "gist": "revert all changes since the last checkpoint"
    },
    "hint": "No params. Requires an active checkpoint. Example: restore_checkpoint({})",
    "manualVerify": false
  },

  {
    "name": "capture_diff_baseline",
    "group": "H — Checkpoint / diff",
    "summary": "Serialize current entity states server-side and return a baselineId.",
    "http": {
      "method": "POST",
      "path": "/diff/capture"
    },
    "params": [
      {
        "name": "entities",
        "type": "array",
        "required": false,
        "description": "Optional list of networkIds to capture (default: all entities)",
        "items": {
          "type": "number"
        }
      }
    ],
    "returns": "{ baselineId } (e.g. \"BL#1\")",
    "notes": [
      "Use before mutating the world, then call diff_state with the baselineId to see what changed.",
      "Optional entities array (networkId list) scopes which entities to capture (default: all)."
    ],
    "example": {
      "args": {
        "entities": [
          1000
        ]
      },
      "gist": "capture baseline for entity 1000 before mutation"
    },
    "hint": "No required params. Optional: entities (array of networkIds). Example: capture_diff_baseline({entities:[1000]})",
    "manualVerify": false
  },

  {
    "name": "diff_state",
    "group": "H — Checkpoint / diff",
    "summary": "Compare a previously captured baseline against current entity state.",
    "http": {
      "method": "POST",
      "path": "/diff/compare"
    },
    "params": [
      {
        "name": "baselineId",
        "type": "string",
        "required": true,
        "description": "Baseline ID from capture_diff_baseline (e.g. \"BL#1\")"
      },
      {
        "name": "entities",
        "type": "array",
        "required": false,
        "description": "Optional list of networkIds to diff (default: all entities in baseline)",
        "items": {
          "type": "number"
        }
      }
    ],
    "returns": "A DiffNode tree showing only what changed (token-efficient). Includes entity births/deaths.",
    "notes": [
      "baselineId comes from capture_diff_baseline.",
      "Returns only changed components — token-efficient for AI consumption."
    ],
    "example": {
      "args": {
        "baselineId": "BL#1",
        "entities": [
          1000
        ]
      },
      "gist": "diff entity 1000 against baseline BL#1"
    },
    "hint": "Req: baselineId (string from capture_diff_baseline). Optional: entities (array). Example: diff_state({baselineId:\"BL#1\"})",
    "manualVerify": false
  },

  // ── Group J — Logs ──────────────────────────────────────────────────────────

  {
    "name": "get_logs",
    "group": "J — Logs",
    "summary": "Query the in-process log sinks. Returns [{timestamp, level, logger, message}] sorted newest-first.",
    "http": {
      "method": "GET",
      "path": "/logs"
    },
    "params": [
      {
        "name": "level",
        "type": "string",
        "required": false,
        "description": "Minimum severity level (inclusive). Omit to return all levels.",
        "enum": [
          "Trace",
          "Debug",
          "Info",
          "Warning",
          "Error",
          "Critical"
        ]
      },
      {
        "name": "logger",
        "type": "string",
        "required": false,
        "description": "Filter by logger name substring (case-insensitive). Omit to return all loggers."
      },
      {
        "name": "since",
        "type": "string",
        "required": false,
        "description": "ISO-8601 timestamp. Only entries with timestamp >= since are returned."
      },
      {
        "name": "max",
        "type": "number",
        "required": false,
        "description": "Maximum number of entries to return (default 200).",
        "default": 200
      }
    ],
    "returns": "[{timestamp, level, logger, message}] sorted newest-first.",
    "notes": [
      "level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.",
      "logger = case-insensitive substring match on logger name.",
      "since = ISO-8601 timestamp; entries with timestamp >= since are included.",
      "Read off-thread — no main-thread marshal required.",
      "ON A CLUSTER ALWAYS PASS level:\"Info\" (or higher). Measured on --mode all: an UNFILTERED read returned 176 of 200 entries as [TC3] time-sync chatter (TimeSyncRequest/SyncResponse/RTT gentle-steer) logged at Trace by four nodes — no application line survives in the window. The same read with level:\"Info\" returned 25 lines and zero [TC3].",
      "AN UNKNOWN FILTER IS STILL SERVED LENIENTLY, BUT IT NOW TELLS YOU (CE-107). level:\"INFO_\" or a misspelled param (limit instead of max) returns the whole ring as before — the endpoint does not refuse a diagnostic call — but the SUCCESS envelope carries hint.why naming the ignored key or the unapplied level, and it also says so when no level filter was given at all. READ hint ON SUCCESS: it is the difference between an answer and an answer to a different question.",
      "The param is max, NOT limit. Nothing rejects limit; it is simply not read."
    ],
    "example": {
      "args": {
        "level": "Warning",
        "max": 50
      },
      "gist": "get last 50 Warning-or-higher log entries"
    },
    "hint": "No required params. Optional: level (Trace|Debug|Info|Warning|Error|Critical), logger (string), since (ISO-8601), max (number). Example: get_logs({level:\"Warning\"})",
    "manualVerify": false
  },

  // ── Group C — Event history ─────────────────────────────────────────────────

  {
    "name": "get_event_history",
    "group": "C — Event history",
    "summary": "Query the diagnostic event history.",
    "http": {
      "method": "GET",
      "path": "/events"
    },
    "params": [
      {
        "name": "bus",
        "type": "string",
        "required": false,
        "description": "Event bus to query",
        "enum": [
          "world",
          "orchestration"
        ],
        "default": "world"
      },
      {
        "name": "type",
        "type": "string",
        "required": false,
        "description": "Filter by event type name"
      },
      {
        "name": "since",
        "type": "number",
        "required": false,
        "description": "Return events since this frame number"
      },
      {
        "name": "max",
        "type": "number",
        "required": false,
        "description": "Maximum events to return (default 200)",
        "default": 200
      }
    ],
    "returns": "Recent diagnostic events from the specified bus.",
    "notes": [
      "bus: \"world\" (default) or \"orchestration\".",
      "Read-only; safe to call any time."
    ],
    "example": {
      "args": {
        "bus": "world",
        "type": "CenterOnEntityCommand",
        "max": 10
      },
      "gist": "query world bus for recent CenterOnEntityCommand events"
    },
    "hint": "No required params. Optional: bus (\"world\"|\"orchestration\"), type (string), since (frame), max (number). Example: get_event_history({bus:\"world\",max:50})",
    "manualVerify": false
  },

  // ── Group D — Sim / preview / time ──────────────────────────────────────────

  {
    "name": "enter_preview",
    "group": "D — Sim / preview / time",
    "summary": "Enter preview mode. Snapshots the world (revertible via stop_preview).",
    "http": {
      "method": "POST",
      "path": "/preview/enter"
    },
    "params": [
      {
        "name": "startPaused",
        "type": "boolean",
        "required": false,
        "description": "Start preview in paused state"
      }
    ],
    "returns": "ok:true envelope.",
    "notes": [
      "Snapshots the world; stop_preview rewinds to this snapshot.",
      "Single preview slot — mutually exclusive with checkpoint and start_recording{preview}."
    ],
    "example": {
      "args": {
        "startPaused": true
      },
      "gist": "enter preview paused for deterministic step-based control"
    },
    "hint": "No required params. Optional: startPaused (bool). Example: enter_preview({startPaused:true})",
    "manualVerify": false
  },

  {
    "name": "stop_preview",
    "group": "D — Sim / preview / time",
    "summary": "Exit preview mode; rewinds to the pre-preview snapshot.",
    "http": {
      "method": "POST",
      "path": "/preview/exit"
    },
    "params": [],
    "returns": "ok:true envelope.",
    "notes": [
      "Rewinds all changes made during preview back to the snapshot taken at enter_preview."
    ],
    "example": {
      "args": {},
      "gist": "exit preview and revert all changes since entering preview"
    },
    "hint": "No params. Example: stop_preview({})",
    "manualVerify": false
  },

  {
    "name": "pause",
    "group": "D — Sim / preview / time",
    "summary": "Pause the simulation. Time freezes; commands queue until step/play.",
    "http": {
      "method": "POST",
      "path": "/sim/pause"
    },
    "params": [],
    "returns": "ok:true envelope.",
    "notes": [
      "Commands and spawns while paused are queued and take effect on the next step/play.",
      "THE ACK MEANS APPLIED as of CE-104: the route waits until isPaused is actually true before returning, so the next read is safe. Measured before the fix on --mode all: ok:true while /status still read isPaused:false, with the clock running a further ~0.2s — because a cluster-wide pause is a FUTURE BARRIER so every node stops at the same simTime.",
      "A 504 from this route means the pause barrier is stuck — a node never reached it. Check get_logs({level:\"Warning\"})."
    ],
    "example": {
      "args": {},
      "gist": "pause the running simulation"
    },
    "hint": "No params. Example: pause({})",
    "manualVerify": false
  },

  {
    "name": "play",
    "group": "D — Sim / preview / time",
    "summary": "Enter preview and/or resume if paused. Time advances after this.",
    "http": {
      "method": "POST",
      "path": "/sim/play"
    },
    "params": [],
    "returns": "ok:true envelope.",
    "notes": [
      "Time advances after play (until pause or a breakpoint fires)."
    ],
    "example": {
      "args": {},
      "gist": "start or resume simulation"
    },
    "hint": "No params. Example: play({})",
    "manualVerify": false
  },

  {
    "name": "get_sim_state",
    "group": "D — Sim / preview / time",
    "summary": "Current sim state: isPaused, inPreview, totalTime, timeScale.",
    "http": {
      "method": "GET",
      "path": "/sim/state"
    },
    "params": [],
    "returns": "{ isPaused, inPreview, totalTime, timeScale }",
    "notes": [
      "Check this before driving — most mistakes are run-state mistakes."
    ],
    "example": {
      "args": {},
      "gist": "check current paused/preview/time state"
    },
    "hint": "No params. Example: get_sim_state({})",
    "manualVerify": false
  },

  {
    "name": "step",
    "group": "D — Sim / preview / time",
    "summary": "Advance simulation by N discrete steps. Only meaningful in preview.",
    "http": {
      "method": "POST",
      "path": "/sim/step"
    },
    "params": [
      {
        "name": "count",
        "type": "number",
        "required": false,
        "description": "Number of steps to advance (default 1)",
        "default": 1
      },
      {
        "name": "dt",
        "type": "number",
        "required": false,
        "description": "Seconds per step, in (0, 0.1] (default 1/60). The film director's coarser rates: 0.05 (20 Hz) or 0.0667 (15 Hz), CE-4038 / DESIGN_Film_Director.md D-1"
      }
    ],
    "returns": "ok:true envelope.",
    "notes": [
      "Only advances time when inPreview==true. In Edit state this is a no-op.",
      "count IS honoured cluster-wide as of CE-105, and it is gated per step: the route issues ONE step, waits for it to land, and repeats — so N steps take N frames and the call returns when the last one is acknowledged. Measured: count:60 advances simTime by exactly 1.0000s. Before the fix the loop ran inside a single frame and the cluster dropped all but the first, answering ok:true with 0.0167s of progress.",
      "STILL VERIFY BY READING simTime, NOT ok. The gate now makes the ack trustworthy, but a simTime delta is the cheap proof and it costs one call.",
      "A STEP IS REFUSED OUTRIGHT UNLESS THE CLUSTER IS IN A STEPPING MODE, and the refusal is not visible in the envelope — it is a warning in the log (see get_logs, RefusedStepCount). A cluster that is free-running drops every step. If simTime does not move, check get_status.isPaused first: pause, then step.",
      "This is the loop's weak link, so prove it once at the start of a session: pause, step, read simTime. A silently-refused step makes every later observation meaningless (it invalidated a root-cause in this repo once — a 'nothing moved' reading taken through a step that never ran)."
    ],
    "example": {
      "args": {
        "count": 5
      },
      "gist": "advance 5 simulation ticks"
    },
    "hint": "No required params. Optional: count (number, def 1), dt (seconds per step, def 1/60). Example: step({count:5})",
    "manualVerify": false
  },

  {
    "name": "set_time_scale",
    "group": "D — Sim / preview / time",
    "summary": "Set simulation time scale.",
    "http": {
      "method": "POST",
      "path": "/sim/timescale"
    },
    "params": [
      {
        "name": "scale",
        "type": "number",
        "required": true,
        "description": "Time scale factor (1.0 = real-time)"
      }
    ],
    "returns": "ok:true envelope.",
    "notes": [
      "1.0 = real-time, >1.0 = faster, <1.0 = slower."
    ],
    "example": {
      "args": {
        "scale": 2
      },
      "gist": "run simulation at 2x real-time"
    },
    "hint": "Req: scale (number, 1.0=real-time). Example: set_time_scale({scale:2.0})",
    "manualVerify": false
  },

  // ── Group F — Commands, discovery, spawn ────────────────────────────────────

  {
    "name": "list_commands",
    "group": "F — Commands, discovery, spawn",
    "summary": "Enumerate publishable FDP event types with field schemas.",
    "http": {
      "method": "GET",
      "path": "/commands"
    },
    "params": [],
    "returns": "Publishable FDP event types + field schemas; each tagged managed:true/false.",
    "notes": [
      "Call this to discover what send_entity_command accepts.",
      "managed:true events have server-side handling; managed:false are raw FDP events."
    ],
    "example": {
      "args": {},
      "gist": "discover available FDP event types before sending a command"
    },
    "hint": "No params. Example: list_commands({})",
    "manualVerify": false
  },

  {
    "name": "send_entity_command",
    "group": "F — Commands, discovery, spawn",
    "summary": "Publish an FDP event by type name.",
    "http": {
      "method": "POST",
      "path": "/entities/command"
    },
    "params": [
      {
        "name": "eventType",
        "type": "string",
        "required": true,
        "description": "FDP event type name (e.g. MissionControlIntent)"
      },
      {
        "name": "payload",
        "type": "object",
        "required": false,
        "description": "Event fields as JSON object"
      },
      {
        "name": "wait",
        "type": "boolean",
        "required": false,
        "description": "Attempt to wait for correlated ack"
      }
    ],
    "returns": "ok:true envelope. awaited:false if sim not running (not an error).",
    "notes": [
      "Set wait:true to attempt correlated-ack wait — only effective while time advances, else awaited:false.",
      "awaited:false is NOT an error — it means time was not advancing.",
      "CE-3168: a payload key that names no field of the event is a 400 (it used to be dropped silently, and the event published with defaults).",
      "To ORDER a unit use order_entity — it addresses units by network id and reaches the unit's brain node from any node."
    ],
    "example": {
      "args": {
        "eventType": "CenterOnEntityCommand",
        "payload": {
          "networkId": 1000
        },
        "wait": false
      },
      "gist": "publish CenterOnEntityCommand event"
    },
    "hint": "Req: eventType (string from list_commands). Optional: payload (object — the event's field names, any case; an entity field takes the unit's network id), wait (bool). Example: send_entity_command({eventType:\"CenterOnEntityCommand\",payload:{networkId:1000}})",
    "manualVerify": false
  },

  {
    "name": "spawn_entity",
    "group": "F — Commands, discovery, spawn",
    "summary": "Spawn an entity from a TKB type.",
    "http": {
      "method": "POST",
      "path": "/entities/spawn"
    },
    "params": [
      {
        "name": "tkbType",
        "type": "number",
        "required": true,
        "description": "TKB type ID (long)"
      },
      {
        "name": "transform",
        "type": "object",
        "required": false,
        "description": "Transform: { position: {x,y,z}, rotation: {x,y,z,w} }"
      },
      {
        "name": "components",
        "type": "array",
        "required": false,
        "description": "Additional component overrides"
      },
      {
        "name": "attributesJson",
        "type": "string",
        "required": false,
        "description": "JSON string of attribute overrides (JsonAttributeCompiler patch)"
      },
      {
        "name": "ownerNodeId",
        "type": "number",
        "required": false,
        "description": "0 (default) or this node's id ⇒ this node creates and owns it (and grants role groups); another node's id ⇒ routed to that node, which creates it"
      },
      {
        "name": "reliable",
        "type": "boolean",
        "required": false,
        "description": "CE-292: true ⇒ engage the cross-node construction barrier (InitType=AllPeers); the creator is this node unless ownerNodeId names another"
      },
      {
        "name": "reliableTimeoutSeconds",
        "type": "number",
        "required": false,
        "description": "CE-292: creator's reliable-init abort timeout in seconds (0 ⇒ gateway default)"
      }
    ],
    "returns": "{ spawned, tkbType, reliable, requestId, ownerNodeId, awaited, reason }. Created through the node's creation pack (request → spawn + ownership grants) on the next ticks — step to realize it.",
    "notes": [
      "Spawn is queued and processed on the next tick — call step to realize it.",
      "CE-515 ③: goes through the node's EntityCreationPack (RequestEntityCreation), like every other author — the creator owns the entity and grants each role's group (Brain/MuscleGround/Perception) to a node serving that role. It no longer publishes a raw SpawnEntityCommand that nobody owned.",
      "Use list_entity_types to discover valid tkbType values.",
      "CE-292 — reliable:true engages the cross-node construction barrier: the creator holds the entity Constructing until the capability-filtered peers (advertising fdp.reliable-init) report Active, or reliableTimeoutSeconds aborts it via an EntityMaster dispose. On the wire this shows as an EntityMaster carrying the WaitForAcks flag + EntityLifecycleStatusDescriptor samples (sniff with ddsmonitor)."
    ],
    "example": {
      "args": {
        "tkbType": 1101,
        "transform": {
          "position": {
            "x": 100,
            "y": 0,
            "z": 50
          },
          "rotation": {
            "x": 0,
            "y": 0,
            "z": 0,
            "w": 1
          }
        }
      },
      "gist": "spawn entity type 1101 at position (100,0,50)"
    },
    "hint": "Req: tkbType (number/long from list_entity_types). Optional: transform ({position,rotation}), components (array), attributesJson (string). Example: spawn_entity({tkbType:1001})",
    "manualVerify": false
  },

  // ── Group I — Recording / replay ────────────────────────────────────────────

  {
    "name": "start_recording",
    "group": "I — Recording / replay",
    "summary": "Start recording. Enters preview and begins writing a .fdp file.",
    "http": {
      "method": "POST",
      "path": "/recording/start"
    },
    "params": [
      {
        "name": "mode",
        "type": "string",
        "required": false,
        "description": "Recording mode: \"preview\" (revertible) or \"live\" (not supported in editor mode). Default: \"preview\"",
        "enum": [
          "preview",
          "live"
        ],
        "default": "preview"
      }
    ],
    "returns": "{ recording:true, mode, fdpPath }",
    "notes": [
      "mode=\"preview\" (default): revertible, uses EnterPreviewMode→PrepareRecordingAsync.",
      "mode=\"live\": not supported in editor mode.",
      "Mutually exclusive with checkpoint (both use the preview slot)."
    ],
    "example": {
      "args": {
        "mode": "preview"
      },
      "gist": "start a revertible preview recording"
    },
    "hint": "No required params. Optional: mode (\"preview\"|\"live\", def \"preview\"). Example: start_recording({mode:\"preview\"})",
    "manualVerify": false
  },

  {
    "name": "stop_recording",
    "group": "I — Recording / replay",
    "summary": "Stop the active recording. Finalizes BEFORE the exit rewind.",
    "http": {
      "method": "POST",
      "path": "/recording/stop"
    },
    "params": [],
    "returns": "{ recording:false, fdpPath }",
    "notes": [
      "For preview mode: finalizes BEFORE the exit rewind (hard ordering rule)."
    ],
    "example": {
      "args": {},
      "gist": "stop recording and get the .fdp file path"
    },
    "hint": "No params. Example: stop_recording({})",
    "manualVerify": false
  },

  {
    "name": "list_replay_entities",
    "group": "I — Recording / replay",
    "summary": "List entities from the ISOLATED replay sandbox at the current frame.",
    "http": {
      "method": "GET",
      "path": "/replay/entities"
    },
    "params": [],
    "returns": "Same schema as list_entities but from the sandbox repo, NOT the live world.",
    "notes": [
      "Requires an active replay (call load_replay first).",
      "Does not touch or affect the live world."
    ],
    "example": {
      "args": {},
      "gist": "inspect entities at current replay frame"
    },
    "hint": "No params. Requires load_replay first. Example: list_replay_entities({})",
    "manualVerify": false
  },

  {
    "name": "get_replay_entity_brain",
    "group": "I — Recording / replay",
    "summary": "get_entity_brain over the loaded recording at the current replay frame (CE-1265): what the unit was doing, who ordered it, what it knew, its recent journal.",
    "http": {
      "method": "GET",
      "path": "/replay/entities/{networkId}/brain"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "Same as get_entity_brain, plus frame.",
    "notes": [
      "Reads the replay SANDBOX, never the live world; seek_replay to the frame before a change to see its cause."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "what unit 1000 was doing at this replay frame"
    },
    "hint": "Req: networkId (number). Load with load_replay and seek first. Example: get_replay_entity_brain({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_replay_entity_journal",
    "group": "I — Recording / replay",
    "summary": "get_entity_journal over the loaded recording at the current replay frame (CE-1265) — the last 24 entries as they were then.",
    "http": {
      "method": "GET",
      "path": "/replay/entities/{networkId}/journal"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "since",
        "type": "number",
        "required": false,
        "description": "Only entries with seq greater than this (query)"
      }
    ],
    "returns": "Same as get_entity_journal, plus frame.",
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "the unit's recent changes at this replay frame"
    },
    "hint": "Req: networkId (number); optional since (number, query). Example: get_replay_entity_journal({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_replay_journal_history",
    "group": "I — Recording / replay",
    "summary": "EVERY brain-journal entry of one unit across the loaded recording (or a frame range), each once, with the frame it first appears at (CE-1265) — the full why-history of a finished exercise.",
    "http": {
      "method": "GET",
      "path": "/replay/entities/{networkId}/journal/history"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "from",
        "type": "number",
        "required": false,
        "description": "First frame (query, default 0)"
      },
      {
        "name": "to",
        "type": "number",
        "required": false,
        "description": "Last frame (query, default the last)"
      }
    ],
    "returns": "{ networkId, from, to, found, gap, count, entries:[{seq, at, kind, text, frame}] }",
    "notes": [
      "It walks the recording frame by frame, so a long file takes a moment; the replay's current frame is restored afterwards.",
      "Then seek_replay to an entry's frame - 1 and read get_replay_entity_brain to see what led to it.",
      "gap:true means entries were lost between two recorded frames (should not happen: every change dirties the unit's record)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "list every decision unit 1000 made in the exercise, and why"
    },
    "hint": "Req: networkId (number); optional from, to (frames, query). Example: get_replay_journal_history({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "load_replay",
    "group": "I — Recording / replay",
    "summary": "Load a .fdp recording into an ISOLATED ReplayBrowserContext.",
    "http": {
      "method": "POST",
      "path": "/replay/load"
    },
    "params": [
      {
        "name": "fdpPath",
        "type": "string",
        "required": true,
        "description": "Absolute path to the .fdp recording file"
      }
    ],
    "returns": "{ loaded:true, fdpPath, totalFrames, currentFrame }",
    "notes": [
      "While replay is active, /replay/entities returns entities from the sandbox (not the live world).",
      "Use list_replay_entities (not list_entities) while replaying."
    ],
    "example": {
      "args": {
        "fdpPath": "/path/to/recording.fdp"
      },
      "gist": "load a .fdp recording for inspection"
    },
    "hint": "Req: fdpPath (string, absolute path to .fdp file). Example: load_replay({fdpPath:\"/path/to/recording.fdp\"})",
    "manualVerify": false
  },

  {
    "name": "search_replay",
    "group": "I — Recording / replay",
    "summary": "Find the frames of the loaded recording where a condition holds (CE-1266) — the same condition shape breakpoints take (component field match, event, lifecycle, spatial, compound).",
    "http": {
      "method": "POST",
      "path": "/replay/search"
    },
    "params": [
      {
        "name": "condition",
        "type": "object",
        "required": true,
        "description": "The condition (SearchPredicateDto JSON, as add_breakpoint takes)"
      },
      {
        "name": "networkId",
        "type": "number",
        "required": false,
        "description": "Only this unit"
      },
      {
        "name": "limit",
        "type": "number",
        "required": false,
        "description": "How many hits to return (default 200)"
      }
    ],
    "returns": "{ count, truncated, hits:[{frame, networkId, context}] }",
    "notes": [
      "Over a recording, Changed has no start-of-tick copy to compare with, so it matches every frame the unit's record was written — combine it with a value test, or use get_replay_journal_history for decisions."
    ],
    "example": {
      "args": {
        "condition": {
          "$type": "PropertyMatch",
          "ComponentType": "Fdp.Toolkit.Behavior.Components.BehaviorState",
          "PropertyPath": "ActiveBehaviorHash",
          "Operator": "Changed",
          "Predicate": {
            "$type": "Numeric"
          }
        },
        "networkId": 1000,
        "limit": 10
      },
      "gist": "find when something happened in the recording"
    },
    "hint": "Req: condition (object, a SearchPredicateDto). Optional networkId (number), limit (number, default 200). Example: search_replay({condition:{\"$type\":\"PropertyMatch\",\"ComponentType\":\"Fdp.Toolkit.Behavior.Components.BehaviorState\",\"PropertyPath\":\"ActiveBehaviorHash\",\"Operator\":\"Changed\",\"Predicate\":{\"$type\":\"Numeric\"}}, networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "seek_replay",
    "group": "I — Recording / replay",
    "summary": "Seek to a specific frame in the ISOLATED sandbox. Does NOT touch the live world.",
    "http": {
      "method": "POST",
      "path": "/replay/seek"
    },
    "params": [
      {
        "name": "frame",
        "type": "number",
        "required": true,
        "description": "Frame index to seek to (0-based)"
      }
    ],
    "returns": "{ frame, totalFrames }",
    "notes": [
      "Isolation guarantee: does NOT touch the live world."
    ],
    "example": {
      "args": {
        "frame": 0
      },
      "gist": "seek replay to frame 0 (start)"
    },
    "hint": "Req: frame (number, 0-based). Example: seek_replay({frame:0})",
    "manualVerify": false
  },

  {
    "name": "get_replay_selection",
    "group": "I — Recording / replay",
    "summary": "The operator's selection at the current replay frame (CE-1265) — the unit they were pointing at then (the selection is recorded).",
    "http": {
      "method": "GET",
      "path": "/replay/selection"
    },
    "params": [],
    "returns": "{ frame, primary, selection:[networkId] }",
    "notes": [
      "To find WHEN the operator selected something, search_replay a Changed condition on SelectionState.IsSelected."
    ],
    "example": {
      "args": {},
      "gist": "which unit the operator had selected at this frame"
    },
    "hint": "No params. Example: get_replay_selection({})",
    "manualVerify": false
  },

  {
    "name": "get_replay_status",
    "group": "I — Recording / replay",
    "summary": "Replay sandbox status.",
    "http": {
      "method": "GET",
      "path": "/replay/status"
    },
    "params": [],
    "returns": "{ replayActive, currentFrame, totalFrames }",
    "example": {
      "args": {},
      "gist": "check if replay is active and current frame"
    },
    "hint": "No params. Example: get_replay_status({})",
    "manualVerify": false
  },

  {
    "name": "step_replay",
    "group": "I — Recording / replay",
    "summary": "Step one frame forward or backward in the ISOLATED sandbox. Does NOT touch the live world.",
    "http": {
      "method": "POST",
      "path": "/replay/step"
    },
    "params": [
      {
        "name": "dir",
        "type": "string",
        "required": false,
        "description": "Step direction: \"forward\" or \"back\". Default: \"forward\"",
        "enum": [
          "forward",
          "back"
        ],
        "default": "forward"
      }
    ],
    "returns": "{ stepped:bool, frame, totalFrames }",
    "notes": [
      "Isolation guarantee: does NOT touch the live world."
    ],
    "example": {
      "args": {
        "dir": "forward"
      },
      "gist": "step one frame forward in the replay"
    },
    "hint": "No required params. Optional: dir (\"forward\"|\"back\", def \"forward\"). Example: step_replay({dir:\"forward\"})",
    "manualVerify": false
  },

  {
    "name": "unload_replay",
    "group": "I — Recording / replay",
    "summary": "Dispose the replay sandbox and return to live world queries.",
    "http": {
      "method": "POST",
      "path": "/replay/unload"
    },
    "params": [],
    "returns": "ok:true envelope.",
    "example": {
      "args": {},
      "gist": "unload replay sandbox when done inspecting"
    },
    "hint": "No params. Example: unload_replay({})",
    "manualVerify": false
  },

  // ── Group K — AI behavior traces ────────────────────────────────────────────

  {
    "name": "get_combat_detonations",
    "group": "K — AI behavior traces",
    "summary": "The last warhead bursts on this node (grenades, mortars, HE) — the warhead and where its numbers came from, every entity in reach with its stance, fragment exposure and blast barrier/shadow, the damage each effect did, and the doors breached (buildings Stage 6).",
    "http": {
      "method": "GET",
      "path": "/combat/detonations"
    },
    "params": [
      {
        "name": "last",
        "type": "number",
        "required": false,
        "description": "how many bursts (newest first; default 10, ring of 64)"
      }
    ],
    "returns": "{ count, returned, detonations:[{seq, tick, shooter, struck, burst, ammo, warhead, warheadSource, fragmentRadius, blastInjuryRadius, effects:[{entity, entityIndex, stance, bodyPoints, distance, fragmentExposure, fragmentFalloff, fragmentArmourChance, fragmentDamage, blastFalloff, blastBarrier, blastShadow, blastDamage, totalDamage, shieldedBy, at}], doorsBreached:[key], rays:[{to, transmission, stopAt}]}] } — newest first",
    "notes": [
      "Written by AreaEffectSystem as it decides each burst — never recomputed. Only bursts of a munition WITH a warhead are recorded (a rifle round has none).",
      "fragmentExposure = the mean over the target's body points (its stance) of the fragments through the terrain and past any body or vehicle; blastBarrier = what slabs/walls/shut doors let through; blastShadow = the diffraction factor behind an obstacle taller than the target.",
      "The entity the round struck is listed too (it is at the burst); its direct hit is a separate damage event.",
      "rays = every fragment line the map's blast layer draws (CE-3117): the body point, the fragment transmission 0..1, and the first obstacle on the way (a terrain piece where the fragments enter it, or the point nearest a blocking collider), null for a clear line."
    ],
    "example": {
      "args": {
        "last": 3
      },
      "gist": "find out why the prone soldier behind the wall survived the grenade"
    },
    "hint": "Optional: last (default 10, ring of 64). Example: get_combat_detonations({last:3})",
    "manualVerify": false
  },

  {
    "name": "get_combat_shots",
    "group": "K — AI behavior traces",
    "summary": "The last rounds fired on this node — the inputs each was fired with (penetration and its provenance, AQ85 sigma/deflection) and why it ended (hit, stopped by a wall, expired), with every terrain crossing (tuning T-4).",
    "http": {
      "method": "GET",
      "path": "/combat/shots"
    },
    "params": [
      {
        "name": "last",
        "type": "number",
        "required": false,
        "description": "how many records (newest first; default 20, ring of 256)"
      },
      {
        "name": "shooter",
        "type": "number",
        "required": false,
        "description": "only rounds fired by this network id"
      },
      {
        "name": "target",
        "type": "number",
        "required": false,
        "description": "only rounds fired at this network id"
      }
    ],
    "returns": "{ count, returned, shots:[{seq, tick, endTick, shooter, target, weaponIndex, muzzle, aim, ordinal, sigmaRad, deflectionRad, penetration, penetrationSource, damage, outcome:InFlight|Hit|StoppedByTerrain|Expired, end, hit, arrivingDamage, arrivingPenetration, raisedAt, firedAt, tripSeconds, caughtUpSeconds, crossings:[{kind, label, material, resistanceMmRha, roundPenetrationMm, chance, passed, at}]}] } — newest first; raisedAt/tripSeconds (CE-2180): the brain's synced sim time and the trip to this node (null when not stamped); caughtUpSeconds: how far a bomb was flown forward to start from its release",
    "notes": [
      "Written by the systems that decide each round (FireProcessing, Ballistics, HitResolution) — never recomputed, so it cannot disagree with what happened.",
      "Served where the fire chain runs (SimHost, Editor); elsewhere it says no round was fired on this node.",
      "A round that hit a unit lists only the crossings before the unit; arrivingDamage is what the damage step received."
    ],
    "example": {
      "args": {
        "last": 10
      },
      "gist": "find out why a rifleman's rounds never hurt the target behind the wall"
    },
    "hint": "Optional: last (default 20), shooter, target (network ids). Example: get_combat_shots({last:10, shooter:1001})",
    "manualVerify": false
  },

  {
    "name": "get_entity_brain",
    "group": "K — AI behavior traces",
    "summary": "What a unit is doing and why — the Brain tab as JSON (CE-1248): task, who ordered it, paused task, SOP, ROE, each channel's right-now, HSM regions, squad play, utility choices (observed units), contacts, and the brain journal's recent changes.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/brain"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ networkId, hasBrain, onBrainNode, doing, origin, urgency, sinceSeconds, paused, sop, roe, rightNow:[{channel, actionId, reason, hold, progress, needed}], regions:[{region, state, stateIndex}], squad, choices:[{decisionId, winner, previousWinner, margin, switches}], contacts, heardOnly, journal:{on, seq, changes:[{seq, at, kind, text}]} }",
    "notes": [
      "onBrainNode:false means this node does not run the unit's brain — only task / SOP / ROE are replicated here.",
      "choices fill only while the unit is observed (observe_trace or debug_unit); the journal is always on.",
      "journal.changes are newest first; poll get_entity_journal with ?since=<seq> for only the new ones."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "see what unit 1000 is doing, who ordered it, and what changed lately"
    },
    "hint": "Req: networkId (number). Read on the brain's perspective (Scenario on a cluster). Example: get_entity_brain({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "debug_unit",
    "group": "K — AI behavior traces",
    "summary": "\"Debug this unit\" on / off (CE-1257) — the same switch as the Brain tab and the map's AI trace menu: arms the unit's traces (BTree / HSM rings and the utility record), pins its map gizmos, and keeps the AI graph's live overlay on it instead of the selection. One unit at a time.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/debug"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "on",
        "type": "boolean",
        "required": false,
        "description": "true = start debugging this unit (moves it from any other), false = stop",
        "default": true
      }
    ],
    "returns": "{ networkId, debugging, debugged }",
    "notes": [
      "The operator sees it too: the gizmos pin on the map and the graph's highlight follows this unit.",
      "Then read get_entity_brain (choices fill), get_entity_trace, and arm break_on_journal."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "on": true
      },
      "gist": "watch unit 1000 closely, live"
    },
    "hint": "Req: networkId (number); optional on (bool, default true). Editor only. Example: debug_unit({networkId:1000, on:true})",
    "manualVerify": false
  },

  {
    "name": "get_entity_journal",
    "group": "K — AI behavior traces",
    "summary": "A unit's brain journal after a sequence number (CE-1249): every task start / pause / resume / end / clear / refusal (with its reason), SOP change, utility switch, move start and HSM region change, with its cause — the last 24, always on.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/journal"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "since",
        "type": "number",
        "required": false,
        "description": "Only entries with seq greater than this (query); pass the last seq you saw"
      }
    ],
    "returns": "{ networkId, on, seq, gap, entries:[{seq, at, kind, text}] } — oldest first; gap:true means more than 24 were written since `since` and the oldest are gone.",
    "notes": [
      "seq counts every entry ever written to the unit and never resets — use it to poll without repeats.",
      "A refusal repeated every tick is ONE entry whose time is refreshed.",
      "on:false — not a brain unit on this node, or the host was started with the journal switched off."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "since": 0
      },
      "gist": "list what changed in unit 1000's mind and why"
    },
    "hint": "Req: networkId (number); optional since (number, query, default 0). Example: get_entity_journal({networkId:1000, since:12})",
    "manualVerify": false
  },

  {
    "name": "get_entity_memory",
    "group": "K — AI behavior traces",
    "summary": "A unit's raw target memory, every slot — heard (anonymous) contacts included (CE-3144).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/memory"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ networkId, hasMemory, count, changeEpoch, anonymousSerial, slots:[{id, anonymous, position:[x,y,z], freshness, radius, lastSeenTick, modalities, sourceClass, alive?, networkId?}] }",
    "notes": [
      "GET /entities/{id} shows memory through the scenario translator, which drops heard slots and entities it cannot map; this route shows them all.",
      "Slots are sorted freshest first; a heard slot has a negative synthetic id and a radius."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read what a unit remembers — seen and heard contacts, how fresh, and where"
    },
    "hint": "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — memory is written there. Example: get_entity_memory({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_reports",
    "group": "K — AI behavior traces",
    "summary": "Reports up the chain of command (CE-2295, CC-12): on a commander its picture — the elements its subordinates reported, each subordinate's last progress / colours / age / overdue, open requests; on any unit the extra requirements its superior set and its outbox (queued, posture, muted).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/reports"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ networkId, now, onBrainNode?, picture?:{elements:[{from, size, count, activity, equipment, at, radius, observedAt, ageSeconds, receivedAt, seq, synthetic}], subordinates:[{networkId, name?, progress, strength, ammo, phase, lastSeq, lastReportAt, ageSeconds, overdue}], requests:[{from, kind, at, receivedAt, seq}]}, requirements?:{pir, ffir, sitrepEverySeconds, pirArea, setBy}, outbox?:{queued:[report + dueAt], nextSeq, posture, muted, mutedUntil} }",
    "notes": [
      "The INTERPRETED view; the raw components (CommandPicture, ReportingRequirements, ReportOutbox) are in get_entity, each buffer a plain array (CC-16).",
      "A report ages from observedAt (when the sender saw it), not from its arrival; overdue = nothing heard for 60 s."
    ],
    "example": {
      "args": {
        "networkId": 5001
      },
      "gist": "what has the platoon heard from its squads"
    },
    "hint": "Req: networkId of a commander (a platoon, a squad). Read on its BRAIN node (Scenario on a cluster). Example: get_entity_reports({networkId:5001})",
    "manualVerify": false
  },

  {
    "name": "fake_entity_report",
    "group": "K — AI behavior traces",
    "summary": "A report AS IF this unit's staff sent it to its superior (CE-2295, CC-13) — it enters before the wire, ages from observedAgoSeconds, and the superior's whole decision path runs as for a real one. Marked synthetic: always taken, replaced by the unit's next real report (CC-14).",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/reports"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "The unit the report comes FROM (its superior receives it)"
      },
      {
        "name": "kind",
        "type": "string",
        "required": true,
        "description": "Contact · Spot · Sitrep · Lace · Progress · Request"
      },
      {
        "name": "subtype",
        "type": "string",
        "required": false,
        "description": "Progress: Set · PhaseLine · Foothold · RoomClear · … · FireLifted · TaskFailed; Request: Casevac · FireSupport · Resupply · Reinforce · Permission"
      },
      {
        "name": "count",
        "type": "number",
        "required": false,
        "description": "Spot / Contact: how many (0 = the element is lost); PhaseLine: the line's index"
      },
      {
        "name": "size",
        "type": "string",
        "required": false,
        "description": "Individual · Team · SquadSize · PlatoonSize"
      },
      {
        "name": "at",
        "type": "array",
        "required": false,
        "description": "[x, y, z] where (default: the unit's own position)"
      },
      {
        "name": "strength",
        "type": "string",
        "required": false,
        "description": "Sitrep: Green · Amber · Red · Black"
      },
      {
        "name": "observedAgoSeconds",
        "type": "number",
        "required": false,
        "description": "how old the observation already is"
      }
    ],
    "returns": "{ networkId, sent:true, synthetic:true, report }",
    "notes": [
      "Run on any node that holds the unit; BR-3 carries the report to the superior's brain node."
    ],
    "example": {
      "args": {
        "networkId": 5002,
        "kind": "Spot",
        "size": "Team",
        "count": 3,
        "at": [
          200,
          320,
          0
        ]
      },
      "gist": "tell the platoon squad 5002 sees three enemies"
    },
    "hint": "Req: networkId of the SENDING unit (a squad), kind. Example: fake_entity_report({networkId:5002, kind:'Spot', size:'Team', count:3, at:[200,320,0]})",
    "manualVerify": false
  },

  {
    "name": "mute_entity_reports",
    "group": "K — AI behavior traces",
    "summary": "Lost comms (CE-2295, CC-15): the unit's staff sends nothing for `seconds` — CONTACT included; its outbox keeps filling. 0 restores the radio.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/reports/mute"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "The unit whose radio fails"
      },
      {
        "name": "seconds",
        "type": "number",
        "required": true,
        "description": "How long (0 = restore)"
      }
    ],
    "returns": "{ networkId, mutedSeconds }",
    "example": {
      "args": {
        "networkId": 5002,
        "seconds": 60
      },
      "gist": "squad 5002 goes silent for a minute"
    },
    "hint": "Req: networkId, seconds. Run on the unit's BRAIN node (the outbox lives there). Example: mute_entity_reports({networkId:5002, seconds:60})",
    "manualVerify": false
  },

  {
    "name": "set_entity_sector",
    "group": "K — AI behavior traces",
    "summary": "Set a man's or a commander's sector — the arc he may choose to shoot into, where he watches, his NoFire areas (CE-2298, DA-26).",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/sector"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "left",
        "type": "number",
        "required": false,
        "description": "Arc's left bearing (compass degrees)"
      },
      {
        "name": "right",
        "type": "number",
        "required": false,
        "description": "Arc's right bearing (compass degrees)"
      },
      {
        "name": "minRange",
        "type": "number",
        "required": false,
        "description": "Arc's min range (m)"
      },
      {
        "name": "maxRange",
        "type": "number",
        "required": false,
        "description": "Arc's max range (m); 0 = none"
      },
      {
        "name": "watch",
        "type": "number",
        "required": false,
        "description": "Where he looks (compass degrees)"
      },
      {
        "name": "allowArea",
        "type": "number",
        "required": false,
        "description": "An area entity's network id he may shoot into"
      },
      {
        "name": "noFire",
        "type": "array",
        "required": false,
        "description": "Up to four area entities' network ids no round may end in"
      }
    ],
    "returns": "{ networkId, arc:{left,right,minRange,maxRange}|null, watch|null, allowArea|null, noFire, setBy } — or { networkId, forwarded:true } when his brain is elsewhere",
    "notes": [
      "Origin Operator through the one writer (EngagementAreaSystem.Apply): it replaces the whole sector; a higher setter refuses it.",
      "The arc's origin is where he stands now. On a node that does not run his brain the sector is forwarded to the one that does (kind 4) — the answer says forwarded.",
      "His eyes follow it: a holding or fighting man looks at his sector when no threat or sound holds them (Look Direction §3.3a)."
    ],
    "example": {
      "args": {
        "networkId": 5301,
        "watch": 0
      },
      "gist": "point a sentry north, or give a man his arc of fire"
    },
    "hint": "Req: networkId. Body (all optional): left, right (compass, 0 = north, clockwise), minRange, maxRange, watch (compass), allowArea, noFire:[areaIds]. Empty body withdraws. Example: set_entity_sector({networkId:5301, watch:0})",
    "manualVerify": false
  },

  {
    "name": "get_entity_sensors",
    "group": "K — AI behavior traces",
    "summary": "Every sensor of a unit — kind, result family and last answer in its own shape (CE-3072).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/sensors"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ count, sensors:[{partId, kind, family:Ranked|Area, templateId, epoch, suspended, origin:tkb|behaviour|other, answer:{ready, count, lastUpdateTick, top:[{entityId, score, position}]} | {ready, count, lastUpdateTick, areas:[{featureId, kind, threat, distanceAlongRoute, center, nearHandle, farHandle}]}, settings? }] }",
    "notes": [
      "A Ranked sensor lists its top 5 results; an Area sensor (the danger-area sensor) lists its areas, the next one ahead first.",
      "answer.ready is false until the first answer — an Area answer with count 0 and ready true means a clear route.",
      "settings appear for an Area sensor: which route it watches (OwnMove | Handle | ToPoint) and its corridor."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read which danger areas a unit sees ahead on its route, and how threatened each is"
    },
    "hint": "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — the answers land there. Example: get_entity_sensors({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_slots",
    "group": "K — AI behavior traces",
    "summary": "Every behaviour occurrence slot a unit carries — the task's, the SOP's, a paused task's, hosted children's — named and decoded (CE-2288).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/slots"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ networkId, count, slots:[{key, kind, payloadSize, structureHash, owner (Task|Sop|PausedTask|OwnerUnknown), behaviour, role (Params|State|Hsm|Working|HostedChild|RoleUnknown), label, layout, value}] }",
    "notes": [
      "GET /entities/{id} projects only the running task's params; this route lists every slot, including a hosted drill's own block and the SOP's.",
      "A slot stores no owner: each is named by recomputing the keys of the task, the SOP and a paused task; an unmatched slot is listed with owner OwnerUnknown.",
      "value is the slot decoded through its behaviour's layout (params, a hosted child's params, a stateful node's working state); null when no layout is known."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "see a commander's whole brain — the play, the SOP, a paused task and the drill it hosts"
    },
    "hint": "Req: networkId (number). Read on the Brain perspective (Scenario on a cluster) — the slots live where the brain runs. Example: get_entity_slots({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_squad",
    "group": "K — AI behavior traces",
    "summary": "A squad as its commander sees it — members with their assigned fire targets, and the merged contact pool (CE-3087).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/squad"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "commander: { role:'commander', memberCount, members:[{slot, networkId, name, assignment:{networkId, name, score, focusFireCount}|null}], lastMergeTick, contactCount, contacts:[{networkId, name | heard:true, threat, position, sources, lastSeenTick}], maneuver:{active, kind, kindName, phase, phaseName, phaseSince, members:[{slot, networkId, name, order:{kind, point, cover, issuedAt, target}|null}], history:[{t, phase, phaseName, event}], gates?:{covering, set}, care?:{casualty, medic, stage, coverPoint}}|null } · member: { role:'member', commander:{networkId, name}, assignment } · otherwise { role:'none' }",
    "notes": [
      "The assignment is the leader's fire distribution (CE-3088): recomputed after each merge of the pool (≈ 10 Hz), at most 2 members per target.",
      "An assignment is a consideration in the member's own threat ranking, not an order — a hurt member may still break off (its posture).",
      "Heard contacts (no identity) appear in the pool with heard:true and are never assigned.",
      "maneuver (CE-2157) is the squad drill the commander runs: its phase, the last 16 phase changes with the event that moved each (Start, BoundComplete, DefiladeReached, Timeout, End, ...), and the order each member last got. null when no drill ever ran."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read which target each squad member is assigned, what the squad knows, and which drill phase it is in"
    },
    "hint": "Req: networkId (number) of a commander or a member. Read on the Brain perspective (Scenario on a cluster). Example: get_entity_squad({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_trace",
    "group": "K — AI behavior traces",
    "summary": "Extract AI behavior trace for an entity.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/trace"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.",
    "notes": [
      "Arm the entity with observe_trace first to populate trace data.",
      "Returns tier field indicating the AI tier type (BTree/HSM/blueprint)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read AI behavior trace for entity 1000 after arming"
    },
    "hint": "Req: networkId (number). Must call observe_trace({networkId,on:true}) first. Example: get_entity_trace({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_utility",
    "group": "K — AI behavior traces",
    "summary": "What an observed unit's utility decisions chose, and why (CE-3069).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/utility"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      }
    ],
    "returns": "{ observed, decisions:[{decision, kind, winner, previousWinner, switchCount, evalCount, margin, hysteresis, ranked:[{option|networkId,name, score}]}], lastPass:{winnerOptionId, winnerScore, margin, options:[{optionIndex, considerations:[{input, raw, curved, weight}]}]} } — scores are AFTER hysteresis.",
    "notes": [
      "observe_trace arms the BTree/HSM trace AND the utility record — one switch.",
      "One entry per decision the unit ran while observed (up to 4); a ranking's winner is a candidate entity {networkId, name}.",
      "switchCount counts winner changes since arming — poll it to see a posture switch.",
      "lastPass is the most recent option decision's per-consideration breakdown; rankings trace only their winner."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read which posture a unit chose and the scores behind it"
    },
    "hint": "Req: networkId (number). Arm with observe_trace({networkId,on:true}) on the Brain perspective (Scenario on a cluster), let the sim tick, then read. Example: get_entity_utility({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_weapons",
    "group": "K — AI behavior traces",
    "summary": "A unit's weapon mounts (owner = mount 0, then mount children) — ammo, cooldown, TKB numbers; with ?target= the WeaponSelection inputs per mount and the chosen mount (CE-3089).",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/weapons"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "target",
        "type": "number",
        "required": false,
        "description": "Network id of a target to score the mounts against (query)"
      }
    ],
    "returns": "{ count, mountChildrenEnabled, mounts:[{index, on:owner|child, ammo, maxAmmo, cooldown, tkb:{range, penetration, damage, muzzleVelocity}|null, inputs?:{hasAmmo, rangeFit, effectiveness, readiness, roundsLeft}}], choice? }",
    "notes": [
      "choice is the mount the AimAndFire executor fires on Mount=Auto (the posture's fire step): 0 = the owner's primary.",
      "rangeFit is distance / mount range (≈ 1 inside the range in the decision's curve); 10 = unknown."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "target": 1002
      },
      "gist": "see which weapon a unit would fire at a target, and why"
    },
    "hint": "Req: networkId (number); optional target (number, query). Read on the Brain perspective. Example: get_entity_weapons({networkId:1000, target:1002})",
    "manualVerify": false
  },

  {
    "name": "explain_line_of_sight",
    "group": "K — AI behavior traces",
    "summary": "Why one unit does or does not see another, as this node's perception decides it — eye and aim heights, stances, every terrain crossing with its transmittance, and any entity in the way (tuning T-4).",
    "http": {
      "method": "GET",
      "path": "/perception/los"
    },
    "params": [
      {
        "name": "observer",
        "type": "number",
        "required": true,
        "description": "the seeing unit's network id"
      },
      {
        "name": "target",
        "type": "number",
        "required": true,
        "description": "the seen unit's network id"
      }
    ],
    "returns": "{ observer, target, visible, verdict, eye, eyeHeight, observerStance, targetStance, threshold, points:[{height, aim, clear, verdict, terrainTransmittance, crossed:[{along, kind, label, material, transmittance, building, storey}], blockingEntity}], note }",
    "notes": [
      "A dry run of the SAME strategy perception composes (TerrainWorldLosStrategy.ForLiveWorld) — it cannot disagree with it.",
      "Answers where perception runs with the entities resident (SimHost, Editor); 404 when either unit is not on this node.",
      "Stage 4: the target is SEEN when ANY body point (per its logical stance) has a clear line; every point is listed with its own verdict."
    ],
    "example": {
      "args": {
        "observer": 1001,
        "target": 1002
      },
      "gist": "find out which wall hides the enemy from the rifleman"
    },
    "hint": "Req: observer, target (network ids). Example: explain_line_of_sight({observer:1001, target:1002})",
    "manualVerify": false
  },

  {
    "name": "get_squad_reports",
    "group": "K — AI behavior traces",
    "summary": "The reports this node's inbox saw, newest first (CE-2295, PC-11) — folded, stale (a late older one dropped) or not addressed here; a ring of 256 per node.",
    "http": {
      "method": "GET",
      "path": "/squad/reports"
    },
    "params": [
      {
        "name": "last",
        "type": "number",
        "required": false,
        "description": "How many (default 50)"
      },
      {
        "name": "unit",
        "type": "number",
        "required": false,
        "description": "Only reports from or to this unit"
      }
    ],
    "returns": "{ count, returned, reports:[{serial, seenAt, from, to, originNode, verdict, kind, subtype, at, observedAt, ageSeconds, seq, synthetic, …}] }",
    "notes": [
      "⛔ Not GET /events: that ring holds 500 events of every type and a fight's shots evict a report within seconds."
    ],
    "example": {
      "args": {
        "last": 20
      },
      "gist": "the last reports up the chain"
    },
    "hint": "Optional: last (default 50), unit (from or to). Read on a brain node. Example: get_squad_reports({last:20, unit:5001})",
    "manualVerify": false
  },

  {
    "name": "observe_trace",
    "group": "K — AI behavior traces",
    "summary": "Arm or disarm AI behavior trace buffer allocation for an entity.",
    "http": {
      "method": "POST",
      "path": "/trace/observe"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "on",
        "type": "boolean",
        "required": true,
        "description": "true to arm tracing, false to disarm"
      }
    ],
    "returns": "{ armed, networkId }",
    "notes": [
      "Must arm before get_entity_trace will return populated trace data.",
      "Without arming, get_entity_trace returns empty trace."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "on": true
      },
      "gist": "arm AI behavior tracing for entity 1000"
    },
    "hint": "Req: networkId (number), on (bool). Example: observe_trace({networkId:1000,on:true})",
    "manualVerify": false
  },

  // ── Group L — Mutation / fault injection ────────────────────────────────────

  {
    "name": "get_attributes_schema",
    "group": "L — Mutation / fault injection",
    "summary": "Return all patchable attribute paths and their JSON Schema.",
    "http": {
      "method": "GET",
      "path": "/attributes/schema"
    },
    "params": [],
    "returns": "{ registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, GeoPosition.*, Heading, …).",
    "notes": [
      "Use patch_attribute to apply a patch using these paths.",
      "Paths not in registeredPaths are silently ignored by patch_attribute."
    ],
    "example": {
      "args": {},
      "gist": "discover patchable attribute paths before calling patch_attribute"
    },
    "hint": "No params. Example: get_attributes_schema({})",
    "manualVerify": false
  },

  {
    "name": "patch_attribute",
    "group": "L — Mutation / fault injection",
    "summary": "Apply a JSON attribute patch to an entity — locally where this node owns it, by request to the owner where it does not.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/attribute"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "patchJson",
        "required": true,
        "description": "Patch as a JSON object {\"Name\":\"Alpha\"} or as a JSON string"
      }
    ],
    "returns": "Updated entity dump on success, plus write:{route, appliedComponents, requestedComponents}; route is direct, requested or noMatch.",
    "notes": [
      "Unregistered keys are silently ignored (no error) — write.route is then noMatch.",
      "CE-3003: a key on a component this node does NOT own is sent to its owner as an UpdateEntityAttributeRequest (the JSON arm the owner already applies) — write.route is requested, and the change is in the OWNER's world, not yet in this dump: read it back from the owner's perspective after a tick. Components this node owns in the same patch are applied here.",
      "CE-3003 / CE-191: a key on a component this node does not own and no network descriptor carries (a node-local component, or a networkless host) has no owner to ask — the route REFUSES with 400 instead of answering ok.",
      "patchJson may be a nested JSON object like {\"Name\":\"Alpha\"} or a JSON string."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "patchJson": {
          "Name": "Alpha"
        }
      },
      "gist": "rename entity 1000 to Alpha"
    },
    "hint": "Req: networkId (number), patchJson (object {\"Name\":\"Alpha\"} or JSON string). Example: patch_attribute({networkId:1000,patchJson:{Name:\"Alpha\"}})",
    "manualVerify": false
  },

  {
    "name": "edit_component",
    "group": "L — Mutation / fault injection",
    "summary": "StructEdit escape hatch for arbitrary component fields.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/component"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID (long)"
      },
      {
        "name": "componentType",
        "type": "string",
        "required": true,
        "description": "ECS component type name (e.g. \"EntityInfo\", \"SimTransform\")"
      },
      {
        "name": "patch",
        "type": "object",
        "required": true,
        "description": "JSON object with field names and new values to apply to the component"
      }
    ],
    "returns": "Updated entity component state. Invalid values → 400, component unchanged.",
    "notes": [
      "Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, and writes the result back to ECS.",
      "Invalid values → 400, component unchanged.",
      "CE-3003: a component another node owns (a network descriptor carries it and this node holds no authority) is REFUSED with 400 — writing it here would change only this node's replica, which the owner overwrites. Use patch_attribute (it asks the owner) or select the owner's perspective (get_entity_ownership names it).",
      "For fields registered in the attribute schema, prefer patch_attribute."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "componentType": "SimTransform",
        "patch": {
          "Position": {
            "X": 999,
            "Y": 0,
            "Z": 0
          }
        }
      },
      "gist": "set SimTransform Position.X to 999 for entity 1000"
    },
    "hint": "Req: networkId (number), componentType (string from list_component_types), patch (object). Example: edit_component({networkId:1000,componentType:\"SimTransform\",patch:{...}})",
    "manualVerify": false
  },

  // ── Group M (TKB) — Entity-type catalog ─────────────────────────────────────

  {
    "name": "resolve_entity_type_parameters",
    "group": "M (TKB) — Entity-type catalog",
    "summary": "Every combat/perception parameter of a TKB type with its value and where it came from (buildings Stage 0) — or, with ammo/weapon, the generated reference library's launcher × ammo pair (tuning T-1).",
    "http": {
      "method": "GET",
      "path": "/tkb/resolve"
    },
    "params": [
      {
        "name": "type",
        "type": "number",
        "required": false,
        "description": "TKB type ID (long)"
      },
      {
        "name": "ammo",
        "type": "string",
        "required": false,
        "description": "reference-library ammo name (e.g. '5.56x45 ball') — answers the generated pair instead of a type"
      },
      {
        "name": "weapon",
        "type": "string",
        "required": false,
        "description": "reference-library weapon name (e.g. 'M4_Carbine'); omitted or not firing this ammo ⇒ the ammo's generic profile"
      },
      {
        "name": "library",
        "type": "string",
        "required": false,
        "description": "any value with no type/ammo: list the whole reference library"
      }
    ],
    "returns": "type: { tkbType, name, disType, engineFallbacks, parameters:[{name, value, provenance:Explicit|Generated|ReferenceByDis|ReferenceByName|EngineFallback|NotApplicable, source}] } · ammo: { ammo, weapon, found, weaponMatched, muzzleSpeed, penetrationMm, penetrationFormula, damage, damageFormula, driving } · library=1: { ammo:[…], weapons:[…] }",
    "notes": [
      "The values are the ones the simulation uses: the translators and the fire chain call the same resolver rules.",
      "Generated = a builder derived it by formula (source names the formula and its inputs); EngineFallback = the type said nothing and an engine default applies.",
      "engineFallbacks counts the parameters on engine defaults — the place to look when a type behaves 'generically'.",
      "ReferenceByName = the generated reference library (tuning T-1): a mount's loaded ammo named by its TKB type's name, when the TKB states no penetration; the formula and its inputs are in source."
    ],
    "example": {
      "args": {
        "type": 100
      },
      "gist": "see which of the M1's numbers are stated, derived or engine defaults"
    },
    "hint": "Req: type (number — tkbType from list_entity_types), OR ammo (+ optional weapon), OR library=1. Example: resolve_entity_type_parameters({type:100}) · resolve_entity_type_parameters({ammo:'12.7x99 AP', weapon:'M2HB'})",
    "manualVerify": false
  },

  {
    "name": "list_entity_types",
    "group": "M (TKB) — Entity-type catalog",
    "summary": "List entity types (TKB templates) with id, name, category, disType.",
    "http": {
      "method": "GET",
      "path": "/tkb/types"
    },
    "params": [
      {
        "name": "category",
        "type": "string",
        "required": false,
        "description": "Filter by category path"
      }
    ],
    "returns": "[{tkbType, name, categoryPath, disType}]",
    "example": {
      "args": {
        "category": "Vehicle"
      },
      "gist": "list all TKB types in the Vehicle category"
    },
    "hint": "No required params. Optional: category (string). Example: list_entity_types({})",
    "manualVerify": false
  },

  {
    "name": "get_entity_type",
    "group": "M (TKB) — Entity-type catalog",
    "summary": "Full TKB descriptor: mandatory components, child blueprints, DIS type, and descriptor DTOs.",
    "http": {
      "method": "GET",
      "path": "/tkb/types/{tkbType}"
    },
    "params": [
      {
        "name": "tkbType",
        "type": "number",
        "required": true,
        "description": "TKB type ID (long)"
      }
    ],
    "returns": "Full TKB descriptor including mandatory components, child blueprints, descriptors. No spawn.",
    "example": {
      "args": {
        "tkbType": 1101
      },
      "gist": "inspect TKB descriptor for type 1101"
    },
    "hint": "Req: tkbType (number/long from list_entity_types). Example: get_entity_type({tkbType:1101})",
    "manualVerify": false
  },

  // ── Group N — World / coordinates ───────────────────────────────────────────

  {
    "name": "list_doors",
    "group": "N — World / coordinates",
    "summary": "The doors the resident terrain defines, by terrain-object key, with their live state (what sight and fire see) and the door entity that stands for each (buildings Stage 5b).",
    "http": {
      "method": "GET",
      "path": "/doors"
    },
    "params": [],
    "returns": "{ terrain, count, doors:[{key, state:Open|Closed|Locked|Destroyed, initial, source, x, y, sillZ, building, storey, runtimeId, entityState}] }",
    "notes": [
      "key = '<terrain>/<building>/<doorId>', a string — never a network id. runtimeId is the door ENTITY's network id (created by the scenario load), null before it exists.",
      "state = the terrain's live state; entityState = the door entity's replicated DoorState. They differ only for the frame before the mirror runs."
    ],
    "example": {
      "args": {},
      "gist": "see which doors of the range are locked"
    },
    "hint": "No params. Example: list_doors({})",
    "manualVerify": false
  },

  {
    "name": "get_terrain_levels",
    "group": "N — World / coordinates",
    "summary": "The terrain levels at a point — ground = level 0, storeys/slabs/roofs above, basements below (buildings Stage 1).",
    "http": {
      "method": "GET",
      "path": "/terrain/levels"
    },
    "params": [
      {
        "name": "x",
        "type": "number",
        "required": true,
        "description": "Local X (m, east)"
      },
      {
        "name": "y",
        "type": "number",
        "required": true,
        "description": "Local Y (m, north)"
      }
    ],
    "returns": "{ terrain, x, y, levels:[{level, z, kind:ground|above|below}] } — or { terrain:null } when no terrain is resident",
    "notes": [
      "The same levels Add Entity's SpawnHeight resolves (TerrainWorld.SurfacesAt): surfaces within 0.3 m of the ground merge into level 0."
    ],
    "example": {
      "args": {
        "x": 102,
        "y": 104
      },
      "gist": "list the floors a unit could be placed on inside a building"
    },
    "hint": "Req: x, y (local metres). Example: get_terrain_levels({x:102, y:104})",
    "manualVerify": false
  },

  {
    "name": "query_terrain",
    "group": "N — World / coordinates",
    "summary": "Dry-run terrain trace between two points — every crossed wall/fence/floor with its material and, per purpose, its transmittance (sight) or ballistic resistance and the round's chance through it (fire).",
    "http": {
      "method": "GET",
      "path": "/terrain/query"
    },
    "params": [
      {
        "name": "from",
        "type": "string",
        "required": true,
        "description": "Start 'x,y,z' (local metres)"
      },
      {
        "name": "to",
        "type": "string",
        "required": true,
        "description": "End 'x,y,z' (local metres)"
      },
      {
        "name": "purpose",
        "type": "string",
        "required": false,
        "description": "sight (default) or fire (sound/fragment/blast arrive with their solvers)"
      },
      {
        "name": "penetration",
        "type": "number",
        "required": false,
        "description": "fire: the round's penetration, mm RHA (0 = unknown round — the engine fallback meets the terrain)"
      },
      {
        "name": "damage",
        "type": "number",
        "required": false,
        "description": "fire: the round's damage (0 = the flat default)"
      }
    ],
    "returns": "sight: { terrain, purpose, transmittance, seesThrough, threshold, length, crossed:[{along, kind:panel|prism|slab|ramp, label, material, transmittance, building, storey}], note } · fire: { terrain, purpose, penetration, length, stopped, stopAlong, arrivingDamage, arrivingPenetration, crossed:[{along, kind, label, material, pathMetres, resistanceMmRha, roundPenetrationMm, chance, passes, building, storey}], note }",
    "notes": [
      "Transmittance multiplies along the line (chain-link 0.85, hedge 0.3, solid walls 0); seesThrough = transmittance >= 0.5.",
      "Perception uses the same rule (TerrainWorld.SegmentBlocked = transmittance < 0.5), so this answer is what a sensor sees.",
      "fire (R-217): resistance = material mm RHA/m × the path inside the piece; chance = ArmorModel.PenetrationChance(round, resistance); damage multiplies by each chance; a crossing the round cannot pass stops it — exactly what bullets do."
    ],
    "example": {
      "args": {
        "from": "102,90,1.6",
        "to": "102,104,1.6"
      },
      "gist": "check whether a window lets a soldier outside see into a room"
    },
    "hint": "Req: from, to ('x,y,z' local metres). Optional: purpose (sight|fire), penetration (mm RHA, fire), damage (fire). Example: query_terrain({from:'102,90,1.6', to:'102,104,1.6', purpose:'fire', penetration:'5'})",
    "manualVerify": false
  },

  {
    "name": "geo_to_local",
    "group": "N — World / coordinates",
    "summary": "Convert geographic coordinates to local ENU {x,y,z}.",
    "http": {
      "method": "POST",
      "path": "/world/geo-to-local"
    },
    "params": [
      {
        "name": "lat",
        "type": "number",
        "required": true,
        "description": "Latitude (degrees)"
      },
      {
        "name": "lon",
        "type": "number",
        "required": true,
        "description": "Longitude (degrees)"
      },
      {
        "name": "alt",
        "type": "number",
        "required": true,
        "description": "Altitude (meters)"
      },
      {
        "name": "headingDeg",
        "type": "number",
        "required": false,
        "description": "Optional heading (degrees CW from North) → rotation quaternion"
      }
    ],
    "returns": "{ x, y, z, rotation? } — optional rotation if headingDeg was provided.",
    "notes": [
      "Optional headingDeg → adds rotation quaternion to response."
    ],
    "example": {
      "args": {
        "lat": 50.0755,
        "lon": 14.4378,
        "alt": 200
      },
      "gist": "convert Prague geo coords to local ECS metres"
    },
    "hint": "Req: lat, lon, alt (all numbers). Optional: headingDeg (number). Example: geo_to_local({lat:50.0,lon:14.0,alt:200})",
    "manualVerify": false
  },

  {
    "name": "get_world_info",
    "group": "N — World / coordinates",
    "summary": "World metadata: geo origin, the perception and collider grids (live, placed over the terrain), the resident terrain and the navmesh.",
    "http": {
      "method": "GET",
      "path": "/world/info"
    },
    "params": [],
    "returns": "{ geo:{origin:{lat,lon,alt}}, spatialGrid:{cellSize,originX,originY,width,height,extent}, colliderGrid:{...}, terrain:{name,bounds,groundZ,prisms,walkables,surfaces}|null, navmesh:{provider,baked,version}|null }",
    "notes": [
      "Reads the ACTIVE PERSPECTIVE's world (on --mode all set the perspective first, e.g. SimHost).",
      "terrain is null when no terrain is resident; navmesh is null on a node that composes no navigation solver.",
      "navmesh.baked is false until a terrain's navmesh is published (the provider is then the straight-line fallback)."
    ],
    "example": {
      "args": {},
      "gist": "get world geo origin and spatial grid extent"
    },
    "hint": "No params. Example: get_world_info({})",
    "manualVerify": false
  },

  {
    "name": "local_to_geo",
    "group": "N — World / coordinates",
    "summary": "Convert local ENU {x,y,z} to geographic coordinates.",
    "http": {
      "method": "POST",
      "path": "/world/local-to-geo"
    },
    "params": [
      {
        "name": "x",
        "type": "number",
        "required": true,
        "description": "Local X (meters East)"
      },
      {
        "name": "y",
        "type": "number",
        "required": true,
        "description": "Local Y (meters Up)"
      },
      {
        "name": "z",
        "type": "number",
        "required": true,
        "description": "Local Z (meters North)"
      },
      {
        "name": "rotation",
        "type": "object",
        "required": false,
        "description": "Optional quaternion {x,y,z,w} → headingDeg in response",
        "properties": {
          "x": {
            "type": "number"
          },
          "y": {
            "type": "number"
          },
          "z": {
            "type": "number"
          },
          "w": {
            "type": "number"
          }
        }
      }
    ],
    "returns": "{ lat, lon, alt, headingDeg? } — Heading: North=0°, East=90°.",
    "notes": [
      "Optional rotation quaternion {x,y,z,w} → adds headingDeg to response.",
      "Heading convention: North=0°, East=90°."
    ],
    "example": {
      "args": {
        "x": 100,
        "y": 0,
        "z": 50
      },
      "gist": "convert local ECS position (100,0,50) to geographic coords"
    },
    "hint": "Req: x, y, z (all numbers). Optional: rotation ({x,y,z,w}). Example: local_to_geo({x:100,y:0,z:50})",
    "manualVerify": false
  },

  // ── Group O — Manual-assist (focus / annotations) ───────────────────────────

  {
    "name": "add_annotation",
    "group": "O — Manual-assist (focus / annotations)",
    "summary": "Draw a debug primitive (sphere, anchor, or line) in the gizmo buffer. MANUAL-VERIFY: gizmo render requires windowed session.",
    "http": {
      "method": "POST",
      "path": "/annotations"
    },
    "params": [
      {
        "name": "type",
        "type": "string",
        "required": true,
        "description": "Annotation type",
        "enum": [
          "sphere",
          "anchor",
          "line"
        ]
      },
      {
        "name": "networkId",
        "type": "number",
        "required": false,
        "description": "Entity network ID (anchor only)"
      },
      {
        "name": "x",
        "type": "number",
        "required": false,
        "description": "World X coordinate"
      },
      {
        "name": "y",
        "type": "number",
        "required": false,
        "description": "World Y coordinate"
      },
      {
        "name": "z",
        "type": "number",
        "required": false,
        "description": "World Z coordinate"
      },
      {
        "name": "radius",
        "type": "number",
        "required": false,
        "description": "Sphere radius in metres"
      },
      {
        "name": "heading",
        "type": "number",
        "required": false,
        "description": "Heading in degrees (anchor)"
      },
      {
        "name": "color",
        "type": "string",
        "required": false,
        "description": "Hex color string e.g. \"#FF0000\""
      },
      {
        "name": "from",
        "type": "object",
        "required": false,
        "description": "Line start point {x,y,z}",
        "properties": {
          "x": {
            "type": "number"
          },
          "y": {
            "type": "number"
          },
          "z": {
            "type": "number"
          }
        }
      },
      {
        "name": "to",
        "type": "object",
        "required": false,
        "description": "Line end point {x,y,z}",
        "properties": {
          "x": {
            "type": "number"
          },
          "y": {
            "type": "number"
          },
          "z": {
            "type": "number"
          }
        }
      }
    ],
    "returns": "{ added: true, primitiveIndex, bufferCount } on success.",
    "notes": [
      "\"sphere\" — x, y, z, radius (float), optional color (hex \"#RRGGBB\").",
      "\"anchor\" — networkId, x, y, z, optional heading (float).",
      "\"line\" — from:{x,y,z}, to:{x,y,z}, optional color.",
      "The buffer write is headless-verifiable; the actual gizmo render requires a windowed session (MANUAL-VERIFY)."
    ],
    "example": {
      "args": {
        "type": "sphere",
        "x": 100,
        "y": 0,
        "z": 50,
        "radius": 10,
        "color": "#FF4400"
      },
      "gist": "draw a red sphere at (100,0,50) with radius 10"
    },
    "hint": "Req: type (\"sphere\"|\"anchor\"|\"line\"). For sphere: x,y,z,radius. For line: from:{x,y,z},to:{x,y,z}. Example: add_annotation({type:\"sphere\",x:0,y:0,z:0,radius:5})",
    "manualVerify": true
  },

  {
    "name": "focus_entity",
    "group": "O — Manual-assist (focus / annotations)",
    "summary": "Pan and zoom the map canvas to an entity. MANUAL-VERIFY: camera move requires windowed session.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/focus"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network entity ID to center the view on"
      }
    ],
    "returns": "{ focused: true } on success.",
    "notes": [
      "Publishes CenterOnEntityCommand (headless-verifiable via event history).",
      "The actual camera move only occurs in a windowed session (MANUAL-VERIFY)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "center editor camera on entity 1000"
    },
    "hint": "Req: networkId (number). Example: focus_entity({networkId:1000})",
    "manualVerify": true
  },

  {
    "name": "get_selection",
    "group": "O — Manual-assist (focus / annotations)",
    "summary": "The units the operator has selected (CE-1263): the primary and all.",
    "http": {
      "method": "GET",
      "path": "/selection"
    },
    "params": [],
    "returns": "{ primary, selection:[networkId] }",
    "notes": [
      "To point the operator at a unit, use focus_entity (centres the camera) or add_annotation — not a selection write."
    ],
    "example": {
      "args": {},
      "gist": "which unit is the operator asking about"
    },
    "hint": "No params. Example: get_selection({})",
    "manualVerify": false
  },

  {
    "name": "get_view",
    "group": "O — Manual-assist (focus / annotations)",
    "summary": "What the operator is looking at (CE-1262): the view mode (2D / 3D / FPS), the camera (eye, forward, yaw, pitch, distance, vertical field of view, viewport, the 2-D rectangle), the possessed unit and the unit its crosshair rests on, the selection, the debugged unit, and the units in view nearest first with their screen point.",
    "http": {
      "method": "GET",
      "path": "/view"
    },
    "params": [
      {
        "name": "limit",
        "type": "number",
        "required": false,
        "description": "How many in-view units to list, nearest first (query; default 50, max 500)"
      }
    ],
    "returns": "{ mode, camera:{eye, forward, yawDeg, pitchDeg, distance, fovyDeg, viewport, rect2D?}, possessed, aimTarget, zoomFovDeg?, inView:[{networkId, name, distance, screen}], selection:[ids], primary, debugged } — camera:null (with a note) where no map canvas draws the world.",
    "notes": [
      "The operator points by SELECTING (click; in first person the select key on the unit under the crosshair) — read primary / selection.",
      "aimTarget is what the first-person crosshair rests on right now; it moves with the aim.",
      "inView in 2D is the map's own culling; in 3D / FPS a frustum test (not occlusion) — check a line with explain_line_of_sight using possessed as observer.",
      "For what the possessed unit itself perceives, read get_entity_sensors / get_entity_memory on possessed."
    ],
    "example": {
      "args": {
        "limit": 20
      },
      "gist": "see what the operator sees and which unit they mean"
    },
    "hint": "Optional limit (number, query, default 50). Example: get_view({limit:20})",
    "manualVerify": false
  },

  // ── Group O — Variables (the watch, over HTTP) ──────────────────────────────

  {
    "name": "get_entity_variable",
    "group": "O — Variables (the watch, over HTTP)",
    "summary": "Read one blueprint variable by name, with its live value and its pending (staged-but-not-yet-applied) value if a write is queued.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/variable"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      },
      {
        "name": "path",
        "type": "string",
        "required": true,
        "description": "The variable's name, as list_entity_variables reports it"
      },
      {
        "name": "asset",
        "type": "string",
        "required": false,
        "description": "Blueprint NAME or asset Guid; omit when the entity carries exactly one"
      }
    ],
    "returns": "{ networkId, asset, assetId, path, type, value, writable, pending, pendingValue? }",
    "notes": [
      "An unknown variable name is a 400 pointing back at list_entity_variables — never an empty success."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "path": "Health"
      },
      "gist": "read entity 1000's Health variable and whether an edit is still queued"
    },
    "hint": "Required: networkId, path. Example: get_entity_variable({networkId:1000, path:\"Health\"})",
    "manualVerify": false
  },

  {
    "name": "stage_entity_variable",
    "group": "O — Variables (the watch, over HTTP)",
    "summary": "STAGE a write to one blueprint variable, through the same seam the editor's Details panel uses. The value lands on the next advancing tick — not on this response.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/variable"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      },
      {
        "name": "path",
        "type": "string",
        "required": true,
        "description": "The variable's name"
      },
      {
        "name": "value",
        "type": "any",
        "required": true,
        "description": "The new value, in the same JSON shape the read reports (a number for a numeric variable, [x,y,z] for a vector)"
      },
      {
        "name": "asset",
        "type": "string",
        "required": false,
        "description": "Blueprint NAME or asset Guid; omit when the entity carries exactly one"
      }
    ],
    "returns": "{ networkId, asset, assetId, path, staged: true, pending: true, note }",
    "notes": [
      "Running is not a reason to refuse — it is a reason to stage. There is no \"pause first\" step.",
      "Until the world advances, get_entity_variable still reports the OLD value with pending: true. Step or play to make it land.",
      "A value whose width does not match the field is refused rather than written: the blackboard is shared between subsystems, so an overrun would corrupt a neighbour."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "path": "Health",
        "value": 42
      },
      "gist": "queue Health = 42; it applies on the next advancing tick"
    },
    "hint": "Required: networkId, path, value. Example: stage_entity_variable({networkId:1000, path:\"Health\", value:42})",
    "manualVerify": false
  },

  {
    "name": "list_entity_variables",
    "group": "O — Variables (the watch, over HTTP)",
    "summary": "List an entity's blueprint variables — the same (entity, asset, path) addressing a Details/watch row uses, with each variable's live value and whether a staged write is still pending on it.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/variables"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity (see list_entities)"
      },
      {
        "name": "asset",
        "type": "string",
        "required": false,
        "description": "Blueprint NAME or asset Guid. Omit when the entity carries exactly one blueprint; the error names the choices when it carries several."
      }
    ],
    "returns": "{ networkId, asset, assetId, dispatch, variables: [{ path, type, value, writable, pending, pendingValue? }] }",
    "notes": [
      "pending: true means a staged write for that variable has not been applied yet, so value is still the OLD number — the machine half of the editor's yellow.",
      "writable: false means the variable has no live address (its blueprint's dispatch kind has no staged-write layout), so it can be read but not staged.",
      "A Library-dispatch blueprint legitimately has no working-state variables and returns an empty list, not an error."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read every blueprint variable on entity 1000"
    },
    "hint": "Required: networkId. Optional: asset. Example: list_entity_variables({networkId:1000})",
    "manualVerify": false
  },

  // ── Group P — Discovery with schema ─────────────────────────────────────────

  {
    "name": "list_behaviors",
    "group": "P — Discovery with schema",
    "summary": "List the behaviours available, each with the JSON schema of its parameter DTO. Key by tkbType (what this KIND of entity can do) or entityId (what THIS entity can do); omit both for every registered behaviour.",
    "http": {
      "method": "GET",
      "path": "/behaviors"
    },
    "params": [
      {
        "name": "tkbType",
        "type": "number",
        "required": false,
        "description": "TKB template id — returns the behaviours valid for that entity type (see list_tkb_types)"
      },
      {
        "name": "entityId",
        "type": "number",
        "required": false,
        "description": "Network id — returns exactly what the editor mission-task combo offers for that entity"
      }
    ],
    "returns": "[{ id, name, brainTier, paramSchema }]",
    "notes": [
      "paramSchema is derived from the behaviour definition the runtime itself parses params with, so what you author matches what the engine reads.",
      "An unknown entityId is a 404 whose hint points at GET /entities — it is not answered with an empty list.",
      "A behaviour with no parameters returns an empty properties object, never null."
    ],
    "example": {
      "args": {
        "entityId": 1000
      },
      "gist": "discover what entity 1000 can be told to do, and how to shape the params"
    },
    "hint": "Optional: tkbType or entityId. Example: list_behaviors({entityId:1000})",
    "manualVerify": false
  },

  // ── Group P — Mission editing ───────────────────────────────────────────────

  {
    "name": "get_mission",
    "group": "P — Mission editing",
    "summary": "Read an entity's mission plan — its ordered tasks (behaviour, params, triggers, state) and the OCC version you pass back when editing. An entity with no mission returns an empty task list, not an error.",
    "http": {
      "method": "GET",
      "path": "/missions/{networkId}"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the entity whose mission to read"
      }
    ],
    "returns": "{ networkId, plan: { activeTaskId, tasks: [{ taskId, behaviorId, behaviorParams, executingEngine, state, triggers: [{ type, params }] }] }, version }",
    "notes": [
      "version is the optimistic-lock token — pass it straight back to add_mission_task / clear_mission_tasks so a concurrent edit is caught as a 409 rather than silently overwritten.",
      "The offline editor does not yet persist a snapshot version, so it reports 0 today; the edit path still round-trips it.",
      "An unknown networkId is a 404 whose hint points at GET /entities."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "read entity 1000's current mission before editing it"
    },
    "hint": "Example: get_mission({networkId:1000}). Add a task with add_mission_task; discover behaviours with list_behaviors.",
    "manualVerify": false
  },

  {
    "name": "run_mission",
    "group": "P — Mission editing",
    "summary": "Run (or restart) an entity's mission by jumping to its first task and resetting the phase clock. run and restart are the same jump-to-start the mechanism offers.",
    "http": {
      "method": "POST",
      "path": "/missions/{networkId}/run"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the entity whose mission to run"
      },
      {
        "name": "restart",
        "type": "boolean",
        "required": false,
        "description": "Reserved: run and restart both jump to task 0 and reset the phase clock.",
        "default": false
      }
    ],
    "returns": "{ networkId, restart, committed:true, version }",
    "notes": [
      "Sends CMD_JUMP_TO_TASK to task index 0 — the mission still only advances while the sim is running (play_simulation / step_simulation)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "start entity 1000 executing its mission from the first task"
    },
    "hint": "Advance the sim (play_simulation or step_simulation) so the entity actually executes the mission after this.",
    "manualVerify": false
  },

  {
    "name": "add_mission_task",
    "group": "P — Mission editing",
    "summary": "Append one mission task to an entity — the PROPER way a behaviour attaches (as a task). Names the behaviour pass-through and carries its params as JSON matching the behaviour's paramSchema. Commits the whole plan through the editor's own mission path with optimistic concurrency.",
    "http": {
      "method": "POST",
      "path": "/missions/{networkId}/task"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the entity to add the task to"
      },
      {
        "name": "behavior",
        "type": "string",
        "required": true,
        "description": "The behaviour this task runs — one of the ids from list_behaviors for this entity"
      },
      {
        "name": "params",
        "type": "object",
        "required": false,
        "description": "The behaviour's parameters, as JSON matching its paramSchema from list_behaviors. Stored verbatim as the task's behaviorParams."
      },
      {
        "name": "triggers",
        "type": "array",
        "required": false,
        "description": "Optional transition triggers [{ type, params? }]. Defaults to a single BehaviorFinished trigger so the task can advance.",
        "items": {
          "type": "object",
          "properties": {
            "type": {
              "type": "string"
            },
            "params": {}
          }
        }
      }
    ],
    "returns": "{ networkId, taskId, behavior, taskCount, committed:true, version }",
    "notes": [
      "params is passed through verbatim — the engine reads it with plain JSON, the same string the editor's Mission panel stores. Shape it to the behaviour's paramSchema (list_behaviors), not to a separate mapper.",
      "CE-228: the example above uses MoveToLocation's REAL keys. It previously showed {Latitude, Longitude}, which bind to nothing — the params region would have stayed all-zero and the entity would have driven to the origin, with no error anywhere (R-132's failure shape, invited by this very doc).",
      "The commit is asynchronous: it resolves when the engine acknowledges. If the sim is not being pumped at all the call returns a 504 pointing at play/step.",
      "A stale version yields a 409 (ERR_VERSION_CONFLICT), never a silent overwrite."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "behavior": "MoveToLocation",
        "params": {
          "X": 500,
          "Y": 520,
          "Speed": 8,
          "ArrivalRadius": 5
        }
      },
      "gist": "give entity 1000 a MoveToLocation task"
    },
    "hint": "Discover the behaviour and its param shape with list_behaviors({entityId}); a bad version is a 409, re-read get_mission and retry.",
    "manualVerify": false
  },

  {
    "name": "clear_mission_tasks",
    "group": "P — Mission editing",
    "summary": "Clear every task from an entity's mission (so a fresh sequence can be added), by committing an empty plan through the same optimistic-concurrency path.",
    "http": {
      "method": "DELETE",
      "path": "/missions/{networkId}/tasks"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the entity whose tasks to clear"
      }
    ],
    "returns": "{ networkId, taskCount:0, committed:true, version }",
    "notes": [
      "Commits an empty plan — the same asynchronous, version-checked path as add_mission_task, so the same 409/504 rules apply."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "wipe entity 1000's mission so a new sequence can be authored"
    },
    "hint": "Then add_mission_task to build the new sequence; read the current state first with get_mission.",
    "manualVerify": false
  },

  // ── Group Q — Blueprint hot-attach ──────────────────────────────────────────

  {
    "name": "list_blueprints",
    "group": "Q — Blueprint hot-attach",
    "summary": "Every blueprint this editor compiled, with whether it can be attached to an entity.",
    "http": {
      "method": "GET",
      "path": "/blueprints"
    },
    "params": [],
    "returns": "{ count, blueprints:[{ blueprintId, name, assetId, kind, stateSize, attachable }] }",
    "notes": [
      "Only Instance-dispatch blueprints occupy a slot on an entity; attachable says so up front rather than through a refusal."
    ],
    "example": {
      "args": {},
      "gist": "find a blueprint to try on a running entity"
    },
    "hint": "No params. Example: list_blueprints({})",
    "manualVerify": false
  },

  {
    "name": "attach_blueprint",
    "group": "Q — Blueprint hot-attach",
    "summary": "Attach an Instance blueprint to an entity — the quick way to try a behaviour without authoring a mission. Run-state-aware: lands immediately while paused/Edit, next tick while running.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/attach-blueprint"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      },
      {
        "name": "blueprint",
        "type": "string",
        "required": true,
        "description": "Blueprint name, asset Guid, or numeric blueprintId (see list_blueprints)"
      },
      {
        "name": "paramsJson",
        "type": "object",
        "required": false,
        "description": "Parameters for the blueprint, keyed by name (its paramSchema); omit for its declared defaults"
      }
    ],
    "returns": "{ networkId, blueprint, blueprintId, attached:true, path:\"direct\"|\"event\", applied:\"immediate\"|\"next-tick\", status?, tier?, note }",
    "notes": [
      "Run-state-aware (mirrors the editor's own panel): while time is FROZEN (Edit or paused) it attaches THIS frame (path:direct); while the sim is advancing it queues the ingress event (path:event) and you must step/play once before reading it back.",
      "Params now PERSIST through save_scenario — an attach with non-default params survives save→reload (they ride the assignment as resolved bytes, layout-versioned by the blueprint's StructureHash).",
      "A malformed paramsJson on the direct path is a 400 that changes nothing (parse-before-commit), not a half-applied slot.",
      "After it lands, the entity's variables appear in list_entity_variables — name the asset, since the entity may now carry more than one. See what is attached with list_entity_blueprints."
    ],
    "example": {
      "args": {
        "networkId": 1001,
        "blueprint": "ComponentCollectionDemo"
      },
      "gist": "try a blueprint on entity 1001 right now"
    },
    "hint": "Required: networkId, blueprint. Example: attach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
    "manualVerify": false
  },

  {
    "name": "list_entity_blueprints",
    "group": "Q — Blueprint hot-attach",
    "summary": "The Instance blueprints currently attached to an entity — see what you have assigned before editing.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/blueprints"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      }
    ],
    "returns": "{ networkId, count, blueprints:[{ blueprintId, name, assetId, payloadSize }] }",
    "notes": [
      "Reads the same slot table save_scenario snapshots, so it shows exactly what would persist.",
      "list_blueprints is the catalog (everything compiled); this is what is attached to ONE entity."
    ],
    "example": {
      "args": {
        "networkId": 1001
      },
      "gist": "see which blueprints are on entity 1001"
    },
    "hint": "Required: networkId. Example: list_entity_blueprints({networkId:1001})",
    "manualVerify": false
  },

  {
    "name": "detach_blueprint",
    "group": "Q — Blueprint hot-attach",
    "summary": "Detach an Instance blueprint from an entity. Run-state-aware, like attach_blueprint.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/detach-blueprint"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      },
      {
        "name": "blueprint",
        "type": "string",
        "required": true,
        "description": "Blueprint name, asset Guid, or numeric blueprintId"
      }
    ],
    "returns": "{ networkId, blueprint, blueprintId, detached, path:\"direct\"|\"event\", applied:\"immediate\"|\"next-tick\", note }",
    "notes": [
      "Run-state-aware: removes the slot THIS frame while time is frozen (path:direct); queues the event while the sim advances (path:event, next tick).",
      "On the direct path, detached:false means no slot for that blueprint was on the entity — nothing to remove."
    ],
    "example": {
      "args": {
        "networkId": 1001,
        "blueprint": "ComponentCollectionDemo"
      },
      "gist": "put the entity back how you found it"
    },
    "hint": "Required: networkId, blueprint. Example: detach_blueprint({networkId:1001, blueprint:\"ComponentCollectionDemo\"})",
    "manualVerify": false
  },

  // ── Group R — Entity state ──────────────────────────────────────────────────

  {
    "name": "get_entity_state",
    "group": "R — Entity state",
    "summary": "The well-known fields parsed out — position, rotation, velocity, speed, current behaviour — so an assertion reads state.position.x instead of digging through component JSON.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/state"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity"
      }
    ],
    "returns": "{ networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier}, roeEffective:{fire,reactions,forceLevel,returnFireWindowSeconds}, look:{bodyYawDeg,headYawDeg,eyeYawDeg,source,hasTarget,target,fovDegrees} }",
    "notes": [
      "A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.",
      "A convenience over get_entity, reading the same components: the two cannot disagree.",
      "roeEffective (CE-2289) is the ROE the unit OBEYS: its own where an order set it, else its nearest commander's, else its type default — a squad's HoldFire shows on every man.",
      "look (CE-3252, LD-D) is where a PERSON looks: eyeYawDeg = body + head, the cone vision uses; source order / aim / glance / free. Omitted for a body with no look (a vehicle's eyes are its body)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "where is entity 1000, how fast, doing what"
    },
    "hint": "Required: networkId. Example: get_entity_state({networkId:1000})",
    "manualVerify": false
  },

  // ── Group S — Discovery with schema ─────────────────────────────────────────

  {
    "name": "list_breakpoint_types",
    "group": "S — Discovery with schema",
    "summary": "List every condition type a breakpoint can use, each with the JSON schema of its parameters. Call this BEFORE set_breakpoint instead of guessing a $type.",
    "http": {
      "method": "GET",
      "path": "/breakpoint-types"
    },
    "params": [],
    "returns": "[{ $type, clrType, paramSchema }]  — paramSchema is { type:\"object\", properties:{...} }",
    "notes": [
      "The condition union is CLOSED: these are exactly the $type values set_breakpoint accepts.",
      "A nested predicate appears as { $ref: \"SearchPredicateDto\" } — fill it with another arm from this same list.",
      "Enum-valued params carry their allowed values in \"enum\"; a param marked picker:\"propertyPath\" wants a dotted field path such as \"Position.X\"."
    ],
    "example": {
      "args": {},
      "gist": "discover the valid condition $type values and their parameter shapes"
    },
    "hint": "No params. Example: list_breakpoint_types({})",
    "manualVerify": false
  },

  // ── Group T — Panels (the UI as data) ───────────────────────────────────────

  {
    "name": "list_panels",
    "group": "T — Panels (the UI as data)",
    "summary": "What the editor's UI is showing, without pixels: every registered window plus every instrumented panel, and which of them published a view-model this frame.",
    "http": {
      "method": "GET",
      "path": "/panels"
    },
    "params": [],
    "returns": "{ captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }",
    "notes": [
      "registered vs captured is the load-bearing distinction: a surface that publishes no model and one whose window is closed are different facts, and only the second is fixed by opening a window.",
      "CE-076: registered is COMPLETE for windows — WindowManager.RegisterWindow declares every window it registers, so a window can no longer be invisible here by forgetting to opt in. A window that publishes no view-model still appears in registered and is absent from captured.",
      "A LAZILY registered window (one created on first activation of its perspective) is absent until that perspective has been visited — switch_perspective first if you are enumerating exhaustively.",
      "kinds groups the live panels by their logical name — the key a cross-host comparison uses, since panel ids are unique per instance by design.",
      "captured entries are latest-wins and are NOT cleared per frame: a panel that stopped drawing still reports its last model."
    ],
    "example": {
      "args": {},
      "gist": "see which panels are live and what kinds they are"
    },
    "hint": "No params. Example: list_panels({})",
    "manualVerify": false
  },

  {
    "name": "get_gizmo_frame",
    "group": "T — Panels (the UI as data)",
    "summary": "What the map is drawing this frame, as data: the debug primitives, projected per shape.",
    "http": {
      "method": "GET",
      "path": "/panels/_gizmo"
    },
    "params": [
      {
        "name": "max",
        "type": "number",
        "required": false,
        "description": "Cap the number of primitives returned (default 500)"
      }
    ],
    "returns": "{ count, dropped, emitted, truncated, primitives:[{shape, space, layer, color, ...shape-specific}] }",
    "notes": [
      "truncated tells you the frame was clipped by max — without it a cap would read as the end of the frame.",
      "A shape with no field projection yet is reported by name with a note, never as aliased bytes.",
      "MOST OF A FRAME IS THE GRID. Measured on --mode all with an EMPTY world: 603 primitives, all of them Line. So a non-zero count is NOT evidence that anything is on the map.",
      "TO ANSWER 'are the entities visible', COUNT PRIMITIVES BY shape AND IGNORE Line. A loaded hill-attack frame read 739 primitives: Line 670 plus Box2D 8, Arrow 12, Text 8, SemanticShape 16, SpatialAnchor 16 — the non-Line shapes are the world. Compare that histogram against the same read on the other host to localise a 'nothing renders here' report.",
      "The default cap is 500 and the grid alone can exceed it, so pass max (e.g. 2000) before concluding a shape is absent — otherwise truncated:true means your answer is about the grid."
    ],
    "example": {
      "args": {
        "max": 50
      },
      "gist": "inspect what the map is drawing without taking a screenshot"
    },
    "hint": "Optional: max. Example: get_gizmo_frame({max:50})",
    "manualVerify": false
  },

  {
    "name": "get_panel",
    "group": "T — Panels (the UI as data)",
    "summary": "One panel's dumped view-model — the same object its draw renders from, so a field here is a field the designer sees.",
    "http": {
      "method": "GET",
      "path": "/panels/{panelId}"
    },
    "params": [
      {
        "name": "panelId",
        "type": "string",
        "required": true,
        "description": "Panel address from list_panels (e.g. \"editor_bp_manager\")"
      }
    ],
    "returns": "{ panelId, panelKind, model }",
    "notes": [
      "The model is structured JSON, never a formatted blob — assert a field, do not parse prose.",
      "A miss says WHICH kind of miss it is: not instrumented, or instrumented but not drawing."
    ],
    "example": {
      "args": {
        "panelId": "editor_bp_manager"
      },
      "gist": "read the breakpoint panel's model and assert what it lists"
    },
    "hint": "Required: panelId. Example: get_panel({panelId:\"editor_bp_manager\"})",
    "manualVerify": false
  },

  // ── Group V — AI assets & graph tabs ────────────────────────────────────────

  {
    "name": "list_assets",
    "group": "V — AI assets & graph tabs",
    "summary": "Every AI asset (BTree/HSM/Blueprint) this host has indexed, with both of its addresses.",
    "http": {
      "method": "GET",
      "path": "/assets"
    },
    "params": [],
    "returns": "{ count, assets[{assetId,name,kind,sourceFilePath,isDirty}], note? }",
    "notes": [
      "CALL THIS FIRST before opening anything — it is how you turn a human path into the assetId the open-by-id route wants.",
      "sourceFilePath is the RELATIVE path including subfolders, normalised to forward slashes; paste it verbatim into open_asset_by_path.",
      "name is NOT an address: two subfolders may hold the same file name. Address by assetId (stable) or sourceFilePath (human).",
      "count:0 with a note means the catalog indexed nothing — on a deployed node the source asset tree is absent (asset roots must come from config)."
    ],
    "example": {
      "args": {},
      "gist": "discover which AI assets this host can open"
    },
    "hint": "No params. Example: list_assets({})",
    "manualVerify": false
  },

  {
    "name": "open_asset",
    "group": "V — AI assets & graph tabs",
    "summary": "Open an AI asset by its stable GUID; the graph canvas and outline then render it.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/open"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of the asset to open — from list_assets"
      }
    ],
    "returns": "{ assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }",
    "notes": [
      "The panels publish the opened asset on the NEXT frame — step a tick before get_panels, or you read the previous content.",
      "Opening an already-open asset re-activates its tab rather than duplicating it.",
      "Opening also switches the perspective to the asset kind (the document manager drives it), so the canvas is actually drawing."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "open a specific asset by id and make it the active graph"
    },
    "hint": "Req: assetId (GUID, from list_assets). Example: open_asset({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "reload_ai_asset",
    "group": "V — AI assets & graph tabs",
    "summary": "Recompile an edited AI asset and commit it into the running behaviour registry.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/reload"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document to recompile"
      }
    ],
    "returns": "{ assetId, name, kind, status, note }",
    "notes": [
      "Compiles from the IN-MEMORY asset, not from the file — so it reflects unsaved edits, and save is a separate intent.",
      "The asset is ACTIVATED first: the reload pipeline acts on the active document, so reloading a background tab without activating it would recompile the wrong graph.",
      "A SOFT reload patches lookup tables and live instances KEEP their state; a HARD (topology) reload bumps the generation and instances RESET — that reset is intended, not a bug.",
      "A Hard reload on a live cluster is a confirmed cluster-wide reset, and the confirmation belongs to the interactive node — this call never prompts.",
      "`status` carries the compiler's own message, including the failure text when it did not compile. A failed compile is a 200 with a failure status, not an HTTP error: it is a legitimate outcome of editing."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "hot-apply an edited graph to the running brain"
    },
    "hint": "Req: assetId (GUID of an OPEN document, from list_documents). Example: reload_ai_asset({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "save_ai_asset",
    "group": "V — AI assets & graph tabs",
    "summary": "Persist edited AI assets to their source files.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/save"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document — save acts on open documents, not files"
      }
    ],
    "returns": "{ assetId, name, sourceFilePath, status, stillDirty, note }",
    "notes": [
      "IT SAVES EVERY DIRTY OPEN DOCUMENT, not only this one — it runs the shared Save-All command, which is what the editor's own Save All button runs.",
      "A document with no source path is SKIPPED with a warning in `status` rather than throwing; check `stillDirty` to see whether this one was written.",
      "Saving is NOT a precondition for reload: reload compiles from the in-memory asset, so an unsaved edit still hot-applies."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "write an edited graph back to disk"
    },
    "hint": "Req: assetId (GUID of an OPEN document, from list_documents). Example: save_ai_asset({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "open_asset_by_path",
    "group": "V — AI assets & graph tabs",
    "summary": "Open an AI asset by its relative source file path — the human address.",
    "http": {
      "method": "POST",
      "path": "/assets/open"
    },
    "params": [
      {
        "name": "path",
        "type": "string",
        "required": true,
        "description": "Relative sourceFilePath, or any suffix of it at a folder boundary"
      }
    ],
    "returns": "{ assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }",
    "notes": [
      "The path travels in the BODY on purpose — a relative path has slashes and dots, which a URL segment would need encoding for.",
      "Matching is a path SUFFIX at a folder boundary: 'sub/x.bp.json' matches, 'x' does not, and 'my_x.bp.json' never matches a query for 'x.bp.json'.",
      "An AMBIGUOUS path is a 400 that lists the candidates — it is never resolved by picking the first, which would silently open the wrong asset."
    ],
    "example": {
      "args": {
        "path": "Assets/Blueprints/hill_attack.bp.json"
      },
      "gist": "open an asset by the path a human would read off disk"
    },
    "hint": "Req: path (string, a sourceFilePath from list_assets). Example: open_asset_by_path({path:\"Assets/Blueprints/sub/x.bp.json\"})",
    "manualVerify": false
  },

  {
    "name": "list_documents",
    "group": "V — AI assets & graph tabs",
    "summary": "The open graph tabs and which one is active.",
    "http": {
      "method": "GET",
      "path": "/documents"
    },
    "params": [],
    "returns": "{ activeAssetId, count, documents[{assetId,name,kind,sourceFilePath,isDirty,isActive}] }",
    "notes": [
      "Only the ACTIVE document's canvas draws, so this is how you confirm which graph get_panels is about to show you.",
      "This is the editor's own tab model, exposed — not a second list."
    ],
    "example": {
      "args": {},
      "gist": "see which graphs are open and which one is on screen"
    },
    "hint": "No params. Example: list_documents({})",
    "manualVerify": false
  },

  {
    "name": "activate_document",
    "group": "V — AI assets & graph tabs",
    "summary": "Switch the active graph tab to an already-open document.",
    "http": {
      "method": "POST",
      "path": "/documents/{assetId}/activate"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document — from list_documents"
      }
    ],
    "returns": "{ activeAssetId, note }",
    "notes": [
      "Activate only switches between tabs that are ALREADY open; a closed asset is a 404, not an implicit open. Use open_asset for that.",
      "Details and the toolbar re-publish for the newly active kind on the NEXT frame."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "bring an already-open graph to the front"
    },
    "hint": "Req: assetId (GUID, from list_documents). Example: activate_document({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "focus_panel",
    "group": "V — AI assets & graph tabs",
    "summary": "Open and focus a window by its panel id.",
    "http": {
      "method": "POST",
      "path": "/panels/{panelId}/focus"
    },
    "params": [
      {
        "name": "panelId",
        "type": "string",
        "required": true,
        "description": "Registered window id — the PANEL id from get_panels, not the panel KIND"
      }
    ],
    "returns": "{ panelId, perspective, isOpen, isPinned, note }",
    "notes": [
      "An unknown id is a 404 here, deliberately — the underlying UI call is a silent no-op, which over HTTP would hand you a 200 and then the wrong panel.",
      "A perspective-bound window belonging to another perspective is PINNED rather than switched to; the response says which happened.",
      "Focus takes effect on the NEXT frame."
    ],
    "example": {
      "args": {
        "panelId": "ai_watch_blueprint"
      },
      "gist": "bring a specific panel on screen before reading it"
    },
    "hint": "Req: panelId (string, from get_panels). Example: focus_panel({panelId:\"ai_watch_blueprint\"})",
    "manualVerify": false
  },

  // ── Group W — AI-asset authoring ────────────────────────────────────────────

  {
    "name": "create_asset",
    "group": "W — AI-asset authoring",
    "summary": "Create a new AI asset (BTree / HSM / Blueprint) through the host's own New-Asset path, then open it as a document.",
    "http": {
      "method": "POST",
      "path": "/assets"
    },
    "params": [
      {
        "name": "kind",
        "type": "string",
        "required": true,
        "description": "BTree | Hsm | Blueprint"
      },
      {
        "name": "name",
        "type": "string",
        "required": true,
        "description": "Asset name"
      },
      {
        "name": "path",
        "type": "string",
        "required": false,
        "description": "Subfolder relative to the kind's asset root (default: the root)"
      },
      {
        "name": "recipe",
        "type": "string",
        "required": false,
        "description": "Recipe NAME from list_asset_recipes (default: the kind's blank template)"
      }
    ],
    "returns": "{ assetId, name, kind, recipe, status, sourceFilePath, note }",
    "notes": [
      "It runs the same per-kind INewAssetService the New-Asset dialog runs, writes the file and refreshes the catalog — so the result appears in list_assets by the same rebuild a dialog-created asset does.",
      "The new asset is opened as a document, so you can author it immediately with read_asset_graph and the graph tools.",
      "A host that composes no create path answers 503 explaining that EDITING an existing asset does not need it.",
      "Call list_asset_recipes first to see what this host can create from. A recipe name it does not offer is REFUSED with the available names — it never silently falls back to a blank asset."
    ],
    "example": {
      "args": {
        "kind": "BTree",
        "name": "PatrolTree"
      },
      "gist": "create a new behaviour tree asset"
    },
    "hint": "Req: kind, name. Optional: path (subfolder), recipe (from list_asset_recipes). Example: create_asset({kind:\"BTree\",name:\"Patrol\"})",
    "manualVerify": false
  },

  {
    "name": "read_asset_graph",
    "group": "W — AI-asset authoring",
    "summary": "Read an open AI asset's graph as JSON: nodes, pins, links and comments, keyed by the in-memory guids the edit tools take.",
    "http": {
      "method": "GET",
      "path": "/assets/{assetId}/graph"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document (open_asset / list_documents)"
      }
    ],
    "returns": "{ assetId, name, kind, graphId, displayName, graphKind, nodeCount, linkCount, nodes[{nodeId,kind,title,position,pins[{pinId,label,direction,kind,type,default}]}], links[{linkId,fromPin,toPin,fromNode,toNode}], comments[], note }",
    "notes": [
      "THIS IS THE FIRST CALL of any authoring session: you never predict an id, you read the ones the edit tools accept.",
      "The ids are the IN-MEMORY guids. The saved .json binds links by deterministic name-derived pin ids instead — an id copied out of the file addresses nothing here.",
      "Re-read after each edit rather than caching: adding a node can reproject another node's pins.",
      "Only the graph-document kinds (BTree, HSM, Blueprint) have a graph; a Scenario or Blackboard asset is a 404 explaining that."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "read the whole graph before editing it"
    },
    "hint": "Req: assetId (GUID of an OPEN document — open_asset first). Example: read_asset_graph({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "list_node_kinds",
    "group": "W — AI-asset authoring",
    "summary": "The node kinds this graph can add, with their pin signatures. Call this instead of guessing a kind id for add_graph_node.",
    "http": {
      "method": "GET",
      "path": "/assets/{assetId}/graph/catalog"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "filter",
        "type": "string",
        "required": false,
        "description": "Case-insensitive substring matched against the kind id AND the display name"
      }
    ],
    "returns": "{ count, total, kinds[{kind,displayName,category,description,isDeprecated,inputs[],outputs[]}], note }",
    "notes": [
      "The catalog is PER GRAPH — a BTree graph and a Blueprint graph offer different kinds, so read the one you are editing.",
      "`kind` is what add_graph_node takes verbatim. An unknown kind is refused with this endpoint named, not silently ignored.",
      "`inputs`/`outputs` are the declared pin SIGNATURES; the actual pin guids only exist once the node is added."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "discover what node kinds this graph accepts"
    },
    "hint": "Req: assetId. Optional: filter (substring over kind id and display name). Example: list_node_kinds({assetId:\"...\",filter:\"branch\"})",
    "manualVerify": false
  },

  {
    "name": "add_graph_link",
    "group": "W — AI-asset authoring",
    "summary": "Connect two pins in an open graph. The host's own link validator runs first, so an illegal wire is refused for the same reason a dragged one would be.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/graph/links"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "fromPin",
        "type": "string",
        "required": true,
        "description": "Source (output-side) pin GUID"
      },
      {
        "name": "toPin",
        "type": "string",
        "required": true,
        "description": "Target (input-side) pin GUID"
      }
    ],
    "returns": "{ linkId, fromPin, toPin, requiresCast, note }",
    "notes": [
      "The validator is the SAME one the canvas consults while dragging a wire, so MCP can never author a graph the editor would reject.",
      "A refusal is a 400 carrying the host's own reason text — it is a legitimate answer, not a server error.",
      "When the validator classes the pair ValidWithCast the canvas would auto-insert a cast node; this route connects them directly and says so in `note`."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "fromPin": "11111111-1111-1111-1111-111111111111",
        "toPin": "22222222-2222-2222-2222-222222222222"
      },
      "gist": "wire two pins together"
    },
    "hint": "Req: assetId, fromPin, toPin (pin GUIDs from read_asset_graph or add_graph_node). Example: add_graph_link({assetId:\"...\",fromPin:\"...\",toPin:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "add_graph_node",
    "group": "W — AI-asset authoring",
    "summary": "Add a node to an open graph through the same command sink human editing uses. Returns the new node's guid and its pins.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/graph/nodes"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "kind",
        "type": "string",
        "required": true,
        "description": "Node kind id — take one verbatim from list_node_kinds"
      },
      {
        "name": "x",
        "type": "number",
        "required": false,
        "description": "Canvas X position (default 0)",
        "default": 0
      },
      {
        "name": "y",
        "type": "number",
        "required": false,
        "description": "Canvas Y position (default 0)",
        "default": 0
      }
    ],
    "returns": "{ nodeId, kind, title, pins[{pinId,label,direction,kind,type}], note }",
    "notes": [
      "The edit goes through the editor's undo stack, so it is undoable exactly like a node dropped on the canvas.",
      "The response carries the new node's PINS because linking needs them — you do not have to re-read the whole graph to wire it up.",
      "An unknown kind is a 400 naming list_node_kinds: the host sink can report success and build nothing, so this route re-reads the model and refuses rather than returning a guid that addresses nothing."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "kind": "bt.selector",
        "x": 120,
        "y": 40
      },
      "gist": "add a node and get back its guid"
    },
    "hint": "Req: assetId, kind (from list_node_kinds). Optional: x, y. Example: add_graph_node({assetId:\"...\",kind:\"bt.selector\"})",
    "manualVerify": false
  },

  {
    "name": "set_graph_param",
    "group": "W — AI-asset authoring",
    "summary": "Set the literal default value on an input data pin of an open graph.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/graph/params"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "pinId",
        "type": "string",
        "required": true,
        "description": "GUID of an INPUT DATA pin (from read_asset_graph)"
      },
      {
        "name": "value",
        "type": "string",
        "required": true,
        "description": "The new default. Sent as JSON and converted to the CLR type the pin's current default already holds; an explicit null clears it"
      }
    ],
    "returns": "{ pinId, label, previousValue, value, note }",
    "notes": [
      "This is a PIN default, not a free-form node property: the pin default is the one edit whose inverse can be built from the model, so it is the one that stays undoable.",
      "An exec pin or an output pin is refused — an exec pin has no value and an output's value is computed.",
      "`value` in the response is RE-READ from the model after the edit, so it shows what the host actually stored rather than what you sent."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "pinId": "11111111-1111-1111-1111-111111111111",
        "value": 3.5
      },
      "gist": "set a literal on an input pin"
    },
    "hint": "Req: assetId, pinId, value. Example: set_graph_param({assetId:\"...\",pinId:\"...\",value:3.5})",
    "manualVerify": false
  },

  {
    "name": "remove_graph_elements",
    "group": "W — AI-asset authoring",
    "summary": "Remove nodes and/or links from an open graph by invoking the editor's own Delete command.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/graph/remove"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "nodes",
        "type": "array",
        "required": false,
        "description": "Node GUIDs to remove"
      },
      {
        "name": "links",
        "type": "array",
        "required": false,
        "description": "Link GUIDs to remove"
      }
    ],
    "returns": "{ removedNodes, removedLinks, nodeCount, linkCount, note }",
    "notes": [
      "It invokes the editor's shared Delete command rather than building its own removal, so incident links, reroute waypoints and attachments are handled and the undo restores nodes before the links that reference them.",
      "`removedLinks` counts the links deleted IMPLICITLY with their nodes, so it is usually larger than the list you named.",
      "An id that is not in the graph refuses the WHOLE call — a partial delete would be worse than a refusal.",
      "The canvas selection is left cleared afterwards, exactly as after a human delete."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "nodes": [
          "11111111-1111-1111-1111-111111111111"
        ]
      },
      "gist": "delete a node and its wires"
    },
    "hint": "Req: assetId and at least one of nodes[] / links[]. Example: remove_graph_elements({assetId:\"...\",nodes:[\"...\"]})",
    "manualVerify": false
  },

  {
    "name": "list_asset_recipes",
    "group": "W — AI-asset authoring",
    "summary": "List the recipes and blank templates create_asset can build from, per asset kind.",
    "http": {
      "method": "GET",
      "path": "/assets/recipes"
    },
    "params": [
      {
        "name": "kind",
        "type": "string",
        "required": false,
        "description": "Restrict to one AssetKind (BTree | Hsm | Blueprint)"
      }
    ],
    "returns": "{ kinds[], recipes[{ id, kind, name, description, category, isBlankTemplate, sourceFilePath }], note }",
    "notes": [
      "`name` is exactly what create_asset takes as its `recipe` argument.",
      "`isBlankTemplate` separates a synthetic empty starting point (Empty, Starter) from a CONTENT recipe cloned from a real asset — the two are not interchangeable and the name alone does not tell you which it is.",
      "The list is read live from each kind's INewAssetService, so recipes added to disk appear without restarting the host.",
      "`description` is null for recipes that carry no RecipeMetadata — the synthetic Empty/Starter entries do not.",
      "A host that composes no per-kind new-asset registry answers 503; the same registry backs create_asset, so if this is 503 then create is too."
    ],
    "example": {
      "args": {},
      "gist": "see what kinds of asset this host can create"
    },
    "hint": "Optional: kind (filter). Example: list_asset_recipes({kind:\"Blueprint\"})",
    "manualVerify": false
  },

  {
    "name": "delete_entity",
    "group": "W — AI-asset authoring",
    "summary": "Remove an entity from the world through the ELM lifecycle. Scenario authoring is world manipulation, and this is its delete.",
    "http": {
      "method": "DELETE",
      "path": "/entities/{networkId}"
    },
    "params": [
      {
        "name": "networkId",
        "type": "number",
        "required": true,
        "description": "Network id of the entity to destroy"
      }
    ],
    "returns": "{ networkId, queued:true, note }",
    "notes": [
      "There is no such thing as editing a scenario FILE: the file is a reduced snapshot of the world at save time, so authoring a scenario means spawning, configuring and deleting entities, then calling save_scenario.",
      "Queued like spawn_entity — teardown runs on a later tick. Call step, then list_entities, before asserting the entity is gone.",
      "An unknown networkId is a 404 rather than a queued no-op."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "delete an entity from the world"
    },
    "hint": "Req: networkId (from list_entities). Example: delete_entity({networkId:1000})",
    "manualVerify": false
  },

  // ── Group X — Graph command union & discovery ───────────────────────────────

  {
    "name": "get_node_kind_schema",
    "group": "X — Graph command union & discovery",
    "summary": "One node kind's full schema and documentation: pins, flags, palette behaviour, and the reflected DTO params when the kind resolves to an action.",
    "http": {
      "method": "GET",
      "path": "/assets/{assetId}/graph/catalog/{kind}"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "kind",
        "type": "string",
        "required": true,
        "description": "Node kind id, verbatim from list_node_kinds"
      }
    ],
    "returns": "{ kind, displayName, category, doc, isPure, isLatent, isDeprecated, paletteAction, isAttachmentKind?, attachmentCategory?, keywords[], inputs[], outputs[], paramsSource, params[], note }",
    "notes": [
      "MEASURED from the host's own INodeCatalog and action-schema exporter — never a hand-authored kind table, which would rot the moment a node kind is added and nothing would fail.",
      "`paramsSource` says where params came from: exporter:exact, exporter:suffix (a probable match, not a certain one), none:not-an-action, none:dto-fields-not-reflected, or none:no-exporter-wired. An empty list WITHOUT that field would read as 'this kind has no params', which is a different and often false claim.",
      "The catalog cannot say whether a kind is a CONTAINER — container-ness belongs to an instantiated node. Read container/region structure per node from read_asset_graph.",
      "`paletteAction` is the kind-level structure fact the catalog does have: CreateNode makes a node, AttachToSelected makes an ATTACHMENT on the selected node."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "kind": "bt.selector"
      },
      "gist": "read one node kind's pins, params and docs"
    },
    "hint": "Req: assetId, kind (verbatim from list_node_kinds). Example: get_node_kind_schema({assetId:\"...\",kind:\"bt.selector\"})",
    "manualVerify": false
  },

  {
    "name": "list_graph_command_types",
    "group": "X — Graph command union & discovery",
    "summary": "Every GraphCommand variant apply_graph_command accepts, with the fields each one takes.",
    "http": {
      "method": "GET",
      "path": "/assets/{assetId}/graph/command"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      }
    ],
    "returns": "{ count, variants[{type,fields[]}], unsupported[{type,reason}], note }",
    "notes": [
      "Call this before apply_graph_command instead of guessing a payload shape — the variant names match the nested record names in NodeEditor.Core.Commands.GraphCommand exactly.",
      "A field suffixed '?' is optional. Ids are GUID strings from read_asset_graph.",
      "'Batch' takes {commands:[...]} and applies them as ONE undo entry, with the inverses reversed so nodes are restored before the links that reference them.",
      "The 'unsupported' list is normally empty; an entry there is a deliberate decision with its reason, not an oversight."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000"
      },
      "gist": "discover every graph-edit command and its fields"
    },
    "hint": "Req: assetId (GUID of an OPEN document). Example: list_graph_command_types({assetId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "apply_graph_command",
    "group": "X — Graph command union & discovery",
    "summary": "Apply ONE GraphCommand to an open graph — the whole ~35-variant union, including BTree decorators (attachments) and HSM parallel regions the typed verbs cannot express.",
    "http": {
      "method": "POST",
      "path": "/assets/{assetId}/graph/command"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "type",
        "type": "string",
        "required": true,
        "description": "The GraphCommand variant name, e.g. AddNode / AddAttachment / AddRegion / Batch"
      },
      {
        "name": "commands",
        "type": "array",
        "required": false,
        "description": "For type:\"Batch\" — the nested commands, applied as one atomic undo entry"
      }
    ],
    "returns": "{ type, applied, undoable, message, newIds{}, nodeCount, linkCount, nodeDelta, linkDelta, note }",
    "notes": [
      "THE PARITY GUARANTEE: the command goes through GraphView.Execute — the same undo stack and the same host sink a canvas gesture uses. There is no MCP-only mutation path, on any of the three hosts, with zero per-host code.",
      "The typed verbs (add_graph_node, add_graph_link, set_graph_param, remove_graph_elements) are sugar over this same union — they are not a parallel model.",
      "`newIds` carries any id the command MINTED (nodeId / linkId / attachmentId / commentId), so you can address what you just created.",
      "`undoable:false` means no inverse could be derived from the read-only model (the refactor ops, SetNodeProperty, RemoveRegion). The edit still applied; the undo stack simply has no entry. A wrong inverse would corrupt the graph silently, so none is recorded.",
      "A refusal is the HOST's own answer (an invalid wire, an unknown kind) and comes back 400 with its reason — it is a legitimate outcome of editing, not a server error."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "type": "AddNode",
        "kind": "bt.selector",
        "position": {
          "x": 80,
          "y": 40
        }
      },
      "gist": "add a node through the union route and get its new guid"
    },
    "hint": "Req: assetId, type (from list_graph_command_types) + that variant's fields. Example: apply_graph_command({assetId:\"...\",type:\"AddAttachment\",host:\"<nodeGuid>\"})",
    "manualVerify": false
  },

  {
    "name": "get_node_properties",
    "group": "X — Graph command union & discovery",
    "summary": "One node's editable properties with their CURRENT values — what the Details panel shows.",
    "http": {
      "method": "GET",
      "path": "/assets/{assetId}/graph/nodes/{nodeId}/properties"
    },
    "params": [
      {
        "name": "assetId",
        "type": "string",
        "required": true,
        "description": "GUID of an OPEN document"
      },
      {
        "name": "nodeId",
        "type": "string",
        "required": true,
        "description": "Node GUID from read_asset_graph or add_graph_node"
      }
    ],
    "returns": "{ assetId, nodeId, kind, title, doc, count, properties[{pinId,name,type,value,hasValue,doc?,rangeMin?,rangeMax?,unit?,picker?}], note }",
    "notes": [
      "Values come from the MODEL and schema from the CATALOG, joined here — so a value is never reported without the type and constraints needed to change it correctly.",
      "Only INPUT DATA pins appear: an exec pin has no value and an output's is computed, so listing them would invite a set that must be refused.",
      "Set one with set_graph_param, or apply_graph_command type:\"SetPinDefault\".",
      "Range / unit / picker metadata is the same the Details editor itself reads — it is carried on the pin's default descriptor, not re-derived here."
    ],
    "example": {
      "args": {
        "assetId": "00000000-0000-0000-0000-000000000000",
        "nodeId": "11111111-1111-1111-1111-111111111111"
      },
      "gist": "read a node's current property values and their schema"
    },
    "hint": "Req: assetId, nodeId (GUID from read_asset_graph). Example: get_node_properties({assetId:\"...\",nodeId:\"...\"})",
    "manualVerify": false
  },

  {
    "name": "list_editor_commands",
    "group": "X — Graph command union & discovery",
    "summary": "The EDITOR command bus — every toolbar/menu/hotkey command with its live enabled and checked state.",
    "http": {
      "method": "GET",
      "path": "/editor/commands"
    },
    "params": [
      {
        "name": "category",
        "type": "string",
        "required": false,
        "description": "Filter to one category, e.g. \"Edit\""
      }
    ],
    "returns": "{ count, total, commands[{id,displayName,category,doc,defaultKey?,isEnabled,isChecked?}], note }",
    "notes": [
      "This is NOT list_commands — that one enumerates publishable FDP EVENT types for send_entity_command. These are the editor's own commands, invoked with invoke_editor_command.",
      "isEnabled/isChecked are evaluated NOW over live editor state (is there a selection? is the undo stack empty?), so they are a snapshot.",
      "The command set is per OPEN DOCUMENT — it is built by the per-kind document factory, so opening a different asset kind changes it. Open an AI asset first.",
      "The descriptors are self-documenting: DisplayName, Category, Description and DefaultKey are carried inline, so no attribute harvest is needed here."
    ],
    "example": {
      "args": {},
      "gist": "list the editor commands and which are currently enabled"
    },
    "hint": "Optional: category. Example: list_editor_commands({})",
    "manualVerify": false
  },

  {
    "name": "get_editor_command",
    "group": "X — Graph command union & discovery",
    "summary": "Describe one editor command.",
    "http": {
      "method": "GET",
      "path": "/editor/commands/{commandId}"
    },
    "params": [
      {
        "name": "commandId",
        "type": "string",
        "required": true,
        "description": "Command id, e.g. \"editor.delete-selection\""
      }
    ],
    "returns": "{ id, displayName, category, doc, defaultKey?, isEnabled, isChecked? }",
    "notes": [
      "Ids look like 'editor.delete-selection'. The available set depends on which document kind is open.",
      "A 404 here means the id is not registered for the currently open document — not that it never exists."
    ],
    "example": {
      "args": {
        "commandId": "editor.delete-selection"
      },
      "gist": "describe one editor command before invoking it"
    },
    "hint": "Req: commandId (from list_editor_commands). Example: get_editor_command({commandId:\"editor.delete-selection\"})",
    "manualVerify": false
  },

  {
    "name": "invoke_editor_command",
    "group": "X — Graph command union & discovery",
    "summary": "Run an editor command through the same seam the toolbar, menu and hotkey use.",
    "http": {
      "method": "POST",
      "path": "/editor/commands/{commandId}/invoke"
    },
    "params": [
      {
        "name": "commandId",
        "type": "string",
        "required": true,
        "description": "Command id from list_editor_commands"
      },
      {
        "name": "args",
        "type": "object",
        "required": false,
        "description": "Parameters, delivered as EditorCommandContext.Args"
      },
      {
        "name": "canvasPos",
        "type": "object",
        "required": false,
        "description": "Canvas position {x,y} for context-menu-style commands"
      }
    ],
    "returns": "{ commandId, displayName, invoked, success, message, note }",
    "notes": [
      "A DISABLED command is refused with 409 BEFORE it is invoked. The editor greys it out for the same reason — usually an empty selection or an empty undo stack — and running it anyway would be the one path that accepts what the editor refuses.",
      "Read list_editor_commands for the live enabled state, and set up the precondition first (e.g. select something).",
      "A headless origin never pre-flights a confirmation (ruling 53): the command runs directly and the origin-side LOG is the safety net. The host logs every invocation.",
      "Effects that redraw appear on the NEXT frame — step a tick before reading get_panels."
    ],
    "example": {
      "args": {
        "commandId": "editor.select-all"
      },
      "gist": "run an editor command headlessly"
    },
    "hint": "Req: commandId. Optional: args (object), canvasPos {x,y}. Example: invoke_editor_command({commandId:\"editor.select-all\"})",
    "manualVerify": false
  },

  // ── Group Y — node diagnostics ──────────────────────────────────────────────

  {
    "name": "get_audio",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 B11 (R-4059) — the Sound switch (the scene heard from the camera in 2-D and 3-D; FPS always sounds), a film take's own switch, the master volume and mute.",
    "http": {
      "method": "GET",
      "path": "/audio"
    },
    "params": [],
    "returns": "{ sound, takeSound, master, muted, note }",
    "example": {
      "args": {},
      "gist": "read the sound switch"
    },
    "hint": "Example: get_audio({})",
    "manualVerify": false
  },

  {
    "name": "set_audio",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 B11 (R-4059) — set the Sound switch, the master volume and mute, as the menu does (saved to the user's file).",
    "http": {
      "method": "POST",
      "path": "/audio"
    },
    "params": [
      {
        "name": "sound",
        "type": "boolean",
        "required": false,
        "description": "Settings ▸ Sound: the scene heard from the camera outside FPS"
      },
      {
        "name": "master",
        "type": "number",
        "required": false,
        "description": "Master volume 0…1"
      },
      {
        "name": "muted",
        "type": "boolean",
        "required": false,
        "description": "Mute"
      }
    ],
    "returns": "as get_audio",
    "example": {
      "args": {
        "sound": true
      },
      "gist": "turn the sound on"
    },
    "hint": "Optional: sound (bool), master (0…1), muted (bool). Example: set_audio({sound:true})",
    "manualVerify": false
  },

  {
    "name": "trigger_cluster_diagnostic_dump",
    "group": "Y — node diagnostics",
    "summary": "Collect diagnostics on the named cluster nodes and pull them to the NAS — the same operation the ExCon's Execute Diagnostic Dump button drives.",
    "http": {
      "method": "POST",
      "path": "/cluster/diagnostics/dump"
    },
    "params": [
      {
        "name": "nodes",
        "type": "array",
        "required": true,
        "description": "Node ids to dump (at least one)"
      },
      {
        "name": "dumpEvents",
        "type": "boolean",
        "required": false,
        "description": "Include event history (default true)"
      },
      {
        "name": "dumpEntities",
        "type": "boolean",
        "required": false,
        "description": "Include entity state (default true)"
      },
      {
        "name": "dumpArchitecture",
        "type": "boolean",
        "required": false,
        "description": "Include the architecture snapshot (default true)"
      },
      {
        "name": "dumpLogs",
        "type": "boolean",
        "required": false,
        "description": "Include NLog files (default true)"
      },
      {
        "name": "eventProviders",
        "type": "array",
        "required": false,
        "description": "Restrict event dumping to these provider names"
      },
      {
        "name": "useMarkdown",
        "type": "boolean",
        "required": false,
        "description": "Wrap the output in a markdown report (default false)"
      },
      {
        "name": "maxAgeHours",
        "type": "number",
        "required": false,
        "description": "Only include log entries younger than this (default 24)"
      },
      {
        "name": "severityThreshold",
        "type": "number",
        "required": false,
        "description": "Minimum log severity to include (default 0)"
      }
    ],
    "returns": "{ transactionId, nodes[], queued:true, note }",
    "notes": [
      "ASYNCHRONOUS and cluster-wide: the response confirms the request was PUBLISHED, not that files exist. Every selected node gathers, then the orchestrator pulls to the NAS over SMB.",
      "Poll get_cluster_diagnostic_status until manifestPaths is non-empty — that is the completion signal.",
      "An empty nodes[] is refused rather than read as 'every node': the editor's own panel disables its button on the same condition, and dumping the whole cluster is a different operation from dumping one node.",
      "This adds no collection mechanism — it publishes the same CQRS intent the operator's button publishes, onto whichever node's orchestration bus is reachable.",
      "The request is logged with its transaction id and target nodes: a headless origin never pre-flights a confirmation, so that log is the safety net (ruling 53)."
    ],
    "example": {
      "args": {
        "nodes": [
          1
        ]
      },
      "gist": "collect diagnostics from node 1 to the NAS"
    },
    "hint": "Req: nodes[] (from get_cluster_diagnostic_status). Example: trigger_cluster_diagnostic_dump({nodes:[1,2]})",
    "manualVerify": false
  },

  {
    "name": "get_cluster_diagnostic_status",
    "group": "Y — node diagnostics",
    "summary": "Whether a cluster transaction is in flight, and the file manifest of the last successful diagnostic dump.",
    "http": {
      "method": "GET",
      "path": "/cluster/diagnostics/status"
    },
    "params": [],
    "returns": "{ inFlight, manifestPaths[], manifestCount, note }",
    "notes": [
      "Reads the same read model the ExCon's Cluster Diagnostics panel renders, so it answers what a human at the console would see.",
      "manifestPaths are relative to the NAS base directory and describe the LAST SUCCESSFUL dump. EMPTY means none has completed yet — not that one failed.",
      "inFlight covers any cluster transaction, not only a dump.",
      "Only a node that builds and pumps a ClusterUiCache can answer (in --mode all that is ExCon); a host without one can still TRIGGER a dump but cannot observe it, and says so."
    ],
    "example": {
      "args": {},
      "gist": "check whether the diagnostic dump finished"
    },
    "hint": "No args. Example: get_cluster_diagnostic_status({})",
    "manualVerify": false
  },

  {
    "name": "get_architecture_diagnostics",
    "group": "Y — node diagnostics",
    "summary": "This NODE's modules, ECS systems and DDS translators, one entry per subsystem, read from each subsystem's own ModuleHostKernel.",
    "http": {
      "method": "GET",
      "path": "/diagnostics/architecture"
    },
    "params": [
      {
        "name": "subsystem",
        "type": "string",
        "required": false,
        "description": "Restrict to one subsystem or perspective name (e.g. SimHost, IG, Scenario)"
      }
    ],
    "returns": "{ subsystems[{ subsystem, perspective, modules[], systems[{ phase, module, systemName, executions, avgMs, recentAvgMs, maxMs, errors }], translators[], moduleCount, systemCount, translatorCount }], note }",
    "notes": [
      "Per SUBSYSTEM, not per node: a --mode all node runs SimHost, IG, CGF and the orchestrator side by side and each holds its own kernel, so one snapshot per node would have to drop the rest.",
      "Every node hosts its own MCP endpoint. This answers for THIS node only — ask each node's own endpoint for its own architecture.",
      "A subsystem with no ECS kernel (ExCon, an orchestrator-only node) correctly reports nothing; check the 'diagnostics.architecture' cell in get_capabilities rather than reading the absence as a wiring bug.",
      "modules carry lifecycleState and circuitState, so a module stuck open or failing shows up here without reading logs.",
      "It allocates the whole snapshot on every call — fine for an operator query, wrong in a loop."
    ],
    "example": {
      "args": {},
      "gist": "see what modules and translators this node is running"
    },
    "hint": "Optional: subsystem (filter). Example: get_architecture_diagnostics({subsystem:\"SimHost\"})",
    "manualVerify": false
  },

  {
    "name": "get_render_diagnostics",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-1145 — the map view's frame rate (fps, frame-time mean / p95 / max over ~2 s) and its camera (mode 2D / 3D / FPS, eye, forward, yaw / pitch, distance, height above the ground, viewport).",
    "http": {
      "method": "GET",
      "path": "/diagnostics/render"
    },
    "params": [],
    "returns": "{ fps, frameMsMean, frameMsP95, frameMsMax, frames, windowSeconds, mode, camera{ eye[3], forward[3], yawDeg, pitchDeg, distance, heightAboveGround, viewport[2] } }",
    "example": {
      "args": {},
      "gist": "read the map view's frame rate and camera"
    },
    "hint": "Zeros until the window has drawn a frame (a headless host never draws). Example: get_render_diagnostics({})",
    "manualVerify": false
  },

  {
    "name": "director_get_checks",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4042 — a scenario's machine-checkable conditions (scenarios/<s>/checks.json) as JSON, what is wrong with them, and its golden's size.",
    "http": {
      "method": "GET",
      "path": "/director/checks/{scenario}"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": true,
        "description": "The scenario's name"
      }
    ],
    "returns": "{ scenario, path, json{…}, problems[], golden?{ path, steps, snapshots, shots } }",
    "notes": [
      "docs/DESIGN_Scenario_Regression.md §3 (the grammar), §4 (the golden)."
    ],
    "example": {
      "args": {
        "scenario": "ua-tank-ammo"
      },
      "gist": "read a scenario's checks"
    },
    "hint": "Req: scenario. Example: director_get_checks({scenario:\"ua-tank-ammo\"})",
    "manualVerify": false
  },

  {
    "name": "director_put_checks",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4042 — write a scenario's checks (authoring by an agent): refused, with every problem, when a key, a cue or a time is wrong. Written to the working copy AND the source tree.",
    "http": {
      "method": "PUT",
      "path": "/director/checks/{scenario}"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": true,
        "description": "The scenario's name"
      },
      {
        "name": "expect",
        "type": "array",
        "required": true,
        "description": "The expectations"
      },
      {
        "name": "runFor",
        "type": "number",
        "required": false,
        "description": "Sim seconds the run lasts (default 60)"
      },
      {
        "name": "golden",
        "required": false,
        "description": "{every: seconds} — compare with a golden; false — conditions only (a demo)"
      },
      {
        "name": "anchors",
        "type": "object",
        "required": false,
        "description": "Named cues other times refer to"
      },
      {
        "name": "do",
        "type": "array",
        "required": false,
        "description": "Changes made during the run: [{name?, at, set:{who, component, patch}}]"
      },
      {
        "name": "sampleEvery",
        "type": "number",
        "required": false,
        "description": "Sim seconds between reads of the world (default 0.25)"
      }
    ],
    "returns": "{ saved[], expect, anchors, do, golden }",
    "example": {
      "args": {
        "scenario": "ua-tank-ammo",
        "runFor": 60,
        "expect": [
          {
            "name": "fires",
            "on": "shot",
            "by": "Tank"
          }
        ]
      },
      "gist": "write a scenario's checks"
    },
    "hint": "Req: scenario, expect[{name, on, …, after?, before?} | {name, never:{on,…}} | {name, on,…, atEnd:true}]. Cues: shot{by?, at?, hit?, n?, where?} · blast{by?, n?, where?} · hit{target} · field{who, path, op, value, quantifier?} · near · task · side · health · embark · gesture. Example: director_put_checks({scenario:\"ua-tank-ammo\", runFor:60, golden:{every:10}, expect:[{name:\"the M1 fires at the T-72\", on:\"shot\", by:\"Tank\", at:\"Enemy Tank\", before:30}]})",
    "manualVerify": false
  },

  {
    "name": "director_next",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — end this reel item (reported as stopped) and go on to the next.",
    "http": {
      "method": "POST",
      "path": "/director/next"
    },
    "params": [],
    "returns": "the director's status",
    "example": {
      "args": {},
      "gist": "skip to the next take"
    },
    "hint": "Example: director_next({})",
    "manualVerify": false
  },

  {
    "name": "director_pause",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — pause the live show (⚠ a paused frame changes the run — the report says so).",
    "http": {
      "method": "POST",
      "path": "/director/pause"
    },
    "params": [],
    "returns": "the director's status",
    "example": {
      "args": {},
      "gist": "pause the show"
    },
    "hint": "Example: director_pause({})",
    "manualVerify": false
  },

  {
    "name": "director_play",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — play a reel or one take: each scenario opened from its file, the take's shots held on the 3-D camera, ONE fixed step per rendered frame (R-3038). Without capture it plays LIVE on screen and saves nothing; with capture it renders frame by frame and writes ONE movie of the reel.",
    "http": {
      "method": "POST",
      "path": "/director/play"
    },
    "params": [
      {
        "name": "reel",
        "type": "string",
        "required": false,
        "description": "A reel's name (films/<name>.reel.json)"
      },
      {
        "name": "scenario",
        "type": "string",
        "required": false,
        "description": "A scenario, to play one take"
      },
      {
        "name": "take",
        "required": false,
        "description": "The take's name (default main), or a take object"
      },
      {
        "name": "speed",
        "type": "number",
        "required": false,
        "description": "Live: sim seconds per wall second (default 1)"
      },
      {
        "name": "loop",
        "type": "boolean",
        "required": false,
        "description": "Live: play the reel again and again"
      },
      {
        "name": "capture",
        "type": "object",
        "required": false,
        "description": "To film: {dir, fps (30), size (1280x720), supersample (1), captions (true: burned in), movie (dir/<reel>.mp4), encode (true)}"
      }
    ],
    "returns": "the director's status (see director_status)",
    "notes": [
      "docs/DESIGN_Film_Director.md §0, §3a, §3b, §4a. Plays in the editor's PREVIEW (R-4061); every item loads again (R-3034). Capture needs a window (Xvfb) and ffmpeg."
    ],
    "example": {
      "args": {
        "reel": "highlights"
      },
      "gist": "play a reel live"
    },
    "hint": "One of: {reel} · {scenario, take?} (take: a name, default main, or the take itself as an object — played, never saved). Optional: speed (live), loop (live), capture{dir, fps?, size?\"1280x720\", supersample?, captions?, movie?, encode?}. Poll director_status. Example: director_play({reel:\"highlights\"})",
    "manualVerify": false
  },

  {
    "name": "director_list_reels",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — the reels (films/<name>.reel.json): playlists of takes, each ONE movie on film.",
    "http": {
      "method": "GET",
      "path": "/director/reels"
    },
    "params": [],
    "returns": "{ root, reels[{ name, title, items, problems[] }] }",
    "example": {
      "args": {},
      "gist": "list the reels"
    },
    "hint": "Example: director_list_reels({})",
    "manualVerify": false
  },

  {
    "name": "director_resume",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — resume the live show.",
    "http": {
      "method": "POST",
      "path": "/director/resume"
    },
    "params": [],
    "returns": "the director's status",
    "example": {
      "args": {},
      "gist": "resume the show"
    },
    "hint": "Example: director_resume({})",
    "manualVerify": false
  },

  {
    "name": "director_scout",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — scout a take or a reel: run each take once, stepped as the film will be, and BAKE its anchor times (with the step rate, the scenario's hash) into the take. The report names every beat that did not happen — a scenario health check.",
    "http": {
      "method": "POST",
      "path": "/director/scout"
    },
    "params": [
      {
        "name": "reel",
        "type": "string",
        "required": false,
        "description": "A reel's name"
      },
      {
        "name": "scenario",
        "type": "string",
        "required": false,
        "description": "A scenario"
      },
      {
        "name": "take",
        "type": "string",
        "required": false,
        "description": "The take (default main)"
      },
      {
        "name": "bake",
        "type": "boolean",
        "required": false,
        "description": "Write the found times into the take (default true; false = a measurement only, e.g. the two-run identity report)"
      }
    ],
    "returns": "the director's status; per item: found{anchor: time}, missing[], mismatches[] (a beat that moved since the last scout = a determinism finding)",
    "notes": [
      "docs/DESIGN_Film_Director.md §0 (R-3038): the filmed run IS the scouted run once CE-3293 S5 lands; until then re-scout after S5."
    ],
    "example": {
      "args": {
        "scenario": "ua-tank-ammo"
      },
      "gist": "bake a take's anchor times"
    },
    "hint": "{reel} or {scenario, take?}. Example: director_scout({scenario:\"ua-tank-ammo\"})",
    "manualVerify": false
  },

  {
    "name": "director_status",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — what the director does: phase (Idle · Loading · Entering · Playing · Encoding · Done · Stopped), the reel item, the shot, sim time, the realtime factor, and a report per item.",
    "http": {
      "method": "GET",
      "path": "/director/status"
    },
    "params": [],
    "returns": "{ phase, mode, reel, item, items, paused, speed, shot, simTime, realtimeFactor, tickHz, frames, movie, error, reports[{ scenario, take, status, problems[], missing[], mismatches[], skippedReplays[], unresolved[], found{}, tickHz, usedBake, realtimeFactor, steps, lateFrames, waitFrames, maxStepsInAFrame, maxRetiredInAFrame, frames, simEnd, wallSeconds, cuts[{ shot, t, rounds, units{ name: [x, y, z, health] } }] }], anchorsSeen?, mismatches? }",
    "example": {
      "args": {},
      "gist": "read the director's status"
    },
    "hint": "maxStepsInAFrame is 1 by construction (the stepping rule); maxRetiredInAFrame is what the master took in one frame (1 when the rule holds at the kernel); waitFrames counts frames with two steps still outstanding. Example: director_status({})",
    "manualVerify": false
  },

  {
    "name": "director_stop",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — stop the director (the preview is left, the camera released, the user's layers and sound back).",
    "http": {
      "method": "POST",
      "path": "/director/stop"
    },
    "params": [],
    "returns": "the director's status",
    "example": {
      "args": {},
      "gist": "stop the director"
    },
    "hint": "Example: director_stop({})",
    "manualVerify": false
  },

  {
    "name": "director_get_take",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — one take as JSON (its shots, anchors, baked anchor times) and what is wrong with it.",
    "http": {
      "method": "GET",
      "path": "/director/take/{scenario}/{take}"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": true,
        "description": "The scenario's name"
      },
      {
        "name": "take",
        "type": "string",
        "required": true,
        "description": "The take's name (main)"
      }
    ],
    "returns": "{ scenario, take, path, json{…}, problems[] }",
    "example": {
      "args": {
        "scenario": "ua-tank-ammo",
        "take": "main"
      },
      "gist": "read a take"
    },
    "hint": "Req: scenario, take (main is the default take). Example: director_get_take({scenario:\"ua-tank-ammo\", take:\"main\"})",
    "manualVerify": false
  },

  {
    "name": "director_put_take",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — write a take (authoring by an agent): the body is the take; refused, with every problem, when a key or a value is wrong.",
    "http": {
      "method": "PUT",
      "path": "/director/take/{scenario}/{take}"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": true,
        "description": "The scenario's name"
      },
      {
        "name": "take",
        "type": "string",
        "required": true,
        "description": "The take's name"
      },
      {
        "name": "shots",
        "type": "array",
        "required": true,
        "description": "The shots, in order"
      },
      {
        "name": "anchors",
        "type": "object",
        "required": false,
        "description": "Named beats the scout bakes (on: shot · blast · hit · near · task · side · health · embark · gesture)"
      },
      {
        "name": "title",
        "type": "string",
        "required": false,
        "description": "The take's title (its card)"
      },
      {
        "name": "caption",
        "type": "string",
        "required": false,
        "description": "The caption shown under the picture"
      },
      {
        "name": "tickHz",
        "type": "number",
        "required": false,
        "description": "The fixed step rate: 60 (default), 20 or 15 when 60 cannot keep up in real time"
      },
      {
        "name": "endAt",
        "required": false,
        "description": "When the take ends: sim seconds or an anchor expression (default: the last shot's end)"
      }
    ],
    "returns": "{ saved, shots, anchors }",
    "notes": [
      "docs/DESIGN_Film_Director.md §4. The shot keys are film.py's plan keys plus to, ease, speed, caption, layers, sound."
    ],
    "example": {
      "args": {
        "scenario": "ua-tank-ammo",
        "take": "short",
        "shots": [
          {
            "at": 1,
            "seconds": 4,
            "follow": "Tank",
            "distance": 20
          }
        ]
      },
      "gist": "write a take"
    },
    "hint": "Req: scenario, take, and the take's keys: title?, caption?, tickHz? (60 | 20 | 15), endAt?, anchors{name:{on, who?, by?, target?, is?, n?, point?, within?, below?}}, shots[{at, seconds, follow | over+toward | lookAt | eye+target, distance?, yaw?, pitch?, orbit?, lift?, back?, height?, ahead?, fov?, to?, ease?, speed?, caption?, layers?, sound?}]. 'at' is sim seconds or '<anchor>[+-offset]'. Example: director_put_take({scenario:\"ua-tank-ammo\", take:\"short\", shots:[{at:1, seconds:4, follow:\"Tank\", distance:20}]})",
    "manualVerify": false
  },

  {
    "name": "director_list_takes",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 — the film takes: every scenario with a takes/ folder and its takes (scenarios/<s>/takes/<t>.take.json).",
    "http": {
      "method": "GET",
      "path": "/director/takes"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": false,
        "description": "Only this scenario's takes"
      }
    ],
    "returns": "{ root, count, scenarios[{ scenario, takes[] }] }",
    "notes": [
      "docs/DESIGN_Film_Director.md §4, D-2: a take lives beside its scenario; 'Save As' copies the takes."
    ],
    "example": {
      "args": {},
      "gist": "list the film takes"
    },
    "hint": "Optional: scenario (one scenario's takes). Example: director_list_takes({})",
    "manualVerify": false
  },

  {
    "name": "director_verify",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4042 — verify scenarios: each loaded from its file, stepped once per frame for runFor, judged by its checks.json and compared with its golden.json (the first difference named). Read the verdicts in director_status's reports[].verify.",
    "http": {
      "method": "POST",
      "path": "/director/verify"
    },
    "params": [
      {
        "name": "scenario",
        "type": "string",
        "required": false,
        "description": "One scenario"
      },
      {
        "name": "scenarios",
        "type": "array",
        "required": false,
        "description": "Several scenarios (one process: each after the first is a reload)"
      },
      {
        "name": "all",
        "type": "boolean",
        "required": false,
        "description": "Every scenario with a checks.json"
      },
      {
        "name": "capture",
        "type": "boolean",
        "required": false,
        "description": "Re-record each golden (default false)"
      },
      {
        "name": "fresh",
        "type": "boolean",
        "required": false,
        "description": "Run in a fresh child process per scenario (the regression verdict); answers a job id"
      }
    ],
    "returns": "the director's status (phase, reports[{ status: pass|fail, verify{ passed, verdicts[{name, ok, t, kind}], golden: none|same|different|missing|captured|not-captured, goldenDiff[], written[], problems[] } }])",
    "notes": [
      "docs/DESIGN_Scenario_Regression.md — R-4065 (conditions for all, a golden for a feature scenario, a fresh process each), R-4066 (the engine runs it; HTTP and the command line drive it)."
    ],
    "example": {
      "args": {
        "scenario": "ua-tank-ammo"
      },
      "gist": "verify a scenario against its checks and golden"
    },
    "hint": "{scenario} or {scenarios:[…]} or {all:true}; capture:true re-records the golden (refused for a failing run, and refused on a RELOAD). ⭐ fresh:true runs it in a FRESH child process per scenario — the regression verdict and the only safe re-record; it answers {job} at once, read the report with director_verify_job. Without fresh it plays in THIS editor (on screen; each run after the first is a reload, CE-3294). Example: director_verify({scenario:\"ua-tank-ammo\", fresh:true})",
    "manualVerify": false
  },

  {
    "name": "director_verify_jobs",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4042 — the fresh-process verify jobs this editor started (POST /director/verify {fresh:true}): each one's state, exit code and output folder.",
    "http": {
      "method": "GET",
      "path": "/director/verify/jobs"
    },
    "params": [],
    "returns": "{ jobs[{ job, state: running|done, exitCode, passed, scenarios[], capture, out, junit, log, seconds }] }",
    "example": {
      "args": {},
      "gist": "list the fresh verify jobs"
    },
    "hint": "Example: director_verify_jobs({})",
    "manualVerify": false
  },

  {
    "name": "director_verify_job",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4042 — one fresh-process verify job: running or done, and when done its full report (every scenario's verdicts, golden, first difference).",
    "http": {
      "method": "GET",
      "path": "/director/verify/jobs/{job}"
    },
    "params": [
      {
        "name": "job",
        "type": "string",
        "required": true,
        "description": "The job id"
      }
    ],
    "returns": "{ job, state, exitCode, passed, scenarios[], out, junit, log, seconds, report?{ passed, failed[], scenarios[{ scenario, status, verdicts[], golden, goldenDiff[], written[] }] } }",
    "example": {
      "args": {
        "job": "v1-101500"
      },
      "gist": "read a fresh verify job's report"
    },
    "hint": "Req: job (from director_verify {fresh:true}). Poll until state is done. Example: director_verify_job({job:\"v1-101500\"})",
    "manualVerify": false
  },

  {
    "name": "film_capture",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-3289 — write the next drawn frame's 3-D SCENE (sky, terrain, bodies, effects, rounds — no panels, gizmos, cards or HUD) as a PNG, at any size.",
    "http": {
      "method": "POST",
      "path": "/film/capture"
    },
    "params": [
      {
        "name": "path",
        "type": "string",
        "required": true,
        "description": "Where to write the PNG"
      },
      {
        "name": "width",
        "type": "number",
        "required": false,
        "description": "Frame width in px (default 1280)"
      },
      {
        "name": "height",
        "type": "number",
        "required": false,
        "description": "Frame height in px (default 720)"
      },
      {
        "name": "supersample",
        "type": "number",
        "required": false,
        "description": "Render this many times larger and scale down, 1-4 (default 1)"
      }
    ],
    "returns": "{ path, width, height, simTime }",
    "notes": [
      "docs/DESIGN_Map_3D_Mode.md §6ab; scripts/film.py drives it."
    ],
    "example": {
      "args": {
        "path": "/tmp/film/0001.png"
      },
      "gist": "capture the scene to a PNG"
    },
    "hint": "Req: path (.png). Optional: width (1280), height (720), supersample (1, up to 4 — renders larger and scales down). Needs a window (Xvfb) and the map on screen; 504 after 30 s otherwise. Pause the sim and step it between captures for a smooth film. Example: film_capture({path:\"/tmp/f/0001.png\"})",
    "manualVerify": false
  },

  {
    "name": "film_release",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-3289 — give the 3-D map's camera back to the operator (it keeps the last pose).",
    "http": {
      "method": "DELETE",
      "path": "/film/shot"
    },
    "params": [],
    "returns": "{ released: true }",
    "example": {
      "args": {},
      "gist": "release the film camera"
    },
    "hint": "Example: film_release({})",
    "manualVerify": false
  },

  {
    "name": "film_shot",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-3289 — hold the 3-D map's camera on a shot (a pose, an eye and a target, or a unit to follow) until released; switches the map to 3-D. The operator's and the scenario's camera are left alone.",
    "http": {
      "method": "POST",
      "path": "/film/shot"
    },
    "params": [
      {
        "name": "follow",
        "type": "number",
        "required": false,
        "description": "Network id of the unit to follow (its position, every frame)"
      },
      {
        "name": "eye",
        "type": "array",
        "required": false,
        "description": "The camera position [x,y,z] (with target)"
      },
      {
        "name": "target",
        "type": "array",
        "required": false,
        "description": "The point looked at [x,y,z] (with eye)"
      },
      {
        "name": "lookAt",
        "type": "array",
        "required": false,
        "description": "The point looked at [x,y,z] (a pose, with distance / yawDeg / pitchDeg)"
      },
      {
        "name": "distance",
        "type": "number",
        "required": false,
        "description": "Metres from the point looked at (follow: 30, lookAt: 100)"
      },
      {
        "name": "yawDeg",
        "type": "number",
        "required": false,
        "description": "View heading, degrees from east counter-clockwise (default 0)"
      },
      {
        "name": "pitchDeg",
        "type": "number",
        "required": false,
        "description": "View elevation, degrees, negative looks down (follow: -20, lookAt: -30)"
      },
      {
        "name": "lift",
        "type": "number",
        "required": false,
        "description": "Follow: metres above the unit's position looked at (default 1)"
      },
      {
        "name": "fovDeg",
        "type": "number",
        "required": false,
        "description": "Vertical field of view, degrees (default: the camera's 45; narrow = telephoto)"
      },
      {
        "name": "spec",
        "type": "object",
        "required": false,
        "description": "⭐ CE-4038 — a take's shot in its own keys (docs/DESIGN_Film_Director.md §4), units by name"
      },
      {
        "name": "t",
        "type": "number",
        "required": false,
        "description": "With spec: sim seconds into the shot (its orbit and its 'to')"
      }
    ],
    "returns": "{ follow, lookAt[3], distance, yawDeg, pitchDeg, fovDeg } — the shot held",
    "notes": [
      "docs/DESIGN_Map_3D_Mode.md §6ab. The camera is a driver, like first person: never saved to the scenario."
    ],
    "example": {
      "args": {
        "follow": 1000,
        "distance": 25,
        "yawDeg": 200,
        "pitchDeg": -15
      },
      "gist": "follow unit 1000 from behind and above"
    },
    "hint": "One of: {follow, distance?, yawDeg?, pitchDeg?, lift?} · {eye:[x,y,z], target:[x,y,z]} · {lookAt:[x,y,z], distance?, yawDeg?, pitchDeg?} · ⭐ CE-4038 {spec:{a take's shot — follow · over+toward · lookAt · eye+target, by NAME, with orbit, to, fov}, t:sim seconds into it} (evaluated by the director's shot math). Yaw from east counter-clockwise (90 = looking north), pitch negative looks down; any shot may add fovDeg (a telephoto lens). Example: film_shot({follow:1000, distance:25, yawDeg:200, pitchDeg:-15})",
    "manualVerify": false
  },

  {
    "name": "get_map_layers",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 B10 (R-4060) — the tactical map's layers, by the names and hover text the Layers panel shows, per view (2-D and 3-D sets), and whether a film take's override is in force.",
    "http": {
      "method": "GET",
      "path": "/map/layers"
    },
    "params": [
      {
        "name": "view",
        "type": "string",
        "required": false,
        "description": "2d or 3d (default both)"
      }
    ],
    "returns": "{ shown: \"2d\"|\"3d\", override, 2d[{ name, display, on, doc }], 3d[…] }",
    "example": {
      "args": {
        "view": "3d"
      },
      "gist": "list the map's layers"
    },
    "hint": "Optional: view (2d | 3d). Example: get_map_layers({view:\"3d\"})",
    "manualVerify": false
  },

  {
    "name": "set_map_layers",
    "group": "Y — node diagnostics",
    "summary": "⭐ CE-4038 B10 (R-4060) — turn map layers on and off EXACTLY as the user's click does (saved to the user's file like one).",
    "http": {
      "method": "POST",
      "path": "/map/layers"
    },
    "params": [
      {
        "name": "view",
        "type": "string",
        "required": false,
        "description": "2d or 3d (default: the view shown)"
      },
      {
        "name": "on",
        "type": "array",
        "required": false,
        "description": "Layers to turn on"
      },
      {
        "name": "off",
        "type": "array",
        "required": false,
        "description": "Layers to turn off"
      }
    ],
    "returns": "the view's layers (as get_map_layers)",
    "notes": [
      "A film take's layers are NOT this: they are a transient override, never saved (docs/DESIGN_Film_Director.md §4b)."
    ],
    "example": {
      "args": {
        "view": "3d",
        "on": [
          "Ai.VisionCones"
        ]
      },
      "gist": "show the vision cones"
    },
    "hint": "Optional: view (2d | 3d, default the one shown), on[], off[] — names as get_map_layers lists them (Group.Row). Example: set_map_layers({view:\"3d\", on:[\"Ai.VisionCones\"], off:[\"Units.Labels\"]})",
    "manualVerify": false
  },

  // ── Group Z — Entity control (orders) ───────────────────────────────────────

  {
    "name": "control_entity",
    "group": "Z — Entity control (orders)",
    "summary": "One control frame for the unit this station possesses — a person: forward/back + strafe in its body frame, facing an absolute yaw; a vehicle: throttle (forward; negative brakes, then reverses) + steer. The newest seq wins; keep sending while the controls are held.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/control"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      },
      {
        "name": "seq",
        "type": "integer",
        "required": true,
        "description": "Frame number, rising — an older frame is ignored"
      },
      {
        "name": "forward",
        "type": "number",
        "required": false,
        "description": "-1..1: a person's forward (+) / back (-, at 60 %); a vehicle's throttle (- brakes, then reverses)"
      },
      {
        "name": "lateral",
        "type": "number",
        "required": false,
        "description": "-1..1: a person's strafe, + = left (at 60 %)"
      },
      {
        "name": "steer",
        "type": "number",
        "required": false,
        "description": "-1..1: a vehicle's steer, + = left"
      },
      {
        "name": "yaw",
        "type": "number",
        "required": false,
        "description": "a person's facing — ABSOLUTE world yaw in radians (0 = +X, counter-clockwise)"
      },
      {
        "name": "gait",
        "type": "integer",
        "required": false,
        "description": "a person's pace: 0 = run, 1 = walk (half speed)"
      },
      {
        "name": "trigger",
        "type": "boolean",
        "required": false,
        "description": "the trigger is held (operator fire — S4d)"
      },
      {
        "name": "aim",
        "type": "array",
        "required": false,
        "description": "[x,y,z] where the station aims, world metres (S4d)"
      },
      {
        "name": "use",
        "type": "integer",
        "required": false,
        "description": "the USE press counter (0-255, wraps): a new value asks the unit's brain to act on useTarget (CE-1151)"
      },
      {
        "name": "useVerb",
        "type": "integer",
        "required": false,
        "description": "which verb of the target's list the use press means (0 = the primary)"
      },
      {
        "name": "useTarget",
        "type": "integer",
        "required": false,
        "description": "network id of what the use press acts on (a door, a man who is down, a vehicle, a weapon)"
      }
    ],
    "returns": "{ networkId, accepted:true, detail:'control' } — 409 when this station does not hold the unit.",
    "notes": [
      "Frames only drive a unit possessed by THIS node's station (possess_entity first).",
      "A unit held in Riding mode (its human drives a vehicle) takes no frames."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "seq": 1,
        "forward": 1,
        "yaw": 0
      },
      "gist": "walk unit 1000 forward, facing east"
    },
    "hint": "Req: networkId, seq (rising). Example: control_entity({networkId:1000, seq:1, forward:1, yaw:0})",
    "manualVerify": false
  },

  {
    "name": "release_entity",
    "group": "Z — Entity control (orders)",
    "summary": "Release a unit: the operator's order ends and the unit's own SOP decides again.",
    "http": {
      "method": "DELETE",
      "path": "/entities/{networkId}/order"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      }
    ],
    "returns": "{ networkId, accepted:true, detail:'released' }",
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "hand unit 1000 back to its AI"
    },
    "hint": "Req: networkId. Example: release_entity({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "get_entity_order",
    "group": "Z — Entity control (orders)",
    "summary": "A unit's operator order: running · paused (a reaction interrupted it) · succeeded · failed · faulted · none — and what it runs now.",
    "http": {
      "method": "GET",
      "path": "/entities/{networkId}/order"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      }
    ],
    "returns": "{ networkId, onBrainNode, state, behavior?, params?, since?, interruptedBy?, finishedAt?, fault?, current:{behavior, origin}, task?:{behavior, origin, since, params} }",
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "has unit 1000 arrived?"
    },
    "hint": "Req: networkId. Read on the unit's brain node (the Scenario perspective on a cluster). Example: get_entity_order({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "order_entity",
    "group": "Z — Entity control (orders)",
    "summary": "Order a unit, as an operator: move to a point, attack an enemy, hold (stop), run any behaviour, or release it to its own SOP. The unit works out HOW (route, cover, weapon). The order outranks its SOP and mission and clears its mission plan; works from any node (forwarded to the unit's brain owner).",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/order"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      },
      {
        "name": "kind",
        "type": "string",
        "required": true,
        "description": "move · attack · hold · stop (= hold) · behavior · release",
        "enum": [
          "move",
          "attack",
          "hold",
          "stop",
          "behavior",
          "release"
        ]
      },
      {
        "name": "lat",
        "type": "number",
        "required": false,
        "description": "move: target latitude (degrees) — the portable form"
      },
      {
        "name": "lon",
        "type": "number",
        "required": false,
        "description": "move: target longitude (degrees)"
      },
      {
        "name": "x",
        "type": "number",
        "required": false,
        "description": "move: target easting in THIS node's local metres (converted to lat/lon when the node can)"
      },
      {
        "name": "y",
        "type": "number",
        "required": false,
        "description": "move: target northing in this node's local metres"
      },
      {
        "name": "speed",
        "type": "number",
        "required": false,
        "description": "move: speed in m/s (default: the unit's)"
      },
      {
        "name": "arrivalRadius",
        "type": "number",
        "required": false,
        "description": "move: arrival radius in metres"
      },
      {
        "name": "roadUse",
        "type": "string",
        "required": false,
        "description": "move: Never · Neutral · Prefer · StronglyPrefer · InTraffic (a vehicle handed to SUMO, AQ93 R-4023)"
      },
      {
        "name": "targetId",
        "type": "integer",
        "required": false,
        "description": "attack: the enemy's network id (get_sitrep lists the contacts your side knows)"
      },
      {
        "name": "behavior",
        "type": "string",
        "required": false,
        "description": "behavior: the behaviour name (list_behaviors)"
      },
      {
        "name": "params",
        "type": "object",
        "required": false,
        "description": "behavior: its parameters, matching its paramSchema"
      }
    ],
    "returns": "{ networkId, accepted:true, detail, behavior?, params?, note } — 404 unknown unit, 409 killed / other side (side lock), 400 malformed.",
    "notes": [
      "An order is origin Operator: the unit's SOP and its mission cannot replace it; a reaction (under fire) may pause it unless its ROE says StayOnTask.",
      "kind 'attack' runs CombatPosture with the target designated and assault: the unit closes in and fights THAT enemy (a nearer threat does not take over) and the order SUCCEEDS when it is down. ROE still applies — on HoldFire it closes in without firing.",
      "The order is resolved on the unit's brain node a frame or two later; get_entity_order reads the result there."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "kind": "move",
        "x": 120,
        "y": -40,
        "speed": 3
      },
      "gist": "send unit 1000 to a point"
    },
    "hint": "Req: networkId, kind. Move: {kind:'move', lat,lon} or {x,y} (+ speed, arrivalRadius, roadUse). Attack: {kind:'attack', targetId}. Any behaviour: {kind:'behavior', behavior, params} — discover with list_behaviors({entityId}). Then poll get_entity_order. Example: order_entity({networkId:1000, kind:'move', x:120, y:-40})",
    "manualVerify": false
  },

  {
    "name": "release_possession",
    "group": "Z — Entity control (orders)",
    "summary": "Let a possessed unit go: it stops, and the task it was doing before resumes.",
    "http": {
      "method": "DELETE",
      "path": "/entities/{networkId}/possess"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      }
    ],
    "returns": "{ networkId, accepted:true, detail:'released' } — 409 when another station (or none) holds it.",
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "hand unit 1000 back to its task"
    },
    "hint": "Req: networkId. Example: release_possession({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "possess_entity",
    "group": "Z — Entity control (orders)",
    "summary": "Possess a unit as this node's station (first person): its current task pauses and control_entity frames drive its body until release_possession, or until it dies; the paused task then resumes. Another station's hold is taken over.",
    "http": {
      "method": "POST",
      "path": "/entities/{networkId}/possess"
    },
    "params": [
      {
        "name": "networkId",
        "type": "integer",
        "required": true,
        "description": "Network id of the unit"
      }
    ],
    "returns": "{ networkId, accepted:true, detail:'possessing' } — 404 unknown unit, 409 killed / other side.",
    "notes": [
      "While possessed, ordinary orders to the unit are refused (409 Possessed) — release it first.",
      "Possession is a behaviour (OperatorControl) at origin Possession, above Operator: no reaction interrupts it (R-270)."
    ],
    "example": {
      "args": {
        "networkId": 1000
      },
      "gist": "take unit 1000 in first person"
    },
    "hint": "Req: networkId. Then send control_entity frames; read who holds it with get_entity_order (current.origin = Possession). Example: possess_entity({networkId:1000})",
    "manualVerify": false
  },

  {
    "name": "list_orders",
    "group": "Z — Entity control (orders)",
    "summary": "The order vocabulary (Move, Attack, Hold …): each order's fields — kind (MapPoint · MapEntity · Value), required, entity-pick presets — and its params schema. entityId narrows it to the orders that unit fulfils.",
    "http": {
      "method": "GET",
      "path": "/orders"
    },
    "params": [
      {
        "name": "entityId",
        "type": "integer",
        "required": false,
        "description": "Only the orders this unit (network id) fulfils"
      }
    ],
    "returns": "{ networkId?, orders:[ { name, gesture, pick, fields:[{name, kind, required, presets?}], paramSchema } ] }",
    "example": {
      "args": {
        "entityId": 1000
      },
      "gist": "what can unit 1000 be ordered to do, and what does each order need"
    },
    "hint": "Opt: entityId. A required field must be in the order's params or the order is refused. Example: list_orders({entityId:1000})",
    "manualVerify": false
  },

  {
    "name": "order_entities",
    "group": "Z — Entity control (orders)",
    "summary": "Several orders in one call (a squad, a turn's worth of moves). Each is answered on its own; a refusal does not stop the rest.",
    "http": {
      "method": "POST",
      "path": "/orders"
    },
    "params": [
      {
        "name": "orders",
        "type": "array",
        "required": true,
        "description": "[{ networkId, kind, … }] — the order_entity fields per unit",
        "items": {
          "type": "object",
          "properties": {
            "networkId": {
              "type": "integer"
            },
            "kind": {
              "type": "string"
            }
          }
        }
      }
    ],
    "returns": "{ results:[ { networkId, accepted, detail, behavior?, params? } ] }",
    "example": {
      "args": {
        "orders": [
          {
            "networkId": 1000,
            "kind": "hold"
          },
          {
            "networkId": 1001,
            "kind": "move",
            "x": 50,
            "y": 10
          }
        ]
      },
      "gist": "hold one unit, move another"
    },
    "hint": "Req: orders — an array of order_entity bodies, each with its networkId.",
    "manualVerify": false
  },

  {
    "name": "lock_side",
    "group": "Z — Entity control (orders)",
    "summary": "Lock this API session to one side: orders to any other side's units are refused, and the sitrep defaults to it. side:null unlocks.",
    "http": {
      "method": "POST",
      "path": "/orders/side-lock"
    },
    "params": [
      {
        "name": "side",
        "type": "string",
        "required": false,
        "description": "The side to play, or null to unlock"
      }
    ],
    "returns": "{ side }",
    "example": {
      "args": {
        "side": "Friend"
      },
      "gist": "play the Friend side"
    },
    "hint": "Req: side (Friend · Hostile · Neutral …, or null). Example: lock_side({side:'Friend'})",
    "manualVerify": false
  },

  {
    "name": "get_sitrep",
    "group": "Z — Entity control (orders)",
    "summary": "The situation as ONE side knows it: its units (position, health, ammo, what each is doing) and the contacts its units remember (fog of war; a heard contact has no id). truth=true lists every other unit instead.",
    "http": {
      "method": "GET",
      "path": "/sitrep"
    },
    "params": [
      {
        "name": "side",
        "type": "string",
        "required": false,
        "description": "Friend · Hostile · Neutral … (defaults to the side lock)"
      },
      {
        "name": "truth",
        "type": "boolean",
        "required": false,
        "description": "true = every other unit, not only the known ones (debugging)"
      }
    ],
    "returns": "{ side, truth, units:[{networkId, name?, position, health, killed, ammo?, doing}], contacts:[{networkId?, heard?, position, freshness, radius?, by, alive?}] }",
    "example": {
      "args": {
        "side": "Friend"
      },
      "gist": "what does the Friend side see?"
    },
    "hint": "Opt: side (defaults to the locked side), truth. Read on the brain node (memory lives there). Example: get_sitrep({side:'Friend'})",
    "manualVerify": false
  },

];
