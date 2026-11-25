# STATUS — terrain, BRIDGE LAYING (CE-4692 — BL-S1, BL-S2)

Handoff: [HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md](HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md) · design:
[DESIGN_Bridge_Laying.md](../../designs/engineering/DESIGN_Bridge_Laying.md) rev 2. Started at `829091759` (merged `coordinator@cdcc9f1e2`).

## 1. Now
Row ①②③ IN PROGRESS — the walkable kind, the lifted deck geometry, the bake ADD.

## 2. The queue
| # | row | state | sha | rail | scenario |
|---|---|---|---|---|---|
| ① | walkable kind (`StaticObstacle.Kind`, TKB types 8825 / 8826) | IN PROGRESS | | | |
| ② | one deck geometry (`DeckGeometry`, lifted from `FeatureKinds`) | IN PROGRESS | | | |
| ③ | the bake ADD (`With`, `BakeObstacles`, dirty, commit) | IN PROGRESS | | | |
| ④ | the floor + the kerb | TODO | | | |
| ⑤ | the road ADD | TODO | | | |
| ⑥ | destroy (immediate skip, deck reach, road cut) | TODO | | | |
| ⑦ | the water (`WaterFallSystem`) | TODO | | | |
| S | `bl-placed-bridge`, `bl-blow-the-bridge` | TODO | | | |

## 3. Decisions taken (deviations, folded into the design)
- `BridgeShape` is NOT a new component: a deck obstacle reuses `ObstacleShape` (Length = span between the deck ends, Width, Height = deck height
  above the bank). It is already `[PerInstanceValue]`, replicated (`EntityObstacleShape`), saved and held until arrival — a second size
  component with its own descriptor and translator would duplicate it (ruling 9).

## 5. Gates
(filled per row)
