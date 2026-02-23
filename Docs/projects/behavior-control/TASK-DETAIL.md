# Behavior Control Subsystem – Task Details

**Reference design:** [DESIGN.md](./DESIGN.md)  
**Task tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## Phase 0 — Universal Spatial Primitives (must be completed first)

This phase provides the universal position/rotation/velocity vocabulary that ALL subsequent toolkits build on.  
It must be completed and green before any Phase 1+ work begins.

---

### BCS-P0-T1 — `SimTransform` / `SimVelocity` in `Fdp.Kernel`

**Goal:** Define the two universal spatial components in `Fdp.Kernel` so every downstream assembly can reference them without circular dependencies.

**File to create:**  
`Kernel/Fdp.Kernel/CoreComponents/SimComponents.cs`

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>World position (meters) and orientation. Present on every entity with a spatial location.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimTransform
    {
        public Vector3    Position; // Flat-Earth Cartesian (meters)
        public Quaternion Rotation; // World-space orientation
    }                               // 28 bytes (12 + 16)

    /// <summary>Linear and angular velocity. Present on every moving entity.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SimVelocity
    {
        public Vector3 Linear;  // m/s
        public Vector3 Angular; // rad/s (roll, pitch, yaw)
    }                           // 24 bytes (12 + 12)
}
```

**Key implementation notes:**
- Both are pure value types with named fields; no `Value` indirection.
- Position and Rotation are merged in `SimTransform` because they are almost always accessed together (rendering, FOV checks, kinematic integration).
- Linear and Angular velocity are merged in `SimVelocity` for the same reason.
- Place in the `Fdp.Kernel` namespace so they are available to every toolkit without extra `using` directives.
- Register via the kernel’s component registry in any `KernelModule` / `ComponentRegistrar` that already registers built-in components.
- No dependencies on any other toolkit.

**Reference:** [DESIGN.md §2](./DESIGN.md#2-universal-spatial-primitives), design talk lines 4804–4870

**Success conditions:**
```csharp
// Kernel/Fdp.Kernel.Tests/SimComponentTests.cs
[Fact] void SimTransform_Is28Bytes() =>
    Assert.Equal(28, Unsafe.SizeOf<SimTransform>());
[Fact] void SimVelocity_Is24Bytes() =>
    Assert.Equal(24, Unsafe.SizeOf<SimVelocity>());
[Fact] void SimComponents_AreUnmanagedValueTypes() {
    Assert.True(typeof(SimTransform).IsValueType);
    Assert.True(typeof(SimVelocity).IsValueType);
}
```

---

### BCS-P0-T2 — Refactor `VehicleState` and `CarKinematicsSystem`

**Goal:** Shrink `VehicleState` to motor/steering internals only and update `CarKinematicsSystem` to read/write `SimTransform` and `SimVelocity` via a 2D↔3D math bridge.

**Files to modify:**
- `Toolkits/FDP.Toolkit.CarKinem/Core/VehicleState.cs` — remove `Position`, `Forward`, `Pitch`, `Roll`
- `Toolkits/FDP.Toolkit.CarKinem/Systems/CarKinematicsSystem.cs` — update query + add math bridge

**Migration of `VehicleState`:**
```csharp
// REMOVE: Vector2 Position, Vector2 Forward, float Pitch, float Roll
// KEEP:   float Speed, float SteerAngle, float Accel, int CurrentLaneIndex
public struct VehicleState
{
    public float Speed;
    public float SteerAngle;
    public float Accel;
    public int   CurrentLaneIndex;
}
```

**3D→2D bridge pattern in `CarKinematicsSystem`:**
```csharp
// Input conversion
Vector2 pos2D = new Vector2(tf.Position.X, tf.Position.Y);
Vector3 fwd3D = Vector3.Transform(Vector3.UnitY, tf.Rotation); // Y-forward convention
Vector2 fwd2D = new Vector2(fwd3D.X, fwd3D.Y);

// ... existing 2D bicycle model math ...

