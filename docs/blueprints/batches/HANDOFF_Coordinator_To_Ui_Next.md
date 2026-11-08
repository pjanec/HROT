<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Input_And_Commands.md — K2 / K3 / K7 (§6, §6b), K4, K5, K6
  - ../../UX/DESIGN_Edit_Mode_Undo.md — U4, Edit ▸ History
  - ../../UX/DESIGN_Action_Catalogue.md — A7
  - ../../UX/DESIGN_Seats_And_Hats.md — H6; §3.4 H4a decided (R-4167)
  - ../../UX/DESIGN_Scenario_Presets.md — §9 (R-4168): L-10 rides backend's Y-1
-->

# HANDOFF — coordinator → ui: the next batch

**For:** ui. **From:** coordinator. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there. Merge `coordinator` and
`backend` first (restore after), push `chore: started HANDOFF_Coordinator_To_Ui_Next at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md). ⛔⛔ Anything needing the user is skipped (R-4156).

⭐ **Your list was stale** — done already: P-7 7a–7d, A4, A5, U-23 / U-24 rest, B-16 H2 / H3 / H4b. **H4a is DECIDED** (R-4167, user: *"Q3 Lean
accepted"*): releasing a unit returns to the map you had; *Back to my view* only after a director take — today's behaviour, its rails stay; nothing to
build beyond closing the row. **L-10 is unblocked by a decision** (R-4168): a Preview with a preset is applied IN PLACE (backend's Y-1, CE-4326) — you
build the menu entry once backend says Y-1 landed.

## The items — in order

| # | what | design basis | done when |
|---|---|---|---|
| **Z-1** | the checkpoint flag in **AAR**: tooltip *"a recording already holds every moment — use Go live from here"* (R-4168: no checkpoints in AAR or an unrecorded Live; your greyed build stands) | R-4168 | the tooltip; the unrecorded-Live text unchanged |
| **Z-2** | **B-14 rest — U4 + *Edit ▸ History*** | [Undo](../../UX/DESIGN_Edit_Mode_Undo.md) (R-4133) | the design's rails; side by side with `menus-visual.svg` (Edit menu) |
| **Z-3** | **A7** — Details tabs carry `Requires` / `Modes` / `Level` (the `ActionDecl` model), registered once in `EntityDetailsBundle`; ⚠ `Hrot.Common.Editing.EditingMode` + `StationMode.Of`, never a third mode vocabulary | [Action catalogue](../../UX/DESIGN_Action_Catalogue.md) A7 | the tab offer per entity kind × host as a golden |
| **Z-4** | **H6** — the Side-controller seat and the Observer seat's camera (H3 is built, so it can go now) | [Seats and hats](../../UX/DESIGN_Seats_And_Hats.md) H6 | side by side with `seats-hats-visual.svg` |
| **Z-5** | **K2 rest + K3 + K7** — hat scopes, central Esc (stops at RTS, R-4163), every user input in the scope chain (mouse gestures as bindings; RTS overrides only RMB ground / hostile). ⚠ **Your K2 finding** (letters stand down for ANY camera driver, follow included): ⏸ **waiting for the user** — the coordinator's lean is *letters stay ON while following (follow is Observe's camera, not a hat); a follow camera's own keys sit in a camera scope in front of the map scope (R-4163)*. If no answer when you reach it: build K3 / K7 and leave the follow behaviour as today | [Input](../../UX/DESIGN_Input_And_Commands.md) §6, §6b | K7's rail: one click → the menu in the map scope, Move / Attack in the RTS scope; 2-D / 3-D and Ctrl+S still work in RTS |
| **Z-6** | **K5 + K6** — closures → actions (buttons and keys call action ids), the *Keyboard shortcuts* window | Input K5, K6 | the K-stage rails |
| **Z-7** | **K4** — the FPS station, the timeline, IG pan / zoom / undo, debug F-keys onto bindings (Hold / Axis kinds) | Input K4 | rails per surface |
| **Z-8** | **L-10** (CE-4317) — the Run setup on the mode menu for Preview AND Live, over backend's Y-1 | Presets §9 (R-4168) | a Preview of a preset shows an unsaved edit; the default Preview unchanged |

## Gates

The CLAUDE.md gate contract; fidelity checked by you (R-4146); display rails under `xvfb-run -a`.

---
_coordinator lane, 2026-11-08_
