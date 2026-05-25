# BATCH-16 REPORT — EQS-039 + EQS-040

**Tasks:** TASK-EQS-039, TASK-EQS-040
**Phase:** 12 — Multi-sensor child-entity support (Part B)

---

## Files Changed

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` | Added `EqsSpawnParams` struct, `Action_SpawnEqsSensorChild`, `Deactivate_SpawnEqsSensorChild`, `Action_WaitForChildSensor`, `BindSensorHandle` (public), `FindExistingChild` private helper |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsCombatNodes.cs` | Added `EqsSensorHandle SensorHandle` field to `MoveToOptimalCoverParams`; updated `Action_MoveToOptimalCover` to resolve buffer entity from handle or fall back to `ctx.Self` |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HideInCoverBehavior.cs` | Added `HideInCoverV2Blackboard` struct, `BindSensorHandle` action, `BuildHideInCoverV2Tree()` BTree definition |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsChildSensorActionTests.cs` | NEW — T-CS-A1..5 (5 tests, all pass) |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsMultiTemplateTests.cs` | NEW — `Eqs_MultiSensor_OneAgentTwoConcurrentQueries` + `HideInCover_BT_v2_SmokeTest_AgentMovesToCover` (2 tests, both pass) |

---

## Test Results

### FDP Toolkits EQS filter

```
dotnet test "FDP/Toolkits/Fdp.Toolkits.Tests/..." --filter "FullyQualifiedName~Eqs"
Total tests: 53   Passed: 53   Failed: 0
```

### Hrot Integration EQS filter

```
dotnet test "Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/..." --filter "FullyQualifiedName~Eqs"
Total tests: 62   Passed: 62   Failed: 0
```

New tests added: **7** (5 in `EqsChildSensorActionTests`, 2 in `EqsMultiTemplateTests`).
Pre-existing tests: **55** (unchanged, all still pass).

---

## Deviations from Spec

### 1. Test project location

**Spec:** `Hrot/Subsystems/Hrot.AI.Behaviors.Tests/Eqs/EqsChildSensorActionTests.cs`
**Actual:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsChildSensorActionTests.cs`

`Hrot.AI.Behaviors.Tests` does not exist. The instructions note this fallback explicitly.

### 2. `GetCommandBuffer()` cast required

**Spec:** `ctx.World.GetCommandBuffer()`
**Actual:** `((ISimulationView)ctx.World).GetCommandBuffer()`

`BTreeContext.World` is typed as `EntityRepository` (not `ISimulationView`). `GetCommandBuffer()` is an explicit interface implementation on `ISimulationView`, so a cast is required.

### 3. `BindSensorHandle` is `public` instead of `internal`

The spec implies an internal helper; made `public` so the test project (separate assembly) can call it directly in the smoke test's node-sequence pattern. No functional difference; it is not exposed through any public API surface of the behavior library.

### 4. `FindExistingChild` builds a fresh query per call (no static cache)

**Spec:** Cache `EntityQuery` as `static EntityQuery? _childScanQuery` (Constraint #2).

The static cache caused `AccessViolationException` during test runs with multiple test cases.
`EntityQuery` caches internal component-array pointers; those pointers become stale after any structural mutation (entity creation, component add/remove) that triggers an internal array reallocation. In a multi-test session the same static query outlives the component-array lifetime.

`FindExistingChild` is invoked at most once per BTree restart (the early-return guard on `SpawnedHandle.IsValid && IsAlive` prevents subsequent calls). The cost of building a query on that single call is negligible. The production concern (Constraint #2 is about steady-state performance, not cold-path) is fully addressed by the existing `SpawnedHandle` idempotency check.

### 5. `Eqs_MultiSensor_OneAgentTwoConcurrentQueries` requires `NetworkIdentity` on the observer

**Spec:** "observer entity (no NetworkIdentity for simplicity)"

`EqsSolverSystem.ProcessSensor` silently returns early when an entity has `PartMetadata` but its parent has no `NetworkIdentity`:
```csharp
if (!repo.IsAlive(parent) || !repo.HasComponent<NetworkIdentity>(parent))
    return; // parent gone or local-only child
```
Without `NetworkIdentity` on the observer, both child sensors are skipped every solver tick and the buffers are never populated. Added `NetworkIdentity { Value = 16001_9900L }` to the observer.

### 6. Template generators use offline-safe stubs

**Spec:** "FindNearestEnemy (EntitiesInRadius generator)" and "FindCoverFromTarget (CoverPointsGenerator)"

`FindCoverFromTarget` / `CoverPointsGenerator` require `SimTransform` and `ICoverProvider` components that are unavailable in the offline `EntityRepository` test environment (same constraint discovered in BATCH-11). Custom `EntityCandidateGenerator` and `PositionalCandidateGenerator` stubs are used instead. Results satisfy the positional vs. entity shape assertions in the spec.

### 7. `HideInCover_BT_v2_SmokeTest` uses direct node-sequence pattern

**Spec:** "Build and run HideInCover_BT_v2 on the observer" (implying full BTree runtime).

For the same reason as deviation #6, the full `HideInCoverV2` BTree cannot run end-to-end against an offline world (no cover provider infrastructure). The smoke test calls the action nodes directly in sequence — same pattern as the existing `T-COV5` test for `HideInCover_BT`. The key assertions (locomotion channel set, destination correct) are preserved.

### 8. `BuildHideInCoverV2Tree()` inserts `BindSensorHandle` action

The spec describes the linkage problem ("linking SpawnedHandle -> MoveConfig.SensorHandle") and offers two options. Implemented as an explicit `Action(BindSensorHandle)` step in the Sequence before `Action_MoveToOptimalCover`, copying `bb.SpawnConfig.SpawnedHandle` into `bb.MoveConfig.SensorHandle`. This is the clearest approach and compiles without reflection or lambda capture of cross-field references.
