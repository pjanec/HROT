<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-09
current-answer: the whole file
-->

# HANDOFF — coordinator → ui: the next batch

**For:** ui. **From:** coordinator. **Dispatched at `82ef92069`**, scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Ui_Next_3 at <sha>`; status file `STATUS_Ui_Next_3.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106). Where a scenario's behaviour and its description disagree and no ruling says which is meant, the DESCRIPTION is the spec (R-4182). ⭐ R-4146: render the REAL host's frame for every UI item.

| # | id | what | done when |
|---|---|---|---|
| **C-0** | CE-4342 … CE-4352 | your Next_2 built these — **tick their tracker rows** with the sha (they are still open) | the rows closed |
| **C-1** | CE-4356 | the Player's Deploy dialog leaves the centre while Observing; a status-bar entry *"👁 Observing · Deploy…"* reopens it at Choosing | a frame rail in Observing |
| **C-2** | CE-4357 | a position row (x · y · z · *Pick on map* · ⌖) measures its trailing buttons instead of reserving a constant (`ReferenceLeafDrawers.cs:500-502, :532`) | a frame rail at UI scale 1 and 1.5: nothing clipped |
| **C-3** | CE-4358 | the struct editor's name / value columns are resizable (`StructTable.cs:21`, `FieldTable.cs:291`, `ComponentEditDrawer.Lists.cs:319`), the width kept per window | a rail |
| **C-4** | CE-4289 (rest) | the Replay Browser: ④ the debug windows grouped under *View ▸ Windows ▸ Debug* · ⑧ the Frame Diff window label-first · Q-B6 the Federation window's rows named by node role | the row closed |
| **C-5** | CE-4258 🔴 | the Run setup's param table: two entity-link rows clash in ImGui ids; the reset crash — the row | a rail per defect |
| **C-6** | CE-4199 | an untouched recipe becomes "unsaved" once its traffic loads | a rail |
| **C-7** | CE-4130 · CE-4138 | a still after a seek shows the 2-D map, not the 3-D scene · an anchor naming a unit found twice resolves to nothing with a wrong reason | rails |
| **C-8** | CE-3330 | the timeline panel's zoom and pan in Live / Review | the row's acceptance |
| **C-9** | CE-4285 | drawing a struct allocates every frame (~24 B per field) — 0 B steady (R-2084) | the allocation rail |
| **C-10** | CE-4036 | draw the decoy flares | a frame rail |

---
_coordinator lane, 2026-11-09_
