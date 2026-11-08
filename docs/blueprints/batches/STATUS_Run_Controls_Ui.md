<!--STATUS state: LIVE · updated: 2026-11-08 · current-answer: the table -->
# STATUS — ui, run controls ([HANDOFF](HANDOFF_Coordinator_To_Ui_And_Backend_Run_Controls.md), dispatched at `dd9eb622`)

| item | id | state | sha | what backend can now use / notes |
|---|---|---|---|---|
| R-1 | CE-3405 | DONE | `27383eca0` | switch now — `Choose` switches; ▶ in Edit = Preview. B-5 unblocked: [NOTE](NOTE_Ui_To_Backend_Run_Controls_R1_R2_Done.md). Gates: Presentation run-mode filter 233/0 under Xvfb (base 216), Editor/AiShared/SimHost/Orchestrator/ClusterRunner rails green, cluster rail CE3413 (Edit → Live → ended → Close → Idle) |
| R-2 | CE-3413 | DONE | `27383eca0` | Live · ended derived (`LiveEnded`); ■ → End, ⏏ → Close → Idle; ended menu. ⚠ Restart from a checkpoint greyed — REQUEST [NOTE](NOTE_Ui_To_Backend_Live_Checkpoint_Labels.md). L-3 as-built folded (LC-17) |
| R-3 | CE-3406 | DONE | `b34096a2c` | the indicator = the current mode (pen / eye / ● / flag / ⟲ / ▶), a glyph button with ▾ + hover + hand; `IRunScenarioControl.SaveDisabledReason` read by pen, slot, File › Save |
| R-4 | CE-3409 | DONE | `f8155fff8` | ▶ glyph wears the indicator's colour; ▾ only Edit + briefing; toolbar 0 B steady in every state (rail) |
| R-5 | CE-3408 | DONE | `9348c80ec` | one Save slot: floppy / greyed with the reason / amber flag (checkpoint); `EditorCommandDescriptor.DynamicGlyph`. Gates: Presentation run-mode filter 236/0 ×2 under Xvfb. ⚠ 8 editor-recording integration rails red since the backend merge — CE-3410, REQUEST [NOTE](NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran.md) |
| R-6 | CE-3418 | DONE | `9da0b698c` | the Player opens its own summary on the rising edge of `LiveEnded` (training ⇒ trainee debrief, game ⇒ game summary, PH-8 features); `ExerciseObserver` gives the Player's transport a read-only ender. AiShared 15/0 |
| R-7 | CE-3416 | DONE | `9958b482b` + `1071cf660` | `PlayerBriefing` on the Player while `LiveBriefing`: title, You are, Mission, my side's objectives, the RESERVED picture-book frame; Mission falls back to the run's scenario file (a Player's world holds no description singleton); 0 B steady. Presentation 130/0 under Xvfb |
| R-8 | CE-3412 | DONE | `1616113fc` + `77cbd5ce5` | every state of both pictures rendered from the real code and compared (AQ108 §0.A R-8 table); fixed: ⏏ right-from-bracket (ended, DAR), menu glyphs, End dialog's *Keep running*. Start screen differences → [NOTE to backend](NOTE_Ui_To_Backend_Start_Screen_Fidelity.md); Save flag where no checkpoint → [QUESTION](NOTE_Ui_To_Coordinator_Save_Flag_When_No_Checkpoint.md) (built greyed) |

## Gate report (end of handoff, HEAD before this commit `5610e597b`)

| command | result | vs base `8b2bb0179` |
|---|---|---|
| Presentation run-mode filter (13 classes) under Xvfb | 247 / 0 / 0 skip | base filter 216 |
| whole `Hrot.Presentation.Tests` under Xvfb | 1764 pass, 3 fail, 3 skip | base: 1733 pass, 4 fail — the same 3 (`OneWindowPerProcess` CE1039, `Map3D.FilmFrameRail`, `Map3DFrameRail`) + `TheProblemsWindow` CE4318 (order-flaky, also red at base); `EntityFormListOpUndoFrameRail` red once in one full run, green alone and in the rerun (order-flaky) |
| AiShared `SaveSlot` + `Rules` | 24 / 0 | — |
| Editor / SimHost / Orchestrator / ClusterRunner run-mode rails | green (R-1 worker) | — |
| ClusterRunner.Integration `TheEditorsRecordingIsFilmed…` | ⚠ 8 red since the backend merge `803bf41a8` — backend CE-3410 drops runs that ran; proved by one line; [REQUEST](NOTE_Ui_To_Backend_CE3410_Drops_Runs_That_Ran.md) | green at ui `27383eca0` |
| hot path | toolbar 0 B steady in Edit / briefing / running / ended; PlayerBriefing 0 B steady | toolbar ~390 B/frame at base |
| `tracker-counts.py --check` | OK | — |

Answered backend's REQUEST `Choose(target, asker, answered)` at `18f762c3d` ([ANSWER](NOTE_Ui_To_Backend_Choose_With_Asker_Answer.md)). Open REQUESTs of ui to backend: live checkpoint labels, CE-3410, Start screen fidelity.
