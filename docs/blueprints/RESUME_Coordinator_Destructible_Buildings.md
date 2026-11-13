<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: ⭐ §3 the slices (S-0…S-5 ✅ built; what remains: the scale-1 numbers on backend's Windows machine (UTM §8.7) and ui's drawing CE-4392; R-4196: decided and baked on the Muscle only; S-0 ✅ · S-1 ✅ breaches, as built in UTM §8.3 — the build design is UTM §8.2; ⛔ §2's 'NEW' row is superseded there: no NavRebuildRequester, the P-7a obstacle bake does it) · §0 the task · §1 settled decisions · §2 what exists (measured) · §3 the build in slices · §4 traps · §5 how to work
stale-below: nothing
related-designs:
  - docs/designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — the ~1 000-man city battle: its O-1 (obstacle removal) reuses this breach / collapse path
  - ../DESIGN_Terrain_Import_UTM.md — ⭐ OWNS the design: §8 (R-4188, D-1…D-8, Q-D1…Q-D4) and §8.1 (the runtime sequence, the module graph, P3-a…e — READY-TO-BUILD)
  - ../DESIGN_Building_Interiors.md — the walls, openings and doors this changes; its E3 ("wall breaching = changing geometry at runtime → later") is what this delivers; §3j 5b′ (R-219) the live door-state pattern to copy
  - designs/navig-2/Navigation_Design_v2_0.md — §14 runtime geometry change (P1 snapshot ✅, P2 tiles + NavTileCache ✅, P3 replan ⛔ not built) — P3 is part of this work
  - ../DESIGN_World_Query_Seam.md — IWorldQuery (R-253): sight and fire read the live breaches here, as they read live doors
  - ../DESIGN_Terrain_Combat_Tuning.md — warheads, materials and penetration (the data D-5's sizes scale with)
  - ../DESIGN_Map_3D_Mode.md — §6ah / §6ai the chunked 3-D terrain the breaches and rubble must be drawn into (ui, R-4195)
  - ../DESIGN_Scale_Rig.md — the rig to measure a rebake and the replans on scale-1
-->

# RESUME — coordinator: DESTRUCTIBLE BUILDINGS (wall breaches + whole-building collapse) — build it

> RELEARN. Read `docs/blueprints/RULINGS.md` (R-4188 is this work's ruling; R-2037, R-219, R-253, R-4192, R-4195 bind it), then this file.

## 0. The task

🔒 User, `2026-11-10`: *"backend is busy with optimizing large scenarios, if you can take the destructible building, write a resumption document
(handoff for yourself) and i will continue in another session with that"* ⇒ **the coordinator BUILDS destructible buildings** — tracker
**CE-4391**. The design is done and approved: [DESIGN_Terrain_Import_UTM.md §8 + §8.1](../DESIGN_Terrain_Import_UTM.md) (backend wrote §8.1 as
L-8, `READY-TO-BUILD`). ⚠ It touches backend's navigation code (`Fdp.Toolkits.Navigation.Recast`) and combat (`AreaEffectSystem`) — backend
is told in [NOTE_Coordinator_To_Backend_Destruction_Taken.md](batches/NOTE_Coordinator_To_Backend_Destruction_Taken.md); state every such
edit in the commit (cross-lane edits are allowed, said).

## 1. Settled — do not reopen *(R-4188, user-approved `2026-11-09`)*

| # | decision |
|---|---|
| D-1 | a breach = an extra OPENING on an existing wall panel (the user's "big window"), a rectangle clipped to the panel and its storey |
| D-2 | bottom on the floor and ≥ 0.8 × 1.8 m ⇒ a **PASSAGE** (walkable, the navmesh changes); smaller ⇒ a **HOLE** (sight and fire only) |
| D-3 | **world state**: an UNMANAGED `WallBreaches` component (a fixed list) on a per-building entity created at the FIRST breach (1 988 houses cost nothing until hit); owned by the Muscle; recorded, replicated, saved with the scenario, restored by checkpoints |
| D-4 | **the Muscle breaches, by ONE rule** (warhead × the panel's material): blasts through `AreaEffectSystem`, penetrating hits at their hit point. ⛔ the brain never asks (R-2037) |
| D-5 | sizes by warhead, as DATA (material resistance scales them): charge ≈ 1 × 2 m passage · tank / missile ≈ 1–1.5 m hole · artillery ≈ 2–3 m · a heavy bomb close by ≈ the whole panel of that storey |
| D-6 | sight and fire read live breaches at QUERY time; a passage triggers the copy-on-write tile rebake (Nav v2 §14), off the frame; building **P3 replan** is part of this |
| D-7 | ⛔ no floor removal. A **COLLAPSE** (a heavy bomb close enough — a threshold in the same table) **replaces the whole building by a non-enterable rubble model**; people inside are casualties |
| D-8 | the external visual engine draws breaches from the replicated state — outside this repo |
| Q-D3 / Q-D4 | breaches and collapses last the whole exercise (no repair); every building, imported and authored, one rule |

## 2. What exists — measured `2026-11-10` (grep; re-run `search_graph` before designing details, CLAUDE.md)

| piece | exists? | where |
|---|---|---|
| wall panel with openings | ✅ | `TerrainWallPanel`, `TerrainOpening` — `FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainBuildings.cs` |
| ⭐ the LIVE-STATE pattern to copy | ✅ | doors: `DoorState` + `TerrainObjectKey` per-entity components, read through `DoorStates` — an immutable VIEW built per snapshot (`TerrainObjects.cs:57-75`, R-219: ⛔ not a field on `TerrainWorld`, ⛔ not a singleton — both leak across background snapshots) |
| blasts acting on doors | ✅ | `AreaEffectSystem` (`Fdp.Toolkits/Combat/Systems/AreaEffectSystem.cs`, door destruction ≈ `:346`) |
| live doors in pathing | ✅ | `DoorAwareQueryFilter` (`Fdp.Toolkits.Navigation.Recast/NavDoorways.cs`) |
| tile rebake | ✅ | `RecastNavmeshFactory.Rebake(provider, world)` (`:86`) → `DotRecastNavmeshProvider.Rebake`; `NavTileCache` keys tiles by their inputs |
| one provider version | ✅ (whole-provider, not per tile) | `DotRecastNavmeshProvider.QueryVersion` |
| `WallBreaches`, `BreachRule`, building state / collapse, per-tile versions, path stamps, replan, the rebuild requester | ⛔ none (grep) | NEW |

## 3. The build — in slices, each shippable with its rails

| slice | what | done when |
|---|---|---|
| **S-0** ✅ `2026-11-10` → [UTM §8.2](../DESIGN_Terrain_Import_UTM.md) (B-1…B-11) | ⭐ the design DELTA, diagram first: classes for `WallBreaches` (unmanaged, its fixed capacity — say the number and why), the per-building entity + its key, the `WallBreachesView` (per snapshot, like `DoorStates`), `BreachRule` + the warhead × material DATA table (where it lives — next to the combat tuning data), `BuildingCollapse` state; fold into UTM §8 as "§8.2 build design" (`INVENTORY` of the graph queries) | the diagrams parse; the user is told in one line (no approval round — R-4188 decided the substance) |
| **S-1** ✅ `2026-11-10` → [UTM §8.3](../DESIGN_Terrain_Import_UTM.md) (as built) | **breaches in the world + sight and fire**: `BreachRule` on blasts and penetrating hits → `WallBreaches`; `IWorldQuery` sight / fire traces treat a breach as an opening (the same code path doors' openings take); recorded + replicated + checkpoint-restored (CLAUDE.md: unmanaged ⇒ recorded by bytes) | rails: a charge makes a passage, a tank shell a hole, sight passes the hole the same frame, a hole is not walkable; replay / checkpoint round-trip; ⛔ 0 B garbage per steady frame (the hot-path rule) |
| **S-2** ✅ `1f4cf2b61` + review `d2716030b`, `a2878b567` (UTM §8.4) | **collapse**: the threshold → the building's panels, slabs and ramps replaced by its rubble prism (solid, non-enterable) in queries; people inside → casualties (the existing damage path) | rails: the bomb collapses, sight is blocked by the mound, the people inside are killed, a smaller blast does not collapse |
| **S-3** ✅ `b2186adf3` (UTM §8.5) | **the navmesh follows** (§8.1 P3-a…e): rebake input = content + the live overlay (P3-c, ⛔ never edit `TerrainWorld`); a coalescing requester, ≤ 1 rebake in flight (P3-d); per-TILE versions + paths stamped with their tiles' versions, a per-frame integer check, replan only stale paths (P3-a, P3-b; zero allocation); units inside a collapse removed first (P3-e) | rails: a passage becomes walkable after the swap and a path uses it; a path NOT through the touched tiles is not replanned; 10 breaches in a second → 1–2 rebakes; ⛔ never a frame stall (CE-4033 — a cold bake ejected SimHost) |
| **S-4** ✅ save / load `21626ca02` (UTM §8.6); ⏳ drawing = ui (CE-4392) | **saved and shown**: a scenario saved after breaches reloads with them (Q-D3); the 2-D map and the 3-D view DRAW breaches and rubble — the drawing half is ui's (R-4195) | save / load rails ✅; the drawing is CE-4392 |
| **S-5** ✅ `bd-destruction` + the numbers (UTM §8.7) | a demo scenario (`scenarios/demos/buildings-and-doors/bd-destruction`, conditions only: no golden, it does not repeat) and the numbers measured in-process on demo-city; ⛔ scale-1 could not be generated on the cloud VM (no `netconvert`), so the rebake seconds on scale-1 and the rig's R-4192 frame / garbage figures are **backend's Windows machine's** | ✅ the demo; ⏳ the scale-1 measurement (CE-4391 stays open for it) |

## 4. Traps — measured or ruled

- ⛔ **live state never on `TerrainWorld` and never a singleton** (R-219): background modules (EQS, paths) query their OWN snapshot; copy the `DoorStates` view pattern exactly.
- ⛔ **no allocation / LINQ per frame** in `BreachRule`'s per-frame part, the stamp check or the queries (CLAUDE.md hot path, rails `GC.GetAllocatedBytesForCurrentThread() == 0`).
- ⛔ **a recorded component is unmanaged** (CLAUDE.md "new components"); a fixed list — choose the capacity from D-5 (how many breaches a wall / a building can take before it is "the whole panel") and say what happens past it (merge into the panel-wide breach).
- ⛔ **the brain never reads geometry** (R-2037) — it learns of a new door only through its sensors / pathing.
- ⚠ `NavTileCache` keys a tile by its INPUTS — the breach overlay must be part of the input, or a stale tile is served.
- ⚠ a collapse can trap navmesh agents (P3-e) and units mid-path.
- ⚠ backend is optimising the same navigation and EQS code right now (DESIGN_Scale_Rig §7, P-1 parallel EQS) — fetch and merge `backend` before each slice; keep edits narrow.

## 5. How to work *(what worked in the terrain-import sessions)*

- design delta first in the OWNING doc (UTM §8 → "§8.2"), diagram first, `INVENTORY` with the graph queries run (`scripts/cbm.sh search_graph …` — start it in the background on a fresh VM)
- big well-specified slices → ONE warm Sonnet worker with a full brief (files, seams, success conditions, red-proof); review its diff, spot-check surprising claims, never re-run its tests wholesale
- gates per slice: the TEST project built, `--no-build` runs, each new rail red-proved by a compiling mutation; `tracker-counts.py --check`, `rulings-check.py`, Mermaid parses; commit only your files by path; push `coordinator`; merge the other lanes at the start and the end of every slice
- lanes now: **backend** — large-scenario performance on the Windows many-core machine (P-1 parallel EQS next) · **ui** — on Windows, hidden windows: the live OSM wizard, real frame rates, then rendering under load · **behaviors** — shoot-and-scoot demos, then (proposed, not yet dispatched) AI cost at 1 000 men, long-range fire, woods tactics, air defence
