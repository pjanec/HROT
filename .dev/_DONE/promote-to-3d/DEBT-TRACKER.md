# 3D Cognitive Spatial Awareness Promotion — Technical Debt Tracker

Carried-over technical debt discovered while implementing the tasks in
[TASK-DETAIL.md](./TASK-DETAIL.md). Empty at kickoff — add a row whenever a task is completed with a
known shortcut, deferral, or partial coverage.

| ID | Source | Description | Priority | Target Batch | Status |
|----|--------|-------------|----------|--------------|--------|
| P3D-402-DEBT | P3D-402 | No DotRecast-backed `INavmeshProvider` exists in the repo (only Fake/Stub/EngineBacked), and Recast `walkableHeight` is not a runtime symbol. The Axis-2 multi-level proof is therefore asserted on the cover-query path (real Z via P3D-203/204) with the `deckClearance > walkableHeight` relation asserted against a fixture constant. Re-do the snap leg against a real DotRecast provider once one is integrated. | P3 | (future, when DotRecast lands) | OPEN |

### P3D-404 sweep results (mandatory pre-merge `, 0f)` / `Position.Z` sweep)

Run on the promoted paths (transform/DEM/EQS/perception/navigation/translators). Outcome:

- **Fixed (genuine altitude drops):** `EngineBackedPathRegistry` (2 sites) converted a now-3D
  `TrajectoryWaypoint` to a Recast `NavWaypoint` with `0f` in the altitude (Y) slot — corrected to
  carry `tw.Position.Z` into Recast Y (§0.1).
- **Legitimate-2D (no change):** spawn placement / scenario `InitialTransform.Position`
  (`SimHostApp`, `SimHostScenarioManager`, `VehicleCommandSystem`) — entities spawn at Z=0 and
  `TerrainQueryResolutionSystem` makes Z authoritative afterward; blueprint-authored MoveTo/wander
  destinations (`CgfNodes`, `HillAttackTankNodes`) — 2D-authored per §0.2; the road-graph branch of
  `PathfindingSolverSystem` — road nodes are a 2D ground network (navmesh/volumetric backends carry
  real Z); `TrajectoryPoolManager.Lift` — the explicit 2D→3D lift for backward-compatible callers.
- **No** `Position.Z = 0` re-zeroing writes found on any promoted path.

Legend:
- **P1 = Critical** (never enters the tracker; always becomes Corrective Task 0 in the next batch).
- **P2 = Should fix** (tracked here, assigned a target batch).
- **P3 = Nice to have** (tracked here, best-effort).
- **Status:** OPEN / RESOLVED (do not delete resolved rows).

> **Already-known deferrals (not debt — by design):** Utility AI `TargetMemory` readers
> (`../group-maneuvers/Step_1_5_TargetMemory_3D_Reconciliation.md`), 3D vehicle dynamics
> (TASK-DETAIL §0.2), and the non-existent EQS generators (TASK-DETAIL §0.3) are intentional scope
> boundaries, not debt. Only record a row here for something this PR *should* have done but didn't.
