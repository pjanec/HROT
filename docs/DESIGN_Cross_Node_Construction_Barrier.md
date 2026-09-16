<!--STATUS
state: LIVE
build-state: BUILT (primary barrier LIVE-PROVEN) `2026-09-15` (CE-283). ✅ The reliable-init cross-node
  barrier engages and releases over CycloneDDS: item B base, A1 durable descriptor, A2 flag bit + peer tag,
  A4 status egress, creator ingress, A5 provider-stamp (option A), A6 gateway construction/registration in
  NedReplicationModule. Proven by `NetworkGatewayIntegrationTests.ReliableInitBarrier_EngagesAndReleases…`
  (CGF creator held Constructing → SimHost peer reports Active over the wire → creator released; the entity
  carries a non-empty stamped peer set) + 7/7 gateway unit rails + no-regression across the existing gateway
  and split-authority rails. ⚠ REMAINING (follow-ons, not the primary barrier): A7 late-joiner durability +
  §2b node-id as EXPLICIT rails (the durable descriptor + node-id are built and exercised, not separately
  asserted); a synthetic delay participant (the natural DDS round-trip proved the hold without one). ⭐ PIECE C
  (production model) is DESIGNED in §3b and NOT yet dispatched: creation-request wait-list + timeout, mandatory
  reply, timeout=abort-via-EntityMaster-dispose, and the LOCAL poll-store result. Slice A's option-A "all present
  peers" is superseded-for-production by §3b. Prereqs P1–P3 (§5) BUILT. Fast stays default (user); reliable is OPT-IN.
updated: 2026-09-15
current-answer: ⭐ §3b IS THE PIECE-C PRODUCTION MODEL — the resolved, measured contract (creation-request
  fields, mandatory reply, timeout=abort-via-EntityMaster-dispose, and the LOCAL poll-store result). §1 is the
  original diagrams (1.1 sequence, 1.2 classes, 1.3 module map). §2 says WHY — incl. §2a late-joiner durability,
  §2b node-id, §2c generic protocol. §3a is slice-A as-built. §4 receiver gate. §5 prereqs (P1–P3 BUILT). §6 acceptance.
known-rot: ⛔⛔ The role→init derivation floated in an earlier piece-C handoff — a `[RequiresPeerInit]` COMPONENT
  ATTRIBUTE + a role→init-capability set + a creator-side type→roles filter — is RETRACTED (user, `2026-09-15`):
  the peer's wait condition is host/type-specific and OPAQUE to the creator; the creator sets a bit + an optional
  node list, and each node decides its own wait locally. §3b is the resolved model; wherever §1.1/§2/§3 imply a
  creator-computed role filter or a component-attribute contract, §3b supersedes them.
  ⚠ Also: §2a/§3 name the DDS attrs as `[DdsStruct]`/`[DdsKey]`/`[DdsQos]` — SUPERSEDED by §3a.1 (real convention
  is a `partial struct` with `[DdsTopic]`+`[DdsQos]`+`[DdsKey,DdsId]`; no `[DdsStruct]` exists). And the Step-0 V2
  note ("Flags is `int`") measured the CYCLONE type by mistake — the `--mode all` NED `EntityMaster.Flags` is
  `ulong` (§3a.0); the original "`ulong`" stands.
related-designs:
  - docs/DESIGN_Entity_Genesis_End_To_End.md — the landing page; this design is stage ⑨'s reliable variant. It owns the end-to-end sequence; this owns the cross-node WAIT. (reciprocal: genesis §"reliable init" links back here.)
  - docs/designs/others/DESIGN-NetworkSpawning.md — owns ReliableInitType / PendingNetworkAck / the SpawnEntityCommand→NetworkSpawningSystem local-create pipeline this rides.
  - docs/designs/SIM/DESIGN-SIMHOST.md — owns the CreateEntityRequestSystem PRODUCER + the (remote) CreateEntityAck; §3b's local poll-store is the LOCAL sibling of that remote ack (kept separate).
  - docs/designs/IG/DESIGN-IG.md — owns the remote requestor form (DdsCommandClient<CreateEntityRequest,CreateEntityAck>); §3b's poll-store is the LOCAL-requestor equivalent.
  - docs/reference/BDC_NED_SST_Descriptor_Rules.md — owns the BDC/NED wire contract; §2a/§2b/§2c fold the durable EntityLifecycleStatusDescriptor + EntityMaster.Flags reliable bit into it as OPTIONAL generic descriptors (reciprocal link to add when that spec is edited for piece A).
  - docs/DESIGN_Role_Affinity_Ownership.md — owns WHO OWNS WHAT (the grant node-set = the owner subset of the peer set) and the role tables.
  - FDP/Docs/projects/toolkits/FDP.Toolkit.Lifecycle.md — owns the ELM participant/ack mechanism this builds on.
  - docs/designs/replay-and-modules/DESIGN.md — §2.1m the ELM rewind boundary (OnWorldReplaced/ResumeFromRestoredWorld) that new participants inherit.
  - docs/blueprints/batches/FRAME_Construction_Barrier_Participants.md — the frame that scoped this (pieces A–D, prerequisites P1–P3).
  - docs/blueprints/Architect_Question_69_Construction_Barrier_And_Rewind_Contract.md — the asks + §9 reconciliation this design answers.
  - docs/blueprints/Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md — §3c consumer view; owns the host-capability token facility + the non-supporting-host degradation.
  - docs/designs/two-ack/TwoAck-DESIGN.md — the REMOTE requestor's two-phase view (SstStatusCode InProgress/NotSupported); this barrier is the mechanism its ack sits on (its STATUS notes the pre-barrier "vacuous" gap this fills).
-->
# DESIGN — the cross-node construction barrier *(reliable distributed init)*

> 🎯 **What this owns:** how a creating node holds an entity in `Constructing` until the **peer nodes that
> must initialise a copy have reported `Active`** — decentralised, **no master controller**. Opt-in per
> entity (`ReliableInitType.AllPeers`); fast mode (each node independent) stays the default.
> ⛔ **What it does NOT own:** the genesis sequence *(the landing page)*, ownership grants *(role-affinity)*,
> the mandatory-components gate *(tkb-1)*, replay suppression *(mgmt-1)*.

## 0. 📐 INVENTORY — the enumerations the diagrams rest on *(measured `2026-09-15`)*
⚠ `check_index_coverage` is not reachable via the CLI, so no coverage attestation; every row is
`search_graph` + grep agreeing, `file:line` inline.

| query | result |
|---|---|
| `search_graph name_pattern=".*(NetworkTopology\|ExpectedPeers\|RoleShard\|ClusterStateCache\|OwnershipStrategy).*"` | 65 nodes — the seams below; `INetworkTopology` has **no production impl** *(tests only)* |
| ELM public API | `EntityLifecycleModule.cs`: `RegisterModule(int):148` · `RegisterRequirement(long,int):158` · `BeginConstruction:182` *(`RemainingAcks = _globalParticipants ∪ _blueprintRequirements[tkbType]` :190)* · `AcknowledgeConstruction:168` · `ProcessConstructionAck:283` *(sets `Active` :305, publishes NOTHING)* · `OnWorldReplaced(bool):465` |
| the deferring-participant pattern | `NetworkGatewaySystem.cs` — `_pendingPeerAcks:47` · `ReceiveLifecycleStatus:167` · timeout `:189`; ⛔ **never constructed in production** |
| the peer-ack wire type | `EntityLifecycleStatusDescriptor.cs` *(Cyclone topic: `EntityId, NodeId, State, Timestamp`)* — **designed for exactly this, ZERO producer/consumer** |
| the creator's reliable stamp | `NetworkSpawningSystem.cs:175` `if (InitType != None) AddComponent(PendingNetworkAck{ExpectedType})` — stamped, unread |
| the owner subset of the peer set | `DeferredTakeOwnershipCommand.Grants[].NodeId` — `CreateEntityRequestSystem:363` + `CgfSubsystem:544` |
| cluster membership | orchestrator `ClusterMaster._roster` → `NodeRoster.ActiveNodes` *(`NodeHealthProfile`: NodeId, SubsystemName, ClusterState, LastHeartbeat, Cpu, Ram — **no role**)*; `NodeHeartbeatEvent` *(NodeId, LocalStateId, WallTicksUtc, SubsystemName — **no role mask**)*; role re-derived lossily at `NedNetworkFactory.MapSubsystemNameToRole:422` |
| role type | `NodeRole` `[Flags]` in `Fdp.Core` *(`Brain=1<<0 … NavigationSolver=1<<4`)* |
| heartbeat producers | `ClusterSlave.cs:153` · `OrchestrationObserverTranslator.cs:86` · `NedOrchestrationTranslator.cs:58` |

