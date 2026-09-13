<!--STATUS
state: LIVE
build-state: N/A — this document describes a sequence that EXISTS. It specifies no new work.
updated: 2026-09-13
verified: 2026-09-13 — every stage below read from source this session, file:line inline.
  ⚠ check_index_coverage is NOT reachable through the codebase-memory CLI, so no coverage
  attestation is claimed; the enumerations were run through search_graph AND grep and agree.
current-answer: §1 IS THE DOCUMENT. Read the four diagrams in order and you have the whole genesis
  path. §2 is the stage table — it is a ROUTING TABLE to the owning designs, nothing more. §3 is the
  one question this document answers that no owner does. §4 is scope. §5 is rot found elsewhere.
owns: ⭐ EXACTLY ONE THING — THE END-TO-END STAGE SEQUENCE AND ITS VOCABULARY. No owning design has
  it, because each owns one stage. If a stage moves, THIS FILE IS WRONG and must be updated with it.
owns-nothing-else: ⛔ Do NOT cite this document to justify a change to any mechanism it draws.
  Every box belongs to an owner named in §2; the owner decides, this file only says where it sits.
  ⛔ And no structural fact here may be restated in an owning design, or the two will rot apart.
related-designs:
  - DESIGN_Entity_Creation_Unification.md — owns the CREATE leg and EntityCreationPack: who may
    request an entity, which systems every host composes, and the authoring affordances.
  - DESIGN_Role_Affinity_Ownership.md — owns WHO OWNS WHAT: the role tables, the creator's
    birthright, and the two legs (create claims, promote claims) that must stay complementary.
  - designs/tkb-1/DESIGN.md — owns the TKB: templates, descriptors, translators, and the two derived
    component lists (birth-critical, mandatory) that gate the birthright and the promotion.
  - DESIGN_Entity_State_Sourcing.md — owns WHERE STATE MAY COME FROM (TKB defaults vs the wire);
    this file shows when each source is consulted, not which is legitimate.
  - blueprints/Architect_Question_65_Entity_Genesis_Uniformity.md — owns the RULING that there is one
    uniform pipeline rather than role-selected halves. This file draws the pipeline that ruling chose.
  - projects/Hrot/Network/Hrot.Network.NED.md — owns the NED wire: topics, QoS, the translator
    inventory, and "Diagram 3", the deferred-takeover protocol at DDS level.
known-rot: none of its own. §5 records rot found in OTHER documents.
-->

# ⭐⭐⭐ ENTITY GENESIS, END TO END — **the landing page**

> ⭐⭐ **What this is for.** The genesis path crosses **five** owning designs and **three** assemblies.
> Each owner is correct and none of them draws the whole line, so the shape has only ever existed in
> people's heads — and the one narrative that did draw it *(`projects/relationships/Hrot-Simulation-Pipeline.md`
> §4.3)* predates ghost promotion entirely and still reads as current. ⇒ **this file is the map.**

> ⛔⛔ **It is a MAP, not a territory.** Every mechanism here has an owner in §2. If this file and an
> owning design disagree, **the owner wins and this file is the bug.**

---

## 1. ⭐⭐⭐ THE DIAGRAMS — *this is the document; §2 onward is routing*

### 1.1 The states, and what moves an entity between them

*What the picture shows that prose hides: **`Ghost` is not a stage of the creator's path at all.** The
creating node never has one. Two different nodes are drawn here, and only one of them ever waits.*

```mermaid
stateDiagram-v2
    direction LR

    state "CREATOR NODE" as C {
        [*] --> Constructing_C : NetworkSpawningSystem.ProcessSpawn<br/>entity exists fully formed
        Constructing_C --> Active_C : ELM — all module ACKs in
    }

    state "RECEIVING NODE" as R {
        [*] --> Ghost : GhostCreationSystem.CreateGhost<br/>called BY an ingress translator
        Ghost --> Ghost : waiting — a derived HARD<br/>requirement has not arrived
        Ghost --> Constructing_R : GhostPromotionSystem<br/>all mandatory components present
        Constructing_R --> Active_R : ELM — all module ACKs in
        Ghost --> [*] : GhostTimeoutSystem
    }

    note right of Ghost
        Only a RECEIVER has this state.
        The gate is DERIVED per host, not authored.
    end note
```

