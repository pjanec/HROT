<!--STATUS
state: LIVE — running HANDOFF_Coordinator_To_Ui_Bug_Queue.md under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18 10:00 UTC
current-answer: §1
-->

# STATUS — ui, the queue

self-wake-up: `ui self-wake-up` (trig_0141R92G1qK6SHY6mh3nnMms, hourly at :02) — created 2026-11-18 09:02, no approval asked.

## 1. Now
IN PROGRESS: CE-1954 — a press on an unselected unit drags it on the threaded editor. Start: reproduce with HROT_THREADED_KERNEL on vs off.

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-1133 RTS right-click orders deselected units | NOT REPRODUCED (original); the RTS right-click found BROKEN at the merge (Convoy took the ground-click gesture) — FIXED under CE-1978 | (this push) | `RtsCommandToolTests` 13/13 incl. `CE1133_EachClickGesture_…` (red-proved) | n/a (editor input) |
| CE-1954 press on an unselected unit drags it (threaded) | IN PROGRESS | | | |
| CE-1470 gizmo buffer overflow drops primitives | TODO | | | |
| CE-1955 three Presentation reds | TODO | | | |
| CE-1298 FPS bare-hand fire does nothing | TODO | | | |
| CE-1283 Debug this unit on a commander | TODO | | | |
| CE-1067 remote terminal panel → global gizmo | TODO | | | |
| CE-1239 .ogg clip missing from a film | TODO | | | |
| CE-1241 15 seeded takes the scout cannot complete | TODO | | | |
| CE-1280 Brain tab: why-this-choice one line | TODO | | | |
| CE-1281 Brain tab: Recent changes scroll / menu | TODO | | | |
| CE-1246 Stride mode 2 host actions | TODO | | | |

## 3. Taken from new handoffs
- HANDOFF_Coordinator_To_Ui_Bug_Queue.md at 2026-11-18 09:00 (merged coordinator `5c0…` into ui)

## 4. Notes sent / received
- sent `NOTE_Ui_To_Convoy_Order_Gesture.md` — ConvoyOrder's GroundClick gesture removed (cross-lane), letter V

## 5. Gates (rule 8)
- CE-1133/1978: `Hrot.Presentation.Tests` RtsCommandToolTests · TheMenusAreGolden · MenuOrdersTests · Keymap · OrderMenu 63/0; `Hrot.Editor.Tests` OrderVocabularyTests 10/0. Goldens re-captured: only order ids + Convoy rows.
