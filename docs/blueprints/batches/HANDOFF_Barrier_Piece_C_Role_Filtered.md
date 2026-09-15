<!--STATUS
state: LIVE
build-state: HANDOFF — dispatched to the UI/CGF lane (H - ui, `claude/reset-working-branch-qd1qpv`).
  Piece C of the cross-node construction barrier: make it PRODUCTION-correct — role-filtered expected
  peers (creator) + real per-type receiver participants — replacing slice A's synthetic "all peers".
  ⛔ STEP 0 is a DESIGN-VERIFICATION gate (as for slice A) — verify the type→roles derivation before building.
updated: 2026-09-15
current-answer: this is the dispatch. Owning DESIGN: DESIGN_Cross_Node_Construction_Barrier.md (§1 diagrams,
  §3a.7 slice-A as-built + the CE-284 follow-ons this closes). Frame: FRAME_Construction_Barrier_Participants.md
  piece C. Build against them; author piece-C UML INTO the design (step 1), do not redraw here.
related-designs:
  - ../../DESIGN_Cross_Node_Construction_Barrier.md — ⭐ THE OWNING DESIGN. §3a.7 = slice A as-built + CE-284.
  - FRAME_Construction_Barrier_Participants.md — piece C (receiver participants) + §4a peer membership.
  - ../../DESIGN_Role_Affinity_Ownership.md — the role vocabulary + who-owns-what (roles are labels, engine holds no semantics).
  - ../../designs/tkb-1/DESIGN.md — §6.6a/§6.6b the GhostPromotion mandatory-components gate (the receiver-side gate piece C extends).
-->
# HANDOFF — barrier piece C: role-filtered peers + real per-type participants *(production-correct)*

> 📌 **Scope is FROZEN at the coordinator HEAD named in the dispatch message.** Documents that change after
> it are FYI only. ⭐ Rule 7: re-sync from `claude/blueprint-authoring-status-6sr5ld` FIRST. ⭐ Rule 1b: push
> an empty `chore: started piece C at <sha>` marker before writing code. ⛔ No PR. ⛔ Continue `CE-` ids
> (plain increments, rule 3a-id) — you allocate them; `CE-284` was opened for these follow-ons. ⛔ If a
> premise here is false, STOP that item and report — do not adapt silently.

## 0. 🎯 GOAL — make the barrier PRODUCTION-correct
Slice A (CE-283) proved the mechanism with option A: the creator waits on **all present peers except itself**,
and the peer gates with a **synthetic** participant. Both are proof-only. Piece C makes it real:
- **Creator side:** the expected-peer set is **role-filtered** — the creator waits only on present peers whose
  role must actually initialise a copy of *this entity type*. (Fixes the CE-284 production-gate: option A
  blocks a creator on peers that never init the type.)
- **Receiver side:** a **real per-type participant** defers `Active` until its node-local data is genuinely
  ready — replacing the synthetic participant — using `RegisterRequirement(tkbType, moduleId)` so it blocks
  **only** for entity types that require it.

## 1. ⛔⛔ STEP 0 — VERIFY THE DESIGN FIRST *(same discipline as slice A; report before code)*
The whole approach rests on ONE premise: **"which roles must init entity type X" is derivable from the
existing role↔component model, not a new vocabulary.** Verify it before building; report the table to
H-coord as your FIRST message. ⛔ If it does NOT hold, STOP — this becomes an architect-question decision
(where the type→roles contract lives), not something to improvise.

| # | claim to verify | expected (coordinator inventory `2026-09-15`) | how |
|---|---|---|---|
| V1 | the `IExpectedPeersProvider` seam is the creator-side plug point, doing option A today | `ClusterCacheExpectedPeersProvider` wired at `NedNetworkFactory.cs:406`; iface at `Fdp.Toolkits/Replication/Abstractions/IExpectedPeersProvider.cs` | open both |
| V2 | a **role→component-set** model exists | `HrotRoleComponentSets` in `Hrot.Core` (owned / brainOnly / birthCritical sets, `[Flags]` role-keyed) | grep/`search_graph` `HrotRoleComponentSets` |
| V3 | a **per-type required-component** set exists | `TkbTemplate.BirthCriticalComponents` = read-only view derived from `[BirthCritical]` on the component type | open `TkbTemplate` + a `[BirthCritical]` usage |
| V4 | ⭐⭐ **the derivation closes:** roles-that-init-type-X = the roles whose component-set intersects X's required (birth-critical / init-requiring) components | this is the LEAN — confirm the two tables compose, OR name exactly what's missing (e.g. "no table says which role *initialises* navmesh vs merely *owns* it") | reason over V2+V3; if the gap is real, that is the STOP finding |
| V5 | `RegisterRequirement` has zero real callers (piece C is first) | only the def (`EntityLifecycleModule.cs:158`) + a comment | grep |
| V6 | the receiver gate to extend already exists | GhostPromotion mandatory-components gate + receiver local ELM (tkb-1 §6.6a/b) | open the gate |

