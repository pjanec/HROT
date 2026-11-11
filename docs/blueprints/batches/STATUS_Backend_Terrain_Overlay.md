<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the table below
related-designs:
  - HANDOFF_Coordinator_To_Backend_Terrain_Overlay.md — the frame (dispatched at 01a3c26cb)
  - ../../DESIGN_Terrain_Authoring.md — §18 the owning design (as-built folds there)
-->

# STATUS — backend: the terrain overlay, compiled at save ([handoff](HANDOFF_Coordinator_To_Backend_Terrain_Overlay.md))

Started at `074859439` (merged `coordinator@67b5fcb8d`, which contains `backend@0db463a60`). The brain-cost batch (CE-4395) is done but for its
Windows measurement (needs the user's machine) — see `STATUS_Backend_Brain_Cost_1000.md`.

| # | id | state | sha | note |
|---|---|---|---|---|
| O-0 | CE-4419 | ✅ DONE | this commit | `EntityLifecycleModule.WaitWithoutTimeout(moduleId)` — a construction waiting on such a module never times out; `EntityCreationPack` gives it the terrain module (was 18 000 FRAMES, `ObstacleConstructionTimeoutFrames`, removed). `CheckTimeouts` also reuses its two lists (it allocated two per frame). Rail `EntityLifecycleModuleTests.CE4419_ABakeSlowerThanAnyFrameBudget_KeepsItsEntity` (red with the skip removed: the sentinel then reads as int.MaxValue frames and frame 3e9 destroys it) |
| O-6 | CE-4425 | ✅ DONE | this commit | `.gitignore`: `assets/terrain/*` except the eight committed demos (named), `assets/terrain/*/source/`, `**/terrain-overlay/` (the overlay folder's name — O-1 settles it as `terrain-overlay/` beside `scenario.json`). `git check-ignore`: a generated terrain, scale-1, a demo's `source/`, a scenario's overlay → ignored; a new file in a demo terrain → not |
| O-1 | CE-4415 (design) | ⏭ next | | |
| O-2 | CE-4416 | | | |
| O-3 | CE-4417 | | | |
| O-4 | CE-4415 (build) | | | |
| O-5 | CE-4422 | | | |
| O-7 | CE-4418 | | | |
| O-8 | CE-4420 | | | |

**Gates (O-0):** `Fdp.Toolkits.Tests` `--filter Lifecycle` 44/44 · `Hrot.SimHost.Tests` `StaticObstacleBakeTests` + `BuildingDamageBakeTests` + `ScenarioLoadStepTests` + `PeekAndFireNodesTests` 101/101 (each built as the test project, then `--no-build`; ⚠ the first SimHost build failed on `Hrot.Terrain.Osm` not restored — the coordinator merge added it — restored, rebuilt, rerun).
