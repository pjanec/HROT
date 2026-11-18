<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Bug_Queue.md — the queue (11 rows)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works it
-->

# STATUS — terrain, the queue

## 1. Now
**idle — waiting for a handoff** (`2026-11-18` ~12:05 UTC; re-checked after the user's message: CE-4656 · CE-4401 · CE-3428 · CE-3426 · CE-3440 are all DONE above) — the 11-row queue is done or recorded, behaviors' clearance note is done, and no open `RW-M`+ terrain row is unheld: CE-4630 waits on the user's re-check; CE-5004's rest is a cross-lane slice (a storage op — backend — + the wizard page — ui) and the deferred prune; CE-5020 / CE-5021 are `RW-S`. The self-wake-up stays (R-4292) and takes any new handoff.
Self-wake-up: `terrain self-wake-up` (`trig_01MWxGq2irAJ7jQwQQZ4eRUR`, hourly at :57) — created at the run's start without an approval prompt; ⭐ KEPT when the queue empties (R-4292); its message is now §1a's text VERBATIM (updated `2026-11-18` 12:05).

## 2. The queue (in order)

| tier | row | state | sha · rail · scenario |
|---|---|---|---|
| P3 | CE-5018 bump makes no breach in a cluster | ✅ DONE — no host published the node id, so the damage entity was requested from the brain node; `HrotNodeBuilder` now publishes `LocalNodeTopology`. Live `--mode all` after: the damage entity on SimHost / Player / Scenario | rails `HrotNodeBuilderTests.CE5018_*` (red-proved: singleton dropped ⇒ 2 red), `BuildingDamageSystemTests.CE5018_*` · ⚠ no scenario row can see it: the scenario run is one editor world (no cluster run exists yet) · UTM §8.3 #8 |
| P3 | CE-4630 a possessed soldier pushed back from a bumped hole | ⚠ NOT REPRODUCED — `--mode all`, bt-doors: a possessed rifleman walks through a dozer's blade hole AND a T-72's door-sized bump into House A and stays (editor and rails agree); lean: before CE-5018 the cluster made no breach at all; left open for the user's re-check | rails `BuildingDamageBakeTests.CE4630_*` (existing) |
| P3 | CE-5019 a charge's crater blocks its own breach | ✅ DONE — a burst that breaches a wall it touches (≤ 0.5 m, `craters.json` `wallContactM`) makes no crater; lane ruling **R-5064 (for review)** | rail `CraterTests.CE5019_*` (red-proved) · `bd-destruction` 10/10 + 2 new 'never in a pit' rows (red with the fix off) · Ground Patches G-7 |
| P4 | CE-2446 an AP mine bursts under a man and hurts nobody | ✅ DONE — the mines' warheads (terrain's own `0573b3a82`) had radii and 0 blast damage; they now take the reference library's blast value | rail `MineClearerTkbTests.AnAntiTankMine_*` (red-proved) · `mf-strike-and-halt` 6/6 · DESIGN_Minefields |
| P4 | CE-2444 an AT mine does not hurt an M1, hurts a man 35 m off | ✅ DONE (verified) — `0573b3a82` + CE-2446 | `mf-at-vs-vehicle` 6/6 |
| P4 | CE-4655 after a strike the squad skirts the field along its edge | ✅ DONE — mines keep `max(spacing/2, 1 m)` inside the outline; ONE `MinePattern.Laid` test (pattern, trigger, clearing, behaviors' recon — cross-lane) | rail `MineTriggerTests.CE4655_*` (red-proved) · NEW scenario `mf-way-round` 4/4 (inset off: blast at 56.5 s) · DESIGN_Minefields MF-2 |
| T2 | CE-4656 a grey block over a baked static car | ✅ DONE — a platform-typed baked obstacle (the parked car) is `DrawnByEntity`; the terrain mesh neither draws it nor cuts the ground under it | rail `StaticObstacleBakeTests.CE4656_*` (red-proved) · NOTE to ui: do the feature obstacles draw twice? |
| T2 | CE-4401 scale-rig roads at height 0 | ✅ DONE — the surface was fixed by ui's CE-1494; the Roads debug gizmo drew at z 0 ⇒ now on the ground (cross-lane, ui's gizmo) | rail `RoadNetworkGizmoTests.CE4401_*` (red-proved) · ⚠ filed **CE-5020** (E2b_V: 9 asphalt vertices ≤ 1.13 m off on a random terrain) |
| T2 | CE-3428 pack the prefilled navmesh tiles | ✅ DONE — measured on `full-1` (106 260 files → about a hundred packs); `NavTilePack` (prefill seals, save merges, seed reads packs then loose) | rails `NavmeshPrefillTests` (2 red-proved) · UTM §12.4 · ⏳ the NAS / Windows staging time is the user's to measure |
| T2 | CE-3426 felling a lone tree, fragments in woods | ✅ DONE — fragments through a wood stopped like rounds (`× (1 − Stop)`); felling was CE-4603 | rail `TerrainWoodsTests.CE3426_*` (red-proved); toolkit Woods / AreaEffect / Crater / BuildingDamage 115 / 115 · ⚠ no scenario bursts in a wood |
| T2 | CE-3440 a breach hot-path allocation rail red 1 in 3 | ✅ DONE — the QueryTimings rails flipped a process-wide switch in parallel with it; their collection now runs alone | rail `QueryTimingsTests.CE3440_*` (red-proved) · 5 / 5 timings runs, every full run green |

