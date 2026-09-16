# Stride Mock — Design Document

## 1. Overview

This workstream introduces **Stride 3D engine integration** into the FDP/HROT cluster as a unified SimHost + IG node. The integration runs outside `ClusterRunner` as a standalone process (eventually as a Stride `Game`) but fully participates in the distributed simulation cluster.

Because the real Stride engine is heavy, we first build a **proof-of-concept mock**: a Raylib/ImGui app (`FakeStrideApp`) plus a `ClusterRunner` wrapper subsystem (`StrideMockSubsystem`) that use identical core logic. All architecture patterns are designed so the mock shell can be swapped for a real Stride shell with zero changes to the cluster-integration code.

### What We Are Building

| Component | Type | Location |
|-----------|------|----------|
| `SharedApplicationBootstrapper` | Abstract class | `Hrot\Engine\Hrot.Common\Infrastructure\` |
| `Hrot.StrideMock` project | Class library | `Hrot\Subsystems\Hrot.StrideMock\` |
| `StrideNodeBootstrapper` | Concrete bootstrapper | `Hrot.StrideMock` |
| `SyncFdpToStrideScript` | Sync engine | `Hrot.StrideMock` |
| `StrideMockSubsystem` | ClusterRunner adapter | `Hrot.StrideMock` |
| `Hrot.FakeStrideApp` project | Executable | `Hrot\Runner\Hrot.FakeStrideApp\` |
| `FakeStrideApp` | Standalone shell | `Hrot.FakeStrideApp` |

### Future (out of scope for this workstream)

| Component | Type | Location |
|-----------|------|----------|
| `Hrot.StrideApp` project | Executable | `Hrot\Runner\Hrot.StrideApp\` |

---

## 2. Goals & Non-Goals

### Goals
- Prove the Stride integration architecture without the real 3D engine overhead.
- `StrideMockSubsystem` is a **drop-in replacement for SimHostSubsystem**: claims `MuscleGround | Perception | NavigationSolver | ImageGenerator` and correctly handles all cluster operations.
- Strict code-sharing (DRY): both `FakeStrideApp` and `StrideMockSubsystem` use the same `StrideNodeBootstrapper` core.
- Full cluster orchestration compliance: recording, replay, file management, 2PC, diagnostics.
- Refactor `SimHostApp` and (where applicable) `IgApplication` to share the new `SharedApplicationBootstrapper`.

### Non-Goals
- Real Stride 3D rendering (deferred to `Hrot.StrideApp`).
- Replacing CGF/Brain tier responsibilities.
- Navmesh pathfinding or Stride 3D raycasting (deferred — plain `GroundKinematicsModule` / `CognitiveSpatialModule` are used in Stage 1).

---

## 3. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Hrot.Common.Infrastructure                                     │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  SharedApplicationBootstrapper (abstract)                │   │
│  │  Phase 1: HrotNodeBuilder.WithReplication(role).Build()  │   │
│  │  Phase 2: RegisterDomainComponents()  ← plugin hook      │   │
│  │  Phase 3: BuildSerializer()           ← plugin hook      │   │
│  │  Phase 4: PopulateSystems()           ← plugin hook      │   │
│  │           → TogglableSimulationGroup                     │   │
│  │  Phase 5: BuildOrchestration()        ← plugin hook      │   │
│  │  Phase 6: RegisterSpawningPipeline()  ← plugin hook      │   │
│  │           RegisterNetworkTranslators()← plugin hook      │   │
│  │  Phase 7: Kernel.Initialize()                            │   │
│  └──────────────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────────────┘
                         │ inherits
         ┌───────────────┴──────────────────────┐
         │         Hrot.StrideMock              │
         │  ┌───────────────────────────────┐   │
         │  │   StrideNodeBootstrapper      │   │
         │  │   (injects Kinematics,        │   │
         │  │    Perception, Combat mods)   │   │
         │  ├───────────────────────────────┤   │
         │  │   SyncFdpToStrideScript       │   │
         │  │   (2-pass differential sync,  │   │
         │  │    visual effects)            │   │
         │  ├───────────────────────────────┤   │
         │  │   StrideMockSubsystem         │   │
         │  │   ISubsystem + IMapCamera-    │   │
         │  │   Provider                   │   │
         │  └───────────────────────────────┘   │
         └────────────┬─────────────────────────┘
                      │ references
         ┌────────────┴───────────────────┐
         │   Hrot.FakeStrideApp           │
         │   (FdpApplication executable)  │
         └────────────────────────────────┘
```

**Also refactored (DRY)**:
- `SimHostApp` migrated to use `SharedApplicationBootstrapper`
- `IgApplication` migrated to use `SharedApplicationBootstrapper` (IG hooks)

---

