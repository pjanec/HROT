<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - PROTOCOL_Two_Lanes_Self_Coordinating.md — the SAME protocol as last night: after every item push, status file, fetch, merge `backend`, answer notes; anything needing the user is skipped (R-4156)
  - HANDOFF_Coordinator_To_Backend_Editing_UX_Build.md — B-10, B-11, B-14, B-16, B-17 move from there to here (NOTE_Coordinator_To_Backend_Items_Moved_To_Ui.md)
  - ../../UX/DESIGN_Rules_Bundle.md — item T-1's design (READY-TO-BUILD, with its diagrams)
-->

# HANDOFF — coordinator → ui: the rules windows as one bundle, then five items taken over from backend

**For:** the ui lane (`ui`). **From:** the coordinator lane. **Dispatched at `DISPATCHSHA`**. ⭐ Scope FROZEN there; later documents are FYI.
Merge `coordinator` and `backend` first, push `chore: started HANDOFF_Coordinator_To_Ui_Rules_Bundle_And_Takeover at <sha>`, then the
[protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md) exactly as last night — keep writing `STATUS_Editing_UX_Build_Ui.md` (add the rows below).
⛔⛔ **Anything that needs the user's decision is skipped, never blocking (R-4156)** — a QUESTION note with your lean, then the next item.

## Why

🔒 User, `2026-11-07`: *"How can be something built just for an editor when the bootstrap is unified and shared and features are available
everywhere by role and suppressed just by feature settings or role settings?"* → CE-4292, design written; *"ui lane seems finished, backend is
about to do B9 and B12"* → the ui lane takes the five UI-heavy backend items nobody has started, so both lanes stay busy.

## The items — in order

| # | what | design basis | done when | ⏳ |
|---|---|---|---|---|
| **T-1** | **CE-4292 — the rules windows as ONE bundle**: R1 move `EntityAiEditModel` to `Hrot.Editor.AiShared` · R2 `RulesHostServices` + `RuleEditorServicesFactory` (seat-gated, writes through `Edits.Submit`, history only where Edit mode exists) · R3 `RulesBundle` (Rules, Mission summary, Trainee debrief, Game summary, Game master) composed by Editor, CGF and the Replay Browser, the hand-written sites deleted · R4 a CGF live edit end to end | [Rules bundle](../../UX/DESIGN_Rules_Bundle.md) §2–§4 (R-174, R-4131 H5, R-4133 Q2, R-4128) | the §4 acceptance rows; window-set goldens: Editor and Replay Browser unchanged, CGF gains the four instructor windows | — |
| **B-10** *(from backend)* | input **K1–K3, K5, K6**: `Keymap` / `KeyBinding` / `KeyRouter` + the scope chain (CE-4282), the map scope (CE-4281), central Esc, actions not closures, the shortcuts window | [Input and commands](../../UX/DESIGN_Input_And_Commands.md) (R-4132) | the K-stage rails; side-by-side with `menus-visual.svg` (shortcuts) | — |
| **B-11** *(from backend)* | action catalogue **A1–A5, A7** — today's items first, with goldens; greyed with reason; targets per surface and mode; multi-selection; inspector closures → actions; the Details tabs' `Requires` | [Action catalogue](../../UX/DESIGN_Action_Catalogue.md) (R-4125) | its goldens; side-by-side with `menus-visual.svg` | — |
| **B-14** *(from backend)* | undo UI side **U2–U5**: `WorldStep` in `EditHistory` on backend's `WorldSnapshotRing` (B-13 done), Ctrl+Z / Ctrl+Y as actions, the take editor onto `EditHistory`, the dirty flag, *Edit ▸ History* | [Undo](../../UX/DESIGN_Edit_Mode_Undo.md) (R-4133) | the design's rails; side-by-side with `menus-visual.svg` (Edit menu) | B-10 for the key actions |
| **B-16** *(from backend)* | seats **H2–H4**: the catalogue and gate read the seat (CE-4279), the `HatArbiter`, one camera owner (H5 is T-1) | [Seats and hats](../../UX/DESIGN_Seats_And_Hats.md) (R-4131) | side-by-side with `seats-hats-visual.svg` | B-11 for H2 |
| **B-17** *(from backend)* | map interaction **P1, P2, P4, P5, P6** — the armed tool visible, `ShapeEditSession`, the pick banner, multi-select + `GroupTransform`, shape / route editing | [Map interaction](../../UX/DESIGN_Map_Interaction.md) (R-4134) | each stage's rail; side-by-side with `map-interaction-visual.svg` | — |
| **U-23 / U-24 / U-25 rest** | the combined rows (Weapon + aimed at, Playable + slot) · the carry-mode change and Damage rows now that backend built them (`NOTE_Backend_To_Ui…`, Damage attributes 85 / 86) · *Knows about* seeds only, current knowledge on the Brain tab on the brain's node (R-4157) · a list of named points beside the map pick | [Details forms](../../UX/DESIGN_Details_Forms.md) §4.0a · [Initial state](../../UX/DESIGN_Initial_State.md) §3, Q4a, §4.2, §4.3 | as in the first handoff | — |

⛔ **Not here:** map interaction P3 / P7, action catalogue A6 / A8, the mission plan editor (behaviors), the Replay Browser's open Q-B3 / Q-B6.

## Gates (report per item — protocol §6)

---
_coordinator lane, 2026-11-07_
