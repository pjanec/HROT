<!--STATUS
state: LIVE — backend → ui: an FYI note (no request beyond drawing a replicated state).
updated: 2026-10-19
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — §13 OWNS the lean (CE-3257): the state, its geometry, who writes it
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — CB-21: why a man leans (behaviors)
  - docs/blueprints/batches/NOTE_Behaviors_To_Ui_Building_Clearing_Poses.md — behaviors' ask that ui draw "the upper body tilted to that side, the weapon leading"
-->

# NOTE — backend → ui: the `Lean` state is on every node *(`CE-3257`, BR-5)*

**What it is.** A man leaning round a corner now carries `Fdp.Toolkit.Combat.Lean { LeanSide Side }` (`LeanNone` · `LeanLeft` ·
`LeanRight`, HIS left / right — body frame +y is left), written by his brain and replicated to every node (`dtLean` 149, Brain group).
Read it with `Leans.Of(view, man)`.

**The sim's geometry, so the drawing can match it:** a SHEAR about his feet across his own left axis — a point `h` above his feet moves
`0.4 · h / eye` sideways, so his eye moves exactly 0.4 m to that side and his feet stay put (`Leans.Offset`). Prone does not lean (the
sim ignores it then). Sight and the round both use this, so a figure tilted the same way shows the head where it can be seen and hit.

**For ui:** ⚠ `MotionPose.Lean` is a FORWARD pitch, so a sideways lean needs a roll — behaviors' poses note already asks for "the upper
body tilted to that side, the weapon leading". The first-person eye (`PossessionStation.EyeOf`) may want `TerrainWorldLosStrategy.EyeOf` as
its fallback so a possessed leaning man looks from where the sim says he sees. Nothing here is a request beyond that; your call.
