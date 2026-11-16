<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: §1 (slices), §2 (lane decisions for the user's review)
stale-below: none
related-designs:
  - docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — the design this lane builds (§23.3 agg-backend column)
  - docs/blueprints/batches/HANDOFF_Coordinator_To_AggBackend.md — the dispatch (CE-4616, at 3f33c0ee3)
  - docs/blueprints/batches/NOTE_AggBackend_To_Agg_Contract.md — the contract layouts published to agg-behaviors / agg-ui
-->

# STATUS — exp/agg-backend *(CE-4616 · tracker CE-8000…8299 · rulings R-8000…8299)*

Started at `d94af8e2f` (coordinator), marker `992d8d0c8`. Scope frozen at the dispatch `3f33c0ee3`.

## 1. Slices

| slice | state | commit | notes |
|---|---|---|---|
| contract | ✅ built | *(this commit)* | 281–284 + own 285 `UnitDetections`, 286 `RunSeed`; descriptors 220–222; routing table; NOTE |
| ST-0 ③ translators | ⏳ | | |
| ST-0 ⑤ refused ⇒ TaskFailed | ⏳ | | |
| ST-0 ⑥ run seed | ⏳ | | |
| ST-1 movement | ⏳ | | |
| ST-2 sensors | ⏳ | | |
| ST-3 combat | ⏳ (harness only until minefield MS-T1 is in coordinator) | | |
| ST-4 determinism | ⏳ | | |
| ST-5 resolution | ⏳ | | |

## 2. Lane decisions — for the user's review

| id | decision | why | what would change it |
|---|---|---|---|
| **R-8000** | ⭐ a slot is **8 B** (`Half` health, `Half` stress, `ushort` ammo, role, composite slot) so a **whole company (120) fits ONE slot table on the unit entity**; the header's `Chunk` stays 0 — no chunk entities | the parallel rule: phase B writes ONLY its own entity, and a chunk on another entity would be a cross-entity write; a 12 B float slot fits only 84; `Half` keeps 25 → 12.5 exact and stress to ~1/2000 | a leaf above company is ruled (then a second chunk COMPONENT on the same entity, never a chunk entity); or the C-8 ladder shows `Half` stress drift |
| **R-8001** | the routing table: **Stochastic with no row ⇒ refused** (never the physical `<Class><Order>` play); **Fusion with no row ⇒ the physical rule**; Physical ⇒ `OrderResolver` untouched; the SOP is the row `"Sop"` | RV-7: a memberless unit must never run a physical play; X-1: a FusionSquad plays the squad's plays | the behaviors lane needs a Fusion-specific refusal |
| **R-8002** | `FireLine.Hits` is written **already × the FIRER's suppression** (phase A owns it); phase B multiplies only by the VICTIM's cover | the firer's suppression is its own state ⇒ phase B needs no read of the firer's `AggregateUnit` (one fewer cross-entity read to declare) | — (equivalent maths) |

## 3. Gates *(per rule 8 — filled per slice)*

| gate | command | result |
|---|---|---|
| contract rails | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter AggregateContractRails` | ✅ 5 / 0 |
| routing rails | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build --filter ResolutionRoutingTableTests` | ✅ 6 / 0 (+ 4 registry) |
| S0 type rails | `--filter ~S0` (Toolkits) | 6 red, ALL pre-existing (the known S0 set + `S0WalkerTests.Resolve_AtLoad`); none names an aggregate type |

## 4. Ids allocated

`CE-8000` (contract) · `R-8000`…`R-8002` · components 285, 286 · descriptors 220–222 (contract).
