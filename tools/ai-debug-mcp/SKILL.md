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

**Usually one process, one world.** In the editor, brain (AI) and muscle (physics/spawning) share one ECS
`EntityRepository` and there is no cluster to reason about. Entities are identified by a stable `networkId`
(a long, e.g. `1000`).

**But check which host you are talking to — `get_capabilities` tells you.** The same API is also served by a
multi-subsystem cluster host (`mode: "all"` = orchestrator + simhost + ig + excon + cgf). There:

- commands act **in the context of the currently selected perspective** (`switch_perspective`), because that
  is how an operator would drive it — so a read answers for *that node*, not for "the world";
- capabilities differ per perspective. A call the active perspective cannot serve answers **HTTP 501
  `NOT_SUPPORTED_HERE`** with the capability key that is missing (e.g. `time.drive`, `world.read`,
  `scenario.load`). That is a truthful "not here", **not** a bug and not a crash — ask `get_capabilities` and
  either switch perspective or use a different endpoint;
- `networkId`s are **not portable between hosts**: the editor and a cluster allocate them from different
  authorities, so the same scenario yields different ids in each. Match entities by name across hosts.

**Four run states** — almost every "why didn't that work" is a run-state mistake:

| State | What it means | Time advances? | How you got here |
|-------|---------------|----------------|------------------|
| **Edit** | Authoring; world is static | No | `load_scenario_edit` |
| **Live** | A real run on every node | Yes | `load_scenario_live` |
| **Preview** | A revertible run from a RAM snapshot | Only when **unpaused** | `enter_preview`, `play`, `checkpoint`, or `start_recording{preview}` |
| **Replay** | Read-only playback of a `.fdp` in an isolated sandbox | N/A (you seek frames) | `load_replay` |

**Two load modes, and they are not the same operation.** `load_scenario_edit` freezes time for authoring;
`load_scenario_live` starts an exercise run. Both are cluster-wide two-phase-commit transitions — the editor
is not special, it is a one-node cluster. ⚠ In `mode: "all"` an *edit* load is currently partial (CGF has no
edit-load handler yet), so use **live** when every node must hold the world.

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
1. `load_scenario_edit {name:"test-move", waitForReady:true}` — blocks until the world is loaded (Edit state).
   Use `load_scenario_live` instead when you want a real run, or when you are on a cluster host and every node
   must hold the world.
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
- **`start_simulation`** — Launch the Hrot ClusterRunner with the AI Debug API enabled, in editor or cluster mode. Polls /status until ready. `runnerDll?` (string), `port?` (number, def 8099), `mode?` (string, def "editor"), `headless?` (boolean, def false). Returns { url, pid, mode }
  Notes: MCP-side lifecycle tool — no HTTP endpoint.; runnerDll is required unless the server was started with --runner-dll.; A cluster mode ("all") serves the same API but commands act in the currently selected perspective — call get_capabilities and switch_perspective.; headless is REFUSED for a cluster mode: a panel publishes only when it draws, and the headless runner loop never draws, so every panel dump would come back empty. Launch it windowed (under Xvfb on Linux)..
  Example: `start_simulation({"runnerDll":"/path/to/Hrot.ClusterRunner.dll","port":8099,"mode":"all"})` — launch the whole cluster in one process on the default port.
- **`get_capabilities`** — What THIS host can actually do — every endpoint, and the measured per-perspective matrix. No params. Returns { mode, host{hasMaster,currentPerspective,routablePerspectives}, endpoints[], matrix{perspective:{capability:bool}}, unclassifiedRoutes[] }
  Notes: ASK THIS FIRST when a call answers 501 NOT_SUPPORTED_HERE — the matrix says which capabilities the active perspective offers, so you can switch perspective or pick another endpoint instead of guessing.; mode tells you how the process was started: "editor" (one context, everything local) or a cluster mode such as "all" (orchestrator + simhost + ig + excon + cgf).; The matrix is MEASURED from wired dependencies, not declared — a false cell is a bug, not a stale table.; host.hasMaster:false means a step cannot be confirmed cluster-wide on this host..
  Example: `get_capabilities({})` — find out what this host supports before driving it.
- **`switch_perspective`** — Switch the active perspective, then report what actually happened. Req `name` (string). Returns { current, note }
  Notes: ALWAYS read `current` back — an unknown name is a no-op, so trusting the 200 would leave you reading the WRONG perspective's panels.; A 400 names the claimed set; a 503 means perspective access is not wired on this host.; The new perspective publishes its panels on the NEXT frame — step a tick before get_panels, or you read the previous one.; In a cluster host (mode "all") this is how you choose which node subsequent commands act on..
  Example: `switch_perspective({"name":"SimHost"})` — act in the SimHost node's context.
- **`list_perspectives`** — Every perspective a registered window claims, plus the active one. No params. Returns { current, perspectives[] }
  Notes: A perspective exists because a window CLAIMS it — this list is derived, not configured.; current is reported alongside the list because it is the only honest answer to "did my switch take?"..
  Example: `list_perspectives({})` — see which perspectives this host can route to.
- **`stop_simulation`** — Shut down the runner gracefully via POST /shutdown, then hard-kill if needed. No params. Returns The /shutdown envelope, or { note: "runner already gone" }
  Notes: MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.; Always call when done to avoid orphan runner processes..
  Example: `stop_simulation({})` — graceful runner shutdown.
- **`get_status`** — Runner liveness + sim state summary. No params. Returns { scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }
  Notes: Use this to verify the runner is alive and check current run state before driving the sim..
  Example: `get_status({})` — check runner liveness and sim state.

