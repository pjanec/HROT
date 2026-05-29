# Hrot.Animation.Replication

**Project path:** `Hrot/Subsystems/Hrot.Animation.Replication/Hrot.Animation.Replication.csproj`
**Assembly:** `Hrot.Animation.Replication`
**Target framework:** net8.0
**Date:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.Animation.Replication` implements the DDS-based cross-node replication of
all animation state between Brain and Muscle simulation nodes. It provides
`AnimationReplicationModule`, which instantiates and wires up all 15 network
translators needed for one node, configured by `NodeRole` (Brain or Muscle).

The module covers:
1. **4 channel topics** — `AnimationChannel` and `LookAtChannel` intent and status
2. **3 descriptor topics** — `AnimationMontageQueue`, `AnimationMontageQueueState`,
   and stance intent/status
3. **7 event topics** — all six lifecycle and notify events defined in the animation
   event catalog (excluding the local-only `FootstepEvent`)

Each topic uses a deterministic QoS policy (Reliable + TransientLocal for
state-bearing topics; Reliable + Volatile for event topics) published by
`AnimTopicQosPolicy` table.

The design is governed by `DD-2_AnimationReplication_v1_1.md`.

---

## Architecture

### Role-Based Translator Instantiation

Each node instantiates the same `AnimationReplicationModule` but with a different
`NodeRole`. The role determines which direction each translator flows:

| Topic | Brain | Muscle |
|-------|-------|--------|
| `AnimationChannel` intent | Egress (publish) | Ingress (subscribe) |
| `AnimationChannel` status | Ingress (subscribe) | Egress (publish) |
| `LookAtChannel` intent | Egress | Ingress |
| `LookAtChannel` status | Ingress | Egress |
| `StanceIntent` (descriptor) | Egress | Ingress |
| `StanceStatus` (descriptor) | Ingress | Egress |
| `AnimationMontageQueue` (side buffer) | Egress | Ingress |
| `AnimationMontageQueueState` (side buffer) | Ingress | Egress |
| `MontageStartedEvent` | Ingress | Egress |
| `MontageEndedEvent` | Ingress | Egress |
| `MontageSectionAdvancedEvent` | Ingress | Egress |
| `StanceChangedEvent` | Ingress | Egress |
| `AnimNotifyEvent` | Ingress | Egress |
| `HitWindowOpenedEvent` | Ingress | Egress |
| `HitWindowClosedEvent` | Ingress | Egress |

`FootstepEvent` (EventId=8211) is **not replicated** — it is local-only on Muscle
(enforced by Blueprint validator BP2017).

### DDS Wire Format

All DDS messages are blittable unmanaged structs (fixed-size, `[StructLayout(LayoutKind.Sequential)]`)
for zero-copy serialization over CycloneDDS. Internal message types are defined
in `AnimationDdsMessages.cs`.

| DDS struct | Maps to | Size |
|------------|---------|------|
| `DdsAnimationChannelIntent` | `AnimationChannel` write fields | ~56 bytes |
| `DdsAnimationChannelStatus` | `AnimationChannel` read fields | 16 bytes |
| `DdsLookAtChannelIntent` | `LookAtChannel` write fields | ~56 bytes |
| `DdsLookAtChannelStatus` | `LookAtChannel` read fields | 16 bytes |
| `DdsStanceIntent` | `StanceIntent` | 20 bytes |
| `DdsStanceStatus` | `StanceStatus` | 20 bytes |
| `DdsMontageQueueEntry` | queue entry | 16 bytes |
| `DdsMontageQueueMessage` | `AnimationMontageQueue` (8 entries) | ~136 bytes |

All DDS message types are `internal` — only `AnimationReplicationModule` is public.

### Translator Folder Structure

```
Translators/
  Channels/       -- 8 translators (AnimationChannel + LookAtChannel intent/status)
  Descriptors/    -- stance intent/status translators
  Events/         -- 7 event translators
  SideBuffers/    -- AnimationMontageQueue and MontageQueueState translators
```

---

## ASCII Block Diagrams

### Diagram 1: Assembly Dependency Graph

```
+-------------------------------------------+
|  Hrot.Animation.Replication               |  net8.0 class library
+-------------------------------------------+
   |        |        |        |
   |        |        |        +-- Fdp.Network.Cyclone
   |        |        |               (DdsParticipant, DdsReliability,
   |        |        |                DdsDurability, IDdsWriter, IDdsReader)
   |        |        |
   |        |        +----------- Fdp.Toolkits
   |        |                        (INetworkTranslator, NodeRole,
   |        |                         NetworkEntityMap, ReplicationServices)
   |        |
   |        +------------------- Fdp.Core
   |                                (Entity, EntityRepository)
   |
   +---- Hrot.MuscleCharacter.Animation
   |        (AnimationChannel, LookAtChannel, StanceIntent, StanceStatus,
   |         AnimationMontageQueue, AnimationMontageQueueState,
   |         MontageStartedEvent, MontageEndedEvent, MontageSectionAdvancedEvent,
   |         StanceChangedEvent, AnimNotifyEvent, HitWindowOpenedEvent,
   |         HitWindowClosedEvent)
   |
   +---- Hrot.Core
            (NetworkEntityMap, common constants)
