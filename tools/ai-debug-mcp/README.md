# ai-debug-mcp

External Node.js MCP server that proxies the Hrot ClusterRunner AI Debug HTTP API as MCP tools.

**Stack:** Node.js 18+, `@modelcontextprotocol/sdk`, native `fetch`, stdio transport.  
**Location:** `tools/ai-debug-mcp/` — external companion, NOT part of `IOS-IG-SimHost.sln`.

---

## Tool Set

Tools are **strictly 1:1 with currently-implemented HTTP endpoints** (Groups A–N + G + H, BATCHes 02–08).
Tools for not-yet-built endpoints (recording, logs, traces, mutation)
are intentionally absent and will be added in their own batches as those API endpoints land.

| Tool | HTTP | Group |
|------|------|-------|
| `start_simulation` | MCP-only (spawns runner) | A |
| `stop_simulation` | `POST /shutdown` | A |
| `get_status` | `GET /status` | A |
| `list_entities` | `GET /entities` | B |
| `get_entity` | `GET /entities/{networkId}` | B |
| `list_component_types` | `GET /components` | B |
| `list_scenarios` | `GET /scenarios` | B/E |
| `get_event_history` | `GET /events` | C |
| `get_sim_state` | `GET /sim/state` | D |
| `play` | `POST /sim/play` | D |
| `pause` | `POST /sim/pause` | D |
| `step` | `POST /sim/step` | D |
| `set_time_scale` | `POST /sim/timescale` | D |
| `enter_preview` | `POST /preview/enter` | D |
| `stop_preview` | `POST /preview/exit` | D |
| `load_scenario` | `POST /scenario/load` | E |
| `save_scenario` | `POST /scenario/save` | E |
| `list_commands` | `GET /commands` | F |
| `send_entity_command` | `POST /entities/command` | F |
| `spawn_entity` | `POST /entities/spawn` | F |
| `list_entity_types` | `GET /tkb/types` | M |
| `get_entity_type` | `GET /tkb/types/{tkbType}` | M |
| `get_world_info` | `GET /world/info` | N |
| `geo_to_local` | `POST /world/geo-to-local` | N |
| `local_to_geo` | `POST /world/local-to-geo` | N |
| `set_breakpoint` | `POST /breakpoints` | G |
| `list_breakpoints` | `GET /breakpoints` | G |
| `remove_breakpoint` | `DELETE /breakpoints/{id}` | G |
| `get_breakpoint_status` | `GET /breakpoints/hits` | G |
| `checkpoint` | `POST /checkpoint` | H |
| `restore_checkpoint` | `POST /checkpoint/restore` | H |
| `capture_diff_baseline` | `POST /diff/capture` | H |
| `diff_state` | `POST /diff/compare` | H |

**33 tools total.** G and H now present; tools for Groups I (recording/replay),
J (logs), K (traces), L (mutation) are not yet implemented — see DEBT entries ADA-06-D01.

---

## Setup

```bash
cd tools/ai-debug-mcp
npm install
```

Requires Node.js 18+ and the runner DLL built:
```bash
dotnet build Hrot/Runner/Hrot.ClusterRunner -c Debug
```

---

## Running

### Launch mode (server spawns the runner)

```bash
node src/index.mjs \
  --runner-dll path/to/Hrot.ClusterRunner.dll \
  --port 8099 \
  --headless
```

The server spawns `dotnet <dll> -m editor --debug-api --debug-api-port <port> [--headless]`,
polls `GET http://localhost:<port>/status` until 200 (up to 60s), then serves MCP tools.

Use the `start_simulation` tool to do this at runtime:
```json
{ "runnerDll": "/path/to/Hrot.ClusterRunner.dll", "port": 8099, "headless": true }
```

### Attach mode (runner already running)

```bash
node src/index.mjs --url http://localhost:8099
```

In attach mode the server connects to an existing runner and does NOT own the child process
(`stop_simulation` still calls `POST /shutdown`, but does not track a child PID).

---

## Process Lifecycle

- **Launch:** spawns runner, polls `/status`, owns child PID.
- **Kill (graceful→hard):** `stop_simulation` calls `POST /shutdown` (empty body, to avoid
  HttpListener 411), waits 10 s, then `SIGKILL` if still alive.
- **Tear-down on exit:** `SIGINT`/`SIGTERM` handlers call `killRunner()`, then `process.exit(0)`.
  A synchronous `process.on('exit')` fallback `SIGKILL`s any surviving child.
- **No orphans:** a launched child is always killed when the MCP server exits.

---

## Ground-Truth Notes (HttpListener quirks)

- **`localhost` only.** The HttpListener binds to `localhost`; using `127.0.0.1` returns
  HTTP.sys "400 Invalid Hostname". All calls use `http://localhost:<port>/...`.
- **Bodyless POST → 411.** HttpListener requires `Content-Length` on POST. Bodyless POSTs
  (`/shutdown`, `/sim/play`, `/sim/pause`, etc.) send `body: ''` so `fetch` sets
  `Content-Length: 0`. This is handled automatically in `callApi`.
- **Envelope passthrough.** `{ok, data, error, awaited}` is returned verbatim. `ok:false` or
  non-2xx responses surface as structured MCP tool errors carrying the API `error` message.

---

## Verification (single command)

Run from `tools/ai-debug-mcp/`:

```bash
npm run verify
```

Or explicitly:

```bash
node verify.mjs [--runner-dll <path>] [--port <N>]
```

Environment overrides:
- `RUNNER_DLL` — path to `Hrot.ClusterRunner.dll`
- `DEBUG_PORT` — port (default 8099)

**Default DLL path:** `../../Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll`
(relative to `tools/ai-debug-mcp/`).

### What it verifies

End-to-end flow over MCP using the real runner:

1. **Tool registration** — all 29 expected tool names present
2. **`start_simulation`** — spawns runner, polls until ready
3. **`get_status`** — liveness, ok:true
4. **`load_scenario("test-move", waitForReady:true)`** — blocks until OperatingEdit
5. **`list_entities`** — entityCount > 0 after scenario load
6. **`get_entity`** — full dump for first entity
7. **`get_world_info`** — Berlin origin + spatial grid
8. **`list_entity_types`** — 15 TKB types
9. **`spawn_entity`** — spawns additional entity
10. **`get_status (post-spawn)`** — entityCount grew (1 → 2)
11. **`awaited:false` envelope passthrough** — `send_entity_command(wait:true)` while paused
12. **Deliberate error** — `get_entity(-999999)` surfaces as structured MCP tool error
13. **`stop_simulation`** — graceful shutdown, runner exits 0
14. **No orphan check** — tracked child gone after stop

Exits non-zero on any assertion failure.

---

## MCP Client Configuration

Claude Code / MCP client configuration example:

```json
{
  "mcpServers": {
    "ai-debug": {
      "command": "node",
      "args": [
        "/path/to/tools/ai-debug-mcp/src/index.mjs",
        "--runner-dll", "/path/to/Hrot.ClusterRunner.dll",
        "--port", "8099",
        "--headless"
      ]
    }
  }
}
```

Or attach to a running session:

```json
{
  "mcpServers": {
    "ai-debug": {
      "command": "node",
      "args": [
        "/path/to/tools/ai-debug-mcp/src/index.mjs",
        "--url", "http://localhost:8099"
      ]
    }
  }
}
```
