# Hrot.Network.NED

**Project path**: `Hrot/Network/Hrot.Network.NED/Hrot.Network.NED.csproj`
**Date**: 2026-05-23
**Target framework**: net8.0
**Assembly**: `Hrot.Network.NED`

---

## README Validation

**Status: Missing**

No `README.md` exists inside the project folder. All documentation is provided by this
architectural document and by the inline XML and block comments within the source files.

---

## Executive Overview

### What NED Stands For

**NED** stands for **Network Entity Distribution** (also referred to internally as
"Network Exchange Description"). It is the full-featured, production DDS network layer
for the HROT military/combat simulation system. NED defines the complete binary contract
between all HROT subsystems: Brain (AI/CGF), Muscle (SimHost physics), Image Generator
(IG/rendering), ExCon (Exercise Controller), and Runner (cluster orchestration).

### Purpose

NED is the primary inter-node communication protocol for HROT. Every significant
simulation capability -- entity lifecycle, spatial replication, mission control, weapon
fire, perception, pathfinding, map interaction, time control, and cluster orchestration --
is transported over DDS topics defined and translated in this project.

NED is implemented as a concrete realisation of `INetworkFactory` and `IReplicationModule`,
the same protocol-neutral contracts that are also satisfied by the lightweight BDC protocol.
Callers (e.g. `NodeBootstrapper`, `ClusterRunner`) select NED at configuration time and
receive the same interfaces; the protocol detail is invisible to higher-level code.

### NED vs BDC

NED and BDC both implement `INetworkFactory` / `IReplicationModule`. The difference is
scope:

| Capability                              | BDC           | NED            |
|-----------------------------------------|---------------|----------------|
| Entity lifecycle (spawn / destroy)      | Yes           | Yes            |
| World position replication              | Yes           | Yes            |
| Mission control (CQRS)                  | Defined stub  | Yes (full)     |
| Navigation intent / status              | No            | Yes            |
| Perception (sensor tracks)              | No            | Yes            |
| Pathfinding (ray/path batches)          | No            | Yes            |
| Weapon fire pipeline                    | No            | Yes            |
| Damage assessment pipeline              | No            | Yes            |
| Tactical intent (Brain-to-Brain)        | No            | Yes            |
| EQS area queries (Brain-Muscle)         | No            | Yes            |
| Map interaction (click, drag, select)   | No            | Yes            |
| ExCon ingress handlers                  | Stub          | Yes (full)     |
| CGF entity lifecycle adapters           | No            | Yes            |
| IG network adapter                      | No            | Yes            |
| Time control gateway                    | Stub          | Yes            |
| Cluster orchestration                   | No            | Yes            |
| ID allocator server                     | Sequential    | DDS-based      |
| DDS topic prefix isolation              | Yes (BDC_)    | No (global)    |
| Deferred ownership protocol             | No            | Yes            |
| Attribute patching (JSON + binary)      | No            | Yes            |
| Gizmo debug interaction                 | No            | Yes            |

BDC is suitable for minimal federation or lightweight IG tracking. NED is required for
all production multi-node simulation runs.

---

## Architecture

### Design Principles

1. **SST entity model** -- Entities have no single wire object. Instead, each entity is
   described by a set of independent DDS topics ("Descriptors") that share a common
   `EntityId` key. Entity existence is governed solely by `EntityMaster`. All other
   descriptors are optional facets.

2. **CQRS descriptor ownership** -- Ownership is granular per descriptor. The Brain node
   owns cognitive descriptors (`EntityMission`, `NavigationIntent`). The Muscle node owns
   physics descriptors (`WorldPos`, `NavigationStatus`). The last successful writer is the
   owner. Handover is coordinated via the `DeferredTakeOwnership` pre-genesis routing
   protocol.

3. **Translator pattern** -- All DDS I/O is handled by translator classes that implement
   `IDescriptorTranslator` or `INetworkTranslator`. Each translator owns its own
   `DdsReader<T>` / `DdsWriter<T>` instance and exposes `PollIngress` / `ScanAndPublish`
   methods driven by the ECS kernel tick.

4. **Role-driven composition** -- `NedReplicationModule` selects translator packs
   according to the node's `NodeRole` flags (`MuscleGround`, `ImageGenerator`, `Brain`,
   `AllInOne`). No translator is registered on a role that does not need it, keeping
   memory and CPU overhead proportional to what a node actually does.

5. **Factory pattern** -- `NedNetworkFactory` is the single entry point for all NED
   subsystems. Higher-level code calls `factory.CreateReplicationModule()`,
   `factory.CreateCommandGateway()`, etc. and receives the concrete NED implementations.

6. **Null-safe factory** -- Every factory method checks whether the `DdsParticipant` is
   null before constructing real translators. When null (headless test mode), it returns a
   null-object stub. This makes unit testing possible without a live DDS stack.

7. **IDL file partitioning** -- Each DDS struct or topic is tagged with `[DdsIdlFile]`
   to map it to the correct generated IDL file:
   - `hrot-common` -- shared geometric/identity primitives
   - `hrot-generic-desc` -- lifecycle and versioning descriptors
   - `hrot-generic-msgs` -- cross-cutting request/response messages
   - `hrot-sim-desc` -- spatial, damage, navigation, sensor, pathfinding descriptors
   - `hrot-map-desc` -- map overlays, routes, symbols, IG config
   - `hrot-map-msgs` -- map interaction events and commands
   - `hrot-missions-desc` -- mission plan and task structures
   - `hrot-missions-msgs` -- mission control requests and ACKs
   - `hrot-tactical-intent` -- Brain-to-Brain tactical intent
   - `hrot-eqs-msgs` -- EQS area-query batch messages
   - `hrot-sim-msgs` -- fire interaction events
   - `runner-msgs` -- subsystem status announcements

### Entity Lifecycle (SST Model)

The SST (Shared-State Topics) model governs how entities exist and are updated:

```
  EntityMaster (EntityId)         -- creates / destroys an entity slot
       |
       +-- EntityInfo             -- name, force, commander, tactical role
       |
       +-- WorldPos               -- position, orientation, velocity (Muscle-owned)
       |
       +-- EntityDamage           -- aggregate damage level (Muscle-owned)
       |
       +-- EntityMission          -- active mission plan (Brain-owned)
       |
       +-- NavigationIntent       -- CQRS nav command (Brain-owned)
       |
       +-- NavigationStatus       -- CQRS nav feedback (Muscle-owned)
       |
       +-- MapVisualOverlay       -- tactical graphic overlay (ExCon-owned)
       |
       +-- MapRoute               -- waypoint route (ExCon-owned)
       |
       +-- MapEntitySymbol        -- IG visual override (ExCon-owned)
       |
       +-- SensorConfig           -- observer sensor parameters (Brain-owned)
       |
       +-- SensorTrackState       -- detected/lost contact events (Muscle-owned)
```

---

## ASCII Block Diagrams

### Diagram 1 -- NED Layer in the HROT Cluster

