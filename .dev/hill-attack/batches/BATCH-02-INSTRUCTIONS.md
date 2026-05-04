# BATCH-02: Corrective Fixes + Commander Behavior + Blueprint + JSON DTO

**Batch Number:** BATCH-02  
**Tasks:** Corrective-0 (CgfLogicPackTests fix), Corrective-1 (missing node tests),
TASK-HA010, TASK-HA011, TASK-HA012, TASK-HA013, TASK-HA014, TASK-HA016  
**Phase:** Phase 4 (PlatoonHillAttack Commander) + Phase 5 (Blueprint) + Phase 6 (JSON DTO)  
**Estimated Effort:** 16-20 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 completed

---

## Onboarding & Workflow

### Developer Instructions

This batch fixes a P1 regression from BATCH-01, adds the missing behavior node unit
tests, and implements the full PlatoonHillAttack commander behavior (BTree definition,
all commander nodes, blueprint update, JSON DTO).

After this batch, the complete hill attack feature is implemented end-to-end except for
multi-node EQS network translators (TASK-HA004) and the scenario integration test
(TASK-HA015), which are saved for BATCH-03.

### Required Reading (IN ORDER)

1. **BATCH-01 Review:** `.dev/hill-attack/reviews/BATCH-01-REVIEW.md` — understand what
   went wrong and what corrective tasks are required
2. **Onboarding:** `.dev/hill-attack/ONBOARDING.md`
3. **Design (Phase 4 + Phase 6):** `.dev/hill-attack/DESIGN.md` — sections 4.1–4.5 and
   6.1–6.3
4. **Task Details (HA010–HA016):** `.dev/hill-attack/TASK-DETAIL.md`
5. **Debt Tracker:** `.dev/hill-attack/DEBT-TRACKER.md` — P2-01, P2-02, P2-03
6. **AI Dev Guide:** `docs/AI_DEV_GUIDE.md`
7. **Existing commander node patterns:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`
   (specifically `[SharedAiHeavyAction]` usage)
8. **BATCH-01 node implementation:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`
   — understand the `BTreeContext` access patterns used
9. **BATCH-01 mapper:** `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/HullDownAttackMapper.cs`
10. **Blueprint format:** find TKB blueprint files in `Hrot/` or `Data/` directory and
    read how `Blackboard1024` is declared in existing commander entities

### Source Code Locations

- **Commander nodes (new):** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`
- **BTree definitions:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` (add
  `PlatoonHillAttack` definition here or create `HillAttackBehaviorDefinitions.cs`)
- **JSON DTO (new):** locate the `Hrot.Map.Definitions.Behavior` namespace/project — see
  `Hrot/` directory for the correct project
- **Registration:** `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`
- **Tests:** `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
- **Test to fix:** `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs`
- **Node tests to add:** `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackNodeTests.cs` (NEW)

### Build and Test Commands

```bat
:: Full solution build
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln

:: Run SimHost tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build

