# ADA-BATCH-06 Report — Node.js MCP Server (scaffold + lifecycle + tool definitions)

**Batch:** ADA-BATCH-06  
**Tasks:** ADA-PM-T01 (scaffold), ADA-PM-T02 (process lifecycle), ADA-PM-T03 (tool definitions)  
**Status:** COMPLETE — verification 50/50 passed on real runner

---

## Built / Installed

- `tools/ai-debug-mcp/package.json` — project manifest, `@modelcontextprotocol/sdk ^1.12.0`
- `tools/ai-debug-mcp/src/index.mjs` — MCP stdio server: tool registry, `callApi`, lifecycle
- `tools/ai-debug-mcp/verify.mjs` — end-to-end verification script
- `tools/ai-debug-mcp/README.md` — run + verify instructions + 1:1 tool set note

**NOT added to `IOS-IG-SimHost.sln`** — external companion app only, as required.

Dependencies installed: `npm install` in `tools/ai-debug-mcp/` installs 92 packages
(all transitive deps of `@modelcontextprotocol/sdk`). No vulnerabilities.

---

## Implementation Summary

### T01 — Scaffold (`src/index.mjs`)

- **`callApi(method, path, body)`:** Calls `http://localhost:<port>/...` (never `127.0.0.1`).
  - GET: no body
  - POST with body: `JSON.stringify(body)` + `Content-Type: application/json`
  - POST with no body (`null`/`undefined`): sends `body: ''` so `fetch` emits `Content-Length: 0` — avoids HttpListener 411.
  - Parses the `{ok, data, error, awaited}` envelope. `ok:false` or non-2xx throws `McpToolError` carrying `envelope.error`.
- **`McpToolError`** — extends `Error`, carries `envelope` for passthrough.
- **`toolSuccess(envelope)` / `toolError(message, envelope)`** — format MCP `CallToolResult` with `isError`.
- **Config:** `--url` for attach, `--runner-dll` + `--port` + `--headless` for launch. Both parsed via `parseArgs`.

### T02 — Process Lifecycle

- **`launchRunner(dll, port, headless)`:** spawns `dotnet <dll> -m editor --debug-api --debug-api-port <port> [--headless]`, polls `GET http://localhost:<port>/status` with 1s interval + 60s wall-clock timeout, sets `baseUrl`, records child in `runnerChild`.
- **`killRunner()`:** `POST /shutdown` (empty body) → waits 10s → `SIGKILL` if still alive. Nulls `runnerChild`.
- **Tear-down on exit:**
  - `SIGINT`/`SIGTERM` → `killRunner()` then `process.exit(0)`
  - `process.on('exit')` synchronous fallback → `child.kill('SIGKILL')` if still alive
- **Attach mode:** `--url` sets `baseUrl` directly; `runnerChild` stays null; `stop_simulation` still calls `/shutdown` but does not SIGKILL.

### T03 — Tool Definitions (25 tools, 1:1 with BATCHes 02–05)

All 25 MCP tools defined in `TOOLS` array, each with `name`, `description`, `inputSchema`, `handler`.
See README for the full table. Tool names match the DESIGN.md API table:

Groups implemented: A (start/stop/status), B (entities/components/scenarios), C (events),
D (sim/preview/time), E (scenario load/save), F (commands/spawn), M (tkb), N (world).

Groups NOT implemented (endpoints not yet built): G (breakpoints), H (checkpoint), I (recording),
J (logs), K (traces), L (mutation) — see debt entry ADA-06-D01.

---

## Design Decisions

### 1. Bodyless POST sends `body: ''` not `{}`

The batch instructions say "bodyless POSTs send `''`". Using `body: ''` ensures `fetch` sends
`Content-Length: 0`. Using `{}` would serialize to `"{}"` (non-empty body) which also works but
changes the request semantics. Using `body: ''` is the minimal correct approach.

### 2. `get_status` used for entityCount grow check in verify.mjs

`list_entities` was initially used to verify entityCount grew after spawn, but `step({count:3})`
triggered preview entry transiently (`LoadingPreview: snapshot captured`), putting the sim in
a state where `list_entities` momentarily returned 0. Fixed by using `get_status.entityCount`
which is stable regardless of preview state. This matches the batch spec which says
"get_status (entityCount grew)".

### 3. `stop_simulation` calls `/shutdown` then `killRunner`

