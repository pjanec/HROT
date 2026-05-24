# Hill Attack Group Behavior — Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for the full architectural design.

---

## Phase 1: EQS Infrastructure

### TASK-HA001: AreaQueryBatchData Types and Component Registration

**Design Reference:** DESIGN.md — Phase 1, sections 1.1 and 1.2

**Scope:**
- Define `AreaQueryRequest` struct.
- Define `AreaQueryResult` struct.
- Define `AreaQueryBatchData` struct (singleton, `[ComponentId(202)]`).
- Define `EqsTargetPool` struct (singleton, allocate a companion ID, e.g. 203).
- Add constants `AreaQueryBatchData = 202` and `EqsTargetPool = 203` to
  `GlobalComponentIds` in `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`.
- Implement `AreaQueryBatchHelper` static class.
- Create files in `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/`.

**NOT included:** Solver system, network translators, behavior usage.

**Constraints:**
- `AreaQueryRequest` and `AreaQueryResult` must be unmanaged structs with
  `[StructLayout(LayoutKind.Sequential)]`.
- `AreaQueryBatchData` must allocate `NativeArray` buffers with `DefaultCapacity = 64`,
  matching the `PathfindingBatchData` pattern.
- `EqsTargetPool` must hold packed `long` entity handles in an unmanaged `NativeArray`.
  Pool capacity = `DefaultCapacity * PerceptionConstants.MaxTrackedTargets * 4` to support
  up to 64 concurrent queries with up to 16 results each (adjust as needed).
- No managed types anywhere in these structs.
- `DataPolicy` for `AreaQueryBatchData` and `EqsTargetPool`: none needed on the struct
  itself (singletons; data is transient by nature of being cleared each frame).
- `AreaQueryBatchHelper.RequestAreaQuery` must construct `RequestId` using
  `((long)entityIndex << 32) | (uint)batchSlot`. The cast to `long` before shifting is
  mandatory: a 20-bit left-shift on a 32-bit `int` silently truncates any entity index
  above 4095, producing colliding `RequestId` values.
- `AreaQueryInitializationSystem` (TASK-HA003) is responsible for resetting pool chunks
  globally at the start of each Brain frame. `AreaQueryBatchHelper` may expose a
  `ResetPool` method used by that system. Behavior nodes never call a manual free.

**Success Conditions:**

SC-HA001-1: Given `AreaQueryBatchData` is initialized with capacity 64, calling
`AreaQueryBatchHelper.RequestAreaQuery` 64 times returns 64 distinct non-negative
`RequestId` values. The 65th call returns -1.

SC-HA001-2: After `AreaQueryInitializationSystem` resets the pool, all pool slots read
as zero/default. A subsequent allocation into the reset pool reads all-zero values,
confirming no stale data leaks across frames.

SC-HA001-3: `sizeof(AreaQueryRequest)` and `sizeof(AreaQueryResult)` are verified at
compile time (static assert or unit test) to be deterministic unmanaged sizes.

SC-HA001-4: `GlobalComponentIds.AreaQueryBatchData == 202` and
`GlobalComponentIds.EqsTargetPool == 203`. No other component in the codebase uses
these IDs (verified by searching GlobalComponentIds and all project-specific ComponentIds
files).

---

### TASK-HA002: AreaQuerySolverSystem (Muscle Tier, SoD Module)

**Design Reference:** DESIGN.md — Phase 1, section 1.3

**Scope:**
- Implement `AreaQuerySolverSystem : IEcsModuleSystem` in `Hrot.SimHost`.
- Implement `EqsModule : IEcsModule` wrapping the solver, configured with
  `ExecutionPolicy.SlowBackground(10)`.
- Register `EqsModule` in the Muscle node's module host configuration.
- Files: `Hrot/Subsystems/Hrot.SimHost/Modules/EqsModule.cs` and
  `Hrot/Subsystems/Hrot.SimHost/Systems/AreaQuerySolverSystem.cs`.

**NOT included:** Network translators, Brain-side resolution.

**Constraints:**
- The solver runs on a background thread against a read-only `ISimulationView` snapshot.
  It must only read components; all writes go through `view.GetCommandBuffer()`.
- The solver must query `SpatialGridData` from the snapshot. If `SpatialGridData` is not
  present, the solver skips the frame gracefully (no exception, no partial writes).
- The polygon resolution must read `EditablePolyline` (managed component) via
  `view.GetManagedComponentRO<EditablePolyline>(req.TargetAreaEntity)`. If the area
  entity is not alive or lacks the component, skip that request and write
  `IsReady = true` with `TargetCount = 0` so the Brain does not block indefinitely.
- Point-in-polygon test must be pure math with no heap allocations. Use `Span` or
  stack-allocated buffers for intermediate geometry.
- Filter by `EntityInfo.ForceId == req.TargetForce`.
- Pool chunk allocation for targets must be guarded: if the pool is full, write
  `TargetCount = 0` and log a warning (no crash).
- The solver must NOT clear `AreaQueryBatchData.Count`; count reset is handled by
  `AreaQueryInitializationSystem` (TASK-HA003) at the top of each Brain frame.
  The solver uses the `IsReady` flag on results to avoid re-processing already-completed
  requests within the same solver cycle.

**Success Conditions:**

SC-HA002-1: Given a snapshot with 3 enemy entities inside a rectangular polygon area
entity and 2 entities outside, `AreaQuerySolverSystem.Execute` writes a result with
`TargetCount = 3` and `IsReady = true` for the corresponding request.

SC-HA002-2: Given a request where `TargetAreaEntity` is not alive in the snapshot,
the system writes `IsReady = true`, `TargetCount = 0` (no exception thrown).

SC-HA002-3: Given 65 pending requests (exceeding the pool capacity limit), the solver
processes the first 64 normally and skips the 65th without crashing or corrupting results.

SC-HA002-4: `EqsModule.Policy` returns `ExecutionPolicy.SlowBackground(10)`. Confirmed
via reflection or a direct property assertion in a unit test.

---

### TASK-HA003: AreaQueryInitializationSystem (Brain Tier, CGF)

