<!--STATUS
state: LIVE · updated: 2026-11-18 09:05 UTC · current-answer: §1
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — the protocol this queue runs under
  - HANDOFF_Coordinator_To_Behaviors_Bug_Queue.md — the queue's source
-->

# STATUS — behaviors, the queue

Self-wake-up: `behaviors self-wake-up` (hourly at :02, into this session) — created 2026-11-18 09:02, no approval asked.
Started at the merge of `origin/coordinator` (2026-11-18 09:00).

## 1. Now

IN PROGRESS: CE-4652 — reproduce `uf-squad-under-fire` ("a mover is near-missed and drops") on `behaviors`; then CE-4653 / CE-3461 (likely the same
suppression / stress chain).

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-4652 a near-missed mover does not drop | IN PROGRESS | | | `uf-squad-under-fire` |
| CE-4653 a 10 HP man never asks to lie down | TODO | | | `ua-fire-distribution` |
| CE-3461 the militia never break | TODO | | | `ts-fire-discipline` |
| CE-4654 a man on a mine loses neither health nor mobility | TODO | | | |
| CE-2447 `mf-no-way-round`: a man enters before the squad reacts | TODO | | | `mf-no-way-round` |
| CE-9023 the immobilised APC's play never ends | TODO | | | `uc-urban-battle` |
| CE-3462 the waverer never breaks | TODO | | | `ts-house-defenders` |
| CE-3463 the guard does not fire first | TODO | | | `ia-area-defend` |
| CE-3464 a pinned man never fires, walks to the house | TODO | | | `rf-pinned-in-open` |
| CE-3465 no grenade at the wall-hugger | TODO | | | `rf-wall-hugger` |
| CE-3444 a unit thrashes between cover points | TODO | | | `uc-urban-battle` |
| CE-2442 no HQ answers an `Engineers` request | TODO | | | |
| CE-3470 the mounted riders abort under the strict parallel env | TODO | | | |

## 3. Taken from new handoffs

`HANDOFF_Coordinator_To_Behaviors_Bug_Queue.md` — 2026-11-18 09:00.

## 4. Notes sent / received

none yet.

## 5. Gates (rule 8)

per row, as it lands.
