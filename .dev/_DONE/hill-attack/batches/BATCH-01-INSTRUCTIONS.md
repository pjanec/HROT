# BATCH-01: EQS Infrastructure + Data Contracts + Tank Behavior

**Batch Number:** BATCH-01  
**Tasks:** TASK-HA001, TASK-HA002, TASK-HA003, TASK-HA005, TASK-HA006, TASK-HA007, TASK-HA008, TASK-HA009  
**Phase:** Phase 1 (EQS Infrastructure) + Phase 2 (Data Contracts) + Phase 3 (HullDownAttackRun)  
**Estimated Effort:** 16-20 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## Onboarding & Workflow

### Developer Instructions

This batch lays the complete foundation for the Hill Attack feature: the EQS batch
pipeline, all unmanaged DTOs, and the full subordinate tank behavior (`HullDownAttackRun`).
After this batch, a tank can receive a `HullDownAttack` tactical intent, execute a creep-
aim-fire-retreat cycle, and the EQS infrastructure exists for the commander to query
enemies inside a polygon area.

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/hill-attack/ONBOARDING.md` - Folder layout, build/test commands
2. **Design:** `.dev/hill-attack/DESIGN.md` - Architecture overview; focus on Phase 1
   (sections 1.1-1.4), Phase 2 (sections 2.1-2.3), and Phase 3 (sections 3.1-3.3)
3. **Task Details:** `.dev/hill-attack/TASK-DETAIL.md` - Per-task success conditions for
   TASK-HA001, HA002, HA003, HA005, HA006, HA007, HA008, HA009
4. **AI Dev Guide:** `docs/AI_DEV_GUIDE.md` - Engine-wide AI patterns; read before coding
   any behavior nodes
5. **Pathfinding Reference:** `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingBatchData.cs`
   - The exact structural pattern for the new `AreaQueryBatchData` singleton
6. **Existing Node Pattern:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`
   - How `[SharedAiAction]`, `[SharedAiCondition]`, `[WritesChannel]` attributes are used
7. **Mapper Pattern:** `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs`
   - How `ITacticalOrderMapper` is implemented and registered
8. **Module Pattern:** `Hrot/Subsystems/Hrot.SimHost/Modules/EyesAndMuscleModule.cs`
   - How SoD modules with `ExecutionPolicy` are wired

### Source Code Location

- **EQS types (new):** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`
- **Component IDs:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **DTOs (new):** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs`
- **Tank behavior nodes (new):** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`
- **Mapper (new):** `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/HullDownAttackMapper.cs`
- **Registration:** `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`
- **EQS Solver (new):** `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs`
- **EQS Module (new):** `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs`
- **CGF Init system (new):** `Hrot/Subsystems/Hrot.CGF/Systems/AreaQueryInitializationSystem.cs`
- **Test projects:**
  - `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
  - `Hrot/Subsystems/Hrot.CGF/` (unit tests alongside source if no dedicated test project)
  - `FDP/Toolkits/Fdp.Toolkits.Tests/` (for AreaQueryBatchData unit tests)

### Build and Test Commands

```bat
:: Full solution build from workspace root
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln

:: Run all tests (from workspace root)
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-build

:: Run only SimHost tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build
```

### Report Submission

**When done, write your report to:**  
`.dev/hill-attack/reports/BATCH-01-REPORT.md`

**If you have blocking questions, create:**  
`.dev/hill-attack/questions/BATCH-01-QUESTIONS.md`

---

## Context

This batch implements the EQS (Environment Query System) infrastructure that allows
Brain-tier AI to asynchronously query which entities lie inside a polygon area, plus all
the unmanaged data contracts for both behaviors, plus the full `HullDownAttackRun`
subordinate tank behavior. The pattern mirrors the existing
`PathfindingBatchData`/`RaycastBatchData` batch singletons.

---

