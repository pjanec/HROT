# Hill Attack Group Behavior — Design

## Overview

This workstream implements a platoon-level hill attack tactical doctrine.
A platoon commander directs a group of subordinate tanks through a sequenced attack
procedure: deploying to a baseline, executing alternating attack waves that crest a hill,
engaging enemies in a designated target area, and cycling until the area is cleared.

The design adheres strictly to the FDP engine's CQRS boundaries, Data-Oriented Design
(DOD) constraints, the universal cognitive bus, and the 256 component-type limit.

---

## Architectural Context

### Existing Engine Mechanisms Used

| Mechanism | Purpose in This Feature |
|---|---|
| `FastBTree` (`BrainTierBTree = 2`) | Sequential multi-phase logic for both commander and tank behaviors |
| `UnitRoster` / `UnitSubordinate` (Fdp.Core) | Native commander-subordinate relationships; no custom roster components |
| `Blackboard1024` (component ID 74) | Heavy mutable working memory for the commander, projected via `Unsafe.As` |
| `AssignTacticalIntentEvent` | Top-down order dispatch from commander to subordinates |
| `TacticalIntentResolutionSystem` (Hrot.CGF) | Bridges intent strings to `AssignBehaviorEvent` via `ITacticalOrderMapper` |
| `BehaviorIngressSystem` (Fdp.Toolkits) | Atomically updates `BehaviorState`, resets BTree execution pointer, parses JSON params |
| `BehaviorFinishedEvent` | Native terminal-state notification from `BTreeTickSystem` to mission layer |
| `LocomotionChannel` / `WeaponChannel` | CQRS actuator channels; behaviors write intents, muscle tier executes |
| `[WritesChannel]` attribute | Roslyn source generator emits failure-reset wrappers preventing zombie actions |
| `PathfindingBatchData` / `RaycastBatchData` | Pattern reference for the new EQS batch singleton |
| `SpatialGridData` singleton | Spatial hash grid on Muscle node, queried by the EQS solver |

### Invariants

- Behavior nodes never mutate physics transforms or ECS structure directly.
- All heavy state that cannot fit in the 60-byte parameter region uses `Blackboard1024`.
- No custom channel types beyond the three standard CQRS actuator channels.
- No phantom/satellite entities. Spatial awareness is provided by the EQS infrastructure.
- No managed heap allocations in hot-path behavior nodes.

---

## Phase 1: EQS Infrastructure (Area Query System)

**Goal:** Provide a generic, reusable capability for Brain-tier AI to query which entities
of a given force affiliation are located inside a polygon area entity.

The organic `TargetMemory` component (capped at 4 entries, `PerceptionConstants.MaxTrackedTargets`)
cannot serve area-wide reconnaissance. Spawning dummy observer entities is an anti-pattern.
Instead, the engine's established batch-singleton pattern (`PathfindingBatchData`,
`RaycastBatchData`) is extended with a new `AreaQueryBatchData` singleton.

### 1.1 Batch Singleton Types

Three unmanaged types are defined in `Fdp.Toolkits` (in a new `Spatial/Eqs` sub-folder)
and registered in `GlobalComponentIds` at ID 202:

**`AreaQueryRequest`** — submitted by a Brain BTree node:
- `long RequestId` — uniquely identifies this request, constructed from entity index + batch slot.
- `Entity TargetAreaEntity` — the area polygon entity whose `EditablePolyline` bounds the query.
- `ForceId TargetForce` — filters results to entities with this force affiliation.
- `int SourceNodeId` — network source node, used by translators for routing.

**`AreaQueryResult`** — written by the EQS solver:
- `long RequestId` — matches the corresponding request.
- `bool IsReady` — set to true when the solver has written results.
- `int TargetCount` — number of enemies found.
- `int TargetGroupHandle` — integer handle into an unmanaged `EqsTargetPool`, analogous to
  the `RouteHandle` in pathfinding.
- `int SourceNodeId` — for network routing on ingress.

**`AreaQueryBatchData`** (component ID 202, singleton) — holds pre-allocated
`NativeArray<AreaQueryRequest>` and `NativeArray<AreaQueryResult>` buffers at a fixed
capacity (initially 64 requests). Identical layout pattern to `PathfindingBatchData`.

