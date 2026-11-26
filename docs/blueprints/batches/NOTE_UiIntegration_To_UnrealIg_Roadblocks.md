# Note: roadblocks — sandbag walls, concrete blocks, barriers — and their removal by a bulldozer

| | |
|---|---|
| **From** | the HROT-integration session, 2026-11-26 |
| **For** | `unreal-ig` |
| **User, verbatim** | *"Do we support roadblocks like sandbags walls in the gateway? I need the Unreal to render these and remove these when the block is breached by the bulldozer."* |
| **Gateway** | `ccr-458b1360-afzl8f` `adb077a` (T-084; [DD-035](https://github.com/pjanec/SimNetGateway/blob/ccr-458b1360-afzl8f/docs/design/DD-035-Dps-Bridges.md) extended) — regenerate from the IDL (+9 lines, none changed); the guide's "Obstacles and roadblocks" |

## 1. What a roadblock is

A static-obstacle ENTITY, one of HROT's obstacle types (`Entity.TypeId`): **8806** car · **8807** sandbag wall · **8808** concrete block · **8809** crate · **8819**
wreck · **8820** barrel · **8821** jersey barrier · **8822** guard booth · **8823** water barrel · **8824** sand barrel. (8825 / 8826 are laid bridges — see the
bridges note; they come as `Deck`, never as `Obstacle`.)

## 2. What arrives

| topic | what | how to use it |
|---|---|---|
| `Entity` + `EntityState` | the type, the position, the heading | as before |
| **`Obstacle`** (new; key `EntityId`) | `Length`, `Width`, `Height` — the obstacle's OWN size (a scenario's sandbag wall can be 2 m or 20 m long; `TkbType` only has the type's default) | ⭐ the box is centred on the entity's position in plan, `Length` along its heading, `Width` across, its BASE at the entity's z (top at z + Height) — HROT's own rule (`TerrainObstacles.PrismOf`). Stretch / tile your sandbag mesh along `Length` |
| `EntityState.Damage` | steps down while a bulldozer pushes it (the blade's push is damage per second) | optional: show it giving way |
| **dispose** of the entity and its `Obstacle` | at Health 0 HROT removes the obstacle — the roadblock is breached | remove it (dust, scattered bags) |

## 3. Not the same thing: terrain WALLS

A garden or field wall authored in the TERRAIN (not a placed obstacle) is cut by a blade as a breach in `BuildingDamage`, keyed `wall:<label>` — the same
breach records as a building's (with `Cause = Blade`, the world `Centre` / `Normal`).

## 4. Where to see it

`demos/navigation-and-terrain/tt-dozer-breach` (bt-range: vehicles crossing and cutting terrain walls) · `showcases/city-battle/city-battle` (roadblocks and a
bulldozer — ⚠ its roadblocks come from scenario macros; that they expand into these obstacles is read, not measured).
