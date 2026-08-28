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

## 5b. ⭐ `ok:true` IS NOT EVIDENCE — verify by reading STATE

**This is the one rule that would have saved the most time.** The envelope reports *"the request was
accepted"*, which is not *"the thing happened"*. Measured on `--mode all`, three different endpoints
answered `ok:true` having done nothing, or a fraction, of what was asked:

| call | said | actually did |
|---|---|---|
| `load_scenario_edit` | `ok:true` | loaded **nothing** on that node — `scenario:null`, `entityCount:0`, empty `list_entities` |
| `step{count:120}` | `ok:true` | advanced `simTime` by **0.0167 s** — one frame; and before the cluster booted paused, **every step was refused** and still acked |
| `pause` | `ok:true` | `isPaused` was still `false` on the next read; it flipped a step later |

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

## 5d. ⭐ Localise a "it works on the editor, not on the cluster" report

The same three reads, run on both hosts, turn a vague UI report into a located defect:

- **`get_status.entityCount`** — does this node hold the world at all?
- **`list_entities`** — and can it enumerate it?
- **`get_gizmo_frame`, counted by `shape` ignoring `Line`** — is it *drawing* it? (An empty world still emits
  ~600 grid `Line`s, so a non-zero `count` proves nothing — see that tool's notes.)

Then, for *"the order is ignored"*, compare an **intent** component against its **status** component on the
entity (`get_entity` notes spell out the three-way split). Populated intent + absent status + zero velocity
are three different bugs that look identical on screen.
