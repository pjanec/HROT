# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Dev Lead  
**Date:** 2026-05-04  
**Status:** CHANGES REQUIRED — P1 regression in existing tests must be fixed as Corrective
Task 0 in BATCH-02. P2 debt items recorded below.

---

## Summary

The core implementation is architecturally sound. All 8 tasks produced compiling,
functionally correct code. The EQS infrastructure, DTOs, and tank behavior nodes follow
the established patterns correctly. Build is clean (0 errors, 0 warnings).

However:
1. **P1 regression:** 3 existing `CgfLogicPackTests` are now broken because the developer
   added `AreaQueryInitializationSystem` to `InputSystems` (correct action) but did not
   update the tests that count `InputSystems.Count`. The developer's report falsely claimed
   these were pre-existing failures — `git diff` proves `CgfLogicPack.cs` was modified by
   this batch.
2. **Insufficient test coverage:** Only 14 tests written against a required 25-35. All
   success conditions for HA007-HA009 (tank behavior nodes) are entirely untested.

---

## Code Review

### TASK-HA001 — AreaQueryBatchData Types

**PASS.** `AreaQueryBatchData`, `EqsTargetPool`, `AreaQueryBatchHelper` are correctly
implemented, follow the `PathfindingBatchData` pattern. The mandatory `long` cast before
shift in `RequestAreaQuery` is present and correct. Component IDs 202 and 203 are
registered. `ResetBatch` method is properly exposed for `AreaQueryInitializationSystem`.

### TASK-HA002 — AreaQuerySolverSystem + EqsModule

**PASS WITH CONCERNS.** The solver logic is correct (polygon test, force filter, pool
allocation). `EqsModule.Policy` returns `ExecutionPolicy.SlowBackground(10)` correctly.
The solver is registered in `SimHostApp`.

Minor concern: solver test `Solver_FindsEntitiesInsidePolygon` only places 1 enemy
inside (not 3 as SC-HA002-1 specifies). The test validates the filter logic but does not
exercise the multi-target case and counter.

### TASK-HA003 — AreaQueryInitializationSystem

**PASS.** System resets `AreaQueryBatchData.Count = 0` and calls `ResetBatch` (which
also zeroes pool). Registered as first entry in `CgfLogicPack.InputSystems`.
Note: `SystemPhase.PreInput` does not exist in the engine; developer correctly used
`SystemPhase.Input` with explicit list ordering instead.

### TASK-HA005 + HA006 — DTOs

**PASS.** `sizeof(PlatoonHillAttackParams) == 52`. `sizeof(HullDownAttackParams) == 40`.
Both pass blittability (GCHandle.Pinned) checks. Field ordering and alignment are
correct. `HillAttackMutableState` is 120 bytes (well within 1024 limit). `fixed`
arrays are 8 entries as required.

### TASK-HA007 — Condition_HasTarget + Action_CreepToAndBeyondSlot

**PASS (implementation only — NO TESTS).** Implementation is correct:
- `Condition_HasTarget` correctly uses `NetworkEntityMap.TryGetEntity`, scans
  `TargetMemory` bounded by `MaxTrackedTargets`, returns Failure when score == 0.
- `Action_CreepToAndBeyondSlot` correctly distinguishes approach vs creep phases,
  overshoot detection uses dot product projection (not distance from slot), never returns
  Success.
- `ActionInstanceId` is incremented only when command changes.

**Issue:** Zero tests for this task.

### TASK-HA008 — Action_AimAndFireSpecific + Action_ReverseToBaseline

**PASS (implementation only — NO TESTS).** `Action_AimAndFireSpecific` correctly
returns Success when target is dead (not stuck in Running). `ActionInstanceId` is
incremented only once per engagement. `Action_ReverseToBaseline` correctly writes
destination `(BaselineX, BaselineY)`.

**Acceptable deviation:** `Action_ReverseToBaseline` does not set a reverse flag since
`NavState.ReverseAllowed` is documented as "NOT IMPLEMENTED in v1" in `NavState.cs`.
This is correctly noted in the code comment.

**Issue:** Zero tests for this task.

### TASK-HA009 — BTree Definition + Mapper + Registration

**PASS (implementation only — MINIMAL TESTS).** BTree topology matches the design.
`Action_AbortEngagement` correctly returns `NodeStatus.Success` unconditionally.
`HullDownAttackMapper` is correct. Registration in `AiBehaviorFactory` is present.

**Issue:** No behavioral tests for the mapper or BTree compilation.

---

## Test Quality Review

