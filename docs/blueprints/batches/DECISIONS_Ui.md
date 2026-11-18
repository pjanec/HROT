<!--STATUS
state: LIVE — the ui lane's open questions for the coordinator / user (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->

# DECISIONS — ui

## CE-1954 — does a PRESS-AND-DRAG on an UNSELECTED unit move it? *(asked 2026-11-18)*

**The question:** the threaded editor moves it, the serial one does not (it only selects); the two must agree (R-4224). Which rule?

| option | what it means | cost |
|---|---|---|
| **A (lean)** — press on an unselected unit SELECTS it and the same drag MOVES it, in both modes | the common editor convention (a click-drag grabs what is under the cursor); `GroupTransformTests.P5_ADragOnAnUnselectedUnit_MovesOnlyThatUnit` already asserts the gizmo-level half | the serial host must emit a drag box for EVERY unit under the cursor (not only selected ones — `DataDrivenGizmoSystem.IsSelectedPredicate`), or select-then-drag on the press; a frame rail on both hosts |
| B — the press only SELECTS; a drag needs the unit selected first | what serial does today | the threaded host must stop answering a press with the previous frame's pick boxes; P5's unselected-drag rail is then wrong and changes |

**Claim table (lean A):** threaded drags it — ✅ measured live (the row, `tests/hill-attack-close`); serial selects only — ✅ the drag box is emitted for selected units only (`DataDrivenGizmoSystem` `IsSelectedPredicate`); the design rules it — ⛔ searched `docs/UX/DESIGN_Map_Interaction.md` (§2.5 table, P5 as-built), no rule for an unselected unit's drag.

**State:** SKIPPED — needs decision. Nothing built.