## 2. ⭐ STEP 1 — DESIGN DETAIL (frame-delegation) — author the UML INTO the design
Fold your buildable sub-design into `DESIGN_Cross_Node_Construction_Barrier.md` (a new §3b, marking slice A's
option-A note superseded-for-production per obligation ⑤). Draw:
- the **type→roles derivation** as a small data-flow (type → required components → role↔component intersect →
  roles → present peers) — the load-bearing new picture;
- the **module-relationship** update (obligation ①a): which real participant registers on which host
  (navmesh/altitude → Muscle; model-load → IG), and confirm it TICKS there (the §1.3 dead-edge discipline —
  the frame's HOST-REACHABILITY hazard is real: `NetworkLifecycleSystemGroup` is NED-only, dead on the
  editor's `NullReplicationModule` and BDC).
⭐ **My lean, for you to confirm or refute in V4:** derive the type→roles map from `HrotRoleComponentSets`
(role→components) inverted per type against the type's init-requiring components — **do NOT invent a new
type→roles config table** (seam law; the role→component model is the single source, `R-134`-style). If V4
shows the existing tables can't distinguish *initialises* from *owns*, STOP and report — that's the architect
call.

## 3. 🔧 STEP 2 — BUILD (you own the item breakdown / slicing)
| # | build | note |
|---|---|---|
| **C1** | **Role-filtered `IExpectedPeersProvider`** — replace option A: expected peers = present peers whose role ∈ roles-that-init(type), minus local. Extend/replace `ClusterCacheExpectedPeersProvider` (it already has the cluster cache with `NodeCapability.Role` from CE-282) | the production-gate; keep the seam so the generic `NetworkSpawningSystem` stays NED-free |
| **C2** | **One real receiver participant** (start with navmesh/altitude on Muscle, subclass `DeferredConstructionParticipant`) — `RegisterRequirement` for the types whose birth-critical set needs it; ack when the node-local data is ready | replaces the synthetic participant; proves per-type gating end-to-end. Model-load on IG can be a further slice if scope demands — your call, argue it |
| **C3** | if scope allows: the CE-284 hardening rails (A7 late-joiner explicit rail, §2b node-id rail) — otherwise leave them in CE-284 and say so | don't let hardening rails block the production-gate |

## 4. ✅ ACCEPTANCE — the proof, not a green gate
On `--mode all`:
- ⭐⭐ **role-filtering proof:** a creator making an entity of a type that only role R must init does **NOT**
  stay blocked on a present peer whose role is NOT R (option A would have) — assert the stamped
  `NetworkAckPeerSet` contains the R-peer and excludes the non-R peer.
- ⭐⭐ **real-participant proof:** spawn a spatial type in an **uncached** area on a Muscle peer → the creator
  stays `Constructing` → the navmesh participant acks when the tile loads → peer `Active` → creator `Active`.
  Timeout path force-activates. Fast mode unchanged (regression rail).
- T-1: **find + run** the feature's own suites first (`NetworkGatewaySystemTests`, `NetworkGatewayIntegrationTests`,
  `SplitAuthoritySpawnTests`); add rails INTO them, red-proofed. Build affected projects only, `--no-build`
  after first; `--mode all` proof in the BACKGROUND (T3, never a foreground blocker).

## 5. ⭐⭐ REPORT BACK TO **H - coord** (the gate contract — substitutes for my re-run)
Two messages:
1. ⭐ **After Step 0:** the V1–V6 verification table (hold / DIFFERS). ⛔ If V4 fails, stop there and wait for
   the architect-question decision.
2. **When done or blocked:** `CE-` ids allocated · the design § you folded (with slice-A option-A note marked
   superseded-for-production) · one row per gate (verbatim command · pass/fail/skip · `--no-build` · delta vs
   base) · every RED confirmed pre-existing against the base sha · working tree clean · the diff shape ·
   `rulings-check.py` + `design-digest.py --check` + `mermaid-check.mjs` verdicts · the two `--mode all` proof
   results. Deliver by trigger to session H - coord, as for CE-283.