## 4. SharedApplicationBootstrapper (Hrot.Common.Infrastructure)

### 4.1 Motivation

`SimHostApp`, `IgApplication`, and `StrideNodeBootstrapper` all share a >150 line initialization block that:
- Creates the `HrotNodeContext` via `HrotNodeBuilder` chaining `.WithReplication(role)` before `.Build()` (required — `HrotNodeBuilder.Build()` alone does not construct `NedReplicationModule`; without `.WithReplication(role)` the builder returns `HrotNodeBuilderWithReplication` which must call `.Build()`, and skipping it leaves `context.NedReplication` permanently null so Phase 6a+ is silently skipped)
- Registers ECS components and events
- Calls `BuildSerializer()` (abstract hook — `HrotScenarioSerializerFactory` lives in `Hrot.SimHost`, above `Hrot.Common` in the dependency hierarchy; the base class cannot reference it directly without creating a circular dependency; the concrete subclass executes the factory)
- Wraps systems in `TogglableSimulationGroup` for replay safety
- Calls `BuildOrchestration()` (abstract hook — `NodeBootstrapper` likewise lives in `Hrot.SimHost`; the concrete subclass executes `NodeBootstrapper.BuildOrchestration()` passing `lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup` to prevent `GhostDestructionSystem` from corrupting replay state)
- Calls `RegisterSpawningPipeline()` (abstract hook — the genesis pipeline varies by node role: CGF requires `CreateEntityRequestSystem` + `CompositeEntityCreationRequestSource`; SimHost and StrideMock both require `NetworkSpawningSystem` + `GenesisMaterializationSystem` — `StrideMock` claims `MuscleGround` and must establish local authority masks and unpack transient Initial Intent DTOs (`InitialRouteIntent`, `InitialTargetsIntent`, `InitialVehicleIntent`) during scenario load, exactly as SimHost does; IG uses `GhostPromotionSystem` + `OwnershipIngressSystem` only and does not use `NetworkSpawningSystem`)
- Registers `NedReplicationModule` (base-class responsibility — not a hook; the module wiring is identical across all node roles)
- Registers domain-specific DDS network translators (plugin hook)
- Wires time-synchronization translators (`TimeNetworkModule`) and calls `CreateTimeControlGateway()` on the **configured** `nodeFactory` returned by `networkFactory.ConfigureForNode(context...)` in Phase 6 — not the raw input `networkFactory`, which is an unbound shell whose event bus is disconnected from the kernel's internal bus; UI clicks dispatched through the raw factory are silently discarded (base-class responsibility — not a hook)
- Calls `Kernel.Initialize()`

**Time translators are not domain-specific.** `NedNetworkFactory`'s domain methods (`CreateSimHostAuxiliaryTranslators`, etc.) do not include the time synchronization bridges. `TimeNetworkModule.CreateDescriptorTranslator` (pause/resume), `CreateSlaveLockstepTranslator` (deterministic stepping), and `CreateSlaveTimeSyncTranslator` (NTP handshake) must be wired by the **base class unconditionally** during Phase 6. Without them, the `SlaveSyncController` never receives clock-advance commands from the Orchestrator and the simulation stalls permanently at `0.0s`.

The **5 fragile traps** in this sequence (documented in the design talk) must be preserved exactly. Extracting them into a Template Method abstract class locks the order permanently.

### 4.2 The 5 Fragile Init Traps

| # | Trap | Rule |
|---|------|------|
| 1 | Component vs Serializer | All components registered **before** `HrotScenarioSerializerFactory.Build()` |
| 2 | Orchestration Handler Chain | `ReferenceReplayLoadHandler` must be **first** in the chain |
| 3 | Spawning vs Network Ingress | `SpawningModule` + ELM registered **before** `CycloneNetworkModule` |
| 4 | TogglableGroup / Replay Safety | Groups built **before** `BuildOrchestration()` |
| 5 | Event Bus Playback | All events registered **before** `Kernel.Initialize()` |

### 4.3 API Contract

