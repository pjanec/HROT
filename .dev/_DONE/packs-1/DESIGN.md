# DESIGN.md — Logic Packs & Translator Packs Refactoring

## Background and Vision

The project is being restructured into **Logic Packs** and **Translator Packs**, following the
"Hrot editor" source architecture. The goal is strict separation of concerns:

- **Logic Packs** — Pure ECS modules (`IEcsModule` groupings) containing domain logic (CGF brain,
  SimHost muscle, Orchestration). Zero knowledge of CycloneDDS, network protocols, or distributed
  ID mapping.
- **Translator Packs** — Network boundary adapters that convert CycloneDDS messages to/from
  internal ECS state and the `FdpEventBus`. Pluggable and swappable (e.g. HROT NED → Bagira BDC
  SST protocol swap in the future).

Packs are **conceptual groupings of `IEcsModule`/`IEcsModuleSystem` instances** — they are not
new C# project assemblies.

### CQRS Boundary Contract

| Tier     | Node Role      | Owns                                                       | Emits                          |
|----------|----------------|------------------------------------------------------------|--------------------------------|
| Brain    | CGF / ExCon    | `BrainBlackboard`, `BrainHsm*`, `BehaviorState`, `NavigationIntent` | Intents (commands)  |
| Muscle   | SimHost        | `NavState`, `SimTransform`, `VehicleState`, `Health`       | Status/State events            |
| Network  | Translator Pack | `DdsReader<T>` / `DdsWriter<T>`, `NetworkEntityMap`       | External DDS messages          |

**Intents flow**: Brain → Translator Pack (Egress) → DDS → Translator Pack (Ingress) → Muscle  
**Status flows**: Muscle → Translator Pack (Egress) → DDS → Translator Pack (Ingress) → Brain

A node (runner subsystem) is assembled by **choosing which packs to install**. An "All-In-One"
editor installs both Brain and Muscle logic packs *without* Translator Packs; they communicate
purely via the internal shared ECS and `FdpEventBus`.

### Protocol Scope

This refactoring covers **HROT NED only**. Bagira (BDC SST) support is added in a future phase
by swapping Translator Packs.

---

## Phase 1: NavigationStatus CQRS — Fix RouteContextSystem

**Goal:** Decouple `RouteContextSystem` from `NavState` (a Muscle component) so it runs correctly
on Brain-only nodes in a distributed cluster.

### The Problem

`RouteContextSystem` (`Hrot.SimHost/Systems/Routing/RouteContextSystem.cs`) queries:

```
_vehicleQuery: .With<NavState>().With<BrainBlackboard>()
```

`NavState` is owned by the Muscle tier; `BrainBlackboard` is owned by the Brain tier. In a
distributed cluster, no single node holds both, so this system silently produces no output. It
reads `nav.Mode`, `nav.TrajectoryId`, and `nav.ProgressS` directly from `NavState`.

### The Fix: Route progress via NavigationStatus

We pipe the routing progress info from the Muscle back to the Brain using the existing CQRS
feedback channel `NavigationStatus`, which already crosses the network via translators.

### 1.A — Extend NavigationStatus with ProgressS

- Add `float ProgressS` to the `NavigationStatus` ECS struct
  (`FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/NavigationComponents.cs`).
- Add a matching `float ProgressS` field to the HROT NED DDS descriptor struct
  (`Hrot.NED/SimDescriptors.cs`, the `NavigationStatus` wire type).

### 1.B — Populate ProgressS on the Muscle Node

- `NavigationExecutionSystem` (`FDP.Toolkit.Navigation`) already translates physics states into
  the `NavigationStatus` CQRS component.
- Update it to read `NavState.ProgressS` and write the value to `NavigationStatus.ProgressS`.

### 1.C — Update Network Translators for ProgressS

- `NavigationStatusEgressTranslator.ScanAndPublish`: map ECS `NavigationStatus.ProgressS` → DDS
  `ProgressS` field.
- `NavigationStatusIngressTranslator.PollIngress`: map DDS `ProgressS` → ECS
  `NavigationStatus.ProgressS`.

### 1.D — Refactor RouteContextSystem

- **Fix the query**: remove `NavState`, add `NavigationIntent` and `NavigationStatus`.
- Read `mode` and `trajectoryId` from `NavigationIntent` (instead of `NavState`).
- Read route progress from `NavigationStatus.ProgressS` (instead of `NavState.ProgressS`).
- Pass `status.ProgressS` into the existing `ResolveSegmentIndex` logic to look up
  `ExtensionJson` from the `RoutePlan`, then write to `BrainBlackboard`.

