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
| **Live** | A real run on every node | Yes — **once you `play`** | `load_scenario_live` |
| **Preview** | A revertible run from a RAM snapshot | Only when **unpaused** | `enter_preview`, `play`, `checkpoint`, or `start_recording{preview}` |
| **Replay** | Read-only playback of a `.fdp` in an isolated sandbox | N/A (you seek frames) | `load_replay` |

**Two load modes, and they are not the same operation.** `load_scenario_edit` freezes time for authoring;
`load_scenario_live` starts an exercise run. Both are cluster-wide two-phase-commit transitions — the editor
is not special, it is a one-node cluster. ⚠ In `mode: "all"` an *edit* load is currently partial (CGF has no
edit-load handler yet), so use **live** when every node must hold the world.

- **Time only advances when `inPreview == true` AND `isPaused == false`.** In Edit state the sim is frozen —
  `step`/commands that need ticks won't progress until you enter preview and unpause. Always check
  `get_sim_state` / `get_status` if unsure.
- ⭐ **A CLUSTER (`mode:"all"`) BOOTS PAUSED, DELIBERATELY, AND A LIVE LOAD DOES NOT START IT.** `simTime`
  stays at 0 with `isPaused:true` until you `play` — so *"nothing is happening"* is the **expected** state,
  not a fault, and it is the first thing to check before diagnosing anything as stuck. ⚠ It did not always
  behave this way: the clock used to start itself ~2 s after boot and run with no scenario loaded, which
  also meant every `step` was silently refused. If you meet an old build whose `simTime` climbs on its own
  with `clusterState:Idle`, that is the old behaviour — and any pause/step measurement taken on it is
  unreliable.
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
  Notes: Non-finite floats appear as string sentinels "NaN"/"Infinity"/"-Infinity" — valid JSON, not a bug.; SHAPE: { EntityId, NetworkId, Components:{ ... } } — Components is PascalCase, and so is every component and field name inside it (SimTransform.Position, NavigationIntent.Mode). Indexing a lowercase 'components' silently yields nothing and reads like an empty entity.; TO DIAGNOSE 'the sim ignores my order', COMPARE THE INTENT COMPONENT WITH ITS STATUS COMPONENT. The pair distinguishes three different bugs that look identical from the UI: intent empty => nothing issued the order; intent set + status ABSENT => the consumer never ran; intent set + status PRESENT + zero velocity => the consumer ran and produced no motion. Worked example: NavigationIntent{Mode,TargetSpeed} against NavigationStatus.; AUTHORITY FIRST: NetworkOwnership/NetworkAuthority carry HasAuthority, PrimaryOwnerId and LocalNodeId. On a cluster a write to an entity this node does not own is legitimately dropped, so check HasAuthority before filing 'the write did nothing'.; MEASURE MOTION AS A POSITION DELTA OVER A simTime DELTA, never over wall-clock. Sample get_status.simTime alongside each dump: BIT-IDENTICAL positions across a real simTime advance is the hard evidence; the same reading across a stalled clock proves nothing.; ON --mode all THIS READS ONE NODE -- THE ACTIVE PERSPECTIVE -- AND THE NODES DISAGREE. Brain (CGF/Scenario) and muscle (SimHost) hold separate copies of the same entity, and a defect can live entirely in the gap between them. Measured 2026-08-28: entity 1001 held Class:Tank, AccelGain:1.8 on CGF and PersonalCar, AccelGain:0 on SimHost, because the scenario's authored VehicleParams reached the brain intact and was dropped on the wire hop. The brain computed a valid path (which rendered) while the muscle could not accelerate -- on screen, indistinguishable from a broken navigator. SO: read the entity on BOTH nodes before concluding anything about 'the cluster'.; AND ?perspective= DOES NOT WORK: it is not implemented on any route and is IGNORED (you get a hint saying so since CE-112). Switch with POST /perspective {name:...} and then read; confirm with get_status.perspective. Passing ?perspective=ExCon -- a subsystem with NO WORLD AT ALL -- used to return a full component dump, which is how the ignored key was caught..
  Example: `get_entity({"networkId":1000})` — get full component dump for entity 1000.
- **`list_scenarios`** — List available scenarios by relative path. No params. Returns Available scenario names (relative paths) for use with load_scenario_edit / load_scenario_live.
  Example: `list_scenarios({})` — discover loadable scenario names.