```
+------------------+      DDS Domain 0       +------------------+
|   Brain / CGF    |<----------------------->|  SimHost/Muscle  |
|                  |                         |                  |
| EntityMission    |-----(dtEntityMission)-->| EntityMission    |
| NavigationIntent |---(dtNavigationIntent)->| NavigationIntent |
|                  |<--(dtNavigationStatus)--| NavigationStatus |
|                  |<----(dtWorldPos)--------| WorldPos         |
|                  |<-(dtDeferredTakeOwn.)->  |                  |
+------------------+                         +------------------+
         ^                                            ^
         |            DDS Domain 0                   |
         v                                            v
+------------------+                         +------------------+
|      ExCon       |<----------------------->| Image Generator  |
|                  |                         |                  |
| MapClickEvent    |<----(MapClickEvent)-----| MapClickEvent    |
| CreateEntityReq  |---(CreateEntityReq.)--->|                  |
| MapInteractConf  |---(MapInteractionConf)->| MapInteractConf  |
| ClusterOpRequest |---(ClusterOpRequest)--->|                  |
+------------------+                         +------------------+
         |
         v
+------------------+
|    Orchestrator  |
|   (Runner/Mgr)   |
| ClusterOpRequest |
| NodeOpCommand    |
| NodeHeartbeat    |
+------------------+
```

### Diagram 2 -- NedNetworkFactory and its Products

```
+------------------------------+
|      NedNetworkFactory       |
|  (implements INetworkFactory)|
+------------------------------+
     |          |         |         |          |          |
     v          v         v         v          v          v
+--------+ +--------+ +------+ +--------+ +-------+ +--------+
|  Ned   | |  Ned   | | Ned  | | Ned    | | Ned   | | Ned    |
| Replic.| | Command| | ExCon| | Time   | |Orchest| | Slave  |
| Module | | Gateway| | Egress| | Control| | Trans | | Orchest|
+--------+ +--------+ +------+ +--------+ +-------+ +--------+
     |
     v
+--------------------------------------------+
|          NedReplicationModule              |
|  (implements INedReplicationModule)        |
|                                            |
|  +------------------+  +--------------+   |
|  |  Shared          |  | Ghost        |   |
|  |  Translator Pack |  | Creation     |   |
|  |  (EntityMaster,  |  | System       |   |
|  |   WorldPos,      |  +--------------+   |
|  |   EntityInfo,    |                      |
|  |   EntityDamage)  |  +--------------+   |
|  +------------------+  | Deferred     |   |
|                         | Takeover     |   |
|  +------------------+  | System       |   |
|  | Kinematic Pack   |  +--------------+   |
|  | (MuscleGround /  |                      |
|  |  AllInOne only)  |  +--------------+   |
|  +------------------+  | Cleanup      |   |
|                         | System       |   |
|  +------------------+  +--------------+   |
|  | Cognitive Pack   |                      |
|  | (Brain / AllInOne|                      |
|  |  only)           |                      |
|  +------------------+                      |
+--------------------------------------------+
```

### Diagram 3 -- DeferredTakeOwnership Protocol (Pre-Genesis Routing)

```
 Brain Node                        Muscle Node
     |                                  |
     |-- DeferredTakeOwnership -------->|  (arrives BEFORE EntityMaster)
     |   { EntityId, Grants:            |
     |     [(dtWorldPos, MuscleNodeId), |
     |      (dtNavigationStatus, M.N.)] }
     |                                  |
     |                                  |  DeferredTakeOwnershipIngressTranslator
     |                                  |  creates bare Ghost, attaches
     |                                  |  PendingAuthorityGrants component
     |                                  |
     |-- EntityMaster ----------------->|
     |-- WorldPos (initial) ----------->|  GhostPromotionSystem promotes ghost
     |                                  |  to Constructing state
     |                                  |
     |                                  |  DeferredTakeoverSystem fires:
     |                                  |  - Claims SetAuthority(dtWorldPos)
     |                                  |  - Claims SetAuthority(dtNavStatus)
     |                                  |  - Strips PendingAuthorityGrants
     |                                  |  - Publishes OwnershipUpdate events
     |<-- OwnershipUpdate --------------|
     |   Brain's OwnershipIngressSystem |
     |   drops its authority bits       |
     |                                  |
```

### Diagram 4 -- Weapon Fire Pipeline (Brain -> Muscle -> IG)

```
 Brain Node           Muscle Node           IG Node
     |                     |                    |
     |--WeaponFireRequest-->|                    |
     |  { ShooterId,        |                    |
     |    TargetId,         |  WeaponFireRequest |
     |    WeaponIndex }     |  IngressTranslator |
     |                      |  publishes local   |
     |                      |  WeaponFireIntent  |
     |                      |  on ECS bus        |
     |                      |                    |
     |                      |--WeaponFire------->|
     |                      |  { ShooterId,      |  WeaponFireIngressTranslator
     |                      |    TargetId }      |  triggers muzzle-flash
     |                      |                    |
     |                      |--MunitionDetonat-->|
     |                      |  { HitEntityId,    |  MunitionDetonation
     |                      |    HitX/Y/Z }      |  IngressTranslator
     |                      |                    |  triggers explosion FX
     |                      |                    |
     |                      |--EntityHitDamage-->|
     |                      |  { HitEntityId,    |
     |                      |    TotalDamage }   |
     |                      |                    |
```

### Diagram 5 -- Attribute Patching Pipeline (ATTR2)

```
  ExCon / Tool                    SimHost (Muscle)
      |                                  |
      |--UpdateEntityAttributeRequest--->|
      |  { EntityId,                     |
      |    AttributePatchJson or         |
      |    AttributeRecords (binary) }   |
      |                                  |
      |                          UpdateEntityAttributeRequestSystem
      |                          polls DDS reader each Input phase
      |                                  |
      |                          JsonAttributeCompiler (JSON path)
      |                          or BinaryInterpreter (binary path)
      |                          applies mutations to live ECS
      |                          components via EcsPatchContext
      |                                  |
      |                          EcsPatchContext.FlushDirtyMarks()
      |                          triggers targeted egress for changed
      |                          components (bypasses coarse chunk tick)
      |                                  |
      |<--CreateUpdateDeleteEntityAck----|
      |  (only if RequireAck=true AND    |
      |   at least one mutation applied) |
      |                                  |
```

---

## Source Structure

### Root-level files