---

## Phase 2: Module Realignment

**Goal:** Ensure every system executes on the node tier where its required components reside.

### 2.A — Relocate HsmDamageBridgeSystem (Brain tier)

`HsmDamageBridgeSystem` (`FDP.Toolkit.Behavior`) queries `BrainHsm128` and `BrainHsm64` — Brain
components. It is currently registered in `CombatModule` (`Hrot.SimHost`), which is deployed
to Muscle/AllInOne nodes and *excluded from Brain nodes*. In a distributed setup the system is
orphaned.

The data flow that enables the Brain to know about damage is already CQRS-correct:

```
Muscle: DamageCalculationSystem → EntityHitDamage (DDS) 
  → Brain: EntityHitDamageIngressTranslator → DamageAssessedEvent (bus) 
    → HealthApplicationSystem strips ActorCapabilities.CanMove on Brain node
      → HsmDamageBridgeSystem enqueues MobilityLost to Brain HSM
```

Fix: remove `HsmDamageBridgeSystem` from `CombatModule.RegisterSystems()`, add it to
`CognitiveRuntimeModule.RegisterSystems()` *before* the HSM tick systems so the event is
processed the same frame it is injected.

### 2.B — Delete ApcMobilityTriggerSystem; Absorb into HealthApplicationSystem

`ApcMobilityTriggerSystem` is a private inner class inside `UrbanCombatNewScenario`
(`FDP/Examples/Fdp.Examples.Scenarios/Integrated/UrbanCombatNewScenario.cs`). It queries both
`Health` (Muscle data) and `BrainHsm128` (Brain data) on the same entity. In a distributed
cluster no single node holds both, so the system silently produces no output (zero matching
entities). `ApcMobilitySystem` (`FDP/Examples/Fdp.Examples.UrbanCombat/Systems/ApcMobilitySystem.cs`)
has the same cross-domain query flaw.

**The correct fix is to delete both systems** and absorb their responsibility into
`HealthApplicationSystem` (`FDP.Toolkit.Combat`), which already consumes `DamageAssessedEvent`
on the Brain node and reduces HP. The missing behaviour is: strip `ActorCapabilities.CanMove`
whenever HP drops below maximum (non-lethal hit = mobility kill). Once `CanMove` is stripped,
the already-correct `HsmDamageBridgeSystem` chain handles the HSM transition automatically:

```
Muscle: DamageCalculationSystem → EntityHitDamage (DDS)
  → Brain: EntityHitDamageIngressTranslator → DamageAssessedEvent (bus)
    → HealthApplicationSystem: reduce HP; if HP < Max → strip CanMove   ← NEW
      → HsmDamageBridgeSystem: detects CanMove cleared → enqueues MobilityLost
        → HsmTickSystem: Cruising → Disabled
```

Steps:
1. **Update `HealthApplicationSystem`**: after reducing `Health.Current`, if
   `Health.Current < Health.Max` (non-lethal hit) and the entity has `ActorCapabilityState`, strip
   `ActorCapabilities.CanMove`.
2. **Delete `ApcMobilityTriggerSystem`** from `UrbanCombatNewScenario.cs` (remove the inner class
   and its registration in `BuildSystems()`).
3. **Delete `ApcMobilitySystem`** from `Fdp.Examples.UrbanCombat` and its registration in
   `HeadlessDemoApp.cs`.
4. Verify the `UrbanCombatNewScenario` integration test (`LatchApcHalted`) still passes via the
   new pure-Brain chain.

---

## Phase 3: Enforce the Intent Bus

**Goal:** Route *all* vehicle movement requests through `NavigationIntent` so the CQRS adapter
`NavigationIntentBridgeSystem` handles the sole Muscle translation path; retire the legacy
`Cmd*` event backdoor.

### The Problem

Three areas bypass the intent bus and directly mutate Muscle state:

1. `PersonalRouteAuthoringSystem` emits `CmdFollowTrajectory`
2. `SimHostVisualization` right-click "Brain-dead path" directly mutates `NavState` / calls
   `CmdFollowTrajectory`
3. `VehicleCommandSystem` processes all legacy `Cmd*` events and mutates `NavState`

### 3.A — Refactor PersonalRouteAuthoringSystem

Replace the `CmdFollowTrajectory` publish with writing a `NavigationIntent`:

```csharp
// NEW: Emitting a clean CQRS Intent
var intent = view.GetComponentRO<NavigationIntent>(vehicle);
intent.IntentId++;
intent.Mode = NavigationMode.FollowRoute;
intent.TrajectoryId = cache.TrajectoryId;
World.SetComponent(vehicle, intent);
```

The existing deferred-frame mechanism (`_pendingFollowCommands`) is preserved; only the
terminal action changes.

### 3.B — Refactor SimHostVisualization Right-Click

Replace the Brain-dead path to write `NavigationIntent`:

```csharp
var intent = repo.GetComponent<NavigationIntent>(entity);
intent.IntentId++;
intent.Mode = NavigationMode.DirectPoint;
intent.FinalDestination = pos;
intent.TargetSpeed = 15f;
intent.ArrivalRadius = 3.0f;
repo.SetComponent(entity, intent);
return;
```

### 3.C — Remove Legacy Commands from VehicleCommandSystem

Delete processing of these command events from `VehicleCommandSystem`:
- `CmdNavigateToPoint`
- `CmdFollowTrajectory`
- `CmdNavigateViaRoad`
- `CmdStop`
- `CmdSetSpeed`

Delete the corresponding `Cmd*` struct definitions from `CommandEvents.cs`.

`CmdSpawnVehicle`, `CmdCreateFormation`, `CmdJoinFormation`, `CmdLeaveFormation` are *out of
scope* for this task (they are not movement intents).

After this change, the network vocabulary between Brain and Muscle for movement is exactly:
- Egress Brain / Ingress Muscle: `NavigationIntent`
- Egress Muscle / Ingress Brain: `NavigationStatus`

---

## Phase 4: Anti-Corruption Layer — Pluggability Violations

**Goal:** Remove residual direct DDS/JSON coupling from Logic Pack systems. Network must be a
true plugin.

### 4.A — MissionControlRequestSystem Split

`MissionControlRequestSystem` (`Hrot.SimHost`) is a monolith: it owns `DdsReader<MissionControlRequest>`,
`DdsWriter<MissionControlAck>`, `DdsWriter<EntityMission>`, and parses JSON directly.

Split into three pieces:

1. **`MissionControlIngressTranslator`** (Translator Pack, `Hrot.SimHost.Network`/ existing
   network boundary namespace): polls `DdsReader<MissionControlRequest>`, deserializes the JSON
   parameters, publishes a strongly-typed `MissionControlIntent` event onto `FdpEventBus`.
2. **`MissionControlAckEgressTranslator`** (Translator Pack): consumes `MissionControlAckEvent`
   from `FdpEventBus`, writes `MissionControlAck` to DDS.
3. **`MissionControlExecutionSystem`** (Logic Pack): renamed/refactored from the existing class.
   Consumes `MissionControlIntent`, mutates `MissionPlanQueue`/`BehaviorState`, publishes
   `MissionControlAckEvent`. No DDS, no JSON. The `DdsWriter<EntityMission>` is *deleted* —
   the existing `EntityMissionEgressTranslator` already replicates `MissionPlanQueue` ECS
   changes over DDS automatically.

New pure domain events (add to `FDP.Toolkit.Behavior.Events` or `ClusterCqrsEvents.cs`):

```csharp
public class MissionControlIntent
{
    public Guid RequestId;
    public long TargetEntityId;
    public long BaseVersion;
    public MissionCommandUnion Payload;
}

public struct MissionControlAckEvent
{
    public Guid RequestId;
    public int ErrorCode;
    public string? ErrorMessage;
    public long NewVersion;
}
```

### 4.B — Extract Spawning Request Systems out of SimHostModule

`SimHostModule` is supposed to be the core Logic Pack for SimHost. However, it currently
contains **three** network-coupled classes that must not live in a Logic Pack:

1. `DdsCreateEntityRequestSource` (inner class) — wraps `DdsReader<CreateEntityRequest>`
2. `DdsCreateUpdateDeleteEntityAckSink` (inner class) — wraps `DdsWriter<CreateEntityAck>` etc.
3. Registration of `CreateEntityRequestSystem` and `DeleteEntityRequestSystem` directly inside
   `SimHostModule.RegisterSystems()`

If the engine runs offline (no network), `SimHostModule`'s constructor currently requires a
`DdsParticipant` just to build these DDS adapter objects.

Fix the packaging:

**Step 4.B.1 — Extract DDS adapters into a dedicated network module**  
Move `DdsCreateEntityRequestSource` and `DdsCreateUpdateDeleteEntityAckSink` out of
`SimHostModule.cs` and into a new class file `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`
(or into the existing `CycloneNetworkModule` / `SimHostNetworkTranslatorPack` boundary, aligned
with whatever network module owns the other DDS adapters).

