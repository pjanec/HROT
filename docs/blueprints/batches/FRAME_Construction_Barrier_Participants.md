<!--STATUS
state: LIVE
build-state: FRAME — DRAFT, NOT DISPATCHED. UI/CGF lane (`CE-`). Awaiting the user's go + the one
  open decision D2 (§4) resolved before the session writes the design and builds.
updated: 2026-09-15
current-answer: this is the coordinator FRAME. The session authors the owning design (inventory + UML)
  in DESIGN-NetworkSpawning.md (or a new DESIGN_Construction_Barrier.md) before any code.
related-designs:
  - ../../DESIGN_Entity_Genesis_End_To_End.md — ⭐⭐⭐ THE LANDING PAGE: the built genesis pipeline (§1.2 sequence, §1.3 who-ticks-what, §1.4 participants). READ FIRST — the peer/ownership flow here is the LIVE mechanism this work must reconcile with, not the dormant gateway path.
  - ../../DESIGN_Role_Affinity_Ownership.md — §3 the ownership legs (create/promote/explicit-grant) + §3.9c the complement rule.
  - ../../designs/tkb-1/DESIGN.md — §6.6a/§6.6b the GhostPromotion mandatory-components gate (the receiver-side readiness gate that ALREADY exists).
  - ../../designs/others/DESIGN-NetworkSpawning.md — owns ReliableInitType / PendingNetworkAck / the reliable-init handshake (the DORMANT path).
  - Architect_Question_69_Construction_Barrier_And_Rewind_Contract.md — asks B/D (peer barrier + ReliableInit source); §7 the reconciliation this frame builds on.
  - ../../DESIGN_Role_Affinity_Ownership.md — the Brain/Muscle node split + BrainMuscleOwnershipStrategy (the source for GetExpectedPeers).
  - ../../designs/replay-and-modules/DESIGN.md — §2.1i/§2.1j the ELM rewind dependency (HN-018 / CE-259au).
  - FDP/Engine/Fdp.ModuleHost/docs/ModuleHost-network-ELM-design-talk.md — §1/§2/Part 1 the origin of the barrier.
-->
# FRAME — real ELM construction-barrier participants *(local module readiness + peer barrier)*

> ⭐⭐⭐ **FRAME, not a design.** Per WHO-DESIGNS: the SESSION authors the inventory + class/sequence/module
> UML in the owning DESIGN, then builds against it. The coordinator verifies design+UML on return.
> ⛔ **NOT DISPATCHED** — the user is still resolving §4. Stamp `Dispatched at <sha>` when it goes.

## 1. 🎯 GOAL — user-approved direction (`2026-09-15`)
Make the entity **construction barrier real**: systems on a node register as ELM participants and hold an
entity in `Constructing` **only for entities that require them**, until their node-local data is ready —
and chain that across nodes with the **peer barrier**, decentralised, **no master controller**.

- ✅ **Fast mode stays the default** until someone requests reliable init *(user)*.
- ✅ **`GetExpectedPeers` derives from the role/ownership model** *(user)* — not "all known nodes".
- ✅ **Decentralised, non-master** is the target shape *(user)*.

## 2. 📐 THE MECHANISM ALREADY EXISTS — measured `2026-09-15`, do not re-derive
The ELM already carries the two-knob model this needs; `RegisterRequirement` has **zero callers** today.

| knob | how | file |
|---|---|---|
| **static / type-level** — is a module in the wait-set *for this type*? | `RegisterRequirement(tkbType, moduleId)` → `_blueprintRequirements`, unioned per-type at `BeginConstruction` | `EntityLifecycleModule.cs:158` / `:190-191` |
| **global** — block for *every* entity | `RegisterModule(moduleId)` → `_globalParticipants` | `EntityLifecycleModule.cs:148` |
| **dynamic / instance-level** — ack now or defer? | the module decides in its own `Execute`; ack when ready; timeout guard | pattern: `NetworkGatewaySystem.cs` (`_pendingPeerAcks`, `ReceiveLifecycleStatus`, timeout) |

`BeginConstruction`: `RemainingAcks = _globalParticipants ∪ _blueprintRequirements[tkbType]`.

**Composition (the decentralised, non-master shape):**
- **Local module barrier (per node):** each node's local ELM waits for *its own* registered participants.
  Navmesh/altitude = a **muscle-node** local barrier; model-load = an **IG-node** local barrier.
- **Peer barrier (cross-node):** `NetworkGatewaySystem` is the one participant that chains nodes — the
  authority node holds until peers report `Active`, and a peer reports `Active` only after *its own* local
  barrier cleared. Local barriers + peer barrier = cross-node readiness with no master.