// Output conversion
tf.Position = new Vector3(pos2D.X, pos2D.Y, tf.Position.Z);  // preserve Z elevation
float yaw = MathF.Atan2(fwd2D.Y, fwd2D.X);
tf.Rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0, 0);
vel.Linear  = new Vector3(fwd2D.X * veh.Speed, fwd2D.Y * veh.Speed, 0);
```

**Reference:** [DESIGN.md §2.2](./DESIGN.md#22-impact-on-vehiclestate), design talk lines 4870–5050

**Success conditions:**
```csharp
// FDP.Toolkit.CarKinem.Tests/VehicleStateRefactorTests.cs
[Fact] void VehicleState_DoesNotContain_PositionField() =>
    Assert.Null(typeof(VehicleState).GetField("Position"));
[Fact] void VehicleState_DoesNotContain_ForwardField() =>
    Assert.Null(typeof(VehicleState).GetField("Forward"));
[Fact] void CarKinematicsSystem_WritesSimTransform_AfterUpdate() {
    var world = TestWorldFactory.Create();
    var e = world.CreateEntity();
    world.AddComponent(e, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
    world.AddComponent(e, new SimVelocity  { Linear = Vector3.Zero });
    world.AddComponent(e, new VehicleState { Speed = 10f });
    world.AddComponent(e, new NavState     { TargetSpeed = 10f });
    world.AddComponent(e, new VehicleParams(VehicleClass.PersonalCar));
    var sys = new CarKinematicsSystem { World = world };
    sys.Update(dt: 0.016f);
    var tf = world.GetComponent<SimTransform>(e);
    Assert.NotEqual(Vector3.Zero, tf.Position); // entity moved
}
```

---

### BCS-P0-T3 — Refactor `SpatialHashSystem` to use `SimTransform`

**Goal:** Replace the `VehicleState`-based position read in `SpatialHashSystem` with `SimTransform`, making the spatial grid universal (cars, pedestrians, obstacles, and future entities).

**File to modify:** `Toolkits/FDP.Toolkit.CarKinem/Systems/SpatialHashSystem.cs`

**New query pattern:**
```csharp
// Was: query entities with VehicleState, read state.Position
// Now: query entities with SimTransform (optionally also PhysicsCollider in a later phase)
var query = World.Query().With<SimTransform>().Build();
foreach (var entity in query)
{
    var pos = World.GetComponentRO<SimTransform>(entity).Position;
    _grid.Add(entity.Index, new Vector2(pos.X, pos.Y));
}
```

**Reference:** [DESIGN.md §2.4](./DESIGN.md#24-impact-on-spatialhashsystem), design talk lines 5050–5080

**Success conditions:**
```csharp
[Fact] void SpatialHashSystem_IndexesNonVehicleEntity_WithSimTransform() {
    // Create entity with ONLY SimTransform (no VehicleState)
    // Run SpatialHashSystem
    // QueryNeighbors near that position → entity is found
}
[Fact] void SpatialHashSystem_IndexesVehicleEntity_WithSimTransform() {
    // Create entity with SimTransform + VehicleState
    // Same assertion
}
```

---

### BCS-P0-T4 — Migrate `Fdp.Examples.CarKinem`

**Goal:** Update the CarKinem example app so all entity spawn sites and systems read/write `SimTransform`/`SimVelocity` instead of `VehicleState.Position`/`VehicleState.Forward`.

**File locations:** `Examples/Fdp.Examples.CarKinem/`

**Changes required:**
- All `world.AddComponent(e, new VehicleState { Position = ..., Forward = ... })` → add a `SimTransform { Position = ..., Rotation = ... }` component separately.
- Any system or visualizer that reads `VehicleState.Position` or `VehicleState.Forward` → read `SimTransform.Position` and derive direction from `SimTransform.Rotation` instead.
- Update `VehicleVisualizer` / `CarKinemInspectorAdapter` to read `SimTransform`.

**Reference:** [DESIGN.md §2.5](./DESIGN.md#25-impact-on-example-apps), design talk lines 5080–5110

**Success conditions:**
```csharp
// Fdp.Examples.CarKinem.Tests/VehicleVisualizerTests.cs (existing file — may exist already)
[Fact] void VehicleVisualizer_ReadsSimTransform_NotVehicleStatePosition() {
    // Create entity with SimTransform but no VehicleState
    // VehicleVisualizer should still return a position without throwing
}
// Additionally: dotnet build Examples/Fdp.Examples.CarKinem produces zero errors
```

---

### BCS-P0-T5 — Migrate `Fdp.Examples.BattleRoyale`

**Goal:** Replace the local `Position` and `Velocity` structs with `SimTransform` and `SimVelocity` from `Fdp.Kernel`. Delete the redundant local files.

**Files to delete:**
- `Examples/Fdp.Examples.BattleRoyale/Components/Position.cs`
- `Examples/Fdp.Examples.BattleRoyale/Components/Velocity.cs`

**Migration pattern:**
```csharp
// Old
world.AddComponent(e, new Position { Value = startPos });
world.AddComponent(e, new Velocity { Value = vel });
// New
world.AddComponent(e, new SimTransform { Position = startPos });
world.AddComponent(e, new SimVelocity  { Linear   = vel });
```

Update all `using Fdp.Examples.BattleRoyale.Components;` references to `Fdp.Kernel` where `Position`/`Velocity` are used.

**Reference:** [DESIGN.md §2.5](./DESIGN.md#25-impact-on-example-apps), design talk lines 5110–5140

**Success conditions:**
```csharp
// dotnet build Examples/Fdp.Examples.BattleRoyale produces zero errors
// Existing BattleRoyale tests still pass:
dotnet test Examples/Fdp.Examples.BattleRoyale/
```

---

### BCS-P0-T6 — Migrate `Fdp.Examples.NetworkDemo`

**Goal:** Replace `DemoComponents.Position`, `DemoComponents.Velocity`, and `DemoPosition` with `SimTransform` and `SimVelocity`. Keep `PositionGeodetic` as a separate domain concept.

**Files to modify or delete:**
- `Examples/Fdp.Examples.NetworkDemo/Components/DemoPosition.cs` — **delete** (replace usages with `SimTransform`)
- `Examples/Fdp.Examples.NetworkDemo/Components/DemoComponents.cs` — remove `Position` and `Velocity` structs

**Note on `PositionGeodetic`:** This represents a WGS84 geographic coordinate and is intentionally NOT replaced — it remains a domain-specific component used only by the `GeographicModule`.

**Reference:** [DESIGN.md §2.5](./DESIGN.md#25-impact-on-example-apps), design talk lines 5140–5200

**Success conditions:**
```csharp
// dotnet build Examples/Fdp.Examples.NetworkDemo produces zero errors
// Existing NetworkDemo tests still pass:
dotnet test Examples/Fdp.Examples.NetworkDemo.Tests/
```

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

> ⚠️ **Phase 0 Adaptation:** `TargetMemory` stores positions as flat 2D floats (`PositionsX`, `PositionsY`). When writing to `TargetMemory` from any system that reads `SimTransform`, project down: `tf.Position.X` → `PositionsX[i]`, `tf.Position.Y` → `PositionsY[i]`. The Z elevation is not stored in target memory (all entities are on the same ground plane). `AudioStimulusEvent.Origin` is `Vector3` (from `SimTransform.Position`); extract `.XY` for distance calculations.

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

> ⚠️ **Phase 0 Adaptation:** All entity position reads in this system must use `SimTransform`, not `VehicleState.Position`. When querying the `SpatialHashGrid` (which is 2D), extract the ground-plane XY: `new Vector2(tf.Position.X, tf.Position.Y)`. The design talk (lines 303–316) shows `state.Position` — replace with `repo.GetComponentRO<SimTransform>(entity).Position.XY` throughout.

**Reference:** [DESIGN.md §4.3](./DESIGN.md#43-systems), design talk lines 303–316

**Success conditions:**
```csharp
[Fact] void AudioPerception_UpdatesTargetMemory_WhenWithinHearingRange() {
    // Spawn listener at (0,0,0) with HearingRange=100, with SimTransform component
    // Publish AudioStimulusEvent at Origin=(50,0,0) with Intensity=60
    // Run system → TargetMemory.Count == 1
}
[Fact] void AudioPerception_IgnoresEntity_OutsideHearingRange() {
    // Listener at (0,0,0), event at (200,0,0), HearingRange=100 → Count==0
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

> ⚠️ **Phase 0 Adaptation:** Several reads in the original design talk (lines 2520–2660) use `VehicleState.Position` and `VehicleState.Forward`. Replace as follows:
> - **Observer position:** `repo.GetComponentRO<SimTransform>(observer).Position` → project `.XY` for grid query.
> - **Observer forward:** derive from `SimTransform`:
>   ```csharp
>   var tf = repo.GetComponentRO<SimTransform>(observer);
>   Vector3 fwd3D = Vector3.Transform(Vector3.UnitY, tf.Rotation); // Y-forward convention
>   Vector2 forward = new Vector2(fwd3D.X, fwd3D.Y);
>   ```
> - **Target position:** `repo.GetComponentRO<SimTransform>(target).Position` → project `.XY`.
> - The `SpatialHashGrid` still operates in 2D; no changes to grid API.
> - `LosCheckRequestEvent` ray endpoints: use `SimTransform.Position` (Vector3) for start/end; `HitResolutionSystem` projects to 2D for `Intersection2D.RaycastCircle`.

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

> ⚠️ **Phase 0 Adaptation:** `MoveToParams.Destination` and `FleeParams`-derived positions are `Vector2` (XY ground plane). This matches `NavState.FinalDestination`, which is still `Vector2` inside `CarKinem`. When a Brain node or BTree action writes `MoveToParams`, it must project the 3D world target to 2D: `new Vector2(target.SimTransform.Position.X, target.SimTransform.Position.Y)`. The design talk samples at lines 1967–2000 use `Vector2` directly and remain valid as-is — no struct changes are needed; only the *source* of the coordinate changes.

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

**Frustration guard:** If `SimVelocity.Linear.Length() < 0.1f` for 120 consecutive ticks while `Vector3.Distance(SimTransform.Position, Destination) > ArrivalRadius*2`, set `Status = Failure`. Read both components via `EntityRepository` — do not read `VehicleState.Speed`.

**Reference:** [DESIGN.md §5.2](./DESIGN.md#52-executor-classes), design talk lines 2000–2050

> ⚠️ **Phase 0 Adaptation:** `OnEnter` writes `NavState.FinalDestination` (still `Vector2`). To obtain the destination from world data, project `SimTransform.Position.XY`. The design talk example at lines 2000–2050 reads `VehicleState.Position` to compute arrival distance — replace that read with `repo.GetComponentRO<SimTransform>(entity).Position.XY` and use `Vector2.Distance(pos2D, params.Destination)`.

**Success conditions:**
```csharp
[Fact] void MoveToExecutor_ReportsSuccess_WhenHasArrived() {
    // Set NavState.HasArrived=1 → Execute sets Status=Success
}
[Fact] void MoveToExecutor_ReportsFailure_OnFrustration() {
    // SimVelocity.Linear stays near-zero length for > 120 ticks → Status=Failure
}
[Fact] void MoveToExecutor_OnExit_SetsTargetSpeedToZero() { ... }
```

---

### BCS-P3-T3 — FleeExecutor

**Goal:** Throttled (30 ticks) escape vector calculation away from threat entity; stops when `Distance > SafeDistance` or threat is dead.

**File:** `Toolkits/FDP.Toolkit.Navigation/Executors/FleeExecutor.cs`

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 2050–2110) shows `awayVector = Normalize(self.Position - threat.Position)` using `VehicleState.Position` (Vector2). Replace both reads with `SimTransform`:
> ```csharp
> Vector2 myPos     = repo.GetComponentRO<SimTransform>(entity).Position.XY;
> Vector2 threatPos = repo.GetComponentRO<SimTransform>(p.Threat).Position.XY;
> Vector2 awayVector = Vector2.Normalize(myPos - threatPos);
> float distance = Vector2.Distance(myPos, threatPos);
> ```
> The rest of the executor (writing `NavState.FinalDestination`) is unchanged.

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

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 2930–3010) reads entity positions from `VehicleState.Position` (Vector2) when building the circle for `Intersection2D.RaycastCircle`. Replace with:
> ```csharp
> Vector2 center = repo.GetComponentRO<SimTransform>(candidate).Position.XY;
> var collider = repo.GetComponentRO<PhysicsCollider>(candidate);
> bool hit = Intersection2D.RaycastCircle(req.Start.XY, req.End.XY, center, collider.Radius, out float t);
> ```
> `RaycastRequest.Start` and `RaycastRequest.End` are `Vector3`; project to `.XY` for the 2D solver.

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

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 2060–2090) defines `BallisticProjectile` with fields `PreviousPosition` (Vector3) and `Velocity` (Vector3). After the Phase 0 refactor:
> - `Velocity` is **removed** — bullet movement is handled by `SimVelocity` on the bullet entity via `LinearKinematicsSystem`.
> - `PreviousPosition` (Vector3) is **kept** — the `BallisticsSystem` must record the bullet's position before `LinearKinematicsSystem` advances it, so the raycast can test the correct swept line-segment (see BCS-P5-T4 for ordering details).
>
> Correct `BallisticProjectile` definition:
> ```csharp
> public struct BallisticProjectile
> {
>     public Entity  Shooter;
>     public Vector3 PreviousPosition; // Captured by BallisticsSystem BEFORE LinearKinematicsSystem runs
>     public float   Damage;
>     public uint    SpawnTick;
> }
> ```

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

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 2094–2160) computes aim direction from `shooterState.Position` and `targetState.Position` (both `VehicleState`-derived Vector2). Replace with `SimTransform`:
> ```csharp
> Vector3 origin    = repo.GetComponentRO<SimTransform>(entity).Position;
> Vector3 targetPos = repo.GetComponentRO<SimTransform>(targetEntity).Position;
> Vector3 direction = Vector3.Normalize(targetPos - origin);
> // Populate FireRequestEvent.Origin = origin, Direction = direction
> ```
> The `FireRequestEvent` fields `Origin` (Vector3) and `Direction` (Vector3) are already defined as 3D in the design. No change to the event struct is required.

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
- `FireProcessingSystem` (`Simulation` phase): consumes `FireRequestEvent`, creates bullet entities with `SimTransform { Position=evt.Origin }`, `SimVelocity { Linear=evt.Direction * weapon.MuzzleVelocity }`, and `BallisticProjectile { Damage, SpawnTick }`. The `LinearKinematicsSystem` (Phase 0 / §2.3) handles bullet movement‬‬ — no explicit `pos += vel*dt` is needed here.  
- `BallisticsSystem` (`PostSimulation` phase): reads each bullet's `SimTransform` to push a `RaycastRequest` (line-segment from previous tick’s position to current). Despawns bullets older than 300 ticks.