:: Run all tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-build
```

### Report Submission

**Report goes to:** `.dev/hill-attack/reports/BATCH-02-REPORT.md`

---

## Mandatory Workflow: Test-Driven Task Progression

**CRITICAL: Complete tasks in sequence with passing tests at each step.**

0. **Corrective-0:** Fix CgfLogicPackTests -> ALL tests pass (including the 3 that were broken)
1. **Corrective-1:** Add missing HA007/HA008/HA009 tests -> ALL tests pass
2. **TASK-HA010:** Commander setup nodes -> Write tests -> ALL tests pass
3. **TASK-HA011:** EQS integration nodes -> Write tests -> ALL tests pass
4. **TASK-HA012:** Wave control nodes -> Write tests -> ALL tests pass
5. **TASK-HA013:** PlatoonHillAttack BTree + Registration -> Write tests -> ALL tests pass
6. **TASK-HA014:** TKB Blueprint Updates -> Verify -> ALL tests pass
7. **TASK-HA016:** JSON DTO + ParseParams -> Write tests -> ALL tests pass

**DO NOT** move to the next task until ALL tests pass. Do NOT stop to ask permission
for obvious steps. Work autonomously until the full batch is done.

---

## Corrective Tasks (P1 — Must Fix First)

### Corrective-0: Fix CgfLogicPackTests Regression

**File to modify:** `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs`

**Root Cause:** BATCH-01 added `AreaQueryInitializationSystem` as the first element in
`CgfLogicPack.InputSystems`, increasing the count from 2 to 3. Three tests that
check `Assert.Equal(2, pack.InputSystems.Count)` now fail.

**Fix:** In all three failing tests, change `Assert.Equal(2, pack.InputSystems.Count)`
to `Assert.Equal(3, pack.InputSystems.Count)` and update the accompanying comment to
reflect the addition:

```csharp
// InputSystems: AreaQueryInitializationSystem (1), MissionControlExecutionSystem (1),
//               BehaviorIngressSystem (1) = 3
Assert.Equal(3, pack.InputSystems.Count);
```

Failing tests:
- `CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException`
- `CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly`
- `CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup`

After fix, run `dotnet test` and confirm those 3 tests now pass.

### Corrective-1: Add Missing HA007/HA008/HA009 Tests

**File to create:** `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackNodeTests.cs`

Also improve existing EQS tests (P2-02 and P2-03 from DEBT-TRACKER.md):
- Add SC-HA002-1 test: 3 enemies inside, 2 outside -> TargetCount == 3
- Add SC-HA002-3 test: 65 requests submitted to solver -> first 64 processed, no crash
- Add SC-HA003-2 test: EqsTargetPool slots all zero after reset  
- Add SC-HA003-3 test: AreaQueryInitializationSystem is first in CgfLogicPack.InputSystems

**Required new tests in HillAttackNodeTests.cs:**

SC-HA007-1: Condition_HasTarget returns Success when target in TargetMemory with score > 0  
SC-HA007-2: Condition_HasTarget returns Failure when NetworkEntityMap cannot resolve  
SC-HA007-3: Action_CreepToAndBeyondSlot returns Running when not overshot  
SC-HA007-3b: Action_CreepToAndBeyondSlot returns Failure when overshot > 50m along attack dir  
SC-HA007-4: Speed in LocomotionChannel matches ApproachSpeed when far from slot  
SC-HA007-5: Speed matches CreepSpeed and destination is far along attack dir when near slot  
SC-HA007-6: ActionInstanceId NOT incremented on second identical call  
SC-HA008-1: Action_AimAndFireSpecific writes WeaponChannel; ActionInstanceId incremented once  
SC-HA008-2: Second call with Running status does NOT increment ActionInstanceId  
SC-HA008-3b: Target dead -> Action_AimAndFireSpecific returns Success immediately  
SC-HA008-4: Action_ReverseToBaseline writes destination (BaselineX, BaselineY)  
SC-HA008-5: Action_ReverseToBaseline returns Success when channel Status == Success  
SC-HA009-1: HullDownAttackMapper.TargetIntentId == "HullDownAttack"  
SC-HA009-2: Tank entity -> TryMap returns true, BehaviorName = "HullDownAttackRun"  
SC-HA009-3: Non-tank entity -> TryMap returns false  

Minimum 15 new tests in `HillAttackNodeTests.cs`. Tests MUST assert actual values,
not just "no exception was thrown."

---

## New Tasks

### TASK-HA010: Action_CalculateSegments, Action_DispatchAllToBaseline, Condition_AreAllAtBaseline

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA010  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 4, sections 4.1–4.3

**File to create:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`

All three nodes use the 5-argument `[SharedAiHeavyAction]` form that projects
`Blackboard1024.Memory` to `HillAttackMutableState` via `Unsafe.As`. Study existing
heavy action examples in `CgfNodes.cs` before implementing.

**Key constraints:**
- `Action_CalculateSegments`:
  - `TotalSlots = Max(1, (int)(segmentLength / p.TankSpacing))`, clamped to 16
  - Initialize ALL bitmasks to 0; `CachedEqsRequestId = -1`; `ActiveAttackerCount = 0`
  - Returns `NodeStatus.Success`
- `Action_DispatchAllToBaseline`:
  - Iterates `UnitRoster.Count`; for each alive subordinate interpolates baseline coords,
    publishes `AssignTacticalIntentEvent` with `IntentId = "MoveToLocation"`
  - Check `AiBehaviorFactory.ParseMoveToParams` or equivalent for JSON format
  - Reserves `BaselineReservedMask` bit for each dispatched tank
  - Returns `NodeStatus.Success`
- `Condition_AreAllAtBaseline`:
  - Returns Success when ALL alive subordinates have
    `NavigationStatus.Result == NavigationResult.Arrived`
  - Dead subordinates count as arrived (do NOT block deployment)

**Success conditions to verify:** SC-HA010-1 through SC-HA010-7

---

### TASK-HA011: Action_RequestAreaQuery and Condition_IsAreaQueryResolved

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA011  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 4, sections 4.1–4.3 and Phase 1
sections 1.1–1.2

