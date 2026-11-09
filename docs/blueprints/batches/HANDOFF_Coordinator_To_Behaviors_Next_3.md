<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-09
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Mission_Plan_Editor.md — §5.13 the task picker (R-4184)
  - ../../DESIGN_Content_Layout.md — §6 scenario categories = sub-folders (R-4185)
-->

# HANDOFF — coordinator → behaviors: the next batch (Next_2's rest + the pickers)

**For:** behaviors. **From:** coordinator. **Dispatched at `82ef92069`**, scope frozen there. Merge the other lanes first; push `chore: started
HANDOFF_Coordinator_To_Behaviors_Next_3 at <sha>`; status file `STATUS_Behaviors_Next_3.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md): a status file after every item, fetch and merge the other lanes after every item. ⛔⛔ Never stop for a question (R-4156): an item that needs the user is SKIPPED with its question in its row; a failed premise stops that item only (R-106). Where a scenario's behaviour and its description disagree and no ruling says which is meant, the DESCRIPTION is the spec (R-4182).

| # | id | what | done when |
|---|---|---|---|
| **H-1** | — | **Next_2 G-1's seven goldens that MOVED** (not representation-only): per scenario, find what moved and why — a behaviour change made on purpose (a ruling, e.g. R-2085, R-4179) ⇒ re-capture and name the ruling; otherwise fix the regression | per scenario: the cause and the diff shape in the report |
| **H-2** | CE-2429 · CE-4035 / CE-2232 · CE-4136 | **Next_2 G-5 / G-3's open layer**: a brain's same-frame `TargetMemory` seed erased by `ThreatEvaluationSystem`'s write-back (CE-2429 — fix it, your lean) and the gun that never completes its aim while the hull moves (the ATGM technical aims ~45 s and never fires; IFV 2 needs ~20 s) — ONE fix for both scenarios | `ua-technical-scoot` 4/4 checks; the `rpg-window` beat holds on verify; rails red-proved |
| **H-3** | CE-4041 (rest) | the demos that still differ between the editor's stepped Preview and the cluster | each passes in both, or its measured cause in the row |
| **H-4** | CE-4151 | (Next_2 G-6) a TKB type's default task carries its params, like its default SOP — the row's *Do* | a rail: a type's default task arrives with its params |
| **H-5** | CE-4359 | ⭐ **the task picker** ([Mission plan editor §5.13](../../UX/DESIGN_Mission_Plan_Editor.md), R-4184, mockup `task-picker-visual.svg`): *+ Add task* opens the ONE `PickerWindow` tree with the preview pane; tests / demos / samples behind an off tick, non-tasks never; the folders in the fixed order (Recent · Orders · Movement · Combat · Weapons and vehicles · Casualties and engineering · Crowd and surrender · Reactions · My behaviours); every curated behaviour's `Group` re-filed (the list in the report); names as words | the frame rail of the open picker for a rifleman (45, in 8 folders) and a tank; R-4146 side-by-side |
| **H-6** | CE-4361 | ⏳ **only after backend's B-0 (CE-4360) has landed** (its NOTE to you) — file the 127 scenarios into `showcases/`, `demos/<area>/`, `tests/` ([Content layout §6](../../DESIGN_Content_Layout.md), R-4185); names unchanged; the list in the report — otherwise skip | the picker shows the folders; the scenario regression passes on the moved tree |

---
_coordinator lane, 2026-11-09_