**`EqsTargetPool`** (companion unmanaged singleton) — a fixed-capacity inline pool
that holds packed `Entity` values for resolved targets, addressed by `TargetGroupHandle`.
This avoids managed `List<Entity>` and keeps the entire pipeline allocation-free.

### 1.2 AreaQueryBatchHelper

A static helper class in `Fdp.Toolkits`, identical in style to `PathfindingBatchHelper`:
- `RequestAreaQuery(EntityRepository, entity, targetAreaEntity, ForceId)` — appends to
  `AreaQueryBatchData.Requests` and returns the `RequestId` (or -1 if batch full).
  The `RequestId` is packed as `((long)entityIndex << 32) | (uint)batchSlot`. The
  explicit cast to `long` before the shift is mandatory — a 20-bit shift on a 32-bit
  `int` silently truncates any entity index above 4095.
- `GetAreaQueryResult(EntityRepository, requestId)` — polls `AreaQueryBatchData.Results`
  and returns the matching `AreaQueryResult` (or `default` if not ready).
- `GetTargetFromPool(EntityRepository, targetGroupHandle, index)` — retrieves the packed
  `Entity` at the given index from `EqsTargetPool`.

Pool chunk lifetime is managed globally by `AreaQueryInitializationSystem` (see 1.4),
which resets the entire pool at the start of each Brain frame. Behavior nodes never
call a manual free function.

### 1.3 AreaQuerySolverSystem (Muscle Tier, SoD Module)

An `IEcsModule` named `EqsModule` runs on the Muscle node using
`ExecutionPolicy.SlowBackground(10)`. It processes pending `AreaQueryRequest` entries
against a background snapshot.

The solver:
1. Iterates the `AreaQueryBatchData.Requests` array up to `Count`.
2. Resolves the polygon from `EditablePolyline` on the `TargetAreaEntity`.
3. Queries `SpatialGridData` with a bounding circle broadphase, then does a precise
   2D point-in-polygon test for each candidate.
4. Filters by `EntityInfo.ForceId` matching the requested `TargetForce`.
5. Allocates a chunk in `EqsTargetPool`, writes packed entity handles.
6. Writes the `AreaQueryResult` into `AreaQueryBatchData.Results` with `IsReady = true`.

The solver runs against a read-only snapshot; all writes go through the
`IEntityCommandBuffer` harvested on the main thread by `ModuleHostKernel`.

In a networkless single-process environment (Editor), the solver runs in the same process
and the result is available within a few frames (no network latency).

### 1.4 AreaQueryInitializationSystem (Brain Tier, CGF)

An `IEcsModuleSystem` registered in `Hrot.CGF` at `SystemPhase.PreInput`. It runs at
the start of each simulation frame to reset the EQS batch state, mirroring how
`RaycastBatchData.Count` is zeroed globally each frame.

Responsibilities:
- Resets `AreaQueryBatchData.Count = 0` to accept new requests for the upcoming frame.
- Resets the `EqsTargetPool` free list so all pool chunks are available for the solver.

Because `Condition_IsAreaQueryResolved` and `Action_DispatchWaveWithTargets` execute in
the same BTree tick once a result becomes ready, all target handles are consumed before
the next frame's reset. Behavior nodes therefore require no manual pool-free calls;
the initialization system provides automatic lifetime management without risk of leaking
unmanaged memory under BTree branch preemption.

The singleton lives on whichever node hosts the ECS world being queried. In a distributed
deployment the Brain node has its own `AreaQueryBatchData` replica maintained by the
network translator pair (see 1.5).

### 1.5 EQS Network Translators

Four translator classes in `Hrot.Network.NED`, mirroring the pathfinding translator pattern:

- `AreaQueryBrainEgressTranslator` — drains `AreaQueryBatchData.Requests` on the Brain
  node, converts entity handles to network IDs via `NetworkEntityMap`, publishes DDS
  topic `AreaQueryRequestBatch`, and clears the batch count.
- `AreaQueryMuscleIngressTranslator` — on the Muscle node, reads `AreaQueryRequestBatch`,
  maps network IDs to local entities, writes into the Muscle node's `AreaQueryBatchData`.
- `AreaQueryMuscleEgressTranslator` — after the solver runs, drains completed results on
  the Muscle node, packages target entity IDs as network IDs, publishes
  `AreaQueryResponseBatch`.
