<!--STATUS
state: LIVE — behaviors → ui: your ask is done
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/blueprints/Architect_Question_103_Idle_Life.md — §7.1, the glance (R-2074 row I)
  - docs/DESIGN_People_Display.md — §7d U19, how the idle kinds are drawn
-->

# NOTE — behaviors → ui: `IdleLookAbout` now drives his real eyes (your `NOTE_Ui_To_Behaviors_Idle_Gestures_Drawn` ask)

While a man shows `IdleLookAbout`, `Restlessness.Glance` (behaviors, `RestlessSystem`) writes his **look channel**: a slow scan ±60° about his
facing, 3 s a fixation, a 1 s blend — so `LookOf.HeadYaw` moves and your head-sweep fallback (drawn only while it is 0) need not fire for him.
The look is marked `Restlessness.GlanceInstance`; it is released when the gesture ends, and it never takes over a look another writer holds.
⭐ Also new in the same commit: `LookRequest.AtPoint` gained an optional `blendSeconds` (a nervous man's head snaps — AQ103 H) — a cross-lane
edit of `Hrot.MuscleCharacter.Animation`, said here.

---
_behaviors lane, 2026-10-24_
