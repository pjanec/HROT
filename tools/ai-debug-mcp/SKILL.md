---
name: ai-debug-sim
description: Drive and inspect a running Hrot ECS simulation (the FDP editor) over the ai-debug MCP server — load scenarios, query/mutate entities, set breakpoints, checkpoint/diff, record/replay, trace AI behaviors. Use when asked to test, debug, reproduce, or author simulation state autonomously.
---

# AI Debug & Test API — Agent Guide

> ⛔⛔ **THIS FILE (`SKILL.md`) IS GENERATED — DO NOT HAND-EDIT IT.** The command reference below is built
> from the C# route table, so a manual edit is silently overwritten on the next regen. To CHANGE what a
> command does or how it is documented, edit the **ground truth** and regenerate:
> 1. **Endpoint behaviour + params + notes** → the `RouteDoc`/`RouteParam` records in
>    `Hrot/Subsystems/Hrot.Editor/DebugApi/DebugApiRouteDocs.cs` *(these flow out through `GET /capabilities`)*.
>    *(C# XML `<summary>` comments on the handler do NOT feed the docs — they are for code readers only.)*
> 2. **Tools with no HTTP endpoint** *(e.g. `start_simulation`)* → `tools/ai-debug-mcp/catalog-supplement.mjs`.
> 3. **This guide's prose sections** *(mental model, workflows, gotchas)* → `tools/ai-debug-mcp/skill-parts/*.md`.
>
> Then regenerate: `node tools/ai-debug-mcp/gen-catalog.mjs` *(runs the built runner to dump `GET /capabilities` →
> writes `tool-catalog.mjs`)* then `node tools/ai-debug-mcp/generate-skill.mjs` *(assembles `SKILL.md`)*.
> `generate-skill.mjs --check` / `gen-catalog.mjs --dump <m> --check` fail if the committed output is stale.

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
| **Preview** | The cluster's dry run, rewound on exit; **never recorded** | Only when **unpaused** | `enter_preview` (from Edit) |
| **Replay** | Every node replays its OWN recording of a past live run | `play`/`pause`/`step` drive the replay clock; `seek_replay` jumps | `enter_replay` (from ANY state) |

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
- **Preview is revertible.** Entering preview snapshots the world; `stop_preview` rewinds to it. It is never
  recorded — only a LIVE run records, on every node, by itself.
- **Every route that changes the cluster goes through the cluster master, on every host** — the editor is a
  one-node cluster. There is no editor shortcut and no `cluster/` prefix to remember: `enter_preview`,
  `checkpoint`, `enter_replay`, `seek_replay`, `save_scenario` are each one master intent.
- **A checkpoint is part of the LIVE recording** — a named keyframe on every node — not a preview snapshot.
  `restore_checkpoint {label}` continues the live run from it.
- **A replay is the cluster's, not a sandbox.** `enter_replay` (one request from any state — the master plans
  the way) puts every node into its own recording; the ORDINARY readers (`list_entities`, `get_entity`,
  `get_entity_brain`, `get_entity_journal`) then read the replayed world. `stop_replay` goes to Idle.

**The envelope.** Every tool returns `{ ok, data, error, awaited }`. On success read `data`. On failure
`ok:false` and `error` explains why (and the tool result is flagged as an MCP error). `awaited` relates to
wait-gating (below). The server passes this through verbatim — it never hides a failure as success.

**On a 500, read `fault` — it says WHERE the exception happened** (`CE-190`). An unhandled server-side
exception now reports its origin, not just its message:

```
"error": "System.NullReferenceException: Object reference not set… @ DebugApi/DebugApiService.cs:1234 in DebugApiService.DumpEntity",
"fault": { "type": …, "message": …, "site": "<file>:<line> in <Type.Method>", "frames": [ … ], "inner": [ … ] }
```

`error` carries the type and site inline (some callers only ever see that string); `fault` carries the frame
list and the inner-exception chain. **Report the `site` when you escalate a 500** — it is the difference
between "the API broke" and a file and line someone can open. Two caveats: a fault raised on the main thread
arrives wrapped in an `AggregateException`, and `site` deliberately names the *inner* throw rather than the
await (`wrappedIn` records the wrapper); and file/line need PDBs beside the assembly — without them `site`
degrades to `Type.Method +IL_0042`, never to nothing.

**A malformed field is now a 400, not a silently-substituted value** (`CE-191`). The API used to answer
`ok:true` after quietly discarding input it could not parse. It no longer does:

| you send | you used to get | you now get |
|---|---|---|
| `spawn` with an unparseable `transform` | the entity **at the origin**, `ok:true` | 400, nothing spawned |
| `spawn` with a **typo'd** component type | the entity **without that component**, `ok:true` | 400 naming the unknown type |
| a graph command with `"x": "left-ish"` | the node at **0**, `ok:true` | 400 naming the field |
| `remove` with one malformed id among five | **the other four deleted**, `ok:true` | 400, nothing removed |

⚠ **Absent optional fields are still legal** — only *unparseable* ones changed. And on reads, an
unserializable row is no longer dropped: it stays in the array carrying `payloadError` /
`serializationError`, so a count is never quietly short.

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

