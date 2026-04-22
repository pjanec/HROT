# MOD1 — Modularising SimHost: Brain/Muscle Split & Node Composition

**Status:** Design (not yet implemented)  
**Workstream prefix:** `MOD1-`  
**Source:** [`docs/modularizing/design-talk.md`](./design-talk.md)  
**Task details:** [`MOD1-TASK-DETAIL.md`](./MOD1-TASK-DETAIL.md)  
**Task tracker:** [`MOD1-TASK-TRACKER.md`](./MOD1-TASK-TRACKER.md)

---

## 1. Executive Summary

The current SimHost is a **monolithic all-in-one process**: it runs the AI brain (doctrine assignment, BTree evaluation, LocomotionDispatcher) and the physics muscles (CarKinematics, collision avoidance) in the same executable, wired together through shared ECS components.

This workstream refactors the architecture into a set of **composable, independently-deployable modules** that snap together at configuration time to form any desired deployment topology:

| Node Role          | What it does                                                                         |
|--------------------|--------------------------------------------------------------------------------------|
| `Brain`            | Mission planning, doctrine evaluation, BTree/HSM ticks, intent output                |
| `MuscleGround`     | Ground vehicle kinematics and collision avoidance                                    |
| `Perception`       | Autonomous smart sensors (vision broadphase, threat evaluation) + raycast solver     |
| `NavigationSolver` | On-demand pathfinding; returns lightweight Route Handles to the Brain                |
| `IG`               | Image-generator presentation (polished 2-D map for end users)                       |
| `AllInOne`         | All of the above in a single executable (current monolith replacement)              |

Each role is assembled by registering the relevant **IModule** implementations, **component registries**, and **translator packs** through a central `NodeBootstrapper`.

The enabling technical prerequisite (Phase 1) is a new **CQRS navigation contract**: replacing the ECS-only `NavState` handshake between `LocomotionDispatcher` and `CarKinematics` with two engine-agnostic DDS descriptors (`NavigationIntent` and `NavigationStatus`). This single change is what allows the Brain and Muscle to run on physically separate nodes—or in different engines (Unreal, Unity) altogether.

---

## 2. Architectural Context — The Existing Pipeline

Understanding the _current_ clean data-flow is essential before splitting it across nodes.

```
Brain layer                        Nervous system              Muscle layer
──────────────────────────────   ─────────────────────────   ────────────────────
MissionDirectorSystem              LocomotionDispatcher         CarKinematicsSystem
DoctrineIngressSystem     ────►   MoveToExecutor        ────►  SpatialHashSystem
BTreeTickSystem                                                 LinearKinemat...
                  ▲ observes                       ▼ writes
              SimTransform                        NavState
```

- **Brain writes** into `LocomotionChannel` (action id + unmanaged param bytes).
- **LocomotionDispatcherSystem** reads `LocomotionChannel`, calls the relevant `IActionExecutor` (e.g., `MoveToExecutor`), which writes a destination into `CarKinem.Core.NavState`.
- **CarKinematicsSystem** reads `NavState` (destination, speed) and integrates the bicycle model into `SimTransform`/`SimVelocity`.
- **Feedback** currently lives in the Brain: `MoveToExecutor` polls `SimTransform` distance-to-target and manages a `_stuckTicks` counter—a leaky abstraction coupling the Brain to kinematic reality.

---

## 2.5 FDP vs Hrot — Namespace Assignment Principles

A key architectural goal of this workstream is to **lift generic, domain-agnostic logic into the `FDP.*` namespace** as reusable toolkit libraries, while keeping Hrot-specific data contracts and orchestration in `Hrot.*`. This boundary is enforced by the following rule:

> **FDP toolkit code is ignorant of what entities _are_.** It knows only how to move generic component data (e.g. `SimTransform`, byte arrays, native arrays). If a module or system needs to know that an entity is "a tank" or refers to a Hrot-specific DDS topic, it belongs in Hrot.

### Assignment Table

| Area | FDP Toolkit Home | What Stays in Hrot |
|---|---|---|
| **AI / Behavior** | `FDP.Toolkit.Behavior` — `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule` | `CombatModule` (Hrot weapon domain) |
| **Navigation Contract (ECS)** | `FDP.Toolkit.Navigation` — `NavigationIntent` component, `NavigationStatus` component, engine-side `NavigationMode` + `NavigationResult` enums | `ENavigationMode` / `ENavigationResult` (DDS wire enums in `Hrot.NED.Descriptors`); `NavigationIntent` / `NavigationStatus` DDS descriptors |
| **Ground Kinematics** | `FDP.Toolkit.CarKinem` — `GroundKinematicsModule` | Project-specific road-network and trajectory pool injection |
| **Terrain Query / Ground Clamping** | `FDP.Toolkit.Geographic` — terrain batch types, clamping systems, `GroundClampingModule` base, `EClampingMode` (engine-side enum, separate from DDS wire enum) | `IgGroundClampingModule` (IG-specific terrain provider wiring); `GroundClampingOverride` DDS descriptor and its translator |
| **Perception & Sensors** | `FDP.Toolkit.Perception` — `AutonomousPerceptionModule`, `PhysicsQueryModule`, sensor batch singletons | `BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack` (Hrot DDS schema) |
| **Pathfinding** | `FDP.Toolkit.Navigation` — `PathfindingBatchData`, `NavigationSolverModule` | `BrainPathfindingTranslatorPack`, `SimPathfindingTranslatorPack` |
| **Recording / Replay** | `FDP.Toolkit.Replay` — `RecordingModule`, `ReplayModule`, `StoryRecorderModule`, `RecordingConfiguration`, `StoryTag`, `StoryReplayTag` | `EcsRecordReplayController` (binds recording lifecycle to Hrot DDS/DSM commands) |
| **Presentation** | `FDP.Toolkit.Vis2D` — `IEntityFilterFactory`, `LayerMaskFilter`, shared `EntityRenderLayer` infrastructure | `IgPresentationModule`, `SimPresentationModule`, `SstVisualizerAdapter` |
| **Time Synchronization** | `FDP.Toolkit.Time` — `MasterTimeController`, `SlaveTimeController`, `TimePulseDescriptor`, `TimePulseIngressTranslator`, `TimePulseEgressTranslator` | (nothing — time sync is fully generic) |
| **Entity Lifecycle** | `FDP.Toolkit.Lifecycle` — `EntityLifecycleModule`, `ConstructionOrder`, `DestructionOrder` state machine | `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator` (Hrot-specific DDS payloads) |
| **Network Identity** | `FDP.Toolkit.Replication.Services` — `INetworkIdAllocator`, `BlockIdManager`, `DdsIdAllocatorServer` | (nothing — ID allocation is fully generic) |
| **Networking Plumbing** | `ModuleHost.Network.Cyclone` — `ITranslatorPack` interface, `CycloneNetworkModule` | `CoreNetworkTranslatorPack` is **NOT** the right abstraction — `EntityMaster`/`OwnershipUpdate` are Hrot-specific; the framework only provides `ITranslatorPack`. Time-pulse translators go via `FDP.Toolkit.Time` |
| **Component IDs** | `Fdp.Kernel` — `GlobalComponentIds` (0–159) | `Hrot.Map.Definitions` — `HrotComponentIds` (160–255) |
| **Application Lifecycle / Runner** | `FDP.Framework.Runner` — `ISubsystem`, `SubsystemOrchestrator`, `WaitingRoomCoordinator`, `HeadlessTestExecutor`, test models, generic action handlers | `Hrot.ClusterRunner` — `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem`, `Program.cs`, Hrot-specific test handlers |

### Practical Impact on This Workstream

Each design phase below records its **target assembly** for each new artefact. When an artefact's target is an FDP toolkit, the implementation task must:
1. Create or extend the toolkit project (`FDP.Toolkit.<Area>`).
2. Add a project reference from `Hrot.SimHost` (or `Hrot.IG`) to the toolkit.
3. Ensure the toolkit has **zero** direct references to `Hrot.*` assemblies.

---

## 3. Design Phases

### Phase 1 — CQRS Navigation Contract + Authority Bug Fixes

**Goal:** Establish the network boundary between Brain and Muscles by introducing two engine-agnostic DDS descriptors. Fix legacy authority guard bugs that break split-authority deployments.

#### 3.1.1  New ECS Components — `NavigationIntent` and `NavigationStatus`

**Target assembly: `FDP.Toolkit.Navigation`** (see §2.5 — generic contract; zero Hrot knowledge)

Replace the implicit Brain↔Muscle handshake through `CarKinem.Core.NavState` with two typed, one-way ownership components:

**`NavigationIntent`** — owned by the Brain node (authority to write rests with the cognitive layer):

```csharp
// FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationIntent.cs
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationIntent)]   // reserved in 20–49 toolkit block
public struct NavigationIntent
{
    public NavigationMode Mode;       // engine-side enum in FDP.Toolkit.Navigation (see §3.1.1a)
    public Vector2        FinalDestination;  // FDP internal Cartesian (m). Conversion to GeoPoint
                                            // is the translator’s responsibility (never in FDP code)
    public float          TargetSpeed;       // m/s
    public float          ArrivalRadius;     // metres
    public uint           IntentId;          // monotonically incremented per new order
}
```

**`NavigationStatus`** — owned by the Muscle node (authority to write rests with the kinematic layer):

```csharp
// FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationStatus.cs
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.NavigationStatus)]   // reserved in 20–49 toolkit block
public struct NavigationStatus
{
    public uint             IntentId;   // echoes the IntentId being executed
    public NavigationResult Result;     // engine-side enum in FDP.Toolkit.Navigation
}
```

#### 3.1.1a  Dual-Enum Pattern for Navigation States

Following the same pattern established for `EClampingMode` in Phase 7, navigation state enums exist in **two separate forms**:

| Enum | Location | Used by |
|---|---|---|
| `NavigationMode` | `FDP.Toolkit.Navigation` | ECS `NavigationIntent` component, `MoveToExecutor` |
| `NavigationResult` | `FDP.Toolkit.Navigation` | ECS `NavigationStatus` component, `MoveToExecutor` |
| `ENavigationMode` | `Hrot.NED.Descriptors` | DDS `NavigationIntent` descriptor |
| `ENavigationResult` | `Hrot.NED.Descriptors` | DDS `NavigationStatus` descriptor |

The `NavigationIntentEgressTranslator` (Hrot) maps the engine-side `NavigationMode` to the wire `ENavigationMode` when publishing. The `NavigationIntentIngressTranslator` maps `ENavigationMode` back when receiving.

Both enums use `byte` backing and are engine-agnostic; they map cleanly to C++, Unreal, and Unity.

#### 3.1.2  New DDS Descriptors — `NavigationIntent` and `NavigationStatus`

Engine-agnostic DDS descriptors defined in `Hrot.NED.Descriptors` (the neutral data-model project):

```csharp
[DdsTopic("NavigationIntent")]
[DdsQos(Reliability = Reliable, Durability = TransientLocal, HistoryKind = KeepLast, HistoryDepth = 1)]
public partial struct NavigationIntent
{
    [DdsKey] public int     EntityId;
    public uint             IntentId;
    public ENavigationMode  Mode;
    public GeoPoint      FinalDestination;   // Lat/Lon/Alt — WGS-84
    public float            TargetSpeed;
    public float            ArrivalRadius;
}

[DdsTopic("NavigationStatus")]
[DdsQos(Reliability = Reliable, Durability = TransientLocal, HistoryKind = KeepLast, HistoryDepth = 1)]
public partial struct NavigationStatus
{
    [DdsKey] public int         EntityId;
    public uint                 IntentId;
    public ENavigationResult    Result;
}
```

`TransientLocal` ensures a newly reconnected Muscle node immediately receives the current movement orders without requiring a re-issue from the Brain.

The DDS descriptor uses `GeoPoint` (Lat/Lon/Alt in WGS-84) for the destination—never FDP's flat-earth Cartesian `Vector2`. Converting from the ECS `NavigationIntent.FinalDestination` (`Vector2` Cartesian) to `GeoPoint` is entirely the responsibility of `NavigationIntentEgressTranslator` (in `Hrot.SimHost.Network`), mirroring the pattern of `WorldPosEgressTranslator`.

