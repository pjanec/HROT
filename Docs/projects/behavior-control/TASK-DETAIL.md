# Behavior Control Subsystem – Task Details

**Reference design:** [DESIGN.md](./DESIGN.md)  
**Task tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## Phase 1 — FDP.Toolkit.Behavior: Core Infrastructure

Project: `Toolkits/FDP.Toolkit.Behavior/FDP.Toolkit.Behavior.csproj`  
References: `Fdp.Kernel`, `Fdp.Interfaces`, `FastBTree (Fbt.Kernel)`, `FastHSM (Fhsm.Kernel)`

---

### BCS-P1-T1 — Behavior Component Types

**Goal:** Define all unmanaged ECS component structs, action ID constants, and the `IActionExecutor<T>` interface used throughout the Behavior toolkit.

**Files to create:**
- `Toolkits/FDP.Toolkit.Behavior/Components/BehaviorComponents.cs` — `DoctrineState`, `BrainBlackboard`, `SimTier`, `ActorCapabilityState`
- `Toolkits/FDP.Toolkit.Behavior/Components/ChannelComponents.cs` — `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`
- `Toolkits/FDP.Toolkit.Behavior/Components/BrainComponents.cs` — `BrainBTreeState`, `BrainHsm64`, `BrainHsm128`
- `Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs` — `MissionPlanQueue`, `MissionPhase`, `MissionTrigger`
- `Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs` — `IActionExecutor<TChannel>` interface

**Key implementation notes:**
- `LocomotionChannel`, `WeaponChannel`, `InteractionChannel` must share the same memory layout: `ushort ActiveAction`, `uint DoctrineInstanceId`, `uint ActionInstanceId`, `uint DispatchedInstanceId`, `NodeStatus Status`, `fixed byte Params[32]`, `fixed byte State[32]` — total ≤ 96 bytes.
- All structs decorated with `[StructLayout(LayoutKind.Sequential)]`.
- `BrainBTreeState` wraps `Fbt.BehaviorTreeState` (64 bytes).
- `BrainHsm64` wraps `Fhsm.Kernel.Data.HsmInstance64`, `BrainHsm128` wraps `HsmInstance128`.
- `IActionExecutor<TChannel>` has `void OnEnter(Entity, ref TChannel, EntityRepository)`, `void Execute(Entity, ref TChannel, EntityRepository, float dt)`, `void OnExit(Entity, ref TChannel, EntityRepository)`.
- `ActorCapabilities` is a `[Flags] enum : byte` with `None=0, CanMove=1, CanShoot=2, CanInteract=4`.

**Reference:** [DESIGN.md §3.1](./DESIGN.md#31-component-types), design talk lines 600–700, 1550–1640

**Success conditions:**
```csharp
// Unit tests in Toolkits/FDP.Toolkit.Behavior.Tests/ComponentLayoutTests.cs
[Fact] void LocomotionChannel_SizeIsAtMost96Bytes() =>
    Assert.True(Unsafe.SizeOf<LocomotionChannel>() <= 96);

[Fact] void WeaponChannel_SameLayoutAsLocomotionChannel() =>
    Assert.Equal(Unsafe.SizeOf<LocomotionChannel>(), Unsafe.SizeOf<WeaponChannel>());

[Fact] void BrainBTreeState_Contains_BehaviorTreeState() {
    var s = new BrainBTreeState();
    Assert.Equal(0, s.State.RunningNodeIndex);
}

[Fact] void BrainHsm128_Contains_HsmInstance128() {
    var h = new BrainHsm128();
    // HsmInstance128 must be accessible through h.State
    Assert.Equal(0, Unsafe.SizeOf<HsmInstance128>() > 0 ? 0 : -1); 
}

[Fact] void ActorCapabilities_CanMove_Is_Bit0() =>
    Assert.Equal(1, (int)ActorCapabilities.CanMove);
```

---

### BCS-P1-T2 — ChannelArbitrationSystem

**Goal:** Detect stale channels (whose `DoctrineInstanceId` no longer matches `DoctrineState.InstanceId`) and reset them so the dispatcher won't invoke a dead executor.

**File:** `Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs`

**Logic:**
- Query: `(DoctrineState, LocomotionChannel)`, same for `WeaponChannel` and `InteractionChannel`.
- For each entity: if `channel.DoctrineInstanceId != doctrine.InstanceId` and `channel.ActiveAction != 0`, reset: `channel.ActiveAction = 0`, `channel.ActionInstanceId++`, `channel.Status = NodeStatus.Failure`.
- Register with `[UpdateInGroup(typeof(SimulationSystemGroup))]`.

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-systems), design talk lines 1665–1710

**Success conditions:**
```csharp
// ChannelArbitrationTests.cs
[Fact] void Arbitration_ClearsChannel_WhenDoctrineInstanceIdMismatch() {
    var world = TestWorldFactory.Create();
    var e = world.CreateEntity();
    world.AddComponent(e, new DoctrineState { InstanceId = 2 });
    world.AddComponent(e, new LocomotionChannel { DoctrineInstanceId = 1, ActiveAction = 3 });
    
    var sys = new ChannelArbitrationSystem();
    sys.World = world;
    sys.Update();
    
    var ch = world.GetComponent<LocomotionChannel>(e);
    Assert.Equal(0, ch.ActiveAction);
    Assert.Equal(NodeStatus.Failure, ch.Status);
}

[Fact] void Arbitration_DoesNotClearValidChannel() {
    // DoctrineInstanceId matches → channel untouched
}
```

---

### BCS-P1-T3 — LocomotionDispatcherSystem

**Goal:** Route the active `LocomotionChannel` action to the registered `IActionExecutor<LocomotionChannel>` with proper capability check, `OnEnter`/`OnExit` lifecycle calls, and `DispatchedInstanceId` tracking.

