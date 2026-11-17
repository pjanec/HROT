<!--STATUS
state: LIVE — REPORTED (CE-3454, the back-out and the civilian signal-stop built; the SUMO car not built)
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
| **MR-5** the MineStrike back-out | ✅ built — `mf-strike-and-halt` back-out row ✔ (red without it), `mf-at-vs-vehicle` ✔, `mf-recon-and-mark` ✔; reds pre-existing: CE-2446, CE-2447 | `TrailWalk` (one walker): the mover's own breadcrumbs, newest first, cut after the first point outside the field; `MineStrike` orders every live man `BackOut` (walk out, then hold); a struck vehicle (or a lone man) backs out itself, reverse allowed. R-2125's interim ENDED |
| checkpoint follow-up: the civilian stops on the signal | ✅ built — all six checkpoint scenarios pass (`cp-civilian-stops` new) | the checkpoint sends the signalled vehicle `StopAtSignal` (a reaction; the VBIED / ram never take it): it slows, stops at the line, holds while searched, ends when its people are back (released) or after 20 s untouched (waved on); scenario `cp-civilian-stops` (route straight through) |
| checkpoint follow-up: the SUMO car | ⏳ not built | not measured whether a SUMO-driven car takes the reaction (SUMO drives it); likely needs a traffic-side hold (backend's SUMO bridge) |

## §1 Ids allocated

R-2136 (lane decision, for the user's review: a firing-seat man opens fire on his own only within 60 m). New behaviours: `FireFromSeat`, `BackOut`
(names, no component ids). Tracker: CE-2446 (terrain's: an AP mine bursts under a man and hurts nobody), CE-2447 (mf-no-way-round: a man enters the field before the squad halts), CE-2448 (cp-vbied's golden does not repeat).

## §2 Findings

| finding | evidence | where |
|---|---|---|
| the drive-by / swarm-raid pillions opened fire from ~170 m (the start line, the approach) — the surprise gone, the pass never came | first scenario run after the row | R-2136 (100 m) |
| the drive-by's RIDER (stopped-only seat) engaged after the pass, drew fire, the pillion was hit, the escape failed | `ua-moto-drive-by` golden diff at 40 s | PF-3/PF-4 exclusion |
| `ua-moto-swarm-raid`'s "the pillions fire at the post" row was anchored after the EAST bike's arrival — written while the pillions never fired; they fire at the post at 33.8 s as the bikes converge | run report | row now says "at the post" (`at: *Post Guard`), no anchor |
| under `HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1`, rider scenarios threw reading the HOST's `CarrierStations` on the spawn frame | `r2` logs | the seat test reads the man's own state |
| at 100 m the drive-by's pillion, his ordered target down at the pass, took on the second sentry ~70 m off the road, lost the duel; bike, rider and pillion killed at the post | probe capture (`t` 34–50 s) | R-2136 tightened to 60 m |
| `mf-strike-and-halt` "a man is hurt" is red — an AP mine bursts under the lead man (0.3 m) and nobody's Health moves; red WITHOUT the back-out too (stash run at the same tree) | dense capture `t` 29–34 s | CE-2446, terrain |
| `mf-no-way-round` "nobody enters the field" red at 49.5 s (the squad reacts at 55.5 s); red at the same tree WITHOUT the back-out too (no mine goes off) | stash run | CE-2447 (behaviors, MS-B4) |
| K-6's RELEASE never re-boarded anyone: sent as a reaction (refused by the running "hands up") and keyed `Vehicle` (Embark reads `vehicle`) — the committed `cp-routine-car` golden shows the driver out from ~50 s to the end | `cp-civilian-stops` probe; base golden | fixed: an instruction, the right key; both car scenarios now check "the driver boards again" |
| the released car read as a threat the moment it moved off the line | `cp-civilian-stops` run | the watch waves through the last vehicle it searched (not held) |

## §3 Gates

| gate | result |
|---|---|
| T-1 the features' own suites | `BasicInfantrySopTests` 58/58 (with ShippedTrees) · `MineStrikeBehaviorTests` 5/5 · `CheckpointBehaviorTests` 9/9 · `Fdp.Toolkits` watch / ladder / custody 14/14 |
| mutants (each new rail red-proved, restored, none committed) | CE-3454 4 + 2 (range, rider) · back-out 7 · signal-stop / release 5 — all red |
| feature scenarios (deterministic editor run) | ✅ `ua-animal-horse-rider` (no scripted order), `ua-moto-swarm-raid` 11/11, `ua-moto-drive-by`, `ua-moto-drive-by-grenade` · ✅ all six checkpoint scenarios incl. `cp-civilian-stops` · ✅ `mf-at-vs-vehicle`, `mf-recon-and-mark` · ⚠ `mf-strike-and-halt` red on CE-2446 only (its back-out row green, red without the change), `mf-no-way-round` red on CE-2447 (red at the base too) |
| goldens | re-captured for the 10 scenarios today's changes moved; a fresh repeat: 9 `same`, `cp-vbied` differs run to run (CE-2448) |
| parallel guards (`HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1`) | the new rails pass; four OLDER SOP rails and the rider / AT-mine scenarios throw on reads outside this work (`EntityInfo` of a same-frame entity, `Armament.SeenControlled`, `PlaceKnowledgeTable`) — ⚠ not measured at the base |
| ⏳ not run | `stepped-hash.py` 1/4/8 on today's scenarios |
