<!--STATUS
state: LIVE
updated: 2026-10-28 (gateway re-check: GeoReference field names/units, ids int64 in the gateway table, the sender-identity layout, derived names in §11 and the related-designs; library 1.2.20)
current-answer: the whole document; §Inventory is generated-checked by scripts/ned-wire-inventory.py --check; §Concepts 15 states the one topic-name rule
stale-below: nothing
known-rot: (1) ../projects/Hrot/Network/Hrot.Network.NED.md is dated 2026-05-23 — its "Key Wire Types" table covers 30 of the 114 wire types here; its EDescriptorType listing stops at 94 (the enum now has 77 entries, AllDescriptors.cs:6); it still lists AreaQueryRequestBatch / AreaQueryResponseBatch and Messages/AreaQueryMessages.cs, RETIRED 2026-10-01 (AllDescriptors.cs:42, file absent); its TacticalIntentRequest row says "[DdsTopic] managed" but the type carries no [DdsTopic] (topic bound by a translator constant). (2) [fixed 2026-10-27 by AQ107 D: `WorldPos.Time` is now `WorldPos.SimTime`, a double of sim seconds]. (3) The doc comment on OwnershipUpdate.DescrTypeId says "1=EntityState, 2=WeaponState" (Fdp.Network.Cyclone/Topics/OwnershipUpdate.cs) but the ordinals are EDescriptorType (dtWorldPos = 2, AllDescriptors.cs:6).
known-conflict: BDC_NED_SST_Descriptor_Rules.md (the authoritative spec) and the code differ in the places listed in §Code versus the spec; neither side has been reconciled by a ruling. (Topic names have no conflict left: every name is derived from the C# type, no exceptions — §Concepts 15.)
related-designs:
  - ../blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md — OWNS the wire change — one frame (local metres + GeoReference), names from types, WorldPos as a quaternion pose (R-4092); all of A–I is BUILT (2026-10-28) and this reference describes the result
  - BDC_NED_SST_Descriptor_Rules.md — OWNS the authoritative descriptor/ownership rules this reference summarises
  - ../projects/Hrot/Network/Hrot.Network.NED.md — the NED project's architecture overview (code structure, translators)
  - ../DESIGN_Entity_Genesis_End_To_End.md — OWNS the stage sequence of entity creation across nodes (ghost, promotion, takeover)
  - ../DESIGN_Ownership_Groups_And_Grants.md — OWNS which role writes which descriptor group, and the node-departure reclaim
  - ../DESIGN_Entity_Ownership_Transfer.md — OWNS how a node initiates an OwnershipUpdate
  - ../blueprints/Architect_Question_79_One_Ownership_Truth.md — OWNS the ownership decisions (push-only, R-166 external nodes use the OwnershipUpdate topic — the ruling's "SST_OwnershipUpdate" is now the derived `Fdp_Network_Cyclone_Topics_OwnershipUpdate`)
  - ../DESIGN_Deterministic_Network_Ids.md — OWNS the entity-id allocator semantics (world-boundary reset to 1000)
  - ../DESIGN_Dead_Reckoning.md — OWNS the WorldPos sim-time stamp and the dead-reckoning rules
  - ../DESIGN_Geo_Origin.md — OWNS where the local-metres to lat/lon origin comes from (the terrain)
  - ../designs/mgmt-1/DESIGN.md — OWNS cluster orchestration and §8.10 entity lifecycle during replay
  - ../DESIGN_Replay_Time_Axis_And_Review.md — OWNS the replay session layer (ReplaySession topic, axis, review)
  - ../DESIGN_Cross_Node_Construction_Barrier.md — OWNS the reliable-init barrier (WaitForAcks, EntityLifecycleStatus)
  - ../designs/dds-to-ecs/DESIGN.md — OWNS the rule that DDS types are DTOs behind translators
  - ../designs/bugs-2/BUG2-DESIGN.md — OWNS the sender-identity requirement (EnableSenderTracking) on every participant
  - ../RUNBOOK_Cluster_Debugging_Over_Http.md — OWNS the ddsmonitor wire-capture recipe (§5a)
  - ../blueprints/Architect_Question_93_Sumo_Traffic_Node.md — OWNS the SUMO traffic node, whose vehicles are NOT NED entities
-->

# NED on the wire — the DDS protocol of HROT

**What this is.** A living reference for anyone who must speak HROT's DDS protocol from outside (first reader: the DIS/HLA gateway
SimNetGateway). It states **concepts, rules and full C# type names** — never the members of a structure (they change; read the C# type
or the build-generated IDL, see Concepts §15). Every rule carries a `file:line` and/or a doc § citation; what could not be
determined says so.

**Reading order.** Diagrams (3) → Concepts and rules → Inventory (all 114 wire types) → For a protocol gateway → Code versus the spec.

**The one-paragraph model.** An entity is *nothing but* a set of keyed DDS instances ("descriptors"), one topic per descriptor type,
all sharing the entity id. `EntityMaster` alive = the entity exists. Each descriptor has its own owner (the node that writes it);
ownership moves with a small `OwnershipUpdate` message, never by dispose. Everything that is not state — commands, requests, shots,
detonations, cluster control — is a *message* on its own topic. Time is cluster-synchronised simulation time; every position on the wire is
**local ENU metres** (X east, Y north, Z up, at the terrain origin — the ECS frame; AQ107 B), orientation a quaternion.

## Diagrams

### 1. Concept model

```mermaid
classDiagram
    direction LR
    class Entity {
        <<logical - never one wire object>>
        entity id
    }
    class EntityMaster {
        <<descriptor - mandatory>>
        key entity id
        TkbType mandatory
        DisType informative
        flags
    }
    class Descriptor {
        <<abstract - keyed DDS topic>>
        key entity id
        one topic per descriptor type
        descriptor type id = EDescriptorType ordinal
    }
    class SingleInstanceDescriptor {
        one instance per entity
        instance id = 0
    }
    class MultiInstanceDescriptor {
        key parent entity id + instance id
        parts and child sensors
    }
    class OwnershipUpdate {
        <<message - Fdp_Network_Cyclone_Topics_OwnershipUpdate>>
        entity id
        descriptor type id
        instance id
        new owner node id
        origin node id
    }
    class Node {
        domain id
        instance id = owner id
    }
    class IdAllocator {
        <<server - one per DDS domain>>
        hands out entity-id blocks
    }
    class TkbCatalog {
        <<code or staged file - not on the wire>>
        TkbType to template
    }
    Entity "1" *-- "1" EntityMaster : existence
    Entity "1" *-- "0..*" Descriptor : state
    Descriptor <|-- EntityMaster
    Descriptor <|-- SingleInstanceDescriptor
    Descriptor <|-- MultiInstanceDescriptor
    Node "1" --> "0..*" Descriptor : owns and writes
    OwnershipUpdate ..> Descriptor : moves ownership of
    OwnershipUpdate ..> Node : names new owner
    IdAllocator ..> Entity : allocates id
    EntityMaster ..> TkbCatalog : TkbType resolves in
```

*What the picture shows that prose could not:* the master is **a descriptor like the others** (same keying) but the only one whose
life is the entity's life; ownership belongs to `Node → Descriptor`, never to `Entity`; and the id allocator and the TKB catalog sit
**outside** the wire model (a gateway must reach both some other way).

### 2. An entity's life on the wire

```mermaid
sequenceDiagram
    autonumber
    participant C as Creator and old owner
    participant A as IdAlloc server
    participant W as DDS domain
    participant R as Receiving node
    participant I as Initiator (any node)
    participant N as New owner
    C->>A: IdRequest (client id, count)
    A-->>C: IdResponse (start, count)
    opt HROT creator granting descriptors to other nodes
        C-->>W: DeferredTakeOwnership (entity id, list of descriptor type to node id)
    end
    C-->>W: EntityMaster (once, TransientLocal)
    C-->>W: EntityInfo, WorldPos and the other descriptors it owns
    W-->>R: samples arrive in ANY order, a ghost is made by the first one
    R->>R: ghost waits for the mandatory descriptors, then is promoted
    Note over C,R: WorldPos is re-sent on change and by a 600-tick heartbeat
    I-->>W: OwnershipUpdate (entity, descriptor type, instance, new owner N)
    W-->>C: OwnershipUpdate
    W-->>N: OwnershipUpdate
    Note over C: old owner stops writing, does NOT dispose
    N-->>W: writes the descriptor (usually same value) to confirm
    C-->>W: dispose ALL instances it owns, EntityMaster included
    W-->>R: EntityMaster NotAliveDisposed, replica destroyed
    Note over R: a dispose of any other descriptor is ignored
```

*What the picture shows that prose could not:* **order is not a protocol** (DDS orders samples only per writer, so receivers tolerate
any arrival order), the id allocation is a *prerequisite round-trip*, ownership change is a message plus a confirming write while
**delete is a dispose of the master** — and a crash is absent from this picture on purpose: it produces no message at all (§Instance lifecycle).

### 3. Who talks to whom

```mermaid
flowchart LR
    subgraph DOM["One DDS domain - no partitions except file transfer"]
        BUS(("topics"))
    end
    CGF["CGF / Brain node<br/>(node id base 400)"]
    SIM["SimHost / Muscle node<br/>(node id base 1)"]
    IG["IG node<br/>(node id base 100)"]
    ORC["Orchestrator / master<br/>(node id base 300)"]
    EXC["ExCon / editor<br/>(node id base 200)"]
    GW["External gateway<br/>(a node like any other)"]
    CGF -- "brain-owned descriptors, requests, intents" --> BUS
    SIM -- "pose, nav status, shots, detonations, results" --> BUS
    IG -- "map events, selection, capabilities" --> BUS
    EXC -- "create/delete/map commands, cluster ops" --> BUS
    ORC -- "ClusterState, node ops, id allocator, file transfer" --> BUS
    GW -- "descriptors it owns, OwnershipUpdate, requests" --> BUS
    BUS --> CGF
    BUS --> SIM
    BUS --> IG
    BUS --> EXC
    BUS --> ORC
    BUS --> GW
```

*What the picture shows that prose could not:* there is **no broker and no per-pair channel** — every node reads every topic it cares
about and writes the descriptors it owns; "who may write what" is a convention enforced by the writers (§Ownership), so a gateway
joins as one more node (needing a node id and a sender identity, §Transport).

## Concepts and rules

Citation key: `path:line` = code in this repo (a path without a leading `Hrot/` or `FDP/` is relative to `Hrot/Network/Hrot.Network.NED/`); `§` = a document section. "Spec" = [BDC_NED_SST_Descriptor_Rules.md](BDC_NED_SST_Descriptor_Rules.md).

### 1. Transport