**File to modify:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`

**Key constraints for Action_RequestAreaQuery:**
- If `CachedEqsRequestId != -1` and result not yet ready: return Running (no duplicate)
- If `CachedEqsRequestId == -1`: call `AreaQueryBatchHelper.RequestAreaQuery`
- If batch full (returns -1): return Running (try next frame)
- On success: store in `state.CachedEqsRequestId`, return Success

**Key constraints for Condition_IsAreaQueryResolved:**
- If `CachedEqsRequestId == -1`: return Failure (guard)
- Poll `AreaQueryBatchHelper.GetAreaQueryResult`
- Not ready: return Running
- Ready + TargetCount == 0: reset `CachedEqsRequestId = -1`, `CachedTargetGroupHandle = -1`,
  return **Failure** (breaks Repeater — area cleared)
- Ready + TargetCount > 0: cache `CachedTargetGroupHandle`, reset `CachedEqsRequestId = -1`,
  return **Success**

**Success conditions to verify:** SC-HA011-1 through SC-HA011-5

---

### TASK-HA012: Action_DispatchWaveWithTargets and Condition_IsWaveCompleted

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA012  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 4, sections 4.4 and 4.5

**File to modify:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`

**Critical: wave assignment uses `Entity.Index % 2`, NOT roster position `i % 2`.**
`UnitHierarchySystem` compacts `UnitRoster` on entity death; roster indices shift.
`Entity.Index` is immutable for the entity lifetime.

**Key constraints for Action_DispatchWaveWithTargets:**
- Reset `WaveUsedSlotsMask = 0` and `ActiveAttackerCount = 0` at start
- For each tank: pick random available slot from `~(BurnedSlotsMask | WaveUsedSlotsMask)`
  limited to `TotalSlots` bits
- Baseline slot: iterate all baseline indices, pick closest unreserved by distance-squared
  from firing slot position
- Targets round-robin: `targetIndex = activeTankIndexInWave % targetCount`
- Retrieve target via `AreaQueryBatchHelper.GetTargetFromPool(repo, CachedTargetGroupHandle, i)`
- Read `NetworkIdentity.NetworkId` for each target entity — serialize as `TargetNetworkId`
  in JSON (NOT the local packed entity handle)
- Reset `CachedTargetGroupHandle = -1`; toggle `CurrentWave`
- Returns `NodeStatus.Success`

**Key constraints for Condition_IsWaveCompleted:**
- Iterate SoA backwards (swap-remove pattern)
- Dead attacker: set `BurnedSlotsMask` bit, clear `BaselineReservedMask` bit, swap-remove
- `HasStartedRun == 0`: if hash matches `HullDownAttackRun`, set to 1 (do NOT remove)
- `HasStartedRun == 1`: if hash no longer matches, run finished — clear baseline bit,
  swap-remove
- Returns Success when `ActiveAttackerCount == 0`

Swap-remove: copy last entry over index `i`; decrement `ActiveAttackerCount`.

**Success conditions to verify:** SC-HA012-1 through SC-HA012-8

---

### TASK-HA013: PlatoonHillAttack BTree Definition and Registration

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA013  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 4, section 4.2

**BTree topology (must match exactly):**
```
Sequence
  Action_CalculateSegments
  Action_DispatchAllToBaseline
  Condition_AreAllAtBaseline
  Repeater(-1)
    Sequence
      Action_RequestAreaQuery
      Condition_IsAreaQueryResolved
      Action_DispatchWaveWithTargets
      Condition_IsWaveCompleted
```

**BTree definition method signature:**  
`[BTreeDefinition("PlatoonHillAttack")]`  
`static BTreeBuilder<PlatoonHillAttackBlackboard, BTreeContext> BuildPlatoonHillAttackTree()`

**Registration in AiBehaviorFactory:**
- Add `private const uint PlatoonHillAttack_BT = <next available after 3013>`
- `BehaviorDefinition.HeavyDtoType = typeof(HillAttackMutableState)`
- `ParseParams` delegate bound per TASK-HA016 (wire after implementing HA016)

**Success conditions to verify:** SC-HA013-1 through SC-HA013-3

---

### TASK-HA014: TKB Blueprint Updates

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA014  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 5, section 5.1

**Scope:** Data-only changes. Find TKB blueprint definition files (JSON or YAML) for:
1. Commander entity blueprint — add `Blackboard1024` component
2. Subordinate tank entity blueprints — verify presence of: `NavState`, `LocomotionChannel`,
   `WeaponChannel`, `TargetMemory`, `BrainBlackboard`, `BehaviorState`, `BrainBTreeState`,
   `UnitSubordinate`

Search for TKB blueprint files: `file_search("*.tkb.json")` or similar pattern in
the `Data/`, `Hrot/`, or scenario directories.

**Success conditions to verify:** SC-HA014-1 through SC-HA014-3  
Run TKB loader test suite after modification to confirm no validation errors.

---

### TASK-HA016: PlatoonHillAttack JSON DTO and ParseParams Delegate

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA016  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 6

**New file:** `PlatoonHillAttackParamsJsonDto.cs` in the `Hrot.Map.Definitions.Behavior`
namespace (find the correct project in `Hrot/` directory)

**ParseParams delegate:** Add static unsafe method `ParsePlatoonHillAttackParams` alongside
the commander node definitions (in `HillAttackCommanderNodes.cs` or a dedicated
`HillAttackIngress.cs`)

