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
- **`start_simulation`** — Launch the Hrot ClusterRunner in editor mode with the AI Debug API enabled. Polls /status until ready. `runnerDll?` (string), `port?` (number, def 8099), `headless?` (boolean, def false). Returns { url, pid }
  Notes: MCP-side lifecycle tool — no HTTP endpoint.; runnerDll is required unless the server was started with --runner-dll..
  Example: `start_simulation({"runnerDll":"/path/to/Hrot.ClusterRunner.dll","port":8099,"headless":true})` — launch runner headless on default port.
- **`stop_simulation`** — Shut down the runner gracefully via POST /shutdown, then hard-kill if needed. No params. Returns The /shutdown envelope, or { note: "runner already gone" }
  Notes: MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.; Always call when done to avoid orphan runner processes..
  Example: `stop_simulation({})` — graceful runner shutdown.
- **`get_status`** — Runner liveness + sim state summary. No params. Returns { scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }
  Notes: Use this to verify the runner is alive and check current run state before driving the sim..
  Example: `get_status({})` — check runner liveness and sim state.

### Group B — Queries
- **`list_entities`** — List all entities with networkId, name, and component names. `component?` (string), `near?` (string). Returns [{networkId, name, components:[names]}]
  Notes: Optional filters compose: component (only entities having it), near ("x,y,r" within radius r of (x,y))..
  Example: `list_entities({"component":"SimTransform"})` — list only entities with SimTransform component.
- **`get_entity`** — Full component dump for one entity. Req `networkId` (number). Returns Full component dump for the entity. Non-finite floats render as "NaN"/"Infinity"/"-Infinity".
  Notes: Non-finite floats appear as string sentinels "NaN"/"Infinity"/"-Infinity" — valid JSON, not a bug..
  Example: `get_entity({"networkId":1000})` — get full component dump for entity 1000.
- **`list_component_types`** — Enumerate registered ECS component types with field schemas. No params. Returns All registered component types + field schemas (for use with edit_component).
  Notes: Use this to discover component type names before calling edit_component..
  Example: `list_component_types({})` — list all ECS component types and their schemas.
- **`list_scenarios`** — List available scenarios by relative path. No params. Returns Available scenario names (relative paths) for use with load_scenario.
  Example: `list_scenarios({})` — discover loadable scenario names.

### Group C — Event history
- **`get_event_history`** — Query the diagnostic event history. `bus?` (string, "world"|"orchestration", def "world"), `type?` (string), `since?` (number), `max?` (number, def 200). Returns Recent diagnostic events from the specified bus.
  Notes: bus: "world" (default) or "orchestration".; Read-only; safe to call any time..
  Example: `get_event_history({"bus":"world","type":"CenterOnEntityCommand","max":10})` — query world bus for recent CenterOnEntityCommand events.

### Group D — Sim / preview / time
- **`get_sim_state`** — Current sim state: isPaused, inPreview, totalTime, timeScale. No params. Returns { isPaused, inPreview, totalTime, timeScale }
  Notes: Check this before driving — most mistakes are run-state mistakes..
  Example: `get_sim_state({})` — check current paused/preview/time state.
- **`play`** — Enter preview and/or resume if paused. Time advances after this. No params. Returns ok:true envelope.
  Notes: Time advances after play (until pause or a breakpoint fires)..
  Example: `play({})` — start or resume simulation.
- **`pause`** — Pause the simulation. Time freezes; commands queue until step/play. No params. Returns ok:true envelope.
  Notes: Commands and spawns while paused are queued and take effect on the next step/play..
  Example: `pause({})` — pause the running simulation.
- **`step`** — Advance simulation by N discrete steps. Only meaningful in preview. `count?` (number, def 1). Returns ok:true envelope.
  Notes: Only advances time when inPreview==true. In Edit state this is a no-op..
  Example: `step({"count":5})` — advance 5 simulation ticks.