| File | Namespace | Contents |
|------|-----------|----------|
| `AssemblyInfo.cs` | (global) | `[InternalsVisibleTo]` grants for test projects |
| `Common.cs` | `Hrot.NED.Common` | Shared wire primitives: `NodeId`, `GeoPoint`, `EulerOri`, `AngularVector`, `EulerRate` |
| `GenericPrimitives.cs` | `Hrot.NED.Messages` | Vector primitives: `Vec3f`, `Vec3d`, `Vec4f` |
| `GenericMessages.cs` | `Hrot.NED.Messages` | `OwnershipUpdate`, `AttributeValueType`, `AttributeValueUnion`, `AttributeRecord`, `CreateEntityRequest` and related request/response types |
| `GenericDescriptors.cs` | `Hrot.NED.Descriptors` | `DisTypeStruct`, `EntityMaster`, `eForceIdentifier`, `eTacticalDesignation`, `EntityInfo`, `DescriptorOptimisticLock` |
| `AllDescriptors.cs` | `Hrot.NED.Descriptors` | `EDescriptorType` enum (all known descriptor ordinals) and `EntityDescriptorUnion` (discriminated union used in `CreateEntityRequest`) |
| `SimDescriptors.cs` | `Hrot.NED.Descriptors` | `WorldPos`, `EntityDamage`, `ENavigationMode`, `ENavigationResult`, `NavigationIntent`, `NavigationStatus`, `RelativeVector3`, `DdsRaycastRequest/Hit`, `RaycastRequestBatch/ResponseBatch`, `SensorConfig`, `DdsTrackedTarget`, `SensorTargets`, `SensorTrackState`, `DdsPathRequest/Result`, `PathRequestBatch/ResponseBatch`, `GroundClampingOverride` |
| `MapDescriptors.cs` | `Hrot.NED.Descriptors` | `MapEntitySymbol`, `PersistenceMode`, `MapVisualOverlay`, `Waypoint`, `MapRoute`, `MapInteractionConfig`, `MapConfigStatus`, `IGCapabilitiesAnnounce` |
| `MapMessages.cs` | `Hrot.NED.Messages` | `EEntitySymbolPart`, `MapObjectRef`, `MapClickEvent`, `DragState`, `DragEvent`, `SelectionChangedEvent`, `CommandType`, `MapCommandRequest`, `MapCommandAck`, `ContextActionsUpdate`, `ContextActionInvoked`, `ContextMenuRequest`, `CreateUpdateDeleteEntityAck` |
| `MissionDescriptors.cs` | `Hrot.NED.Descriptors` | `eTaskState`, `MissionTrigger`, `MissionTask`, `MissionPlan`, `EntityMission` |
| `MissionMessages.cs` | `Hrot.NED.Messages` | `eMissionCommandType`, `MissionCommandUnion`, `MissionControlRequest`, `MissionControlAck` |
| `FireInteractionMessages.cs` | `Hrot.NED.Messages` | `FireInteractionEvent`, `WeaponFireRequest`, `WeaponFire`, `MunitionDetonation`, `EntityHitDamage` |
| `TacticalIntentMessages.cs` | `Hrot.NED.Messages` | `TacticalIntentRequest` |

### Attributes/ -- Attribute Patching Sub-System

| File | Class | Purpose |
|------|-------|---------|
| `AttributeCompilerFactory.cs` | `AttributeCompilerFactory` (static) | Builds the application-wide `JsonAttributeCompiler` with `Name`, `Affiliation`, `GeoPosition.Latitude/Longitude/Altitude`, and `Heading` route registrations |
| `EntityAttributeSchemaPublisherSystem.cs` | `EntityAttributeSchemaPublisherSystem` | ECS system (`BeforeSync` phase) that publishes the attribute schema JSON once at startup via the `EntityAttributeSchema` DDS topic |
| `EntityDataAttributeInstaller.cs` | `EntityDataAttributeInstaller` | `IBinaryAttributeInstaller<AttributeRecord>` for routing `Name` and `ForceId` binary records to `EntityInfo` ECS component |
| `SimTransformAttributeInstaller.cs` | `SimTransformAttributeInstaller` | `IBinaryAttributeInstaller<AttributeRecord>` for routing `GeoLat/Lon/Alt` binary attribute records to `SimTransform` ECS component; uses scratchpad accumulation + deferred `ToCartesian` flush |

### CGF/ -- Computer Generated Forces Adapters

| File | Class | Purpose |
|------|-------|---------|
| `NedCgfEntityLifecycleAdapters.cs` | `NedEntityCreationRequestSource` | DDS reader for `CreateEntityRequest`; converts wire descriptors to neutral `EntityCreationRequest` DTO |
| | `NedEntityDeletionRequestSource` | DDS reader for `DeleteEntityRequest` |
| | `NedEntityAckSink` | DDS writer for `CreateUpdateDeleteEntityAck` |

### Commands/ -- Command Gateway

| File | Class | Purpose |
|------|-------|---------|
| `NedCommandGateway.cs` | `INedCommandGateway` | Abstraction interface for test injection |
| | `NedCommandGateway` | Full DDS implementation; provides async `CreateEntityAsync`, async `SendMissionControlRequestAsync`, and fire-and-forget `SendUpdateDescriptor` / attribute update |

### ExCon/ -- Exercise Controller Support

| File | Class | Purpose |
|------|-------|---------|
| `NedExConIngressTranslators.cs` | `NedMapClickIngressHandler` | Reads `MapClickEvent` DDS samples; enqueues `MapClickEventDto` via callback |
| | `NedSelectionChangedIngressHandler` | Reads `SelectionChangedEvent` samples |
| | `NedEntityLifecycleAckIngressHandler` | Reads `CreateUpdateDeleteEntityAck` samples |
| | `NedMapCommandAckIngressHandler` | Reads `MapCommandAck` samples |
| `NedExConEgressWriters.cs` | `NedExConEgressWriters` | Implements `IExConEgressWriters`; owns writers for `MapInteractionConfig`, `CreateEntityRequest`, `MapCommandRequest`, `DeleteEntityRequest`, `ContextActionsUpdate` |
| `NedTimeControlGateway.cs` | `NedTimeControlGateway` | Implements `ITimeControlGateway`; publishes `ClusterOpRequest` for pause/resume/step/time-scale operations |
| `NedMissionHelper.cs` | `NedMissionHelper` (static, internal) | Converts between `Hrot.Core.Mission.MissionPlan` and NED wire `MissionPlan`/`MissionTask`/`MissionTrigger` |
| `NedTranslationHelper.cs` | `NedTranslationHelper` (static, internal) | Builds `EntityDescriptorUnion` list for create-entity requests; translates `MissionControlCommand` to `MissionControlRequest` |

### Factory/ -- NED Factory and Orchestration

| File | Class | Purpose |
|------|-------|---------|
| `NedNetworkFactory.cs` | `NedNetworkFactory` | Implements `INetworkFactory`; single entry point for all NED subsystem construction |
| `NedOrchestrationTranslator.cs` | `NedOrchestrationTranslator` (internal) | Master-side orchestration translator: heartbeat bridge, `ClusterOpMasterTranslator`, `NodeOpMasterTranslator` |
| `NedSlaveOrchestrationTranslator.cs` | `NedSlaveOrchestrationTranslator` (internal) | Slave-side orchestration: `NodeOpSlaveTranslator` + `ClusterOpEgressTranslator` |
| `NedOrchestrationObserver.cs` | `NedOrchestrationObserver` (internal) | Wraps `OrchestrationObserverTranslator` |
| `NedMasterTimeTranslators.cs` | `NedMasterTimeTranslators` (internal) | Groups `SwitchTimeModeDescriptorTranslator`, `MasterLockstepTranslator`, `MasterTimeSyncTranslator` |
| `HostedIdAllocatorServer.cs` | `HostedIdAllocatorServer` (internal) | Background-thread wrapper around `DdsIdAllocatorServer` with clean `Dispose` |

### Gizmos/ -- Debug/Gizmo Interaction

