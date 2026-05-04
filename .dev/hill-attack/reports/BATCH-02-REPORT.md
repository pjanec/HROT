# BATCH-02 Report

**Batch:** BATCH-02  
**Developer:** GitHub Copilot  
**Date:** 2026-05-04  
**Status:** Complete

---

## Task Completion

| Task ID         | Status | Notes                                                                   |
|-----------------|--------|-------------------------------------------------------------------------|
| Corrective-0    | DONE   | `CgfLogicPackTests.cs`: count 2->3 in 3 tests; all pass                |
| Corrective-1    | DONE   | 19 tests (SC-HA007-x, SC-HA008-x, SC-HA009-x, SC-HA002-x, SC-HA003-x) |
| TASK-HA010      | DONE   | `Action_CalculateSegments`, `Action_DispatchAllToBaseline`, `Condition_AreAllAtBaseline` |
| TASK-HA011      | DONE   | `Action_RequestAreaQuery`, `Condition_IsAreaQueryResolved`              |
| TASK-HA012      | DONE   | `Action_DispatchWaveWithTargets`, `Condition_IsWaveCompleted`           |
| TASK-HA013      | DONE   | `BuildPlatoonHillAttackTree` BTree + `AiBehaviorFactory` registration   |
| TASK-HA014      | DONE   | `WithHeavyMemory` builder method; `Unit_TankPlatoon` and `Unit_TankPlatoon_Auto` blueprints updated |
| TASK-HA016      | DONE   | `PlatoonHillAttackParamsJsonDto.cs` + `ParsePlatoonHillAttackParams`    |

---

## Testing Results

**HillAttackNodeTests (new):** 46 / 46  
**CgfLogicPackTests (corrective):** 3 / 3 (previously broken, now fixed)  
**Full SimHost.Tests suite:** Passed: 558, Failed: 6, Skipped: 3, Total: 567  
**Pre-existing failures (unchanged):** 6  
- `UnitSubordinateTranslatorTests` (3 tests)  
- `MissionPlanTranslatorTests` (2 tests)  
- `CreateEntityRequestSystemTests.C013_ChildOverride_KeyAbsent_AllocatorCalledForChild` (1 test)  
**New failures introduced by this batch:** 0

**Key Test Scenarios Verified:**

### Corrective-0 (EQS system count)
- [x] SC-HA003-3: `AreaQueryInitializationSystem` is first in `CgfLogicPack.InputSystems`
- [x] Three `CgfLogicPackTests` now expect count 3

### Corrective-1 (Tank node tests)
- [x] SC-HA007-1: `Condition_HasTarget` returns `Success` when target in TargetMemory with score > 0
- [x] SC-HA007-2: `Condition_HasTarget` returns `Failure` when `NetworkEntityMap` cannot resolve
- [x] SC-HA007-3: `Action_CreepToAndBeyondSlot` returns `Running` when not overshot
- [x] SC-HA007-3b: Returns `Failure` when overshot > 50m along attack direction
- [x] SC-HA007-4: Speed in `LocomotionChannel` equals `ApproachSpeed` when far from slot
- [x] SC-HA007-5: Speed equals `CreepSpeed`; destination far along attack dir when near slot
- [x] SC-HA007-6: `ActionInstanceId` NOT incremented on second identical call (idempotency)
- [x] SC-HA008-1: `Action_AimAndFireSpecific` writes `WeaponChannel`; `ActionInstanceId` incremented once
- [x] SC-HA008-2: Second call with `Running` status does NOT increment `ActionInstanceId`
- [x] SC-HA008-3b: Dead target -> `Action_AimAndFireSpecific` returns `Success` immediately
- [x] SC-HA008-4: `Action_ReverseToBaseline` writes destination `(BaselineX, BaselineY)`
- [x] SC-HA008-5: `Action_ReverseToBaseline` returns `Success` when channel `Status == Success`
- [x] SC-HA009-1: `HullDownAttackMapper.TargetIntentId == "HullDownAttack"`
- [x] SC-HA009-2: Tank entity -> `TryMap` returns true, `BehaviorName = "HullDownAttackRun"`
- [x] SC-HA009-3: Non-tank entity -> `TryMap` returns false
- [x] SC-HA002-1: 3 enemies inside query radius, 2 outside -> `TargetCount == 3`
- [x] SC-HA002-3: 65 requests submitted to solver -> first 64 processed, no crash
- [x] SC-HA003-2: `EqsTargetPool` slots all zeroed after `AreaQueryInitializationSystem.Execute`