#### 3.1.3  Refactor `MoveToExecutor` — Pure Brain Observer

Strip all physics awareness from `MoveToExecutor`:

- **Remove:** `Vector2.Distance` calculation, `_stuckTicks` dictionary, direct reads of `SimTransform`/`SimVelocity`. **Also remove:** any `IGeographicTransform` injection — `MoveToExecutor` must not perform coordinate conversions.
- **OnEnter:** Write `NavigationIntent` with incremented `IntentId`. Copy `MoveToParams.Destination` (FDP Cartesian `Vector2`) directly into `NavigationIntent.FinalDestination` — **no conversion**.
- **Execute:** Read `NavigationStatus`, skip if `IntentId` mismatch (stale response). Map `Arrived` → `Success`, `Failed_*` → `Failure`.
- **OnExit:** Zero out `NavigationIntent.Mode`.

The MoveToExecutor becomes a 30-line pure state-observer; frustration detection is the Muscle's responsibility.

#### 3.1.4  Add Fulfillment Logic to `CarKinematicsSystem` (or dedicated `NavigationExecutionSystem`)

The Muscle layer gains final authority over motion result determination:

- Reads `NavigationIntent`.
- Writes `NavigationStatus.Result = InProgress` initially.
- On successful arrival (entity position within `ArrivalRadius`): → `Arrived`.
- On detected frustration (`vel.Linear.Length() < threshold` for `N` consecutive ticks): → `Failed_Blocked`.
- On pathfinding failure (no valid path found by road network): → `Failed_Unreachable`.
- Echoes `IntentId` in all written statuses.

#### 3.1.5  Fix Legacy Authority Guard Bugs

The following systems use `PrimaryOwnerId == LocalNodeId` checks instead of the ECS-native `WithOwned<T>()` query, which silently breaks distributed split-authority:

| System | Bug | Fix |
|--------|-----|-----|
| `Fdp.Modules.Geographic.Systems.CoordinateTransformSystem` | `.With<NetworkOwnership>()` + manual primary check | Remove ownership query; add `.WithOwned<PositionGeodetic>()` |
| `Fdp.Modules.Geographic.Systems.GeodeticSmoothingSystem` | primary check to skip owned entities | `.WithoutOwned<Position>()` |

These are in `FDP\Toolkits\Fdp.Toolkit.Geographic\Systems\`.

---

### Phase 2 — Brain & Muscle Module Decomposition

**Goal:** Break the monolithic `SimulationLogicModule` into strictly scoped `IModule` implementations, each installable independently. This is the structural prerequisite to running Brain and Muscle on separate processes.

**Target namespaces (see §2.5):**
- `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule` → **`FDP.Toolkit.Behavior`** (generic AI; zero knowledge of entity domain)
- `GroundKinematicsModule` → **`FDP.Toolkit.CarKinem`** (generic ground-vehicle physics)
- `CombatModule` → **`Hrot.SimHost.Modules`** (Hrot weapon-domain logic)

#### 3.2.1  Current State

`SimulationLogicModule` is **not** an `IModule`; it exposes a `RegisterSystems(inputGroup, simGroup, postSimGroup)` method called directly from `SimHostApp.OnLoad`. It registers in a single call: mission direction, doctrine ingress, channel arbitration, BTree ticks, HSM ticks, weapon dispatch, perception, damage, locomotion dispatch, car kinematics, linear kinematics, ballistics, and formation systems.

#### 3.2.2  `MissionControlModule` (The Higher Brain)

**Responsibility:** Top-down command processing — doctrine assignment, multi-phase mission plan advancement.  
**Systems registered (in order):**
1. `DoctrineIngressSystem(_doctrineRegistry)` — input phase
2. `MissionDirectorSystem()` — simulation phase

#### 3.2.3  `CognitiveRuntimeModule` (The Core Brain)

**Responsibility:** Per-frame AI evaluation; behavior tree and HSM stepping.  
**Systems registered (in order):**
1. `ChannelArbitrationSystem()` — clears stale channels when doctrine changes
2. `BTreeTickSystem(_doctrineRegistry)`
3. `HsmTickSystem<BrainHsm128>(_doctrineRegistry)`
4. `HsmTickSystem<BrainHsm64>(_doctrineRegistry)`

The `DoctrineRegistry` is injected via constructor.

#### 3.2.4  `ActionDispatchModule` (The Nervous System)

**Responsibility:** Translate generic intents from channels into concrete domain targets.  
**Systems registered (in order):**
1. `LocomotionDispatcherSystem` with `MoveToExecutor`, `FollowRouteExecutor`, `JoinFormationExecutor`
2. `WeaponDispatcherSystem` with `AimAndFireExecutor`

Executors are wired during module construction/registration.

#### 3.2.5  `GroundKinematicsModule` (The Ground Muscle)

**Responsibility:** All ground-vehicle physics and spatial management.  
**Systems registered (in order):**
1. `MissionControlRequestSystem` (if not already in a separate command-processing module)
2. `SpatialHashSystem`
3. `FormationTargetSystem(_formationTemplates, _trajectoryPool)`
4. `VehicleCommandSystem`
5. `CarKinematicsSystem(_roadNetwork, _trajectoryPool)`
6. `LinearKinematicsSystem`

Query filters use `.WithOwned<SimTransform>()` rather than manual `NetworkOwnership` checks.

#### 3.2.6  `CombatModule` (Combat & Perception)

**Responsibility:** Perception, line-of-sight, damage, and ballistics.  
**Systems registered (in order):**
1. `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem` — input phase
2. `PerceptionBroadphaseSystem`, `LosRequestBatchingSystem`, `ThreatEvaluationAdapterSystem`
3. `DamageSystem`, `HsmDamageBridgeSystem`
4. `BallisticsSystem` — post-sim phase

#### 3.2.7  Retain `SimulationLogicModule` as Convenience Aggregate

Until all call-sites are migrated, `SimulationLogicModule` is refactored to **delegate** to the new modules rather than registering systems itself. It becomes a thin facade that instantiates and calls all five modules in the correct order.

---

### Phase 3 — Network Translator Packs + Node Bootstrapper

**Goal:** Eliminate the God-Class `Initialize()` pattern in `SimHostApp.OnLoad`. Replace with a `NodeBootstrapper` that composes the node from declarative `NodeRole` flags.

#### 3.3.1  Domain-Specific Translator Packs

Replace the long `translators.Add(...)` list in `SimHostApp.OnLoad` with sealed static factory classes:

**`KinematicTranslatorPack`** (Muscle egress/ingress):
- Egress: `WorldPosEgressTranslator`, `NavigationStatusEgressTranslator`
- Ingress: `NavigationIntentIngressTranslator`

**`CognitiveTranslatorPack`** (Brain egress/ingress):
- Egress: `NavigationIntentEgressTranslator`, `EntityMissionEgressTranslator`
- Ingress: `WorldPosIngressTranslator`, `NavigationStatusIngressTranslator`

**`SharedTranslatorPack`** (always required):
- `EntityMasterEgressTranslator`, `EntityMasterIngressTranslator`, `EntityInfoEgressTranslator`

Each pack's `Create(DdsParticipant, NetworkEntityMap, ...)` factory method returns `IEnumerable<IDescriptorTranslator>`.

#### 3.3.2  Domain-Specific Component Registries

Complement the existing `HrotSharedComponentRegistry` with domain-scoped registries. Because `RegisterComponent<T>()` is **idempotent** in the FDP kernel, domains may redundantly register shared primitives without risk; however, by convention each domain only registers its own types.

```
HrotSharedComponentRegistry   — SimTransform, SimVelocity, network identity, lifecycle
CognitiveComponentRegistry       — DoctrineState, BrainBlackboard, BrainBTreeState, BrainHsm128/64, LocomotionChannel, WeaponChannel, MissionPlanQueue, NavigationIntent
KinematicComponentRegistry       — VehicleState, VehicleParams, NavState, FormationMember, FormationRoster, FormationTarget, NavigationStatus
CombatComponentRegistry          — Faction, PerceptionReceptor, TargetMemory, WeaponState, Health, HealthData, BallisticProjectile, PhysicsCollider
```

#### 3.3.3  `NodeRole` Enum and `NodeBootstrapper`

```csharp
public enum NodeRole { Brain, MuscleGround, ImageGenerator, AllInOne }

public class NodeBootstrapper
{
    public void Bootstrap(NodeRole role, EntityRepository world,
                          ModuleHostKernel kernel, DdsParticipant dds, ...)
    {
        HrotSharedComponentRegistry.RegisterAll(world);
        var translators = SharedTranslatorPack.Create(dds, entityMap).ToList();

        if (role is NodeRole.MuscleGround or NodeRole.AllInOne)
        {
            KinematicComponentRegistry.RegisterAll(world);
            CombatComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new GroundKinematicsModule(...));
            kernel.RegisterModule(new CombatModule());
            translators.AddRange(KinematicTranslatorPack.Create(dds, entityMap, geo));
        }

        if (role is NodeRole.Brain or NodeRole.AllInOne)
        {
            CognitiveComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new MissionControlModule(doctrineRegistry));
            kernel.RegisterModule(new CognitiveRuntimeModule(doctrineRegistry));
            kernel.RegisterModule(new ActionDispatchModule(vehicleApi, entityMap));
            translators.AddRange(CognitiveTranslatorPack.Create(dds, entityMap));
        }

        if (role is NodeRole.ImageGenerator or NodeRole.AllInOne)
        {
            VisualComponentRegistry.RegisterAll(world);
            kernel.RegisterModule(new IgPresentationModule(tkbDb, mapCanvas));
            translators.AddRange(VisualTranslatorPack.Create(dds, entityMap));
        }

        kernel.RegisterModule(
            new CycloneNetworkModule(dds, nodeMapper, idAllocator, topology, elm,
                customTranslators: translators, sharedEntityMap: entityMap));
    }
}
```

`SimHostApp.OnLoad` is refactored to delegate to `NodeBootstrapper`, reducing its role to configuration loading and object graph construction.

---

#### 3.3.4  Concrete `IDescriptorTranslator` Implementations for Navigation

The translator packs introduced in §3.3.1 must contain **fully working** egress/ingress implementations — not stubs. These are the objects that physically move `NavigationIntent` and `NavigationStatus` across a DDS domain boundary. The pattern mirrors the existing `WorldPosEgressTranslator` in the codebase.

**`NavigationIntentEgressTranslator`** (Brain → wire):

```csharp
public sealed class NavigationIntentEgressTranslator : IDescriptorEgressTranslator
{
    private readonly DdsWriter<DDS.DM.NavigationIntent> _writer;
    private readonly NetworkEntityMap _entityMap;

    public NavigationIntentEgressTranslator(DdsParticipant dds, NetworkEntityMap entityMap)
    {
        _writer   = dds.CreateWriter<DDS.DM.NavigationIntent>();
        _entityMap = entityMap;
    }

    public void OnUpdate(EntityRepository world)
    {
        var query = world.Query()
            .With<EcsComponents.NavigationIntent>()
            .With<NetworkId>()
            .WithOwned<EcsComponents.NavigationIntent>()
            .Build();

        foreach (var entity in query)
        {
            var intent = world.GetComponentRO<EcsComponents.NavigationIntent>(entity);
            var netId  = world.GetComponentRO<NetworkId>(entity);
            _writer.Write(new DDS.DM.NavigationIntent
            {
                EntityId         = netId.Value,
                IntentId         = intent.IntentId,
                Mode             = intent.Mode,
                FinalDestination = intent.FinalDestination,
                TargetSpeed      = intent.TargetSpeed,
                ArrivalRadius    = intent.ArrivalRadius
            });
        }
    }
}
```

**`NavigationIntentIngressTranslator`** (wire → Muscle):

```csharp
public sealed class NavigationIntentIngressTranslator : IDescriptorIngressTranslator
{
    private readonly DdsReader<DDS.DM.NavigationIntent> _reader;
    private readonly NetworkEntityMap _entityMap;

