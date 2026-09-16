<!--STATUS
state: LIVE
build-state: HANDOFF — dispatched to the UI/CGF lane (H - ui, `claude/reset-working-branch-qd1qpv`).
  Piece C, PRODUCTION model. SUPERSEDES HANDOFF_Barrier_Piece_C_Role_Filtered.md (whose premise was retracted).
updated: 2026-09-16
current-answer: build against the AUTHORITATIVE design — DESIGN_Cross_Node_Construction_Barrier.md §3b
  (barrier + local poll-store), §3c (degradation), and Architect_Question_70 (host capabilities + roles-from-
  tokens). Those are settled; do not redesign them — author only the as-built + any missing UML into them.
related-designs:
  - ../../DESIGN_Cross_Node_Construction_Barrier.md — §3b/§3c the authoritative model.
  - ../../blueprints/Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md — §Q70-A/B/C the capability + roles-from-tokens facility.
  - ../../designs/cluster-master-cqrs-1/DESIGN.md — §8 the roster gathering host attributes (capability tokens + derived role mask).
  - ../../designs/two-ack/TwoAck-DESIGN.md — SstStatusCode {InProgress, NotSupported} reused for the requestor-facing status.
-->
# HANDOFF — barrier piece C (PRODUCTION model)

> 📌 **Scope FROZEN at coordinator HEAD named in the dispatch message.** ⭐ Rule 7: re-sync from
> `claude/blueprint-authoring-status-6sr5ld` FIRST. ⛔ No PR. ⛔ Continue `CE-` ids (plain increments) — you allocate them.
> ⛔ If a premise here is false, STOP that item and report.

