<!--STATUS
state: LIVE
updated: 2026-11-14
current-answer: §1 decisions (for the user's review), §2 tasks, §3 gates
related-designs:
  - ../PLAN_Dynamic_Terrain.md — the programme
  - RESUME_Terrain.md — where the next session starts
  - batches/HANDOFF_Coordinator_To_Terrain.md — the dispatch (CE-4604)
-->

# STATUS — `terrain` lane (dynamic terrain, CE-4604)

## 1. Decisions taken — for the user's review

| ruling | decision | what would change it |
|---|---|---|
| [R-5000](RULINGS.md) | the hull band = the probe height: any wall panel whose top is above a hull's wheels meets it (a low garden wall is met, never passed over) | vehicles should roll over very low edging ⇒ a minimum panel height |
| [R-5001](RULINGS.md) | a free-wall breach opens for a hull only when open from the wall's base; no width test per trace (B-3 sizes the cut) | narrow blast gaps hulls try to squeeze through ⇒ a width test |
| [R-5002](RULINGS.md) | the break gate reads the hull's own `PossessedBy` (Fps) on the Manual branch, outside a replay | a vehicle driven without a possession of the hull |
| [R-5003](RULINGS.md) | the bake's latency cap is 2 s | back-to-back bakes cost too much frame time ⇒ a longer cap |
| [R-5004](RULINGS.md) | avoidance never steers a hull into a wall (swerve checked ~1 s ahead; dropped if it meets the guard) | — |
| [R-5005](RULINGS.md) | a vehicle swerves for a man DOWN only when he lies in its hull's way | a wider berth from casualties wanted ⇒ a larger margin |
| [R-5006](RULINGS.md) | a free-wall line is a wall group after the buildings; `BuildingDamage.Building` holds the group — record and wire NOT widened | a need to tell a line from a building on the wire without the terrain |
| [R-5007](RULINGS.md) | a line's key comes from its content (`wall:<label>`, `#n`, else its first post), not a label written at import | an import that re-orders / re-splits lines between versions |
| [R-5008](RULINGS.md) | V-12 (one cut across panels) moves to B-3 | — |
| [R-5009](RULINGS.md) | overlapping pad skirts combine by the minimum over the pads, each against the natural ground (G-3 said a weighted blend) | stepped terraces between close houses |
| [R-5010](RULINGS.md) | no bridge / tunnel data in the net ⇒ the 6 m corridor cap is the bridge guard (netconvert unchanged) | a shallow valley under a real bridge filled in |
| [R-5011](RULINGS.md) | overlapping corridors: the nearest road decides, a junction over all | seams between parallel carriageways |
| [R-5013](RULINGS.md) | road blockage judged in each net's own geometry (no SUMO edge ids in `roads.json`) | a UI naming one road in both nets |
| [R-5014](RULINGS.md) | a road is cut only where a blocker spans its full width; obstacles go to SUMO as square chains at rest, negative ids | SUMO cars squeezing past a barrier |
| [R-5015](RULINGS.md) | `contact-breach.json` starting values (tank ≤ 2000 + building bump, IFV ≤ 150, ram 1.5 m/s, push 2.5 s, cut ≥ 4.2 m) | a playtest |
| [R-5016](RULINGS.md) | push / grace / cue state is per-hull process state, not recorded | a break decided across a save mid-push |
| [R-5017](RULINGS.md) | V-17 feedback split into B-3b | — |
| [R-5018](RULINGS.md) | a scenario house's pad is applied at overlay merge (`TerrainWorldMerge.PadHeights`), against the base ground; *Save terrain as* writes the padded store and re-elevates the net | pads wanted only at save, not live ⇒ move it to `SaveAs` |
| [R-5019](RULINGS.md) | `craters.json` starting values (radius 0.8 / depth 0.35 / rim 0.08 m per ∛kg; none below 0.5 kg or for an air burst; large from 3 m; cover after 30 / 60 s) | a playtest |
| [R-5020](RULINGS.md) | the crater "index" is the view's table on `DoorStates` (not a world singleton — R-219); every world now has live state | a caller that needs craters without a view |
| [R-5021](RULINGS.md) | a crater on a crater does not deepen it (overlaps combine by the minimum) | a barrage that must dig deeper |
| [R-5022](RULINGS.md) | `trees.json`: a blast fells lone trunks within 1.2 m per ∛kg, from 2 kg | a playtest |
| [R-5023](RULINGS.md) | a trunk is felled by a contact row's `trees` flag (tank, IFV), not its strength | trunks only a tank should fell |
| [R-5024](RULINGS.md) | the dozer's cut is a Muscle system reading its marker and blade position, not the hull guard's contact | a dozer that must break what it drives into |
| [R-5025](RULINGS.md) | a zero `At` is "no point" (the DTO's zero-default rule) | a flag field instead |
| [R-5026](RULINGS.md) | a ground shape uses G-14's field mapping (mode / value / draw order in `Seed`, skirt derived); no sibling component until P-6 | a shape needing its own skirt or heading |
| [R-5027](RULINGS.md) | ditches are presets on the ground shape (ditch 2×1, AT 4×2, foxhole 2×1.5×1); width the preset's; every ditch an obstacle area | a ditch width the presets lack |
| [R-5012](RULINGS.md) | D-3's road-to-façade ramp is the two skirts meeting; D-4 and V-18 left to their own steps | a step between a road and a house on a real town |

## 2. Tasks

| task | state | evidence |
|---|---|---|
| **B-1** CE-4598 — the hull sees every wall | ✅ built | [Breaching §7.8](../DESIGN_Vehicle_Breaching.md); rails `CarKinematicsSystemTests.CE4598_*`, `BuildingBreachTests.Hull_*`; red-proved |
| **CE-6014** (showcase-world's row, cross-lane) — vehicles swerve through walls round a man down | ✅ fixed (B-1 exposed it: `RoadblockBlastScenarioTests` ④ went red) | rails `CE6014_*`; see the tracker row |
| **P-0** CE-4596 — cheap terrain change on every node | ✅ built (no runtime caller of the height patch yet — P-1/P-3/P-4) | [Ground §9.6](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `HeightStoreTests.CE4596_*`, `StaticObstacleBakeTests.CE4596_*` |
| **B-2** CE-4599 — one breach record for every wall | ✅ built | [Breaching §7.9](../DESIGN_Vehicle_Breaching.md); rails `CE4599_*` in five suites; red-proved (the old building-only blast grid ⇒ the garden-wall charge rail red) |
| **P-1** CE-4403 part 1 — house pads at import | ✅ built | [Ground §9.7](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `ImporterTests.CE4403_*` (red-proved), `HeightStoreTests.CE4403_*`; importer suite 50/0 |
| **P-1** CE-4403 part 2 — road corridors at import + the net re-elevated | 🟡 built; ⛔ open before DONE: V-15 (bridge data), D-4 (versioned re-import), CE-4402 (needs the scale terrains re-imported) | [Ground §9.8](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `ImporterTests.CE4403_*` (red-proved); importer suite 52/0; InputSet suite 6/0 |
| **P-2** CE-4590 — road blockage: SUMO + our road graph | ✅ built | [Ground §9.9](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `BridgeClosureTests.CE4590_*` (red-proved); traffic suite 85/0; scenarios 2/0 |
| **B-3** CE-4600 — a player's vehicle breaks barriers | 🟡 core built; ⛔ B-3b (dust, crash sound, HUD cue + mockup), barrage numbers (with P-4) | [Breaching §7.10](../DESIGN_Vehicle_Breaching.md); rails `CE4600_*` (red-proved); kinematics 110/0 under the parallel checks |
| **P-3** CE-4597 — scenario house pads + *Save terrain as* | ✅ built | [Ground §9.10](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `TerrainWorldMergeTests.CE4597_*`, `TerrainBakeTests.CE4597_*` (red-proved) |
| **P-4** CE-4591 — craters, parts 1–2 + the wire | 🟡 built: the record, the rule, the cratered ground the same tick (D-1), the bake cache on every node (V-6), the dirty discs (V-4), nav areas closed to vehicles (D-2), the road cut (G-10), `dtGroundDamage` 184; + part 3: saved in `TerrainObjects["#ground"]` (merge, load), the rim as cover after the gases (G-35, live from the view); + re-grounding of owned entities (V-8), `RoadHeights` over a crater (V-20); ⛔ open: the decal (V-11, ui), a vehicle stranded in a new crater (V-12), stepped-hash | [Ground §9.11](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `CraterTests`, `CraterBakeTests`, `EntityGroundDamageTranslatorTests`, `BridgeClosureTests.CE4591_*` |
| **B-6** CE-4603 — lone trees felled | ✅ built | [Breaching §7.11](../DESIGN_Vehicle_Breaching.md); rails `TreeFellingTests`, `TreeFellingBakeTests` (red-proved); debt CE-5000 (one record system, not three) |
| **B-4** CE-4601 — the bulldozer clears at a point | ✅ built (cross-lane into behaviors' `ClearObstacleBehavior`, said) | [Breaching §7.12](../DESIGN_Vehicle_Breaching.md); rails `ClearObstacleBehaviorTests.CE4601_*` (red-proved), `BladeCutWireTests`; SimHost 42 / 0 under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1` |
| **B-5** CE-4602 — every breach is a way in for the AI | ✅ built | [Breaching §7.13](../DESIGN_Vehicle_Breaching.md); rail `BreachEntryTests` (red-proved) |
| **P-5** CE-4592 part 1 — ground shapes, the data path | 🟡 built: kind Ground, compile, parse, `GroundShapes.Apply` at merge (islands, newest wins); ⛔ open: corridors (G-24), the overlay SUMO net (G-16), grade read-out (G-19), the UI + mockup | [Ground §9.12](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `TerrainWorldMergeTests.CE4592_*` (red-proved) |
| **P-5** CE-4592 part 2a — the Earthworks picks | ✅ built (cross-lane into ui's picker, said) | [Ground §9.13](../DESIGN_Ground_Patches_And_Road_Blockage.md); rail `TerrainAuthoringPickerTests.CE4592_*`; `EntityTypeCatalogTests.CE2138_*` red is pre-existing (the drone-jammer type `e81eaa50`, behaviors) |
| **P-6** CE-4593 part 1 — ditches and foxholes | 🟡 built: presets, steep banks, obstacle areas, picks; ⛔ open: cover points (G-27/G-36), road corridors (G-29), coarse-grid warning, Details | [Ground §9.14](../DESIGN_Ground_Patches_And_Road_Blockage.md); rail `TerrainWorldMergeTests.CE4593_*` (red-proved) |
| next | the open parts, in order: P-6 cover points · P-5 part 2b (Details, grade, tint, fidelity) · P-4 decal (ui) · P-1 V-15/D-4/CE-4402 · B-3b · stepped-hash · CE-5000 | |

## 3. Gates

| gate | command | result |
|---|---|---|
| kinematics suite (T-1) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter CarKinematicsSystemTests` | 102 / 0 failed |
| breach + height + B-1 rails | `… --filter HeightStoreTests\|GroundSightPyramidTests\|CE4598\|Hull_` | 32 / 0 |
| scenarios | `Hrot.ClusterRunner.Integration.Tests --filter RoadblockBlast\|WreckBlocksStreet` | 2 / 0 — the IFV now drives through the GATE (it used to cross the concrete 7 m off it) |
| bake suites | `Hrot.SimHost.Tests --filter StaticObstacleBakeTests\|BuildingDamageBakeTests\|LiveOverlayBakeTests` | 28 / 0 (`TenPassagesInsideOneDebounce_GiveOneBake` re-timed to 1.8 s: it asserted the starvation V-1 removes) |
| ledger | `python3 scripts/rulings-check.py` | 624/624 |
| tracker | `python3 scripts/tracker-counts.py --check` | OK |
| design digest | `python3 scripts/design-digest.py --check` | 1 red, pre-existing (AQ13 has no INVENTORY — same at the base) |
| mermaid | `scripts/mermaid-check.mjs` on both designs | all parse |
| B-2: toolkit suite, full (T1) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5028 / 5 failed — all 5 are S0 type rails on members this lane never touched (`TerrainBlock.HeadingDeg`, `TerrainFeature.HeadingDeg`, `ReviewHostFacts.ExerciseId`, …; `git diff 8e3a8000` on them is empty) — from the coordinator merge, not B-2 |
| B-2: SimHost damage suites | `--filter BuildingDamageBakeTests\|EntityBuildingDamageTranslatorTests\|StaticObstacleBakeTests\|LiveOverlayBakeTests` | 38 / 0 |
| B-2: drawing (Xvfb) | `xvfb-run -a … --filter TheBuildingDamagePlanTests\|TheBuildingDamageDrawsTests\|TheTerrainChunks` | 9 / 0, 4 skipped (the scale-terrain look rails: their assets are not here) |
| B-2: scenarios | roadblock-blast, wreck-blocks-street | 2 / 0 |

| P-3: toolkit suite, full (T1) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5039 / 5 failed — the same 5 pre-existing S0 rails |
| P-3: editor save | `Hrot.Editor.Tests --filter TerrainSave\|BlockOperations` | 3 / 1 — `EditorTerrainSourceServiceTests.E2_…IsGreyedWithItsReason` is pre-existing: the catalogue now falls back to the kept ground (`8ccb92a3`, `TerrainCatalog.KeptGroundOf`), so deleting the base no longer greys the terrain; test not touched by this lane |

| P-4: toolkit suite, full (T1) | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build` | 5045 / 6 failed → after typing `BornAt` as `SimInstant`: the 5 pre-existing S0 rails only (crater + S0 + breach + kinematics filter: 179 / 3, all pre-existing) |
| P-4: SimHost bake / road / wire | `--filter CraterBakeTests\|BridgeClosureTests\|StaticObstacleBakeTests\|BuildingDamageBakeTests\|LiveOverlayBakeTests\|EntityBuildingDamageTranslatorTests` then `…\|EntityGroundDamageTranslatorTests` | 48 / 0 · 14 / 0 |
| P-4: parallel checks | `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1` on `CraterTests\|CarKinematicsSystemTests\|BuildingDamageSystemTests`; scenarios roadblock-blast + wreck-blocks-street | 128 / 0; scenarios 2 / 0 after declaring `SensorMount` in `BrainReads` (cross-lane, behaviors: an undeclared read in `ThreatDanger` → `EqsTerrainSight.Mount`, not crater code — green without the checks) |
| P-4: red-proofs | `MakeCrater` dropped · the query's ground back to the cache · `NavDoorways.Craters` dropped · `AddCraters` dropped | each turns its rail red (the navmesh one only on a SHALLOW crater — a deep bowl's walls are already over the vehicle slope) |
| P-4 part 3 | `Fdp.Toolkits.Tests --filter CraterTests\|CoverGeneratorAndLosTests\|TerrainEqsTests\|StaticObstacleTests\|BuildingDamageSaveTests\|ScenarioMerge`; `Hrot.Core.Tests --filter TerrainObjectRequestsTests\|ScenarioLoadStep` | 95 / 0, 45 / 0; 4 / 0 |
| P-4 re-ground + roads | `--filter TerrainHeightTests\|CraterTests\|RoutePlanner` | 46 / 0 |
| B-6 | toolkit `--filter TreeFellingTests\|CraterTests\|TerrainChunkTrees\|Woods\|CarKinematicsSystemTests\|ContactBreach` · SimHost `TreeFellingBakeTests\|CraterBakeTests\|StaticObstacleBakeTests\|BuildingDamageBakeTests\|EntityGroundDamage\|BridgeClosure` | 139 / 0 · 36 / 0 |
| B-5 | toolkit `--filter WorldSurvey\|BuildingLayout\|SoundPath\|Building` · SimHost `BreachEntryTests\|ClearBuilding\|RoomOrder` | 138 / 0 · 3 / 0 |
| P-5 part 2a | `Hrot.Presentation.Tests --filter TerrainAuthoringPickerTests\|TerrainBirth\|AddEntity` | 96 / 1 — the 1 is pre-existing (`CE2138_*`, a TKB type from behaviors' CE-7014) |

⚠ Not yet run: `stepped-hash.py --threads 1,4,8`, drawing rails under Xvfb (nothing drawn changed), the full solution build.

## 4. Designs updated · ids

- `DESIGN_Vehicle_Breaching.md` §7.8 (B-1 as built, CE-6014) · `DESIGN_Ground_Patches_And_Road_Blockage.md` §9.6 (P-0 as built) · `CAPABILITIES.md` §2b (✅ every wall stops a vehicle)
- `DESIGN_Vehicle_Breaching.md` §7.9 (B-2 as built) · `CAPABILITIES.md` §2b (✅ blasts breach garden walls)
- ids: R-5000…R-5008. No new CE rows (B-1, P-0, B-2 and CE-6014 are existing rows).
- ⚠ cross-lane edits (said): P-4 — `BrainReads` declares `SensorMount` (behaviors), `EntityBodyLayer3D` skips the ground-damage TKB (ui), `SharedTranslatorPack` / `NedOwnershipGroupBinding` / `AllDescriptors` gain `dtGroundDamage` (network).
- ⚠ earlier cross-lane edits: ui's `TerrainPlan2D` / `TerrainLayer2D` (the plan keyed by wall group), the network lane's `EntityBuildingDamageTranslators` (the "terrain resident" gate counts free walls).
