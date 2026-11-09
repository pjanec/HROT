<!--STATUS
state: LIVE
updated: 2026-11-09
current-answer: the table
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Next_3.md — the batch (dispatched at 82ef92069)
-->
# STATUS — behaviors Next_3

Started at `43368610f` (merged backend / ui / coordinator, nothing new). Running beside the user-ordered replica-watch build (Review §6.13 / §6.13a: R-1 done, R-2 building, R-3, the watch).

| # | id | state | notes |
|---|---|---|---|
| H-1 | — | ✅ 7 re-captured at `0f14d4514` (each passes every check). Measured first: ALL seven shift at t = 0 by REPRESENTATION (one hash per component across every unit — BehaviorState, MissionPlanQueue, BlueprintBlackboard256, PassengerBuffer: the layout changes since 10-23 — rules/actions 11-01..02, then-picker 11-07, carry state 11-07..08); small drift only: `ua-m2-engage` (≤ 0.08 m, one-tick fire), `ld-sentry-surprise-360` (≤ 1.5 m from t 40), `ua-car-bomb` (≤ 2.15 m), `ua-shootdown-eject` (≤ 2.2 m, one tick); behaviour moved on purpose: `ua-javelin-lock` (the exposure cycle — CE-4136 creep, CE-2429, R-2085: Javelin B hits 6.83 s, the cycling tank fires 25.1 s), `ua-weather-regions` (one tick; the fog bank now carries its EntityGuid — CE-4266/CE-4326), `ua-casualty-care` (contact one tick earlier; the drill runs 3.5 → 24.25 s, every beat passes — the restore-run's 'inactive at 20 s' was an artefact) | Next_2 G-1's seven moved goldens |
| H-2 | CE-2429 · CE-4035 / CE-2232 · CE-4136 | 🟡 CE-2429 ✅ (`6aac2318b`) · CE-4136 ① ✅ creep-until-seen (`0926ff85c`/`7c7cebb17`): rpg-window 5/5, javelin-lock 5/5 · ② ⏸ QUESTION in CE-4136: technical-scoot 3/4 — the exposure budget (0.7 × the M1's fallback 0.8 s aim) is shorter than the Kornet's 1.5 s aim; lean (a) a budget floor at own aim + 0.5 s when it saw first | the memory write-back (lean: the evaluation writes back only what it decided) + the vehicle aim on a moving hull |
| H-3 | CE-4041 (rest) | ✅ CE-4041 closed — `ua-civ-escalation` passes editor 12/12 (golden same) and cluster 6/6; provocation / hill-attack fixed in Oct | |
| H-4 | CE-4151 | ✅ already DONE in Next_2 G-6 (`8c4132bb7`, `6862784c3`): a TKB type's default task by name + params JSON through `AssignBehaviorEvent` at origin Sop; rail `BehaviorTkbDefaultStartTests.CE4151_ANamedDefaultTask_StartsThroughTheIngress_WithItsParams_AtOriginSop` (red-proved); CE-4151 ticked | — |
| H-5 | CE-4359 | ✅ `0926ff85c` — the picker, 44 tasks in 7 folders, 75 re-filed (deviations in CE-4359) | the task picker (worker, after the replica watch) |
| H-6 | CE-4361 | ⏭ skipped — backend B-0 (CE-4360) not on `origin/backend` at `90d846a2d` | |