| File | Class | Purpose |
|------|-------|---------|
| `GizmoTranslatorPack.cs` | `GizmoTranslatorPack` (static) | Factory for `GizmoInteractionIngressTranslator` and `GizmoInteractionEgressTranslator` |
| `GizmoInteractionIngressTranslator.cs` | `GizmoInteractionIngressTranslator` | Receives `GizmoInteractionBatch` from DDS; publishes to local event bus |
| `GizmoInteractionEgressTranslator.cs` | `GizmoInteractionEgressTranslator` | Sends local `GizmoInteractionBatch` events to DDS |
| `DebugPrimitivesIngressTranslator.cs` | `DebugPrimitivesIngressTranslator` | Receives `DebugPrimitivesBatch` (debug shapes) from DDS |

### Helpers/ -- Shared Helpers

| File | Class | Purpose |
|------|-------|---------|
| `MissionTriggerHelper.cs` | `MissionTriggerHelper` (static) | Resolves DDS trigger strings (`"TimerElapsed"`, `"BehaviorFinished"`, etc.) to ECS `MissionTrigger` enum values and numeric parameters |

### IG/ -- Image Generator Support

| File | Class | Purpose |
|------|-------|---------|
| `NedIgNetworkAdapter.cs` | `NedIgNetworkAdapter` | Implements `IIgNetworkAdapter`; owns all IG DDS writers (`MapClickEvent`, `SelectionChangedEvent`, `MapCommandAck`, `ContextMenuRequest`) and readers (`MapInteractionConfig`, `MapCommandRequest`, `CreateUpdateDeleteEntityAck`) |
| `NedIgTranslators.cs` | `NedIgTranslators` | Implements `IIgTranslators`; creates IG ingress translators for missions, ground clamping, audio target detected, weapon fire, munition detonation, context menu updates |
| `IgMissionIngressTranslator.cs` | `IgMissionIngressTranslator` | Receives `EntityMission` DDS samples for IG ghost entities |
| `WeaponFireIngressTranslator.cs` | `WeaponFireIngressTranslator` | Receives `WeaponFire` DDS samples; triggers IG muzzle-flash effect |
| `GroundClampingOverrideTranslator.cs` | `GroundClampingOverrideTranslator` | Receives `GroundClampingOverride` DDS samples |
| `AudioTargetDetectedIngressTranslator.cs` | `AudioTargetDetectedIngressTranslator` | Receives `AudioTargetDetected` DDS samples (IG sound events) |
| `ContextActionsUpdateTranslator.cs` | `ContextActionsUpdateTranslator` | Receives `ContextActionsUpdate` DDS samples; populates IG context menu |

### Infrastructure/ -- Node Builder Integration

| File | Class | Purpose |
|------|-------|---------|
| `HrotNodeBuilderReplicationExtensions.cs` | `HrotNodeBuilderWithReplication` | Fluent builder that adds `WithReplication(role)` to `HrotNodeBuilder`; constructs both `HrotNodeContext` and `NedReplicationModule` in one `Build()` call |
| | `HrotNodeBuilderReplicationExtensions` (static) | Extension methods `WithReplication(role)` and `BindReplicationParticipant(...)` |

### Messages/ -- Additional Wire Messages

| File | Contents |
|------|----------|
| `DeferredTakeOwnership.cs` | `DescriptorOwnerEntry`, `DeferredTakeOwnership` (pre-genesis routing table message) |
| `AreaQueryMessages.cs` | `DdsAreaQueryRequest`, `DdsAreaQueryResponse`, `AreaQueryRequestBatch`, `AreaQueryResponseBatch` |

### Replication/ -- Replication Module

| File | Class | Purpose |
|------|-------|---------|
| `NedReplicationModule.cs` | `NedReplicationModule` | Implements `INedReplicationModule`; role-driven composition of translator packs, ghost lifecycle systems, deferred takeover, cleanup |

### Routing/ -- Ownership Distribution

| File | Class | Purpose |
|------|-------|---------|
| `IClusterStateCache.cs` | `IClusterStateCache` | Abstraction for querying peer node health; `NodeCapability` snapshot class |
| `SimpleClusterStateCache.cs` | `SimpleClusterStateCache` | Thread-safe, dictionary-backed implementation; O(1) least-loaded-node query |
| `BrainMuscleOwnershipStrategy.cs` | `BrainMuscleOwnershipStrategy` | `IOwnershipDistributionStrategy` that routes `dtWorldPos` and `dtNavigationStatus` to the least-loaded MuscleGround node |

### Runner/ -- Subsystem Status

| File | Class | Purpose |
|------|-------|---------|
| `SubsystemStatusAnnounce.cs` | `SubsystemStatusAnnounce` (in `Hrot.DDS.DataModel.Runner`) | DDS topic struct for subsystem presence/readiness announcements (used by the Waiting Room protocol) |

### SimHost/ -- SimHost-Specific Translators

| File | Class | Purpose |
|------|-------|---------|
| `NedSimHostMissionSender.cs` | `NedSimHostMissionSender` | Implements `ISimHostMissionSender`; sends mission control ACKs and `EntityMission` descriptor updates |
| `NedSimHostAuxiliaryTranslators.cs` | `NedSimHostAuxiliaryTranslators` | Bundles SimHost-specific translators not covered by the replication module: fire interaction, damage, IG ground clamping writer |
| `NedSimHostPathfindingTranslators.cs` | `NedSimHostPathfindingTranslators` | Implements `ISimHostPathfindingTranslators`; raycast and path batch reader/writer pairs |
| `NedSimHostPerceptionTranslators.cs` | `NedSimHostPerceptionTranslators` | Implements `ISimHostPerceptionTranslators`; sensor config, sensor tracks, area queries |
| `SimHostAuxiliaryTranslatorPack.cs` | `SimHostAuxiliaryTranslatorPack` | Factory for auxiliary translator instances |
| `SimPathfindingTranslatorPack.cs` | `SimPathfindingTranslatorPack` | Factory for pathfinding translator pairs |
| `SimPerceptionTranslatorPack.cs` | `SimPerceptionTranslatorPack` | Factory for perception translator pairs |
| `BrainPathfindingTranslatorPack.cs` | `BrainPathfindingTranslatorPack` | Pathfinding translators from the Brain node perspective |
| `BrainPerceptionTranslatorPack.cs` | `BrainPerceptionTranslatorPack` | Perception translators from the Brain node perspective |
| Various `*IngressTranslator.cs` / `*EgressTranslator.cs` | -- | Individual translators for: weapon fire request, weapon fire notification, weapon fire intent, tactical intent, munition detonation, mission control, entity hit damage, damage assessed, audio target detected, area queries |

### Systems/ -- ECS Systems

| File | Class | Purpose |
|------|-------|---------|
| `DeferredTakeoverSystem.cs` | `DeferredTakeoverSystem` | `BeforeSync` ECS system; executes authority handover for entities with `PendingAuthorityGrants` that have reached `Constructing` state |
| `UpdateEntityAttributeRequestSystem.cs` | `UpdateEntityAttributeRequestSystem` | `Input` phase ECS system; applies JSON and binary attribute patches to live ECS components |
| `IUpdateEntityAttributeRequestSource.cs` | `IUpdateEntityAttributeRequestSource` | Abstraction for the attribute request DDS reader (enables unit testing) |
| `IUpdateEntityAttributeAckSink.cs` | `IUpdateEntityAttributeAckSink` | Abstraction for the attribute ACK DDS writer (enables unit testing) |

