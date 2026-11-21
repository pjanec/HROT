<!--STATUS
state: LIVE — REPORTED (every slice built; the morning report is §0)
updated: 2026-11-16
current-answer: §0 the morning report, then §1 the slices, §2 lane decisions, §3 gates, §4 ids
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — the design
  - HANDOFF_Coordinator_To_Terrain_Minefields.md — the dispatch (scope frozen at 8b02d1a2a)
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract in numbers (published first)
-->

# STATUS — `terrain`: minefields, the engine (CE-4624) — started at `b1f83aeb0` (merged `coordinator@50a2f828d`)

## 0. The morning report

⭐ **Every slice of the handoff is built, red-proved and pushed** (MS-T1 … MS-T4, the contract first). Behaviors' two asks (`R-4278` note) are
done: the per-mover breadcrumbs (MR-5, `MineBreadcrumbs` 326, R-5049) and the clearer's switch named (`MineClearing` 325 —
[NOTE](NOTE_Terrain_To_Behaviors_Minefield_Switch.md)). The other lanes' findings are answered in
[NOTE_Terrain_To_Behaviors_Ui_Minefield_Findings.md](NOTE_Terrain_To_Behaviors_Ui_Minefield_Findings.md) — ⭐ the AT mine now hurts the hull
through its belly and no longer reaches a man 35 m off (`0573b3a82`).