## 0. ⛔⛔⛔ STEP 0 — DISCARD THE RETRACTED WORK, THEN RE-SYNC
Your commit **`b0f05216`** (`CE-283 piece C — C1: role-filtered expected peers`) built the **RETRACTED** contract —
`RequiresPeerInitAttribute`, `HrotRoleComponentSets.Initialises`, `ComponentAttributeSets.RequiresPeerInit`, and the
component-mask `IExpectedPeersProvider.GetExpectedPeers(componentMask)` role-filter. The user retracted ALL of it
*(design known-rot; the peer's wait condition is host-local and OPAQUE to the creator — there is no creator-side
component→role→init filter)*. ⇒ **discard `b0f05216` entirely** *(it also edited §3b, now superseded by the
coordinator's authoritative §3b)*:
```bash
git fetch origin claude/blueprint-authoring-status-6sr5ld
git checkout claude/reset-working-branch-qd1qpv
git reset --hard origin/claude/blueprint-authoring-status-6sr5ld   # drop b0f05216 + the old started-marker; take the authoritative design
git push --force-with-lease origin claude/reset-working-branch-qd1qpv
git commit --allow-empty -m "chore: started piece C (production model) at <dispatch-sha>" && git push
```
⚠ **Nothing in `b0f05216` is salvaged** — the new model computes the wait-set from capabilities + an optional list,
not from a component attribute. Re-derive what you need against §3b/§3c.

## 1. 🎯 GOAL — make reliable init PRODUCTION-correct + gracefully degrade
Build the settled §3b/§3c/AQ-70 model. **Do not redesign** — those docs are authoritative; author the as-built
(and any missing UML: the `NodeCapabilities` descriptor + derive-at-ingest flow if AQ-70's `graph TD` doesn't cover
your build) INTO them (obligation ⑤), marking prior state superseded.

## 2. 🔧 WHAT TO BUILD *(you own the CE- id breakdown + slicing/ordering; suggested order below)*
| # | item | design |
|---|---|---|
| **C-cap** | **Host-capability facility.** A durable `NodeCapabilities` descriptor *(`[DdsQos(Reliable, TransientLocal, KeepLast 1)]`, keyed by NodeId, `string[]` tokens)*, published once at join. Orchestrator gathers it into the roster/cache. Tokens are **OpenGL-extension-style namespaced** *(`fdp.reliable-init`, `fdp.role.brain`, …)*. `caps.Supports(nodeId, token)`. | AQ-70 §Q70-A/B, cluster-master §8 |
| **C-roles** | **Roles from tokens (supersede CE-282 roles-on-heartbeat).** Host lists `fdp.role.*` tokens on the descriptor; orchestrator **derives** the `NodeRole` mask at ingest via a **closed enum↔token table in `Fdp.Core`**; heartbeat returns to **telemetry-only** *(drop `RolesMask`)*. `NodesWithRole` + ownership tables consume the derived mask, **unchanged**. | AQ-70 §Q70-C, barrier §5 (transport-superseded), cluster-master §8 |
| **C1** | **Creator wait-set.** `SpawnEntityCommand` gains `ReliableInitPeers : int[]?` *(null ⇒ all present nodes that advertise `fdp.reliable-init`; creator MAY narrow by role)* + `ReliableInitTimeout : TimeSpan?`. Wait-set = capability-filtered present nodes ∩ the optional list, minus local → stamp `NetworkAckPeerSet`. ⛔ NOT a component-mask filter. | §3b.1 |
| **C2** | **Mandatory reply + short phase-1 probe.** Supporting host publishes `EntityLifecycleStatusDescriptor(State=Constructing)` promptly *(phase-1)* then `Active` *(phase-2)*; a **non-ghosting** node replies immediately *(from the `EntityMaster` ingress)*. Creator arms a **short phase-1 timeout** per wait-set host; missing phase-1 ⇒ drop it + record a `!fdp.reliable-init` override on its roster entry *(self-heal)*. | §3b.2, §3c ②, AQ-70 §Q70-A |
| **C3** | **Timeout = ABORT via EntityMaster dispose.** Creator-authoritative long `ReliableInitTimeout` → dispose the `EntityMaster` instance → existing `ProcessDispose` cleanup on peers. Receiver **never self-force-activates** a reliable ghost. Reuse the existing disposal-sample path *(also covers writer-death via liveliness)*. | §3b.3 |
| **C4** | **Local poll-store.** A singleton `ConstructionResults` component *(dict keyed by `NetworkId` + TTL, evict-on-read)*, written by the gateway on resolve *(Success / Failed(Timeout))*, polled by the local requestor `ConstructionResults.Get(world, networkId)`. Mirrors `PathfindingBatchData`; NOT the remote `CreateEntityAck`. | §3b.4/§3b.5 |
| **C5** | **One REAL receiver participant** *(navmesh/altitude on Muscle, subclass `DeferredConstructionParticipant`, `RegisterRequirement` per type, ack when the tile is ready)* — replaces slice A's synthetic participant; the live proof. | §3b / §4 |

⭐ Requestor-facing status reuses `SstStatusCode.NotSupported`/`InProgress` *(two-ack §3.2)* — do not invent codes.

## 3. ✅ ACCEPTANCE — `--mode all` proofs (not a green gate)
- **capability/role filter:** a creator does NOT wait on a present peer that lacks `fdp.reliable-init`; roles resolve from `fdp.role.*` tokens *(derived mask)* end to end.
- **real participant:** an uncached spatial type holds the creator `Constructing` until the Muscle navmesh tile loads → peer `Active` → creator `Active`.
- **degradation:** a peer that never publishes a phase-1 status is dropped within the short timeout and does NOT block; it is remembered *(`!fdp.reliable-init`)*.
- **abort:** the long timeout disposes the `EntityMaster`; all ghosts are removed; the local `ConstructionResults` reads `Failed(Timeout)`.
- **fast mode unchanged** (regression rail). T-1: run the feature's own suites first *(`NetworkGatewaySystemTests`, `NetworkGatewayIntegrationTests`, `SplitAuthoritySpawnTests`, orchestration/roster suites)*; add rails INTO them, red-proofed. Build affected projects only, `--no-build` after first; `--mode all` in the BACKGROUND (T3).

## 4. ⭐⭐ REPORT BACK TO **H - coord** (gate contract)
Report at natural checkpoints *(after C-cap/C-roles; after the barrier)* and when done/blocked: `CE-` ids allocated ·
the design §§ you folded (with superseded prior state) · one row per gate *(verbatim cmd · pass/fail/skip · `--no-build` ·
delta vs base)* · every RED confirmed pre-existing against the base sha · working tree clean · diff shape ·
`rulings-check` + `design-digest --check` + `mermaid-check` verdicts · the `--mode all` proof results. Deliver by trigger to session H - coord.