### Group E — Scenario
- **`load_scenario_edit`** — Load a scenario for AUTHORING (Edit state), cluster-wide. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor.
  Notes: Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).; Edit state freezes sim time — nothing ticks until enter_preview or play.; CE-102 (2026-08-28) gave CGF the shared edit-load handler, so an edit load is NO LONGER partial on that node. This note previously said CGF had none -- that is now stale. Still prefer load_scenario_live for a real run, and verify either load by reading state.; AND THAT PARTIAL LOAD STILL ANSWERS ok:true — this is the single most misleading response in the API. Measured on --mode all from the Scenario perspective: ok:true with scenario:NULL, entityCount:0, an empty list_entities, and a gizmo frame of 603 primitives that were ALL grid lines. Every field says 'empty world' and the envelope says 'success'.; SO VERIFY A LOAD, NEVER TRUST IT: after any load read get_status.entityCount AND list_entities AND (if the map matters) the non-Line shape count from get_gizmo_frame. Three independent reads, because the envelope is not one of them. On the same host load_scenario_live gave 8 entities and Box2D 8 / Arrow 12 / Text 8..
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
  Notes: level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.; logger = case-insensitive substring match on logger name.; since = ISO-8601 timestamp; entries with timestamp >= since are included.; Read off-thread — no main-thread marshal required.; ON A CLUSTER ALWAYS PASS level:"Info" (or higher). Measured on --mode all: an UNFILTERED read returned 176 of 200 entries as [TC3] time-sync chatter (TimeSyncRequest/SyncResponse/RTT gentle-steer) logged at Trace by four nodes — no application line survives in the window. The same read with level:"Info" returned 25 lines and zero [TC3].; AN UNKNOWN FILTER IS STILL SERVED LENIENTLY, BUT IT NOW TELLS YOU (CE-107). level:"INFO_" or a misspelled param (limit instead of max) returns the whole ring as before — the endpoint does not refuse a diagnostic call — but the SUCCESS envelope carries hint.why naming the ignored key or the unapplied level, and it also says so when no level filter was given at all. READ hint ON SUCCESS: it is the difference between an answer and an answer to a different question.; The param is max, NOT limit. Nothing rejects limit; it is simply not read..
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
  Notes: Commands and spawns while paused are queued and take effect on the next step/play.; THE ACK MEANS APPLIED as of CE-104: the route waits until isPaused is actually true before returning, so the next read is safe. Measured before the fix on --mode all: ok:true while /status still read isPaused:false, with the clock running a further ~0.2s — because a cluster-wide pause is a FUTURE BARRIER so every node stops at the same simTime.; A 504 from this route means the pause barrier is stuck — a node never reached it. Check get_logs({level:"Warning"})..
  Example: `pause({})` — pause the running simulation.
- **`play`** — Enter preview and/or resume if paused. Time advances after this. No params. Returns ok:true envelope.
  Notes: Time advances after play (until pause or a breakpoint fires)..
  Example: `play({})` — start or resume simulation.
- **`get_sim_state`** — Current sim state: isPaused, inPreview, totalTime, timeScale. No params. Returns { isPaused, inPreview, totalTime, timeScale }
  Notes: Check this before driving — most mistakes are run-state mistakes..
  Example: `get_sim_state({})` — check current paused/preview/time state.
- **`step`** — Advance simulation by N discrete steps. Only meaningful in preview. `count?` (number, def 1). Returns ok:true envelope.
  Notes: Only advances time when inPreview==true. In Edit state this is a no-op.; count IS honoured cluster-wide as of CE-105, and it is gated per step: the route issues ONE step, waits for it to land, and repeats — so N steps take N frames and the call returns when the last one is acknowledged. Measured: count:60 advances simTime by exactly 1.0000s. Before the fix the loop ran inside a single frame and the cluster dropped all but the first, answering ok:true with 0.0167s of progress.; STILL VERIFY BY READING simTime, NOT ok. The gate now makes the ack trustworthy, but a simTime delta is the cheap proof and it costs one call.; A STEP IS REFUSED OUTRIGHT UNLESS THE CLUSTER IS IN A STEPPING MODE, and the refusal is not visible in the envelope — it is a warning in the log (see get_logs, RefusedStepCount). A cluster that is free-running drops every step. If simTime does not move, check get_status.isPaused first: pause, then step.; This is the loop's weak link, so prove it once at the start of a session: pause, step, read simTime. A silently-refused step makes every later observation meaningless (it invalidated a root-cause in this repo once — a 'nothing moved' reading taken through a step that never ran)..
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

### Group P — Mission editing
- **`get_mission`** — Read an entity's mission plan — its ordered tasks (behaviour, params, triggers, state) and the OCC version you pass back when editing. An entity with no mission returns an empty task list, not an error. Req `networkId` (integer). Returns { networkId, plan: { activeTaskId, tasks: [{ taskId, behaviorId, behaviorParams, executingEngine, state, triggers: [{ type, params }] }] }, version }
  Notes: version is the optimistic-lock token — pass it straight back to add_mission_task / clear_mission_tasks so a concurrent edit is caught as a 409 rather than silently overwritten.; The offline editor does not yet persist a snapshot version, so it reports 0 today; the edit path still round-trips it.; An unknown networkId is a 404 whose hint points at GET /entities..
  Example: `get_mission({"networkId":1000})` — read entity 1000's current mission before editing it.
- **`run_mission`** — Run (or restart) an entity's mission by jumping to its first task and resetting the phase clock. run and restart are the same jump-to-start the mechanism offers. Req `networkId` (integer), `restart?` (boolean, def false). Returns { networkId, restart, committed:true, version }
  Notes: Sends CMD_JUMP_TO_TASK to task index 0 — the mission still only advances while the sim is running (play_simulation / step_simulation)..
  Example: `run_mission({"networkId":1000})` — start entity 1000 executing its mission from the first task.