---

## 1. ⭐⭐⭐ THE DIAGRAMS — *this is the design; §2 onward only says why*

### 1.1 The reliable-init sequence across the wire
*What the picture shows that prose hides: the creator's `Active` is gated on a **count-down of peer acks it
resolved at spawn from the roster**, and each peer's ack fires only after **its own** local barrier cleared —
so the cross-node wait is a composition of local waits, never a central controller.*

```mermaid
sequenceDiagram
    autonumber
    participant CR as CREATOR node
    participant OR as Orchestrator roster
    participant W as DDS wire
    participant PX as PEER node (owner or display)

    CR->>OR: expected peers = nodes whose role mask participates (P3 query)
    CR->>CR: NetworkSpawningSystem stamps PendingNetworkAck + ExpectedAckPeers
    CR->>CR: ELM BeginConstruction — gateway is a participant (reliable only)
    Note over CR: creator held in Constructing<br/>gateway will not ack yet
    CR-->>W: EntityMaster + baseline
    W-->>PX: ingress — CreateGhost, GhostPromotion gate, local ELM
    PX->>PX: navmesh / model-load participant defers until data ready
    PX->>PX: all local ACKs in -> entity Active
    PX-->>W: EntityLifecycleStatusDescriptor(State=Active)
    W-->>CR: PeerLifecycleStatusIngress -> gateway.ReceiveLifecycleStatus
    CR->>CR: drop peer from ExpectedAckPeers
    Note over CR: when the set empties (or timeout)<br/>gateway.AcknowledgeConstruction -> creator Active
```

### 1.2 The classes — *existing on the same canvas as new, so duplicates are visible*
*What it shows: the peer-ack carrier and the participant mechanism ALREADY EXIST; the new work is one base
class, its subclasses, one egress producer, and one field on the membership records.*

```mermaid
classDiagram
    class EntityLifecycleModule {
        <<EXISTS>>
        +RegisterModule(int)
        +RegisterRequirement(long tkbType, int moduleId)
        +BeginConstruction(...)
        +AcknowledgeConstruction(entity, moduleId, ...)
    }
    class DeferredConstructionParticipant {
        <<NEW abstract>>
        #TryComplete(entity) bool
        +Execute(view, dt)
    }
    class NetworkGatewaySystem {
        <<CHANGE: construct in prod; subclass the base>>
        +ReceiveLifecycleStatus(entity, nodeId, state)
    }
    class NavmeshReadinessParticipant {
        <<NEW>>
    }
    class ModelLoadReadinessParticipant {
        <<NEW>>
    }
    class PeerLifecycleStatusEgressSystem {
        <<NEW>>
        +publishes EntityLifecycleStatusDescriptor on Constructing to Active
    }
    class EntityLifecycleStatusDescriptor {
        <<EXISTS orphan - revive as DURABLE descriptor>>
        +long EntityId DdsKey
        +int NodeId DdsKey - SAME id as OwnershipUpdate
        +EntityLifecycle State
        +long Timestamp
    }
    note for EntityLifecycleStatusDescriptor "DdsQos Reliable + TransientLocal + KeepLast 1 -- owner-instance authoritative; late joiner reads it"
    class PendingNetworkAck {
        <<CHANGE: add expected peer set>>
        +ReliableInitType ExpectedType
        +int[] ExpectedAckPeers
    }
    class NodeHeartbeatEvent {
        <<CHANGE: P1>>
        +int NodeId
        +NodeRole Roles
    }
    class NodeHealthProfile {
        <<CHANGE: P2>>
        +NodeRole Roles
    }
    class NodeRoster {
        <<CHANGE: P3>>
        +NodesWithRole(NodeRole) IEnumerable~int~
    }

    DeferredConstructionParticipant <|-- NetworkGatewaySystem
    DeferredConstructionParticipant <|-- NavmeshReadinessParticipant
    DeferredConstructionParticipant <|-- ModelLoadReadinessParticipant
    DeferredConstructionParticipant ..> EntityLifecycleModule : RegisterRequirement + AcknowledgeConstruction
    PeerLifecycleStatusEgressSystem ..> EntityLifecycleStatusDescriptor : publishes on Active
    NetworkGatewaySystem ..> EntityLifecycleStatusDescriptor : consumes (peer ack)
    NetworkGatewaySystem ..> PendingNetworkAck : reads ExpectedAckPeers
    NodeRoster ..> NodeHealthProfile : holds
    NodeHealthProfile ..> NodeHeartbeatEvent : filled from
```

### 1.3 Who registers what, on which node — ⚠ *and what never ticks*
*What it shows that neither of the above can: the creator waiter runs on Brain/CGF, the readiness
participants run on the peers, and the dead NED-only group is NOT the home for any of them.*

```mermaid
graph TD
    subgraph CRN["CREATOR node (Brain / CGF)"]
        NSS["NetworkSpawningSystem<br/>stamps PendingNetworkAck+peers"]
        GW["NetworkGatewaySystem<br/>creator waiter (reliable)"]
        PLI["PeerLifecycleStatusIngress"]
    end
    subgraph PXN["PEER node (Muscle / Map2D)"]
        RP["Navmesh / Model-load participant<br/>RegisterRequirement per type"]
        ELMP["local ELM"]
        PLE["PeerLifecycleStatusEgressSystem<br/>publishes ack on Active"]
    end
    subgraph ORC["Orchestrator"]
        ROS["NodeRoster + role mask (P2)<br/>NodesWithRole (P3)"]
    end

    NSS --> GW
    ROS -->|"membership"| NSS
    GW -->|"BeginConstruction participant"| ELMP2["creator local ELM"]
    RP --> ELMP
    ELMP --> PLE
    PLE -.->|"DDS EntityLifecycleStatusDescriptor"| PLI
    PLI --> GW

    DEAD["NetworkLifecycleSystemGroup / DeferredTakeoverSystem<br/>NED-only, one caller — NOT a home for these"]
    classDef dead fill:#fde,stroke:#c33,stroke-width:2px
    class DEAD dead
```

---

## 2. ⭐ WHY — the rationale the diagrams cannot carry

- **Why revive `EntityLifecycleStatusDescriptor` rather than invent an ack.** It is a DDS topic already
  designed for exactly this *("published by peer nodes to confirm entity activation in reliable init mode")*
  with no producer/consumer — the seam law: adopt the under-adopted seam, do not add a parallel one.
