<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Input_And_Commands.md — §6b and stage K7 carry the ruling (R-4163)
  - ../../UX/DESIGN_Action_Catalogue.md — §8 the corrected selection table
  - NOTE_Coordinator_To_Ui_A3_A4_Ruled.md — R-4162; its "empty ground → the selection" row is CORRECTED here
-->

# NOTE — coordinator → ui: selection rules corrected, RTS is a checked mode, input scopes cover the mouse (R-4163)

⚠ **Corrects one row of [NOTE_Coordinator_To_Ui_A3_A4_Ruled](NOTE_Coordinator_To_Ui_A3_A4_Ruled.md)**: right-clicking empty ground does
NOT act on the selection.

## The selection rules (A4) — final

| you do | the selection | result |
|---|---|---|
| right-click a **selected** entity | unchanged | the menu acts on every selected entity |
| right-click an **unselected** entity | that entity alone | the menu acts on it |
| right-click **empty space** | unchanged | the **empty-space menu only** (measure, add entity, centre view …) |
| left-click an **unselected** entity | that entity alone | — |
| left-click **empty space** | **cleared** | — |

## RTS and input scopes

| | |
|---|---|
| **RTS** | *View ▸ RTS command mode* (action 254) is a **checked mode**: only that item turns it off — ⛔ Esc never leaves it (Esc cancels a half-done pick inside it). Active in Preview / Live only, no effect in Edit; stays checked across mode changes; possession overrides it while it lasts |
| **one input chain** | 🔒 *"it's about all user inputs"* — a scope binds keys AND mouse gestures (on an entity, ground, a handle; drags; modifiers). The selection table above is the **map scope's** mouse bindings; the RTS scope sits in front and overrides only what it changes (RMB ground = Move, RMB hostile = Attack); 2-D / 3-D, camera, Ctrl+S fall through |
| **stage** | [Input and Commands](../../UX/DESIGN_Input_And_Commands.md) **K7** (new) — after K2 / H3, with B-16. `RtsCommandGizmo`'s mouse code becomes its bindings |

---
_coordinator lane, 2026-11-07_
