<!--STATUS
state: LIVE — coordinator → backend: the CE-4099 autosave design (AQ108 §4c, `1e589f5a7`) reviewed — accepted, with one gap to close in the design before building
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — §4c the design reviewed here; D23 the decision; D18 a recipe opens as an unsaved new scenario
  - ../PLAN_Run_Modes_And_Content_Build.md — §2 CE-4099's done-when
-->
# NOTE — coordinator → backend: CE-4099 autosave design accepted; one gap first

✅ **Accepted as designed** — the autosave is the ONE save round with one flag (A2), it never counts as a save (A3), extraction on a worker from a
private sandbox (A4, R-4001), `autosave.json` never listed by construction (A5), and A0 measured before building. ⭐ The gate A0 is right: build
only if the keyframe round-trip saves byte-identical JSON.

## ⚠ The gap — a scenario that was never saved

The plan's done-when is *"an edited, **unsaved** scenario survives a killed editor"*. The commonest unsaved scenario is a **new one from a recipe**
(D18: a recipe opens as an unsaved new scenario) — it has **no `scenarios/<name>/` folder** and no `scenario.json`, so A5's location
(`scenarios/<name>/.autosave/`) and A7's recovery check (*"`savedHash` equals the current `scenario.json`'s hash"*) have nothing to point at.

| what to add to §4c before building | lean |
|---|---|
| where a never-saved scenario's autosave lives | ⭐ the NAS working area beside the opened recorded copies (the same home CE-4089 gives those — one place for "opened, not saved" content), keyed by the recipe it came from + a time; `savedHash` empty |
| how it is offered back | ⭐ on the start surface's *New from a recipe* / start (not on Open, which lists saved scenarios only): *"recover the unsaved scenario from <time>?"* — ui's CE-4096 surface shows it; the HTTP form `GET /autosaves` |
| what the first real *Save As* does | ⭐ deletes it (A6's rule, applied to this home) |

Fold it into §4c (a fourth row in A5–A7 is enough), then build. Also say in §4c what the ~30 ms capture every 2 min costs a running editor
(one hitch per interval per node — acceptable while editing; ⛔ never in Live, where no autosave runs).

_coordinator lane, 2026-10-29_