- **`set_time_scale`** — Set simulation time scale. Req `scale` (number). Returns ok:true envelope.
  Notes: 1.0 = real-time, >1.0 = faster, <1.0 = slower..
  Example: `set_time_scale({"scale":2})` — run simulation at 2x real-time.
- **`enter_preview`** — Enter preview mode. Snapshots the world (revertible via stop_preview). `startPaused?` (boolean). Returns ok:true envelope.
  Notes: Snapshots the world; stop_preview rewinds to this snapshot.; Single preview slot — mutually exclusive with checkpoint and start_recording{preview}..
  Example: `enter_preview({"startPaused":true})` — enter preview paused for deterministic step-based control.
- **`stop_preview`** — Exit preview mode; rewinds to the pre-preview snapshot. No params. Returns ok:true envelope.
  Notes: Rewinds all changes made during preview back to the snapshot taken at enter_preview..
  Example: `stop_preview({})` — exit preview and revert all changes since entering preview.

### Group E — Scenario
- **`load_scenario`** — Load a scenario by name. Puts the world into Edit state. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope.
  Notes: Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).; Loads into Edit state — sim is static until enter_preview or play..
  Example: `load_scenario({"name":"test-move","waitForReady":true})` — load test-move scenario and wait for ready.
- **`save_scenario`** — Save the current authored world as a scenario. Req `name` (string). Returns ok:true envelope.
  Example: `save_scenario({"name":"my-scenario"})` — save current world as my-scenario.

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

### Group G — Breakpoints
- **`set_breakpoint`** — Register a run-until-condition breakpoint. Req `condition` (object), `filterNetworkId?` (number), `occurrenceThreshold?` (number, def 1), `name?` (string). Returns { breakpointId } (e.g. "BP#1").
  Notes: condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).; Poll get_breakpoint_status after play to detect when the breakpoint fires..
  Example: `set_breakpoint({"condition":{"$type":"PropertyMatch","ComponentType":"SimTransform","PropertyPath":"Position.X","Operator":"GreaterThan","Predicate":{"$type":"Numeric","MinValue":100,"MaxValue":1000000000}},"name":"moved-east"})` — pause when entity SimTransform.Position.X > 100.
- **`continue_from_breakpoint`** — Resume the debugger after a breakpoint hit. Also what applies any live variable writes staged while it was stopped. `step?` (boolean). Returns { wasPaused, action, isPaused, note }
  Notes: ⚠ Deleting a breakpoint does NOT resume: the debugger stays stopped, and while it is stopped every staged variable write is queued and never applied. Call this after a hit, not remove_breakpoint.; Harmless when nothing is stopped — it answers wasPaused:false..
  Example: `continue_from_breakpoint({})` — let the world run again after a breakpoint fired.
