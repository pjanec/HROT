# 3D Cognitive Spatial Awareness Promotion — Technical Debt Tracker

Carried-over technical debt discovered while implementing the tasks in
[TASK-DETAIL.md](./TASK-DETAIL.md). Empty at kickoff — add a row whenever a task is completed with a
known shortcut, deferral, or partial coverage.

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| _(none yet)_ | | | | | |

Legend:
- **P1 = Critical** (never enters the tracker; always becomes Corrective Task 0 in the next batch).
- **P2 = Should fix** (tracked here, assigned a target batch).
- **P3 = Nice to have** (tracked here, best-effort).
- **Status:** OPEN / RESOLVED (do not delete resolved rows).

> **Already-known deferrals (not debt — by design):** Utility AI `TargetMemory` readers
> (`../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md`), 3D vehicle dynamics
> (TASK-DETAIL §0.2), and the non-existent EQS generators (TASK-DETAIL §0.3) are intentional scope
> boundaries, not debt. Only record a row here for something this PR *should* have done but didn't.
