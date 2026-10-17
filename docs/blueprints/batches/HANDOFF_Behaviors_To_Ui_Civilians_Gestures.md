<!--STATUS
state: LIVE — a FRAME (behaviors → ui): U-6, drawing gestures and the placard. The receiving lane designs the details first.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §4 CV-13 (R-2024), §2.1 (FigurePose ..> Gesture), §8.4 U3-a (your EffectState poses, the precedent).
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md — U-1…U-5, the frame this one extends (a new file, not an amendment).
-->

# FRAME — behaviors → ui: **U-6, the gestures and the placard on a pole** *(CV-13, R-2024)*

**Dispatched at** `80dd5ad34` (behaviors). Scope frozen there; later documents are FYI only. Allocate the row from your block.

| | |
|---|---|
| **goal** | a protester LOOKS like one: the arms show the backend's `Gesture` (B-6) on the 3-D map, and HoldSign carries a pole with a placard |
| **fences** | ⛔ no clothes or colour change (U2-c: side is the symbol and the card) · ⛔ no prop entity — the placard is drawn from the gesture · ⚠ `EffectState` wins over a gesture (knocked down / cowering is not waving a sign) |
| **lean** | `FigurePose` reads `Gesture` AFTER `EffectState` (your U3-a order) as an ARM override over any stance — the way `BlockFigure.CarrierArms` overrides arms today; HoldSign = both arms up + a pole + a board; HandsUp = both arms straight up, open; FistRaised = one arm up (cycled by the brain, not animated by you); Throwing = the wind-up; and in 2-D one text line on the `StanceGizmo` shape (HANDS UP / PROTEST) |
| **design basis** | CV-13 in §4; the poses' precedent is your §8.4 U3-a |
| **acceptance** | a rail per gesture that the figure's arm boxes differ from the stance's; demo `ua-civ-protest` (§5c) shows them on every map host |
