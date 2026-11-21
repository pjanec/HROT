<!--STATUS
state: LIVE — dispatched, not started (re-dispatched `2026-11-13` before any start, rule 1a: base `ui`, not `exp/threaded-build`)
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — ⭐ THE design: §9 the parallel contract, §11 the work (every item a row), §12 the tests; the UML is there, not here
  - ../../DESIGN_Terrain_Authoring.md — §8 rivers and bridges, §10 the unmeasured water question, §12 / §18f–§18k the random terrain, recipes, block layout, bridges (CE-6001, CE-6002)
  - ../../DESIGN_Peek_And_Fire.md — §9 P-7a: an obstacle entity IS terrain, baked on every node, lifecycle-gated (R-242…R-244) (CE-6003…CE-6005)
  - ../../DESIGN_Parallel_Simulation.md — §18.5–§18.8 the per-entity parallel phases every new system runs in
  - ../../DESIGN_Scenario_Macros.md — macros, InsertMacro, variants §17 (CE-6009…CE-6011)
-->

# HANDOFF — ui → `showcase-world`: the world of the city battle (CE-6000)

**For:** a new lane on its own VM, branch **`showcase-world`**. **From:** ui. **Dispatched at `9c8478744`, re-dispatched on `ui`** (+ the commit that carries
this file); ⛔ **your scope is FROZEN there** — documents that change later are FYI only; one that invalidates an item ⇒ STOP that item and
report it (R-106: never the batch). 🔒 User, `2026-11-13`: *"if we can start with the world now lets break into tasks and lets write handoff"* ·
*"all needs to be recorded in traceable tasks"* · *"no new UI, all is just new showcase scenario (final goal) and all it takes (terrain,
behaviors...) with all the small feature-test scenarios necessary for testing it all"* · *"..just new bulldozer model"*.
⛔⛔ **Autonomous** (R-4156): a decision that needs the user is skipped with a note, never a pause.
🔒 User, `2026-11-13`: *"does the HANDOFF_Ui_To_Showcase_World.md require the exp/threaded-build? if not lets rewrite the handoff to NOT using it"*
⇒ ⭐ **it does not**: every seam this handoff names is byte-identical on `ui` and `exp/threaded-build` (measured `git diff origin/ui origin/exp/threaded-build` on `StaticObstacleBake.cs`, `AreaEffectSystem.cs`, `RecastNavmeshBaker.cs`, `NedTkbCatalog.cs`, `TkbEntityTypes.cs`, `EffectSystems.cs`: empty). ⚠ What it costs: the three parallel machine checks exist ONLY on `exp/threaded-build` — so the one item that IS a determinism measurement (CE-6004) waits, and the checks for the two new simulation systems run later as **CE-6012**.

## 0. Start

| # | step |
|---|---|
| 0.1 | `git fetch origin ui` · `git switch -c showcase-world origin/ui` · push · an empty commit `chore: started showcase-world at <sha>` (rule 1b) |
| 0.2 | ⭐ **merge `ui` (and any lane you build on) as it moves; never rebase.** ⛔ Do NOT merge `exp/threaded-build` — the parallel programme brings this lane in when it unifies (CE-6012) |
| 0.2a | ⭐ **before each item, re-check the overlap:** `git fetch origin 'refs/heads/exp/*:refs/remotes/origin/exp/*'` then `git diff --name-only HEAD...origin/exp/threaded-build` (and any newer `exp/*`) against the files you are about to edit — a hit ⇒ STOP that item, report it, take the next |
| 0.3 | ⭐ read RULINGS.md, then the design [`DESIGN_City_Battle_Showcase.md`](../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md) end to end (§3–§5 the battle, §6–§8 UML, §9, §11, §12) |
| 0.4 | ⭐ **ids:** tracker `CE-6000…`, rulings `R-6000…` — the blocks are registered (tracker "ID blocks", RULINGS §0a). Your rows are **Area S-W**; new rows go at the END of S-W |

## 1. The rules every item keeps — ⛔ not restated, POINTED AT