**Step 4.B.2 — Move system registration to the network boundary module**  
Remove `_requestSystem` and `_deleteSystem` fields and their registration from
`SimHostModule.RegisterSystems()`. Register `CreateEntityRequestSystem` and
`DeleteEntityRequestSystem` inside the **network-boundary module** (e.g. the same module or
startup block that registers the GeoSpatial egress translators), passing the DDS adapters as
constructor arguments.

**Step 4.B.3 — Relocate UpdateEntityDescriptorRequestSystem**  
`UpdateEntityDescriptorRequestSystem` is an accepted **Command Ingress Translator** (it performs
direct DOD-friendly ECS unpacking from the DDS struct, avoiding heap allocations). No rewrite
of its internals is needed.
- Move the file from `Hrot.Map.Common/Systems/` to `Hrot.Map.Common/Replication/Ingress/`.
- Update the namespace from `Hrot.Map.Common.Systems` to `Hrot.Map.Common.Replication.Ingress`.
- Remove the unconditional registration from `SimHostApp.cs` (`_kernelGroup.AddSystem(...)`).
- Register it conditionally inside the same network-boundary module as the other spawning systems.

After this change, `SimHostModule` constructor must not accept `DdsParticipant` as a required
parameter — it should be instantiated with only domain-level dependencies.

### 4.C — Strip NetworkEntityMap from HitResolutionSystem and AimAndFireExecutor

`HitResolutionSystem` and `AimAndFireExecutor` currently accept `NetworkEntityMap` in their
constructors to stamp `long` net IDs onto outgoing events. Core physics/combat engines must not
know what a "Network ID" is.

Steps:

1. **Modify `DetonationNotification`** (in `FDP.Toolkit.Combat.Contracts`): replace the
   shooter/hit `long` NetworkEntityId fields with local ECS `Entity` handles.
2. **Modify `WeaponFireIntent`** (in `FDP.Toolkit.Combat.Events`): replace shooter/target `long`
   net IDs with local `Entity` handles.
3. **Refactor `HitResolutionSystem`**: remove the `NetworkEntityMap` overload. Always emit the
   cleansed `DetonationNotification` using local handles.
4. **Refactor `AimAndFireExecutor`**: remove `NetworkEntityMap` from constructor.
5. **Update `MunitionDetonationEgressTranslator`**: inject `NetworkEntityMap`, resolve
   local `Entity` handles to net IDs and publish the DDS packet.
6. **Update `WeaponFireIntentEgressTranslator`**: same — resolve local handles → net IDs before
   publishing the `WeaponFireRequest` DDS message.

---

## Phase 5: Orchestration Domain CQRS Cleanup

**Goal:** `ClusterMaster` and `ClusterUiCache` must operate exclusively via `FdpEventBus`. Remove
DDS fallback paths from orchestration domain classes.

No backward compatibility — DDS constructors are **deleted**, not deprecated.

### 5.A — Purify ClusterMaster

`ClusterMaster` (`Hrot.Orchestrator`) currently has three constructors: two DDS-based and one
bus-based. The DDS-based constructors start a `DdsIdAllocatorServer` background thread and
initialize seven DDS readers/writers.

Remove:
- `ClusterMaster(DdsParticipant)` and `ClusterMaster(DdsParticipant, ClusterConfiguration)` — deleted entirely
- All DDS fields: `_systemStateWriter`, `_heartbeatReader`, `_sysOpRequestReader`, `_sysOpStatusWriter`,
  `_nodeOpStatusReader`, `_nodeOpWriterCache`, `_nodeOpParticipant`, `_inventoryWriter`
- `_idAllocatorServer`, `_idServerCts`, `_idServerThread`
- `ProcessClusterOpRequests()` and `ProcessSingleClusterOpRequest()` (handled by `ClusterOpMasterTranslator`)
- DDS polling branches in `Tick()`, `IngestHeartbeats()`, `ConsumeNodeOpStatuses()`, `PublishOpStatus()`,
  `PublishClusterState()`, `FanOutNodeOp()`, `EjectNode()`, `Dispose()`

Consolidate `ConsumeNodeOpStatuses()` so the Live-from-Replay temporal interlock check and the Episode
2PC ACK check live only in the `_eventBus.ConsumeManaged<NodeOpCompletedEvent>()` loop.