```csharp
// Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs
namespace Hrot.Common.Infrastructure;

public abstract class SharedApplicationBootstrapper
{
    // Entry point — strict 7-phase pipeline (non-overridable)
    public HrotNodeContext BootstrapNode(
        HrotNodeConfig config,
        NodeRole role,
        INetworkFactory networkFactory);

    // Produced after BootstrapNode() — available to all concrete subclasses
    public ITimeControlGateway? TimeControl { get; private set; }

    // ── Plugin Hooks — all must be implemented ────────────────────

    // Phase 2: Register ECS components and domain events
    protected abstract void RegisterDomainComponents(EntityRepository world);

    // Phase 3: Build the scenario serializer — abstract hook to avoid circular
    // dependency. HrotScenarioSerializerFactory lives in Hrot.SimHost (above
    // Hrot.Common); referencing it from the base class would create an illegal
    // dependency cycle. The concrete subclass (StrideNodeBootstrapper, SimHostApp,
    // IgApplication) executes the factory using the fully registered component world.
    protected abstract ScenarioSerializer BuildSerializer(BehaviorRegistry? registry);

    // Phase 4a: Populate input/sim/postSim system lists for TogglableGroups
    // (systems that MUST be suspended during replay)
    protected abstract void PopulateSystems(
        HrotNodeContext context,
        List<IEcsModuleSystem> input,
        List<IEcsModuleSystem> sim,
        List<IEcsModuleSystem> postSim);

    // Phase 4b: Supply intact IEcsModule instances whose internal phases
    // must NOT be flattened into TogglableGroups (e.g. IG presentation modules).
    // Registered after the TogglableGroups, before network translators.
    protected virtual IEnumerable<IEcsModule> GetAdditionalModules() => Array.Empty<IEcsModule>();

    // Phase 5: Build orchestration handlers — abstract hook for the same reason as
    // BuildSerializer. NodeBootstrapper lives in Hrot.SimHost. The concrete subclass
    // calls NodeBootstrapper.BuildOrchestration(...) and must pass
    // lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup to prevent
    // GhostDestructionSystem from fighting the flight recorder during PrepareReplay.
    protected abstract ClusterSlave BuildOrchestration(
        HrotNodeContext context,
        TogglableSimulationGroup simGroup,
        TogglablePostSimulationGroup postSimGroup,
        ScenarioSerializer serializer);

    // Phase 6a: Register spawn pipeline (before DDS translators)
    protected abstract void RegisterSpawningPipeline(HrotNodeContext context);

    // Phase 6a+: Register NedReplicationModule — base class, NOT a hook.
    // Called immediately after RegisterSpawningPipeline:
    //   if (context.NedReplication != null)
    //       context.Kernel.RegisterModule(context.NedReplication);
    // PREREQUISITE: context.NedReplication is non-null ONLY when HrotNodeBuilder was
    // chained with .WithReplication(role) in Phase 1. Omitting .WithReplication()
    // silently skips this entire block — no exception, but GhostCreationSystem,
    // DeadReckoningSyncSystem, and all egress systems are absent from the kernel.
    // This activates GhostCreationSystem, DeadReckoningSyncSystem (role-injected),
    // and the ownership egress systems. Must precede domain translator registration
    // so ghost promotion is ready before any EntityMaster DDS packets arrive.
    // Subclasses must NOT call this — double-registration corrupts the system schedule.

    // Phase 6b: Register DDS network translators (domain-specific — hook)
    protected abstract void RegisterNetworkTranslators(
        HrotNodeContext context,
        INetworkFactory configuredFactory);

    // Phase 6c: Wire time-sync translators — base class, NOT a hook.
    // Called unconditionally after 6b. Registers:
    //   TimeNetworkModule.CreateDescriptorTranslator     (SwitchTimeModeEvent)
    //   TimeNetworkModule.CreateSlaveLockstepTranslator  (FrameOrderDescriptor / ACK)
    //   TimeNetworkModule.CreateSlaveTimeSyncTranslator  (NTP handshake)
    // These are wrapped in CycloneNetworkIngressSystem + CycloneEgressSystem.
    // Also calls nodeFactory.CreateTimeControlGateway() on the CONFIGURED factory
    // returned by networkFactory.ConfigureForNode(context...) — NOT the raw input
    // networkFactory, which is an unbound shell whose event bus is disconnected from
    // the kernel. UI clicks through the raw factory are silently discarded.
    // The result is stored in TimeControl. This is the ONLY place TimeControl is set.
    // Subclasses must NOT register these manually — doing so causes double-handling.

    // Optional overrides
    protected virtual BehaviorRegistry? GetBehaviorRegistry() => null;
}
```

---

## 5. StrideNodeBootstrapper (Hrot.StrideMock)

### 5.1 Purpose

Concrete `SharedApplicationBootstrapper` that wires the full SimHost-equivalent module set for the Stride node. Accepts injected `IEcsModule` instances so Stage 2 can swap in Stride-native kinematics/perception without touching orchestration code.

### 5.2 API Contract

```csharp
public sealed class StrideNodeBootstrapper : SharedApplicationBootstrapper
{
    // Stage 1: use default FDP simple implementations
    public StrideNodeBootstrapper(
        IEcsModule kinematicsModule,      // e.g. GroundKinematicsModule
        IEcsModule perceptionModule,      // e.g. CognitiveSpatialModule
        IEcsModule combatModule,          // e.g. CombatModule
        IEcsModule? navigationModule = null) // e.g. NavigationSolverModule

    // Produced after BootstrapNode():
    public HrotNodeContext Context { get; private set; }
    public DebugPrimitiveBuffer ProducerBuffer { get; }   // local systems write here
    public DebugPrimitiveBuffer ConsumerBuffer { get; }   // renderer reads here
    public MapCamera Camera { get; }
    // ITimeControlGateway? TimeControl — inherited from SharedApplicationBootstrapper

    public void Tick(float dt);
    public void Dispose();
}
```