## Mandatory Workflow: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TASK-HA001** (EQS types) - Implement -> Write unit tests -> ALL tests pass
2. **TASK-HA002** (EQS solver) - Implement -> Write unit tests -> ALL tests pass
3. **TASK-HA003** (EQS init system) - Implement -> Write unit tests -> ALL tests pass
4. **TASK-HA005** (Commander DTOs) - Implement -> Write unit tests -> ALL tests pass
5. **TASK-HA006** (Tank DTOs) - Implement -> Write unit tests -> ALL tests pass
6. **TASK-HA007** (Condition_HasTarget + Action_CreepToAndBeyondSlot) - Implement -> Write unit tests -> ALL tests pass
7. **TASK-HA008** (Action_AimAndFireSpecific + Action_ReverseToBaseline) - Implement -> Write unit tests -> ALL tests pass
8. **TASK-HA009** (BTree + Mapper + Registration) - Implement -> Write unit tests -> ALL tests pass

**DO NOT** move to the next task until:
- Current task implementation is complete
- Current task tests are written and meaningful (see Test Quality below)
- **ALL tests passing** (run `dotnet test` and confirm)

Do NOT stop to ask for permission to run tests, fix compilation errors, or fix failing
tests. Work autonomously until all success criteria are met and all tests pass, then write
the report.

---

## Tasks

### TASK-HA001: AreaQueryBatchData Types and Component Registration

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA001  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 1, sections 1.1 and 1.2

**Files to create:**
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchData.cs` (NEW)
- `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/AreaQueryBatchHelper.cs` (NEW)

**Files to modify:**
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` - add `AreaQueryBatchData = 202` and
  `EqsTargetPool = 203`

**Critical implementation notes:**
- Mirror `PathfindingBatchData.cs` structure exactly (capacity 64, `NativeArray` fields)
- `AreaQueryBatchHelper.RequestAreaQuery` MUST cast `entityIndex` to `long` BEFORE
  shifting: `((long)entityIndex << 32) | (uint)batchSlot`. Shifting a 32-bit int by 32+
  silently truncates for entity indices above 4095.
- `EqsTargetPool` capacity = `64 * PerceptionConstants.MaxTrackedTargets * 4`
- All structs must have `[StructLayout(LayoutKind.Sequential)]`
- No managed types anywhere

**Success conditions to verify:** SC-HA001-1 through SC-HA001-4 (all in TASK-DETAIL.md)

---

### TASK-HA002: AreaQuerySolverSystem

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA002  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 1, section 1.3

**Files to create:**
- `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs` (NEW)
- `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` (NEW)

**Critical implementation notes:**
- Runs on Muscle node; reads via `ISimulationView` snapshot; writes via command buffer
- Point-in-polygon must use zero heap allocations (Span or stack-alloc for geometry)
- Filter by `EntityInfo.ForceId == req.TargetForce`
- If `SpatialGridData` absent, skip frame gracefully (no exception)
- If `TargetAreaEntity` dead or lacks `EditablePolyline`, write `IsReady=true, TargetCount=0`
- Do NOT reset `AreaQueryBatchData.Count` - that is done by `AreaQueryInitializationSystem`
- Use `IsReady` flag to skip already-processed requests within same solver cycle
- `EqsModule.Policy` must return `ExecutionPolicy.SlowBackground(10)`
- Register `EqsModule` in Muscle node module host configuration

**Success conditions to verify:** SC-HA002-1 through SC-HA002-4

---

### TASK-HA003: AreaQueryInitializationSystem

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA003  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 1, section 1.4

**Files to create:**
- `Hrot/Subsystems/Hrot.CGF/Systems/AreaQueryInitializationSystem.cs` (NEW)

**Files to modify:**
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` (or wherever `CgfLogicPack` registers
  systems) - register at `SystemPhase.PreInput`, BEFORE `BTreeTickSystem`

**Critical implementation notes:**
- Must run BEFORE `BTreeTickSystem` and BEFORE `AreaQueryBrainEgressTranslator`
- Resets `AreaQueryBatchData.Count = 0`
- Resets entire `EqsTargetPool` free list (all pool chunks zeroed)
- If `AreaQueryBatchData` singleton is absent, do nothing (no crash)

**Success conditions to verify:** SC-HA003-1 through SC-HA003-3

---

### TASK-HA005: Commander DTOs

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA005  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 2, sections 2.1 and 2.3

**Files to create:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs` (NEW)

