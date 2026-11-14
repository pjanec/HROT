---

## 3. Canonical workflows (compose these — they are the point of the API)

### A. Load and inspect
1. `POST /scenario/load/edit {name:"test-move", waitForReady:true}` — blocks until the world is loaded (Edit state).
   Use `POST /scenario/load/live` instead when you want a real run, or when you are on a cluster host and every node
   must hold the world.
2. `GET /entities` → pick a `networkId`. (Filter with `component` / `near` to avoid dumping everything.)
3. `GET /entities/{networkId} {networkId}` → full component dump.

### B. Drive and observe (the safe, deterministic loop)
1. `POST /preview/enter {startPaused:true}` (or `POST /sim/play` then `POST /sim/pause`).
2. Send an action: `POST /entities/command {...}` or `POST /entities/spawn {...}`. (While paused these are queued.)
3. `POST /sim/step {count:N}` to advance N ticks and actually process them.
4. `GET /entities/{networkId}` / `GET /events` to observe the result.
Repeat 2–4. This gives you reproducible, frame-by-frame control. (`POST /sim/play` runs free; `POST /sim/pause` to POST /sim/stop.)

### C. Run until a condition (auto-POST /sim/pause)
1. `POST /breakpoints {condition:{...SearchPredicateDto...}}` → returns `breakpointId`.
2. `POST /sim/play`.
3. Poll `GET /breakpoints/hits` → when `isPaused:true`, the condition fired; `lastHit` names the entity.
4. Inspect, then `DELETE /breakpoints/{id} {id}` and continue.

