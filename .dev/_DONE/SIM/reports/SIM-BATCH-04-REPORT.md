# SIM-BATCH-04 REPORT — MissionAdapterSystem & JoinFormationExecutor (Phase S4.3 & S4.4)

**Batch:** SIM-BATCH-04  
**Tasks:** TASK-S4.3, TASK-S4.4  
**Status:** ✅ COMPLETE  
**Tests:** 41 passing (0 failures, 0 skipped)

---

## Deliverables

| File | Status | Description |
|------|--------|-------------|
| `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationConstants.cs` | ✅ Updated | Added `ActionIdJoinFormation = 5` constant |
| `Hrot.SimHost/DoctrineIds.cs` | ✅ Created | Stable doctrine ID constants for SimHost (`MoveTo_BT=3001`, `FollowRoute_BT=3002`, `JoinFormation_BT=3003`, `Idle_HSM=3010`) |
| `Hrot.SimHost/Systems/MissionAdapterSystem.cs` | ✅ Implemented | Full replacement of stub — BehaviorId → DoctrineId translation, ParseParams, task advancement |
| `Hrot.SimHost/Systems/JoinFormationExecutor.cs` | ✅ Implemented | Full replacement of stub — `JoinFormationParams`, `InFormationTag`, `IActionExecutor<LocomotionChannel>` |
| `Hrot.SimHost/Modules/SimulationLogicModule.cs` | ✅ Updated | Uncommented `JoinFormationExecutor` registration with `ActionIdJoinFormation` |
| `Hrot.SimHost/Hrot.SimHost.csproj` | ✅ Updated | Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (required for `BrainBlackboard.Memory` pointer access) |
| `Hrot.SimHost.Tests/MissionAdapterSystemTests.cs` | ✅ Created | 5 tests covering all TASK-S4.3 acceptance criteria |
| `Hrot.SimHost.Tests/JoinFormationExecutorTests.cs` | ✅ Created | 4 tests covering all TASK-S4.4 acceptance criteria |

---

## Test Coverage

### MissionAdapterSystem (TASK-S4.3)

| Test | Verifies |
|------|----------|
| `MissionAdapter_ResolvesDoctrineId` | `BehaviorId = "MoveToLocation"` → `DoctrineState.ActiveDoctrineHash` set to `SimHostDoctrineIds.MoveTo_BT` |
| `MissionAdapter_AdvancesTaskOnSuccess` | `LocomotionChannel.Status = Success` → task1 marked `TASK_DONE`, task2 becomes active |
| `MissionAdapter_MarksFailedOnChannelFailure` | `LocomotionChannel.Status = Failure` → active task marked `TASK_FAILED` |
| `MissionAdapter_UnknownBehaviorId_DoesNotThrow` | Unregistered `BehaviorId` → logs warning, skips entity, no exception |
| `MissionAdapter_MissionComplete_RemovesHolder` | Single-task mission + `Success` → `EntityMissionHolder` removed from entity |

### JoinFormationExecutor (TASK-S4.4)

| Test | Verifies |
|------|----------|
| `JoinFormation_LeaderFound_SetsRunning` | Valid `LeaderNetworkId` in `NetworkEntityMap` → `VehicleAPI.JoinFormation` called, `channel.Status = Running` |
| `JoinFormation_LeaderNotFound_SetsFailure` | Unknown `LeaderNetworkId` → `channel.Status = Failure`, no exception |
| `JoinFormation_Execute_SuccessOnFormationTag` | `InFormationTag` present on entity → `Execute` sets `channel.Status = Success` |
| `JoinFormation_Execute_KeepsRunningWithoutFormationTag` | No `InFormationTag` → `Execute` leaves `channel.Status = Running` |

---

## Report Questions

### Q1 — Doctrine Definition Access: Retrieving `DoctrineDefinition` from the Integer Registry Hash

Retrieval worked smoothly. The `DoctrineRegistry` exposes two complementary lookup methods:
- `TryGetId(string name, out int id)` — string-to-int lookup at the task translation boundary
- `TryGetDefinition(int id, out DoctrineDefinition def)` — int-to-definition lookup to get the `ParseParams` delegate

Both are dictionary lookups; no methods were missing. The only gotcha was that `ParseParams` is a nullable delegate field on `DoctrineDefinition` (`Func<string, byte*, void>?`), so the implementation guards against null and empty params before calling it:

```csharp
if (_doctrineRegistry.TryGetDefinition(doctrineId, out var def)
    && def.ParseParams != null
    && !string.IsNullOrEmpty(activeTask.BehaviorParams))
{
    ref var bbRW = ref World.GetComponentRW<BrainBlackboard>(entity);
    fixed (byte* ptr = &bbRW.Memory[0])
        def.ParseParams(activeTask.BehaviorParams, ptr);
}
```

