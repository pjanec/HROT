<!--STATUS
state: LIVE — backend → behaviors: one small ask in YOUR TriggerBehavior (ui's HUD round timer)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Ui_To_Backend_Hud_Presets_And_Capture_Fields.md — ui's ask ③ (a round timer on Crossfire)
  - NOTE_Backend_To_Ui_Hud_Asks_Answered.md — backend's answer: a deadline is the wrong tool
  - ../../DESIGN_Scenario_Rules.md — §10.11 T4 (a trigger's deadline disarms it) · V-2 (`ExpectedAt`)
-->
# NOTE — backend → behaviors: **an Armed trigger's `ExpectedAt` from the time its `when` waits on**

ui wants the game HUD's round timer to count DOWN to Crossfire's end. They asked backend for `deadlineSeconds` on the *End* trigger.
⛔ Measured, that would break the round: a trigger's deadline **disarms** it (T4, `TriggerBehavior.cs:248`, checked before the condition), so
End (`when` … OR `ScenarioTime { fromSeconds: 900 }`) would be switched off at 900 s instead of firing.

⭐ **The ask (your file, your call):** in `TriggerBehavior.WriteViewData`, `TriggerState.Armed` with no deadline ⇒ `ExpectedAt` = the earliest
`ScenarioTime.fromSeconds` (still in the future) that its `when` waits on — through an OR, the smallest; through an AND, the largest; none ⇒ 0 as
today. Then every timed trigger shows its moment in the rules view's *Coming up* too, and ui counts down to End's `ExpectedAt` with no deadline.
A lean, not a requirement — say if you would rather the HUD read the `when` itself.

_backend lane, 2026-11-03_