- **`add_mission_task`** — Append one mission task to an entity — the PROPER way a behaviour attaches (as a task). Names the behaviour pass-through and carries its params as JSON matching the behaviour's paramSchema. Commits the whole plan through the editor's own mission path with optimistic concurrency. Req `networkId` (integer), Req `behavior` (string), `params?` (object), `triggers?` (array). Returns { networkId, taskId, behavior, taskCount, committed:true, version }
  Notes: params is passed through verbatim — the engine reads it with plain JSON, the same string the editor's Mission panel stores. Shape it to the behaviour's paramSchema (list_behaviors), not to a separate mapper.; The commit is asynchronous: it resolves when the engine acknowledges. If the sim is not being pumped at all the call returns a 504 pointing at play/step.; A stale version yields a 409 (ERR_VERSION_CONFLICT), never a silent overwrite..
  Example: `add_mission_task({"networkId":1000,"behavior":"MoveToLocation","params":{"Latitude":50.1,"Longitude":14.4}})` — give entity 1000 a MoveToLocation task.
- **`clear_mission_tasks`** — Clear every task from an entity's mission (so a fresh sequence can be added), by committing an empty plan through the same optimistic-concurrency path. Req `networkId` (integer). Returns { networkId, taskCount:0, committed:true, version }
  Notes: Commits an empty plan — the same asynchronous, version-checked path as add_mission_task, so the same 409/504 rules apply..
  Example: `clear_mission_tasks({"networkId":1000})` — wipe entity 1000's mission so a new sequence can be authored.

### Group Q — Blueprint hot-attach
- **`list_blueprints`** — Every blueprint this editor compiled, with whether it can be attached to an entity. No params. Returns { count, blueprints:[{ blueprintId, name, assetId, kind, stateSize, attachable }] }
  Notes: Only Instance-dispatch blueprints occupy a slot on an entity; attachable says so up front rather than through a refusal..
  Example: `list_blueprints({})` — find a blueprint to try on a running entity.
- **`attach_blueprint`** — Attach an Instance blueprint to an entity — the quick way to try a behaviour without authoring a mission. Run-state-aware: lands immediately while paused/Edit, next tick while running. Req `networkId` (number), Req `blueprint` (string), `paramsJson?` (object). Returns { networkId, blueprint, blueprintId, attached:true, path:"direct"|"event", applied:"immediate"|"next-tick", status?, tier?, note }
  Notes: Run-state-aware (mirrors the editor's own panel): while time is FROZEN (Edit or paused) it attaches THIS frame (path:direct); while the sim is advancing it queues the ingress event (path:event) and you must step/play once before reading it back.; Params now PERSIST through save_scenario — an attach with non-default params survives save→reload (they ride the assignment as resolved bytes, layout-versioned by the blueprint's StructureHash).; A malformed paramsJson on the direct path is a 400 that changes nothing (parse-before-commit), not a half-applied slot.; After it lands, the entity's variables appear in list_entity_variables — name the asset, since the entity may now carry more than one. See what is attached with list_entity_blueprints..
  Example: `attach_blueprint({"networkId":1001,"blueprint":"ComponentCollectionDemo"})` — try a blueprint on entity 1001 right now.
- **`list_entity_blueprints`** — The Instance blueprints currently attached to an entity — see what you have assigned before editing. Req `networkId` (number). Returns { networkId, count, blueprints:[{ blueprintId, name, assetId, payloadSize }] }
  Notes: Reads the same slot table save_scenario snapshots, so it shows exactly what would persist.; list_blueprints is the catalog (everything compiled); this is what is attached to ONE entity..
  Example: `list_entity_blueprints({"networkId":1001})` — see which blueprints are on entity 1001.
- **`detach_blueprint`** — Detach an Instance blueprint from an entity. Run-state-aware, like attach_blueprint. Req `networkId` (number), Req `blueprint` (string). Returns { networkId, blueprint, blueprintId, detached, path:"direct"|"event", applied:"immediate"|"next-tick", note }
  Notes: Run-state-aware: removes the slot THIS frame while time is frozen (path:direct); queues the event while the sim advances (path:event, next tick).; On the direct path, detached:false means no slot for that blueprint was on the entity — nothing to remove..
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
- **`list_panels`** — What the editor's UI is showing, without pixels: every registered window plus every instrumented panel, and which of them published a view-model this frame. No params. Returns { captureEnabled, registered:[panelId], captured:[panelId], kinds:{kind:[panelId]}, staleness }
  Notes: registered vs captured is the load-bearing distinction: a surface that publishes no model and one whose window is closed are different facts, and only the second is fixed by opening a window.; CE-076: registered is COMPLETE for windows — WindowManager.RegisterWindow declares every window it registers, so a window can no longer be invisible here by forgetting to opt in. A window that publishes no view-model still appears in registered and is absent from captured.; A LAZILY registered window (one created on first activation of its perspective) is absent until that perspective has been visited — switch_perspective first if you are enumerating exhaustively.; kinds groups the live panels by their logical name — the key a cross-host comparison uses, since panel ids are unique per instance by design.; captured entries are latest-wins and are NOT cleared per frame: a panel that stopped drawing still reports its last model..
  Example: `list_panels({})` — see which panels are live and what kinds they are.
