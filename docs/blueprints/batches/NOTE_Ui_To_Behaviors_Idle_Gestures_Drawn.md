<!--STATUS
state: LIVE — ui → behaviors: the idle gestures are drawn; one small ask
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/DESIGN_People_Display.md — §7d U19, how the four idle kinds are drawn (CE-1308)
  - docs/blueprints/Architect_Question_103_Idle_Life.md — row I, the idle kinds (R-2074)
-->

# NOTE — ui → behaviors: the idle gestures are drawn (`CE-1308`, your `CE-2368` handoff)

**Built:** [People Display §7d](../../DESIGN_People_Display.md) U19 — smoking (the right hand to the lips and down, a cigarette), the stretch,
the weight shift (the upper body sways over the standing leg, the free knee bends), looking about (the head sweeps). Your guard in
`EntityBodyLayer3D.Gestured` is replaced by `BlockFigure.IdleArms`; `GestureWord` stays as you left it (no label). A soldier's rifle stays where
his posture holds it. The IG contract (7–10 = idle animations to blend, never poses to snap to) is in the `EntityGesture` comment and
Civilians §7.7.

**⚠ The rail that went red with your merge:** `Map3DTests.CE1166_TheDrawingsGestures_AreBackendsGestures_ByteForByte` (the drawing's
names must be `GestureKind`'s) — it now compares the ACTS only, since the idle kinds never reach the cast.

**Ask (your call):** `IdleLookAbout` is drawn as a head sweep **only while `LookOf.HeadYaw` is 0** — the drawn head shows where his eyes
really look (Entity Control LK-5), so a drawn sweep is a small fiction perception does not share. If `RestlessSystem` drove a real, slow
glance (the LD-3 glance) for `IdleLookAbout`, the drawing would follow the eyes by construction and his perception would sweep too.

---
_ui lane, 2026-10-24_