- **Why a new `PeerLifecycleStatusEgressSystem`.** `ProcessConstructionAck` sets `Active` but **publishes no
  event** *(measured, `:305`)*, so nothing today can react to "my copy became `Active`". The producer must
  OBSERVE the `Constructing → Active` transition for reliable entities and publish the ack. ⚠ It emits only
  for entities the local node treats as reliable, never for every activation.
- **Where `EntityLifecycleStatusDescriptor` is filled from — measured `2026-09-15`.** The four fields are all
  **local peer state** at the moment the copy reaches `Active`:
  | field | source |
  |---|---|
  | `EntityId` | the peer entity's `NetworkIdentity.Value` *(written at ghost creation from `EntityMaster.EntityId`)* |
  | `NodeId` | the reporting node's own local id |
  | `State` | `Active` — the observed transition |
  | `Timestamp` | wall clock *(same `WallTicksUtc` source as the heartbeat)* or the frame `GlobalVersion` |
  ⛔⛔ **BUT the ELIGIBILITY — "which entities publish at all" — is NOT sourced on the peer today.** Reliability
  *(`ReliableInitType`)* is a CREATE-time fact that lives only on the creator *(`SpawnEntityCommand.InitType`,
  the creator's `PendingNetworkAck`)*; it never travels. `EntityMaster` *(the wire descriptor that makes the
  peer's ghost)* has a general `ulong Flags` field *(the NED `Hrot.NED.Descriptors.EntityMaster`,
  `GenericDescriptors.cs:102` — see §3a.0 on the two EntityMaster types)*, but the sole egress producer writes
  **`Flags = 0`**
  *(`EntityMasterEgressTranslator.cs:103`)*. ⇒ **the reliable bit must ride `EntityMaster.Flags`** *(exactly
  the design-talk's `Flags = WaitForAcks`)*: the peer reads it at ghost creation, tags the ghost
  *"report-on-Active"*, and the producer publishes for tagged, not-yet-reported entities. **This wire-plumbing
  is a dependency of piece A and does not exist yet.**

### 2a. ⭐⭐ LATE JOINERS — the status descriptor is DURABLE, not fire-and-forget *(user hard requirement, `2026-09-15`)*
> 🔒 **User:** *"Late joiners must get complete information about entity state to avoid using half-baked entity.
> So if reliable-init flag is set, they need to check the state of `EntityLifecycleStatusDescriptor`."*

⛔ **The barrier as drawn in §1.1 only helps nodes that were PRESENT for the `Constructing → Active` transition.**
A node that joins *after* an entity was created would see the durable `EntityMaster` *(TransientLocal → it gets
the last sample)* and build a ghost, **with no idea the entity is still mid-init on a peer** — the exact
"half-baked entity" the user forbids.

⇒ ⭐⭐⭐ **`EntityLifecycleStatusDescriptor` must be a FIRST-CLASS DURABLE DESCRIPTOR, not an event.** Measured
today it is a plain `class` with `{get;set;}` properties and **no DDS attributes at all** — so this is real
work, not a flag flip. ⭐ **The exact converted shape (a `partial struct` with `[DdsTopic]`+`[DdsQos]`+`[DdsKey,
DdsId]`) is in §3a.1** — the attribute names in the table just below are shorthand, superseded by §3a.1.

| what it must become | why |
|---|---|
| ⭐⭐ **`[DdsQos(Reliable, TransientLocal, KeepLast 1)]`** | ⛔ so the LAST status per instance is retained and delivered to late joiners — the SST convention for critical descriptors *(`docs/reference/BDC_NED_SST_Descriptor_Rules.md`)*. A `Volatile` sample is gone before the joiner subscribes |
| ⭐⭐⭐ **`[DdsKey]` on `(EntityId, NodeId)`** | one retained instance **per (entity, reporting node)** — a joiner reads every peer's latest status for an entity, so it can tell "all required peers `Active`" from "one still `Constructing`" |
| ⭐⭐ **owner-instance authoritative for the entity's overall readiness** | the joiner correlates the descriptor's `NodeId` with the entity's OWNER *(from `EntityMaster` / `OwnershipUpdate`)*: the **owner's** status instance is the one that says "the entity is fully live". Peer instances say "this peer's copy is ready" |
| ⭐ **the late-joiner READ path** | on ghost creation, if the ghost's `EntityMaster.Flags` has the reliable bit, the joiner does NOT treat the entity as usable until it has read a `State = Active` status instance for the **owner** node *(and, if it is itself a required participant, until its OWN local init acks)* — same "hold until Active" the live barrier enforces, sourced from durable state instead of a live ack |

⚠ **This makes the wire self-describing and removes any need for a joiner to replay history:** the retained
`EntityMaster.Flags` says *"this entity uses reliable init"*, and the retained `EntityLifecycleStatusDescriptor`
instances say *"and here is each node's current readiness"*. A late joiner needs nothing else.

### 2b. ⛔⛔ NODE-ID CONSISTENCY — the descriptor's `NodeId` IS the ownership node-id *(user hard requirement, `2026-09-15`)*
> 🔒 **User:** *"Node id in the `EntityLifecycleStatusDescriptor` must be the same as used for ownership update
> message."*

📌 **Why this is load-bearing, not cosmetic.** §2a's late-joiner correlation — *"is the OWNER's status
instance `Active`?"* — only works if the joiner can match the owner it learns from ownership against the
`NodeId` on the status instance. If the two used different id spaces *(e.g. an orchestrator roster id vs a
NED wire node id)* the join would silently never match and the entity would be treated as forever-unready.

**Measured `2026-09-15` — the ownership node-id in each representation:**
| representation | file | the node-id field |
|---|---|---|
| NED wire *(the one that crosses BDC/NED)* | `Hrot/Network/Hrot.Network.NED/GenericMessages.cs` | `OwnershipUpdate { … NodeId NewOwner }` — a **`NodeId` struct** |
| Cyclone topic | `FDP/Network/Fdp.Network.Cyclone/Topics/OwnershipUpdate.cs` | `int NewOwner` + `int OriginNodeId` |
| ECS event | `FDP/Toolkits/Fdp.Toolkits/Replication/Messages/OwnershipMessages.cs` | `int NewOwnerNodeId` + `int OriginNodeId` |
| the authority test | `OwnershipIngressSystem.cs:67` | `update.NewOwnerNodeId == localNodeId` |

⇒ ⭐⭐⭐ **`EntityLifecycleStatusDescriptor.NodeId` uses the SAME node-id as `OwnershipUpdate`** — the `NodeId`
struct at the NED/BDC wire, projected to the same `int` the ownership ingress compares against `localNodeId`
at the ECS layer. ⛔ **NOT** the orchestrator roster id, **NOT** the lossy subsystem name. The producer stamps
its own local node id *(the identity it also claims ownership with)*; the consumer compares against the owner
learned from `OwnershipUpdate` and against its own `localNodeId` — one id space end to end.

⚠ **This also fixes the §2 "filled from" table's `NodeId` row:** it is not merely "the reporting node's own
local id" — it is *"the reporting node's OWNERSHIP node-id, the same value it puts in `OwnershipUpdate.NewOwner`
when it owns"*.

### 2c. ⭐ GENERIC NED/BDC PROTOCOL — reliable init leaks no Hrot specifics *(user check, `2026-09-15`; confirmed viable)*
Both new wire pieces sit in the **generic** layer and carry no engine-specific meaning, so any external BDC/NED
host can implement them:
- **The reliable bit rides `EntityMaster.Flags`** *(the NED descriptor's `ulong`, documented "entity type
  specific flags")* — one reserved bit `WaitForAcks`. A host that ignores it simply behaves as fast-mode; the
  field already exists on the generic descriptor.
- **`EntityLifecycleStatusDescriptor` is already in the generic `Fdp.Network.Cyclone` layer** and its
  `EntityLifecycle` enum *(`Fdp.Core`: `Constructing/Active/TearDown/Ghost`)* is a generic lifecycle vocabulary —
  no Hrot type names. The SST rules doc *(`BDC_NED_SST_Descriptor_Rules.md`)* already contemplates *"the app may
  wait before considering the entity completely created"*; this makes that concept a concrete, optional descriptor.
⇒ ⭐ **Fold into the SST spec as an OPTIONAL descriptor + one reserved `EntityMaster.Flags` bit** — a host that
implements neither is a valid fast-mode-only host. *(Spec edit is its own small task, tracked with piece A.)*

- **Why the participant base is one class.** `NetworkGatewaySystem` already hand-rolls pending-set +
  defer-ack + timeout + destruction-cleanup; navmesh and model-load need the identical shape. One
  `DeferredConstructionParticipant` (hook `TryComplete(entity)`) or the gateway becomes three copies *(ruling 9)*.
- **Why membership comes from the orchestrator role mask, not the grant.** The grant covers only *owners*;
  a display replica *(Map2D/IG)* owns nothing yet must still be waited for. The roster knows every present
  node; filtering by role mask against "roles that register a participant for this type" yields the full set.
  📄 frame §4a.
- **Why fast stays default.** The design-talk's latency argument holds: cross-node waiting is opt-in per
  entity; `ReliableInitType.None` skips the gateway participant entirely.
- **Why rewind needs nothing here.** Replay does not run the ELM *(mgmt-1 §8.10)*; the participants inherit
  `OnWorldReplaced`/`ResumeFromRestoredWorld` *(replay-and-modules §2.1m, BUILT)* — on resume a still-`Constructing`
  entity re-publishes `ConstructionOrder` and the participant re-blocks. No new rewind code.

## 3. NEW vs EXISTS vs CHANGE
| element | status |
|---|---|
| `EntityLifecycleModule` participant/ack API | ✅ EXISTS — used as-is |
| `EntityLifecycleStatusDescriptor` (peer-ack topic) | ⚠ CHANGE — EXISTS but ORPHAN and untyped: add `[DdsStruct]`, `[DdsKey]` on `(EntityId, NodeId)`, `[DdsQos(Reliable, TransientLocal, KeepLast 1)]` so it is DURABLE for late joiners (§2a); `NodeId` = the OwnershipUpdate node-id (§2b); then a producer + a consumer (live + late-joiner read) |
| `NetworkGatewaySystem` (creator waiter) | ⚠ CHANGE — construct in production; reslot onto the base; consume the ack topic; read `ExpectedAckPeers` |
| `DeferredConstructionParticipant` | 🆕 NEW abstract base |
| `NavmeshReadinessParticipant` / `ModelLoadReadinessParticipant` | 🆕 NEW — `RegisterRequirement` per type |
| `PeerLifecycleStatusEgressSystem` | 🆕 NEW — observe `Constructing→Active`, publish the ack |
| `PendingNetworkAck` | ⚠ CHANGE — carry `ExpectedAckPeers` *(or a sibling component)* |
| `EntityMaster.Flags` + `EntityMasterEgressTranslator` | ⚠ CHANGE — carry the reliable bit on the wire *(the egress writes `Flags=0` today)* so the peer knows to report-on-`Active`; the peer tags its ghost from it. **Dependency of piece A** |
| heartbeat/roster role mask | ⚠ CHANGE — P1/P2/P3 *(§5)* |
| `INetworkTopology.GetExpectedPeers(tkbType)` | ⛔ RETIRE as the peer source — type-only, no prod impl |

## 3a. 🔧 SLICE A — AS-VERIFIED BUILDABLE DETAIL *(H-ui, `2026-09-15`, Step-0 verified)*
> The Step-0 gate (V1–V9) confirmed every seam. This section pins the exact type shapes the build uses and
> **supersedes** two approximations in §2a/§3 above; the superseded text is marked inline.

### 3a.0 ⛔⛔ TWO `EntityMaster` TYPES — the barrier lives on the NED one *(measured `2026-09-15`, corrects the Step-0 V2 note)*
There are **two parallel DDS descriptor families**, both on `CycloneDDS.Runtime`, in **separate topic namespaces**:
| family | `EntityMaster` type | topic | `Flags` | used by |
|---|---|---|---|---|
| ⭐ **NED** | `Hrot.NED.Descriptors.EntityMaster` (`GenericDescriptors.cs:78`) | `"EntityMaster"` | **`ulong`** (`:102`), `[DdsQos(Reliable, TransientLocal, KeepLast 1)]` — durable | ⭐⭐ **`--mode all` entity replication** (`NedNetworkFactory`; CGF/SimHost/IG via `CreateReplicationModule()` → `INedReplicationModule`) |
| Cyclone/SST | `Fdp.Network.Cyclone.Topics.EntityMasterTopic` (`:17`) | `"SST_EntityMaster"` | `int` (`:29`) | the SST/BDC + examples path — **NOT** `--mode all` entity replication |

⛔⛔ **The Step-0 V2 note ("Flags is `int` not `ulong`") measured the CYCLONE type by mistake.** The descriptor
that makes the peer's ghost in `--mode all` is the **NED** `EntityMaster`, whose `Flags` is **`ulong`** — so the
design's original "`ulong`" was correct, and the reserved `WaitForAcks` bit rides the **NED** `EntityMaster.Flags`.
⇒ **A2's egress edit is `EntityMasterEgressTranslator.cs:103`** (which writes the NED `EntityMaster`, `Flags = 0`
today), registered by the NED `SharedTranslatorPack`.

⭐ **The status descriptor stays the Cyclone orphan `EntityLifecycleStatusDescriptor`** *(§2a/§3a.1)*: it lives in
the generic `Fdp.Network.Cyclone` layer, which **`Hrot.Network.NED` references** (`csproj:39`), so a NED system
publishes/subscribes it on the NED participant. That is exactly §2c's "one generic type, both transports" — the
seam-law adoption of the orphan, in the shared layer, is what makes the protocol generic across NED and BDC.

### 3a.1 The durable descriptor — real DDS convention *(supersedes §2a's `[DdsStruct]` shorthand)*
⛔ **There is NO `[DdsStruct]` attribute in this codebase.** Measured on the sibling `EntityMasterTopic.cs`:
a Cyclone topic is a **`public partial struct`** with `[DdsTopic(name)]` + a type-level `[DdsQos(…)]`, and
`[DdsKey, DdsId(n)]` on each field. `EntityLifecycleStatusDescriptor` today is a **`class` with `{get;set;}`
properties and zero DDS attributes** (V1) — A1 **converts it to a struct with fields**. Zero refs (V1) → the
conversion breaks nothing.

```csharp
[DdsTopic("SST_EntityLifecycleStatus")]
[DdsQos(Reliability = DdsReliability.Reliable,
        Durability  = DdsDurability.TransientLocal,   // ⭐ retained → late joiners (§2a)
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1)]                             // one retained sample per instance
public partial struct EntityLifecycleStatusDescriptor
{
    [DdsKey, DdsId(0)] public long EntityId;   // network entity id
    [DdsKey, DdsId(1)] public int  NodeId;     // §2b: the OwnershipUpdate node-id, NOT the roster id
    [DdsId(2)]         public int  StateValue; // EntityLifecycle as int — see note
    [DdsId(3)]         public long Timestamp;  // WallTicksUtc / GlobalVersion
}
```
⭐ **`State` travels as `int StateValue`, cast to/from `EntityLifecycle`** — the same wire-precedent CE-282
(role propagation) set for `RolesMask`: keep the DDS codegen free of a cross-assembly enum dependency. The
composite `[DdsKey]` on `(EntityId, NodeId)` gives one retained instance per (entity, reporting node), exactly
as §2a requires.

### 3a.2 The reliable bit on the NED `EntityMaster.Flags` *(the field is `ulong` — §3a.0)*
```csharp
// Hrot.Network.NED generic layer — one reserved bit, host-ignorable.
[Flags] public enum EntityMasterFlags : ulong { None = 0, WaitForAcks = 1UL << 0 }
```
- **Egress (A2):** `EntityMasterEgressTranslator.cs:103` stops writing `Flags = 0`; it writes
  `WaitForAcks` when the source entity's `PendingNetworkAck.ExpectedType != None`.
- **Peer read (A2):** at ghost creation the peer reads `(EntityMasterFlags)master.Flags`; if `WaitForAcks`
  is set it tags the ghost *report-on-Active* (a transient tag component, e.g. `ReportLifecycleOnActive`).

### 3a.3 `ExpectedAckPeers` carrier *(supersedes §1.2's `int[]` on the struct)*
`PendingNetworkAck` is a **transient struct component** (`NetworkComponents.cs:27`). Rather than a mutable
`int[]` that shrinks (awkward in a blittable component), the component carries an **immutable snapshot** and
the gateway keeps the shrinking set in its own `_pendingPeerAcks` dict (which already exists):
```csharp
public struct PendingNetworkAck {
    public ReliableInitType ExpectedType;
    public PeerSetHandle     ExpectedAckPeers; // A5: snapshot from NodeRoster.NodesWithRole(mask) minus local
}
```
⚠ **Component-array caveat:** ECS transient components are ideally blittable; an `int[]` is a managed ref. A5
stamps the peer set via a **side table keyed by the creator's local spawn** (the same shape the gateway's
`_pendingPeerAcks` uses) OR a small fixed inline buffer — the build picks the one that keeps `PendingNetworkAck`
blittable; the peer-set VALUE is what matters, not its carrier. Documented as an open build choice, resolved in
the report.