- **`get_gizmo_frame`** — What the map is drawing this frame, as data: the debug primitives, projected per shape. `max?` (number). Returns { count, dropped, emitted, truncated, primitives:[{shape, space, layer, color, ...shape-specific}] }
  Notes: truncated tells you the frame was clipped by max — without it a cap would read as the end of the frame.; A shape with no field projection yet is reported by name with a note, never as aliased bytes.; MOST OF A FRAME IS THE GRID. Measured on --mode all with an EMPTY world: 603 primitives, all of them Line. So a non-zero count is NOT evidence that anything is on the map.; TO ANSWER 'are the entities visible', COUNT PRIMITIVES BY shape AND IGNORE Line. A loaded hill-attack frame read 739 primitives: Line 670 plus Box2D 8, Arrow 12, Text 8, SemanticShape 16, SpatialAnchor 16 — the non-Line shapes are the world. Compare that histogram against the same read on the other host to localise a 'nothing renders here' report.; The default cap is 500 and the grid alone can exceed it, so pass max (e.g. 2000) before concluding a shape is absent — otherwise truncated:true means your answer is about the grid..
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
- **`create_asset`** — Create a new AI asset (BTree / HSM / Blueprint) through the host's own New-Asset path, then open it as a document. Req `kind` (string), Req `name` (string), `path?` (string), `recipe?` (string). Returns { assetId, name, kind, recipe, status, sourceFilePath, note }
  Notes: It runs the same per-kind INewAssetService the New-Asset dialog runs, writes the file and refreshes the catalog — so the result appears in list_assets by the same rebuild a dialog-created asset does.; The new asset is opened as a document, so you can author it immediately with read_asset_graph and the graph tools.; A host that composes no create path answers 503 explaining that EDITING an existing asset does not need it.; Call list_asset_recipes first to see what this host can create from. A recipe name it does not offer is REFUSED with the available names — it never silently falls back to a blank asset..
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
- **`list_asset_recipes`** — List the recipes and blank templates create_asset can build from, per asset kind. `kind?` (string). Returns { kinds[], recipes[{ id, kind, name, description, category, isBlankTemplate, sourceFilePath }], note }
  Notes: `name` is exactly what create_asset takes as its `recipe` argument.; `isBlankTemplate` separates a synthetic empty starting point (Empty, Starter) from a CONTENT recipe cloned from a real asset — the two are not interchangeable and the name alone does not tell you which it is.; The list is read live from each kind's INewAssetService, so recipes added to disk appear without restarting the host.; `description` is null for recipes that carry no RecipeMetadata — the synthetic Empty/Starter entries do not.; A host that composes no per-kind new-asset registry answers 503; the same registry backs create_asset, so if this is 503 then create is too..
  Example: `list_asset_recipes({})` — see what kinds of asset this host can create.
- **`delete_entity`** — Remove an entity from the world through the ELM lifecycle. Scenario authoring is world manipulation, and this is its delete. Req `networkId` (number). Returns { networkId, queued:true, note }
  Notes: There is no such thing as editing a scenario FILE: the file is a reduced snapshot of the world at save time, so authoring a scenario means spawning, configuring and deleting entities, then calling save_scenario.; Queued like spawn_entity — teardown runs on a later tick. Call step, then list_entities, before asserting the entity is gone.; An unknown networkId is a 404 rather than a queued no-op..
  Example: `delete_entity({"networkId":1000})` — delete an entity from the world.

### Group X — Graph command union & discovery
- **`get_node_kind_schema`** — One node kind's full schema and documentation: pins, flags, palette behaviour, and the reflected DTO params when the kind resolves to an action. Req `assetId` (string), Req `kind` (string). Returns { kind, displayName, category, doc, isPure, isLatent, isDeprecated, paletteAction, isAttachmentKind?, attachmentCategory?, keywords[], inputs[], outputs[], paramsSource, params[], note }
  Notes: MEASURED from the host's own INodeCatalog and action-schema exporter — never a hand-authored kind table, which would rot the moment a node kind is added and nothing would fail.; `paramsSource` says where params came from: exporter:exact, exporter:suffix (a probable match, not a certain one), none:not-an-action, none:dto-fields-not-reflected, or none:no-exporter-wired. An empty list WITHOUT that field would read as 'this kind has no params', which is a different and often false claim.; The catalog cannot say whether a kind is a CONTAINER — container-ness belongs to an instantiated node. Read container/region structure per node from read_asset_graph.; `paletteAction` is the kind-level structure fact the catalog does have: CreateNode makes a node, AttachToSelected makes an ATTACHMENT on the selected node..
  Example: `get_node_kind_schema({"assetId":"00000000-0000-0000-0000-000000000000","kind":"bt.selector"})` — read one node kind's pins, params and docs.
- **`list_graph_command_types`** — Every GraphCommand variant apply_graph_command accepts, with the fields each one takes. Req `assetId` (string). Returns { count, variants[{type,fields[]}], unsupported[{type,reason}], note }
  Notes: Call this before apply_graph_command instead of guessing a payload shape — the variant names match the nested record names in NodeEditor.Core.Commands.GraphCommand exactly.; A field suffixed '?' is optional. Ids are GUID strings from read_asset_graph.; 'Batch' takes {commands:[...]} and applies them as ONE undo entry, with the inverses reversed so nodes are restored before the links that reference them.; The 'unsupported' list is normally empty; an entry there is a deliberate decision with its reason, not an oversight..
  Example: `list_graph_command_types({"assetId":"00000000-0000-0000-0000-000000000000"})` — discover every graph-edit command and its fields.
