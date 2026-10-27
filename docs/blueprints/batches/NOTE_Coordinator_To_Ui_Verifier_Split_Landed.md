<!--STATUS
state: LIVE — coordinator → ui: CE-4061 landed; P1 (one director, CE-1303) is unblocked
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Director_Timeline_Editor.md — §11h the seam, as built; §11f P1
  - NOTE_Coordinator_To_Ui_Films_From_Recordings.md — the note this follows up (its P1 row waited on CE-4061)
-->
# NOTE — coordinator → ui: the verifier is out of the runner (CE-4061) — P1 can start

**Dispatched at** the commit that adds this file (`coordinator`). FYI to [the films note](NOTE_Coordinator_To_Ui_Films_From_Recordings.md);
its scope is unchanged.

| | |
|---|---|
| what landed | `DirectorRunner` names no verify type (1185 → 800 lines). The verifier is `ScenarioVerifyRun : IRunExtension` (editor), plugged in by `EditorSubsystem`; the verify host members are `IVerifyHost` (editor); `EditorDirectorHost` implements both. [§11h](../../DESIGN_Director_Timeline_Editor.md) has the diagrams and the as-built |
| what MOVES in your P1 | `DirectorRunner.cs` (runner, `PlayRequest`, `ItemReport`, `CaptureOptions`, `DirectorPhase`, `DirectorMode`, `CutSnapshot`, `Ffmpeg`) · `RunExtension.cs` (the seam) · `IDirectorHost`, `ICinema`, `ILayerOverride`, `IFrameSink`, `IDirectorSound` (`DirectorHost.cs`) · `ScenarioReadyProbe` · the session, window and adapters per your note |
| what STAYS in the editor | `ScenarioVerifyRun`, `IVerifyHost`, `ScenarioVerification`, `ScenarioChecks`, `RecordedRun`, `CommandLineVerify`, `EditorVerifyWorld`, the Director window's Verify tab (contributed by the editor) |
| the API change | `PlayRequest(…, Plan: new VerifyOptions(…))` (was `Verify:`); `ItemReport.Result` (an `IRunResult`; `report.Verify()` in the editor) |
| the rail that guards it | `DirectorRunnerTests.CE4061_TheFilmRunner_NamesNoVerifyType_TheVerifierIsAnExtension` — keep it green across the move (it lists the verify types by name) |

_coordinator lane, 2026-10-27_