    public void OnUpdate(EntityRepository world)
    {
        foreach (var msg in _reader.TakeAll())
        {
            if (!_entityMap.TryGetEntity(msg.EntityId, out var entity)) continue;
            world.SetComponent(entity, new EcsComponents.NavigationIntent
            {
                IntentId         = msg.IntentId,
                Mode             = msg.Mode,
                FinalDestination = msg.FinalDestination,
                TargetSpeed      = msg.TargetSpeed,
                ArrivalRadius    = msg.ArrivalRadius
            });
        }
    }
}
```

**`NavigationStatusEgressTranslator`** (Muscle → wire) and **`NavigationStatusIngressTranslator`** (wire → Brain) follow the same pattern, mapping the `NavigationStatus` ECS component to its DDS descriptor counterpart and back.

All Phase 6 perception and pathfinding translators follow this same structure (read from ECS / write to `DdsWriter` on egress; read from `DdsReader` / write to ECS on ingress).

---

#### 3.3.5  CycloneDDS Discovery Configuration

Distributed operation requires each process to join a shared DDS domain and auto-discover peers. Two canonical CycloneDDS XML configuration files are added to `Hrot.SimHost.Standalone/Config/`:

**`dds-allinone.xml`** — single-process, loopback only, no real network traffic:

```xml
<CycloneDDS>
  <Domain id="42">
    <General><NetworkInterfaceAddress>127.0.0.1</NetworkInterfaceAddress></General>
  </Domain>
</CycloneDDS>
```

**`dds-node.xml`** — multi-process config; multicast auto-discovery on the local subnet:

```xml
<CycloneDDS>
  <Domain id="42">
    <General><NetworkInterfaceAddress>auto</NetworkInterfaceAddress></General>
    <Discovery>
      <ParticipantIndex>auto</ParticipantIndex>
    </Discovery>
  </Domain>
</CycloneDDS>
```

**Domain ID 42** is the canonical Hrot simulation domain. All node roles (Brain, Muscle, Perception, NavigationSolver, IG) that share this domain ID on the same IP subnet auto-discover each other via CycloneDDS multicast.

For unicast environments (VPN / cross-subnet) the config can add explicit `<Peers>` addresses in place of multicast; everything else is identical.

The `CYCLONEDDS_URI` environment variable selects the active config file. Example startup:

```bat
set CYCLONEDDS_URI=file://C:\hrot\config\dds-node.xml
SimHostApp.exe --role Brain --config brain.json
```

---

#### 3.3.6  Entry Point + Role Selection

`SimHostApp` gains a command-line argument parser that selects `NodeRole` before invoking `NodeBootstrapper.Bootstrap`:

```csharp
// SimHostApp.Main or OnLoad:
var role = args.TryGetFlagValue("--role", out var roleStr)
    ? Enum.Parse<NodeRole>(roleStr, ignoreCase: true)
    : NodeRole.AllInOne;   // default: backward-compatible single-process operation

var configPath = args.TryGetFlagValue("--config", out var cp) ? cp : "config/default.json";
var nodeConfig = NodeConfiguration.LoadFrom(configPath);

new NodeBootstrapper().Bootstrap(role, world, kernel, ddsParticipant, nodeConfig, ...);
```

A new `NodeConfiguration` class (deserialised from JSON) carries role-specific parameters:

```csharp
public record NodeConfiguration
{
    public string  CycloneDdsConfigPath  { get; init; } = "config/dds-allinone.xml";
    public int     DdsDomainId           { get; init; } = 42;
    public string  RoadNetworkBlobPath   { get; init; }
    public string  DoctrineRegistryPath  { get; init; }
    public string  EntityTemplatePath    { get; init; }
}
```

`Hrot.SimHost.Standalone` and `Hrot.ExCon.Standalone` both delegate role selection to this mechanism. The same binary serves as Brain, Muscle, AllInOne, Perception, or NavigationSolver by changing only the `--role` argument and pointing to the corresponding JSON config file.

---

#### 3.3.7  Entity Lifecycle Coordination Across Processes

When Brain and Muscle run in separate processes, each replica node needs to create local ghost entities and associate them with incoming DDS descriptors. This is handled entirely by the **existing** FDP replication infrastructure — no new lifecycle coordination code is needed.

**Owner node (e.g., Brain / AllInOne):**
- `EntityMasterEgressTranslator` (in `Hrot.Map.Common`) already polls all entities with `NetworkIdentity` + `TkbIdentity` components and publishes to the Hrot BDC SST **`EntityMaster`** DDS topic.
- It uses `SmartEgressUtil.ShouldPublish` (dirty-state tracking) so it emits once on initial creation and again only if the master data changes — not every frame.
- It also queries `EntityLifecycle.All` so the announcement fires during the `Constructing` state, giving replica nodes time to initialise before the entity becomes Active.

**Replica nodes (Muscle, Perception, NavigationSolver):**
- `EntityMasterIngressTranslator` (in `Hrot.Map.Common`) subscribes to the `EntityMaster` DDS topic. On each new sample it calls `GhostCreationSystem` to materialise a local ghost entity and registers the `NetworkId ↔ Entity` mapping in `NetworkEntityMap`.
- `ReplicationLogicModule` (already exists in `FDP.Toolkit.Replication`) wraps the full replication pipeline: `OwnershipIngressSystem`, `GhostCreationSystem`, `GhostPromotionSystem`, `SubEntityCleanupSystem`, `DisposalMonitoringSystem`, `OwnershipEgressSystem`, and `SmartEgressSystem`.
- The `GhostPromotionSystem` watches `TkbIdentity` to look up the blueprint and attach the role-appropriate components, using the `ITkbDatabase` — this is the component-attachment mechanism for replica nodes, not a custom configurer.
- When a DDS instance is disposed (entity destroyed on the owner), `EntityMasterIngressTranslator` publishes `DestroyEntityCommand` to trigger clean teardown through the existing ELM pipeline.

**`NodeBootstrapper` implications:**
- All roles (Brain, Muscle, Perception, NavigationSolver) install `SharedTranslatorPack`, which already includes both `EntityMasterEgressTranslator` and `EntityMasterIngressTranslator`.
- All roles install `ReplicationLogicModule` to get ghost creation and cleanup for free.
- No custom IShadowEntityConfigurer needed; blueprint-driven component attachment is handled by `GhostPromotionSystem` + `ITkbDatabase` templates.

> **Note:** `EntityMaster` is the Hrot BDC SST project-specific DDS descriptor — it is **not** a generic FDP kernel concept. Nothing in the FDP kernel or toolkits knows about `EntityMaster`; it belongs entirely in `Hrot.Map.Common` and its consumers.

---

### Phase 4 — Presentation Module Split + Dynamic Perspective Switching

**Goal:** Wrap the two map presentations (IG end-user view, SimHost debug view) in formal `IModule` implementations. Allow a single executable to switch between them dynamically without visual disruption.

#### 3.4.1  `IgPresentationModule`

Encapsulates the polished end-user presentation:
- Instantiates a dedicated `MapCanvas` with `SstVisualizerAdapter` (MIL-STD-2525 resolution, LOD culling via `CullingState`).
- Registers `IgMapRenderSystem` in `PresentationSystemGroup`.
- Provides the operational tool stack: `CreationTool`, `MeasureTool`, `SelectionTool`, `EditTool`.
- Respects `ActivePerspective.Current == PerspectiveType.IG` before drawing.

#### 3.4.2  `SimPresentationModule`

Encapsulates the developer/debug presentation:
- Instantiates a dedicated `MapCanvas` with `SimHostVehicleVisualizer` (raw bounding boxes, NavState colour-coding).
- Registers debug-specific layers: `SimHostRoadLayer` (navigation graph), `SimHostTrajectoryLayer` (Pure Pursuit paths).
- Respects `ActivePerspective.Current == PerspectiveType.Sim` before drawing.

Both modules share the same `FDP.Toolkit.Vis2D` infrastructure (`IMapLayer`, `IMapTool`, `IVisualizerAdapter`, shared `EntityRenderLayer` with `MapDisplayComponent` bit-mask filtering).

#### 3.4.3  `ActivePerspective` Singleton + `PerspectiveCoordinatorSystem`

```csharp
[ComponentId(GlobalComponentIds.ActivePerspective)]
public struct ActivePerspective
{
    public PerspectiveType Current;   // IG | Sim
}
```

`PerspectiveCoordinatorSystem` (runs before render systems):
1. Listens for a UI toggle event (ImGui button or keyboard shortcut).
2. Updates `ActivePerspective` singleton.
3. Calls `incomingCamera.SnapTo(outgoingCamera)` to preserve zoom/position.

When only one perspective module is registered (e.g., headless `MuscleGround` node), no perspective switching is needed and neither module is installed.

#### 3.4.4  Shared Tool and Layer Infrastructure

Both perspectives consume `Vis2D` generic tools identically:
- `EntityRenderLayer` → shared; injected with different `IVisualizerAdapter` per module.
- `EntityPickerTool` → shared; injected with a different `IEntityFilterFactory` per module.
- `MapOverlayRenderLayer` → shared; the IG module adds tactical graphics, the Sim module adds trajectory overlays.

---

### Phase 5 — Component ID Registry Split

**Goal:** Remove Hrot-specific component ID constants from the FDP engine source (`GlobalComponentIds.cs` in `Fdp.Kernel`) into a single project-local registry, so that adding a new Hrot component never requires touching FDP engine files.

#### 3.5.1  Current State

`Fdp.Kernel.GlobalComponentIds` documents block allocations (0–19 kernel, 20–49 toolkits, 50–79 replication, 80–109 Vis2D, 110–139 Hrot.IG, 140–159 ModuleHost, 160–199 application descriptors) but **all constants reside in a single FDP-owned file**. Adding a new Hrot-specific component requires editing the FDP engine source.

#### 3.5.2  Target State — Two Registries Only

| Registry | Location | ID Range | Ownership |
|---|---|---|---|
| `GlobalComponentIds` | `Fdp.Kernel` (existing file, stays) | 0–159 | FDP engine team |
| `HrotComponentIds` | `Hrot.Map.Definitions` (new file) | 160–255 | Hrot project |

`Hrot.Map.Definitions` is already referenced by both `Hrot.IG` and `Hrot.SimHost`, making it the natural shared home for all project-specific IDs. All blocks from 160 upward — SimHost application components, IG-specific components added by Hrot (e.g., `GroundClampingConfig`), future integrator components — live here.

IDs 110–139 ("Hrot.IG components") and 140–159 ("ModuleHost.Core") are currently listed in `GlobalComponentIds` for documentation purposes. **Only constants actually defined in Hrot source (i.e., no matching constant exists in the FDP codebase) are moved**; any that already live in FDP-owned assemblies stay in `GlobalComponentIds`.

```csharp
// Hrot.Map.Definitions/HrotComponentIds.cs
namespace Hrot.Map.Definitions
{
    /// <summary>
    /// Project-wide ECS component ID registry for all Hrot-specific components.
    /// Block 160–255 — never edit GlobalComponentIds in Fdp.Kernel for Hrot components.
    /// </summary>
    public static class HrotComponentIds
    {
        // ── Hrot.SimHost application components (160–189) ──────────────────
        // NOTE: NavigationIntent and NavigationStatus are NOT here — they live in
        // FDP.Toolkit.Navigation and use GlobalComponentIds (20–49 toolkit block).
        public const byte ActivePerspective   = 160;
        // ... next SimHost component at 161 ...

