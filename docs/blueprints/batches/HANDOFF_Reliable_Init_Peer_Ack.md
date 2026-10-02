<!--STATUS
state: LIVE
build-state: HANDOFF — dispatched to the UI/CGF lane (H - ui, `claude/reset-working-branch-qd1qpv`).
  Slice A of the cross-node construction barrier: the reliable-init peer-Active ack wire + creator waiter.
  ⛔ STEP 0 is a DESIGN-VERIFICATION gate (user ask, 2026-09-15) — verify before building.
updated: 2026-09-15
current-answer: this is the dispatch. The owning DESIGN is DESIGN_Cross_Node_Construction_Barrier.md —
  §1 diagrams · §2/§2a/§2b/§2c the WHY + the three 2026-09-15 hard-requirements · §3 NEW/EXISTS/CHANGE ·
  §4 receiver gate · §6 acceptance. Build against it; do not redraw its diagrams here.
related-designs:
  - ../../DESIGN_Cross_Node_Construction_Barrier.md — ⭐ THE OWNING DESIGN. §2a durability · §2b node-id · §2c generic protocol.
  - ../../DESIGN_Entity_Genesis_End_To_End.md — the LIVE genesis pipeline this reconciles with (read first).
  - ../../designs/others/DESIGN-NetworkSpawning.md — ReliableInitType / PendingNetworkAck (the dormant path revived here).
  - ../../reference/BDC_NED_SST_Descriptor_Rules.md — the wire contract; §2c folds the durable descriptor + Flags bit in as OPTIONAL generic descriptors.
  - FRAME_Construction_Barrier_Participants.md — the frame; this handoff is its piece A (+ piece B folded in).
-->
# HANDOFF — reliable-init peer-Active ack + creator waiter *(barrier piece A, +B)*

> 📌 **Scope is FROZEN at the coordinator HEAD named in the dispatch message.** Documents that change after
> it are FYI only. ⭐ Rule 7: re-sync from `claude/blueprint-authoring-status-6sr5ld` FIRST. ⭐ Rule 1b: push
> an empty `chore: started slice A at <sha>` marker before writing code. ⛔ No PR. ⛔ Continue `CE-` ids
> (plain increments, rule 3a-id) — you allocate them. ⛔ If a premise here is false, STOP that item and
> report — do not adapt silently.

## 0. ⛔⛔⛔ STEP 0 — VERIFY THE DESIGN FIRST *(user ask, `2026-09-15`)*
> 🔒 **User:** *"do the handoff so it gets implemented, ask the ui session to verify the design first."*

⭐⭐⭐ **Before writing ANY code, re-measure the load-bearing claims the design rests on against the current
HEAD, and REPORT the verification to H-coord as your first message.** The design was authored `2026-09-15`;
this gate confirms nothing rotted and that the seams are where the design says. ⛔ **If any row is FALSE, STOP
and report it — the design is wrong and must be corrected before build** *(that is a finding, not a blocker to
route around)*. ✅ If all hold, proceed to Step 1 in the same run — no round-trip needed.

| # | claim to verify (design §) | expected (measured `2026-09-15`) | how |
|---|---|---|---|
| V1 | `EntityLifecycleStatusDescriptor` is an **untyped orphan** (§2a/§3) | the type has **NO `[DdsStruct]`, NO `[DdsKey]`, NO `[DdsQos]`**, and **zero producer/consumer** | open `FDP/Network/Fdp.Network.Cyclone/Topics/EntityLifecycleStatusDescriptor.cs`; grep the type name for refs |
| V2 | the reliable bit does NOT travel today (§2) | `EntityMasterEgressTranslator.cs:103` writes **`Flags = 0`**; `EntityMaster.Flags` is `ulong`; nobody reads it | open the egress line; grep `.Flags` on `EntityMaster` |
| V3 | activation is silent (§2) | `ProcessConstructionAck` sets `Active` (~`:305`) and **publishes no event** | `get_code_snippet` on `EntityLifecycleModule.ProcessConstructionAck` |
| V4 | the creator waiter is dormant (§0) | `NetworkGatewaySystem` is **never constructed in production** (tests only) | Roslyn/grep for `new NetworkGatewaySystem` |
| V5 | the reliable stamp is unread (§0) | `NetworkSpawningSystem:175` stamps `PendingNetworkAck` if `InitType != None`; no consumer | open the site; grep `PendingNetworkAck` |
| V6 | `RegisterRequirement` has **zero prod callers** (§2/frame §2) | only tests call it | `trace_path`/grep `RegisterRequirement` |
| V7 | ⭐⭐ **node-id space (§2b)** — the ownership node-id in each layer | NED wire `OwnershipUpdate { NodeId NewOwner }`; Cyclone `int NewOwner`; ECS `int NewOwnerNodeId`; `OwnershipIngressSystem.cs:67` compares `== localNodeId` | open the three `OwnershipUpdate.cs`/`GenericMessages.cs`/`OwnershipMessages.cs` + the ingress line |
| V8 | membership source exists (P3 BUILT) | `NodeRoster.NodesWithRole(NodeRole)` yields active ids where `mask & role != 0` | open `NodeRoster` |
| V9 | the lifecycle enum is generic (§2c) | `Fdp.Core` `EntityLifecycle` = `Constructing/Active/TearDown/Ghost`, no Hrot names | open `Fdp.Core/EntityLifecycleState.cs` |

⭐ **Report V1–V9 as a table: hold / DIFFERS (with the real value).** That IS the design-verification the user
asked for.