**Design Reference:** DESIGN.md — Phase 1, section 1.4

**Scope:**
- Implement `AreaQueryInitializationSystem : IEcsModuleSystem` in `Hrot.CGF/Systems/`.
- Register it in `CgfLogicPack` at `SystemPhase.PreInput` (runs before BTreeTickSystem).
- File: `Hrot/Subsystems/Hrot.CGF/Systems/AreaQueryInitializationSystem.cs`.

**NOT included:** Network translators. This system runs on the Brain node in both
Editor and distributed deployments.

**Constraints:**
- The system's `Execute` must reset `AreaQueryBatchData.Count = 0` each frame, allowing
  new requests to be submitted by BTree nodes in the upcoming tick.
- The system must reset the `EqsTargetPool` free list so all pool chunks are available
  for the solver after the reset. Expose a `ResetPool()` method on `AreaQueryBatchHelper`
  or directly on `EqsTargetPool` for this purpose.
- The system must run BEFORE `BTreeTickSystem` and BEFORE `AreaQueryBrainEgressTranslator`
  to guarantee a clean state at the start of each frame.
- Because `Condition_IsAreaQueryResolved` and `Action_DispatchWaveWithTargets` execute
  in the same BTree tick, handles are always consumed before the next frame's reset.
  No behavior node ever needs to call a manual free.
- If `AreaQueryBatchData` is not present (singleton not initialized), the system does
  nothing (no crash, no exception).

**Success Conditions:**

SC-HA003-1: After `AreaQueryInitializationSystem.Execute`, `AreaQueryBatchData.Count == 0`
regardless of the value it held at end of the previous frame.

SC-HA003-2: After `AreaQueryInitializationSystem.Execute`, all `EqsTargetPool` chunks
read as zero/default (confirmed by reading packed entity values at all pool offsets).

SC-HA003-3: `CgfLogicPack` registers `AreaQueryInitializationSystem` at
`SystemPhase.PreInput`, ordered before `BTreeTickSystem`. Confirmed by inspecting the
registered system list in a unit test.

---

### TASK-HA004: EQS Network Translators

**Design Reference:** DESIGN.md — Phase 1, section 1.5

**Scope:**
- Implement 4 translator classes in `Hrot/Network/Hrot.Network.NED/SimHost/`:
  - `AreaQueryBrainEgressTranslator`
  - `AreaQueryMuscleIngressTranslator`
  - `AreaQueryMuscleEgressTranslator`
  - `AreaQueryBrainIngressTranslator`
- Define DDS message types `AreaQueryRequestBatch` and `AreaQueryResponseBatch`.
- Register translators in the appropriate translator packs (Brain and Muscle).

**NOT included:** Solver system, EQS batch types (TASK-HA001/HA002).

**Constraints:**
- DDS messages must not contain managed fields other than `[DdsManaged] List<long>` for
  the target entity ID list in the response batch.
- Egress translators must drain only the current frame's batch (up to `Count` entries),
  then reset `Count = 0` to prevent reprocessing.
- Ingress translators must guard against entities not yet materialized on the local node:
  skip targets that cannot be resolved via `NetworkEntityMap`.
- If the `TargetAreaEntity` network ID cannot be resolved on the Muscle node, skip the
  request and write a `TargetCount = 0` response back. Do not crash.
- Authority check: the Brain egress translator should only forward requests that originate
  on the local Brain node (verified by `SourceNodeId`).

**Success Conditions:**

SC-HA004-1: In a two-node test fixture (Brain + Muscle in separate processes), submitting
an `AreaQueryRequest` on the Brain node via `AreaQueryBatchHelper.RequestAreaQuery`
results in a completed `AreaQueryResult` appearing in the Brain's `AreaQueryBatchData`
within 3 simulation seconds.

SC-HA004-2: Given a request where the area entity has not materialized on the Muscle node,
a `TargetCount = 0` result is returned to the Brain without a crash or unhandled exception.

SC-HA004-3: Entity IDs that fail `NetworkEntityMap` lookup are silently skipped; the
`TargetCount` in the result reflects only successfully resolved entities.

---

## Phase 2: Hill Attack Data Contracts

### TASK-HA005: Commander DTOs

**Design Reference:** DESIGN.md — Phase 2, section 2.1 and 2.3

**Scope:**
- Define `PlatoonHillAttackParams` struct (52 bytes).
- Define `PlatoonHillAttackBlackboard` wrapper struct.
- Define `HillAttackMutableState` struct (projected onto `Blackboard1024`).
- File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs`
  (commander section).

**NOT included:** Behavior nodes, BTree definition.

**Constraints:**
- `PlatoonHillAttackParams` must be decorated with `[StructLayout(LayoutKind.Sequential)]`.
- `sizeof(PlatoonHillAttackParams)` must not exceed 60 bytes
  (`BehaviorConstants.MaxBehaviorParamByteSize`). Verified in a unit test or static assert.
- `HillAttackMutableState` must be decorated with `[StructLayout(LayoutKind.Sequential)]`.
- `sizeof(HillAttackMutableState)` must not exceed 1024 bytes
  (`Blackboard1024.ByteSize`). Verified in a unit test or static assert.
- Field order in `HillAttackMutableState` must respect natural alignment to avoid
  unintended padding that would shift the `fixed` array offsets.
- No string fields, no managed fields anywhere.
- `CachedEqsRequestId` must be `long` to hold the `RequestId` returned by
  `AreaQueryBatchHelper.RequestAreaQuery`.
- `CachedTargetGroupHandle` must be `int`, initialized to -1. Stores the pool handle
  from the EQS result between `Condition_IsAreaQueryResolved` and
  `Action_DispatchWaveWithTargets`.
- The `fixed` array sizes are 8 (`ActiveEntityPacked`, `ActiveSlotIndex`,
  `ReturnBaselineSlotIndex`, `HasStartedRun`), matching `UnitRoster.MaxSubordinates / 2`.
- `HasStartedRun` is a `fixed byte[8]` array initialized to all-zero at the start of
  each wave dispatch. It is set to 1 per attacker slot by `Condition_IsWaveCompleted`
  the first time that attacker's `BehaviorState.ActiveBehaviorHash` matches
  `HullDownAttackRun`. This prevents false completion detection during the one-frame
  ingress pipeline delay.

**Success Conditions:**

SC-HA005-1: `sizeof(PlatoonHillAttackParams) == 52`. Assert in `HillAttackDtosTests`.

SC-HA005-2: `sizeof(HillAttackMutableState) <= 1024`. Assert in `HillAttackDtosTests`.

SC-HA005-3: `sizeof(HillAttackMutableState)` is greater than 0 and the struct is
`blittable` (verified with `GCHandle.Alloc(new HillAttackMutableState(), GCHandleType.Pinned)`
succeeding without exception).

SC-HA005-4: The `ActiveEntityPacked` fixed array holds 8 `long` values. Manually writing
indices 0 through 7 and reading them back returns identical values (no out-of-bounds
access within the struct boundary).

---

### TASK-HA006: Tank DTOs

**Design Reference:** DESIGN.md — Phase 2, section 2.2

**Scope:**
- Define `HullDownAttackParams` struct (40 bytes).
- Define `HullDownAttackBlackboard` wrapper struct.
- File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackDtos.cs`
  (tank section, same file as TASK-HA005).