- **`list_breakpoints`** — List all registered breakpoints. No params. Returns [{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]
  Example: `list_breakpoints({})` — list all active breakpoints and their hit counts.
- **`remove_breakpoint`** — Remove a breakpoint by its ID string. Req `id` (string). Returns ok:true envelope.
  Example: `remove_breakpoint({"id":"BP#1"})` — remove breakpoint BP#1.
- **`get_breakpoint_status`** — Current pause state and last breakpoint hit. No params. Returns { isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }
  Notes: Poll this after play to detect when a breakpoint fires..
  Example: `get_breakpoint_status({})` — poll for breakpoint hit after calling play.

### Group S — Discovery with schema
- **`list_breakpoint_types`** — List every condition type a breakpoint can use, each with the JSON schema of its parameters. Call this BEFORE set_breakpoint instead of guessing a $type. No params. Returns [{ $type, clrType, paramSchema }]  — paramSchema is { type:"object", properties:{...} }
  Notes: The condition union is CLOSED: these are exactly the $type values set_breakpoint accepts.; A nested predicate appears as { $ref: "SearchPredicateDto" } — fill it with another arm from this same list.; Enum-valued params carry their allowed values in "enum"; a param marked picker:"propertyPath" wants a dotted field path such as "Position.X"..
  Example: `list_breakpoint_types({})` — discover the valid condition $type values and their parameter shapes.

### Group P — Discovery with schema
- **`list_behaviors`** — List the behaviours available, each with the JSON schema of its parameter DTO. Key by tkbType (what this KIND of entity can do) or entityId (what THIS entity can do); omit both for every registered behaviour. `tkbType?` (number), `entityId?` (number). Returns [{ id, name, brainTier, paramSchema }]
  Notes: paramSchema is derived from the behaviour definition the runtime itself parses params with, so what you author matches what the engine reads.; An unknown entityId is a 404 whose hint points at GET /entities — it is not answered with an empty list.; A behaviour with no parameters returns an empty properties object, never null..
  Example: `list_behaviors({"entityId":1000})` — discover what entity 1000 can be told to do, and how to shape the params.

### Group O — Variables (the watch, over HTTP)
- **`list_entity_variables`** — List an entity's blueprint variables — the same (entity, asset, path) addressing a Details/watch row uses, with each variable's live value and whether a staged write is still pending on it. Req `networkId` (number), `asset?` (string). Returns { networkId, asset, assetId, dispatch, variables: [{ path, type, value, writable, pending, pendingValue? }] }
  Notes: pending: true means a staged write for that variable has not been applied yet, so value is still the OLD number — the machine half of the editor's yellow.; writable: false means the variable has no live address (its blueprint's dispatch kind has no staged-write layout), so it can be read but not staged.; A Library-dispatch blueprint legitimately has no working-state variables and returns an empty list, not an error..
  Example: `list_entity_variables({"networkId":1000})` — read every blueprint variable on entity 1000.
- **`get_entity_variable`** — Read one blueprint variable by name, with its live value and its pending (staged-but-not-yet-applied) value if a write is queued. Req `networkId` (number), Req `path` (string), `asset?` (string). Returns { networkId, asset, assetId, path, type, value, writable, pending, pendingValue? }
  Notes: An unknown variable name is a 400 pointing back at list_entity_variables — never an empty success..
  Example: `get_entity_variable({"networkId":1000,"path":"Health"})` — read entity 1000's Health variable and whether an edit is still queued.
- **`stage_entity_variable`** — STAGE a write to one blueprint variable, through the same seam the editor's Details panel uses. The value lands on the next advancing tick — not on this response. Req `networkId` (number), Req `path` (string), Req `value` (any), `asset?` (string). Returns { networkId, asset, assetId, path, staged: true, pending: true, note }
  Notes: Running is not a reason to refuse — it is a reason to stage. There is no "pause first" step.; Until the world advances, get_entity_variable still reports the OLD value with pending: true. Step or play to make it land.; A value whose width does not match the field is refused rather than written: the blackboard is shared between subsystems, so an overrun would corrupt a neighbour..
  Example: `stage_entity_variable({"networkId":1000,"path":"Health","value":42})` — queue Health = 42; it applies on the next advancing tick.

### Group T — Panels (the UI as data)
- **`list_panels`** — What the editor's UI is showing, without pixels: which panels are instrumented at all, and which published a view-model this frame. No params. Returns { captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }
  Notes: registered vs captured is the load-bearing distinction: a panel nobody instrumented and a panel whose window is closed are different facts, and only the second is fixed by opening a window.; kinds groups the live panels by their logical name — the key a cross-host comparison uses, since panel ids are unique per instance by design.; captured entries are latest-wins and are NOT cleared per frame: a panel that stopped drawing still reports its last model..
  Example: `list_panels({})` — see which panels are live and what kinds they are.
- **`get_panel`** — One panel's dumped view-model — the same object its draw renders from, so a field here is a field the designer sees. Req `panelId` (string). Returns { panelId, panelKind, model }
  Notes: The model is structured JSON, never a formatted blob — assert a field, do not parse prose.; A miss says WHICH kind of miss it is: not instrumented, or instrumented but not drawing..
  Example: `get_panel({"panelId":"editor_bp_manager"})` — read the breakpoint panel's model and assert what it lists.