| ⚠ open | |
|---|---|
| ui T-2 — a burst effect seen at z ≈ 1500 on a flat map | not reproduced by reading (the burst takes the hull's Z); the next live run reads both heights |
| the minefield FEATURE SCENARIOS (R-4277) | behaviors owns them (`mf-*`, `NOTE_Coordinator_To_Behaviors_Minefield_Scenarios.md`); terrain's slices are judged there too |
| next | `HANDOFF_Coordinator_To_Terrain_Breaching_Fixes_And_Versions.md` (dispatched as this batch's follow-up) |

## 1. The slices

| slice | state | evidence |
|---|---|---|
| contract NOTE + stubs | ✅ | [NOTE](NOTE_Terrain_To_All_Minefield_Contract.md); `MinefieldContractTests` (4), the ordinals pinned (`EntityGroundDamageTranslatorTests.CE4624_*`), `TestComponentIdUniquenessTests.EveryGlobalComponentId_IsUnique` |
| MS-T1 field, trigger, bursts, armour bottom | ✅ `68ae4763e` (trigger: segment test, AP / AT / livestock, once per mine, 0 B; munitions 6051–6053; armour Bottom facing; TKB 8830, 7206, 7207) + the strikes record `7e2b1b8e1` | `MineTriggerTests` (10), armour rails (4) — 15 mutations red-proved; gates 1132/1132 toolkit (PAR + READ + GUARD), SimHost 329/331 (2 not this slice) |
| MS-T2 place-knowledge core | ✅ core `7e2b1b8e1` (RecordWriterCore, PlaceKnowledgeSystem on SimHost + CGF, the union table on DoorStates); ✅ translators `c1bd65533` (191–195, round-trip rails; red-proved by the coordinator); ✅ the save `e5656e0d1` — the `Knowledge` section, kind-policy merge arm, load requests (`MinefieldSaveTests` 11, `TerrainObjectRequestsTests` 1; DESIGN §3d) | `PlaceKnowledgeTests` (8), red-proved |
| MS-T3 routing by side | ✅ the side in the route mask + `JudgeBy(doors, side)` `55b5e0690`; ✅ the danger kind `55b5e0690`; ✅ SUMO closure `59d1c2226`; ✅ stamping (composite areas 5/6/7, lane restores, polygon → field map) + pricing by side (DESIGN §3e) | `TerrainWoodsTests.CE4624_*`, `DangerAreaSensorSystemTests.CE4624_*`, `BridgeClosureTests.CE4624_*`, `MinefieldNavTests` 9, `MinefieldBakeTests` 2 |
| MS-T4 lanes and clearing | ✅ the marker + the lanes record `e99beab11`; ✅ the swath / line-charge Muscle, `MinesCleared` 5109, TKB 116 (DESIGN §3f) | `PlaceKnowledgeTests.ALaneCleared_*` |

## 2. Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-5042 | the mine LATTICE is anchored at the field entity's position (not the outline bounds) — a (row, col) survives an outline edit; a MIXED field alternates rows (AP even, AT odd) | a doctrine source wanting another mixed layout |
| R-5043 | `DangerAreaKinds` widened to `ushort` (MR-9) but `Shelled` KEPT on bit 0 (MR-9 said drop the hack): no recorded / saved value changes | a reason to renumber (none found) |
| R-5045 | the strikes record has its own TKB 7207 (the design gave a TKB only to the knowledge record) | — |
| R-5046 | known-at-start is NOT a record: the field's `KnownAtStartSides` answers it (records per deciding node would duplicate it in saves) | a need to forget an own field (none) |
| R-5047 | the route's SIDE rides in the layer mask's spare bits (14…18), not the request's pad byte (MR-4 / KR-13): the mask already reaches every route caller and the wire, so no API or wire change; `NavLayerSelection.For` adds it by construction | a mask consumer that compares whole masks (none found: graph + grep) |
| R-5048 | a lane cleared at runtime is a RECORD of the deciding node (`MinefieldLanes`), never a write to the authored `Minefield` (owned by the scenario's owner — MR-11) | — |
| R-5054 | the save is a top-level `Knowledge` section, not `TerrainObjects` (KR-15: a field is an entity); strikes and lanes of all chunks are ONE entry per field; the merge applies the kind's policy and never throws on a duplicate key | a kind whose merge is not monotone (a bridge): a `case` in `PlaceKnowledgeSection.MergeInto` |
| R-5049 | a unit's back-out BREADCRUMBS (MR-5, `MineBreadcrumbs`) are written by its BRAIN node — where the MineStrike drill and the medic run — recorded, NOT replicated (no descriptor, no ownership group) | another node needing the trail (a Muscle back-out executor) ⇒ a translator |
| R-5050 | minefield composites (6 over a wood, 7 over a crater) are cut by GEOMETRY (field triangles clipped against the wood / crater volumes), not by a masked area stamp (ground is 63: all bits set) | DotRecast gaining a conditional stamp |
| R-5051 | pricing: known-mined ×200, suspected ×20, worst cell of the polygon, never closed; a lane open to the side that cleared it and its known-to sides | the user's tuning |
| R-5052 | a plough / flail clears 3 m beyond its hull's FRONT (survival 5 % / 2 %, hashed per field id, row, col); the flail's bursts never target it; a line charge bursts every 8 m from 8 m out | a flail model with its own reach; the user's survival numbers |
| R-5053 | TKB 116 carries no `MineClearing` at birth — the brain adds it (the dozer's `BladeCut` pattern) | an initial-component seam in the TKB |
| R-5055 | a bare spawn of the field type (8830) with no `Minefield` / outline stays a husk — every system ignores it; no birth default (it would invent an outline) | a non-HTTP path that can produce one |
| R-5044 | `PlacementGroup` moved 500 → 309: the animals' `Fear` took 500 on coordinator the same day; ours was terrain-only, unmerged; 500–511 are full | — |

## 3. Gates (rule 8)

| gate | command | result | delta / note |
|---|---|---|---|
| T-1 minefield + CE4624 rails, after merging `behaviors` + `coordinator` | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter "…CE4624\|Minefield\|PlaceKnowledge\|MineTrigger\|MineBreadcrumb\|DangerAreaSensor"` | 83 / 83 | built after `dotnet restore` (third stale-binary trap) |
| the catalogue rails | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build --filter "MineClearerTkbTests\|Munition\|EntityClass"` | 34 / 34 | +1 rail (the belly) |
| MS-T1 full toolkit under PAR + READ + GUARD | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1 dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` (Terrain) | 1132 / 1132 | — |
| SimHost | `dotnet test …SimHost… --no-build` | 329 / 331 | 2 red pre-existing on the base (not this batch) |
| red-proofs | one compiling mutation per new rail | all red | the worker's slices red-proved by the worker; the translators by the coordinator |
| pre-existing reds, named | `UtmEverywhereTests` (zone 32 vs 33), `ReadingARowAllocatesNothing` (flaky) | red on base `b1f83aeb0` | not this batch |

## 4. Ids
component 309 (moved), 318, 319, 323, 324, 325, 326 · DDS 191–195 · events 5105–5109 · TKB 8830, 7206, 7207, 7208, 116 · munitions 6051–6053 ·
navmesh areas 5, 6, 7 · rulings R-5042…R-5055.