**Commander-side structs:**
- `PlatoonHillAttackParams` (52 bytes, `[StructLayout(LayoutKind.Sequential)]`)
- `PlatoonHillAttackBlackboard` (wrapper with one field: `Params`)
- `HillAttackMutableState` (projected onto `Blackboard1024.Memory`)

**Critical notes:**
- `sizeof(PlatoonHillAttackParams)` must equal exactly 52 (test this!)
- `sizeof(HillAttackMutableState)` must be <= 1024 (test this!)
- `CachedEqsRequestId` is `long`; `CachedTargetGroupHandle` is `int` initialized to -1
- `fixed` arrays are size 8 (`ActiveEntityPacked`, `ActiveSlotIndex`,
  `ReturnBaselineSlotIndex`, `HasStartedRun`)
- All fields must respect natural alignment to avoid padding surprises

**Success conditions to verify:** SC-HA005-1 through SC-HA005-4

---

### TASK-HA006: Tank DTOs

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA006  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 2, section 2.2

**Files to modify:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs` (ADD to existing)

**Tank-side structs (add to same file):**
- `HullDownAttackParams` (40 bytes)
- `HullDownAttackBlackboard` (wrapper with one field: `Params`)

**Critical notes:**
- `TargetNetworkId` is `long` (network-stable ID, NOT a local ECS Entity handle)
- `sizeof(HullDownAttackParams)` must equal 40 (test this!)

**Success conditions to verify:** SC-HA006-1 through SC-HA006-2

---

### TASK-HA007: Condition_HasTarget and Action_CreepToAndBeyondSlot

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA007  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 3, sections 3.1-3.2

**Files to create:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` (NEW)

**Critical notes for Condition_HasTarget:**
- Attribute: `[SharedAiCondition(typeof(HullDownAttackBlackboard), nameof(HullDownAttackBlackboard.Params))]`
- Resolves `p.TargetNetworkId` via `NetworkEntityMap.TryGetEntity`; returns Failure if not found
- Scans `TargetMemory` (bounded by `PerceptionConstants.MaxTrackedTargets = 4`); no allocations
- Returns Success only if `ThreatScores[i] > 0f`

**Critical notes for Action_CreepToAndBeyondSlot:**
- Attributes: `[SharedAiAction(...)]`, `[WritesChannel(ChannelKind.Locomotion)]`
- NEVER returns `NodeStatus.Success`; returns Running (normal) or Failure (overshoot)
- Overshoot = distance from tank to slot > `HillAttackConstants.MaxOvershootMeters` (50m)
  WHEN the tank is already past the slot along the attack direction
- Only increment `ActionInstanceId` when the command actually changes (check current
  `loco.ActiveAction` and speed before writing)
- Phase 1 (far from slot): Destination = slot pos, Speed = ApproachSpeed
- Phase 2 (near slot, not overshot): Destination = currentPos + AttackDir * 10000f,
  Speed = CreepSpeed

**Add `HillAttackConstants` class:** `MaxOvershootMeters = 50f` and any other constants
needed by the tank nodes.

**Success conditions to verify:** SC-HA007-1 through SC-HA007-6

---

### TASK-HA008: Action_AimAndFireSpecific and Action_ReverseToBaseline

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA008  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 3, sections 3.1-3.2