### Group B — Queries
- **`list_component_types`** — Enumerate registered ECS component types with field schemas. No params. Returns All registered component types + field schemas (for use with edit_component).
  Notes: Use this to discover component type names before calling edit_component..
  Example: `list_component_types({})` — list all ECS component types and their schemas.
- **`list_entities`** — List all entities with networkId, name, and component names. `component?` (string), `near?` (string). Returns [{networkId, name, components:[names]}]
  Notes: Optional filters compose: component (only entities having it), near ("x,y,r" within radius r of (x,y))..
  Example: `list_entities({"component":"SimTransform"})` — list only entities with SimTransform component.
- **`get_entity`** — Full component dump for one entity. Req `networkId` (number). Returns Full component dump for the entity. Non-finite floats render as "NaN"/"Infinity"/"-Infinity".
  Notes: Non-finite floats appear as string sentinels "NaN"/"Infinity"/"-Infinity" — valid JSON, not a bug..
  Example: `get_entity({"networkId":1000})` — get full component dump for entity 1000.
- **`list_scenarios`** — List available scenarios by relative path. No params. Returns Available scenario names (relative paths) for use with load_scenario_edit / load_scenario_live.
  Example: `list_scenarios({})` — discover loadable scenario names.

### Group E — Scenario
- **`load_scenario_edit`** — Load a scenario for AUTHORING (Edit state), cluster-wide. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.
  Notes: Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).; Edit state freezes sim time — nothing ticks until enter_preview or play.; In --mode all this load is PARTIAL: CGF has no edit-load handler yet, so SimHost loads and CGF does not. Use load_scenario_live when every node must hold the world..
  Example: `load_scenario_edit({"name":"test-move","waitForReady":true})` — load test-move for authoring and wait for ready.
- **`load_scenario_live`** — Load a scenario for RUNNING (Live state), cluster-wide, on any host. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.
  Notes: Set waitForReady:true to block until the cluster reaches OperatingLive (recommended).; Every host has live-load handlers, so this is the mode that loads on ALL nodes — use it when the world must be the same everywhere.; A live load starts a new exercise run (a fresh ExerciseId), which is what recording and replay key off..
  Example: `load_scenario_live({"name":"test-move","waitForReady":true})` — load test-move live across the cluster and wait for ready.
- **`save_scenario`** — Save the current authored world as a scenario. Req `name` (string). Returns ok:true envelope.
  Example: `save_scenario({"name":"my-scenario"})` — save current world as my-scenario.

### Group G — Breakpoints
- **`list_breakpoints`** — List all registered breakpoints. No params. Returns [{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]
  Example: `list_breakpoints({})` — list all active breakpoints and their hit counts.
- **`set_breakpoint`** — Register a run-until-condition breakpoint. Req `condition` (object), `filterNetworkId?` (number), `occurrenceThreshold?` (number, def 1), `name?` (string). Returns { breakpointId } (e.g. "BP#1").
  Notes: condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).; Poll get_breakpoint_status after play to detect when the breakpoint fires..
  Example: `set_breakpoint({"condition":{"$type":"PropertyMatch","ComponentType":"SimTransform","PropertyPath":"Position.X","Operator":"GreaterThan","Predicate":{"$type":"Numeric","MinValue":100,"MaxValue":1000000000}},"name":"moved-east"})` — pause when entity SimTransform.Position.X > 100.
- **`remove_breakpoint`** — Remove a breakpoint by its ID string. Req `id` (string). Returns ok:true envelope.
  Example: `remove_breakpoint({"id":"BP#1"})` — remove breakpoint BP#1.
- **`continue_from_breakpoint`** — Resume the debugger after a breakpoint hit. Also what applies any live variable writes staged while it was stopped. `step?` (boolean). Returns { wasPaused, action, isPaused, note }
  Notes: ⚠ Deleting a breakpoint does NOT resume: the debugger stays stopped, and while it is stopped every staged variable write is queued and never applied. Call this after a hit, not remove_breakpoint.; Harmless when nothing is stopped — it answers wasPaused:false.; The host also serves POST /breakpoints/step, which is exactly this call with step:true. Deliberately ONE tool, not two — use continue_from_breakpoint({step:true})..
  Example: `continue_from_breakpoint({})` — let the world run again after a breakpoint fired.
- **`get_breakpoint_status`** — Current pause state and last breakpoint hit. No params. Returns { isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }
  Notes: Poll this after play to detect when a breakpoint fires..
  Example: `get_breakpoint_status({})` — poll for breakpoint hit after calling play.

### Group H — Checkpoint / diff
- **`checkpoint`** — Take a single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true). No params. Returns ok:true with inPreview:true. Returns 409 if a live run is active; 400 if already in preview/checkpointed.
  Notes: Single slot: mutually exclusive with enter_preview and start_recording{preview}.; Restore with restore_checkpoint to rewind all changes..
  Example: `checkpoint({})` — take a checkpoint before an experiment.
- **`restore_checkpoint`** — Rewind the simulation to the checkpointed state via IPreviewController.ExitPreviewMode(). No params. Returns ok:true with inPreview:false. Returns 400 if no checkpoint is active.
  Notes: Returns 400 if no checkpoint is active..
  Example: `restore_checkpoint({})` — revert all changes since the last checkpoint.
