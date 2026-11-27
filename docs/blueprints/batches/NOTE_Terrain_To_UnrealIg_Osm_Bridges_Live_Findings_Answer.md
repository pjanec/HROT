<!--STATUS
state: LIVE — a NOTE (protocol §5): terrain's answer to NOTE_UiIntegration_To_Terrain_Osm_Bridges_Live_Findings.md · updated: 2026-11-27 · current-answer: the whole file
related-designs:
  - ../../DESIGN_Terrain_OSM_Bridges.md — §8a: what was built and measured for each row
-->
# NOTE — `terrain` → `unreal-ig`: the OSM bridges live findings — answered (CE-11075 … CE-11078)

Answer to [NOTE_UiIntegration_To_Terrain_Osm_Bridges_Live_Findings.md](NOTE_UiIntegration_To_Terrain_Osm_Bridges_Live_Findings.md). Built on `terrain` (with `unreal-ig` and `coordinator` merged).
Details: [DESIGN_Terrain_OSM_Bridges.md](../../DESIGN_Terrain_OSM_Bridges.md) §8a.

| id | answer |
|---|---|
| ⭐ **CE-11075** a fallen OSM bridge stays open to traffic | ✅ **fixed.** The importer now writes each deck's bridge row into `terrain.json` (`bridges[]`, the overlay header's shape): its structure label, the road-graph segments and BOTH directions' SUMO edges; the residency publishes them in `ResolvedBridges` beside the overlay's, and `BridgeClosureSystem` closes them on the collapse like an authored bridge. ⚠ **Importer revision 6 ⇒ every machine re-imports Stechovice ONCE** on its next load (expect the first-use import time). Rails: `ImporterTests.CE11075_*`, `BridgeClosureTests.CE11075_*` |
| **CE-11076** no HROT-driven vehicle moved | ✅ fixed by `backend` (CE-3503 / CE-3504, CE-3505 part — the live solver budget), already on `coordinator`; not a terrain defect |
| **CE-11077** lane shapes under the deck | ⚠ **not reproduced**: on the revision-5 import of manifest `7a73289d…` every bridged lane is on the deck within 0.15 m of its edge shape (`4412124_0` lowest 211.03 vs edge 211.14; `26476508_0` 202.75 vs 202.90; the junction lanes at both ends too), in all three copies here. ⭐ Lean: the lane reading came from the bridge-less `fe13be9c…` copy (CE-11078). **Ask:** re-measure on the revision-6 import; if a lane still dips, name the file path you read it from |
| **CE-11078** which folder is the import | ⭐ the **machine root** (`TerrainSources.SharedImportRoot`: `assets/terrain/<name>` in a dev checkout, else the NAS shared root). `data/shared/…` and `data/nodes/*/Terrain/…` are a run's copies; a copy whose recorded manifest or importer revision differs from the input set is stale and never used (CE-11061 re-imports into the machine root — what your run did). **Ask:** the `[Terrain]` log lines of that editor start, so we can see what put `fe13be9c…` into `data/shared` |
