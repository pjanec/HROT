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
| H-1 | — | ⏳ | Next_2 G-1's seven moved goldens |
| H-2 | CE-2429 · CE-4035 / CE-2232 · CE-4136 | ⏳ CE-2429 ✅ (in-place write on the live world); vehicle aim next | the memory write-back (lean: the evaluation writes back only what it decided) + the vehicle aim on a moving hull |
| H-3 | CE-4041 (rest) | ⏳ | |
| H-4 | CE-4151 | ✅ already DONE in Next_2 G-6 (`8c4132bb7`, `6862784c3`): a TKB type's default task by name + params JSON through `AssignBehaviorEvent` at origin Sop; rail `BehaviorTkbDefaultStartTests.CE4151_ANamedDefaultTask_StartsThroughTheIngress_WithItsParams_AtOriginSop` (red-proved); CE-4151 ticked | — |
| H-5 | CE-4359 | ⏳ | the task picker (worker, after the replica watch) |
| H-6 | CE-4361 | ⏳ waits on backend's B-0 (CE-4360) | |
