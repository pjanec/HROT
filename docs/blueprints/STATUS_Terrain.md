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
| next | **P-3** CE-4597 — scenario house pads + *Save terrain as* | |

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

⚠ Not yet run: `stepped-hash.py --threads 1,4,8`, drawing rails under Xvfb (nothing drawn changed), the full solution build.

## 4. Designs updated · ids

- `DESIGN_Vehicle_Breaching.md` §7.8 (B-1 as built, CE-6014) · `DESIGN_Ground_Patches_And_Road_Blockage.md` §9.6 (P-0 as built) · `CAPABILITIES.md` §2b (✅ every wall stops a vehicle)
- `DESIGN_Vehicle_Breaching.md` §7.9 (B-2 as built) · `CAPABILITIES.md` §2b (✅ blasts breach garden walls)
- ids: R-5000…R-5008. No new CE rows (B-1, P-0, B-2 and CE-6014 are existing rows).
- ⚠ cross-lane edits (said): ui's `TerrainPlan2D` / `TerrainLayer2D` (the plan keyed by wall group), the network lane's `EntityBuildingDamageTranslators` (the "terrain resident" gate counts free walls).
