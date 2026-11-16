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
| CE-8015 attribute rows | ✅ built | *(CE-8015 commit)* | agg-ui's follow-up (R-4278 note): `Aggregate.Resolution / Pin / Echelon / Formation / Posture / Prepared` (ids 281–286) — component + JSON + binary + reader, the four routing tables agree; the wire joins through the existing `dtAggregateUnit` map |
| CE-8016 spawn-time fill | ✅ built | *(CE-8016 commit)* | agg-behaviors' F-9: `StochasticUnitFillSystem` (Hrot.Core, in `CgfLogicPack`) fills an authored stochastic unit's slots + holdings from its TKB composite; closes CE-8013 ① (`StochasticUnitFill.ChildType`) and the personal-weapons half of CE-8008 |
| CE-8017 bottom row | ✅ built | *(round 3)* | ST-3's TODO: the mine row against the belly (MS-T1's `ArmorBottom`, merged from coordinator `341ae8ad6`) |
| CE-8013 ST-5 wiring | 🟡 ①②③④⑥ built | *(round 3)* | manager in `CgfLogicPack`; `HeldChildParticipant` (EntityCreationPack, per-entity ELM requirement, unbounded); ammo both ways; UNOBSERVED; lifecycle teardown — ⏳ ⑤ the planners' posts (agg-behaviors'), ⑦ CE-8005 |
| CE-8005 route half | ✅ built | *(round 3)* | a navmesh-less node asks the solver over the path-request topic (5 s, then local); the Brain-only CGF node composes the pair with its pool — ⏳ the position-ownership half |
| CE-8018 N-3 | ✅ built | *(round 3)* | a stochastic observer sees physical bodies (one way); `UnitSensingRails.N3_…` 47 / 0 plain, 37 / 0 under the parallel checks; red-proofs: bodies not gathered · `Health` undeclared (read check) — 2 / 2 red |
| CE-8019 4× lethality | ✅ fixed | *(round 3)* | found by the C-8 ladder's first stochastic run; red-proved |
| CE-8009 C-8 ladder rung 1 | ✅ rung 1 PASS — ⚠ 200 / 300 m only vacuously (CE-8020) | *(round 3)* | physical rungs `scenarios/tests/c8-ladder/c8-open-{100,200,300}` (`scripts/gen-c8-ladder.py`), 30 seeds each via `scripts/c8-ladder.py physical` (90 real-time runs, 3 at a time, ~1 h); stochastic side `C8LadderStochasticTests` (HROT_C8_OUT); verdict `scripts/c8-ladder.py compare` — table §5. ⭐ First compare NOT MET; diagnosed by a per-step trace and calibrated (R-8005, R-8006); rungs 2–4 (MG, building, IFV / ATGM) not built |
| ST-5 resolution | 🟡 built (engine), not wired → see CE-8013 above | *(ST-5 commit)* | `CE-8012` the manager, the process stages, `BusResolutionSwitch` (derived identities, held children, X-4 both ways), pins, C-7, hysteresis; ⛔ `CE-8013` production wiring (the TKB composite resolver, the held lifecycle participant, ammunition, UNOBSERVED, the planners' posts) |

## 2. Lane decisions — ✅ ALL APPROVED by the user `2026-11-16` (R-4278, [NOTE_Coordinator_To_All_Lanes_Decisions_Approved.md](NOTE_Coordinator_To_All_Lanes_Decisions_Approved.md))

| id | decision | why | what would change it |
|---|---|---|---|
| **R-8000** ✅ approved | ⭐ a slot is **8 B** (`Half` health, `Half` stress, `ushort` ammo, role, composite slot) so a **whole company (120) fits ONE slot table on the unit entity**; the header's `Chunk` stays 0 — no chunk entities | the parallel rule: phase B writes ONLY its own entity, and a chunk on another entity would be a cross-entity write; a 12 B float slot fits only 84; `Half` keeps 25 → 12.5 exact and stress to ~1/2000 | a leaf above company is ruled (then a second chunk COMPONENT on the same entity, never a chunk entity); or the C-8 ladder shows `Half` stress drift |
| **R-8001** ✅ approved | the routing table: **Stochastic with no row ⇒ refused** (never the physical `<Class><Order>` play); **Fusion with no row ⇒ the physical rule**; Physical ⇒ `OrderResolver` untouched; the SOP is the row `"Sop"` | RV-7: a memberless unit must never run a physical play; X-1: a FusionSquad plays the squad's plays | the behaviors lane needs a Fusion-specific refusal |
| **R-8003** ✅ approved | the run seed is a **process-wide** `SimRng.RunSeed` (env `HROT_RUN_SEED`, synced with the recorded `RunSeed` singleton), mixed into `FromSim` only when non-zero | ⭐ 0 keeps every existing number (no golden / scenario-check churn); `FromSim` is static with ~30 callers and no world in reach — a per-world seed would change every call site; every world of one process runs one scenario | two scenarios in one process needing different seeds |
| **R-8004** ✅ approved | a stochastic unit's ammunition CAPACITY is DERIVED from the TKB (each mount's `InitialAmmunition`), not a recorded field (agg-behaviors' F-1) | the recorded layout stays (SV-12); the same numbers as a spawned physical soldier; no second source of a basic load | a per-unit loadout that differs from its TKB types' (then a capacity column in a NEW component, never in the slot) |
| **R-8005** 🔵 for the user's review | ⭐ C-3's hit placement, CALIBRATED by the C-8 ladder: a share of the hits (`FocusShare` 0.3) lands on the man ALREADY under fire; a BROKEN man (stress ≥ the break level) has gone to ground — the unit's incoming λ × the mean of 1 (standing) / `BrokenExposure` 0.5 (down) — and still fires at `BrokenFireShare` 0.6 | 📐 measured, a per-step trace: with hits spread by exposure and broken men silent, both sides took ~3 hits / s for 10 s and NOBODY fell (the mean damage, 6.4 of 25, spread evenly), then the first side to break stopped firing and was wiped out without loss to the winner (winner 0.2 down vs the physical 3.3; LER 7.8 vs 1.9). Each constant is load-bearing, red-proved: no focus ⇒ first loss 9.4 s (physical 5.1); broken men silent ⇒ winner 0.8 down | a physical measurement of who shoots whom (a target-selection trace) — the focus share is a stand-in for it; rung 2+ (the MG) disagreeing |
| **R-8006** 🔵 for the user's review | the stochastic NAKED EYE sees no further than the soldiers' own TKB `VisionRange` (the personal soldiers', the largest — `KillerVictimTkb.SoldierEyeReach`); the R50 curve applies inside it; optics / thermal / radar holdings are not capped | ⭐ one source: the physical soldier's `VisualPerception` stops at that range (150 m for 2002), so the stochastic one now does too; before, its eye R50 of 400 m fought at 200 / 300 m where the physical squads never saw each other (0 losses in 60 runs) | the TKB soldier's open-ground vision corrected (CE-8020) — both follow it then |
| **R-8007** 🔵 for the user's review | a stochastic unit's POSITION stays with its BRAIN (the unit's owner), its route from the solver over the path-request topic; a BURST on it is applied by that owner (`StochasticAreaEffectSystem`, from the replicated detonation) — never by the Muscle | 📐 the Muscle group applies only to templates with vehicle / flight / weapon-system parameters (`HrotOwnershipGroups.cs:191`); a unit template has none, and a physical squad's commander is placed by its owner (`UnitCentroidSystem`) — the same rule for both resolutions; R-171 (damage where the health is) | a unit template given kinematics (then the Muscle would own both resolutions' unit positions, together) |
| **R-8008** ✅ user `2026-11-16` | the FusionSquad is NOT shown anywhere in the UI; `AggregateResolution.Fusion = 2` stays in the recorded / wire enum (never requested, never drawn) | 🔒 user: *"we are not simulating Fusion Squad because it was dropped … we do not need to mention it anywhere in the UI"* (the design deferred it, R-4253) | the FusionSquad revived |
| **R-8002** ✅ approved | `FireLine.Hits` is written **already × the FIRER's suppression** (phase A owns it); phase B multiplies only by the VICTIM's cover | the firer's suppression is its own state ⇒ phase B needs no read of the firer's `AggregateUnit` (one fewer cross-entity read to declare) | — (equivalent maths) |

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
| CE-8015 gates | `Hrot.SimHost.Tests --filter TheFourRoutingTablesAgree\|Attribute\|AggregateUnitReplication\|CgfLogicPack\|TacticalIntent` · `Hrot.Network.NED.Tests` (all) · `Fdp.Toolkits.Tests --filter ~Aggregate\|~Attribute\|~Replication` | ✅ 330 / 0 · ✅ 198 / 0 · 305 / 6 — the 6 are the pre-existing S0 set (row below), none an aggregate type |
| CE-8016 gates | `Hrot.Core.Tests --filter StochasticUnitFillTests` · `Hrot.SimHost.Tests --filter …CgfLogicPack…` (44 / 52) | ✅ 4 / 0 · ✅ 360 / 0 (with CE-8015's row set) |
| CE-8015 / CE-8016 red-proofs | the `Pin` reader reads `Resolution` (→ `WhatTheJsonSetterWritesTheReaderReadsBack(Aggregate.Pin)`) · `CompositeSlot` off by one · the fill not once | ✅ 3 / 3 red; clean rebuild after |
| round 3 gates (after the coordinator merge `215b27feb`, test projects RESTORED) | `Fdp.Toolkits.Tests --filter ~Aggregate\|~Lifecycle\|~NetworkGateway` · `Hrot.SimHost.Tests --filter TheDescriptorMapIsWired\|CgfLogicPackTests\|EntityCreationPackRails\|TheFourRoutingTablesAgree\|AggregateUnitReplication\|TacticalIntent\|AttributeCompilerFactoryTests\|TheBinaryApplyTellsSmartEgress\|TheJsonAndBinaryPathsAgree\|TheUnknownKeyIsWarned` · `Hrot.Core.Tests --filter StochasticUnitFill\|ResolutionRouting` | ✅ 90 / 0 · ✅ 435 / 0 · ✅ 10 / 0 |
| ⚠ reds found by round 3 | ① `TheEntityKeyedSetIsExactlyTheProtocolsEntityKeyedRows` (NED): the minefield wire types (`EntityMineClearing`, `EntityMinefield`, `EntityMinefieldKnowledge` …) have no row in `NED_DDS_Protocol.md` — ⚠ **red on coordinator `341ae8ad6` too** (its doc has 0 such rows), the minefield lane's; not edited here · ② `CgfLogicPackTests` pinned 39 / 47 on coordinator while its pack has 42 (`MineStrikeReflexSystem`, `PlaceKnowledgeSystem`, `MineBreadcrumbSystem`) — re-pinned to the measured 48 / 56 here (cross-lane pin, said) · ③ mine: `EveryComponentTheVocabularyWritesIsCoveredByATranslator` — CE-8015's rows lacked the test's production mirror pairing (`FakeDescriptorTranslator`: dtAggregateUnit → AggregateUnit); production declared it already | ⚠ CE-8015's gate MISSED ③ (its filter did not include `TheDescriptorMapIsWired`) |
| round 3 red-proofs (compiling mutants; two first tries did not compile and were redone) | the participant acking a held child at once · no abandon · UNOBSERVED ignored · the launcher not scaled · the request not stamped with the node | ✅ 5 / 5 red |
| round 3 gates, final | `Fdp.Toolkits.Tests --filter ~Aggregate\|~Lifecycle\|~NetworkGateway\|~Pathfinding` · `Hrot.SimHost.Tests --filter TheDescriptorMapIsWired\|CgfLogicPackTests\|EntityCreationPackRails\|TheFourRoutingTablesAgree\|AggregateUnitReplication\|TacticalIntent\|NodeBootstrapper` · builds `Hrot.CGF`, `Hrot.Editor` | ✅ 144 / 0 · ✅ 427 / 0 · ✅ |
| CE-8009 calibration gates | `Fdp.Toolkits.Tests --filter ~Aggregate\|~Lifecycle` · the same `~Aggregate` under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` · `Hrot.Core.Tests --filter C8LadderStochasticTests\|StochasticUnitFillTests` | ✅ 84 / 0 · ✅ 37 / 0 · ✅ 5 / 0. Red-proofs (compiling mutants): `BrokenFireShare` 0 ⇒ "the winner loses men too (mean 0.80)" red · the eye cap never fires ⇒ 200 m `Expected 0, Actual 12.17` red · `FocusShare` 0 ⇒ "the first loss comes early (mean 9.4 s)" red |
| CE-8021 (F-11) gate | `Hrot.Core.Tests --filter StochasticUnitFillTests\|C8LadderStochasticTests` | ✅ 5 / 0; red-proof: the pool never added ⇒ red |
| ⭐ T2 (once, at the end) | `Fdp.Toolkits.Tests --filter ~Aggregate\|~Combat\|~Squad\|~Navigation.RoutePlanner\|~Replay` · `Hrot.Core.Tests` (all) · `Hrot.Network.NED.Tests` (all) · `Hrot.SimHost.Tests --filter` the eight touched feature suites | ✅ 1156 / 0 (1 skip) · ✅ 380 / 0 · ✅ 198 / 0 after the fix below · ✅ 102 / 0 (1 skip) |
| ⛔ a gate MISS, fixed | `TheEntityKeyedWireTypesAreReadableByReflectionTests` was red from the ST-0 commit (`d45dfa77a`) to the last: the three new entity-keyed wire types had no row in `docs/reference/NED_DDS_Protocol.md` — ST-0 gated NED on `RoleGroupOwnershipStrategyTests` only. Rows added | ⚠ the lesson: a new wire type ⇒ run the WHOLE `Hrot.Network.NED.Tests` (4 s) |
| ⚠ pre-existing red fixed | `CgfLogicPackTests` pinned 37 / 45 while the pack had 39 / 47 (the animals lane's `HerdSystem`, `AnimalAreaSystem`) — now 40 / 48 with the follower | cross-lane edit of a pin, said |
| S0 type rails | `--filter ~S0` (Toolkits) | 6 red, ALL pre-existing (the known S0 set + `S0WalkerTests.Resolve_AtLoad`); none names an aggregate type |

## 4. Ids allocated

`CE-8000` … `CE-8022` (Area AGG-B in the tracker) · `R-8000`…`R-8008` · components 281–284 (contract) + 285 `UnitDetections`, 286 `RunSeed`, 287 `UnitRoute`, 288 `AggregateChild` (289 free) · descriptors 220–222 (contract; 223–229 free).

## 5. C-8 ladder, rung 1 — the verdict *(CE-8009, `2026-11-16`, after R-8005 / R-8006)*

| rung | metric | physical mean [95 % CI] (n) | stochastic mean (n) | band (CI ± 15 %) | verdict |
|---|---|---|---|---|---|
| c8-open-100 | total down | 11.87 [11.30, 12.44] (30) | 12.37 (30) | [9.60, 14.30] | ✅ |
| c8-open-100 | loss-exchange ratio | 1.94 [1.27, 2.61] (30) | 1.92 (30) | [1.08, 3.00] | ✅ |
| c8-open-100 | time to 50 % (s) | 22.70 [16.39, 29.01] (30) | 23.23 (30) | [13.93, 33.36] | ✅ |
| c8-open-100 | first loss (s) — informative | 5.12 [4.77, 5.46] (30) | 4.27 (30) | — | ℹ |
| c8-open-200 | total down | 0.00 [0.00, 0.00] (30) | 0.00 (30) | [0.00, 0.00] | ✅ |
| c8-open-200 | loss-exchange ratio | 1.00 [1.00, 1.00] (30) | 1.00 (30) | [0.85, 1.15] | ✅ |
| c8-open-200 | time to 50 % (s) | 120.00 [120.00, 120.00] (30) | 120.00 (30) | [102.00, 138.00] | ✅ |
| c8-open-300 | total down | 0.00 [0.00, 0.00] (30) | 0.00 (30) | [0.00, 0.00] | ✅ |
| c8-open-300 | loss-exchange ratio | 1.00 [1.00, 1.00] (30) | 1.00 (30) | [0.85, 1.15] | ✅ |
| c8-open-300 | time to 50 % (s) | 120.00 [120.00, 120.00] (30) | 120.00 (30) | [102.00, 138.00] | ✅ |

C-8 rung 1: PASS

⚠ **200 / 300 m pass VACUOUSLY** — neither resolution sees past the soldier's 150 m eye, so nobody fights (CE-8020). ⭐ The first compare (before
R-8005 / R-8006) was NOT MET: 100 m LER 7.84 vs 1.94, total 9.20 vs 11.87; 200 / 300 m 9.17 men down vs 0. Physical data: `artifacts/c8-ladder/round3`
(90 reports, not committed — regenerate with `scripts/c8-ladder.py physical --seeds 30 --out <dir>`).