### 5.3 Node Role

```
NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.ImageGenerator
```

### 5.4 Module Registration (Stage 1)

Default (FDP simple) implementations injected:
- `GroundKinematicsModule(roadNetwork, trajectoryPool)` — vehicle kinematics
- `CognitiveSpatialModule(world)` — LOS, spatial hash, EQS
- `CombatModule()` — hit resolution, damage
- `NavigationSolverModule(roadNetwork, trajectoryPool)` — A\*/Dijkstra

**Do NOT manually register `DeadReckoningSyncSystem`.** When `NedReplicationModule` initializes it inspects the node role. Because our role includes `NodeRole.ImageGenerator`, it automatically registers `DeadReckoningSyncSystem` configured with `driveFromNetwork: false` (ghost-only interpolation, no authority override). Manually registering it again causes double-tick interpolation corruption every frame.

### 5.5 Dual-Buffer Gizmo Terminal

The node uses **two separate** `DebugPrimitiveBuffer` instances:

- **ProducerBuffer**: local ECS systems write gizmos here each frame, then published to DDS via `DebugPrimitivesBatchPublisherSystem`.
- **ConsumerBuffer**: populated from DDS by `DebugPrimitivesIngressTranslator` with `filterNodeId`. The Raylib renderer reads from here.

Because CycloneDDS routes local writer → local reader transparently, the local node stream can be viewed through the consumer terminal with zero extra overhead. Switching `filterNodeId` selects which cluster node's stream is displayed.

### 5.6 Time Synchronisation

The node is **always a Slave** — time is owned by the Cluster Master:
- Time controller configured to `TimeRole.Slave`
- Three slave-side network translators registered: `DescriptorTranslator`, `SlaveLockstepTranslator`, `SlaveTimeSyncTranslator`
- Local UI play/pause/step **must** use `ITimeControlGateway`, which sends `ClusterOpRequest` over DDS to the master
- `Context.Kernel.Update()` called **without** a `dt` argument (kernel reads from `SlaveSyncController`)

### 5.7 Component & Event Registry (Segregated)

#### Components
```
HrotSharedComponentRegistry.RegisterAll(world)   // network, lifecycle, geo
KinematicComponentRegistry.RegisterAll(world)    // NavState, VehicleState, PhysicsCollider…

// IG presentation components — not covered by any shared registry
world.RegisterComponent<VisualEffectState>();    // required by SyncFdpToStrideScript effect query
world.RegisterComponent<TracerTarget>();         // required for tracer endpoint resolution
```

`CognitiveComponentRegistry` is **not registered** — Brain AI data stays on the CGF node only. `TkbTemplate.ApplyTo()` uses `repo.IsComponentTypeRegistered<T>()` to silently skip components absent on this node, keeping ECS chunk layout lean and cache-dense.

#### Events — register only what this node's systems actually consume

Every call to `world.RegisterEvent<T>()` permanently allocates double-buffered `NativeEventStream<T>` arrays in the `FdpEventBus`. `SwapBuffers()` and the Flight Recorder hot-path iterate over **every registered stream** at 60 Hz, even empty ones. Registering the global union of AI and pathfinding events would force the bus to iterate dozens of dead streams per frame.

```csharp
// Only these — driven by EventToEffectSystem and the visual renderer
world.RegisterEvent<WeaponFireNotification>();
world.RegisterEvent<DetonationNotification>();
// VisualEffectState is an ECS component, not an event — no registration needed here
```

Do **not** register `NavigationIntent`, `AreaQueryRequestBatch`, `BehaviorStateChangedEvent`, or any other Brain/CGF events. Their translators publish and read them via their own domain registries.

### 5.8 Orchestration Compliance (via NodeBootstrapper)

`NodeBootstrapper.BuildOrchestration()` is called inside `SharedApplicationBootstrapper` Phase 5. The Stride node receives:

| Handler | Purpose |
|---------|---------|
| `ReferenceReplayLoadHandler` | First in chain; intercepts Replay→Live transitions |
| `ReferenceLiveLoadHandler` | Scenario load on PrepareLife |
| `ReferencePrefetchHandler` | File prefetch 2PC |
| `ReferenceArchiveHandler` | Serialize/archive 2PC |
| `EcsRecordReplayController` | Auto-provisioned (MuscleGround role) — writes `node_700.fdp` |
| `DiagnosticsDumpClusterOpHandler` | CollectDiagnostics cluster op |

