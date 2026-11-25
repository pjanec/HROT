<!--STATUS
state: LIVE
updated: 2026-11-25
current-answer: §4 THE BRIDGEHEAD (CE-4694, in progress) · §1 what was measured · §2 the findings and their state · §3 what is left
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
| the riverbank platoons never fire at each other | ✅ **scenario** — `PlatoonDefend` without `SearchRadius` (< 40 m) posts every line squad on the platoon's exact centre: the men shuffled there for 300 s, at ease, zero contacts (though `/perception/los` saw the other bank clear at 225 m). `SearchRadius: 60` (`e4ad44e87`). ⚠ **CORRECTED:** the run that showed "both banks fire" still loaded the OLD scenario — a dev editor reads the NAS (`data/shared`), seeded from the repository only at its start (`ContentSeed.SeedIntoNas`, R-4097), and that editor had started before the edit; Red fired there and not in the previous run of the same scenario ⇒ the riverbank fire varies run to run, and the fix is verified only by reading the platoon's posts. ✅ **VERIFIED `2026-11-23`** (an editor started AFTER the edit): `SearchRadius` spreads the posts (three distinct, radii 8–25 m); the battle positions it surveys are BUILDINGS, so on the first Red pick two of three posts sat behind house walls and the riverbank was silent for 120 s — the Red platoon moved 44 m south (`e12f7b711`) where `/terrain/query` (buildings included; the generator's DEM + woods search cannot see houses) sees 8 of 9 lines to the Blue bank ⇒ **both banks fire and hit within 120 s: Blue 42 shots / 23 hits, Red 118 / 38** | live |
| the "outside the perception grid [0..1000]" warning | ✅ transient — the burst is at load, before the grid re-fits to the terrain; the village infantry (y ≈ 2 265, outside 0..1000) sees and fires | live |
| the A-10 flies on for hundreds of km after the strike | ⏳ **CE-5046** — confirmed: one bomb on the ZU-23 (500 → 391), then `nothing` and straight and level (`FixedWingModel`: "no order: straight and level", B9) — 28 km away by 320 s. The design's §4 sequence ends the attack with *"Egress route, or orbit (Hold)"*; `AirPlatformHold` was never built | live |
| the HQ re-tasked the Apache | ⚠ **CE-5047** — the HQ's busy rule counts only orders ITS run gave (`CommandOrders.Busy`, `CommandNodes.cs:122`), so a scenario-authored attack is "free"; the design records this gap (Command Chain §8d, the `Free`/`Busy` row). Not reproduced in two runs (the Apache stayed on its target, then hovered idle) | live + code |
| the village tank `FailedBlocked` | ✅ **scenario** + ⏳ **CE-5049** — traced from spawn: the generator faced every unit 90° off (yaw 0 taken as north); the tank, told west, faced north, U-turned smoothly at 2.8 m/s and the watchdog's no-progress rule (CE-2304, 4 s) failed it at 4.2 s, still moving — generator fixed (`0494af3b5`); the rule's misreading of a turning vehicle is CE-5049. Also ⚠ **CE-5048** — NOT `OffRoad*` (`OffRoad.Authored` treats all-zero as "not modelled", `SurfaceClassGrid.cs:160`): the `VehicleFightFromCover` reaction drove the M1 (3.7 m wide) to a cover point **1.9 m from a brick house**, then the resumed `MoveToLocation` made no progress and failed. The cover point is hull-sized (`VehicleTemplates.cs:17`) for a hull parallel to the wall; the tank arrives in its driving pose | live |
| the village tank WEDGED at a house corner (after CE-5048) | ✅ **CE-5052** — not the route: the hull guard traced only points, and a back-out reversing while turning swung the long side onto the house's corner (~1 m inside); the guard now traces the hull's edges. Re-run: 0 `FailedBlocked` in 300 s (was 2, stuck from 149 s) | live + rail |
| "almost no vehicles, no SUMO cars, nobody fires at the aircraft" (user, `2026-11-23`) | ✅ **scenario** — 7 → 17 vehicles (village Blue: 3 M1, 2 Bradley, a HMMWV; village Red: 2 T-72, HMG / ATGM / DShK technicals; riverbank: an M1 and a T-72), every start snapped onto a SUMO lane the router can use (⛔ a `highway.track` start was `FailedUnreachable`), and SUMO traffic (`TrafficDemand` 250 cars, 60 pedestrians; ⚠ SUMO is not installed in the cloud container, so the cars are unverified here). 📐 The air defence DOES fire: in 90 s the ZU-23 15 rounds, the DShK 25, the radar SAM 3 missiles (the A-10 bombed the ZU-23 out at ~90 s and was shot down at ~95 s in the 150 s run) — the guns are their own weapon entities (`ZU-23-2`, `DShK HMG`). ⚠ The village Blue armour stays near its start doing `VehicleFightFromCover` at 90 s instead of advancing (the cover reaction pre-empts the move — with CE-5051) | live |
| "more armour, all soldiers playable, MG technicals on both sides, some air combat" (user, `2026-11-24`) | ✅ **scenario** — 32 vehicles (6 M1, 4 Bradley, 3 HMMWV, 5 T-72, 8 HMG + 3 DShK technicals, an ATGM technical); all **74 soldiers `Playable`** (Player Join D-9, unique slots ≤ 31 B); a 2v2 (two F-16s vs two MiG-29s, each on a named bandit, an `AirArena` 4 km round the valley). 📐 Live 240 s: the merge and fight are real (F-16 2 81 rounds, MiG 1 65, an F-16 down ~100 s), then the survivors climb to 8–13 km (**CE-5053**); every riverbank vehicle's cover move is `FailedUnreachable` (**CE-5054**). ⛔ `AirArmedSearch` (the CAP) was tried first: it ran an F-16 straight off the map | live |
| the village Blue `PlatoonAttack` starts late | ⏳ open — its squads fire from the start (300+ rounds by 150 s) but no hit was recorded on Red and the platoon had not advanced by 150 s; not yet root-caused | live |

## 3. Left

1. CE-5046 / CE-5047 / CE-5048 — behaviour fixes outside the terrain lane's code (air operations, HQ, vehicle tactics): leans in the tracker rows, for the user.
2. The village attack (above), then tune `checks.draft.json` → `checks.json` against a 1/60 run; ⚠ at 0.065× a 300 s run is ~77 min, past the regression's 60-min child limit — shorten `runFor` or pass `--verify-timeout`.
3. Preview, CAPABILITIES / USAGES rows, the optional air show.

## 4. ⭐ THE BRIDGE AND THE BLUE BRIDGEHEAD — CE-4694 ([handoff](HANDOFF_Coordinator_To_Terrain_Stechovice_Bridgehead.md), taken `2026-11-25` at `867fbbf6e`)

| # | item | state | sha | evidence |
|---|---|---|---|---|
| ① | the perception grid covers the terrain | ✅ DONE | `8bf0dd195` | 📐 4 455 warnings, ALL in the first perception tick (the epoch branch rebuilt on `[0..1000]²` before the fit), then fitted `[-358..4898] × [-249..5008]`; 14 more = SUMO traffic / aircraft 18–198 m past the 50 m margin. Fit first + 250 m margin; rail `LocalGridBuilderSystemTests.CE4694_*` (red with the old order). Terrain World §4.4 |
| ② | why tanks get no route | ✅ ROOT-CAUSED + FIXED | `101295e4c` | a probe on the real import (vehicle layer, the deck at y 1590): the deck WIDTH matters (4 m: the deck alone disconnects under the 1.8 m erosion; 8 m carries) but the BREAK was a **0.6 m cliff down the middle of the west-bank road**: a two-way road was two import corridors (one per SUMO edge) levelled to two profiles. Refuted on the way (measured): deck headroom fill, a deeper ramp-foot sink. Now ONE corridor per two-way road (importer revision 4, Ground Patches G-21a); after the re-import the east bank reaches the west road over the deck. Rail `ImporterTests.CE4694_*`. The same-bank pair (1345, 292) → (1330, 320) still has no vehicle path — open, see §4 notes |
| ③ | the bridge + the Blue bridgehead in the scenario | 🚧 IN PROGRESS | `bf0e9f68b` `de88a74de` `8c221d308` | 📐 exploratory runs (step 1/6): with Red's vehicles 70–100 m down the road the bridgehead died (1 of 18 by 150 s); pulled back to 150–250 m ⇒ **Blue holds** (6 of 18 vs Red 1 of 18 at 180 s; 50 bridgehead hits on Red, 30 Red hits on it in the first 60 s). ⚠ The TANK CROSSING still fails: instrumented, the hull was held by a RISE at the ramp's SOUTH EDGE (its centre 3 m off the bridge's axis) — turning onto the ramp (CE-5058) and, from the axis too, at y 1586.8 — being measured (the planned / rounded trajectory). `checks.json` written (23 rows: the draft's 15 + 8 bridge rows, runFor 180) — not yet run in the regression |
| ④ | file the OSM-bridge-deck row | ✅ DONE | `a3efdad9a` | **CE-5057** |