**NOT included:** Behavior nodes, BTree definition.

**Constraints:**
- `sizeof(HullDownAttackParams)` must not exceed 60 bytes. Verified in a test.
- All fields must be `float` or `long`; `TargetNetworkId` is `long`
  (the network-stable replication ID of the assigned target, NOT a local ECS `Entity`
  handle). Behavior nodes resolve it to a local entity via `NetworkEntityMap` at runtime;
  the unmanaged params struct never stores a local generational entity pointer.
- `HullDownAttackBlackboard` contains only one field: `HullDownAttackParams Params`.

**Success Conditions:**

SC-HA006-1: `sizeof(HullDownAttackParams) == 40`. Assert in `HillAttackDtosTests`.

SC-HA006-2: A `HullDownAttackBlackboard` can be pinned via `GCHandle` (blittable check).

---

## Phase 3: HullDownAttackRun Behavior (Subordinate Tank)

### TASK-HA007: Condition_HasTarget and Action_CreepToAndBeyondSlot

**Design Reference:** DESIGN.md — Phase 3, sections 3.1–3.2

**Scope:**
- Implement `Condition_HasTarget` in `HullAttackTankNodes.cs`.
- Implement `Action_CreepToAndBeyondSlot` in `HillAttackTankNodes.cs`.
- File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`.

**NOT included:** Other tank nodes, BTree definition.

**Constraints:**
- `Condition_HasTarget`:
  - Attribute: `[SharedAiCondition(typeof(HullDownAttackBlackboard), nameof(HullDownAttackBlackboard.Params))]`.
  - Resolves `p.TargetNetworkId` to a local `Entity` via
    `repo.GetSingleton<NetworkEntityMap>().TryGetEntity(p.TargetNetworkId, out var targetEntity)`.
    If resolution fails (entity not yet materialized on this node), returns `NodeStatus.Failure`.
  - Reads `TargetMemory` from the entity. Iterates `mem.Count` entries.
  - Returns `NodeStatus.Success` if any entry's local entity matches `targetEntity`
    and `ThreatScores[i] > 0f`. Otherwise `NodeStatus.Failure`.
  - Must not allocate; loop is bounded by `PerceptionConstants.MaxTrackedTargets = 4`.

- `Action_CreepToAndBeyondSlot`:
  - Attributes: `[SharedAiAction(...)]`, `[WritesChannel(ChannelKind.Locomotion)]`.
  - Returns `NodeStatus.Running` while the tank is approaching or creeping normally.
  - Returns `NodeStatus.Failure` when the tank has overshot the assigned slot by more
    than the tactical overshoot limit (constant `HillAttackConstants.MaxOvershootMeters`,
    default 50 m). Compute overshoot as `Vector2.Distance(currentPos, slotPos)` when the
    tank is already past the slot along the attack direction.
  - Must NEVER return `NodeStatus.Success`.
  - Phase 1 (distance to slot > threshold): writes `ActionIdMoveTo` with `Destination`
    = slot position and `Speed = p.ApproachSpeed` to `LocomotionChannel`.
  - Phase 2 (distance <= threshold, overshoot not yet exceeded): writes `ActionIdMoveTo`
    with `Destination` = `currentPos + p.AttackDir * 10000f` (far point along attack
    vector) and `Speed = p.CreepSpeed`.
  - Must increment `loco.ActionInstanceId` only when the command changes
    (check `loco.ActiveAction` and speed mismatch before writing to avoid spamming
    the dispatcher with identical intents each frame).
  - Use `repo.GetComponentRO<SimTransform>(self)` for position; use
    `repo.GetComponentRW<LocomotionChannel>(self)` for writing.
  - `AttackDir` must be used as a raw `(AttackDirX, AttackDirY)` 2D vector; it is
    already normalized by the commander at dispatch time.

**Success Conditions:**

SC-HA007-1: `Condition_HasTarget` returns `NodeStatus.Success` when `NetworkEntityMap`
resolves `p.TargetNetworkId` to a local entity that appears in `TargetMemory` with
`ThreatScore > 0`. It returns `NodeStatus.Failure` when the target is absent or score == 0.

SC-HA007-2: `Condition_HasTarget` returns `NodeStatus.Failure` when `p.TargetNetworkId`
cannot be resolved by `NetworkEntityMap` (entity not yet materialized on this node).

SC-HA007-3: `Action_CreepToAndBeyondSlot` returns `NodeStatus.Running` while the
tank has not yet exceeded the tactical overshoot limit past the assigned slot.

SC-HA007-3b: `Action_CreepToAndBeyondSlot` returns `NodeStatus.Failure` when the tank's
position has overshot the assigned slot by more than `HillAttackConstants.MaxOvershootMeters`
(50 m) along the attack direction. The `LocomotionChannel.ActiveAction` is NOT written
again after Failure is returned (the `[WritesChannel]` exit wrapper handles cleanup).

SC-HA007-4: When the entity position is more than the threshold distance from the slot,
`LocomotionChannel.Params` decodes to a `MoveToParams.Speed` matching `p.ApproachSpeed`.

SC-HA007-5: When the entity position is within the threshold distance, the locomotion
params decode to `Speed = p.CreepSpeed` and `Destination` is a point far along the
attack direction from the current position.

SC-HA007-6: Calling `Action_CreepToAndBeyondSlot` twice with the same position and same
`LocomotionChannel.ActiveAction` does not change `ActionInstanceId` on the second call
(no redundant channel writes).

---

### TASK-HA008: Action_AimAndFireSpecific and Action_ReverseToBaseline

**Design Reference:** DESIGN.md — Phase 3, sections 3.1–3.2

**Scope:**
- Implement `Action_AimAndFireSpecific` in `HillAttackTankNodes.cs`.
- Implement `Action_ReverseToBaseline` in `HillAttackTankNodes.cs`.

**Constraints:**
- `Action_AimAndFireSpecific`:
  - Attributes: `[SharedAiAction(...)]`, `[WritesChannel(ChannelKind.Weapon)]`.
  - Resolves `p.TargetNetworkId` to a local `Entity` via `NetworkEntityMap`. If
    resolution fails, returns `NodeStatus.Failure` immediately.
  - If `!repo.IsAlive(targetEntity)`, returns `NodeStatus.Success` immediately (target
    destroyed; standard executors do not natively detect target destruction and would
    leave the node stuck in Running indefinitely).
  - Otherwise writes `ActionIdAimAndFire` to `WeaponChannel` with
    `AimAndFireParams.Target = targetEntity`.
  - Returns `NodeStatus.Running` while `WeaponChannel.Status == NodeStatus.Running`.
  - Returns `NodeStatus.Success` when `WeaponChannel.Status == NodeStatus.Success`
    (engagement concluded). Returns `NodeStatus.Failure` on `NodeStatus.Failure`.
  - Must only write to the channel if `WeaponChannel.ActiveAction != ActionIdAimAndFire`
    or `Status == NodeStatus.Failure` (prevents re-issuing the same command every frame).
  - `AimAndFireParams` must match the existing struct in `Fdp.Toolkits`; check that
    it has a `Target` field of type `Entity` before writing.

- `Action_ReverseToBaseline`:
  - Attributes: `[SharedAiAction(...)]`, `[WritesChannel(ChannelKind.Locomotion)]`.
  - Writes `ActionIdMoveTo` to `LocomotionChannel` with `Destination = (BaselineX, BaselineY)`
    and a reverse flag.
  - The reverse flag is written into `MoveToParams` or passed via a separate mechanism
    consistent with how `NavState.ReverseAllowed` is wired in the muscle tier. Use the
    existing `MoveToParams` struct fields (check `CarKinem.Core` for the correct field name).
  - Returns `NodeStatus.Running` while `LocomotionChannel.Status == NodeStatus.Running`.
  - Returns `NodeStatus.Success` when `LocomotionChannel.Status == NodeStatus.Success`
    (arrived at baseline). Returns `NodeStatus.Failure` on failure.

**Success Conditions:**

SC-HA008-1: `Action_AimAndFireSpecific` resolves `p.TargetNetworkId` to a local entity
and writes a `WeaponChannel` command with that target entity. The `ActionInstanceId` is
incremented exactly once per new engagement, not once per frame.

SC-HA008-2: On the second consecutive call with `WeaponChannel.Status == NodeStatus.Running`,
`ActionInstanceId` is NOT incremented again.

SC-HA008-3: `Action_AimAndFireSpecific` returns `NodeStatus.Success` when
`WeaponChannel.Status == NodeStatus.Success` OR when `!repo.IsAlive(targetEntity)`.

SC-HA008-3b: When the assigned target entity is no longer alive, `Action_AimAndFireSpecific`
returns `NodeStatus.Success` immediately without waiting for weapon channel status.

SC-HA008-4: `Action_ReverseToBaseline` writes a locomotion command with `Destination`
matching `(p.BaselineX, p.BaselineY)`.

SC-HA008-5: `Action_ReverseToBaseline` returns `NodeStatus.Success` when
`LocomotionChannel.Status == NodeStatus.Success`.

---

### TASK-HA009: HullDownAttackRun BTree, Mapper, and Registration

**Design Reference:** DESIGN.md — Phase 3, section 3.2 and 3.3

**Scope:**
- Implement `[BTreeDefinition("HullDownAttackRun")]` factory method.
- Implement `HullDownAttackMapper : ITacticalOrderMapper`.
- Register the behavior and mapper in `AiBehaviorFactory.cs`.
- Files:
  - BTree definition: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs`
    (or a dedicated `HillAttackBehaviorDefinitions.cs`).
  - Mapper: `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/HullDownAttackMapper.cs`.
  - Registration: `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`.