## 2b. ⛔⛔ RECONCILE WITH THE LIVE GENESIS PIPELINE FIRST — measured `2026-09-15` (`DESIGN_Entity_Genesis_End_To_End.md`)
🔴 **The reliable-init/peer path I first scoped (`NetworkGatewaySystem` + `PendingNetworkAck` +
`INetworkTopology.GetExpectedPeers`) is NOT what the built genesis pipeline uses.** The live flow (§1.2)
already threads ownership across nodes with a **richer, different** mechanism — and the seam law says check
it before building a parallel one.

**The built cross-node flow (§1.2), all wired:**
1. Creator: `NetworkSpawningSystem` → `ELM BeginConstruction`; broadcasts **`DeferredTakeOwnership`** (grants → node ids).
2. Receiver: `DeferredTakeOwnershipIngressTranslator` → `CreateGhost` + `PendingAuthorityGrants` *(grant arrives before the entity)*.
3. Creator broadcasts `EntityMaster` + the `WorldPos`/`EntityInfo` baseline.
4. Receiver: **`GhostPromotionSystem` — the mandatory-components gate** *(HARD, no timeout; names the missing component after 600 frames)* → Ghost → `Constructing`.
5. Receiver: `DeferredTakeoverSystem` claims the grants → broadcasts **`OwnershipUpdate`**.
6. Creator: `OwnershipIngressSystem` drops the matching authority bits; receiver's local ELM acks → `Active`.

⇒ **Two things this changes:**
- ⭐⭐ **The receiver-side "data ready" gate ALREADY EXISTS** — `GhostPromotionSystem`'s mandatory-components
  gate, plus the receiver's local ELM *(§1.4: the ELM owns `Constructing→Active` on BOTH legs)*. So the
  navmesh/IG-model participants *(piece C)* extend **that** existing receiver gate — they are NOT a new path.
- ⛔⛔ **SEAM-LAW CHECK RESOLVED BY MEASUREMENT `2026-09-15` — `OwnershipUpdate` does NOT subsume the peer
  barrier; it is genuine new work.** Three measured reasons: ① **wrong moment** — `DeferredTakeoverSystem`
  emits `OwnershipUpdate` at `WithLifecycle(Constructing)` *(`DeferredTakeoverSystem.cs:154/125`)*, i.e. at
  *claim*, before the receiver is `Active`/ready; ② **wrong coverage** — it is emitted only by grant
  recipients *(`:106` `if (ownerNodeId != _localNodeId) continue`)*, so **display-only replicas that own
  nothing — exactly the IG-model case — emit none**; ③ **no peer-Active ack exists** — `OwnershipIngressSystem`
  only sets authority bits + `PrimaryOwnerId` *(no lifecycle gate)*, and `EntityLifecycleStatusDescriptor` is
  an orphan DTO with no producer. ⇒ **reliable-init needs its own peer-Active ack** *(give
  `EntityLifecycleStatusDescriptor` a producer-on-`Active` + a consumer, ≈ the dormant `EntityAcknowledge`
  concept; `PendingNetworkAck` is already stamped by `NetworkSpawningSystem:175`)*. ⭐ What IS reuse: the
  RECEIVER-side gate above; ⛔ the CROSS-node creator-wait is real.

⚠ **HOST-REACHABILITY HAZARD (§1.3):** `DeferredTakeoverSystem` and `NetworkLifecycleSystemGroup` are
**dead on non-NED hosts** *(private group, one caller = `NedReplicationModule.Tick`; unreachable on the
editor's `NullReplicationModule` and on BDC)*, and `GhostCreationSystem.Execute` is a **no-op** *(entry is a
direct translator call)*. Any participant wiring must draw the module diagram *(obligation ①a)* and show it
ticks on the hosts that need it.

