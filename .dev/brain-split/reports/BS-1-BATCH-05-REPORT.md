# BS-1-BATCH-05 Report: Node Role Hardening & Navigation CQRS Compliance

**Batch:** BS-1-BATCH-05  
**Date:** 2025-07-24  
**Status:** ✅ COMPLETE — All tasks done, all tests pass

---

## Summary

Batch 05 completes Phase 4 (node role reconfiguration) and begins Phase 5 (Navigation CQRS
compliance). The `CombatModule` / `DamageAssessmentModule` split is now enforced in
`NodeBootstrapper`, all combat/detonation translators are wired up in `SimHostApp`, and all
three navigation executors (`FleeExecutor`, `FollowRoadGraphExecutor`, `FollowRouteExecutor`)
have been refactored to write `NavigationIntent` instead of mutating `NavState` directly. The
`NavigationIntentBridgeSystem` was extended to handle all three navigation modes; the
supporting ECS contracts and DDS model were updated accordingly.

---

## Test Results

```
FDP.Toolkit.Navigation.Tests   — Passed: 37  Failed: 0
Hrot.SimHost.Tests           — Passed: 357  Failed: 0
```

| Task | Scope | Tests | Status |
|------|-------|-------|--------|
| TD-9         | EntityDamageEgressTranslator         | code review | ✅ Complete |
| BS1-T016     | NodeBootstrapper / SimulationLogicModule | 4 new/updated | ✅ All Pass |
| BS1-T017     | SimHostApp translator registration   | no new tests needed | ✅ Complete |
| BS1-T018     | FleeExecutor                         | 5 tests | ✅ All Pass |
| BS1-T019     | FollowRoadGraphExecutor              | 4 tests | ✅ All Pass |
| BS1-T020     | FollowRouteExecutor                  | 4 tests | ✅ All Pass |

---

## Files Modified

| File | Task | Change |
|------|------|--------|
| `Hrot.Map.Common/Replication/Egress/EntityDamageEgressTranslator.cs` | TD-9 | Added `FdpLog.Warn` in `Dispose(long)` + XML risk doc on `_lastPublished` |
| `Hrot.SimHost/NodeBootstrapper.cs` | BS1-T016 | Excluded Brain from CombatModule; added DamageAssessmentModule for Muscle/AllInOne |
| `Hrot.SimHost/Modules/SimulationLogicModule.cs` | BS1-T016 | Added `_damageAssessmentModule` field and conditional registration |
| `Hrot.SimHost.Tests/NodeBootstrapperTests.cs` | BS1-T016 | Updated count 5→6; added Brain/MuscleGround role tests |
| `Hrot.SimHost.Tests/SimulationLogicModuleTests.cs` | BS1-T016 | Updated simGroup.SystemCount 21→22 |
| `Hrot.SimHost/SimHostApp.cs` | BS1-T017 | Added Brain/Muscle/AllInOne-conditional translator registrations |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs` | BS1-T018/T019/T020 | Added `NavigationMode.RoadGraph=4`; added `TargetNodeId` and `TrajectoryId` to `NavigationIntent` |
| `Hrot.NED/SimDescriptors.cs` | BS1-T019 | Added `ENavigationMode.NAV_ROAD_GRAPH=4` |
| `Hrot.SimHost/Network/NavigationIntentEgressTranslator.cs` | BS1-T019 | Added `RoadGraph` mapping in `MapMode` |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/NavigationIntentBridgeSystem.cs` | BS1-T018/T019/T020 | Full rewrite: handles DirectPoint, RoadGraph, FollowRoute modes |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs` | BS1-T019/T020 | Added `NavState.HasArrived` path for RoadGraph/FollowRoute modes |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs` | BS1-T018 | Removed NavState writes; now writes NavigationIntent |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs` | BS1-T019 | Full rewrite; now writes NavigationIntent and polls NavigationStatus |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs` | BS1-T020 | Full rewrite; now writes NavigationIntent and polls NavigationStatus |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FleeExecutorTests.cs` | BS1-T018 | Updated to assert NavigationIntent instead of NavState |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRoadGraphExecutorTests.cs` | BS1-T019 | Rewritten for intent-based pattern |
| `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/ExecutorTests/FollowRouteExecutorTests.cs` | BS1-T020 | Rewritten for intent-based pattern |

---

## Challenges Encountered

### 1. SimulationLogicModule not listed in BS1-T016 file scope