**Files:**
- `Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs`
- `Toolkits/FDP.Toolkit.Combat/Systems/BallisticsSystem.cs`

> ⚠️ **Phase 0 Adaptation — system ordering for the swept-segment raycast:**
>
> The design talk (lines 2170–2240) shows `BallisticsSystem` both *moving* bullets (`pos += vel * dt`) and adding the raycast. With Phase 0, movement is delegated to `LinearKinematicsSystem`. This creates an ordering dependency:
>
> **Required execution order in `PostSimulation`:**
> 1. `BallisticsSystem` runs **first**: captures `SimTransform.Position` into `BallisticProjectile.PreviousPosition`, submits raycast request `Start=PreviousPosition, End=SimTransform.Position` (using the *current* position as the endpoint — i.e., where the bullet will be). Wait — actually `LinearKinematicsSystem` hasn't run yet. So `PreviousPosition` = last frame's position (already stored), and End = new position after movement. To accomplish this:
>    - Each frame: read `SimTransform.Position` → store into `BallisticProjectile.PreviousPosition` → submit `Start=PreviousPosition, End=SimTransform.Position + SimVelocity.Linear*dt` (predicted) OR let `LinearKinematicsSystem` run first.
>
> **Simplest correct ordering:**
> ```
> PostSimulation:
>   [UpdateBefore(BallisticsSystem)] LinearKinematicsSystem  ← moves bullet: SimTransform.Position += SimVelocity.Linear * dt
>   [UpdateAfter(LinearKinematicsSystem)] BallisticsSystem   ← raycast: Start=PreviousPosition, End=SimTransform.Position
>                                                             ← store SimTransform.Position into PreviousPosition for next frame
> ```
> On *spawn*, `FireProcessingSystem` initialises `BallisticProjectile.PreviousPosition = evt.Origin` so the first frame has a valid start point.
>
> No changes to `FireProcessingSystem` are needed beyond confirming `SimTransform`, `SimVelocity`, `BallisticProjectile` are all added to the bullet entity during spawn (see BCS-P5-T1 struct).