#### C0. What a utility decision IS, and which node runs it (CE-1325)
- `list_utility_decisions` → every decision (the C# `[UtilityDecision]` classes).
- `get_utility_decision {key:"Combat posture", networkId?}` → per option its aggregation and considerations (input, context, weight, curve
  kind + slope / exponent / shift, modifier); with `networkId` that unit's winner, margin and raw → curved → weighted per consideration
  (traced units), and the consideration that zeroed an option.
- `get_behaviour_decisions {name:"CombatPosture"}` → which node / state / transition binds which decision: `Chooses` (ChooseOption),
  `Ranks` (RankCandidates), `Reads` + the option (an IsOption guard), `Holds` (a custom action's params). Works for C#-built behaviours
  with no asset.

#### C1. Stop on a BEHAVIOUR STEP (CE-1323) — "when he enters Withdrawing", "when that transition fires"
Read the running behaviour's name first (`get_entity_brain` → the running behaviour), then:
```json
set_breakpoint {"condition":{"$type":"BehaviourStep","Behaviour":"<name>","Kind":"StateEnter","State":"Withdrawing"},
                "filterNetworkId":<his id>}
```
| `Kind` | fields | stops when |
|---|---|---|
| `StateEnter` / `StateExit` | `State` | he arrives in / leaves the state |
| `InsideEnter` | `State`, `Region` (-1 = anything under it; n = region n of a parallel state) | any state inside it is entered |
| `TransitionFires` | `From`, `To`, `Event` (optional) — no `From` = a global transition | that transition fires |
| `EventHandled` | `Event` | his machine takes the event off its queue (consumed or deferred — even when nothing reacts: "the order came, why didn't he move?") |
| `NodeRuns` | `Node` (the BTree node's VisualId) | that tree node runs |

- A name that is not in the behaviour is a **400 that lists what is** — no silent never-firing breakpoint.
- `filterNetworkId` = only him (and it arms his traces). Without it the step watches every **armed** unit (a Brain tab open, `set_debug_settings` trace, observe-all).
- With a condition: `{"$type":"Compound","Operator":"And","Conditions":[<BehaviourStep>, <PropertyMatch …>]}`.
- `get_breakpoint_status` → `lastHit.what` says what fired: `"entered Withdrawing"`, `"Engaging -> Withdrawing fired on OrderReceived"`, `"event Contact handled (consumed)"`.
- One arrival stops ONCE: `continue_from_breakpoint` does not stop again on the same step.

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
1. `load_scenario_live {name}` → a live run; every node records it by itself (nothing to start).
2. `play` for a while (optionally `checkpoint {label}` at an interesting moment).
3. `enter_replay {atSeconds:30, waitForReady:true}` → the newest exercise (or name one from `list_exercises`),
   entered in ONE request from wherever the cluster is.
4. Read the moment with the ordinary readers: `list_entities`, `get_entity`, `get_entity_brain`.
5. `seek_replay {atSeconds:45}` → jump; `get_replay_status` shows the state and the replay's length.
6. `get_journal_history {networkId}` / `search_replay {condition}` → scan this node's whole recording (ask the
   node that runs the unit's brain).
7. `stop_replay` when done (the cluster goes to Idle).

### G. Inspect AI behavior (BTree/HSM/blueprint)
1. `observe_trace {networkId, on:true}` — **arms** tracing (allocates trace buffers). Without this, traces
   are empty.
2. `play` / `step` a few frames so the buffer fills.
3. `get_entity_trace {networkId}` → active node/state + recent history.
4. `observe_trace {networkId, on:false}` to disarm.

**A unit's debug settings — what the operator sees too (CE-1329).** `get_debug_settings {networkId}` /
`set_debug_settings {networkId, pins?, trace?, follow?, reset?}` read and write the ONE per-unit store the Brain tab's
*Show on map* ticks and the map's right-click *Pin gizmos* use: `pins` (REPLACES the set — Perception, Contacts, EQS,
Utility, Squad, Actions, Command picture, My units' contacts, Path) draw on the map for that unit, selected or not
(selecting a unit draws none of them); `trace` records its BTree / HSM / utility trace; `follow` puts the 3-D camera on
it. Remembered per scenario (back on its next load). `observe_trace` stays a temporary arming that never touches the
remembered `trace`. ⛔ `debug_unit` is gone.

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

### J. Choose the CHEAPEST experiment — before you edit code and restart anything
⛔ **The default failure: change C#, rebuild, reload the scenario, run to the moment again — minutes per try — for a
question a live edit answers in seconds.** Climb this ladder from the top; stop at the first rung that answers the question.
📄 `docs/DESIGN_Entity_Brain_View.md` §12g (BV-65) owns the edit path.

| rung | the question | tools | cost | what it does NOT tell you |
|---|---|---|---|---|
| **0. Read** | *what is it doing, why, with what?* | `get_entity_brain`, `get_entity_journal`, `get_entity_slots` (every slot: task, SOP, paused, hosted children — params + working state decoded), `observe_trace` → `get_entity_trace` | ms | nothing about a different value |
| **1. What-if a VALUE** | *would a different param / variable / trait change the outcome?* *is this value the cause?* | `checkpoint` → write (below) → `step {count}` / `play` + breakpoint → read → `restore_checkpoint`; repeat per value | seconds per try | whether the LOGIC is right — only whether this value matters |
| **2. What-if the STRUCTURE (JSON assets)** | *would another node / transition / wiring fix it?* | `read_asset_graph` → `add_graph_node` / `add_graph_link` / `set_graph_param` / `apply_graph_command` → **`reload_ai_asset`** — hot-applies to the running brain | seconds | ⚠ a **SOFT** reload keeps live instances' state; a **HARD** (topology) reload bumps the generation and RESETS them — re-reach the moment (rung 1's checkpoint first) |
| **3. Change C# in `Hrot.AI.Behaviors`** | the fix is a C# behaviour / node / action in THAT assembly | edit the source → ✅ **`reload_ai_behaviours({})`** (`POST /ai/reload`, CE-1321 — the same path as the toolbar's **Reload AI**): builds the AI Behaviors project in the editor's configuration, COPIES the dll into the directory the watcher watches, waits for the swap (`AiHotReloadCoordinator`: a fresh collectible ALC, every registrar re-run, the registry merged in place), the catalog re-projects the C#-built graphs and open documents are re-decompiled (`AiDocumentManager.ReconcileFromCatalog`). It answers `{build, errors[], reloaded, added[], removed[], documentsReconciled}` — a failed build is `build:failed` with the compiler's lines, the running assembly unchanged | a build (~tens of s), no restart | ⚠ running units KEEP their task and slot memory across the swap — a changed params / state LAYOUT is yours to restart (assign again); ⛔ C# outside `Hrot.AI.Behaviors` is rung 4 |
| **4. Change C# anywhere else** | engine / toolkit / system code (`Fdp.Toolkits`, `Hrot.*` outside the AI Behaviors assembly) | edit → build → restart the editor → reload the scenario → re-reach the moment | minutes | — the hot-reload watcher covers ONE assembly |

**Rung 1's writes**
- **a blueprint variable:** `stage_entity_variable {networkId, path, value}` — staged, lands on the next advancing tick (✅ built).
- **a trait / setting / any component field:** `edit_component {networkId, componentType, patch}` or `patch_attribute` (✅ built). For
  `CombatTemperament` also set its `Set` bit, or the reader keeps the type's value.
- ✅ **a behaviour's PARAMS or a BTree / HSM / C# behaviour's WORKING STATE, any slot (task, SOP, paused task, hosted child, attached
  blueprint)** *(CE-1320)*: `stage_entity_variable {networkId, owner:"task"|"sop"|"paused"|"blueprint", behaviour?, part?, role:"params"|"variables",
  path, value}` — `path` is dotted, `[n]` indexes an inline array (`Shots.Items[1]`); discover owners, behaviours and parts (node labels)
  with `get_entity_slots`. Or `paramsJson` instead of `path`/`value`: a whole parameter set through the behaviour's own parser. It lands
  IN PLACE on the next advancing tick, keeps the start record true, and NEVER restarts the behaviour (`restarted:false` in the reply).
  ⚠ `order_entity {kind:'behavior', params}` RESTARTS the behaviour — use it only when a restart is the experiment.

**Use rung 1 for:** sweeping a threshold / radius / timing (two or three values from one checkpoint) · confirming a bug hypothesis before
touching code (*"force `Phase = Assault` — does the squad move? then the bug is whatever should have set it"*) · reaching a node path
without staging the whole situation.
⚠ **A forced value can create a state the behaviour never reaches on its own** — a rung-1 result is a HINT to confirm (a rail, or the
real situation), never proof. ⭐ A value that should STAY changed belongs in the order's / asset's defaults or the unit's settings — not
in a live edit (it lasts until the behaviour is replaced).

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
- **`cancel_cluster_operation`** — The operator's cancel (R-4004): cancel a cluster request in flight by its transactionId; the cluster returns to Idle. Req `targetId` (string). Returns { published:true, targetId }
  Notes: ASYNCHRONOUS: confirms the cancel was published; read get_status for the cluster returning to Idle.; Any node with an orchestration bus can publish it; a host without one answers 503..
  Example: `cancel_cluster_operation({"targetId":"00000000-0000-0000-0000-000000000000"})` — cancel a cluster load in flight.
- **`get_cluster_roster`** — Every node the cluster master holds, as the MASTER sees it: id, subsystem, its own reported state, roles, heartbeat age. No params. Returns { count, nodes[{ nodeId, subsystem, reportedState, roles, secondsSinceHeartbeat }], note }
  Notes: reportedState is each node's OWN claim from its heartbeat, not the cluster's state — read get_status for that.; A node leaves the list after the heartbeat timeout (default 5 s) of silence.; Only the node hosting the ClusterMaster can answer (the orchestrator process, or the editor's in-process core); elsewhere it answers 503 — check the 'cluster.roster' cell in get_capabilities..
  Example: `get_cluster_roster({})` — list the nodes in the cluster.
- **`request_cluster_transition`** — Ask the cluster master to move the whole cluster to a target state — the same TransitionStateIntent the operator panel publishes. Req `target` (string), `scenario?` (string), `exerciseId?` (string), `timeMode?` (string), `targetWallTicks?` (number). Returns { published:true, transactionId, target, note }
  Notes: ASYNCHRONOUS: the answer confirms the intent was PUBLISHED, not that the cluster reached the state — poll get_status or get_cluster_roster.; Pass the returned transactionId to cancel_cluster_operation to cancel it; a cancel returns the cluster to Idle (R-4004).; Any node with an orchestration bus can publish it; a host without one answers 503..
  Example: `request_cluster_transition({"target":"Idle"})` — return the cluster to Idle.
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
  Notes: Non-finite floats appear as string sentinels "NaN"/"Infinity"/"-Infinity" — valid JSON, not a bug.; SHAPE: { EntityId, NetworkId, Components:{ ... } } — Components is PascalCase, and so is every component and field name inside it (SimTransform.Position, NavigationIntent.Mode). Indexing a lowercase 'components' silently yields nothing and reads like an empty entity.; TO DIAGNOSE 'the sim ignores my order', COMPARE THE INTENT COMPONENT WITH ITS STATUS COMPONENT. The pair distinguishes three different bugs that look identical from the UI: intent empty => nothing issued the order; intent set + status ABSENT => the consumer never ran; intent set + status PRESENT + zero velocity => the consumer ran and produced no motion. Worked example: NavigationIntent{Mode,TargetSpeed} against NavigationStatus.; AUTHORITY FIRST: NetworkAuthority carry HasAuthority, PrimaryOwnerId and LocalNodeId. On a cluster a write to an entity this node does not own is legitimately dropped, so check HasAuthority before filing 'the write did nothing'.; MEASURE MOTION AS A POSITION DELTA OVER A simTime DELTA, never over wall-clock. Sample get_status.simTime alongside each dump: BIT-IDENTICAL positions across a real simTime advance is the hard evidence; the same reading across a stalled clock proves nothing.; ON --mode all THIS READS ONE NODE -- THE ACTIVE PERSPECTIVE -- AND THE NODES DISAGREE. Brain (CGF/Scenario) and muscle (SimHost) hold separate copies of the same entity, and a defect can live entirely in the gap between them. Measured 2026-08-28: entity 1001 held Class:Tank, AccelGain:1.8 on CGF and PersonalCar, AccelGain:0 on SimHost, because the scenario's authored VehicleParams reached the brain intact and was dropped on the wire hop. The brain computed a valid path (which rendered) while the muscle could not accelerate -- on screen, indistinguishable from a broken navigator. SO: read the entity on BOTH nodes before concluding anything about 'the cluster'.; AND ?perspective= DOES NOT WORK: it is not implemented on any route and is IGNORED (you get a hint saying so since CE-112). Switch with POST /perspective {name:...} and then read; confirm with get_status.perspective. Passing ?perspective=ExCon -- a subsystem with NO WORLD AT ALL -- used to return a full component dump, which is how the ignored key was caught..
  Example: `get_entity({"networkId":1000})` — get full component dump for entity 1000.
- **`get_entity_ownership`** — Per-descriptor network ownership of one entity (NED nodes only). Req `networkId` (number). Returns { networkId, primaryOwnerId, localNodeId, descriptors:[{ descriptorTypeId, isMaster, ownedByThisNode, ownerNodeId, components:[..] }] }.
  Notes: Ownership is DESCRIPTOR-level (a NED network concept), not component-level: each descriptorTypeId binds a set of components, and one component may belong to several descriptors. isMaster marks the EntityMaster descriptor whose owner is the entity's primary/save owner.; ownedByThisNode is the authority fact this node acts on; ownerNodeId is the explicit per-descriptor override when one exists (null ⇒ follows the primary owner).; VERIFICATION SURFACE for transfer_entity_ownership: read ownership, POST the transfer, read again.; PERSPECTIVE-SCOPED on --mode all: reports the ACTIVE perspective node's view. Read on both the giving and receiving node to confirm a transfer landed..
  Example: `get_entity_ownership({"networkId":1000})` — read which node owns each of an entity's descriptors.
- **`transfer_entity_ownership`** — Hand an entity (or a subset of its descriptors) to another node (NED nodes only). Req `networkId` (number), Req `newOwnerNodeId` (integer), `scope?` (string, "MasterOnly"|"AllOwnedByThisNode"|"SpecificDescriptors", def "AllOwnedByThisNode"), `descriptors?` (array). Returns { requested:true, networkId, newOwnerNodeId, scope, note } — fire-and-forget; verify with GET .../ownership.
  Notes: DESCRIPTOR-level and NED-initiated (ruling 2026-09-15): 'descriptors' are network descriptor type ids (get them from get_entity_ownership), never ECS component types.; Only descriptors THIS node owns move — so on a native cluster AllOwnedByThisNode hands off the brain's descriptors but leaves the muscle-owned world-position descriptor where it is.; Save ownership (primaryOwnerId) moves IFF EntityMaster is in the set (MasterOnly and AllOwnedByThisNode include it; a SpecificDescriptors set that omits master leaves it here).; The old owner stops publishing without disposing (entity never blinks out); the new owner confirms by re-publishing EntityMaster. Verify with get_entity_ownership on the new owner..
  Example: `transfer_entity_ownership({"networkId":1000,"newOwnerNodeId":2,"scope":"AllOwnedByThisNode"})` — hand entity 1000 to node 2.
- **`create_entity_request`** — Create an entity THROUGH the request path (routing + auto-takeover grant). Req `tkbType` (integer), `ownerNodeId?` (integer, def 0), `transform?` (object), `attributesJson?` (string). Returns { networkId, ... } for the created entity, or a 400 on a malformed body.
  Notes: CE-271 seam ⑤ / CE-515 ③: the create-request pipeline (routing + ownership grants). POST /entities/spawn now uses the same pipeline; the only difference is ownerNodeId:0 — here 'forward to the arbiter', there 'this node'..
  Example: `create_entity_request({"tkbType":123,"ownerNodeId":1})` — create an entity through the create-request pipeline, owned by node 1.
- **`list_scenarios`** — List available scenarios by relative path. No params. Returns Available scenario names (relative paths) for use with load_scenario_edit / load_scenario_live.
  Example: `list_scenarios({})` — discover loadable scenario names.

### Group E — Scenario
- **`load_scenario_edit`** — Load a scenario for AUTHORING (Edit state), cluster-wide. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor, stoppedRun (CE-1025: "a live run" / "a dry run" / null).
  Notes: ⭐ CE-1025 — a running live run or dry run is stopped first, with no prompt (HTTP is non-interactive): the dry run's restore point is FORGOTTEN, not rewound to; the answer's stoppedRun names the run ("a live run" / "a dry run") or is null.; Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).; Edit state freezes sim time — nothing ticks until enter_preview or play.; CE-102 (2026-08-28) gave CGF the shared edit-load handler, so an edit load is NO LONGER partial on that node. This note previously said CGF had none -- that is now stale. Still prefer load_scenario_live for a real run, and verify either load by reading state.; AND THAT PARTIAL LOAD STILL ANSWERS ok:true — this is the single most misleading response in the API. Measured on --mode all from the Scenario perspective: ok:true with scenario:NULL, entityCount:0, an empty list_entities, and a gizmo frame of 603 primitives that were ALL grid lines. Every field says 'empty world' and the envelope says 'success'.; SO VERIFY A LOAD, NEVER TRUST IT: after any load read get_status.entityCount AND list_entities AND (if the map matters) the non-Line shape count from get_gizmo_frame. Three independent reads, because the envelope is not one of them. On the same host load_scenario_live gave 8 entities and Box2D 8 / Arrow 12 / Text 8..
  Example: `load_scenario_edit({"name":"test-move","waitForReady":true})` — load test-move for authoring and wait for ready.
- **`load_scenario_live`** — Load a scenario for RUNNING (Live state), cluster-wide, on any host. Req `name` (string), `waitForReady?` (boolean, def false). Returns ok:true envelope with loaded, target, entityCount, sawWorldChange, hadWorldAnchor, stoppedRun (CE-1025: "a live run" / "a dry run" / null).
  Notes: ⭐ CE-1025 — a running live run or dry run is stopped first, with no prompt (HTTP is non-interactive): the dry run's restore point is FORGOTTEN, not rewound to; the answer's stoppedRun names the run ("a live run" / "a dry run") or is null.; Set waitForReady:true to block until the cluster reaches OperatingLive (recommended).; Every host has live-load handlers, so this is the mode that loads on ALL nodes — use it when the world must be the same everywhere.; A live load starts a new exercise run (a fresh ExerciseId), which is what recording and replay key off..
  Example: `load_scenario_live({"name":"test-move","waitForReady":true})` — load test-move live across the cluster and wait for ready.
- **`save_scenario`** — Save the current authored world as a scenario. Req `name` (string). Returns ok:true envelope.
  Example: `save_scenario({"name":"my-scenario"})` — save current world as my-scenario.

### Group G — Breakpoints
- **`list_breakpoints`** — List all registered breakpoints. No params. Returns [{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]
  Example: `list_breakpoints({})` — list all active breakpoints and their hit counts.
- **`set_breakpoint`** — Register a run-until-condition breakpoint. Req `condition` (object), `filterNetworkId?` (number), `occurrenceThreshold?` (number, def 1), `name?` (string). Returns { breakpointId, conditionSummary } (e.g. "BP#1", "Withdrawing entered (PlatoonCommander)").
  Notes: condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).; Poll get_breakpoint_status after play to detect when the breakpoint fires.; ⭐ CE-1323 — BEHAVIOUR BREAKPOINTS: {"$type":"BehaviourStep","Behaviour":"<running behaviour name>","Kind":K,...} with K = StateEnter|StateExit {State} · InsideEnter {State, Region?: -1 = anything under it, n = region n of a parallel state} · TransitionFires {From?, To, Event?} (no From = a global transition) · EventHandled {Event} (dequeued, consumed or deferred, even when no transition uses it) · NodeRuns {Node: the BTree node's VisualId}. Names are the machine's own (GET /entities/{id}/brain names the running behaviour); a name not in it is a 400 that lists what is.; Per unit: filterNetworkId. Any other condition: {"$type":"Compound","Operator":"And","Conditions":[<BehaviourStep>, <PropertyMatch …>]} — the rest is checked on the unit at the step. A step scoped to a unit arms its traces; an unscoped step sees only armed units (a Brain tab open, Debug this unit, observe-all).; Only records written since the last look count, so continue_from_breakpoint never stops twice on one arrival. get_breakpoint_status names what fired (lastHit.what)..
  Example: `set_breakpoint({"condition":{"$type":"PropertyMatch","ComponentType":"SimTransform","PropertyPath":"Position.X","Operator":"GreaterThan","Predicate":{"$type":"Numeric","MinValue":100,"MaxValue":1000000000}},"name":"moved-east"})` — pause when entity SimTransform.Position.X > 100.
- **`remove_breakpoint`** — Remove a breakpoint by its ID string. Req `id` (string). Returns ok:true envelope.
  Example: `remove_breakpoint({"id":"BP#1"})` — remove breakpoint BP#1.
- **`continue_from_breakpoint`** — Resume the debugger after a breakpoint hit. Also what applies any live variable writes staged while it was stopped. `step?` (boolean). Returns { wasPaused, action, isPaused, note }
  Notes: ⚠ Deleting a breakpoint does NOT resume: the debugger stays stopped, and while it is stopped every staged variable write is queued and never applied. Call this after a hit, not remove_breakpoint.; Harmless when nothing is stopped — it answers wasPaused:false.; The host also serves POST /breakpoints/step, which is exactly this call with step:true. Deliberately ONE tool, not two — use continue_from_breakpoint({step:true})..
  Example: `continue_from_breakpoint({})` — let the world run again after a breakpoint fired.
- **`get_breakpoint_status`** — Current pause state and last breakpoint hit. No params. Returns { isPaused, pausedTick, lastHit: { breakpointId, networkId, name, conditionSummary, what, journalWrittenByTheTick?:[{seq, at, kind, text}] } | null }  — what (CE-1323) is what a behaviour step saw: "entered Withdrawing", "Engaging -> Withdrawing fired on OrderReceived", "event Contact handled (consumed)"; null for a data breakpoint
  Notes: Poll this after play to detect when a breakpoint fires..
  Example: `get_breakpoint_status({})` — poll for breakpoint hit after calling play.
- **`break_on_journal`** — Stop the simulation when THIS unit changes its mind (CE-1258): a data breakpoint on its brain journal's sequence number, filtered to the unit (a neighbour's change does not stop it); optionally only for some entry kinds. Req `networkId` (number), `kinds?` (array). Returns { breakpointId, networkId } — then poll get_breakpoint_status: its lastHit.journalWrittenByTheTick lists the entries the hit tick wrote.
  Notes: A stop shows the world at the START of the tick that changed it (the cause still in place); continue_from_breakpoint({step:true}) runs that tick.; It keeps firing on every later change until removed with remove_breakpoint.; Editor only, like every breakpoint..
  Example: `break_on_journal({"networkId":1000})` — pause the next time unit 1000's brain changes.

### Group H — Checkpoint / diff
- **`checkpoint`** — Take a named checkpoint of the LIVE run — a keyframe of every node's live recording, through the cluster master (R-3044). `label?` (string). Returns { published, requestId, label }
  Notes: A checkpoint is part of the live recording (it survives into the replay), not a preview snapshot.; restore_checkpoint continues the live run from it..
  Example: `checkpoint({"label":"before-assault"})` — take a checkpoint before an experiment.
- **`restore_checkpoint`** — Continue the live run from a named checkpoint, through the cluster master. Req `label` (string). Returns { published, requestId, label }
  Example: `restore_checkpoint({"label":"before-assault"})` — revert all changes since the last checkpoint.
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
- **`enter_preview`** — Enter the cluster's dry run (OperatingPreview) from Edit, through the cluster master on every host (R-3052). Never recorded (R-3053). `startPaused?` (boolean), `waitForReady?` (boolean). Returns { published, transactionId, target } (+ awaited, reached with waitForReady).
  Notes: stop_preview rewinds every node to the moment the dry run began.; A dry run is never recorded: only a live run records (R-3053)..
  Example: `enter_preview({"startPaused":true,"waitForReady":true})` — enter preview paused for deterministic step-based control.
- **`stop_preview`** — Leave the dry run back to Edit through the cluster master; every node rewinds to the moment it began. `waitForReady?` (boolean). Returns { published, transactionId, target } (+ awaited, reached with waitForReady).
  Notes: Rewinds all changes made during the dry run..
  Example: `stop_preview({"waitForReady":true})` — exit preview and revert all changes since entering preview.
- **`pause`** — Pause the simulation. Time freezes; commands queue until step/play. No params. Returns ok:true envelope.
  Notes: Commands and spawns while paused are queued and take effect on the next step/play.; THE ACK MEANS APPLIED as of CE-104: the route waits until isPaused is actually true before returning, so the next read is safe. Measured before the fix on --mode all: ok:true while /status still read isPaused:false, with the clock running a further ~0.2s — because a cluster-wide pause is a FUTURE BARRIER so every node stops at the same simTime.; A 504 from this route means the pause barrier is stuck — a node never reached it. Check get_logs({level:"Warning"})..
  Example: `pause({})` — pause the running simulation.
- **`play`** — Enter preview and/or resume if paused. Time advances after this. No params. Returns ok:true envelope.
  Notes: Time advances after play (until pause or a breakpoint fires)..
  Example: `play({})` — start or resume simulation.
- **`get_sim_state`** — Current sim state: isPaused, inPreview, totalTime, timeScale. No params. Returns { isPaused, inPreview, totalTime, timeScale }
  Notes: Check this before driving — most mistakes are run-state mistakes..
  Example: `get_sim_state({})` — check current paused/preview/time state.
- **`step`** — Advance simulation by N discrete steps. Only meaningful in preview. `count?` (number, def 1), `dt?` (number). Returns ok:true envelope.
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
  Notes: Set wait:true to attempt correlated-ack wait — only effective while time advances, else awaited:false.; awaited:false is NOT an error — it means time was not advancing.; CE-3168: a payload key that names no field of the event is a 400 (it used to be dropped silently, and the event published with defaults).; To ORDER a unit use order_entity — it addresses units by network id and reaches the unit's brain node from any node..
  Example: `send_entity_command({"eventType":"CenterOnEntityCommand","payload":{"networkId":1000},"wait":false})` — publish CenterOnEntityCommand event.
- **`spawn_entity`** — Spawn an entity from a TKB type. Req `tkbType` (number), `transform?` (object), `components?` (array), `attributesJson?` (string), `ownerNodeId?` (number), `reliable?` (boolean), `reliableTimeoutSeconds?` (number). Returns { spawned, tkbType, reliable, requestId, ownerNodeId, awaited, reason }. Created through the node's creation pack (request → spawn + ownership grants) on the next ticks — step to realize it.
  Notes: Spawn is queued and processed on the next tick — call step to realize it.; CE-515 ③: goes through the node's EntityCreationPack (RequestEntityCreation), like every other author — the creator owns the entity and grants each role's group (Brain/MuscleGround/Perception) to a node serving that role. It no longer publishes a raw SpawnEntityCommand that nobody owned.; Use list_entity_types to discover valid tkbType values.; CE-292 — reliable:true engages the cross-node construction barrier: the creator holds the entity Constructing until the capability-filtered peers (advertising fdp.reliable-init) report Active, or reliableTimeoutSeconds aborts it via an EntityMaster dispose. On the wire this shows as an EntityMaster carrying the WaitForAcks flag + EntityLifecycleStatusDescriptor samples (sniff with ddsmonitor)..
  Example: `spawn_entity({"tkbType":1101,"transform":{"position":{"x":100,"y":0,"z":50},"rotation":{"x":0,"y":0,"z":0,"w":1}}})` — spawn entity type 1101 at position (100,0,50).

### Group I — Replay
- **`get_journal_history`** — EVERY brain-journal entry of one unit across this node's replay recording (or a frame range), each once, with the frame it first appears at (CE-1265) — the full why-history of a replayed exercise. Req `networkId` (number), `from?` (number), `to?` (number). Returns { networkId, from, to, found, gap, count, entries:[{seq, at, kind, text, frame}] }
  Notes: Ask the node that runs the unit's brain (its journal is in that node's recording).; It reads the recording file frame by frame, read-only: the replayed world is not touched; a long file takes a moment.; gap:true means entries were lost between two recorded frames (should not happen: every change dirties the unit's record)..
  Example: `get_journal_history({"networkId":1000})` — list every decision unit 1000 made in the exercise, and why.
- **`list_exercises`** — The exercises the cluster master can replay (its inventory) — every live run is one, recorded on every node (R-3053). No params. Returns { count, exercises:[{exerciseId, scenario, startUtc, durationSeconds}] } newest first.
  Notes: Every host answers the same: each node keeps the master's inventory as it arrives over the cluster bus..
  Example: `list_exercises({})` — what can be replayed.
- **`enter_replay`** — Replay an exercise on the whole cluster, from ANY state, in ONE request — the cluster master plans the way (unload, load, seek) (R-3055). `exerciseId?` (string), `atSeconds?` (number), `atSimSeconds?` (number), `waitForReady?` (boolean), `path?` (string), `paths?` (array). Returns { published, transactionId, target, exerciseId, atSeconds } (+ awaited, reached with waitForReady).
  Notes: With no exerciseId the cluster master picks: the live run in progress, else the newest finished one — the same on every host.; Every node replays its OWN recording; the ordinary readers (list_entities, get_entity, get_entity_brain, get_entity_journal, get_selection) then read the replayed world.; play / pause / step drive the replay clock; seek_replay jumps.; The Replay Browser (-m replaybrowser, standalone) answers the same: its exercises are the recordings under the staging root; path / paths open any file or node group. A cluster host refuses a path..
  Example: `enter_replay({"atSeconds":30,"waitForReady":true})` — replay the last exercise from 30 s in.
- **`set_replay_axis`** — ⭐ CE-3329 — switch the replay's time axis for the whole session (R-3058): Sim (the run's own clock — a play jumps over every paused stretch and its changes land at once) or Wall (pauses play out). No seek: the frame on screen stays. Req `axis` (string). Returns { published, requestId, axis }
  Notes: The master holds it and every node hears it (the cluster-state broadcast); the Replay Browser keeps its own (the viewer's).; get_replay_status reports it as axis; atSimSeconds seeks work on either axis..
  Example: `set_replay_axis({"axis":"Wall"})` — skip or play the pauses.
- **`export_replay_json`** — Replay Browser only (RB-4): export the shown node's recording to JSON, every option the Timeline panel has, as a job off the main thread. `window?` (string), `from?` (number), `to?` (number), `format?` (string), `entities?` (array), `entityIndex?` (integer), `includeEntities?` (boolean), `includeEvents?` (boolean), `minified?` (boolean), `epsilon?` (number), `output?` (string). Returns { job, state:'running', input, path } — poll get_replay_export
  Notes: The file is what the panel's 'Save to JSON' writes for the same options..
  Example: `export_replay_json({"window":"frames","from":0,"to":300,"format":"changelog"})` — dump the first 300 frames' changes to a JSON file.
- **`get_replay_export`** — Replay Browser only (RB-4): an export job's state, file and size. Req `job` (string). Returns { job, state: running | done | failed, input, path, bytes, error }
  Example: `get_replay_export({"job":"a1b2c3d4e5f6"})` — is the export finished, and where is the file.
- **`step_replay_history`** — Replay Browser only (RB-3): back / forward through the playback waypoints — the Timeline's back and forward buttons (every seek leaves one). Req `direction` (string). Returns the Replay Browser's status
  Example: `step_replay_history({"direction":"back"})` — go back to where the replay was before the last jump.
- **`set_replay_loop`** — ⭐ CE-3329 §12n — loop the replay (or review) between two moments: when the playhead reaches the end the master's replay clock jumps back to the start, on either axis. A SESSION setting held by the master and drawn by every host; {} clears it. `fromSeconds?` (number), `toSeconds?` (number), `fromSimSeconds?` (number), `toSimSeconds?` (number). Returns { published, requestId, cleared }
  Notes: Valid only 0 <= from < to <= the replay's length; otherwise the master refuses it (the previous loop stays). Cleared when the cluster leaves the replay / review.; Enforced only while the clock runs; a seek outside the loop is honoured. get_replay_status reports it as loop {fromSeconds,toSeconds} on the active axis (null = none). The Replay Browser keeps its own loop (the viewer's)..
  Example: `set_replay_loop({"fromSeconds":2,"toSeconds":5})` — loop a stretch of the replay.
- **`search_replay`** — Find the frames of this node's replay recording where a condition holds (CE-1266) — the same condition shape breakpoints take (component field match, event, lifecycle, spatial, compound). `condition?` (object), `lifecycle?` (object), `networkId?` (number), `limit?` (number). Returns { count, truncated, hits:[{frame, networkId, context}] }
  Notes: Changed compares each frame with the frame before it (a unit's first frame counts as changed) — e.g. every frame a unit's ActiveBehaviorHash switched.; ComponentType takes the component's name or its full name; an unknown name is an error, never an empty result.; Then seek_replay to a hit to read the moment with the ordinary readers..
  Example: `search_replay({"condition":{"$type":"PropertyMatch","ComponentType":"Fdp.Toolkit.Behavior.Components.BehaviorState","PropertyPath":"ActiveBehaviorHash","Operator":"Changed","Predicate":{"$type":"Numeric"}},"networkId":1000,"limit":10})` — find when something happened in the recording.
- **`seek_replay`** — Seek the replay to a moment — the operator timeline's seek round: every node restores its own recording there and the clocks snap. The moment is atSeconds, or a lookup over THIS node's recording: a frame, a step of frames, the frame after an event (cause), or the next / previous change of a unit (RB-2). `atSeconds?` (number), `atSimSeconds?` (number), `frame?` (integer), `frames?` (integer), `cause?` (object), `next?` (object), `direction?` (integer). Returns { published, requestId, atSeconds } (+ frame, resolvedFrom for a lookup; next: found, or {found:false, note})
  Notes: The cluster pauses for the seek; read get_replay_status, then the ordinary readers.; A lookup reads this node's own recording; next scans it off the main thread in a reader of its own (the replayed world is not touched) — a long file takes a moment..
  Example: `seek_replay({"atSeconds":45})` — jump the replay to 45 s.
- **`get_replay_status`** — The cluster's state, the loaded replay's length, this node's replay recording, and its clock. No params. Returns { clusterState, replaying, durationSeconds, recording, simSeconds, axis, loop, sim, browser } — axis: the session's time axis (Sim | Wall, CE-3329) — loop: {fromSeconds,toSeconds} on the active axis, or null (CE-3329 §12n) — simSeconds: the RECORDED sim time of the frame on screen (the live run's own clock; sim.totalTime is the replay clock, wall seconds after the zero); browser (Replay Browser only): {exerciseId, frame, frames, atSeconds, spanSeconds, primaryNode, view, playing, rate, nodes[], history, selectionHistory}
  Example: `get_replay_status({})` — is the cluster replaying, and where.
- **`stop_replay`** — Leave the replay: the cluster goes to Idle. `waitForReady?` (boolean). Returns { published, transactionId, target } (+ awaited, reached with waitForReady).
  Example: `stop_replay({"waitForReady":true})` — end the replay.
- **`set_replay_view`** — Replay Browser only (RB-3): show one node's replayed world or the merged world of all nodes — the Federation panel's switch. Req `view` (string). Returns the Replay Browser's status (get_replay_status's browser field)
  Notes: The ordinary readers then read that world. Every other host answers NOT_SUPPORTED_HERE(replay.browser)..
  Example: `set_replay_view({"view":"merged"})` — look at every node's units together.
- **`enter_review`** — ⭐ CE-3329 — During Action Review: pause the LIVE run and review its own recording in place — every node keeps its exact live point (a full checkpoint in RAM, a labelled keyframe review-<n> in its recording), the past is shown on the wire, and POST /review/exit returns to exactly where it paused. Only from OperatingLive. `atSeconds?` (number), `atSimSeconds?` (number), `waitForReady?` (boolean). Returns { published, transactionId, target } (+ awaited, reached with waitForReady).
  Notes: In a review: seek with POST /replay/seek (or /review/seek), play / pause / step with /sim/*, the axis with /replay/axis; it pauses itself at the live point.; World-changing commands answer 409 until you return (spawn, orders, components, save, checkpoints …)..
  Example: `enter_review({"atSimSeconds":30,"waitForReady":true})` — look back at the live run.
- **`exit_review`** — ⭐ CE-3329 — leave the review: every node puts its live point back (the checkpoint restore's semantics), the clock returns to it, the recording resumes — live, paused. `waitForReady?` (boolean). Returns { published, transactionId, target } (+ awaited, reached with waitForReady).
  Example: `exit_review({"waitForReady":true})` — back to the live run.
- **`seek_review`** — ⭐ CE-3329 — the same seek as POST /replay/seek, named for a review (atSeconds, atSimSeconds, frame, frames, cause, next). `atSeconds?` (number), `atSimSeconds?` (number). Returns as seek_replay
  Example: `seek_review({"atSimSeconds":12})` — move within the review.
- **`step_selection_history`** — Replay Browser only (RB-3): back / forward through the units selected. Req `direction` (string). Returns the Replay Browser's status
  Example: `step_selection_history({"direction":"back"})` — select the unit selected before.
- **`get_timeline_events`** — One timeline lane's events in a frame range, optionally only one unit's (RB-6) — what the index already holds, no scan of the file. Req `lane` (string), `from?` (integer), `to?` (integer), `entity?` (number), `limit?` (integer). Returns { lane, count, truncated, events:[{frame, subject, other, x, y, value}] }
  Notes: seek_replay({next:{lane}}) jumps to the next event of a lane..
  Example: `get_timeline_events({"lane":"shots","limit":20})` — list a lane's events.
- **`get_timeline_lanes`** — The Event Timeline's lanes on this host (RB-6): each lane with its event count in the shown recording's index, and the index's progress (it builds in the background on first open). No params. Returns { recording, progress, complete, error, frames, lanes:[{id, name, group, priority, glyph, indexed, count, error}] }
  Notes: Only a host that keeps a timeline index answers (the Replay Browser, the editor and every ECS cluster node — live); others: NOT_SUPPORTED_HERE(timeline.read)..
  Example: `get_timeline_lanes({})` — what the timeline shows, and how far its index is.
- **`add_timeline_lane`** — Add a user's lane to the timeline (RB-6): a descriptor and the condition that makes an event — the lane catalogue's Register; the index scans it in the background. Req `Lane` (object), Req `When` (object), `Payload?` (object). Returns { registered, note }
  Example: `add_timeline_lane({"Lane":{"Id":"dmg","DisplayName":"Damage"},"When":{"$type":"PropertyMatch","ComponentType":"Health","PropertyPath":"Current","Operator":"Changed","Predicate":{"$type":"Numeric"}}})` — add a lane that marks every health change.

### Group K — AI behavior traces
- **`get_behaviour_decisions`** — Which node / state / transition of a behaviour binds which utility decision, and how (CE-1325): ChooseOption chooses, RankCandidates ranks, an IsOption guard reads one option, a custom action holds one in its params. Req `name` (string). Returns { behaviour, bindings:[{kind: node|state|transition, element, label, role: Chooses|Ranks|Reads|Holds, via (the params field), decision, assetId, decisionId, option?}] }
  Notes: Read from the running blob: a node's method key carries its params offset, and the behaviour's default params hold the decision reference there..
  Example: `get_behaviour_decisions({"name":"CombatPosture"})` — which nodes of a behaviour run which utility decision.
- **`get_combat_detonations`** — The last warhead bursts on this node (grenades, mortars, HE) — the warhead and where its numbers came from, every entity in reach with its stance, fragment exposure and blast barrier/shadow, the damage each effect did, and the doors breached (buildings Stage 6). `last?` (number). Returns { count, returned, detonations:[{seq, tick, shooter, struck, burst, ammo, warhead, warheadSource, fragmentRadius, blastInjuryRadius, effects:[{entity, entityIndex, stance, bodyPoints, distance, fragmentExposure, fragmentFalloff, fragmentArmourChance, fragmentDamage, blastFalloff, blastBarrier, blastShadow, blastDamage, totalDamage, shieldedBy, at}], doorsBreached:[key], rays:[{to, transmission, stopAt}]}] } — newest first
  Notes: Written by AreaEffectSystem as it decides each burst — never recomputed. Only bursts of a munition WITH a warhead are recorded (a rifle round has none).; fragmentExposure = the mean over the target's body points (its stance) of the fragments through the terrain and past any body or vehicle; blastBarrier = what slabs/walls/shut doors let through; blastShadow = the diffraction factor behind an obstacle taller than the target.; The entity the round struck is listed too (it is at the burst); its direct hit is a separate damage event.; rays = every fragment line the map's blast layer draws (CE-3117): the body point, the fragment transmission 0..1, and the first obstacle on the way (a terrain piece where the fragments enter it, or the point nearest a blocking collider), null for a clear line..
  Example: `get_combat_detonations({"last":3})` — find out why the prone soldier behind the wall survived the grenade.
- **`get_combat_shots`** — The last rounds fired on this node — the inputs each was fired with (penetration and its provenance, AQ85 sigma/deflection) and why it ended (hit, stopped by a wall, expired), with every terrain crossing (tuning T-4). `last?` (number), `shooter?` (number), `target?` (number). Returns { count, returned, shots:[{seq, tick, endTick, shooter, target, weaponIndex, muzzle, aim, ordinal, sigmaRad, deflectionRad, penetration, penetrationSource, damage, outcome:InFlight|Hit|StoppedByTerrain|Expired, end, hit, arrivingDamage, arrivingPenetration, raisedAt, firedAt, tripSeconds, caughtUpSeconds, crossings:[{kind, label, material, resistanceMmRha, roundPenetrationMm, chance, passed, at}]}] } — newest first; raisedAt/tripSeconds (CE-2180): the brain's synced sim time and the trip to this node (null when not stamped); caughtUpSeconds: how far a bomb was flown forward to start from its release
  Notes: Written by the systems that decide each round (FireProcessing, Ballistics, HitResolution) — never recomputed, so it cannot disagree with what happened.; Served where the fire chain runs (SimHost, Editor); elsewhere it says no round was fired on this node.; A round that hit a unit lists only the crossings before the unit; arrivingDamage is what the damage step received..
  Example: `get_combat_shots({"last":10})` — find out why a rifleman's rounds never hurt the target behind the wall.
- **`get_entity_brain`** — What a unit is doing and why — the Brain tab as JSON (CE-1248): task, who ordered it, paused task, SOP, ROE, each channel's right-now, HSM regions, squad play, utility choices (every brain unit), contacts, and the brain journal's recent changes. Req `networkId` (number). Returns { networkId, hasBrain, onBrainNode, doing, origin, urgency, sinceSeconds, paused, sop, roe, rightNow:[{channel, actionId, reason, hold, progress, needed}], regions:[{region, state, stateIndex}], squad, choices:[{decisionId, winner, previousWinner, margin, switches}], contacts, heardOnly, journal:{on, seq, changes:[{seq, at, kind, text}]} }
  Notes: onBrainNode:false means this node does not run the unit's brain — only task / SOP / ROE are replicated here.; choices fill only while the unit is observed (observe_trace, set_debug_settings trace, or its Brain tab open); the journal is always on.; journal.changes are newest first; poll get_entity_journal with ?since=<seq> for only the new ones..
  Example: `get_entity_brain({"networkId":1000})` — see what unit 1000 is doing, who ordered it, and what changed lately.
- **`get_debug_settings`** — A unit's debug settings (CE-1329) — the ONE store the Brain tab's Show on map ticks and the map's right-click Pin gizmos / AI trace items use: the gizmo families pinned on it (they draw for it, selected or not; selection draws none), its trace (the user's switch; traceOn also counts a Brain tab showing it and an observe / breakpoint arming), and whether the 3-D camera follows it. Remembered per scenario. Req `networkId` (number). Returns { networkId, pins:[family labels], trace, traceOn, follow }
  Example: `get_debug_settings({"networkId":1000})` — what is switched on for unit 1000.
- **`set_debug_settings`** — Sets a unit's debug settings (CE-1329) — exactly what the Brain tab's ticks and the right-click menu set, and the operator sees it: pins (REPLACES the pinned gizmo families), trace (records the unit's BTree / HSM rings and utility trace), follow (the 3-D camera follows it), reset (forgets what is remembered for it first). Only what is given changes. Remembered per scenario (restored on its next load). ⛔ Replaces debug_unit. Req `networkId` (number), `pins?` (array), `trace?` (boolean), `follow?` (boolean), `reset?` (boolean). Returns { networkId, pins:[family labels], trace, traceOn, follow }
  Notes: Then read get_entity_brain (choices fill), get_entity_trace, and arm break_on_journal.; A unit whose Brain tab is open is traced anyway (traceOn), without touching its trace switch..
  Example: `set_debug_settings({"networkId":1000,"pins":["Perception"],"trace":true})` — watch unit 1000 closely: its vision cone on the map and its trace.
- **`get_entity_journal`** — A unit's brain journal after a sequence number (CE-1249): every task start / pause / resume / end / clear / refusal (with its reason), SOP change, utility switch, move start and HSM region change, with its cause — the last 24, always on. Req `networkId` (number), `since?` (number). Returns { networkId, on, seq, gap, entries:[{seq, at, kind, text}] } — oldest first; gap:true means more than 24 were written since `since` and the oldest are gone.
  Notes: seq counts every entry ever written to the unit and never resets — use it to poll without repeats.; A refusal repeated every tick is ONE entry whose time is refreshed.; on:false — not a brain unit on this node, or the host was started with the journal switched off..
  Example: `get_entity_journal({"networkId":1000,"since":0})` — list what changed in unit 1000's mind and why.
- **`get_entity_memory`** — A unit's raw target memory, every slot — heard (anonymous) contacts included (CE-3144). Req `networkId` (number). Returns { networkId, hasMemory, count, changeEpoch, anonymousSerial, slots:[{id, anonymous, position:[x,y,z], freshness, radius, lastSeenTick, modalities, sourceClass, alive?, networkId?}] }
  Notes: GET /entities/{id} shows memory through the scenario translator, which drops heard slots and entities it cannot map; this route shows them all.; Slots are sorted freshest first; a heard slot has a negative synthetic id and a radius..
  Example: `get_entity_memory({"networkId":1000})` — read what a unit remembers — seen and heard contacts, how fresh, and where.
- **`get_entity_reports`** — Reports up the chain of command (CE-2295, CC-12): on a commander its picture — the elements its subordinates reported, each subordinate's last progress / colours / age / overdue, open requests; on any unit the extra requirements its superior set and its outbox (queued, posture, muted). Req `networkId` (number). Returns { networkId, now, onBrainNode?, picture?:{elements:[{from, size, count, activity, equipment, at, radius, observedAt, ageSeconds, receivedAt, seq, synthetic}], subordinates:[{networkId, name?, progress, strength, ammo, phase, lastSeq, lastReportAt, ageSeconds, overdue}], requests:[{from, kind, at, receivedAt, seq}]}, requirements?:{pir, ffir, sitrepEverySeconds, pirArea, setBy}, outbox?:{queued:[report + dueAt], nextSeq, posture, muted, mutedUntil} }
  Notes: The INTERPRETED view; the raw components (CommandPicture, ReportingRequirements, ReportOutbox) are in get_entity, each buffer a plain array (CC-16).; A report ages from observedAt (when the sender saw it), not from its arrival; overdue = nothing heard for 60 s..
  Example: `get_entity_reports({"networkId":5001})` — what has the platoon heard from its squads.
- **`fake_entity_report`** — A report AS IF this unit's staff sent it to its superior (CE-2295, CC-13) — it enters before the wire, ages from observedAgoSeconds, and the superior's whole decision path runs as for a real one. Marked synthetic: always taken, replaced by the unit's next real report (CC-14). Req `networkId` (number), Req `kind` (string), `subtype?` (string), `count?` (number), `size?` (string), `at?` (array), `strength?` (string), `observedAgoSeconds?` (number). Returns { networkId, sent:true, synthetic:true, report }
  Notes: Run on any node that holds the unit; BR-3 carries the report to the superior's brain node..
  Example: `fake_entity_report({"networkId":5002,"kind":"Spot","size":"Team","count":3,"at":[200,320,0]})` — tell the platoon squad 5002 sees three enemies.
- **`mute_entity_reports`** — Lost comms (CE-2295, CC-15): the unit's staff sends nothing for `seconds` — CONTACT included; its outbox keeps filling. 0 restores the radio. Req `networkId` (number), Req `seconds` (number). Returns { networkId, mutedSeconds }
  Example: `mute_entity_reports({"networkId":5002,"seconds":60})` — squad 5002 goes silent for a minute.
- **`set_entity_sector`** — Set a man's or a commander's sector — the arc he may choose to shoot into, where he watches, his NoFire areas (CE-2298, DA-26). Req `networkId` (number), `left?` (number), `right?` (number), `minRange?` (number), `maxRange?` (number), `watch?` (number), `allowArea?` (number), `noFire?` (array). Returns { networkId, arc:{left,right,minRange,maxRange}|null, watch|null, allowArea|null, noFire, setBy } — or { networkId, forwarded:true } when his brain is elsewhere
  Notes: Origin Operator through the one writer (EngagementAreaSystem.Apply): it replaces the whole sector; a higher setter refuses it.; The arc's origin is where he stands now. On a node that does not run his brain the sector is forwarded to the one that does (kind 4) — the answer says forwarded.; His eyes follow it: a holding or fighting man looks at his sector when no threat or sound holds them (Look Direction §3.3a)..
  Example: `set_entity_sector({"networkId":5301,"watch":0})` — point a sentry north, or give a man his arc of fire.
- **`get_entity_sensors`** — Every sensor of a unit — kind, result family and last answer in its own shape (CE-3072). Req `networkId` (number). Returns { count, sensors:[{partId, kind, family:Ranked|Area, templateId, epoch, suspended, origin:tkb|behaviour|other, answer:{ready, count, lastUpdateTick, top:[{entityId, score, position}]} | {ready, count, lastUpdateTick, areas:[{featureId, kind, threat, distanceAlongRoute, center, nearHandle, farHandle}]}, settings? }] }
  Notes: A Ranked sensor lists its top 5 results; an Area sensor (the danger-area sensor) lists its areas, the next one ahead first.; answer.ready is false until the first answer — an Area answer with count 0 and ready true means a clear route.; settings appear for an Area sensor: which route it watches (OwnMove | Handle | ToPoint) and its corridor..
  Example: `get_entity_sensors({"networkId":1000})` — read which danger areas a unit sees ahead on its route, and how threatened each is.
- **`get_entity_slots`** — Every behaviour occurrence slot a unit carries — the task's, the SOP's, a paused task's, hosted children's — named and decoded (CE-2288). Req `networkId` (number). Returns { networkId, count, slots:[{key, kind, payloadSize, structureHash, owner (Task|Sop|PausedTask|OwnerUnknown), behaviour, role (Params|State|Hsm|Working|HostedChild|RoleUnknown), label, layout, value}] }
  Notes: GET /entities/{id} projects only the running task's params; this route lists every slot, including a hosted drill's own block and the SOP's.; A slot stores no owner: each is named by recomputing the keys of the task, the SOP and a paused task; an unmatched slot is listed with owner OwnerUnknown.; value is the slot decoded through its behaviour's layout (params, a hosted child's params, a stateful node's working state); null when no layout is known..
  Example: `get_entity_slots({"networkId":1000})` — see a commander's whole brain — the play, the SOP, a paused task and the drill it hosts.
- **`get_entity_squad`** — A squad as its commander sees it — members with their assigned fire targets, and the merged contact pool (CE-3087). Req `networkId` (number). Returns commander: { role:'commander', memberCount, members:[{slot, networkId, name, assignment:{networkId, name, score, focusFireCount}|null}], lastMergeTick, contactCount, contacts:[{networkId, name | heard:true, threat, position, sources, lastSeenTick}], maneuver:{active, kind, kindName, phase, phaseName, phaseSince, members:[{slot, networkId, name, order:{kind, point, cover, issuedAt, target}|null}], history:[{t, phase, phaseName, event}], gates?:{covering, set}, care?:{casualty, medic, stage, coverPoint}}|null } · member: { role:'member', commander:{networkId, name}, assignment } · otherwise { role:'none' }
  Notes: The assignment is the leader's fire distribution (CE-3088): recomputed after each merge of the pool (≈ 10 Hz), at most 2 members per target.; An assignment is a consideration in the member's own threat ranking, not an order — a hurt member may still break off (its posture).; Heard contacts (no identity) appear in the pool with heard:true and are never assigned.; maneuver (CE-2157) is the squad drill the commander runs: its phase, the last 16 phase changes with the event that moved each (Start, BoundComplete, DefiladeReached, Timeout, End, ...), and the order each member last got. null when no drill ever ran..
  Example: `get_entity_squad({"networkId":1000})` — read which target each squad member is assigned, what the squad knows, and which drill phase it is in.
- **`get_entity_trace`** — Extract AI behavior trace for an entity. Req `networkId` (number). Returns BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.
  Notes: Arm the entity with observe_trace first to populate trace data.; Returns tier field indicating the AI tier type (BTree/HSM/blueprint)..
  Example: `get_entity_trace({"networkId":1000})` — read AI behavior trace for entity 1000 after arming.
- **`get_entity_utility`** — What a unit's utility decisions chose, and why (CE-3069) — the decisions on EVERY brain unit, always (CE-1319); the per-consideration breakdown only while observed. Req `networkId` (number). Returns { observed, decisions:[{decision, kind, winner, previousWinner, switchCount, evalCount, margin, hysteresis, ranked:[{option|networkId,name, score}]}], lastPass:{winnerOptionId, winnerScore, margin, options:[{optionIndex, considerations:[{input, raw, curved, weight}]}]} } — scores are AFTER hysteresis.
  Notes: ⭐ CE-1319 (BV-61): the decision log is on every brain unit, always — so it replays too; observe_trace arms only the BTree/HSM trace and the utility per-consideration trace (lastPass).; One entry per decision the unit ran (up to 4); a ranking's winner is a candidate entity {networkId, name}. observed:false = no brain log on this node (not a brain unit here).; switchCount counts winner changes since the unit's brain started — poll it to see a posture switch.; lastPass is the most recent option decision's per-consideration breakdown; rankings trace only their winner..
  Example: `get_entity_utility({"networkId":1000})` — read which posture a unit chose and the scores behind it.
- **`get_entity_weapons`** — A unit's weapon mounts (owner = mount 0, then mount children) — ammo, cooldown, TKB numbers; with ?target= the WeaponSelection inputs per mount and the chosen mount (CE-3089). Req `networkId` (number), `target?` (number). Returns { count, mountChildrenEnabled, mounts:[{index, on:owner|child, ammo, maxAmmo, cooldown, turret?, tkb:{range, penetration, damage, muzzleVelocity}|null, inputs?:{hasAmmo, rangeFit, effectiveness, readiness, roundsLeft}}], turrets:[{index, part, azimuthDeg?, elevationDeg?, task?:{mode, mount, epoch, target, point, roundsGranted}, muscle?:{state, failure, epoch, roundsFired}}], choice? }
  Notes: choice is the mount the AimAndFire executor fires on Mount=Auto (the posture's fire step): 0 = the owner's primary.; rangeFit is distance / mount range (≈ 1 inside the range in the decision's curve); 10 = unknown..
  Example: `get_entity_weapons({"networkId":1000,"target":1002})` — see which weapon a unit would fire at a target, and why.
- **`explain_hearing`** — What one unit could hear in the last seconds and why not, as this node's hearing sensor decides it — each sound's verdict (heard / beyond its carry / beyond his ear / walls or corners / his own side), the straight line's loss, the way round through doorways, windows, stairs and street corners, and where it seems to come from (AQ106 L). Req `listener` (number), `seconds?` (number). Returns { listener, seconds, count, heard, sounds:[{source, kind, class, at, ageSeconds, carry, distance, effectiveCarry, verdict, via, heardFrom, straightDb, crossings, pathMetres, pathDb, openings, corners, route:[[x,y,z]]}], note }
  Notes: A dry run of the SAME sensor (its ears and its one verdict, AcousticSensorGenerator.Judge) — it cannot disagree with what was heard; it moves no cursor.; Answers where hearing runs (the Perception node: SimHost, the editor); 404 when the listener is not on this node or has no acoustic sensor here.; The moving and shouting bodies are those near him NOW; the short sounds are the ring's of the last `seconds`..
  Example: `explain_hearing({"listener":1001,"seconds":5})` — find out why the defender did not hear the attacker on the stairs.
- **`explain_line_of_sight`** — Why one unit does or does not see another, as this node's perception decides it — eye and aim heights, stances, every terrain crossing with its transmittance, and any entity in the way (tuning T-4). Req `observer` (number), Req `target` (number). Returns { observer, target, visible, verdict, eye, eyeHeight, observerStance, targetStance, threshold, points:[{height, aim, clear, verdict, terrainTransmittance, crossed:[{along, kind, label, material, transmittance, building, storey}], blockingEntity}], note }
  Notes: A dry run of the SAME strategy perception composes (TerrainWorldLosStrategy.ForLiveWorld) — it cannot disagree with it.; Answers where perception runs with the entities resident (SimHost, Editor); 404 when either unit is not on this node.; Stage 4: the target is SEEN when ANY body point (per its logical stance) has a clear line; every point is listed with its own verdict..
  Example: `explain_line_of_sight({"observer":1001,"target":1002})` — find out which wall hides the enemy from the rifleman.
- **`get_squad_reports`** — The reports this node's inbox saw, newest first (CE-2295, PC-11) — folded, stale (a late older one dropped) or not addressed here; a ring of 256 per node. `last?` (number), `unit?` (number). Returns { count, returned, reports:[{serial, seenAt, from, to, originNode, verdict, kind, subtype, at, observedAt, ageSeconds, seq, synthetic, …}] }
  Notes: ⛔ Not GET /events: that ring holds 500 events of every type and a fight's shots evict a report within seconds..
  Example: `get_squad_reports({"last":20})` — the last reports up the chain.
- **`observe_trace`** — Arm or disarm AI behavior trace buffer allocation for an entity. Req `networkId` (number), Req `on` (boolean). Returns { armed, networkId }
  Notes: Must arm before get_entity_trace will return populated trace data.; Without arming, get_entity_trace returns empty trace..
  Example: `observe_trace({"networkId":1000,"on":true})` — arm AI behavior tracing for entity 1000.
- **`list_utility_decisions`** — Every registered utility decision (CE-1325): name, asset id, kind, option count — the C# [UtilityDecision] classes. No params. Returns [{ decisionId, name, assetId, kind, options }]
  Example: `list_utility_decisions({})` — list the utility decisions.
- **`get_utility_decision`** — What a utility decision IS (CE-1325): per option its aggregation and considerations (input, context, weight, curve kind + slope / exponent / shift, modifier); with networkId that unit's values (winner, margin, per-consideration raw -> curved -> weighted). Req `key` (string), `networkId?` (number). Returns { decisionId, name, assetId, kind, ranking, hysteresis, options:[{optionId, name, aggregation, considerations:[{input, context, weight, curve, slope, exponent, xShift, modifier, raw?, curved?, weighted?}], score?, winner?, zeroedBy?}], live?:{unit, traced, evaluations, switches, winner, runnerUp, margin, ranked} }
  Notes: The settings come from the RUNNING definition — the same for a C#-built behaviour as for an authored one.; Live per-consideration values need the unit traced (observe_trace); without, live.traced is still true when the decision log exists and the rows have no raw values.; An option scored 0 names the consideration that zeroed it (zeroedBy)..
  Example: `get_utility_decision({"key":"Combat posture"})` — read a utility decision's settings.

### Group L — Mutation / fault injection
- **`get_attributes_schema`** — Return all patchable attribute paths and their JSON Schema. No params. Returns { registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, GeoPosition.*, Heading, …).
  Notes: Use patch_attribute to apply a patch using these paths.; Paths not in registeredPaths are silently ignored by patch_attribute..
  Example: `get_attributes_schema({})` — discover patchable attribute paths before calling patch_attribute.
- **`patch_attribute`** — Apply a JSON attribute patch to an entity — locally where this node owns it, by request to the owner where it does not. Req `networkId` (number), Req `patchJson`. Returns Updated entity dump on success, plus write:{route, appliedComponents, requestedComponents}; route is direct, requested or noMatch.
  Notes: Unregistered keys are silently ignored (no error) — write.route is then noMatch.; CE-3003: a key on a component this node does NOT own is sent to its owner as an UpdateEntityAttributeRequest (the JSON arm the owner already applies) — write.route is requested, and the change is in the OWNER's world, not yet in this dump: read it back from the owner's perspective after a tick. Components this node owns in the same patch are applied here.; CE-3003 / CE-191: a key on a component this node does not own and no network descriptor carries (a node-local component, or a networkless host) has no owner to ask — the route REFUSES with 400 instead of answering ok.; patchJson may be a nested JSON object like {"Name":"Alpha"} or a JSON string..
  Example: `patch_attribute({"networkId":1000,"patchJson":{"Name":"Alpha"}})` — rename entity 1000 to Alpha.
- **`edit_component`** — StructEdit escape hatch for arbitrary component fields. Req `networkId` (number), Req `componentType` (string), Req `patch` (object). Returns Updated entity component state. Invalid values → 400, component unchanged.
  Notes: Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, and writes the result back to ECS.; Invalid values → 400, component unchanged.; CE-3003: a component another node owns (a network descriptor carries it and this node holds no authority) is REFUSED with 400 — writing it here would change only this node's replica, which the owner overwrites. Use patch_attribute (it asks the owner) or select the owner's perspective (get_entity_ownership names it).; For fields registered in the attribute schema, prefer patch_attribute..
  Example: `edit_component({"networkId":1000,"componentType":"SimTransform","patch":{"Position":{"X":999,"Y":0,"Z":0}}})` — set SimTransform Position.X to 999 for entity 1000.

### Group M (TKB) — Entity-type catalog
- **`resolve_entity_type_parameters`** — Every combat/perception parameter of a TKB type with its value and where it came from (buildings Stage 0) — or, with ammo/weapon, the generated reference library's launcher × ammo pair (tuning T-1). `type?` (number), `ammo?` (string), `weapon?` (string), `library?` (string). Returns type: { tkbType, name, disType, engineFallbacks, parameters:[{name, value, provenance:Explicit|Generated|ReferenceByDis|ReferenceByName|EngineFallback|NotApplicable, source}] } · ammo: { ammo, weapon, found, weaponMatched, muzzleSpeed, penetrationMm, penetrationFormula, damage, damageFormula, driving } · library=1: { ammo:[…], weapons:[…] }
  Notes: The values are the ones the simulation uses: the translators and the fire chain call the same resolver rules.; Generated = a builder derived it by formula (source names the formula and its inputs); EngineFallback = the type said nothing and an engine default applies.; engineFallbacks counts the parameters on engine defaults — the place to look when a type behaves 'generically'.; ReferenceByName = the generated reference library (tuning T-1): a mount's loaded ammo named by its TKB type's name, when the TKB states no penetration; the formula and its inputs are in source..
  Example: `resolve_entity_type_parameters({"type":100})` — see which of the M1's numbers are stated, derived or engine defaults.
- **`list_entity_types`** — List entity types (TKB templates) with id, name, category, disType. `category?` (string). Returns [{tkbType, name, categoryPath, disType}]
  Example: `list_entity_types({"category":"Vehicle"})` — list all TKB types in the Vehicle category.
- **`get_entity_type`** — Full TKB descriptor: mandatory components, child blueprints, DIS type, and descriptor DTOs. Req `tkbType` (number). Returns Full TKB descriptor including mandatory components, child blueprints, descriptors. No spawn.
  Example: `get_entity_type({"tkbType":1101})` — inspect TKB descriptor for type 1101.

### Group N — World / coordinates
- **`list_doors`** — The doors the resident terrain defines, by terrain-object key, with their live state (what sight and fire see) and the door entity that stands for each (buildings Stage 5b). No params. Returns { terrain, count, doors:[{key, state:Open|Closed|Locked|Destroyed, initial, source, x, y, sillZ, building, storey, runtimeId, entityState}] }
  Notes: key = '<terrain>/<building>/<doorId>', a string — never a network id. runtimeId is the door ENTITY's network id (created by the scenario load), null before it exists.; state = the terrain's live state; entityState = the door entity's replicated DoorState. They differ only for the frame before the mirror runs..
  Example: `list_doors({})` — see which doors of the range are locked.
- **`get_terrain_levels`** — The terrain levels at a point — ground = level 0, storeys/slabs/roofs above, basements below (buildings Stage 1). Req `x` (number), Req `y` (number). Returns { terrain, x, y, levels:[{level, z, kind:ground|above|below}] } — or { terrain:null } when no terrain is resident
  Notes: The same levels Add Entity's SpawnHeight resolves (TerrainWorld.SurfacesAt): surfaces within 0.3 m of the ground merge into level 0..
  Example: `get_terrain_levels({"x":102,"y":104})` — list the floors a unit could be placed on inside a building.
- **`query_terrain`** — Dry-run terrain trace between two points — every crossed wall/fence/floor with its material and, per purpose, its transmittance (sight) or ballistic resistance and the round's chance through it (fire). Req `from` (string), Req `to` (string), `purpose?` (string), `penetration?` (number), `damage?` (number). Returns sight: { terrain, purpose, transmittance, seesThrough, threshold, length, crossed:[{along, kind:panel|prism|slab|ramp, label, material, transmittance, building, storey}], note } · fire: { terrain, purpose, penetration, length, stopped, stopAlong, arrivingDamage, arrivingPenetration, crossed:[{along, kind, label, material, pathMetres, resistanceMmRha, roundPenetrationMm, chance, passes, building, storey}], note }
  Notes: Transmittance multiplies along the line (chain-link 0.85, hedge 0.3, solid walls 0); seesThrough = transmittance >= 0.5.; Perception uses the same rule (TerrainWorld.SegmentBlocked = transmittance < 0.5), so this answer is what a sensor sees.; fire (R-217): resistance = material mm RHA/m × the path inside the piece; chance = ArmorModel.PenetrationChance(round, resistance); damage multiplies by each chance; a crossing the round cannot pass stops it — exactly what bullets do..
  Example: `query_terrain({"from":"102,90,1.6","to":"102,104,1.6"})` — check whether a window lets a soldier outside see into a room.
- **`geo_to_local`** — Convert geographic coordinates to local ENU {x,y,z}. Req `lat` (number), Req `lon` (number), Req `alt` (number), `headingDeg?` (number). Returns { x, y, z, rotation? } — optional rotation if headingDeg was provided.
  Notes: Optional headingDeg → adds rotation quaternion to response..
  Example: `geo_to_local({"lat":50.0755,"lon":14.4378,"alt":200})` — convert Prague geo coords to local ECS metres.
- **`get_world_info`** — World metadata: geo origin, the perception and collider grids (live, placed over the terrain), the resident terrain and the navmesh. No params. Returns { geo:{origin:{lat,lon,alt}}, spatialGrid:{cellSize,originX,originY,width,height,extent}, colliderGrid:{...}, terrain:{name,bounds,groundZ,prisms,walkables,surfaces}|null, navmesh:{provider,baked,version}|null }
  Notes: Reads the ACTIVE PERSPECTIVE's world (on --mode all set the perspective first, e.g. SimHost).; terrain is null when no terrain is resident; navmesh is null on a node that composes no navigation solver.; navmesh.baked is false until a terrain's navmesh is published (the provider is then the straight-line fallback)..
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
- **`get_selection`** — The units the operator has selected (CE-1263): the primary and all. No params. Returns { primary, selection:[networkId] }
  Notes: To point the operator at a unit, select_entity (the panels follow the selection) and focus_entity (centres the camera)..
  Example: `get_selection({})` — which unit is the operator asking about.
- **`select_entity`** — Select a unit for the operator (RB-7, R-4083) — the panels that follow the selection (the inspector, the Diff, seek-to-change) then show it. Through the UI's own request path: SelectEntityCommand, applied by the host's SelectionRequestSystem on the next frame. Req `entity` (number). Returns { requested, note } — read get_selection after a frame
  Notes: Your own reads need no selection — every per-unit route takes the id. This is for pointing the OPERATOR at a unit.; On the Replay Browser the selection joins its selection history (step_selection_history)..
  Example: `select_entity({"entity":1042})` — show the operator unit 1042 in the panels.
- **`get_view`** — What the operator is looking at (CE-1262): the view mode (2D / 3D / FPS), the camera (eye, forward, yaw, pitch, distance, vertical field of view, viewport, the 2-D rectangle), the possessed unit and the unit its crosshair rests on, the selection, and the units in view nearest first with their screen point. `limit?` (number). Returns { mode, camera:{eye, forward, yawDeg, pitchDeg, distance, fovyDeg, viewport, rect2D?}, possessed, aimTarget, zoomFovDeg?, inView:[{networkId, name, distance, screen}], selection:[ids], primary, following?, followDistance? } — camera:null (with a note) where no map canvas draws the world.
  Notes: The operator points by SELECTING (click; in first person the select key on the unit under the crosshair) — read primary / selection.; aimTarget is what the first-person crosshair rests on right now; it moves with the aim.; inView in 2D is the map's own culling; in 3D / FPS a frustum test (not occlusion) — check a line with explain_line_of_sight using possessed as observer.; For what the possessed unit itself perceives, read get_entity_sensors / get_entity_memory on possessed..
  Example: `get_view({"limit":20})` — see what the operator sees and which unit they mean.
- **`follow_unit`** — The map's 3-D camera follows a unit (CE-1293): far away it orbits and looks at him, near it is over his shoulder, at distance 0 it is at his eyes looking where he looks. entity:null hands the camera back to the operator. Req `entity` (number), `distance?` (number). Returns { following, distance } — or { following:null } after entity:null. 409 where no map canvas draws the world, or while the station possesses a unit.
  Notes: The operator takes the camera back with W/A/S/D, Esc or a middle-button drag; get_view reports following / followDistance while it lasts.; Distances are a person's: a vehicle's orbit / shoulder band is 4x, a flyer's 8x..
  Example: `follow_unit({"entity":4242,"distance":20})` — watch a unit the agent is debugging.

### Group O — Variables (the watch, over HTTP)
- **`get_entity_variable`** — Read one blueprint variable by name, with its live value and its pending (staged-but-not-yet-applied) value if a write is queued. Req `networkId` (number), Req `path` (string), `asset?` (string), `owner?` (string, "task"|"sop"|"paused"|"blueprint"), `behaviour?` (string), `role?` (string, "params"|"variables"), `part?` (string), `paramsJson?` (string). Returns { networkId, asset, assetId, path, type, value, writable, pending, pendingValue? }
  Notes: An unknown variable name is a 400 pointing back at list_entity_variables — never an empty success..
  Example: `get_entity_variable({"networkId":1000,"path":"Health"})` — read entity 1000's Health variable and whether an edit is still queued.
- **`stage_entity_variable`** — STAGE a write to a running behaviour's value, through the same seam the editor's Details panel uses — a blueprint variable by name, or (with owner) ANY behaviour slot's params or working state: the task's, the SOP's, a paused task's, an attached blueprint's, a node's or a hosted child's. The value lands on the next advancing tick, IN PLACE — the behaviour is never restarted. Req `networkId` (number), Req `path` (string), Req `value` (any), `asset?` (string). Returns { networkId, asset, assetId, path, staged: true, pending: true, note } — with owner: { networkId, owner, behaviour, part, role, path, component, offset, size, staged, pending, willDrain, startRecordUpdated, restarted:false, note }
  Notes: Running is not a reason to refuse — it is a reason to stage. There is no "pause first" step.; Until the world advances, get_entity_variable still reports the OLD value with pending: true. Step or play to make it land.; A value whose width does not match the field is refused rather than written: the blackboard is shared between subsystems, so an overrun would corrupt a neighbour.; ⭐ With owner, path is dotted (St fields, In fields — e.g. Shots.Count) and [n] indexes an inline array (Shots.Items[1]). A task-params write also rewrites the start record's JSON, so a save and the "same order again?" checks stay true.; ⭐ The what-if loop: checkpoint → stage_entity_variable(owner…) → step → read → restore_checkpoint (skill §J)..
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
  Notes: params is passed through verbatim — the engine reads it with plain JSON, the same string the editor's Mission panel stores. Shape it to the behaviour's paramSchema (list_behaviors), not to a separate mapper.; CE-228: the example above uses MoveToLocation's REAL keys. It previously showed {Latitude, Longitude}, which bind to nothing — the params region would have stayed all-zero and the entity would have driven to the origin, with no error anywhere (R-132's failure shape, invited by this very doc).; The commit is asynchronous: it resolves when the engine acknowledges. If the sim is not being pumped at all the call returns a 504 pointing at play/step.; A stale version yields a 409 (ERR_VERSION_CONFLICT), never a silent overwrite..
  Example: `add_mission_task({"networkId":1000,"behavior":"MoveToLocation","params":{"X":500,"Y":520,"Speed":8,"ArrivalRadius":5}})` — give entity 1000 a MoveToLocation task.
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
- **`get_entity_state`** — The well-known fields parsed out — position, rotation, velocity, speed, current behaviour — so an assertion reads state.position.x instead of digging through component JSON. Req `networkId` (number). Returns { networkId, alive, position:{x,y,z}, rotation:{yawDeg,pitchDeg,rollDeg}, velocity:{x,y,z}, speed, behavior:{hash,name,brainTier}, roeEffective:{fire,reactions,forceLevel,returnFireWindowSeconds}, look:{bodyYawDeg,headYawDeg,eyeYawDeg,source,hasTarget,target,fovDegrees} }
  Notes: A field whose component the entity does not carry is OMITTED, never defaulted — a zero position would be indistinguishable from the origin.; A convenience over get_entity, reading the same components: the two cannot disagree.; roeEffective (CE-2289) is the ROE the unit OBEYS: its own where an order set it, else its nearest commander's, else its type default — a squad's HoldFire shows on every man.; look (CE-3252, LD-D) is where a PERSON looks: eyeYawDeg = body + head, the cone vision uses; source order / aim / glance / free. Omitted for a body with no look (a vehicle's eyes are its body)..
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
- **`reload_ai_behaviours`** — ⭐ CE-1321 — rebuild the C# behaviours (Hrot.AI.Behaviors) and HOT-SWAP them into the running editor: build, copy into the watched directory, wait for the swap; open graph documents are re-decompiled. The same path as the toolbar's Reload AI. No params. Returns { build: ok|failed|not-found|not-wired|busy, exitCode, errors[], reloaded, note, added[], removed[], behaviours, documentsReconciled, registrarFailures[], seconds }
  Notes: Running units KEEP their state: the registry merges in place and a running behaviour's slot memory is untouched; HSM instances are re-bound by slot. A changed params/state LAYOUT is yours to restart (assign it again).; A failed build is a 200 with build:failed and the compiler's error lines — the running assembly is unchanged.; Only Hrot.AI.Behaviors hot-reloads. C# anywhere else (engine, toolkits) needs a rebuild and a restart of the editor.; Cheaper first: a value (stage_entity_variable with owner) or a JSON asset (reload_ai_asset) needs no build at all — the skill's §J ladder..
  Example: `reload_ai_behaviours({})` — rebuild and hot-swap the C# behaviours after editing their source.
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
- **`get_audio`** — ⭐ CE-4038 B11 (R-4059) — the Sound switch (the scene heard from the camera in 2-D and 3-D; FPS always sounds), a film take's own switch, the master volume and mute. No params. Returns { sound, takeSound, master, muted, note }
  Example: `get_audio({})` — read the sound switch.
- **`set_audio`** — ⭐ CE-4038 B11 (R-4059) — set the Sound switch, the master volume and mute, as the menu does (saved to the user's file). `sound?` (boolean), `master?` (number), `muted?` (boolean). Returns as get_audio
  Example: `set_audio({"sound":true})` — turn the sound on.
- **`trigger_cluster_diagnostic_dump`** — Collect diagnostics on the named cluster nodes and pull them to the NAS — the same operation the ExCon's Execute Diagnostic Dump button drives. Req `nodes` (array), `dumpEvents?` (boolean), `dumpEntities?` (boolean), `dumpArchitecture?` (boolean), `dumpLogs?` (boolean), `eventProviders?` (array), `useMarkdown?` (boolean), `maxAgeHours?` (number), `severityThreshold?` (number). Returns { transactionId, nodes[], queued:true, note }
  Notes: ASYNCHRONOUS and cluster-wide: the response confirms the request was PUBLISHED, not that files exist. Every selected node gathers, then the orchestrator pulls to the NAS over SMB.; Poll get_cluster_diagnostic_status until manifestPaths is non-empty — that is the completion signal.; An empty nodes[] is refused rather than read as 'every node': the editor's own panel disables its button on the same condition, and dumping the whole cluster is a different operation from dumping one node.; This adds no collection mechanism — it publishes the same CQRS intent the operator's button publishes, onto whichever node's orchestration bus is reachable.; The request is logged with its transaction id and target nodes: a headless origin never pre-flights a confirmation, so that log is the safety net (ruling 53)..
  Example: `trigger_cluster_diagnostic_dump({"nodes":[1]})` — collect diagnostics from node 1 to the NAS.
- **`get_cluster_diagnostic_status`** — Whether a cluster transaction is in flight, and the file manifest of the last successful diagnostic dump. No params. Returns { inFlight, manifestPaths[], manifestCount, note }
  Notes: Reads the same read model the ExCon's Cluster Diagnostics panel renders, so it answers what a human at the console would see.; manifestPaths are relative to the NAS base directory and describe the LAST SUCCESSFUL dump. EMPTY means none has completed yet — not that one failed.; inFlight covers any cluster transaction, not only a dump.; Only a node that builds and pumps a ClusterUiCache can answer (in --mode all that is ExCon); a host without one can still TRIGGER a dump but cannot observe it, and says so..
  Example: `get_cluster_diagnostic_status({})` — check whether the diagnostic dump finished.
- **`get_architecture_diagnostics`** — This NODE's modules, ECS systems and DDS translators, one entry per subsystem, read from each subsystem's own ModuleHostKernel. `subsystem?` (string). Returns { subsystems[{ subsystem, perspective, modules[], systems[{ phase, module, systemName, executions, avgMs, recentAvgMs, maxMs, errors }], translators[], moduleCount, systemCount, translatorCount }], note }
  Notes: Per SUBSYSTEM, not per node: a --mode all node runs SimHost, IG, CGF and the orchestrator side by side and each holds its own kernel, so one snapshot per node would have to drop the rest.; Every node hosts its own MCP endpoint. This answers for THIS node only — ask each node's own endpoint for its own architecture.; A subsystem with no ECS kernel (ExCon, an orchestrator-only node) correctly reports nothing; check the 'diagnostics.architecture' cell in get_capabilities rather than reading the absence as a wiring bug.; modules carry lifecycleState and circuitState, so a module stuck open or failing shows up here without reading logs.; It allocates the whole snapshot on every call — fine for an operator query, wrong in a loop..
  Example: `get_architecture_diagnostics({})` — see what modules and translators this node is running.
- **`get_render_diagnostics`** — ⭐ CE-1145 — the map view's frame rate (fps, frame-time mean / p95 / max over ~2 s) and its camera (mode 2D / 3D / FPS, eye, forward, yaw / pitch, distance, height above the ground, viewport). No params. Returns { fps, frameMsMean, frameMsP95, frameMsMax, frames, windowSeconds, mode, camera{ eye[3], forward[3], yawDeg, pitchDeg, distance, heightAboveGround, viewport[2] } }
  Example: `get_render_diagnostics({})` — read the map view's frame rate and camera.
- **`director_get_checks`** — ⭐ CE-4042 — a scenario's machine-checkable conditions (scenarios/<s>/checks.json) as JSON, what is wrong with them, and its golden's size. Req `scenario` (string). Returns { scenario, path, json{…}, problems[], golden?{ path, steps, snapshots, shots } }
  Notes: docs/DESIGN_Scenario_Regression.md §3 (the grammar), §4 (the golden)..
  Example: `director_get_checks({"scenario":"ua-tank-ammo"})` — read a scenario's checks.
- **`director_put_checks`** — ⭐ CE-4042 — write a scenario's checks (authoring by an agent): refused, with every problem, when a key, a cue or a time is wrong. Written to the working copy AND the source tree. Req `scenario` (string), Req `expect` (array), `runFor?` (number), `golden?`, `anchors?` (object), `do?` (array), `sampleEvery?` (number). Returns { saved[], expect, anchors, do, golden }
  Example: `director_put_checks({"scenario":"ua-tank-ammo","runFor":60,"expect":[{"name":"fires","on":"shot","by":"Tank"}]})` — write a scenario's checks.
- **`director_next`** — ⭐ CE-4038 — end this reel item (reported as stopped) and go on to the next. No params. Returns the director's status
  Example: `director_next({})` — skip to the next take.
- **`director_pause`** — ⭐ CE-4038 — pause the live show (⚠ a paused frame changes the run — the report says so). No params. Returns the director's status
  Example: `director_pause({})` — pause the show.
- **`director_play`** — ⭐ CE-4038 — play a reel or one take: each scenario opened from its file, the take's shots held on the 3-D camera, ONE fixed step per rendered frame (R-3038). Without capture it plays LIVE on screen and saves nothing; with capture it renders frame by frame and writes ONE movie of the reel. `reel?` (string), `scenario?` (string), `take?`, `speed?` (number), `loop?` (boolean), `capture?` (object). Returns the director's status (see director_status)
  Notes: docs/DESIGN_Film_Director.md §0, §3a, §3b, §4a. Plays in the editor's PREVIEW (R-4061); every item loads again (R-3034). Capture needs a window (Xvfb) and ffmpeg..
  Example: `director_play({"reel":"highlights"})` — play a reel live.
- **`director_list_reels`** — ⭐ CE-4038 — the reels (films/<name>.reel.json): playlists of takes, each ONE movie on film. No params. Returns { root, reels[{ name, title, items, problems[] }] }
  Example: `director_list_reels({})` — list the reels.
- **`director_resume`** — ⭐ CE-4038 — resume the live show. No params. Returns the director's status
  Example: `director_resume({})` — resume the show.
- **`director_scout`** — ⭐ CE-4038 — scout a take or a reel: run each take once, stepped as the film will be, and BAKE its anchor times (with the step rate, the scenario's hash) into the take. The report names every beat that did not happen — a scenario health check. `reel?` (string), `scenario?` (string), `take?` (string), `bake?` (boolean). Returns the director's status; per item: found{anchor: time}, missing[], mismatches[] (a beat that moved since the last scout = a determinism finding)
  Notes: docs/DESIGN_Film_Director.md §0 (R-3038): the filmed run IS the scouted run once CE-3293 S5 lands; until then re-scout after S5..
  Example: `director_scout({"scenario":"ua-tank-ammo"})` — bake a take's anchor times.
- **`director_status`** — ⭐ CE-4038 — what the director does: phase (Idle · Loading · Entering · Playing · Encoding · Done · Stopped), the reel item, the shot, sim time, the realtime factor, and a report per item. No params. Returns { phase, mode, reel, item, items, paused, speed, shot, simTime, realtimeFactor, tickHz, frames, movie, error, reports[{ scenario, take, status, problems[], missing[], mismatches[], skippedReplays[], unresolved[], found{}, tickHz, usedBake, realtimeFactor, steps, lateFrames, waitFrames, maxStepsInAFrame, maxRetiredInAFrame, frames, simEnd, wallSeconds, cuts[{ shot, t, rounds, units{ name: [x, y, z, health] } }] }], anchorsSeen?, mismatches? }
  Example: `director_status({})` — read the director's status.
- **`director_stop`** — ⭐ CE-4038 — stop the director (the preview is left, the camera released, the user's layers and sound back). No params. Returns the director's status
  Example: `director_stop({})` — stop the director.
- **`director_get_take`** — ⭐ CE-4038 — one take as JSON (its shots, anchors, baked anchor times) and what is wrong with it. Req `scenario` (string), Req `take` (string). Returns { scenario, take, path, json{…}, problems[] }
  Example: `director_get_take({"scenario":"ua-tank-ammo","take":"main"})` — read a take.
- **`director_put_take`** — ⭐ CE-4038 — write a take (authoring by an agent): the body is the take; refused, with every problem, when a key or a value is wrong. Req `scenario` (string), Req `take` (string), Req `shots` (array), `anchors?` (object), `title?` (string), `caption?` (string), `tickHz?` (number), `endAt?`. Returns { saved, shots, anchors }
  Notes: docs/DESIGN_Film_Director.md §4. The shot keys are film.py's plan keys plus to, ease, speed, caption, layers, sound..
  Example: `director_put_take({"scenario":"ua-tank-ammo","take":"short","shots":[{"at":1,"seconds":4,"follow":"Tank","distance":20}]})` — write a take.
- **`director_list_takes`** — ⭐ CE-4038 — the film takes: every scenario with a takes/ folder and its takes (scenarios/<s>/takes/<t>.take.json). `scenario?` (string). Returns { root, count, scenarios[{ scenario, takes[] }] }
  Notes: docs/DESIGN_Film_Director.md §4, D-2: a take lives beside its scenario; 'Save As' copies the takes..
  Example: `director_list_takes({})` — list the film takes.
- **`director_verify`** — ⭐ CE-4042 — verify scenarios: each loaded from its file, stepped once per frame for runFor, judged by its checks.json and compared with its golden.json (the first difference named). Read the verdicts in director_status's reports[].verify. `scenario?` (string), `scenarios?` (array), `all?` (boolean), `capture?` (boolean), `fresh?` (boolean), `replay?` (boolean). Returns the director's status (phase, reports[{ status: pass|fail, verify{ passed, verdicts[{name, ok, t, kind}], golden: none|same|different|missing|captured|not-captured, goldenDiff[], written[], problems[], replay: null|same|different|failed, replaySnapshots, replayDiff[], replayNotCarried[] } }])
  Notes: docs/DESIGN_Scenario_Regression.md — R-4085 (replay: the live run against its own recording, §10-1), R-4065 (conditions for all, a golden for a feature scenario, a fresh process each), R-4066 (the engine runs it; HTTP and the command line drive it)..
  Example: `director_verify({"scenario":"ua-tank-ammo"})` — verify a scenario against its checks and golden.
- **`director_verify_jobs`** — ⭐ CE-4042 — the fresh-process verify jobs this editor started (POST /director/verify {fresh:true}): each one's state, exit code and output folder. No params. Returns { jobs[{ job, state: running|done, exitCode, passed, scenarios[], capture, replay, out, junit, log, seconds }] }
  Example: `director_verify_jobs({})` — list the fresh verify jobs.
- **`director_verify_job`** — ⭐ CE-4042 — one fresh-process verify job: running or done, and when done its full report (every scenario's verdicts, golden, first difference). Req `job` (string). Returns { job, state, exitCode, passed, scenarios[], out, junit, log, seconds, report?{ passed, failed[], scenarios[{ scenario, status, verdicts[], golden, goldenDiff[], written[] }] } }
  Example: `director_verify_job({"job":"v1-101500"})` — read a fresh verify job's report.
- **`film_capture`** — ⭐ CE-3289 — write the next drawn frame's 3-D SCENE (sky, terrain, bodies, effects, rounds — no panels, gizmos, cards or HUD) as a PNG, at any size. Req `path` (string), `width?` (number), `height?` (number), `supersample?` (number). Returns { path, width, height, simTime }
  Notes: docs/DESIGN_Map_3D_Mode.md §6ab; scripts/film.py drives it..
  Example: `film_capture({"path":"/tmp/film/0001.png"})` — capture the scene to a PNG.
- **`film_release`** — ⭐ CE-3289 — give the 3-D map's camera back to the operator (it keeps the last pose). No params. Returns { released: true }
  Example: `film_release({})` — release the film camera.
- **`film_shot`** — ⭐ CE-3289 — hold the 3-D map's camera on a shot (a pose, an eye and a target, or a unit to follow) until released; switches the map to 3-D. The operator's and the scenario's camera are left alone. `follow?` (number), `eye?` (array), `target?` (array), `lookAt?` (array), `distance?` (number), `yawDeg?` (number), `pitchDeg?` (number), `lift?` (number), `fovDeg?` (number), `spec?` (object), `t?` (number). Returns { follow, lookAt[3], distance, yawDeg, pitchDeg, fovDeg } — the shot held
  Notes: docs/DESIGN_Map_3D_Mode.md §6ab. The camera is a driver, like first person: never saved to the scenario..
  Example: `film_shot({"follow":1000,"distance":25,"yawDeg":200,"pitchDeg":-15})` — follow unit 1000 from behind and above.
- **`get_map_layers`** — ⭐ CE-4038 B10 (R-4060) — the tactical map's layers, by the names and hover text the Layers panel shows, per view (2-D and 3-D sets), and whether a film take's override is in force. `view?` (string). Returns { shown: "2d"|"3d", override, 2d[{ name, display, on, doc }], 3d[…] }
  Example: `get_map_layers({"view":"3d"})` — list the map's layers.
- **`set_map_layers`** — ⭐ CE-4038 B10 (R-4060) — turn map layers on and off EXACTLY as the user's click does (saved to the user's file like one). `view?` (string), `on?` (array), `off?` (array). Returns the view's layers (as get_map_layers)
  Notes: A film take's layers are NOT this: they are a transient override, never saved (docs/DESIGN_Film_Director.md §4b)..
  Example: `set_map_layers({"view":"3d","on":["Ai.VisionCones"]})` — show the vision cones.

### Group Z — Entity control (orders)
- **`control_entity`** — One control frame for the unit this station possesses — a person: forward/back + strafe in its body frame, facing an absolute yaw; a vehicle: throttle (forward; negative brakes, then reverses) + steer. The newest seq wins; keep sending while the controls are held. Req `networkId` (integer), Req `seq` (integer), `forward?` (number), `lateral?` (number), `steer?` (number), `yaw?` (number), `gait?` (integer), `trigger?` (boolean), `aim?` (array), `use?` (integer), `useVerb?` (integer), `useTarget?` (integer). Returns { networkId, accepted:true, detail:'control' } — 409 when this station does not hold the unit.
  Notes: Frames only drive a unit possessed by THIS node's station (possess_entity first).; A unit held in Riding mode (its human drives a vehicle) takes no frames..
  Example: `control_entity({"networkId":1000,"seq":1,"forward":1,"yaw":0})` — walk unit 1000 forward, facing east.
- **`release_entity`** — Release a unit: the operator's order ends and the unit's own SOP decides again. Req `networkId` (integer). Returns { networkId, accepted:true, detail:'released' }
  Example: `release_entity({"networkId":1000})` — hand unit 1000 back to its AI.
- **`get_entity_order`** — A unit's operator order: running · paused (a reaction interrupted it) · succeeded · failed · faulted · none — and what it runs now. Req `networkId` (integer). Returns { networkId, onBrainNode, state, behavior?, params?, since?, interruptedBy?, finishedAt?, fault?, current:{behavior, origin}, task?:{behavior, origin, since, params} }
  Example: `get_entity_order({"networkId":1000})` — has unit 1000 arrived?.
- **`order_entity`** — Order a unit, as an operator: move to a point, attack an enemy, hold (stop), run any behaviour, or release it to its own SOP. The unit works out HOW (route, cover, weapon). The order outranks its SOP and mission and clears its mission plan; works from any node (forwarded to the unit's brain owner). Req `networkId` (integer), Req `kind` (string), `lat?` (number), `lon?` (number), `x?` (number), `y?` (number), `speed?` (number), `arrivalRadius?` (number), `roadUse?` (string), `targetId?` (integer), `behavior?` (string), `params?` (object). Returns { networkId, accepted:true, detail, behavior?, params?, note } — 404 unknown unit, 409 killed / other side (side lock), 400 malformed.
  Notes: An order is origin Operator: the unit's SOP and its mission cannot replace it; a reaction (under fire) may pause it unless its ROE says StayOnTask.; kind 'attack' runs CombatPosture with the target designated and assault: the unit closes in and fights THAT enemy (a nearer threat does not take over) and the order SUCCEEDS when it is down. ROE still applies — on HoldFire it closes in without firing.; The order is resolved on the unit's brain node a frame or two later; get_entity_order reads the result there..
  Example: `order_entity({"networkId":1000,"kind":"move","x":120,"y":-40,"speed":3})` — send unit 1000 to a point.
- **`release_possession`** — Let a possessed unit go: it stops, and the task it was doing before resumes. Req `networkId` (integer). Returns { networkId, accepted:true, detail:'released' } — 409 when another station (or none) holds it.
  Example: `release_possession({"networkId":1000})` — hand unit 1000 back to its task.
- **`possess_entity`** — Possess a unit as this node's station (first person): its current task pauses and control_entity frames drive its body until release_possession, or until it dies; the paused task then resumes. Another station's hold is taken over. Req `networkId` (integer). Returns { networkId, accepted:true, detail:'possessing' } — 404 unknown unit, 409 killed / other side.
  Notes: While possessed, ordinary orders to the unit are refused (409 Possessed) — release it first.; Possession is a behaviour (OperatorControl) at origin Possession, above Operator: no reaction interrupts it (R-270)..
  Example: `possess_entity({"networkId":1000})` — take unit 1000 in first person.
- **`list_orders`** — The order vocabulary (Move, Attack, Hold …): each order's fields — kind (MapPoint · MapEntity · Value), required, entity-pick presets — and its params schema. entityId narrows it to the orders that unit fulfils. `entityId?` (integer). Returns { networkId?, orders:[ { name, gesture, pick, fields:[{name, kind, required, presets?}], paramSchema } ] }
  Example: `list_orders({"entityId":1000})` — what can unit 1000 be ordered to do, and what does each order need.
- **`order_entities`** — Several orders in one call (a squad, a turn's worth of moves). Each is answered on its own; a refusal does not stop the rest. Req `orders` (array). Returns { results:[ { networkId, accepted, detail, behavior?, params? } ] }
  Example: `order_entities({"orders":[{"networkId":1000,"kind":"hold"},{"networkId":1001,"kind":"move","x":50,"y":10}]})` — hold one unit, move another.
- **`lock_side`** — Lock this API session to one side: orders to any other side's units are refused, and the sitrep defaults to it. side:null unlocks. `side?` (string). Returns { side }
  Example: `lock_side({"side":"Friend"})` — play the Friend side.
- **`get_sitrep`** — The situation as ONE side knows it: its units (position, health, ammo, what each is doing) and the contacts its units remember (fog of war; a heard contact has no id). truth=true lists every other unit instead. `side?` (string), `truth?` (boolean). Returns { side, truth, units:[{networkId, name?, position, health, killed, ammo?, doing}], contacts:[{networkId?, heard?, position, freshness, radius?, by, alive?}] }
  Example: `get_sitrep({"side":"Friend"})` — what does the Friend side see?.

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
4. **A checkpoint needs a live run, a restore needs its label.** `checkpoint` answers the label it used; pass it
   to `restore_checkpoint`. A preview is not a checkpoint and is never recorded.
5. **A replay is the cluster's.** `enter_replay` puts every node into its recording; read it with the ordinary
   readers (`list_entities`, `get_entity_brain`). The routes PUBLISH — pass `waitForReady:true` or poll
   `get_replay_status` before reading.
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
