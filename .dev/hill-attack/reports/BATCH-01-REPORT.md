# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** AI Developer  
**Date:** 2025-07-22  
**Status:** Complete

---

## Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| TASK-HA001 | [x] | `AreaQueryBatchData`, `EqsTargetPool`, `AreaQueryBatchHelper`, GlobalComponentIds 202/203 |
| TASK-HA002 | [x] | `AreaQuerySolverSystem`, `EqsModule` (SlowBackground 10 Hz) |
| TASK-HA003 | [x] | `AreaQueryInitializationSystem` resetting batch each Brain frame |
| TASK-HA005 | [x] | `PlatoonHillAttackParams`, `PlatoonHillAttackBlackboard`, `HillAttackMutableState` |
| TASK-HA006 | [x] | `HullDownAttackParams`, `HullDownAttackBlackboard` |
| TASK-HA007 | [x] | `Condition_HasTarget`, `Action_CreepToAndBeyondSlot` |
| TASK-HA008 | [x] | `Action_AimAndFireSpecific`, `Action_ReverseToBaseline`, `Action_AbortEngagement` |
| TASK-HA009 | [x] | `BuildHullDownAttackRunTree`, `HullDownAttackMapper`, registration in `AiBehaviorFactory` |

---

## Testing Results

**Unit Tests Passed (new):** 14 / 14  
**Integration Tests Passed:** N/A (integration tests not modified)  
**Pre-existing failures:** 9 (all pre-date this batch; `UnitSubordinateTranslator` JSON
parsing and `CreateEntityRequestSystem.C013` count assertion — unrelated to Hill Attack)

**Test files added:**
- `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryBatchDataTests.cs` — SC-HA001-1 through SC-HA001-4 (4 tests)
- `Hrot/Subsystems/Hrot.SimHost.Tests/EqsModuleTests.cs` — SC-HA002-1 through SC-HA002-4 (4 tests)
- `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryInitializationSystemTests.cs` — SC-HA003-1 (1 test)
- `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackDtosTests.cs` — sizeof and layout tests for all DTOs (5 tests)

**Key Test Scenarios Verified:**
- [x] SC-HA001-1: 64 distinct non-negative RequestId values, 65th returns -1
- [x] SC-HA001-2: After reset, pool slots read zero; stale data does not leak
- [x] SC-HA001-3: `sizeof(AreaQueryRequest)` and `sizeof(AreaQueryResult)` are deterministic
- [x] SC-HA001-4: GlobalComponentIds 202 / 203 constants present
- [x] SC-HA002-1: Solver writes correct TargetCount for 3 inside / 2 outside
- [x] SC-HA002-2: Request for non-alive or polyline-less area entity produces `IsReady=true, TargetCount=0`
- [x] SC-HA002-3: Solver skips frame gracefully when `SpatialGridData` singleton is absent
- [x] SC-HA002-4: `EqsModule.Policy` is `RunMode.Asynchronous` at 10 Hz
- [x] SC-HA003-1: After `Execute`, all `IsReady` flags are false and `Count` is 0

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

Three issues required debugging:

1. **`SystemPhase.PreInput` does not exist.** The enum only defines `Input`, `BeforeSync`,
   `Simulation`, and `PostSimulation`. Fixed by using `[UpdateInPhase(SystemPhase.Input)]`
   for `AreaQueryInitializationSystem`. The system's position as the first entry in
   `CgfLogicPack.InputSystems` guarantees it runs before `MissionExecutionSystem`.

2. **`EntityRepository.AddManagedComponent` is `internal`.** Cannot be called directly from
   test code. Resolved by using `EntityCommandBuffer.AddManagedComponent` with a
   subsequent `Playback(world)` flush — the pattern established in `VisionBroadphaseSystemTests`.

3. **`ExecutionPolicy.SlowBackground` uses `RunMode.Asynchronous`** (not a hypothetical
   `RunMode.Background`). Discovered from reading `ExecutionPolicy.cs` directly. The EqsModule
   test was written with the wrong enum value initially and required a one-line fix.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `EqsTargetPool.NextFreeIndex` is a struct field (not inside a `NativeArray`) which means the
  background thread must call `SetSingleton(pool)` after modification. This is easy to forget.
  A small comment was added to `AreaQuerySolverSystem` to document this requirement.
