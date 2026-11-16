<!--STATUS
state: LIVE — in progress
updated: 2026-11-16
current-answer: §1 the items, §2 lane decisions, §3 gates
related-designs:
  - ../../DESIGN_Vehicle_Breaching.md — B-D4, B-D11, §7.6, §7.8 (as built)
  - ../Architect_Question_112_Terrain_Versions_On_Reimport.md — terrain versions (R-4280)
  - HANDOFF_Coordinator_To_Terrain_Breaching_Fixes_And_Versions.md — the dispatch (scope frozen at b324236a6)
-->

# STATUS — `terrain`: breaching fixes + terrain versions (R-4280; CE-4629, CE-4630, CE-5004) — started at `88e5520ff`

## 1. The items

| # | item | state | evidence |
|---|---|---|---|
| 1 | R-5000 changed — a vehicle crosses a kerb | ✅ | `HullTrace.MinPanelHeightM` 0.25 m, skipped in the hull trace; `CarKinematicsSystemTests.R4280_*` (a tank and a possessed car over 10 cm; the car still held by 1 m) — red-proved (skip removed ⇒ 2 red); DESIGN §7.8 row |
| 2 | CE-4630 — a possessed soldier cannot enter a bumped hole | ⚠ not reproduced | editor live run (bt-doors): an ordered AND a possessed rifleman walked a possessed bulldozer's 1.2 m bump hole into House A. Rails `BuildingDamageBakeTests.CE4630_*` (6) red-proved (breached panel kept whole ⇒ 6 red). Found: CE-5018 (no breach at all in `--mode all`), CE-5019 (a charge's crater blocks its own breach), the bump hole centred on the hull's contact corner (→ item 3). Tracker row stays open until the user's case is matched |
| 3 | CE-4629 — a bulldozer's hole is blade-wide | ✅ | ① a `bulldozer` row (DIS 1.1.*.9.1, `blade`): a building bump opens hull width + 2 × margin at full height, centred on the hull; every bump centred on the hull's centreline; ② `DozerCutSystem` uses the one multi-panel cut `ContactBreach.CutAlong`. Rails `CarKinematicsSystemTests.CE4629_*`, `ClearObstacleBehaviorTests.CE4629_*` (3) red-proved (blade branch off ⇒ 2.2 m high; centre on the contact ⇒ 2.15 m off; cut clamped to 1 m ⇒ 1.00 m gap). DESIGN §7.8 table. ⭐ + a 0 B rail for a held hull (`CE4629_ABulldozerHeldAtAWall_AllocatesNothing_PerSteadyFrame`), which caught a PRE-EXISTING 40 B a frame in `ContactBreachTable.For` (a foreach over the `IReadOnlyList` interface, CE-4600) — fixed (`a03dcb887`) |
| 4 | feature scenario `tt-dozer-breach` | ⏳ | |
| 5 | AQ112 / CE-5004 — terrain versions | ✅ B · A · C-function; ⏳ C-wizard page, D | B `TerrainCatalog.ResolveDefinition(name, hash)` + `TerrainResidency.Prepare` (overlay base hash ⇒ the kept version; no version ⇒ the error names the hash) + the replay (same line); A `TerrainImporter.Swap` files the old folder under `versions/<hash>/`, `versions/` out of the content hash and git; C `StorageProcessManagerTests`: `StorageProcessManager.RecompileScenariosOnTerrain` over the save's one compile (`CompileOverlayCore`). Rails + red-proofs in AQ112 §4. ⛔ C is not reachable from the import wizard yet (R-5062) |

## 2. Lane decisions (for the user's review)

| id | decision | what would change it |
|---|---|---|
| R-5056 | the kerb height is a constant `HullTrace.MinPanelHeightM` = 0.25 m beside `HullBandM`, not a row of `contact-breach.json`: the hull trace lives in the terrain world, below the breach table, and every hull (AI, possessed, any class) uses one rule | a per-class kerb (a car vs a tank) ⇒ move it to the table |
| R-5060 | `Prepare` finds the old version through the OVERLAY HEADER's base hash — the replay needs no code of its own, because a recording's `TerrainHash` is stamped only with an overlay and equals that header hash | a recording that must pin the terrain WITHOUT an overlay ⇒ stamp `TerrainHash` for every recording and pass it through a new `Prepare` argument |
| R-5061 | the same content imported again keeps NO version (the new hash equals the old); a version is kept only when the bytes differ | a user wanting an explicit "import" log ⇒ keep it as a report line, not a folder |
| R-5062 | C is the orchestrator function `RecompileScenariosOnTerrain` + report, NOT yet the wizard's last page: the wizard (editor process) has no path to the master's NAS and compile delegates; it needs a storage-op message (`StorageOpType` row + intent) — a UI-lane slice. A re-compile never deletes a failing scenario's old overlay (it still loads by B), unlike a save's failed compile | the UI lane picks the slice up ⇒ the message + the wizard page showing the report |
| R-5057 | EVERY bump hole (a tank's door-sized one too) is centred on the hull's centreline where it meets the wall, not on the first contact point (a corner) | a wish for the hole at the corner that struck |
| R-5058 | the bulldozer is recognised by a `contact-breach.json` row matched on its DIS type (`1.1.*.9.1`, the class EntityClassTable already gives it), not by a TKB id or a new descriptor | a dozer without that DIS class ⇒ a TKB flag |
| R-5059 | `ContactBreach.CutAlong` cuts ONE wall: a building's own panels, or the free walls — never both (the ordered cut keeps cutting building walls as before) | — |

## 3. Gates
*(per item)*
