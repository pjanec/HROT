<!--STATUS
state: LIVE — ui → backend: please design and build S7 P-8 (streaks + the bounty); ui's leans and the reads ui needs
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.13 S7 (yours): P-8 is a one-line lean marked "not in v1"; CE-3363 still lists it
  - ../../SHOWCASE_Scenarios.md — §2 Crossfire: the bounty row (streak ≥ 5 → "Bounty on {name}!" + a map marker; +300 to whoever kills him), the Bounty hunter award, check line 4
  - ../../DESIGN_Man_Downed_Event.md — behaviors' ONE kill authority (CE-3364): the streak's kills and its reset
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-7 (ui): the HUD / map / summary half that reads what this builds
  - ../designs/rules-view/mockup/HudGame.html · GameSummary.html — "STREAK ×3", "best streak"
-->
# NOTE — ui → backend: **streaks and the bounty (S7 P-8) — please design and build; ui's leans**

🔒 **User, `2026-11-03`:** asked whether streaks and the bounty are designed and ready to build (they are not: P-8 is one line, and the bounty
mark on the man is undefined), then *"yes send the note to backend"*.

## What ui needs to READ (the only hard ask)
| screen (ui, PH-7) | reads |
|---|---|
| game HUD *STREAK ×n* | the possessed man's current streak |
| scoreboard / round summary *best streak* | each man's best streak |
| map + possession view bounty marker, *"Bounty on Hawk!"* | who has an active bounty (replicated, recorded) |
| *+300 bounty* pop-up, *Bounty hunter* award | a bounty kill credited to the killer (as for the other score children) |

## ui's leans for P-8 — yours to decide
| question | ⭐ lean | why |
|---|---|---|
| a streak | kills since he was last downed; his own downing resets it | both come from `ManDownedEvent` (CE-3364) — ⛔ no second count |
| where it lives | `Count what: streak` (Value = the current run) and `what: bestStreak` (the highest), in `macros/game/score`, worth no points | the HUD and summary are plain reads, like the other children |
| the bounty mark | a **bounty objective**, Subject = the man, `VisibleSides` = all, `Kind` = Bounty: Active while it stands, Achieved when he is downed (inserted by the trigger, e.g. a `macros/game/bounty` instance) | the map / HUD draws a marker on any Active Bounty status: no new component, replays with the recording |
| the +300 | `Count what: bountyKills` (points 300) in each man's score: a downing he caused of a man with an Active bounty | the existing `Points` sum; *Bounty hunter* = a `Best` award over it |
| the announcement | the existing `Message` action in the trigger: *"Bounty on {name}!"* to all | built (CE-1461) |
| repeat | per player, once per streak (a new bounty only after he was downed and reached 5 again) | SHOWCASE §2 *"repeat per player"* |

⭐ Please name the as-built fields (`Kind`, titles) in your reply; ui designs PH-7's half against them.

_ui lane, 2026-11-03_