- **`capture_diff_baseline`** — Serialize current entity states server-side and return a baselineId. `entities?` (array). Returns { baselineId } (e.g. "BL#1")
  Notes: Use before mutating the world, then call diff_state with the baselineId to see what changed.; Optional entities array (networkId list) scopes which entities to capture (default: all)..
  Example: `capture_diff_baseline({"entities":[1000]})` — capture baseline for entity 1000 before mutation.
- **`diff_state`** — Compare a previously captured baseline against current entity state. Req `baselineId` (string), `entities?` (array). Returns A DiffNode tree showing only what changed (token-efficient). Includes entity births/deaths.
  Notes: baselineId comes from capture_diff_baseline.; Returns only changed components — token-efficient for AI consumption..
  Example: `diff_state({"baselineId":"BL#1","entities":[1000]})` — diff entity 1000 against baseline BL#1.

### Group J — Logs
- **`get_logs`** — Query the in-process log sinks. Returns [{timestamp, level, logger, message}] sorted newest-first. `level?` (string, "Trace"|"Debug"|"Info"|"Warning"|"Error"|"Critical"), `logger?` (string), `since?` (string), `max?` (number, def 200). Returns [{timestamp, level, logger, message}] sorted newest-first.
  Notes: level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.; logger = case-insensitive substring match on logger name.; since = ISO-8601 timestamp; entries with timestamp >= since are included.; Read off-thread — no main-thread marshal required..
  Example: `get_logs({"level":"Warning","max":50})` — get last 50 Warning-or-higher log entries.

### Group C — Event history
- **`get_event_history`** — Query the diagnostic event history. `bus?` (string, "world"|"orchestration", def "world"), `type?` (string), `since?` (number), `max?` (number, def 200). Returns Recent diagnostic events from the specified bus.
  Notes: bus: "world" (default) or "orchestration".; Read-only; safe to call any time..
  Example: `get_event_history({"bus":"world","type":"CenterOnEntityCommand","max":10})` — query world bus for recent CenterOnEntityCommand events.

### Group D — Sim / preview / time
- **`enter_preview`** — Enter preview mode. Snapshots the world (revertible via stop_preview). `startPaused?` (boolean). Returns ok:true envelope.
  Notes: Snapshots the world; stop_preview rewinds to this snapshot.; Single preview slot — mutually exclusive with checkpoint and start_recording{preview}..
  Example: `enter_preview({"startPaused":true})` — enter preview paused for deterministic step-based control.
- **`stop_preview`** — Exit preview mode; rewinds to the pre-preview snapshot. No params. Returns ok:true envelope.
  Notes: Rewinds all changes made during preview back to the snapshot taken at enter_preview..
  Example: `stop_preview({})` — exit preview and revert all changes since entering preview.
- **`pause`** — Pause the simulation. Time freezes; commands queue until step/play. No params. Returns ok:true envelope.
  Notes: Commands and spawns while paused are queued and take effect on the next step/play..
  Example: `pause({})` — pause the running simulation.
- **`play`** — Enter preview and/or resume if paused. Time advances after this. No params. Returns ok:true envelope.
  Notes: Time advances after play (until pause or a breakpoint fires)..
  Example: `play({})` — start or resume simulation.
- **`get_sim_state`** — Current sim state: isPaused, inPreview, totalTime, timeScale. No params. Returns { isPaused, inPreview, totalTime, timeScale }
  Notes: Check this before driving — most mistakes are run-state mistakes..
  Example: `get_sim_state({})` — check current paused/preview/time state.
- **`step`** — Advance simulation by N discrete steps. Only meaningful in preview. `count?` (number, def 1). Returns ok:true envelope.
  Notes: Only advances time when inPreview==true. In Edit state this is a no-op..
  Example: `step({"count":5})` — advance 5 simulation ticks.
- **`set_time_scale`** — Set simulation time scale. Req `scale` (number). Returns ok:true envelope.
  Notes: 1.0 = real-time, >1.0 = faster, <1.0 = slower..
  Example: `set_time_scale({"scale":2})` — run simulation at 2x real-time.

### Group F — Commands, discovery, spawn
- **`list_commands`** — Enumerate publishable FDP event types with field schemas. No params. Returns Publishable FDP event types + field schemas; each tagged managed:true/false.
  Notes: Call this to discover what send_entity_command accepts.; managed:true events have server-side handling; managed:false are raw FDP events..
  Example: `list_commands({})` — discover available FDP event types before sending a command.
- **`send_entity_command`** — Publish an FDP event by type name. Req `eventType` (string), `payload?` (object), `wait?` (boolean). Returns ok:true envelope. awaited:false if sim not running (not an error).
  Notes: Set wait:true to attempt correlated-ack wait — only effective while time advances, else awaited:false.; awaited:false is NOT an error — it means time was not advancing..
  Example: `send_entity_command({"eventType":"MissionControlIntent","payload":{"targetId":1000},"wait":false})` — publish MissionControlIntent event.
- **`spawn_entity`** — Spawn an entity from a TKB type. Req `tkbType` (number), `transform?` (object), `components?` (array), `attributesJson?` (string). Returns ok:true envelope. Spawn is processed on the next tick (step to realize it).
  Notes: Spawn is queued and processed on the next tick — call step to realize it.; Use list_entity_types to discover valid tkbType values..
  Example: `spawn_entity({"tkbType":1001,"transform":{"position":{"x":100,"y":0,"z":50},"rotation":{"x":0,"y":0,"z":0,"w":1}}})` — spawn entity type 1001 at position (100,0,50).