### 1.2 The genesis sequence across the wire

*What the picture shows that prose hides: **the order of the last three steps.** The grant arrives
**first** and waits; the takeover fires **after** promotion, not on arrival — so anything that delays
promotion delays the authority handover with it.*

```mermaid
sequenceDiagram
    autonumber
    participant A as Authoring / ExCon
    participant CR as CREATOR node
    participant W as DDS wire
    participant RX as RECEIVING node

    A->>CR: RequestEntityCreation
    CR->>CR: CreateEntityRequestSystem — validate TkbType, allocate network id
    CR->>CR: NetworkSpawningSystem — create entity, run TKB translators
    Note over CR: AuthorityMask = componentMask AND OwnableMask<br/>isCreator TRUE — keeps the birthright
    CR->>CR: ELM BeginConstruction

    CR-->>W: DeferredTakeOwnership — grants addressed to a node id
    W-->>RX: DeferredTakeOwnershipIngressTranslator
    RX->>RX: CreateGhost + attach PendingAuthorityGrants
    Note over RX: the grant arrives BEFORE the entity exists

    CR-->>W: EntityMaster — reliable, transient-local
    CR-->>W: WorldPos, EntityInfo — the guaranteed baseline
    W-->>RX: ingress translators write SimTransform, EntityInfo

    RX->>RX: GhostPromotionSystem — are the derived mandatory components here?
    Note over RX: HARD, no timeout. Missing means return,<br/>and after 600 frames it says WHICH one.
    RX->>RX: run TKB translators, then claim by role — isCreator FALSE, no birthright
    RX->>RX: Ghost becomes Constructing

    RX->>RX: DeferredTakeoverSystem — queries Constructing, claims the grants
    RX-->>W: OwnershipUpdate
    W-->>CR: OwnershipIngressSystem drops the matching authority bits
    RX->>RX: ELM ACKs complete, entity becomes Active
```

### 1.3 Who registers what — ⚠ **and what never ticks**

*What the picture shows that neither of the above can: **`DeferredTakeoverSystem` is unreachable on a
host without the NED replication module.** It is not scheduled — it sits inside a private group whose
`ExecuteGroup` has **exactly one** production caller. On a BDC node or the editor's
`NullReplicationModule`, deferred grants are never claimed, and nothing says so.*

```mermaid
graph TD
    subgraph PACK["EntityCreationPack — EVERY ECS host builds this"]
        CERS["CreateEntityRequestSystem"]
        NSS["NetworkSpawningSystem"]
        GPS["GhostPromotionSystem"]
        ERFS["EntityRequestFinalizationSystem"]
    end

    subgraph NED["NedReplicationModule — NED hosts only"]
        GCS["GhostCreationSystem<br/>Execute is a NO-OP"]
        OIS["OwnershipIngressSystem"]
        GRP["NetworkLifecycleSystemGroup<br/>PRIVATE — one caller"]
        DTS["DeferredTakeoverSystem"]
    end

    subgraph ING["ingress translators"]
        DTOI["DeferredTakeOwnershipIngressTranslator"]
        EMI["EntityMaster / WorldPos / EntityInfo ingress"]
    end

    SCHED["SystemScheduler — BeforeSync phase"]
    TICK["NedReplicationModule.Tick"]

    SCHED --> CERS
    SCHED --> NSS
    SCHED --> GPS
    SCHED --> ERFS
    SCHED --> OIS
    SCHED --> GCS
    GRP --> DTS
    TICK -.->|"THE ONLY CALLER"| GRP

    DTOI -->|"calls directly, not scheduled"| GCS
    EMI -->|"calls directly, not scheduled"| GCS

    GPS -.->|"UpdateAfter"| DTS

    classDef dead fill:#fde,stroke:#c33,stroke-width:2px
    class GRP,DTS dead
    classDef noop fill:#ffd,stroke:#cc3
    class GCS noop
```