**Files to modify:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` (ADD nodes)

**Critical notes for Action_AimAndFireSpecific:**
- Attribute: `[WritesChannel(ChannelKind.Weapon)]`
- If `NetworkEntityMap.TryGetEntity` fails: return Failure immediately
- If `!repo.IsAlive(targetEntity)`: return Success immediately (prevents stuck Running)
- Only write `WeaponChannel` if `ActiveAction != ActionIdAimAndFire` OR
  `Status == NodeStatus.Failure`
- Return Running while weapon channel Running; Success when weapon channel Success or
  target dead; Failure when weapon channel Failure

**Critical notes for Action_ReverseToBaseline:**
- Attribute: `[WritesChannel(ChannelKind.Locomotion)]`
- Destination = `(p.BaselineX, p.BaselineY)` with reverse flag enabled
- Check `CarKinem.Core` MoveToParams struct for the correct reverse flag field name
- Returns Running/Success/Failure mirroring LocomotionChannel.Status

**Success conditions to verify:** SC-HA008-1 through SC-HA008-5

---

### TASK-HA009: HullDownAttackRun BTree, Mapper, and Registration

**Task Details:** `.dev/hill-attack/TASK-DETAIL.md` section TASK-HA009  
**Design Reference:** `.dev/hill-attack/DESIGN.md` Phase 3, sections 3.2-3.3

**Files to create:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/HullDownAttackMapper.cs` (NEW)

**Files to modify:**
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` (ADD BTree definition)
- `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs` (ADD registration)

**BTree topology (must match exactly):**
```
Sequence
  Selector
    Sequence
      Selector([Condition_HasTarget, Action_CreepToAndBeyondSlot])
      Action_AimAndFireSpecific
    Action_AbortEngagement
  Action_ReverseToBaseline