        // NOTE: GroundClampingConfig, GroundClampingState, TerrainQueryBatchData are NOT here —
        // they live in FDP.Toolkit.Geographic and use GlobalComponentIds (20–49 toolkit block).
    }
}
```

The FDP kernel's `ComponentTypeRegistry` performs runtime collision detection (duplicate IDs → `InvalidOperationException` at startup), providing a safety net during migration.

#### 3.5.3  Migration Strategy

1. Create `Hrot.Map.Definitions/HrotComponentIds.cs` with the initial constant set (all IDs currently in `GlobalComponentIds` that reference Hrot-owned component structs).
2. Replace every `[ComponentId(GlobalComponentIds.X)]` for a Hrot-owned struct with `[ComponentId(HrotComponentIds.X)]`.
3. Remove the migrated constants from `GlobalComponentIds`; leave a comment block documenting the 160–255 range as "Hrot.Map.Definitions / HrotComponentIds".

**Explicit migration list** — the following application-specific components currently use `[ComponentId(GlobalComponentIds.X)]` but belong in the Hrot block and must be updated (along with their constants in `GlobalComponentIds`):

| Component | Source file | Current `GlobalComponentIds` constant | New `HrotComponentIds` constant |
|---|---|---|---|
| `EntityMissionHolder` | `Hrot.SimHost/Components/EntityMissionHolder.cs` | `EntityMissionHolder = 162` | `EntityMissionHolder = 162` |
| `InFormationTag` | `Hrot.SimHost/Components/InFormationTag.cs` | `InFormationTag = 163` | `InFormationTag = 163` |
| `IgEntityData` | `Hrot.IG/Components/IgEntityData.cs` | `IgEntityData = 164` | `IgEntityData = 164` |
| `IgHealthState` | `Hrot.IG/Components/IgHealthState.cs` | `IgHealthState = 165` | `IgHealthState = 165` |

After migrating these, remove the corresponding constants from `GlobalComponentIds`. The FDP kernel is then completely free of Hrot application knowledge.

---

## 4. Summary of New Artefacts

| Artefact | Type | Phase |
|---|---|---|
| `NavigationIntent` (ECS component) | `struct` in **`FDP.Toolkit.Navigation`** (Cartesian `Vector2` destination; toolkit ID 20–49 block) | P1 |
| `NavigationStatus` (ECS component) | `struct` in **`FDP.Toolkit.Navigation`** (toolkit ID 20–49 block) | P1 |
| `NavigationMode` enum (engine-side) | `enum byte` in **`FDP.Toolkit.Navigation`** | P1 |
| `NavigationResult` enum (engine-side) | `enum byte` in **`FDP.Toolkit.Navigation`** | P1 |
| `ENavigationMode` enum (DDS wire) | `enum byte` in `Hrot.NED.Descriptors` | P1 |
| `ENavigationResult` enum (DDS wire) | `enum byte` in `Hrot.NED.Descriptors` | P1 |
| `NavigationIntent` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P1 |
| `NavigationStatus` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P1 |
| `MissionControlModule` | `IModule` in **`FDP.Toolkit.Behavior`** | P2 |
| `CognitiveRuntimeModule` | `IModule` in **`FDP.Toolkit.Behavior`** | P2 |
| `ActionDispatchModule` | `IModule` in **`FDP.Toolkit.Behavior`** | P2 |
| `GroundKinematicsModule` | `IModule` in **`FDP.Toolkit.CarKinem`** | P2 |
| `CombatModule` | `IModule` in `Hrot.SimHost.Modules` | P2 |
| `KinematicTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P3 |
| `CognitiveTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P3 |
| `SharedTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P3 |
| `CognitiveComponentRegistry` | `static` class in `Hrot.SimHost` | P3 |
| `KinematicComponentRegistry` | `static` class in `Hrot.SimHost` | P3 |
| `CombatComponentRegistry` | `static` class in `Hrot.SimHost` | P3 |
| `NodeRole` enum | `Hrot.SimHost` | P3 |
| `NodeBootstrapper` | class in `Hrot.SimHost` | P3 |
| `NavigationIntentEgressTranslator` | `IDescriptorEgressTranslator` in `Hrot.SimHost.Network` | P3 |
| `NavigationIntentIngressTranslator` | `IDescriptorIngressTranslator` in `Hrot.SimHost.Network` | P3 |
| `NavigationStatusEgressTranslator` | `IDescriptorEgressTranslator` in `Hrot.SimHost.Network` | P3 |
| `NavigationStatusIngressTranslator` | `IDescriptorIngressTranslator` in `Hrot.SimHost.Network` | P3 |
| `NodeConfiguration` | record class in `Hrot.SimHost` | P3 |
| `dds-allinone.xml`, `dds-node.xml` | CycloneDDS XML configs in `Hrot.SimHost.Standalone/Config/` | P3 |
| `IgPresentationModule` | `IModule` in `Hrot.SimHost.Modules` | P4 |
| `SimPresentationModule` | `IModule` in `Hrot.SimHost.Modules` | P4 |
| `ActivePerspective` (singleton component) | `struct` in `Hrot.SimHost.Components` | P4 |
| `PerspectiveCoordinatorSystem` | `IModuleSystem` in `Hrot.SimHost.Systems` | P4 |
| `HrotComponentIds` | `static` class in `Hrot.Map.Definitions` | P5 |
| `EClampingMode` enum | `enum byte` in **`FDP.Toolkit.Geographic`** (engine-side; separate from DDS wire enum) | P7 |
| `GroundClampingOverride` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P7 |
| `GroundClampingConfig` ECS component | `struct` in **`FDP.Toolkit.Geographic`** | P7 |
| `GroundClampingState` ECS component | `struct` in **`FDP.Toolkit.Geographic`** | P7 |
| `TerrainQueryBatchData` ECS singleton | `struct` in **`FDP.Toolkit.Geographic`** | P7 |
| `ITerrainProvider` interface | **`FDP.Toolkit.Geographic`** | P7 |
| `IgGroundClampingModule` | `IModule` in `Hrot.IG.Modules` (IG-specific wiring) | P7 |
| `TerrainQueryInitializationSystem` | `IModuleSystem` in **`FDP.Toolkit.Geographic`** | P7 |
| `TerrainQuerySubmitSystem` | `IModuleSystem` in **`FDP.Toolkit.Geographic`** | P7 |
| `TerrainQuerySolverSystem` | `IModuleSystem` in **`FDP.Toolkit.Geographic`** | P7 |
| `TerrainQueryResolutionSystem` | `IModuleSystem` in **`FDP.Toolkit.Geographic`** | P7 |
| `GroundClampingOverrideTranslator` | `IDescriptorTranslator` in `Hrot.IG.Network` | P7 |
| `SensorModality` flags enum | `enum byte` in **`FDP.Toolkit.Perception`** | P6 |
| `VisualReceptor` ECS component | `struct` in **`FDP.Toolkit.Perception`** | P6 |
| `RadarReceptor` ECS component | `struct` in **`FDP.Toolkit.Perception`** | P6 |
| `PathfindingBatchData` ECS singleton | `struct` in **`FDP.Toolkit.Navigation`** | P6 |
| `PhysicsQueryActionNode` | Abstract BTree action base class in **`FDP.Toolkit.Physics`** — provides `RequestRaycast` / `GetRaycastResult` that go directly to `RaycastBatchData`; replaces removed `BTreeContext` stubs (see §3.6.6) | P6 |
| `PathfindingActionNode` | Abstract BTree action base class in **`FDP.Toolkit.Navigation`** — provides `RequestPath` / `GetPathResult` that go directly to `PathfindingBatchData`; replaces removed `BTreeContext` stubs (see §3.6.6) | P6 |
| `RelativeVector3` DDS struct | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `RaycastRequestBatch` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `RaycastResponseBatch` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `SensorConfig` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `SensorTargets` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `PathRequestBatch` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `PathResponseBatch` DDS descriptor | `partial struct` in `Hrot.NED.Descriptors` | P6 |
| `AutonomousPerceptionModule` | `IModule` in **`FDP.Toolkit.Perception`** | P6 |
| `PhysicsQueryModule` | `IModule` in **`FDP.Toolkit.Physics`** (wraps existing raycast/hit systems) | P6 |
| `NavigationSolverModule` | `IModule` in **`FDP.Toolkit.Navigation`** | P6 |
| `BrainPerceptionTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P6 |
| `SimPerceptionTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P6 |
| `BrainPathfindingTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P6 |
| `SimPathfindingTranslatorPack` | `static` class in `Hrot.SimHost.Network` | P6 |
| `EcsRecordReplayController` | `IDsmHandler` + factory in `Hrot.SimHost/Modules/Orchestration/` | P8 |
| `RecordingModule` | `IModule` + `IDisposable` in **`FDP.Toolkit.Replay`** | P8 |
| `StoryRecorderModule` | `IModule` + `IDisposable` in **`FDP.Toolkit.Replay`** | P8 |
| `ReplayModule` | `IModule` + `IDisposable` in **`FDP.Toolkit.Replay`** | P8 |
| `RecordingConfiguration` | `sealed class` in **`FDP.Toolkit.Replay`** | P8 |
| `StoryTag` ECS component | `struct` in **`FDP.Toolkit.Replay`** | P8 |
| `StoryReplayTag` ECS component | `struct` in **`FDP.Toolkit.Replay`** | P8 |
| `RecorderSystem.EntityFilter` | Additive extension to `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` | P8 |
| `ISubsystem` interface + `SubsystemConfig` | Contract in **`FDP.Framework.Runner`** | P9 |
| `IMapCameraProvider` interface | **`FDP.Framework.Runner`** | P9 |
| `SubsystemOrchestrator` (refactored) | Class in **`FDP.Framework.Runner`** (no Hrot coupling) | P9 |
| `WaitingRoomCoordinator` | Class in **`FDP.Framework.Runner`** | P9 |
| `RunnerConfiguration` (base) | Class in **`FDP.Framework.Runner`** | P9 |
| `HeadlessTestExecutor` | Class in **`FDP.Framework.Runner`** | P9 |
| `TestScript`, `TestStep`, `TestReport` | Models in **`FDP.Framework.Runner`** | P9 |
| `ITestActionHandler` | Interface in **`FDP.Framework.Runner`** | P9 |
| `WaitActionHandler`, `TickActionHandler`, `AssertAllActionHandler` | Generic handlers in **`FDP.Framework.Runner`** | P9 |
| `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem` | Concrete subsystems in `Hrot.ClusterRunner` (unchanged) | P9 |
| `SpawnActionHandler`, `MoveActionHandler`, `AssertPositionActionHandler` | Domain handlers in `Hrot.ClusterRunner` | P9 |

---

---

### Phase 6 — Distributed Perception & Pathfinding Modules

**Goal:** Modularise the perception pipeline (smart autonomous sensors + dumb batch queries) and on-demand pathfinding so that the Brain, a dedicated Perception solver, and a dedicated Navigation solver can run on separate nodes or be composed into a single executable. Remove the `RequestRaycast` and `RequestPath` stubs from `BTreeContext` entirely and replace them with dedicated BTree Action Node base classes that live in the correct toolkit assemblies — this avoids the circular project dependency (`FDP.Toolkit.Navigation` → `FDP.Toolkit.Behavior` → `FDP.Toolkit.Navigation`) that would arise from coupling the generic AI context to specific physics/navigation singletons.

**Target namespaces (see §2.5):**
- `AutonomousPerceptionModule`, per-modality receptor components, `SensorModality` → **`FDP.Toolkit.Perception`**
- `PhysicsQueryModule` (wraps `RaycastSolverSystem`, `HitResolutionSystem`) → **`FDP.Toolkit.Physics`**
- `NavigationSolverModule`, `PathfindingBatchData` (Cartesian `Vector3` coords) → **`FDP.Toolkit.Navigation`**
- Translator packs (`BrainPerceptionTranslatorPack`, `SimPerceptionTranslatorPack`, `BrainPathfindingTranslatorPack`, `SimPathfindingTranslatorPack`) → **`Hrot.SimHost.Network`** (Hrot DDS schema; converters do geo transforms)

---

#### 3.6.1  Architectural Overview

The existing code already implements the single-node version of both pipelines:

- **Raycasts:** `RaycastBatchData` singleton → `RaycastSolverSystem` → `HitResolutionSystem` (all in `FDP.Toolkit.Physics`).
- **Perception:** `PerceptionReceptor` + `TargetMemory` → `VisionBroadphaseSystem` → `LosRequestBatchingSystem` → `ThreatEvaluationSystem` (all in `FDP.Toolkit.Perception`).
- **BTree stubs (to be deleted):** `BTreeContext.RequestRaycast` and `RequestPath` both return `-1` in `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs`. Phase 6 **deletes** these methods entirely from `BTreeContext` (and from `IAIContext`) rather than wiring them to toolkit singletons. Wiring would force `FDP.Toolkit.Behavior` to reference `FDP.Toolkit.Navigation` / `FDP.Toolkit.Physics`, creating an uncompilable circular dependency since `FDP.Toolkit.Navigation` already depends on `FDP.Toolkit.Behavior` (via `MoveToExecutor implements IActionExecutor<LocomotionChannel>`). See §3.6.6 for the corrected approach.

Phase 6 inserts the **DDS network split** into the middle of these existing pipelines. Within a single `AllInOne` node, the translators simply write between the local ECS singletons without touching the network (zero-overhead pass-through). When roles are separated, the translators serialise/deserialise over DDS.

---

#### 3.6.1a  Prerequisite: Fix Hardcoded Hrot IDs on Perception Components

> ⚠️ **ID leak:** Two components destined for `FDP.Toolkit.Perception` currently carry `[ComponentId]` values from the Hrot project block (160–255): `Faction` uses `[ComponentId(250)]` and `PerceptionReceptor` uses `[ComponentId(251)]`. IDs 160–255 are strictly reserved for the Hrot application (see §3.5). FDP toolkit components must never use IDs from that block, as it couples the generic engine layer to the application layer.

**Fix:** Before any other Phase 6 work, change both structs to use the FDP toolkit block (20–49) and register the new constants in `GlobalComponentIds`:

```csharp
// FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs  — toolkit block 20–49
public const byte Faction            = 26;   // next available after existing toolkit IDs
public const byte PerceptionReceptor = 27;

