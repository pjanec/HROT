<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-09
current-answer: the whole file
related-designs:
  - ../../DESIGN_Recording_Storage.md — §8 the mockup (A-1)
  - ../../UX/DESIGN_Details_Forms.md — Part 2 as-built by backend (A-2)
  - ../../DESIGN_Struct_Editor.md — the one struct editor (A-3)
-->

# HANDOFF — coordinator → ui: the next batch

**For:** ui. **From:** coordinator. **Dispatched at `bd4994196`**, scope frozen there. Merge `coordinator`, `backend`, `behaviors` first; push
`chore: started HANDOFF_Coordinator_To_Ui_Next_2 at <sha>`; status file `STATUS_Ui_Next_2.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106). ⭐ R-4146: render the REAL host's frame for
every UI item and report the side-by-side.

| # | id | what | done when |
|---|---|---|---|
| **A-1** | CE-4330 (ui rest) | now UNBLOCKED (backend `7a9016887`): the picker's location chips, the import dialog, the close-the-session question, the quit guard, the delete-from-archive confirmation, the orchestrator panel's job list | mockup `recording-storage-visual.svg` ② – ⑦ rendered from real jobs |
| **A-2** | CE-4346 (ui half) | ONE Scenario tab — the form; the old raw one gone from Details; sections Scenario time · Terrain · Global weather; the wind levels / cloud layers with their own editors; *Local time offset* (backend's name) — backend's NOTE says what landed | a frame rail of the tab; the old tab absent |
| **A-3** | CE-4351 · CE-4352 | the struct editor: a fixed-size buffer lists its entries (`BrainJournal.Entries` and every other), a struct element's form shows its fields (`EgressPublicationState.LastPublishedStateTickMap`) | rails on both named fields + one other component of each shape |
| **A-4** | CE-4342 | dragging one of several selected entities moves them all, keeping their offsets, ONE undo step | a rail |
| **A-5** | CE-4343 | toasts laid out from the font (title, reason, *Show*), at a UI scale above 1 | a frame rail at scale 1 and 1.5 |
| **A-6** | CE-4347 | *View ▸ RTS command mode* shows its checkmark | a menu rail |
| **A-7** | CE-4348 | a possessed aircraft (A-10C, AH-64): wheeling in reaches a view clear of the nose — lean: the wheel may go past the eyes for every type; the eye point at the nose tip if that is simpler — say which and why | a frame rail on both aircraft |
| **A-8** | CE-4349 | the entity pick dropdown: no clipped left part, the second column not over the name | a frame rail |
| **A-9** | — | the toolbar ▶ greyed (with the reason) while the Review shows the merged world — the seam is in [NOTE_Behaviors_To_Ui_Backend_Review_Source](NOTE_Behaviors_To_Ui_Backend_Review_Source.md) | a rail |
| **A-10** | CE-4273 (rest) | the Car's thumbnail against a live editor | the picture in the atlas, the row closed |

---
_coordinator lane, 2026-11-09_