| Rule | Evidence |
|---|---|
| DDS stack is **CycloneDDS** through the **CycloneDDS.NET** bindings (`DdsParticipant`, `DdsReader<T>`, `DdsWriter<T>`); one participant per node. ⭐ **The package version is ONE MSBuild property, `CycloneDdsNetVersion`** (`Directory.Build.props`; shipped value **`1.2.20`** from nuget.org since `2026-10-28`; a local build is tried with `-p:CycloneDdsNetVersion=<v>` and its `.nupkg` in the gitignored `nugets/` feed). ⛔ **The derived topic names below need the library's 1.2 line** (`DdsTopicName`, applied by `DdsWriter` / `DdsReader` when no name is passed): on `0.3.x` a name-less `[DdsTopic]` opens a null topic — never go back below 1.2 | `Hrot/Engine/Hrot.Core/HrotEnvironment.cs:76`; `Directory.Build.props:17`; library `src/CycloneDDS.Schema/DdsTopicName.cs`, `DdsWriter.cs:97`, `DdsReader.cs:152` (CycloneDDS.NET 1.2.20) |
| **One DDS domain per deployment**, chosen by configuration (`DdsDomainId`; shipped default 0, code default 42 when absent). Tests use other ids | `Hrot/Subsystems/Hrot.SimHost/config.json:2`, `Hrot/Subsystems/Hrot.SimHost/NodeConfiguration.cs:54` |
| **No partitions on entity or message topics.** The only partition use: file-transfer chunks travel in a per-transfer partition | `Hrot/Network/Hrot.Network.NED/Transfer/DdsFileTransferProvider.cs:151,180` (searched: `grep -rniE partition Hrot/Network FDP/Network`) |
| A node's **identity** = `(AppDomainId = DDS domain id, AppInstanceId = node id)`, attached to every writer by `EnableSenderTracking`, which must be called **before** writers exist. Receivers resolve the writer of each sample to a node id from it. A writer without it is anonymous (owner −1) | `Hrot/Runner/Hrot.ClusterRunner/Program.cs:286-291`, `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs:360`, `Hrot/Subsystems/Hrot.IG/IgApplication.cs:714`, `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs:650`; reader side `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs:84-91,252`; §[BUG2-DESIGN](../designs/bugs-2/BUG2-DESIGN.md) 1.2 |
| How the identity travels on the wire: the library's own topic **`__FcdcSenderIdentity`** (type `CycloneDDS.Runtime.Tracking.SenderIdentity`, appendable, Reliable + TransientLocal, one instance per participant): key `ParticipantGuid` (`DdsGuid`, the participant's 16-byte DDS GUID), then `AppDomainId` (int), `AppInstanceId` (int — HROT puts the **node id** here), `ComputerName`, `ProcessName`, `ProcessId`, `ComputerIP`. A non-.NET gateway publishes this sample for its participant (or uses CycloneDDS.NET with sender tracking on) | CycloneDDS.NET 1.2.20 `src/CycloneDDS.Runtime/Tracking/SenderIdentity.cs:13-59`, `SenderRegistry.cs:41-48`; `ddsmonitor` decodes it ([RUNBOOK §5a](../RUNBOOK_Cluster_Debugging_Over_Http.md)) |
| **Node ids**: assigned per subsystem as `base + offset` (SimHost 1, IG 100, ExCon 200, Orchestrator 300, CGF 400, CI 500, Traffic 800, other 600), or **granted by the master** on request (`NodeIdRequest` → `NodeIdGrant`, keyed by computer hash + process id) | `Hrot/Runner/Hrot.ClusterRunner/Program.cs:800-816`; `Hrot/Network/Hrot.Network.NED/Factory/NodeIdBroker.cs:25` |
| Transport tuning (CycloneDDS XML, interface selection, discovery) | **not determinable from sources** — `CYCLONEDDS_URI` appears only in docs (`FDP/Docs/projects/relationships/DDS-Integration.md:435`); searched `grep -rln "CYCLONEDDS_URI\|cyclonedds.xml"` |

### 2. Entity = a set of descriptors