// FDP/Toolkits/FDP.Toolkit.Perception/Components/Faction.cs
[ComponentId(GlobalComponentIds.Faction)]
public struct Faction { ... }

// FDP/Toolkits/FDP.Toolkit.Perception/Components/PerceptionReceptor.cs
[ComponentId(GlobalComponentIds.PerceptionReceptor)]
public struct PerceptionReceptor { ... }
```

This step is incorporated into **MOD1-P6T1** (see task detail). After the change, IDs 250 and 251 become available in the Hrot block for future application use.

---

#### 3.6.2  Multi-Modal Sensor Support

**Problem:** A single monolithic `PerceptionReceptor` struct cannot cleanly hold the parameters of N heterogeneous sensor types (visual, radar, thermal, acoustic) while respecting the component-count limit (~256).

**Solution:** One **per-modality receptor component** per sensor type. Only the active sensor types are added to an entity.

```csharp
[ComponentId(GlobalComponentIds.VisualReceptor)]
public struct VisualReceptor  { public float VisionRange; public float FovCos; }

[ComponentId(GlobalComponentIds.RadarReceptor)]
public struct RadarReceptor   { public float MaxRange; public float EmissionPower; public int TargetMask; }
// AcousticReceptor, ThermalReceptor — same pattern
```

Existing `PerceptionReceptor` retains its current role for vision in the monolithic path; the new per-modality structs extend this into the distributed world.

**Fused result — `TargetMemory` modality bitmask:**

```csharp
[Flags] public enum SensorModality : byte { Visual = 1, Radar = 2, Thermal = 4, Acoustic = 8 }

// Added to the existing TargetMemory fixed arrays:
public fixed byte Modalities[PerceptionConstants.MaxTrackedTargets];
```

The `SensorFusionIngressTranslator` on the Brain node receives `SensorTargets` from multiple solver nodes and applies a bitwise `OR` to update `Modalities`, fusing heterogeneous sensor results into the single shared `TargetMemory`. The Brain's BTree can optionally inspect `(Modalities[i] & SensorModality.Radar) != 0` for modality-aware decisions.

The `SensorConfig` DDS topic carries dynamic sensor parameter updates (Brain → Solver). On ingress at the Solver node, `FovDegrees` is pre-converted to `FieldOfViewCos` to keep the `VisionBroadphaseSystem` hot path free of `MathF.Cos` calls.

---

#### 3.6.3  Smart Sensor Module — `AutonomousPerceptionModule`

**Responsibility:** Continuous, asynchronous environment scanning at a lower tick rate (≈10 Hz). Installed on any node that acts as a Perception or AllInOne role.

**Execution Policy:** `ExecutionPolicy.SlowBackground(10)` — runs on a background thread using Snapshot-on-Demand (SoD) to avoid stalling the 60 Hz physics loop.

**Registered Systems (in order):**
1. `LocalGridBuilderSystem` — rebuilds the spatial index from `SimTransform` snapshots.
2. `VisionBroadphaseSystem` — evaluates FOV and faction filters; emits `LosCheckRequestEvent`.
3. `LosRequestBatchingSystem` — translates events into `RaycastBatchData.Requests`.
4. `ThreatEvaluationSystem` — decays existing scores and boosts confirmed-visible targets; writes `TargetMemory`.

**Data Contract:** Reads `PerceptionReceptor` (and per-modality receptors when present), `SimTransform`, `Faction`. Mutates `TargetMemory`.

---

#### 3.6.4  Dumb Query Module — `PhysicsQueryModule`

**Responsibility:** Synchronous, high-resolution per-frame batch raycast solving. Installed on any Perception or AllInOne node.

**Execution Policy:** `ExecutionPolicy.Synchronous` (main thread, 60 Hz).

**Registered Systems (in order):**
1. `RaycastSolverSystem` — parallel `Parallel.For` loop over `RaycastBatchData.Requests`; writes `RaycastBatchData.Hits`.
2. `HitResolutionSystem` — dispatches `TargetVisibleEvent` for confirmed hits; resets `RaycastBatchData.Count`.

---

#### 3.6.5  DDS Descriptors for Perception (Engine-Agnostic)

All coordinates use the **Local Tangent Plane (ENU)** compression strategy: one double-precision `GeoPoint BatchOrigin` anchor per batch, with all per-ray offsets as `float`-precision `RelativeVector3{East, North, Up}`. This halves payload size vs. sending absolute WGS-84 doubles per ray and eliminates engine-specific coordinate-axis differences from the wire.

**Shared struct:**
```csharp
[DdsStruct]
public partial struct RelativeVector3 { public float East, North, Up; }
```

**Dumb Raycast CQRS pipeline:**

```
RaycastRequestBatch  (Brain → Solver, DdsQos: Reliable/Volatile)
  [DdsKey] int SourceNodeId  |  uint BatchCorrelationId  |  GeoPoint BatchOrigin
  List<DdsRaycastRequest>  { long RayId, RelativeVector3 Start/End, int LayerMask, long IgnoreEntityId }

RaycastResponseBatch  (Solver → Brain, DdsQos: Reliable/Volatile)
  [DdsKey] int TargetNodeId  |  uint BatchCorrelationId
  List<DdsRaycastHit>  { long RayId, bool HasHit, long HitEntityId, float HitT }
```

**Smart Sensor CQRS pipeline:**

```
SensorConfig  (Brain → Solver, DdsQos: Reliable/TransientLocal, KeepLast=1)
  [DdsKey] long EntityId  |  float VisionRange, HearingRange, FovDegrees

SensorTargets  (Solver → Brain, DdsQos: BestEffort/Volatile)
  [DdsKey] long ObserverEntityId  |  uint Tick
  List<DdsTrackedTarget>  { long TargetEntityId, float ThreatScore, float Distance, float BearingDegrees }
