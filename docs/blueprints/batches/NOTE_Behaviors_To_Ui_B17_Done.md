<!--STATUS
state: LIVE — behaviors → ui: B-17 (map interaction, taken from ui) P1–P6 built; P7 (the look review) awaits the user
updated: 2026-11-08
current-answer: the whole note
related-designs:
  - ../../UX/DESIGN_Map_Interaction.md — AS-BUILT P1 … P6 (each with its classDiagram)
  - STATUS_Editing_UX_Build_Behaviors.md — the W-2 row and its gates
-->
# NOTE — behaviors → ui: **B-17 built (P1–P6) — your files changed; what to know**

| stage | what you now have (cross-lane edits in your files, each commit says so) |
|---|---|
| P2 | `ShapeEditSession` — the drawing gizmos on one core (D11): Enter / ✓ / menu Finish, Esc cancel, ⭐ **right-click = the menu everywhere**, Backspace, too few ⇒ stays armed with the reason. `DrawingMenu` (catalogue ids 2410–2412). ⚠ the map's gizmo font has no ✓ — the cursor hint says "tick" |
| P4 | the pick banner: `PickerToolHost.Prompt / Rejected / Notice`, `PickBannerGizmo`; `IMapPickService` overloads with a prompt; a second pick REPLACES the first with a notice |
| P5 | multi-select gesture rows (Shift add, Ctrl toggle, Shift-band add, Alt-click cycle) in `MapGestures`; `GroupTransform` (move / rotate each or about the group centre, Tab toggles, one undo step); a plain press on a selection member narrows at RELEASE |
| P6 | the centre handle on every editor (routes too), click a segment = insert, `Delete` = a keymap row `shape.delete.point`; ⭐ `SelectionInteractionSystem.ToolOwnsKeys` — while a raw-input tool holds focus, Delete no longer destroys the selected units (it would have deleted the route being edited) |
| ⏳ P7 | the gizmo look review (§2.6) — awaits the user's approval before it is built. Gaps it will settle: no backdrop behind screen-space text (banner, cursor hint), no 3-D editing handles |

⚠ For you: `NoProductionHostKeepsAParallelSelectionStoreTests.OnlyTheHostForwardWritesTheAiEditorsEntityCell` is red, pre-existing (CE-2426, measured unchanged across P5) — your selection cell.

_behaviors lane, 2026-11-08_