- **`apply_graph_command`** — Apply ONE GraphCommand to an open graph — the whole ~35-variant union, including BTree decorators (attachments) and HSM parallel regions the typed verbs cannot express. Req `assetId` (string), Req `type` (string), `commands?` (array). Returns { type, applied, undoable, message, newIds{}, nodeCount, linkCount, nodeDelta, linkDelta, note }
  Notes: THE PARITY GUARANTEE: the command goes through GraphView.Execute — the same undo stack and the same host sink a canvas gesture uses. There is no MCP-only mutation path, on any of the three hosts, with zero per-host code.; The typed verbs (add_graph_node, add_graph_link, set_graph_param, remove_graph_elements) are sugar over this same union — they are not a parallel model.; `newIds` carries any id the command MINTED (nodeId / linkId / attachmentId / commentId), so you can address what you just created.; `undoable:false` means no inverse could be derived from the read-only model (the refactor ops, SetNodeProperty, RemoveRegion). The edit still applied; the undo stack simply has no entry. A wrong inverse would corrupt the graph silently, so none is recorded.; A refusal is the HOST's own answer (an invalid wire, an unknown kind) and comes back 400 with its reason — it is a legitimate outcome of editing, not a server error..
  Example: `apply_graph_command({"assetId":"00000000-0000-0000-0000-000000000000","type":"AddNode","kind":"bt.selector","position":{"x":80,"y":40}})` — add a node through the union route and get its new guid.
- **`get_node_properties`** — One node's editable properties with their CURRENT values — what the Details panel shows. Req `assetId` (string), Req `nodeId` (string). Returns { assetId, nodeId, kind, title, doc, count, properties[{pinId,name,type,value,hasValue,doc?,rangeMin?,rangeMax?,unit?,picker?}], note }
  Notes: Values come from the MODEL and schema from the CATALOG, joined here — so a value is never reported without the type and constraints needed to change it correctly.; Only INPUT DATA pins appear: an exec pin has no value and an output's is computed, so listing them would invite a set that must be refused.; Set one with set_graph_param, or apply_graph_command type:"SetPinDefault".; Range / unit / picker metadata is the same the Details editor itself reads — it is carried on the pin's default descriptor, not re-derived here..
  Example: `get_node_properties({"assetId":"00000000-0000-0000-0000-000000000000","nodeId":"11111111-1111-1111-1111-111111111111"})` — read a node's current property values and their schema.
- **`list_editor_commands`** — The EDITOR command bus — every toolbar/menu/hotkey command with its live enabled and checked state. `category?` (string). Returns { count, total, commands[{id,displayName,category,doc,defaultKey?,isEnabled,isChecked?}], note }
  Notes: This is NOT list_commands — that one enumerates publishable FDP EVENT types for send_entity_command. These are the editor's own commands, invoked with invoke_editor_command.; isEnabled/isChecked are evaluated NOW over live editor state (is there a selection? is the undo stack empty?), so they are a snapshot.; The command set is per OPEN DOCUMENT — it is built by the per-kind document factory, so opening a different asset kind changes it. Open an AI asset first.; The descriptors are self-documenting: DisplayName, Category, Description and DefaultKey are carried inline, so no attribute harvest is needed here..
  Example: `list_editor_commands({})` — list the editor commands and which are currently enabled.
- **`get_editor_command`** — Describe one editor command. Req `commandId` (string). Returns { id, displayName, category, doc, defaultKey?, isEnabled, isChecked? }
  Notes: Ids look like 'editor.delete-selection'. The available set depends on which document kind is open.; A 404 here means the id is not registered for the currently open document — not that it never exists..
  Example: `get_editor_command({"commandId":"editor.delete-selection"})` — describe one editor command before invoking it.
- **`invoke_editor_command`** — Run an editor command through the same seam the toolbar, menu and hotkey use. Req `commandId` (string), `args?` (object), `canvasPos?` (object). Returns { commandId, displayName, invoked, success, message, note }
  Notes: A DISABLED command is refused with 409 BEFORE it is invoked. The editor greys it out for the same reason — usually an empty selection or an empty undo stack — and running it anyway would be the one path that accepts what the editor refuses.; Read list_editor_commands for the live enabled state, and set up the precondition first (e.g. select something).; A headless origin never pre-flights a confirmation (ruling 53): the command runs directly and the origin-side LOG is the safety net. The host logs every invocation.; Effects that redraw appear on the NEXT frame — step a tick before reading get_panels..
  Example: `invoke_editor_command({"commandId":"editor.select-all"})` — run an editor command headlessly.

### Group Y — node diagnostics
- **`trigger_cluster_diagnostic_dump`** — Collect diagnostics on the named cluster nodes and pull them to the NAS — the same operation the ExCon's Execute Diagnostic Dump button drives. Req `nodes` (array), `dumpEvents?` (boolean), `dumpEntities?` (boolean), `dumpArchitecture?` (boolean), `dumpLogs?` (boolean), `eventProviders?` (array), `useMarkdown?` (boolean), `maxAgeHours?` (number), `severityThreshold?` (number). Returns { transactionId, nodes[], queued:true, note }
  Notes: ASYNCHRONOUS and cluster-wide: the response confirms the request was PUBLISHED, not that files exist. Every selected node gathers, then the orchestrator pulls to the NAS over SMB.; Poll get_cluster_diagnostic_status until manifestPaths is non-empty — that is the completion signal.; An empty nodes[] is refused rather than read as 'every node': the editor's own panel disables its button on the same condition, and dumping the whole cluster is a different operation from dumping one node.; This adds no collection mechanism — it publishes the same CQRS intent the operator's button publishes, onto whichever node's orchestration bus is reachable.; The request is logged with its transaction id and target nodes: a headless origin never pre-flights a confirmation, so that log is the safety net (ruling 53)..
  Example: `trigger_cluster_diagnostic_dump({"nodes":[1]})` — collect diagnostics from node 1 to the NAS.