### 3a.4 The creator waiter reslot *(A6 — retire `INetworkTopology` as the peer source)*
`NetworkGatewaySystem` already has the whole pending-set + timeout + destruction-cleanup machine
(`_pendingPeerAcks`, `ReceiveLifecycleStatus`, `CheckPendingAckTimeouts`). Today it seeds the set from
`_topology.GetExpectedPeers((long)ExpectedType)` (`:125`). ⛔ `INetworkTopology` has **no production impl**
(§0). A6 seeds `peerSet` from `pendingInfo.ExpectedAckPeers` (§3a.3) instead; the reactive
`ReceiveLifecycleStatus`, the timeout and the cleanup are unchanged. The ctor drops the `INetworkTopology`
dependency (or accepts `null` in production).

### 3a.5 `DeferredConstructionParticipant` base (B) — the shared shape
The gateway hand-rolls: register-with-ELM · on `ConstructionOrder` decide ack-now/defer · hold a pending set ·
ack on empty-or-timeout · `DestructionAck` cleanup. The base lifts exactly that; two completion modes:
| mode | who | how it becomes ready |
|---|---|---|
| **reactive** | `NetworkGatewaySystem` | external `ReceiveLifecycleStatus` drops peers from the set |
| **poll** | synthetic test participant (slice A) · navmesh/model (piece C) | base calls `bool TryComplete(entity)` each frame; ack when true |
Base owns: `RegisterModule`, `_pendingStartFrame` timeout, `DestructionOrder` cleanup + `DestructionAck`, the
`AcknowledgeConstruction` plumbing. Subclass owns: seed-the-pending-state on construction + the ready signal
(reactive override OR `TryComplete`). ⭐ Slice A proves the base with a **synthetic poll participant**; piece C
adds the real navmesh/model subclasses without copying the machine (ruling 9).