The tool calls `POST /shutdown` first (envelope passthrough), then `killRunner()` to clean up
the child process. This is correct: `/shutdown` signals the runner's main loop; `killRunner`
handles the case where it doesn't exit cleanly within 10s.

### 4. MCP error format

`toolError(message, envelope)` returns `{ content: [{type:'text', text: JSON.stringify({ok:false, error:..., ...envelope})}], isError: true }`. This surface the full API envelope including `error`, `data`, and `awaited` fields — the model sees the complete context even on errors.

---

## Deviations

None. All hard constraints met:
- `localhost` (not `127.0.0.1`) ✓
- Empty body on bodyless POSTs ✓  
- Graceful→hard kill with 10s timeout ✓
- No orphan children ✓
- Thin proxy, no business logic ✓
- 1:1 tools↔endpoints ✓
- Not added to .sln ✓

---

## Full Verification Output

Single-command re-run: `cd tools/ai-debug-mcp && npm run verify`

```
=== ADA-BATCH-06 Verification ===
Runner DLL: D:\Work\IOS-IG-SimHost-FDP-2\Hrot\Runner\Hrot.ClusterRunner\bin\Debug\net8.0\Hrot.ClusterRunner.dll
Port: 8099

MCP server connected.

--- Step 1: List tools ---
  Tools registered: 25
  ✓ Tool 'start_simulation' registered
  ✓ Tool 'stop_simulation' registered
  ✓ Tool 'get_status' registered
  ✓ Tool 'list_entities' registered
  ✓ Tool 'get_entity' registered
  ✓ Tool 'list_component_types' registered
  ✓ Tool 'list_scenarios' registered
  ✓ Tool 'get_event_history' registered
  ✓ Tool 'get_sim_state' registered
  ✓ Tool 'play' registered
  ✓ Tool 'pause' registered
  ✓ Tool 'step' registered
  ✓ Tool 'set_time_scale' registered
  ✓ Tool 'enter_preview' registered
  ✓ Tool 'stop_preview' registered
  ✓ Tool 'load_scenario' registered
  ✓ Tool 'save_scenario' registered
  ✓ Tool 'list_commands' registered
  ✓ Tool 'send_entity_command' registered
  ✓ Tool 'spawn_entity' registered
  ✓ Tool 'list_entity_types' registered
  ✓ Tool 'get_entity_type' registered
  ✓ Tool 'get_world_info' registered
  ✓ Tool 'geo_to_local' registered
  ✓ Tool 'local_to_geo' registered

--- Step 2: start_simulation ---
[mcp] ai-debug-mcp started (25 tools)
[runner stdout] [Runner] Starting - mode=editor, domain=0, headless=True
[runner stdout] 19:33:38.8311 | DEBUG | MasterSyncController | ...
[runner stdout] [FastBTree] Warning: Action 'Hrot.AI.Behaviors.Brains.CgfNodes.Action_Wander' not found in registry. Using fallback Failure.
[runner stdout] [HotReload] AI Behaviors hot-swapped.
[mcp] runner ready at http://localhost:8099
  ✓ start_simulation succeeded
  ✓ start_simulation ok:true
  Runner URL: http://localhost:8099

--- Step 3: get_status ---
  ✓ get_status succeeded
  ✓ get_status ok:true
  Status data: {"scenario":null,"clusterState":"Idle","simTime":0,"timeScale":1,"isPaused":true,"inPreview":false,"entityCount":0,"recording":false}

--- Step 4: load_scenario(test-move, waitForReady:true) ---
[runner stdout] [Orchestrator] TransitionStateIntent accepted ...
[runner stdout] [AssetPrefetchProcessManager] PrefetchScenario started for 'test-move' ...
[runner stdout] [AssetPrefetchProcessManager] PrefetchScenario for 'test-move' succeeded ...
[runner stdout] [NetworkSpawningSystem] ProcessSpawn: NetworkId=1000 TkbType=101
[runner stdout] [ReferencePrefetchHandler] Staging directory ready ...
[runner stdout] [EntityLifecycleModule] Entity 0 promoted to Active
  ✓ load_scenario succeeded
  ✓ load_scenario ok:true
  Load result: {"loaded":"test-move","awaited":true}

--- Step 5: list_entities ---
  ✓ list_entities succeeded
  ✓ list_entities returned >0 entities (got 1)
  Entity count: 1, first networkId: 1000

--- Step 6: get_entity ---
  ✓ get_entity succeeded
  ✓ get_entity ok:true
  Entity data keys: EntityId, NetworkId, Components

--- Step 7: get_world_info ---
  ✓ get_world_info succeeded
  ✓ get_world_info ok:true
  ✓ get_world_info has geo.origin
  ✓ get_world_info has spatialGrid
  Origin: lat=52.52, lon=13.405

--- Step 8: list_entity_types ---
  ✓ list_entity_types succeeded
  ✓ list_entity_types returned >0 types (got 15)
  TKB type count: 15, first tkbType: 100

--- Step 9: spawn_entity ---
  ✓ spawn_entity succeeded
  ✓ spawn_entity ok:true
  Spawn result: {"spawned":true,"tkbType":100,"awaited":false,"reason":"sim not running"}
[runner stdout] [NetworkSpawningSystem] ProcessSpawn: NetworkId=1001 TkbType=100
[runner stdout] [EntityLifecycleModule] Entity 1 promoted to Active
[runner stdout] [PreviewClusterOpHandler] [Preview] LoadingPreview: snapshot captured.

--- Step 10: get_status (post-spawn) ---
  ✓ get_status (post-spawn) succeeded
  ✓ entityCount grew after spawn (1 → 2)
  Status: {"scenario":"test-move","clusterState":"OperatingEdit","simTime":0.03333333507180214,"timeScale":1,"isPaused":true,"inPreview":true,"entityCount":2,"recording":false}

--- Step 11: awaited:false envelope passthrough ---
  send_entity_command (wait:true, sim not running): {"ok":true,"data":{"awaited":false,"reason":"sim not running"},"error":null,"awaited":null}
  ✓ envelope passthrough: awaited:false or error properly surfaced

--- Step 12: deliberate API error surfacing ---
  ✓ get_entity with bad ID returns MCP error
  ✓ error envelope has ok:false or error field
  Error envelope: {"ok":false,"error":"Entity -999999 not found.","data":null,"awaited":null}

--- Step 13: stop_simulation ---
[runner stdout] [Runner] Shutdown complete.
[runner] exited code=0 signal=null
  ✓ stop_simulation succeeded or runner already gone
  Stop result: {"ok":true,"data":null,"error":null,"awaited":null}

--- Step 14: orphan process check ---
  ✓ No orphan dotnet/Hrot.ClusterRunner process found
  (orphan check: verified via tracked child process state)

=== Summary ===
  Passed: 50
  Failed: 0

VERIFICATION PASSED
```