- `AreaQueryBrainIngressTranslator` — on the Brain node, reads `AreaQueryResponseBatch`,
  maps network IDs back to local entities, writes `TargetGroupHandle` entries into
  `EqsTargetPool`, writes `AreaQueryResult` into `AreaQueryBatchData.Results`.

---

## Phase 2: Hill Attack Data Contracts

**Goal:** Define the unmanaged DTOs that govern both behaviors' memory layouts, fitting
all static configuration within the strict 60-byte `BrainBlackboard` parameter region
and all mutable working state into the `Blackboard1024` component.

### 2.1 Commander DTOs

**`PlatoonHillAttackParams`** (52 bytes, fits in 60-byte param region):
- Firing line segment: `StartX`, `StartY`, `EndX`, `EndY` (16 bytes).
- Baseline segment: `BaselineStartX`, `BaselineStartY`, `BaselineEndX`, `BaselineEndY`
  (16 bytes).
- Attack trajectory: `AttackDirX`, `AttackDirY`, `TankSpacing` (12 bytes).
- Target area: `TargetAreaEntity` (`Entity`, 8 bytes).

**`PlatoonHillAttackBlackboard`** — single-field wrapper used as `TBlackboard` type in
the `BTreeBuilder` expression-binding overloads. Contains one field: `Params`.

**`HillAttackMutableState`** (projected onto `Blackboard1024.Memory`):
- `int TotalSlots` — computed from segment length / `TankSpacing`.
- `byte CurrentWave` — 0 or 1.
- `long CachedEqsRequestId` — stores the request ID between `Action_RequestAreaQuery`
  and `Condition_IsAreaQueryResolved`.
- `int CachedTargetGroupHandle` — stores the `TargetGroupHandle` from the EQS result
  between `Condition_IsAreaQueryResolved` and `Action_DispatchWaveWithTargets`;
  initialized to -1.
- `ushort BurnedSlotsMask` — permanently blocked firing-line slot indices (wrecks).
- `ushort WaveUsedSlotsMask` — firing-line slots occupied in the current wave.
- `ushort BaselineReservedMask` — baseline slots currently reserved by live tanks.
- `int ActiveAttackerCount` — number of tanks currently executing the attack run.
- `fixed long ActiveEntityPacked[8]` — packed entity handles (SoA, decoupled from `UnitRoster`).
- `fixed byte ActiveSlotIndex[8]` — firing-line slot index per attacker.
- `fixed byte ReturnBaselineSlotIndex[8]` — baseline slot index per attacker.
- `fixed byte HasStartedRun[8]` — per-attacker flag initialized to 0 at dispatch time;
  set to 1 by `Condition_IsWaveCompleted` the first tick it observes the attacker's
  `BehaviorState.ActiveBehaviorHash` matching `HullDownAttackRun`. Only after this flag
  is 1 does a subsequent hash mismatch indicate a completed retreat. Prevents false
  completion detection during the one-frame window while `TacticalIntentResolutionSystem`
  and `BehaviorIngressSystem` process the `AssignTacticalIntentEvent`.

The SoA arrays use 8 entries because the maximum wave size is
`UnitRoster.MaxSubordinates / 2 = 8` (odd-indexed or even-indexed roster entries from
a 16-subordinate platoon).

### 2.2 Subordinate Tank DTOs

**`HullDownAttackParams`** (40 bytes, well within 60-byte param region):
- Firing slot: `SlotX`, `SlotY` (8 bytes).
- Baseline slot: `BaselineX`, `BaselineY` (8 bytes).
- Attack direction: `AttackDirX`, `AttackDirY` (8 bytes).
- Kinematic limits: `ApproachSpeed`, `CreepSpeed` (8 bytes).
- Assigned target: `TargetNetworkId` (`long`, 8 bytes) — the network-stable replication
  ID of the assigned target. Resolved to a local ECS `Entity` via `NetworkEntityMap`
  inside behavior nodes at runtime; the unmanaged params struct never stores a local
  generational entity pointer.

**`HullDownAttackBlackboard`** — single-field wrapper containing `Params`.

### 2.3 Architectural Notes on 1D Slot Parameterization