### 3a.7 ⭐ AS-BUILT STATUS — slice A, session `2026-09-15` *(obligation ⑤)*
| item | state | where |
|---|---|---|
| **B** `DeferredConstructionParticipant` base | ✅ **BUILT + unit-proven** | `Fdp.Toolkits/Replication/Systems/DeferredConstructionParticipant.cs`; poll-mode rail green |
| **A6** gateway reslot onto the base + `NetworkAckPeerSet` peer source | ✅ **BUILT + unit-proven** (7/7 `NetworkGatewaySystemTests`) | `NetworkGatewaySystem.cs`; `INetworkTopology` retired as the gateway peer source (kept for Ownership systems) |
| **A5** carrier `NetworkAckPeerSet` (managed, id 145) | ✅ **BUILT** (carrier); ⛔ **production STAMP pending** | `NetworkComponents.cs`; the stamp at `NetworkSpawningSystem.cs:184` needs the membership source — see the open question below |
| **A1** durable `EntityLifecycleStatusDescriptor` | ✅ **BUILT + compiles** | `Fdp.Network.Cyclone/Topics/…`; keyed `(EntityId,NodeId)`, Reliable+TransientLocal+KeepLast1 |
| **A2** `WaitForAcks` bit — egress write + peer tag | ✅ **BUILT + compiles** | egress `EntityMasterEgressTranslator.cs:103`; peer tag `EntityMasterIngressTranslator` → `ReportLifecycleOnActive` (id 146) |
| **A4** `PeerLifecycleStatusEgressSystem` | ✅ **BUILT + compiles**; ⛔ **not registered** | `…/Egress/PeerLifecycleStatusEgressSystem.cs` |
| creator status ingress | ✅ **BUILT + compiles**; ⛔ **not registered** | `…/Ingress/PeerLifecycleStatusIngressTranslator.cs` |
| **A6** gateway CONSTRUCTION in prod + register the 2 translators | ✅ **BUILT + no-regression proven** | `NedReplicationModule.cs`; the gateway is a GLOBAL ELM participant (id 918273) on every NED node; the existing gateway + 6 split-authority rails pass |
| **A5** membership SOURCE (creator → peer set) | ✅ **BUILT** (option A: all present peers except local) | `ClusterCacheExpectedPeersProvider` on the CGF adapters' cluster cache → `EntityCreationContext.ExpectedPeers` → the spawn stamp; ⚠ CGF-only for now (SimHost/IG-as-creator is a follow-on) |
| **live barrier proof** | ✅ **PROVEN** (integration, real CycloneDDS) | `NetworkGatewayIntegrationTests.ReliableInitBarrier_EngagesAndReleases…` — creator held → peer Active over the wire → released; non-empty stamped peer set asserted |
| **A7** late-joiner read path | ⚠ **partial** — descriptor is durable (built + delivered), but the "hold ghost until owner Active" JOIN and an explicit late-subscribe rail are NOT built | §2a — follow-on |
| §2b node-id EXPLICIT rail | ⚠ **built + exercised, not separately asserted** | the peer stamps its own node id; the creator correlates it — proven implicitly by the release, not by a dedicated node-id assertion |
| synthetic delay participant | ⚠ **not built** — the natural DDS round-trip proved the hold without an artificial delay | piece C brings the real navmesh/model participants |
| ⛔ id-collision lesson | 📌 `ReportLifecycleOnActive` first took id 146, which COLLIDED with `BehaviorApplicationComponentIds.BTreeTraceWorkingMemory` — the `GlobalComponentIds` "reserved" comment lied; Behavior/Utility allocate from the same space. Caught by RUNNING the feature's rail (a runtime static-init check). Moved to 152. **Enumerate real `[ComponentId]` usage; never trust a range comment.** |

