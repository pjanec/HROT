<!--STATUS
state: LIVE
updated: 2026-11-13
current-answer: one section per item, newest last — CE-6003
stale-below: nothing
related-designs:
  - ../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md — OWNS the city battle; §11 holds every item of this lane as a row
  - ../../DESIGN_Peek_And_Fire.md — §9.9 the as-built obstacle removal (CE-6003)
  - HANDOFF_Ui_To_Showcase_World.md — the frames and the gate contract (§3) this report answers
-->

# STATUS — `showcase-world` (the city battle's world lane)

Base: `ui` `d9f024272` (started marker `809265f1d`). Gate contract: [`HANDOFF_Ui_To_Showcase_World.md`](HANDOFF_Ui_To_Showcase_World.md) §3.

## CE-6003 — an obstacle can be destroyed, and its removal opens the street

**As built:** [`DESIGN_Peek_And_Fire.md` §9.9](../../DESIGN_Peek_And_Fire.md) (classDiagram, sequenceDiagram, module graph, decisions, rejected) ·
[`DESIGN_City_Battle_Showcase.md`](../../designs/group-maneuvers/DESIGN_City_Battle_Showcase.md) §11 O-1 row + §8 caption updated.
⚠ **Deviation from the lean, measured:** the person / hull law of `AreaEffectSystem` is NOT reused for an obstacle — with the obstacle branch off
it gave a concrete block **80 hp for every warhead** at its face (8 kg charge = 81 mm = 120 mm = M67): the fragments stop in the block's own prism
and only the warhead-independent `BlastLethalDamage` lands. An obstacle takes `ObstacleDamage.BlastDamage` = 100 hp/kg × kg × f(d / ∛kg) instead
(the breach law's scaled distance), still as ONE `DamageAssessedEvent` to its Health owner.

**Graph queries run** (`scripts/cbm.sh`): `search_graph name_pattern .*Obstacle.* label Class` → 22, none damages or removes an obstacle;
`search_graph .*(Demoli|Destructi|Wreck|Removal|Clear(ance)?Obstacle).* Class` → 7 (ELM destruction events, `WreckSpawnSystem` — Presentation only);
`search_code IsObstacle` → 7 (LOS occluder, bullet narrow phase, vehicle cover). ⚠ The CLI cannot check index coverage. Grep confirmed: the
destroy idiom is `DestroyEntityCommand` (`Charges.End`, `AreaEffectCloudSystem.End`), the one teardown.

**Overlap with the parallel programme** (`git diff --name-only HEAD...origin/exp/threaded-build`, `2026-11-13`): none of the edited files
(`AreaEffectSystem.cs`, `HealthApplicationSystem.cs` untouched, `ObstacleTkbTranslator.cs`, `StaticObstacleDto.cs`, `CoreInfrastructureCapabilities.cs`,
`NedTkbCatalog.cs`). ⚠ One HIT on the re-check before commit: `Hrot.ReplayBrowser.Tests/ReplayBrowserSubsystemTests.cs` is on the list ⇒ the
replay rail was MOVED to its own file (`ObstacleRemovalReplayTests.cs`, own fixture) and that file is untouched. `CarKinematicsSystem.cs` is on it too ⇒ CE-6014 not built. Edits to `NedTkbCatalog.cs` are confined to `RegisterObstacles` / `RegisterObstacle` (the bulldozer worker's CE-6006 touches the
vehicle region); `TkbEntityTypes.cs` untouched. Cross-lane edit, said: `Hrot.Editor.Tests/EditorCapabilitiesTests.cs` (ui) — its pinned
health-application head is now read from the capability (`HealthApplicationHead()`), as its tail already was.

### Gates

| # | command | result | `--no-build` |
|---|---|---|---|
| 1 | `dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-restore` | 0 errors | builds |
| 2 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build --filter "FullyQualifiedName~ObstacleDamageTests\|FullyQualifiedName~StaticObstacleTests\|FullyQualifiedName~AreaEffectSystemTests\|FullyQualifiedName~HealthApplicationSystemTests\|FullyQualifiedName~ChargesTests\|FullyQualifiedName~AreaEffectCloudTests\|FullyQualifiedName~BuildingCollapseTests\|FullyQualifiedName~BuildingDamageSystemTests"` | **111 / 0 / 0** | ✅ (test bin dll 18:09:37, after the last edit) |
| 3 | `dotnet build Hrot/Subsystems/Hrot.ReplayBrowser.Tests/... --no-restore` · `dotnet test … --no-build --filter "FullyQualifiedName~CE6003\|FullyQualifiedName~O8_\|FullyQualifiedName~T6_"` (the CE-6003 rail in its own file `ObstacleRemovalReplayTests.cs`) | **9 / 0 / 0** | ✅ |
| 4 | `dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/... --no-restore` · `dotnet test … --no-build --filter "FullyQualifiedName~RoadblockBlastScenarioTests"` | **1 / 0 / 0** (75 s) | ✅ |
| 5 | `dotnet build Hrot/Subsystems/Hrot.SimHost.Tests/... --no-restore` · `dotnet test … --no-build --filter "…UtmEverywhereTests\|…LiveSimulationPlanRails\|…StaticObstacleBakeTests\|…UnitCommanderTests\|…CgfLogicPackTests\|…BuildingDamageBakeTests"` | **48 / 1 / 0** — the red `CgfLogicPackTests.CE454_TheSquadLayerRunsThroughTheRealCgfPack` (*Expected 2, Actual 0*) is PRE-EXISTING: red with this item's five code files stashed (base `d9f024272` code + the test edits only) | ✅ |
| 6 | `dotnet build Hrot/Subsystems/Hrot.Editor.Tests/... --no-restore` · `dotnet test … --no-build --filter "FullyQualifiedName~EditorCapabilitiesTests\|FullyQualifiedName~Systems.SystemTests"` | **12 / 0 / 0** | ✅ |
| — | `python3 scripts/design-digest.py --check` | ⚠ fails on files this item did not touch (`DESIGN_Terrain_Synth.md`, `DESIGN_Readable_Scenario_Json.md`, `UX/DESIGN_Rules_Bundle.md`, `UX/DESIGN_Replay_Browser_UX.md`, `UX/DESIGN_Show_On_Map.md`, a `superpowers/plans` file): pre-existing | — |
| — | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs` on both designs | 28 + 6 blocks parse | — |
| — | `python3 scripts/tracker-counts.py --check` | OK (3134 rows) | — |
| — | not run: the E2E / system suite (T3), the full solution build | the change is gated by its own rails + the touched classes; ⚠ the three parallel checks and `stepped-hash.py` do not exist on `ui` ⇒ CE-6012 | — |

### Red-proofs (each a compiling mutation, applied, tested and reverted in one command, the file touched after)

| mutation | rail | result |
|---|---|---|
| M1 the obstacle branch off (`… && w.ExplosiveKg < 0f`) — the person / hull law runs | `CE6003_A1`, `CE6003_A2` | 🔴 both: *"the charge took 80 of 600"*; 81 mm = 120 mm = M67 = 80 |
| M2 no `HealthOwnership.AppliesHere` gate | `CE6003_R1` | 🔴 |
| M3 `BlastDamage` without the kg factor | `CE6003_L1`, `CE6003_A2` | 🔴 both (every warhead 800) |
| M4 the query rebuilt every frame | `CE6003_H1` | ⚪ stayed green — building an `EntityQuery` allocates nothing measurable; replaced by M4b |
| M4b a boxed read per obstacle | `CE6003_H1` | 🔴 *"100 steady frames allocated 120000 B"* |
| M5 the translator writes no Health | `CE6003_T2` | 🔴 |
| M6 the seam ignores `IsDestructible` | `CE6003_S1` | 🔴 |
| M7 the trace runs INTO the block (own prism crossed) | `CE6003_A1`, `A2` | ⚪ green — finding: an obstacle prism is not a closed barrier, so the 2 cm offset changes nothing today (kept, documented §9.9) |
| R1 replay: no `BakeRecordedObstacles` on a jump (`ReplayBrowserSubsystem.DebugSurface.cs:266`) | `ObstacleRemovalReplayTests.CE6003_ARemovedObstacle_…` | 🔴 (at the load, which is a jump — the seeks go through the same line) |
| T1 (test-side) the roadblock kept in the "after" world | `CE6003_T1` | not run separately: the "before" half asserts exactly that world closed |
| F1 the twin: the removal never fires (`Current > -1f`) | `RoadblockBlastScenarioTests` | 🔴 *"② … CGF: Roadblock Active hp 0; SimHost: Roadblock Active hp 0; Player: Roadblock Active hp 0"* — the damage reached every node, nothing removed it |

### Parallel contract (CE-6012 reruns these unchanged with `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`)

`ObstacleRemovalSystem` — owner-only (it asks for its OWN teardown: a command + `TearDown` through the command buffer), no statics, no
randomness, no clock, a cached query + a counter as its only state, 0 B per steady frame (`CE6003_H1`). Rails: `ObstacleDamageTests.*`,
`StaticObstacleTests.CE6003_T1/T2`, `ObstacleRemovalReplayTests.CE6003_*`, `RoadblockBlastScenarioTests.*`.

### The feature scenario — `scenarios/showcases/city-battle/features/roadblock-blast` (+ NEW feature terrain `assets/terrain/roadblock-street`)

A 3 m wall cuts the terrain in two with one 6 m gate, closed by a 6 × 1 × 1 m concrete barrier (type 8808, a per-instance `ObstacleShape`); 'Charge Man'
sets off the 8 kg demolition charge 0.6 m off it; 'IFV' is ordered across at 40 s. Measured (the twin): no ground / vehicle path before; the barrier gone on
CGF, SimHost and the Player by sim 12.4 s; both paths through the gate by 12.9 s; the IFV reaches (150, 51.5). `checks.json`: the burst, then the IFV at the
far side. 📐 Why not `cqb-village`: its compound gate is 4 m and the vehicle navmesh agent radius is 1.8 m (`RecastNavmeshBaker.cs:81`) — it never opens to a
vehicle (the IFV's route stayed `None`). ⚠ No `preview.jpg` for the scenario or the terrain: `scripts/thumbnails.py` drives a live editor (skipped, a
follow-up for whoever runs one). ⚠ The replay half is the Replay Browser rail on a recorded removal, not a recording of this scenario.

### Findings

- **CE-6013** (filed, Area S-W): obstacles now carry `Health` ⇒ the objective measures read a blasted roadblock as a Neutral person (civilian harm,
  `MeasuresEvidence.cs:90`; `ObjectiveCensus.IsMan`). Brains code; lean in the row.
- **CE-6014** (filed, Area S-W): the IFV swerved round the charge man's body and crossed the wall line ~7 m off the gate THROUGH the concrete (measured three
  times, `AvoidanceRadius` 5; an authored 1 m changed nothing). `CarKinematicsSystem` is on the parallel programme's list ⇒ not touched; the twin asserts the
  route, logs the crossing.
- Replay Browser: a recording opened through the debug provider's `Open` shows its recorded obstacles only after the first check interval / jump
  (`LoadFdpViaManager` and every seek bake at once). Pre-existing, not filed (cosmetic, one throttle interval).

### Ids allocated

`CE-6013`, `CE-6014`. No ruling ids.

### Clean tree / tracker

The tree is clean after every run but for this item's own files (`git status` lists only them; no golden moved). Every mutation file restored and touched.
Committed by path, pushed to `origin showcase-world`. `.gitignore` gains `!assets/terrain/roadblock-street/` — a committed demo terrain, added on purpose as its own comment asks.
