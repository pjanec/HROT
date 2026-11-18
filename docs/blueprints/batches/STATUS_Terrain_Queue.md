<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Bug_Queue.md — the queue (11 rows)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works it
-->

# STATUS — terrain, the queue

## 1. Now
IN PROGRESS: **CE-4655** (after a strike the squad skirts the field along its edge) — step ①: reproduce.
Self-wake-up: `terrain self-wake-up` (`trig_01MWxGq2irAJ7jQwQQZ4eRUR`, hourly at :57) — created at the run's start without an approval prompt.

## 2. The queue (in order)

| tier | row | state | sha · rail · scenario |
|---|---|---|---|
| P3 | CE-5018 bump makes no breach in a cluster | ✅ DONE — no host published the node id, so the damage entity was requested from the brain node; `HrotNodeBuilder` now publishes `LocalNodeTopology`. Live `--mode all` after: the damage entity on SimHost / Player / Scenario | rails `HrotNodeBuilderTests.CE5018_*` (red-proved: singleton dropped ⇒ 2 red), `BuildingDamageSystemTests.CE5018_*` · ⚠ no scenario row can see it: the scenario run is one editor world (no cluster run exists yet) · UTM §8.3 #8 |
| P3 | CE-4630 a possessed soldier pushed back from a bumped hole | ⚠ NOT REPRODUCED — `--mode all`, bt-doors: a possessed rifleman walks through a dozer's blade hole AND a T-72's door-sized bump into House A and stays (editor and rails agree); lean: before CE-5018 the cluster made no breach at all; left open for the user's re-check | rails `BuildingDamageBakeTests.CE4630_*` (existing) |
| P3 | CE-5019 a charge's crater blocks its own breach | ✅ DONE — a burst that breaches a wall it touches (≤ 0.5 m, `craters.json` `wallContactM`) makes no crater; lane ruling **R-5064 (for review)** | rail `CraterTests.CE5019_*` (red-proved) · `bd-destruction` 10/10 + 2 new 'never in a pit' rows (red with the fix off) · Ground Patches G-7 |
| P4 | CE-2446 an AP mine bursts under a man and hurts nobody | ✅ DONE — the mines' warheads (terrain's own `0573b3a82`) had radii and 0 blast damage; they now take the reference library's blast value | rail `MineClearerTkbTests.AnAntiTankMine_*` (red-proved) · `mf-strike-and-halt` 6/6 · DESIGN_Minefields |
| P4 | CE-2444 an AT mine does not hurt an M1, hurts a man 35 m off | ✅ DONE (verified) — `0573b3a82` + CE-2446 | `mf-at-vs-vehicle` 6/6 |
| P4 | CE-4655 after a strike the squad skirts the field along its edge | TODO | |
| T2 | CE-4656 a grey block over a baked static car | TODO | |
| T2 | CE-4401 scale-rig roads at height 0 | TODO | |
| T2 | CE-3428 pack the prefilled navmesh tiles | TODO | |
| T2 | CE-3426 felling a lone tree, fragments in woods | TODO | |
| T2 | CE-3440 a breach hot-path allocation rail red 1 in 3 | TODO | |

## 3. Taken from new handoffs
- `HANDOFF_Coordinator_To_Terrain_Bug_Queue.md` at 2026-11-18 (merged coordinator at the run's start).

## 3a. Lane decisions (for the user's review)
| id | decision | what would change it |
|---|---|---|
| R-5064 | a burst that breaches a wall within 0.5 m of its face makes NO crater (a contact charge spends itself on the wall) | a big bomb landing at a wall should still crater ⇒ scale the skip by kg (e.g. only below the breach table's collapse mass) |

## 4. Notes sent / received
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
| CE-5018 | live `--mode all`, bt-doors, a possessed TKB 112 rams House A | breach entity 1201 on all three perspectives (before: none) |