✅ **A5 DECISION — OPTION A APPROVED** *(user, `2026-09-15`, via H-coord relay: "A, go with the lean").*
At spawn the creator resolves the peer set through an injected **`IExpectedPeersProvider`** seam
(so `NetworkSpawningSystem`, generic in `Fdp.Toolkits`, takes no NED dependency) and stamps
`NetworkAckPeerSet`. For this slice the provider yields **all present peers except local** (the NED cluster
cache `NodeCapability` list minus the local node); the real **type→required-roles** filtering is deferred to
piece C with the actual navmesh/model participants.

⚠⚠ **PROOF-CORRECT, NOT PRODUCTION-CORRECT.** "All peers wait" is correct for the synthetic proof.
⛔⛔ **SUPERSEDED-FOR-PRODUCTION by §3b (user, `2026-09-15`).** The earlier framing here — "piece C role-FILTERS
the peer set by which roles initialise the type" — was RETRACTED: the peer's wait is host/type-specific and
opaque to the creator, so there is no creator-side role→type filter. §3b is the real production model: the
**creator supplies an OPTIONAL wait-node-list** (unset ⇒ all present; the creator MAY narrow it by role as its
own policy) **+ a timeout**, and **every listed node replies** (immediately if it isn't waiting). The membership
source is still the NED cluster cache `NodeCapability.Role` (`NedNetworkFactory.cs:412`, CE-282) when the creator
chooses to narrow.

### 3a.6 `RegisterRequirement` — zero callers *(V6 correction)*
`EntityLifecycleModule.RegisterRequirement(long tkbType, int moduleId):158` has **zero callers, not "tests
only"** as the handoff said. It is the per-type participant-registration seam the poll participants (synthetic
here, navmesh/model in C) will be its first callers of — dormant-by-design, revived here.

## 3b. ⭐⭐⭐ PIECE C — THE PRODUCTION MODEL *(resolved with the user, `2026-09-15`)*
> This section is the **authoritative contract** for piece C. It supersedes, for production: slice A's option-A
> "all present peers" (§3a.5) and the retracted `[RequiresPeerInit]`/role→type-filter machinery (STATUS
> `known-rot`). It is **DESIGNED, not yet dispatched.**

⭐ **The governing principle (user):** the requestor sets a **bit** ("this entity needs reliable init") and, at
most, a **node list + timeout**. It does **NOT** know or specify *what* each node waits for — the wait condition
is **host-specific and entity-type-driven, decided entirely on each receiver, and opaque to the creator**
(navmesh tile, textures, model — anything). The creator never computes a role→type filter.

### 3b.1 The creation request *(fields on the local `SpawnEntityCommand`)*
| field | meaning | default |
|---|---|---|
| `InitType = AllPeers` | engage the barrier (EXISTS) | `None` (fast mode) |
| `RequestId` | correlation for the result store (EXISTS) | — |
| 🆕 `ReliableInitPeers : int[]?` | the node ids to wait for; the creator **may** narrow this by role *(its own policy, from the NED cache `NodeCapability.Role`, CE-282)* | **null ⇒ wait for ALL present nodes** at request time |
| 🆕 `ReliableInitTimeout : TimeSpan?` | creator-supplied bound *(honours "wait as long as necessary" — a slow type sets a longer bound)* | a sane default |

### 3b.2 Mandatory reply — every listed node replies exactly once
On receiving a reliable `EntityMaster` (the `WaitForAcks` bit), a node **must** send exactly one reply:
- ghosted + local condition → reply when it clears; ghosted + no condition → reply immediately *(slice A does this via the `Constructing→Active` egress)*;
- ⭐ **NOT ghosting / not applicable → reply immediately anyway** *(the NEW branch — from the `EntityMaster` ingress, since there is no ghost to drive the egress)*. The reply means *"I'm not blocking,"* whatever the reason. ⇒ the creator never hangs on a non-participant.

### 3b.3 Timeout = ABORT via `EntityMaster` dispose *(supersedes the inherited force-ack)*
The **creator owns one authoritative timeout** (`ReliableInitTimeout`). On expiry with the wait-set unsatisfied:
the creator **disposes the `EntityMaster` DDS instance** *(`EntityMasterEgressTranslator.Dispose`)* → every
receiver gets the `NotAliveDisposed` sample → the existing `EntityMasterIngressTranslator.ProcessDispose`
removes the ghost + `DeferredConstructionParticipant.OnDestroyed` cancels the local wait. **No `DestroyEntityCommand`
crosses DDS** — the disposal sample is the teardown. ⭐ Bonus: the ingress fires on `InstanceState != Alive`,
covering **writer-death (liveliness)** too, so a creator crash also cleans up. ⛔ The receiver **never
self-force-activates** a reliable ghost — it waits for its condition or the creator's disposal.

### 3b.4 The result — a LOCAL poll-store *(mimics `PathfindingBatchData`; NOT the remote `CreateEntityAck`)*
⛔ **Two different paths — do not conflate** *(the mistake that produced this section)*:
- **Remote** creation *(explicit DDS `CreateEntityRequest` from a tool)* → replied by the two-phase
  `CreateEntityAck`/`EntityLifecycleAckDto` *(owned by DESIGN-SIMHOST/DESIGN-IG)*. **Not this.**
- **Local** creation *(a code call on the owning node)* → the result is written to a **store the local requestor
  polls** — the pattern `PathfindingBatchData`/`GetPathResult` already uses for "async result in a ticked system."