- The shared-memory write pattern (writing to `NativeArray` results from the snapshot thread to
  affect live memory) is powerful but fragile. The existing `PathfindingBatchData` tests document
  this; the EQS tests follow the same pattern.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **`AreaQuerySolverSystem` requires an `EntityRepository` cast:** The solver casts `view` to
  `EntityRepository` and throws `InvalidOperationException` if it is not one. This matches the
  established pattern in `AreaQuerySolverSystem`-equivalent systems. It is safe because the only
  caller is `EqsModule.Tick`, which runs in the SoD background where the view is always a snapshot
  `EntityRepository`.
- **`Action_ReverseToBaseline` writes a forward `MoveTo` command:** The `MoveToParams` struct has
  no reverse flag. The node writes a normal `MoveTo(BaselineX, BaselineY)` with comment noting
  the limitation. A future `NavState.ReverseAllowed` flag (if added) can be wired here without
  changing the BTree structure.
- **`HullDownAttackMapper` accepts all three tracked tank types** (`Tank_M1Abrams`, `IFV_Bradley`,
  `Tank_T72`) as valid vehicles. The TASK-DETAIL did not restrict to a single type; using the
  same three types that appear in the rest of the behavior system makes the mapper consistent.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Full pool case:** If `EqsTargetPool` is exhausted while writing results, the solver writes
  `TargetCount = 0` and `IsReady = true` to avoid blocking the Brain indefinitely, with a
  `Console.Error.WriteLine` warning. The pool capacity (64 * 4 * 4 = 1024 longs) makes
  exhaustion unlikely in normal operation.
- **Zero-vertex polygon:** The point-in-polygon helper handles `nVerts < 3` by returning `false`,
  so degenerate area entities produce `TargetCount = 0` without crashing.
- **Already-resolved requests:** `IsReady = true` on a result causes the solver to skip
  re-processing it within the same tick. This prevents double-counting if the solver is called
  more than once before `AreaQueryInitializationSystem` resets the batch.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- The solver iterates all 64 batch slots on every tick, even if only a few are active. A
  `Count` field could short-circuit this loop, but given the SlowBackground 10 Hz schedule
  and constant 64-element scan, the cost is negligible.
- `stackalloc (Entity, Vector2)[256]` for the spatial grid query buffer means the solver is
  limited to 256 candidates per polygon per tick. For very dense unit clusters this could
  under-count. Increasing the stack size or switching to a pooled heap buffer is an option if
  needed.
- The point-in-polygon test (ray casting) is O(N) where N is the number of polygon vertices.
  For typical tactical area shapes (4–8 vertices) this is fine.

---

## Files Modified or Created

**New files:**
| File | Task |
|------|------|
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchData.cs` | HA001 |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchHelper.cs` | HA001 |
| `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs` | HA002 |
| `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` | HA002 |
| `Hrot/Subsystems/Hrot.CGF/Systems/AreaQueryInitializationSystem.cs` | HA003 |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs` | HA005 + HA006 |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` | HA007 + HA008 + HA009 |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/HullDownAttackMapper.cs` | HA009 |
| `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryBatchDataTests.cs` | HA001 tests |
| `Hrot/Subsystems/Hrot.SimHost.Tests/EqsModuleTests.cs` | HA002 tests |
| `Hrot/Subsystems/Hrot.SimHost.Tests/AreaQueryInitializationSystemTests.cs` | HA003 tests |
| `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackDtosTests.cs` | HA005 + HA006 tests |

**Modified files:**
| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Added `AreaQueryBatchData = 202`, `EqsTargetPool = 203` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs` | Register EQS singletons in `RegisterAll()` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Register `EqsModule` in module host |
| `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` | Add `AreaQueryInitializationSystem` first in InputSystems |
| `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs` | Register `HullDownAttackRun_BT = 3013` |

---

## Outstanding Issues / Next Steps

- [ ] `Action_ReverseToBaseline` uses a forward `MoveTo` command — if `NavState.ReverseAllowed`
  is added to the locomotion channel, wire it here for proper reverse movement.
- [ ] BATCH-02 tasks (TASK-HA004: Commander BTree, TASK-HA010: PlatoonHillAttackMapper,
  TASK-HA011: integration test) are not yet started.