- **`get_cluster_diagnostic_status`** — Whether a cluster transaction is in flight, and the file manifest of the last successful diagnostic dump. No params. Returns { inFlight, manifestPaths[], manifestCount, note }
  Notes: Reads the same read model the ExCon's Cluster Diagnostics panel renders, so it answers what a human at the console would see.; manifestPaths are relative to the NAS base directory and describe the LAST SUCCESSFUL dump. EMPTY means none has completed yet — not that one failed.; inFlight covers any cluster transaction, not only a dump.; Only a node that builds and pumps a ClusterUiCache can answer (in --mode all that is ExCon); a host without one can still TRIGGER a dump but cannot observe it, and says so..
  Example: `get_cluster_diagnostic_status({})` — check whether the diagnostic dump finished.
- **`get_architecture_diagnostics`** — This NODE's modules, ECS systems and DDS translators, one entry per subsystem, read from each subsystem's own ModuleHostKernel. `subsystem?` (string). Returns { subsystems[{ subsystem, perspective, modules[], systems[], translators[], moduleCount, systemCount, translatorCount }], note }
  Notes: Per SUBSYSTEM, not per node: a --mode all node runs SimHost, IG, CGF and the orchestrator side by side and each holds its own kernel, so one snapshot per node would have to drop the rest.; Every node hosts its own MCP endpoint. This answers for THIS node only — ask each node's own endpoint for its own architecture.; A subsystem with no ECS kernel (ExCon, an orchestrator-only node) correctly reports nothing; check the 'diagnostics.architecture' cell in get_capabilities rather than reading the absence as a wiring bug.; modules carry lifecycleState and circuitState, so a module stuck open or failing shows up here without reading logs.; It allocates the whole snapshot on every call — fine for an operator query, wrong in a loop..
  Example: `get_architecture_diagnostics({})` — see what modules and translators this node is running.

---

## 5. Gotchas (the things that actually trip agents up)

1. **Time is frozen in Edit state.** `step`/commands do nothing visible until `enter_preview` + `play`.
   Check `get_sim_state.inPreview`.
1b. **A 501 `NOT_SUPPORTED_HERE` is an answer, not a fault.** It names the missing capability
   (`time.drive`, `world.read`, `scenario.load`, …) and means *the active perspective cannot serve this*.
   Call `get_capabilities`, then `switch_perspective` to one whose matrix row has that capability — do not
   retry the same call.
1c. **Pick the load mode deliberately.** `load_scenario_edit` = authoring, time frozen.
   `load_scenario_live` = a real run on every node. CGF gained the shared edit-load handler in `CE-102`
   (`2026-08-28`), so an edit load is no longer partial on that node — still prefer **live** when you want a
   real run, and verify either load by reading state rather than trusting the envelope (§5b).
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

## 5b. ⭐ `ok:true` IS NOT EVIDENCE — verify by reading STATE

**This is the one rule that would have saved the most time.** The envelope reports *"the request was
accepted"*, which is not *"the thing happened"*. Measured on `--mode all`, three different endpoints
answered `ok:true` having done nothing, or a fraction, of what was asked:

| call | said | actually did |
|---|---|---|
| `load_scenario_edit` | `ok:true` | loaded **nothing** on that node — `scenario:null`, `entityCount:0`, empty `list_entities` |
| `step{count:120}` | `ok:true` | advanced `simTime` by **0.0167 s** — one frame; and before the cluster booted paused, **every step was refused** and still acked |
| `pause` | `ok:true` | `isPaused` was still `false` on the next read; it flipped a step later |

**Two of those three are now GATED** — `step` honours `count` and returns when the last tick is acknowledged
(`CE-105`), and `pause` returns only once `isPaused` is actually true (`CE-104`). ⛔ **The load is not**:
`load_scenario_edit` still answers `ok:true` having loaded nothing on a cluster node. ⭐ And a **success**
envelope can now carry `hint.why` (see the `/logs` notes) — so **read `hint` on success**, not only on failure.

**So: for every state-changing call, name the read that proves it.** `simTime` for a step,
`get_status.isPaused` for a pause, `entityCount` + `list_entities` for a load. It is one extra call and it
is the difference between a diagnosis and a guess.

> **What it costs to skip.** Two root causes in this repo were wrong because of this. A *"the entities never
> move"* finding was measured through steps that were being silently refused — the reading was real, the
> instrument was broken, and the conclusion (*"nothing consumes the intent"*) was false; the consumer was
> running fine. **A measurement taken through an unverified instrument is not evidence.**

## 5c. ⭐ Prove your instrument once, at the start of a session

Before diagnosing anything on a cluster, spend four calls establishing that the tools work:

1. `get_status` → note `isPaused`, `simTime`, `clusterState`, `perspective`.
2. `pause`, then poll `get_status` until `isPaused:true` — now you know pause lands.
3. `step{count:1}`, and check `simTime` **moved** — now you know steps are not being refused.
4. `get_logs({level:"Info", max:40})` — now you know the log is readable (see the `/logs` notes: an
   unfiltered cluster read is ~90% time-sync `Trace` chatter, and an unknown filter is *silently ignored*,
   so a typo returns the whole ring and looks like an answer).

