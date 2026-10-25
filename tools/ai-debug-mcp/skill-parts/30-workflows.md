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

### J. Choose the CHEAPEST experiment — before you edit code and restart anything
⛔ **The default failure: change C#, rebuild, reload the scenario, run to the moment again — minutes per try — for a
question a live edit answers in seconds.** Climb this ladder from the top; stop at the first rung that answers the question.
📄 `docs/DESIGN_Entity_Brain_View.md` §12g (BV-65) owns the edit path.

| rung | the question | tools | cost | what it does NOT tell you |
|---|---|---|---|---|
| **0. Read** | *what is it doing, why, with what?* | `get_entity_brain`, `get_entity_journal`, `get_entity_slots` (every slot: task, SOP, paused, hosted children — params + working state decoded), `observe_trace` → `get_entity_trace` | ms | nothing about a different value |
| **1. What-if a VALUE** | *would a different param / variable / trait change the outcome?* *is this value the cause?* | `checkpoint` → write (below) → `step {count}` / `play` + breakpoint → read → `restore_checkpoint`; repeat per value | seconds per try | whether the LOGIC is right — only whether this value matters |
| **2. What-if the STRUCTURE (JSON assets)** | *would another node / transition / wiring fix it?* | `read_asset_graph` → `add_graph_node` / `add_graph_link` / `set_graph_param` / `apply_graph_command` → **`reload_ai_asset`** — hot-applies to the running brain | seconds | ⚠ a **SOFT** reload keeps live instances' state; a **HARD** (topology) reload bumps the generation and RESETS them — re-reach the moment (rung 1's checkpoint first) |
| **3. Change C# in `Hrot.AI.Behaviors`** | the fix is a C# behaviour / node / action in THAT assembly | edit → `dotnet build` the AI Behaviors project (what the editor's **Reload BTrees** button runs, `EditorApplication.RebuildAndReloadAI`) → the editor's watcher sees `Hrot.AI.Behaviors.dll` change, loads it into a fresh collectible ALC, re-runs every registrar and swaps the registry (`AiHotReloadCoordinator`); the catalog re-projects the C#-built graphs and open documents are reconciled, so a shown graph is re-decompiled (`AiDocumentManager.ReconcileFromCatalog`) | a build (~tens of s), no restart | ⚠ not yet an HTTP route; ⚠ *measure before relying on it*: whether running instances keep their state across this swap, and that the build output lands where the watcher watches (the editor's base directory) |
| **4. Change C# anywhere else** | engine / toolkit / system code (`Fdp.Toolkits`, `Hrot.*` outside the AI Behaviors assembly) | edit → build → restart the editor → reload the scenario → re-reach the moment | minutes | — the hot-reload watcher covers ONE assembly |

**Rung 1's writes**
- **a blueprint variable:** `stage_entity_variable {networkId, path, value}` — staged, lands on the next advancing tick (✅ built).
- **a trait / setting / any component field:** `edit_component {networkId, componentType, patch}` or `patch_attribute` (✅ built). For
  `CombatTemperament` also set its `Set` bit, or the reader keeps the type's value.
- ⏳ **a behaviour's PARAMS or a BTree / HSM / C# behaviour's WORKING STATE, any slot (task, SOP, hosted child, blueprint):** the same
  `stage_entity_variable` route grows `owner` + `role` (CE-1320, not built yet). A params write updates the start record and does
  NOT restart the behaviour. ⛔ Until it lands, `order_entity {kind:'behavior', params}` is the only params path — it RESTARTS the
  behaviour, so its outcome is not comparable with the checkpoint's.

**Use rung 1 for:** sweeping a threshold / radius / timing (two or three values from one checkpoint) · confirming a bug hypothesis before
touching code (*"force `Phase = Assault` — does the squad move? then the bug is whatever should have set it"*) · reaching a node path
without staging the whole situation.
⚠ **A forced value can create a state the behaviour never reaches on its own** — a rung-1 result is a HINT to confirm (a rail, or the
real situation), never proof. ⭐ A value that should STAY changed belongs in the order's / asset's defaults or the unit's settings — not
in a live edit (it lasts until the behaviour is replaced).
