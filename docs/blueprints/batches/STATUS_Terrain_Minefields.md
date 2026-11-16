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
| MS-T1 field, trigger, bursts, armour bottom | 🔨 building (worker: trigger / bursts / bottom / TKB; me: the strikes record through the shared writer) | |
| MS-T2 place-knowledge core | 📋 | |
| MS-T3 routing by side | 📋 | |
| MS-T4 lanes and clearing | 📋 | |

## 2. Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-5042 | the mine LATTICE is anchored at the field entity's position (not the outline bounds) — a (row, col) survives an outline edit; a MIXED field alternates rows (AP even, AT odd) | a doctrine source wanting another mixed layout |
| R-5043 | `DangerAreaKinds` widened to `ushort` (MR-9) but `Shelled` KEPT on bit 0 (MR-9 said drop the hack): no recorded / saved value changes | a reason to renumber (none found) |
| R-5044 | `PlacementGroup` moved 500 → 309: the animals' `Fear` took 500 on coordinator the same day; ours was terrain-only, unmerged; 500–511 are full | — |

## 3. Gates
*(per slice, filled as they land)*

## 4. Ids
component 309 (moved), 318, 319, 323 · DDS 191–193 · events 5105–5108 · TKB 8830, 7206, 116 · munitions 6051–6053 · rulings R-5042…R-5044.