**Constraints:**
- The `[BTreeDefinition]` method must match the exact static factory signature expected
  by `Fbt.SourceGen`: `static Interpreter<BrainBlackboard, BTreeContext> GetHullDownAttackRun()`.
- The BTree topology must match DESIGN.md Phase 3 section 3.2 exactly:
  ```
  Sequence
    Selector
      Sequence
        Selector([Condition_HasTarget, Action_CreepToAndBeyondSlot])
        Action_AimAndFireSpecific
      Action_AbortEngagement
    Action_ReverseToBaseline
  ```
- `Action_AbortEngagement` is a trivial `[SharedAiAction]` that returns
  `NodeStatus.Success` unconditionally with no channel writes. Its sole purpose is to
  ensure `Action_ReverseToBaseline` runs even when the engagement path fails due to
  overshoot.
- `HullDownAttackMapper.TargetIntentId` returns `"HullDownAttack"`.
- `HullDownAttackMapper.TryMap` verifies the entity has `TkbIdentity` and is a tank type
  (use `TkbEntityTypes` constants from `Hrot.Core`). Returns `false` for non-tanks.
- `TryMap` sets `BehaviorName = "HullDownAttackRun"` and passes `JsonParams` verbatim.
- In `AiBehaviorFactory`, the behavior ID constant for `HullDownAttackRun` must be added
  (as a `private const uint` or in a dedicated constants class).
