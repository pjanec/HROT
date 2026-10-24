<!--STATUS
state: LIVE
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/blueprints/Architect_Question_103_Idle_Life.md — OWNS idle life (R-2074); row I is this note's subject
  - docs/DESIGN_People_Display.md — OWNS how a person is drawn (U14: gestures on each man's own clock)
  - docs/DESIGN_Civilians_And_Crowd_Control.md — OWNS the existing gestures (§7.7, CE-3224) and their figures (§8.7 U6)
-->

# HANDOFF behaviors → ui — the IDLE gestures (a relaxed man's animation)

*Dispatched at: the behaviors commit that adds this file (`CE-2368`). A FRAME — ui designs the drawing.*

🔒 **User, `2026-10-24`, verbatim:** *"soldiers who are actually really idle or waiting in some boring job like patrolling or just feeling
relaxed, they might need some animations expressing that state. Some of them could be smoking a cigarette, some of them just moving their
bodies, stepping from one leg to the other. This might need some flag that the IG can run an animation in such a case that will give a
feel of realism. And that of course applies to civilians as well, not just soldiers."*

## What behaviors built

| | |
|---|---|
| the flag | four new `GestureKind`s on the existing gesture channel (`Gesture` → `dtGesture` / DDS `EntityGesture.Kind`, a byte): **`IdleSmoking` = 7 · `IdleShiftWeight` = 8 · `IdleStretch` = 9 · `IdleLookAbout` = 10** — `Gestures.IsIdle(kind)` tells them from the acts |
| who writes them | `RestlessSystem` (behaviors, brain host) — a person idle and CALM (standing or crouched, no enemy seen for 10 s, no round near him for 30 s, held by nothing but an order to stay): shown 20–60 s, then a 20–90 s pause; each man has a seeded habit (a smoker smokes). ⭐ Soldiers and civilians alike. A gesture his behaviour sets (hands up, a fist, the police warning) is never overwritten; any threat clears his idle one within a second |
| ⚠ what behaviors touched in ui's files (said, cross-lane) | `EntityBodyLayer3D.Gestured`: an idle kind returns the figure UNCHANGED — ⛔ cast through to `ArmGesture` it fell to the switch's default and drew the THROW's wind-up. `EffectStateGizmo.GestureWord`: an idle kind has no label (it read "PROTEST") |

## The frame for ui

| | |
|---|---|
| goal | the 3-D figure (and the 2-D mark if wanted) SHOWS each idle kind — a cigarette at the mouth on a slow cycle, the weight shifting leg to leg, a stretch, an unhurried look about — on the man's own clock (`GestureClock`, U14), so a crowd of idlers is out of step |
| fence | ⛔ the four byte values are on the wire — keep them; ⛔ an idle kind is a MOOD, never an act — no label like an act's; a soldier keeps his rifle in hand or slung as his posture says (not slung across the back as for the arm gestures) |
| leans | extend `BlockFigure`'s figure with the four (replace the guard above); the legs may move for `IdleShiftWeight` (today only the arms change) |
| the external IG | the same byte reaches it over DDS — the four values and their meaning are the contract; ⭐ say in the IG notes that 7–10 are idle animations to blend, not poses to snap to |
| acceptance | in the editor's 3-D view an idle squad and a civilian group show the four, out of step; a threat makes them stop at once |