- **`get_gizmo_frame`** — What the map is drawing this frame, as data: the debug primitives, projected per shape. `max?` (number). Returns { count, dropped, emitted, truncated, primitives:[{shape, space, layer, color, ...shape-specific}] }
  Notes: truncated tells you the frame was clipped by max — without it a cap would read as the end of the frame.; A shape with no field projection yet is reported by name with a note, never as aliased bytes..
  Example: `get_gizmo_frame({"max":50})` — inspect what the map is drawing without taking a screenshot.

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

### Group I — Recording / replay
- **`start_recording`** — Start recording. Enters preview and begins writing a .fdp file. `mode?` (string, "preview"|"live", def "preview"). Returns { recording:true, mode, fdpPath }
  Notes: mode="preview" (default): revertible, uses EnterPreviewMode→PrepareRecordingAsync.; mode="live": not supported in editor mode.; Mutually exclusive with checkpoint (both use the preview slot)..
  Example: `start_recording({"mode":"preview"})` — start a revertible preview recording.
- **`stop_recording`** — Stop the active recording. Finalizes BEFORE the exit rewind. No params. Returns { recording:false, fdpPath }
  Notes: For preview mode: finalizes BEFORE the exit rewind (hard ordering rule)..
  Example: `stop_recording({})` — stop recording and get the .fdp file path.
- **`load_replay`** — Load a .fdp recording into an ISOLATED ReplayBrowserContext. Req `fdpPath` (string). Returns { loaded:true, fdpPath, totalFrames, currentFrame }
  Notes: While replay is active, /replay/entities returns entities from the sandbox (not the live world).; Use list_replay_entities (not list_entities) while replaying..
  Example: `load_replay({"fdpPath":"/path/to/recording.fdp"})` — load a .fdp recording for inspection.
- **`seek_replay`** — Seek to a specific frame in the ISOLATED sandbox. Does NOT touch the live world. Req `frame` (number). Returns { frame, totalFrames }
  Notes: Isolation guarantee: does NOT touch the live world..
  Example: `seek_replay({"frame":0})` — seek replay to frame 0 (start).
- **`step_replay`** — Step one frame forward or backward in the ISOLATED sandbox. Does NOT touch the live world. `dir?` (string, "forward"|"back", def "forward"). Returns { stepped:bool, frame, totalFrames }
  Notes: Isolation guarantee: does NOT touch the live world..
  Example: `step_replay({"dir":"forward"})` — step one frame forward in the replay.
- **`get_replay_status`** — Replay sandbox status. No params. Returns { replayActive, currentFrame, totalFrames }
  Example: `get_replay_status({})` — check if replay is active and current frame.
- **`list_replay_entities`** — List entities from the ISOLATED replay sandbox at the current frame. No params. Returns Same schema as list_entities but from the sandbox repo, NOT the live world.
  Notes: Requires an active replay (call load_replay first).; Does not touch or affect the live world..
  Example: `list_replay_entities({})` — inspect entities at current replay frame.
- **`unload_replay`** — Dispose the replay sandbox and return to live world queries. No params. Returns ok:true envelope.
  Example: `unload_replay({})` — unload replay sandbox when done inspecting.

### Group J — Logs
- **`get_logs`** — Query the in-process log sinks. Returns [{timestamp, level, logger, message}] sorted newest-first. `level?` (string, "Trace"|"Debug"|"Info"|"Warning"|"Error"|"Critical"), `logger?` (string), `since?` (string), `max?` (number, def 200). Returns [{timestamp, level, logger, message}] sorted newest-first.
  Notes: level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.; logger = case-insensitive substring match on logger name.; since = ISO-8601 timestamp; entries with timestamp >= since are included.; Read off-thread — no main-thread marshal required..
  Example: `get_logs({"level":"Warning","max":50})` — get last 50 Warning-or-higher log entries.