### Translators/ -- Cross-Cutting Translators

| File | Class | Purpose |
|------|-------|---------|
| `CognitiveTranslatorPack.cs` | `CognitiveTranslatorPack` (static) | Factory for Brain-side translators: `NavigationIntentEgressTranslator`, `EntityMissionEgressTranslator`, `EntityMissionIngressTranslator`, `NavigationStatusIngressTranslator` |
| `DeferredTakeOwnershipEgressTranslator.cs` | `DeferredTakeOwnershipEgressTranslator` | Egress-only; publishes `DeferredTakeOwnership` samples from bus events before `EntityMaster` |
| `DeferredTakeOwnershipIngressTranslator.cs` | `DeferredTakeOwnershipIngressTranslator` | Ingress-only; reads `DeferredTakeOwnership` samples and attaches `PendingAuthorityGrants` to pre-ghost entities |

---

## Public API Reference

### NedNetworkFactory

```csharp
public sealed class NedNetworkFactory : INetworkFactory
{
    // Constructor
    public NedNetworkFactory(
        DdsParticipant?        participant,
        NetworkEntityMap       entityMap,
        IGeographicTransform   geoTransform,
        FdpEventBus            eventBus,
        int                    localNodeId,
        NodeRole               role,
        ITkbDatabase?          tkbDb             = null,
        EntityLifecycleModule? lifecycleModule   = null,
        BehaviorRegistry?      behaviorRegistry  = null,
        FdpEventBus?           worldBus          = null);

    // Properties
    public DdsParticipant? Participant { get; }

    // INetworkFactory implementation
    public IReplicationModule                    CreateReplicationModule();
    public ICommandGateway                       CreateCommandGateway();
    public IExConEgressWriters                   CreateExConEgressWriters();
    public ITimeControlGateway                   CreateTimeControlGateway();
    public ISimHostMissionSender                 CreateSimHostMissionSender();
    public ISimHostAuxiliaryTranslators          CreateSimHostAuxiliaryTranslators();
    public IReadOnlyList<IEcsModuleSystem>       CreateSimHostAttributeUpdateSystems();
    public ISimHostPathfindingTranslators        CreateSimHostPathfindingTranslators(
        TrajectoryPoolManager? trajectoryPool = null);
    public ISimHostPerceptionTranslators         CreateSimHostPerceptionTranslators(
        GhostCreationSystem? ghostCreationSystem = null);
    public IIgTranslators                        CreateIgTranslators();
    public IIgNetworkAdapter                     CreateIgNetworkAdapter(
        DdsParticipant? participant, long nodeId = 0);
    public IOrchestrationTranslator              CreateOrchestratorTranslators();
    public ISlaveOrchestrationTranslator         CreateSlaveOrchestratorTranslators(int nodeId);
    public IOrchestrationObserver                CreateOrchestrationObserver();
    public IMasterTimeTranslators                CreateMasterTimeTranslators();
    public IDisposable                           CreateIdAllocatorServer();
    public IEnumerable<IIngressHandler>          CreateExConIngressHandlers(
        DdsParticipant? participant,
        long localNodeId,
        IDerRepo repo,
        Action<MapClickEventDto>         onMapClick,
        Action<SelectionChangedEventDto> onSelectionChanged,
        Action<EntityLifecycleAckDto>    onEntityLifecycleAck,
        Action<MapCommandAckDto>         onMapCommandAck);
}
```

### NedReplicationModule

```csharp
public sealed class NedReplicationModule : INedReplicationModule
{
    // Ctor (excerpt of key params)
    public NedReplicationModule(
        DdsParticipant?       participant,
        NodeRole              role,
        NetworkEntityMap      entityMap,
        IGeographicTransform  geoTransform,
        FdpEventBus           eventBus,
        int                   localNodeId,
        int                   domainId,
        BehaviorRegistry?     behaviorRegistry  = null,
        ITkbDatabase?         tkbDb             = null,
        EntityLifecycleModule? lifecycleModule  = null,
        IReadOnlyList<ITkbEntityTranslator>? tkbEntityTranslators = null);

    // Properties
    public string                        Name { get; }  // "NedReplication"
    public ExecutionPolicy               Policy { get; }
    public GhostCreationSystem           GhostCreationSystem { get; }
    public NetworkLifecycleSystemGroup   NetworkLifecycleGroup { get; }
    public CycloneNetworkCleanupSystem?  CleanupSystem { get; }
    public Action?                       AfterSeekCallback { get; }
    public bool                          DriveFromNetwork { get; }

    // IEcsModule
    public void RegisterSystems(IEcsKernelBuilder builder);
    public void Dispose();
}
```

### NedCommandGateway

```csharp
public class NedCommandGateway : INedCommandGateway, ICommandGateway
{
    public NedCommandGateway(DdsParticipant participant, long localNodeId = 0);

    public Task<CreateUpdateDeleteEntityAck> CreateEntityAsync(
        CreateEntityRequest request, int timeoutMs = 5000);

    public Task<MissionControlAck> SendMissionControlRequestAsync(
        MissionControlRequest request, int timeoutMs = 5000);

    public void SendUpdateDescriptor(UpdateEntityDescriptorRequest request);

    // ICommandGateway: additional attribute update + entity lifecycle methods
}
```

### Key Wire Types (DDS Topics)

| Topic name | Struct | Namespace | QoS summary |
|------------|--------|-----------|-------------|
| `EntityMaster` | `EntityMaster` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `EntityInfo` | `EntityInfo` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `WorldPos` | `WorldPos` | `Hrot.NED.Descriptors` | BestEffort / TransientLocal / KeepLast(1) |
| `EntityDamage` | `EntityDamage` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `NavigationIntent` | `NavigationIntent` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `NavigationStatus` | `NavigationStatus` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `EntityMission` | `EntityMission` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `SensorConfig` | `SensorConfig` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `SensorTrackState` | `SensorTrackState` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `RaycastRequestBatch` | `RaycastRequestBatch` | `Hrot.NED.Descriptors` | Reliable / Volatile |
| `RaycastResponseBatch` | `RaycastResponseBatch` | `Hrot.NED.Descriptors` | Reliable / Volatile |
| `PathRequestBatch` | `PathRequestBatch` | `Hrot.NED.Descriptors` | Reliable / Volatile |
| `PathResponseBatch` | `PathResponseBatch` | `Hrot.NED.Descriptors` | Reliable / Volatile |
| `DeferredTakeOwnership` | `DeferredTakeOwnership` | `Hrot.NED.Messages` | Reliable / Volatile / KeepAll(100) |
| `OwnershipUpdate` | `OwnershipUpdate` | `Hrot.NED.Messages` | Reliable / Volatile / KeepLast(1) |
| `CreateEntityRequest` | `CreateEntityRequest` | `Hrot.NED.Messages` | Reliable / Volatile / KeepAll |
| `MapClickEvent` | `MapClickEvent` | `Hrot.NED.Messages` | Reliable / Volatile / KeepAll |
| `SelectionChangedEvent` | `SelectionChangedEvent` | `Hrot.NED.Messages` | (default) |
| `MapInteractionConfig` | `MapInteractionConfig` | `Hrot.NED.Descriptors` | Reliable / TransientLocal / KeepLast(1) |
| `MapCommandRequest` | `MapCommandRequest` | `Hrot.NED.Messages` | (default) |
| `WeaponFireRequest` | `WeaponFireRequest` | `Hrot.NED.Messages` | (default) |
| `WeaponFire` | `WeaponFire` | `Hrot.NED.Messages` | (default) |
| `MunitionDetonation` | `MunitionDetonation` | `Hrot.NED.Messages` | (default) |
| `EntityHitDamage` | `EntityHitDamage` | `Hrot.NED.Messages` | (default) |
| `TacticalIntentRequest` | `TacticalIntentRequest` | `Hrot.NED.Messages` | (managed) |
| `AreaQueryRequestBatch` | `AreaQueryRequestBatch` | `Hrot.NED.Messages` | Reliable / Volatile |
| `AreaQueryResponseBatch` | `AreaQueryResponseBatch` | `Hrot.NED.Messages` | Reliable / Volatile |
| `MissionControlRequest` | `MissionControlRequest` | `Hrot.NED.Messages` | Reliable / Volatile / KeepAll |
| `MissionControlAck` | `MissionControlAck` | `Hrot.NED.Messages` | Reliable / Volatile / KeepAll |
| `SubsystemStatusAnnounce` | `SubsystemStatusAnnounce` | `Hrot.DDS.DataModel.Runner` | Reliable / TransientLocal / KeepLast(1) |