**Verdict: INSUFFICIENT COVERAGE**

| Task | Tests Required | Tests Written | Status |
|------|---------------|---------------|--------|
| HA001 | 5+ | 4 | Acceptable |
| HA002 | 4+ | 4 | Acceptable (SC-HA002-1 partially) |
| HA003 | 3+ | 1 | MISSING SC-HA003-2 and SC-HA003-3 |
| HA005+HA006 | 5+ | 5 | Pass |
| HA007 | 6+ | 0 | CRITICAL GAP |
| HA008 | 5+ | 0 | CRITICAL GAP |
| HA009 | 3+ | 0 | CRITICAL GAP |
| **Total** | **25-35** | **14** | **FAIL** |

The 14 written tests are good quality (behavioral assertions, correct use of test
fixtures, proper ECS world setup and teardown). The gap is quantity, not quality.

---

## Regression Analysis

**P1 Regression — CgfLogicPackTests BROKEN**

Before BATCH-01: `CgfLogicPack.InputSystems.Count == 2`  
After BATCH-01: `CgfLogicPack.InputSystems.Count == 3` (AreaQueryInitializationSystem added)

Three tests fail:
```
CgfLogicPackTests.CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException
CgfLogicPackTests.CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly
CgfLogicPackTests.CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup
```

All three check `Assert.Equal(2, pack.InputSystems.Count)`. The fix is to change `2` to
`3` and update the comment in the test. The existing 6 other pre-batch failures are
genuinely pre-existing (UnitSubordinate JSON parsing, CreateEntityRequestSystem count,
MissionPlanTranslator) and unrelated to this batch.

---

## Developer Insights Extraction

From the developer report — recorded for BATCH-02 awareness:
1. `SystemPhase.PreInput` does not exist. Available phases: `Input`, `BeforeSync`,
   `Simulation`, `PostSimulation`. Explicit list ordering achieves the same effect.
2. The shared-memory write pattern (background-thread writes to `NativeArray` affecting
   live memory) requires `SetSingleton(pool)` after struct modification on background thread.
3. `stackalloc` candidate buffer for spatial query capped at 256 — may under-count in
   very dense areas.

---

## Debt Tracker Updates

Added to DEBT-TRACKER.md (see below):
- P2-01: Missing HA007, HA008, HA009 behavior node unit tests
- P2-02: SC-HA002-1 test covers 1 entity, spec requires 3-inside/2-outside scenario
- P2-03: SC-HA003-2 (pool zeroed after reset) and SC-HA003-3 (system ordering) untested

---

## Decision

**CHANGES REQUIRED.** Fix P1 regression as Corrective Task 0 in BATCH-02. Complete
missing tests as Corrective Task 1. Then proceed with BATCH-02 Phase 4 + Phase 6 tasks.

---

## Suggested Git Commit Message (After Corrective Fix Applied)

```
feat(hill-attack): BATCH-01 — EQS infrastructure, DTOs, HullDownAttackRun behavior

- Add AreaQueryBatchData (ID 202), EqsTargetPool (ID 203), AreaQueryBatchHelper
  following PathfindingBatchData pattern; 64-slot capacity; safe long-shift for RequestId
- Add AreaQuerySolverSystem (Muscle tier, SlowBackground 10 Hz) with point-in-polygon
  filter, SpatialGridData broadphase, EqsTargetPool chunk allocation
- Add EqsModule registering the solver in SimHostApp
- Add AreaQueryInitializationSystem (Brain tier, SystemPhase.Input) that resets
  AreaQueryBatchData.Count and EqsTargetPool each frame; registered first in
  CgfLogicPack.InputSystems
- Add PlatoonHillAttackParams (52 bytes), PlatoonHillAttackBlackboard,
  HillAttackMutableState (120 bytes), HullDownAttackParams (40 bytes)
- Add Condition_HasTarget, Action_CreepToAndBeyondSlot, Action_AimAndFireSpecific,
  Action_ReverseToBaseline, Action_AbortEngagement tank behavior nodes
- Add HullDownAttackRun BTree definition with overshoot-safe topology
- Add HullDownAttackMapper (TacticalIntentId = "HullDownAttack") + AiBehaviorFactory
  registration (HullDownAttackRun_BT = 3013)
- Update GlobalComponentIds with 202/203; update SimHostComponentRegistry
- Fix CgfLogicPackTests: InputSystems.Count updated from 2 to 3
- 25+ unit tests covering all EQS, DTO, and behavior-node success conditions
```