### Group K — AI behavior traces
- **`observe_trace`** — Arm or disarm AI behavior trace buffer allocation for an entity. Req `networkId` (number), Req `on` (boolean). Returns { armed, networkId }
  Notes: Must arm before get_entity_trace will return populated trace data.; Without arming, get_entity_trace returns empty trace..
  Example: `observe_trace({"networkId":1000,"on":true})` — arm AI behavior tracing for entity 1000.
- **`get_entity_trace`** — Extract AI behavior trace for an entity. Req `networkId` (number). Returns BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.
  Notes: Arm the entity with observe_trace first to populate trace data.; Returns tier field indicating the AI tier type (BTree/HSM/blueprint)..
  Example: `get_entity_trace({"networkId":1000})` — read AI behavior trace for entity 1000 after arming.

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

### Group O — Manual-assist (focus / annotations)
- **`focus_entity`** — Pan and zoom the map canvas to an entity. MANUAL-VERIFY: camera move requires windowed session. Req `networkId` (number). Returns { focused: true } on success.
  Notes: Publishes CenterOnEntityCommand (headless-verifiable via event history).; The actual camera move only occurs in a windowed session (MANUAL-VERIFY)..
  Example: `focus_entity({"networkId":1000})` — center editor camera on entity 1000.
- **`add_annotation`** — Draw a debug primitive (sphere, anchor, or line) in the gizmo buffer. MANUAL-VERIFY: gizmo render requires windowed session. Req `type` (string), `networkId?` (number), `x?` (number), `y?` (number), `z?` (number), `radius?` (number), `heading?` (number), `color?` (string), `from?` (object), `to?` (object). Returns { added: true, primitiveIndex, bufferCount } on success.
  Notes: "sphere" — x, y, z, radius (float), optional color (hex "#RRGGBB").; "anchor" — networkId, x, y, z, optional heading (float).; "line" — from:{x,y,z}, to:{x,y,z}, optional color.; The buffer write is headless-verifiable; the actual gizmo render requires a windowed session (MANUAL-VERIFY)..
  Example: `add_annotation({"type":"sphere","x":100,"y":0,"z":50,"radius":10,"color":"#FF4400"})` — draw a red sphere at (100,0,50) with radius 10.

### Group N — World / coordinates
- **`get_world_info`** — World metadata: geo origin, spatial grid extent. terrain and navmesh are null in editor mode. No params. Returns { geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null }
  Notes: terrain and navmesh are null in editor mode..
  Example: `get_world_info({})` — get world geo origin and spatial grid extent.
- **`geo_to_local`** — Convert geographic coordinates to local ENU {x,y,z}. Req `lat` (number), Req `lon` (number), Req `alt` (number), `headingDeg?` (number). Returns { x, y, z, rotation? } — optional rotation if headingDeg was provided.
  Notes: Optional headingDeg → adds rotation quaternion to response..
  Example: `geo_to_local({"lat":50.0755,"lon":14.4378,"alt":200})` — convert Prague geo coords to local ECS metres.
- **`local_to_geo`** — Convert local ENU {x,y,z} to geographic coordinates. Req `x` (number), Req `y` (number), Req `z` (number), `rotation?` (object). Returns { lat, lon, alt, headingDeg? } — Heading: North=0°, East=90°.
  Notes: Optional rotation quaternion {x,y,z,w} → adds headingDeg to response.; Heading convention: North=0°, East=90°..
  Example: `local_to_geo({"x":100,"y":0,"z":50})` — convert local ECS position (100,0,50) to geographic coords.

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

## 5b. Field notes — learned by using this against a real defect (2026-08-23)

> Added after diagnosing "the platoon drives to (0,0) instead of the computed baseline" through this
> API. Everything below cost time to discover and is not covered above.

### ⛔ Enablement is an ENV VAR, not a CLI flag — and launch mode is broken