Asset inventory egress: create `AssetInventoryUpdateEvent` (see §4.A new events section for the
`[EventId(9017)]` definition) and publish it from `ClusterMaster.PublishAssetInventory()` instead
of writing directly to DDS. `ClusterOpMasterTranslator` consumes this event and performs the
`_inventoryWriter.Write(...)`.

The resulting constructor signature:

```csharp
public ClusterMaster(FdpEventBus eventBus, ClusterConfiguration? config = null)
```

All fields of `FdpEventBus _eventBus` are non-nullable; no `null!` suppression.

### 5.B — Purify ClusterUiCache + Create OrchestrationObserverTranslator

`ClusterUiCache` (`Hrot.ClusterRunner`) currently holds seven `DdsReader<T>` fields. Remove all
of them. The class accepts only `FdpEventBus`:

```csharp
public ClusterUiCache(FdpEventBus bus, ITimeController? localTimeController = null)
```

Its `Update()` loop switches from `reader.Take()` blocks to `_bus.ConsumeManaged<T>()`:

| Old DDS topic              | New FdpEvent                   |
|----------------------------|--------------------------------|
| `SystemStateTopic`         | `SystemStateUpdateEvent`       |
| `AssetInventoryTopic`      | `AssetInventoryUpdateEvent`    |
| `NodeHeartbeat`            | `NodeHeartbeatEvent`           |
| `SwitchTimeModeWireDto`    | `SwitchTimeModeEvent`          |
| `ClusterOpStatus`          | `ClusterOpCompletedEvent`      |
| `NodeOpCommand`            | `ExecuteNodeOpIntent`          |
| `NodeOpStatus`             | `NodeOpCompletedEvent`         |

The `Process2PcNetworkTraffic()` method switches from `JsonDocument.Parse` to reading the
strongly-typed `DomainPayload` property of `ExecuteNodeOpIntent`.

Create **`OrchestrationObserverTranslator`** in `Hrot.Common/Orchestration/`:

This translator promiscuously sniffs all orchestration DDS topics and bridges them onto the
`FdpEventBus`. It holds all seven `DdsReader<T>` fields that were removed from `ClusterUiCache`.
Used by lightweight subsystems such as ExCon that do not need the full simulation kernel.

ExCon wiring (`ExConSubsystem.cs`):

```csharp
_orchestrationBus = new FdpEventBus();
_orchestrationObserverTranslator = new OrchestrationObserverTranslator(_participant, _orchestrationBus);
_uiCache = new ClusterUiCache(_orchestrationBus, _slaveSyncController);
```

`FdpEventBus` is an independent, lightweight double-buffered queue — it does not require an
`EntityRepository` or a `ModuleHostKernel`.

---

## Phase 6: ExCon Egress Anti-Corruption Layer

**Goal:** Every outbound command from ExCon UI panels and services travels via `FdpEventBus`
only. No `DdsWriter<T>` reference or `System.Text.Json` call inside UI or service classes.

Phase 5 correctly purified the *ingress* side of ExCon (what it *observes*: `ClusterUiCache`
reads from the bus). This phase completes the picture by purifying the *egress* side (what it
*commands*).

### 6.A — Eradicate DdsWriter from ClusterScenarioPanel

`ClusterScenarioPanel` (`Hrot.ClusterRunner/Services/ClusterScenarioPanel.cs`) currently has two
construction paths:

- **Orchestrator path**: `ClusterScenarioPanel(ClusterMaster, ClusterUiCache)` — fine; calls
  `_master.HandleClusterOpRequest()` directly.
- **ExCon / remote path**: `ClusterScenarioPanel(DdsWriter<ClusterOpRequest>, ClusterUiCache)` —
  **violation**: UI class holds a live DDS writer socket and builds raw JSON strings inline
  (e.g. `PayloadJson = $"{{\"TargetWallTicks\":{wallTicks}}}"`).

`ExConSubsystem` also creates `_sysOpWriter = new DdsWriter<ClusterOpRequest>(_participant)` and
passes it directly to `ClusterScenarioPanel`, embedding a DDS socket inside the UI stack.

**The Fix:**

1. **Define `ClusterOpIntent`** in `ClusterCqrsEvents.cs` — a strongly-typed bus event that
   carries the same fields as `ClusterOpRequest` but with no DDS attributes:
   ```csharp
   [EventId(9018)]
   [DataPolicy(DataPolicy.NoRecord)]
   public sealed class ClusterOpIntent
   {
       public Guid             RequestId;
       public ClusterOpType    OperationType;
       public object?          DomainPayload;  // typed payload, NOT raw JSON
   }
   ```