### Group I — Recording / replay
- **`start_recording`** — Start recording. Enters preview and begins writing a .fdp file. `mode?` (string, "preview"|"live", def "preview"). Returns { recording:true, mode, fdpPath }
  Notes: mode="preview" (default): revertible, uses EnterPreviewMode→PrepareRecordingAsync.; mode="live": not supported in editor mode.; Mutually exclusive with checkpoint (both use the preview slot)..
  Example: `start_recording({"mode":"preview"})` — start a revertible preview recording.
- **`stop_recording`** — Stop the active recording. Finalizes BEFORE the exit rewind. No params. Returns { recording:false, fdpPath }
  Notes: For preview mode: finalizes BEFORE the exit rewind (hard ordering rule)..
  Example: `stop_recording({})` — stop recording and get the .fdp file path.
- **`list_replay_entities`** — List entities from the ISOLATED replay sandbox at the current frame. No params. Returns Same schema as list_entities but from the sandbox repo, NOT the live world.
  Notes: Requires an active replay (call load_replay first).; Does not touch or affect the live world..
  Example: `list_replay_entities({})` — inspect entities at current replay frame.
- **`load_replay`** — Load a .fdp recording into an ISOLATED ReplayBrowserContext. Req `fdpPath` (string). Returns { loaded:true, fdpPath, totalFrames, currentFrame }
  Notes: While replay is active, /replay/entities returns entities from the sandbox (not the live world).; Use list_replay_entities (not list_entities) while replaying..
  Example: `load_replay({"fdpPath":"/path/to/recording.fdp"})` — load a .fdp recording for inspection.
- **`seek_replay`** — Seek to a specific frame in the ISOLATED sandbox. Does NOT touch the live world. Req `frame` (number). Returns { frame, totalFrames }
  Notes: Isolation guarantee: does NOT touch the live world..
  Example: `seek_replay({"frame":0})` — seek replay to frame 0 (start).
- **`get_replay_status`** — Replay sandbox status. No params. Returns { replayActive, currentFrame, totalFrames }
  Example: `get_replay_status({})` — check if replay is active and current frame.
- **`step_replay`** — Step one frame forward or backward in the ISOLATED sandbox. Does NOT touch the live world. `dir?` (string, "forward"|"back", def "forward"). Returns { stepped:bool, frame, totalFrames }
  Notes: Isolation guarantee: does NOT touch the live world..
  Example: `step_replay({"dir":"forward"})` — step one frame forward in the replay.
- **`unload_replay`** — Dispose the replay sandbox and return to live world queries. No params. Returns ok:true envelope.
  Example: `unload_replay({})` — unload replay sandbox when done inspecting.

### Group K — AI behavior traces
- **`get_entity_trace`** — Extract AI behavior trace for an entity. Req `networkId` (number). Returns BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.
  Notes: Arm the entity with observe_trace first to populate trace data.; Returns tier field indicating the AI tier type (BTree/HSM/blueprint)..
  Example: `get_entity_trace({"networkId":1000})` — read AI behavior trace for entity 1000 after arming.
- **`observe_trace`** — Arm or disarm AI behavior trace buffer allocation for an entity. Req `networkId` (number), Req `on` (boolean). Returns { armed, networkId }
  Notes: Must arm before get_entity_trace will return populated trace data.; Without arming, get_entity_trace returns empty trace..
  Example: `observe_trace({"networkId":1000,"on":true})` — arm AI behavior tracing for entity 1000.

### Group L — Mutation / fault injection
- **`get_attributes_schema`** — Return all patchable attribute paths and their JSON Schema. No params. Returns { registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, GeoPosition.*, Heading, …).
  Notes: Use patch_attribute to apply a patch using these paths.; Paths not in registeredPaths are silently ignored by patch_attribute..
  Example: `get_attributes_schema({})` — discover patchable attribute paths before calling patch_attribute.
- **`patch_attribute`** — Apply a JSON attribute patch to an entity. Req `networkId` (number), Req `patchJson`. Returns Updated entity dump on success.
  Notes: Authority-aware; unregistered keys are silently ignored (no error).; patchJson may be a nested JSON object like {"Name":"Alpha"} or a JSON string..
  Example: `patch_attribute({"networkId":1000,"patchJson":{"Name":"Alpha"}})` — rename entity 1000 to Alpha.
- **`edit_component`** — StructEdit escape hatch for arbitrary component fields. Req `networkId` (number), Req `componentType` (string), Req `patch` (object). Returns Updated entity component state. Invalid values → 400, component unchanged.
  Notes: Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, and writes the result back to ECS.; Invalid values → 400, component unchanged.; For fields registered in the attribute schema, prefer patch_attribute..
  Example: `edit_component({"networkId":1000,"componentType":"SimTransform","patch":{"Position":{"X":999,"Y":0,"Z":0}}})` — set SimTransform Position.X to 999 for entity 1000.

### Group M (TKB) — Entity-type catalog
- **`list_entity_types`** — List entity types (TKB templates) with id, name, category, disType. `category?` (string). Returns [{tkbType, name, categoryPath, disType}]
  Example: `list_entity_types({"category":"Vehicle"})` — list all TKB types in the Vehicle category.
- **`get_entity_type`** — Full TKB descriptor: mandatory components, child blueprints, DIS type, and descriptor DTOs. Req `tkbType` (number). Returns Full TKB descriptor including mandatory components, child blueprints, descriptors. No spawn.
  Example: `get_entity_type({"tkbType":1001})` — inspect TKB descriptor for type 1001.