| | |
|---|---|
| ✅ **works** | `HROT_DEBUG_API_PORT=8099` on the runner's environment. That is the **only** thing `EditorSubsystem` §8b checks. `Hrot.SystemTests/EditorProcessFixture.cs` does exactly this |
| ⛔ **does not work** | `--debug-api` / `--debug-api-port`. 📐 **Measured:** no such option exists in `HrotRunnerConfiguration` or `RunnerOptions` — the flags were designed in `.dev/ai-debug-api/` and never landed on trunk |
| 🔴 **consequence** | `src/index.mjs:64-65` spawns the runner with those dead flags, so **`start_simulation` produces a runner with the API off** and then polls `/status` until timeout. **Attach mode (`--url`) is fine.** Fix is one line — set the env var on the spawn — but it is a code change, so it is recorded here rather than assumed |

⇒ ⭐ **Until that is fixed, drive the API directly over HTTP** (`localhost` only) against a runner you
started yourself with the env var set. Every endpoint in §4 works that way; the MCP layer is a wrapper,
not a requirement.

### ⭐⭐ Free-running loses the evidence — step, always

§3.B already prescribes `enter_preview{startPaused:true}` → `step{count:N}`. ⚠ **The reason matters:**
a behaviour under test may **complete before you can ask about it**. Free-running the hill-attack
scenario, every query returned the *post-hoc* state (all subordinates "arrived"), which hides the
dispatch that is the actual subject. Stepping 20 ticks caught it mid-flight.

### ⚠ Entity `networkId`s are REASSIGNED by `load_scenario`

Reloading the same scenario renumbered the platoon `1000-1007` → `1008-1015`. ⛔ Ids held across a
reload silently return **empty component dumps**, which reads like "the field is gone" rather than
"wrong entity". ⇒ ⭐ **re-run `list_entities` after every load**, never cache an id across one.

### ⚠ `/logs` can return zero entries — the real log is on disk

`GET /logs?max=300` returned `count: 0` on a live editor that was logging heavily. The behaviour trail
was in the NLog file: `<runner-bin>/logs/editor_{nodeId}.log`. ⇒ ⭐ **treat `/logs` as best-effort and
grep the file** when it comes back empty; that file is where `Behavior | Node:[…]` lines live, and they
are what identified which tree was actually running.

### ⭐⭐ `/world/geo-to-local` is a parameter ORACLE

Geo-authored parameters (`[lat, lon]` pairs in a mission plan) can be validated **independently of the
behaviour**: convert them yourself and compare against what the entity received.

```
POST /world/geo-to-local {"lat":52.523603,"lon":13.412705}  ->  {"x":523.0,"y":401.0}
```

That one call proved the geo transform was healthy and moved suspicion onto the parameter plumbing —
without it, "the coordinates are wrong" and "the transform is wrong" are indistinguishable.

### ⭐⭐⭐ An all-zero params block: read the CLAMPED field first

When a params dump is all zeros, the question is *parsed-badly* vs **never-parsed**. ⭐ Look for a field
the parser cannot leave at zero — a clamp or a fallback:

```csharp
TankSpacing = dto.TankSpacing > 0f ? dto.TankSpacing : 30f;   // can never yield 0
```

`TankSpacing == 0` in the live dump therefore **proved the resolver never ran**, in one step, with no
breakpoints. ⛔ Absence of a `ParseError` in the log said the same thing and is easy to misread as
"parsing succeeded". ⇒ ⭐ **a clamped field reading zero is the cheapest "this code never executed"
probe available.**

### ⚠ Blueprint-tier entities have no trace

`observe_trace` + `get_entity_trace` on a Blueprint-tier entity returns
`{"tier":"Blueprint","note":"Blueprint trace: assetId resolution not available via Debug API."}` —
armed successfully, but empty. Not a failure to report; use `get_entity` component dumps instead.

---

## 6. Discover before you guess

The API is self-describing — prefer discovery over assumptions:
- `list_commands` before `send_entity_command`
- `list_component_types` before `edit_component`
- `get_attributes_schema` before `patch_attribute`
- `list_entity_types` before `spawn_entity`
- `get_status` / `get_sim_state` whenever a command "did nothing" — you are probably in the wrong run state.