**Critical constraints:**
- `AttackDir` is computed NOT authored:
  ```csharp
  var fireVector = Normalize(FiringLineEnd - FiringLineStart);
  attackDir = new Vector2(-fireVector.Y, fireVector.X); // left-hand perpendicular
  ```
- Call `geoTransform.ToCartesian` at parse time (cold path only)
- If `TargetAreaNetworkId` unresolvable: write `Entity.Null` — no exception
- `TankSpacing` defaults to 30f when absent from JSON
- Bind in `AiBehaviorFactory`:
  `ParseParams = (json, ptr) => ParsePlatoonHillAttackParams(json, ptr, geoTransform, entityMap)`
- `geoTransform` and `entityMap` are injected into `AiBehaviorFactory` from DI container

**Success conditions to verify:** SC-HA016-1 through SC-HA016-6

---

## Test Quality Requirements

**Minimum additional tests for this batch: 35-45 tests**

**Corrective-1 (15 tests minimum):**
- All SC-HA007-1 through SC-HA009-3 listed in the corrective task above
- Plus SC-HA002-1 with 3 inside/2 outside, SC-HA002-3 overflow, SC-HA003-2 pool zero,
  SC-HA003-3 system ordering

**TASK-HA010 (7 tests):**
- SC-HA010-1 through SC-HA010-7 exactly

**TASK-HA011 (5 tests):**
- SC-HA011-1 through SC-HA011-5 exactly

**TASK-HA012 (8 tests):**
- SC-HA012-1 through SC-HA012-8 exactly (including SC-HA012-6, 6b, 6c for wave assignment)

**TASK-HA013 (3 tests):**
- SC-HA013-1 through SC-HA013-3

**TASK-HA016 (6 tests):**
- SC-HA016-1 through SC-HA016-6

**Test Quality (NON-NEGOTIABLE):**
- Tests MUST assert actual computed values (not just "no exception")
- Wave assignment tests must verify `Entity.Index % 2` parity stability after roster compaction
- JSON parsing tests must actually parse JSON and check the resulting struct fields
- Overshoot tests must measure actual dot-product result

---

## Quality Standards

- No managed allocations in heavy action hot-path nodes
- `Unsafe.As<HillAttackMutableState>` projection must be the only way mutable state is accessed
- `Entity.Index % 2` wave assignment (not roster-index parity)
- All JSON serialization happens in `ParseParams` cold path — never in BTree tick hot path

---

## Success Criteria (Batch Complete When)

- [ ] Corrective-0: `CgfLogicPackTests` - all 3 previously-broken tests now pass
- [ ] Corrective-1: 15+ new tests for HA007/HA008/HA009 nodes; all pass
- [ ] TASK-HA010: CalculateSegments, DispatchAllToBaseline, AreAllAtBaseline implemented + tested
- [ ] TASK-HA011: RequestAreaQuery, IsAreaQueryResolved implemented + tested
- [ ] TASK-HA012: DispatchWaveWithTargets, IsWaveCompleted implemented + tested
- [ ] TASK-HA013: PlatoonHillAttack BTree compiled (GetPlatoonHillAttack() accessible) + tested
- [ ] TASK-HA014: TKB blueprints updated; loader tests pass
- [ ] TASK-HA016: JSON DTO + ParseParams implemented + tested
- [ ] All 35+ new tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` — zero errors, zero warnings
- [ ] Total failing tests across the whole solution is now <= 6 (pre-existing failures only)
- [ ] Report written to `.dev/hill-attack/reports/BATCH-02-REPORT.md`

---

## Developer Insights Questions (Answer in Report)

**Q1:** What integration issues did you hit when wiring the commander nodes to the BTree
builder (especially `[SharedAiHeavyAction]` with Unsafe.As projection)?

**Q2:** How did you handle the JSON format for `AssignTacticalIntentEvent` in
`Action_DispatchAllToBaseline`? What existing format did you match?

**Q3:** What did you observe about the wave dispatch algorithm edge cases (e.g., fewer
slots than tanks, all baseline slots reserved)?

**Q4:** Did the TKB blueprint changes require any schema/format investigation? What format
is actually used?

**Q5:** What design decisions did you make beyond the spec in the commander nodes?

---

## Reference Materials

- **BATCH-01 review:** `.dev/hill-attack/reviews/BATCH-01-REVIEW.md`
- **Task Details:** `.dev/hill-attack/TASK-DETAIL.md` — HA010–HA016
- **Design:** `.dev/hill-attack/DESIGN.md` — Phase 4, sections 4.1–4.5; Phase 6
- **BATCH-01 implementation:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`
- **Debt Tracker:** `.dev/hill-attack/DEBT-TRACKER.md`