**File:** `Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs`

**Logic:**
1. Query: `(LocomotionChannel, ActorCapabilityState)`.
2. If `!CanMove` and channel is `Running` → set `Failure`, `continue`.
3. If `channel.ActionInstanceId != channel.DispatchedInstanceId`:
   - Look up `_previousAction` per entity (store in a parallel array indexed by entity index).
   - Call `_executors[oldAction]?.OnExit(...)`.
   - Call `_executors[channel.ActiveAction]?.OnEnter(...)`.
   - `channel.DispatchedInstanceId = channel.ActionInstanceId`.
   - Update `_previousAction`.
4. If `channel.ActiveAction != 0` and `channel.Status == Running` → call `_executors[channel.ActiveAction]?.Execute(...)`.
5. Public method: `RegisterExecutor(ushort actionId, IActionExecutor<LocomotionChannel> executor)`.

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-systems), design talk lines 1740–1830

**Success conditions:**
```csharp
[Fact] void Dispatcher_CallsOnEnter_OnFirstTick() {
    // Register a spy executor; verify OnEnter called exactly once on first tick, Execute on subsequent ticks
}
[Fact] void Dispatcher_CallsOnExit_WhenActionChanges() {
    // Change ActionInstanceId → verify old executor.OnExit() called
}
[Fact] void Dispatcher_FailsChannel_WhenCannotMove() {
    // CanMove=false, Status=Running → Status becomes Failure, Execute not called
}
[Fact] void Dispatcher_SkipsNullExecutor_Gracefully() {
    // No executor registered for active action → no exception
}
```

---

### BCS-P1-T4 — WeaponDispatcherSystem + InteractionDispatcherSystem

**Goal:** Same dispatcher pattern as `LocomotionDispatcherSystem` but for `WeaponChannel` (checks `CanShoot`) and `InteractionChannel` (checks `CanInteract`).

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs`
- `Toolkits/FDP.Toolkit.Behavior/Systems/InteractionDispatcherSystem.cs`

**Note:** Consider a generic base `DispatcherSystemBase<TChannel>` to avoid duplication, if FDP's generic system registration supports it.

**Reference:** design talk lines 1835–1870

**Success conditions:**
```csharp
[Fact] void WeaponDispatcher_FailsChannel_WhenCannotShoot() { ... }
[Fact] void InteractionDispatcher_RunsExecutor_WhenCanInteract() { ... }
```

---

### BCS-P1-T5 — BTreeTickSystem (FastBTree Adapter)

**Goal:** Step `BrainBTreeState` for all entities with `DoctrineState.BrainTier == 2` that have a BTree brain; provide a concrete `BTreeContext` that exposes `EntityRepository` + entity to BTree node methods.

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`
- `Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs`

**Logic:**
- Query: `(DoctrineState, BrainBTreeState, BrainBlackboard)`.
- For each: look up `BTreeBlobAsset` from `DoctrineRegistry` by `doctrine.ActiveDoctrineId`.
- Call `FastBTree.Interpreter.Tick(ref blackboard, ref btState.State, context, entity, btBlob)` — adapt IAIContext from source.
- `[UpdateAfter(typeof(ChannelArbitrationSystem))]`.

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-systems), design talk lines 1718–1745, FastBTree `IAIContext` at `ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs`

**Success conditions:**
```csharp
[Fact] void BTreeTick_DoesNotThrow_WhenBlobNotRegistered() {
    // Missing doctrine ID → skip gracefully, no exception
}
[Fact] void BTreeTick_DoesNotTick_WhenBrainTierIsNotTwo() {
    // SimTier=1 entities skipped
}
[Fact] void BTreeTick_ProducesChannelWrite_ForRegisteredTree() {
    // Manually register a simple one-node ActionBTree; confirm it writes LocomotionChannel
}
```

---

### BCS-P1-T6 — HsmTickSystem\<T\> (FastHSM Adapter)

**Goal:** Generic HSM adapter that steps `HsmInstance64` or `HsmInstance128` per entity using `Fhsm.Kernel.HsmKernel.UpdateBatch`.

**File:** `Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs`

**Key points:**
- Generic type parameter `THsmComponent` constrained to `unmanaged, IHsmComponent` (define `IHsmComponent` interface with property `ref TInstance GetInstance()` or simply access by known struct layout).
- Registered twice: `new HsmTickSystem<BrainHsm64>()` and `new HsmTickSystem<BrainHsm128>()`.
- Context passed to HSM actions must contain a pointer to the entity and the world: define `unsafe struct FdpHsmContext { Entity Self; EntityRepository* World; }`.
- `[UpdateAfter(typeof(HsmDamageBridgeSystem))]`, `[UpdateAfter(typeof(ChannelArbitrationSystem))]`.

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-systems), design talk lines 1102–1190 (HSM integration), `ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs`

**Success conditions:**
```csharp
[Fact] void HsmTick_TransitionsState_OnRegisteredEvent() {
    // Build minimal HSM (State A --EventX--> State B)
    // Push EventX into HsmInstance128
    // Run one tick → verify state is B
}
[Fact] void HsmTick64_And_HsmTick128_AreIndependent() {
    // Entity with Hsm64 only → Hsm128 system skips it
}
```

---

### BCS-P1-T7 — DoctrineRegistry + DoctrineIngressSystem