Firing-line and baseline slots are never pre-computed as Cartesian coordinates. They are
stored as 1D indices into the segment. Absolute coordinates are computed just-in-time
during dispatch using linear interpolation:

    t = slotIndex / (float)(TotalSlots - 1)
    X = Lerp(SegmentStartX, SegmentEndX, t)
    Y = Lerp(SegmentStartY, SegmentEndY, t)

This eliminates the need for `fixed float` arrays in the working state, dramatically
reducing the memory footprint and simplifying randomization to a bitmask operation.

Randomization of firing-line slot assignment: a random available bit index is selected
from `~(BurnedSlotsMask | WaveUsedSlotsMask)` (constrained to `TotalSlots`).

Baseline slot assignment per attacker: the commander iterates all baseline indices and
picks the one closest (by Euclidean distance-squared) to the assigned firing slot, from
those not already reserved in `BaselineReservedMask`.

---

## Phase 3: HullDownAttackRun Behavior (Subordinate Tank)

**Goal:** Implement the subordinate tank behavior and its tactical intent mapper.

### 3.1 Behavior Summary

The `HullDownAttackRun` behavior drives a single tank through a four-phase attack run:

1. **Approach** — navigate at `ApproachSpeed` toward the rough firing slot.
2. **Creep and Scan** — slow tactical creep along the attack direction vector, continuing
   past the rough slot until the assigned target enters line of sight. If the tank
   overshoots the slot by more than a defined tactical limit (default 50 m) without
   acquiring the target, `Action_CreepToAndBeyondSlot` returns `NodeStatus.Failure`,
   aborting the engagement attempt. The BTree topology guarantees `Action_ReverseToBaseline`
   still executes via an overshoot fallback node (see 3.2).
3. **Engagement** — halt forward movement, aim, and fire at the specific assigned target.
4. **Retreat** — reverse to the assigned baseline slot using `ReverseAllowed`.

Phases 1 and 2 are handled by a single action (`Action_CreepToAndBeyondSlot`) with an
internal distance check, interrupted by a `Selector` the moment `Condition_HasTarget`
succeeds. The tank never scans opportunistically. `Condition_HasTarget` resolves
`p.TargetNetworkId` to a local ECS entity via `NetworkEntityMap`, then scans
`TargetMemory` for that entity.

### 3.2 BTree Topology

```
Sequence
  Selector
    // Engagement path: creep until target visible, then fire
    Sequence
      Selector
        Condition_HasTarget          // succeeds when assigned target is in TargetMemory
        Action_CreepToAndBeyondSlot  // Running while creeping; Failure when overshoot limit exceeded
      Action_AimAndFireSpecific      // [WritesChannel(Weapon)]
    // Overshoot fallback: engagement path failed; proceed unconditionally to reverse
    Action_AbortEngagement           // always returns NodeStatus.Success immediately
  Action_ReverseToBaseline           // [WritesChannel(Locomotion)] — guaranteed to run
```

The outer `Selector` has two children. The first (the engagement `Sequence`) succeeds
when a target was found and fired upon. If it fails for any reason — including
`Action_CreepToAndBeyondSlot` returning `Failure` on overshoot — the second child
`Action_AbortEngagement` succeeds immediately, ensuring the outer `Selector` always
succeeds and `Action_ReverseToBaseline` always executes.

The Roslyn-generated failure-reset wrapper on `Action_CreepToAndBeyondSlot` (via
`[WritesChannel(Locomotion)]`) clears the `LocomotionChannel` automatically whenever the
action is preempted or returns `Failure`.

`Action_AbortEngagement` is a trivial one-line node with no channel writes:
`return NodeStatus.Success;`

`Action_AimAndFireSpecific` resolves `p.TargetNetworkId` via `NetworkEntityMap` to a
local `Entity`, writes it to `WeaponChannel`, then returns `NodeStatus.Running` while
the weapon channel reports the engagement in progress. It returns `NodeStatus.Success`
when either the weapon channel confirms the engagement concluded OR
`!repo.IsAlive(targetEntity)` — because standard executors do not natively detect target
destruction and would leave the node stuck in `Running` indefinitely.