### TASK-HA010 (Commander setup)
- [x] SC-HA010-1: `TotalSlots = Max(1, (int)(length / spacing))`, clamped to 16
- [x] SC-HA010-2: All bitmasks initialized to 0; `CachedEqsRequestId = -1`; `ActiveAttackerCount = 0`
- [x] SC-HA010-3: Returns `Success`
- [x] SC-HA010-4: `Action_DispatchAllToBaseline` publishes `AssignTacticalIntentEvent` for each alive subordinate
- [x] SC-HA010-5: Interpolates baseline coordinates per roster position
- [x] SC-HA010-6: Sets `BaselineReservedMask` bits
- [x] SC-HA010-7: `Condition_AreAllAtBaseline` returns `Success` when all alive subordinates have `Arrived`

### TASK-HA011 (EQS integration)
- [x] SC-HA011-1: `Action_RequestAreaQuery` calls `RequestAreaQuery`; stores `CachedEqsRequestId`; returns `Success`
- [x] SC-HA011-2: Duplicate request guard: if `CachedEqsRequestId != -1`, returns `Running`
- [x] SC-HA011-3: Batch full -> returns `Running`
- [x] SC-HA011-4: `Condition_IsAreaQueryResolved` returns `Running` while result pending
- [x] SC-HA011-5: Result with `TargetCount > 0` -> caches `CachedTargetGroupHandle`, returns `Success`

### TASK-HA012 (Wave dispatch and completion)
- [x] SC-HA012-1: `Action_DispatchWaveWithTargets` resets `WaveUsedSlotsMask = 0`
- [x] SC-HA012-2: Dispatches `HullDownAttackRun` intent for each alive tank
- [x] SC-HA012-3: `targetNetId` = `NetworkIdentity.Value` (not local entity handle); round-robin target assignment
- [x] SC-HA012-4: Resets `CachedTargetGroupHandle = -1` and toggles `CurrentWave`
- [x] SC-HA012-5: `Condition_IsWaveCompleted` returns `Running` while active attackers remain
- [x] SC-HA012-6: Dead attacker -> `BurnedSlotsMask` set, entry swap-removed
- [x] SC-HA012-6b: `HasStartedRun = 0` + hash matches `HullDownAttackRun` -> set to 1, NOT removed
- [x] SC-HA012-6c: `HasStartedRun = 1` + hash no longer matches -> clear baseline bit, swap-remove
- [x] SC-HA012-7: `Entity.Index % 2` wave parity stable after roster compaction (not roster index)
- [x] SC-HA012-8: Returns `Success` when `ActiveAttackerCount == 0`

### TASK-HA013 (BTree definition)
- [x] SC-HA013-1: `FbtTreeCatalog.GetPlatoonHillAttack()` returns non-null compiled tree
- [x] SC-HA013-2: BTree topology matches spec (Sequence -> CalculateSegments -> DispatchAllToBaseline -> AreAllAtBaseline -> Repeater -> Sequence -> ...)
- [x] SC-HA013-3: `AiBehaviorFactory` registers `PlatoonHillAttack_BT = 3014` with correct `HeavyDtoType`

### TASK-HA016 (JSON DTO and ParseParams)
- [x] SC-HA016-1: `PlatoonHillAttackParamsJsonDto` deserializes all required fields
- [x] SC-HA016-2: `ParsePlatoonHillAttackParams` computes `AttackDir` as left-hand perpendicular to firing line vector
- [x] SC-HA016-3: `ToCartesian` called for all geographic coordinates
- [x] SC-HA016-4: Unresolvable `TargetAreaNetworkId` -> `Entity.Null`, no exception
- [x] SC-HA016-5: `sizeof(PlatoonHillAttackParams) == 52`
- [x] SC-HA016-6: `TankSpacing` defaults to 30f when absent from JSON

---

## Files Changed