#### C0. What a utility decision IS, and which node runs it (CE-1325)
- `GET /utility/decisions` → every decision (the C# `[UtilityDecision]` classes).
- `GET /utility/decisions/{key} {key:"Combat posture", networkId?}` → per option its aggregation and considerations (input, context, weight, curve
  kind + slope / exponent / shift, modifier); with `networkId` that unit's winner, margin and raw → curved → weighted per consideration
  (traced units), and the consideration that zeroed an option.
- `GET /behaviours/{name}/decisions {name:"CombatPosture"}` → which node / state / transition binds which decision: `Chooses` (ChooseOption),
  `Ranks` (RankCandidates), `Reads` + the option (an IsOption guard), `Holds` (a custom action's params). Works for C#-built behaviours
  with no asset.

#### C1. Stop on a BEHAVIOUR STEP (CE-1323) — "when he enters Withdrawing", "when that transition fires"
Read the running behaviour's name first (`GET /entities/{networkId}/brain` → the running behaviour), then:
```json
POST /breakpoints {"condition":{"$type":"BehaviourStep","Behaviour":"<name>","Kind":"StateEnter","State":"Withdrawing"},
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
- `filterNetworkId` = only him (and it arms his traces). Without it the POST /sim/step watches every **armed** unit (a Brain tab open, `POST /entities/{networkId}/debug-settings` trace, observe-all).
- With a condition: `{"$type":"Compound","Operator":"And","Conditions":[<BehaviourStep>, <PropertyMatch …>]}`.
- `GET /breakpoints/hits` → `lastHit.what` says what fired: `"entered Withdrawing"`, `"Engaging -> Withdrawing fired on OrderReceived"`, `"event Contact handled (consumed)"`.
- One arrival stops ONCE: `POST /breakpoints/continue` does not POST /sim/stop again on the same POST /sim/step.

### D. Experiment and revert (try something, undo it)
1. `POST /checkpoint` (snapshots the world; enters preview, paused).
2. Mutate / spawn / `POST /sim/step` to run your experiment.
3. `POST /checkpoint/restore` → the world rewinds to the snapshot exactly. The experiment is undone.
> Note: a mutation only "sticks" into the snapshot's dirty-tracking after a `POST /sim/step` — in practice you POST /sim/step to
> run the experiment anyway, so this is automatic.

### E. State diff (what changed, cheaply)
1. `POST /diff/capture` → `baselineId` (serializes current entity state).
2. Do something (move, spawn, POST /sim/step).
3. `POST /diff/compare {baselineId}` → a tree of only the changed components (plus entity births/deaths).

### F. Record and replay (post-mortem)
1. `POST /scenario/load/live {name}` → a live run; every node records it by itself (nothing to start).
2. `POST /sim/play` for a while (optionally `POST /checkpoint {label}` at an interesting moment).
3. `POST /replay {atSeconds:30, waitForReady:true}` → the newest exercise (or name one from `GET /exercises`),
   entered in ONE request from wherever the cluster is.
4. Read the moment with the ordinary readers: `GET /entities`, `GET /entities/{networkId}`, `GET /entities/{networkId}/brain`.
5. `POST /replay/seek {atSeconds:45}` → jump; `GET /replay/status` shows the state and the replay's length.
6. `GET /entities/{networkId}/journal/history {networkId}` / `POST /replay/search {condition}` → scan this node's whole recording (ask the
   node that runs the unit's brain).
7. `POST /replay/stop` when done (the cluster goes to Idle).

### G. Inspect AI behavior (BTree/HSM/blueprint)
1. `POST /trace/observe {networkId, on:true}` — **arms** tracing (allocates trace buffers). Without this, traces
   are empty.
2. `POST /sim/play` / `POST /sim/step` a few frames so the buffer fills.
3. `GET /entities/{networkId}/trace {networkId}` → active node/state + recent history.
4. `POST /trace/observe {networkId, on:false}` to disarm.

**A unit's debug settings — what the operator sees too (CE-1329).** `GET /entities/{networkId}/debug-settings {networkId}` /
`POST /entities/{networkId}/debug-settings {networkId, pins?, trace?, follow?, reset?}` read and write the ONE per-unit store the Brain tab's
*Show on map* ticks and the map's right-click *Pin gizmos* use: `pins` (REPLACES the set — Perception, Contacts, EQS,
Utility, Squad, Actions, Command picture, My units' contacts, Path) draw on the map for that unit, selected or not
(selecting a unit draws none of them); `trace` records its BTree / HSM / utility trace; `follow` puts the 3-D camera on
it. Remembered per scenario (back on its next load). `POST /trace/observe` stays a temporary arming that never touches the
remembered `trace`.

### H. Mutate / fault-inject
- Discoverable, safe path: `GET /attributes/schema` → see patchable paths → `POST /entities/{networkId}/attribute {networkId,
  patchJson:{...}}` (authority-aware; unregistered keys ignored).
- Escape hatch (any component field): `POST /entities/{networkId}/component {networkId, componentType, patch:{...}}` (validated;
  invalid values rejected with 400).

### I. Author a scenario
1. `GET /tkb/types` → choose a `tkbType`. `GET /tkb/types/{tkbType} {tkbType}` for its components.
2. `GET /world/info` for the geo origin + placement extent; `POST /world/geo-to-local`/`POST /world/local-to-geo` to convert coords.
3. `POST /entities/spawn {tkbType, transform}` (POST /sim/step to process); `POST /entities/{networkId}/attribute` to set Name/Affiliation/etc.
4. `POST /scenario/save {name}`.

### J. Choose the CHEAPEST experiment — before you edit code and restart anything
⛔ **The default failure: change C#, rebuild, reload the scenario, run to the moment again — minutes per try — for a
question a live edit answers in seconds.** Climb this ladder from the top; POST /sim/stop at the first rung that answers the question.
📄 `docs/DESIGN_Entity_Brain_View.md` §12g (BV-65) owns the edit path.

| rung | the question | routes | cost | what it does NOT tell you |
|---|---|---|---|---|
| **0. Read** | *what is it doing, why, with what?* | `GET /entities/{networkId}/brain`, `GET /entities/{networkId}/journal`, `GET /entities/{networkId}/slots` (every slot: task, SOP, paused, hosted children — params + working state decoded), `POST /trace/observe` → `GET /entities/{networkId}/trace` | ms | nothing about a different value |
| **1. What-if a VALUE** | *would a different param / variable / trait change the outcome?* *is this value the cause?* | `POST /checkpoint` → write (below) → `POST /sim/step {count}` / `POST /sim/play` + breakpoint → read → `POST /checkpoint/restore`; repeat per value | seconds per try | whether the LOGIC is right — only whether this value matters |
| **2. What-if the STRUCTURE (JSON assets)** | *would another node / transition / wiring fix it?* | `GET /assets/{assetId}/graph` → `POST /assets/{assetId}/graph/nodes` / `POST /assets/{assetId}/graph/links` / `POST /assets/{assetId}/graph/params` / `POST /assets/{assetId}/graph/command` → **`POST /assets/{assetId}/reload`** — hot-applies to the running brain | seconds | ⚠ a **SOFT** reload keeps live instances' state; a **HARD** (topology) reload bumps the generation and RESETS them — re-reach the moment (rung 1's POST /checkpoint first) |
| **3. Change C# in `Hrot.AI.Behaviors`** | the fix is a C# behaviour / node / action in THAT assembly | edit the source → ✅ **`POST /ai/reload {}`** (`POST /ai/reload`, CE-1321 — the same path as the toolbar's **Reload AI**): builds the AI Behaviors project in the editor's configuration, COPIES the dll into the directory the watcher watches, waits for the swap (`AiHotReloadCoordinator`: a fresh collectible ALC, every registrar re-run, the registry merged in place), the catalog re-projects the C#-built graphs and open documents are re-decompiled (`AiDocumentManager.ReconcileFromCatalog`). It answers `{build, errors[], reloaded, added[], removed[], documentsReconciled}` — a failed build is `build:failed` with the compiler's lines, the running assembly unchanged | a build (~tens of s), no restart | ⚠ running units KEEP their task and slot memory across the swap — a changed params / state LAYOUT is yours to restart (assign again); ⛔ C# outside `Hrot.AI.Behaviors` is rung 4 |
| **4. Change C# anywhere else** | engine / toolkit / system code (`Fdp.Toolkits`, `Hrot.*` outside the AI Behaviors assembly) | edit → build → restart the editor → reload the scenario → re-reach the moment | minutes | — the hot-reload watcher covers ONE assembly |

**Rung 1's writes**
- **a blueprint variable:** `POST /entities/{networkId}/variable {networkId, path, value}` — staged, lands on the next advancing tick (✅ built).
- **a trait / setting / any component field:** `POST /entities/{networkId}/component {networkId, componentType, patch}` or `POST /entities/{networkId}/attribute` (✅ built). For
  `CombatTemperament` also set its `Set` bit, or the reader keeps the type's value.
- ✅ **a behaviour's PARAMS or a BTree / HSM / C# behaviour's WORKING STATE, any slot (task, SOP, paused task, hosted child, attached
  blueprint)** *(CE-1320)*: `POST /entities/{networkId}/variable {networkId, owner:"task"|"sop"|"paused"|"blueprint", behaviour?, part?, role:"params"|"variables",
  path, value}` — `path` is dotted, `[n]` indexes an inline array (`Shots.Items[1]`); discover owners, behaviours and parts (node labels)
  with `GET /entities/{networkId}/slots`. Or `paramsJson` instead of `path`/`value`: a whole parameter set through the behaviour's own parser. It lands
  IN PLACE on the next advancing tick, keeps the start record true, and NEVER restarts the behaviour (`restarted:false` in the reply).
  ⚠ `POST /entities/{networkId}/order {kind:'behavior', params}` RESTARTS the behaviour — use it only when a restart is the experiment.

**Use rung 1 for:** sweeping a threshold / radius / timing (two or three values from one POST /checkpoint) · confirming a bug hypothesis before
touching code (*"force `Phase = Assault` — does the squad move? then the bug is whatever should have set it"*) · reaching a node path
without staging the whole situation.
⚠ **A forced value can create a state the behaviour never reaches on its own** — a rung-1 result is a HINT to confirm (a rail, or the
real situation), never proof. ⭐ A value that should STAY changed belongs in the order's / asset's defaults or the unit's settings — not
in a live edit (it lasts until the behaviour is replaced).
