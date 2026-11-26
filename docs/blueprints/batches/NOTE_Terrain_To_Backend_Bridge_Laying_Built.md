# Note: terrain → backend — the laid bridge you spawn is BUILT (CE-4692 BL-S1/S2), what BL-S3 builds on

| | |
|---|---|
| **From** | `terrain`, 2026-11-25 |
| **For** | your [HANDOFF_Coordinator_To_Backend_Bridge_Laying.md](HANDOFF_Coordinator_To_Backend_Bridge_Laying.md) (CE-4693, BL-S3/S4) |
| **Design** | [DESIGN_Bridge_Laying.md](../../designs/engineering/DESIGN_Bridge_Laying.md) §2b (AS-BUILT) and §11 |
| **Status** | [STATUS_Terrain_Bridge_Laying.md](STATUS_Terrain_Bridge_Laying.md) |

## What `BridgeWorkSystem` spawns

A laid bridge is an ordinary **static obstacle** entity of a bridge TKB type:

| TKB | name | span × width × deck height | hp |
|---|---|---|---|
| `TkbEntityTypes.Obstacle_AssaultBridge` = 8825 | Assault bridge | 18 m × 4 m × 1.2 m | 600 |
| `TkbEntityTypes.Obstacle_FloatingBridge` = 8826 | Floating bridge | 60 m × 4.5 m × 0.6 m | 600 |

⚠ Placeholder figures, said in `NedTkbCatalog.RegisterBridges` — your Q-6 replaces them with sourced values.

- **Where it lies:** `SimTransform.Position` = the deck's CENTRE, `Rotation` = heading along the bridge (bank to bank).
- **Its size:** ⭐ there is **no `BridgeShape`**. The size is the existing per-instance `ObstacleShape`: Length = the span between the deck ends, Width = the deck, Height = the deck above the bank. Give a laid instance its own `ObstacleShape` when the gap needs a different span; it replicates like any per-instance obstacle size.
- **Ramps:** each end gets one beyond the deck end, at the authored 1 : 4 (`DeckGeometry.RampLengthFor`). The deck ends must stand on the banks — your gap refusal: `DeckGeometry.Ends(centre, yaw, span)` gives them.
- **Lifecycle:** as every obstacle, held Constructing until every node's bake commits. It is **road** (in `BridgeClosureSystem`'s published net) only once Active.
- **Recovery:** removing the entity (the ordinary teardown) leaves no deck, no road, no rubble — the rebake, the road step and the immediate floor skip (`DeckTable`) do the rest.

## Rails you can lean on
`StaticObstacleBakeTests.CE4692_*` · `BridgeClosureTests.CE4692_*` · `ObstacleDamageTests.CE4692_*` · `FeatureKindsTests.CE4692_*`.
Scenarios: `bl-placed-bridge`, `bl-placed-bridge-absent`, `bl-blow-the-bridge` (`scripts/bridge_laying_scenarios.py`) — copy their river (basic-desert, y 60–72) for `bl-assault-crossing`.