- Mapper registration: `HullDownAttackMapper` must be added to the
  `TacticalIntentMapperRegistry` in `CgfSubsystem` or wherever mappers are registered.

**Success Conditions:**

SC-HA009-1: `HullDownAttackMapper.TargetIntentId == "HullDownAttack"`.

SC-HA009-2: Given a tank entity with a valid `TkbIdentity`, `TryMap` returns `true` and
sets `assignment.BehaviorName = "HullDownAttackRun"`.

SC-HA009-3: Given a non-tank entity (e.g., infantry or APC), `TryMap` returns `false`.

SC-HA009-4: The fluent BTree factory method compiles without Roslyn source gen errors.
`FbtTreeCatalog.GetHullDownAttackRun()` is accessible after a successful build.

SC-HA009-5: In a headless test scenario, assigning `"HullDownAttack"` intent to a tank
entity results in `BehaviorState.ActiveBehaviorHash` changing to the `HullDownAttackRun`
behavior hash within one simulation frame (via `TacticalIntentResolutionSystem` +
`BehaviorIngressSystem`).

---

## Phase 4: PlatoonHillAttack Behavior (Commander)

### TASK-HA010: Action_CalculateSegments, Action_DispatchAllToBaseline, Condition_AreAllAtBaseline

**Design Reference:** DESIGN.md — Phase 4, sections 4.1–4.3

**Scope:**
- Implement three commander nodes in a new file
  `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs`.

**Constraints:**
- All three nodes use the 5-argument `[SharedAiHeavyAction]` form that projects
  `Blackboard1024.Memory` to `HillAttackMutableState` via `Unsafe.As`.

- `Action_CalculateSegments`:
  - Computes segment length of the firing line as `Vector2.Distance((StartX,StartY), (EndX,EndY))`.
  - `TotalSlots = Max(1, (int)(segmentLength / p.TankSpacing))`.
  - Clamps `TotalSlots` to 16 (matching `ushort` bitmask capacity).
  - Initializes `BurnedSlotsMask = 0`, `WaveUsedSlotsMask = 0`, `BaselineReservedMask = 0`,
    `ActiveAttackerCount = 0`, `CurrentWave = 0`, `CachedEqsRequestId = -1`.
  - Returns `NodeStatus.Success`.

- `Action_DispatchAllToBaseline`:
  - Iterates `UnitRoster.Count` on the commander entity.
  - For each alive subordinate (check `repo.IsAlive`), computes a baseline slot index
    `i` (tank index in roster), interpolates coordinates, and publishes
    `AssignTacticalIntentEvent { IntentId = "MoveToLocation", JsonParams = "{...}" }`.
  - The JsonParams format for MoveToLocation must match what the existing
    `MoveToLocationMapper` (or equivalent) expects. Check `AiBehaviorFactory.ParseMoveToParams`.
  - Reserves the corresponding bit in `BaselineReservedMask`.
  - Returns `NodeStatus.Success`.

- `Condition_AreAllAtBaseline`:
  - Attribute: `[SharedAiCondition(...)]` (reads `UnitRoster` + iterates subordinate
    `NavigationStatus` components via `repo`).
  - Returns `NodeStatus.Success` only when every alive subordinate has
    `NavigationStatus.Result == NavigationResult.Arrived`. `NavState` belongs exclusively
    to the Muscle tier; the Brain node reads only the CQRS feedback component
    `NavigationStatus`, which is replicated to the Brain node.
  - Returns `NodeStatus.Running` if any alive subordinate has not yet arrived.
  - Dead subordinates (not alive per `repo.IsAlive`) are counted as arrived (they
    cannot block deployment).

**Success Conditions:**

SC-HA010-1: `Action_CalculateSegments` with a 100m segment and `TankSpacing = 30f`
produces `TotalSlots = 3`. All bitmasks are 0 after the call.

SC-HA010-2: `Action_CalculateSegments` with a segment shorter than `TankSpacing` produces
`TotalSlots = 1` (minimum 1 slot; does not produce 0 or negative).

SC-HA010-3: `Action_CalculateSegments` with a very long segment produces `TotalSlots = 16`
(capped at 16).

SC-HA010-4: `Action_DispatchAllToBaseline` with a 4-tank roster publishes exactly 4
`AssignTacticalIntentEvent` entries on the event bus with distinct computed coordinates
for each baseline slot.

SC-HA010-5: `Condition_AreAllAtBaseline` returns `NodeStatus.Running` when at least one
alive subordinate has `NavigationStatus.Result != NavigationResult.Arrived`.

SC-HA010-6: `Condition_AreAllAtBaseline` returns `NodeStatus.Success` when all alive
subordinates have `NavigationStatus.Result == NavigationResult.Arrived`.

SC-HA010-7: A dead subordinate (not alive) does not prevent `Condition_AreAllAtBaseline`
from returning `NodeStatus.Success`.

---

### TASK-HA011: Action_RequestAreaQuery and Condition_IsAreaQueryResolved

**Design Reference:** DESIGN.md — Phase 4, sections 4.1–4.3; Phase 1, sections 1.1–1.2

**Scope:**
- Implement `Action_RequestAreaQuery` in `HillAttackCommanderNodes.cs`.
- Implement `Condition_IsAreaQueryResolved` in `HillAttackCommanderNodes.cs`.

**Constraints:**
- `Action_RequestAreaQuery`:
  - Clears any stale request: if `state.CachedEqsRequestId != -1`, check if a result
    already exists in the batch (guard against re-submitting without consuming the
    previous result). If the result is not yet ready, return `NodeStatus.Running` to
    wait rather than submitting a duplicate.
  - When `CachedEqsRequestId == -1` (fresh cycle): call
    `AreaQueryBatchHelper.RequestAreaQuery(repo, self, p.TargetAreaEntity, ForceId.Hostile)`.
  - If `RequestAreaQuery` returns -1 (batch full), return `NodeStatus.Running` (try
    again next frame without flooding the batch).
  - On success, store the returned ID in `state.CachedEqsRequestId`.
  - Returns `NodeStatus.Success` to advance the sequence.