| Rule | Evidence |
|---|---|
| An entity is composed of descriptors and nothing else; each descriptor type has its own topic; which descriptors an entity type needs is a convention among apps | Spec L20-32; code mirrors it: `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs` header comment (L24-40) |
| Each descriptor type has a stable integer **ordinal** (`EDescriptorType`, 77 entries, never reused — retired: 93, 94, 121, 134). It is the "descriptor type id" of `OwnershipUpdate`, `DeferredTakeOwnership`, `UpdateEntityDescriptorRequest` | `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs:6,42` |
| ⛔ **Retired topics** *(R-3067 / AQ109 X3 + X4, 2026-10-30 — a WIRE-CONTRACT REMOVAL: a peer on an older build publishes topics nobody reads)*: the derived names `Hrot_NED_Messages_MapCommandRequest`, `Hrot_NED_Messages_ContextActionsUpdate`, `Hrot_NED_Messages_ContextMenuRequest`, `Hrot_NED_Descriptors_MapInteractionConfig` (X3) and `Hrot_NED_Messages_MapCommandAck`, `Hrot_NED_Messages_ContextActionInvoked`, `Hrot_NED_Messages_MapClickEvent`, `Hrot_NED_Messages_SelectionChangedEvent`, `Hrot_NED_Messages_DragEvent`, `Hrot_NED_Descriptors_MapConfigStatus`, `Hrot_NED_Descriptors_IGCapabilitiesAnnounce` (X4) are **reserved — never reuse them for a different type** (⚠ `Hrot_IG_Gizmos_IGCapabilitiesAnnounce`, the gizmo one, is live); the `CommandType` enum (`CMD_SET_VIEW`=0 … `CMD_DRAW_PERSONAL_ROUTE`=8) stays defined so its values are never renumbered | `Hrot/Network/Hrot.Network.NED/MapMessages.cs` tombstone comment; `docs/blueprints/Architect_Question_109_Retire_ExCon.md` §6.3, §6.4 |
| In code every descriptor is reached through an `IDescriptorTranslator` (carries `TopicName` + `DescriptorOrdinal`); DDS structs are DTOs, never the ECS components | graph: `search_graph Interface ".*Descriptor.*"` → `FDP/Engine/Fdp.Core/Abstractions/IDescriptorTranslator.cs:82`; §[dds-to-ecs](../designs/dds-to-ecs/DESIGN.md) 1 |
| Which descriptors a **replica** needs before it accepts an entity is *derived at runtime* from the TKB template (`MandatoryComponentResolver`), not listed anywhere; an unknown `TkbType` skips the gate entirely | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs:225-296`. The per-type set: **not determinable from sources** as a static list |

### 3. EntityMaster

| Rule | Evidence |
|---|---|
| Mandatory for every entity; alive = entity exists. Carries entity id, `TkbType` (**mandatory**), `DisType` (7-field SISO-REF-010 decomposition, informative, all zeros = invalid), `Flags`, and the id of the creation request that made it | `GenericDescriptors.cs:97-122`; Spec L30,L115 |
| **No owner field** — the owner of the master is the *writer of the sample* (sender identity) | `GenericDescriptors.cs` (IMPORTANT NOTE under EntityMaster); `EntityMasterIngressTranslator.cs:84-91,252` |
| Published **exactly once** per entity by its owner; `TransientLocal` keeps it for late joiners | `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/EntityMasterEgressTranslator.cs:80-83,103-120` |
| Flag bits: `WaitForAcks` (reliable-init: receivers must report readiness), `Lightweight` (a replicated round: created Active on first sample, no ghost, no lifecycle; destroyed when the master is disposed) | `GenericDescriptors.cs:16-27`; `EntityMasterIngressTranslator.cs:220-232`; §[Cross-Node Barrier](../DESIGN_Cross_Node_Construction_Barrier.md) |
| **Name and force/side are NOT in the master** — they live in `EntityInfo` (name, `eForceIdentifier` UNKNOWN/FRIENDLY/OPPOSING/NEUTRAL = 0..3, commander id, tactical designation) | `GenericDescriptors.cs:133-139,169-200` |

### 4. Keys, instances and descriptor instance ids

| Rule | Evidence |
|---|---|
| A descriptor topic is **keyed by the entity id** (80 of the 114 wire types are keyed; the 34 unkeyed are messages or singletons) | `scripts/ned-wire-inventory.py` (a type is keyed when a member carries `[DdsKey]`) |
| Spec says `KeepLast 1` so `read_instance` returns one sample; HROT state topics are `Reliable + TransientLocal + KeepLast(1)` (kind `S`) | Spec L59; §QoS kinds below |
| **Multi-instance descriptors** are keyed by *(parent entity id + part/child instance id)*: turret pose and weapon task/status (part id = 2000 + turret index), the four EQS-family topics (child sensor index = `PartMetadata.InstanceId`) | `SimDescriptors.cs` comments at `EntityTurretPose`; `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs:23-40` |
| Other composite keys: `MapEntitySymbol` (entity + map group), `SensorTrackState` (observer + target), `DescriptorOptimisticLock` (entity + descriptor type) | inventory §A, §F |
| For ownership a *(descriptor type, instance)* pair is packed as `(type << 32) OR instance` | `FDP/Toolkits/Fdp.Toolkits/Replication/Extensions/OwnershipExtensions.cs:16-28` |
| **Entity ids are `int64` everywhere** *(changed by AQ107 E / R-4093, 2026-10-27; before it the older descriptors, 19 types, carried `int32`)*: every `EntityId` key and every field that names an entity (`EntityInfo.CommanderId`, `ContextActionInvoked.ContextEntityId`, the map selection lists `SelectedEntityIds` / `ForSelection`) is a `long`, in all 125 wire types; `0` = invalid. ⚠ **Node ids stay `int32`** (`AppInstanceId`, `NodeId`, the owner ids of `OwnershipUpdate`); a receiver built before AQ107 E reads an `EntityId` key at the wrong width | `scripts/ned-wire-inventory.py --verbose` key members (every entity key is `long`); `Replication/Map/Egress/GeoSpatialEgressTranslator.cs:226` writes `netId.Value` with no cast; rail `EntityMasterTranslatorTests.AQ107E_AnEntityIdAboveInt32_SurvivesEgressDdsAndIngress` |

### 5. How entity ids are allocated

| Rule | Evidence |
|---|---|
| Ids come from a **central DDS id-allocator server** (blocks of ids): client writes `IdRequest` (alloc / reset / status), server answers `IdResponse` keyed by client id and publishes `IdStatus` (highest id) — topic names in §Inventory E | `FDP/Network/Fdp.Network.Cyclone/Topics/IdAllocTopics.cs:20-60`; `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocatorServer.cs:13-100`; client `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocator.cs:22` |
| The server is hosted by the **orchestrator** (`CreateIdAllocatorServer`) | `Factory/NedNetworkFactory.cs:336`; `Factory/HostedIdAllocatorServer.cs:17`; `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocatorServer.cs:107-111` |
| **One allocator per DDS domain.** The topics carry no drill/session id | `FDP/Network/Fdp.Network.Cyclone/Topics/IdAllocTopics.cs` (no such member). *Differs from the spec's "multi-drill capable"* — see §Code versus the spec |
| At a **world boundary** the master resets the counter to **1000** and broadcasts `Resp_Reset`: entity ids **repeat across worlds/runs**, they are not globally unique over time | `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocatorServer.cs:119-136`; §[Deterministic Network Ids](../DESIGN_Deterministic_Network_Ids.md) 11 |
| `Req_Reset` sets the counter to the request's start value (empty client id = global reset + broadcast); the counter starts at 1 | `FDP/Network/Fdp.Network.Cyclone/Services/DdsIdAllocatorServer.cs:26,90-100` |

### 6. Instance lifecycle: creation, dispose, crash

| Rule | Evidence |
|---|---|
| Creation order on the wire is **not a protocol**: a receiver makes a *ghost* from the first sample of any descriptor, promotes it when the master and the mandatory descriptors are present; a ghost with no master after **3600 frames (60 s at 60 Hz)** is destroyed. HROT's own creator sends `DeferredTakeOwnership` first, then the master, then `WorldPos`/`EntityInfo` as "the guaranteed baseline" | `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostTimeoutSystem.cs:20`; §[Entity Genesis](../DESIGN_Entity_Genesis_End_To_End.md) 1.2. Spec L182-185 says "EntityMaster last, preferably" |
| **Delete = the owner disposes every descriptor instance it wrote** (`DisposeAll` over all translators), master included; also on a world wipe (owned entities only) | `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs:210`; world wipe `:68-75` |
| A **receiver acts only on the EntityMaster dispose** (→ `DestroyEntityCommand`); every other descriptor's not-alive sample is skipped. A delete from a node that is not the entity's *known* primary owner is ignored | `EntityMasterIngressTranslator.cs:115-121,161-190`; e.g. `Replication/Map/EntityWeatherTranslators.cs:155` |
| **Crash of a partial owner**: HROT does *not* react to a descriptor dispose. Every node watches `NodeHeartbeat` / `NodeCapabilities` instances; when a node's instance goes not-alive each node *computes locally* that its descriptors return to the master's owner. **No message is sent** | `Hrot/Network/Hrot.Network.NED/Routing/ClusterCapabilityIngestSystem.cs:31,82-100`; `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/PartialOwnerReclaimSystem.cs:39`; §[Ownership Groups](../DESIGN_Ownership_Groups_And_Grants.md) 5.3. *Differs from the spec's dispose-based rule* |
| **Every descriptor topic sets `[DdsQos(AutodisposeUnregisteredInstances = false)]`** *(CE-4079 ②, CycloneDDS.NET 1.2.20)*: a writer that **unregisters** an instance only releases its own claim on it — readers see `NotAliveNoWriters` once no writer is left, never `NotAliveDisposed`; only an explicit **dispose** deletes. Message topics keep the library default (closing a writer disposes what it sent). The creator of an entity that granted a descriptor at creation unregisters its instance once the grantee has confirmed, so the grantee is the **sole writer** and its writer going away reads `NotAliveNoWriters` | `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs` (`ReleaseHandedOver`); `IDescriptorTranslator.ReleaseInstance`; §[Ownership Groups](../DESIGN_Ownership_Groups_And_Grants.md) 5.3 "As built: the giver lets go"; rail `Hrot.Network.NED.Tests/TheDescriptorTopicsDoNotAutodisposeTests.cs` |
| Reliable-init: a creator may hold the entity *Constructing* until peers publish `EntityLifecycleStatus` (key = entity + node) | `FDP/Network/Fdp.Network.Cyclone/Topics/EntityLifecycleStatusDescriptor.cs:24`; §[Barrier](../DESIGN_Cross_Node_Construction_Barrier.md) |

### 7. Ownership

| Rule | Evidence |
|---|---|
| Ownership is **per descriptor** (and per instance); the owner is the node that writes it; **only the owner publishes** — enforced by the *writer* (a send gate on the node's own claim), not by DDS | Spec L129-137; `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneEgressSystem.cs:19`; §[Q79](../blueprints/Architect_Question_79_One_Ownership_Truth.md) 0.7 (push-only) |
| **Default owners** are *role groups*: Brain group (mission, navigation intent, health, sensor/EQS config, brain intent, most state descriptors), MuscleGround group (`WorldPos`, `NavigationStatus`, turret pose, hit marks, descent), Perception group (EQS results); the **creator** keeps `EntityMaster`, `EntityInfo`, map descriptors | §[Ownership Groups](../DESIGN_Ownership_Groups_And_Grants.md) 2 |
| **Handoff**: any node sends `OwnershipUpdate` (topic `Fdp_Network_Cyclone_Topics_OwnershipUpdate`, no key, `Reliable·Volatile·KeepAll`); the old owner stops writing **without dispose**; the new owner writes the descriptor to confirm. *(HROT's creator additionally **unregisters** a descriptor it granted at creation once the grantee confirmed — §6, auto-dispose row; a general `OwnershipUpdate` transfer does not, there is no confirmation signal)*. **External nodes use this very message** (no bridge, no mapping) | `FDP/Network/Fdp.Network.Cyclone/Topics/OwnershipUpdate.cs:10`; `Replication/Map/OwnershipUpdateTranslator.cs:41,120`; R-166 in [RULINGS](../blueprints/RULINGS.md); §[Ownership Transfer](../DESIGN_Entity_Ownership_Transfer.md) |
| The owner ids in `OwnershipUpdate` and in `DeferredTakeOwnership` are **node ids** = `AppInstanceId` of the sender identity (a single `int32`), the same value the receivers record as `PrimaryOwnerId` | `OwnershipUpdateTranslator.cs:80-90`; `EntityMasterIngressTranslator.cs:252` |
| The receiver of an `OwnershipUpdate` drops its own echo (`OriginNodeId == local`) | `OwnershipUpdateTranslator.cs:120` |
| **Grants at creation**: `DeferredTakeOwnership` (keyed by entity; list of *(descriptor type id → node id)*) arrives *before* the master; the target claims after promotion. HROT-specific — not in the spec | `Hrot/Network/Hrot.Network.NED/Messages/DeferredTakeOwnership.cs:39-75`; `Systems/DeferredTakeoverSystem.cs:44` |
| Changing a descriptor you do **not** own: send `UpdateEntityDescriptorRequest` (entity, descriptor type, part id, expected version, payload union) → `UpdateEntityDescriptorAck`; versions live in `DescriptorOptimisticLock` | `GenericMessages.cs:218-250`; `GenericDescriptors.cs:206` |
| One-shot actions on an entity you do not own travel to its owner as a typed request (`EntityInteractionRequest`); there is **no reply** — the result reappears as the target's own state | `Hrot/Network/Hrot.Network.NED/InteractionMessages.cs:265` |

### 8. QoS kinds (what each means for a late joiner)

The attribute `[DdsQos]` also carries `AutodisposeUnregisteredInstances` *(library 1.2.20; default `true`)*: **`false` on every descriptor topic**, default on messages (§6). The codes below describe Reliability, Durability and History only. The inventory's QoS column uses these codes:

| Code | Reliability · Durability · History | A late joiner... | Used for |
|---|---|---|---|
| `S` | Reliable · TransientLocal · KeepLast(1) | **gets the latest value of every instance** | state descriptors, singleton cluster state |
| `SL` | BestEffort · TransientLocal · KeepLast(1) | gets the latest retained value; loss repaired by next change or heartbeat | `WorldPos`, turret pose, hit marks, manual drive, node heartbeat |
| `SN` | Reliable · TransientLocal · KeepLast(N>1) | gets the last N (N = 64 or 100) | `SysOpStatus`, library demo master |
| `SA` | Reliable · TransientLocal · KeepAll | gets everything retained | `DescriptorOptimisticLock` |
| `Q` | Reliable · Volatile · KeepAll | **gets nothing**; no sample of a burst is lost | requests, acks, shots, detonations, commands |
| `QN` | Reliable · Volatile · KeepLast(N) | gets nothing; bounded burst | `MissileWarning` |
| `L` | BestEffort · Volatile · KeepLast(N) | gets nothing; cosmetic or high rate | `NearMiss`, `MeleeStrike`, `RoundImpact`, `AudioTargetDetected`, `SensorTargets`, library demo state |
| `B` | Reliable · Volatile · history not stated | gets nothing | raycast and path batches |
| `D` | **no `[DdsQos]` (or history only)** — library default; measured effect: *a reader keeps ONE sample*, so two events in one take overwrite each other | gets the library default | 9 types; the fire-pipeline events were moved off it for exactly that loss (`FireInteractionMessages.cs:27-29`) |

Counts over the 125 types: S 66 · Q 31 · D 9 · SL 5 · L 6 · B 4 · SN 2 · SA 1 · QN 1. The exact default of `D` is **not determinable from sources** (library 1.2.20); the "one sample" effect is the measured statement in `Hrot/Network/Hrot.Network.NED/FireInteractionMessages.cs:27-29` (CE-3095).

### 9. Descriptors versus messages

| Rule | Evidence |
|---|---|
| **Descriptor** = keyed state, retained (`S*`), has an owner, moves by `OwnershipUpdate`, deleted by dispose. **Message** = event, request, ack or command, volatile (`Q*`, `L`, `B`), has no owner, is never disposed. Inventory kind: 67 descriptors, 58 messages *(was written 69 / 59 — recounted from the Kind column, CE-4079)* | inventory column "Kind" (rule: keyed ∧ TransientLocal ⇒ descriptor, with four manual exceptions listed in §Not determinable #10) |
| Messages carry the originating node id (`SourceNodeId`) so every node drops its own sample when it reads the topic back | `FireInteractionMessages.cs` (WeaponFire, RoundImpact, MunitionDetonation, EntityHitDamage) |
| A request is answered by an ack keyed by a **request id** (GUID or uint) — `CreateEntityRequest` → `CreateUpdateDeleteEntityAck`; `MissionControlRequest` → `MissionControlAck`; `ClusterOpRequest` → `SysOpStatus`; `WeaponFireRequest` → `WeaponFire` (carries the id and a refusal code) | `GenericMessages.cs:158-320`; `Hrot/Network/Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs:191-215` |

### 10. Time and session control

| Rule | Evidence |
|---|---|
| **Simulation time is cluster-synchronised.** Each position sample carries the sender's *sim time* (not wall time) in `WorldPos.SimTime`: **sim seconds as a `double`** *(AQ107 D, 2026-10-27; before it: `DateTime` ticks plus one, 0 = "unspecified" — gone, sim time 0.0 is a legitimate stamp)* | `Hrot/Network/Hrot.Network.NED/SimDescriptors.cs` (`WorldPos`); `Replication/Map/Egress/GeoSpatialEgressTranslator.cs` (`WorldPosCodec.Encode(…, simNowSeconds, …)`) |
| Clock sync: slave sends `TimeSyncRequest` (keyed by client node id), master answers `TimeSyncResponse` with the four NTP timestamps; PLL-synchronised virtual wall clock | `FDP/Toolkits/Fdp.Toolkits/Time/Messages/TimeMessages.cs:202-235` |
| **Pause / resume / step / time scale** are operator requests: `ClusterOpRequest` with op `PauseTime`(10) / `ResumeTime`(11) / `StepTime`(14) / `SetTimeScale`(15); the master executes them by switching time mode at a wall-clock barrier (`SwitchTimeModeEvent`: continuous ↔ deterministic, with sim-time snapshot and scale) and, in deterministic mode, by lock-step `FrameOrder` / `FrameAck` | `Hrot/Network/Hrot.Network.NED/ExCon/NedTimeControlGateway.cs:24-60`; `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs:429`; `OrchestrationMessages.cs:29-72`; `TimeMessages.cs:8-160` |
| **Cluster state machine** is the retained singleton `ClusterState` (`S`): Idle · Loading/Operating/Unloading × {Edit, Preview, Live, Replay, Review} · Degraded; carries exercise id, **session id**, replay axis. Transitions are requested by `ClusterOpRequest` op `TransitionState` (JSON payload with the target state) and driven by `NodeOpCommand` / `NodeOpStatus` per node | `OrchestrationMessages.cs:5-22,121-140`; payload type `Hrot/Network/Hrot.Network.Orchestration/NodeOpMasterTranslator.cs:125`. The JSON payload schema per operation: **not determinable from sources** as one document (see the senders, e.g. `Hrot/Runner/Hrot.ClusterRunner/Testing/OrchestratorActionHandlers.cs:140`) |
| Liveness: `NodeHeartbeat` (BestEffort retained) every node (carries the unique `NodeName`, CE-3354 ②; empty from an older sender); static roles/features in `NodeCapabilities`; a node whose session id differs from the master's drops to Idle | `OrchestrationMessages.cs:339-380` |
| **The frame origin is the retained, keyless `GeoReference`** (`S`): the geodetic origin of the local ENU frame, published by the **cluster master only** (never a CGF) — see §11 *(AQ107 C, R-4094)* | `OrchestrationMessages.cs` (`GeoReference`); `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.GeoReference.cs` |
| Scenario time of day: `EntityScenarioClock` on the world-environment entity (UTC ticks at sim time 0 + display offset); weather `EntityWeather` | `SimDescriptors.cs:140-170` |

### 11. Coordinate frames and units

| Rule | Evidence |
|---|---|
| **Every position on the wire is LOCAL ENU metres** — X east, Y north, Z up, relative to the one origin, as `Vec3d` (double): `WorldPos.Position`, `Waypoint.Position` (map routes), `MapVisualOverlay.Points` (**absolute**, not offsets), `NavigationIntent.FinalDestination`, the three `BatchOrigin` anchors (the `RelativeVector3` offsets stay relative to them), `MapClickEvent.Position`, `DragEvent.CurrentPosition`. `GeoPoint` (lat/lon/alt) is **no longer a wire type** *(AQ107 B, R-4092, 2026-10-27)*. The JSON / binary **attribute route is local too** *(CE-4073, 2026-10-27)*: `Position.X` / `Position.Y` / `Position.Z` (`AttributeIds.PositionX/Y/Z` = 14/15/16, local ENU metres, a plain copy per axis into `SimTransform.Position`, registered on every host); ids 10–12 (`GeoLat/GeoLon/GeoAlt`) are retired and never reused, and the `GeoPosition.*` JSON paths are gone. The protocol-neutral ExCon/IG DTOs (`MapClickEventDto`, `CreateEntityCommand`, route waypoints, the move-update JSON `x/y/z`) carry the same local metres — `NeutralGeoBoundary` is deleted, so the NED network path has **zero** geodetic conversions. Lat/lon survives only where a person reads it (status-bar readouts) and in the `GeoReference` gateway origin | `Hrot/Network/Hrot.Network.NED/SimDescriptors.cs` (`WorldPos`, `NavigationIntent`, `*Batch`); `MapDescriptors.cs`; `MapMessages.cs`; `Replication/Map/Utils/EnuWire.cs` |
| **Orientation** = a **quaternion**, sent as its vector part `OrientationXyz` (`OrientationQuat`: x, y, z — the bytes of a `Vec3f`, a type of its own only for display) with **w implied**: `w = +sqrt(max(0, 1 − |v|²))`; the sender negates `q` when `w < 0`, so an **all-zero struct is the identity**, never NaN. The rotation is the ECS `SimTransform.Rotation` — body axes expressed in the local ENU frame (so a nose straight up or down round-trips; CE-4068) *(AQ107 D)*. No Euler angles on the wire. ⭐ `ddsmonitor` nevertheless DISPLAYS it as `HPR [045.0, 3.2, -1.0] deg, Quat [0.010, 0.030, 0.380, 0.924]` (compass heading, pitch nose-up, roll right-wing-down, then x, y, z and the implied w — `OrientationDisplay`, hand-written `ToString` / `GetFormatTokens` on `OrientationQuat`); it is a quaternion on the wire and in the JSON capture (`{"X","Y","Z"}`) | `FDP/Toolkits/Fdp.Toolkits/Replication/Components/QuaternionWireCodec.cs`; `Replication/Map/Utils/WorldPosCodec.cs` |
| **Velocity** is a Cartesian vector in the local ENU frame: `Velocity` (`Vec3f`, **m/s**) = `SimVelocity.Linear` as is; `AngularVelocity` (`Vec3f`, **rad/s**) = `SimVelocity.Angular` as is, about the local ENU axes (X east, Y north, Z up) — only vehicles fill it today (`CarKinematicsSystem.cs:720`), people and aircraft send 0 *(AQ107 D, §5a)* | `Replication/Map/Utils/WorldPosCodec.cs`; `GeoSpatialEgressTranslator.cs` |
| **Acceleration is not on the wire** — it was always published as zero and was dropped with the `WorldPos` reshape *(AQ107 D; appendable later)* | `SimDescriptors.cs` (`WorldPos`) |
| **Events and commands carry LOCAL metres too** ("world metres": X east, Y north, Z up, relative to the node's geo origin) — now the same frame as every state position: aim point, shot origin and velocity, hit point (field comments say "world metres" / "world-space"). The other event positions (near miss, round impact, missile warning, EQS points, `FireInteractionEvent` X/Y) do not state their frame — presumably the same local frame, **not determinable from the comments** | `FireInteractionMessages.cs` (field comments); `FDP/Toolkits/Fdp.Toolkits/Geographic/Transforms/WGS84Transform.cs:8` |
| The local frame is a **flat ENU tangent plane at one origin**, accurate "< 100 km from origin" (`WGS84Transform.cs:8-9`). **The origin is published**: the retained, keyless `Hrot.NED.Descriptors.Orchestration.GeoReference` (topic `Hrot_NED_Descriptors_Orchestration_GeoReference` — derived, §Concepts 15; Reliable + TransientLocal + KeepLast 1 — a late joiner gets the newest). Fields: `OriginLatitudeDeg` / `OriginLongitudeDeg` (degrees) and `OriginAltitudeM` (metres above the **WGS84 ellipsoid**), `Frame` (`byte`; **0 = ENU tangent plane at the origin**, the only value), `TerrainName`, `Generation` (`int`; **+1 whenever a different origin is published**, the first publish is 1; republishing the same origin keeps it) and `ProtocolVersion` (`int`; the NED wire version, `NedWire.ProtocolVersion` = **1**) *(AQ107 C, R-4094)* | `OrchestrationMessages.cs` (`GeoReference`); `Orchestration/NedWire.cs`; `ClusterOpMasterTranslator.cs` (the writer); `docs/blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md` §3, §4 C |
| **Who publishes it, and from where.** One writer — the **cluster master** (`ClusterMaster`, the orchestrator; 🔒 user: *"there could be multiple CGF nodes and only one needs to publish the georeference, so it must be the orchestrator"*). **At a load** (a world-boundary load, when its round succeeds): the loaded terrain's declared `origin`, read from the terrain's own `terrain.json` through the one `TerrainCatalog` the nodes use (the NAS `terrain/` root, then the shipped terrains); a terrain that declares none ⇒ **0, 0, 0** (R-229). **At a replay entry:** the origin stored in the **recording's metadata** (the origin that run actually used; 0, 0, 0 when the recording pre-dates the field). ⚠ Not republished for a branch into live from a replay (the world continues), for a review (same run), or for a late joiner's catch-up. ⚠ An in-session terrain change that is not a cluster load (the editor's terrain picker) does not republish. Nothing in HROT reads it — every node holds the terrain | `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.GeoReference.cs`; `ReplayConsensusAggregator.cs`; `TerrainCatalog.ReadDefinition`; `RecordingMetadata.GeoOrigin`; §[Geo Origin](../DESIGN_Geo_Origin.md) 2 |
| **Units — ONE rule: SI on the wire** *(AQ107 I, R-4093)*. Distances **metres**, durations **seconds**, speeds **m/s**, angles **radians**, angular rates **rad/s**, temperature **kelvin**, pressure **pascal**, ratios and shares a **0…1 fraction**. **The one exception: `GeoReference.OriginLatitudeDeg` / `OriginLongitudeDeg` are DEGREES** (geodetic coordinates; the name says so). Not quantities, so not converted: integer enumerations, counters, ids, tick counts, and integer epoch timestamps (`OwnershipUpdate.Timestamp`, `SubsystemStatusAnnounce.Timestamp`: Unix milliseconds; `*Ticks`: 100-ns ticks). Time-stamped samples use **sim time** (`WorldPos.SimTime`, seconds). ⚠ The ECS components behind some descriptors keep another unit (compass degrees, °C, hPa, mm/h, a percent byte, minutes) — **the translator converts at the boundary** through `WireUnits`, so the wire is SI whatever the ECS holds. Converted by I (was → now): `EntityEngagementArea.LeftBearing/RightBearing/WatchBearing` degrees → **rad** (compass: 0 = north, clockwise; not normalised) · `EntityWeather` wind `W0From…W7From` degrees → **rad**, `GroundTemperature` °C → **K**, `LapseRate` K/km → **K/m**, `SeaLevelPressure` hPa → **Pa**, `PrecipitationRate` mm/h → **m/s** · `EntityWeaponAmmo.Heat0…Heat3` byte percent → **float 0…1** · `EntityScenarioClock.DisplayUtcOffsetSeconds` `short` minutes → **`int` seconds** · `SensorConfig.FovRad` and `DdsTrackedTarget.BearingRad` degrees → **rad** · `NodeHeartbeat.CpuUsage` percent → **0…1**. Already SI before I: `EntityTurretPose` (`Azimuth`, `Elevation`, `AzimuthRate`, `ElevationRate`: rad, rad/s), `EntityLookState.HeadYaw`, `EntityManualDrive.Yaw`, `DangerAreaWire.AngleRad`, `WorldPos`. ⚠ Not converted (not typed wire fields): the JSON payload strings — the engagement-area directive JSON (`EngagementAreaDirectiveJson`, bearings in degrees) and the attribute route (CE-4073) | `Hrot/Network/Hrot.Network.Orchestration/Orchestration/WireUnits.cs`; `SimDescriptors.cs`; `Replication/Map/EntityWeatherTranslators.cs`, `EntityEngagementAreaTranslators.cs`, `EntityWeaponAmmoTranslators.cs`, `EntityScenarioClockTranslators.cs`; `SimHost/PerceptionTranslators.cs` |
| **Dead reckoning is receiver-side and implicit**: position projected linearly from the sample's pose + velocity × (sim-now − sample stamp); no algorithm id or parameter set on the wire. Owner re-sends `WorldPos` on change (1 cm / quaternion-dot 0.9999 (~0.5°) / 5 cm·s⁻¹ thresholds) and **every 600 ticks (10 s at 60 Hz)** as a heartbeat | `FDP/Toolkits/Fdp.Toolkits/Replication/ReplicatedMotion.cs:20`; `GeoSpatialEgressTranslator.cs:125-131`; §[Dead Reckoning](../DESIGN_Dead_Reckoning.md) |

### 12. Entity typing: TKB type ids and DIS types

| Rule | Evidence |
|---|---|
| `TkbType` = integer (`int64`) id of a **template in the Template Knowledge Base**; **mandatory**. It decides what a receiver builds (components, mandatory descriptors) | `GenericDescriptors.cs:97-122`; `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/GhostPromotionSystem.cs:225` |
| The **catalog is not on the wire.** It is code-registered at node start (`NedTkbCatalog`, `UrbanCombatTkbCatalog`, civilian/munition/effect/sound/voice/map-camera/area-effect catalogs) and, when present, replaced by a **staged TKB file** synced to all nodes | `Hrot/Engine/Hrot.Core/HrotEnvironment.cs:38-58`; `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/NedTkbCatalog.cs:10`; `Hrot/Engine/Hrot.Core/Tkb/*Catalog.cs`; interface `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs:10`. The file format/location: **not determinable from sources** here (the R-4018 "NAS master copy" is only referenced in `HrotEnvironment.cs:38`) |
| `DisType` is the DIS enumeration (kind.domain.country.category.subcategory.specific.extra) and is **optional per template** (`TkbTemplate.DisType`); the master publishes whatever the template/cold metadata holds (zeros when unset) | `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs:158`; `EntityMasterEgressTranslator.cs:87-110` |
| Munition/effect/sound types in events are **also TKB ids** (`MunitionType`, `AmmoGuid`) | `FireInteractionMessages.cs` |

### 13. Force / side

`EntityInfo.ForceIdentifier` (`eForceIdentifier`: UNKNOWN 0, FRIENDLY 1, OPPOSING 2, NEUTRAL 3) is the only side marker on an entity; the EQS family carries a faction bitmask instead (`FactionFilter`). No DIS-style "force id" on the master. `GenericDescriptors.cs:133-139`, `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs:39`.

### 14. Recording and replay — what changes on the wire

| Rule | Evidence |
|---|---|
| **Recordings are ECS flight-recorder frames, not DDS captures** (the in-flight `.dds` capture was withdrawn). Replay therefore re-creates the *same topics* from the restored world: authoritative nodes republish; other nodes see ordinary samples | §[mgmt-1](../designs/mgmt-1/DESIGN.md) 8.10 and status block (R-3041) |
| Cluster state shows `LoadingReplay` / `OperatingReplay` / Review states, and the retained `ReplaySession` topic carries the replay's zero, length, time axis, marks | `OrchestrationMessages.cs:5-22,147-155`; §[Replay Time Axis](../DESIGN_Replay_Time_Axis_And_Review.md) |
| **After a backward seek, entities that did not survive are retracted by a dispose sweep** of the owning node, and unowned nodes purge their ghosts (inference, not measured: sim-time stamps on `WorldPos` also go backwards) | `CycloneNetworkCleanupSystem.cs:68-100,161`; §[mgmt-1](../designs/mgmt-1/DESIGN.md) 8.10 |
| Late joiners during replay still get retained state (`S`) from the republishing owners | inferred from `S` QoS; no separate evidence |

### 15. Schema and type names (for an external DDS client)

| Rule | Evidence |
|---|---|
| Types are generated to **IDL at build** into `<project>/obj/<cfg>/<tfm>/CycloneDdsGenerated/*.idl`, one file per `[DdsIdlFile("…")]` name (`hrot-generic-desc`, `hrot-sim-desc`, `hrot-map-desc`, `hrot-orchestration`, `hrot-anim-msgs`, …); **not committed** | `Hrot/Network/Hrot.Network.NED/obj/Debug/net8.0/CycloneDdsGenerated/hrot-generic-desc.idl` (build output) |
| The DDS **type name** is the C# namespace as IDL modules + type name (`Hrot::NED::Descriptors::EntityMaster`); extensibility is **`@appendable`**: new fields are only ever **appended** (marked "APPENDED" in comments), so an older reader keeps working | same generated IDL; R-169 in RULINGS |
| ⭐ **One rule, no exceptions: the topic name is CycloneDDS.NET's derived name of the C# type** *(AQ107 A, R-4092/R-4093)* — `CycloneDDS.Schema.DdsTopicName.Of<T>()`: **the namespace, the containing types and the type name, each part joined with `_`**. `Hrot.NED.Descriptors.WorldPos` is the topic `Hrot_NED_Descriptors_WorldPos`; a nested `A.B.Outer.Inner` is `A_B_Outer_Inner`. So the **Full name** column of §Inventory *is* the topic: replace every `.` (and the `+` of a nested type) with `_`. No `[DdsTopic]`, reader, writer, translator or helper in HROT writes a name (a bare `[DdsTopic]` is the only form; `DdsWriterAdapter` / `DdsCommandClient` take none), and no type has a name that differs from its own — `scripts/ned-wire-inventory.py --check` FAILS on an explicit name anywhere in HROT-owned code (the one place it does not look: vendored SumoSharp, R-4046). The same rule is used by the library's runtime, its code generator and DdsMonitor. An external client gets the name from the type's IDL name: for a top-level type, the DDS *type* name with `::` replaced by `_`. ⚠ A namespace or type rename is therefore a **wire break**. The one pair of spellings the rule cannot tell apart (`A.B` + `C` vs `A` + `B_C`) is rejected by `--check` and by the NED test `TheTopicNamesAreDerivedFromTypesTests` (no explicit name · all derived names unique · a name-less pair exchanges a sample on a private domain) | `Hrot/Network/Hrot.Network.NED.Tests/TheTopicNamesAreDerivedFromTypesTests.cs`; `scripts/ned-wire-inventory.py`; library `src/CycloneDDS.Schema/DdsTopicName.cs`; [AQ107](../blueprints/Architect_Question_107_NED_Wire_Frame_And_Names.md) item A |
| Some types are `[DdsManaged]` (strings, sequences, unions): variable length; a few payloads are **JSON text** (`ClusterOpRequest.PayloadJson`, EQS/survey results, attribute patches) | inventory; `GenericMessages.cs`, `OrchestrationMessages.cs` |

## Inventory

All **125** production wire types carry a bare `[DdsTopic]` — the topic name of each is DERIVED from its full name (§Concepts 15), so there is no topic column. Full names are
**reflection style** (`Namespace.Type`; a nested type would print `Namespace.Outer+Nested` — none exists today). Kind and QoS legend
are in §Concepts 8 and 9; "Keyed" names the key *concept* only. Roots scanned: `Hrot/`, `FDP/`, `Stride/`; excluded roots: see
§Excluded roots. Family counts are informational; `--check` compares the full names only (and fails on an explicit topic name anywhere in HROT-owned code).

### A. Entity descriptors (keyed state of an entity) — 37

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Hrot.NED.Descriptors.DescriptorOptimisticLock` | descriptor | yes: entity id + descriptor type | SA | Hrot.Network.NED | Version sidecar for optimistic descriptor updates |
| `Hrot.NED.Descriptors.EntityAreaEffectCloud` | descriptor | yes: entity id | S | Hrot.Network.NED | Gas or smoke cloud description |
| `Hrot.NED.Descriptors.EntityCombatProficiency` | descriptor | yes: entity id | S | Hrot.Network.NED | Soldier's combat proficiency values |
| `Hrot.NED.Descriptors.EntityCustody` | descriptor | yes: entity id | S | Hrot.Network.NED | Person detained, restrained or searched |
| `Hrot.NED.Descriptors.EntityDamage` | descriptor | yes: entity id | S | Hrot.Network.NED | Current and max health of the entity |
| `Hrot.NED.Descriptors.EntityDemeanour` | descriptor | yes: entity id | S | Hrot.Network.NED | Civilian mode, fear and anger |
| `Hrot.NED.Descriptors.EntityDescent` | descriptor | yes: entity id | S | Hrot.Network.NED | Body on a rope or under a canopy |
| `Hrot.NED.Descriptors.EntityDoorState` | descriptor | yes: entity id | S | Hrot.Network.NED | Live state of a terrain door |
| `Hrot.NED.Descriptors.EntityEffectState` | descriptor | yes: entity id | S | Hrot.Network.NED | Stored non-lethal effects of a person |
| `Hrot.NED.Descriptors.EntityEngagementArea` | descriptor | yes: entity id | S | Hrot.Network.NED | Sector assigned to a man or commander |
| `Hrot.NED.Descriptors.EntityGesture` | descriptor | yes: entity id | S | Hrot.Network.NED | Arm gesture or idle mood of a person |
| `Hrot.NED.Descriptors.EntityHitMarks` | descriptor | yes: entity id | SL | Hrot.Network.NED | Where rounds struck a body (ring) |
| `Hrot.NED.Descriptors.EntityHostileAct` | descriptor | yes: entity id | S | Hrot.Network.NED | A man's hostile act and its kind |
| `Hrot.NED.Descriptors.EntityInfo` | descriptor | yes: entity id | S | Hrot.Network.NED | Name, force/side, commander, tactical role |
| `Hrot.NED.Descriptors.EntityLean` | descriptor | yes: entity id | S | Hrot.Network.NED | Person leaning round a corner |
| `Hrot.NED.Descriptors.EntityLookState` | descriptor | yes: entity id | S | Hrot.Network.NED | Wanted look direction and head turn |
| `Hrot.NED.Descriptors.EntityManualDrive` | descriptor | yes: entity id | SL | Hrot.Network.NED | Operator motion axes for a manually driven body |
| `Hrot.NED.Descriptors.EntityMaster` | descriptor | yes: entity id | S | Hrot.Network.NED | Entity existence, TKB type, DIS type, flags |
| `Hrot.NED.Descriptors.EntityObstacleShape` | descriptor | yes: entity id | S | Hrot.Network.NED | Static obstacle box, applied before promotion |
| `Hrot.NED.Descriptors.EntityOperatorUseStatus` | descriptor | yes: entity id | S | Hrot.Network.NED | Verdict on the operator's last use press |
| `Hrot.NED.Descriptors.EntityPartDamage` | descriptor | yes: entity id | S | Hrot.Network.NED | Damage level per body part |
| `Hrot.NED.Descriptors.EntityPassengers` | descriptor | yes: entity id | S | Hrot.Network.NED | Passenger list of a vehicle |
| `Hrot.NED.Descriptors.EntityPossessedBy` | descriptor | yes: entity id | S | Hrot.Network.NED | Which operator station holds the unit |
| `Hrot.NED.Descriptors.EntityScenarioClock` | descriptor | yes: entity id | S | Hrot.Network.NED | Scenario start time and display UTC offset |
| `Hrot.NED.Descriptors.EntitySpawnLevel` | descriptor | yes: entity id | S | Hrot.Network.NED | Birth level and whether position owner applied it |
| `Hrot.NED.Descriptors.EntityTrafficDemand` | descriptor | yes: entity id | S | Hrot.Network.NED | Scenario traffic settings |
| `Hrot.NED.Descriptors.EntityTurretPose` | descriptor | yes: parent entity id + part instance id | SL | Hrot.Network.NED | Turret part pose, per part instance |
| `Hrot.NED.Descriptors.EntityUnderFire` | descriptor | yes: entity id | S | Hrot.Network.NED | When the man was last hit or nearly missed |
| `Hrot.NED.Descriptors.EntityUtterance` | descriptor | yes: entity id | S | Hrot.Network.NED | What a person says now |
| `Hrot.NED.Descriptors.EntityWeaponAmmo` | descriptor | yes: entity id | S | Hrot.Network.NED | Ammunition, breech and heat per weapon mount |
| `Hrot.NED.Descriptors.EntityWeaponPosture` | descriptor | yes: entity id | S | Hrot.Network.NED | Weapon posture: at ease, ready, aimed |
| `Hrot.NED.Descriptors.EntityWeaponSystemState` | descriptor | yes: entity id | S | Hrot.Network.NED | Weapon system deploy state |
| `Hrot.NED.Descriptors.EntityWeaponTask` | descriptor | yes: parent entity id + part instance id | S | Hrot.Network.NED | Standing order to one turret (Brain to Muscle) |
| `Hrot.NED.Descriptors.EntityWeaponTaskStatus` | descriptor | yes: parent entity id + part instance id | S | Hrot.Network.NED | Muscle's report on a turret order |
| `Hrot.NED.Descriptors.EntityWeather` | descriptor | yes: entity id | S | Hrot.Network.NED | Authored weather, global or regional |
| `Hrot.NED.Descriptors.GroundClampingOverride` | descriptor | yes: entity id | S | Hrot.Network.NED | Per-entity ground clamping override for the IG |
| `Hrot.NED.Descriptors.WorldPos` | descriptor | yes: entity id | SL | Hrot.Network.NED | Local-ENU pose: position, quaternion, velocity, angular velocity, sim time (AQ107 D) |

### B. Interactions and events (fire, impact, damage, interaction) — 11

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Hrot.NED.Descriptors.AudioTargetDetected` | message | no | L | Hrot.Network.NED | Sound contact heard by a listener |
| `Hrot.NED.Messages.EntityHitDamage` | message | no | Q | Hrot.Network.NED | Computed damage applied to a struck entity |
| `Hrot.NED.Messages.EntityInteractionRequest` | message | no | Q | Hrot.Network.NED | Actor asks the target's owner to apply an interaction |
| `Hrot.NED.Messages.FireInteractionEvent` | message | no | D | Hrot.Network.NED | Shooter-to-target line for the IG (2-D) |
| `Hrot.NED.Messages.MeleeStrike` | message | no | L | Hrot.Network.NED | A blow was struck (cosmetic) |
| `Hrot.NED.Messages.MissileWarning` | message | no | QN | Hrot.Network.NED | A guided missile was launched at an aircraft |
| `Hrot.NED.Messages.MunitionDetonation` | message | no | Q | Hrot.Network.NED | A round detonated at a point or entity |
| `Hrot.NED.Messages.NearMiss` | message | no | L | Hrot.Network.NED | A round passed close to a unit |
| `Hrot.NED.Messages.RoundImpact` | message | no | L | Hrot.Network.NED | A round marked a world surface (cosmetic) |
| `Hrot.NED.Messages.WeaponFire` | message | no | Q | Hrot.Network.NED | A shot was fired (or refused) |
| `Hrot.NED.Messages.WeaponFireRequest` | message | no | Q | Hrot.Network.NED | Brain asks Muscle to fire a weapon |

### C. Mission, AI, perception and navigation — 16

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Hrot.NED.Descriptors.EntityMission` | descriptor | yes: entity id | S | Hrot.Network.NED | Active mission plan of an entity |
| `Hrot.NED.Descriptors.NavigationIntent` | descriptor | yes: entity id | S | Hrot.Network.NED | Brain's navigation command (CQRS command side) |
| `Hrot.NED.Descriptors.NavigationStatus` | descriptor | yes: entity id | S | Hrot.Network.NED | Muscle's navigation feedback (CQRS status side) |
| `Hrot.NED.Descriptors.PathRequestBatch` | message | yes: source node id | B | Hrot.Network.NED | Batched path requests to the navigation solver |
| `Hrot.NED.Descriptors.PathResponseBatch` | message | yes: target node id | B | Hrot.Network.NED | Batched path answers |
| `Hrot.NED.Descriptors.RaycastRequestBatch` | message | yes: source node id | B | Hrot.Network.NED | Batched raycast requests to the navigation solver |
| `Hrot.NED.Descriptors.RaycastResponseBatch` | message | yes: target node id | B | Hrot.Network.NED | Batched raycast answers |
| `Hrot.NED.Descriptors.SensorConfig` | descriptor | yes: entity id | S | Hrot.Network.NED | Sensor configuration of an observer entity |
| `Hrot.NED.Descriptors.SensorTargets` | descriptor | yes: observer entity id | L | Hrot.Network.NED | Per-observer snapshot of detected targets |
| `Hrot.NED.Descriptors.SensorTrackState` | descriptor | yes: observer entity id + target entity id | S | Hrot.Network.NED | Contact detected or lost, per observer and target |
| `Hrot.NED.Messages.BehaviorFaultReport` | message | no | D | Hrot.Network.NED | A behaviour faulted; operator notification |
| `Hrot.NED.Messages.EntityBrainIntent` | descriptor | yes: entity id | S | Hrot.Network.NED | Unit's AI task, SOP and ROE |
| `Hrot.NED.Messages.MissionControlAck` | message | no | Q | Hrot.Network.NED | Reply to a mission control request |
| `Hrot.NED.Messages.MissionControlRequest` | message | no | Q | Hrot.Network.NED | Command to start, stop or change a mission |
| `Hrot.NED.Messages.SquadReport` | message | no | D | Hrot.Network.NED | Report up the chain of command |
| `Hrot.NED.Messages.TacticalIntentRequest` | message | no | D | Hrot.Network.NED | Commander brain to subordinate brain intent |

### D. Entity lifecycle, ownership and change requests — 10

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Fdp.Network.Cyclone.Topics.EntityLifecycleStatusDescriptor` | descriptor | yes: entity id + node id | S | Fdp.Network.Cyclone | Per-node readiness for reliable-init construction barrier |
| `Fdp.Network.Cyclone.Topics.OwnershipUpdate` | message | no | Q | Fdp.Network.Cyclone | Per-descriptor ownership handoff (the spec's OwnershipUpdate) |
| `Hrot.NED.Messages.CreateEntityRequest` | message | no | Q | Hrot.Network.NED | Ask the creator node to create an entity |
| `Hrot.NED.Messages.CreateUpdateDeleteEntityAck` | message | no | Q | Hrot.Network.NED | Unified ack for create, update and delete requests |
| `Hrot.NED.Messages.DeferredTakeOwnership` | message | yes: entity id | Q | Hrot.Network.NED | Pre-genesis grant table: who will own which descriptor |
| `Hrot.NED.Messages.DeleteEntityRequest` | message | no | Q | Hrot.Network.NED | Ask the owner to delete an entity |
| `Hrot.NED.Messages.EntityAttributeSchema` | descriptor | yes: node id | S | Hrot.Network.NED | JSON schema of attributes a node accepts |
| `Hrot.NED.Messages.UpdateEntityAttributeRequest` | message | no | Q | Hrot.Network.NED | JSON or binary attribute patch for an owned entity |
| `Hrot.NED.Messages.UpdateEntityDescriptorAck` | message | no | Q | Hrot.Network.NED | Result of a descriptor update request |
| `Hrot.NED.Messages.UpdateEntityDescriptorRequest` | message | no | Q | Hrot.Network.NED | Ask the owner to change a descriptor it owns |

### E. Id allocation and node identity — 5

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Fdp.Network.Cyclone.Topics.IdRequest` | message | yes: client id | Q | Fdp.Network.Cyclone | Entity-id block request to the allocator server |
| `Fdp.Network.Cyclone.Topics.IdResponse` | message | yes: client id | Q | Fdp.Network.Cyclone | Allocator's granted id block, reset broadcast |
| `Fdp.Network.Cyclone.Topics.IdStatus` | message | no | S | Fdp.Network.Cyclone | Highest entity id allocated so far |
| `Hrot.NED.Descriptors.Orchestration.NodeIdGrant` | message | yes: computer hash + process id | S | Hrot.Network.Orchestration | Master's node-id grant or refusal |
| `Hrot.NED.Descriptors.Orchestration.NodeIdRequest` | message | yes: computer hash + process id | S | Hrot.Network.Orchestration | Process asks the master for a node id |

### F. Map, gizmo and operator UI — 15

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Hrot.IG.Gizmos.IGCapabilitiesAnnounce` | descriptor | yes: node id | S | Hrot.IG | Same simple name as the NED type but a different type with a different layout, hence a different topic; its publisher is registered by no host and nothing reads it (§Not determinable 8) |
| `Hrot.NED.Descriptors.MapEntitySymbol` | descriptor | yes: entity id + map group id | S | Hrot.Network.NED | Per-map-group symbol override for an entity |
| `Hrot.NED.Descriptors.MapRoute` | descriptor | yes: entity id | S | Hrot.Network.NED | Waypoint route of an entity |
| `Hrot.NED.Descriptors.MapVisualOverlay` | descriptor | yes: entity id | S | Hrot.Network.NED | Tactical graphic overlay attached to an entity |

### G. Time, session and cluster orchestration — 21

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Fdp.Toolkit.Time.Messages.FrameAckDescriptor` | message | no | D | Fdp.Toolkits | Lock-step: node acknowledges a frame |
| `Fdp.Toolkit.Time.Messages.FrameOrderDescriptor` | message | no | D | Fdp.Toolkits | Lock-step: master orders the next frame |
| `Fdp.Toolkit.Time.Messages.SwitchTimeModeWireDto` | message | no | S | Fdp.Toolkits | Switch continuous / deterministic time at a barrier |
| `Fdp.Toolkit.Time.Messages.TimeSyncRequest` | message | yes: client node id | D | Fdp.Toolkits | Slave starts the NTP-style clock handshake |
| `Fdp.Toolkit.Time.Messages.TimeSyncResponse` | message | yes: client node id | D | Fdp.Toolkits | Master's reply with four timestamps |
| `Hrot.DDS.DataModel.Runner.SubsystemStatusAnnounce` | descriptor | yes: node id | S | Hrot.Network.NED | Subsystem presence for the waiting-room protocol |
| `Hrot.NED.Descriptors.Orchestration.AssetInventoryTopic` | descriptor | yes: node id | S | Hrot.Network.Orchestration | Scenario and exercise asset lists (every 5 s) |
| `Hrot.NED.Descriptors.Orchestration.ClusterOpRequest` | message | no | Q | Hrot.Network.Orchestration | Operator request: state change, pause, step, save ... |
| `Hrot.NED.Descriptors.Orchestration.ClusterOpStatus` | message | no | SN | Hrot.Network.Orchestration | Result of a cluster operation (SysOpStatus) |
| `Hrot.NED.Descriptors.Orchestration.ClusterStateTopic` | message | no | S | Hrot.Network.Orchestration | Cluster state machine state and session id |
| `Hrot.NED.Descriptors.Orchestration.FileAck` | message | yes: transfer id + node id + file index | Q | Hrot.Network.Orchestration | Target's transfer progress or verdict |
| `Hrot.NED.Descriptors.Orchestration.FileChunk` | message | yes: transfer id | Q | Hrot.Network.Orchestration | 64 KB file piece in the transfer's partition |
| `Hrot.NED.Descriptors.Orchestration.FileNeed` | message | yes: transfer id + node id | Q | Hrot.Network.Orchestration | Target lists the files it lacks |
| `Hrot.NED.Descriptors.Orchestration.FileOffer` | message | yes: transfer id | Q | Hrot.Network.Orchestration | Master offers a file transfer to targets |
| `Hrot.NED.Descriptors.Orchestration.GeoReference` | message | no | S | Hrot.Network.Orchestration | Origin of the local ENU frame (retained; published by the cluster master) |
| `Hrot.NED.Descriptors.Orchestration.NodeCapabilitiesTopic` | descriptor | yes: node id | S | Hrot.Network.Orchestration | Static capability and role tokens of a node |
| `Hrot.NED.Descriptors.Orchestration.NodeHeartbeat` | descriptor | yes: node id | SL | Hrot.Network.Orchestration | Node liveness and cluster state telemetry |
| `Hrot.NED.Descriptors.Orchestration.NodeOpCommand` | message | yes: target node id | Q | Hrot.Network.Orchestration | Master's per-node operation command |
| `Hrot.NED.Descriptors.Orchestration.NodeOpStatus` | message | no | Q | Hrot.Network.Orchestration | Node's reply to a node operation |
| `Hrot.NED.Descriptors.Orchestration.OrchestratorContextTopic` | message | no | S | Hrot.Network.Orchestration | Exercise, scenario and required nodes context |
| `Hrot.NED.Descriptors.Orchestration.ReplaySessionTopic` | message | no | S | Hrot.Network.Orchestration | Replay session layer: zero, length, axis, marks |

### H. EQS (environment-query sensors) — 4

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Fdp.Toolkit.Perception.Survey.Topics.WorldSurveyResultTopic` | descriptor | yes: parent entity id + child instance id | S | Fdp.Toolkits | World-survey sensor answer (JSON payload) |
| `Fdp.Toolkit.Spatial.Eqs.Topics.EqsResultTopic` | descriptor | yes: parent entity id + child instance id | S | Fdp.Toolkits | Ranked EQS results back to the brain |
| `Fdp.Toolkit.Spatial.Eqs.Topics.EqsSensorConfigTopic` | descriptor | yes: parent entity id + child instance id | S | Fdp.Toolkits | Brain's EQS sensor parameters, per child sensor |
| `Fdp.Toolkit.Squad.DangerArea.Topics.DangerAreaResultTopic` | descriptor | yes: parent entity id + child instance id | S | Fdp.Toolkits | Danger-area sensor answer |

### I. Animation — 4

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Hrot.Animation.Replication.DdsLookAtChannelIntent` | descriptor | yes: entity id | S | Hrot.Animation.Replication | Look-at channel intent, Brain to Muscle |
| `Hrot.Animation.Replication.DdsLookAtChannelStatus` | descriptor | yes: entity id | S | Hrot.Animation.Replication | Look-at channel status, Muscle to Brain |
| `Hrot.Animation.Replication.DdsStanceIntent` | descriptor | yes: entity id | S | Hrot.Animation.Replication | Wanted stance, Brain to Muscle |
| `Hrot.Animation.Replication.DdsStanceStatus` | descriptor | yes: entity id | S | Hrot.Animation.Replication | Current stance and transition, Muscle to Brain |

### J. Other (FDP library demo topics) — 2

> The former BDC stack's four `BDC_*` types left with it — **removed 2026-10-28, R-4095** (NED is the one network implementation).

| Full name | Kind | Keyed | QoS | Assembly | Role |
|---|---|---|---|---|---|
| `Fdp.Network.Cyclone.Topics.EntityMasterTopic` | descriptor | yes: entity id | SN | Fdp.Network.Cyclone | FDP library demo master; no HROT production use |
| `Fdp.Network.Cyclone.Topics.EntityStateTopic` | descriptor | yes: entity id | L | Fdp.Network.Cyclone | FDP library demo state; no HROT production use |


## Excluded roots

`scripts/ned-wire-inventory.py` skips these roots — **excluded from §Inventory, still real on the wire in some deployments**:

| Root | Why excluded | What is there |
|---|---|---|
| any `*.Tests` / `Tests` project | test doubles and rails, not product protocol | 4 types (`ned-wire-inventory.py --all` lists them) |
| `FDP/Examples/**` | demo programs | 8 types (`Fdp.Examples.DDS` 5, `Fdp.Examples.DER` 3); derived names, no explicit name |
| `docs/blueprints/experiments/**` | experiments, not built into the product; outside the three scanned roots | 4 bare `[DdsTopic]` types in one file (found by grep, not by the script); names derived like the rest |
| `FDP/ExtDeps/GizmoMap` | an in-tree library that HROT edits in place (no sync script, no upstream pin — unlike SumoSharp), so its topic types follow the same rule: bare `[DdsTopic]`, derived names (`GizmoMap_Network_GizmoInteractionBatch`, `…_DebugPrimitivesBatch`, `…_GizmoUiState`, `…_StringInternEntry`); **its topics ride the same domain**. A second `EntityAttributeSchema` type that used to live here (the same wire shape as `Hrot.NED.Messages.EntityAttributeSchema`, nothing wrote or read it) was deleted by AQ107 A | 4 types |
| `FDP/ExtDeps/SumoSharp` | vendored SUMO traffic bridge; **SUMO vehicles are not NED entities** — they travel on `sumo/*` topics — ⛔ **the one place explicit names remain: vendored, read-only (R-4046), so AQ107 A leaves them** (`sumo/vehicles`, `sumo/vehicles-structured`, `sumo/lifecycle`, `sumo/geometry`, `sumo/tl`, `sumo/commands`, `sumo/status`, `sumo/ped-patharc`, `sumo/ped-lifecycle`, `sumo/ped-crowd`, `sumo/ped-activity`) | 11 types; §[Q93](../blueprints/Architect_Question_93_Sumo_Traffic_Node.md) |
| `bin/`, `obj/` | build output | — |

## For a protocol gateway (DIS/HLA)

### What a gateway must do to be a NED node

0. **Read `GeoReference` first** (retained — a late joiner gets it at once) and refuse the wire when `ProtocolVersion` is not one you know. It fixes the local frame: with `lat, lon, alt` = `OriginLatitudeDeg`, `OriginLongitudeDeg` (**degrees** — the one non-SI field on the wire) and `OriginAltitudeM` (metres above the WGS84 ellipsoid), and `Frame` = 0 (ENU tangent plane — refuse anything else), `R` the rotation from the local ENU axes (X east, Y north, Z up) to ECEF at the origin — `R = [ −sin λ, −sin φ·cos λ, cos φ·cos λ ; cos λ, −sin φ·sin λ, cos φ·sin λ ; 0, cos φ, sin φ ]` (columns east, north, up; φ = latitude, λ = longitude) — convert **`ECEF = ECEF(lat, lon, alt) + R · local`** for every position, and rotate velocities, angular velocities and orientations (`q_ecef = q_R · q_local`) by the same `R` (the standard geodetic-to-ECEF formula for `ECEF(lat, lon, alt)`; this is the same transform as `WGS84Transform`; ✅ checked 2026-10-27 by the rail `WGS84TransformGatewayFormulaTests` (Fdp.Toolkits.Tests): an independent double-precision implementation of exactly this formula, with a textbook ECEF → geodetic, agrees with `WGS84Transform.ToGeodetic` to 0.01 mm at 0 km, 0.14 mm at 1 km, 1.3 mm at 10 km and 6.1 mm at 50 km from origins at 0°, 52.5° N and 70° S — ⚠ the residual is **float32 rounding inside HROT's own transform** (its `Matrix4x4` rotation and `Vector3` result are `float`: ≈1.2e-7 × distance), not an error of the formula; a gateway computing it in `double` has none of it). A new sample with a higher **`Generation`** means a new origin: re-base every entity from it. Time of day: `EntityScenarioClock` start + `WorldPos.SimTime`.
0a. **Subscribe by the derived topic names** (§Concepts 15: the type's full name, dots → `_`, e.g. `Hrot_NED_Descriptors_WorldPos`) with the **same type definitions** as HROT — reference HROT's type assemblies through CycloneDDS.NET 1.2.20, or generate from the IDL its code generator emits. ⚠ A HROT namespace or type rename is a wire break, and nodes built before and after one do not talk.
1. Pick or request a **node id** (`NodeIdRequest` → `NodeIdGrant`, or a fixed id outside the ranges above) and **enable sender identity** with `(domain id, node id)` — otherwise HROT cannot tell who owns what.
2. Get **entity ids** from the allocator (`IdRequest` / `IdResponse` / `IdStatus`, a string client id); ids restart at 1000 after a world reset.
3. To *introduce* an entity: publish `EntityMaster` (with a `TkbType` that exists in the receivers' TKB — otherwise the replica skips its mandatory-descriptor gate), `EntityInfo` and `WorldPos`, then whatever else it owns; **keep re-sending `WorldPos`** (HROT does every 10 s). To *ask HROT* to create one instead: `CreateEntityRequest` → `CreateUpdateDeleteEntityAck`.
4. To *remove* an entity: dispose its `EntityMaster` (and its other instances). Never dispose a descriptor to hand it over — send `OwnershipUpdate`.
5. Never write a descriptor you do not own; to change someone else's: `UpdateEntityDescriptorRequest` / `UpdateEntityAttributeRequest`.

### Concept mapping: DIS PDU families and RPR FOM classes ↔ HROT topics

| DIS / RPR concept | HROT counterpart (full type names) | Gap or caution |
|---|---|---|
| Entity State PDU / `BaseEntity.PhysicalEntity` | `Hrot.NED.Descriptors.EntityMaster` + `EntityInfo` + `WorldPos` (+ `EntityDamage`, `EntityPartDamage`, `EntityTurretPose`) | no single message; assemble from 3+ topics keyed by entity id |
| Entity ID (site, application, entity) | HROT entity id (`int64`) + owner node id (`AppInstanceId`) | **not carried**; the gateway maps an id range |
| Entity Type (kind.domain.country.category.subcategory.specific.extra) | `EntityMaster.DisType` (optional) + `TkbType` (mandatory) | `DisType` may be zeros; the TKB↔DIS table is **not on the wire** |
| Marking | `EntityInfo.Name` (free string) | DIS marking is 11 characters |
| Force ID | `EntityInfo.ForceIdentifier` | values 0..3 (UNKNOWN, FRIENDLY, OPPOSING, NEUTRAL); the numbering coincides with the DIS Force ID values Other, Friendly, Opposing, Neutral (SISO-REF-010, from general knowledge — not checked in this repo) |
| Location (ECEF metres) | `WorldPos.Position`: **local ENU metres** (`Vec3d`) | convert: `ECEF = origin_ECEF + R(origin) · Position` — the origin is the retained `GeoReference` (step 0 below; AQ107 C; flat ENU plane, ≈ valid < 100 km) |
| Orientation (psi, theta, phi, geocentric radians) | `WorldPos.OrientationXyz` (`OrientationQuat`): quaternion vector part, **local ENU**, w implied | convert: rebuild `q`, rotate by `R(origin)`, then to DIS Euler; **GAP**: not geocentric, not Euler |
| Linear velocity (ECEF m/s) | `WorldPos.Velocity`: m/s, **local ENU** vector; `AngularVelocity` rad/s about the ENU axes | convert: rotate by `R(origin)` |
| Linear acceleration | **not on the wire** (dropped with AQ107 D; it was always zero) | **GAP** (never real) |
| Dead-reckoning algorithm + parameters | none; receiver-side linear projection from sim-time stamp | **GAP**: pick DIS algorithm FPW (position + velocity) yourself |
| Entity appearance, damage state | `EntityDamage` (current / max health, floats), `EntityPartDamage`, `EntityEffectState` | thresholds to DIS's 4 damage states are **not defined** by HROT |
| Articulated parts | `EntityTurretPose` (per turret part, azimuth/elevation) | other parts: none |
| Fire PDU | `Hrot.NED.Messages.WeaponFire` (the fact) ← `WeaponFireRequest` (the order) | positions in **local metres**; munition = TKB id; event id = `RequestId` (32-bit), no GUID |
| Detonation PDU | `Hrot.NED.Messages.MunitionDetonation` (+ `EntityHitDamage` for the damage result) | no result enum: hit entity 0 = ground impact; detonation location local metres |
| Collision PDU | **none** — searched `search_graph Class ".*Collision.*"` (only editor/test/example hits) and `grep -rn Collision Hrot/Network/Hrot.Network.NED` | **GAP** |
| Start/Resume, Stop/Freeze (Simulation Management) | `ClusterOpRequest` ops `ResumeTime` / `PauseTime` / `StepTime` / `SetTimeScale`; state in `ClusterState` | no "Stop" of the whole exercise: that is a `TransitionState` to an Unloading state |
| Create Entity / Remove Entity PDU, Acknowledge | `CreateEntityRequest` / `DeleteEntityRequest` → `CreateUpdateDeleteEntityAck` | ack carries a request GUID; creation is "ask the creator node", not a command to a federate |
| Action Request (set data) | `UpdateEntityDescriptorRequest` / `UpdateEntityAttributeRequest` | JSON paths for attributes (schema published as `EntityAttributeSchema`) |
| Transfer Control / HLA ownership management | `Fdp.Network.Cyclone.Topics.OwnershipUpdate` per descriptor | richer than DIS: per-descriptor, per-instance |
| Emissions, designator, radio/signal, IFF, electromagnetic | **none** — searched `grep -rniE "\biff\b\|emission\|radio\|transponder" Hrot/Network FDP/Network` (only substring hits inside "Mission"); `search_graph` name regex hits were unrelated ("Diff") | **GAP** |
| HLA object class / attribute | entity (EntityMaster) / descriptor | attribute ownership ≈ descriptor ownership |
| HLA interaction | any **message** topic (§Inventory kind = message) | |
| HLA federation save/restore, time management | `ClusterOpRequest` ops `TakeCheckpoint` / `RestoreCheckpoint` / pause / step; cluster sim time | no HLA time-advance (LBTS) semantics: HROT is a lock-step or continuous cluster clock |

**What HROT carries that DIS does not:** per-descriptor ownership, mission and AI commands (`EntityMission`, `NavigationIntent`, `EntityBrainIntent`), sensor/EQS pipelines, TKB ids, sim-time stamps, cluster orchestration and file transfer.
**What DIS needs that NED does not carry:** ECEF/geocentric frames, DR parameters, entity-id triple, marking length rules, appearance bits, emissions/IFF/radio, collision, DIS timestamps.

### SimNetGateway simplified model → real HROT types

Source: `SimNetGateway.Protocol.Ned/NedMessages.cs` and DD-001 §9, Appendix A (`/home/user/pjanec/simnetgateway/docs/design/DD-001-Gateway-Engine-PoC.md`).

| Gateway message | Real HROT type(s) | Mismatch to handle |
|---|---|---|
| `NedEntityMaster(Id, Name, TkbType, Force)` | `Hrot.NED.Descriptors.EntityMaster` (id, `TkbType`) **+** `Hrot.NED.Descriptors.EntityInfo` (name, force) | Name and Force are in **EntityInfo**; `TkbType` is `int64`; entity ids are `int64` on every topic since AQ107 E (gateway `ulong` — map, never truncate); the master also holds `DisType`, flags, request id |
| `NedGeoSpatial(Id, lat, lon, alt, heading, pitch, roll, speed)` | `Hrot.NED.Descriptors.WorldPos` | **no lat/lon, no Euler** (AQ107 B/D): local-ENU `Position` (m), quaternion `OrientationXyz`, ENU `Velocity` (m/s, a vector — speed = its length) and `AngularVelocity` (rad/s), `SimTime` (sim seconds); no acceleration; the gateway converts with the origin (the retained `GeoReference`, step 0 below); sender must re-send every ≤ 10 s |
| `NedEntityDamage(Id, Level 0..3)` | `Hrot.NED.Descriptors.EntityDamage` (current, max health floats); event `Hrot.NED.Messages.EntityHitDamage` (HP lost) | **no discrete level**; derive from current/max — the thresholds are the gateway's decision |
| `NedWeaponStatus(Id, SelectedMunitionTkb, Ammo, Member)` | `Hrot.NED.Descriptors.EntityWeaponAmmo` (per mount ammo + loaded round type), `EntityWeaponPosture`, `EntityWeaponSystemState` | **no "selected munition" field**; ammo is per mount (up to 4); no formation `Member` index |
| `NedEntityDisposed(Id)` | **a dispose of the `EntityMaster` instance** (+ its other descriptors by the owner) | not a message: read `InstanceState`, check the disposer is the owner |
| `NedWeaponFire(FireId, Shooter, Target, MunitionTkb, lat/lon/alt, Quantity, …)` | `Hrot.NED.Messages.WeaponFire` (and `WeaponFireRequest`) | position is **local metres** + velocity; `FireId` GUID ↔ `RequestId` uint32; `MunitionTkb` ↔ `AmmoGuid` (TKB id, `int64`); target 0 = none/point; formation members: none |
| `NedDetonation(DetonationId, Target, MunitionTkb, lat/lon/alt, Result, TargetDamage, …)` | `Hrot.NED.Messages.MunitionDetonation` + `EntityHitDamage` | local metres; no result enum (hit entity 0 = terrain); damage arrives as a *separate* message, HP not level; no id |
| `NedSimControl(Load / LoadDebrief / Start / Pause / Resume / Unload, scenario)` | `Hrot.NED.Descriptors.Orchestration.ClusterOpRequest` (`TransitionState` toward Loading/Operating/Unloading Live or Replay states; `PauseTime`, `ResumeTime`) observed through `ClusterStateTopic` | **approximate** — state machine is richer (Edit, Preview, Review); scenario id travels in `OrchestratorContextTopic`; the exact `TransitionState` payload per gateway kind was **not verified** |
| `NedFormation(Id, Name, TkbType, Force, Members[])` | **no real equivalent**: members are separate entities; the unit hierarchy is `EntityInfo` (commander id, tactical designation) | a composite "unit" has no single descriptor; vehicle occupants: `EntityPassengers` |
| `NedFireAmmunition(FireId, MunitionTkb, Quantity)` | **no real equivalent** (quantity and munition ride on `WeaponFire`) | the gateway's split-fire shape does not exist on the wire |
| `NedDamageNotification(Id, Member, Old, New)` | **no real equivalent** (outbound notification) | derive from `EntityDamage` changes |
| `NedIffStatus(Id, Mode)` | **no real equivalent** | see the IFF search above |

## Code versus the spec

Spec = [BDC_NED_SST_Descriptor_Rules.md](BDC_NED_SST_Descriptor_Rules.md). Where code differs, this document describes the **code**.

| # | Spec says | Code does | Where |
|---|---|---|---|
| 1 | `EntityId` is `long` in every descriptor (L40-57) | **same** since AQ107 E (R-4093, 2026-10-27): `int64` in every descriptor and event; ~~mixed: `int32` on 19 descriptor types~~ *(superseded)* | `GenericDescriptors.cs:97`, `SimDescriptors.cs:17`, `FDP/Network/Fdp.Network.Cyclone/Topics/OwnershipUpdate.cs` (long) |
| 2 | `EntityMaster.DisType` is a `uint64` (L122) | a 7-field **struct** `DisTypeStruct` | `GenericDescriptors.cs:53,97-122`. Also: `StaticType` (spec L111) does not exist |
| 3 | `OwnershipUpdate.NewOwner` is a `NodeId { Domain, Node }` (L150-172) | a single **`int32` node id**, plus `OriginNodeId` and `Timestamp` fields not in the spec | `FDP/Network/Fdp.Network.Cyclone/Topics/OwnershipUpdate.cs:10-55`; R-166 states "same thing as the spec's" without reconciling the shape |
| 4 | Ownership is "the most recent writer" (L135) | owner of the **master** is the sample's *writer identity* (needs sender tracking); other descriptors' owners follow `OwnershipUpdate` / grants (`FDP/Toolkits/Fdp.Toolkits/Replication/Systems/OwnershipIngressSystem.cs:13`) | `EntityMasterIngressTranslator.cs:84-91,252` |
| 5 | A non-master dispose by a partial owner returns ownership to the master's owner, detected by the master's owner (L80) | **node departure** (heartbeat / capabilities not-alive) triggers a *local recomputation* on every node; no dispose-based path | `ClusterCapabilityIngestSystem.cs:82-100`; `PartialOwnerReclaimSystem.cs:39`; Q79 §0.11 superseded wording |
| 6 | Entity ids are allocated by a DDS server that is **multi-drill capable** (L100) | one allocator **per DDS domain**; topics carry no drill id; counter reset to 1000 at world boundaries | `FDP/Network/Fdp.Network.Cyclone/Topics/IdAllocTopics.cs:20-60`, `…/Services/DdsIdAllocatorServer.cs:26,119` |
| 7 | Multi-instance descriptor: `EntityId` then `InstanceId` as the second field (L49-57) | same shape but the first key is named `ParentNetworkId` and the second `PartId` / `LocalChildIndex` | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs:23-40`, `SimDescriptors.cs` (turret) |
| 8 | Update acknowledged by an ack with **descriptor type** and result (L237) | `UpdateEntityDescriptorAck` carries request id, entity id, error code (no descriptor type) | `GenericMessages.cs:243-250` |
| 9 | Create: publish descriptors, EntityMaster **last**; delete: unpublish EntityMaster **first** (L182-188) | HROT's owner publishes the master as soon as its translator sees the entity (before `WorldPos`/`EntityInfo`), and on delete disposes **all** instances; receivers tolerate any order | `EntityMasterEgressTranslator.cs:80-120`; `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs:210` |
| 10 | Descriptor topics "KeepLast 1" (L59) | true for the state topics (`S`, `SL`); exception: `DescriptorOptimisticLock` is `KeepAll` | inventory QoS column; `GenericDescriptors.cs:206` |

## Not determinable and unverified

Searches named where a claim says "not found". Each item is a claim I **could not verify** or a statement the sources do not make:

| # | Item | Searches run |
|---|---|---|
| 1 | Wire layout of the sender identity (`__FcdcSenderIdentity`) | `strings CycloneDDS.Runtime.dll`; `grep -rn "SenderIdentity" Hrot FDP` |
| 2 | The exact default QoS of types with no `[DdsQos]` (kind `D`) | `grep -rniE "default QoS\|no \[DdsQos\]" docs .dev`; library is a NuGet binary |
| 3 | The per-`TkbType` mandatory-descriptor set (derived at runtime) | `GhostPromotionSystem.cs:225-296`; `MandatoryComponentResolver` not enumerated |
| 4 | Behaviour for an unknown `TkbType` beyond "gate skipped, blueprint defaults not injected" | read `GhostPromotionSystem.cs:218-296`; downstream `LifecycleModule.BeginConstruction` not traced |
| 5 | The staged TKB file format and location | `grep -rn "KnowledgeBase\|TkbFile\|tkb.json" --include=*.cs Hrot/Runner Hrot/Network/Hrot.Network.Orchestration` (no hit) |
| 6 | The JSON payload of `ClusterOpRequest` per operation, and the exact sequence of ops a `NedSimControl` maps to | `grep -rn "ClusterOpType.TransitionState" Hrot FDP` (senders found, schema not read in full) |
| 7 | CycloneDDS network configuration | `grep -rln "CYCLONEDDS_URI\|cyclonedds.xml"` — docs only |
| 8 | Whether the `Hrot.IG.Gizmos.IGCapabilitiesAnnounce` variant is ever instantiated | `grep -rn "IGCapabilitiesPublisherSystem" Hrot` — its publisher system is registered nowhere in production code; the NED variant is what the IG actually writes (`Hrot/Subsystems/Hrot.IG/IgApplication.cs:1060` → `Replication`-side `IG/NedIgNetworkAdapter.cs:128`) |
| 9 | Coverage of the code graph for absence claims | `scripts/cbm.sh check_index_coverage` is not available through the CLI (CLAUDE.md); absence claims are grep + `search_graph` only |
| 10 | The *kind* column (descriptor / message) is assigned by a rule (keyed ∧ TransientLocal ⇒ descriptor) with four overrides (`EntityStateTopic`, `SensorTargets` forced to descriptor; `NodeIdRequest`, `NodeIdGrant` forced to message) | judgement, not from sources |
| 11 | Descriptor role text in the inventory is paraphrased from comments at the type; ownership group per descriptor was taken from the Ownership Groups design (live `2026-10-14`), not re-measured | — |

Queries behind the inventory (coverage): `grep -rn "\[DdsTopic" --include=*.cs Hrot FDP Stride` (since AQ107 A every hit is a bare `[DdsTopic]`; `python3 scripts/ned-wire-inventory.py --check` fails on a production type with an explicit name);
`scripts/cbm.sh search_code {"pattern":"DdsTopic(","file_pattern":"*.cs","limit":1000}` (141 results, `has_more: true` — it also matches docs/tests/ExtDeps);
`scripts/cbm.sh search_graph {"name_pattern":".*Descriptor.*","label":"Class","limit":200}` (90 total, 82 returned, paginated incompletely — used only as a sanity check) and `{"label":"Interface"}` (1: `IDescriptorTranslator`); a `DdsReader<T>` / `DdsWriter<T>` generic-argument sweep found the three translator-bound types (now `[DdsTopic]`, AQ107 A).

## Keeping this current

**Update this document when** a `[DdsTopic]` type is added, removed, renamed or moved to another namespace (that changes its topic name); a **key** or **QoS** changes; a descriptor ordinal is allocated or retired; ownership, id-allocation, time or lifecycle behaviour changes; or a new concept enters the wire (a new coordinate frame, a new session mechanism).

```bash
python3 scripts/ned-wire-inventory.py            # sorted table: full name, derived topic, keyed, file:line
python3 scripts/ned-wire-inventory.py --verbose  # + QoS text, [DdsManaged], key members
python3 scripts/ned-wire-inventory.py --check    # exit 1 if code and §Inventory disagree (missing / stale / duplicate rows), or any HROT-owned [DdsTopic] has an explicit name, or two derived names collide
python3 scripts/ned-wire-inventory.py --skeleton # draft rows for the types the doc lacks
MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/reference/NED_DDS_Protocol.md
```

`--check` compares **full names only** (first backticked cell of each §Inventory row) and enforces the naming rule on the code. It does not check key, QoS or role text: when
`--verbose` shows a changed key or QoS for a type, edit its row by hand. `--skeleton` prints a draft row (role = TODO) for every type the doc lacks. Add the doc and the script to the owning design's
`related-designs` when a new wire-owning design appears.