Isolated temp directory: `<staging>/nodes/node-700/`

### 5.9 Replay Safety

Mutative systems are wrapped in `TogglableSimulationGroup` (and input/postSim variants) **before** `BuildOrchestration()` is called (Phase 4 < Phase 5 guarantee). During `LoadingReplay`:
- `simGroup.Enabled = false` (kinematics, combat, EventToEffectSystem)
- `ghostCreationSystem.BypassLifecycle = true`
- ECS memory is overwritten by `PlaybackSystem` — no live systems corrupt it

### 5.10 Tick Loop

```csharp
public void Tick(float dt)
{
    ProducerBuffer.EndFrame(dt);
    ConsumerBuffer.Clear();

    Context.SlaveTranslator?.Tick();
    Context.ClusterSlave.Tick();

    Context.Kernel.Update();           // parameterless — SlaveSyncController provides dt
    Context.EventBus.SwapBuffers();

    _gizmoIngress?.PollAndApply();     // fills ConsumerBuffer from DDS
}
```

---

## 6. SyncFdpToStrideScript (Hrot.StrideMock)

Mimics the API of a Stride `SyncScript`. Owns the ECS→engine synchronization logic. Can be reused as-is inside the real Stride app by replacing `FakeStrideEntity` / `FakeStrideEffect` with Stride scene entities.

### 6.1 API

```csharp
public abstract class FakeStrideScript
{
    public abstract void Start();
    public abstract void Update(float deltaTime);
}

public sealed class SyncFdpToStrideScript : FakeStrideScript
{
    public IEnumerable<FakeStrideEntity> ActiveEntities { get; }
    public IEnumerable<FakeStrideEffect> ActiveEffects  { get; }
    public string CurrentStateMessage { get; }           // non-empty = splash needed
    public ClusterState CurrentClusterState { get; }

    public override void Start() { }
    public override void Update(float dt);
}
```

### 6.2 Differential 2-Pass Sync

Uses `Dictionary<Entity, FakeStrideEntity>` and `Dictionary<Entity, FakeStrideEffect>` keyed on the **full** `Entity` struct (Index + Generation). This gives generational safety with zero extra event subscriptions.

**Pass 1 — Destructions:**  
Iterate the dictionaries; call `repo.IsAlive(e)` (bitwise generation check). Stale entries collected in a pre-allocated `List<Entity>` (no GC alloc). Stride entities/effects removed.

**Pass 2 — Creations & Updates:**  
Query the ECS for `SimTransform` (entities) and `SimTransform + VisualEffectState` (effects). If `Entity` key absent → creation. Positions/rotations updated each frame via `GetComponentRO<SimTransform>`.

**Why not event bus lifecycle events?** During replay `PlaybackSystem` blasts raw memory directly — `ConstructionOrder`/`DestructionOrder` events are **not** fired. The 2-pass approach handles live, replay, and seek transparently.

### 6.3 Cluster State Gating

`SyncStrideEntities()` only runs when `IsOperatingState(currentClusterState)` is true:
```
OperatingLive | OperatingEdit | OperatingPreview | OperatingReplay
```
During loading states (`LoadingLive`, `LoadingReplay`, etc.) the 3D scene is cleared and a splash screen is displayed. The `ClusterSlave` background tasks never stall the Stride render loop.

### 6.4 Visual Effects

`EventToEffectSystem` (registered in `StrideNodeBootstrapper`) spawns ephemeral `VisualEffectState` entities on `WeaponFireNotification` / `DetonationNotification`. `VisualEffectCleanupSystem` ages and destroys them.

`FakeStrideEffect` tracks:
```csharp
public class FakeStrideEffect
{
    public EffectType Type { get; set; }       // Explosion | Tracer | Fire
    public Vector3 Position { get; set; }
    public Vector3 TracerEnd { get; set; }     // Tracer endpoint
    public float Scale { get; set; }
    public float Alpha { get; set; }           // [0,1] from VisualEffectState
}
```

Both dictionaries share the same two-pass loop and `_staleEntities` list.

---

## 7. StrideMockSubsystem (Hrot.StrideMock)

### 7.1 Purpose

Thin `ISubsystem` + `IMapCameraProvider` adapter. Embeds a `StrideNodeBootstrapper` and a `SyncFdpToStrideScript`, delegates all lifecycle and rendering to them. Pattern identical to `SimHostSubsystem` (thin adapter over `SimHostApp`).

### 7.2 API