```

`Action_AbortEngagement` is a trivial node: `[SharedAiAction] return NodeStatus.Success;`  
It ensures `Action_ReverseToBaseline` always runs even on overshoot failure.

**BTree definition method signature:**  
`static Interpreter<BrainBlackboard, BTreeContext> GetHullDownAttackRun()`  
decorated with `[BTreeDefinition("HullDownAttackRun")]`

**Mapper:** `HullDownAttackMapper : ITacticalOrderMapper`
- `TargetIntentId` = `"HullDownAttack"`
- `TryMap`: verify entity has `TkbIdentity` + is a tank type (use `TkbEntityTypes` constants)
- Sets `assignment.BehaviorName = "HullDownAttackRun"`, passes `JsonParams` verbatim

**Registration in `AiBehaviorFactory`:**
- Add `private const uint HullDownAttackRun_BehaviorId = <next available value>`
- Register the behavior definition
- Add `HullDownAttackMapper` to `TacticalIntentMapperRegistry`

**Success conditions to verify:** SC-HA009-1 through SC-HA009-5

---

## Test Quality Requirements

**Minimum test coverage for this batch: 25-35 unit tests** covering:

**TASK-HA001 (5+ tests):**
- SC-HA001-1: 64 distinct RequestIds on full batch; 65th returns -1
- SC-HA001-2: Pool reset produces all-zero values; fresh alloc reads zero
- SC-HA001-3: `sizeof(AreaQueryRequest)` and `sizeof(AreaQueryResult)` are deterministic
- SC-HA001-4: Component IDs 202 and 203 are correct and unique

**TASK-HA002 (4+ tests):**
- SC-HA002-1: 3 enemies inside polygon -> TargetCount=3, IsReady=true
- SC-HA002-2: Dead TargetAreaEntity -> IsReady=true, TargetCount=0, no exception
- SC-HA002-3: 65 requests -> first 64 normal, 65th skipped without crash
- SC-HA002-4: EqsModule.Policy == SlowBackground(10)

**TASK-HA003 (3+ tests):**
- SC-HA003-1: Count reset to 0 after Execute
- SC-HA003-2: Pool reset to all-zero after Execute
- SC-HA003-3: System registered at PreInput before BTreeTickSystem

**TASK-HA005 + HA006 (5+ tests):**
- sizeof checks (SC-HA005-1, SC-HA006-1)
- Blittable checks (SC-HA005-3, SC-HA006-2)
- Fixed array read/write (SC-HA005-4)

**TASK-HA007 (6+ tests):**
- Condition_HasTarget success/failure cases (SC-HA007-1, HA007-2)
- Action_CreepToAndBeyondSlot Running while under limit (SC-HA007-3)
- Action_CreepToAndBeyondSlot Failure at overshoot (SC-HA007-3b)
- Speed and destination checks (SC-HA007-4, HA007-5)
- No redundant ActionInstanceId increment (SC-HA007-6)

**TASK-HA008 (5+ tests):**
- Weapon channel written once (SC-HA008-1, HA008-2)
- Target dead -> Success (SC-HA008-3b)
- Locomotion destination matches baseline (SC-HA008-4)

**TASK-HA009 (3+ tests):**
- Mapper TargetIntentId (SC-HA009-1)
- Tank entity -> TryMap true (SC-HA009-2)
- Non-tank entity -> TryMap false (SC-HA009-3)

**Test Quality Standard (NON-NEGOTIABLE):**
- Tests MUST assert actual values and behaviors, not just "no exception was thrown"
- Tests MUST NOT rely on checking string contents of generated code
- Tests MUST use minimal in-process test fixtures (no live simulation required for unit tests)
- Every success condition in TASK-DETAIL.md MUST have at least one matching test

---

## Quality Standards

**Code quality:**
- All unmanaged structs: `[StructLayout(LayoutKind.Sequential)]`
- No managed heap allocations in hot-path behavior nodes
- All early failure paths are explicit: fail loud, fail fast
- No swallowed exceptions or silent error paths

**Architecture:**
- Behavior nodes never mutate ECS structure directly
- `TargetNetworkId` in params; local entity resolved at runtime
- `[WritesChannel]` attribute on every action that writes a channel

---

## Success Criteria (Batch Complete When)

- [ ] TASK-HA001: `AreaQueryBatchData`, `EqsTargetPool`, `AreaQueryBatchHelper` implemented and tested
- [ ] TASK-HA002: `AreaQuerySolverSystem` + `EqsModule` implemented and registered
- [ ] TASK-HA003: `AreaQueryInitializationSystem` registered at PreInput in `CgfLogicPack`
- [ ] TASK-HA005: `PlatoonHillAttackParams` (52 bytes), `HillAttackMutableState` (<= 1024 bytes) defined
- [ ] TASK-HA006: `HullDownAttackParams` (40 bytes) defined
- [ ] TASK-HA007: `Condition_HasTarget` and `Action_CreepToAndBeyondSlot` implemented
- [ ] TASK-HA008: `Action_AimAndFireSpecific` and `Action_ReverseToBaseline` implemented
- [ ] TASK-HA009: `HullDownAttackRun` BTree, `HullDownAttackMapper`, and registration complete
- [ ] All 25+ unit tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with zero errors and zero warnings
- [ ] Report submitted to `.dev/hill-attack/reports/BATCH-01-REPORT.md`

---

## Developer Insights Questions (Answer in Report)

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase (EQS infrastructure,
behavior node framework, batch singleton pattern)? What would you improve?

**Q3:** What design decisions did you make beyond the specifications? What alternatives
did you consider?

**Q4:** What edge cases did you discover that weren't explicitly covered in the success
conditions?

**Q5:** Are there any performance concerns or allocation risks you identified in the
hot path?

**Q6:** What is the actual `sizeof(HillAttackMutableState)` and what are the dominant
fields contributing to its size?

---

## Reference Materials

- **Task Details:** `.dev/hill-attack/TASK-DETAIL.md` - Full success conditions for all tasks
- **Design:** `.dev/hill-attack/DESIGN.md` - Architecture and phase breakdown
- **Onboarding:** `.dev/hill-attack/ONBOARDING.md` - Folder layout reference
- **Pattern reference:** `FDP/Toolkits/Fdp.Toolkits/Navigation/PathfindingBatchData.cs`
- **Node patterns:** `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs`
- **Mapper pattern:** `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs`
- **Module pattern:** `Hrot/Subsystems/Hrot.SimHost/Modules/EyesAndMuscleModule.cs`