2. **Refactor `ClusterScenarioPanel`**: Remove the `DdsWriter<ClusterOpRequest>` constructor
   and field. The `FdpEventBus` is the sole egress channel for the remote path. `SendRequest()`
   becomes `_bus.PublishManaged(new ClusterOpIntent { ... })`. JSON serialization is **deleted
   from this class entirely**.
3. **Create `ClusterOpEgressTranslator`** in `Hrot.Common/Orchestration/` (same boundary
   package as `OrchestrationObserverTranslator`). It consumes `ClusterOpIntent` from the bus,
   serializes `DomainPayload` to JSON via `System.Text.Json`, and writes a `ClusterOpRequest`
   to DDS. This is the **only** class in ExCon's stack that may reference `CycloneDDS.Runtime`
   for command egress.
4. **Update `ExConSubsystem.cs`**: Replace the `_sysOpWriter` field with `_clusterOpEgressTranslator`.
   Wire it: `_clusterOpEgressTranslator = new ClusterOpEgressTranslator(_participant, _orchestrationBus)`.
   Remove the DDS writer injection into `ClusterScenarioPanel`.

### 6.B — Eradicate IDdsWriter from MissionEditorService

`MissionEditorService` (`Hrot.ExCon/Services/MissionEditorService.cs`) currently accepts
`IDdsWriter<MissionControlRequest>` in its constructor and calls `_requestWriter.Write(...)`
to send mission commands. This couples the ExCon mission-authoring service directly to the
DDS transport.

Note: `MissionControlIntent` is already defined in Phase 4.A as the strongly-typed event
that `MissionControlIngressTranslator` (SimHost side) consumes. The ExCon egress side is the
missing half.

**The Fix:**

1. **`MissionEditorService` switches to `FdpEventBus`**: Remove the
   `IDdsWriter<MissionControlRequest>` constructor parameter and field. Instead accept
   `FdpEventBus`. In `CommitMissionAsync`, publish a `MissionControlIntent` (the same event
   type defined in Phase 4.A) to the bus instead of calling `_requestWriter.Write(...)`.
2. **Create `MissionControlEgressTranslator`** in `Hrot.ExCon/Network/` (or the existing
   `Hrot.Common` network boundary folder). It consumes `MissionControlIntent` from the bus,
   serializes the parameters to JSON, and writes a `MissionControlRequest` DDS message.
3. **ACK ingress**: `MissionEditorService` currently implements `IIngressHandler` to receive
   `MissionControlAck`. After the refactor the ACK still arrives as a DDS message; an existing
   ingress translator must bridge it onto the bus as `MissionControlAckEvent`, and
   `MissionEditorService` reads it via `_bus.ConsumeManaged<MissionControlAckEvent>()` instead.
4. **Update all construction sites** (ExCon wiring, tests) to pass `FdpEventBus` instead of
   an `IDdsWriter<MissionControlRequest>`.

After Phase 6, neither `ClusterScenarioPanel`, `ExConLogic`, nor `MissionEditorService` may
contain a reference to `CycloneDDS.Runtime`, `DdsWriter`, or `System.Text.Json`.

---

---

## Phase 7: Remaining Combat and Perception ACL Leaks

**Goal:** Eliminate the final category of ACL violations: (a) a combat event that carries a
network ID instead of a local ECS handle, (b) a Muscle-tier perception system that mutates a
Brain-tier component, and (c) ECS components that embed raw DDS-generated structs causing a
transitive dependency on the network descriptor assembly inside Logic Packs.

### 7.A — Purify DamageAssessedEvent (Network ID Leak)

**The Problem**

`DamageAssessedEvent` (`FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs`) declares:

```csharp
public struct DamageAssessedEvent
{
    public long HitEntityId;   // ← network ID on an in-process event bus
    public float TotalDamage;
}
```

As a result, two pure Logic Pack systems must know what a network ID is:

- `DamageCalculationSystem` injects `NetworkEntityMap` to look up the hit entity's net ID and
  stamp it onto the event.
- `HealthApplicationSystem` injects `NetworkEntityMap` to resolve `long HitEntityId` back to
  an `Entity` handle before it can apply the damage.

The root cause is that the event was designed to serve both as a local ECS bus notification
and as a DDS-ready payload — violating single responsibility.

**The Fix**

1. **Change `DamageAssessedEvent.HitEntityId: long` → `HitEntity: Entity`** in
   `DetonationEvents.cs`.
2. **`DamageCalculationSystem`**: read `DetonationNotification.Target` (already an `Entity`
   handle) directly and set `HitEntity = target`. Remove `NetworkEntityMap` from constructor.