```csharp
public sealed class StrideMockSubsystem : ISubsystem, IMapCameraProvider
{
    public string Name => "StrideMock";
    public Vector4 TitleBarColor => new(0.8f, 0.4f, 0.1f, 1f);  // orange

    // INetworkFactory injected by the ClusterRunner composition root (Program.cs),
    // exactly as SimHostSubsystem and IgSubsystem do.
    public StrideMockSubsystem(INetworkFactory networkFactory);

    public void Initialize(SubsystemConfig config);
    public void Update(float deltaTime);
    public void DrawWorld();
    public void DrawUI();
    public void Shutdown();

    // IMapCameraProvider
    public MapCameraView? GetCameraView();
    public void ApplyCameraView(MapCameraView view);
}
```

### 7.3 Initialization — TKB Population

`Initialize(config)` must call `BootstrapNode` **first**, then populate the `ITkbDatabase` extracted from the live context. Populating a standalone `ITkbDatabase` instance beforehand is silently orphaned: `HrotNodeBuilder` (inside Phase 1 of the bootstrapper) unconditionally provisions its own fresh database and wires it into the genesis and ghost-promotion pipelines — any pre-populated external instance is unreachable by those systems.

```csharp
public void Initialize(SubsystemConfig config)
{
    var nodeConfig = new HrotNodeConfig
    {
        DomainId      = config.DomainId,
        NodeId        = config.NodeId,
        Headless      = false,
        SubsystemName = "StrideMock",
        LocalTempRoot = Path.Combine(
            OrchestrationConstants.DefaultStagingDirectory,
            "nodes", $"node-{config.NodeId}"),
        LogDirectory  = Path.Combine(AppContext.BaseDirectory, "logs")
    };

    _core = new StrideNodeBootstrapper(...);
    _core.BootstrapNode(nodeConfig, role, _networkFactory);

    // Populate TKB AFTER BootstrapNode — extract the database the builder wired
    // into the genesis pipeline; populating any other instance has no effect.
    var tkb = _core.Context.TkbDb;
    DemoTkbSetup.RegisterAll(tkb);      // CommandTank (ID 100)
    Fdp.Examples.Scenarios.UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);  // IDs 1001–2003
    // ...
}
```

Without populating `_core.Context.TkbDb`, any entity the CGF Brain spawns over DDS will stall permanently in the `Constructing` phase because `GhostPromotionSystem` cannot resolve its blueprint.

### 7.5 Camera Pan & Zoom

The `MapCamera` lives in `StrideNodeBootstrapper`. The subsystem uses `RaylibInputProvider` gated by `SubsystemConfig.IsActiveMapOwner`:
- Input only processed when this tab is active (prevents background camera drift).
- `Camera.Update(dt)` always called to finish smooth-damping animations.
- `IMapCameraProvider` copies zoom/pan from the outgoing subsystem when the user switches tabs (no screen jump).

### 7.6 Rendering

`DrawWorld()` is only called by the orchestrator when this subsystem is the active map owner. Inside:
1. `_core.Camera.BeginMode()`
2. Draw gizmo terminal from `ConsumerBuffer` via `DebugPrimitiveRenderer2D`
3. Draw fake entities as 2D circles (red, radius 5)
4. Draw effects: orange expanding circles (Explosion), yellow lines (Tracer)
5. `_core.Camera.EndMode()`

`DrawUI()` renders cluster status splash screen via ImGui when `CurrentStateMessage` is non-empty, plus optional time-control buttons via `ITimeControlGateway`.

---

## 8. FakeStrideApp (Hrot.FakeStrideApp)

### 8.1 Purpose

Standalone Raylib/ImGui application for independent testing of the Stride integration architecture without the `ClusterRunner`. Runs as a separate process.

### 8.2 API

```csharp
public sealed class FakeStrideApp : FdpApplication
{
    // FdpApplication hooks
    protected override void OnLoad();
    protected override void OnUpdate(float dt);
    protected override void OnDrawWorld();
    protected override void OnDrawUI();
    protected override void OnUnload();

    // Follows SimHostApp / IgApplication pattern (no IMapCameraProvider)
    public MapCamera GetMapCamera() => _core.Camera;
}
```

### 8.3 Startup

`OnLoad()`:
1. Creates a local `DdsParticipant` via `HrotEnvironment.CreateParticipant(domainId)`.
2. Constructs a `NedNetworkFactory(participant, new NetworkEntityMap(), HrotEnvironment.CreateGeoTransform(), new FdpEventBus(), nodeId, role)`.
3. Builds the `HrotNodeConfig`, including an isolated staging root to avoid file-lock collisions with other processes on the same machine:
   ```csharp
   var config = new HrotNodeConfig
   {
       DomainId   = domainId,
       NodeId     = nodeId,
       Headless   = false,
       SubsystemName = "StrideMock",
       LocalTempRoot = Path.Combine(
           OrchestrationConstants.DefaultStagingDirectory,
           "nodes", $"node-{nodeId}"),
       LogDirectory  = Path.Combine(AppContext.BaseDirectory, "logs")
   };
   ```
