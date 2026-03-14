# MOD1 Task Detail

**Design reference:** [`MOD1-DESIGN.md`](./MOD1-DESIGN.md)  
**Task tracker:** [`MOD1-TASK-TRACKER.md`](./MOD1-TASK-TRACKER.md)

Task IDs use the pattern `MOD1-PnTm` (Phase n, Task m).

---

## Phase 1 — CQRS Navigation Contract + Authority Bug Fixes

See [MOD1-DESIGN.md §3.1](./MOD1-DESIGN.md#phase-1--cqrs-navigation-contract--authority-bug-fixes)

---

### MOD1-P1T1 — Define `NavigationIntent` and `NavigationStatus` ECS components + DDS descriptors

**Goal:** Create the two ECS component structs in `FDP.Toolkit.Navigation` (using FDP-native types) and their matching DDS descriptors in `Bagira.BDC.SSTD`. Apply the **dual-enum pattern** (see §3.1.1a): engine-side enums live in `FDP.Toolkit.Navigation`; DDS wire enums live in `Bagira.BDC.SSTD`. Translators (P3T4) map between them.

**Files to create/modify:**

| File | Action |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationIntent.cs` | **Create** — ECS struct with `Vector2 FinalDestination` (Cartesian) and `NavigationMode` engine enum |
| `FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationStatus.cs` | **Create** — ECS struct with `NavigationResult` engine enum |
| `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationMode.cs` | **Create** — engine-side `enum NavigationMode : byte { None=0, DirectPoint, FollowRoute, JoinFormation }` |
| `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationResult.cs` | **Create** — engine-side `enum NavigationResult : byte { InProgress=0, Arrived, FailedBlocked, FailedUnreachable }` |
| `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` | Reserve two new constants in the **20–49 toolkit block** (e.g., `NavigationIntent = 24`, `NavigationStatus = 25`) |
| `Bagira.DDS.DataModel/SimDescriptors.cs` | Add DDS wire enums `ENavigationMode`, `ENavigationResult` and the two DDS descriptor partial structs |

**`NavigationIntent` ECS struct spec (in `FDP.Toolkit.Navigation`):**
```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationIntent)]   // toolkit block ID
public struct NavigationIntent
{
    public NavigationMode Mode;         // None = 0; default is inactive
    public Vector2        FinalDestination; // FDP Cartesian metres — NOT GeoPosition
    public float          TargetSpeed;      // m/s
    public float          ArrivalRadius;    // metres
    public uint           IntentId;         // monotonically incremented per new order
}
```

**`NavigationStatus` ECS struct spec (in `FDP.Toolkit.Navigation`):**
```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationStatus)]   // toolkit block ID
public struct NavigationStatus
{
    public uint             IntentId;
    public NavigationResult Result;   // InProgress = 0; default matches uninitialised
}
```

**DDS wire enums and descriptor spec (in `Bagira.DDS.DataModel/SimDescriptors.cs`):**
```csharp
// Wire enums — separate from engine enums; translators convert between them
public enum ENavigationMode : byte { NAV_NONE = 0, NAV_DIRECT_POINT = 1, NAV_FOLLOW_ROUTE = 2, NAV_JOIN_FORMATION = 3 }
public enum ENavigationResult : byte { RES_IN_PROGRESS = 0, RES_ARRIVED = 1, RES_FAILED_BLOCKED = 2, RES_FAILED_UNREACHABLE = 3 }