**Goal:** At startup, register doctrine definitions (name → BrainTier, blob asset ID, JSON parser). At runtime, consume `AssignDoctrineEvent` to update `BrainBlackboard` and `DoctrineState`.

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/DoctrineRegistry.cs`
- `Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs`
- `Toolkits/FDP.Toolkit.Behavior/Events/AssignDoctrineEvent.cs`

**Logic in `DoctrineIngressSystem`:**
- Consumes managed `AssignDoctrineEvent { Entity, DoctrineName, JsonParams }`.
- Looks up registry by `DoctrineName.GetHashCode()`.
- Calls `def.ParseParams(json, *blackboard.Memory)`.
- Updates `DoctrineState.ActiveDoctrineId`, increments `InstanceId`, sets `BrainTier`.
- Resets `BrainBTreeState` or HSM instance to zero-state.

**Reference:** [DESIGN.md §3.3](./DESIGN.md#33-doctrine-registry--parameter-flow), design talk lines 4540–4620 (parameter flow)

**Success conditions:**
```csharp
[Fact] void DoctrineIngress_ParsesFleeBlackboard_FromJson() {
    // Register "FleeToSafety" doctrine with FleeBlackboard parser
    // Publish AssignDoctrineEvent with { safeDist: 50.0 }
    // Run system → verify BrainBlackboard bytes match FleeBlackboard { SafeDistance=50.0 }
}
[Fact] void DoctrineIngress_IncrementsInstanceId() { ... }
[Fact] void DoctrineIngress_ResetsBTreeState_OnNewDoctrine() {
    // BrainBTreeState.State.RunningNodeIndex → 0 after assignment
}
```

---

## Phase 2 — FDP.Toolkit.Perception

Project: `Toolkits/FDP.Toolkit.Perception/FDP.Toolkit.Perception.csproj`  
References: `Fdp.Kernel`, `ModuleHost.Core`, `FDP.Toolkit.CarKinem` (for `VehicleState`, `SpatialHashGrid`)

---

### BCS-P2-T1 — Perception Component Types

**Goal:** Define all unmanaged perception components and event types.

**Files:**
- `Toolkits/FDP.Toolkit.Perception/Components/PerceptionComponents.cs` — `Faction`, `PerceptionReceptor`, `TargetMemory`
- `Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs` — `AudioStimulusEvent`, `LosCheckRequestEvent`, `TargetVisibleEvent`

**Key notes:**
- `TargetMemory`: `int Count; fixed long EntityIds[4]; fixed float PositionsX[4]; fixed float PositionsY[4]; fixed float ThreatScores[4]; fixed uint LastSeenTick[4]`
- `PerceptionReceptor.FieldOfViewCos` is the precomputed cosine of the half-FOV angle (e.g., 60° FOV → cos(30°) ≈ 0.866).
- All events: `[EventId(…)]` attribute from `Fdp.Kernel`. Use 4001–4003 as defined in design.

**Reference:** [DESIGN.md §4.1–4.2](./DESIGN.md#41-component-types), design talk lines 2450–2510

**Success conditions:**
```csharp
[Fact] void TargetMemory_IsUnmanaged() =>
    Assert.True(typeof(TargetMemory).IsValueType);
    
[Fact] void TargetMemory_MaxFourSlots() {
    var tm = new TargetMemory();
    tm.Count = 4;
    // Set slot 3 EntityId → no overflow
}
```

---

### BCS-P2-T2 — AudioPerceptionSystem (Main Thread)

**Goal:** Main-thread system that consumes `AudioStimulusEvent` from the bus and directly updates `TargetMemory` for all entities within hearing range.

**File:** `Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs`

**Logic:**
- Run in `Simulation` phase.
- For each event: query `SpatialHashGrid` for entities with `PerceptionReceptor` within `event.Intensity` radius.
- For each listener: if it can hear (HearingRange ≥ distance), call `AddOrUpdateTarget(ref TargetMemory, source, origin, boost=20, tick)`.
- Sort by threat score, keep top 4.

**Reference:** [DESIGN.md §4.3](./DESIGN.md#43-systems), design talk lines 303–316

**Success conditions:**
```csharp
[Fact] void AudioPerception_UpdatesTargetMemory_WhenWithinHearingRange() {
    // Spawn listener at (0,0) with HearingRange=100
    // Publish AudioStimulusEvent at (50,0) with Intensity=60
    // Run system → TargetMemory.Count == 1
}
[Fact] void AudioPerception_IgnoresEntity_OutsideHearingRange() {
    // Listener at (0,0), event at (200,0), HearingRange=100 → Count==0
}
```

---

### BCS-P2-T3 — PerceptionModule (Async Vision Broadphase)

**Goal:** An async `IModule` running at 10Hz with SoD; performs spatial broadphase + FOV cone filtering; emits `LosCheckRequestEvent` via ECB; decays and integrates threats in `ThreatEvaluationSystem`.

**Files:**
- `Toolkits/FDP.Toolkit.Perception/PerceptionModule.cs`
- `Toolkits/FDP.Toolkit.Perception/Systems/VisionBroadphaseSystem.cs`
- `Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs`

**Key implementation notes:**
- `VisionBroadphaseSystem`:
  - For each entity with `PerceptionReceptor`: query `SpatialHashGrid` within `VisionRange`.
  - Filter by `Faction` (different team).
  - FOV check: `Vector2.Dot(forward, toTarget_normalized) >= FieldOfViewCos`.
  - Emit `LosCheckRequestEvent` via ECB if passes broadphase.
- `ThreatEvaluationSystem`:
  - Consume `TargetVisibleEvent` and `AudioStimulusEvent` from accumulated event history.
  - Decay existing scores by `dt * 0.1f`.
  - `AddOrUpdateTarget` for visible events (boost=50), audio events (boost=20).
  - Write back via `ECB.SetComponent<TargetMemory>`.

**Reference:** [DESIGN.md §4.3–4.4](./DESIGN.md#44-perceptionmodule), design talk lines 2520–2660

**Success conditions:**
```csharp
// Integration test: spawn two faction entities, run 3 perception cycles (300ms sim)
[Fact] async Task PerceptionModule_PopulatesTargetMemory_ForEnemyInSight() {
    // Entity A (Blue), Entity B (Red) at distance 20 within FOV
    // Run module → TargetMemory on A contains B
}
[Fact] void VisionBroadphase_ExcludesSameFaction() { ... }
[Fact] void VisionBroadphase_ExcludesOutsideFOV() { ... }
```

---

### BCS-P2-T4 — LosRequestBatchingSystem & TargetMemory Integration

**Goal:** Bridge `LosCheckRequestEvent` from the async module to the `RaycastBatchData` physics singleton (main thread). Also apply `TargetVisibleEvent` results back to main world when raycasts confirm visibility.

**File:** `Toolkits/FDP.Toolkit.Perception/Systems/LosRequestBatchingSystem.cs`

**Logic:**
- `[UpdateInPhase(BeforeSync)]` (runs before physics batch is solved).
- Consume `LosCheckRequestEvent` from bus.
- For each: add entry to `RaycastBatchData.Requests` with `RayId = PackIds(observer.Index, target.Index)`.
- After physics solves (next frame `Input`), `HitResolutionSystem` in the Combat toolkit will emit `TargetVisibleEvent` for rays that do NOT hit terrain (or hit intended target).

**Note:** For the demo (no real terrain geometry), simplify: skip LOS rays, treat broadphase visibility as confirmed. A `LOS_MOCK_MODE` compile flag allows bypassing actual ray submission and directly emitting `TargetVisibleEvent`.

**Reference:** [DESIGN.md §4.3](./DESIGN.md#43-systems), design talk lines 2580–2620

**Success conditions:**
```csharp
[Fact] void LosRequestBatching_AddsToRaycastBatch() {
    // Emit LosCheckRequestEvent → verify RaycastBatchData.Count increased
}
```

---

## Phase 3 — FDP.Toolkit.Navigation

Project: `Toolkits/FDP.Toolkit.Navigation/FDP.Toolkit.Navigation.csproj`  
References: `Fdp.Kernel`, `FDP.Toolkit.Behavior`, `FDP.Toolkit.CarKinem`

---

### BCS-P3-T1 — Navigation Action IDs + Parameter Structs

**Goal:** Define all locomotion action IDs (constants) and their parameter/state structs.

**File:** `Toolkits/FDP.Toolkit.Navigation/NavigationActions.cs`

**Structs** (all `unmanaged`, ≤ 32 bytes):
- `MoveToParams { Vector2 Destination; float ArrivalRadius; float Speed; }`
- `FleeParams { Entity Threat; float SafeDistance; float Speed; }`
- `FleeState { uint NextReplanTick; }`
- `FollowRouteParams { int TrajectoryId; byte IsLooped; }`
- `FollowRoadGraphParams { int TargetNodeId; float Speed; }`

**Reference:** [DESIGN.md §5.1](./DESIGN.md#51-action-ids-and-parameter-structs), design talk lines 1967–2000

**Success conditions:**
```csharp
[Fact] void MoveToParams_FitsInChannel() =>
    Assert.True(Unsafe.SizeOf<MoveToParams>() <= 32);
