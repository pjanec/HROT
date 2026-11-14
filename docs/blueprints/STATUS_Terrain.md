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

## 2. Tasks

| task | state | evidence |
|---|---|---|
| **B-1** CE-4598 — the hull sees every wall | ✅ built | [Breaching §7.8](../DESIGN_Vehicle_Breaching.md); rails `CarKinematicsSystemTests.CE4598_*`, `BuildingBreachTests.Hull_*`; red-proved |
| **CE-6014** (showcase-world's row, cross-lane) — vehicles swerve through walls round a man down | ✅ fixed (B-1 exposed it: `RoadblockBlastScenarioTests` ④ went red) | rails `CE6014_*`; see the tracker row |
| **P-0** CE-4596 — cheap terrain change on every node | ✅ built (no runtime caller of the height patch yet — P-1/P-3/P-4) | [Ground §9.6](../DESIGN_Ground_Patches_And_Road_Blockage.md); rails `HeightStoreTests.CE4596_*`, `StaticObstacleBakeTests.CE4596_*` |
| next | **B-2** CE-4599 — one breach record for every wall | |

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
| mermaid | `scripts/mermaid-check.mjs` on both designs | 11/11 parse |

⚠ Not yet run: `stepped-hash.py --threads 1,4,8`, drawing rails under Xvfb (nothing drawn changed), the full solution build.

## 4. Designs updated · ids

- `DESIGN_Vehicle_Breaching.md` §7.8 (B-1 as built, CE-6014) · `DESIGN_Ground_Patches_And_Road_Blockage.md` §9.6 (P-0 as built) · `CAPABILITIES.md` §2b (✅ every wall stops a vehicle)
- ids: R-5000…R-5005. No new CE rows (B-1, P-0 and CE-6014 are existing rows).