### Group N — World / coordinates
- **`geo_to_local`** — Convert geographic coordinates to local ENU {x,y,z}. Req `lat` (number), Req `lon` (number), Req `alt` (number), `headingDeg?` (number). Returns { x, y, z, rotation? } — optional rotation if headingDeg was provided.
  Notes: Optional headingDeg → adds rotation quaternion to response..
  Example: `geo_to_local({"lat":50.0755,"lon":14.4378,"alt":200})` — convert Prague geo coords to local ECS metres.
- **`get_world_info`** — World metadata: geo origin, spatial grid extent. terrain and navmesh are null in editor mode. No params. Returns { geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null }
  Notes: terrain and navmesh are null in editor mode..
  Example: `get_world_info({})` — get world geo origin and spatial grid extent.
- **`local_to_geo`** — Convert local ENU {x,y,z} to geographic coordinates. Req `x` (number), Req `y` (number), Req `z` (number), `rotation?` (object). Returns { lat, lon, alt, headingDeg? } — Heading: North=0°, East=90°.
  Notes: Optional rotation quaternion {x,y,z,w} → adds headingDeg to response.; Heading convention: North=0°, East=90°..
  Example: `local_to_geo({"x":100,"y":0,"z":50})` — convert local ECS position (100,0,50) to geographic coords.

### Group O — Manual-assist (focus / annotations)
- **`add_annotation`** — Draw a debug primitive (sphere, anchor, or line) in the gizmo buffer. MANUAL-VERIFY: gizmo render requires windowed session. Req `type` (string), `networkId?` (number), `x?` (number), `y?` (number), `z?` (number), `radius?` (number), `heading?` (number), `color?` (string), `from?` (object), `to?` (object). Returns { added: true, primitiveIndex, bufferCount } on success.
  Notes: "sphere" — x, y, z, radius (float), optional color (hex "#RRGGBB").; "anchor" — networkId, x, y, z, optional heading (float).; "line" — from:{x,y,z}, to:{x,y,z}, optional color.; The buffer write is headless-verifiable; the actual gizmo render requires a windowed session (MANUAL-VERIFY)..
  Example: `add_annotation({"type":"sphere","x":100,"y":0,"z":50,"radius":10,"color":"#FF4400"})` — draw a red sphere at (100,0,50) with radius 10.
- **`focus_entity`** — Pan and zoom the map canvas to an entity. MANUAL-VERIFY: camera move requires windowed session. Req `networkId` (number). Returns { focused: true } on success.
  Notes: Publishes CenterOnEntityCommand (headless-verifiable via event history).; The actual camera move only occurs in a windowed session (MANUAL-VERIFY)..
  Example: `focus_entity({"networkId":1000})` — center editor camera on entity 1000.

### Group O — Variables (the watch, over HTTP)
- **`get_entity_variable`** — Read one blueprint variable by name, with its live value and its pending (staged-but-not-yet-applied) value if a write is queued. Req `networkId` (number), Req `path` (string), `asset?` (string). Returns { networkId, asset, assetId, path, type, value, writable, pending, pendingValue? }
  Notes: An unknown variable name is a 400 pointing back at list_entity_variables — never an empty success..
  Example: `get_entity_variable({"networkId":1000,"path":"Health"})` — read entity 1000's Health variable and whether an edit is still queued.
- **`stage_entity_variable`** — STAGE a write to one blueprint variable, through the same seam the editor's Details panel uses. The value lands on the next advancing tick — not on this response. Req `networkId` (number), Req `path` (string), Req `value` (any), `asset?` (string). Returns { networkId, asset, assetId, path, staged: true, pending: true, note }
  Notes: Running is not a reason to refuse — it is a reason to stage. There is no "pause first" step.; Until the world advances, get_entity_variable still reports the OLD value with pending: true. Step or play to make it land.; A value whose width does not match the field is refused rather than written: the blackboard is shared between subsystems, so an overrun would corrupt a neighbour..
  Example: `stage_entity_variable({"networkId":1000,"path":"Health","value":42})` — queue Health = 42; it applies on the next advancing tick.
- **`list_entity_variables`** — List an entity's blueprint variables — the same (entity, asset, path) addressing a Details/watch row uses, with each variable's live value and whether a staged write is still pending on it. Req `networkId` (number), `asset?` (string). Returns { networkId, asset, assetId, dispatch, variables: [{ path, type, value, writable, pending, pendingValue? }] }
  Notes: pending: true means a staged write for that variable has not been applied yet, so value is still the OLD number — the machine half of the editor's yellow.; writable: false means the variable has no live address (its blueprint's dispatch kind has no staged-write layout), so it can be read but not staged.; A Library-dispatch blueprint legitimately has no working-state variables and returns an empty list, not an error..
  Example: `list_entity_variables({"networkId":1000})` — read every blueprint variable on entity 1000.

### Group P — Discovery with schema
- **`list_behaviors`** — List the behaviours available, each with the JSON schema of its parameter DTO. Key by tkbType (what this KIND of entity can do) or entityId (what THIS entity can do); omit both for every registered behaviour. `tkbType?` (number), `entityId?` (number). Returns [{ id, name, brainTier, paramSchema }]
  Notes: paramSchema is derived from the behaviour definition the runtime itself parses params with, so what you author matches what the engine reads.; An unknown entityId is a 404 whose hint points at GET /entities — it is not answered with an empty list.; A behaviour with no parameters returns an empty properties object, never null..
  Example: `list_behaviors({"entityId":1000})` — discover what entity 1000 can be told to do, and how to shape the params.

