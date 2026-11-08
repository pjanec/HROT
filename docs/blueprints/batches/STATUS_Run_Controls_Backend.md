<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — backend, run controls ([HANDOFF](HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls.md), dispatched at `dd9eb622`)

| item | id | state | sha | what ui can now use / notes |
|---|---|---|---|---|
| B-1 | CE-3407 | BUILT — menu rail gating | (this commit) | ⭐ `IScenarioSession.SaveDisabledReason` is THE answer: cluster error · any state but `OperatingEdit` (`SaveRefusal` constants: Preview / Live / Review / Replay / Idle / busy) · then the variant. Read it for the pen and the Save slot (R-3, R-5); 0 B per read. `SaveAs` / `SaveCurrent` keep only the variant backstop (New-from-recipe saves while Idle). File › Save as Recipe now gated too |
| B-2 | CE-3410 | IN PROGRESS | — | an unrun Live (clock never advanced) writes no ledger entry ⇒ never listed; a run resumed from DAR is kept |
| B-3 | CE-3415 | IN PROGRESS (worker) | — | *Start immediately* run setting, mirrored on *Ask to keep recordings* |
| B-4 | CE-3414 | QUEUED | — | Start screen Recent rows |
| B-5 | CE-3411 | SKIPPED — blocked | — | ⏳ needs ui R-1 |
| B-6 | — | SKIPPED — needs user | — | QUESTION [NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question](NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question.md): the setup is applied only by a load (DOM-level); lean (B) preview the saved scenario with the setup. ui's L-10 stays ⏳ |