[DdsTopic("NavigationIntent")]
[DdsIdlFile("bdc-sst-sim-desc")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct NavigationIntent
{
    [DdsKey] public int EntityId;
    public uint           IntentId;
    public ENavigationMode Mode;
    public GeoPosition    FinalDestination;  // WGS-84 — translator converts from ECS Cartesian Vector2
    public float          TargetSpeed;
    public float          ArrivalRadius;
}

[DdsTopic("NavigationStatus")]
[DdsIdlFile("bdc-sst-sim-desc")]
[DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct NavigationStatus
{
    [DdsKey] public int          EntityId;
    public uint                   IntentId;
    public ENavigationResult      Result;
}
```

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Navigation` succeeds; `dotnet build Bagira.DDS.DataModel` succeeds; `dotnet build Bagira.SimHost` succeeds (adds project reference to FDP.Toolkit.Navigation).
2. A unit test verifies that `NavigationIntent.Mode` defaults to `NavigationMode.None` for a zero-initialised struct.
3. `NavigationMode.None` has value `0`; `NavigationResult.InProgress` has value `0`.
4. `ENavigationMode.NAV_NONE` has value `0`; `ENavigationResult.RES_IN_PROGRESS` has value `0`.
5. `FDP.Toolkit.Navigation` has **zero** references to `Bagira.*` assemblies; confirmed by `dotnet build` without circular-dependency error.

---

### MOD1-P1T2 — Refactor `MoveToExecutor` to CQRS Pattern

**Goal:** Strip all physics awareness from `MoveToExecutor`. It must become a pure observer of `NavigationStatus` written by the Muscle layer.

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs`

**Required changes:**

- **Remove:** `_stuckTicks` dictionary (if present), any `Vector2.Distance` / `SimTransform` / `SimVelocity` reads. **Do not inject** `IGeographicTransform` — coordinate conversion is not the executor's responsibility.
- **`OnEnter`:** Read `MoveToParams` from `channel.Params`. Write `NavigationIntent`:
  - Copy `MoveToParams.Destination` (`Vector2` Cartesian) directly to `intent.FinalDestination` — **no geo conversion**.
  - Increment `intent.IntentId`.
  - Set `Mode = NavigationMode.DirectPoint`, `TargetSpeed`, `ArrivalRadius`.
  - Set `channel.Status = Running`.
- **`Execute`:** Read `NavigationStatus` if present; if `status.IntentId != intent.IntentId` → return (stale).
  - `Arrived` → `channel.Status = Success`.
  - `FailedBlocked` / `FailedUnreachable` → `channel.Status = Failure`.
  - `InProgress` → keep `Running`.
- **`OnExit`:** Set `NavigationIntent.Mode = NavigationMode.None`, `TargetSpeed = 0`.

**Success conditions:**

1. `FDP.Toolkit.Navigation` builds cleanly; **no** `IGeographicTransform` or geo-conversion code in `MoveToExecutor`.
2. Unit test: `MoveToExecutor_OnEnter_WritesNavigationIntentWithIncrementedId`  
   — Arrange entity with `NavigationIntent{IntentId=5}`, call `OnEnter` with params → assert `intent.IntentId == 6`, `intent.Mode == NavigationMode.DirectPoint`, `intent.FinalDestination == params.Destination` (raw Cartesian copy).
3. Unit test: `MoveToExecutor_Execute_ReturnsSuccessWhenStatusArrived`  
   — Set `NavigationStatus{IntentId=6, Result=NavigationResult.Arrived}`, call `Execute` → assert `channel.Status == Success`.
4. Unit test: `MoveToExecutor_Execute_IgnoresStaleStatus`  
   — Set `NavigationStatus{IntentId=3}` (stale), `NavigationIntent{IntentId=6}` → call `Execute` → assert `channel.Status == Running` (unchanged).
5. Unit test: `MoveToExecutor_Execute_ReturnsFailureWhenBlocked`  
   — Set `NavigationStatus{IntentId=6, Result=NavigationResult.FailedBlocked}` → assert `channel.Status == Failure`.
6. No reference to `SimTransform`, `SimVelocity`, `GeoPosition`, `IGeographicTransform`, or distance calculations remains in `MoveToExecutor`.

---

### MOD1-P1T3 — Fix Authority Guard Bugs in Geographic Systems

**Goal:** Replace `PrimaryOwnerId == LocalNodeId` ownership checks with `WithOwned<T>()` query filters in the two `Fdp.Toolkit.Geographic` systems identified in the design talk.

**Files:**
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/CoordinateTransformSystem.cs`
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/GeodeticSmoothingSystem.cs`

**`CoordinateTransformSystem` changes:**
- Remove `.With<NetworkOwnership>()` from the outbound query.
- Remove the `if (ownership.PrimaryOwnerId != ownership.LocalNodeId) continue;` block.
- Add `.WithOwned<Position>()` (or whichever owned component is the write target) to the outbound query.

**`GeodeticSmoothingSystem` changes (ghost entities — no ownership):**
- Remove `.With<NetworkOwnership>()`.
- Replace the `if (primaryOwner == localNode) continue;` skip with `.WithoutOwned<Position>()` in the query.

**Success conditions:**

1. `dotnet build Fdp.Toolkit.Geographic` succeeds.
2. Existing unit tests in `Fdp.Toolkit.Geographic.Tests` (e.g., `GeodeticSmoothingSystemTests`) continue to pass.
3. New unit test `CoordinateTransformSystem_SkipsGhostEntities`:  
   — Create two entities: one with authority over `Position` (owned), one without (ghost). Run system. Assert that only the owned entity's `PositionGeodetic` is updated.
4. New unit test `GeodeticSmoothingSystem_ProcessesOnlyGhostEntities`:  
   — Create owned and ghost entities. Run system. Assert that only ghost entity is processed.
5. No remaining reference to `NetworkOwnership.PrimaryOwnerId` in either file.

---

### MOD1-P1T4 — Add Navigation Fulfillment Logic to `CarKinematicsSystem`

**Goal:** Transfer motion-completion authority from the Brain to the Muscle. `CarKinematicsSystem` (or a new lightweight `NavigationExecutionSystem`) becomes the authoritative writer of `NavigationStatus`.

**File(s):** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs` (generic arrival-detection logic; no Bagira dependency)

**Spec:**

At the start of each entity's kinematics update:
1. If entity has no `NavigationIntent` or `intent.Mode == NavigationMode.None` → skip (no active command).
2. If `status.IntentId != intent.IntentId` → initialise: write `NavigationStatus{IntentId = intent.IntentId, Result = NavigationResult.InProgress}`, reset frustration counter.
3. Check if current position (Cartesian `Vector2`) is within `ArrivalRadius` of `intent.FinalDestination` (also Cartesian `Vector2`) using a direct distance check — **no geo conversion needed**. If within radius → write `Result = NavigationResult.Arrived`.
4. Else if `vel.Linear.Length() < FrustrationSpeedThreshold` for `FrustrationTickLimit` consecutive ticks → write `Result = NavigationResult.FailedBlocked`.
5. Else keep `Result = NavigationResult.InProgress`.

Constants: `FrustrationSpeedThreshold = 0.2f` (m/s), `FrustrationTickLimit = 120` (ticks ≈ 2 s at 60 Hz).

**Success conditions:**

1. Integration test `NavigationExecution_WritesArrivedWhenEntityReachesTarget`:  
   — Spawn entity at origin, set `NavigationIntent{Mode=NavigationMode.DirectPoint, FinalDestination=new Vector2(100,0), ArrivalRadius=5}`. Tick simulation until entity reaches target. Assert `NavigationStatus.Result == NavigationResult.Arrived`.
2. Integration test `NavigationExecution_WritesFailedWhenEntityStuck`:  
   — Spawn entity with zero-max-speed `VehicleParams`, set a valid `NavigationIntent`. Tick for >120 frames. Assert `Result == NavigationResult.FailedBlocked`.
3. Unit test `NavigationExecution_IntentIdMismatch_ResetsOnNewCommand`:  
   — Change `intent.IntentId` mid-execution. Assert next tick resets `status.IntentId` and `Result = NavigationResult.InProgress`.
4. All existing `Bagira.SimHost.Tests` kinematics tests continue to pass.

---

## Phase 2 — Brain & Muscle Module Decomposition

See [MOD1-DESIGN.md §3.2](./MOD1-DESIGN.md#phase-2--brain--muscle-module-decomposition)

---

### MOD1-P2T1 — Create `MissionControlModule`

**Goal:** Extract doctrine ingress and mission direction into a discrete `IModule`.

**Target assembly:** `FDP.Toolkit.Behavior` (see §2.5 — generic AI; no Bagira domain knowledge)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/MissionControlModule.cs`

**Spec:**

```csharp
public sealed class MissionControlModule : IModule
{
    public string Name => "MissionControl";
    private readonly DoctrineRegistry _registry;
    public MissionControlModule(DoctrineRegistry registry) => _registry = registry;

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.AddToGroup(SystemPhase.Input,      new DoctrineIngressSystem(_registry));
        registry.AddToGroup(SystemPhase.Simulation, new MissionDirectorSystem());
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Behavior` succeeds; `dotnet build Bagira.SimHost` succeeds (adds project reference to toolkit).
2. Unit test `MissionControlModule_RegistersSystems`:  
   — Instantiate `MissionControlModule`, register into a `ModuleHostKernel`, verify `DoctrineIngressSystem` and `MissionDirectorSystem` are discoverable via kernel's system list.
3. Existing `Bagira.SimHost.Tests.SimulationLogicModuleTests` continue to pass (verify `SimulationLogicModule` delegates to the new module correctly).

---

### MOD1-P2T2 — Create `CognitiveRuntimeModule`

**Goal:** Extract BTree/HSM tick systems and channel arbitration into a discrete `IModule`.

**Target assembly:** `FDP.Toolkit.Behavior` (see §2.5)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs`

**Spec:**

```csharp
public sealed class CognitiveRuntimeModule : IModule
{
    public string Name => "CognitiveRuntime";
    private readonly DoctrineRegistry _registry;
    public CognitiveRuntimeModule(DoctrineRegistry registry) => _registry = registry;

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.AddToGroup(SystemPhase.Simulation, new ChannelArbitrationSystem());
        reg.AddToGroup(SystemPhase.Simulation, new BTreeTickSystem(_registry));
        reg.AddToGroup(SystemPhase.Simulation, new HsmTickSystem<BrainHsm128>(_registry));
        reg.AddToGroup(SystemPhase.Simulation, new HsmTickSystem<BrainHsm64>(_registry));
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Behavior` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `CognitiveRuntimeModule_RegistersAllTickSystems`:  
   — Instantiate with a populated `DoctrineRegistry`, register into a kernel, verify `BTreeTickSystem`, `HsmTickSystem<BrainHsm128>`, `HsmTickSystem<BrainHsm64>`, and `ChannelArbitrationSystem` are present in the kernel's system schedule.
3. Existing `Bagira.SimHost.Tests` behavior AI tests (`BrainBTreeSystem`, channel arbitration tests) continue to pass.

---

### MOD1-P2T3 — Create `ActionDispatchModule`

**Goal:** Extract locomotion and weapon dispatcher systems into a discrete `IModule`.

**Target assembly:** `FDP.Toolkit.Behavior` (see §2.5)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/ActionDispatchModule.cs`

**Spec:**

```csharp
public sealed class ActionDispatchModule : IModule
{
    public string Name => "ActionDispatch";
    private readonly VehicleAPI?       _vehicleApi;
    private readonly NetworkEntityMap  _entityMap;
    private readonly IGeographicTransform _geo;

    public ActionDispatchModule(NetworkEntityMap entityMap, IGeographicTransform geo, VehicleAPI? vehicleApi = null)
    {
        _entityMap  = entityMap;
        _geo        = geo;
        _vehicleApi = vehicleApi;
    }

    public void RegisterSystems(ISystemRegistry reg)
    {
        var locoDispatcher = new LocomotionDispatcherSystem();
        locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdMoveTo, new MoveToExecutor(_geo));
        locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdFollowRoute, new FollowRouteExecutor());
        locoDispatcher.RegisterExecutor(NavigationConstants.ActionIdJoinFormation,
            new JoinFormationExecutor(_vehicleApi, _entityMap));

        var weaponDispatcher = new WeaponDispatcherSystem();
        weaponDispatcher.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor());

        reg.AddToGroup(SystemPhase.Simulation, locoDispatcher);
        reg.AddToGroup(SystemPhase.Simulation, weaponDispatcher);
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Behavior` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `ActionDispatchModule_RegistersLocoAndWeaponDispatchers`.
3. Integration test: An entity given a `LocomotionChannel` action for `MoveTo` is dispatched correctly when both `MissionControlModule`, `CognitiveRuntimeModule`, and `ActionDispatchModule` are registered together.

---

### MOD1-P2T4 — Create `GroundKinematicsModule`

**Goal:** Extract all ground vehicle physics and spatial systems into a discrete `IModule`.

**Target assembly:** `FDP.Toolkit.CarKinem` (see §2.5 — generic ground-vehicle physics; road network and pool injected at Bagira wiring time)

**File:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Modules/GroundKinematicsModule.cs`

**Spec:**

```csharp
public sealed class GroundKinematicsModule : IModule
{
    public string Name => "GroundKinematics";
    private readonly RoadNetworkBlob         _roadNetwork;
    private readonly TrajectoryPoolManager    _trajectoryPool;
    private readonly FormationTemplateManager _formationTemplates;

    public GroundKinematicsModule(
        RoadNetworkBlob roadNetwork = default,
        TrajectoryPoolManager? trajectoryPool = null,
        FormationTemplateManager? formationTemplates = null)
    {
        _roadNetwork        = roadNetwork;
        _trajectoryPool     = trajectoryPool     ?? new TrajectoryPoolManager();
        _formationTemplates = formationTemplates ?? new FormationTemplateManager();
    }

    public TrajectoryPoolManager TrajectoryPool    => _trajectoryPool;
    public FormationTemplateManager FormationTemplates => _formationTemplates;

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.AddToGroup(SystemPhase.Simulation, new SpatialHashSystem());
        reg.AddToGroup(SystemPhase.Simulation, new FormationTargetSystem(_formationTemplates, _trajectoryPool));
        reg.AddToGroup(SystemPhase.Simulation, new VehicleCommandSystem());
        reg.AddToGroup(SystemPhase.Simulation, new CarKinematicsSystem(_roadNetwork, _trajectoryPool));
        reg.AddToGroup(SystemPhase.Simulation, new LinearKinematicsSystem());
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

All queries within `CarKinematicsSystem` and `LinearKinematicsSystem` must use `.WithOwned<SimTransform>()` rather than manual `NetworkOwnership` checks.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.CarKinem` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `GroundKinematicsModule_RegistersAllKinematicSystems`.
3. All existing `Bagira.SimHost.Tests` kinematics and physics tests pass.
4. `CarKinematicsSystem` contains no reference to `NetworkOwnership.PrimaryOwnerId`.

---

### MOD1-P2T5 — Refactor `SimulationLogicModule` as Delegation Facade

**Goal:** Make `SimulationLogicModule` delegate to the five new modules so existing call sites continue to work unchanged during the migration period.

**File:** `Bagira.SimHost/Modules/SimulationLogicModule.cs`

**Spec:**

Replace all `simGroup.AddSystem(...)` calls with instantiation and registration of the five sub-modules. Maintain existing constructor signature and `TrajectoryPool`, `FormationTemplates`, `RoadNetwork` public properties for backward compatibility.

**Success conditions:**

1. `dotnet build Bagira.SimHost` succeeds.
2. `dotnet test Bagira.SimHost.Tests` — all tests pass without modification.
3. `dotnet test Bagira.SimHost.Integration.Tests` — all integration tests pass.

---

## Phase 3 — Network Translator Packs + Node Bootstrapper

See [MOD1-DESIGN.md §3.3](./MOD1-DESIGN.md#phase-3--network-translator-packs--node-bootstrapper)

---

### MOD1-P3T1 — Create Domain-Specific Translator Packs

**Goal:** Extract translator construction from `SimHostApp.OnLoad` into static factory classes organized by domain.

**Files to create:**
- `Bagira.SimHost/Network/SharedTranslatorPack.cs`
- `Bagira.SimHost/Network/KinematicTranslatorPack.cs`
- `Bagira.SimHost/Network/CognitiveTranslatorPack.cs`

Each pack's `Create(...)` returns `IEnumerable<IDescriptorTranslator>`.

**`SharedTranslatorPack`** creates: `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator` (if in scope), `EntityInfoEgressTranslator`.

**`KinematicTranslatorPack`** creates: `GeoSpatialEgressTranslator`, `NavigationStatusEgressTranslator` (new — Phase 1), `NavigationIntentIngressTranslator` (new — Phase 1).

**`CognitiveTranslatorPack`** creates: `NavigationIntentEgressTranslator` (new — Phase 1), `EntityMissionEgressTranslator`, `GeoSpatialIngressTranslator`, `NavigationStatusIngressTranslator` (new — Phase 1).

**Important:** `NavigationIntentEgressTranslator`, `NavigationIntentIngressTranslator`, `NavigationStatusEgressTranslator`, and `NavigationStatusIngressTranslator` are **full implementations** in MOD1 — not stubs. See [MOD1-DESIGN.md §3.3.4](./MOD1-DESIGN.md#334--concrete-idescriptortranslator-implementations-for-navigation) for the required code structure. Each translator must read from/write to the real ECS components and the corresponding DDS topics. The full implementations are created as task P3T4 (below); P3T1 wires them into the packs.

**Success conditions:**

1. All three pack files compile.
2. Unit test: Instantiate `KinematicTranslatorPack.Create(...)` and verify it returns a non-empty `IEnumerable<IDescriptorTranslator>` without exception.
3. `SimHostApp.OnLoad` continues to compile and function (no regression on existing tests).

---

### MOD1-P3T2 — Create Domain-Specific Component Registries

**Goal:** Complement `BagiraSharedComponentRegistry` with domain-scoped registries to make each module's required components explicit and independently composable.

**Files to create:**
- `Bagira.SimHost/CognitiveComponentRegistry.cs`
- `Bagira.SimHost/KinematicComponentRegistry.cs`
- `Bagira.SimHost/CombatComponentRegistry.cs`

Each registry has a single `RegisterAll(EntityRepository world)` static method. Components already in `BagiraSharedComponentRegistry` are NOT duplicated (idempotency is safe but DRY is preferred).

**`CognitiveComponentRegistry.RegisterAll`** registers: `DoctrineState`, `LocomotionChannel`, `WeaponChannel`, `InteractionChannel`, `ActorCapabilityState`, `BrainBTreeState`, `BrainBlackboard`, `BrainHsm128`, `BrainHsm64`, `MissionPlanQueue`, `MissionAdapterState`, `NavigationIntent`.

**`KinematicComponentRegistry.RegisterAll`** registers: `VehicleState`, `VehicleParams`, `NavState`, `FormationMember`, `FormationRoster`, `FormationTarget`, `NavigationStatus`.

**`CombatComponentRegistry.RegisterAll`** registers: `Faction`, `PerceptionReceptor`, `TargetMemory`, `WeaponState`, `Health`, `HealthData`, `BallisticProjectile`, `PhysicsCollider`.

**`SimHostComponentRegistry.RegisterAll`** is updated to delegate to all four registries in order.

**Success conditions:**

1. All files compile.
2. `SimHostComponentRegistry.RegisterAll` still registers all the same components as before (verified by test that checks component availability in a fresh world).
3. Unit test: `CognitiveComponentRegistry_RegisterAll_DoesNotThrow` — registering into a fresh `EntityRepository` does not throw.
4. Unit test: `KinematicComponentRegistry_RegisterAll_DoesNotThrow`.

---

### MOD1-P3T3 — Create `NodeRole` and `NodeBootstrapper`

**Goal:** Create a role-based composition root that replaces the hard-coded initialization in `SimHostApp.OnLoad`.

**Files to create:**
- `Bagira.SimHost/NodeRole.cs`
- `Bagira.SimHost/NodeBootstrapper.cs`

**`NodeRole` enum:**

```csharp
public enum NodeRole { Brain, MuscleGround, ImageGenerator, AllInOne }
```

**`NodeBootstrapper.Bootstrap` spec:** See [MOD1-DESIGN.md §3.3.3](./MOD1-DESIGN.md#333--noderole-enum-and-nodebootstrapper). Must accept all required services via constructor or Bootstrap method parameters; must document which arguments are required for each role.

`SimHostApp.OnLoad` is refactored to instantiate a `NodeBootstrapper` and call `Bootstrap(NodeRole.AllInOne, ...)`, passing the already-created services. All module registrations and translator list building are delegated to the bootstrapper.

**Success conditions:**

1. `dotnet build Bagira.SimHost` succeeds.
2. `dotnet test Bagira.SimHost.Integration.Tests` — all tests pass (no regressions).
3. Unit test `NodeBootstrapper_AllInOne_RegistersAllModuleClasses`:  
   — Instantiate bootstrapper with mocked DDS/services, call `Bootstrap(NodeRole.AllInOne, ...)`. Assert that the kernel's module list contains `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`, `GroundKinematicsModule`, `CombatModule`.
4. Unit test `NodeBootstrapper_Brain_DoesNotRegisterKinematicModule`:  
   — Call `Bootstrap(NodeRole.Brain, ...)`. Assert `GroundKinematicsModule` is absent from the kernel.
5. Unit test `NodeBootstrapper_MuscleGround_DoesNotRegisterCognitiveModules`:  
   — Call `Bootstrap(NodeRole.MuscleGround, ...)`. Assert `MissionControlModule` and `CognitiveRuntimeModule` are absent.

---

### MOD1-P3T4 — Implement Concrete Navigation Translator Classes

**Goal:** Deliver the four fully working `IDescriptorTranslator` implementations that actually move `NavigationIntent` and `NavigationStatus` across a DDS boundary, enabling Brain and Muscle to run as separate OS processes.

**Design reference:** [MOD1-DESIGN.md §3.3.4](./MOD1-DESIGN.md#334--concrete-idescriptortranslator-implementations-for-navigation)

**Files to create:**
- `Bagira.SimHost/Network/NavigationIntentEgressTranslator.cs`
- `Bagira.SimHost/Network/NavigationIntentIngressTranslator.cs`
- `Bagira.SimHost/Network/NavigationStatusEgressTranslator.cs`
- `Bagira.SimHost/Network/NavigationStatusIngressTranslator.cs`

**Implementation requirements:**

- **Egress translators**: Query `.With<NavigationIntent>().With<NetworkId>().WithOwned<NavigationIntent>()` (egress only for owned components). Map ECS struct fields to the DDS partial struct fields. **`NavigationIntentEgressTranslator` is the only class that calls `IGeographicTransform` — it converts `NavigationIntent.FinalDestination` (Cartesian `Vector2`) to `DDS.NavigationIntent.FinalDestination` (WGS-84 `GeoPosition`), mirroring `GeoSpatialEgressTranslator`.** Also maps engine-side `NavigationMode` to DDS wire `ENavigationMode`. Call `DdsWriter.Write` for each entity.
- **Ingress translators**: Call `DdsReader.TakeAll()` in a foreach. Resolve `msg.EntityId` via `NetworkEntityMap.TryGetEntity`. Skip unknown entities (replica not yet created). Map DDS `GeoPosition` back to Cartesian `Vector2` via `IGeographicTransform`. Map `ENavigationMode` back to `NavigationMode`. Call `world.SetComponent(entity, ...)` with mapped values.
- Both egress and ingress constructors accept `DdsParticipant` and `NetworkEntityMap`; the `DdsWriter`/`DdsReader` is created in the constructor and disposed with the translator.

**Success conditions:**

1. All four files compile without warnings.
2. Unit test `NavigationIntentEgressTranslator_WritesOnce_PerOwnedEntity`:
   - Create a world with one owned entity carrying `NavigationIntent{IntentId=7, Mode=NavDirect, TargetSpeed=10}`.
   - Run egress translator `OnUpdate`. Assert `DdsWriter` captured exactly one message with matching fields.
3. Unit test `NavigationIntentIngressTranslator_SetsComponent_WhenEntityKnown`:
   - Pre-register entity 99 in a mock `NetworkEntityMap`.
   - Feed a `DDS.DM.NavigationIntent{EntityId=99, IntentId=3, Mode=NavDirect}` to the reader stub.
   - Run ingress translator `OnUpdate`. Assert the entity now has an ECS `NavigationIntent` with `IntentId=3`.
4. Unit test `NavigationIntentIngressTranslator_Ignores_UnknownEntity`:
   - Feed a message for an entity not in the `NetworkEntityMap`. `OnUpdate` must not throw.
5. `NavigationStatusEgressTranslator` and `NavigationStatusIngressTranslator` have equivalent symmetric tests.
6. `KinematicTranslatorPack.Create(...)` returns instances of the real translator classes (verified by `Assert.IsAssignableFrom<NavigationStatusEgressTranslator>`).

---

### MOD1-P3T5 — DDS Discovery Config + Entry-Point Role Selection

**Goal:** Allow the same `SimHostApp` binary to launch as a Brain, Muscle, Perception, NavigationSolver, or AllInOne node by parsing a `--role` command-line argument and loading a per-role JSON `NodeConfiguration`.

**Design reference:** [MOD1-DESIGN.md §3.3.5](./MOD1-DESIGN.md#335--cyclonedds-discovery-configuration) and [§3.3.6](./MOD1-DESIGN.md#336--entry-point--role-selection)

**Files to create / modify:**
- `Bagira.SimHost/NodeConfiguration.cs` — JSON-serialisable record with `CycloneDdsConfigPath`, `DdsDomainId`, `RoadNetworkBlobPath`, `DoctrineRegistryPath`, `EntityTemplatePath`.
- `Bagira.SimHost.Standalone/Config/dds-allinone.xml` — loopback-only config.
- `Bagira.SimHost.Standalone/Config/dds-node.xml` — multicast auto-discovery config.
- `Bagira.SimHost.Standalone/Config/default.json`, `brain.json`, `muscle.json`, `perception.json`, `navsolver.json` — role-specific parameter files.
- **Modify** `SimHostApp.OnLoad` (or `Main`): parse `--role` and `--config` args; set `CYCLONEDDS_URI` env var if not already set; call `NodeBootstrapper.Bootstrap(role, ..., nodeConfig)`.
- **Modify** `NodeBootstrapper.NodeRole` enum to include `Perception` and `NavigationSolver` (from Phase 6).

**Success conditions:**

1. `dotnet build Bagira.SimHost.Standalone` succeeds with no errors.
2. Integration test `SimHostApp_AllInOneRole_StartsAndProcessesOneTick`:
   - Launch a `SimHostApp` instance (or call `Bootstrap(NodeRole.AllInOne, ...)` in test) with `--role AllInOne`.
   - Advance one tick. Assert no exceptions; assert all existing integration tests continue to pass.
3. Unit test `NodeConfiguration_LoadFrom_ReturnsDefaults_WhenFileAbsent`:
   - Call `NodeConfiguration.LoadFrom` on a nonexistent path. Assert defaults (`DdsDomainId=42`, etc.) without exception.
4. Unit test `SimHostApp_ParsesRole_Brain`:
   - Pass `["--role", "Brain"]` to the arg parser. Assert the resolved `NodeRole == NodeRole.Brain`.
5. Unit test `SimHostApp_ParsesRole_DefaultsToAllInOne`:
   - Pass `[]` to the arg parser. Assert `NodeRole == NodeRole.AllInOne`.

> **Note — Entity lifecycle across processes: no extra task needed.** Ghost/replica entity creation on non-owner nodes is handled by the existing FDP toolkit. The owner node publishes via the project-specific Bagira BDC SST `EntityMaster` DDS topic (`EntityMasterEgressTranslator` in `Bagira.Map.Common`). Replica nodes receive it via `EntityMasterIngressTranslator` which calls `GhostCreationSystem`. Component attachment per blueprint is handled by `GhostPromotionSystem` + `ITkbDatabase`. `NodeBootstrapper` installs `ReplicationLogicModule` (from `FDP.Toolkit.Replication`) on all roles via `SharedTranslatorPack`; no custom configurer classes are required.

---

## Phase 4 — Presentation Module Split + Dynamic Perspective Switching

See [MOD1-DESIGN.md §3.4](./MOD1-DESIGN.md#phase-4--presentation-module-split--dynamic-perspective-switching)

---

### MOD1-P4T1 — Create `IgPresentationModule` and `SimPresentationModule`

**Goal:** Wrap the two existing map presentations in formal `IModule` implementations.

**Files to create:**
- `Bagira.SimHost/Modules/IgPresentationModule.cs`
- `Bagira.SimHost/Modules/SimPresentationModule.cs`

**`IgPresentationModule` spec:**  
Constructor accepts a pre-configured `MapCanvas` (using `SstVisualizerAdapter`) or configuration parameters to create one. Registers `IgMapRenderSystem` in the `PresentationSystemGroup`. The render system checks `ActivePerspective.Current == PerspectiveType.IG` before calling `canvas.Draw()`.

**`SimPresentationModule` spec:**  
Constructor accepts a pre-configured `MapCanvas` (using `SimHostVehicleVisualizer`) or parameters. Registers `SimMapRenderSystem` which checks `ActivePerspective.Current == PerspectiveType.Sim`.

Both modules expose their inner `MapCanvas` via a property for use by `PerspectiveCoordinatorSystem`.

**Success conditions:**

1. Both modules compile.
2. Unit test `IgPresentationModule_DoesNotDraw_WhenSimPerspectiveActive`:  
   — Set `ActivePerspective.Current = Sim`. Tick the IgMapRenderSystem. Verify `MapCanvas.Draw()` was not called (use a mock/spy canvas).
3. Unit test `SimPresentationModule_DrawsCalled_WhenSimPerspectiveActive`:  
   — Set `ActivePerspective.Current = Sim`. Tick SimMapRenderSystem. Verify `Draw()` was called.
4. `SimHostApp` compiles and runs after integrating both modules.

---

### MOD1-P4T2 — `ActivePerspective` Singleton + `PerspectiveCoordinatorSystem`

**Goal:** Allow dynamic switching between IG and Sim perspectives in an all-in-one application.

**Files to create:**
- `Bagira.SimHost/Components/ActivePerspective.cs`
- `Bagira.SimHost/Systems/PerspectiveCoordinatorSystem.cs`

**`ActivePerspective` spec:**

```csharp
public enum PerspectiveType : byte { IG = 0, Sim = 1 }

[StructLayout(LayoutKind.Sequential)]
[ComponentId(BagiraComponentIds.ActivePerspective)]   // BagiraComponentIds constant, e.g. 162
public struct ActivePerspective
{
    public PerspectiveType Current;
}
```

The singleton is seeded in `SimHostApp.OnLoad` (or `NodeBootstrapper`) via `world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim })`.

**`PerspectiveCoordinatorSystem` spec:**  
Runs in `PresentationSystemGroup` before both render systems. Listens for a toggle UI event (e.g., an ImGui button press captured in `SimHostVisualization.DrawUI`). On toggle:
1. Flip `ActivePerspective.Current`.
2. Retrieve cameras from both modules via `IMapCameraProvider` interface.
3. Call `incomingCamera.SnapTo(outgoingCamera)`.

**Success conditions:**

1. `dotnet build Bagira.SimHost` succeeds.
2. Unit test `PerspectiveCoordinator_Toggle_FlipsPerspective`:  
   — Seed `ActivePerspective{Current = IG}`, dispatch toggle event, tick system → assert `Current == Sim`.
3. Unit test `PerspectiveCoordinator_Toggle_SnapsCamera`:  
   — Verify `incomingCamera.SnapTo()` was called with outgoing camera's state.
4. Integration: Run `SimHostApp`, press toggle key, verify the perspective label in the ImGui panel switches.

---

## Phase 5 — Component ID Registry Split

See [MOD1-DESIGN.md §3.5](./MOD1-DESIGN.md#phase-5--component-id-registry-split)

---

### MOD1-P5T1 — Create `BagiraComponentIds` in `Bagira.Map.Definitions`

**Goal:** Extract all Bagira-specific component ID constants out of `GlobalComponentIds` (FDP kernel file) into a single project-local registry. No more editing FDP engine source to add a Bagira component.

**Design reference:** [MOD1-DESIGN.md §3.5](./MOD1-DESIGN.md#352--target-state--two-registries-only)

**Files:**
- **Create:** `Bagira.Map.Definitions/BagiraComponentIds.cs`
- **Modify:** `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` — remove constants for Bagira-owned structs (IDs that are referenced exclusively in Bagira assemblies); add a comment block documenting that IDs 160–255 live in `BagiraComponentIds`.

**`BagiraComponentIds` minimal initial content:**

```csharp
namespace Bagira.Map.Definitions
{
    /// <summary>
    /// Project-wide ECS component ID registry for all Bagira-specific components.
    /// FDP + toolkit IDs (0–159) remain in <c>Fdp.Kernel.GlobalComponentIds</c>.
    /// </summary>
    public static class BagiraComponentIds
    {
        // ── Bagira.SimHost application components (160–189) ─────────────────────────
        public const byte NavigationIntent    = 160;
        public const byte NavigationStatus    = 161;
        public const byte ActivePerspective   = 162;

        // NOTE: GroundClampingConfig, GroundClampingState, TerrainQueryBatchData live in
        // FDP.Toolkit.Geographic → their IDs are reserved in GlobalComponentIds (20–49
        // toolkit block), not here.
    }
}
```

Update every `[ComponentId(GlobalComponentIds.X)]` on a Bagira-owned struct to `[ComponentId(BagiraComponentIds.X)]`.

**Explicit migration list** — the following application-specific components must have their `[ComponentId]` attribute and corresponding `GlobalComponentIds` constant updated as part of this task:

| Component | Source file (approximate location) | Action |
|---|---|---|
| `EntityMissionHolder` | `Bagira.SimHost/Components/EntityMissionHolder.cs` | Change to `[ComponentId(BagiraComponentIds.EntityMissionHolder)]`; remove `GlobalComponentIds.EntityMissionHolder` |
| `InFormationTag` | `Bagira.SimHost/Components/InFormationTag.cs` | Change to `[ComponentId(BagiraComponentIds.InFormationTag)]`; remove `GlobalComponentIds.InFormationTag` |
| `IgEntityData` | `Bagira.IG/Components/IgEntityData.cs` | Change to `[ComponentId(BagiraComponentIds.IgEntityData)]`; remove `GlobalComponentIds.IgEntityData` |
| `IgHealthState` | `Bagira.IG/Components/IgHealthState.cs` | Change to `[ComponentId(BagiraComponentIds.IgHealthState)]`; remove `GlobalComponentIds.IgHealthState` |

After migrating: verify that `GlobalComponentIds` contains no references to the component names `EntityMissionHolder`, `InFormationTag`, `IgEntityData`, or `IgHealthState`. The FDP kernel file must be completely free of Bagira application knowledge.

> **Note:** `Faction` and `PerceptionReceptor` are **not** migrated here. Although they currently carry IDs in the 160–255 Bagira block (250, 251), they live in `FDP.Toolkit.Perception` and must use FDP toolkit IDs. That fix is performed in Phase 6 (MOD1-P6T1), which reassigns them to the 20–49 toolkit block and registers the new constants in `GlobalComponentIds`.

**Identifying any additional constants to move:** Any constant in `GlobalComponentIds` whose corresponding struct lives in `Bagira.*` rather than `FDP.*` or `ModuleHost.*` assemblies — grep for `[ComponentId(GlobalComponentIds` across all `Bagira.*` source files and verify each match against the table above.

**Success conditions:**

1. `dotnet build` of the entire solution succeeds.
2. No remaining `GlobalComponentIds` references for IDs ≥ 160 in any `Bagira.*` project.
3. Unit test `BagiraComponentIds_NoDuplicates`: collects all constants via reflection; asserts all values are unique.
4. Startup smoke test: `SimHostApp` starts without `InvalidOperationException` from `ComponentTypeRegistry` (no ID collision).
5. `dotnet build Bagira.IG` and `dotnet build Bagira.SimHost` both succeed; all existing unit tests pass.

---

## Phase 6 — Distributed Perception & Pathfinding Modules

See [MOD1-DESIGN.md §Phase 6](./MOD1-DESIGN.md#phase-6--distributed-perception--pathfinding-modules)

---

### MOD1-P6T1 — Fix Perception Component IDs + Add `SensorModality` bitmask to `TargetMemory` + per-modality receptor components

**Goal:** (a) Fix the hardcoded Bagira-block IDs on `Faction` and `PerceptionReceptor` so they use the FDP toolkit block (20–49), then (b) extend the existing `TargetMemory` struct with a `Modalities` parallel fixed array and add thin per-modality receptor structs.

**Design reference (ID fix):** [MOD1-DESIGN.md §3.6.1a](./MOD1-DESIGN.md#3611a--prerequisite-fix-hardcoded-bagira-ids-on-perception-components)

> ⚠️ **Why this matters:** `Faction` currently carries `[ComponentId(250)]` and `PerceptionReceptor` carries `[ComponentId(251)]`. IDs 160–255 are reserved for the Bagira application (see §3.5). Because these components live in `FDP.Toolkit.Perception`, they must use the FDP toolkit block (20–49), just like `VisualReceptor` and `RadarReceptor` being introduced below.

**Files to modify/create:**

| File | Action |
|------|--------|
| `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` | **(ID fix)** Add constants `Faction` and `PerceptionReceptor` in the **20–49 toolkit block** (e.g., IDs 26 and 27 if available); reserve IDs for `VisualReceptor` and `RadarReceptor` (IDs 28, 29 or next available) |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/Faction.cs` | **(ID fix)** Change `[ComponentId(250)]` → `[ComponentId(GlobalComponentIds.Faction)]` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/PerceptionReceptor.cs` | **(ID fix)** Change `[ComponentId(251)]` → `[ComponentId(GlobalComponentIds.PerceptionReceptor)]` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/TargetMemory.cs` | Add `public fixed byte Modalities[MaxTrackedTargets]` and update `AddOrUpdateTarget` to accept a `SensorModality modalityMask` parameter and OR it into the slot |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/SensorModality.cs` | New file — `[Flags] public enum SensorModality : byte { Visual = 1, Radar = 2, Thermal = 4, Acoustic = 8 }` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/VisualReceptor.cs` | New file — `[ComponentId(GlobalComponentIds.VisualReceptor)] public struct VisualReceptor { public float VisionRange; public float FovCos; }` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Components/RadarReceptor.cs` | New file — `[ComponentId(GlobalComponentIds.RadarReceptor)] public struct RadarReceptor { public float MaxRange; public float EmissionPower; public int TargetMask; }` |
| `Bagira.SimHost/SimHostComponentRegistry.cs` | Register new receptor components |

**`AddOrUpdateTarget` signature change:**
```csharp
// Before:
public static void AddOrUpdateTarget(ref TargetMemory mem, long entityId, float posX, float posY, float threatScore)

// After:
public static void AddOrUpdateTarget(ref TargetMemory mem, long entityId, float posX, float posY, float threatScore, SensorModality modality = SensorModality.Visual)
```

On update of an existing slot: `mem.Modalities[slot] |= (byte)modality;`  
On eviction/replacement: `mem.Modalities[slot] = (byte)modality;` (fresh modality for new entry).

**Success conditions:**

1. **ID fix:** `Faction` and `PerceptionReceptor` structs no longer carry hardcoded numeric literals (`250`, `251`); they reference `GlobalComponentIds.Faction` and `GlobalComponentIds.PerceptionReceptor` respectively.
2. **ID fix:** The new `Faction` and `PerceptionReceptor` constants in `GlobalComponentIds` are in the 20–49 toolkit range and do not collide with any existing constants (verified by `dotnet test` — startup `ComponentTypeRegistry` collision detection).
3. `dotnet build FDP.Toolkit.Perception` passes.
4. Existing `ThreatEvaluationSystemTests` and `VisionBroadphaseSystemTests` continue to pass (default `modality = Visual` keeps backward compatibility).
5. New unit test `TargetMemory_ModalityFusion_OrsModalities`:  
   — Add target with `Visual`, then `AddOrUpdateTarget` same entity with `Radar` → assert `Modalities[0] == Visual | Radar`.
6. New unit test `TargetMemory_Eviction_ResetsModality`:  
   — Fill all slots, add new target with `Thermal` that evicts lowest-threat → assert evicted slot's `Modalities` equals `Thermal`.

---

### MOD1-P6T2 — Add DDS Descriptors for Perception & Pathfinding

**Goal:** Define all engine-agnostic DDS descriptor types for the distributed raycast, smart-sensor, and pathfinding pipelines.

**File:** `Bagira.DDS.DataModel/SimDescriptors.cs` (existing file, extend with new types)

**New types to add:**

```csharp
// Shared coordinate helper
[DdsStruct]
public partial struct RelativeVector3 { public float East; public float North; public float Up; }

// Dumb Raycast pipeline
[DdsStruct]
public partial struct DdsRaycastRequest { public long RayId; public RelativeVector3 Start; public RelativeVector3 End; public int LayerMask; public long IgnoreEntityId; }

[DdsTopic("RaycastRequestBatch"), DdsQos(Reliable, Volatile)]
public partial struct RaycastRequestBatch { [DdsKey] public int SourceNodeId; public uint BatchCorrelationId; public GeoPosition BatchOrigin; [DdsManaged] public List<DdsRaycastRequest> Requests; }

[DdsStruct]
public partial struct DdsRaycastHit { public long RayId; public bool HasHit; public long HitEntityId; public float HitT; }

[DdsTopic("RaycastResponseBatch"), DdsQos(Reliable, Volatile)]
public partial struct RaycastResponseBatch { [DdsKey] public int TargetNodeId; public uint BatchCorrelationId; [DdsManaged] public List<DdsRaycastHit> Hits; }

// Smart Sensor pipeline
[DdsTopic("SensorConfig"), DdsQos(Reliable, TransientLocal, KeepLast=1)]
public partial struct SensorConfig { [DdsKey] public long EntityId; public float VisionRange; public float HearingRange; public float FovDegrees; }

[DdsStruct]
public partial struct DdsTrackedTarget { public long TargetEntityId; public float ThreatScore; public float Distance; public float BearingDegrees; }

[DdsTopic("SensorTargets"), DdsQos(BestEffort, Volatile)]
public partial struct SensorTargets { [DdsKey] public long ObserverEntityId; public uint Tick; [DdsManaged] public List<DdsTrackedTarget> Targets; }

// Pathfinding pipeline
[DdsStruct]
public partial struct DdsPathRequest { public long RequestId; public RelativeVector3 Start; public RelativeVector3 End; public byte MobilityProfile; }

[DdsTopic("PathRequestBatch"), DdsQos(Reliable, Volatile)]
public partial struct PathRequestBatch { [DdsKey] public int SourceNodeId; public GeoPosition BatchOrigin; [DdsManaged] public List<DdsPathRequest> Requests; }

[DdsStruct]
public partial struct DdsPathResult { public long RequestId; public bool IsReachable; public float TotalDistanceMeters; public int RouteHandle; [DdsManaged] public List<RelativeVector3> CoarseWaypoints; }

[DdsTopic("PathResponseBatch"), DdsQos(Reliable, Volatile)]
public partial struct PathResponseBatch { [DdsKey] public int TargetNodeId; [DdsManaged] public List<DdsPathResult> Results; }
```

**Success conditions:**

1. `dotnet build Bagira.DDS.DataModel` passes.
2. All new descriptor types are reachable from `Bagira.BDC.SSTD` namespace.
3. Existing `Bagira.DDS.DataModel.Tests` pass without modification.
4. A simple unit test verifies `RelativeVector3` has `East`, `North`, `Up` fields of type `float`.

---

### MOD1-P6T3 — Add `PathfindingBatchData` ECS Singleton

**Goal:** Add the zero-allocation `NativeArray`-backed singleton for pathfinding requests/results, mirroring the existing `RaycastBatchData` pattern.

**File to create:** `FDP/Toolkits/FDP.Toolkit.Navigation/PathfindingBatchData.cs`

**Spec:**

```csharp
[ComponentId(GlobalComponentIds.PathfindingBatchData)]
public struct PathfindingBatchData
{
    public const int DefaultCapacity = 64;
    public int Count;
    public NativeArray<PathRequest> Requests;
    public NativeArray<PathResult>  Results;
}

[StructLayout(LayoutKind.Sequential)]
public struct PathRequest
{
    public long     RequestId;
    public Vector3  Start;   // FDP Cartesian metres — NOT GeoPosition; translator converts when publishing
    public Vector3  End;
    public byte     MobilityProfile;  // 0=Wheeled, 1=Tracked, 2=Infantry
}

[StructLayout(LayoutKind.Sequential)]
public struct PathResult
{
    public long  RequestId;
    public bool  IsReachable;
    public float TotalDistanceMeters;
    public int   RouteHandle;
}
```

Register in `GlobalComponentIds` — **20–49 toolkit block** (e.g., next available ID after `RaycastBatchData`). `PathfindingBatchData` lives in `FDP.Toolkit.Navigation` so it uses the toolkit block, not the 160–199 application block.  
Add `world.RegisterSingleton<PathfindingBatchData>(new PathfindingBatchData { ... })` to `NodeBootstrapper` Brain and AllInOne branches (it is not Bagira-specific, so `SimHostComponentRegistry` should delegate to a `NavigationComponentRegistry` in `FDP.Toolkit.Navigation` or a thin Bagira wrapper — either is acceptable).

**Success conditions:**

1. `dotnet build` succeeds.
2. Unit test `PathfindingBatchData_Allocation_CapacityMatchesDefault`:  
   — Create and initialize the singleton with `DefaultCapacity`; assert `Requests.Length == DefaultCapacity`.
3. Singleton can be retrieved via `world.GetSingleton<PathfindingBatchData>()` without exception.
4. Existing `Bagira.SimHost.Tests` pass.

---

### MOD1-P6T4 — Delete `RequestRaycast`/`GetRaycastResult` from `BTreeContext` and create `PhysicsQueryActionNode`

> ⚠️ **Architectural fix (was: wire stubs):** The previous plan wired `RaycastBatchData` directly into `BTreeContext` in `FDP.Toolkit.Behavior`. This would couple `FDP.Toolkit.Behavior` to `FDP.Toolkit.Physics`, making `FDP.Toolkit.Behavior` a non-generic toolkit tied to specific physics singletons. The corrected approach removes the stubs entirely and moves the logic to a dedicated base class in `FDP.Toolkit.Physics`.

**Design reference:** [MOD1-DESIGN.md §3.6.6](./MOD1-DESIGN.md#366--btreecontext-cleanup--deleting-the-stubs)

**Files to modify/create:**

| File | Action |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs` | **Delete** the `RequestRaycast` and `GetRaycastResult` stub methods |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Abstractions/IAIContext.cs` | **Remove** `RequestRaycast` and `GetRaycastResult` from the interface |
| `FDP/Toolkits/FDP.Toolkit.Physics/BTreeNodes/PhysicsQueryActionNode.cs` | **Create** — abstract base class (see spec below) |
| `FDP/Toolkits/FDP.Toolkit.Physics/BTreeNodes/Action_QueryRaycast.cs` | **Create** — concrete example node subclassing `PhysicsQueryActionNode` |

**`PhysicsQueryActionNode` spec** (`FDP.Toolkit.Physics` — no reference to `FDP.Toolkit.Behavior` assembly required; `BTreeActionNode` is the common base from the toolkit):

```csharp
namespace FDP.Toolkit.Physics.BTreeNodes;

/// <summary>
/// Abstract base for BTree leaf nodes that need to submit or read synchronous raycast queries.
/// Extends the BTree node with helper methods that access RaycastBatchData directly via the world.
/// Lives in FDP.Toolkit.Physics — maintaining the one-way dependency (Physics does not reference Behavior).
/// </summary>
public abstract class PhysicsQueryActionNode : BTreeActionNode
{
    protected int RequestRaycast(EntityRepository world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        ref var batch = ref world.GetSingletonRef<RaycastBatchData>();
        if (batch.Count >= batch.Requests.Length) return -1;
        int idx = batch.Count++;
        long rayId = ((long)EntityIndex << 20) | (uint)idx;
        batch.Requests[idx] = new RaycastRequest
        {
            Start        = origin,
            End          = origin + direction * maxDistance,
            RayId        = rayId,
            IgnoreEntity = Entity
        };
        return (int)(rayId & int.MaxValue);
    }

    protected RaycastHit GetRaycastResult(EntityRepository world, int rayId)
    {
        ref readonly var batch = ref world.GetSingletonRO<RaycastBatchData>();
        for (int i = 0; i < batch.HitCount; i++)
            if (batch.Hits[i].RayId == (long)rayId) return batch.Hits[i];
        return default;
    }
}
```

**Backward compatibility:** `MockContext` tests in `FDP.Toolkit.Behavior.Tests` that previously exercised `IAIContext.RequestRaycast` stubs must be removed or migrated to test `PhysicsQueryActionNode` directly (create a minimal `EntityRepository` with `RaycastBatchData` singleton).

**Success conditions:**

1. `dotnet build FDP.Toolkit.Behavior` passes and contains **no** `RaycastBatchData` or `RaycastRequest` references.
2. `dotnet build FDP.Toolkit.Physics` passes; `PhysicsQueryActionNode` and `Action_QueryRaycast` compile.
3. `FDP.Toolkit.Behavior` has **zero** project or assembly references to `FDP.Toolkit.Physics` — confirmed by `dotnet build` or `dotnet list reference`.
4. Unit test `PhysicsQueryActionNode_RequestRaycast_WritesToBatch`:  
   — Create an `EntityRepository` with `RaycastBatchData` singleton; instantiate an `Action_QueryRaycast` node; call `RequestRaycast`; assert `batch.Count == 1` and `batch.Requests[0].RayId != -1`.
5. Unit test `PhysicsQueryActionNode_GetRaycastResult_ReturnsMatchingHit`:  
   — Pre-populate `batch.Hits[0]` with a known `RayId`; call `GetRaycastResult(rayId)` → assert `HasHit == expected`.
6. Unit test `PhysicsQueryActionNode_GetRaycastResult_ReturnsDefaultForUnresolvedId`:  
   — Call with a `rayId` not present in `Hits` → assert `default` returned.
7. Existing `FDP.Toolkit.Physics.Tests` and `Bagira.SimHost.Tests` pass.

---

### MOD1-P6T5 — Delete `RequestPath`/`GetPathResult` from `BTreeContext` and create `PathfindingActionNode`

> ⚠️ **Architectural fix (was: wire stubs):** Wiring `PathfindingBatchData` (in `FDP.Toolkit.Navigation`) into `BTreeContext` (in `FDP.Toolkit.Behavior`) would create an **uncompilable circular project dependency**: `FDP.Toolkit.Navigation` already references `FDP.Toolkit.Behavior` because `MoveToExecutor` implements `IActionExecutor<LocomotionChannel>`. The corrected approach moves the logic to a dedicated base class in `FDP.Toolkit.Navigation`.

**Design reference:** [MOD1-DESIGN.md §3.6.6](./MOD1-DESIGN.md#366--btreecontext-cleanup--deleting-the-stubs)

**Files to modify/create:**

| File | Action |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs` | **Delete** the `RequestPath` and `GetPathResult` stub methods |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Abstractions/IAIContext.cs` | **Remove** `RequestPath` and `GetPathResult` from the interface |
| `FDP/Toolkits/FDP.Toolkit.Navigation/BTreeNodes/PathfindingActionNode.cs` | **Create** — abstract base class (see spec below) |
| `FDP/Toolkits/FDP.Toolkit.Navigation/BTreeNodes/Action_PlanRoute.cs` | **Move/refactor** — the existing or placeholder `Action_PlanRoute` node must subclass `PathfindingActionNode` |

**`PathfindingActionNode` spec** (`FDP.Toolkit.Navigation` — this toolkit already references `FDP.Toolkit.Behavior` via `MoveToExecutor`; adding a BTree node base class here keeps the dependency one-way):

```csharp
namespace FDP.Toolkit.Navigation.BTreeNodes;

/// <summary>
/// Abstract base for BTree leaf nodes that need to submit or read on-demand pathfinding requests.
/// Extends the BTree node with helper methods that access PathfindingBatchData directly via the world.
/// Lives in FDP.Toolkit.Navigation — no circular dependency arises because Navigation already
/// references Behavior (via MoveToExecutor), not the other way round.
/// </summary>
public abstract class PathfindingActionNode : BTreeActionNode
{
    /// <param name="from">FDP Cartesian metres — NOT GeoPosition. Translator converts on publish.</param>
    /// <param name="to">FDP Cartesian metres — NOT GeoPosition.</param>
    protected int RequestPath(EntityRepository world, Vector3 from, Vector3 to, byte mobilityProfile = 0)
    {
        ref var batch = ref world.GetSingletonRef<PathfindingBatchData>();
        if (batch.Count >= batch.Requests.Length) return -1;
        long requestId = ((long)EntityIndex << 20) | (uint)batch.Count;
        batch.Requests[batch.Count++] = new PathRequest
        {
            RequestId        = requestId,
            Start            = from,
            End              = to,
            MobilityProfile  = mobilityProfile
        };
        return (int)(requestId & int.MaxValue);
    }

    protected PathResult GetPathResult(EntityRepository world, int requestId)
    {
        ref readonly var batch = ref world.GetSingletonRO<PathfindingBatchData>();
        for (int i = 0; i < batch.Count; i++)
            if (batch.Results[i].RequestId == (long)requestId) return batch.Results[i];
        return default;
    }
}
```

**Note — no `IGeographicTransform` in the node:** Both `from` and `to` are FDP Cartesian `Vector3`. The `PathRequestEgressTranslator` (in `Bagira.SimHost.Network`) is solely responsible for converting to relative WGS-84 ENU floats when publishing to DDS.

**Success conditions:**

1. `dotnet build FDP.Toolkit.Behavior` passes and contains **no** `PathfindingBatchData` or `PathRequest` references.
2. `dotnet build FDP.Toolkit.Navigation` passes; `PathfindingActionNode` and `Action_PlanRoute` compile.
3. `FDP.Toolkit.Behavior` has **zero** project or assembly references to `FDP.Toolkit.Navigation` — confirmed by `dotnet build` or `dotnet list reference`.
4. Unit test `PathfindingActionNode_RequestPath_WritesToBatch`:  
   — Create an `EntityRepository` with `PathfindingBatchData` singleton; instantiate an `Action_PlanRoute` node; call `RequestPath`; assert `PathfindingBatchData.Count == 1`.
5. Unit test `PathfindingActionNode_GetPathResult_ReturnsRouteHandleWhenResolved`:  
   — Pre-populate `PathfindingBatchData.Results` with `IsReachable=true, RouteHandle=42`; call `GetPathResult` → assert `RouteHandle == 42`.
6. Unit test `PathfindingActionNode_GetPathResult_ReturnsDefaultWhilePending`:  
   — No matching result in batch → assert `default` returned.
7. Existing `FDP.Toolkit.Navigation.Tests` and `Bagira.SimHost.Tests` pass.

---

### MOD1-P6T6 — Create `AutonomousPerceptionModule` and `PhysicsQueryModule`

**Goal:** Wrap the existing perception systems into formal `IModule` implementations installable independently of the Brain modules.

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs`
- `FDP/Toolkits/FDP.Toolkit.Physics/Modules/PhysicsQueryModule.cs` (wraps systems from `FDP.Toolkit.Physics`; placed in the physics toolkit, not perception)

**`AutonomousPerceptionModule` spec:**

```csharp
public sealed class AutonomousPerceptionModule : IModule
{
    public string Name => "AutonomousPerception";
    // ExecutionPolicy: SlowBackground at 10 Hz (background thread, SoD snapshot)

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.AddToGroup(SystemPhase.Simulation, new LocalGridBuilderSystem());
        reg.AddToGroup(SystemPhase.Simulation, new VisionBroadphaseSystem());
        reg.AddToGroup(SystemPhase.Simulation, new LosRequestBatchingSystem());
        reg.AddToGroup(SystemPhase.Simulation, new ThreatEvaluationSystem());
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

**`PhysicsQueryModule` spec** (in `FDP.Toolkit.Physics`):

```csharp
public sealed class PhysicsQueryModule : IModule
{
    public string Name => "PhysicsQuery";
    // ExecutionPolicy: Synchronous

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.AddToGroup(SystemPhase.Input,      new RaycastSolverSystem());
        reg.AddToGroup(SystemPhase.PostSimulation, new HitResolutionSystem());
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

Note: `RaycastSolverSystem` and `HitResolutionSystem` already exist in `FDP.Toolkit.Physics.Systems` — this task only wraps them, not re-implements them. The module lives in `FDP.Toolkit.Physics` (not `FDP.Toolkit.Perception`) because it isolates raycast physics concerns, not sensor perception logic.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Perception` passes; `dotnet build FDP/Toolkits/FDP.Toolkit.Physics` passes; `dotnet build Bagira.SimHost` passes.
2. Unit test `AutonomousPerceptionModule_RegistersAllPerceptionSystems`.
3. Unit test `PhysicsQueryModule_RegistersRaycastAndHitSystems`.
4. Existing `Bagira.SimHost.Tests` perception-related tests still pass.
5. `SimulationLogicModule` no longer directly registers `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem`, `PerceptionBroadphaseSystem`, `LosRequestBatchingSystem`, `ThreatEvaluationAdapterSystem`, `DamageSystem`, `BallisticsSystem` — these are delegated to the new modules or `CombatModule` (Phase 2).

---

### MOD1-P6T7 — Create `NavigationSolverModule`

**Goal:** Extract on-demand path computation into an `IModule` installable on dedicated NavigationSolver nodes.

**File to create:** `FDP/Toolkits/FDP.Toolkit.Navigation/Modules/NavigationSolverModule.cs`

**Spec:**

```csharp
public sealed class NavigationSolverModule : IModule
{
    public string Name => "NavigationSolver";
    private readonly RoadNetworkBlob       _roadNetwork;
    private readonly TrajectoryPoolManager _trajectoryPool;

    public NavigationSolverModule(RoadNetworkBlob roadNetwork, TrajectoryPoolManager? trajectoryPool = null)
    {
        _roadNetwork    = roadNetwork;
        _trajectoryPool = trajectoryPool ?? new TrajectoryPoolManager();
    }

    public void RegisterSystems(ISystemRegistry reg)
    {
        reg.AddToGroup(SystemPhase.Simulation, new PathfindingSolverSystem(_roadNetwork, _trajectoryPool));
    }
    public void Tick(ISimulationView view, float dt) { }
}
```

**`PathfindingSolverSystem` spec** (new system to create in `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/`):
- Reads `PathfindingBatchData.Requests[0..Count]`.
- For each request: calls `_roadNetwork.FindPath(cartesianStart, cartesianEnd)` (or returns `IsReachable=false` if `_roadNetwork` is default/empty).
- Registers the trajectory: `int handle = _trajectoryPool.RegisterTrajectory(waypoints)`.
- Writes `PathResult{RequestId, IsReachable=true, TotalDistanceMeters, RouteHandle=handle}` into `PathfindingBatchData.Results`.
- Resets `PathfindingBatchData.Count = 0` after processing.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Navigation` passes; `dotnet build Bagira.SimHost` passes.
2. Unit test `PathfindingSolverSystem_WritesRouteHandle`:  
   — Create world with `PathfindingBatchData`, load a road network with a known path, write one `PathRequest`, tick system; assert `PathfindingBatchData.Results[0].IsReachable == true` and `RouteHandle >= 0`.
3. Unit test `PathfindingSolverSystem_WritesUnreachable_WhenNoPath`:  
   — Request a path on an empty `RoadNetworkBlob`; assert `IsReachable == false`.
4. Unit test `NavigationSolverModule_RegistersPathfindingSystem`.
5. `NodeBootstrapper` `NavigationSolver` role installs `NavigationSolverModule`.

---

### MOD1-P6T8 — Create Perception & Pathfinding Translator Packs

**Goal:** Create the four translator packs that bridge the Brain and Solver roles across the DDS network for both the perception and pathfinding pipelines. Translator implementations may be stubs (log-and-discard) as long as the pack structure compiles and is wired into `NodeBootstrapper`.

**Files to create:**
- `Bagira.SimHost/Network/BrainPerceptionTranslatorPack.cs`
- `Bagira.SimHost/Network/SimPerceptionTranslatorPack.cs`
- `Bagira.SimHost/Network/BrainPathfindingTranslatorPack.cs`
- `Bagira.SimHost/Network/SimPathfindingTranslatorPack.cs`

Each follows the static factory pattern established in Phase 3 (MOD1-P3T1):

```csharp
public static class BrainPerceptionTranslatorPack
{
    public static IEnumerable<IDescriptorTranslator> Create(
        DdsParticipant dds, NetworkEntityMap entityMap, IGeographicTransform geo)
    {
        yield return new SensorConfigEgressTranslator(dds, entityMap, geo);
        yield return new RaycastBatchEgressTranslator(dds, entityMap, geo);    // Brain → Solver
        yield return new SensorTargetsIngressTranslator(dds, entityMap);       // Solver → Brain
        yield return new RaycastBatchIngressTranslator(dds, entityMap);        // Solver → Brain
    }
}
```

`NodeBootstrapper` is updated:
- `NodeRole.Brain` / `AllInOne` → `translators.AddRange(BrainPerceptionTranslatorPack.Create(...))` + `BrainPathfindingTranslatorPack`.
- `NodeRole.Perception` / `AllInOne` → `translators.AddRange(SimPerceptionTranslatorPack.Create(...))`.
- `NodeRole.NavigationSolver` / `AllInOne` → `translators.AddRange(SimPathfindingTranslatorPack.Create(...))`.

**Success conditions:**

1. All four pack files compile.
2. `NodeBootstrapper_AllInOne_RegistersAllTranslatorPacks`:  
   — Call `Bootstrap(NodeRole.AllInOne, ...)` and assert that the translator list passed to `CycloneNetworkModule` contains at least one instance from each of the four new packs (by type check on the returned `IDescriptorTranslator` list).
3. `NodeBootstrapper_Brain_DoesNotRegisterSimPerceptionPack`:  
   — Call `Bootstrap(NodeRole.Brain, ...)` and assert no `SimPerceptionTranslatorPack` translators are present.
4. Existing `Bagira.SimHost.Integration.Tests` pass without regression.
5. `NodeRole.Perception` and `NodeRole.NavigationSolver` compile and produce non-empty kernels when bootstrapped.

---

## Phase 7 — IG Ground Clamping Module

See [MOD1-DESIGN.md §3.7](./MOD1-DESIGN.md#phase-7--ig-ground-clamping-module)

---

### MOD1-P7T1 — `GroundClampingOverride` DDS Descriptor + `EClampingMode` Enum

**Goal:** Define the network contract that lets SimHost modules (flight dynamics, editor) dynamically enable/disable per-entity terrain clamping on all remote IG nodes.

**Files to create:**
- `Bagira.BDC.SSTD/GroundClampingOverride.cs` — DDS partial struct (Bagira network contract)
- `Bagira.BDC.SSTD/EClampingMode.cs` — wire-format enum `enum EClampingMode : byte { CLAMP_DEFAULT, CLAMP_FORCE_ON, CLAMP_FORCE_OFF }` (DDS wire enum, separate from engine-side enum)
- `FDP/Toolkits/FDP.Toolkit.Geographic/EClampingMode.cs` — engine-side `enum EClampingMode : byte { Default = 0, ForceOn = 1, ForceOff = 2 }` (this is what ECS components use; kept separate from the DDS wire enum per §2.5)

**Success conditions:**

1. Both files compile.
2. Unit test: Deserialise a `GroundClampingOverride` DDS message with `Mode = CLAMP_FORCE_OFF` and assert field values.
3. `DdsQos` attribute reflects `Reliable` + `TransientLocal` + `KeepLast` depth 1 (verified by attribute introspection or by reading the generated IDL).

---

### MOD1-P7T2 — ECS Components: `GroundClampingConfig`, `GroundClampingState`, `TerrainQueryBatchData`

**Goal:** Create the three ECS structs that drive the clamping pipeline on the IG side.

**Target assembly:** `FDP.Toolkit.Geographic` (see §2.5)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Geographic/Components/GroundClampingConfig.cs`
- `FDP/Toolkits/FDP.Toolkit.Geographic/Components/GroundClampingState.cs`
- `FDP/Toolkits/FDP.Toolkit.Geographic/Components/TerrainQueryBatchData.cs` (includes `TerrainQueryRequest` and `TerrainQueryResult` nested structs)

See [MOD1-DESIGN.md §3.7.3](./MOD1-DESIGN.md#373--ecs-components) for exact field definitions.

**Success conditions:**

1. All three files compile without warnings.
2. `GroundClampingConfig.IsClampingActive` property returns `true` when `Mode == CLAMP_FORCE_ON`, `true` when `Mode == CLAMP_DEFAULT` and `BaseRequiresClamping == 1`, `false` otherwise — unit tested with three parameterised cases.
3. `TerrainQueryBatchData` uses `NativeArray<T>` (unmanaged); a unit test allocates the singleton, writes 3 requests, reads them back, and disposes without exception.

---

### MOD1-P7T3 — `ITerrainProvider` Interface + `GroundClampingOverrideTranslator`

**Goal:** Define the terrain-query abstraction and the DDS ingress translator that feeds `GroundClampingConfig` from the network into the ECS world.

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Geographic/ITerrainProvider.cs`:
  ```csharp
  public interface ITerrainProvider
  {
      void QueryBatch(
          NativeArray<TerrainQueryRequest> requests, int count,
          NativeArray<TerrainQueryResult>  results);
  }
  ```
- `Bagira.IG/Network/GroundClampingOverrideTranslator.cs` — ingress-only `IDescriptorTranslator` (stays in Bagira.IG.Network; translates the DDS wire `EClampingMode` to the engine-side `FDP.Toolkit.Geographic.EClampingMode` when writing `GroundClampingConfig`):
  - `PollIngress`: `DdsReader<GroundClampingOverride>.TakeAll()` → for each sample resolve entity via `NetworkEntityMap`, call `world.SetComponent(entity, new GroundClampingConfig { Mode = sample.Mode, BaseRequiresClamping = /* from TkbIdentity lookup */ })`.
  - `ScanAndPublish`: no-op.

**Success conditions:**

1. Both files compile.
2. Unit test `GroundClampingOverrideTranslator_SetsConfig_WhenEntityKnown`: feed `GroundClampingOverride{EntityId=5, Mode=CLAMP_FORCE_OFF}` → assert ECS entity 5 has `GroundClampingConfig.Mode == CLAMP_FORCE_OFF`.
3. Unit test: unknown entity id → translator does not throw.

---

### MOD1-P7T4 — Three-Phase Execution Systems

**Goal:** Implement `TerrainQuerySubmitSystem`, `TerrainQuerySolverSystem`, `TerrainQueryResolutionSystem`, and `TerrainQueryInitializationSystem` as described in [MOD1-DESIGN.md §3.7.4](./MOD1-DESIGN.md#374--three-phase-execution-pipeline).

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Geographic/Systems/TerrainQueryInitializationSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Geographic/Systems/TerrainQuerySubmitSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Geographic/Systems/TerrainQuerySolverSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Geographic/Systems/TerrainQueryResolutionSystem.cs`

**Success conditions:**

1. All four files compile (`dotnet build FDP/Toolkits/FDP.Toolkit.Geographic`).
2. Unit test `TerrainQuerySubmitSystem_SkipsEntity_WhenClampingInactive`: entity with `Mode = CLAMP_FORCE_OFF` → batch remains empty after system tick.
3. Unit test `TerrainQueryResolutionSystem_RejectsJump_GreaterThan5m`: provide `LastValidIgAltitude = 10f`, `HitZ = 16f` → state not updated.
4. Unit test `TerrainQueryResolutionSystem_AcceptsHit_WhenWithin5m`: provide `LastValidIgAltitude = 10f`, `HitZ = 13f` → `TargetZOffset == 3f - ReferenceSimZ_delta` (exact value per formula).
5. Integration test with a `FlatEarthTerrainProvider` stub: spawn entity, tick all three systems for 3 frames, assert `GroundClampingState.TargetZOffset` converges.

---

### MOD1-P7T5 — `IgGroundClampingModule` + `TransformSyncSystem` Z-Offset Application

**Goal:** Package the four systems into a single installable `IModule`; extend the existing `TransformSyncSystem` to lerp `CurrentZOffset` and apply it to the visual Z only — never overwriting the authoritative `SimTransform`.

**Files to create / modify:**
- **Create** `Bagira.IG/Modules/IgGroundClampingModule.cs` — accepts `ITerrainProvider` (from `FDP.Toolkit.Geographic`); registers all four systems from `FDP.Toolkit.Geographic.Systems`; adds project reference to `FDP.Toolkit.Geographic`.
- **Modify** `Bagira.IG/Systems/TransformSyncSystem.cs` (or equivalent): after the horizontal dead-reckoning lerp, add the conditional Z-offset block described in [MOD1-DESIGN.md §3.7.5](./MOD1-DESIGN.md#375--visual-offset-application--transformsyncsystem-modification).
- **Modify** `Bagira.IG/NodeBootstrapper` (or IG bootstrapper): conditionally register `IgGroundClampingModule` based on `igConfig.Requires3DClamping`.

**Success conditions:**

1. `dotnet build Bagira.IG` succeeds.
2. Unit test `TransformSyncSystem_AppliesZOffset_WhenClampingStatePresent`:  
   — Entity has `GroundClampingState { TargetZOffset = 2.0f, CurrentZOffset = 0f }`. After one tick with `deltaTime = 1/60f`, assert `CurrentZOffset` is between 0 and 2 (lerped), and the output `SimTransform.Position.Z` equals `netTf.LastPosition.Z + CurrentZOffset`.
3. Unit test `TransformSyncSystem_DoesNotModifyZ_WithoutClampingState`:  
   — Entity without `GroundClampingState`; output `SimTransform.Position.Z` equals dead-reckoned value exactly.
4. Unit test `IgGroundClampingModule_RegistersSystems_WhenInstalled`:  
   — Instantiate with a mock `ITerrainProvider`; assert registry contains all four systems.
5. Integration test (2D IG path): Bootstrap without `IgGroundClampingModule`. Verify `TerrainQueryBatchData` singleton is never created and no terrain queries are issued.

---

## Phase 8 — Recording/Replay Module Architecture

---

### MOD1-P8T1 — `RecordingConfiguration` + `EcsRecordReplayController` Skeleton

**Goal:** Define the initialization contract data class and create the control-plane orchestrator that acts as a factory/`IDsmHandler`, with no direct `AsyncRecorder` ownership. Described in [MOD1-DESIGN.md §3.8.2 and §3.8.6](./MOD1-DESIGN.md#382--control-plane--ecsrecordreplaycontroller).

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Replay/RecordingConfiguration.cs` — `sealed class` with `FilePath`, `EntityFilter` (`EntityQuery?`), and `DrillId` (`Guid`) properties (code snippet in §3.8.6).
- `Bagira.SimHost/Modules/Orchestration/EcsRecordReplayController.cs` — implements `IDsmHandler`; holds `ModuleHostKernel` reference and owns the `Dictionary<Guid, StoryRecorderModule>` for concurrent story modules. Methods: `PrepareRecordingAsync`, `FinalizeRecordingAsync`, `StartStoryRecordingAsync`, `StopStoryRecordingAsync`, `PrepareReplayAsync`, `TeardownReplayAsync` (full signatures in §3.8.2). Does **not** directly call `AsyncRecorder` or `PlaybackController`.

**Success conditions:**

1. Both files compile with no warnings.
2. Unit test `EcsRecordReplayController_PrepareRecordingAsync_InstallsModule`: mock `ModuleHostKernel`; call `PrepareRecordingAsync`; assert `InstallModuleAsync` was called with a `RecordingModule`.
3. Unit test `EcsRecordReplayController_FinalizeRecordingAsync_UninstallsModule`: after `Prepare`, call `Finalize`; assert `UninstallModuleAsync` called and `_activeRecordingModule` is null.
4. Unit test `EcsRecordReplayController_StartStopStory_InstallsAndUninstalls`: call `StartStoryRecordingAsync(guid, path)` then `StopStoryRecordingAsync(guid)`; assert both kernel calls are made in order.

---

### MOD1-P8T2 — `RecordingModule` + `RecorderSystem.EntityFilter` Extension

**Goal:** Implement the data-plane recording module that strictly owns `AsyncRecorder` and registers `RecorderTickSystem`. Extend the existing `RecorderSystem` with an optional entity-filter predicate. Described in [MOD1-DESIGN.md §3.8.3 and §3.8.7](./MOD1-DESIGN.md#383--data-plane--recordingmodule).

**Files to create / modify:**
- **Create** `FDP/Toolkits/FDP.Toolkit.Replay/RecordingModule.cs` — `IModule` + `IDisposable`; constructs `AsyncRecorder` in `RegisterSystems()`; `Dispose()` calls `AsyncRecorder.Dispose()` (blocking: flushes LZ4 buffers, writes `.meta.json` with `MaxNetworkId`). Uses `RecordingConfiguration` for file path and `DrillId`.
- **Modify** `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` — add `public Predicate<int>? EntityFilter { get; set; } = null;`; in the delta-frame / keyframe inner capture loop, add `if (EntityFilter != null && !EntityFilter(entityId)) continue;`. This is an additive, non-breaking change; the default `null` preserves all existing behavior.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Replay` succeeds; `dotnet build FDP/Kernel/Fdp.Kernel` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `RecordingModule_Dispose_BlocksUntilAsyncRecorderFlushed`: create module with a temp file path; call `RegisterSystems` then `Dispose()`; assert `.fdp` file exists on disk.
3. Unit test `RecorderSystem_SkipsEntity_WhenFilterRejects`: set `EntityFilter = id => id != 42`; run one record tick with entity id 42 present; assert entity 42 does not appear in the resulting frame data.
4. Unit test `RecorderSystem_RecordsAllEntities_WhenFilterIsNull`: `EntityFilter = null`; assert entity 42 is recorded.
5. All existing `RecorderSystem` tests continue to pass (`dotnet test FDP/Kernel/Fdp.Kernel.Tests`).

---

### MOD1-P8T3 — `StoryRecorderModule` + `StoryTag` / `StoryReplayTag` Components

**Goal:** Implement the story-specific recording module (filtered, concurrent-safe) and the ECS components that mark story entities. Described in [MOD1-DESIGN.md §3.8.4 and §3.8.8](./MOD1-DESIGN.md#384--data-plane--storyrecordermodule).

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryRecorderModule.cs` — same `IModule` + `IDisposable` lifecycle as `RecordingModule`. Key difference: translates `RecordingConfiguration.EntityFilter` (`EntityQuery?`) into a `Predicate<int>` and assigns it to the `RecorderTickSystem` instance inside `RegisterSystems()`. Uses the story-specific file path from `RecordingConfiguration.FilePath`.
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryTag.cs` — `[ComponentId(GlobalComponentIds.StoryTag)] public struct StoryTag { public Guid StoryId; }` (reserve a new ID in `GlobalComponentIds` 20–49 toolkit block).
- `FDP/Toolkits/FDP.Toolkit.Replay/StoryReplayTag.cs` — `[ComponentId(GlobalComponentIds.StoryReplayTag)] public struct StoryReplayTag { public Guid StoryId; public int OriginalEntityId; }`.
- **Modify** `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs` — reserve `StoryTag` and `StoryReplayTag` IDs in the 20–49 toolkit block.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Replay` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `StoryRecorderModule_OnlyRecordsStoryEntities`: create two entities — one with `StoryTag { StoryId = A }`, one without; run a `StoryRecorderModule` with a filter for `StoryId == A`; assert only the tagged entity appears in the output frame.
3. Unit test `TwoStoryRecorderModules_RunConcurrently_ProduceIsolatedFiles`: install two story modules for different story IDs; tick the scheduler; both `.fdp` files written independently with no shared data.
4. Unit test `StoryReplayTag_IsSkipped_ByPhysicsSystem` (mock): assert that a physics system with a `QueryBuilder.Without<StoryReplayTag>()` filter does not process tagged entities.

---

### MOD1-P8T4 — `ReplayModule`

**Goal:** Implement the data-plane replay module that strictly owns `PlaybackController`, registers `PlaybackTickSystem`, and exposes `SeekToTimeAsync` for SysOp-coordinated heavy seeks. Described in [MOD1-DESIGN.md §3.8.5](./MOD1-DESIGN.md#385--data-plane--replaymodule).

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Replay/ReplayModule.cs` — `IModule` + `IDisposable`; constructs `PlaybackController` in `RegisterSystems()` (schema validation runs in ctor); registers `PlaybackTickSystem` with dual-strategy catch-up (Strategy A: sequential `StepForward`; Strategy B: `SeekToWallClockTicks` binary-search anchor + ≤59 delta frames); `Dispose()` closes `PlaybackController`; `SeekToTimeAsync(long)` delegates to `Task.Run(() => _playback.SeekToWallClockTicks(_repo, ticks))`.

**Success conditions:**

1. `dotnet build FDP/Toolkits/FDP.Toolkit.Replay` succeeds; `dotnet build Bagira.SimHost` succeeds.
2. Unit test `ReplayModule_Initialize_ThrowsInvalidDataException_OnSchemaLayoutDrift`: pass a `.fdp` file recorded with a different struct layout; assert ctor throws.
3. Unit test `ReplayModule_PlaybackTickSystem_StrategyA_SmallGap`: mock `PlaybackController`; set target wall ticks to 2 frames ahead; assert `StepForward` called twice, no `SeekToWallClockTicks`.
4. Unit test `ReplayModule_PlaybackTickSystem_StrategyB_LargeGap`: set target wall ticks to 300 frames ahead (TimeScale 5×); assert `SeekToWallClockTicks` called exactly once.
5. Unit test `ReplayModule_SeekToTimeAsync_IsOffMainThread`: call `SeekToTimeAsync`; assert it returns a running `Task` (not completed synchronously) before the seek finishes.

---

### MOD1-P8T5 — NodeBootstrapper Integration + `DrillSlave` Registration

**Goal:** Wire `EcsRecordReplayController` into the node startup sequence so `DrillSlave` delegates 2PC recording/replay commands to the controller, which in turn routes module installation through `ModuleHostKernel`. Described in [MOD1-DESIGN.md §3.8.9](./MOD1-DESIGN.md#389--nodebootstrapper-integration).

**Files to modify:**
- `Bagira.SimHost/NodeBootstrapper.cs` (or the Brain / AllInOne bootstrapper): instantiate `EcsRecordReplayController(kernel, nodeId, world)` and call `drillSlave.RegisterHandler(recordReplayController)`. Apply to roles `NodeRole.Brain` and `NodeRole.AllInOne` (recording is Brain-side; Muscle / IG nodes do not record simulation state).
- Confirm or extend `DrillSlave` (or `IDsmHandler` registration mechanism) to accept and dispatch `PrepareRecordingAsync` / `FinalizeRecordingAsync` / `PrepareReplayAsync` / `TeardownReplayAsync` commands to all registered handlers.

**Success conditions:**

1. `dotnet build Bagira.SimHost` succeeds.
2. Integration test `NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`: boot a Brain node; assert `DrillSlave` has a registered `IDsmHandler` of type `EcsRecordReplayController`.
3. Integration test `RecordingLifecycle_InstallUninstall_AddsAndRemovesRecorderTickSystem`: call `PrepareRecordingAsync` on the controller; assert `RecorderTickSystem` appears in the kernel's active system list; call `FinalizeRecordingAsync`; assert it is removed.
4. Integration test `StoryRecording_WithLiveGlobalRecorder_BothFilesProducedCorrectly`: start global recording and one story recording concurrently; tick 120 frames; finalize both; assert two distinct `.fdp` files exist and can be played back independently.

---

## Phase 9 — `FDP.Framework.Runner` — Generic Application Lifecycle Toolkit

See [MOD1-DESIGN.md §3.9](./MOD1-DESIGN.md#phase-9--fdpframeworkrunner--generic-application-lifecycle-toolkit)

---

### MOD1-P9T1 — Create `FDP.Framework.Runner` Project + Extract `ISubsystem` / `IMapCameraProvider`

**Goal:** Create the new toolkit project and move the two core contracts into it. Extend `ISubsystem` with a `TitleBarColor` property so the orchestrator can theme panels without knowing subsystem names.

**Target assembly:** `FDP.Framework.Runner` (new project at `FDP/Framework/FDP.Framework.Runner/`)

**Files to create/modify:**

| File | Action |
|------|--------|
| `FDP/Framework/FDP.Framework.Runner/FDP.Framework.Runner.csproj` | Create project; reference `Raylib-cs`, `ImGui.NET`, `ModuleHost.Core` |
| `FDP/Framework/FDP.Framework.Runner/ISubsystem.cs` | Move from `Bagira.Runner`; add `Vector4 TitleBarColor { get; }` property |
| `FDP/Framework/FDP.Framework.Runner/SubsystemConfig.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/IMapCameraProvider.cs` | Move from `Bagira.Runner` |
| `Bagira.Runner/Subsystems/SimHostSubsystem.cs` | Add `TitleBarColor` impl (Red) |
| `Bagira.Runner/Subsystems/IgSubsystem.cs` | Add `TitleBarColor` impl (Green) |
| `Bagira.Runner/Subsystems/IosSubsystem.cs` | Add `TitleBarColor` impl (Violet) |

**Success conditions:**

1. `dotnet build FDP/Framework/FDP.Framework.Runner` succeeds with zero errors.
2. `dotnet build Bagira.Runner` succeeds (project now references `FDP.Framework.Runner`).
3. `FDP.Framework.Runner` has **zero** references to `Bagira.*` assemblies.
4. Unit test `ISubsystem_TitleBarColor_IsSetOnConcretes`: instantiate `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem` (or stubs); assert each returns a distinct non-zero `Vector4` for `TitleBarColor`.

---

### MOD1-P9T2 — Refactor `SubsystemOrchestrator` into `FDP.Framework.Runner`

**Goal:** Move `SubsystemOrchestrator` to the new toolkit project and remove all three Bagira coupling points (hardcoded subsystem construction, hardcoded UI colours, hardcoded main-menu buttons).

**File:** `FDP/Framework/FDP.Framework.Runner/SubsystemOrchestrator.cs` (moved + refactored from `Bagira.Runner/`)

**Required changes:**

1. **Remove `BuildSubsystems` and `RunMode`** entirely. The constructor `SubsystemOrchestrator(IEnumerable<ISubsystem> subsystems, RunnerOptions options)` becomes the only way to instantiate. `Bagira.Runner.Program` is responsible for constructing and passing concrete subsystems.
2. **Replace `PushSubsystemColors` hardcoded switch** with a loop: `ImGui.PushStyleColor(ImGuiCol.TitleBg, subsystem.TitleBarColor)` before each `DrawUI()` call.
3. **Replace `DrawMainMenuBar` hardcoded buttons** with a loop over `_subsystems.OfType<IMapCameraProvider>()` to generate toggle buttons generically.
4. Keep `WaitingRoomCoordinator` usage and the 60 Hz Raylib loop unchanged.

**Success conditions:**

1. `dotnet build FDP/Framework/FDP.Framework.Runner` succeeds.
2. `dotnet build Bagira.Runner` succeeds; `Program.cs` now constructs concrete subsystems and injects them.
3. No remaining references to `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem`, or `RunMode` in `SubsystemOrchestrator.cs`.
4. Unit test `SubsystemOrchestrator_DrawUI_UsesSubsystemTitleBarColor`: mock subsystem with known `TitleBarColor`; verify `ImGui.PushStyleColor` called with that colour.
5. Unit test `SubsystemOrchestrator_MenuBar_ShowsToggleForMapCameraProviders`: inject two subsystems, one implementing `IMapCameraProvider`; assert main menu contains exactly one toggle entry.

---

### MOD1-P9T3 — Extract `WaitingRoomCoordinator` and `RunnerConfiguration` into `FDP.Framework.Runner`

**Goal:** Move the DDS peer-startup synchronisation and the base CLI configuration class.

**Files to create/modify:**

| File | Action |
|------|--------|
| `FDP/Framework/FDP.Framework.Runner/WaitingRoomCoordinator.cs` | Move from `Bagira.Runner`; no content changes needed (already generic) |
| `FDP/Framework/FDP.Framework.Runner/RunnerConfiguration.cs` | Move base flags (`--headless`, `--domain`, `--no-wait`, `TestScriptPath`) from `Bagira.Runner`; Bagira-specific flags remain in `Bagira.Runner/BagiraRunnerConfiguration.cs` |

**Success conditions:**

1. `dotnet build FDP/Framework/FDP.Framework.Runner` and `dotnet build Bagira.Runner` succeed.
2. Existing `WaitingRoomCoordinator` tests pass without modification.
3. `BagiraRunnerConfiguration : RunnerConfiguration` adds `--mode` and `--role` parsing in `Bagira.Runner`.
4. `WaitingRoomCoordinator` has **zero** `Bagira.*` references.

---

### MOD1-P9T4 — Extract `HeadlessTestExecutor` Core + Generic Action Handlers into `FDP.Framework.Runner`

**Goal:** Move the model types, executor, and domain-agnostic handlers to the toolkit. Keep Bagira-specific handlers in `Bagira.Runner`.

**Files to create/modify:**

| File | Action |
|------|--------|
| `FDP/Framework/FDP.Framework.Runner/Testing/TestScript.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/TestStep.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/TestReport.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/ITestActionHandler.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/HeadlessTestExecutor.cs` | Move from `Bagira.Runner`; registrar loop unchanged |
| `FDP/Framework/FDP.Framework.Runner/Testing/WaitActionHandler.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/TickActionHandler.cs` | Move from `Bagira.Runner` |
| `FDP/Framework/FDP.Framework.Runner/Testing/AssertAllActionHandler.cs` | Move from `Bagira.Runner` |
| `Bagira.Runner/Testing/SpawnActionHandler.cs` | Stays; references Bagira ECS world |
| `Bagira.Runner/Testing/MoveActionHandler.cs` | Stays; references Bagira entity types |
| `Bagira.Runner/Testing/AssertPositionActionHandler.cs` | Stays |

**Success conditions:**

1. `dotnet build FDP/Framework/FDP.Framework.Runner` and `dotnet build Bagira.Runner` succeed.
2. All existing headless test scenarios (`--headless --script`) produce the same `TestReport` output as before.
3. `HeadlessTestExecutor` has **zero** `Bagira.*` references.
4. Unit test `HeadlessTestExecutor_WaitAction_PassesAfterNTicks`: configure a `wait` step; advance N ticks; assert report step passes.
5. Integration test `BagiraRunner_RegistersSpawnHandler_BeforeExecuting`: `Bagira.Runner.Program` creates executor, calls `RegisterHandler("spawn", ...)`, runs a script that spawns an entity; assert entity exists in ECS world.

---

### MOD1-P9T5 — Refactor `Bagira.Runner` as Pure Composition Root

**Goal:** Strip all orchestration logic from `Bagira.Runner/Program.cs` and replace with constructor-injection calls into the FDP toolkit. This completes the separation.

**File:** `Bagira.Runner/Program.cs`

**Required changes:**

1. Parse `--mode` CLI argument to determine which concrete `ISubsystem` instances to create.
2. Instantiate `SimHostSubsystem(config)`, `IgSubsystem(config)`, `IosSubsystem(config)` as appropriate.
3. Pass the subsystem list into `new SubsystemOrchestrator(subsystems, options)`.
4. If `--headless` is set: create `HeadlessTestExecutor(orchestrator)`, call `RegisterHandler` for each Bagira-specific action, then call `executor.Run(TestScript.LoadFrom(...))`.
5. Otherwise: call `orchestrator.Initialize(); orchestrator.Run();`.

**Success conditions:**

1. `dotnet build Bagira.Runner` and `dotnet build Bagira.IOS.Standalone` succeed.
2. End-to-end smoke test: launch `Bagira.Runner --mode simhost --headless --script default_smoke.json`; assert `TestReport.AllPassed == true`.
3. End-to-end smoke test: launch `Bagira.Runner --mode ig`; assert window opens and first frame renders without exception.
4. `Bagira.Runner.Program` has **no** direct references to `Raylib.*` or `ImGui.*` (those belong to `SubsystemOrchestrator`).
5. `dotnet test Bagira.Runner.Tests` — all existing runner tests pass.
