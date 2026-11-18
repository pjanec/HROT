<!--STATUS
state: LIVE — a STEER (pre-empts, protocol §2 ④) · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - PROTOCOL_Autonomous_Lanes.md §6a — the new scenarios-per-row rule (R-4294)
  - HANDOFF_Coordinator_To_Terrain_Moved_Rows.md · HANDOFF_Coordinator_To_Ui_Moved_Rows.md — where your rows went
-->
# STEER — coordinator → `backend`: fewer scenarios per row, and nine rows moved to idle lanes

🔒 User, `2026-11-18`: *"backend seems to go through many scenarios so it does not do basically any fixing just running scenarios, so the other lanes
might take some of its tasks?"* · *"pls revise if running 40+ scenarios is a good idea and if there are not more efficient ways"*.

## ① From now: PROTOCOL §6a (R-4294)
Per row: the row's own scenario + the touched feature's set (1–5), background, `NO_BUILD=1`. The wide family (your 46 movement / 25 vehicle runs) once
per several rows — before `backend-wip-*` merges into `backend` — in the background while you build the next row. A red is pre-existing by the last
wide run's verdict; ⛔ no base re-run per row. ⭐ For CE-3452 now: its own scenario(s) decide it; the 25-vehicle run, if already going, finishes in the
background and counts as this batch's wide run.

## ② Rows MOVED — drop them from your queue (they belong to the receiving lane now, exchange rule 6)
| row | to | |
|---|---|---|
| CE-3424 (scale-1: 1 of 12 movers moved) · CE-3471 (a pig walked through its pen wall) · CE-4664 (TD-S8 ground heat marks) | **terrain** | ⚠ CE-4664: terrain declares `GroundHeatMarks` (component 493, ordinal 242) + `MarkWarmth` + a writer entry point; your `HeatSourceSystem` (CE-4658) CALLS it when a stand ends — your CE-4657 declares only `HeatSource` and `FireState` |
| CE-3468 (Points / Best blind under the parallel kernel) · CE-2438 (paused-preview + run-to does not repeat) · CE-1473 + CE-3451 (`Hrot.ClusterRunner.Tests` build + drift) · CE-1475 (Czech-Windows Sumo rails) · CE-1038 + CE-1046 (SimHost run counts, the load-sensitive replay rail) | **ui** | |

## ③ Unchanged
Everything else in your queue and its order: CE-3452 → CE-3457 → the thermal rows (minus CE-4664) → your remaining T2 rows.
