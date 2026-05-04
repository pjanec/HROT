# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Date:** 2026-05-04  
**Status:** APPROVED WITH MINOR DEBT

---

## Summary

All 8 batch tasks are complete and correct. Build: 0 errors, 78 pre-existing warnings.
Tests: 558 passing, 6 failing (all 6 are pre-existing failures unrelated to this
workstream). Zero new failures introduced.

46 new tests added, covering all success conditions SC-HA007 through SC-HA016 plus the
debt tests from P2-01, P2-02, P2-03. All corrective tasks are done.

---

## Code Review

### Corrective-0: CgfLogicPackTests Fix

**PASS.** All 3 previously-failing tests now pass. Count updated from 2 to 3 with
updated comment. No other changes to the pack file.

### Corrective-1: Missing Node Tests

**PASS.** 19 tests added covering SC-HA007-1 through SC-HA009-3 and debt items P2-02,
P2-03. `Solver_FindsEntitiesInsidePolygon` now tests 3-inside/2-outside as required.
SC-HA002-3 (65-request overflow) added. SC-HA003-2 (pool zeroed) and SC-HA003-3
(ordering) added.

### TASK-HA010: Commander Setup Nodes

**PASS.** `Action_CalculateSegments` initializes all bitmasks to 0 and correctly clamps
`TotalSlots` to the range [1, 16]. `Action_DispatchAllToBaseline` uses `t = i/(count-1)`
interpolation with correct edge-case guard (`count == 1 -> t = 0.5`). Publishes at
Speed=15 m/s. `Condition_AreAllAtBaseline` correctly skips dead subordinates.

Minor observation: `allParticipate` flag (skip wave parity when roster <= 3) is not in
the spec. It is a reasonable edge-case choice but not validated by tests. Recorded as
P2 debt below.

### TASK-HA011: EQS Integration Nodes

**PASS.** `Action_RequestAreaQuery` correctly guards against duplicate submission when
`CachedEqsRequestId != -1`. Returns Running when batch is full. `Condition_IsAreaQueryResolved`
correctly returns Failure on empty result and Success with cached `TargetGroupHandle`.
Per SC-HA011-5, `CachedEqsRequestId` is intentionally NOT cleared on the Success path
(preserved for the dispatchr to use as a pool fallback).

### TASK-HA012: Wave Control Nodes

**PASS.** Entity.Index % 2 wave parity is correctly used, not roster index. SC-HA012-7
explicitly tests wave parity stability after roster compaction. Swap-remove is correct
(backwards iteration, last-over-current copy). `HasStartedRun` guard prevents premature
completion during intent pipeline propagation latency.