4. Constructs `StrideNodeBootstrapper` with default modules.
5. Calls `BootstrapNode(config, role, networkFactory)` → sets `_core.Context`. `HrotNodeBuilder` (Phase 1 inside the bootstrapper) unconditionally provisions its own `ITkbDatabase` and wires it into the genesis and ghost-promotion pipelines.
6. Extracts the active TKB from the live context and populates it:
   ```csharp
   var tkb = _core.Context.TkbDb;
   DemoTkbSetup.RegisterAll(tkb);          // CommandTank (ID 100)
   Fdp.Examples.Scenarios.UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);  // IDs 1001–2003
   ```
   Both calls are required. `DemoTkbSetup` only registers CommandTank (ID 100); UrbanCombat entities (IDs 1001–2003) require the second call. Populating any `ITkbDatabase` instance other than `_core.Context.TkbDb` is silently orphaned — the genesis pipeline holds a reference to the builder-provisioned database, not any external one. Without this step every spawned entity stalls permanently in `Constructing`.
7. Constructs `SyncFdpToStrideScript(_core)`.
8. Calls `_script.Start()`.
9. Creates `RaylibInputProvider`.
10. Configures 1280×720 window, 60 FPS.

Steps 1–2 are mandatory before `BootstrapNode` (missing them stalls the clock). Step 3 (`LocalTempRoot`) must be set before `BootstrapNode` to prevent file-lock collisions during `SerializeLocal` / `PrefetchFiles`. Step 6 (TKB population) must happen after `BootstrapNode` and before the first `Tick()`.

### 8.4 Update & Draw

`OnUpdate(dt)`:
1. `_core.Camera.HandleInput(_inputProvider)` — always active (no tab switching)
2. `_core.Camera.Update(dt)`
3. `_script.Update(dt)` — ticks integration core + ECS sync

`OnDrawWorld()`: identical draw logic as `StrideMockSubsystem.DrawWorld()` but inside `FdpApplication`'s Raylib loop.

`OnDrawUI()`: ImGui splash overlay and time control buttons.

### 8.5 Network Configuration

The standalone app connects to the cluster via DDS (same `NedNetworkFactory` as other subsystems). There is no offline/standalone time mode — the app always requires a running Cluster Master for time control. Use `Hrot.ClusterRunner.exe -m orchestrator` alongside the standalone app.

---

## 9. ClusterRunner Integration

### 9.1 Subsystem Discovery

`ClusterRunner` discovers subsystems via `ScanForSubsystems()` (reflection scan). `StrideMockSubsystem` will be auto-discovered once the `Hrot.StrideMock` assembly is referenced by `Hrot.ClusterRunner.csproj`.

### 9.2 NodeId Assignment

Add to `ResolveAppNodeId()` in `Program.cs`:
```csharp
"STRIDEMOCK" => 700,
```

### 9.3 CLI Validation

Add `"stridemock"` to the valid names `HashSet` in `HrotRunnerConfiguration.Validate()`. Update the "all"/"demo" expansion to optionally include stridemock (or keep it separate — StrideMock is not part of the default cluster).

### 9.4 CLI Usage

```
# Standalone (requires external cluster master):
Hrot.ClusterRunner.exe -m stridemock --no-wait

# Full cluster with StrideMock replacing SimHost:
Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock

# Separate process, standalone fake stride app (needs external orchestrator):
Hrot.FakeStrideApp.exe
```

---

## 10. DRY Refactoring of Existing Nodes

### 10.1 SimHostApp

`SimHostApp.OnLoad()` currently contains the monolithic initialization block. After this workstream it should:
1. Inherit `SharedApplicationBootstrapper`
2. Implement `RegisterDomainComponents` (calls `SimHostComponentRegistry.RegisterAll`)
3. Implement `PopulateSystems` (uses `SimHostCoreLogicPack` decomposed modules)
4. Implement `RegisterNetworkTranslators` (calls existing SimHost translator factory methods)

All existing SimHost unit and integration tests must remain green.

**Critical:** `SimHostAuxiliaryTranslatorPack.Create()` currently registers `CreateDescriptorTranslator`, `CreateSlaveLockstepTranslator`, and `CreateSlaveTimeSyncTranslator` directly. When `SimHostApp` migrates to `SharedApplicationBootstrapper`, the base class Phase 6c registers these same translators unconditionally. The duplicate registration will produce DDS reader contention and duplicated `SwitchTimeModeEvent` processing. As part of SM-009, **remove the time translator wiring from `SimHostAuxiliaryTranslatorPack.Create()`** so all nodes rely solely on the base-class Phase 6c for time sync.