| File                                                                                        | Change                                              |
|---------------------------------------------------------------------------------------------|-----------------------------------------------------|
| `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs`                                   | Count 2->3 in 3 tests (Corrective-0)                |
| `Hrot/Subsystems/Hrot.SimHost/CognitiveComponentRegistry.cs`                                | Added `Blackboard1024` registration                 |
| `Hrot/Subsystems/Hrot.CGF/CgfComponentRegistry.cs`                                          | Added `Blackboard1024` registration                 |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`                           | `GetSingletonManaged`, `loco.Status` fix, BTree definition |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`                      | NEW: all commander nodes + `ParsePlatoonHillAttackParams` |
| `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`                                    | `PlatoonHillAttack_BT = 3014` registration          |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs`                              | Added `PlatoonHillAttack_BT = 3014` constant        |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/PlatoonHillAttackParamsJsonDto.cs`           | NEW: JSON DTO with `[BehaviorContract]`             |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbBuilder.cs`                                 | Added `WithHeavyMemory(long tkbId)` method          |
| `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbCatalog.cs`                                 | `Unit_TankPlatoon` (301) and `Unit_TankPlatoon_Auto` (303) updated |
| `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackNodeTests.cs`                                  | NEW: 46 tests                                       |

---

## Developer Insights

**Q1: What integration issues did you hit when wiring the commander nodes to the BTree builder (especially `[SharedAiHeavyAction]` with `Unsafe.As` projection)?**

The main friction was distinguishing `[BTreeAction]` (3-argument unmanaged form used by
tank nodes) from `[SharedAiHeavyAction]` (5-argument heavy-memory form used by commander
nodes). Commander nodes project `Blackboard1024.Memory` bytes directly to
`HillAttackMutableState` via `Unsafe.As<byte, HillAttackMutableState>(ref bb.Memory[0])`.
This is zero-allocation by design, but it requires that `HillAttackMutableState` fits
within `Blackboard1024.MemorySize` bytes (verified: `sizeof(HillAttackMutableState) <= 1024`).

The builder wiring for heavy actions uses `.HeavyAction(bb => bb.Params, MethodName)`,
while the regular actions use `.Action(bb => bb.Params, MethodName)`. Missing the
`[SharedAiHeavyAction]` attribute on a heavy-memory method causes a build error due to
the overload ambiguity in the FBT builder; this made the distinction easy to catch at
compile time.

**Q2: How did you handle the JSON format for `AssignTacticalIntentEvent` in `Action_DispatchAllToBaseline`? What existing format did you match?**

The `MoveToLocation` JSON format was taken directly from `HullDownAttackMapper.cs` and
the existing `ParseHullDownAttackRunParams` reference in `AiBehaviorFactory.cs`:

```json
{"X":<bx>,"Y":<by>,"Speed":15.0}
```

Baseline coordinates are interpolated per roster position:
`bx = p.BaselineStartX + (i / (float)(count - 1)) * (p.BaselineEndX - p.BaselineStartX)`,
clamped to the start point when `count == 1`. Speed is fixed at 15.0 m/s (approach
speed) to match the existing tactical intent format.

**Q3: What did you observe about the wave dispatch algorithm edge cases (e.g., fewer slots than tanks, all baseline slots reserved)?**

Two notable edge cases:

1. **Fewer available slots than tanks**: The bitmask probe `~(BurnedSlotsMask | WaveUsedSlotsMask)`
   may produce zero available bits within the `TotalSlots` range. The implementation
   falls back to slot 0 in this case (first slot, regardless of burnt/used state) to
   avoid skipping alive tanks. This is a graceful degradation: the tank still advances,
   just without slot uniqueness. The spec did not specify a priority for this case.

2. **All baseline slots reserved**: `BaselineReservedMask` accumulates across waves, and
   all `TotalSlots` bits may be set if the previous wave's `Condition_IsWaveCompleted`
   did not clear them (e.g., all attackers died). The dispatch falls back to baseline
   position 0. A future improvement (DEBT-TRACKER) should add a staleness check that
   clears `BaselineReservedMask` when `BurnedSlotsMask` covers all slots.

3. **`Entity.Index % 2` parity vs roster-index parity**: The batch instructions explicitly
   required `Entity.Index % 2` for wave assignment. This is correct: when a tank dies,
   `UnitHierarchySystem` swap-removes its entry from `UnitRoster`, shifting all subsequent
   roster indices. An entity's `Index` is fixed for its lifetime, so `Entity.Index % 2`
   is stable across roster compaction. Using `i % 2` (roster index) would reassign live
   tanks to the wrong wave after any death.

**Q4: Did the TKB blueprint changes require any schema/format investigation? What format is actually used?**

The TKB blueprint system uses pure C# code (not JSON/YAML files). Blueprints are
defined programmatically in `BdcTkbCatalog.cs` via a fluent builder API. The format is:

```csharp
builder.Define(TkbEntityTypes.Unit_TankPlatoon, name: "TankPlatoon")
    .WithBehavior(TkbEntityTypes.Unit_TankPlatoon)
    .WithHeavyMemory(TkbEntityTypes.Unit_TankPlatoon)
    .AsComposite(...)
    .Build();