3. **`HealthApplicationSystem`**: read `evt.HitEntity` directly. Remove `NetworkEntityMap`
   from constructor.
4. **`DamageAssessedEgressTranslator`** (`Hrot.SimHost/Network/Egress/`): inject
   `NetworkEntityMap`. Resolve `evt.HitEntity` → `long` net ID before writing the DDS packet.
5. **`EntityHitDamageIngressTranslator`** (`Hrot.SimHost/Network/Ingress/`): already injects
   `NetworkEntityMap`. Change the event publish line:
   - Before: `HitEntityId = msg.HitEntityId`
   - After: `HitEntity = _entityMap.GetEntity(msg.HitEntityId)`
6. Update all tests that assert on `long HitEntityId` to assert on `Entity HitEntity`.

After this change `DamageAssessedEvent` carries only local ECS handles. Network ID translation
happens exclusively inside `DamageAssessedEgressTranslator` and `EntityHitDamageIngressTranslator`.

### 7.B — Fix AudioPerceptionSystem Split-Brain

**The Problem**

`AudioPerceptionSystem` (`FDP.Toolkit.Perception.Systems`) is a Muscle-tier system — it
performs physics-layer range and occlusion checks. However, it directly mutates `TargetMemory`,
which is a Brain-tier component:

```csharp
// Line 65 — Brain-tier guard inside a Muscle-tier system:
if (!World.HasComponent<TargetMemory>(listener)) continue;

// Lines 76-82 — direct mutation of Brain-tier data from a Muscle-tier system:
var mem = World.GetComponentRW<TargetMemory>(listener);
TargetMemory.AddOrUpdateTarget(ref mem.ValueRW, source, ...);
```

In a distributed cluster the entity exists on the Muscle node without a `TargetMemory`
component, so the guard causes silent no-ops and target-heard information is never delivered
to the Brain.

**The Fix**

1. **Define `TargetHeardEvent`** in `FDP.Toolkit.Perception.Events`:

   ```csharp
   [EventId(PerceptionConstants.TargetHeardEventId)]
   [StructLayout(LayoutKind.Sequential)]
   public struct TargetHeardEvent
   {
       public Entity  Listener;
       public int     SourceEntityIndex;
       public Vector3 Origin;
   }
   ```

2. **Add `TargetHeardEventId = 4004`** to `PerceptionConstants.cs`.

3. **Purify `AudioPerceptionSystem`**: remove the `HasComponent<TargetMemory>` guard and the
   `GetComponentRW<TargetMemory>` mutation block. Replace with:

   ```csharp
   _eventBus.Publish(new TargetHeardEvent
   {
       Listener          = listener,
       SourceEntityIndex = evt.SourceEntityIndex,
       Origin            = evt.Origin,
   });
   ```

   `AudioPerceptionSystem` must accept `FdpEventBus` in its constructor if it does not already.

4. **Extend `ThreatEvaluationSystem`** (`FDP.Toolkit.Perception.Systems`): add a consumption
   loop for `TargetHeardEvent` alongside the existing `TargetVisibleEvent` loop. Call
   `TargetMemory.AddOrUpdateTarget(ref mem, entityId: heardEvt.SourceEntityIndex,
   posX: heardEvt.Origin.X, posY: heardEvt.Origin.Y, scoreBoost: 20f,
   modality: SensorModality.Acoustic)`.

5. **Add network translators** for cross-node deployment:
   - **Perception Node (Egress)**: create `AudioTargetDetectedEgressTranslator` — catches
     `TargetHeardEvent` from the bus and writes a DDS `AudioTargetDetected` message.
   - **Brain Node (Ingress)**: create `AudioTargetDetectedIngressTranslator` — receives the DDS
     message and publishes `TargetHeardEvent` onto the Brain node’s local `FdpEventBus`, where
     `ThreatEvaluationSystem` picks it up seamlessly.

After this change `TargetMemory` is exclusively mutated by `ThreatEvaluationSystem`, which
is correctly placed on the Brain tier.

### 7.C — Remove DDS Structs from ECS Components (Mission Holders)

**The Problem**

Two ECS managed components embed raw DDS-generated structs directly as properties:

| Component | Location | Embedded DDS Struct |
|---|---|---|
| `EntityMissionHolder` | `Hrot.SimHost/Components/EntityMissionHolder.cs` | `Hrot.NED.Descriptors.EntityMission` |
| `IgMissionHolder` | `Hrot.IG/Components/IgMissionHolder.cs` | `Hrot.NED.Descriptors.EntityMission` |

