<!--STATUS
state: LIVE — coordinator → backend: the per-player GAME score (what S6 left waiting)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.12 S6 (yours): *"Score per player, team-kill −200, awards and the bounty wait for S4's measures (behaviors) and CE-4155"* — both have landed
  - ../../DESIGN_Player_Join.md — D-15 (score), R-4105 (the score is the engine's, the ui only shows it)
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-7 waits for this (score pop-ups, scoreboard points, awards, best moment, round summary) — ⚠ its leans await the user
  - ../../SHOWCASE_Scenarios.md — §2 Crossfire: §2.6 lines 3 (team kill) and 5 (awards macro)
  - HANDOFF_Coordinator_To_Behaviors_Rules_S4_Scores.md — item ⑥ there: the kill / assist inputs you read
-->
# HANDOFF — coordinator → backend: **the per-player game score**

**Dispatched at** `eabedf562` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106).
🔒 **User, `2026-11-03`:** *"Write handoffs to behavior and backend."*

## Why now
S6's own note left these waiting for S4's measures and CE-4155 — ✅ both built on `behaviors` (E-2/E-3 measures; CE-4155 attribution, `OriginalSource`). Merge `coordinator` first.

## Items
| # | item | spec | done when |
|---|---|---|---|
| ① | **score per player** — kills, assists, captures, deaths → points per the game profile's table; owned by the engine (R-4105), replicated, recorded; per possessed PERSON and per team | D-15, S6 | a scoreboard is a read; rail per rule |
| ② | **team kill −200**, never counted as a kill (CE-4155's `Source` / `OriginalSource` decides; a rethrown grenade blames the rethrower) | SHOWCASE §2.6 line 3 | the Crossfire check line green |
| ③ | **the awards macro** (best shot, most captures, …) + the **bounty** — computed from ① and the measures, in a MACRO, not the scenario file | SHOWCASE §2.6 line 5, SR-112 | the Crossfire check line green |
| ④ | the data the round summary + HUD pop-ups need (PH-7): per-player events (*+100 capture*), the round's best moment marker on the timeline index | SR-112, PH-7 | ui draws from reads only |
⭐ Read behaviors' measures (`Count`, …) and attribution before counting anything — ⛔ one counting mechanism (their handoff item ⑥ coordinates it). The `Counter` (G-2) stays the scenario-moved pool.

## Gates *(Rule 8)*
Touched classes, rails red-proved · a live Crossfire round with the §2.6 checks incl. lines 3 and 5 · the gate scripts · fold the as-built into §00.12.

_coordinator lane, 2026-11-03_
