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
| contract | ✅ built | `1a070da53` | 281–284 + own 285 `UnitDetections`, 286 `RunSeed`; descriptors 220–222; routing table; NOTE |
| ST-0 ③ translators + recording | ✅ built | *(ST-0 commit)* | `CE-8001`; three pairs in `SharedTranslatorPack`, Brain group |
| ST-0 ④ routing wired | ✅ built | *(ST-0 commit)* | `TacticalIntentResolutionSystem` resolves through `ResolutionRoutingTable` with the unit's resolution |
| ST-0 ⑤ refused ⇒ TaskFailed | ✅ built | *(ST-0 commit)* | `CE-8002`, generic (every unit) |
| ST-0 ⑥ run seed | ✅ built | *(ST-0 commit)* | `CE-8003`, R-8003 |
| ST-1 movement | ✅ built | *(ST-1 commit)* | `CE-8004`; follower in `CgfLogicPack`; ⚠ `CE-8005` split-cluster navmesh (finding) |
| ST-2 sensors | ✅ built | *(ST-2 commit)* | `CE-8006`; `StochasticUnitSensorSystem` a per-entity phase in `CgfLogicPack`; the squad merge skips stochastic units; ⚠ N-3 (a stochastic observer seeing PHYSICAL entities) not yet — stochastic targets only |
| ST-3 combat | 🟡 built, done-when NOT met | *(ST-3 commit)* | `CE-8007` the table + generator harness (four facings; the bottom a named TODO — MS-T1 not in coordinator) and the two phases; 🟡 `CE-8008` the table now loads from the world's TKB for the held systems (personal weapons await CE-8013); ⛔ `CE-8009` the C-8 ladder needs the physical runs; ✅ `CE-8010` the `AreaEffectSystem` sink (C-6) |
| ST-4 determinism | ✅ built (engine half) | *(ST-4 commit)* | `CE-8011` battalion vs battalion the same at 1 / 4 / 8 threads; the phases made coarse jobs (its red-proof found 18 units ran as one batch); ⏳ `stepped-hash.py` on the scenario when agg-behaviors ships it |
| ST-5 resolution | 🟡 built (engine), not wired | *(ST-5 commit)* | `CE-8012` the manager, the process stages, `BusResolutionSwitch` (derived identities, held children, X-4 both ways), pins, C-7, hysteresis; ⛔ `CE-8013` production wiring (the TKB composite resolver, the held lifecycle participant, ammunition, UNOBSERVED, the planners' posts) |

## 2. Lane decisions — for the user's review

| id | decision | why | what would change it |
|---|---|---|---|
| **R-8000** | ⭐ a slot is **8 B** (`Half` health, `Half` stress, `ushort` ammo, role, composite slot) so a **whole company (120) fits ONE slot table on the unit entity**; the header's `Chunk` stays 0 — no chunk entities | the parallel rule: phase B writes ONLY its own entity, and a chunk on another entity would be a cross-entity write; a 12 B float slot fits only 84; `Half` keeps 25 → 12.5 exact and stress to ~1/2000 | a leaf above company is ruled (then a second chunk COMPONENT on the same entity, never a chunk entity); or the C-8 ladder shows `Half` stress drift |
| **R-8001** | the routing table: **Stochastic with no row ⇒ refused** (never the physical `<Class><Order>` play); **Fusion with no row ⇒ the physical rule**; Physical ⇒ `OrderResolver` untouched; the SOP is the row `"Sop"` | RV-7: a memberless unit must never run a physical play; X-1: a FusionSquad plays the squad's plays | the behaviors lane needs a Fusion-specific refusal |
| **R-8003** | the run seed is a **process-wide** `SimRng.RunSeed` (env `HROT_RUN_SEED`, synced with the recorded `RunSeed` singleton), mixed into `FromSim` only when non-zero | ⭐ 0 keeps every existing number (no golden / scenario-check churn); `FromSim` is static with ~30 callers and no world in reach — a per-world seed would change every call site; every world of one process runs one scenario | two scenarios in one process needing different seeds |
| **R-8004** | a stochastic unit's ammunition CAPACITY is DERIVED from the TKB (each mount's `InitialAmmunition`), not a recorded field (agg-behaviors' F-1) | the recorded layout stays (SV-12); the same numbers as a spawned physical soldier; no second source of a basic load | a per-unit loadout that differs from its TKB types' (then a capacity column in a NEW component, never in the slot) |
| **R-8002** | `FireLine.Hits` is written **already × the FIRER's suppression** (phase A owns it); phase B multiplies only by the VICTIM's cover | the firer's suppression is its own state ⇒ phase B needs no read of the firer's `AggregateUnit` (one fewer cross-entity read to declare) | — (equivalent maths) |

## 3. Gates *(per rule 8 — filled per slice)*

| gate | command | result |
|---|---|---|
| contract rails | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter AggregateContractRails` | ✅ 5 / 0 |
| routing rails | `dotnet test Hrot/Engine/Hrot.Core.Tests --no-build --filter ResolutionRoutingTableTests` | ✅ 6 / 0 (+ 4 registry) |
| ST-0 ③ replication + recording | `Fdp.Toolkits.Tests --filter ~Aggregate` · `Hrot.SimHost.Tests --filter AggregateUnitReplicationTests` | ✅ 6 / 0 · ✅ 3 / 0 |
| ST-0 ③ wiring | `Hrot.Core.Tests` (routing + registry + ownership groups) · `Hrot.Network.NED.Tests --filter RoleGroupOwnershipStrategyTests` · `Hrot.SimHost.Tests --filter TheDescriptorMapIsWiredTests` | ✅ 20 / 0 · ✅ 8 / 0 · ✅ (in the 78 below) |
| ST-0 ④⑤⑥ feature suites | `Hrot.SimHost.Tests --filter TacticalIntentResolutionSystemTests\|SimRngRails\|TheDescriptorMapIsWiredTests\|BodyPartReplicationTests\|AggregateUnitReplicationTests` | ✅ 78 / 0 |
| red-proofs (compiling mutants, each reddened its rail) | ReportRefusal skipped · `ResolutionOf` → Physical · the run-seed mix skipped · slot egress drops Stress · holdings ingress loses the type · (ST-1) the follower ignores the segment factor · the recording side drops the slot table · (ST-2) the curve not squared · the level replaced not accumulated · the merge guard removed · the stable reads undeclared (under `HROT_READ_CHECK=1`) · (ST-3) phase B ignores the enemy lines · cover ignored · phase A stable reads undeclared (read check) · the generator ignores the zone factor · (C-6) the overlap taken as 0 · (ST-4) phase B seeded by the thread (red only once the phases became coarse jobs) · (ST-5) the handover not mapping health · no hysteresis margin (red only once the rail checked every step) · (CE-8008) the rate ignoring the reload · (CE-8014) the load summed over down slots | ✅ 21 / 21 red |
| ST-1 rails | `Fdp.Toolkits.Tests --filter ~Aggregate` (incl. `UnitMovementRails`: arrival within ± 10 % of M-2's prediction, 0 B steady, a lost route re-planned) · `Hrot.SimHost.Tests --filter CgfLogicPackTests\|SimHostComponentRegistration` | ✅ 10 / 0 · ✅ 24 / 0 (1 skip, pre-existing) |
| ST-2 rails | `Fdp.Toolkits.Tests --filter ~Aggregate`, plain AND with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` (serial = parallel at 4 / 8 threads) · `--filter SquadPerceptionMerge\|SquadCoordinationSystemTests` · `Hrot.SimHost.Tests --filter CgfLogicPackTests` | ✅ 15 / 0 · ✅ 15 / 0 · ✅ 32 / 0 · ✅ 18 / 0 |
| ST-3 rails | `Fdp.Toolkits.Tests --filter ~Aggregate`, plain AND with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` (incl. `StochasticCombatRails`: the table = HitModel / ArmorModel numbers, a duel trades losses, cover over 30 seeds, serial = parallel at 4 / 8, 0 B steady) · `Hrot.SimHost.Tests --filter CgfLogicPackTests` (43 / 51) | ✅ 20 / 0 · ✅ 20 / 0 · ✅ 18 / 0 |
| C-6 + ST-4 rails | `Fdp.Toolkits.Tests --filter ~Aggregate`, plain AND under the parallel checks · `--filter ~AreaEffect` (T-1, the touched feature) | ✅ 23 / 0 · ✅ 23 / 0 · ✅ 51 / 0 |
| ST-5 rails | `Fdp.Toolkits.Tests --filter ~Aggregate`, plain AND under the parallel checks (incl. `UnitResolutionRails`) | ✅ 27 / 0 · ✅ 27 / 0 |
| ⭐ T2 (once, at the end) | `Fdp.Toolkits.Tests --filter ~Aggregate\|~Combat\|~Squad\|~Navigation.RoutePlanner\|~Replay` · `Hrot.Core.Tests` (all) · `Hrot.Network.NED.Tests` (all) · `Hrot.SimHost.Tests --filter` the eight touched feature suites | ✅ 1156 / 0 (1 skip) · ✅ 380 / 0 · ✅ 198 / 0 after the fix below · ✅ 102 / 0 (1 skip) |
| ⛔ a gate MISS, fixed | `TheEntityKeyedWireTypesAreReadableByReflectionTests` was red from the ST-0 commit (`d45dfa77a`) to the last: the three new entity-keyed wire types had no row in `docs/reference/NED_DDS_Protocol.md` — ST-0 gated NED on `RoleGroupOwnershipStrategyTests` only. Rows added | ⚠ the lesson: a new wire type ⇒ run the WHOLE `Hrot.Network.NED.Tests` (4 s) |
| ⚠ pre-existing red fixed | `CgfLogicPackTests` pinned 37 / 45 while the pack had 39 / 47 (the animals lane's `HerdSystem`, `AnimalAreaSystem`) — now 40 / 48 with the follower | cross-lane edit of a pin, said |
| S0 type rails | `--filter ~S0` (Toolkits) | 6 red, ALL pre-existing (the known S0 set + `S0WalkerTests.Resolve_AtLoad`); none names an aggregate type |

## 4. Ids allocated

`CE-8000` … `CE-8014` (Area AGG-B in the tracker) · `R-8000`…`R-8004` · components 281–284 (contract) + 285 `UnitDetections`, 286 `RunSeed`, 287 `UnitRoute`, 288 `AggregateChild` (289 free) · descriptors 220–222 (contract; 223–229 free).
