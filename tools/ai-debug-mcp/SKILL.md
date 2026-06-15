---
name: ai-debug-sim
description: Drive and inspect a running Hrot ECS simulation (the FDP editor) over the ai-debug MCP server — load scenarios, query/mutate entities, set breakpoints, checkpoint/diff, record/replay, trace AI behaviors. Use when asked to test, debug, reproduce, or author simulation state autonomously.
---

# AI Debug & Test API — Agent Guide

You are driving a **single-process FDP simulation** (the ClusterRunner in `-m editor` mode) through the
`ai-debug` MCP server. Every tool is a thin 1:1 proxy onto an HTTP endpoint; the simulation owns all the
real logic. This guide teaches the mental model, the canonical workflows, and every command.

---

## 1. Mental model (read this first — most mistakes come from skipping it)

**One process, one world.** Brain (AI) and muscle (physics/spawning) share one ECS `EntityRepository`. There
is no network/cluster to reason about. Entities are identified by a stable `networkId` (a long, e.g. `1000`).

**Three run states** — almost every "why didn't that work" is a run-state mistake:

| State | What it means | Time advances? | How you got here |
|-------|---------------|----------------|------------------|
| **Edit** | Authoring; world is static | No | After `load_scenario` |
| **Preview** | A revertible run from a RAM snapshot | Only when **unpaused** | `enter_preview`, `play`, `checkpoint`, or `start_recording{preview}` |
| **Replay** | Read-only playback of a `.fdp` in an isolated sandbox | N/A (you seek frames) | `load_replay` |

- **Time only advances when `inPreview == true` AND `isPaused == false`.** In Edit state the sim is frozen —
  `step`/commands that need ticks won't progress until you enter preview and unpause. Always check
  `get_sim_state` / `get_status` if unsure.
- **Preview is revertible.** Entering preview snapshots the world; exiting (`stop_preview` /
  `restore_checkpoint`) rewinds to that snapshot. This is the basis of checkpoint/restore.
- **Replay is isolated.** Seeking a replay never touches the live world — they are independent.

**Single preview slot.** `checkpoint`, `enter_preview`, and `start_recording{preview}` all use the *same one*
snapshot slot. You cannot nest them. Restore/stop/exit before starting another. Recording and checkpoint are
mutually exclusive.

**The envelope.** Every tool returns `{ ok, data, error, awaited }`. On success read `data`. On failure
`ok:false` and `error` explains why (and the tool result is flagged as an MCP error). `awaited` relates to
wait-gating (below). The server passes this through verbatim — it never hides a failure as success.

**Wait-gating (why you sometimes see `awaited:false`).** Commands that *could* wait for a result only do so
when time is advancing. If you send a command with `wait:true` while the sim is paused/in Edit, you get
`{awaited:false, reason:"sim not running"}` immediately instead of a hang. That is expected — pause-step-inspect
is the intended flow (see Workflow B).

---

## 2. Lifecycle — starting and stopping the runner

| Tool | Purpose | Key params |
|------|---------|-----------|
| `start_simulation` | Launch the runner (or it may already be attached via `--url`) and wait until ready | `runnerDll` (abs path, optional if server has `--runner-dll`), `port` (default 8099), `headless` (bool) |
| `stop_simulation` | Graceful shutdown (`/shutdown` → wait → SIGKILL fallback). Always call when done. | none |
| `get_status` | Liveness + summary: `scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording` | none |

Typical opening: `start_simulation{headless:true}` → poll/observe `get_status` returns `ok` → proceed.
Typical close: `stop_simulation`. Never leave a runner running between unrelated tasks.

---

## 3. Canonical workflows (compose these — they are the point of the API)

### A. Load and inspect
1. `load_scenario {name:"test-move", waitForReady:true}` — blocks until the world is loaded (Edit state).
2. `list_entities` → pick a `networkId`. (Filter with `component` / `near` to avoid dumping everything.)
3. `get_entity {networkId}` → full component dump.