`unsafe` blocks are required for the `fixed (byte* ptr = ...)` pointer. Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to both `Hrot.SimHost.csproj` and `Hrot.SimHost.Tests.csproj`.

---

### Q2 — Component Read/Write: Mutating Structs Inside a Managed List

`MissionTask` is a struct. `List<MissionTask>` stores value-type copies, meaning the standard C# idiom of `list[idx].State = ...` does NOT compile (cannot assign to return value of indexer). The correct approach is to copy-out, mutate, and copy-back:

```csharp
var done = tasks[idx];
done.State = eTaskState.TASK_DONE;
tasks[idx] = done;
```

The same pattern applies to activating the next task and to updating `ActiveTaskId` on the `MissionPlan` struct (which is itself a value type inside `EntityMission`):

```csharp
var next = tasks[idx + 1];
next.State = eTaskState.TASK_ACTIVE;
tasks[idx + 1] = next;

var mission       = holder.Mission;   // copy out EntityMission (value type)
var plan          = mission.Plan;     // copy out MissionPlan (value type)
plan.ActiveTaskId = next.TaskId;      // mutate
mission.Plan      = plan;            // re-seat into EntityMission
holder.Mission    = mission;         // re-seat into holder
```

Finally, **`World.SetManagedComponent(entity, holder)` must be called** to bump the ECS managed-component version counter. Without this call the `EntityRepository`'s `Changed()` / dirty-flag mechanism will not detect the mutation (the list reference itself has not changed — only its contents). This guarantees the egress translator picks up the updated task state on the next publish cycle, which is a documented requirement from the batch instructions.

---

### Q3 — Unknown Behaviors: Spam Mitigation via Idle Fallback

The current implementation logs a `Warn` message every frame for any entity whose `BehaviorId` is not registered in `DoctrineRegistry`. In a real deployment this would produce continuous log spam if an entity receives a mission with an unfamiliar behavior string.

**Two mitigation options:**

**Option A — Idle fallback:** Map unknown `BehaviorId` strings to `SimHostDoctrineIds.Idle_HSM` instead of skipping. This makes the entity visibly idle rather than freezing, which is a more graceful failure mode in a live exercise.

```csharp
if (!_doctrineRegistry.TryGetId(activeTask.BehaviorId, out int doctrineId))
{
    FdpLog<MissionAdapterSystem>.Warn(...);
    doctrineId = SimHostDoctrineIds.Idle_HSM;   // graceful fallback
}
```

**Option B — Per-entity deduplication:** Track the last logged `(entity, behaviorId)` pair in a `HashSet` and only log once per unique combination. This eliminates per-frame spam while still alerting the developer.

**Recommendation:** Prefer **Option A** at runtime — it keeps entities behaviourally coherent (idle is always safe) and still produces one warning to surface the misconfiguration. Option B is complementary and could be layered on top to rate-limit the warning to once per entity.

---

## Implementation Notes

### `JoinFormationParams.FormationTypeId` as `byte`, Not `string`

The initial design considered `string FormationType` in the params struct, but `string` is a managed reference type — it cannot be stored in an unmanaged `[StructLayout(Sequential)]` struct or cast from a raw `byte*` pointer. Changed to `byte FormationTypeId` (storing the byte value of `CarKinem.Formation.FormationType`) so the struct is fully blittable and pointer-cast safe.

### `NodeStatus` Enum Values

`Fbt.NodeStatus` is a `byte` enum with values `Failure = 0`, `Success = 1`, `Running = 2`. There is no `None` member — `default(NodeStatus)` equals `NodeStatus.Failure`. Tests that need a "pre-call neutral state" use `default` or `NodeStatus.Running` where semantically appropriate.

### `MissionTrigger` Namespace Collision

`MissionTrigger` exists in both `Hrot.NED.Descriptors` (the DDS data model) and `FDP.Toolkit.Behavior.Components` (the behavior toolkit). All test helper methods that construct `MissionTask` values now use the fully qualified `Hrot.NED.Descriptors.MissionTrigger` to avoid the `CS0104` ambiguous reference compile error.

---

## Outstanding Issues / Next Steps

- The `Idle_HSM` doctrine (`SimHostDoctrineIds.Idle_HSM = 3010`) must be registered in `SimulationLogicModule`'s `DoctrineRegistry.Register` call before the Idle fallback in Q3 Option A can be enabled.
- `VehicleAPI.JoinFormation` currently does not take a `FormationType` parameter — the `FormationTypeId` decoded from `JoinFormationParams` is mapped to `CarKinem.Formation.FormationType` but not yet forwarded to the API call. This will require a `VehicleAPI` overload addition in a future batch.
- Consider adding a `FormationLeaveExecutor` to handle orderly departure from formations (currently entities remain in `InFormationTag` indefinitely after task completion).
