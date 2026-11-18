<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — ⭐ HOW you work: the loop, the rate-limit rules, new handoffs, decisions skipped and written, notes
  - ../PLAN_Bug_Priorities.md — WHY this order (P1…P5 the user's priority, then the second tier)
-->

# HANDOFF — coordinator → `ui`: THE BUG QUEUE (12 rows)

**For:** ui. **From:** coordinator. **Dispatched at the commit that adds this file.** **When:** after air combat (CE-4647) and your user-observation rows.

🔒 User, `2026-11-18`: *"I would like to focus on the most serious bugs"* · *"I need the handoffs to contain more bugs so the lanes keep themselves busy
for longer … sync with others … aware of token rate limit … watching for new handoffs … skipping stuff needed decisions (reporting those in written)"*.

## ⭐⭐⭐ Read first: [`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md)
It is the HOW for every row below: push after every row, the status file `STATUS_Ui_Queue.md` always resumable, the loop after each row
(fetch · take new handoffs addressed to you · read notes · merge the others · next row), decisions SKIPPED and written to
`DECISIONS_Ui.md` with your lean, notes to other lanes, and what to take when the queue runs dry.

## The queue — in order (P = the user's priority, T2 = the second tier; ⭐ each row: reproduce · root-cause · fix with a rail · its scenario passes)

| tier | row | the bug (from the tracker — the row there is the truth) | note |
|---|---|---|---|
| T2 | **CE-1133** | OPEN (ui) — RTS RIGHT-CLICK MOVE ALSO ORDERS UNITS THAT ARE NO LONGER SELECTED. | the editor |
| T2 | **CE-1954** | OPEN `2026-11-15` (ui; found by CE-4342) — A PRESS ON AN UNSELECTED UNIT DRAGS IT ON THE THREADED EDITOR, NOT ON THE SERIAL ONE: | the editor |
| T2 | **CE-1470** | OPEN `2026-11-10` (ui; measured on Windows, CE-4388) — THE MAP'S GIZMO BUFFER OVERFLOWS UNDER LOAD AND DROPS PRIMITIVES SILENTLY: | the map under load: the gizmo buffer drops primitives silently |
| T2 | **CE-1955** | OPEN `2026-11-16` (ui; found by the minefields batch CE-4624) — THREE `Hrot.Presentation.Tests` REDS THAT PREDATE IT: | tests |
| T2 | **CE-1298** | OPEN `2026-10-23` (ui, maybe cross-lane behaviors) — FPS WITH THE BARE HAND: FIRE SHOWS A LOCK BOX AND DOES NOTHING; IT SHOULD BE A MELEE ATTACK. | FPS: the bare-hand fire does nothing (a melee attack wanted) |
| T2 | **CE-1283** | OPEN `2026-10-23` (ui) — *DEBUG THIS UNIT* ON A PLATOON COMMANDER CHANGES NOTHING; UTILITY SCORES SHOULD BE A GLOBAL SWITCH. | Debug this unit on a commander changes nothing |
| T2 | **CE-1067** | OPEN `2026-10-14` (ui; found building CE-1066, PRE-EXISTING) — A REMOTE TERMINAL'S PANEL CANNOT REACH A GLOBAL GIZMO. | a remote terminal's panel cannot reach a global gizmo |
| T2 | **CE-1239** | OPEN `2026-10-21` (ui; found building CE-4038 B11) — A CLIP FROM AN `.ogg` FILE IS MISSING FROM A FILM'S SOUND TRACK. | films: an .ogg clip missing from the sound track · 15 seeded takes the scout cannot complete |
| T2 | **CE-1241** | OPEN `2026-10-21` (ui; measured by CE-4038's scout of the 55 seeded takes) — 15 SEEDED TAKES THE SCOUT DOES NOT COMPLETE: 14 MISSING A BEAT, 1 NAMING A UNIT THE SCENARIO LACKS. |  |
| T2 | **CE-1280** | OPEN `2026-10-23` (ui) — BRAIN TAB: *WHY THIS CHOICE* PACKS SEVERAL TESTS INTO ONE "Decision" LINE. | the Brain tab: 'why this choice' packs several tests · Recent changes scrolls with the panel |
| T2 | **CE-1281** | OPEN `2026-10-23` (ui) — BRAIN TAB: *RECENT CHANGES* SCROLLS WITH THE WHOLE PANEL; ITS ITEMS HAVE NO MENU. |  |
| T2 | **CE-1246** | OPEN `2026-10-22` (ui; found by CE-1150) — STRIDE MODE 2 HAS NO HOST ACTIONS: the Details panel is there, Properties… and View ▸ Details are not. | Stride mode 2 has no host actions |

## Always available (when everything above is done, skipped or blocked)
Open tracker rows of your area marked `RW-M` or higher that no handoff holds — name each in your status before you start it.
