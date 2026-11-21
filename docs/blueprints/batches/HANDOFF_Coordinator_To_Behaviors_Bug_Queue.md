<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18
current-answer: the whole file
related-designs:
  - PROTOCOL_Autonomous_Lanes.md — ⭐ HOW you work: the loop, the rate-limit rules, new handoffs, decisions skipped and written, notes
  - ../PLAN_Bug_Priorities.md — WHY this order (P1…P5 the user's priority, then the second tier)
-->

# HANDOFF — coordinator → `behaviors`: THE BUG QUEUE (13 rows)

**For:** behaviors. **From:** coordinator. **Dispatched at the commit that adds this file.** **When:** after CE-3454, your minefield / checkpoint follow-ups and CE-1967 (done).

🔒 User, `2026-11-18`: *"I would like to focus on the most serious bugs"* · *"I need the handoffs to contain more bugs so the lanes keep themselves busy
for longer … sync with others … aware of token rate limit … watching for new handoffs … skipping stuff needed decisions (reporting those in written)"*.

## ⭐⭐⭐ Read first: [`PROTOCOL_Autonomous_Lanes.md`](PROTOCOL_Autonomous_Lanes.md)
It is the HOW for every row below: push after every row, the status file `STATUS_Behaviors_Queue.md` always resumable, the loop after each row
(fetch · take new handoffs addressed to you · read notes · merge the others · next row), decisions SKIPPED and written to
`DECISIONS_Behaviors.md` with your lean, notes to other lanes, and what to take when the queue runs dry.

⭐⭐ **First action of the run: create your hourly SELF-WAKE-UP** (protocol §1a — `behaviors self-wake-up`, into your own session, unless it already exists), so a rate-limit stop does not end the run; keep it: when the queue is empty it checks for new handoffs addressed to you and takes them — never delete it (R-4292).

## The queue — in order (P = the user's priority, T2 = the second tier; ⭐ each row: reproduce · root-cause · fix with a rail · its scenario passes)

| tier | row | the bug (from the tracker — the row there is the truth) | note |
|---|---|---|---|
| P2 | **CE-4652** | OPEN `2026-11-18` (coordinator; found re-running `uf-squad-under-fire` on `coordinator` and on the agg rehearsal — the same red on both; owner: behaviors (stress / suppression)) — A MOVER WHO IS NEAR-MISSED DOES NOT DROP: | soldiers react to fire — likely ONE cause in the suppression / stress chain |
| P2 | **CE-4653** | OPEN `2026-11-18` (coordinator; found re-running `ua-fire-distribution` on `coordinator`; owner: behaviors) — A BADLY HURT MAN (10 HP) NEVER ASKS TO LIE DOWN (`HoldProne`): | soldiers react to fire — likely ONE cause in the suppression / stress chain |
| P2 | **CE-3461** | OPEN `2026-11-16` (found by item 7 of the regression suite — `ts-fire-discipline`'s new `checks.json`, editor tier; owner: behaviors (stress)) — THE MILITIA NEVER BREAK UNDER FIRE. | soldiers react to fire — likely ONE cause in the suppression / stress chain |
| P4 | **CE-4654** | OPEN `2026-11-18` (coordinator; user observation in the editor; owner: terrain (the mine's effect) + behaviors (the injury)) — A MAN WHO STEPS ON A MINE LOSES NEITHER HEALTH NOR MOBILITY: | the mine casualty: an AP strike wounds the LEGS — health lost, no walking; the squad's casualty drill · the squad reacts before a man enters a field |
| P4 | **CE-2447** | OPEN `2026-11-17` (behaviors, MS-B4; found re-running the minefield set after the `coordinator@56b704904` merge) — `mf-no-way-round`: A MAN ENTERS THE FIELD (49.5 s) BEFORE THE SQUAD REACTS `MinefieldAhead` (55.5 s). | the mine casualty: an AP strike wounds the LEGS — health lost, no walking; the squad's casualty drill · the squad reacts before a man enters a field |
| P1 | **CE-9023** |  · `convoy` → behaviors 📐 — `uc-urban-battle`'s *"and the convoy moves on"* row was a FALSE GREEN: the IED-immobilised APC kept "running" `FollowRoute` for good, which satisfied the row the instant a  | the immobilised vehicle's play must end (with backend's P1 locomotion result) |
| T2 | **CE-3462** | OPEN `2026-11-16` (found by item 7 of the regression suite — `ts-house-defenders`'s new `checks.json`, editor tier; owner: behaviors (stress / clearing)) — THE WAVERER NEVER BREAKS OR SURRENDERS. | fights |
| T2 | **CE-3463** | OPEN `2026-11-16` (found by item 7 of the regression suite — `ia-area-defend`'s new `checks.json`, editor tier; owner: behaviors) — THE GUARD DOES NOT FIRE FIRST. | fights |
| T2 | **CE-3464** | OPEN `2026-11-16` (found by item 7 of the regression suite — `rf-pinned-in-open`'s new `checks.json`, editor tier; owner: behaviors) — A PINNED MAN NEVER FIRES AND WALKS TOWARD THE HOUSE INSTEAD OF CRAWLING TO THE WRECK. | fights |
| T2 | **CE-3465** | OPEN `2026-11-16` (found by item 7 of the regression suite — `rf-wall-hugger`'s new `checks.json`, editor tier; owner: behaviors) — THE DEFENDERS NEVER THROW A GRENADE AT A MAN HUGGING THEIR WALL. | fights |
| T2 | **CE-3444** | OPEN `2026-11-12` (backend, from CE-4456 N-2) — a unit THRASHES between cover points in `uc-urban-battle`. | fights |
| T2 | **CE-2442** | OPEN `2026-11-16` (behaviors, CE-4624 MS-B4) — NO HQ ANSWERS AN `Engineers` REQUEST. | fights |
| T2 | **CE-3470** | OPEN `2026-11-17` (backend; found gating part E) — UNDER THE STRICT PARALLEL ENV EVERY MOUNTED-RIDER SCENARIO ABORTS ON AN UNDECLARED READ THAT IS DECLARED. | ⭐ the mounted riders abort under the strict parallel checks — blocks the war party and the hunt |

## Always available (when everything above is done, skipped or blocked)
Open tracker rows of your area marked `RW-M` or higher that no handoff holds — name each in your status before you start it.
