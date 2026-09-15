<!--STATUS
state: LIVE
build-state: FRAME — DRAFT, NOT DISPATCHED. UI/CGF lane (`CE-`). Awaiting the user's go + the one
  open decision D2 (§4) resolved before the session writes the design and builds.
updated: 2026-09-15
current-answer: this is the coordinator FRAME. The session authors the owning design (inventory + UML)
  in DESIGN-NetworkSpawning.md (or a new DESIGN_Construction_Barrier.md) before any code.
related-designs:
  - ../../designs/others/DESIGN-NetworkSpawning.md — owns ReliableInitType / PendingNetworkAck / the reliable-init handshake.
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

## 3. 🔧 WHAT TO BUILD *(the session designs the detail + UML per piece)*
| # | piece | note |
|---|---|---|
| **A** | **Construct `NetworkGatewaySystem` in production** (it exists, never instantiated) | needs an `INetworkTopology.GetExpectedPeers` that derives the owning nodes from `BrainMuscleOwnershipStrategy`/the role model |
| **B** | **Extract a reusable `DeferredConstructionParticipant` base** | the gateway hand-rolls pending-set / defer-ack / timeout / destruction-cleanup; navmesh + IG-model must not copy it *(ruling 9 / seam law)*. Hook: `TryComplete(entity) → bool`. Gateway becomes a thin subclass |
| **C** | **Wire the two real participants via `RegisterRequirement`** | navmesh/altitude on SimHost (muscle) for spatial types; model-load on IG for types with a model. "Which types require it" derived from the TKB template *(SimTransform presence / a component attribute — same shape as `[BirthCritical]`, CE-266)* |
| **D** | **Integration proof** | on `--mode all`: spawn a spatial type in an *uncached* area → stays `Constructing` → data arrives → `Active`; plus timeout path + IG model-load. T-1: extend `EntityLifecycleModuleTests` + `NetworkGatewaySystemTests`, add an integration rail |

## 4. ⛔ ONE DECISION TO RESOLVE BEFORE BUILD *(architect/user)*
| # | decision | coordinator lean |
|---|---|---|
| **D2** | **`GetExpectedPeers` membership** | derive from the ownership grants (the nodes that will own a part of this entity), not "all known nodes" — user-approved; the session designs the exact seam into `BrainMuscleOwnershipStrategy`/`IClusterStateCache` |

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