### Group Q — Blueprint hot-attach
- **`list_blueprints`** — Every blueprint this editor compiled, with whether it can be attached to an entity. No params. Returns { count, blueprints:[{ blueprintId, name, assetId, kind, stateSize, attachable }] }
  Notes: Only Instance-dispatch blueprints occupy a slot on an entity; attachable says so up front rather than through a refusal..
  Example: `list_blueprints({})` — find a blueprint to try on a running entity.
- **`attach_blueprint`** — Attach an Instance blueprint to a running entity — the quick way to try a behaviour without authoring a mission. Req `networkId` (number), Req `blueprint` (string), `paramsJson?` (object). Returns { networkId, blueprint, blueprintId, attached:true, note }
  Notes: Queued: the ingress system applies it on the NEXT tick, so step or play once before reading it back.; After it lands, the entity's variables appear in list_entity_variables — name the asset, since the entity may now carry more than one..
  Example: `attach_blueprint({"networkId":1001,"blueprint":"ComponentCollectionDemo"})` — try a blueprint on entity 1001 right now.
- **`detach_blueprint`** — Detach an Instance blueprint from an entity. Req `networkId` (number), Req `blueprint` (string). Returns { networkId, blueprint, blueprintId, detached:true, note }
  Notes: Queued like the attach — applied on the next tick..
  Example: `detach_blueprint({"networkId":1001,"blueprint":"ComponentCollectionDemo"})` — put the entity back how you found it.

### Group R — Entity state
- **`get_entity_state`** — The well-known fields parsed out — position, rotation, velocity, speed, current behaviour — so an assertion reads state.position.x instead of digging through component JSON. Req `networkId` (number). Returns { networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier} }
  Notes: A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.; A convenience over get_entity, reading the same components: the two cannot disagree..
  Example: `get_entity_state({"networkId":1000})` — where is entity 1000, how fast, doing what.

### Group S — Discovery with schema
- **`list_breakpoint_types`** — List every condition type a breakpoint can use, each with the JSON schema of its parameters. Call this BEFORE set_breakpoint instead of guessing a $type. No params. Returns [{ $type, clrType, paramSchema }]  — paramSchema is { type:"object", properties:{...} }
  Notes: The condition union is CLOSED: these are exactly the $type values set_breakpoint accepts.; A nested predicate appears as { $ref: "SearchPredicateDto" } — fill it with another arm from this same list.; Enum-valued params carry their allowed values in "enum"; a param marked picker:"propertyPath" wants a dotted field path such as "Position.X"..
  Example: `list_breakpoint_types({})` — discover the valid condition $type values and their parameter shapes.

### Group T — Panels (the UI as data)
- **`list_panels`** — What the editor's UI is showing, without pixels: which panels are instrumented at all, and which published a view-model this frame. No params. Returns { captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }
  Notes: registered vs captured is the load-bearing distinction: a panel nobody instrumented and a panel whose window is closed are different facts, and only the second is fixed by opening a window.; kinds groups the live panels by their logical name — the key a cross-host comparison uses, since panel ids are unique per instance by design.; captured entries are latest-wins and are NOT cleared per frame: a panel that stopped drawing still reports its last model..
  Example: `list_panels({})` — see which panels are live and what kinds they are.
- **`get_gizmo_frame`** — What the map is drawing this frame, as data: the debug primitives, projected per shape. `max?` (number). Returns { count, dropped, emitted, truncated, primitives:[{shape, space, layer, color, ...shape-specific}] }
  Notes: truncated tells you the frame was clipped by max — without it a cap would read as the end of the frame.; A shape with no field projection yet is reported by name with a note, never as aliased bytes..
  Example: `get_gizmo_frame({"max":50})` — inspect what the map is drawing without taking a screenshot.
- **`get_panel`** — One panel's dumped view-model — the same object its draw renders from, so a field here is a field the designer sees. Req `panelId` (string). Returns { panelId, panelKind, model }
  Notes: The model is structured JSON, never a formatted blob — assert a field, do not parse prose.; A miss says WHICH kind of miss it is: not instrumented, or instrumented but not drawing..
  Example: `get_panel({"panelId":"editor_bp_manager"})` — read the breakpoint panel's model and assert what it lists.

### Group V — AI assets & graph tabs
- **`list_assets`** — Every AI asset (BTree/HSM/Blueprint) this host has indexed, with both of its addresses. No params. Returns { count, assets[{assetId,name,kind,sourceFilePath,isDirty}], note? }
  Notes: CALL THIS FIRST before opening anything — it is how you turn a human path into the assetId the open-by-id route wants.; sourceFilePath is the RELATIVE path including subfolders, normalised to forward slashes; paste it verbatim into open_asset_by_path.; name is NOT an address: two subfolders may hold the same file name. Address by assetId (stable) or sourceFilePath (human).; count:0 with a note means the catalog indexed nothing — on a deployed node the source asset tree is absent (asset roots must come from config)..
  Example: `list_assets({})` — discover which AI assets this host can open.
- **`open_asset`** — Open an AI asset by its stable GUID; the graph canvas and outline then render it. Req `assetId` (string). Returns { assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }
  Notes: The panels publish the opened asset on the NEXT frame — step a tick before get_panels, or you read the previous content.; Opening an already-open asset re-activates its tab rather than duplicating it.; Opening also switches the perspective to the asset kind (the document manager drives it), so the canvas is actually drawing..
  Example: `open_asset({"assetId":"00000000-0000-0000-0000-000000000000"})` — open a specific asset by id and make it the active graph.