## 3. Taken from new handoffs
- `HANDOFF_Coordinator_To_Terrain_Bug_Queue.md` at 2026-11-18 (merged coordinator at the run's start).

## 3a. Lane decisions (for the user's review)
| id | decision | what would change it |
|---|---|---|
| R-5064 ✅ APPROVED as built (`DECISIONS_Terrain.md`, user via coordinator `2026-11-18`) | a burst that breaches a wall within 0.5 m of its face makes NO crater (a contact charge spends itself on the wall) | a big bomb landing at a wall should still crater ⇒ scale the skip by kg (e.g. only below the breach table's collapse mass) |

## 4. Notes sent / received
- ⏳ expected: behaviors' note on D-3 — a "trigger man is maimed in the legs" rule in the mine's area effect; review it when it lands
- received: `NOTE_Behaviors_To_Terrain_NoWayRound_EdgeClearance.md` (behaviors, 2026-11-18) — taken: the route clearance (above)
- sent: `NOTE_Terrain_To_Behaviors_Route_Clearance.md` — the ring is built; `mf-no-way-round` goes round now (theirs to re-author)
- sent: `NOTE_Terrain_To_Behaviors_Mine_Edge_Inset.md` · `NOTE_Terrain_To_Ui_Obstacle_Drawing.md`
- none yet

## 5. Gates (rule 8)
| row | gate | result |
|---|---|---|
| CE-5018 | `dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests --no-build --filter HrotNodeBuilderTests` | 26 / 27 — ⚠ `CE1436_CgfShapedHost_PlayLive_…` red, and red with the fix removed too ⇒ not this change (`Single()` on an empty sequence, line 771) |
| CE-5018 | `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests --no-build --filter BuildingDamage` | 34 / 34 |
| CE-5019 | `Fdp.Toolkits.Tests` Crater / BuildingDamage / BuildingBreach / BuildingCollapse | 86 / 86 |
| CE-5019 | `scripts/scenario-regression.sh bd-destruction` | 10 / 10 (wallContactM 0: 3 red) |
| CE-2446 | `Hrot.Core.Tests` MineClearerTkbTests | 2 / 2 (mutation: red) |
| CE-2446 / CE-2444 | `scripts/scenario-regression.sh mf-strike-and-halt,mf-at-vs-vehicle` | 6 / 6 · 6 / 6 |
| CE-4655 | `Fdp.Toolkits.Tests` Mine / Minefield / PlaceKnowledge | 58 / 58 (`CE4624_ATurnedJitteredField_*` now walks only laid cells) |
| CE-4655 | `scenario-regression.sh mf-strike-and-halt,mf-at-vs-vehicle,mf-recon-and-mark,mf-way-round` | 6/6 · 6/6 · 6/6 · 4/4 — ⚠ `mf-no-way-round` 7/8: "nobody enters the field" red at 49.5 s, red WITH THE INSET OFF TOO ⇒ not this change; behaviors' CE-2447 |
| CE-4656 | `Hrot.SimHost.Tests` StaticObstacleBake / CraterBake / BuildingDamageBake / LiveOverlayBake | 41 / 41 (mutation: red) |
| CE-4656 | `Fdp.Toolkits.Tests` Terrain | 475 / 475 |
| CE-4656 / CE-4401 | `xvfb-run -a dotnet test Hrot.Presentation.Tests --filter Map3D\|TheDynamicTerrainLooks` | 713 / 724 — 5 red: `CE4388_*`, `CE4582_*`, 2 × `MotorcycleModelRail` (pre-existing, §1 of STATUS_Terrain_User_Observations) and `E2b_V_*` (needs netconvert, ui reported it red before ⇒ CE-5020) |
| CE-4401 | `Hrot.Presentation.Tests` RoadNetworkGizmoTests | 3 / 3 (mutation: red) |
| post-merge | `mf-way-round,mf-strike-and-halt,mf-recon-and-mark` (after the recon edit, behaviors + ui merged) | 4/4 · 7/7 · 6/6 |
| CE-3440 | `QueryTimings` × 5; `Fdp.Toolkits.Tests` Terrain / Recast / Diagnostics (+ OverlayCompiler, StaticObstacle) | 6 / 6 × 5; 937 / 937 |
| CE-3428 | the same toolkit run · `Hrot.Terrain.Import.Tests` · `Hrot.SimHost.Tests` TerrainLoad / Overlay / Bake | 937 / 937 · 54 / 55 (⚠ `ImporterTests.TheHeightStore_AtAFewPosts_*`: red only WITH SUMO installed — the synth then builds roads and the importer levels them; green at `6c2788ed3` in a clean worktree without SUMO ⇒ filed **CE-5021**, a rail gap, not CE-3428) · 97 / 97 |
| CE-4655 (route) | `Fdp.Toolkits.Tests` Mine / Minefield / PlaceKnowledge / Recast / DangerArea · `Hrot.SimHost.Tests` Minefield / DangerArea / Recon | 208 / 208 · 13 / 13 |
| CE-4655 (route) | `scenario-regression.sh mf-no-way-round,mf-way-round,mf-strike-and-halt,mf-at-vs-vehicle,mf-recon-and-mark` | 4 / 5 — `mf-no-way-round` goes round now (no MinefieldAhead, no blast; the x-only "enters" row and "still halted" red) ⇒ behaviors' CE-2447 re-authoring |
| CE-5018 | live `--mode all`, bt-doors, a possessed TKB 112 rams House A | breach entity 1201 on all three perspectives (before: none) |
