<!--STATUS
state: LIVE — in progress
updated: 2026-11-16
current-answer: §1 the slices, §2 lane decisions, §3 gates, §4 ids
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md — the design
  - HANDOFF_Coordinator_To_Terrain_Minefields.md — the dispatch (scope frozen at 8b02d1a2a)
  - NOTE_Terrain_To_All_Minefield_Contract.md — the contract in numbers (published first)
-->

# STATUS — `terrain`: minefields, the engine (CE-4624) — started at `b1f83aeb0` (merged `coordinator@50a2f828d`)

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
| R-5049 | the save is a top-level `Knowledge` section, not `TerrainObjects` (KR-15: a field is an entity); strikes and lanes of all chunks are ONE entry per field; the merge applies the kind's policy and never throws on a duplicate key | a kind whose merge is not monotone (a bridge): a `case` in `PlaceKnowledgeSection.MergeInto` |
| R-5050 | minefield composites (6 over a wood, 7 over a crater) are cut by GEOMETRY (field triangles clipped against the wood / crater volumes), not by a masked area stamp (ground is 63: all bits set) | DotRecast gaining a conditional stamp |
| R-5051 | pricing: known-mined ×200, suspected ×20, worst cell of the polygon, never closed; a lane open to the side that cleared it and its known-to sides | the user's tuning |
| R-5052 | a plough / flail clears 3 m beyond its hull's FRONT (survival 5 % / 2 %, hashed per field id, row, col); the flail's bursts never target it; a line charge bursts every 8 m from 8 m out | a flail model with its own reach; the user's survival numbers |
| R-5053 | TKB 116 carries no `MineClearing` at birth — the brain adds it (the dozer's `BladeCut` pattern) | an initial-component seam in the TKB |
| R-5044 | `PlacementGroup` moved 500 → 309: the animals' `Fear` took 500 on coordinator the same day; ours was terrain-only, unmerged; 500–511 are full | — |

## 3. Gates
*(per slice, filled as they land)*

## 4. Ids
component 309 (moved), 318, 319, 323 · DDS 191–193 · events 5105–5108 · TKB 8830, 7206, 116 · munitions 6051–6053 · rulings R-5042…R-5044.