## 1. ⭐ STEP 1 — DESIGN DETAIL (frame-delegation)
Fold your buildable sub-design into `DESIGN_Cross_Node_Construction_Barrier.md` **before code**:
- The exact new types/fields for the durable descriptor (§2a), the `EntityMaster.Flags` reliable bit (§2c),
  the `ExpectedAckPeers` carrier (§0/§1.2), the egress producer, the creator-side consumer + waiter.
- ⭐ **If the WIRE SHAPE changes** *(a new descriptor field, a changed key set)*, update §1.2 and mark the
  prior state superseded *(obligation ⑤)*. A field-only add on an existing box needs no new diagram.
- Draw/confirm the **module-relationship** view *(obligation ①a)*: which host ticks the egress producer, which
  ticks the creator waiter — the design's §1.3 already marks the NED-only dead group; show your producer/consumer
  land on hosts that actually tick *(the §2b HOST-REACHABILITY hazard in the frame is real — the editor's
  `NullReplicationModule` and BDC do not tick `NetworkLifecycleSystemGroup`)*.

## 2. 🔧 STEP 2 — BUILD SLICE A (+B)
| # | build | design basis |
|---|---|---|
| **A1** | Make `EntityLifecycleStatusDescriptor` a **first-class DURABLE descriptor**: `[DdsStruct]`, `[DdsKey]` on `(EntityId, NodeId)`, `[DdsQos(Reliable, TransientLocal, KeepLast 1)]` | §2a / §3 |
| **A2** | Carry the **reliable bit on `EntityMaster.Flags`** (a reserved `WaitForAcks` bit); egress writes it (stop writing `0`); peer reads it at ghost creation and tags the ghost *report-on-`Active`* | §2 / §2c / §3 |
| **A3** | ⭐ **Node-id consistency (§2b):** the producer stamps `EntityLifecycleStatusDescriptor.NodeId` with the **same node-id it uses for `OwnershipUpdate.NewOwner`** (the `NodeId` struct at the NED wire, projected to the `int` the ownership ingress compares to `localNodeId`). ⛔ NOT the roster id, NOT the subsystem name | §2b |
| **A4** | `PeerLifecycleStatusEgressSystem` (NEW) — observe `Constructing→Active` for tagged/reliable entities, publish the ack. ⚠ only for reliable, never every activation | §2 / §3 |
| **A5** | Carry `ExpectedAckPeers` (the roster membership set from `NodeRoster.NodesWithRole`, minus local) onto `SpawnEntityCommand` → stamped into `PendingNetworkAck` (widen or a sibling component) | §0 / §1.2 / frame §4a |
| **A6** | Construct `NetworkGatewaySystem` (creator waiter) in PRODUCTION: consume the ack topic, drop each peer from `ExpectedAckPeers`, `AcknowledgeConstruction` when the set empties or on timeout | §1.1 / §3 |
| **B** | Extract `DeferredConstructionParticipant` base (pending-set + defer-ack + timeout + destruction-cleanup); the gateway becomes a thin subclass *(ruling 9 — navmesh/IG in piece C must not copy it)* | §1.2 / §3 |
| **A7** | ⭐⭐ **Late-joiner read path (§2a):** on ghost creation, if `EntityMaster.Flags(WaitForAcks)` is set, do NOT treat the entity as usable until a `State=Active` status instance for the **OWNER** node has been read from the durable topic (correlate the descriptor `NodeId` with the owner from `EntityMaster`/`OwnershipUpdate`) | §2a / §2b |

⛔ **OUT OF SCOPE for this slice** — the real navmesh/altitude + IG model-load participants (frame piece C):
they need the TKB "which types require me" work *(CE-266 shape)*. Prove A with a **synthetic test participant**
on the peer. Piece C is a follow-up handoff once the protocol exists.

## 3. ✅ ACCEPTANCE
- **Live barrier:** `--mode all`, a reliable entity with a synthetic peer participant → the CREATOR stays
  `Constructing` → the peer acks → publishes `Active` status → the creator goes `Active`. Timeout path
  force-activates. **Fast mode unchanged** (`ReliableInitType.None` skips the gateway) — a regression rail.
- **Late-joiner (§2a):** a node subscribing AFTER creation receives the RETAINED `EntityMaster.Flags` +
  `EntityLifecycleStatusDescriptor` samples with no live transition, and holds the entity until the owner's
  instance reads `Active`.
- **Node-id (§2b):** a rail asserts a status instance's `NodeId` equals the `NewOwner` the same node stamps
  on `OwnershipUpdate`.
- T-1: **find + run** the feature's own suites first *(`EntityLifecycleModuleTests`, `NetworkGatewaySystemTests`)*;
  add rails INTO them, red-proofed; add an integration rail. Build affected projects only, `--no-build` after
  the first; E2E in the BACKGROUND.

## 4. ⭐⭐ REPORT BACK TO **H - coord** (the gate contract — substitutes for my re-run)
Two messages:
1. ⭐ **After Step 0:** the V1–V9 verification table (hold / DIFFERS). ⛔ If anything DIFFERS materially, stop
   there and wait.
2. **When done or blocked:** the `CE-` ids allocated · the design edits folded *(§ + prior state superseded)* ·
   one row per gate *(verbatim command · pass/fail/skip · `--no-build` · delta vs base)* · every RED confirmed
   pre-existing against the base sha · working tree clean · the diff shape *(files, additive vs destructive)* ·
   `rulings-check.py` + `design-digest.py --check` + `mermaid-check.mjs` verdicts.