[Fact] void FleeParams_FitsInChannel() =>
    Assert.True(Unsafe.SizeOf<FleeParams>() <= 32);
[Fact] void FleeState_FitsInChannel() =>
    Assert.True(Unsafe.SizeOf<FleeState>() <= 32);
```

---

### BCS-P3-T2 — MoveToExecutor

**Goal:** `IActionExecutor<LocomotionChannel>` that `OnEnter` configures `NavState.FinalDestination/ArrivalRadius/TargetSpeed/Mode` and `Execute` reads `NavState.HasArrived` to report `Success`.

**File:** `Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs`

**Frustration guard:** If `VehicleState.Speed < 0.1f` for 120 consecutive ticks while `Distance > ArrivalRadius*2`, set `Status = Failure`.

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-executor-classes), design talk lines 2000–2050

**Success conditions:**
```csharp
[Fact] void MoveToExecutor_ReportsSuccess_WhenHasArrived() {
    // Set NavState.HasArrived=1 → Execute sets Status=Success
}
[Fact] void MoveToExecutor_ReportsFailure_OnFrustration() {
    // Speed stays near-zero for > 120 ticks → Status=Failure
}
[Fact] void MoveToExecutor_OnExit_SetsTargetSpeedToZero() { ... }
```

---

### BCS-P3-T3 — FleeExecutor

**Goal:** Throttled (30 ticks) escape vector calculation away from threat entity; stops when `Distance > SafeDistance` or threat is dead.

**File:** `Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs`

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-executor-classes), design talk lines 2050–2110

**Success conditions:**
```csharp
[Fact] void FleeExecutor_ReportsSuccess_WhenSafeDistanceReached() { ... }
[Fact] void FleeExecutor_ReportsSuccess_WhenThreatIsDead() { ... }
[Fact] void FleeExecutor_RecalculatesFleeVector_AfterThrottle() {
    // Runs 31 frames → verify NavState.FinalDestination was updated twice
}
```

---

### BCS-P3-T4 — FollowRoadGraphExecutor

**Goal:** Sets `NavState.Mode = RoadGraph`, `TargetNodeId`, and `TargetSpeed`. Reports `Success` when `NavState.HasArrived`.

**File:** `Toolkits/FDP.Toolkit.Navigation/Executors/FollowRoadGraphExecutor.cs`

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-executor-classes)

**Success conditions:**
```csharp
[Fact] void FollowRoadGraphExecutor_SetsRoadGraphMode() {
    // OnEnter → NavState.Mode == NavigationMode.RoadGraph
}
```

---

### BCS-P3-T5 — FollowRouteExecutor

**Goal:** Sets `NavState.Mode = CustomTrajectory`, `TrajectoryId`. Detects end-of-route and sets `Success` (or loops if `IsLooped`).

**File:** `Toolkits/FDP.Toolkit.Navigation/Executors/FollowRouteExecutor.cs`

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-executor-classes)

**Success conditions:**
```csharp
[Fact] void FollowRouteExecutor_LoopsRoute_WhenFlagSet() { ... }
[Fact] void FollowRouteExecutor_ReportsSuccess_WhenNotLooped() { ... }
```

---

## Phase 4 — FDP.Toolkit.Physics

Project: `Toolkits/FDP.Toolkit.Physics/FDP.Toolkit.Physics.csproj`  
References: `Fdp.Kernel`, `FDP.Toolkit.CarKinem` (for `SpatialGridData`)

---

### BCS-P4-T1 — PhysicsCollider + RaycastBatchData

**Goal:** Define `PhysicsCollider` component and the `RaycastBatchData` singleton with its request/response array structs. Provide a module `PhysicsToolkitModule` that initializes the singleton with pre-allocated `NativeArray` buffers.

**Files:**
- `Toolkits/FDP.Toolkit.Physics/Components/PhysicsComponents.cs`
- `Toolkits/FDP.Toolkit.Physics/PhysicsToolkitModule.cs`

**Sizes:** Pre-allocate `Requests[4096]`, `Hits[4096]` at startup.

**Reference:** [DESIGN.md §7.1](./DESIGN.md#71-component-types--singletons), design talk lines 2820–2860

**Success conditions:**
```csharp
[Fact] void PhysicsModule_Initialize_CreatesSingleton() {
    // Register PhysicsToolkitModule; Initialize → HasSingleton<RaycastBatchData>() == true
}
[Fact] void RaycastBatchData_Capacity_Is4096() { ... }
```

---

### BCS-P4-T2 — Intersection2D Math

**Goal:** Static utility `Intersection2D.RaycastCircle(start, end, center, radius, out float t)` using quadratic discriminant method. Returns `true` on hit, `t ∈ [0,1]` along segment.

**File:** `Toolkits/FDP.Toolkit.Physics/Math/Intersection2D.cs`

**Reference:** [DESIGN.md §7.2](./DESIGN.md#72-math), design talk lines 2860–2920

**Success conditions:**
```csharp
[Fact] void RaycastCircle_HitsCenter() {
    bool hit = Intersection2D.RaycastCircle(
        new Vector2(-5, 0), new Vector2(5, 0),
        Vector2.Zero, 1f, out float t);
    Assert.True(hit);
    Assert.InRange(t, 0.35f, 0.45f); // ~t=0.4 when r=1, ray length=10
}
[Fact] void RaycastCircle_MissesCircle() {
    bool hit = Intersection2D.RaycastCircle(
        new Vector2(-5, 5), new Vector2(5, 5), Vector2.Zero, 1f, out _);
    Assert.False(hit);
}
[Fact] void RaycastCircle_MissesCircle_WhenSegmentTooShort() {
    // Circle at (3,0), segment from (-5,0) to (-1,0) — doesn't reach circle
    Assert.False(...);
}
[Fact] void RaycastCircle_ReturnsTMin_WhenTwoIntersections() {
    // Ray through full diameter → t is the entry point
}
```

---

### BCS-P4-T3 — RaycastSolverSystem

**Goal:** Main-thread system in `Input` phase; fans out across all CPU cores via `Parallel.For`; for each request queries `SpatialHashGrid` and tests circle intersections; writes to `Hits`.

**File:** `Toolkits/FDP.Toolkit.Physics/Systems/RaycastSolverSystem.cs`

**Key implementation notes:**
- `Parallel.For` over `[0, batchData.Count)`, each iteration writes exclusively to `batchData.Hits[i]` (lock-free).
- Query `SpatialHashGrid` with AABB expanded by `maxRadius=5m` around ray.
- Verify `LayerMask & collider.CollisionLayer != 0` before testing.
- Skip `req.IgnoreEntity`.

**Reference:** [DESIGN.md §7.3](./DESIGN.md#73-systems), design talk lines 2930–3010

**Success conditions:**
```csharp
[Fact] void RaycastSolver_DetectsHit_WhenBulletPathCrossesCollider() {
    // Spawn entity at (5,0) with PhysicsCollider(r=1)
    // Request ray from (-5,0) to (10,0)
    // Run solver → Hits[0].HasHit == true, HitEntity == spawned entity
}
[Fact] void RaycastSolver_ReturnsNoHit_WhenNoEntitiesInPath() { ... }
[Fact] void RaycastSolver_RespectsLayerMask() {
    // Entity on layer=2, request with LayerMask=1 → no hit
}
[Fact] void RaycastSolver_IgnoresIgnoreEntity() { ... }
[Fact] void RaycastSolver_ReturnsClosestHit_WhenMultipleInPath() { ... }
```

---

### BCS-P4-T4 — HitResolutionSystem (Physics→Combat bridge)

**Goal:** `Input`-phase system (after `RaycastSolverSystem`) that iterates `RaycastBatchData.Hits`, emits `HitEvent` for bullets and `TargetVisibleEvent` for LOS requests, then destroys hit bullet entities.

**File:** `Toolkits/FDP.Toolkit.Physics/Systems/HitResolutionSystem.cs`

**Logic:** Use `RayId` high/low bits to distinguish bullet rays (by entity index) from LOS rays (packed observer+target IDs). Publish events accordingly. Set `batchData.Count = 0` after processing.

**Reference:** [DESIGN.md §6.4 + 4.3](./DESIGN.md), design talk lines 2210–2260

**Success conditions:**
```csharp
[Fact] void HitResolution_EmitsHitEvent_ForBulletHit() { ... }
[Fact] void HitResolution_EmitsTargetVisibleEvent_ForLosHit() { ... }
[Fact] void HitResolution_ClearsCount_AfterProcessing() { ... }
```

---

## Phase 5 — FDP.Toolkit.Combat

Project: `Toolkits/FDP.Toolkit.Combat/FDP.Toolkit.Combat.csproj`  
References: `Fdp.Kernel`, `FDP.Toolkit.Behavior`, `FDP.Toolkit.Perception`, `FDP.Toolkit.Physics`, `FDP.Toolkit.CarKinem`

---

### BCS-P5-T1 — Combat Component Types

**File:** `Toolkits/FDP.Toolkit.Combat/Components/CombatComponents.cs`

Components: `WeaponState`, `Health`, `BallisticProjectile`.

**Reference:** [DESIGN.md §6.1](./DESIGN.md#61-component-types), design talk lines 2060–2090

**Success conditions:**
```csharp
[Fact] void WeaponState_IsUnmanaged() => Assert.True(typeof(WeaponState).IsValueType);
[Fact] void Health_DefaultIsZero() { var h = new Health(); Assert.Equal(0f, h.Current); }
```

---

### BCS-P5-T2 — Combat Events

**File:** `Toolkits/FDP.Toolkit.Combat/Events/CombatEvents.cs`  
Events: `FireRequestEvent [EventId 5001]`, `HitEvent [EventId 5002]`.

**Success conditions:**
```csharp
[Fact] void FireRequestEvent_HasEventIdAttribute() { ... }
```

---

### BCS-P5-T3 — AimAndFireExecutor

**Goal:** Executor registered to `WeaponDispatchers`. Validates target alive, checks ammo and cooldown, emits `FireRequestEvent`. Marks `Status=Success` when target is dead, `Status=Failure` when ammo exhausted.

**File:** `Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs`

**Reference:** [DESIGN.md §6.3–6.4](./DESIGN.md#63-action-ids), design talk lines 2094–2160

**Success conditions:**
```csharp
[Fact] void AimAndFire_EmitsFireRequest_WhenConditionsAreMet() {
    // Ammo=5, Cooldown=0, Target alive → FireRequestEvent on bus
}
[Fact] void AimAndFire_DoesNotFire_WhenCooldownActive() { ... }
[Fact] void AimAndFire_ReportsFailure_WhenAmmoZero() { ... }
[Fact] void AimAndFire_ReportsSuccess_WhenTargetDead() { ... }
```

---

### BCS-P5-T4 — FireProcessingSystem + BallisticsSystem

**Goal:**  
- `FireProcessingSystem` (`Simulation` phase): consumes `FireRequestEvent`, creates `BallisticProjectile` entities.  
- `BallisticsSystem` (`PostSimulation` phase): moves bullets by `Velocity * dt`; adds `RaycastRequest` to batch. Despawns bullets older than 300 ticks.

**Files:**
- `Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs`
- `Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs`

**Reference:** [DESIGN.md §6.4](./DESIGN.md#64-systems), design talk lines 2170–2240

**Success conditions:**
```csharp
[Fact] void FireProcessing_SpawnsBullet_OnFireRequestEvent() {
    // Publish FireRequestEvent → query BallisticProjectile → Count == 1
}
[Fact] void Ballistics_DestroysBullet_AfterTimeout() {
    // Set SpawnTick = GlobalVersion - 300 → next BallisticsSystem tick destroys it
}
[Fact] void Ballistics_AddsSingleRaycastRequest_PerBulletPerFrame() { ... }
```

---

### BCS-P5-T5 — DamageSystem

**Goal:** Consume `HitEvent`; apply damage to `Health`; if `Health.Current <= 0`, strip `CanMove` and `CanShoot` from `ActorCapabilityState`. Handle `PassengerBuffer` unloading on vehicle death.

**File:** `Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`

**Reference:** [DESIGN.md §6.4](./DESIGN.md#64-systems), design talk lines 2370–2415

**Success conditions:**
```csharp
[Fact] void Damage_ReducesHealth() {
    // Health(100), HitEvent(50) → Health.Current == 50
}
[Fact] void Damage_StripsCapabilities_OnLethalHit() {
    // HitEvent(200), Health(100) → CanMove==false, CanShoot==false
}
[Fact] void Damage_DoesNotCrash_WhenTargetHasNoCapabilities() { ... }
```

---

## Phase 6 — FDP.Toolkit.Behavior: Advanced Features

---

### BCS-P6-T1 — MissionPlanQueue + MissionDirectorSystem

**Goal:** Fixed-size mission phase queue. System evaluates trigger condition of current phase each frame; advances queue and updates `DoctrineState` when trigger fires.

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/Components/MissionComponents.cs` (add `MissionPlanQueue`, `MissionPhase`, `MissionTrigger`)
- `Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`