> 🔴 **Red = reachable ONLY through `NedReplicationModule.Tick`.** ⭐ Yellow = registered as a system but
> its `Execute` does nothing; the real entry point is a direct method call from a translator.

### 1.4 The participants

*What the picture shows that the sequence cannot: **which of these are per-node state and which are
shared records** — the distinction that decides where any derived answer may be cached.*

```mermaid
classDiagram
    class TkbTemplate {
        <<SHARED — one object serves every node>>
        +long TkbType
        +BirthCriticalComponents : derived, host-independent
        +MandatoryComponents : explicit escape hatch only
    }
    class IRoleAffinityPolicy {
        <<per NODE — built from its roles>>
        +OwnableMask(template, isCreator, shardKey)
    }
    class MandatoryComponentResolver {
        <<per NODE — 3 of its 4 inputs are host-local>>
        +Resolve(template, repo, translators, ingressMap)
    }
    class NetworkSpawningSystem {
        <<CREATE leg>>
    }
    class GhostPromotionSystem {
        <<PROMOTE leg>>
    }
    class DeferredTakeoverSystem {
        <<EXPLICIT grants — overrides role default>>
    }
    class EntityLifecycleModule {
        <<owns Constructing to Active, both legs>>
    }

    NetworkSpawningSystem ..> IRoleAffinityPolicy : isCreator TRUE
    GhostPromotionSystem ..> IRoleAffinityPolicy : isCreator FALSE
    GhostPromotionSystem ..> MandatoryComponentResolver : the gate
    NetworkSpawningSystem ..> TkbTemplate
    GhostPromotionSystem ..> TkbTemplate
    DeferredTakeoverSystem ..> GhostPromotionSystem : UpdateAfter
    NetworkSpawningSystem --> EntityLifecycleModule : BeginConstruction
    GhostPromotionSystem --> EntityLifecycleModule : BeginConstruction
```

---

## 2. ⭐⭐ THE STAGE TABLE — **a routing table, not an explanation**

| # | stage | the code | ⭐ the OWNING design — go here to change it |
|---|---|---|---|
| ① | request → validate → allocate id | `CreateEntityRequestSystem.cs` | [`DESIGN_Entity_Creation_Unification.md`](DESIGN_Entity_Creation_Unification.md) · [`DESIGN_Entity_Authoring_Surface.md`](DESIGN_Entity_Authoring_Surface.md) |
| ② | spawn: entity + TKB projection | `NetworkSpawningSystem.cs:158-249` | [`designs/tkb-1/DESIGN.md`](designs/tkb-1/DESIGN.md) §6 *(translators)* |
| ③ | **the creator's authority** | `NetworkSpawningSystem.cs:229-238` — `AuthorityMask &= OwnableMask(isCreator: true)` | [`DESIGN_Role_Affinity_Ownership.md`](DESIGN_Role_Affinity_Ownership.md) §3.1, §3.9c |
| ④ | grant routing *(explicit, optional)* | `DeferredTakeOwnershipIngressTranslator.cs` → `PendingAuthorityGrants` | [`projects/Hrot/Network/Hrot.Network.NED.md`](projects/Hrot/Network/Hrot.Network.NED.md) "Diagram 3" *(⚠ see §5)* |
| ⑤ | ghost creation | `GhostCreationSystem.CreateGhost` — **called by translators, never by the scheduler** | [`projects/Hrot/Network/Hrot.Network.NED.md`](projects/Hrot/Network/Hrot.Network.NED.md) |
| ⑥ | **the promotion gate** | `GhostPromotionSystem.cs` + `MandatoryComponentResolver` | [`designs/tkb-1/DESIGN.md`](designs/tkb-1/DESIGN.md) **§6.6a / §6.6b** |
| ⑦ | **the promoter's authority** | `GhostPromotionSystem` — `OwnableMask(isCreator: false)` | [`DESIGN_Role_Affinity_Ownership.md`](DESIGN_Role_Affinity_Ownership.md) §3.2 |
| ⑧ | explicit takeover | `DeferredTakeoverSystem.cs` — queries `Constructing`, `[UpdateAfter(GhostPromotionSystem)]` | [`DESIGN_Role_Affinity_Ownership.md`](DESIGN_Role_Affinity_Ownership.md) §3.4 *(explicit grants still win)* |
| ⑨ | `Constructing` → `Active` | `EntityLifecycleModule.ProcessConstructionAck` | [`designs/two-ack/TwoAck-DESIGN.md`](designs/two-ack/TwoAck-DESIGN.md) · `FDP/Docs/projects/toolkits/FDP.Toolkit.Lifecycle.md` |
| ⑩ | ACK back to the requester | `EntityRequestFinalizationSystem.cs` | [`DESIGN_Entity_Creation_Unification.md`](DESIGN_Entity_Creation_Unification.md) |