Noted edge case: when fewer available firing slots than wave-eligible tanks, the
developer skips those tanks (returns `continue` when `GetFirstAvailableSlot` returns -1).
This is correct behavior (don't fire twice into the same slot).

### TASK-HA013: PlatoonHillAttack BTree Definition

**PASS.** BTree topology matches spec exactly. `[BTreeDefinition("PlatoonHillAttack")]`
attribute on `BuildPlatoonHillAttackTree()`. Registration in `AiBehaviorFactory` uses
`PlatoonHillAttack_BT = 3014`, `HeavyDtoType = typeof(HillAttackMutableState)`.

Minor observation: condition nodes (`Condition_AreAllAtBaseline`, 
`Condition_IsAreaQueryResolved`, `Condition_IsWaveCompleted`) use `[BTreeAction]`
instead of `[BTreeCondition]`. Both attributes produce the same delegate signature;
the source generator does not differentiate them functionally. This is a cosmetic
inconsistency. Recorded as P2 debt.

### TASK-HA014: TKB Blueprint Updates

**PASS.** `BdcTkbBuilder.WithHeavyMemory()` added. `Unit_TankPlatoon` (301) and
`Unit_TankPlatoon_Auto` (303) blueprints updated. The TKB system is code-based
(not JSON/YAML files) — all changes are in `BdcTkbCatalog.cs` and `BdcTkbBuilder.cs`.

### TASK-HA016: JSON DTO and ParseParams

**PASS.** `PlatoonHillAttackParamsJsonDto` is correct. `AttackDir` is computed as the
left-hand perpendicular to the normalized firing-line vector
`(-normalize.Y, normalize.X)`. `TankSpacing` defaults to 30f when absent. Unresolvable
`TargetAreaNetworkId` correctly writes `Entity.Null`. `ToCartesian` called for all
geo coords. SC-HA016-1 through SC-HA016-6 all pass.

---

## Test Quality Review

**Verdict: PASS — 46 tests, excellent coverage**

| Task | Tests Required | Tests Written | Status |
|------|---------------|---------------|--------|
| Corrective-1 | 15+ | 19 | Pass |
| HA010 | 7 | 7 | Pass |
| HA011 | 5 | 5 | Pass |
| HA012 | 8 | 9 | Pass (extra SC-HA012-7 wave parity) |
| HA013 | 3 | 3 | Pass |
| HA016 | 6 | 6 | Pass |
| **Total** | **44+** | **46** | **PASS** |

---

## Debt Tracker Updates

| ID | Description | Priority |
|----|-------------|----------|
| P2-04 | `allParticipate` flag (bypass wave parity for roster <= 3) is undocumented in spec and untested. If platoon size varies in test scenarios this may produce unexpected wave assignments. | P2 |
| P2-05 | Condition_* commander nodes use `[BTreeAction]` instead of `[BTreeCondition]`. No functional impact (source gen does not differentiate), but cosmetic inconsistency. | P2 |
| P2-06 | `BaselineReservedMask` staleness when `BurnedSlotsMask` covers all slots — baseline fallback reverts to nearest slot regardless of reservation. Known limitation documented in developer report. | P2 |

---

## Decision

**APPROVED.** Proceed to commit BATCH-01 + BATCH-02 work, update TASK-TRACKER.md, then
create and delegate BATCH-03 (EQS network translators + integration test).

---

## Suggested Git Commit Message

```
feat(hill-attack): BATCH-02 -- commander nodes, blueprints, JSON DTO, corrective fixes

Corrective:
- Fix CgfLogicPackTests: InputSystems.Count 2->3 (AreaQueryInitializationSystem added)
- Add 19 missing tests for tank behavior nodes (SC-HA007..SC-HA009, SC-HA002-3,
  SC-HA003-2, SC-HA003-3); fix SC-HA002-1 to test 3-inside/2-outside polygon scenario
- Fix Action_CreepToAndBeyondSlot idempotency: loco.Status set to Running after write
- Fix Condition_HasTarget: use GetSingletonManaged<NetworkEntityMap>()

New:
- Add Action_CalculateSegments, Action_DispatchAllToBaseline, Condition_AreAllAtBaseline
  (TASK-HA010) with 7 tests
- Add Action_RequestAreaQuery, Condition_IsAreaQueryResolved (TASK-HA011) with 5 tests
- Add Action_DispatchWaveWithTargets, Condition_IsWaveCompleted (TASK-HA012) with 9 tests;
  wave assignment uses Entity.Index % 2 (immutable, roster-compaction-safe)
- Add BuildPlatoonHillAttackTree BTree definition registered as PlatoonHillAttack_BT=3014
  (TASK-HA013) with 3 tests
- Add BdcTkbBuilder.WithHeavyMemory; update Unit_TankPlatoon (301) and
  Unit_TankPlatoon_Auto (303) blueprints with Blackboard1024 (TASK-HA014)
- Add PlatoonHillAttackParamsJsonDto + ParsePlatoonHillAttackParams delegate with AttackDir
  computed as left-hand perpendicular; TankSpacing default 30f (TASK-HA016) with 6 tests

Result: Failed: 6 (pre-existing only), Passed: 558, Skipped: 3, Total: 567
```
