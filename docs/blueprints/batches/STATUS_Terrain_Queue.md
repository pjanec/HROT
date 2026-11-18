<!--STATUS
state: LIVE · updated: 2026-11-18 · current-answer: §1
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Bug_Queue.md — the queue (11 rows)
  - PROTOCOL_Autonomous_Lanes.md — how this lane works it
-->

# STATUS — terrain, the queue

## 1. Now
IN PROGRESS: **CE-5018** (a possessed bulldozer's bump makes no breach in `--mode all`) — step ①: reproduce in `--mode all` (bt-doors), then find which node runs the contact rule.
Self-wake-up: `terrain self-wake-up` (`trig_01MWxGq2irAJ7jQwQQZ4eRUR`, hourly at :57) — created at the run's start without an approval prompt.

## 2. The queue (in order)

| tier | row | state | sha · rail · scenario |
|---|---|---|---|
| P3 | CE-5018 bump makes no breach in a cluster | IN PROGRESS | |
| P3 | CE-4630 a possessed soldier pushed back from a bumped hole | TODO — ⚠ not reproduced in the EDITOR (2026-11-16, rails `BuildingDamageBakeTests.CE4630_*`); next: the cluster (likely the same root as CE-5018) | |
| P3 | CE-5019 a charge's crater blocks its own breach | TODO | |
| P4 | CE-2446 an AP mine bursts under a man and hurts nobody | TODO | |
| P4 | CE-2444 an AT mine does not hurt an M1, hurts a man 35 m off | TODO — ⚠ largely answered at `0573b3a82` (belly armour, per-mine warheads; rail `MineClearerTkbTests.AnAntiTankMine_*`); verify `mf-at-vs-vehicle` | |
| P4 | CE-4655 after a strike the squad skirts the field along its edge | TODO | |
| T2 | CE-4656 a grey block over a baked static car | TODO | |
| T2 | CE-4401 scale-rig roads at height 0 | TODO | |
| T2 | CE-3428 pack the prefilled navmesh tiles | TODO | |
| T2 | CE-3426 felling a lone tree, fragments in woods | TODO | |
| T2 | CE-3440 a breach hot-path allocation rail red 1 in 3 | TODO | |

## 3. Taken from new handoffs
- `HANDOFF_Coordinator_To_Terrain_Bug_Queue.md` at 2026-11-18 (merged coordinator at the run's start).

## 4. Notes sent / received
- none yet

## 5. Gates (rule 8)
*(per row)*