**A cluster boots PAUSED** (that is deliberate), so *"nothing is happening"* is the expected state until you
`play`. Check `isPaused` before believing anything is stuck.

### 5c.1 ⭐⭐⭐ The one test that has caught every instrument fault so far

**Ask the instrument something it CANNOT truthfully answer, and see whether it answers anyway.**

Three faults were found this way in a single investigation (`2026-08-28`), and all three had the *same
shape*: **a plausible, well-formed answer to a different question** — never an error, never an empty
result that looked wrong.

| the impossible question | what a broken instrument did |
|---|---|
| read an entity **scoped to `ExCon`**, which has no ECS world at all | returned a full 33-component dump — proving `?perspective=` was **ignored** and every "per-node" read had been the same node (`CE-112`) |
| `GET /tkb/types` on a cluster node whose catalog was known to be populated | answered `[]`, and *empty is a valid-looking answer*, so it was believed and became the leading hypothesis for a movement bug (`CE-110`) |
| `/logs?limit=400` — `limit` is not a parameter | returned the whole unfiltered ring (`CE-107`) |

⇒ **Empty lists and zero counts are the dangerous ones**, because they read as data rather than as
failure. Before trusting a read that will drive a diagnosis, make one call whose *only* correct answer is a
refusal. If it succeeds, the instrument is not measuring what you think.

### 5c.2 ⛔ `?perspective=` IS IGNORED — switch, then read

`?perspective=` is **not implemented on any route**. Passing it silently reads the **ACTIVE** perspective,
which on `--mode all` is usually a *different node with different state*. Since `CE-112` the response
carries a hint saying so, but the correct pattern is:

```
POST /perspective {"name":"Scenario"}   # then read
GET  /status                            # confirms the active perspective
```

**This matters more than it sounds.** On `--mode all`, `CGF` (Brain) and `SimHost` (Muscle) genuinely
disagree about the same entity: measured `2026-08-28`, entity 1001 held `Class: Tank, AccelGain: 1.8` on
CGF and `PersonalCar, AccelGain: 0` on SimHost. Reading "the cluster" without switching gives you one of
those two answers with no indication which.

### 5c.3 🔴🔴🔴 A FIELD IS ONLY TRUE ON THE NODE THAT OWNS ITS TIER — a zero on the other one is CORRECT

§5c.2 tells you to switch. **This tells you what a switched read is worth**, and it is the trap that costs
the most, because the wrong answer is *well-formed, plausible and silently wrong*.

⛔ **First: the perspective name is NOT the subsystem name.**

| subsystem | perspective to `POST` | role |
|---|---|---|
| CGF | **`Scenario`** ⚠ | Brain |
| SimHost | `SimHost` | Muscle |
| IG | `IG` | — |
| ExCon | `ExCon` | no ECS world — entity routes answer `NOT_SUPPORTED_HERE` |

📌 **Measured `2026-09-04`, and it produced a wrong root cause that was committed before it was caught.**
`BehaviorState.ActiveBehaviorHash` read **`0`** on `SimHost` and was reported as *"no behaviour is running
on the cluster"*. On `Scenario` the same entity, same instant, read **`-1606975122` — byte-identical to the
editor.** The Brain was running the behaviour all along; the Muscle simply does not run the Brain tier, so
its zero was **correct**.

⭐⭐⭐ **The rule: before reading a field, ask which tier owns it. A zero, an absence, or a default on the
other node is EXPECTED and is not evidence of anything.**

| field | authoritative on | on the other node |
|---|---|---|
| `BehaviorState.ActiveBehaviorHash`, `BrainBTreeState`, `MissionPlanQueue`, `TargetMemory`, `ActiveSensorTracks` | **Brain** (`Scenario`) | 0 / empty **by design** |
| `SensorContactList`, `VehicleState`, `NavState`, `WeaponChannel` execution | **Muscle** (`SimHost`) | may lag or differ |
| `NavigationIntent` | written by the **Brain**, consumed by the **Muscle** | present on both — ⭐ compare them to prove the wire |
| `Health`, `VehicleParams`, `PerceptionReceptor` | ⚠ **both, and they can DISAGREE** | scenario-authored on the Brain vs TKB-derived on the Muscle |

⚠ **That last row is a live defect class, not a quirk** — measured the same day: `Health` `50/50` on the
Brain (the scenario's value) and `3000/3000` on the Muscle (the TKB's), from one entity, one instant.

### 5c.4 ⭐ `/diagnostics/architecture` is the ONE route that ignores the perspective

Every other data route answers for the **active** perspective only. This one reports **every subsystem on
the node at once** — modules, ECS systems, and per-translator `sentSamples`/`receivedSamples`. Use it to
answer *"is this system even scheduled here?"* and *"is the wire carrying X?"* **without** switching, and
before you start switching for anything else.

### 5c.5 ⛔⛔ "NOT AVAILABLE" CAN MEAN "NOT WIRED" — and it reads exactly like "not there"

📌 **Measured `2026-09-04` (`CE-169`).** `GET /behaviors` answered `"Behavior registry not available."` on a
cluster node **that was resolving a behaviour hash to run a behaviour at that moment**. The registry was
fully populated; the composition root had simply never handed it to the API. Two more reads were degraded
by the same omission: `/entities/{id}/state` omitted the behaviour **name**, and `/trace` reported
`tier: "unknown"`.