`Action_ReverseToBaseline` writes the reverse locomotion intent to `LocomotionChannel`.
It returns `NodeStatus.Running` while `LocomotionChannel.Status == NodeStatus.Running`
and `NodeStatus.Success` when `LocomotionChannel.Status == NodeStatus.Success`.

### 3.3 ITacticalOrderMapper: HullDownAttackMapper

Registered in `TacticalIntentMapperRegistry` in `Hrot.CGF`. Maps the intent string
`"HullDownAttack"` to an `AssignBehaviorEvent` targeting the `HullDownAttackRun` behavior
name, passing `JsonParams` through unchanged.

The mapper checks that the entity has `TkbIdentity` with a tank entity type before
mapping. Non-tank entities silently return `false`, leaving the intent unhandled.

---

## Phase 4: PlatoonHillAttack Behavior (Commander)

**Goal:** Implement the platoon commander behavior that orchestrates the full hill attack.

### 4.1 Behavior Summary

The commander receives static geometry parameters (firing line, baseline, attack direction,
tank spacing, target area) and drives the platoon through:

1. **Preparation** — compute slot counts; dispatch all subordinates to baseline staging
   positions; wait until all are staged.
2. **Attack loop (infinite until area cleared)** — alternating waves:
   a. Request EQS area query.
   b. Poll for EQS result; terminate the loop if 0 targets remain.
   c. Distribute targets (round-robin) and randomized firing/baseline slots to the active
      wave; dispatch `AssignTacticalIntentEvent` ("HullDownAttack") for each tank.
   d. Monitor the wave until all dispatched tanks have finished their run or died.
   e. Toggle the wave index.

If a tank is destroyed mid-wave, its firing slot is permanently burned
(`BurnedSlotsMask`) and its baseline slot is freed (`BaselineReservedMask`).
The SoA tracker detects destruction via `EntityRepository.IsAlive` with O(1)
swap-remove.

When no targets remain, the Repeater propagates `NodeStatus.Failure` to the root
and `BTreeTickSystem` publishes `BehaviorFinishedEvent(Success)`.

### 4.2 BTree Topology

```
Sequence
  Action_CalculateSegments          // computes TotalSlots, inits masks
  Action_DispatchAllToBaseline      // sends MoveToLocation intent to all subordinates
  Condition_AreAllAtBaseline        // blocks until all tanks report NavigationStatus.Result == Arrived
  Repeater(-1)
    Sequence
      Action_RequestAreaQuery       // submits EQS request; caches RequestId in mutable state
      Condition_IsAreaQueryResolved // polls batch; Running->Success (targets found) / Failure (0 targets)
      Action_DispatchWaveWithTargets // distributes targets + slots, dispatches HullDownAttack intents
      Condition_IsWaveCompleted     // blocks until all active attackers done/dead
```

### 4.3 Node Attribute Requirements

| Node | Attribute |
|---|---|
| `Action_CalculateSegments` | `[SharedAiHeavyAction]` (5-arg, projects Blackboard1024 -> HillAttackMutableState) |
| `Action_DispatchAllToBaseline` | `[SharedAiHeavyAction]` |
| `Condition_AreAllAtBaseline` | `[SharedAiCondition]` (reads UnitRoster + NavigationStatus via repo) |
| `Action_RequestAreaQuery` | `[SharedAiHeavyAction]` (writes CachedEqsRequestId) |
| `Condition_IsAreaQueryResolved` | `[SharedAiHeavyCondition]` |
| `Action_DispatchWaveWithTargets` | `[SharedAiHeavyAction]` |
| `Condition_IsWaveCompleted` | `[SharedAiHeavyCondition]` |

### 4.4 Wave Dispatch Algorithm

For wave dispatch, the commander iterates `UnitRoster.Count`. Wave assignment is derived
from the stable `Entity.Index % 2` of each subordinate entity — NOT from the volatile
roster position `i`. When `UnitHierarchySystem` compacts the roster on entity destruction,
survivors shift left and their roster indices change, but their `Entity.Index` values
are immutable for the entity's lifetime. Using roster index parity would corrupt the
wave doctrine after any tank death.

A tank with `subordinate.Index % 2 == 0` belongs to wave 0; `% 2 == 1` belongs to
wave 1. If `roster.Count <= 3` all tanks are dispatched in a single wave (no alternation).

