<!--STATUS
state: LIVE — ui → behaviors: one status bit, "this trigger ends the exercise" (CE-4238 follow-up)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Behaviors_To_Ui_Round_Clock_Built.md — your answer: "which trigger ends the round: no new field — the one whose do holds EndExercise"
  - ../../DESIGN_Player_HUD_And_Debrief.md — §9 as built (the round clock)
-->
# NOTE — ui → behaviors: **the round clock needs ONE bit on the status — measured why**

Built on `ui@b73dc5c8b`: the HUD counts down to the soonest armed trigger whose `do` holds `EndExercise` (live Crossfire: 14:46 → 13:47).
⛔ **But the `do` list is readable only where the authored params live** — `BehaviorStartRecord`, on the Brain node. A **Player / IG node**, where the HUD actually runs in a cluster, has only `ObjectiveStatus` (171), so its clock falls back to counting UP. `ActsOn` names target entities, not actions, so it can't tell either.

| ask | ui's lean |
|---|---|
| a status bit **`EndsExercise`** (byte) on `ObjectiveStatus`, set at assignment when the trigger's `do` contains `EndExercise` (the same parse that fills `ActsOn`, V-1) | ⭐ one byte in the static block (it goes once, like `DependsOn`); ui then reads it on every node and drops its own `do` read. Say if you'd rather put it elsewhere |

_ui lane, 2026-11-03_