⭐⭐ **The distinguishing test — one call, and it is the same shape as §5c.1:** ask the **editor** the same
question. Identical hash + a resolvable name there ⇒ the thing exists and your instrument on the cluster is
blind. ⛔ **Never let a "not available" become a premise** — it is the R-133 shape: an instrument that cannot
tell *absent* from *unwired* will be read as evidence of absence.

⚠ **Still open at the time of writing (`CE-171`):** `/trace` answers `tier: "unknown"` on every cluster
node, because the tier is selected from the debug **sessions**, which the cluster's service constructor does
not accept at all. `BrainTier` is present and correct on both hosts — ⛔ **do not read `tier: "unknown"` as
"no BTree is running"**.

### 5c.6 ⚠ Fixed-size array components come back COLLAPSED — you cannot decode them from `/entities/{id}`

`BrainBlackboard.BehaviorParameters`, `SensorContactList.EntityIds`, `TargetMemory.ThreatScores` and every
other inline fixed array render as **`{"FixedElementField": N}`** — one element, not the buffer. So an
entity dump can tell you a contact **count** but never the behaviour's live **parameter values**.
⛔ Do not plan a diagnosis around reading blackboard params out of an entity dump; there is no route for it
today.

## 5d. ⭐ Localise a "it works on the editor, not on the cluster" report

The same three reads, run on both hosts, turn a vague UI report into a located defect:

- **`get_status.entityCount`** — does this node hold the world at all?
- **`list_entities`** — and can it enumerate it?
- **`get_gizmo_frame`, counted by `shape` ignoring `Line`** — is it *drawing* it? (An empty world still emits
  ~600 grid `Line`s, so a non-zero `count` proves nothing — see that tool's notes.)

Then, for *"the order is ignored"*, compare an **intent** component against its **status** component on the
entity (`get_entity` notes spell out the three-way split). Populated intent + absent status + zero velocity
are three different bugs that look identical on screen.

⭐⭐ **And on a cluster, add a fourth read: THE SAME ENTITY ON BOTH NODES** (`POST /perspective` between
them — §5c.2). Brain and muscle hold *different copies*, and a defect can live entirely in the gap:
measured `2026-08-28`, a scenario's authored `VehicleParams` reached CGF intact and was **dropped on the
wire hop**, so the Brain computed a valid path (which rendered) while the Muscle had `AccelGain: 0` and
could not accelerate. **On screen that is indistinguishable from a broken navigator.** A one-node read —
either node — would have confirmed the wrong theory.

## 5e. ⛔⛔ DRIVING IT OVER PLAIN HTTP (when the MCP server is down)

The MCP server does drop. When it does, **the whole surface is still reachable with `curl`** — every tool
in this document is an HTTP route on the same host. Do not downgrade to reading engine source.

```bash
curl -s --noproxy '*' -m 15 http://localhost:8099/status
curl -s --noproxy '*' -m 90 -X POST http://localhost:8099/scenario/load/live \
     -H 'Content-Type: application/json' -d '{"name":"hill-attack","waitForReady":true}'
```

`--noproxy '*'` matters: an agent sandbox usually exports `HTTPS_PROXY`, and localhost must bypass it.

### 5e.1 🔴🔴🔴 THE ROUTE NAMES ARE NOT THE TOOL NAMES — and a wrong path returns an EMPTY DATASET

**This cost a whole false finding, `2026-08-28.`** The tool is `get_gizmo_frame`; the route is
**`GET /panels/_gizmo`**. There is no `/gizmo/frame`. The mapping is *not* mechanical — do not derive a
path from a tool name. **`Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs` is the authoritative
route list** (`grep -oE '"/[a-zA-Z0-9/_{}-]+"'` over it prints every one).

⛔⛔ **And here is the trap that turns a typo into a bug report:** a path that does not exist answers

```json
{"ok":false,"data":null,"error":"Not found","hint":null}
```

with **HTTP 200**. A script that reads `data` without checking `ok` gets `None`, defaults it to `[]`, and
prints a confident **zero**. Measured: probing `/gizmo/frame` reported *"0 primitives on every
perspective, not even the grid"* — which reads exactly like a systemic capture failure and was filed as
one. The correct route, same boot, same second, returned **739 primitives with 69 non-`Line` shapes**.

🔒 **So: check `ok` and print `error` BEFORE touching `data`, in every probe script.**

```python
r = json.load(sys.stdin)
if not r.get("ok"):
    print("ROUTE ERROR:", r.get("error")); sys.exit(1)   # never fall through to data
```

**Why this is worse than an ordinary typo:** every other instrument fault in §5c was the *server*
answering a different question. This one is the *client* inventing an answer — and "the feature is
broken" is a far more expensive wrong conclusion than "my call failed". An empty result that you did not
prove came from a live route is not a measurement.

---

## 6. Discover before you guess

The API is self-describing — prefer discovery over assumptions:
- `list_commands` before `send_entity_command`
- `list_component_types` before `edit_component`
- `get_attributes_schema` before `patch_attribute`
- `list_entity_types` before `spawn_entity`
- `get_status` / `get_sim_state` whenever a command "did nothing" — you are probably in the wrong run state.