### 10.2 IgApplication

`IgApplication` has analogous initialization. Where overlapping with `SharedApplicationBootstrapper` (orchestration, togglable groups), migrate. However, IG presentation modules (`MapLayerModule`, `MapCullingModule`, `StyleResolutionModule`, `EventEffectModule`) have carefully controlled internal execution phases and must **not** be flattened into flat system lists for `TogglableSimulationGroup`. Use the `GetAdditionalModules()` hook instead:

```csharp
protected override IEnumerable<IEcsModule> GetAdditionalModules()
{
    yield return new MapLayerModule(...);
    yield return new MapCullingModule(...);
    yield return new StyleResolutionModule(...);
    yield return new EventEffectModule();
}
```

These modules are registered after the `TogglableGroups` (Phase 4b), ensuring IG's visual pipeline phases are preserved while orchestration replay-safety still applies to the physics/kinematics groups.

---

## 11. Key Design Decisions

### Why differential sync instead of event bus lifecycle?
Replay `PlaybackSystem` does raw ECS memory blits — lifecycle events are not fired. The 2-pass `IsAlive()` approach works identically in live, replay, and seek modes.

### Why always Slave time mode?
Single authoritative time source (Cluster Master) ensures all nodes halt on the exact same microsecond on pause. Local UI buttons send `ClusterOpRequest` via `ITimeControlGateway`.

### Why not unify component registries?
`TkbTemplate.ApplyTo()` uses `repo.IsComponentTypeRegistered<T>()` to silently skip components not present on a node. Unifying registries would cause the Stride/IG node to allocate `BrainHsm128` (128 B), `Blackboard1024` (1 KB), etc. for every entity — destroying DOD cache density and exhausting the 256-component limit.

### Why `MapCamera` lives in `StrideNodeBootstrapper` (not in the wrappers)?
Follows the `SimHostApp`/`IgApplication` pattern: the core app owns the camera state. The ClusterRunner wrapper exposes `IMapCameraProvider` by delegating to the core. The standalone app exposes `GetMapCamera()`. Keeps `StrideNodeBootstrapper` free of `IMapCameraProvider` (Runner toolkit) dependency.

### Future Stage 2 — swapping engine modules
To replace 2D kinematics/perception with Stride 3D native implementations, change **only** what is injected into `StrideNodeBootstrapper`:
```csharp
// Stage 1 (now):  GroundKinematicsModule, CognitiveSpatialModule
// Stage 2 (later): StrideNavMeshKinematicsModule, StrideRaycastPerceptionModule
```
No changes to orchestration, DDS translators, replay safety, or recording.

---

## 12. Success Conditions

### 12.1 Architectural / DRY

- Both `FakeStrideApp` and `StrideMockSubsystem` instantiate and tick the exact same `StrideNodeBootstrapper`.
- `StrideNodeBootstrapper` has zero references to Raylib, ImGui, or `IMapCameraProvider`.
- `SimHostApp` migrated to `SharedApplicationBootstrapper` with all existing tests green.

### 12.2 Standalone App (FakeStrideApp)

- Runs as a separate process, connects to the cluster over DDS.
- Dual-buffer gizmo terminal correctly isolates local draws from the remote stream.
- Red circles represent simulated vehicles; orange circles and yellow lines represent explosions and tracers.
- Right-click pan and scroll-wheel zoom work smoothly via `RaylibInputProvider`.

### 12.3 ClusterRunner Wrapper (StrideMockSubsystem)

- `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock` boots all three nodes, resolves NodeId offset 700.
- `[StrideMock]` tab appears in the main menu; clicking it switches the active map.
- `IMapCameraProvider.ApplyCameraView` correctly copies pan/zoom from the previous tab (no screen jump).
- 2D entities and gizmos only draw when `IsActiveMapOwner` is true.

### 12.4 Orchestration Lifecycle

- During `LoadingReplay`: `TogglableSimulationGroup` disabled; ECS sync suspended; splash screen shown.
- `ReplaySeek` time-jump: stale generation-counter entities removed (Pass 1), restored entities spawned (Pass 2) — no ghosting.
- Node participates in `PrefetchFiles` and `SerializeLocal` 2PC operations, ACKs correctly.
- `node_700.fdp` recording file written to disk during `OperatingLive`.
- `CollectDiagnostics` cluster op fulfilled by `DiagnosticsDumpClusterOpHandler`.

### 12.5 Time Control

- `Context.Kernel.Update()` called **without** `dt` — `SlaveSyncController` provides it.
- Local UI Pause/Step/Play buttons exclusively use `ITimeControlGateway.RequestPause()` / `RequestStep()` / `RequestResume()`.
- All nodes halt on the same tick when Orchestrator sends `SwitchTimeModeEvent`.
