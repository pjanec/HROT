# Note: `unreal-ig` → `terrain`: the OSM bridges live — the deck works; a fallen one stays open to traffic, and no HROT-driven vehicle moved (CE-11075 … CE-11078)

| | |
|---|---|
| **From** | the HROT-integration session (`unreal-ig`), 2026-11-27 |
| **For** | `terrain` (after the breachable-walls handoff — not more urgent than it unless you judge CE-11076 so) |
| **Evidence** | Unreal's live test [NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md](NOTE_UnrealIg_To_UiIntegration_Osm_Bridges_Live_Test.md) (`unreal-ig` `56acdc2f8`): HROT `9c38cb332`, `--mode editor`, `stechovice-battle` live, the debug API only |

✅ **What works, measured live:** the deck of "most Vltavanů" (`osm-bridge/4412124`) is at 215.41 over ground 199.71; SUMO traffic (a van, an `InTraffic`
HMMWV) and a rifleman cross ON it, never in the water; a 94 kg Mk-82 at mid-deck collapses the structure and the navmesh stops joining the banks.

| id | finding | measured / where | lean |
|---|---|---|---|
| ⭐ **CE-11075** | **a fallen OSM bridge is not closed for traffic**: after the collapse a SUMO HMMWV drove onto it at deck height and stopped in the air (5.3 m up), still in Traffic | `BridgeClosureSystem` closes only `TerrainDefinition.ResolvedBridges`, filled from the OVERLAY header's `bridges[]` (`TerrainResidency.cs:355`); the importer's `osm-bridge/*` are base walkables with no edge list — `DESIGN_Terrain_OSM_Bridges.md` D-6 says *"destruction as the authored bridge"*: the deck half holds, the road half does not | the importer records each OSM bridge's SUMO edges (it knows them: the bridged edge chain, D-2) the way the overlay does, so `ResolvedBridges` covers both |
| ⭐ **CE-11076** | **no HROT-driven vehicle moved anywhere in the run** — every move `FailedBlocked` within ~2 s: an M1 and a crewed HMMWV over the bridge AND on their own bank, and the scenario's own M1 by the authored bridge | ⛔ not investigated. ⚠ the regression's bridge rows passed on `2026-11-26` (fixed step), so it may be live-only or import-dependent (CE-11078) — `stechovice-battle`'s *"a Blue tank ordered over the bridge reaches the west bank"* would fail the same way live | measure first: does the vehicle navmesh exist after this fresh import (its `navtiles` seeded from a stale folder?), and does the same order pass at fixed step on that import |
| **CE-11077** | the bridged edges' `<edge shape>` carries the deck height, but their `<lane shape>`s dip to the river bed (4412124: 199.4; `26476508_0`: 199.2) | harmless in HROT today (vehicles ride the deck); a client drawing roads from lane shapes draws them under water. `NetElevation.Apply`'s edge-surface pass (D-7) reaches the edge shape only | D-7 lifts the lane shapes too |
| **CE-11078** | at the editor's start `data/shared/assets/terrain/Stechovice` (and `data/nodes/node-1/Terrain/Stechovice`) went back to an import WITHOUT bridges (manifest `fe13be9c…`); the run then imported afresh into `assets/terrain/Stechovice` (manifest `7a73289d…`, with bridges) | the run and the Unreal client still agree (one hash); which folder is "the" import moved under them | say which folder is authoritative; whether the stale-import fix (CE-11061) covers the staged copies |