### B. Drive and observe (the safe, deterministic loop)
1. `enter_preview {startPaused:true}` (or `play` then `pause`).
2. Send an action: `send_entity_command {...}` or `spawn_entity {...}`. (While paused these are queued.)
3. `step {count:N}` to advance N ticks and actually process them.
4. `get_entity` / `get_event_history` to observe the result.
Repeat 2–4. This gives you reproducible, frame-by-frame control. (`play` runs free; `pause` to stop.)

### C. Run until a condition (auto-pause)
1. `set_breakpoint {condition:{...SearchPredicateDto...}}` → returns `breakpointId`.
2. `play`.
3. Poll `get_breakpoint_status` → when `isPaused:true`, the condition fired; `lastHit` names the entity.
4. Inspect, then `remove_breakpoint {id}` and continue.

### D. Experiment and revert (try something, undo it)
1. `checkpoint` (snapshots the world; enters preview, paused).
2. Mutate / spawn / `step` to run your experiment.
3. `restore_checkpoint` → the world rewinds to the snapshot exactly. The experiment is undone.
> Note: a mutation only "sticks" into the snapshot's dirty-tracking after a `step` — in practice you step to
> run the experiment anyway, so this is automatic.

### E. State diff (what changed, cheaply)
1. `capture_diff_baseline` → `baselineId` (serializes current entity state).
2. Do something (move, spawn, step).
3. `diff_state {baselineId}` → a tree of only the changed components (plus entity births/deaths).

