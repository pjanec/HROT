<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — §3 OWNS the top-attack profile (CE-2201) and the Javelin's numbers
  - docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md — §4: ua-javelin-lock ① waits on this
  - docs/blueprints/batches/HANDOFF_Backend_To_Behaviors_Live_Rerun_Results.md — the live re-run that measured ① after CE-2217
-->

# HANDOFF — behaviors → backend: the Javelin's top attack fits only beyond ~610 m (CE-2222)

**Dispatched at the commit that adds this file** (behaviors). One item, yours (combat).

## The measurement

`GuidedRoundSystem.TopAttackFits` with the Javelin's numbers — 140 m/s, `TurnRateDegPerSec` 30 (⇒ a 267 m turn
radius), `ClimbAngleDeg` 30, `DiveAngleDeg` 60, `CruiseAltitudeM` 150 — computed offline in 25 m steps:

| ground range | fits |
|---|---|
| 150 … 600 m | ⛔ false |
| 625 m and on | ✅ true |

`MunitionTkbCatalog` declares `TopAttackMinRangeM = 150` (a real Javelin top-attacks from about 150 m, climbing steeply in
its close mode). ⇒ ⛔ **inside ~610 m every Javelin flies direct**, whatever the TKB says.

## What it costs live

`ua-javelin-lock` ① (your re-run handoff): Javelin A at ~190 m and ~19° off the cycling M1's nose fires DIRECT onto the
front. `ThreatDanger.ToMe` reads the same `TopAttackFits` and rates A at 0.11, so the tank correctly ignores him and the
vignette can't test the lock denial. Moving A beyond 610 m is no fix: that is past test-town's blocks and the soldier's
150 m sight, and it would hide the data inconsistency.

## Lean

Let the profile fit from `TopAttackMinRangeM`. Either the round gets its own, tighter turn rate (a missile is not a
30 °/s airframe), or the close range gets a steeper climb. Keep the launch and `ThreatDanger.ToMe` on the ONE
`TopAttackFits`, as CE-2201 set up. **What would change the lean:** if the 610 m is intended (a simplification on
purpose), say so and set `TopAttackMinRangeM` to match. Then behaviors re-places ① with a flank shot instead.

**Acceptance:** `TopAttackFits(javelin, 140, 200 m)` is true. After that, behaviors re-runs `ua-javelin-lock` ①.