**Reference:** [DESIGN.md §6.4](./DESIGN.md#64-systems), design talk lines 2170–2240

**Success conditions:**
```csharp
[Fact] void FireProcessing_SpawnsBullet_OnFireRequestEvent() {
    // Publish FireRequestEvent → query BallisticProjectile → Count == 1
    // Also verify bullet entity has SimTransform, SimVelocity components
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
- `EjectPassengersExecutor`: iterate `PassengerBuffer` → restore capabilities → set `SimTransform.Position` near vehicle → remove `IsEmbarkedTag` → clear buffer.

**Files:**
- `Toolkits/FDP.Toolkit.Behavior/Executors/EmbarkExecutor.cs`
- `Toolkits/FDP.Toolkit.Behavior/Executors/EjectPassengersExecutor.cs`
- `Toolkits/FDP.Toolkit.Behavior/Components/InteractionComponents.cs` — `IsEmbarkedTag`, `PassengerBuffer`

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 880–995, 4580–4685) uses `VehicleState.Position` to compute proximity for `EmbarkExecutor` and to scatter passengers in `EjectPassengersExecutor`. Replace all position reads and writes with `SimTransform`:
> - **Distance check (Embark):** `Vector3.Distance(repo.GetComponentRO<SimTransform>(soldier).Position, repo.GetComponentRO<SimTransform>(vehicle).Position)`
> - **Eject spawn offset:** compute a slot position relative to vehicle:
>   ```csharp
>   Vector3 vehiclePos = repo.GetComponentRO<SimTransform>(vehicle).Position;
>   Vector3 slotOffset = new Vector3(i * 1.5f - 1.5f, -4f, 0f); // side of vehicle
>   ref var soldierTf = ref repo.GetComponentRW<SimTransform>(passengerId);
>   soldierTf.Position = vehiclePos + slotOffset;
>   ```
> The design talk's Z=0 assumption still holds; all actors are on the ground plane.

**Reference:** [DESIGN.md §8.2](./DESIGN.md#82-interaction-executors), design talk lines 880–995, 4580–4685

**Success conditions:**
```csharp
[Fact] void Embark_AddsSoldierToPassengerBuffer_WhenInRange() {
    // Soldier within 3m of APC (via SimTransform) → one Embark tick → PassengerBuffer.Count==1
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

> ⚠️ **Phase 0 Adaptation:** Every entity template that existed in the design talk (lines 3230–3300) used to add `VehicleState { Position=..., Forward=... }` for spatial presence. After Phase 0, **every template must add the two universal spatial components**:
> ```csharp
> t.AddComponent(new SimTransform());     // Required: all entities with a world location and orientation
> t.AddComponent(new SimVelocity());      // Required: all moving entities
> // VehicleState now only has motor data — do NOT initialise Position/Forward on it:
> t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
> ```
> See [DESIGN.md §9.2](./DESIGN.md#92-tkb-blueprints) for the updated full component list per entity type. Bullet entities are spawned at runtime by `FireProcessingSystem` and do not need TKB templates.

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

> ⚠️ **Phase 0 Adaptation:** The design talk (lines 3420–3530) sets initial positions via `VehicleState { Position = spawnPos, Forward = spawnFwd }`. After Phase 0, spawn positions and orientations are set on the universal components instead:
> ```csharp
> float yaw = MathF.Atan2(spawnFwd.Y, spawnFwd.X);
> world.AddComponent(entity, new SimTransform
> {
>     Position = new Vector3(spawnPos.X, spawnPos.Y, 0f),
>     Rotation = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f)
> });
> world.AddComponent(entity, new SimVelocity { Linear = Vector3.Zero });
> // VehicleState no longer has Position/Forward:
> world.AddComponent(entity, new VehicleState { Speed = 0 });
> ```

**Success conditions:**
```csharp
[Fact] void ScenarioDirector_SpawnsExpectedEntityCount() {
    // After SetupAmbushScenario: query all entities with SimTransform → count == 14
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
    // After frame 100: APC SimTransform.Position.Y > -90 (moved north from spawn)
    // Query: world.QueryEntities().With<SimTransform>().With<BrainHsm128>().First()
}
```