```

Multiple sensor modalities each get their own DDS topic pair (e.g., `RadarSensorConfig` / `RadarTargets`), allowing dedicated solver nodes to subscribe only to their relevant topics and ignore all others — standard DDS pub/sub routing with zero application-level dispatch code.

---

#### 3.6.6  BTreeContext Cleanup — Deleting the Stubs

> ⚠️ **Why we delete instead of wire:** The previous design called for injecting `RaycastBatchData` (from `FDP.Toolkit.Physics`) and `PathfindingBatchData` (from `FDP.Toolkit.Navigation`) directly into `BTreeContext` (in `FDP.Toolkit.Behavior`). This would create a **fatal circular project dependency**: `FDP.Toolkit.Navigation` already references `FDP.Toolkit.Behavior` (via `MoveToExecutor implements IActionExecutor<LocomotionChannel>`). Coupling `FDP.Toolkit.Behavior` back to `FDP.Toolkit.Navigation` makes compilation impossible. The fix is to keep `BTreeContext` ignorant of any specific toolkit singletons.

**Action:** Delete `RequestRaycast`, `GetRaycastResult`, `RequestPath`, and `GetPathResult` from `BTreeContext.cs` and from the `IAIContext` interface. `BTreeContext` holds only an `EntityRepository` reference — it remains a thin, dependency-free execution container.

**Replacement — Dedicated Action Node Base Classes:**

Instead, two lightweight base classes are created in the appropriate toolkit assemblies. Concrete BTree Action Nodes that need raycasts or pathfinding subclass these directly:

**`PhysicsQueryActionNode`** (in `FDP.Toolkit.Physics`):
```csharp
// Base class for any BTree leaf node that needs to submit or read raycast queries.
// Lives in FDP.Toolkit.Physics — no reference back to FDP.Toolkit.Behavior.
public abstract class PhysicsQueryActionNode : BTreeActionNode
{
    protected int RequestRaycast(EntityRepository world, Vector3 origin, Vector3 direction, float maxDistance)
    {
        ref var batch = ref world.GetSingletonRef<RaycastBatchData>();
        if (batch.Count >= batch.Requests.Length) return -1;
        int idx = batch.Count++;
        long rayId = ((long)EntityIndex << 20) | (uint)idx;
        batch.Requests[idx] = new RaycastRequest { Start = origin, End = origin + direction * maxDistance, RayId = rayId, IgnoreEntity = Entity };
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

**`PathfindingActionNode`** (in `FDP.Toolkit.Navigation`):
```csharp
// Base class for any BTree leaf node that needs to request or read pathfinding results.
// Lives in FDP.Toolkit.Navigation — no reference back to FDP.Toolkit.Behavior.
public abstract class PathfindingActionNode : BTreeActionNode
{
    protected int RequestPath(EntityRepository world, Vector3 from, Vector3 to, byte mobilityProfile = 0)
    {
        ref var batch = ref world.GetSingletonRef<PathfindingBatchData>();
        if (batch.Count >= batch.Requests.Length) return -1;
        long requestId = ((long)EntityIndex << 20) | (uint)batch.Count;
        batch.Requests[batch.Count++] = new PathRequest { RequestId = requestId, Start = from, End = to, MobilityProfile = mobilityProfile };
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

**Naming of concrete nodes:** `Action_QueryRaycast` subclasses `PhysicsQueryActionNode`; `Action_PlanRoute` subclasses `PathfindingActionNode`. Both node implementations live in their respective toolkits, keeping the dependency graph strictly one-way.

For distributed deployment, the Brain node's local `RaycastBatchData.Hits` and `PathfindingBatchData.Results` are populated by ingress translators (`RaycastBatchIngressTranslator`, `PathResponseIngressTranslator`), completing the async loop transparently — the concrete action nodes require no changes.

---

#### 3.6.7  Pathfinding Queries — Route Handle Pattern

A major architectural concern: the Brain node must be able to request a path and — crucially — the Sim node must **not** send thousands of waypoints back over the network. Instead, the Sim node caches the full high-resolution spline in its local `TrajectoryPoolManager` and returns a lightweight `RouteHandle` (an integer ID).

**New ECS Singleton — `PathfindingBatchData`:**

```csharp
[ComponentId(GlobalComponentIds.PathfindingBatchData)]
public struct PathfindingBatchData
{
    public int Count;
    public NativeArray<PathRequest> Requests;   // { long RequestId, Vector3/GeoPoint Start/End, byte MobilityProfile }
    public NativeArray<PathResult> Results;     // { long RequestId, bool IsReachable, float TotalDistanceMeters, int RouteHandle }
}
```

This mirrors the existing `RaycastBatchData` pattern and lives in the same pre-allocated, zero-GC native memory.

**Action node pattern (replacing removed BTreeContext stubs):**
BTree leaf nodes that need pathfinding subclass `PathfindingActionNode` (in `FDP.Toolkit.Navigation`) — see §3.6.6. The concrete `Action_PlanRoute` node calls `RequestPath(world, from, to)` / `GetPathResult(world, requestId)` from that base class. No routing through `BTreeContext` is required.

**DDS descriptors (in `Hrot.NED.Descriptors`):**

```
PathRequestBatch  (Brain → Solver, Reliable/Volatile)
  [DdsKey] int SourceNodeId  |  GeoPoint BatchOrigin
  List<DdsPathRequest>  { long RequestId, RelativeVector3 Start/End, byte MobilityProfile }

PathResponseBatch  (Solver → Brain, Reliable/Volatile)
  [DdsKey] int TargetNodeId
  List<DdsPathResult>  {
    long RequestId, bool IsReachable, float TotalDistanceMeters,
    int RouteHandle,             // ID cached in Sim's TrajectoryPoolManager
    List<RelativeVector3> CoarseWaypoints  // Optional: major intersections only
  }
```

**Execution flow:**
1. BTree `Action_PlanRoute` node writes `PathRequest` → `PathfindingBatchData.Requests`, returns `Running`.
2. `PathRequestEgressTranslator` publishes the batch over DDS as `PathRequestBatch`.
3. Solver node's `PathfindingSolverSystem` (in `NavigationSolverModule`) runs A* on `RoadNetworkBlob` / Unreal NavMesh, calls `_trajectoryPool.RegisterTrajectory(waypoints)` → gets a `TrajectoryId`.
4. `PathResponseEgressTranslator` publishes `PathResponseBatch` with `RouteHandle = TrajectoryId`.
5. Brain's `PathResponseIngressTranslator` writes results into `PathfindingBatchData.Results`.
6. On next BTree tick, `Action_PlanRoute` calls `GetPathResult`, reads `RouteHandle`.
7. Brain writes `FollowRouteParams { TrajectoryId = RouteHandle }` into `LocomotionChannel` → `NavigationIntentEgressTranslator` publishes the intent.
8. Sim node's `FollowRouteExecutor` maps `TrajectoryId` → locally cached spline → executes Pure Pursuit.

Within a single `AllInOne` node, steps 2–5 short-circuit locally with no DDS overhead.

---

#### 3.6.8  `NavigationSolverModule`

**Responsibility:** On-demand path computation. Installed on any NavigationSolver or AllInOne node.

**Execution Policy:** `ExecutionPolicy.Synchronous` (or `SlowBackground` for heavy NavMesh scenarios).

**Registered Systems:**
1. `PathfindingSolverSystem` — reads `PathfindingBatchData.Requests`, queries `RoadNetworkBlob`/NavMesh, calls `TrajectoryPoolManager.RegisterTrajectory`, writes `PathfindingBatchData.Results`.

---

#### 3.6.9  Translator Packs (Perception & Pathfinding)

**`SimPerceptionTranslatorPack`** — installed on Perception/AllInOne nodes:
- Ingress: `SensorConfigIngressTranslator` (DDS → `PerceptionReceptor`), `RaycastBatchIngressTranslator` (DDS → `RaycastBatchData.Requests`).
- Egress: `SensorTargetsEgressTranslator` (`TargetMemory` → DDS), `RaycastBatchEgressTranslator` (`RaycastBatchData.Hits` → DDS).

**`BrainPerceptionTranslatorPack`** — installed on Brain/AllInOne nodes:
- Egress: `SensorConfigEgressTranslator` (`PerceptionReceptor` changes → DDS), `RaycastBatchEgressTranslator` (`RaycastBatchData.Requests` → DDS).
- Ingress: `SensorTargetsIngressTranslator` (DDS → `TargetMemory`; applies `OR` on `Modalities` for sensor fusion), `RaycastBatchIngressTranslator` (DDS → `RaycastBatchData.Hits`).

**`SimPathfindingTranslatorPack`** — installed on NavigationSolver/AllInOne nodes:
- Ingress: `PathRequestIngressTranslator` (DDS → `PathfindingBatchData.Requests`).
- Egress: `PathResponseEgressTranslator` (`PathfindingBatchData.Results` → DDS).

**`BrainPathfindingTranslatorPack`** — installed on Brain/AllInOne nodes:
- Egress: `PathRequestEgressTranslator` (`PathfindingBatchData.Requests` → DDS).
- Ingress: `PathResponseIngressTranslator` (DDS → `PathfindingBatchData.Results`).

In `AllInOne` mode both the Brain and Sim translator packs are installed. Their pass-through translators co-exist harmlessly because ECS singletons are shared in-process: the egress translator on the Brain side reads from the same native array that the solver system writes to, making the DDS publish step a no-op (or simply omitted by a `--no-network` flag).

---

#### 3.6.10  `NodeRole` Extensions

The `NodeRole` enum (Phase 3) gains two new values:

```csharp
public enum NodeRole { Brain, MuscleGround, Perception, NavigationSolver, ImageGenerator, AllInOne }
```

`NodeBootstrapper` gains corresponding branches:
- `Perception` → registers `CombatComponentRegistry` (raycasts), `AutonomousPerceptionModule`, `PhysicsQueryModule`, `SimPerceptionTranslatorPack`.
- `NavigationSolver` → registers `KinematicComponentRegistry`, `NavigationSolverModule`, `SimPathfindingTranslatorPack`.

---

#### 3.6.11  Standalone Process Compositions

To actually run Perception and NavigationSolver as separate operating-system processes, `NodeBootstrapper` (§3.3.6) is extended with launch-ready configurations for each new role. The same `SimHostApp.exe` binary serves all roles via `--role`.

**Perception node launch:**

```bat
set CYCLONEDDS_URI=file://config/dds-node.xml
SimHostApp.exe --role Perception --config perception.json
```

- Registers: `HrotSharedComponentRegistry`, `CombatComponentRegistry`
- Modules: `AutonomousPerceptionModule`, `PhysicsQueryModule`
- Translators: `SharedTranslatorPack` + `SimPerceptionTranslatorPack`
  - Subscribes to `SensorConfig` (Brain → Solver) and `RaycastRequestBatch` (Brain → Solver).
  - Publishes `SensorTargets` (Solver → Brain) and `RaycastResponseBatch` (Solver → Brain).
- Shadow entities: `ShadowEntityConfigurer` attaches `PerceptionReceptor` and `SimTransform` (position replica) per announced template.

**NavigationSolver node launch:**

```bat
set CYCLONEDDS_URI=file://config/dds-node.xml
SimHostApp.exe --role NavigationSolver --config navsolver.json
```

- Registers: `HrotSharedComponentRegistry`, `KinematicComponentRegistry`
- Modules: `NavigationSolverModule` (requires `RoadNetworkBlobPath` from `NodeConfiguration`)
- Translators: `SharedTranslatorPack` + `SimPathfindingTranslatorPack`
  - Subscribes to `PathRequestBatch` (Brain → Solver).
  - Publishes `PathResponseBatch` (Solver → Brain) with `RouteHandle` IDs referencing the solver's local `TrajectoryPoolManager`.
- No `CarKinematicsSystem` — vehicle physics are not simulated on this node; it only answers A\* queries.

In the `AllInOne` deployment, the same `NodeBootstrapper` branch registration happens in-process. The DDS publish/subscribe short-circuits through in-memory ECS singletons when `dds-allinone.xml` points to loopback, achieving zero real-network overhead while exercising the same translator code paths.

---

### Phase 7 — IG Ground Clamping Module

**Goal:** Solve the Heterogeneous Terrain Correlation problem: because the IG uses LOD-dependent terrain meshes and the Sim uses a high-fidelity physics terrain, the same geo-coordinates produce different Z values on each side. An asynchronous, zero-allocation Z-offset pipeline on the IG corrects the visual altitude of ghost entities without corrupting the authoritative `SimTransform` state and without blocking the horizontal movement pipeline.

This phase is **IG-only** — `SimHost` and `Brain` nodes are not affected.

**Target namespaces (see §2.5):**
- `TerrainQueryBatchData`, `TerrainQueryRequest`, `TerrainQueryResult`, all four execution systems (`TerrainQueryInitializationSystem`, `TerrainQuerySubmitSystem`, `TerrainQuerySolverSystem`, `TerrainQueryResolutionSystem`), `ITerrainProvider`, `EClampingMode` (engine-side enum), `GroundClampingConfig`, `GroundClampingState`, `GroundClampingModule` (base) → **`FDP.Toolkit.Geographic`**
- `IgGroundClampingModule` (wires IG-specific `ITerrainProvider`, conditionally installed by `NodeBootstrapper`) → **`Hrot.IG.Modules`**
- `GroundClampingOverride` DDS descriptor + `GroundClampingOverrideTranslator` → **`Hrot.NED.Descriptors`** / **`Hrot.IG.Network`**

---

#### 3.7.1  Problem Statement

- `SimHost` vehicle altitude is authoritative (physics-accurate, high-resolution terrain).
- IG renders using LOD meshes; the same position may resolve to a terrain Z that differs by ±N metres from the Sim value.
- Result without correction: ghost tanks partially buried or floating.
- Correction must not introduce horizontal stutter, must handle multi-storey buildings/bridges by bracketing the raycast, and must support run-time enabling/disabling per-entity (aircraft landing, editor drag-and-drop).

---

#### 3.7.2  Network Contract — `GroundClampingOverride` DDS Descriptor

Dynamic per-entity clamping control is published by the Sim module (for aircraft take-off/landing) and by the IOS editor (for drag-and-drop). Using a ternary mode allows entities to rely on TKB defaults while giving operators override power.

```csharp
// Hrot.NED.Descriptors
public enum EClampingMode : byte
{
    CLAMP_DEFAULT   = 0,   // Engine decides (grounded vehicle = ON, airborne = OFF)
    CLAMP_FORCE_ON  = 1,   // Explicitly clamped (e.g. taxiing aircraft)
    CLAMP_FORCE_OFF = 2    // Explicitly unclamped (e.g. editor drag, in-flight)
}

[DdsTopic("GroundClampingOverride")]
[DdsQos(Reliability = Reliable, Durability = TransientLocal, HistoryKind = KeepLast, HistoryDepth = 1)]
public partial struct GroundClampingOverride
{
    [DdsKey] public int EntityId;
    public EClampingMode Mode;
}
```

`TransientLocal` ensures late-joining IG nodes immediately receive the current clamping state for every entity without requiring a republish.

---

#### 3.7.3  ECS Components

**`GroundClampingConfig`** — written by the ingress translator; read by `TerrainQuerySubmitSystem`:

```csharp
[ComponentId(HrotComponentIds.GroundClampingConfig)]
public struct GroundClampingConfig
{
    public EClampingMode Mode;
    public byte BaseRequiresClamping;  // Seed from TKB: 1 = grounded vehicle, 0 = aircraft

    public bool IsClampingActive =>
        Mode == EClampingMode.CLAMP_FORCE_ON ||
        (Mode == EClampingMode.CLAMP_DEFAULT && BaseRequiresClamping == 1);
}
```

**`GroundClampingState`** — mutable interpolation state, modified by `TerrainQueryResolutionSystem`:

```csharp
[ComponentId(HrotComponentIds.GroundClampingState)]
public struct GroundClampingState
{
    public float TargetZOffset;        // Desired visual correction (sim Z → IG Z)
    public float CurrentZOffset;       // Lerped value applied this frame
    public float LastValidIgAltitude;  // Previous accepted IG hit; used for jump rejection
}
```

**`TerrainQueryBatchData`** — pre-allocated unmanaged singleton; zero GC, same pattern as `RaycastBatchData`:

```csharp
[ComponentId(HrotComponentIds.TerrainQueryBatchData)]
public struct TerrainQueryBatchData
{
    public int Count;
    public NativeArray<TerrainQueryRequest> Requests;
    public NativeArray<TerrainQueryResult>  Results;
}

public struct TerrainQueryRequest
{
    public Entity Entity;
    public float  QueryX;
    public float  QueryY;
    public float  ReferenceSimZ;   // Authoritative Sim altitude — used to bracket the cast
}

public struct TerrainQueryResult
{
    public float HitZ;
    public bool  HasHit;
}
```

---

#### 3.7.4  Three-Phase Execution Pipeline

**Phase A — `TerrainQuerySubmitSystem`** (`InputSystemGroup`)

Forward-predicts position by 1 frame using `SimVelocity` before submitting — because the terrain result arrives _next_ frame, querying the predicted position compensates for the 1-frame delay on moving vehicles:

```csharp
foreach (var entity in query.With<SimTransform>().With<SimVelocity>().With<GroundClampingConfig>())
{
    if (!config.IsClampingActive) continue;
    var predicted = tf.Position + vel.Linear * DeltaTime;
    batch.Requests[batch.Count++] = new TerrainQueryRequest
    {
        Entity = entity, QueryX = predicted.X, QueryY = predicted.Y,
        ReferenceSimZ = predicted.Z
    };
}
```

**Phase B — `TerrainQuerySolverSystem`** (background thread between Input and PostSim)

Issues terrain raycasts via `ITerrainProvider`. The cast bounds are `[ReferenceSimZ - 3m, ReferenceSimZ + 3m]` — a tight vertical bracket around the Sim's authoritative altitude. This makes the solver select the correct topological layer (bridge deck vs. road below) matching the Sim's intent, regardless of the number of mesh layers in the IG's LOD representation.

**Phase C — `TerrainQueryResolutionSystem`** (`PostSimulationSystemGroup`, `[UpdateAfter(DeadReckoningSyncSystem)]`)

Applies heuristics and updates `GroundClampingState`. Runs _after_ `DeadReckoningSyncSystem` so the horizontal position in `SimTransform` is already smoothed before the Z correction is overlaid:

```csharp
// Jump rejection heuristic — prevents snapping when the IG LOD hasn't opened a tunnel mesh yet
if (MathF.Abs(res.HitZ - state.LastValidIgAltitude) > 5.0f) continue;

state.LastValidIgAltitude = res.HitZ;
state.TargetZOffset       = res.HitZ - req.ReferenceSimZ;
```

---

#### 3.7.5  Visual Offset Application — `TransformSyncSystem` Modification

`SimTransform` is the shadow of the authoritative network state. It must not be overwritten with IG-corrected Z; doing so would corrupt the dead-reckoning baseline.

Instead, `TransformSyncSystem.SyncRemoteEntities` (existing system) is extended to smoothly lerp `CurrentZOffset` toward `TargetZOffset` and apply it only to the visual position:

```csharp
if (view.HasComponent<GroundClampingState>(entity))
{
    ref var clamp = ref view.GetComponentRW<GroundClampingState>(entity);
    // Smooth the offset to prevent pop-in when LODs swap
    clamp.CurrentZOffset = MathF.Lerp(clamp.CurrentZOffset, clamp.TargetZOffset, deltaTime * 5f);
    smoothed.Z = netTf.LastPosition.Z + clamp.CurrentZOffset;
}
cmd.SetComponent(entity, new SimTransform { Position = smoothed, Rotation = currentTf.Rotation });
```

The raw `SimTransform.Position.Z` (the Sim's authoritative value from the network) is preserved; only the visual render position has the offset applied.

---

#### 3.7.6  Modularity — `IgGroundClampingModule`

```csharp
public class IgGroundClampingModule : IModule
{
    private readonly ITerrainProvider _terrainProvider;

    public IgGroundClampingModule(ITerrainProvider terrainProvider)
        => _terrainProvider = terrainProvider;

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new TerrainQueryInitializationSystem());  // allocates singleton
        registry.RegisterSystem(new TerrainQuerySubmitSystem());
        registry.RegisterSystem(new TerrainQuerySolverSystem(_terrainProvider));
        registry.RegisterSystem(new TerrainQueryResolutionSystem());
    }
}
```

`ITerrainProvider` is an interface injected at construction time, allowing engine-specific implementations (`UnrealTerrainProvider`, `UnityTerrainProvider`, `FlatEarthTerrainProvider` for tests).

`NodeBootstrapper` installs the module only when the IG config flag `Requires3DClamping` is true:

```csharp
if (role is NodeRole.ImageGenerator or NodeRole.AllInOne && igConfig.Requires3DClamping)
    kernel.RegisterModule(new IgGroundClampingModule(igConfig.TerrainProvider));
