# ADA-BATCH-06: Node.js MCP Server (scaffold + lifecycle + tool definitions)

**Batch Number:** ADA-BATCH-06
**Tasks:** ADA-PM-T01 (scaffold), ADA-PM-T02 (process lifecycle), ADA-PM-T03 (1:1 tool definitions)
**Phase:** Phase MCP — the external companion that makes the loop usable end-to-end
**Estimated Effort:** ~16 hours
**Executor:** sonnet (stdio MCP wiring + child-process lifecycle + graceful→hard kill are subtle)
**Priority:** HIGH (recommended order is P0 → P1 → **P-MCP** → leverage tier)
**Dependencies:** Phase 1 complete (all groups A–N HTTP endpoints exist and are lead-verified).

---

## Onboarding & Workflow

Build the external Node.js MCP server: a **thin** stdio proxy that maps MCP tools 1:1 onto the runner's HTTP
endpoints, plus runner process lifecycle (launch / attach / graceful→hard kill). No business logic — the API
owns all semantics (including wait-gating); the server passes the `{ok,data,error,awaited}` envelope through
verbatim.

### Required reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md`
2. `.dev/_DONE/ai-debug-api/reviews/ADA-BATCH-04-REVIEW.md` + `ADA-BATCH-05-REVIEW.md` (the real-reproduce gate; the
   "assert the spec, not impl-reach" lesson).
3. **Design:** `.dev/_DONE/ai-debug-api/DESIGN.md` — "MCP Server (Node.js)" section + the "API Surface
   (shared HTTP ↔ MCP spec)" table (the authoritative endpoint list).
4. **Task detail:** `.dev/_DONE/ai-debug-api/TASK-DETAIL.md` — ADA-PM-T01, ADA-PM-T02, ADA-PM-T03.

> No codebase-memory MCP (hangs — Grep/Glob/Read only). No git commit. Report HONESTLY — the lead re-runs the
> server's own test/verification AND a real launch→drive→stop reproduce, and reads the full diff. Do NOT fake
> or narrow verification to pass.

### Location & stack
- Place the server at **`tools/ai-debug-mcp/`** (repo root; it is an external companion app, NOT part of
  `IOS-IG-SimHost.sln`). `package.json`, `src/`, `README.md`.
- Node 18+, `@modelcontextprotocol/sdk`, native `fetch`. stdio transport. Keep dependencies minimal.

### Ground truth from the running API (use these EXACT details — learned from real reproduce)
- CLI flags are **`--debug-api --debug-api-port <N>`** and optional **`--headless`** (NOT `--port`).
  Runner DLL: `Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll`; launch via
  `dotnet <dll> -m editor --debug-api --debug-api-port <N> --headless`.
- **The HttpListener binds to `localhost`** — poll/call `http://localhost:<N>/...`. Calling `127.0.0.1`
  returns HTTP.sys "400 Invalid Hostname". Use the `localhost` hostname.
- `POST` with no body → HttpListener returns **411** (needs Content-Length). For bodyless POSTs
  (`/shutdown`, `/sim/play`, etc.) send an explicit empty body so fetch sets `Content-Length: 0`.
- Envelope is `{ "ok": bool, "data": <JsonNode|null>, "error": string|null, "awaited": bool|null }`.
  Pass it through verbatim; surface `ok:false`/HTTP errors as structured MCP tool errors with `error`.

---

## Scope

### T01 — Scaffold
- stdio MCP server; tool registry; a generic `callApi(method, path, body?)` that does the `localhost` fetch,
  parses the envelope, and returns it verbatim (success → `data`; `ok:false` or non-2xx → MCP tool error
  carrying the API `error` message). Bodyless POSTs send `''`.
- Config via env/args: `--url` (attach mode base URL) OR launch params (`--runner-dll`, `--port`, `--headless`).

### T02 — Process lifecycle
- **launch:** spawn the runner with the flags above; poll `GET http://localhost:<N>/status` until 200
  (wall-clock timeout, e.g. 60s); own the child.
- **attach:** use a configured base URL of an already-running instance; do not own/kill it.
- **kill (graceful→hard):** `POST /shutdown` (empty body) → wait with timeout → `SIGKILL` if still alive.
  Tear down a launched child on MCP-server exit (process exit / SIGINT / SIGTERM) — never leak children.

### T03 — Tool definitions (1:1 with CURRENTLY-IMPLEMENTED endpoints)
Define one MCP tool per HTTP endpoint that EXISTS today (groups A–N implemented in BATCHes 02–05), input
schemas mirroring the request bodies, each calling `callApi`. Plus the two MCP-only lifecycle tools
(`start_simulation`, `stop_simulation`). Strictly 1:1; **no composite tools**.

Currently-implemented endpoints to mirror (verify each against the route table in `DebugApiHost.cs`):
`GET /status`, `GET /entities`, `GET /entities/{id}`, `GET /events`, `GET /sim/state`,
`POST /sim/play|pause|step|time-scale`, `POST /preview/enter|exit`, `GET /scenario/list`,
`POST /scenario/load`, `POST /scenario/save`, `GET /commands`, `GET /components`,
`POST /entities/command`, `POST /entities/spawn`, `GET /tkb/types`, `GET /tkb/types/{id}`,
`GET /world/info`, `POST /world/geo-to-local`, `POST /world/local-to-geo`, `POST /shutdown`.

> Do NOT define tools for endpoints that don't exist yet (breakpoints, checkpoint, recording, logs, traces,
> mutation). Those tools get added in their own batches as the endpoints land — keep the tool set 1:1 with
> reality, not with the full future table. Note this clearly in the README so it's not mistaken for a gap.

## Verification (ship it; loop to green — this MUST be automatable for the lead to re-run)
- A Node verification script (e.g. `npm run verify` / `node verify.mjs`) that:
  1. **launches** the real runner via the server's launch path (build the runner first if needed:
     `dotnet build Hrot/Runner/Hrot.ClusterRunner -c Debug`),
  2. drives a representative end-to-end flow over MCP using **only implemented** endpoints:
     `start_simulation → get_status → load_scenario(test-move) → list_entities → get_entity →
     get_world_info → get_tkb_types → spawn_entity → get_status (entityCount grew) → stop_simulation`,
  3. asserts the envelope passthrough (including an `awaited:false` case) and a deliberate API error
     surfacing as an MCP error,
  4. exits non-zero on any failure.
- Also verify graceful→hard kill: `stop_simulation` shuts a healthy runner down; killing the MCP server
  terminates a launched child (no orphan `dotnet`/`Hrot.ClusterRunner` process left).
- Document exactly how the lead re-runs the verification (single command) in the README.

## Constraints (hard)
- Thin proxy ONLY — no business logic, no client-side wait reasoning; envelope verbatim.
- 1:1 tools↔endpoints; no composites; names mirror the API paths/groups.
- `localhost` (not 127.0.0.1); empty body on bodyless POSTs; graceful→hard kill with timeout; no orphan children.
- The server is external — do NOT add it to `IOS-IG-SimHost.sln` or the .NET build.

## Deliverables
- `tools/ai-debug-mcp/` (package.json, src, README with run + verify instructions + the "tools are 1:1 with
  currently-implemented endpoints" note).
- A runnable verification script + its output captured in the report.
- `.dev/_DONE/ai-debug-api/reports/ADA-BATCH-06-REPORT.md` (DEV-GUIDE format): built/installed, decisions/deviations,
  the FULL verification-run output (launch→drive→stop, envelope passthrough, error surfacing, no-orphan check),
  blockers, debt → DEBT-TRACKER.