- `Condition_IsAreaQueryResolved`:
  - If `state.CachedEqsRequestId == -1`, return `NodeStatus.Failure` (guard — should
    not occur if BTree topology is correct).
  - Call `AreaQueryBatchHelper.GetAreaQueryResult(repo, state.CachedEqsRequestId)`.
  - If `result == default` or `!result.IsReady`: return `NodeStatus.Running`.
  - If `result.IsReady && result.TargetCount == 0`: reset `state.CachedEqsRequestId = -1`;
    reset `state.CachedTargetGroupHandle = -1`;
    return `NodeStatus.Failure` (breaks the `Repeater`; pool cleanup is automatic next frame).
  - If `result.IsReady && result.TargetCount > 0`: cache
    `state.CachedTargetGroupHandle = result.TargetGroupHandle`;
    reset `state.CachedEqsRequestId = -1`; return `NodeStatus.Success`.

**Success Conditions:**

SC-HA011-1: `Action_RequestAreaQuery` sets `state.CachedEqsRequestId` to a valid (>= 0)
value on first call when the batch is not full.

SC-HA011-2: `Action_RequestAreaQuery` returns `NodeStatus.Running` when the batch is
full (capacity exceeded). Does not modify `CachedEqsRequestId` on a failed submission.

SC-HA011-3: `Condition_IsAreaQueryResolved` returns `NodeStatus.Running` while the
result `IsReady == false`.

SC-HA011-4: `Condition_IsAreaQueryResolved` returns `NodeStatus.Failure` and resets
`CachedEqsRequestId = -1` when `IsReady == true` and `TargetCount == 0`.

SC-HA011-5: `Condition_IsAreaQueryResolved` returns `NodeStatus.Success` when
`IsReady == true` and `TargetCount > 0`. The `CachedEqsRequestId` is NOT cleared yet.

---

### TASK-HA012: Action_DispatchWaveWithTargets and Condition_IsWaveCompleted

**Design Reference:** DESIGN.md — Phase 4, sections 4.1, 4.4, and 4.5

**Scope:**
- Implement `Action_DispatchWaveWithTargets` in `HillAttackCommanderNodes.cs`.
- Implement `Condition_IsWaveCompleted` in `HillAttackCommanderNodes.cs`.

**Constraints:**
- `Action_DispatchWaveWithTargets`:
  - Reads the EQS result using `state.CachedEqsRequestId`.
  - Resets `state.WaveUsedSlotsMask = 0` and `state.ActiveAttackerCount = 0` at the
    start of each wave.
  - Iterates `UnitRoster`. Selects tanks for the current wave:
    - If `roster.Count <= 3`: all alive tanks participate.
    - Else: tanks where `i % 2 == state.CurrentWave`.
  - For each selected tank: pick a random available firing-line slot from
    `~(state.BurnedSlotsMask | state.WaveUsedSlotsMask)` limited to `state.TotalSlots`.
    If no slots available, skip the tank.
  - Select baseline slot: iterate all baseline indices, choose the unreserved index
    closest (by distance-squared from firing-line slot position) to the firing-line slot.
    If all baseline slots are reserved, use the closest regardless (edge case: more tanks
    than slots).
  - Update `WaveUsedSlotsMask`, `BaselineReservedMask`; write into SoA at
    `ActiveAttackerCount` index; increment `ActiveAttackerCount`.
  - Compute JIT coordinates for firing slot (interpolate from `p.StartX/Y` to `p.EndX/Y`)
    and baseline slot (interpolate from `p.BaselineStartX/Y` to `p.BaselineEndX/Y`).
  - Distribute targets round-robin: `targetIndex = activeTankIndexInWave % targetCount`
    where targets come from `EqsTargetPool` via
    `AreaQueryBatchHelper.GetTargetFromPool(repo, state.CachedTargetGroupHandle, targetIndex)`.
  - Read `NetworkIdentity.NetworkId` for each assigned target entity to obtain the
    network-stable ID; serialize as `TargetNetworkId` in the JSON payload. Never
    serialize the local packed entity handle, which is invalid across the network boundary.
  - Publish `AssignTacticalIntentEvent { IntentId = "HullDownAttack", JsonParams = ... }`.
  - Reset `state.CachedTargetGroupHandle = -1`.
    Pool chunk cleanup is automatic via `AreaQueryInitializationSystem` (no manual free).
  - Toggle `state.CurrentWave = (byte)(1 - state.CurrentWave)`.
  - Returns `NodeStatus.Success`.

- `Condition_IsWaveCompleted`:
  - Iterates SoA backwards from `ActiveAttackerCount - 1` to 0.
  - For each entry:
    - `Entity attacker = new Entity((ulong)state.ActiveEntityPacked[i])`.
    - If `!repo.IsAlive(attacker)`: set `BurnedSlotsMask` bit for `ActiveSlotIndex[i]`;
      clear `BaselineReservedMask` bit for `ReturnBaselineSlotIndex[i]`; swap-remove.
    - Else if `HasStartedRun[i] == 0`:
      if `BehaviorState.ActiveBehaviorHash == HullDownAttackRun` hash, set
      `HasStartedRun[i] = 1`. Do not remove; the tank just started.
      Otherwise do nothing (intent still propagating through ingress pipeline).
    - Else (`HasStartedRun[i] == 1`): if `BehaviorState.ActiveBehaviorHash` no longer
      matches `HullDownAttackRun` hash, the run finished (retreat complete or overshoot
      abort). Clear `BaselineReservedMask` bit; swap-remove.
  - Returns `NodeStatus.Success` when `state.ActiveAttackerCount == 0`.
  - Returns `NodeStatus.Running` while any attacker remains.

  - Swap-remove helper: copy last entry over index `i`
    (`ActiveEntityPacked`, `ActiveSlotIndex`, `ReturnBaselineSlotIndex`,
    `HasStartedRun`); decrement `ActiveAttackerCount`.

