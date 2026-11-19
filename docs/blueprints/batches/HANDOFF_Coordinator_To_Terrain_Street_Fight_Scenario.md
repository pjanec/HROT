<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Behaviors_Street_Fight.md — the street-fight rows (CE-4666…CE-4669) and the expect rows the scenario needs
  - ../../DESIGN_Building_Interiors.md — §3l the cover database (`TerrainCoverProvider`, the Cover layer)
-->
# HANDOFF — coordinator → `terrain`: A REAL-STREET FIGHT WE CAN RUN (CE-4672), and is the cover there? (CE-4673)

🔒 User, `2026-11-19`: *"I am especially concerned about the unrealistic street fights"*. The report that started it was on **real-1, which is not in
this repo** — behaviors fixed what they could reproduce on `pc-defend-house` (dead men acting, the idle squads, the suppression exits, the
window-sound flank turn) but the street's own part stays OPEN for want of a street: *Red's posts on the street centreline (`SearchRadius` 10, walls
9–18 m away) find no cover; Blue's base of fire stands in the open street.*

## Priority
Next, before your remaining building-edit rows (BE-4 toolbar); finish the row in progress first.

| order | row | builds | done when |
|---|---|---|---|
| 1 | **CE-4672** | ⭐ a feature-test scenario **`pc-street-fight`** on an IN-REPO terrain with a straight town street (~400 m × ~18 m between houses, houses with doors and windows on both sides — `city-battle`, `demo-city` or an OSM import small enough to commit; your call): the report's setup — Red Platoon `PlatoonDefend` with each squad's `Point` ON the street centreline, `SearchRadius` 10, the reserve 40 m behind; Blue Platoon `PlatoonAttack` 160 m down the street under a Blue HQ; behaviours unchanged. `description` + `checks.json` expect rows for what a realistic fight is: the base of fire within N m of cover / a wall and not standing after its first minute; defenders off the centreline within N s; men of one squad not all within 4 m of each other; nobody suppressing with no ammo or no live target; no behaviour on a dead man | the scenario runs; its rows are RED where the street fight is still wrong (that is the point) — the red rows are behaviors' targets; a NOTE to behaviors names them |
| 2 | **CE-4673** | ⭐ is the COVER there? Along that street: does `TerrainCoverProvider` produce points along both house rows (walls, door recesses, corners), at what spacing, and within 10 m / 25 m of each centreline post? Measure (the `Cover` layer, the provider's query) and fix a DATA gap in your provider (e.g. no points on an imported building's facade, none at corners) | a rail: every facade of the street's houses has cover points at ≤ N m spacing; the measured "points within 10 / 25 m of each post" written in the row — so behaviors knows whether "no cover found" is the data or the search |

§6a per row. Fold findings into `DESIGN_Building_Interiors.md` §3l (obligation ⑤).