---

## 3. 🔒 THE ONE QUESTION THIS FILE ANSWERS THAT NO OWNER DOES

> **"There are three things that decide who owns a component. Which one wins?"**

⭐ They do not compete — **they apply at different moments, and each is narrower than the last:**

| | when | what it decides |
|---|---|---|
| **role affinity, create leg** ③ | the instant the entity is born, on the creating node | the default: *everything this node's roles may own*, **plus the creator's birthright** |
| **role affinity, promote leg** ⑦ | when a ghost promotes, on the receiving node | the complement: *everything this node's roles may own*, ⛔ **and no birthright** |
| **explicit grant** ⑧ | after promotion, only where a grant was addressed | **overrides both** — it is additive and deliberate |

⛔ **The two role legs must stay complementary or two nodes own one component and their egress fights.**
That property is not maintained by agreement — **both nodes evaluate the same function over the same
entity**, which is why the tables live in one place and take a role and nothing else.
📄 The rule and its rails: [`DESIGN_Role_Affinity_Ownership.md`](DESIGN_Role_Affinity_Ownership.md) §3.9c.

---

## 4. ⛔ SCOPE — **what this file deliberately does NOT draw**

| out of scope | why, and where it lives |
|---|---|
| **destruction / teardown** | a separate two-ack path with its own hazards — `FDP.Toolkit.Lifecycle.md`, [`designs/two-ack/TwoAck-DESIGN.md`](designs/two-ack/TwoAck-DESIGN.md) |
| **replay** | genesis is *suppressed* during playback rather than performed — [`designs/mgmt-1/DESIGN.md`](designs/mgmt-1/DESIGN.md) §8.10 |
| **which components are legitimate state** | [`DESIGN_Entity_State_Sourcing.md`](DESIGN_Entity_State_Sourcing.md) |
| **the DDS topics, QoS and translator inventory** | [`projects/Hrot/Network/Hrot.Network.NED.md`](projects/Hrot/Network/Hrot.Network.NED.md) |
| **child entities / sub-parts** | `TkbTemplate.ChildBlueprints`, `PartMetadata`, `SubEntityCleanupSystem` — no owning design found; ⚠ searched `docs/` and `.dev/` by topic |

---

## 5. ⚠ ROT FOUND IN OTHER DOCUMENTS — *(measured `2026-09-13`; each is fixed or flagged at source)*

| document | the rot |
|---|---|
| 🔴 [`projects/relationships/Hrot-Simulation-Pipeline.md`](projects/relationships/Hrot-Simulation-Pipeline.md) §4.3 | the only other end-to-end spawn narrative, and it contains **ZERO** occurrences of *"promote"*. It shows CGF as `isDefaultProcessor`, no `EntityCreationPack`, no role affinity and no promotion gate — the pre-P1/P2/P3 world. ⭐ A STATUS block with `known-rot` was added there rather than rewriting §4.3, which this file now supersedes |
| ⚠ [`projects/Hrot/Network/Hrot.Network.NED.md`](projects/Hrot/Network/Hrot.Network.NED.md) "Diagram 3" | the **protocol order is correct and it is the best picture of the handshake anywhere**. ⛔ But it predates `P2`: `GhostPromotionSystem` no longer belongs to the NED module — `EntityCreationPack` registers it on every ECS host. A correction note was added beside the diagram |

📌 **Both were found by asking *"is the genesis path described anywhere?"*** — and the answer being *"in
eleven places, none of them joined up"* is what produced this file.
