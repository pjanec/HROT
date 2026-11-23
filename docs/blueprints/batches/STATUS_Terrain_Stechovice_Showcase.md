<!--STATUS
state: LIVE
updated: 2026-11-23
current-answer: §1 what was measured · §2 the findings and their state · §3 what is left
stale-below: nothing
related-designs:
  - HANDOFF_Ui_To_Terrain_Stechovice_Battle_Showcase.md — the dispatch this answers (ui → terrain, scope at 92f251dc2)
  - HANDOFF_Ui_To_Terrain_Large_Terrain_Router_And_Cover.md — the first handoff (CE-5043 / CE-5044)
  - ../../designs/navig-2/Navigation_Design_v2_0.md §15 — the road graph's index, the router, NearestAccess (CE-5043, CE-5045)
  - ../../designs/air-attack/DESIGN_Air_Operations.md §4 / §9c — the strike's end ("egress route, or orbit (Hold)") — the A-10 finding
  - ../../designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8d — the HQ's busy rule (the Apache finding)
-->

# STATUS — terrain: the Stechovice battle showcase (`2026-11-23`)

## 1. Measured (headless editor, Preview, `/sim/run-to`, 4 cores)

| | |
|---|---|
| first-use import | 392 s (navmesh 353 s, 59 834 tiles); every stage now reports into the load (CE-11040) |
| load (imported) | ~60 s to `OperatingEdit`, 4 886 entities (4 431 of them doors) |
| speed, step 1/60 (the regression's) | **0.065×** real time — the simulation group ≈ 114 ms per tick; no single system dominates (≈ 20 systems each walk all ~4 500 entities per tick) |
| speed, step 1/6 (exploring) | 0.46× |
| path solver / EQS per execution | 175 / 173 ms (were 6.2 s / 7.6 s before CE-5043 / CE-5044) |

## 2. The handoff's findings (§3.2) and what was found running it

| finding | state | how measured |
|---|---|---|
| ⭐ **the editor HUNG for minutes** (not in the handoff) | ✅ **CE-5045** — the danger classifier walked every segment per node per route point; one shared `RoadGraphIndex` | a `dotnet-trace` of the hung process |
| `NearestAccess` 156 ms a call (not in the handoff) | ✅ CE-5043 — pruned exactly | the router's own rail |
| the riverbank platoons never fire at each other | ✅ **scenario** — `PlatoonDefend` without `SearchRadius` (< 40 m) posts every line squad on the platoon's exact centre: the men shuffled there for 300 s, at ease, zero contacts (though `/perception/los` saw the other bank clear at 225 m). `SearchRadius: 60` ⇒ both banks fire and hit within 120 s | live, `e4ad44e87` |
| the "outside the perception grid [0..1000]" warning | ✅ transient — the burst is at load, before the grid re-fits to the terrain; the village infantry (y ≈ 2 265, outside 0..1000) sees and fires | live |
| the A-10 flies on for hundreds of km after the strike | ⏳ **CE-5046** — confirmed: one bomb on the ZU-23 (500 → 391), then `nothing` and straight and level (`FixedWingModel`: "no order: straight and level", B9) — 28 km away by 320 s. The design's §4 sequence ends the attack with *"Egress route, or orbit (Hold)"*; `AirPlatformHold` was never built | live |
| the HQ re-tasked the Apache | ⚠ **CE-5047** — the HQ's busy rule counts only orders ITS run gave (`CommandOrders.Busy`, `CommandNodes.cs:122`), so a scenario-authored attack is "free"; the design records this gap (Command Chain §8d, the `Free`/`Busy` row). Not reproduced in two runs (the Apache stayed on its target, then hovered idle) | live + code |
| the village tank `FailedBlocked` | ⚠ **CE-5048** — NOT `OffRoad*` (`OffRoad.Authored` treats all-zero as "not modelled", `SurfaceClassGrid.cs:160`): the `VehicleFightFromCover` reaction drove the M1 (3.7 m wide) to a cover point **1.9 m from a brick house**, then the resumed `MoveToLocation` made no progress and failed. The cover point is hull-sized (`VehicleTemplates.cs:17`) for a hull parallel to the wall; the tank arrives in its driving pose | live |
| the village Blue `PlatoonAttack` starts late | ⏳ open — its squads fire from the start (300+ rounds by 150 s) but no hit was recorded on Red and the platoon had not advanced by 150 s; not yet root-caused | live |

## 3. Left

1. CE-5046 / CE-5047 / CE-5048 — behaviour fixes outside the terrain lane's code (air operations, HQ, vehicle tactics): leans in the tracker rows, for the user.
2. The village attack (above), then tune `checks.draft.json` → `checks.json` against a 1/60 run; ⚠ at 0.065× a 300 s run is ~77 min, past the regression's 60-min child limit — shorten `runFor` or pass `--verify-timeout`.
3. Preview, CAPABILITIES / USAGES rows, the optional air show.