## 3. 🔧 WHAT TO BUILD *(the session designs the detail + UML per piece)*
| # | piece | note |
|---|---|---|
| **A** | **Build the PEER-Active ack (MEASURED necessary — §2b, seam-law check done)** | `OwnershipUpdate` does NOT subsume it *(wrong moment: claim-not-ready; wrong coverage: grant-recipients only, no display replicas)*. Give `EntityLifecycleStatusDescriptor` a **producer that fires when a peer's copy reaches `Active`** + a **consumer on the creator** that clears the pending set; `PendingNetworkAck` is already stamped at `NetworkSpawningSystem:175`. Construct the creator-side waiter *(the gateway, or an equivalent `DeferredConstructionParticipant`)*. This IS genuine new work. ⚠ Depends on **§4a P1–P3** — the orchestrator must carry the node role mask before membership can be resolved |
| **B** | **Extract a reusable `DeferredConstructionParticipant` base** | the gateway hand-rolls pending-set / defer-ack / timeout / destruction-cleanup; navmesh + IG-model must not copy it *(ruling 9 / seam law)*. Hook: `TryComplete(entity) → bool`. If piece A keeps a gateway, it becomes a thin subclass |
| **C** | **Wire the two receiver-side participants — extending the EXISTING gate, not a new path** | navmesh/altitude on the muscle node, model-load on IG, each blocking only for entities requiring it *(`RegisterRequirement` per-type; "which types" from the TKB template — `SimTransform`/a component attribute, `[BirthCritical]` shape, CE-266)*. ⭐ These extend the receiver readiness gate that already exists *(GhostPromotion mandatory-components gate + the receiver's local ELM, §2b)* |
| **D** | **Integration proof** | on `--mode all`: spawn a spatial type in an *uncached* area → stays `Constructing` → data arrives → `Active`; plus timeout path + IG model-load. T-1: extend `EntityLifecycleModuleTests` + `NetworkGatewaySystemTests`, add an integration rail |

## 4. ✅ D2 RESOLVED — `GetExpectedPeers` source, MEASURED `2026-09-15` (not assumed)
🔒 **The expected-peer set = the distinct owner `NodeId`s in `DeferredTakeOwnershipCommand.Grants` for the
entity, minus the local node** — the grant recipients *(the nodes that will own a delegated part and must
initialize it)*. Traced end-to-end:
| fact | site |
|---|---|
| creator computes owner nodes per entity via the strategy | `CreateEntityRequestSystem.cs:361-372` → `BuildOwnershipGrants` → `_ownershipStrategy.GetInitialGrants(disType, assignedOwner)` (`:510`) |
| load-balanced **per-instance** ⇒ a type lookup CANNOT match | `BrainMuscleOwnershipStrategy.cs:43` `GetLeastLoadedNode(MuscleGround)` |
| carried in ONE uniform per-entity command; BOTH producers converge on it | `DeferredTakeOwnershipCommand { NetworkId, Grants[] }` — `CreateEntityRequestSystem:363` **and** `CgfSubsystem.cs:544-546` |
| broadcast to peers before `EntityMaster` | `DeferredTakeOwnershipEgressTranslator.cs:55`; `NedReplicationModule.cs:638` |
| `[DataPolicy(NoReplay)]` — consistent with the barrier not running under replay | `DeferredTakeOwnershipCommand.cs:38` |

⛔ **NOT `INetworkTopology.GetExpectedPeers(long tkbType)`** — it has **no production impl** and is type-only,
so it cannot match a per-instance load-balanced grant. Retire it as the peer source; keep `INetworkTopology`
for `LocalNodeId`/`GetAllNodes` only. ⛔ **NOT `BrainMuscleOwnershipStrategy` directly** *(role-pair-specific,
NED layer)* — tap the **command it feeds**, which both producers share.

### 4a. ✅ PEER MEMBERSHIP RESOLVED — user ruling + measurement `2026-09-15`
🔒 **User:** *"cluster orchestrator should know what nodes are present and what role mask they have. display
replica does not need to own anything to block entity creation until all display-replica nodes registered as
ELM participant are ready."*
⇒ **Membership = the present nodes (from the orchestrator roster) whose role mask marks them a participant
for this entity; the creator blocks until each such node publishes its peer-`Active` ack.** The grant node-set
is then just the *owner* subset; display replicas *(IG/Map2D)* are included by their role mask, not by any grant.

📐 **Measured — `NodeRole` is a `[Flags]` mask** *(`Brain=1<<0, MuscleGround=1<<1, Map2D=1<<2, Perception=1<<3,
NavigationSolver=1<<4`; used via `HasFlag`, combined as `MuscleGround|Perception`)*, so "role mask" is real.
⛔⛔ **BUT the orchestrator does NOT hold it today** — three grounded PREREQUISITES:
| # | gap (measured) | fix |
|---|---|---|
| **P1** | `NodeHeartbeatEvent` carries `{ NodeId, LocalStateId, WallTicksUtc, SubsystemName }` — **no role mask**; role is re-derived downstream by a lossy hardcoded switch `NedNetworkFactory.MapSubsystemNameToRole` *(3 names → a SINGLE role, else `None`)* | **carry the node's `NodeRole` mask on the heartbeat** — the node knows its own mask at boot *(consistent with `CE-259bb`: `--mode` fixes the role)*. ⭐ Retires `MapSubsystemNameToRole` — the seam law: the source already has the mask, stop reconstructing it from a display string |
| **P2** | the orchestrator roster `NodeHealthProfile` stores `SubsystemName` only, **no role**; `SimpleClusterStateCache.NodeCapability.Role` exists but is fed by the lossy switch | **store the role mask** in the roster (and/or the cache) so the authority the user names actually holds it |
| **P3** | neither registry answers *"present nodes whose mask intersects role R"* — `SimpleClusterStateCache` exposes only `GetLeastLoadedNode`; `NodeRoster` exposes `ActiveNodes` (no role) | **add the membership query** — a small accessor over the existing dictionaries |

⇒ with P1–P3, the creator resolves expected-peers = `roster nodes where mask ∩ {roles that register a
participant for this entity type} ≠ ∅`, minus local. The role→"registers a participant" mapping is
application config *(same shape as the role→components table)*, never engine knowledge.

⭐ **The one remaining HOW (session design detail, grounded):** the grant is computed in
`CreateEntityRequestSystem` but `PendingNetworkAck` is stamped later in `NetworkSpawningSystem:159`. Carry
the expected-peer node set on `SpawnEntityCommand` *(which the creator already publishes and which already
carries `InitType`)* so `NetworkSpawningSystem` stamps `PendingNetworkAck` *(widened from `{ ExpectedType }`,
or a sibling `ExpectedAckPeers` component)* with the actual set. The gateway then reads a per-entity set and
needs no `GetExpectedPeers` re-derivation.

⭐ **NOT a decision — corrected `2026-09-15` (user).** Being an ELM participant *(does this system ACK
construction, gating entry to `Active`)* and `WithOwned<T>` *(which entities a system's steady-state query
returns each tick)* are **orthogonal**. A system can be both — it acks once when its data is ready, and
independently ticks its owned entities thereafter. There is no "source of truth" conflict; the earlier
**D1 was a false dichotomy** and is withdrawn.

## 5. ✅ REWIND-SAFETY IS ALREADY BUILT — new participants inherit it *(corrected `2026-09-15`)*
⛔ **An earlier version of this section claimed rewind-safety "goes live once the queues are non-empty" and
told the session to budget it. That was WRONG**, and the user corrected the premise: **replay restores the
full ECS snapshot in sync on all nodes and ELM systems do not run during replay** *(`mgmt-1/DESIGN.md`
§8.10 — entities materialise `Active` from the recorded chunk; `NetworkLifecycleSystemGroup.Enabled=false`;
`GhostCreationSystem.BypassLifecycle=true`)*. **Preview is live mode where the ELM blocks normally**; only
the discard-rewind clears.
- ⭐ The ELM's transient queues are **cleared + re-derived at every world-replacement boundary** —
  `OnWorldReplaced` + `ResumeFromRestoredWorld`, wired at `CgfSubsystem`, SimHost `NodeBootstrapper`,
  `EditorSubsystem`; preview via `PreviewParticipants.LifecycleModule` on the same three hosts *(`HN-018`
  CLOSED `2026-09-12`; `CE-259ap`/`CE-259ar` replay-boundary clears landed in step 3;
  `replay-and-modules/DESIGN.md` §2.1m BUILT)*. It **re-derives** from recorded `LifecycleState` +
  `TkbIdentity`, so no `Entity` handle crosses the boundary.
- ⭐⭐ **A new deferring participant (navmesh / IG-model) inherits this for free:** on resume-to-live
  `ResumeFromRestoredWorld` re-publishes `ConstructionOrder` for still-`Constructing` entities and the
  participant simply re-blocks and re-acks when its data is ready. **Nothing to budget here.**

## 6. ⭐ ACCEPTANCE + PROCESS
- Owning design (extend `DESIGN-NetworkSpawning.md` or new `DESIGN_Construction_Barrier.md`) carries the
  **class + sequence + module-relationship UML** before code; the module diagram must show **who ticks
  each participant on which node** *(the dead-edge rule — a participant nobody registers is the failure
  this codebase produces most)*.
- Fast path unchanged (default); reliable path opt-in per entity via `ReliableInitType`.
- The **D proof** is the deliverable that shows it works — not a green unit gate.
- Fold the as-built back into the owning design *(obligation ⑤)*; close/refile `CE-259au` accordingly.
- ⭐ Lane: UI/CGF. IDs: the session allocates the next free plain `CE-` numbers *(rule 3 / 3a-id)*.
