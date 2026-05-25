# BATCH-16 REVIEW — APPROVED

**Batch:** BATCH-16
**Tasks:** EQS-039, EQS-040
**Reviewer:** Dev Lead
**Verdict:** APPROVED (no corrective action needed)

---

## Summary

BATCH-16 delivers the BTree spawn/destroy child-sensor actions (EQS-039) and the multi-sensor
integration test + `HideInCover_BT_v2` recipe (EQS-040). All 62 Hrot integration EQS tests pass
(+7 new); FDP toolkit suite unchanged at 53/53.

---

## Implementation Quality Assessment

### EQS-039: Child-sensor spawn/destroy BTree actions

**`EqsSpawnParams` struct:** Clean layout matching spec. Doubles as blackboard output slot.

**`Action_SpawnEqsSensorChild`:**
- Deterministic `localChildIndex = (int)(((uint)ctx.Self.Index << 8) | ChildSlotIndex)` ✓
- Idempotency: `SpawnedHandle.IsValid && IsAlive` fast-path check prevents double-spawn in steady state ✓
- ECB-only structural mutation: `((ISimulationView)ctx.World).GetCommandBuffer()` (cast required because `BTreeContext.World` is typed as `EntityRepository`) ✓
- Carrier components: `PartMetadata + EqsSensor + EqsCognitiveBuffer`, nothing else ✓
- `FindExistingChild` builds a fresh query per call (no static cache — avoids `AccessViolationException` from stale component-array pointers across test boundaries; cold-path only so cost is negligible) ✓

**`Deactivate_SpawnEqsSensorChild`:**
- ECB `DestroyEntity` + clears handle ✓
- Deactivator key `@0` compound suffix matches spec ✓

**`Action_WaitForChildSensor`:**
- Null/invalid/dead handle → Running ✓
- No buffer → Running ✓
- `IsReady` true → Success ✓

### EQS-040: Multi-sensor test + HideInCover_BT_v2

**`Action_MoveToOptimalCover` extension:**
- `EqsSensorHandle SensorHandle` added to `MoveToOptimalCoverParams` ✓
- `bufferEntity = handle.IsValid && IsAlive(handle.ChildId) ? handle.ChildId : ctx.Self` ✓
- `LocomotionChannel` still read from `ctx.Self` (unchanged semantics) ✓
- `SensorHandle.IsValid == false` → reads `ctx.Self` buffer (backwards compat) ✓

**`HideInCover_BT_v2`:**
- Uses `Action_SpawnEqsSensorChild` + `Action_WaitForChildSensor` + `Action_BindSensorHandle` ✓
- `BindSensorHandle` copies `SpawnConfig.SpawnedHandle` → `MoveConfig.SensorHandle` ✓
- Existing `HideInCover_BT` unchanged ✓

### Tests (T-CS-A1 through T-CS-A5, multi-sensor, smoke test)

All tests exercise the correct behaviors:
- T-CS-A1: ECB spawn + PartMetadata correctness — properly calls `PlaybackAndClearEcb()` after tick 1
- T-CS-A2: Steady-state idempotency via handle fast-path (no double-spawn)
- T-CS-A3: Two slots → two distinct entities  
- T-CS-A4: Deactivate → ECB DestroyEntity → `!IsAlive` confirmed
- T-CS-A5: Parent death → `SubEntityCleanupSystem` → child destroyed automatically
- Multi-sensor: two child sensors with different template shapes (entity vs positional), observer has no buffer
- Smoke test: direct node-sequence invocation drives `LocomotionChannel` to `ActionIdMoveTo`

### Acceptable Deviations

1. Test project: `Hrot.AI.Behaviors.Tests` doesn't exist; tests placed in integration test project (noted in instructions as fallback).
2. ECB cast `((ISimulationView)ctx.World)` — `GetCommandBuffer()` is on `ISimulationView`, not `EntityRepository`. Correct workaround.
3. No static `EntityQuery` cache in `FindExistingChild` — `AccessViolationException` from stale pointers; cold-path cost is negligible.
4. Observer needs `NetworkIdentity` for multi-sensor test — solver skips `PartMetadata` children whose parent has no `NetworkIdentity`; correct for production usage (real agents always have `NetworkIdentity`).

---

## Test Results

```
FDP toolkit: Passed: 53, Failed: 0, Total: 53
Hrot integration: Passed: 62, Failed: 0, Total: 62
```