```

A pure 2D tactical-map IG simply does not register the module. Zero overhead: the `TerrainQueryBatchData` singleton is never allocated, the three systems never run, and `TransformSyncSystem` skips the Z-offset branch because no entity has `GroundClampingState`.

---

#### 3.7.7  Dynamic Switching Flows

**Editor drag-and-drop (IOS / IG):**
- Drag start → publish `GroundClampingOverride { Mode = CLAMP_FORCE_OFF }` → entity floats at cursor altitude without jittering.
- On drop → publish `GroundClampingOverride { Mode = CLAMP_DEFAULT }` → clamping resumes and snaps entity to correct floor.

**Aircraft take-off / landing (SimHost flight dynamics):**
- Take-off: `FlightDynamicsSystem` detects positive Z velocity + gear retraction → publishes `GroundClampingOverride { Mode = CLAMP_FORCE_OFF }`. All 3D IGs stop clamping immediately.
- Landing: radar-altimeter detects touchdown → publishes `GroundClampingOverride { Mode = CLAMP_DEFAULT }`. IGs resume terrain queries.

Both flows are driven purely by the DDS topic; no coupling between `SimHost` nodes and the IG rendering pipeline.

---

### Phase 8 — Recording/Replay Module Architecture

**Goal:** Move `AsyncRecorder` (and its playback counterpart `PlaybackController`) ownership out of a monolithic orchestrator into dynamically installable `IModule` objects managed by `ModuleHostKernel`. Split the recording concern into a **Control Plane** (`EcsRecordReplayController` as factory/orchestrator) and a **Data Plane** (`RecordingModule` / `StoryRecorderModule` / `ReplayModule` as hot-path owners). Achieve zero-cost "not recording" behavior, concurrent per-story recording with isolated I/O pipelines, and ACID-safe lifecycle semantics inside the existing 2PC barrier.

**Target namespaces (see §2.5):**
- `RecordingModule`, `ReplayModule`, `StoryRecorderModule`, `RecordingConfiguration`, `StoryTag`, `StoryReplayTag` → **`FDP.Toolkit.Replay`** (generic; operate purely on ECS chunk memory, no Hrot domain knowledge)
- `RecorderSystem.EntityFilter` extension → **`Fdp.Kernel.FlightRecorder`** (additive, non-breaking)
- `EcsRecordReplayController` → **`Hrot.SimHost.Modules.Orchestration`** (binds generic recording mechanics to Hrot `IDsmHandler` / DSM commands)

---

#### 3.8.1 Motivation

**Current state (prior to Phase 8):**
- `AsyncRecorder` is owned directly by whatever module manages the recording concern, causing SRP violations, brittle lifetime management, and `if (isRecording)` guards on the 60 Hz hot path.
- Concurrent story recording is not possible without shared state or cross-module coupling.
- There is no clean boundary between "decide to record" (control plane) and "actually record" (data plane).

**Target state:**
- `EcsRecordReplayController` is a pure factory/orchestrator — it constructs typed `IModule` objects and routes them through `ModuleHostKernel`. It never directly touches `AsyncRecorder` or `PlaybackController`.
- Each `RecordingModule` / `StoryRecorderModule` / `ReplayModule` strictly owns its `AsyncRecorder` or `PlaybackController`. The 60 Hz `RecorderTickSystem` is physically added to (or removed from) the FDP scheduler when the module is installed/uninstalled — no runtime `if` guards, zero overhead when not recording.
- Multiple `StoryRecorderModule` instances may run concurrently with full isolation: distinct `AsyncRecorder` instances, distinct LZ4 background workers, distinct file streams, distinct `EntityQuery` predicates.

---

#### 3.8.2 Control Plane — `EcsRecordReplayController`

`EcsRecordReplayController` implements `IDsmHandler` and is registered with `ClusterSlave`. It acts exclusively as a **factory**: it constructs modules with the correct `RecordingConfiguration` context and delegates their lifecycle to `ModuleHostKernel`:

```csharp
// Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs
public class EcsRecordReplayController : IDsmHandler
{
    private readonly ModuleHostKernel _kernel;
    private readonly int _nodeId;
    private RecordingModule? _activeRecordingModule;
    private ReplayModule?    _activeReplayModule;
    private readonly Dictionary<Guid, StoryRecorderModule> _storyModules = new();

    // --- Global recording ---

    public async Task PrepareRecordingAsync(Guid drillId, string storageDirectory)
    {
        var config = new RecordingConfiguration
        {
            FilePath     = $"{storageDirectory}/{drillId}/node_{_nodeId}.fdp",
            EntityFilter = null,          // record all above MinRecordableId
            ExerciseId      = drillId,
        };
        _activeRecordingModule = new RecordingModule(config);
        await _kernel.InstallModuleAsync(_activeRecordingModule);
        // InstallModuleAsync → RecordingModule.Initialize() → opens AsyncRecorder
        // → registers RecorderTickSystem → rebuilds topological graph (off-path, 2PC barrier)
    }

    public async Task FinalizeRecordingAsync()
    {
        await _kernel.UninstallModuleAsync(_activeRecordingModule!);
        // UninstallModule → RecordingModule.Dispose() → AsyncRecorder.Dispose()
        // BLOCKING: flushes LZ4 buffers, writes MaxNetworkId, writes .meta.json
        _activeRecordingModule = null;
    }

    // --- Story recording ---

    public async Task StartEpisodeRecordingAsync(Guid storyId, string storageDir)
    {
        var config = new RecordingConfiguration
        {
            FilePath     = $"{storageDir}/stories/{storyId}_node{_nodeId}.fdp",
            EntityFilter = Query().With<StoryTag>().Build(),
            ExerciseId      = storyId,
        };
        var module = new StoryRecorderModule(config);
        _storyModules[storyId] = module;
        await _kernel.InstallModuleAsync(module);
    }

    public async Task StopEpisodeRecordingAsync(Guid storyId)
    {
        if (_storyModules.Remove(storyId, out var module))
            await _kernel.UninstallModuleAsync(module);
    }

    // --- Replay ---

    public async Task PrepareReplayAsync(Guid drillId, string storageDirectory)
    {
        _activeReplayModule = new ReplayModule(
            $"{storageDirectory}/{drillId}/node_{_nodeId}.fdp", _repo);
        await _kernel.InstallModuleAsync(_activeReplayModule);
    }

    public async Task TeardownReplayAsync()
    {
        await _kernel.UninstallModuleAsync(_activeReplayModule!);
        _activeReplayModule = null;
        // EntityRepository is preserved intact at the historical state —
        // PrepareRecordingAsync may immediately follow for a live-from-replay branch.
    }
}
```

---

#### 3.8.3 Data Plane — `RecordingModule`

`RecordingModule` is `IModule` + `IDisposable`. It strictly owns one `AsyncRecorder` and the `RecorderTickSystem` that runs it at 60 Hz:

```csharp
// Hrot.SimHost/Modules/Orchestration/RecordingModule.cs
public class RecordingModule : IModule, IDisposable
{
    private readonly RecordingConfiguration _config;
    private AsyncRecorder? _recorder;

    public RecordingModule(RecordingConfiguration config) => _config = config;

    public void RegisterSystems(ISystemRegistry registry)
    {
        _recorder = new AsyncRecorder(_config.FilePath);
        registry.RegisterSystem(new RecorderTickSystem(_recorder, _config.EntityFilter));
    }

