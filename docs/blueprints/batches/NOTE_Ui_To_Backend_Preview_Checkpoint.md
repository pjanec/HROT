<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
related-designs:
  - ../../UX/DESIGN_Action_Catalogue.md — §8, the A3 table and R-4162's Save row (as built)
-->

# NOTE — ui → backend: a checkpoint in PREVIEW (R-4162's Save row) — REQUEST

🔒 User, R-4162: *"The save Icon Should work as save only in edit mode, and if used during preview or live, it should create a checkpoint."*

| mode | Save today (measured, ui `2026-11-07`) | ruling |
|---|---|---|
| Edit | writes the scenario | ✅ |
| Live | takes a checkpoint (`RunModeChoice.TryCheckpointInsteadOfSave`) | ✅ |
| a recording | refused — *"a recording cannot take a checkpoint"* | ✅ (not offered) |
| **Preview** | ⛔ refused — *"Not saved: stop the preview to save"* | **a checkpoint** |

**Why ui did not just route it:** `RunModeChoice.CheckpointDisabledReason` answers *"a checkpoint is taken of a live run"* for a Preview, and
`TakeCheckpoint` asks the session (→ the master / `ReferenceCheckpointHandler`), which needs the run's recorder. A Preview runs a COPY of the
edit world (CE-4250) — is it recorded, and can the master checkpoint it?

**REQUEST:** say whether a Preview can be checkpointed (and how it is restored — Load Checkpoint goes LIVE today). Once it can, ui flips the
one line in `TryCheckpointInsteadOfSave` (Preview → `TakeCheckpoint()`) and `CheckpointDisabledReason`. ⭐ Lean: a Preview checkpoint restores
as a Preview, from the edit world's start, so it never touches the authored scenario.

---
_ui lane, 2026-11-07_
