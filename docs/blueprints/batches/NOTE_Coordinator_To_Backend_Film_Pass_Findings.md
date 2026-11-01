<!--STATUS
state: LIVE — coordinator → backend: the engine/host findings of the feature-film pass and the user's demo-city report (2026-10-30/31)
updated: 2026-10-31
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../PLAN_Feature_Films.md — §1d (what filming found)
  - ../../DESIGN_Content_Layout.md — CE-4089's layout: a recipe loads as recipes/scenarios/<seed>
  - ../Blueprint_Issues_Tracker.md — rows CE-4131, CE-4129, CE-4199
-->
# NOTE — coordinator → backend: what the film pass and the demo-city report found

## ① [CE-4131](../Blueprint_Issues_Tracker.md) — the editor slows with every scenario it loads, and so does what it records ⭐ the big one

📐 One editor process, scenarios loaded one after another (`/scenario/load/edit` → `/scenario/load/live` → `/sim/play` → `/sim/stop`), states
per sim second from each recording's `node_1.fdp.meta.json` (TotalFrames / Duration):

| load # | scenario | states / sim s |
|---|---|---|
| 1 | `ua-weather-regions` | 26.3 |
| 6 | `ua-civ-provocation` | 14.9 |
| ~20 | `bt-window-duel` | 6.7 |
| ~50 | `ua-weapon-systems` | 2.7 |
| fresh process | `ua-attack-approach` | 30.4 |

Nothing else heavy ran (an idle Replay Browser, ~1 core, was stopped). ⇒ the cause is the **process's age**, not the host's load — a
likely cousin of CE-3294's reload leaks. A film cut from such a recording steps at ~3 Hz. Workaround used: a fresh editor per recording.
⭐ Done when: N loads in one process record at the fresh rate (or the recorder samples per sim step regardless). ⚠ Measure first — a
memory/handle/subscription census after each load is the cheap first probe.

## ② The user's demo-city report — not reproduced; CE-4131 is my lean

🔒 User: *"Freshly started editor node. Loaded recipe demo city. Pressed play. No entities. I think it was working on the first try, maybe
some file accumulation?"* — and earlier: in Edit SUMO cars show up, in Live none.
📐 Not reproduced on Linux, `coordinator` build: a fresh editor, a SECOND fresh editor on the same staging root, Edit → Live (with and
without a run in Edit first) — all fill up (519 entities, ~20 cars by 30 s). Two editor rows went to ui (CE-4129: ▶ accepted mid-load with a
silent empty world · CE-4199: traffic in Edit dirties an untouched recipe). The user's log is asked for. ⚠ If the user's editor had loaded
other scenarios first, **CE-4131 explains "worked the first time"**.

## ③ A load of a name that does not exist fails quietly (found while reproducing ②)

📐 `POST /scenario/load/edit {"name":"demo-city"}` (the pre-CE-4089 name; the recipe is now `recipes/scenarios/demo-city`): the master logs
`PrefetchScenario … NAS source directory '…/shared/scenarios/demo-city' does not exist` and abandons the transition — but the route answers
only with a generic timeout, and `/status` afterwards reports `scenario: "demo-city"`, `canOperate: true`, 0 entities. ⭐ Lean: the load
refuses at once with *"no scenario 'demo-city' — did you mean `recipes/scenarios/demo-city`?"* (the content layout knows both trees), and
`/status` does not name a scenario that never loaded. Not filed as a row yet — yours to file if you agree (from your block).

_coordinator lane, 2026-10-31_