Downstream Logic Pack consumers (`MissionAdapterSystem`, `MissionRenderLayer`) therefore
transitively depend on `Hrot.NED.Descriptors`, coupling them to the network descriptor assembly.
In a Logic-Pack-only node this creates an unnecessary dependency on the entire NED protocol
layer even when no network is present.

**The Fix**

1. **Define domain POCOs** in `FDP.Toolkit.Behavior.Components` (no `Hrot.NED` dependency):

   ```csharp
   public class DomainMissionTask
   {
       public Guid   TaskId;
       public string ExecutingEngine  = string.Empty;
       public string BehaviorId       = string.Empty;
       public string BehaviorParams   = string.Empty;
       // Trigger and state fields mapped as needed
   }

   public class DomainMissionPlan
   {
       public Guid                    ActiveTaskId;
       public List<DomainMissionTask> Tasks = new();
   }
   ```

   Create a unified managed ECS component in the same assembly:

   ```csharp
   [ComponentId(BehaviorComponentIds.ActiveMissionPlan)]
   public class ActiveMissionPlan
   {
       public DomainMissionPlan Plan { get; set; } = new();
   }
   ```

2. **Delete `EntityMissionHolder`** (`Hrot.SimHost.Components`) and **delete `IgMissionHolder`**
   (`Hrot.IG.Components`). Register the unified `ActiveMissionPlan` managed component in both
   `SimHostComponentRegistry` and `IgApplication` in their place.

3. **Update ingress translators** to map at the network boundary:
   - `EntityMissionIngressTranslator` (`Hrot.SimHost`): map `EntityMission` DDS struct →
     `DomainMissionPlan` POCO; write to the new `ActiveMissionPlan` component.
   - `IgMissionIngressTranslator` (`Hrot.IG`): same POCO mapping; write to `ActiveMissionPlan`.

4. **Update egress translators:**
   - `EntityMissionEgressTranslator` (`Hrot.SimHost`): read `ActiveMissionPlan.Plan`, map
     `DomainMissionPlan` → `EntityMission` DDS struct for publication.

5. **Update `MissionAdapterSystem`** (`Hrot.SimHost.Systems`): query `ActiveMissionPlan`
   instead of the deleted `EntityMissionHolder`. Access `plan.Plan.Tasks` and `plan.Plan.ActiveTaskId`.
   Evaluate removing `NetworkEntityMap` injection if it was only needed for DDS struct ID resolution.

6. **Update `MissionRenderLayer`** (`Hrot.IG.Systems`): query `ActiveMissionPlan` instead of
   the deleted `IgMissionHolder`; iterate `plan.Plan.Tasks` for waypoint rendering.

After this change `EntityMissionHolder` and `IgMissionHolder` no longer exist. The unified
`ActiveMissionPlan` component has no `using Hrot.NED.*` directive, and all Logic Pack consumers
are fully decoupled from the network descriptor assembly.

---

## Architectural Decisions

| Decision | Rationale |
|---|---|
| No backward-compatibility for DDS constructors | Clean break; existing callers are all internal |
| HROT NED only (Bagira later) | Scope control — Bagira added by swapping Translator Packs |
| Packs are conceptual groupings, not new assemblies | Avoids project proliferation; uses existing `IEcsModule` composition |
| `UpdateEntityDescriptorRequestSystem` kept as-is internally | DOD performance constraint prevents intermediate DTO allocation |
| `FdpEventBus` in ExCon uses no ECS kernel | Bus is an independent lightweight component |
| `EntityMissionEgressTranslator` replaces manual DDS push from MissionControl | Avoids duplicate replication paths |
| `ApcMobilityTriggerSystem` absorbed into `HealthApplicationSystem` | Eliminates cross-domain query; no new system type needed |
| `ClusterScenarioPanel` uses `FdpEventBus` not `DdsWriter` in remote path | UI classes must never hold network socket references |
| `MissionEditorService` uses bus not `IDdsWriter` | Service layer must not depend on transport layer |
| `DamageAssessedEvent.HitEntity` is always a local `Entity` handle | Network ID translation belongs exclusively at translator boundaries |
| `TargetMemory` exclusively mutated by `ThreatEvaluationSystem` | Only Brain-tier systems may mutate Brain-tier components; Muscle-tier perception systems publish events |
| ECS components in Logic Packs must never embed DDS-generated struct fields | Logic Pack consumers must compile and run without any `Hrot.NED` / `CycloneDDS` assembly reference |