| the store | spec |
|---|---|
| **what** | a **singleton ECS component** on the creator's world *(like `PathfindingBatchData`)* holding `ConstructionResult { NetworkId, Outcome: Pending\|Success\|Failed, Reason: None\|Timeout\|PeerRejected }` |
| **where** | the generic replication toolkit *(`Fdp.Toolkits`)*, beside the barrier systems |
| **keyed by** | the entity's **`NetworkId`** *(the local owner allocates it at the create call, so the requestor holds it immediately)* |
| **retention** | ⭐ **a dictionary keyed by `NetworkId` + TTL, evict-on-read** *(user choice — a lost creation-FAILURE must not be silently overwritten; creation volume is low, so a dict is affordable, unlike Pathfinding's collision-tolerant ring)* |
| **written by** | the creator-side gateway on barrier resolution: pending-set empties → `Success`; timeout → `Failed(Timeout)` *(it already knows both — today it force-acks instead)* |
| **read by** | the local requestor: `ConstructionResults.Get(world, networkId)` each tick → `Pending` until resolved *(survives slow/irregular ticking — the reason it is a store, not a transient event)* |

```mermaid
sequenceDiagram
    autonumber
    participant REQ as Local requestor (code)
    participant CR as Creator (ECS + gateway)
    participant W as DDS wire
    participant PX as Peer node
    participant ST as ConstructionResults store
    REQ->>CR: SpawnEntityCommand WaitForAcks RequestId Peers? Timeout?
    CR->>CR: create entity allocate NetworkId BeginConstruction
    CR->>ST: NetworkId = Pending
    CR-->>W: EntityMaster with WaitForAcks flag plus baseline
    W-->>PX: ghost then reply now if not waiting else hold on local condition
    PX-->>W: EntityLifecycleStatusDescriptor Active
    W-->>CR: status ingress drops peer from wait-set
    alt all listed peers replied
        CR->>ST: NetworkId = Success
    else timeout expires
        CR-->>W: dispose EntityMaster instance
        W-->>PX: disposal sample removes ghost and cancels wait
        CR->>ST: NetworkId = Failed Timeout
    end
    REQ->>ST: poll Get NetworkId each tick returns Pending or Success or Failed
```
*Caption: what this shows that §1.1 could not — the LOCAL result path (store + poll) and the abort-by-dispose leg; the peer's wait condition is a black box (`hold on local condition`), never a creator-computed filter.*

### 3b.5 The API the creator calls
```csharp
var reqId = Guid.NewGuid();
eventBus.PublishManaged(new SpawnEntityCommand {
    TkbType = tkbType, InitType = ReliableInitType.AllPeers, RequestId = reqId,
    ReliableInitPeers   = null,                       // null ⇒ all present; or a (role-narrowed) id list
    ReliableInitTimeout = TimeSpan.FromSeconds(10),   // 0/unset ⇒ default
    InitialTransform = ...,
});
// later, at the requestor's OWN cadence — keyed by the NetworkId it got at the call:
var r = ConstructionResults.Get(world, networkId);   // Pending → Success | Failed(Timeout)
```

### 3b.6 ✅ AS-BUILT — the CREATOR SIDE (C1 + C3 + C4), `2026-09-16` *(obligation ⑤)*
| item | as-built |
|---|---|
| **C1** (CE-287) wait-set | `SpawnEntityCommand.ReliableInitPeers:int[]?` + `ReliableInitTimeout:TimeSpan?`. `ClusterCacheExpectedPeersProvider` filters present-minus-local to `Supports(fdp.reliable-init)` (§3c ① — non-supporting host never waited for). `NetworkSpawningSystem.IntersectReliablePeers` narrows by the optional creator list; stamps `NetworkAckPeerSet{ExpectedAckPeers, TimeoutSeconds}`. |
| **C3** (CE-289) timeout=abort | New base hook `DeferredConstructionParticipant.OnTimeout` (default = legacy force-ack; per-entity timeout via `SetPendingTimeout`, seeded from `NetworkAckPeerSet.TimeoutSeconds` @ 60 fps). `NetworkGatewaySystem.OnTimeout` OVERRIDES to ABORT: write `Failed(Timeout)` + `_elm.BeginDestruction`. ⭐ **Measured finding folded here:** `BeginDestruction` emits a `DestructionOrder` that **`CycloneNetworkCleanupSystem` already turns into an `EntityMaster` dispose sample** (`translator.Dispose(netId)`) — so the abort disposes the wire instance with **no new cross-layer seam**; the receiver's existing `ProcessDispose` removes the ghost. The creator never force-acks a reliable entity. |
| **C4** (CE-290) poll-store | `ConstructionResults` — a managed singleton (component id **153**, verified free) holding a `Dictionary<long,Entry>` keyed by `NetworkId`, evict-on-read of terminal results + TTL sweep. Gateway writes `Pending` on defer, `Success` in `ReceiveLifecycleStatus` when the wait-set empties, `Failed(Timeout)` on abort. ⚠ The reactive success path has no `view`, so the gateway caches the persistent `EntityRepository` at defer time. ⚠ `GetSingletonManaged` **throws** when unset — guarded with `HasSingletonManaged`. |
| rails | `ClusterCacheExpectedPeersProviderTests` 2/2 · `ConstructionResultsTests` 5/5 (incl. a uint-underflow red-proof in the TTL sweep) · `NetworkGatewaySystemTests` 8/8 (+ the C3 abort rail; the 7 pre-existing green). |
| ⛔ REMAINING | **C2** (CE-288, peer-side mandatory reply + short phase-1 probe + `!fdp.reliable-init` self-heal) and **C5** (CE-291, real navmesh/altitude Muscle participant) + the `--mode all` proof — the peer-side + live pieces. |

## 3c. ⭐ GRACEFUL DEGRADATION — a host that does not support reliable init *(AQ-70, `2026-09-16`)*
> Full design + the decision: **[`Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md`](blueprints/Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md)**. This is the consumer summary.

> ✅ **AS-BUILT `2026-09-16` (CE-285 C-cap + CE-286 C-roles) — the host-capability facility + roles-from-tokens
> are BUILT** (mechanism ① below; mechanism ② the short phase-1 probe ships with C2). What landed:
> - **`NodeCapabilitiesTopic`** — a durable DDS descriptor (`[DdsQos(Reliable, TransientLocal, KeepLast 1)]`,
>   keyed by `NodeId`, `CapabilitiesJson` = a JSON `string[]` of namespaced tokens), added to
>   `OrchestrationMessages.cs` beside `NodeHeartbeat`. Published ONCE at join by `ClusterSlave`
>   (`NodeCapabilitiesEvent`, EventId 9022), egressed by `NodeOpSlaveTranslator`, ingested by
>   `OrchestrationObserverTranslator` + `NedOrchestrationTranslator` (orchestrator roster) and by
>   `NedNetworkFactory.PollNetwork` (the NED cluster cache the barrier's wait-set reads).
> - **Tokens:** `NodeRoleTokens` (`Fdp.Core`) is the closed enum↔token table (`fdp.role.brain`,
>   `fdp.role.muscle-ground`, `fdp.role.map2d`, `fdp.role.perception`, `fdp.role.navigation-solver`);
>   `CapabilityTokens.ReliableInit = "fdp.reliable-init"` (`Fdp.Toolkits.Replication`) is the first feature token.
>   Every NED node (CGF/SimHost/IG/`HrotNodeBuilder`) advertises `fdp.reliable-init`; the orchestrator advertises nothing.
> - **Roles-from-tokens (C-roles):** `NodeHeartbeat.RolesMask` and `NodeHeartbeatEvent.Roles` are **REMOVED** — the
>   heartbeat is telemetry-only. `NodeHealthProfile.Roles` and `NodeCapability.Role` are now **DERIVED** at ingest via
>   `NodeRoleTokens.MaskFromTokens` (supersedes CE-282). `NodesWithRole` + ownership (`GetLeastLoadedNode`) consume the derived mask, unchanged.
> - **Query:** `IClusterStateCache.Supports(nodeId, token)` + `NodeRoster.Supports(nodeId, token)`.
> - Rails: `NodeRoleTokensTests` (round-trip, degrade), `ClusterSlaveHeartbeatTests` re-homed to the capability event.
> ⚠ Mechanism ② (short phase-1 probe + `!fdp.reliable-init` self-heal) is C2, not yet built.

### 3c.1 The as-built capability flow — advertise once → gather → derive → filter *(CE-285/286)*
*What this shows that §3c's prose cannot: the token set has ONE producer and TWO independent consumers on
different reads (the orchestrator roster and the NED cache), the `NodeRole` mask is derived at BOTH ingests
from the same `NodeRoleTokens` table, and the barrier's wait-set is filtered by `Supports(fdp.reliable-init)`
— never by a creator-computed role→type map.*

```mermaid
sequenceDiagram
    autonumber
    participant SL as ClusterSlave (any node)
    participant EG as NodeOpSlaveTranslator (egress)
    participant W as DDS NodeCapabilities (durable)
    participant OR as Orchestrator ingest (ClusterMaster)
    participant NF as NedNetworkFactory PollNetwork (NED cache)
    participant WS as Creator wait-set (C1)
    SL->>EG: NodeCapabilitiesEvent once at join (fdp.role.* + fdp.reliable-init)
    EG->>W: write retained sample (CapabilitiesJson)
    W-->>OR: retained tokens
    OR->>OR: NodeHealthProfile.Capabilities + Roles = NodeRoleTokens.MaskFromTokens
    W-->>NF: retained tokens
    NF->>NF: NodeCapability.Capabilities + Role = NodeRoleTokens.MaskFromTokens
    WS->>NF: Supports(peer, fdp.reliable-init)?  (include only if true)
```
*Caption: a node advertising no `fdp.reliable-init` token is never in a wait-set — capability degradation (§3c ①) — and a node advertising no `fdp.role.*` token derives to `NodeRole.None`.*

A peer that does not support reliable init never publishes `EntityLifecycleStatusDescriptor`. It must never make a
creator block forever. Two composed mechanisms:
- **① capability filter (proactive):** hosts advertise capabilities as an **OpenGL-extension-style namespaced token
  set** *(`fdp.reliable-init`, …)* on a durable `NodeCapabilities` descriptor gathered by the orchestrator into the
  roster/cache. ⭐ **Roles are the bit-backed SUBSET of this same token set** *(`fdp.role.*`)* — the tokens are the
  **sole wire source** and the `NodeRole` mask is **DERIVED at ingest** via a closed enum↔token table, superseding
  CE-282's roles-on-heartbeat *(AQ-70 §Q70-C; the CE-282 rework is part of piece C)*. The creator's wait-set
  *(§3b.1)* includes **only** nodes advertising `fdp.reliable-init`. A non-advertising host is never waited for.
- **② short phase-1 probe (reactive, self-healing):** a supporting host publishes `State=Constructing` **promptly**
  on receipt *(the §3b.2 mandatory reply, now as the fast phase-1 ack)*, then `Active`. The creator arms a **short
  phase-1 timeout**; missing phase-1 ⇒ the host doesn't support reliable init ⇒ **drop it from the wait-set + record
  a `!fdp.reliable-init` override** on its roster entry so later creates skip it. The **long** `ReliableInitTimeout`
  *(§3b.3)* still governs phase-2 *(Constructing-but-never-Active ⇒ abort-via-dispose)*.

⭐ Requestor-facing status reuses `SstStatusCode.NotSupported`/`InProgress` *(two-ack §3.2)* — not reinvented. The
capability facility is **general**; `fdp.reliable-init` is its first token.

## 4. ✅ THE RECEIVER-SIDE GATE IS REUSE
The peer's "is my data ready?" gate already exists: `GhostPromotionSystem`'s mandatory-components gate *(HARD,
no timeout)* + the receiver's local ELM. The navmesh/model participants are **additional local participants
on that existing gate**, not a new pipeline. A peer reaches `Active` only after they ack — which is precisely
the moment `PeerLifecycleStatusEgressSystem` publishes.

## 5. ⛔ PREREQUISITES — order before READY-TO-BUILD
1. **P1** — carry `NodeRole` mask on `NodeHeartbeatEvent` *(the 3 producers know the node's boot role)*;
   **retire `NedNetworkFactory.MapSubsystemNameToRole`** *(lossy name→single-role switch)*.
2. **P2** — store the mask on `NodeHealthProfile` *(and `NodeCapability`)*.
3. **P3** — add `NodeRoster.NodesWithRole(NodeRole)` *(and a cache equivalent)*.
4. **User nod:** confirm reliable is opt-in and that display replicas are in scope of the wait *(both are the
   current rulings)*.

### ✅ P1–P3 AS-BUILT (`2026-09-15`) — role propagation done; the barrier itself is still DESIGN
> ✅⛔ **TRANSPORT SUPERSEDED AND REBUILT by CE-286 (C-roles, `2026-09-16`):** P1 below carried the role mask as
> `NodeHeartbeat.RolesMask` (per-tick, CE-282). That field — and `NodeHeartbeatEvent.Roles` — are now **REMOVED**:
> the host publishes `fdp.role.*` **capability tokens** on the durable `NodeCapabilities` descriptor (CE-285) and
> the orchestrator/cache **DERIVE** the `NodeRole` mask at ingest via `NodeRoleTokens.MaskFromTokens`; the heartbeat
> is telemetry-only. The `NodesWithRole` query + ownership tables (P2/P3) are UNCHANGED — they consume the derived
> mask. Full as-built in §3c. The P1 text below is the CE-282 as-built (HISTORY — the RolesMask wire it describes is gone).
> The §0 INVENTORY sites were accurate and unchanged; the §1.2 class diagram's P1/P2/P3 boxes are the
> as-built shape. The wire gained one field (a field, not a new shape) → no new diagram.

- **P1 — role on the heartbeat + wire; switch retired.** `NodeHeartbeatEvent` gained `NodeRole Roles`
  (`ClusterCqrsEvents.cs`); the DDS `NodeHeartbeat` topic gained `int RolesMask` (`OrchestrationMessages.cs`
  — carried as the `[Flags]` int to keep the wire free of a cross-assembly enum codegen dep). `ClusterSlave`
  takes the mask (`_roles`, both ctors default `None`) and stamps it on every heartbeat; the egress
  (`NodeOpSlaveTranslator`) writes `RolesMask`, both ingress bridges (`OrchestrationObserverTranslator`,
  `NedOrchestrationTranslator`) read it back. **`NedNetworkFactory.MapSubsystemNameToRole` is DELETED** —
  `PollNetwork` reads `(NodeRole)sample.Data.RolesMask` into `NodeCapability.Role` (seam law; an un-set/foreign
  node reads `None`, the correct "unknown role", so no name fallback). ⭐ The 4 role-bearing producers are
  wired at their `ClusterSlave` sites: CGF `DefaultRole`(Brain) · SimHost `role` (the full mask, multi-role
  preserved) · IG `Map2D` · `HrotNodeBuilder._role`. The orchestrator stays `None`.
  ⚠ **Latent bug fixed in passing:** `HrotNodeBuilder.WithRole` **silently discarded** its `role` arg — the
  same "role dropped" disease one level up; it now retains `_role` and publishes it.
- **P2 — roster stores the mask.** `NodeHealthProfile` gained `NodeRole Roles`; `ClusterMaster.IngestHeartbeats`
  sets it from `hb.Roles`. `NodeCapability.Role` is populated from the real mask via P1's factory change.
- **P3 — membership query.** `NodeRoster.NodesWithRole(NodeRole)` yields active ids where `mask & role != 0`
  (`[Flags]` intersect, so a multi-role node matches EITHER role). No cache accessor added — the roster is the
  orchestrator's membership authority for this slice; a `SimpleClusterStateCache` equivalent lands with the
  barrier consumer that needs it.
- **Rails:** `NodeRosterTests` (new — P2/P3, incl. a multi-role `&`-vs-`==` red-proof); `ClusterSlaveHeartbeatTests`
  extended to assert a `MuscleGround|Perception` mask survives the heartbeat un-collapsed (P1).
- ⛔ **Stopped at the P1–P3 boundary** — the barrier / gateway / participants (pieces A–C) are untouched, still DESIGN.

## 6. ⭐ ACCEPTANCE — the proof, not a green gate
`--mode all`, reliable entity: spawn a spatial type in an **uncached** area on a Muscle peer → the CREATOR
stays `Constructing` → the Muscle navmesh participant acks when the tile loads → the peer goes `Active`,
publishes the status → the creator goes `Active`. Then: a display-only Map2D peer with a slow model-load
holds the creator too; and the **timeout** path force-activates. T-1: extend `EntityLifecycleModuleTests` +
`NetworkGatewaySystemTests`; add an integration rail. Fast mode unchanged (a regression rail).

**Late-joiner case (§2a):** a node joins the cluster AFTER a reliable entity was created and is still
`Constructing` on a peer → it reads the retained `EntityMaster.Flags(WaitForAcks)` + the retained
`EntityLifecycleStatusDescriptor` instances → it holds the entity unusable until the OWNER's status instance
reads `Active` → when the peer finishes and re-publishes `Active`, the joiner releases it. Prove the durability
path directly: subscribe late, assert the retained samples arrive with no live transition. **Node-id (§2b):**
assert the `NodeId` on a status instance equals the `NewOwner` the same node stamps on `OwnershipUpdate`.
