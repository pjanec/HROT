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
      "Non-finite floats appear as string sentinels \"NaN\"/\"Infinity\"/\"-Infinity\" — valid JSON, not a bug."
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
      "In --mode all this load is PARTIAL: CGF has no edit-load handler yet, so SimHost loads and CGF does not. Use load_scenario_live when every node must hold the world."
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
    "returns": "{ isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }",
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
      "Read off-thread — no main-thread marshal required."
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
      "Commands and spawns while paused are queued and take effect on the next step/play."
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
      }
    ],
    "returns": "ok:true envelope.",
    "notes": [
      "Only advances time when inPreview==true. In Edit state this is a no-op."
    ],
    "example": {
      "args": {
        "count": 5
      },
      "gist": "advance 5 simulation ticks"
    },
    "hint": "No required params. Optional: count (number, def 1). Example: step({count:5})",
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
      "awaited:false is NOT an error — it means time was not advancing."
    ],
    "example": {
      "args": {
        "eventType": "MissionControlIntent",
        "payload": {
          "targetId": 1000
        },
        "wait": false
      },
      "gist": "publish MissionControlIntent event"
    },
    "hint": "Req: eventType (string from list_commands). Optional: payload (object), wait (bool). Example: send_entity_command({eventType:\"MissionControlIntent\",payload:{}})",
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
      }
    ],
    "returns": "ok:true envelope. Spawn is processed on the next tick (step to realize it).",
    "notes": [
      "Spawn is queued and processed on the next tick — call step to realize it.",
      "Use list_entity_types to discover valid tkbType values."
    ],
    "example": {
      "args": {
        "tkbType": 1001,
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
      "gist": "spawn entity type 1001 at position (100,0,50)"
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
    "summary": "Apply a JSON attribute patch to an entity.",
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
    "returns": "Updated entity dump on success.",
    "notes": [
      "Authority-aware; unregistered keys are silently ignored (no error).",
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
        "tkbType": 1001
      },
      "gist": "inspect TKB descriptor for type 1001"
    },
    "hint": "Req: tkbType (number/long from list_entity_types). Example: get_entity_type({tkbType:1001})",
    "manualVerify": false
  },

  // ── Group N — World / coordinates ───────────────────────────────────────────

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
    "summary": "World metadata: geo origin, spatial grid extent. terrain and navmesh are null in editor mode.",
    "http": {
      "method": "GET",
      "path": "/world/info"
    },
    "params": [],
    "returns": "{ geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null }",
    "notes": [
      "terrain and navmesh are null in editor mode."
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
      "The commit is asynchronous: it resolves when the engine acknowledges. If the sim is not being pumped at all the call returns a 504 pointing at play/step.",
      "A stale version yields a 409 (ERR_VERSION_CONFLICT), never a silent overwrite."
    ],
    "example": {
      "args": {
        "networkId": 1000,
        "behavior": "MoveToLocation",
        "params": {
          "Latitude": 50.1,
          "Longitude": 14.4
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
    "returns": "{ networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier} }",
    "notes": [
      "A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.",
      "A convenience over get_entity, reading the same components: the two cannot disagree."
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
      "A shape with no field projection yet is reported by name with a note, never as aliased bytes."
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
    "returns": "{ subsystems[{ subsystem, perspective, modules[], systems[], translators[], moduleCount, systemCount, translatorCount }], note }",
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

];