For each dispatched tank:
- Select a random firing slot from `~(BurnedSlotsMask | WaveUsedSlotsMask)`.
- Find the closest unreserved baseline slot using distance-squared comparison.
- Update `WaveUsedSlotsMask` and `BaselineReservedMask`.
- Record the tank and its slots in the SoA tracker.
- Compute JIT coordinates via linear interpolation.
- Query `NetworkIdentity.NetworkId` for each assigned target entity to obtain the
  network-stable ID; serialize it as `TargetNetworkId` in the JSON payload (never
  use the local packed entity handle, which is invalid across the network boundary).
- Publish `AssignTacticalIntentEvent { IntentId = "HullDownAttack", JsonParams = ... }`.
- Reset `state.CachedTargetGroupHandle = -1`. Pool chunk cleanup is automatic via
  `AreaQueryInitializationSystem` at the start of the next frame.

After dispatch, toggle `CurrentWave`.

### 4.5 Wave Completion Check

`Condition_IsWaveCompleted` iterates the SoA tracker backwards:
- If `!repo.IsAlive(attacker)`: permanently set `BurnedSlotsMask` bit; clear
  `BaselineReservedMask` bit; swap-remove the entry from the SoA arrays.
- If alive and `HasStartedRun[i] == 0`: check if
  `BehaviorState.ActiveBehaviorHash == HullDownAttackRun` hash. If so, set
  `HasStartedRun[i] = 1`. Do NOT remove the entry yet — the tank has just started.
  If the hash still doesn't match this tick, do nothing (intent is still in flight
  through `TacticalIntentResolutionSystem` / `BehaviorIngressSystem`).
- If alive and `HasStartedRun[i] == 1`: check `BehaviorState.ActiveBehaviorHash` — if
  it no longer matches the `HullDownAttackRun` hash, the run has finished (tank
  completed the retreat or aborted via overshoot). Swap-remove the entry; clear
  `BaselineReservedMask` bit.

Returns `NodeStatus.Success` when `ActiveAttackerCount == 0`.
Returns `NodeStatus.Running` otherwise.

---

## Phase 5: TKB Blueprint and Integration Validation

**Goal:** Ensure the commander entity carries the required ECS components and validate
end-to-end behavior correctness through scenario-based integration tests.

### 5.1 TKB Blueprint Requirements

The commander entity blueprint (TKB definition) must include:
- `BrainBlackboard` — standard behavior bus (already present on AI entities).
- `Blackboard1024` — heavy working memory for `HillAttackMutableState`.
- `UnitRoster` — commander-subordinate hierarchy (already standard on platoon commanders).
- `TargetMemory` — required if the commander also has a perception role; not needed for
  hill attack commander logic itself.

Subordinate tank entities must include:
- `BrainBlackboard`, `BehaviorState`, `BrainBTreeState`
- `LocomotionChannel`, `WeaponChannel`
- `NavState` (for `ReverseAllowed` support in reverse locomotion)
- `NavigationStatus` (CQRS feedback component read by `Condition_AreAllAtBaseline`;
  replicated to the Brain node from the Muscle tier)
- `TargetMemory` (for `Condition_HasTarget` evaluation)
- `UnitSubordinate` (linking back to the commander)

### 5.2 Integration Validation

A scenario-based integration test in `Hrot.SimHost.Tests` or `Hrot.CGF` tests should:
1. Spawn a 4-tank platoon with 1 commander in a test zone.
2. Assign the `PlatoonHillAttack` behavior to the commander with valid geometry params.
3. Spawn 2 enemy entities inside the target polygon area.
4. Run the simulation for N frames and verify:
   - All subordinates receive and execute `HullDownAttackRun` with distinct firing slots.
   - Both enemies are engaged (weapon channel events observed).
   - After all enemies are eliminated, `BehaviorFinishedEvent` is published for the commander.
   - No duplicate slot assignments (bitmask invariant).
5. Test the destruction case: kill one tank mid-wave and verify the slot is burned and
   the wave still completes with the surviving tanks.

---

## Architectural Decisions