**Success Conditions:**

SC-HA012-1: With 4 tanks in a 4-tank roster and `CurrentWave = 0`, dispatch selects
the 2 tanks whose `Entity.Index % 2 == 0`. `ActiveAttackerCount == 2` after dispatch.

SC-HA012-2: With `roster.Count <= 3`, all alive tanks are dispatched regardless of
parity. `ActiveAttackerCount == roster.Count` (minus any skipped-due-to-no-slots).

SC-HA012-3: With 2 targets and 3 tanks dispatched, target assignment produces:
tank 0 -> target 0, tank 1 -> target 1, tank 2 -> target 0 (round-robin).

SC-HA012-4: Calling `Condition_IsWaveCompleted` when `ActiveAttackerCount == 0` returns
`NodeStatus.Success` immediately.

SC-HA012-5: Given one active attacker that is no longer alive (`!repo.IsAlive`), the
corresponding `BurnedSlotsMask` bit is set, `BaselineReservedMask` is cleared for that
tank's baseline slot, and `ActiveAttackerCount` decrements to 0, causing the condition
to return `NodeStatus.Success`.

SC-HA012-6: `Condition_IsWaveCompleted` does NOT remove an entry when
`HasStartedRun[i] == 0` even if the current `BehaviorState.ActiveBehaviorHash`
differs from `HullDownAttackRun` (the intent has not been ingested yet). The entry
remains in the tracker.

SC-HA012-6b: Given one active attacker whose `BehaviorState.ActiveBehaviorHash`
matches `HullDownAttackRun` on tick T (setting `HasStartedRun = 1`), and whose hash
no longer matches on tick T+N, the entry is removed from the tracker and
`BaselineReservedMask` is cleared (run completed).

SC-HA012-6c: Two tanks in the same wave are each assigned a different `Entity.Index`
parity consistent with `state.CurrentWave`. Destroying one tank, causing the roster
to compact, does not change the other surviving tank's wave assignment (confirmed by
comparing its `Entity.Index % 2` before and after the destruction).

SC-HA012-7: Firing slots of burned tanks are not assigned in subsequent waves. Given
`BurnedSlotsMask = 0b0000_0001` (slot 0 burned), `Action_DispatchWaveWithTargets` never
assigns slot 0 in any subsequent wave.

SC-HA012-8: `state.CachedTargetGroupHandle == -1` after `Action_DispatchWaveWithTargets`
returns. Pool chunks are reset automatically by `AreaQueryInitializationSystem` at the
start of the next frame.

---

### TASK-HA013: PlatoonHillAttack BTree Definition and Registration

**Design Reference:** DESIGN.md — Phase 4, section 4.2

**Scope:**
- Implement `[BTreeDefinition("PlatoonHillAttack")]` factory method.
- Register the behavior in `AiBehaviorFactory.cs` with `HeavyDtoType = typeof(HillAttackMutableState)`.
- File: same as TASK-HA009 behavior definitions file.

**Constraints:**
- BTree topology must match DESIGN.md Phase 4 section 4.2 exactly:
  `Sequence(CalculateSegments, DispatchAllToBaseline, AreAllAtBaseline, Repeater(-1,
  Sequence(RequestAreaQuery, IsAreaQueryResolved, DispatchWaveWithTargets, IsWaveCompleted)))`.
- `BehaviorDefinition.HeavyDtoType = typeof(HillAttackMutableState)` — required for
  the `Blackboard1024Renderer` to project and display debug state.
- The behavior ID constant must be a stable `uint` added alongside existing IDs in
  `AiBehaviorFactory`.
- `ParseParams` delegate must be implemented per TASK-HA016: converts geodetic
  coordinates to ENU Cartesian, derives the attack direction from the firing line
  vector, and resolves `TargetAreaNetworkId` to a local ECS `Entity` via
  `NetworkEntityMap`.

**Success Conditions:**

SC-HA013-1: `FbtTreeCatalog.GetPlatoonHillAttack()` is accessible and non-null after a
clean build.

SC-HA013-2: The `BehaviorDefinition` for `PlatoonHillAttack` has `BrainTier ==
BehaviorConstants.BrainTierBTree`, `HeavyDtoType == typeof(HillAttackMutableState)`,
and a non-null `BTreeInterpreter`.

SC-HA013-3: In a headless test, assigning `PlatoonHillAttack` to a commander entity via
`AssignBehaviorEvent` results in `BehaviorState.ActiveBehaviorHash` updating to the
`PlatoonHillAttack` behavior hash within one frame.

---

## Phase 5: TKB Blueprint and Integration Validation

### TASK-HA014: TKB Blueprint Updates

**Design Reference:** DESIGN.md — Phase 5, section 5.1

**Scope:**
- Add `Blackboard1024` component to the commander entity's TKB blueprint definition.
- Verify (or add) that subordinate tank blueprints include `NavState`,
  `LocomotionChannel`, `WeaponChannel`, `TargetMemory`, `BrainBlackboard`,
  `BehaviorState`, `BrainBTreeState`, `UnitSubordinate`.
- Files: TKB definition JSON/YAML files in the appropriate data directory.

**NOT included:** No code changes. Data-only.

**Constraints:**
- The component must be added using the existing TKB component authoring format.
- Do not add `Blackboard1024` to subordinate tank blueprints unless they already require
  it for another behavior (avoid unnecessary component bloat).
- Verify that there are no blueprint validation errors by running the TKB loader test suite.

**Success Conditions:**

SC-HA014-1: The commander entity blueprint loads without validation errors after adding
`Blackboard1024`.

SC-HA014-2: After scenario load, `repo.HasComponent<Blackboard1024>(commanderEntity)`
returns `true`.

SC-HA014-3: All subordinate tank entities satisfy `repo.HasComponent<NavState>(tankEntity)`
and `repo.HasComponent<TargetMemory>(tankEntity)` after spawn.

---

