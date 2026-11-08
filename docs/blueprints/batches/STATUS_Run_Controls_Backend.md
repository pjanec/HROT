<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — backend, run controls ([HANDOFF](HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls.md), dispatched at `dd9eb622`)

| item | id | state | sha | what ui can now use / notes |
|---|---|---|---|---|
| B-1 | CE-3407 | DONE | `509485b9b` | ⭐ `IScenarioSession.SaveDisabledReason` is THE answer: cluster error · any state but `OperatingEdit` (`SaveRefusal` constants: Preview / Live / Review / Replay / Idle / busy) · then the variant. Read it for the pen and the Save slot (R-3, R-5); 0 B per read. `SaveAs` / `SaveCurrent` keep only the variant backstop (New-from-recipe saves while Idle). File › Save as Recipe now gated too |
| B-2 | CE-3410 | DONE | (see log) | an unrun Live (clock never advanced) writes no ledger entry ⇒ never listed; a run resumed from DAR is kept |
| B-3 | CE-3415 | DONE | `dfdc52985` | `ClusterView.StartLiveImmediately` / `RunModeChoice.StartImmediatelySetting`; `SendRunSettings(…, startLiveImmediately)`; Orchestrator panel checkbox; `/run-settings {startLiveImmediately}` |
| B-4 | CE-3414 | DONE | `1894b560e` | Start screen Recent rows |
| B-5 | CE-3411 | IN PROGRESS | — | ui R-1 merged (`48c58d65b`) |
| B-6 | — | SKIPPED — needs user | — | QUESTION [NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question](NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question.md): the setup is applied only by a load (DOM-level); lean (B) preview the saved scenario with the setup. ui's L-10 stays ⏳ |
| ui REQUEST | — | QUEUED | — | [live checkpoint labels](NOTE_Ui_To_Backend_Live_Checkpoint_Labels.md): the master keeps the marks of the exercise now recording and publishes them on the replay-session topic in Live; ANSWER note when built |
