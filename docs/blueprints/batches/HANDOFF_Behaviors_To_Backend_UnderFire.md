<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md §9b + §9b.1 — ⭐ THE design you build: the UnderFire kind, and the review's binding resolutions K1–K13
  - ../../designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.9 — behaviors' half (R1–R6), the CONSUMER of §1's contract; FYI, not yours to build
-->

# HANDOFF — behaviors → `backend`: the `UnderFire` place-knowledge kind (CE-2468, R-2140)

**For:** the `backend` lane. **From:** behaviors. **Dispatched at `ded921982`**; ⛔ scope FROZEN there — later documents are FYI; one that invalidates
a task ⇒ STOP that task, do the rest (R-106).

🔒 User, `2026-11-21`: *"once the design review comes back, let's integrate it into the design, and we will let another lane, the backend lane, to build
it, so you can focus on the behaviors"* · *"let's make sure that the knowledge can age. So nothing lasts forever."* · the decisions: R-2140 (`RULINGS.md`).

## 0. Start
`git fetch origin behaviors backend coordinator`; on `backend`, merge `origin/behaviors` (it carries the design at `ded921982`); restore the test projects
you run; push `chore: started UnderFire at <sha>`. Read [Minefields §9 – §9b.1](../../designs/minefields/DESIGN_Minefields.md) END TO END first (R-129) —
⭐ §9b.1's resolutions K1–K13 BIND the build; where §9b's first table and §9b.1 disagree, §9b.1 wins.

## 1. The contract — fixed names behaviors builds against (⛔ do not rename; ids are yours to allocate, rule 3)

| name | shape | used by behaviors for |
|---|---|---|
| `Fdp.Toolkit.Terrain.UnderFireCause` | `enum : byte { Indirect = 1, Direct = 2 }` | — |
| `UnderFireEvents.LearnDirect(BatchContext/buffer, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at)` | an EVENT through the batch buffer, applied by your serial writer (R-4212 — callable from the parallel brain phase) | Squad Tactics R1 |
| `UnderFireTable.Of(ISimulationView view)` → `.NearestFresh(int side, Vector3 point, float withinM, out UnderFireRecord r)` | allocation-free, a fresh (not `Cooled`) record of that side whose frozen point is within `withinM` | R4 (`Firm()`), R2 (the casualty exposure) |
| `UnderFireRecord` | `{ Cause, Point, RangeM, EnemyStrength, LearnedAt }` (readonly struct) | R4's superiority |
| `DangerAreaKind.CoveredByFire` + its `DangerAreaKinds` bit | reported by the route danger solve when the route enters a `Direct` cell of the unit's side | R3's SOP row `CoveredByFireAhead`, `DangerRouteWatch` asks for it |
| the per-side route cost | automatic — every path query of a mover of side S (navmesh AND road legs) | R3 (the march's lead, `BoundAlongRoute`) |

⭐ **Your FIRST commit after the start marker:** `NOTE_Backend_To_Behaviors_UnderFire_Contract.md` — the ids you allocated (component, TKB, descriptor
ordinal, DDS type) and the exact signatures as built, so behaviors binds early. Until your work merges, behaviors builds against a stand-in with these names.

## 2. The tasks (Minefields §9b + §9b.1)
| # | task | done when |
|---|---|---|
| UF-1 | `RecordWriterCore.Retire(key)` (K2): owned only, drops a pending creation, DDS dispose to ghosts, the recorder records it; orphans retired by the inheriting owner | rails: retire an owned record, a pending one, a ghost disposed, replay/seek agree |
| UF-2 | the kind: `UnderFireKnowledge` (header `SubjectKind.Place`, frozen point, `State` incl. `Cooled`), the writer subclass (merge newest, cool → `Cooled`, 2 × cool → `Retire`), `PlaceKnowledgeKind.UnderFire`, `KnowledgeSource.Contact/.Burst`, ids, DDS + translators in the shared pack, registration (K7, K9) | the kind's rails + both cool times, a two-node replication rail |
| UF-3 | save/load: AGE written, REBASED at load (K1) | a save at t = 3000 s loaded at t = 0 ages from the saved age |
| UF-4 | `UnderFireTable` on `DoorStates`, its own cache (K11); `UnderFireSight` on the AUTHORED terrain, eye 1.6 m, target 1.0 m, 5 m grid, built in the serial pre-pass, cleared on `WorldEpoch.Moved` (K3, K5) | equal grids on two loads; 0 B on a steady frame (R-2084); the build off the path-query threads |
| UF-5 | the cost: `UnderFirePolyMap` per side (1 + 7 × covered share, `Direct` only — ⛔ `Indirect` NOT priced, K8), one lookup in `NavDoorways.GetCost`; road legs priced in `RoadGraphRouter` (K4, K12) | a two-street navmesh rail: the covered street avoided for that side only, the other side unaffected |
| UF-6 | the danger solve: `Indirect` → `Shelled` areas FROM THE TABLE (the unit's side), `Direct` → `CoveredByFire` (`CoveredByFireAreas`, the `MinefieldAreas` shape, its cap), `Wants` cap (K10) | the solve rails; the shelled scenarios re-run and reported (K8's intended change) |
| UF-7 | the Indirect detector: `ShelledAreas` emits `Learn` per witness side within `WitnessMetres` 1 500 m when a point becomes shelled, refreshes with `At = T0` (K6, K8) — closes CE-4626 | a witness side learns, a side 2 km off does not |

## 3. Gates — every task
T-1 first (the minefield knowledge suites, the danger-area suites, the nav suites, `agg-shelled-unit`); every rail red-proved by a compiling mutation; the
parallel checks (`HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) on every rail the brain phase reaches; `stepped-hash.py --threads 1,4,8`;
0 B per steady frame; invariant output (R-4221); new components unmanaged and fixed before their first recording. ⭐ UF-5 and UF-6 touch shared seams —
re-run `mf-*`, `ua-squad-danger-crossing*`, `pc-urban-*` and report them unchanged.

## 4. The report — `STATUS_Backend_UnderFire.md`
per task built / stopped and the design folded (obligation ⑤) · every lane decision with what would change it · the gate-report contract (rule 8) · ids allocated.
