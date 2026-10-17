<!--STATUS
state: LIVE — behaviors → backend: CIV-1's behaviour half (`CE-2235`) has landed; B-4 (`perf-civilians.sh`) has its brained type.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §5d the CivilianHsm as-built, §7.4 B-4 (yours), §5c the `ua-civ-crowd-300` demo row.
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Civ0_Landed.md — the note this one follows.
-->

# NOTE — behaviors → backend: `CivilianHsm` landed, B-4 may go

**Not orders.** On `behaviors`. Row: [`CE-2235`](../Blueprint_Issues_Tracker.md) (`CivilianHsm` Routine / Alarmed / Casualty).

| for you | what |
|---|---|
| ⭐⭐ **B-4 (`scripts/perf-civilians.sh`, [§7.4](../../DESIGN_Civilians_And_Crowd_Control.md)) has what it waited for** | your `CivilianTkbCatalog` 1101–1104 now carry `DefaultBehaviorHash = CivilianHsm` — the cross-lane edit you OK'd (B2-d), named in commit `fb67bd3c7`. A civilian placed with no behaviour runs his routine on CGF (`BehaviorTkbTranslator`) |
| ⭐ what the gate should time | CGF: `BrainTickSystem` (one HSM per civilian) and the new **`CivilianFearSystem`** (CGF `simList`, right after `CgfThreatEvaluationSystem` — one pass over the frame's `SoundContactEvent`s); SimHost: the pedestrian movers (each civilian walks a ~12 m wander leg, lingers 3–10 s) |
| ⚠ **place the cast INSIDE the terrain's bounds and off water** | 📐 measured on the first `ua-civ-routine-alarm` run: test-town is 0–400 m, and a civilian outside it (or in the Pond, x 298–365 / y 300–365) gets `FailedUnreachable` on every move — no error anywhere, he just stands. A generated 300-civilian scenario should sample from the navmesh, or the gate times men who never walk |
| ⭐ the acceptance is behaviors' demo | `ua-civ-crowd-300` (§5c, CV-10) — behaviors writes it once your script exists, and its check reads your numbers |
| ⭐ `CivilianState` (component **399**) | brain-local (registered in `CognitiveComponentRegistry`, `NoScenario`, never replicated) — ⚠ 399 is now taken in `GlobalComponentIds` |
