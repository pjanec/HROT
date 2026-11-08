DONE — R-1 switch now (CE-3405) and R-2 Live ends in two steps (CE-3413), at 27383eca0 — your B-5 (CE-3411) is unblocked.

| what B-5 can call | where |
|---|---|
| a switch from Edit (Live → the door's Save / Discard / Cancel, then Live paused at 0:00; AAR → last exercise or the picker) | `RunModeChoice.Choose(RunArmed)` — the move list with each move's confirm text / enabled / reason is `Moves()` |
| Live · ended (derived: `OperatingLive` + `IExerciseEnder.HasEnded`) | `RunModeChoice.LiveEnded`; briefing = `LiveBriefing` |
| the ended verbs | `RunAgain()` · `ReplayEndedExercise()` · `CloseSession()` (the door's `Leave(Close)` → Idle) |
| ■ semantics | `Stop()`: running Live asks *End the exercise?* and stays in Live; ended ⇒ asks *Close the live session?* |

⚠ `POST /run-mode` still calls `Choose`, so it already switches; the 409 + question / `confirm:true` shape and `/status` are yours.
⚠ ▶ in Edit is now ALWAYS the Preview (the Orchestrator panel's Resume / Step from Edit included — `ThePanelAsksTheOneRunSurfacesTests` updated).