The task spec for BS1-T016 only listed `NodeBootstrapper.cs` and `NodeBootstrapperTests.cs`.
However, `NodeBootstrapper` delegates actual system registration to `SimulationLogicModule`.
Because `SimulationLogicModule` independently creates a `DamageAssessmentModule` instance
(separate from the one in `NodeBootstrapper`), it also needed the same `hasDamageAssessment`
guard. This was discovered during build — it compiled, but the `SimulationLogicModule_EmptyWorld`
test asserted a hardcoded system count of 21 which jumped to 22. The fix required editing
both `SimulationLogicModule.cs` (guard logic) and `SimulationLogicModuleTests.cs` (count
expectation). Both changes are small and clearly correct.

### 2. NavigationIntentBridgeSystem required a full rewrite

The task specs for T018/T019/T020 each listed `NavigationIntentBridgeSystem.cs` as a file to
modify, but the existing system only handled a single case (`DirectPoint`). Supporting
`RoadGraph` and `FollowRoute` required a complete structural rewrite:

- Switch statement across all three modes
- New `Dictionary<int, uint> _lastAppliedIntentId` per-entity tracking to detect IntentId
  changes for the FollowRoute loop-reset signal (BridgeSystem must reset `ProgressS = 0f`
  only when the intent genuinely changed, not every tick)

### 3. NavigationExecutionSystem needed extension

`NavigationExecutionSystem` was not listed in T019/T020 file scope, but the existing system
only checked Cartesian distance for arrival. For RoadGraph and FollowRoute, arrival is
signalled by `NavState.HasArrived` (set by the kinematics layer), not by position proximity.
A guard (`HasComponent<NavState>`) plus a mode switch was added to choose the correct arrival
check.

### 4. NavigationIntent struct missing fields

`NavigationComponents.cs` had no `TargetNodeId` or `TrajectoryId` fields. These were added as
new fields at the end of the unmanaged struct (safe; no serialized format concerns here).
`NavigationMode.RoadGraph = 4` was also missing and added to the enum. The corresponding DDS
enum value `ENavigationMode.NAV_ROAD_GRAPH = 4` was added to `SimDescriptors.cs`.

Note: `NavigationIntentEgressTranslator` (Brain→DDS) now maps the RoadGraph mode, but does
**not** forward `TargetNodeId` or `TrajectoryId` over the wire. Distributed navigation
execution (Muscle reading RoadGraph/FollowRoute intents from DDS) is **deferred** — currently
`NavigationIntentBridgeSystem` only runs inside the AllInOne role where both Brain and Muscle
ECS worlds share memory. Distributed support will require extending `DdsNavigationIntent` and
the ingress translator in a future batch.

---

## Design Gaps / Edge Cases Not Covered by Spec

### FollowRoute loop reset: IntentId contract must be documented

The loop reset mechanism in `FollowRouteExecutor` relies on `intent.IntentId` incrementing to
signal the BridgeSystem to reset `ProgressS`. This is an implicit protocol that is not
documented in the `NavigationIntent` struct definition or in any architecture doc. Suggest
adding an XML doc comment to `NavigationIntent.IntentId` explaining the monotonic-id contract,
and a code comment in `NavigationIntentBridgeSystem` explaining why `ProgressS` is only reset
on intent change.

### NavigationStatus IntentId echo on looped FollowRoute

When `FollowRouteExecutor` loops, it increments `IntentId` without any frame of `Running` in
between — the executor immediately sees the old `NavigationStatus.IntentId` from the previous
cycle and keeps looping. Correctness depends on `NavigationExecutionSystem` writing
`NavigationStatus.IntentId` (echoing the current `NavigationIntent.IntentId`) at the beginning
of each new intent. This is already the case in the current implementation, but the coupling
is fragile. A defensive pattern would be to document this latency assumption in both systems.

### TD-9 uses Warn level rather than Debug

The batch spec said "FdpLog.Debug or FdpLog.Warn". Warn was chosen because the scenario where
`Dispose(long)` is never called for a destroyed entity is a silent resource leak — it should
draw attention in logs, not disappear under a debug filter. If the team prefers Debug to reduce
noise in production logs, this can be downgraded without any functional consequence.

---

## No Temporary Hacks or Deviations

All changes follow the established patterns in the codebase. No stubs, no `// TODO` comments
left in production code, no skipped tests.