| Decision | Rationale |
|---|---|
| EQS uses `NativeArray` batch singleton, not managed components | Consistent with `PathfindingBatchData`/`RaycastBatchData`; zero GC pressure; no structural ECS mutations |
| 1D slot indices stored, not 2D coordinates | Eliminates `fixed float` arrays; bitmask suffices for 16 slots; JIT interpolation at dispatch |
| SoA tracker decoupled from `UnitRoster` | `UnitHierarchySystem` compacts `UnitRoster` on entity destruction; SoA tracker remains stable |
| `Blackboard1024` reused (not a new component) | Preserves the 256 component-type budget |
| Wave assignment by `Entity.Index % 2`, not roster index | `UnitHierarchySystem` compacts `UnitRoster` on death, shifting indices; `Entity.Index` is immutable for the entity lifetime |
| `HasStartedRun` flag guards wave completion | `AssignTacticalIntentEvent` takes one frame to propagate through ingress pipeline; a premature hash check would falsely signal completion before the behavior starts |
| `Action_AbortEngagement` no-op in BTree | Guarantees `Action_ReverseToBaseline` runs even when `Action_CreepToAndBeyondSlot` fails on overshoot; avoids stranded tanks |
| `Repeater(-1)` terminates on child Failure | Standard FastBTree semantics; zero custom termination logic |
| `ReverseAllowed` flag on locomotion params | Delegated to muscle tier kinematics; cognitive tier writes intent only |
| `NavigationStatus` for baseline arrival check | `NavState` is Muscle-tier-only; Brain reads the CQRS feedback replica `NavigationStatus` |
| `TargetNetworkId` in `HullDownAttackParams` | Local ECS entity handles are node-local and invalid across network boundary; network ID is resolved at runtime via `NetworkEntityMap` |
| `AreaQueryInitializationSystem` owns pool lifetime | Prevents unmanaged pool leaks on BTree branch preemption; no manual free calls in behavior nodes |

---

## Phase 6: JSON Authoring DTO and ParseParams Delegate

**Goal:** Bridge the mission authoring tier (WGS-84 geodetic coordinates, network entity
IDs) to the simulation tier (`PlatoonHillAttackParams`, ENU Cartesian floats, local ECS
handles) without leaking managed or network-aware types into the behavior hot path.

### 6.1 Managed JSON DTO

`PlatoonHillAttackParamsJsonDto` lives in `Hrot.Map.Definitions.Behavior`. It exposes
five geographic parameters authored via the mission editor:
- `FiringLineStart`, `FiringLineEnd` — `PickableGeoPoint` values (map-pickable clicks).
- `BaselineStart`, `BaselineEnd` — `PickableGeoPoint` values.
- `TankSpacing` — float, default 30f.
- `TargetAreaNetworkId` — long, decorated with `[RemapNetworkId]` (Orchestrator patches
  the ID when transitioning from staging to live cluster) and
  `[MapPickableEntity("tactical_graphics")]` (restricts UI picker to area overlay entities).

The attack direction is NOT a user-authored field. It is computed at parse time as the
left-hand perpendicular of the normalized firing line vector so the facing is always
consistently derived from line geometry.

### 6.2 ParseParams Delegate

`ParsePlatoonHillAttackParams(string json, byte* ptr, IGeographicTransform, NetworkEntityMap)`
is a static unsafe method placed alongside the BTree node definitions. It runs on the
cold ingress path only (never in the BTree tick hot path).

Responsibilities:
1. Deserialize `PlatoonHillAttackParamsJsonDto` from JSON.
2. Convert all four `PickableGeoPoint` values to ENU Cartesian via `geoTransform.ToCartesian`.
3. Compute `AttackDir` as the left-hand perpendicular of the normalized firing line vector.
4. Resolve `TargetAreaNetworkId` to a local ECS entity via `NetworkEntityMap`
   (writes `Entity.Null` on failure; does not throw).
5. Write the fully populated `PlatoonHillAttackParams` directly to the 60-byte blackboard
   memory region via `Unsafe.Write(ptr, p)`.

### 6.3 Registry Binding

In `AiBehaviorFactory`, the `PlatoonHillAttack` `BehaviorDefinition` binds the delegate
as a closure capturing DI-injected `geoTransform` and `entityMap`:

```
ParseParams = (json, ptr) => ParsePlatoonHillAttackParams(json, ptr, geoTransform, entityMap)
```

`geoTransform` and `entityMap` are injected into `AiBehaviorFactory` at construction
time from the DI container.