**Reference:** [DESIGN.md §8.1](./DESIGN.md#81-mission-plan-queue), design talk lines 4058–4138

**Success conditions:**
```csharp
[Fact] void MissionDirector_AdvancesPhase_WhenTimerElapses() {
    // Phase0: DoctrineA, TimerElapsed(0.5s)
    // Run 31 ticks at 60Hz → DoctrineState.ActiveDoctrineId == DoctrineB
}
[Fact] void MissionDirector_AdvancesPhase_WhenReachedDestination() {
    // NavState.HasArrived=1 triggers transition
}
[Fact] void MissionDirector_DoesNotAdvance_WhenConditionNotMet() { ... }
[Fact] void MissionDirector_StopsAtEndOfQueue() { ... }
```

---

### BCS-P6-T2 — HsmDamageBridgeSystem

**Goal:** Detects `CanMove` being cleared on an entity with `BrainHsm128` (or `BrainHsm64`); injects `HsmEvent(MobilityLost)` into the HSM's event queue using `HsmEventQueue.TryEnqueue`.

**File:** `Toolkits/FDP.Toolkit.Behavior/Systems/HsmDamageBridgeSystem.cs`

**Note:** Track previous capability state using a transient component `PreviousCapabilities` or check for capability removal each frame.

**Reference:** [DESIGN.md §3.2](./DESIGN.md#32-systems), design talk lines 3780–3830

**Success conditions:**
```csharp
[Fact] void HsmDamageBridge_InjectsMobilityLostEvent_WhenCanMoveCleared() {
    // Entity with BrainHsm128, CanMove initially set
    // Strip CanMove → run bridge system
    // Verify HsmEventQueue has MobilityLost event pending
}
```

---

### BCS-P6-T3 — EmbarkExecutor + EjectPassengersExecutor

**Goal:** Full interaction executor lifecycle for embark/eject operations:
- `EmbarkExecutor`: distance check → strip capabilities → add `IsEmbarkedTag` → insert into `PassengerBuffer`.
- `EjectPassengersExecutor`: iterate `PassengerBuffer` → restore capabilities → set position near vehicle → remove `IsEmbarkedTag` → clear buffer.

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/Executors/EmbarkExecutor.cs`
- `Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs`
- `Toolkits/FDP.Toolkit.Behavior/Components/InteractionComponents.cs` — `IsEmbarkedTag`, `PassengerBuffer`

**Reference:** [DESIGN.md §8.2](./DESIGN.md#82-interaction-executors), design talk lines 880–995, 4580–4685

**Success conditions:**
```csharp
[Fact] void Embark_AddsSoldierToPassengerBuffer_WhenInRange() {
    // Soldier within 3m of APC → one Embark tick → PassengerBuffer.Count==1
}
[Fact] void Embark_DoesNotEmbark_WhenDistanceTooFar() { ... }
[Fact] void Embark_StripsCanMove_Capability() { ... }
[Fact] void Eject_RestoresCanMove_AndRemovesTag() { ... }
[Fact] void Eject_ClearsPassengerBuffer() { ... }
```

---

## Phase 7 — Fdp.Examples.UrbanCombat (Demo App)

Project: `Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj`  
References: All five new toolkits, `FDP.Toolkit.CarKinem`, `FDP.Toolkit.Tkb`, `FDP.Toolkit.Lifecycle`, `ModuleHost.Core`, `Fdp.Kernel`

---

### BCS-P7-T1 — Project Scaffold + HeadlessDemoApp Shell

**Goal:** Create the .csproj with all references; create `HeadlessDemoApp.cs` that initializes `EntityRepository`, `EventAccumulator`, and `ModuleHostKernel`; registers all toolkit modules; exposes `RunSimulation(int frames)`.

**Files:**
- `Examples/Fdp.Examples.UrbanCombat/Fdp.Examples.UrbanCombat.csproj`
- `Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`
- `Examples/Fdp.Examples.UrbanCombat/Program.cs` (stub, calls `app.Initialize(); app.RunSimulation(600)`)

**Reference:** [DESIGN.md §9 + §10](./DESIGN.md#9-demo-application--fdpexamplesurlbancombat), design talk lines 3230–3300

**Success conditions:**
```csharp
[Fact] void HeadlessDemoApp_InitializesWithoutException() {
    using var app = new HeadlessDemoApp();
    app.Initialize();
    // No exception, kernel is initialized
}
```

---

### BCS-P7-T2 — TKB Blueprints (Entity Templates)

**Goal:** `DemoTkbSetup.RegisterAll(TkbDatabase)` defining all 5 entity templates with correct component sets.

**File:** `Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs`

**Reference:** [DESIGN.md §9.2](./DESIGN.md#92-tkb-blueprints)

**Success conditions:**
```csharp
[Fact] void TkbSetup_RegistersAllFiveTemplates() {
    var tkb = new TkbDatabase();
    DemoTkbSetup.RegisterAll(tkb);
    Assert.NotNull(tkb.GetByType(1001)); // CivilianPedestrian
    Assert.NotNull(tkb.GetByType(1002)); // CivilianCar
    Assert.NotNull(tkb.GetByType(2001)); // MilitaryAPC
    Assert.NotNull(tkb.GetByType(2002)); // InfantrySoldier
    Assert.NotNull(tkb.GetByType(2003)); // Insurgent
}
[Fact] void APC_Template_HasPassengerBuffer() { ... }
[Fact] void Soldier_Template_HasWeaponState() { ... }
```

---

### BCS-P7-T3 — DemoEnvironmentSetup (Road Graph)

**Goal:** `DemoEnvironmentSetup.CreateCityIntersection()` builds a `RoadNetworkBlob` with 5 nodes and 8 segments representing a 4-way intersection.

**File:** `Examples/Fdp.Examples.UrbanCombat/Setup/DemoEnvironmentSetup.cs`

**Reference:** [DESIGN.md §9.3](./DESIGN.md#93-road-graph), design talk lines 3370–3420

**Success conditions:**
```csharp
[Fact] void Environment_HasFiveNodes() {
    var blob = DemoEnvironmentSetup.CreateCityIntersection();
    Assert.Equal(5, blob.Nodes.Length);
}
[Fact] void Environment_HasEightSegments() { ... }
```

---

### BCS-P7-T4 — TrafficBrainSystem (Tier 1)

**Goal:** Per-frame hardcoded brain for `SimTier=1` entities ensuring civilians wander and cars follow road graph; switches to `Flee` when `TargetMemory.Count > 0`.

**File:** `Examples/Fdp.Examples.UrbanCombat/Systems/TrafficBrainSystem.cs`

**Reference:** [DESIGN.md §9.1](./DESIGN.md#91-scenario-urban-ambush), design talk lines 3540–3610

**Success conditions:**
```csharp
[Fact] void TrafficBrain_SetsFlee_WhenThreatDetected() {
    // Entity SimTier=1, TargetMemory.Count=1 → LocomotionChannel.ActiveAction == Flee
}
[Fact] void TrafficBrain_SetsMoveTo_WhenIdle() { ... }
```

---

### BCS-P7-T5 — Insurgent BTree Nodes + JSON

**Goal:** Author the "Ambush_BT" behavior tree JSON and implement the C# node delegates `Condition_HasTarget` and `Action_AimAndFire` (writes `WeaponChannel`).

**Files:**
- `Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs`
- `Examples/Fdp.Examples.UrbanCombat/Assets/Ambush.json`

**Reference:** [DESIGN.md §9.4](./DESIGN.md#94-brain-authoring), design talk lines 3620–3710

**Success conditions:**
```csharp
[Fact] void Ambush_BT_HoldPosition_WhenNoTarget() {
    // TargetMemory.Count=0 → Selector falls through to HoldPosition → WeaponChannel.ActiveAction==0
}
[Fact] void Ambush_BT_AimsAtTarget_WhenTargetPresent() {
    // TargetMemory.Count=1 → WeaponChannel.ActiveAction==CombatActions.AimAndFire
}
```

---

### BCS-P7-T6 — APC HSM Authoring

**Goal:** Build the "ConvoyEscort_HSM" using `Fhsm.Compiler.HsmBuilder`. Implement `Activity_Cruise` and `OnEnter_Disabled` actions. Register action methods in an `ApcHsmActionsRegistry`.

**Files:**
- `Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmSetup.cs`
- `Examples/Fdp.Examples.UrbanCombat/Brains/ApcHsmActions.cs`

**Reference:** [DESIGN.md §9.4](./DESIGN.md#94-brain-authoring), design talk lines 3715–3810

**Success conditions:**
```csharp
[Fact] void ApcHsm_Builds_WithoutException() {
    var blob = ApcHsmSetup.Build();
    Assert.NotNull(blob);
}
[Fact] void ApcHsm_InitialState_IsCruising() {
    // Initialize HsmInstance128 with Convoy blob → confirm state index == [Cruising]
}
[Fact] void ApcHsm_TransitionsToDisabled_OnMobilityLostEvent() {
    // Push MobilityLost event → run one tick → state == [Disabled]
}
```

---

### BCS-P7-T7 — ScenarioDirector (Entity Spawning)

**Goal:** `ScenarioDirector.SetupAmbushScenario()` spawns all actors at their correct positions with correct initial doctrines and component values.

**File:** `Examples/Fdp.Examples.UrbanCombat/ScenarioDirector.cs`

**Reference:** [DESIGN.md §9.1](./DESIGN.md#91-scenario-urban-ambush), design talk lines 3420–3530

**Success conditions:**
```csharp
[Fact] void ScenarioDirector_SpawnsExpectedEntityCount() {
    // After SetupAmbushScenario: query all entities with VehicleState → count == 14
    // (5 pedestrians + 3 cars + 1 APC + 4 soldiers + 1 insurgent)
}
[Fact] void ScenarioDirector_SoldiersAreEmbarked_Initially() {
    // 4 soldiers have IsEmbarkedTag after setup
}
[Fact] void ScenarioDirector_InsurgentHasRedFaction() { ... }
```

---

### BCS-P7-T8 — TelemetryReporterSystem

**Goal:** `Export`-phase system that prints structured `[FRAME NNNN] EVENT: ...` lines to `Console.Out`. Covers: gunfire, hits, doctrine changes, capability losses, flee starts.

**File:** `Examples/Fdp.Examples.UrbanCombat/Systems/TelemetryReporterSystem.cs`

**Reference:** [DESIGN.md §9.5](./DESIGN.md#95-telemetryreportersystem), design talk lines 3300–3355

**Success conditions:**
```csharp
[Fact] void Telemetry_PrintsGunfireEvent_WhenFireRequestPublished() {
    // Redirect Console.Out to StringWriter
    // Publish FireRequestEvent → run Export phase → output contains "GUNFIRE"
}
[Fact] void Telemetry_PrintsHitEvent() { ... }
```

---

### BCS-P7-T9 — End-to-End Integration Test (10-second simulation)

**Goal:** Run the full 600-frame simulation and assert that the "Urban Ambush" timeline unfolds as expected by verifying the console output log.

**File:** `Examples/Fdp.Examples.UrbanCombat/Tests/UrbanAmbushIntegrationTest.cs` (or separate test project)

**Reference:** [DESIGN.md §9.1](./DESIGN.md#91-scenario-urban-ambush), design talk lines 4025–4040 (expected output)

**Success conditions:**
```csharp
[Fact] void UrbanAmbush_SimulationProducesExpectedTimeline() {
    using var app = new HeadlessDemoApp();
    app.Initialize();
    
    var output = new StringWriter();
    Console.SetOut(output);
    
    var tkb = DemoTkbSetup.BuildDatabase();
    var road = DemoEnvironmentSetup.CreateCityIntersection();
    new ScenarioDirector(app.World, tkb, road).SetupAmbushScenario();
    
    app.RunSimulation(600);
    
    var log = output.ToString();
    
    // Key milestones must appear in order
    Assert.Contains("DOCTRINE ASSIGNED", log);              // Frame 1
    Assert.Contains("GUNFIRE", log);                        // Frame ~181
    Assert.Contains("HIT", log);                            // Frame ~182
    Assert.Contains("CAPABILITY LOST", log);                // Frame ~182
    Assert.Contains("HSM TRANSITION", log);                 // Frame ~183
    Assert.Contains("INTERACTION: EjectPassengers", log);   // Frame ~184
    Assert.Contains("FLEE", log);                           // Frame ~185+
}

[Fact] void UrbanAmbush_ApcMovesNorthward_BeforeAmbush() {
    // After frame 100: APC VehicleState.Position.Y > -90 (moved north from spawn)
}
```