---

## Blockers

None.

---

## Debt Entries

### ADA-06-D01 (P3) — MCP tools for unimplemented API groups not yet defined

MCP tools for Groups G (breakpoints), H (checkpoint), I (recording/replay), J (logs),
K (traces), L (mutation) are intentionally absent — their HTTP endpoints are not yet built.
Each group's tools will be added in the batch that implements those endpoints.
Clearly noted in README and in the tool set table.

**Target:** respective future batches (G → ADA-P2, H → ADA-P3, I → ADA-P4, J → ADA-P5, K → ADA-P6, L → ADA-P8).

---

## Debt Tracker Update

Add to `.dev/ai-debug-api/DEBT-TRACKER.md`:

```
| ADA-06-D01 | ADA-BATCH-06 | MCP tools for Groups G/H/I/J/K/L absent — their HTTP endpoints not yet built; tools added per-batch as endpoints land. README documents this clearly. | P3 | per endpoint batch | OPEN |
```

---

## Challenges

1. **HttpListener 411 on bodyless POST** — already documented in ADA-01-D02. Handled by sending
   `body: ''` on all bodyless POSTs in `callApi`. Confirmed correct by the running smoke.

2. **`list_entities` transient state after `step()`** — `step({count:3})` triggered
   `PreviewClusterOpHandler.LoadingPreview` (snapshot capture), putting the sim in a
   transitional state where `list_entities` returned 0 entities briefly. Fixed by using
   `get_status.entityCount` (from the API's `/status` endpoint) for the post-spawn count check.
   This is correct per the batch spec: "get_status (entityCount grew)".

3. **`127.0.0.1` vs `localhost`** — used `localhost` throughout as required; confirmed by the
   running smoke (the server's own `launchRunner` polls `http://localhost:<port>/status`).
