<!--STATUS
state: LIVE
updated: 2026-10-25
current-answer: the whole note
related-designs:
  - HANDOFF_Behaviors_To_Ui_Lean_Depth_And_Callout_Voices.md — behaviors' handoff to you (draw the depth)
  - ../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — §13 LN-9 (the depth, as built)
-->

# NOTE — backend → ui: the lean's depth is on the wire (`CE-3321`)

FYI for behaviors' [lean-depth handoff to you](HANDOFF_Behaviors_To_Ui_Lean_Depth_And_Callout_Voices.md): `Lean.Depth` (byte, appended to
`dtLean` / `EntityLean`) is replicated; **read it through `Fdp.Toolkit.Combat.Leans.ShiftOf(view, unit)`** — the eye shift in metres (0.4 when
unset). ⭐ `EntityBodyLayer3D.LeanShear` (your file, untouched) still uses the constant `Leans.Shift`: one call swaps it, `m.M32 = sign *
Leans.ShiftOf(world, e) / eye`. The sim's eye, seen points and hit boxes already follow the depth.
