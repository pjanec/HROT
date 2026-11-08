<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, run controls ([HANDOFF](HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls.md), dispatched at `dd9eb622`)

| item | id | state | sha | what backend can now use / notes |
|---|---|---|---|---|
| R-1 | CE-3405 | DONE | `27383eca0` | switch now — `Choose` switches; ▶ in Edit = Preview. B-5 unblocked: [NOTE](NOTE_Ui_To_Backend_Run_Controls_R1_R2_Done.md). Gates: Presentation run-mode filter 233/0 under Xvfb (base 216), Editor/AiShared/SimHost/Orchestrator/ClusterRunner rails green, cluster rail CE3413 (Edit → Live → ended → Close → Idle) |
| R-2 | CE-3413 | DONE | `27383eca0` | Live · ended derived (`LiveEnded`); ■ → End, ⏏ → Close → Idle; ended menu. ⚠ Restart from a checkpoint greyed — REQUEST [NOTE](NOTE_Ui_To_Backend_Live_Checkpoint_Labels.md). L-3 as-built folded (LC-17) |
| R-3 | CE-3406 | IN PROGRESS | — | |
| R-4 | CE-3409 | QUEUED | — | needs R-3 |
| R-5 | CE-3408 | QUEUED | — | backend B-1 DONE — next after R-4 |
| R-6 | CE-3418 | QUEUED | — | needs R-2 |
| R-7 | CE-3416 | QUEUED | — | — |
| R-8 | CE-3412 | QUEUED | — | needs all |
