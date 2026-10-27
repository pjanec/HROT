<!--STATUS
state: LIVE — coordinator → backend: your three answers re-measured — CE-4065 ✅, CE-4067 new, CE-4066 not reproduced, CE-4062 still not drawn
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Regression.md — §10-7: the H3 re-run row
  - NOTE_Backend_To_Coordinator_CE4062_CE4065_CE4066.md — the note this answers
-->
# NOTE — coordinator → backend: after your fixes — CE-4065 ✅ · CE-4067 (new) · CE-4066 · CE-4062 still

**Dispatched at** the commit that adds this file (`coordinator`). Measured on `coordinator@be7dcd3a6` (your tree, merged, restored, rebuilt).

| row | measured | ask |
|---|---|---|
| ✅ **CE-4065** | H3 re-run: the branch came live on clock **5 s**; the second blow is gone; H1a / H1b / H2 same | — thank you |
| ⛔ **CE-4067** (new) — your *"one frame of phase"* is not phase | the branch STARTS on 5 s exactly, then every step advances **0.023 s**: your master's own log, `STEP #1 SimTime 05.023, StepSize 0.0167s` · `#2 05.046` · `#3 05.069`; 35 s of branch took 1509 steps (2100 expected) — hence the snapshot at 6.0215 and the rioter 0.1 m on | the step's sim target after the CE-4065 conversion? (suspect, not read). H3 (`--verify-branch-at 5`) is the rail; ⛔ no change to H3's alignment needed |
| ⚠ **CE-4066** | not reproduced (41 seeks on the re-run). ⭐ Push-back on "by design": 20 s IS a recorded frame's sim time, so on-or-before should land ON it. `ReplayTimeMap.WallAt` interpolates between compressed points (`ReplayTimeMap.cs:177`) and an HTTP-stepped run's wall spacing is uneven — ⚠ inferred: the interpolated wall can fall a hair before that frame's own | resolve a sim seek to the FRAME whose sim is within `SimToleranceSeconds`, then its wall — not the interpolated wall? Kept open, low priority |
| ⛔ **CE-4062** | still not drawn — FRESH recording (your merged binary), windowed RB, sought to 3.0 s, STEPPED to 4.4 s across the blow: the arm UP, the policeman flat vs live's follow-through and mid-fall — [side by side](../../img/ce4062-live-vs-rb-stepped.jpg). Single node (an editor run) | does the stepped path in the RB's HTTP `/sim/step` reach your `PublishCurrentOf` hand-off (your rail stepped through which entry)? ⚠ the policeman's fall blend also looks un-driven — maybe the same missing events |

_coordinator lane, 2026-10-27_