### EDescriptorType Enum

```csharp
public enum EDescriptorType
{
    dtEntityMaster          = 0,
    dtEntityInfo            = 1,
    dtWorldPos              = 2,
    dtMapVisualOverlay      = 3,
    dtMapRoute              = 4,
    dtEntityDamage          = 30,
    dtMapEntitySymbol       = 40,
    dtEntityMission         = 51,
    dtNavigationIntent      = 52,
    dtNavigationStatus      = 53,
    dtDeferredTakeOwnership = 54,
    dtOwnershipUpdate       = 55,
    dtSensorConfig          = 60,
    dtRaycastRequestBatch   = 61,
    dtSensorTrackState      = 62,
    dtRaycastResponseBatch  = 63,
    dtPathRequestBatch      = 64,
    dtPathResponseBatch     = 65,
    dtGroundClampingOverride= 66,
    dtWeaponFireRequest     = 80,
    dtWeaponFire            = 81,
    dtMunitionDetonation    = 82,
    dtEntityHitDamage       = 83,
    dtAudioTargetDetected   = 84,
    dtMissionControlRequest = 90,
    dtMissionControlAck     = 91,
    dtTacticalIntentRequest = 92,
    dtAreaQueryRequestBatch = 93,
    dtAreaQueryResponseBatch= 94,
}
```

### Routing and Cluster State

```csharp
public interface IClusterStateCache
{
    int?  GetLeastLoadedNode(NodeRole requiredRole);
    void  UpdateNode(NodeCapability capability);
    void  PruneStale(double nowUtcSeconds, double maxSilenceSeconds = 10.0);
}

public sealed class NodeCapability
{
    public int     NodeId              { get; set; }
    public NodeRole Role               { get; set; }
    public float   CpuUsagePercent    { get; set; }
    public long    RamUsedBytes       { get; set; }
    public double  LastSeenUtcSeconds { get; set; }
}

public sealed class BrainMuscleOwnershipStrategy : IOwnershipDistributionStrategy
{
    // Routes dtWorldPos and dtNavigationStatus to least-loaded MuscleGround node.
    public BrainMuscleOwnershipStrategy(IClusterStateCache clusterCache);
    public IReadOnlyList<DescriptorGrant> GetInitialGrants(
        DISEntityType entityType, int masterNodeId);
}
```

### HrotNodeBuilderReplicationExtensions

```csharp
public static class HrotNodeBuilderReplicationExtensions
{
    // Fluent extension: adds NED replication to a node builder
    public static HrotNodeBuilderWithReplication WithReplication(
        this HrotNodeBuilder builder, NodeRole role);

    // Upgrades a headless context with a live DDS participant
    public static HrotNodeContext BindReplicationParticipant(
        this HrotNodeContext context,
        NodeRole             role,
        DdsParticipant       participant,
        BehaviorRegistry?    behaviorRegistry = null);
}

public sealed class HrotNodeBuilderWithReplication
{
    public HrotNodeBuilderWithReplication WithBehaviorRegistry(BehaviorRegistry? registry);
    public HrotNodeBuilderWithReplication WithTranslators(
        IReadOnlyList<ITkbEntityTranslator>? translators);
    public HrotNodeContext Build();
}
```

---

## Dependencies

### Project References

| Reference | Purpose |
|-----------|---------|
| `Hrot.Core` | Domain model, neutral interfaces (`INetworkFactory`, `IReplicationModule`, `NodeRole`, `ICommandGateway`, etc.) |
| `Hrot.Common` | `HrotNodeBuilder`, `HrotNodeContext`, `HrotEnvironment`, `ISimHostMissionSender`, etc. |
| `Hrot.Network.Orchestration` | `NedStatusCode`, `ClusterOpRequest/Status`, `NodeOpCommand/Status`, `NodeHeartbeat`, `AssetInventoryTopic`, `ClusterStateTopic`; orchestration translator implementations |
| `Fdp.Core` | ECS kernel (`EntityRepository`, `FdpEventBus`, `Entity`, `EntityInfo`, `SimTransform`), `DISEntityType`, `ITkbDatabase` |
| `Fdp.Toolkits` | Navigation toolkit (`NavigationMode`, `NavigationResult`), behavior toolkit, lifecycle module, replication patching (`JsonAttributeCompiler`, `BinaryInterpreter`, `AttributeIds`) |
| `Fdp.Diagnostics.Network` | Gizmo DDS schema types (`GizmoInteractionBatch`, `DebugPrimitivesBatch`) |
| `Fdp.Network.Cyclone` | `DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>`, `CycloneNetworkIngressSystem`, `CycloneNetworkCleanupSystem`, `DdsIdAllocatorServer`, `TimeNetworkModule`, `DdsCommandClient<TReq,TAck>` |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `CycloneDDS.NET` | 0.2.2 | CycloneDDS C# bindings; `[DdsTopic]`, `[DdsStruct]`, `[DdsQos]`, `[DdsKey]`, `[DdsManaged]` schema attributes; code-generated IDL serialisers |

### InternalsVisibleTo Grants

The assembly exposes its internals to:
- `Hrot.Network.NED.Tests`
- `Hrot.IG.Tests`
- `Hrot.SimHost.Tests`
- `Hrot.ClusterRunner.Integration.Tests`
- `Hrot.Map.Common.Tests`

---

## Usage Examples

### Example 1 -- Bootstrapping a Brain Node with NED Replication

