<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the task table and DECISIONS
stale-below: nothing
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md §9b, §9b.1 — the design built (K1–K13 binding)
  - HANDOFF_Behaviors_To_Backend_UnderFire.md — the dispatch (frozen at ded921982)
  - NOTE_Backend_To_Behaviors_UnderFire_Contract.md — the ids and signatures behaviors binds to
-->

# STATUS — backend: the `UnderFire` place-knowledge kind (CE-2468, R-2140)

Branch `backend`; merged `behaviors@25431b39c` (a fast-forward: it already held every `backend` commit), started marker `979d55e6a`, contract note
`91da592a6`.

| task | state | evidence |
|---|---|---|
| UF-1 `RecordWriterCore.Retire(key)` (K2) | DONE | `Retire` ends this node's OWNED records of a key: a networked one through `DestroyEntityCommand` (the ELM teardown, the DDS dispose to every ghost) and marked `TearDown` at once, an offline one destroyed. A ghost is never touched; a creation still in flight is ended as it arrives and its queued items are forgotten. The destroy is ONE helper now, `NetworkEntityDestroy.Request` (it was written out in obstacles, charges and gas clouds; all three go through it). Rails `RecordRetireTests` (5: owned networked + offline, a ghost untouched, a pending creation, keyframes before / after seek back and forth), each red-proved by its own mutation (no end · no owner test · a retired pending key drained as usual). ⏳ The ghost's DDS dispose on a SECOND node is proved by UF-2's two-node rail. Orphans: `Retire` acts where `Owns` holds, which the ownership handover makes true on the inheriting node |
| UF-2 the kind, writer, ids, wire, registration | DONE (part 1 `d081a9ff2`, wire part 2 — the commit after `5fcb3dddb`) | `UnderFireKnowledge` (component 171, TKB 7211, unmanaged), `UnderFireWriter` (a `RecordWriterCore` subclass: merge within 25 m of a record's frozen point, newest wins; cools at 120 s / 600 s by sim time, retires at 2 ×), registered in `DamageAssessmentModule` and `CgfLogicPack`; wire `EntityUnderFireKnowledge` / `dtUnderFireKnowledge` 210, the egress / ingress pair in the minefield-knowledge shape, in `SharedTranslatorPack` + `NedOwnershipGroupBinding`. Rails `UnderFireKnowledgeTests` (7, red-proved) + `EntityGroundDamageTranslatorTests.CE2468_*` (2: the ordinal matches the wire; a record round-trips byte for byte to a ghost and the owner never takes its own back — red-proved by dropping the `Point.Z` copy, the `State` test in `Matches`, the ingress owner guard). The pack's system count `SimHostCoreLogicPackTests` 23 → 28 (+3 flame lane, CE-3487 / CE-3488, stale; +2 here) |
| UF-3 save by age (K1) | DONE `b9a0daa74` | the save writes an owned record's age; a load makes it `Loaded` (age in `LearnedAt`, no reader treats it as fresh); the owning writer rebases it each tick it finds one. Rails `TerrainObjectRequestsTests.CE2468_APlaceUnderFire_IsSavedByAge_AndLoadedHoldingIt`, `UnderFireKnowledgeTests.UF3_*` (a save at t 3000 loaded at t 0 ages from the saved age) |
| UF-4 table + sight (K3, K5, K11) | DONE `c3911d14c` | `UnderFireTable` on `DoorStates`, its own cache; `UnderFireSight` grids per Direct record on the authored terrain (`SegmentBlocked(doors: null)`, eye 1.6, target 1.0, 5 m), built by `UnderFireSightSystem` in the serial pass only when the table, terrain or builders changed, cleared on `WorldEpoch.Moved`. Rail `UnderFireNavTests.UF4_*`: equal grids on two loads cell for cell, a path query builds nothing, 0 B over 10 steady frames |
| UF-5 per-side route cost (K4, K8, K12) | DONE `c3911d14c` | `UnderFirePolyMap` per mesh registered by the bake (keyed by layer: a re-bake replaces it), factor 1 + 7 × covered share, Direct only; one lookup in `DoorAwareQueryFilter.GetCost`; `RoadGraphRouter` prices a segment whose midpoint is covered (×8), `RoutePlanner` passes the side. Rails `UnderFireNavTests.UF5_*`: two streets round a wood — Blue (knows) drives the far street, Red the near one; Indirect not priced; the road leg goes round for Blue |
| UF-6 the danger solve | DONE `1f0f1c9be` | Shelled areas from the side's Indirect records; `CoveredByFire` (kind 9, bit 1 << 9, `Wants` bound) from the side's Direct grids (`CoveredByFireAreas`, the `MinefieldAreas` shape, cap 4); rated 1. Rail `UnderFireNavTests.UF6_*`. Shared-seam scenarios: below |
| UF-7 the witness detector (K6, K8) | DONE `1f0f1c9be` — closes CE-4626 | `ShelledAreas.Teach`: each side with a live unit within 1 500 m learns a shelled point when it becomes shelled, on a new burst and every 20 s; `At` = the last burst. Rail `UnderFireKnowledgeTests.UF7_*`: Blue at 1 km learns, Red at 2 km does not until it comes within range; a refresh does not move `LearnedAt` |

📄 Design folded: [`DESIGN_Minefields.md` §9b.2 "As built"](../../designs/minefields/DESIGN_Minefields.md) (obligation ⑤).

### Shared-seam scenarios (UF-5 / UF-6) — `scenario-regression.sh`, deterministic editor tier

| scenario | after CE-2468 (`a58bc1c29`) | base `91da592a6` (before UF-1, clean worktree) | verdict |
|---|---|---|---|
| `mf-engineers-answer` · `mf-knowledge-survives` · `mf-at-vs-vehicle` · `mf-way-round` · `mf-recon-and-mark` · `mf-strike-and-halt` | PASS | — | unchanged |
| `ua-squad-danger-crossing` · `-contact` · `-hold` · `pc-urban-move` · `agg-shelled-unit` | PASS | — | unchanged (⭐ `agg-shelled-unit` passes with shelling per side, 1 500 m witnesses — K8's intended change did not move it) |
| `mf-no-way-round` | FAIL: "the squad reacts MinefieldAhead", "still halted at the end" | FAIL, the SAME two rows | ⚠ pre-existing (`CE-2447` history) |
| `mf-clear-lane-and-cross` | FAIL: S8, S10, S11 (t 1956.5 s) | FAIL, the SAME three rows at the same time | ⚠ pre-existing |
| `pc-urban-ambush` | FAIL: ClearBuilding H5, UrbanMovement again, east to (280,190) | FAIL, the SAME three rows | ⚠ pre-existing |

⚠ **The first run found a real defect:** 14 / 14 aborted on the first frame — `UnderFireSightSystem` had no `[TickMode]` (CE-3293); fixed `a58bc1c29`, with a guard in
`SimHostCoreLogicPackTests` (every pack system declares one; red-proved). ⭐ The parallel checks (`HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) on the 18
UnderFire rails: 18 / 18. ⏳ `stepped-hash.py --threads 1,4,8`: not yet run.

### Red-proofs — every rail by a compiling mutation

| mutation | rail | result |
|---|---|---|
| `GetCost` multiplies by 1 | `UF5_TheCoveredStreet…` | red |
| road segment factor 1 | `UF5_ARoadLeg…` | red |
| `UnderFireSight.Covered` no side test | `UF5_TheCoveredStreet…` | red (⚠ first SURVIVED: Red had no grid of its own — the rail now gives it one, `5fcb3dddb`) |
| `UnderFirePolyMap` box filter no side test | — | survives by design: a pre-filter only; the side decision is `Covered` (above) |
| `CoveredByFireAreas` no side test | `UF6_…` | red (after the same rail fix) |
| `ShelledFootprints` no side test | `UF6_…` | red |
| witness distance dropped | `UF7_…` | red |
| refresh `At` = now | `UF7_…` | red |
| sight rebuilt every frame | `UF4_…` | red |
| wire: no `Point.Z`, no `State` in `Matches`, no owner guard | `CE2468_AnUnderFireRecord…` | red ×3 |

⚠ **Stale-binary trap met here:** restoring a mutated file from a backup kept the backup's OLDER mtime, so MSBuild kept the mutated `Fdp.Toolkits.dll` and a "clean"
run reddened; every source is touched after a restore now. Every verdict above was taken on a build of the mutated source (newer than the DLL).


## CE-2469 — the route's steer point (`HANDOFF_Behaviors_To_Backend_RouteSteerPoint.md`, dispatched at `ba1559906`; started `e221f5f0f`, merged `behaviors@2ea9fe7fc`)

| task | state | evidence |
|---|---|---|
| SP-1 steer point in the danger answer + `DangerRouteSteer.Of` | DONE `1a738faf0`, `123be9441` | buffer `HasSteer` + `SteerPoint` (608 B), event, `DangerAreaResultTopic` `SteerX/Y/Z/HasSteer` (ordinal unchanged), both translators, `Apply`, cleared on a new epoch. Rails: `UnderFireNavTests.CE2469_*` (a Friend that knows the enemy steers round the wood, a Hostile straight, a target with no wheeled plan carries no steer; `Of` fresh then stale) — red-proved ×3 (steer from start/end only · the straight fallback steering · `Apply` dropping it); `EqsDistributedTests.DangerAlongRoute_AcrossHosts_*` (two nodes, 13 s, the steer reaches the Brain); the parallel checks 19 / 19. Design `DESIGN_Utility_AI_Demo_Scenarios.md` §10.7c |
| gates — `mf-*`, `ua-squad-danger-crossing*` | ⚠ 8 / 11 | `mf-no-way-round`, `mf-clear-lane-and-cross` red at the base too (above); ⚠ `ua-squad-danger-crossing-hold` red SINCE THE BEHAVIORS MERGE — red at `e221f5f0f` (no CE-2469 code), green at `a58bc1c29`: reported in `NOTE_Backend_To_Behaviors_RouteSteerPoint_Done.md` |


## DECISIONS

| # | decision | why · what would change it |
|---|---|---|
| D-UF1a | the network destroy is one helper (`NetworkEntityDestroy.Request`), not a fourth copy | the shared-and-unified rule; each caller keeps its own extra step (a charge spent, a cloud ending flag) around it |
| D-UF1b | `Retire` writes the lifecycle / destroy directly, not through the command buffer | the writer is serial and `EntityQuery` is an index walk, so changing an entity under it is safe; the record leaves the writer's own query (`AllButTearDown`) at once |
| D-UF2a | `MergeM` = the subject grid's 25 m | one size for "the same place"; a finer merge would need its own key |
| D-UF5a | the cost maps are keyed by the layer's door-poly map (the token every working copy of the filter shares) and their builders by the layer | a re-bake makes a new mesh; keyed by the layer it replaces the old builder instead of leaking one per bake |
| D-UF5b | a road segment is priced by its curve MIDPOINT (K12), ×8 when covered | K12 as written; a per-sample share would be finer — change it if a road under partial fire mis-routes |
| D-UF6a | `CoveredByFire` is rated 1 (as shelling and gas) | the ground is the threat while the record lives (600 s); rating by contact memory would read 0 once the contact is forgotten |
| D-UF6b | a sensor with no side sees no shelling | the shelling is now per side (K8); every unit has a side, so this only affects side-less test sensors |
| D-UF7a | any live `EntityInfo` entity of a side witnesses, Neutral included | KG-3 says "a live unit of that side"; restrict to combatants if civilians' records cost too much replication |
| D-SP1a | the solve plans whenever its node has a navmesh (the early return before planning now needs no navmesh too) | a clear route must still carry its steer; costs one plan per refresh on a road-less map — revisit if the solver's budget shows it |
| D-SP1b | no steer for the straight fallback | a straight line is not a priced path; the bound keeps its own straight line then (behaviors' fallback) |
| D-UF4a | the published sight (`UnderFireSightHolder`) is NOT in checkpoints; after a restore it is rebuilt on the next serial pass | it is derived from recorded records; ⚠ until that pass a query reads the pre-restore cover — tracked as [CE-3491](../Blueprint_Issues_Tracker.md) (measure the window; rebuild at the restore if it exists) |