### TASK-HA015: Integration Test (Scenario-based)

**Design Reference:** DESIGN.md — Phase 5, section 5.2

**Scope:**
- Implement a scenario-based integration test in `Hrot.SimHost.Tests` or
  `Hrot.CGF.Tests`.
- File: `Hrot/Subsystems/Hrot.SimHost.Tests/HillAttackIntegrationTests.cs` or equivalent.

**Constraints:**
- The test must spin up a full simulation context using the test fixture pattern from
  existing `Hrot.SimHost.Tests` tests.
- Simulate enough frames to fully exercise the preparation phase and at least one
  complete attack wave.
- Use `BehaviorFinishedEvent` observation (subscribe via event bus) to detect completion.
- Enemy entities in the test must have `CombatHealth` and `ForceId.Hostile`.
- The test must run headless (no rendering, no network) using the Editor/networkless path.
- Must NOT require real EQS network translators; the EqsModule SoD solver runs locally
  in the same process.

**Success Conditions:**

SC-HA015-1: After assigning `PlatoonHillAttack` to a 4-tank platoon commander and
spawning 2 enemy entities inside the target polygon:
  - Within 30 simulation seconds, `BehaviorFinishedEvent` is published for the commander
    entity with `Result == NodeStatus.Failure` (behavior end = area cleared).
  - The 2 enemy entities reach `CombatHealth.HitPoints <= 0` before the event.

SC-HA015-2: No two tanks in the same wave are assigned the same firing slot index.
Verified by recording `AssignTacticalIntentEvent` JSON params and parsing slot coordinates.

SC-HA015-3: When one tank is killed during a wave (manually set `IsAlive = false` via
test command), the wave still completes. The killed tank's slot appears in `BurnedSlotsMask`
in the subsequent wave (observable via `HillAttackMutableState` debug projection).

SC-HA015-4: With 2 enemies and 3 tanks dispatched in one wave, targets are distributed
as: tank 0 -> enemy 0, tank 1 -> enemy 1, tank 2 -> enemy 0.

---

## Phase 6: JSON Authoring DTO and ParseParams Delegate

### TASK-HA016: PlatoonHillAttack JSON DTO and ParseParams Delegate

**Design Reference:** DESIGN.md — Phase 6

**Scope:**
- Define `PlatoonHillAttackParamsJsonDto` class in `Hrot.Map.Definitions.Behavior`.
- Implement `ParsePlatoonHillAttackParams` static unsafe method alongside the BTree node
  definitions (e.g., in `HillAttackCommanderNodes.cs` or a dedicated
  `HillAttackIngress.cs`).
- Update `AiBehaviorFactory` to bind `ParseParams` in the `PlatoonHillAttack`
  registration.

**NOT included:** HullDownAttackRun params are authored programmatically by the commander
and do not require a human-editable JSON DTO.

**Constraints:**
- `PlatoonHillAttackParamsJsonDto` is a managed class. It must NOT be referenced from
  any hot-path behavior node. It is used solely by the `ParseParamsDelegate` on the cold
  ingress path.
- `[BehaviorContract(BehaviorIds.PlatoonHillAttack_BT, BehaviorId, BehaviorCategory.Commander)]`
  decoration is required for the behavior registry tooling.
- `[RemapNetworkId]` on `TargetAreaNetworkId` is required for Orchestrator ID patching
  when transitioning from a staging scenario into a live cluster.
- `[MapPickableEntity("tactical_graphics")]` on `TargetAreaNetworkId` restricts the
  UI picker to area overlay entities. Verify the constant string against the entity type
  tag used for area overlays in the TKB/scenario editor.
- The attack direction is NOT a user-authored field. It must be computed inside
  `ParsePlatoonHillAttackParams` as the left-hand perpendicular of the normalized firing
  line vector: `attackDir = Normalize(new Vector2(-fireVector.Y, fireVector.X))`.
- `geoTransform.ToCartesian` must be called at parse time (cold path). All runtime BTree
  nodes operate exclusively on pre-converted ENU Cartesian floats.
- `TankSpacing` defaults to 30f when absent from the JSON.
- If `TargetAreaNetworkId` cannot be resolved via `NetworkEntityMap`, write `Entity.Null`
  to `PlatoonHillAttackParams.TargetAreaEntity` without throwing.
- Bind the delegate as a lambda in `AiBehaviorFactory`:
  `ParseParams = (json, ptr) => ParsePlatoonHillAttackParams(json, ptr, geoTransform, entityMap)`.
  `geoTransform` and `entityMap` are injected into `AiBehaviorFactory` from the DI container.

**Success Conditions:**

SC-HA016-1: `PlatoonHillAttackParamsJsonDto` deserializes from a JSON string containing
`firingLineStart`, `firingLineEnd`, `baselineStart`, `baselineEnd`, `tankSpacing`, and
`targetAreaNetworkId` without exception. Missing `tankSpacing` uses the default 30f.

SC-HA016-2: After calling `ParsePlatoonHillAttackParams` with a horizontal firing line
(start=(0,0), end=(100,0) in ENU), the resulting `PlatoonHillAttackParams.AttackDirX == 0`
and `|AttackDirY| == 1.0f`, confirming the direction is perpendicular to the line and
derived from line geometry, not authored directly.

SC-HA016-3: After calling `ParsePlatoonHillAttackParams` with a valid
`TargetAreaNetworkId` that maps to a live ECS entity, `PlatoonHillAttackParams.TargetAreaEntity != Entity.Null`.

SC-HA016-4: After calling `ParsePlatoonHillAttackParams` with `TargetAreaNetworkId = 0`
or an unresolvable ID, `PlatoonHillAttackParams.TargetAreaEntity == Entity.Null`. No
exception is thrown.

SC-HA016-5: `sizeof(PlatoonHillAttackParams)` remains 52 bytes after all fields are
populated by the parse delegate (confirms no hidden managed references escape).

SC-HA016-6: The `BehaviorDefinition` for `PlatoonHillAttack` in `AiBehaviorFactory` has
a non-null `ParseParams` delegate after the registration call.
