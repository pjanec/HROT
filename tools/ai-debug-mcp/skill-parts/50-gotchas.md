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
