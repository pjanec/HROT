# Note: the OSM bridges in a live HROT run — traffic and men cross on the deck, a bomb drops it, the roads stay open (CE-11063)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-27 |
| **For** | the HROT-integration session (and terrain / traffic, FYI) |
| **Follows** | [NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Drawn.md](NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Drawn.md) |
| **Build** | HROT-live at `unreal-ig` `9c38cb332` (Release), `--mode editor`, `stechovice-battle` loaded live (its TrafficDemand on); measured over the debug API only, nothing changed in HROT |
| **Bridge** | `osm-bridge/4412124` "most Vltavanů" (local x 858…896, y 3899…4200), deck up to 15.8 m over the river |

⭐ The user asked: do SUMO's vehicles use the bridge (they drove through the water before), do HROT's vehicles and men go on top, can it be destroyed.

## 1. Measured

| | result | evidence |
|---|---|---|
| the deck at run time | ✅ `GET /terrain/levels` mid-river: ground 199.71, level 1 at **215.41** | the walkables of the import (`assets/terrain/Stechovice`, hash `7c98b6ff…` = the Unreal client's) |
| ⭐ SUMO traffic | ✅ a background delivery van (5628, TrafficDemand) crossed south → north, and an uncrewed HMMWV ordered `roadUse: InTraffic` (6385) crossed north → south: **always within 0.15 m of the deck, up to 15.8 m over the ground** — never in the water | per-2 s position vs `/terrain/levels` (scratch `track2/3.jsonl`) |
| ⭐ a man | ✅ a rifleman (5624) walked the whole deck south → north, z = the deck exactly (15.7 m over the ground mid-river) | same |
| ⭐ HROT's own vehicle driving | ⛔ **every** move fails within ~2 s (`PlatformMove ended: Failure`, `NavigationStatus.Result FailedBlocked`) — an M1 (5623) and a crewed HMMWV (6523) over the bridge, ⚠ **and the same two on their own bank (35–50 m, `roadUse` Prefer / Never / unset), and the scenario's own M1 (1131) 30 m beside the authored bridge** | not a bridge defect: no HROT-driven vehicle moved anywhere in this run. ⛔ Not investigated further (yours) |
| ⭐ destruction | ✅ a `DetonationNotification` (Mk-82, ammo 6022, 94 kg ≥ `collapseMinKg` 80) at the deck's middle **collapsed the whole structure** (building 4098, `BuildingDamage.Collapsed = 1` at sim 336.9, cause Detonation, 94 kg); the levels along it are ground only | `GET /combat/detonations`, the `BuildingDamage` entity |
| after the fall, a man | ✅ the rifleman's walk back is refused (`PersonMove ended: Failure`, FailedBlocked) — the navmesh no longer joins the banks | |
| ⭐ after the fall, SUMO | ⛔ **the road stays open**: the SUMO HMMWV (6385), ordered back north, drove onto the fallen bridge at the old deck height and **stopped in the air** at (891.6, 3946.4), z 218.88 over ground 213.55 (5.3 m), still `NavState.Mode Traffic` | §2 ① |

## 2. Findings for HROT (not changed here)

| # | finding | why (file:line) |
|---|---|---|
| ① | ⭐ **a fallen OSM bridge is not closed for traffic** — SUMO keeps routing over it (and HROT's vehicle road net would too) | `BridgeClosureSystem` closes only the bridges in `TerrainDefinition.ResolvedBridges`, which is filled from the **overlay** header's `bridges[]` only (`TerrainResidency.cs:264`, `:355`). The importer's `osm-bridge/*` structures are base-terrain walkables with no edge list there ⇒ `DESIGN_Terrain_OSM_Bridges.md` D-6 says *"destruction as the authored bridge"* — the deck half holds, the road half (DESIGN_Terrain_Authoring §18k-3) does not |
| ② | ⭐ HROT-driven vehicles cannot move on Stechovice in this run (any move, anywhere — FailedBlocked) | §1; ⚠ `stechovice-battle`'s row *"a Blue tank ordered over the bridge reaches the west bank"* would fail the same way |
| ③ | FYI: in `Stechovice.net.xml` the bridged edges' `<edge shape>` carries the deck height (4412124: 220.3 → 211.3), ⚠ but their `<lane shape>`s dip to the river bed (199.4) — both directions (`4412124_0`, `-12529718#0_0`) and `26476508_0` (199.2) | harmless in HROT today (the vehicles ride the deck, §1) — a reader of the lanes (a road mesh from lane shapes) would draw the road under water. `NetElevation.Apply`'s edge-surface pass (D-7) seems to reach the edge shape only |
| ④ | FYI: at the editor's start `data/shared/assets/terrain/Stechovice` (and `data/nodes/node-1/Terrain/Stechovice`) went back to an import without bridges (18:17:25, manifest `fe13be9c…`, `world.geojson` 0 `osm-bridge`); the run imported afresh into `assets/terrain/Stechovice` from `data/shared/assets/terrain-input/Stechovice` (manifest `7a73289d…`, the bridges, hash `7c98b6ff…`) | the run and the Unreal client still agree (same hash); noted because which folder is "the" import moved under us |

Recipe (scratch, nothing in git): editor `--mode editor` on debug API 8111, `POST /scenario/load/live stechovice-battle`, spawn + `POST /entities/{id}/order`,
a 2 s logger of `SimTransform` against `GET /terrain/levels`, the bomb `POST /entities/command {eventType: DetonationNotification, payload: {HitX, HitY, HitZ, Ammo: 6022}}`.
