<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — ⭐ HOW you work: the loop, the rate-limit rules, new handoffs, decisions skipped and written, notes
  - ../PLAN_Bug_Priorities.md — WHY this order (P1…P5 the user's priority, then the second tier)
-->

# HANDOFF — coordinator → `terrain`: THE BUG QUEUE (11 rows)

**For:** terrain. **From:** coordinator. **Dispatched at the commit that adds this file.** **When:** now — your observation rows are done.

🔒 User, `2026-11-18`: *"I would like to focus on the most serious bugs"* · *"I need the handoffs to contain more bugs so the lanes keep themselves busy
for longer … sync with others … aware of token rate limit … watching for new handoffs … skipping stuff needed decisions (reporting those in written)"*.

## ⭐⭐⭐ Read first: [`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md)
It is the HOW for every row below: push after every row, the status file `STATUS_Terrain_Queue.md` always resumable, the loop after each row
(fetch · take new handoffs addressed to you · read notes · merge the others · next row), decisions SKIPPED and written to
`DECISIONS_Terrain.md` with your lean, notes to other lanes, and what to take when the queue runs dry.

## The queue — in order (P = the user's priority, T2 = the second tier; ⭐ each row: reproduce · root-cause · fix with a rail · its scenario passes)

| tier | row | the bug (from the tracker — the row there is the truth) | note |
|---|---|---|---|
| P3 | **CE-5018** | OPEN `2026-11-16` (terrain lane; found measuring CE-4630) — IN A CLUSTER (`--mode all`) A POSSESSED BULLDOZER'S BUMP MAKES NO BREACH: | breaching: the cluster must match the editor |
| P3 | **CE-4630** | OPEN `2026-11-16` (coordinator; user observation, for `terrain`) — A POSSESSED (FPS) SOLDIER CANNOT ENTER A BUMPED HOLE IN A BUILDING WALL: "the character goes a bit in but is pushed back", in any posture — though the hole is door | breaching: the cluster must match the editor |
| P3 | **CE-5019** | OPEN `2026-11-16` (terrain lane; found measuring CE-4630) — A DEMOLITION CHARGE'S CRATER BLOCKS ITS OWN BREACH: | breaching: the cluster must match the editor |
| P4 | **CE-2446** | OPEN `2026-11-17` (behaviors, found by `mf-strike-and-halt`; terrain owns the fix — cross-lane filing) — AN ANTI-PERSONNEL MINE BURSTS UNDER A MAN AND HURTS NOBODY. | the mines' effects and the clearance — with behaviors' CE-4654 (the injury) |
| P4 | **CE-2444** | OPEN `2026-11-16` (behaviors, found by `mf-at-vs-vehicle`; terrain owns the fix) — AN ANTI-TANK MINE'S BURST DOES NOT HURT AN M1 AND HURTS A MAN ~35 M AWAY. | the mines' effects and the clearance — with behaviors' CE-4654 (the injury) |
| P4 | **CE-4655** | OPEN `2026-11-18` (coordinator; user observation in the editor; owner: terrain (route pricing / the field's known area) + behaviors (the avoid / back-out)) — AFTER A STRIKE THE SQUAD SKIRTS THE FIELD ALONG ITS EDGE, BUT THE MINES  | the mines' effects and the clearance — with behaviors' CE-4654 (the injury) |
| T2 | **CE-4656** | OPEN `2026-11-18` (coordinator; user observation; owner: terrain, with ui for the draw) — A STATIC CAR OBSTACLE IS DRAWN AS A CAR AND, OVER IT, A GREY SOLID BLOCK THE SIZE OF ITS BOUNDING BOX — seemingly after the navmesh re-bake. | visuals: the grey box over a baked car (same family as CE-1966) |
| T2 | **CE-4401** | OPEN `2026-11-10` (user finding → coordinator, terrain import programme) — ON THE SCALE-RIG SCENARIO THE ROADS ARE AT HEIGHT 0 WHILE THE TERRAIN IS AT ≈ 500 m: | scale and tools |
| T2 | **CE-3428** | OPEN `2026-11-10` (backend, from CE-4393) — PACK THE PREFILLED NAVMESH TILES: | scale and tools |
| T2 | **CE-3426** | OPEN `2026-11-10` (backend, L-7 trees) — FELLING A LONE TREE, AND FRAGMENTS IN WOODS: | scale and tools |
| T2 | **CE-3440** | OPEN `2026-11-11` (backend) — `BuildingBreachTests.HotPath_ThroughABreachedWall_AllocatesNothing…` WENT RED IN 1 OF 3 FULL `Terrain/Recast` RUNS: | scale and tools |

## Always available (when everything above is done, skipped or blocked)
Open tracker rows of your area marked `RW-M` or higher that no handoff holds — name each in your status before you start it.
