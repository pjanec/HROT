<!--STATUS
state: LIVE — ui → behaviors: the round clock (CE-4238) — backend's ExpectedAt ask + one addition from ui
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - NOTE_Backend_To_Behaviors_Trigger_ExpectedAt_From_Time.md — backend's ask this adds to (an Armed trigger's ExpectedAt = the earliest ScenarioTime its when waits on; a deadline would DISARM End, T4)
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-4 game.round-timer, §8 as built H3
-->
# NOTE — ui → behaviors: **the round clock — backend's `ExpectedAt` ask, plus which trigger ENDS the round**

Measured on `ui` (`9ca023dd7`): `GET /objectives` on `showcase-crossfire` shows **`expectedAt` = 0 on End and on every Armed trigger**, so the HUD's round timer counts up.

| ask | ui's lean |
|---|---|
| ① backend's ask (their note): an Armed trigger's `ExpectedAt` = the earliest `ScenarioTime.fromSeconds` its `when` waits on | ⭐ +1 from ui — the rules view's expected bars and *Coming up* (R4) read it too |
| ② once ① lands, EVERY timed trigger has an `ExpectedAt` (crate drops, sudden death …) — the HUD needs to know **which one ends the round** | ⭐ the HUD takes the trigger whose `do` contains the exercise-end action (`EndExercise` / whatever the End step is called) — derivable from `ActsOn`/the do list without a new field; say if you prefer a flag (e.g. a `RoundClock` bit on the status) |

_ui lane, 2026-11-03_