- **`reload_ai_asset`** — Recompile an edited AI asset and commit it into the running behaviour registry. Req `assetId` (string). Returns { assetId, name, kind, status, note }
  Notes: Compiles from the IN-MEMORY asset, not from the file — so it reflects unsaved edits, and save is a separate intent.; The asset is ACTIVATED first: the reload pipeline acts on the active document, so reloading a background tab without activating it would recompile the wrong graph.; A SOFT reload patches lookup tables and live instances KEEP their state; a HARD (topology) reload bumps the generation and instances RESET — that reset is intended, not a bug.; A Hard reload on a live cluster is a confirmed cluster-wide reset, and the confirmation belongs to the interactive node — this call never prompts.; `status` carries the compiler's own message, including the failure text when it did not compile. A failed compile is a 200 with a failure status, not an HTTP error: it is a legitimate outcome of editing..
  Example: `reload_ai_asset({"assetId":"00000000-0000-0000-0000-000000000000"})` — hot-apply an edited graph to the running brain.
- **`save_ai_asset`** — Persist edited AI assets to their source files. Req `assetId` (string). Returns { assetId, name, sourceFilePath, status, stillDirty, note }
  Notes: IT SAVES EVERY DIRTY OPEN DOCUMENT, not only this one — it runs the shared Save-All command, which is what the editor's own Save All button runs.; A document with no source path is SKIPPED with a warning in `status` rather than throwing; check `stillDirty` to see whether this one was written.; Saving is NOT a precondition for reload: reload compiles from the in-memory asset, so an unsaved edit still hot-applies..
  Example: `save_ai_asset({"assetId":"00000000-0000-0000-0000-000000000000"})` — write an edited graph back to disk.
- **`open_asset_by_path`** — Open an AI asset by its relative source file path — the human address. Req `path` (string). Returns { assetId, name, kind, sourceFilePath, opened, activeAssetId, openDocumentCount, note }
  Notes: The path travels in the BODY on purpose — a relative path has slashes and dots, which a URL segment would need encoding for.; Matching is a path SUFFIX at a folder boundary: 'sub/x.bp.json' matches, 'x' does not, and 'my_x.bp.json' never matches a query for 'x.bp.json'.; An AMBIGUOUS path is a 400 that lists the candidates — it is never resolved by picking the first, which would silently open the wrong asset..
  Example: `open_asset_by_path({"path":"Assets/Blueprints/hill_attack.bp.json"})` — open an asset by the path a human would read off disk.
- **`list_documents`** — The open graph tabs and which one is active. No params. Returns { activeAssetId, count, documents[{assetId,name,kind,sourceFilePath,isDirty,isActive}] }
  Notes: Only the ACTIVE document's canvas draws, so this is how you confirm which graph get_panels is about to show you.; This is the editor's own tab model, exposed — not a second list..
  Example: `list_documents({})` — see which graphs are open and which one is on screen.
- **`activate_document`** — Switch the active graph tab to an already-open document. Req `assetId` (string). Returns { activeAssetId, note }
  Notes: Activate only switches between tabs that are ALREADY open; a closed asset is a 404, not an implicit open. Use open_asset for that.; Details and the toolbar re-publish for the newly active kind on the NEXT frame..
  Example: `activate_document({"assetId":"00000000-0000-0000-0000-000000000000"})` — bring an already-open graph to the front.
- **`focus_panel`** — Open and focus a window by its panel id. Req `panelId` (string). Returns { panelId, perspective, isOpen, isPinned, note }
  Notes: An unknown id is a 404 here, deliberately — the underlying UI call is a silent no-op, which over HTTP would hand you a 200 and then the wrong panel.; A perspective-bound window belonging to another perspective is PINNED rather than switched to; the response says which happened.; Focus takes effect on the NEXT frame..
  Example: `focus_panel({"panelId":"ai_watch_blueprint"})` — bring a specific panel on screen before reading it.

### Group W — AI-asset authoring
- **`create_asset`** — Create a new AI asset (BTree / HSM / Blueprint) through the host's own New-Asset path, then open it as a document. Req `kind` (string), Req `name` (string), `path?` (string). Returns { assetId, name, kind, status, sourceFilePath, note }
  Notes: It runs the same per-kind INewAssetService the New-Asset dialog runs, writes the file and refreshes the catalog — so the result appears in list_assets by the same rebuild a dialog-created asset does.; The new asset is opened as a document, so you can author it immediately with read_asset_graph and the graph tools.; A host that composes no create path answers 503 explaining that EDITING an existing asset does not need it..
  Example: `create_asset({"kind":"BTree","name":"PatrolTree"})` — create a new behaviour tree asset.
- **`read_asset_graph`** — Read an open AI asset's graph as JSON: nodes, pins, links and comments, keyed by the in-memory guids the edit tools take. Req `assetId` (string). Returns { assetId, name, kind, graphId, displayName, graphKind, nodeCount, linkCount, nodes[{nodeId,kind,title,position,pins[{pinId,label,direction,kind,type,default}]}], links[{linkId,fromPin,toPin,fromNode,toNode}], comments[], note }
  Notes: THIS IS THE FIRST CALL of any authoring session: you never predict an id, you read the ones the edit tools accept.; The ids are the IN-MEMORY guids. The saved .json binds links by deterministic name-derived pin ids instead — an id copied out of the file addresses nothing here.; Re-read after each edit rather than caching: adding a node can reproject another node's pins.; Only the graph-document kinds (BTree, HSM, Blueprint) have a graph; a Scenario or Blackboard asset is a 404 explaining that..
  Example: `read_asset_graph({"assetId":"00000000-0000-0000-0000-000000000000"})` — read the whole graph before editing it.
