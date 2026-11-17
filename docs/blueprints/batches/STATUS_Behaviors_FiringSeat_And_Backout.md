<!--STATUS
state: LIVE — in progress
updated: 2026-11-17
current-answer: §0
stale-below: nothing
related-designs:
  - ../../designs/vehicle-tactics/DESIGN_Motorcycle.md — OWNS PF-1…PF-4 (the firing seat); as-built row "PF-3 the brain" is CE-3454
  - ../../designs/minefields/DESIGN_Minefields.md — OWNS MR-5 (back out on breadcrumbs); §11 "MS-B1 back-out" is the as-built
  - ../../designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — OWNS the checkpoint follow-ups (R-2133's ⏭)
-->

# STATUS — behaviors: the firing seat (CE-3454), the minefield back-out (MR-5), the checkpoint follow-ups

Dispatched by the coordinator `2026-11-17` (`56b704904`, "CE-3454 dispatched to behaviors first — the mounted archers need it"); started at the
merge of `coordinator@56b704904`.

## §0 Where it stands

| item | state | what |
|---|---|---|
| **CE-3454** a man in an open firing seat engages on his own | ✅ built | `BasicInfantrySop`'s FIRST row → React `FireFromSeat` (fires from the seat, no move; ends 3 s after the arc is clear). On a firing seat = his own `CanShoot` aboard (boarding strips it everywhere else but a weapon's gunner seat); an enemy SEEN within 1 s, within 60 m (R-2136), inside the seat's arc; no task; the ROE lets him fire; ⛔ never the rider's stopped-only seat (PF-3/PF-4). An order is a task, so the drive-by's ordered burst keeps it |
| **MR-5** the MineStrike back-out | ✅ built | `TrailWalk` (one walker): the mover's own breadcrumbs, newest first, cut after the first point outside the field; `MineStrike` orders every live man `BackOut` (walk out, then hold); a struck vehicle (or a lone man) backs out itself, reverse allowed. R-2125's interim ENDED |
| checkpoint follow-ups | ⏳ next | a civilian who slows on seeing the signal; the waved-on SUMO car (R-2133's ⏭) |

## §1 Ids allocated

R-2136 (lane decision, for the user's review: a firing-seat man opens fire on his own only within 60 m). New behaviours: `FireFromSeat`, `BackOut`
(names, no component ids). No tracker ids.

## §2 Findings

| finding | evidence | where |
|---|---|---|
| the drive-by / swarm-raid pillions opened fire from ~170 m (the start line, the approach) — the surprise gone, the pass never came | first scenario run after the row | R-2136 (100 m) |
| the drive-by's RIDER (stopped-only seat) engaged after the pass, drew fire, the pillion was hit, the escape failed | `ua-moto-drive-by` golden diff at 40 s | PF-3/PF-4 exclusion |
| `ua-moto-swarm-raid`'s "the pillions fire at the post" row was anchored after the EAST bike's arrival — written while the pillions never fired; they fire at the post at 33.8 s as the bikes converge | run report | row now says "at the post" (`at: *Post Guard`), no anchor |
| under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`, rider scenarios threw reading the HOST's `CarrierStations` on the spawn frame | `r2` logs | the seat test reads the man's own state |
| at 100 m the drive-by's pillion, his ordered target down at the pass, took on the second sentry ~70 m off the road, lost the duel; bike, rider and pillion killed at the post | probe capture (`t` 34–50 s) | R-2136 tightened to 60 m |