```

Adding `Blackboard1024` required adding a `WithHeavyMemory(long tkbId)` convenience
method to `BdcTkbBuilder.cs` (which calls `template.AddComponent(new Blackboard1024())`
on the template returned by `_db.GetByType(tkbId)`). The method mirrors the existing
`WithBehavior` pattern.

No JSON/YAML schema investigation was needed. `AddComponent` stores closures that are
applied when `ApplyTo(repo, entity)` is called at runtime; there is no external data file.

---

## Bug Fixes Applied During This Batch

### Bug 1: `GetSingleton<NetworkEntityMap>` throws `NotSupportedException`

`NetworkEntityMap` is a managed class (it holds a `Dictionary<long, Entity>`). The
generic `GetSingleton<T>()` routes to `UnsafeShim.GetSingletonManaged<T>()` for managed
types, which was not implemented and threw `NotSupportedException("Managed Singleton
Ref Access not implemented")`. Fixed in `HillAttackTankNodes.cs` by switching to
`GetSingletonManaged<NetworkEntityMap>()` with a null guard. Tests updated to use
`repo.SetSingletonManaged<NetworkEntityMap>(new NetworkEntityMap())`.

### Bug 2: `loco.Status` never set to `Running` in `Action_CreepToAndBeyondSlot`

`LocomotionChannel.Status` has default value `NodeStatus.Failure (0)`. The node checked
`loco.Status != NodeStatus.Running || loco.Speed != p.Speed || ...` to decide whether
to write a new locomotion command. On the first call, `needsWrite = true` (correct).
But after writing, `loco.Status` was left at `Failure` (the newly-written struct was not
assigned back to the component). So on all subsequent calls `needsWrite` was again true,
causing redundant writes and incrementing `ActionInstanceId` on every tick.

Fix: added `loco.Status = NodeStatus.Running;` inside the `if (needsWrite)` block before
writing the component back. SC-HA007-6 verifies this by calling the node twice and
asserting `ActionInstanceId` is incremented only once.

### Bug 3: `CachedTargetGroupHandle = -1` in SC-HA012_3 broke pool probe

The pool probe in `Action_DispatchWaveWithTargets` uses `CachedTargetGroupHandle` as the
base index into `EqsTargetPool.Targets`. With `handle = -1`, the pool index for the
first target is `-1 + 0 = -1`, which is out of range, so `GetTargetFromPool` returns
`0L` immediately. The test was fixed to use `CachedTargetGroupHandle = 0` (matching the
pool layout: targets written to indices 0 and 1).

### Bug 4: `NetworkIdentity` not registered in `HillAttackNodeTests.CreateWorld()`

`NetworkIdentity` is registered by `HrotSharedComponentRegistry.RegisterAll()`, which is
called by `SimHostComponentRegistry.RegisterAll()`. No additional call was needed — the
`CreateWorld()` method already invokes `SimHostComponentRegistry.RegisterAll(repo)`.
The issue was that the initial test scaffold called `repo.RegisterComponent<NetworkIdentity>()`
redundantly; the duplicate call was harmless but clarified ownership.

---

## Outstanding Issues / Next Steps

- [ ] TASK-HA004 (multi-node EQS network translators) — deferred to BATCH-03
- [ ] TASK-HA015 (scenario integration test) — deferred to BATCH-03
- [ ] DEBT-TRACKER P2-02: `AreaQueryInitializationSystem` stale-world edge case (graceful degrade when `AreaQueryBatchData` not yet set)
- [ ] DEBT-TRACKER P2-03: `BaselineReservedMask` staleness when all slots burned (all-dead wave edge case; current fallback is slot 0)