- **`.claude/CLAUDE.md` "BEHAVIOUR CODE RUNS IN PARALLEL"** — ⭐ the nine never / instead rows are KEPT BY HAND here: the machine checks do not
  exist on `ui`. ⭐ The two new simulation systems (CE-6003's destroy-at-zero, CE-6005's wreck) stay tiny and owner-only: an entity changes only
  ITSELF, a new entity is born through the ELM, no statics, no shared `Random`, sim time only. ⭐ Their rails are written so CE-6012 can rerun them
  with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1` + `stepped-hash.py --threads 1,4,8` unchanged — name them in the report.
- ⛔⛔ **Files the parallel programme is changing — do NOT edit them** (measured: `git diff --name-only origin/ui...origin/exp/threaded-build`, 353 files; ⭐ none under terrain, navmesh, obstacles, TKB catalogue, `ShapeKit`, area damage or wrecks): `Hrot.Common/Macros/MacroExpansionSystem.cs`, `Hrot.Core/Scenario/Map/ScenarioEntityWalk.cs`, `ScenarioRules/Actions/ActionComponents.cs`, the scenario editor's sessions and gizmos. ⇒ CE-6009's macros and CE-6010's scenario are CONTENT (JSON) on the existing machinery; if one needs a change in those files, STOP that item and report it (it becomes CE-6012's).
- **New components:** unmanaged when recorded; a managed one immutable and replaced whole (CLAUDE.md "NEW COMPONENTS").
- **Hot path:** no LINQ, zero steady-state allocation (R-2084) — a system that runs every frame proves it in its rail.
- **Entities are never born in one frame** — the ELM, multi-frame, acked (an obstacle type is held `Constructing` until its bake commits, R-243).
- **No UI work** except CE-6007's model. ⛔ No screenshots in git (the thumbnail atlas page is the one allowed exception, R-4117).
- **Each item ships its own small feature-test scenario** under `scenarios/showcases/city-battle/features/` (seconds to minutes, in-process),
  and its rails drive it. ⛔ Never test correctness on the full battle (D-12).
- **Obligation ⑤:** where the build deviates from the design, fold the as-built back into it (§11 row + the touched section) before the item closes.

## 2. The items — in this order

📐 **INVENTORY behind item 1** (`2026-11-13`): `search_graph` `.*Obstacle.*` Class → **22** (bake, bakery, residency `ObstacleBake`, `ObstacleTkbTranslator`, the shape egress / ingress translators, the placement gizmo, SUMO / ORCA / pedestrian obstacles, tests) — ⛔ **nothing damages or removes an obstacle today**; grep confirms `AreaEffectSystem` has no obstacle case. ⚠ The CLI cannot check index coverage.

| # | row | frame — the measured seams (on `exp/threaded-build`, `2026-11-13`) | feature test → done when |
|---|---|---|---|
| **1** | **CE-6003** O-1 obstacle removal | ⭐ obstacle types `Obstacle_Car 8806 · SandbagWall 8807 · ConcreteBlock 8808 · Crate 8809` (`TkbEntityTypes.cs:108-114`), made terrain by `ObstacleTkbTranslator` (`StaticObstacle` + `ObstacleShape`, `StaticObstacles.cs:17,31`); `AreaEffectSystem.cs:162` already sends ONE `DamageAssessedEvent` to every living `Health` in reach ⇒ **lean: the obstacle types carry `Health` (TKB) — no new damage component, no new damage path**; at ≤ 0 the obstacle's OWNER destroys the entity; `StaticObstacleBakeSystem` (`StaticObstacleBake.cs:49`) sees the set change and rebakes world + cover + touched navmesh tiles on every node. ⚠ Obstacles are skipped by the bullet narrow phase (`TerrainObstacles.IsObstacle`) — blasts only, by design. ⚠ Expose the damage seam the blade will use (CE-7012): a push is damage per second while in contact — say in the design how B calls it | `features/roadblock-blast`: a charge removes a concrete-block roadblock; a vehicle ordered through drives through after the rebake; replay seek shows it gone. Rails red-proved |
| **2** | **CE-6006** V-1a bulldozer type | `TkbEntityTypes` (next free number in the vehicle range — ⛔ permanent wire value), `NedTkbCatalog` (`p.Mobility = TerrainMobility.Tracked` as at `:610`), `ArmorModel`; engineering type, no weapon; armour: small arms bounce, a few RPG hits kill | `features/bulldozer-armour`: a rifle squad cannot kill it; the 3rd RPG does |
| **3** | **CE-6007** V-1b bulldozer model | `ShapeKit.cs` + `VisualFamily` (a new family only if no existing one fits — say why); tracked chassis, engine block, cab, articulated blade, ⛔ not armoured-looking; thumbnail by `scripts/thumbnails.py --add`/`--pack` | the model renders in the 3-D frame rail under `xvfb-run -a` (⚠ display rails skip silently without it); the gallery shows it. Render to the scratchpad and show the user in chat |
| ~~4~~ | **CE-6004** O-1b the debounce — ⏳ **NOT IN THIS BATCH** (it is a stepped-hash measurement; it runs where `stepped-hash.py` exists, with CE-6012) | `StaticObstacleBake.cs:52` `DebounceSeconds = 0.5` of WALL clock (`:72,78,167`; R-244: it must fire while paused / loading) ⇒ the tick a removal reaches the navmesh may differ run to run | ⭐ MEASURE: `stepped-hash.py --threads 1,4,8` on `roadblock-blast`. Equal ⇒ close with the numbers. Unequal ⇒ propose the fix (lean: in the stepped / deterministic mode the debounce counts sim ticks) as a note for the user — ⛔ R-244 is a user ruling, do not change it silently |
| **5** | **CE-6005** O-2 wreck → obstacle | death = `Health.Current ≤ 0` (as `WreckSpawnSystem`, `EffectSystems.cs:406-445`, reads it — that system is Presentation-only fire/smoke); ⭐ lean: the vehicle's owner creates an obstacle entity (a wreck type, the hulk's footprint and heading) through the ELM; removable as CE-6003; tanks / IFVs only (light cars stay pushable later) | `features/wreck-blocks-street`: a tank killed in a street blocks the path; a charge clears it |
| **6** | **CE-6002** T-2 water | ⚠ Terrain Authoring §10 left *"water impassable in the navmesh"* UNMEASURED; `RecastNavmeshBaker.cs:45` has no surface handling; `PlacementSolverSystem.cs:138` avoids water, `TerrainWorld.SurfaceTypeAt` (`:961`); `RubbleMound` (CE-3438) already clips rubble to land | ⭐ MEASURE first on a small terrain with a river + one bridge: does a path cross the water? Only if yes, fix in the bake (water areas excluded) and fold into Terrain Authoring §8/§10. Rail: bank to bank goes over the bridge; a destroyed bridge cuts it |
| **7** | **CE-6001** T-1 the town | ⭐ the terrain-authoring pipeline is BUILT — `Hrot.Terrain.Synth` / `Hrot.Terrain.Random` (CE-4428, §18f), ten feature kinds incl. water (CE-4429, §18g), recipes (CE-4430, §18i), block operations + road alignment (§18h), a bridge is road in both nets (CE-4423, §18k), *Save terrain as* (§18d). ⇒ **lean: author the town as a fixed-seed block layout + a river + two bridges, adjusted by moving blocks, saved as `assets/terrain/city-battle`** — new code only where the layout cannot express §2's picture (say what). ⛔ No OSM | `features/town-loads`: loads in budget (R-4192), the river crossable only at the two bridges (with item 6), the old town square + town hall where §2 draws them; `preview.jpg` by `thumbnails.py` |
| **8** | **CE-6009** M-1 macros | Scenario Macros + `InsertMacro`; company / platoon macros carrying side, kit, entry point; the company commander slot stays empty until B's CE-7002 | a macro inserts a platoon by trigger, multi-frame, acked |
| **9** | **CE-6010** B-0 skeleton | the forces of §4 by macro, prepared positions (sandbags, roadblocks = item 1's types), weather 5 km visibility (`PerceptionWeather`), recording ON (D-17), civilians placed (behaviour is B's CE-7011), the scripted top of §3 (phase lines, sectors, waves by trigger) | `showcases/city-battle` loads and runs 60 s with the EXISTING brains, recording ON (its stepped-hash goes to CE-6012) |
| **10** | **CE-6008** L-1 lethality | existing parameters first (drone count + battery, accuracy, armour) as scenario settings | the setting changes the outcome in a feature scenario |
| **11** | **CE-6011** V-A variants | `$variant`, variant forms (Scenario Macros §17): defender layout, roadblock set, AA / MANPADS positions, civilian density | each variant loads; two variants differ where they should |

⭐ **Items 1 + 2 are what the brains lane waits for** (CE-7012 ClearObstacle) — push them as soon as they are green, and say so in the tracker row.

## 3. Report — the gate contract (CLAUDE.md rule 8)

Per item, in its tracker row and in `STATUS_Showcase_World.md` (this folder, on `showcase-world`): verbatim commands · pass / fail / skip counts ·
`--no-build` column (build the TEST project) · the three parallel checks' results · stepped-hash per thread count · every red proved pre-existing at
the base sha · clean tree after runs · `tracker-counts.py --check` · every id allocated · the design section updated (obligation ⑤).
⛔ Never re-run broad suites "to be sure"; ⛔ never a full-solution build inside the fix loop.