### F. Record and replay (post-mortem)
1. `start_recording {mode:"preview"}` → enters preview + begins recording; returns `fdpPath`.
2. `play` / `step` to record some frames.
3. `stop_recording` → finalizes the `.fdp` (and rewinds, since it's preview); returns `fdpPath`.
4. `load_replay {fdpPath}` → isolated sandbox; `get_replay_status` shows `totalFrames`.
5. `seek_replay {frame}` / `step_replay {dir}` → move through it; `list_replay_entities` to inspect a frame.
6. `unload_replay` when done. The live world was never touched.

### G. Inspect AI behavior (BTree/HSM/blueprint)
1. `observe_trace {networkId, on:true}` — **arms** tracing (allocates trace buffers). Without this, traces
   are empty.
2. `play` / `step` a few frames so the buffer fills.
3. `get_entity_trace {networkId}` → active node/state + recent history.
4. `observe_trace {networkId, on:false}` to disarm.

### H. Mutate / fault-inject
- Discoverable, safe path: `get_attributes_schema` → see patchable paths → `patch_attribute {networkId,
  patchJson:{...}}` (authority-aware; unregistered keys ignored).
- Escape hatch (any component field): `edit_component {networkId, componentType, patch:{...}}` (validated;
  invalid values rejected with 400).

### I. Author a scenario
1. `list_entity_types` → choose a `tkbType`. `get_entity_type {tkbType}` for its components.
2. `get_world_info` for the geo origin + placement extent; `geo_to_local`/`local_to_geo` to convert coords.
3. `spawn_entity {tkbType, transform}` (step to process); `patch_attribute` to set Name/Affiliation/etc.
4. `save_scenario {name}`.

---

## 4. Full command reference

Conventions: **Req** = required param. Coordinates are local ECS metres unless stated; `networkId` is a long.

### Group A — Lifecycle & status
- **`start_simulation`** — launch the runner. `runnerDll?` (string), `port?` (number, def 8099),
  `headless?` (bool). Returns `{url, pid}`.
- **`stop_simulation`** — graceful→hard shutdown. Returns the `/shutdown` envelope.
- **`get_status`** — `{scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording}`.

### Group B — Queries
- **`list_entities`** — all entities (`[{networkId, name, components:[names]}]`). Optional filters:
  `component` (string — only entities having it), `near` (string `"x,y,r"` — within radius r of (x,y) on the
  ground plane). Filters compose.
- **`get_entity`** — Req `networkId`. Full component dump. Non-finite floats render as the strings
  `"NaN"`/`"Infinity"`/`"-Infinity"` (valid JSON — see Gotchas).
- **`list_component_types`** — all registered component types + field schemas (for `edit_component`).
- **`list_scenarios`** — available scenario names (relative paths) for `load_scenario`.

### Group C — Event history
- **`get_event_history`** — recent events. `bus?` (`"world"`|`"orchestration"`, def `world`), `type?` (event
  type name filter), `since?` (frame number), `max?` (def 200). Read-only; safe any time.

### Group D — Sim / preview / time
- **`get_sim_state`** — `{isPaused, inPreview, totalTime, timeScale}`. Check this before driving.
- **`play`** — enter preview and/or resume. Time advances after this (until `pause`/breakpoint).
- **`pause`** — pause. Time freezes; commands queue until you `step`/`play`.
- **`step`** — advance discrete ticks. `count?` (def 1). Only meaningful in preview.
- **`set_time_scale`** — Req `scale` (number; 1.0 = real-time). Speeds/slows free-running play.
- **`enter_preview`** — enter preview. `startPaused?` (bool). Snapshots the world (revertible).
- **`stop_preview`** — exit preview; **rewinds** to the pre-preview snapshot.

### Group E — Scenario
- **`load_scenario`** — Req `name`. `waitForReady?` (bool — block until `OperatingEdit`; use `true`). Loads
  into Edit state.
- **`save_scenario`** — Req `name`. Saves the current authored world.

### Group F — Commands, discovery, spawn
- **`list_commands`** — publishable FDP event types + field schemas; each tagged `managed:true/false`. Call
  this to discover what `send_entity_command` accepts.
- **`send_entity_command`** — publish an FDP event. Req `eventType` (a name from `list_commands`). `payload?`
  (object — the event fields). `wait?` (bool — wait for a correlated ack; only effective while time advances,
  else `awaited:false`).
- **`spawn_entity`** — Req `tkbType` (long, from `list_entity_types`). `transform?` (`{position:{x,y,z},
  rotation:{x,y,z,w}}`), `components?` (array), `attributesJson?` (string — JsonAttributeCompiler patch).
  Spawn is processed on the next tick (`step` to realize it).

### Group M — Entity-type (TKB) catalog
- **`list_entity_types`** — `[{tkbType, name, categoryPath, disType}]`. `category?` filter.
- **`get_entity_type`** — Req `tkbType`. Mandatory components, child blueprints, descriptors. No spawn.

### Group N — World / coordinates
- **`get_world_info`** — `{geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null}`.
- **`geo_to_local`** — Req `lat, lon, alt`. `headingDeg?` → adds `rotation`. Returns `{x,y,z, rotation?}`.
- **`local_to_geo`** — Req `x, y, z`. `rotation?` (quaternion `{x,y,z,w}`) → adds `headingDeg`. Returns
  `{lat,lon,alt, headingDeg?}`. (Heading: North=0°, East=90°.)

### Group G — Breakpoints (run-until-condition)
- **`set_breakpoint`** — Req `condition` (a `SearchPredicateDto`, polymorphic via a `$type` discriminator —
  e.g. `{"$type":"Lifecycle","IdentifierType":"NameSubstring","TargetValue":"Alpha"}`, or
  `{"$type":"PropertyMatch","ComponentType":"SimTransform","PropertyPath":"Position.X","Operator":"GreaterThan",
  "Predicate":{"$type":"Numeric","MinValue":100,"MaxValue":1e9}}`). `filterNetworkId?`, `occurrenceThreshold?`
  (def 1), `name?`. Returns `breakpointId`.
- **`list_breakpoints`** — all breakpoints with `id, conditionSummary, enabled, occurrenceThreshold, hitCount,
  name`.
- **`remove_breakpoint`** — Req `id` (e.g. `"BP#1"`).
- **`get_breakpoint_status`** — `{isPaused, pausedTick, lastHit:{breakpointId, networkId}|null}`. Poll after
  `play`.

### Group H — Checkpoint / diff
- **`checkpoint`** — single-slot snapshot (enters preview, paused). Rejected if a live run is active or already
  in preview.
- **`restore_checkpoint`** — rewind to the snapshot (exits preview).
- **`capture_diff_baseline`** — `entities?` (list of networkIds, def all). Returns `baselineId`.
- **`diff_state`** — Req `baselineId`. `entities?`. Returns a `DiffNode` tree of changes (incl births/deaths).

### Group I — Recording / replay
- **`start_recording`** — `mode?` (`"preview"` def | `"live"` — live not supported in editor). Returns
  `fdpPath`. Enters preview.
- **`stop_recording`** — finalize (before the rewind); returns `fdpPath`.
- **`load_replay`** — Req `fdpPath`. Stands up an isolated replay sandbox. Returns `{totalFrames, currentFrame}`.
- **`seek_replay`** — Req `frame`. **`step_replay`** — `dir?` (`"forward"`|`"back"`).
- **`get_replay_status`** — `{replayActive, currentFrame, totalFrames}`.
- **`list_replay_entities`** — entities at the current replay frame (does not touch the live world).
- **`unload_replay`** — dispose the sandbox.

### Group J — Logs
- **`get_logs`** — `level?` (min severity inclusive), `logger?` (name substring), `since?` (ISO-8601
  timestamp), `max?` (def 200). Returns `[{timestamp, level, logger, message}]`, newest-first.

### Group K — AI behavior traces
- **`observe_trace`** — Req `networkId` (or `assetId`), `on` (bool). Arms/disarms trace-buffer allocation.
  **Must arm before extracting**, or the trace is empty.
- **`get_entity_trace`** — Req `networkId`. BTree: active node + node history; HSM: active state +
  transitions; blueprint: live state snapshot. `traceArmed` shows whether you armed it.

### Group L — Mutation / fault injection
- **`get_attributes_schema`** — `{registeredPaths, schema}` — the discoverable, authority-aware patch paths
  (Name, Affiliation, GeoPosition.*, Heading, …).
- **`patch_attribute`** — Req `networkId`, `patchJson` (a JSON **object** `{"Name":"Alpha"}` *or* a JSON
  string). Unregistered keys are ignored (no error). Authority-aware.
- **`edit_component`** — Req `networkId`, `componentType` (from `list_component_types`), `patch` (object of
  field→value). Validated; an invalid value returns 400 and changes nothing.

### Group F (manual-assist) — Focus / annotations
- **`focus_entity`** — Req `networkId`. Centers the editor camera (visible only in a windowed session).
- **`add_annotation`** — draw a debug primitive: `type` (`"sphere"`|`"anchor"`|`"line"`), plus
  `x,y,z`/`radius` or `start`/`end`. Visible only in a windowed session.

---

## 5. Gotchas (the things that actually trip agents up)

1. **Time is frozen in Edit state.** `step`/commands do nothing visible until `enter_preview` + `play`.
   Check `get_sim_state.inPreview`.
2. **Arm traces first.** `get_entity_trace` is empty unless you `observe_trace{on:true}` and then step.
3. **`awaited:false, reason:"sim not running"` is not an error** — it means time wasn't advancing; pause-step
   to observe results instead of waiting.
4. **One preview slot.** Don't `checkpoint` while in preview, or `start_recording` while checkpointed.
   Restore/stop first.
5. **Replay never affects the live world** — and the live world never affects replay. Use
   `list_replay_entities` (not `list_entities`) while replaying.
6. **`patch_attribute` keys must be registered** (see `get_attributes_schema`); unregistered keys are silently
   ignored. For arbitrary fields use `edit_component`.
7. **Non-finite floats** appear as the strings `"NaN"`/`"Infinity"`/`"-Infinity"` in dumps — that's valid
   JSON and tells you a field is non-finite (often a real sim signal), not a serialization bug.
8. **Spawns/commands while paused are queued** — they take effect on the next `step`/`play`.
9. **`live` recording is unavailable** in editor mode; use `mode:"preview"`.
10. **Always `stop_simulation`** when finished so no runner process is left behind.

---

## 6. Discover before you guess

The API is self-describing — prefer discovery over assumptions:
- `list_commands` before `send_entity_command`
- `list_component_types` before `edit_component`
- `get_attributes_schema` before `patch_attribute`
- `list_entity_types` before `spawn_entity`
- `get_status` / `get_sim_state` whenever a command "did nothing" — you are probably in the wrong run state.
