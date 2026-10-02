# ADA-BATCH-06 Review (Node.js MCP server — scaffold + lifecycle + tools)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (diff + re-ran verify + independent orphan check).

## Verified independently (lead)
- **Re-ran `npm run verify` myself** → **50/50 passed**, `VERIFICATION PASSED`. Real launch→drive→stop over
  MCP against the actual runner: `start_simulation → get_status → load_scenario(test-move,waitForReady) →
  list_entities (1) → get_entity → get_world_info (Berlin) → list_entity_types (15) → spawn_entity →
  get_status (entityCount 1→2) → stop_simulation (runner exit 0)`.
- **Envelope passthrough:** `send_entity_command(wait:true)` while paused → `{ok:true, data:{awaited:false,
  reason:"sim not running"}}` — the full envelope reaches the model verbatim (no client-side wait reasoning).
- **Error surfacing:** `get_entity(-999999)` → MCP `isError:true` with `{ok:false, error:"Entity -999999 not
  found."}` — the API message is preserved.
- **Orphan check (independent of the script's own claim):** PowerShell `Get-Process Hrot.ClusterRunner`
  showed **0 before and 0 after** the run; runner exited code 0 on `/shutdown`. No leaked child. ✅

## Diff review
- `src/index.mjs` — thin and correct. `callApi` uses `http://localhost:<port>` (not 127.0.0.1), sends `body:''`
  on bodyless POSTs (avoids the 411), parses the `{ok,data,error,awaited}` envelope verbatim, throws
  `McpToolError` carrying the API `error` on `ok:false`/non-2xx. No business logic.
- Lifecycle: `launchRunner` spawns `dotnet <dll> -m editor --debug-api --debug-api-port <N> [--headless]`,
  polls `/status` (60s wall-clock). `killRunner` = `POST /shutdown` (empty body) → 10s wait → SIGKILL.
  Teardown on SIGINT/SIGTERM + a synchronous `process.on('exit')` SIGKILL fallback — defends against orphans.
- **Tool/route audit (all 25 tools vs the 21 actual routes in `DebugApiHost.cs`):** every path matches.
  Specifically confirmed `list_scenarios → GET /scenarios` is correct (the route exists at host line 122 — an
  earlier worry that it should be `/scenario/list` was wrong; the real route is `/scenarios`). `set_time_scale
  → /sim/timescale`, `stop_preview → /preview/exit`, etc. all line up.
- `.gitignore` added by the lead so `node_modules/` (3501 files) is NOT committed; `package-lock.json` kept.

## Coverage note (not blocking)
The verify exercises ~8 tools end-to-end plus the error/await cases. The remaining tools (events, play/pause/
step/timescale, preview enter, save_scenario, list_commands, list_component_types, geo/local convert) are
identical thin passthroughs and their paths were audited against the route table by hand. Low risk; a fuller
sweep can be added when the leverage-tier tools land.

## Debt
- **ADA-06-D01** (agent-logged, correct): MCP tools for Groups G/H/I/J/K/L are absent because their HTTP
  endpoints don't exist yet — they'll be added per-batch as the endpoints land. Documented in the README's
  tool table. This is the deliberate "1:1 with reality, not the future table" stance, not a gap.

## Lesson
For an MCP/process-lifecycle batch, the agent's own "no orphans" claim is exactly what must NOT be trusted at
face value — I verified it with an out-of-band `Get-Process` snapshot before/after, not the script's internal
check. Also did a full by-hand tool↔route audit since the verify only drives a subset; that's how path drift
in an untested tool would be caught (none found). Third clean batch in a row on the real gate; the loop is now
usable end-to-end (launch → drive → stop over MCP).
