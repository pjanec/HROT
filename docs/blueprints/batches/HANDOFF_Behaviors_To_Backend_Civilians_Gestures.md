<!--STATUS
state: LIVE — a FRAME (behaviors → backend): B-6, the gesture component. The receiving lane designs the details first.
updated: 2026-10-17
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §4 CV-13 / CV-13a (R-2024), §2.1 the class diagram (Gesture), §5c demos ua-civ-protest / ua-civ-surrender.
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — B-1…B-5, the frame this one extends (a new file, not an amendment).
-->

# FRAME — behaviors → backend: **B-6, the `Gesture` component** *(CV-13, R-2024)*

**Dispatched at** `80dd5ad34` (behaviors). Scope frozen there; later documents are FYI only. Allocate the row from your block; the
behaviors rows are `CE-2240` (who writes it) and `CE-2241` (hands up holds fire).

| | |
|---|---|
| **goal** | one REPLICATED byte per person — `Gesture` {None, HandsUp, FistRaised, HoldSign, Waving, Throwing} — so every node sees what a protester is doing with his arms |
| **fences** | ⛔ not in `EffectState` (timed hit effects — a gesture is his own act) · ⛔ not a stance (stance = locomotion) · ⛔ no prop entity (the placard is drawn by ui) |
| **lean** | the same shape as your `EffectState.Cowering` (a brain-owned field written on the brain owner, on the wire): a small component + its descriptor, `Effects`-style helpers `Gestures.Set(repo, unit, g)` / `Of(view, unit)`; default None ⇒ absent costs nothing |
| **design basis** | CV-13 in §4; §2.1 draws `CivilianHsm ..> Gesture : writes`, `FigurePose ..> Gesture : reads`, `AimAndFireExecutor ..> Gesture` |
| **acceptance** | a two-node rail: set on CGF ⇒ read on SimHost and IG; and the field reaches the replay (it is recorded like `EffectState`) |
