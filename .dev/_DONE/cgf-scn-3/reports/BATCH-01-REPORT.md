# BATCH-01 Report — Phase 1 Core ECS Correctness

**Batch:** BATCH-01  
**Tasks:** S301, S302, S303, S304  
**Status:** COMPLETE  

---

## Summary

All four Phase 1 tasks have been implemented. Production code compiles cleanly
(`dotnet build IOS-IG-SimHost.sln --no-restore` — Build succeeded, zero errors).

---

## Changes Made

### TASK-S301 — Fix SetManagedComponent / RemoveManagedComponent

**File:** `Hrot\Engine\Hrot.Common\Systems\MissionControlExecutionSystem.cs`

1. `CMD_REPLACE_MISSION` path (~line 167):  
   `repo.SetComponent(entity, new ActiveMissionPlan {...})` →  
   `repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = domainPlan })`

2. `CMD_ABORT_ALL` path (~line 213):  
   `repo.RemoveComponent<ActiveMissionPlan>(entity)` →  
   `repo.SetManagedComponent<ActiveMissionPlan>(entity, null!)`  
   *(Note: `EntityRepository.RemoveManagedComponent<T>` is `internal` to `Fdp.Core` and not
   accessible from `Hrot.Common`. Setting to `null!` via the public `SetManagedComponent`
   overload is the correct public API — the implementation clears the component mask when
   value is null.)*

**Tests added** (`Hrot\Subsystems\Hrot.SimHost.Tests\Systems\MissionControlExecutionSystemTests.cs`):
- `ReplaceMission_SetsManagedComponent_AbortAll_ClearsIt` — covers SC1 (HasManagedComponent true
  after CMD_REPLACE_MISSION), SC2 (GetManagedComponentRO returns non-null plan with correct task
  count), SC3 (HasManagedComponent false after CMD_ABORT_ALL).

---

### TASK-S302 — Fix InlineArray Span Mutation in TryBuildQueue

**File:** `Hrot\Engine\Hrot.Common\Systems\MissionControlExecutionSystem.cs`

In `TryBuildQueue` (~line 265): extracted `Span<MissionPhase>` before the for-loop to avoid
the C# 12 InlineArray defensive-copy trap:
```csharp
Span<MissionPhase> phases = queue.Phases;
for (int i = 0; i < plan.Tasks.Count && i < MaxPhases; i++)
    phases[i] = new MissionPhase { BehaviorId = behaviorId, TaskId = taskIds[i] };
```

**Tests added** (`Hrot\Subsystems\Hrot.SimHost.Tests\Systems\MissionControlExecutionSystemTests.cs`):
- `ReplaceMission_3TaskPlan_PhaseCountAndBehaviorIdCorrect` — SC1 (PhaseCount == 3), SC2 (each
  phase has BehaviorId == 101).
- `ReplaceMission_EmptyPlan_PhaseCountIsZero` — SC3 (zero-task plan gives PhaseCount 0).

---

### TASK-S303 — Add DataPolicy.NoSave to BrainBlackboard

**File:** `FDP\Toolkits\Fdp.Toolkits\Behavior\Components\BehaviorComponents.cs`

Added `[DataPolicy(DataPolicy.NoSave)]` attribute between `[ComponentId]` and the struct
declaration for `BrainBlackboard`.

**Test replaced** (`FDP\Toolkits\Fdp.Toolkits.Tests\Scenario\FdpAutoSerializerFixedBufferTests.cs`):
- Replaced `RoundTrip_BrainBlackboard_ByteForByteIdentity` (which expected serialization to
  succeed — now incorrect after NoSave) with:
  `BrainBlackboard_DataPolicyNoSave_ExcludedFromDom` — asserts BrainBlackboard key absent from
  DOM and co-present FixedByteComp key present.

---

### TASK-S304 — Fix SteppingTimeController.GetMode Return Value

**File:** `FDP\Toolkits\Fdp.Toolkits\Time\Controllers\SteppingTimeController.cs`

`GetMode()` changed to return `TimeMode.Deterministic` (was incorrectly returning
`TimeMode.Continuous`).

**Test added** (`FDP\Toolkits\Fdp.Toolkits.Tests\Time\TimeControllerSwappingTests.cs`):
- `SteppingController_GetMode_ReturnsDeterministic` — SC1 (asserts `GetMode() == TimeMode.Deterministic`).

---

## Test Results

| Project | Passed | Failed | Notes |
|---|---|---|---|
| `Hrot.SimHost.Tests` | 451 | 0 | All tests pass including 5 new S301/S302 tests |
| `Fdp.Toolkits.Tests` | 754 | 7 | 7 pre-existing failures unrelated to BATCH-01 |

### Pre-existing failures in Fdp.Toolkits.Tests (not caused by BATCH-01):
- `CombatComponentTests.WeaponFireIntent_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.DetonationNotification_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.DamageAssessedEvent_IsUnmanaged_AndHasCorrectSize`
- `CombatComponentTests.WeaponFireNotification_IsUnmanaged_AndHasCorrectSize`
- `NavigationIntentBridgeSystemTests.NoneIntent_IsSkipped_NavStateUnchanged`
- `PhysicsQueryActionNodeTests.PhysicsQueryActionNode_GetRaycastResult_ReturnsDefaultForUnresolvedId`
- `FireProcessingSystemTests.FireProcessing_SkipsBullet_WhenShooterNotAuthoritative`

New tests `BrainBlackboard_DataPolicyNoSave_ExcludedFromDom` and
`SteppingController_GetMode_ReturnsDeterministic` both passed.

---

## Blockers / Deviations

- `RemoveManagedComponent<T>` does not exist as a public method on `EntityRepository` (it is
  `internal` to `Fdp.Core`). Used `SetManagedComponent<T>(entity, null!)` as the public
  equivalent — consistent with the implementation which branches on null to clear the component
  mask. This is the same semantic result.
