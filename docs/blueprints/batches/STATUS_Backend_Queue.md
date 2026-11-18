<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bug_Queue.md — the queue (22 rows) and its order
  - PROTOCOL_Autonomous_Lanes.md — how: push per row, the loop, decisions skipped and written
-->
# STATUS — backend, the queue

Handoff: [`HANDOFF_Coordinator_To_Backend_Bug_Queue.md`](HANDOFF_Coordinator_To_Backend_Bug_Queue.md). Started at `2e274c5fd` (`origin/coordinator` merged).
Self-wake-up: ✅ the scheduled task `backend self-wake-up` (hourly, :58), created `2026-11-18 08:58 UTC` without an approval prompt; it fired at 09:58 and 10:58 into this session, no approval asked.

## 1. Now
IN PROGRESS: **CE-3452** (a bike from a standstill reports FailedBlocked while it moves) — reproducing (test-town, a motorcycle at (40, 200) east to (230, 200)); lead: vehicle path progress is speed × the path tangent at the current progress, so a path that starts at an angle to the heading gains nothing while the bike drives and the 4 s no-progress watchdog fails it.

## 2. The queue (in order)
| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-3458 P1 unreachable move never fails | ✅ DONE | (this commit) | `MoveToLocationRoadUseTests.CE3458_*` 2/2, red-proved (no OwnMoveFailed / no OwnRouteFailed ⇒ red); neighbours 79/79 | 46 movement scenarios: 40 PASS; 6 RED AT THE BASE TOO (same checks): pc-ambush-bypass, pc-attack-dry, pc-urban-ambush, uc-reinforce-dry, uc-request-chain-dry, uc-urban-battle — behaviors' scenarios, open rows CE-2284 / CE-2352 / CE-2277 / CE-4042 / CE-2310 / CE-9023 |
| CE-3452 P1 bike from a standstill reports FailedBlocked | IN PROGRESS | | | |
| CE-9023 P1 disabled vehicle runs its route forever (the locomotion half) | TODO | | | |
| CE-3457 P5 vehicles never yield to animals | TODO | | | |
| CE-3468 T2 Points / Best blind under the parallel phase | TODO | | | |
| CE-3469 T2 six SimHost rails red under the parallel env | TODO | | | |
| CE-2440 T2 cc-battalion not thread-count independent | TODO | | | |
| CE-3445 T2 rare mode-3 stepped divergence | TODO | | | |
| CE-2438 T2 paused-preview + run-to does not repeat | TODO | | | |
| CE-2448 T2 cp-vbied golden does not repeat | TODO | | | |
| CE-3449 T2 five goldens differ from t = 0 | TODO | | | |
| CE-3424 T2 scale-1: 1 of 12 movers moved | TODO | | | |
| CE-3425 T2 scale-1 frame cost | TODO | | | |
| CE-4395 T2 the brain's cost at 1 000 men | TODO | | | |
| CE-3460 T2 the root objective never aggregates | TODO | | | |
| CE-3471 T2 a pig walked through its pen wall | TODO | | | |
| CE-1038 T2 SimHost run counts differ | TODO | | | |
| CE-1046 T2 AfterBranch_SimGroupsReEnabled under load | TODO | | | |
| CE-1473 T2 ClusterRunner.Tests does not build | TODO | | | |
| CE-3451 T2 six ClusterRunner.Tests rails drifted | TODO | | | |
| CE-1475 T2 SumoNetPlacementTests on a Czech Windows | TODO | | | |
| CE-167 T2 re-check, close if stale | TODO | | | |

## 3. Taken from new handoffs
(none yet)

## 4. Notes sent / received
- ⬅ `NOTE_Behaviors_To_Backend_NearMiss_And_LyingCap.md` ② (my `CE4649` lying-cap allocation rail red under the strict guards) — ✅ DONE: the process-wide AccessTrace growing inside the one window; three windows, cheapest 0 B; guarded 4/4, red-proved. ➡ answered in `NOTE_Backend_To_Behaviors_LyingCapRail.md`.

## 5. Gates (rule 8)
(per row, in §2)