- **`list_node_kinds`** — The node kinds this graph can add, with their pin signatures. Call this instead of guessing a kind id for add_graph_node. Req `assetId` (string), `filter?` (string). Returns { count, total, kinds[{kind,displayName,category,description,isDeprecated,inputs[],outputs[]}], note }
  Notes: The catalog is PER GRAPH — a BTree graph and a Blueprint graph offer different kinds, so read the one you are editing.; `kind` is what add_graph_node takes verbatim. An unknown kind is refused with this endpoint named, not silently ignored.; `inputs`/`outputs` are the declared pin SIGNATURES; the actual pin guids only exist once the node is added..
  Example: `list_node_kinds({"assetId":"00000000-0000-0000-0000-000000000000"})` — discover what node kinds this graph accepts.
- **`add_graph_link`** — Connect two pins in an open graph. The host's own link validator runs first, so an illegal wire is refused for the same reason a dragged one would be. Req `assetId` (string), Req `fromPin` (string), Req `toPin` (string). Returns { linkId, fromPin, toPin, requiresCast, note }
  Notes: The validator is the SAME one the canvas consults while dragging a wire, so MCP can never author a graph the editor would reject.; A refusal is a 400 carrying the host's own reason text — it is a legitimate answer, not a server error.; When the validator classes the pair ValidWithCast the canvas would auto-insert a cast node; this route connects them directly and says so in `note`..
  Example: `add_graph_link({"assetId":"00000000-0000-0000-0000-000000000000","fromPin":"11111111-1111-1111-1111-111111111111","toPin":"22222222-2222-2222-2222-222222222222"})` — wire two pins together.
- **`add_graph_node`** — Add a node to an open graph through the same command sink human editing uses. Returns the new node's guid and its pins. Req `assetId` (string), Req `kind` (string), `x?` (number, def 0), `y?` (number, def 0). Returns { nodeId, kind, title, pins[{pinId,label,direction,kind,type}], note }
  Notes: The edit goes through the editor's undo stack, so it is undoable exactly like a node dropped on the canvas.; The response carries the new node's PINS because linking needs them — you do not have to re-read the whole graph to wire it up.; An unknown kind is a 400 naming list_node_kinds: the host sink can report success and build nothing, so this route re-reads the model and refuses rather than returning a guid that addresses nothing..
  Example: `add_graph_node({"assetId":"00000000-0000-0000-0000-000000000000","kind":"bt.selector","x":120,"y":40})` — add a node and get back its guid.
- **`set_graph_param`** — Set the literal default value on an input data pin of an open graph. Req `assetId` (string), Req `pinId` (string), Req `value` (string). Returns { pinId, label, previousValue, value, note }
  Notes: This is a PIN default, not a free-form node property: the pin default is the one edit whose inverse can be built from the model, so it is the one that stays undoable.; An exec pin or an output pin is refused — an exec pin has no value and an output's value is computed.; `value` in the response is RE-READ from the model after the edit, so it shows what the host actually stored rather than what you sent..
  Example: `set_graph_param({"assetId":"00000000-0000-0000-0000-000000000000","pinId":"11111111-1111-1111-1111-111111111111","value":3.5})` — set a literal on an input pin.
- **`remove_graph_elements`** — Remove nodes and/or links from an open graph by invoking the editor's own Delete command. Req `assetId` (string), `nodes?` (array), `links?` (array). Returns { removedNodes, removedLinks, nodeCount, linkCount, note }
  Notes: It invokes the editor's shared Delete command rather than building its own removal, so incident links, reroute waypoints and attachments are handled and the undo restores nodes before the links that reference them.; `removedLinks` counts the links deleted IMPLICITLY with their nodes, so it is usually larger than the list you named.; An id that is not in the graph refuses the WHOLE call — a partial delete would be worse than a refusal.; The canvas selection is left cleared afterwards, exactly as after a human delete..
  Example: `remove_graph_elements({"assetId":"00000000-0000-0000-0000-000000000000","nodes":["11111111-1111-1111-1111-111111111111"]})` — delete a node and its wires.
- **`delete_entity`** — Remove an entity from the world through the ELM lifecycle. Scenario authoring is world manipulation, and this is its delete. Req `networkId` (number). Returns { networkId, queued:true, note }
  Notes: There is no such thing as editing a scenario FILE: the file is a reduced snapshot of the world at save time, so authoring a scenario means spawning, configuring and deleting entities, then calling save_scenario.; Queued like spawn_entity — teardown runs on a later tick. Call step, then list_entities, before asserting the entity is gone.; An unknown networkId is a 404 rather than a queued no-op..
  Example: `delete_entity({"networkId":1000})` — delete an entity from the world.

---

## 5. Gotchas (the things that actually trip agents up)

1. **Time is frozen in Edit state.** `step`/commands do nothing visible until `enter_preview` + `play`.
   Check `get_sim_state.inPreview`.
1b. **A 501 `NOT_SUPPORTED_HERE` is an answer, not a fault.** It names the missing capability
   (`time.drive`, `world.read`, `scenario.load`, …) and means *the active perspective cannot serve this*.
   Call `get_capabilities`, then `switch_perspective` to one whose matrix row has that capability — do not
   retry the same call.
1c. **Pick the load mode deliberately.** `load_scenario_edit` = authoring, time frozen.
   `load_scenario_live` = a real run on every node. On a cluster host an *edit* load is partial today
   (CGF has no edit-load handler), so prefer **live** when the whole cluster must hold the world.
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