    public void Dispose()
    {
        // Blocking: drains LZ4 front-buffer, writes RecordingMetadata (MaxNetworkId),
        // closes the .fdp stream. NodeOpStatus(Success) is not sent before this returns.
        _recorder?.Dispose();
        _recorder = null;
    }
}
```

The 60 Hz hot path in `RecorderTickSystem.Execute()`:

```csharp
void Execute(EntityRepository repo, uint prevTick)
{
    if (++_framesSinceKeyframe >= KEYFRAME_INTERVAL)   // default: 60 frames
    {
        _recorder.CaptureKeyframe(repo);
        _framesSinceKeyframe = 0;
    }
    else
    {
        _recorder.CaptureFrame(repo, prevTick);
        // Zero-allocation: raw memcpy to front-buffer; LZ4 on BG worker thread
    }
}
```

When `ModuleHostKernel.UninstallModuleAsync(module)` completes:
- `RecorderTickSystem` is **physically removed** from the topological graph (topology rebuild, off-path).
- `RecordingModule.Dispose()` is called synchronously — the ECS scheduler will never call `RecorderTickSystem.Execute()` again.
- Zero CPU overhead after uninstall: no `if (isRecording)` guards anywhere.

---

#### 3.8.4 Data Plane — `StoryRecorderModule`

`StoryRecorderModule` follows the identical `IModule` + `IDisposable` lifecycle but is constructed with an `EntityQuery` filter predicate. Multiple instances may run concurrently alongside the global `RecordingModule`:

- Each instance owns a **distinct** `AsyncRecorder` → distinct LZ4 background worker → distinct `.fdp` file stream. No shared I/O bottleneck, no mutex.
- `AsyncRecorder.CaptureFrame()` performs a raw `memcpy` read of `NativeChunkTable`. Concurrent read-only access by multiple recorder instances creates no race conditions.
- The `EntityFilter` predicate (e.g. `Query().With<StoryTag>().Build()`) is passed to `RecorderSystem` during `Initialize()`; entity IDs that do not satisfy it are skipped in the delta-frame loop.
- Uninstalling a specific `StoryRecorderModule` at `StopEpisode` flushes that story's buffers and closes its file handles without affecting any other running module.

---

#### 3.8.5 Data Plane — `ReplayModule`

`ReplayModule` is the symmetric counterpart for playback. It strictly owns one `PlaybackController` and registers a `PlaybackTickSystem`:

```csharp
// Hrot.SimHost/Modules/Orchestration/ReplayModule.cs
public class ReplayModule : IModule, IDisposable
{
    private PlaybackController? _playback;
    private readonly string _filePath;
    private readonly EntityRepository _repo;

    public void RegisterSystems(ISystemRegistry registry)
    {
        _playback = new PlaybackController(_filePath);
        // SchemaValidator runs inside ctor — throws InvalidDataException on struct drift
        registry.RegisterSystem(new PlaybackTickSystem(_playback, _repo));
    }

    public void Dispose() { _playback?.Dispose(); _playback = null; }

    // SysOp-coordinated heavy seek — delegated by EcsRecordReplayController
    public Task SeekToTimeAsync(long targetWallClockTicks) =>
        Task.Run(() => _playback!.SeekToWallClockTicks(_repo, targetWallClockTicks));
}
```

`PlaybackTickSystem` implements dual-strategy catch-up:
- **Strategy A (small gap, ≤3 frames):** Sequential `StepForward` loop — all deltas applied in-memory; intermediate frames never rendered.
- **Strategy B (large gap, TimeScale ≥ 4×):** Binary-search to nearest preceding keyframe, blast it into `NativeChunkTable` via `memcpy`, then apply ≤59 delta frames. Completes in ~5–15 ms regardless of jump magnitude.

---

#### 3.8.6 `RecordingConfiguration` — Initialization Contract

```csharp
// Hrot.SimHost/Modules/Orchestration/RecordingConfiguration.cs
public sealed class RecordingConfiguration
{
    /// Absolute path for the .fdp output file.
    public required string FilePath { get; init; }

    /// ECS entity filter predicate. null = record all entities above MinRecordableId.
    /// StoryRecorderModule injects Query().With<StoryTag>().Build() here.
    public EntityQuery? EntityFilter { get; init; }

    /// Drill or Story identifier embedded in the recording header.
    public required Guid ExerciseId { get; init; }
}
```

---

#### 3.8.7 `RecorderSystem.EntityFilter` — Minimal FDP Kernel Extension

The existing `RecorderSystem` in `FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs` gains a single optional predicate property. This is a **non-breaking, additive change**:

```csharp
// Addition to RecorderSystem.cs
public Predicate<int>? EntityFilter { get; set; } = null;
// In the inner delta-frame / keyframe capture loop:
//   if (EntityFilter != null && !EntityFilter(entityId)) continue;
```

`EcsRecordReplayController` never sets this directly. `StoryRecorderModule` translates the `RecordingConfiguration.EntityFilter` into a concrete `Predicate<int>` during `RegisterSystems()` and assigns it to the `RecorderTickSystem` instance it creates.

---

#### 3.8.8 `StoryTag` / `StoryReplayTag` ECS Components

```csharp
// FDP.Toolkit.Replay — IDs from GlobalComponentIds 20–49 toolkit block

[ComponentId(GlobalComponentIds.StoryTag)]
public struct StoryTag { public Guid StoryId; }

[ComponentId(GlobalComponentIds.StoryReplayTag)]
public struct StoryReplayTag { public Guid StoryId; public int OriginalEntityId; }
```

Entities spawned for a story receive `StoryTag`. The story recorder's filter query (`Query().With<StoryTag>().Build()`) provides logical isolation — only story entities enter that recorder's `AsyncRecorder`. During story replay, ghost entities receive `StoryReplayTag`; AI and physics systems skip any entity carrying it.

---

#### 3.8.9 NodeBootstrapper Integration

`EcsRecordReplayController` is instantiated during node startup and registered with `ClusterSlave`:

```csharp
// Inside NodeBootstrapper (Brain / AllInOne roles):
var recordReplayController = new EcsRecordReplayController(_kernel, _nodeId, _world);
drillSlave.RegisterHandler(recordReplayController);
```

`ModuleHostKernel.InstallModuleAsync` performs the topology rebuild off the hot path inside the existing 2PC barrier — no additional synchronisation is required.

---

---

### Phase 9 — `FDP.Framework.Runner` — Generic Application Lifecycle Toolkit

**Goal:** Extract the application orchestration infrastructure from `Hrot.ClusterRunner` into a standalone, project-agnostic toolkit (`FDP.Framework.Runner`). `Hrot.ClusterRunner` becomes a pure **composition root** that wires Hrot domain subsystems into the generic framework lifecycle.

**Target namespaces (see §2.5):**
- Generic lifecycle contracts and orchestration → **`FDP.Framework.Runner`**
- Concrete Hrot subsystems and entry point → **`Hrot.ClusterRunner`** (unchanged assembly)

#### 3.9.1  Candidates for `FDP.Framework.Runner`

| Class / Interface | Why it generalises |
|---|---|
| `ISubsystem` + `SubsystemConfig` | Pure contract: `Initialize()`, `Update(dt)`, `DrawWorld()`, `DrawUI()`, `Shutdown()`, `Headless`, `OwnWindow` |
| `IMapCameraProvider` | Domain-agnostic camera-snap interface |
| `SubsystemOrchestrator` (refactored) | Manages Raylib window + ImGui + 60 Hz loop; accepts `IEnumerable<ISubsystem>` via constructor |
| `WaitingRoomCoordinator` | DDS-based peer startup synchronisation — entirely generic |
| `RunnerConfiguration` (base flags) | CLI parsing for `--headless`, `--domain`, `--no-wait` |
| `HeadlessTestExecutor` | Background-thread orchestrator with JSON script engine |
| `TestScript`, `TestStep`, `TestReport`, `ITestActionHandler` | Core test model types |
| `WaitActionHandler`, `TickActionHandler`, `AssertAllActionHandler` | Universal test actions (no domain knowledge) |

#### 3.9.2  What Stays in `Hrot.ClusterRunner`

| Class | Why it stays |
|---|---|
| `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem` | Concrete adapters that bootstrap Hrot ECS worlds and UI panels |
| `Program.cs` entry point | Maps `--mode` CLI arg to concrete `ISubsystem` instances; injects them into `SubsystemOrchestrator` |
| `NodeBootstrapper` / `NodeRole` | Assemble the *internal* ECS module graph for a specific subsystem |
| `SpawnActionHandler`, `MoveActionHandler`, `AssertPositionActionHandler` | Domain-specific test actions that reference Hrot entity types |
| Hrot-specific runner CLI flags (`--mode`, `--role`, etc.) | Extend `RunnerConfiguration` base in project-local class |

#### 3.9.3  Refactoring `SubsystemOrchestrator` for FDP

Three areas of Hrot coupling must be removed:

**A. Hardcoded subsystem construction (`BuildSubsystems`):**
Remove the `BuildSubsystems` / `RunMode` pattern entirely from `SubsystemOrchestrator`. The orchestrator must receive `IEnumerable<ISubsystem>` via its constructor. `Hrot.ClusterRunner.Program` parses the CLI and injects the concrete list.

**B. Hardcoded UI theme colours (`PushSubsystemColors`):**
Add a `Vector4 TitleBarColor { get; }` property to `ISubsystem`. The orchestrator queries this before drawing instead of switching on hardcoded name strings (`"IG"`, `"SimHost"`, `"IOS"`).

**C. Hardcoded main menu bar buttons:**
`DrawMainMenuBar` currently hardcodes which subsystems to toggle. Replace with a loop over the injected `ISubsystem` list: any subsystem that also implements `IMapCameraProvider` gets a toggle button automatically.

#### 3.9.4  Refactoring `HeadlessTestExecutor` for FDP

1. Move `TestScript`, `TestStep`, `TestReport`, `ITestActionHandler` models to `FDP.Framework.Runner`.
2. Move `WaitActionHandler`, `TickActionHandler`, `AssertAllActionHandler` to `FDP.Framework.Runner` (no domain knowledge).
3. Keep `SpawnActionHandler`, `MoveActionHandler`, `AssertPositionActionHandler` in `Hrot.ClusterRunner`.
4. `HeadlessTestExecutor` already exposes `RegisterHandler(ITestActionHandler)`. During test startup `Hrot.ClusterRunner` calls this to inject Hrot-specific handlers.

**`TestMetricsCollector`** (`FDP.Framework.Runner.Testing`) — thread-safe collector for numeric metrics sampled during a headless test run. Introduced alongside `HeadlessTestExecutor` to allow test scripts to assert on aggregate performance data (min/max/avg/P95) after a run completes. Contains no `Hrot.*` references and is fully generic. Usage pattern:

```csharp
var metrics = new TestMetricsCollector();
// Record per-frame metrics during the simulation loop:
metrics.SampleWorld(world, frameMs: stopwatch.ElapsedMilliseconds);
metrics.RecordMetric("entity_count_peak", world.EntityCount);
// Assert after the run:
Assert.True(metrics.GetSummary("frame_duration_ms").P95 < 20.0, "P95 frame time must be under 20ms");
```

#### 3.9.5  How `Hrot.ClusterRunner` Uses the Toolkit

```csharp
// Hrot.ClusterRunner/Program.cs (sketch)
var subsystems = ParseMode(args) switch
{
    "simhost" => new ISubsystem[] { new SimHostSubsystem(config) },
    "ig"      => new ISubsystem[] { new IgSubsystem(config) },
    _         => new ISubsystem[] { new SimHostSubsystem(config), new IgSubsystem(config) }
};

var orchestrator = new SubsystemOrchestrator(subsystems);    // FDP.Framework.Runner
orchestrator.Initialize();

if (config.Headless)
{
    var executor = new HeadlessTestExecutor(orchestrator);   // FDP.Framework.Runner
    executor.RegisterHandler("spawn",   new SpawnActionHandler(_world));   // Hrot
    executor.RegisterHandler("moveto",  new MoveActionHandler(_world));    // Hrot
    executor.RegisterHandler("assertpos", new AssertPositionActionHandler(_world)); // Hrot
    executor.Run(TestScript.LoadFrom(config.TestScriptPath));
}
else
{
    orchestrator.Run();   // blocks until window close / Ctrl-C
}
```

---

## 5. Non-Goals (Out of Scope for MOD1)

- **AirKinematicsModule / HumanKinematicsModule** — the design defines the pattern; only `GroundKinematicsModule` is implemented in MOD1.
- **Full NavState removal** — `CarKinem.Core.NavState` remains the Muscle's internal movement target; the new `NavigationIntent` component is the clean public API surface. Internal refactoring of NavState usage inside CarKinematicsSystem is outside MOD1 scope.
- **BrainHsm registration** — `BrainHsm64` and `BrainHsm128` are already registered; their system wiring is maintained.
- **Radar / Thermal / Acoustic solver implementations** — Phase 6 defines the multi-modal pattern and creates the Visual pipeline end-to-end. Other modalities (Radar, Thermal, Acoustic) follow the same blueprint and are deferred to a subsequent workstream.
- **AllInOne no-network short-circuit optimisation** — in Phase 6 the translator packs are always wired; a future optimisation can bypass DDS serialisation within-process.