```

### Diagram 2: Translator topology for one Brain node

```
Brain node
  AnimationReplicationModule(participant, entityMap, NodeRole.Brain)

  Egress translators (Brain -> DDS -> Muscle):
    AnimationChannelIntentEgressTranslator    -- AnimationChannel.{ActiveAction,Params} -> DDS
    LookAtChannelIntentEgressTranslator       -- LookAtChannel.{ActiveAction,Params}    -> DDS
    StanceIntentEgressTranslator              -- StanceIntent.{TargetStance,Version}    -> DDS
    AnimationMontageQueueEgressTranslator     -- AnimationMontageQueue entries          -> DDS

  Ingress translators (DDS -> Brain components):
    AnimationChannelStatusIngressTranslator   -- DDS -> AnimationChannel.{Status,DispatchedInstanceId}
    LookAtChannelStatusIngressTranslator      -- DDS -> LookAtChannel.{Status,DispatchedInstanceId}
    StanceStatusIngressTranslator             -- DDS -> StanceStatus.{CurrentStance,AckVersion}
    AnimationMontageQueueStateIngressTranslator -- DDS -> AnimationMontageQueueState

    MontageStartedEventIngressTranslator      -- DDS -> MontageStartedEvent (Brain event bus)
    MontageEndedEventIngressTranslator        -- DDS -> MontageEndedEvent
    MontageSectionAdvancedEventIngressTranslator -- DDS -> MontageSectionAdvancedEvent
    StanceChangedEventIngressTranslator       -- DDS -> StanceChangedEvent
    AnimNotifyEventIngressTranslator          -- DDS -> AnimNotifyEvent
    HitWindowOpenedEventIngressTranslator     -- DDS -> HitWindowOpenedEvent
    HitWindowClosedEventIngressTranslator     -- DDS -> HitWindowClosedEvent
```

---

## Key Types

### `AnimationReplicationModule`

| Member | Description |
|--------|-------------|
| `AllTranslators` | `IReadOnlyList<INetworkTranslator>` — 15 translators for the configured node role |
| `TopicQosPolicies` (static) | `IReadOnlyList<AnimTopicQosPolicy>` — deterministic QoS table for all 15 DDS topics |
| Constructor | Takes `DdsParticipant` (can be null in test mode), `NetworkEntityMap`, `NodeRole` |

### `AnimTopicQosPolicy`

Carries `TopicName`, `DdsReliability`, and `DdsDurability` for one topic.

QoS conventions:
- State-bearing topics (channels, descriptors, side buffers): **Reliable + TransientLocal**
- Event topics (lifecycle and notify events): **Reliable + Volatile**

### DDS Message Types (internal)

All structs are in `Hrot.Animation.Replication` namespace, marked `internal`.

| Struct | Purpose |
|--------|---------|
| `DdsAnimationChannelIntent` | Brain → Muscle; carries ActiveAction, ActionInstanceId, BehaviorInstanceId, ActionParams[32] |
| `DdsAnimationChannelStatus` | Muscle → Brain; carries NodeStatus, DispatchedInstanceId |
| `DdsLookAtChannelIntent` | Brain → Muscle; same layout as DdsAnimationChannelIntent |
| `DdsLookAtChannelStatus` | Muscle → Brain; same layout as DdsAnimationChannelStatus |
| `DdsStanceIntent` | Brain → Muscle; TargetStance, BlendTime, Version |
| `DdsStanceStatus` | Muscle → Brain; CurrentStance, Phase, TransitionProgress, AckVersion |

---

## Dependencies

```
Hrot.Animation.Replication
  --> Hrot.MuscleCharacter.Animation   (all channel/event/component types)
  --> Hrot.Core                        (NetworkEntityMap, NodeRole)
  --> Fdp.Core                         (Entity, EntityRepository)
  --> Fdp.Toolkits                     (INetworkTranslator, NetworkGateway)
  --> Fdp.Network.Cyclone              (DdsParticipant, CycloneDDS bindings)
```

---

## Usage Patterns

### Constructing the module on Brain and Muscle nodes

```csharp
// On Brain node:
var brainModule = new AnimationReplicationModule(
    participant: ddsParticipant,
    entityMap: entityMap,
    role: NodeRole.Brain);

foreach (var translator in brainModule.AllTranslators)
    networkGateway.RegisterTranslator(translator);

// On Muscle node:
var muscleModule = new AnimationReplicationModule(
    participant: ddsParticipant,
    entityMap: entityMap,
    role: NodeRole.Muscle);

foreach (var translator in muscleModule.AllTranslators)
    networkGateway.RegisterTranslator(translator);
```

### Using in unit tests without a DDS participant

```csharp
// Pass null as participant -- all readers/writers become no-ops:
var module = new AnimationReplicationModule(
    participant: null,
    entityMap: testEntityMap,
    role: NodeRole.Brain);
```

### Registering QoS policies with the DDS infrastructure

```csharp
foreach (var policy in AnimationReplicationModule.TopicQosPolicies)
{
    ddsParticipant.RegisterTopic(
        policy.TopicName,
        policy.Reliability,
        policy.Durability);
}
```

---

## Test Projects

| Project | Description |
|---------|-------------|
| `Hrot.Animation.Replication.Tests` | Unit tests for individual translator pairs (intent/status round-trips, event serialization) |
| `Hrot.Animation.Network.Integration.Tests` | Full networked stage-2 integration suite exercising Brain+Muscle with live DDS or in-process stub |
