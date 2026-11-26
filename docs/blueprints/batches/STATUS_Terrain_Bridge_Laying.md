# STATUS — terrain, BRIDGE LAYING (CE-4692 — BL-S1, BL-S2)

Handoff: [HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md](HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md) · design:
[DESIGN_Bridge_Laying.md](../../designs/engineering/DESIGN_Bridge_Laying.md) rev 2. Started at `829091759` (merged `coordinator@cdcc9f1e2`).

## 1. Now
DONE — every row built, all three scenarios PASS (`scripts/scenario-regression.sh bl-placed-bridge,bl-placed-bridge-absent,bl-blow-the-bridge`, 3/3).
Red-proofs: see §5. Backend may start BL-S3 — [NOTE_Terrain_To_Backend_Bridge_Laying_Built.md](NOTE_Terrain_To_Backend_Bridge_Laying_Built.md).

## 2. The queue
| # | row | state | sha | rail | scenario |
|---|---|---|---|---|---|
| ① | walkable kind (`StaticObstacle.Kind`, TKB 8825 / 8826) | DONE | `6f9abc267` | `StaticObstacleBakeTests.CE4692_*` | `bl-placed-bridge` PASS |
| ② | one deck geometry (`DeckGeometry`, lifted from `FeatureKinds`) | DONE | `6f9abc267` | `FeatureKindsTests.CE4692_*` | — |
| ③ | the bake ADD (`With`, `BakeObstacles`, `DeckDirt`, commit) | DONE | `6f9abc267` | `StaticObstacleBakeTests.CE4692_*` (incremental) | `bl-placed-bridge` PASS |
| ④ | the floor + the kerb (+ hull points on a deck, found live) | DONE | `9089fb47e` | `ObstacleDamageTests.CE4692_*` | `bl-placed-bridge` PASS |
| ⑤ | the road ADD (`base − blocked + added`) | DONE | `6f9abc267` | `BridgeClosureTests.CE4692_*` | — (no road in the stage) |
| ⑥ | destroy (immediate skip, deck reach) | DONE | `6f9abc267` | `ObstacleDamageTests.CE4692_*` | `bl-blow-the-bridge` PASS |
| ⑦ | the water (`WaterFallSystem`, no rubble) | DONE | `6f9abc267` | `ObstacleDamageTests.CE4692_*` | `bl-blow-the-bridge` PASS |
| S | `bl-placed-bridge`, `bl-placed-bridge-absent`, `bl-blow-the-bridge` | DONE | | | 3/3 PASS |

📐 The first scenario run was red: a tank was held at the ramp's foot (its corner read with the hull centre's hint ⇒ out of reach ⇒ the
ground; the trace ran through the ramp). Fixed by `IWorldQuery.HullPointZ` (`9089fb47e`); design §11 BL-12.

## 3. Decisions taken (deviations, folded into the design)
- `BridgeShape` is NOT a new component: a deck obstacle reuses `ObstacleShape` (Length = span between the deck ends, Width, Height = deck height
  above the bank). It is already `[PerInstanceValue]`, replicated (`EntityObstacleShape`), saved and held until arrival — a second size
  component with its own descriptor and translator would duplicate it (ruling 9).

## 5. Gates
| gate | result |
|---|---|
| `Fdp.Toolkits.Tests` `CE4692|CarKinematicsSystemTests|HullClearance` | 180 / 180 |
| `Fdp.Toolkits.Tests` `ObstacleDamage|StaticObstacle|FeatureKinds|BridgeRoads|CarKinematicsSystem|TerrainWorld` (before the ramp fix) | 285 / 286 — the one red was the new rail itself, then fixed |
| `Hrot.SimHost.Tests` `StaticObstacleBake|BuildingDamageBake|LiveOverlayBake` + `CE4692` | 40 / 40 |
| scenarios (editor tier) | 3 / 3 PASS |
| red-proofs (7 compiling mutations) | 7 / 7 RED: no `DeckDirt` · road `Edited` → no added · flipped deck normal · reach to the centre (needed a 600 m test deck: every fragmentation warhead reaches tens of metres) · no fall · no `DeckGone` skip · no kerb |
| `ObstacleDamageTests` under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` | 7 / 7 |
| ⚠ not run | the cluster tier; the integration suite (row 8) — the bake and the road step are per-node derivations of replicated components, measured by the incremental-bake and road rails |
| ids | TKB 8825, 8826; no new CE ids |
