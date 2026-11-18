<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bug_Queue.md — the queue (22 rows) and its order
  - PROTOCOL_Autonomous_Lanes.md — how: push per row, the loop, decisions skipped and written
-->
# STATUS — backend, the queue

Handoff: [`HANDOFF_Coordinator_To_Backend_Bug_Queue.md`](HANDOFF_Coordinator_To_Backend_Bug_Queue.md). Started at `2e274c5fd` (`origin/coordinator` merged).
Self-wake-up: ✅ the scheduled task `backend self-wake-up` (hourly, :58), created `2026-11-18 08:58 UTC` without an approval prompt.

## 1. Now
IN PROGRESS: **CE-3458** (an unreachable move never fails) — reproducing; then CE-3452 and CE-9023 under the same principle.

## 2. The queue (in order)
| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-3458 P1 unreachable move never fails | IN PROGRESS | | | |
| CE-3452 P1 bike from a standstill reports FailedBlocked | TODO | | | |
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
(none yet)

## 5. Gates (rule 8)
(per row, in §2)
