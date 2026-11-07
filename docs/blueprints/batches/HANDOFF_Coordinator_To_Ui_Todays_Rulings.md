<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - HANDOFF_Coordinator_To_Ui_Rules_Bundle_And_Takeover.md — your running handoff; this one sets the ORDER of what is left of it plus today's rulings
  - HANDOFF_Coordinator_To_Lanes_Mission_Plan.md — P-7 / P-8 come from there (backend has P-1…P-4 DONE, P-5 started)
  - NOTE_Coordinator_To_Ui_A3_A4_Ruled.md · NOTE_Coordinator_To_Ui_Selection_And_Input_Scopes.md — the rulings in detail
-->

# HANDOFF — coordinator → ui: today's rulings, in one order

**For:** the ui lane. **From:** the coordinator lane. **Dispatched at `<sha of the commit adding this file>`**. ⭐ Scope FROZEN there.
Merge `coordinator` and `backend` first (backend's P-1…P-4 are what P-7 builds on), push `chore: started HANDOFF_Coordinator_To_Ui_Todays_Rulings
at <sha>`, then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) as before. ⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-07`: *"The backend is still working on the mission plan, so the UI lane is idle. Can the UI lane work on all this, what we designed
just now?"* — your two waiting questions (A3 review, O6 vs A4) are ruled: R-4162, R-4163, R-4164.

## The order

| # | what | design basis | done when |
|---|---|---|---|
| **1** | **A3 corrections** — Rotate in Edit AND Live; *Save selection as macro…* Edit only; Follow / Select everywhere; **Save** writes the scenario only in Edit, takes a **checkpoint** in Preview / Live, not offered in Review (measure what is built first — CAPABILITIES says Live checkpoint exists) | [Action Catalogue §8](../../UX/DESIGN_Action_Catalogue.md) R-4162 | the menu goldens regenerated to the table |
| **2** | **P-7 (CE-4303)** — the Orders tab replaces `MissionPanel` — read backend's P-1…P-4 rows in `STATUS_Editing_UX_Build_Backend.md` first (storage shape, *no condition = when done — write nothing*, keep `TaskId` across edits) | [Mission plan](../../UX/DESIGN_Mission_Plan_Editor.md) §5.2 D8 D9, §5.6, §5.8 E5 E6, §5.9 E9 | the mission plan handoff's P-7 row; ⛔ claim it in your status file first |
| **3** | **A4 — the selection rules** (right-click a selected entity = all selected; an unselected one = select it alone; empty space = the empty-space menu only; left-click unselected = select it alone; left-click empty = clear) | §8 R-4162 + R-4163 | a rail per row |
| **4** | **B-16 — seats H2, H3, H4** — H2 the catalogue and gate read the seat (a Player gets no RTS / Add Entity, CE-4279); **H3 the `HatArbiter`** with: the hat FOLLOWS the run mode (R-4164), **RTS a CHECKED mode** (*View ▸ RTS command mode* only — no Esc exit, active in Preview / Live only), first person overrides it while possessing and release returns to it | [Seats and Hats](../../UX/DESIGN_Seats_And_Hats.md) §2.2a, §2.3; [Input §6b](../../UX/DESIGN_Input_And_Commands.md) | side-by-side with `seats-hats-visual.svg`; a rail: RTS checked → possess → FPS keys → release → RTS again |
| **5** | **B-10 rest — K2 hat scopes, K3 central Esc, then K7: every user input in the scope chain** (mouse gestures become bindings; the RTS scope overrides only RMB ground / hostile; `RtsCommandGizmo`'s mouse code becomes its bindings) | Input §6, §6b, stage K7 | K7's rail: one click → the menu in the map scope, Move / Attack in the RTS scope; 2-D / 3-D and Ctrl+S still work in RTS |
| **6** | **CE-4305** — server stations default Author; `features.station.seat` in the runner's `--config` file — ⚠ it is in [a note to backend](NOTE_Coordinator_To_Backend_Server_Seat_Config.md): take it ONLY if backend's status file has not claimed it | Seats and Hats §2.2a | that note's rails |
| **7** | **P-8 (CE-4304)** — the plan on the map, the picker's rule entries, the Phases group, ONE Tasks timeline lane — after backend's P-5 / P-6 | Mission plan §5.5 M3 M5 M6, §5.8 E2 E4 | the handoff's P-8 row |
| **8** | then the rest of your running handoff: A7, A5, B-14 (undo), B-17 (map interaction), U-23 / U-24 rest | as there | as there |

⭐ Gates: the CLAUDE.md gate contract; fidelity checked by you (R-4146). Leave `TriggerAdvertisesItsAuthoredKeys` to behaviors.

---
_coordinator lane, 2026-11-07_
