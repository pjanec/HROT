<!--STATUS
state: LIVE — dispatched, not started — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-25
current-answer: the whole file
related-designs:
  - STATUS_Terrain_Stechovice_Showcase.md — your Stechovice battle work; continue it
  - ../../DESIGN_Terrain_Authoring.md — §8 the authored Bridge feature (deck 1.5 m, ramps 6 m, width from the feature's pad), §18k the bridge roads
  - ../../DESIGN_Terrain_Import_UTM.md — the importer (Corridors.cs: an OSM bridge levels nothing and gets no deck — item ④)
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — the LAID bridge (CE-4692, yours too): the same deck shape; this handoff uses the AUTHORED feature that exists today
  - ../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — PlatoonDefend / PlatoonAttack the two sides run
  - PROTOCOL_Autonomous_Lanes.md — how you work
-->

# HANDOFF — coordinator → `terrain`: THE STECHOVICE BATTLE — a bridge and a BLUE BRIDGEHEAD (CE-4694)

**For:** terrain (owner of the Stechovice showcase). **From:** coordinator. **Dispatched at the commit that adds this file**; ⛔ scope FROZEN
there; a later document that invalidates an item ⇒ STOP that item, do the rest (R-106).

🔒 User, `2026-11-25`: *"I would like there will be a bridge in the Stechovice battle scenario. Where the soldiers on the opposite sides of the
river are firing at each other. By adding the bridge, they would at least look like they are fighting for the bridge"* · *"yes, write the
handoff, blue holds bridgehead"*.

## Priority
After the row in progress. Continue `STATUS_Terrain_Stechovice_Showcase.md` (a new §, the bridgehead); decisions to `DECISIONS_Terrain.md`.

## ⭐ Measured by the coordinator's live run (`2026-11-25`, editor headless, a scratch scenario — nothing committed)

| fact | evidence |
|---|---|
| the two real OSM road bridges (south ≈ scenario (1380, 180), north ≈ (875, 4080)) have **no deck**: only ground at water level (z 199–201; banks 204–208) — the net carries no bridge tags, so the importer levelled the road corridor across the river bed | `GET /terrain/levels` along the bridge; `Corridors.cs:31` |
| over the south bridge a **squad crossed at water level**; a HMMWV crossed twice (from the road, `roadUse: StronglyPrefer`) then failed (`FailedBlocked` after 3 m) in a clean run; **M1 tanks never** — `PlatformMove ended: Failure` ≈ 0.12 s after the order, `RouteHandle 0` (no route planned) | `/orders`, `/entities/{id}/journal` |
| ⚠ some **same-bank** tank moves also fail at once (one at (1345, 292) → (1330, 320)), others succeed — vehicle routing on this terrain is patchy; the API gives no reason | same |
| an **authored Bridge feature** (TKB 8818, `TerrainFeature{Kind:10}` + `EditablePolyline`, two points) at **y ≈ 1590, (762 → 928)** works for infantry: the squad crossed ON the deck (z ≈ 204 over water at 199.4) in ≈ 300 s; without it the squad stayed on its bank. ⚠ at y ≈ 1570 the east end fell inside OSM house `w422408926` — check both ends against buildings | `/terrain/query`, the samples |
| the **tank did not use the placed bridge**: it planned 1.5 km to the south OSM bridge; ordered onto the deck it failed at once. ⚠ likely cause (unverified): the deck is **4 m** wide (pad 0) and the vehicle navmesh's agent radius leaves no polygon on it for a 3.7 m hull | same |
| ⚠ **4 300+ `[Perception] … outside the perception grid [0..1000]`** warnings: the perception grid is 1 km wide on this ~4 km terrain — at the bridge and the riverbank nobody saw or heard anybody in that run | the log — ⛔ not checked in the battle scenario itself |
| timing: first import 198 s (navmesh 59 834 tiles in 149 s), load ≈ 37 s, the bridge's overlay rebake 18 tiles in 1.8 s; the sim ran at ≈ 0.2–0.3× real time on the VM | the log |

## What you build

| # | item | done when |
|---|---|---|
| ① | ⭐ **the perception grid covers the terrain** — measure it in `stechovice-battle` first; if it is the same 1 km, root-cause why (a fixed size, not fitted to the terrain?) and fix it so it covers the whole terrain (the grids fit the terrain elsewhere — Terrain World §4.4) | no "outside the perception grid" warning in a battle run; units at the riverbank SEE each other (a scenario row) |
| ② | **why tanks get no route** — root-cause (claim table): the 4 m deck vs the vehicle agent radius; the patchy same-bank failures; the OSM bridge corridor. Fix what is HROT's; the scenario's bridge is **≈ 8 m wide** (the feature's pad) unless the root cause says otherwise | a tank ordered across the placed bridge reaches the far bank (a scenario row) |
| ③ | ⭐ **the bridge and the BLUE BRIDGEHEAD** in `stechovice-battle`: an authored bridge at the riverbank fight (y ≈ 1590, both ends on open ground, clear of houses), and the story — **Blue holds a bridgehead on the WEST bank** (a squad or two dug in at the west end, PlatoonDefend facing west/south-west) with the rest of its platoon, the IFV and the tanks **supporting by fire from the east bank** over the bridge; **Red counter-attacks** from its terrace (PlatoonAttack on the bridgehead, its technicals and T-72s supporting) to take or deny the bridge; Red's demolition team optional (a charge on the deck — the authored bridge collapses as a building does) | the scenario's own rows below |
| ④ | **file a row** (your block): the importer gives an OSM bridge a real DECK (the bridge / layer tags kept in the net — CE-4403 — and a walkable deck at road height through the ONE deck geometry of the bridge-laying design, BL-4), so the real bridges are crossed at bridge height and drawn right by an external IG — ⛔ not built here | the row exists, linked to this handoff |

## ⛔⛔ DONE = THE FEATURE-TEST SCENARIO PASSES (R-4277)

`stechovice-battle`'s description states **What** happens at the bridge and how to **Judge** it, and its `checks.json` carries the same as
`expect` rows — ⭐ the outcomes as **OBJECTIVES** (Scenario Rules), keyed on state, never a time (the bake is wall-clock paced):

| objective / row | expect |
|---|---|
| **"Bridgehead held"** — a HoldArea at the bridge's west end, Blue only | Blue holds it at the end (or, if Red wins, the row says Red took it — say which the run gives and keep it deterministic) |
| **"Bridge stands"** (only if the demolition team is in) | the bridge's state at the end |
| both sides engage | Blue fires at Red and Red at Blue across the river and at the bridgehead (shot cues) |
| they see each other | no unit at the riverbank is outside the perception grid; a riverbank SPOT is reported on each side |
| a tank crosses | a Blue tank ordered over the bridge reaches the west bank (②) |

⛔ Not done until `scripts/scenario-regression.sh stechovice-battle` passes in the deterministic editor run; the report lists every row and its
result, and the scenario's other fights (the village, the air war) still pass their rows.

## Gates (CLAUDE.md rule 8)
The perception / navigation suites the fixes touch + the bridge rails (`FeatureKindsTests`, `BridgeClosureTests`) + the scenario; the parallel
env on any system you change. Fold any root cause into its owning design (obligation ⑤ / the investigation rule). Report the ids you allocated.