```csharp
// Build the Hrot node context with NED replication for a Brain node.
HrotNodeContext context = new HrotNodeBuilder()
    .WithConfig(cfg)                     // loads DDS domain, node ID, etc.
    .WithReplication(NodeRole.Brain)     // NedReplicationModule configured for Brain
    .WithBehaviorRegistry(behaviorReg)   // mission serialisation support
    .WithTranslators(tkbTranslators)     // TKB entity translators for ghost promotion
    .Build();

// context.NedReplication is now a fully-wired NedReplicationModule.
// context.GhostCreationSystem can be passed to perception / IG components.

// Register the replication module on the ECS kernel.
kernel.AddModule(context.NedReplication);
```

### Example 2 -- Creating a Factory and Spawning Subsystem Objects

```csharp
var factory = new NedNetworkFactory(
    participant:    ddsParticipant,
    entityMap:      entityMap,
    geoTransform:   geoTransform,
    eventBus:       eventBus,
    localNodeId:    nodeId,
    role:           NodeRole.MuscleGround,
    tkbDb:          tkbDatabase,
    lifecycleModule: lifecycleModule);

// Create the replication module (handles EntityMaster, WorldPos, EntityInfo, EntityDamage).
IReplicationModule replication = factory.CreateReplicationModule();

// Create the pathfinding translators (SimHost-side raycast/path batch I/O).
ISimHostPathfindingTranslators pathfinding =
    factory.CreateSimHostPathfindingTranslators(trajectoryPool);

// Create the perception translators (SensorConfig in, SensorTrackState out, area queries).
ISimHostPerceptionTranslators perception =
    factory.CreateSimHostPerceptionTranslators(ghostCreationSystem);

// Create auxiliary translators (fire interaction, damage events, ground clamping write).
ISimHostAuxiliaryTranslators aux = factory.CreateSimHostAuxiliaryTranslators();

// Create attribute update systems (JSON + binary attribute patching).
IReadOnlyList<IEcsModuleSystem> attrSystems =
    factory.CreateSimHostAttributeUpdateSystems();

// Register all modules on the ECS kernel.
kernel.AddModule(replication);
kernel.AddModule(pathfinding);
foreach (var sys in attrSystems)
    kernel.AddSystem(sys);
```

### Example 3 -- ExCon: Creating an Entity Over DDS

```csharp
// ExCon side: get the factory (created with NodeRole containing ExCon or AllInOne).
IExConEgressWriters egress = factory.CreateExConEgressWriters();
ICommandGateway gateway    = factory.CreateCommandGateway();

// Write a create-entity command with initial position and force.
var cmd = new CreateEntityCommand
{
    RequestId = Guid.NewGuid(),
    TkbType   = 8001,              // e.g., M1A1 Abrams in the TKB database
    Latitude  = 52.3745,
    Longitude = 16.9234,
    Altitude  = 150.0,
    ForceId   = 1,                 // 1 = Friendly
};
egress.WriteCreateEntity(cmd);

// Or use the async command gateway to get an ACK with the assigned EntityId:
NedCommandGateway nedGw = (NedCommandGateway)gateway;
CreateUpdateDeleteEntityAck ack =
    await nedGw.CreateEntityAsync(NedTranslationHelper.ToCreateEntityRequest(cmd));
int assignedEntityId = ack.EntityId;
```

### Example 4 -- Sending a Mission Control Request

```csharp
// From ExCon: issue a REPLACE_MISSION command to entity 42.
var plan = new CoreMission.MissionPlan
{
    ActiveTaskId = Guid.NewGuid(),
    Tasks = new List<CoreMission.MissionTask>
    {
        new CoreMission.MissionTask
        {
            TaskId          = Guid.NewGuid(),
            ExecutingEngine = "CGFX",
            BehaviorId      = "MoveToLocation",
            BehaviorParams  = "{\"x\":1500.0,\"y\":800.0}",
            Triggers        = new List<CoreMission.MissionTrigger>
            {
                new CoreMission.MissionTrigger { Type = "BehaviorFinished", Params = "" }
            },
        }
    },
};

MissionControlAck ack = await nedGw.SendMissionControlRequestAsync(
    NedTranslationHelper.ToMissionControlRequest(new MissionControlCommand
    {
        EntityId    = 42,
        CommandType = CoreMission.eMissionCommandType.CMD_REPLACE_MISSION,
        Plan        = plan,
        BaseVersion = 0,   // no optimistic locking check
    }));

if (ack.ErrorCode != 0)
    Console.WriteLine($"Mission rejected: {ack.ErrorMessage}");
```

### Example 5 -- Time Control (ExCon Pause / Resume)

```csharp
ITimeControlGateway time = factory.CreateTimeControlGateway();

// Pause the cluster simulation.
time.RequestPause();

// Fast-forward at 4x speed.
time.SetTimeScale(4.0f);

// Resume at normal speed.
time.RequestResume();

// Advance one frame.
time.RequestStep();
```

### Example 6 -- Cluster State Cache and Ownership Strategy

```csharp
// Build a cluster state cache and subscribe to heartbeat events.
var cache = new SimpleClusterStateCache();

eventBus.Subscribe<NodeHeartbeatEvent>(hb =>
{
    cache.UpdateNode(new NodeCapability
    {
        NodeId             = hb.NodeId,
        Role               = (NodeRole)hb.LocalStateId,
        CpuUsagePercent    = hb.CpuPercent,
        LastSeenUtcSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    });
});

// Use the ownership strategy to route physics descriptors.
var strategy = new BrainMuscleOwnershipStrategy(cache);

IReadOnlyList<DescriptorGrant> grants =
    strategy.GetInitialGrants(entityType, masterNodeId: brainNodeId);
// Each DescriptorGrant tells the spawning system which remote node should
// publish dtWorldPos and dtNavigationStatus for the new entity.
```

---

## Best Practices

### 1. Always Use the Factory

Do not construct `NedReplicationModule`, `NedCommandGateway`, or individual translators
directly in application code. Use `NedNetworkFactory` as the single composition root.
This ensures null-participant handling, role validation, and correct lifecycle wiring.

### 2. Respect the DeferredTakeOwnership Ordering Invariant

The `DeferredTakeOwnershipEgressTranslator` must be positioned before
`EntityMasterEgressTranslator` in the translator array. `NedReplicationModule` handles
this automatically via the order in which translators are registered. Do not reorder
translator lists manually.

### 3. Use TransientLocal QoS for Persistent State, Volatile for Transient Events

Descriptors that define durable entity state (`WorldPos`, `EntityMission`, `EntityInfo`,
`MapRoute`, etc.) use `TransientLocal` QoS so late-joining nodes receive the last known
state. Transient events (`WeaponFireRequest`, `MunitionDetonation`, `AreaQueryRequestBatch`,
etc.) use `Volatile` QoS to avoid stale replays on reconnect.

### 4. Do Not Publish WorldPos on Brain Nodes

The `WorldPos` descriptor is Muscle-owned. Brain nodes should publish `NavigationIntent`
and let the Muscle node drive `WorldPos` updates. Only when no Muscle node is available
(e.g. `AllInOne` mode) should a single node own both.

### 5. Use `UpdateEntityAttributeRequest` for Field-Level Patches

For fine-grained property updates (e.g. changing just the `Name` or `Affiliation` of an
entity), use `UpdateEntityAttributeRequest` with a JSON patch rather than writing a full
descriptor update. This minimises network bandwidth and reduces the risk of overwriting
concurrent changes from other nodes.

