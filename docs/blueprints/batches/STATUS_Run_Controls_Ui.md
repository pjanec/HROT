<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, run controls ([HANDOFF](HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls.md), dispatched at `dd9eb622`)

| item | id | state | sha | what backend can now use / notes |
|---|---|---|---|---|
| R-1 | CE-3405 | DONE | `27383eca0` | switch now — `Choose` switches; ▶ in Edit = Preview. B-5 unblocked: [NOTE](NOTE_Ui_To_Backend_Run_Controls_R1_R2_Done.md). Gates: Presentation run-mode filter 233/0 under Xvfb (base 216), Editor/AiShared/SimHost/Orchestrator/ClusterRunner rails green, cluster rail CE3413 (Edit → Live → ended → Close → Idle) |
| R-2 | CE-3413 | DONE | `27383eca0` | Live · ended derived (`LiveEnded`); ■ → End, ⏏ → Close → Idle; ended menu. ⚠ Restart from a checkpoint greyed — REQUEST [NOTE](NOTE_Ui_To_Backend_Live_Checkpoint_Labels.md). L-3 as-built folded (LC-17) |
| R-3 | CE-3406 | DONE | `b34096a2c` | the indicator = the current mode (pen / eye / ● / flag / ⟲ / ▶), a glyph button with ▾ + hover + hand; `IRunScenarioControl.SaveDisabledReason` read by pen, slot, File › Save |
| R-4 | CE-3409 | DONE | `f8155fff8` | ▶ glyph wears the indicator's colour; ▾ only Edit + briefing; toolbar 0 B steady in every state (rail) |
| R-5 | CE-3408 | DONE | `9348c80ec` | one Save slot: floppy / greyed with the reason / amber flag (checkpoint); `EditorCommandDescriptor.DynamicGlyph`. Gates: Presentation run-mode filter 236/0 ×2 under Xvfb. ⚠ 8 editor-recording integration rails red since the backend merge — CE-3410, REQUEST [NOTE](NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran.md) |
| R-6 | CE-3418 | IN PROGRESS | — | |
| R-7 | CE-3416 | QUEUED | — | — |
| R-8 | CE-3412 | QUEUED | — | needs all |