### 6. Check Authority Before Writing ECS Components

The `DeferredTakeoverSystem` calls `SetAuthority(entity, componentId, true)` for granted
descriptors. Only call `repo.SetAuthority(entity, id, true)` if the descriptor was
explicitly granted to this node, either via `DeferredTakeOwnership` or via the
`BrainMuscleOwnershipStrategy`.

### 7. Prune the Cluster State Cache Periodically

`SimpleClusterStateCache.PruneStale` should be called from a timer or from the
heartbeat subscription handler. Without pruning, stale Muscle node entries can cause
entity creation to route physics descriptors to a node that is no longer online, resulting
in permanently unowned descriptors.

### 8. Prefer Descriptors over Messages for Persistent Data

NED distinguishes between **Descriptors** (durable state published as key-based DDS topics
with TransientLocal QoS) and **Messages** (transient events published as Volatile topics).
Never use a Message topic to carry data that must survive a node reconnect.

### 9. Keep IDL File Assignments Consistent

Every new DDS struct or topic must carry `[DdsIdlFile("...")]` matching the correct logical
file group (see the IDL file table above). Mismatched or missing annotations cause
CycloneDDS schema generation to include a type in the wrong IDL partition, potentially
breaking binary compatibility with peer nodes.

### 10. Test Headless First

All NED translators and systems accept a null `DdsParticipant`. Unit and integration tests
should construct `NedNetworkFactory` with `participant: null` to avoid the overhead of a
live DDS stack. The factory returns null-object stubs for all subsystems in that mode.

---

## Related Projects

### Direct Network Layer Siblings

| Project | Relationship |
|---------|-------------|
| `Hrot.Network.BDC` | Alternative minimal protocol. Implements the same `INetworkFactory` / `IReplicationModule` contracts but with only `BDC_EntityMaster` and `BDC_WorldPos` topics. Used for lightweight federation or heterogeneous simulator integration. See [Hrot.Network.BDC.md](Hrot.Network.BDC.md). |
| `Hrot.Network.BDC.Tests` | Unit tests for the BDC protocol. |
| `Hrot.Network.NED.Tests` | Unit and integration tests for NED translators, attribute patching, DeferredTakeOwnership, and ownership strategy. Uses null-participant headless mode and injectable stubs. |
| `Hrot.NED.Tests` | Additional test project with InternalsVisibleTo access; covers internal wire types and descriptor union logic. |
| `Hrot.Network.Orchestration` | Defines `NedStatusCode`, cluster operation request/status types, node heartbeat topics, and the orchestration translator implementations that `NedOrchestrationTranslator` and `NedSlaveOrchestrationTranslator` delegate to. |

### Consumer Projects

| Project | How NED is Used |
|---------|----------------|
| `Hrot.SimHost` | Primary consumer for `MuscleGround` role; uses `NedNetworkFactory.CreateReplicationModule()`, `CreateSimHostPathfindingTranslators()`, `CreateSimHostPerceptionTranslators()`, `CreateSimHostAuxiliaryTranslators()`, and `CreateSimHostAttributeUpdateSystems()`. |
| `Hrot.IG` (Image Generator) | Consumes `IgTranslators` and `IgNetworkAdapter` for rendering entity positions and receiving map interaction events. |
| `Hrot.ExCon` (Exercise Controller) | Consumes `ExConEgressWriters`, `ExConIngressHandlers`, `CommandGateway`, and `TimeControlGateway` to manage entity lifecycle, map configuration, and mission control from the instructor station. |
| `Hrot.ClusterRunner` | Consumes the orchestration translators and `IdAllocatorServer` for cluster startup, node-op commands, and heartbeat monitoring. |
| `Hrot.Editor` | Uses NED replication to synchronise scenario edits between editor nodes. |

### Engine Dependencies

| Project | What NED Uses From It |
|---------|----------------------|
| `Hrot.Core` | `INetworkFactory`, `IReplicationModule`, `ICommandGateway`, `NodeRole`, `IOrchestrationTranslator`, `ISlaveOrchestrationTranslator`, `ITimeControlGateway`, `IExConEgressWriters` |
| `Hrot.Common` | `HrotNodeBuilder`, `HrotNodeContext`, `HrotEnvironment`, `NodeCapability`, `IOwnershipDistributionStrategy`, `ISimHostMissionSender` |
| `Fdp.Core` | `EntityRepository`, `Entity`, `FdpEventBus`, `EntityInfo`, `SimTransform`, `DISEntityType`, `ITkbDatabase` |
| `Fdp.Network.Cyclone` | `DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>`, all network lifecycle systems, `DdsIdAllocatorServer`, `TimeNetworkModule`, `DdsCommandClient<,>` |
| `Fdp.Toolkits` | `JsonAttributeCompiler`, `BinaryInterpreter`, `NavigationMode`, `NavigationResult`, `BehaviorRegistry`, `EntityLifecycleModule`, replication services, `NetworkEntityMap`, `DescriptorOwnershipMap` |
| `Fdp.Diagnostics.Network` | `GizmoInteractionBatch`, `DebugPrimitivesBatch` schema types |

### Protocol Comparison Table (NED vs BDC)

```
+--------------------------------------+------------------+------------------+
| Capability                           |       NED        |       BDC        |
+--------------------------------------+------------------+------------------+
| DDS topic prefix                     | (none, shared)   | BDC_             |
| Entity lifecycle                     | EntityMaster     | BDC_EntityMaster |
| World position                       | WorldPos         | BDC_WorldPos     |
| Mission / behavior                   | EntityMission,   | Not available    |
|                                      | MissionControl   |                  |
| Navigation CQRS                      | NavigationIntent | Not available    |
|                                      | NavigationStatus |                  |
| Perception sensor tracks             | SensorTrackState | Not available    |
| Pathfinding (ray/path)               | *RequestBatch    | Not available    |
| Weapon fire pipeline                 | WeaponFire*,     | Not available    |
|                                      | MunitionDetonat. |                  |
| Damage pipeline                      | EntityHitDamage  | Not available    |
| Tactical intent (Brain-to-Brain)     | TacticalIntent   | Not available    |
| Area queries (EQS)                   | AreaQuery*Batch  | Not available    |
| Map interaction                      | MapClickEvent,   | Not available    |
|                                      | DragEvent, etc.  |                  |
| ExCon commands                       | CreateEntity,    | Not available    |
|                                      | MapCommand, etc. |                  |
| Time control                         | ClusterOpRequest | Not available    |
| Orchestration                        | NodeOp/Cluster   | Not available    |
|                                      | Op topics        |                  |
| Pre-genesis ownership routing        | DeferredTake     | Not available    |
|                                      | Ownership        |                  |
| Binary attribute patching            | UpdateEntity     | Not available    |
|                                      | AttributeRequest |                  |
| ID allocator                         | DdsIdAllocator   | Sequential       |
|                                      | Server           | counter          |
| Descriptor count (per entity)        | 10+              | 2                |
| Suitable for external federation     | Limited          | Yes              |
| Suitable for full HROT cluster       | Yes              | No               |
+--------------------------------------+------------------+------------------+
```
